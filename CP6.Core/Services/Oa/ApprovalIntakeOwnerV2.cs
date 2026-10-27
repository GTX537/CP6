using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Core.Services.Wms;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Oa.ApprovalSubmissionWireV2;
namespace CP6.Core.Services.Oa;

/// <summary>TxS only: durable original submission, exact binding/pin and start intent. Never calls the legacy engine.</summary>
public sealed class ApprovalIntakeOwnerV2(CP6Context db,ScopeAuthorizationFenceV1 fence,IApprovalServiceAuthorityV2 authority,
    ToolDispositionApprovalOwnerV1 evidence,FlowAuthoringOwnerV2 flows)
{
    internal Func<DateTimeOffset> ReceiveClock { get; set; }=()=>DateTimeOffset.UtcNow;
    private sealed record Registration(string Owner,Guid Revision,string SnapshotSchema,string SnapshotFields,bool Enabled,Guid Principal,string Audience,string EvidenceMode);
    private sealed record Saved(Guid Id,string Owner,string Core,string IntentCore,string Intent,byte[] Payload,string Ack);
    private sealed record Original(string Hash,string Ack,string Owner);
    private sealed record Alias(Guid Id,Guid Canonical,string Core,string IntentCore);
    private sealed record Binding(Guid Id,Guid Timeline,Guid Revision,string Owner,Guid RegistrationRevision,string Body,string TimelineBody);
    private sealed record Selection(Guid BindingId,Guid Timeline,Guid Revision,Guid FlowHead,Guid FlowVersion,Guid? RuleId,string FlowKey,string Evidence);
    private Task Ready(CancellationToken ct)=>RequireReady(ct);
    private async Task RequireReady(CancellationToken ct)
    {if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_approval_intake_v2.ready() AS \"Value\"").SingleAsync(ct))throw Error("AUTHORITY_UNAVAILABLE",503);}
    private async Task<ApprovalServiceIdentityV2> Service(CancellationToken ct)
    {
        var service=await authority.CurrentAsync(ct);
        if(service.Environment!=fence.EnvironmentId||service.Tenant!=db.CurrentTenantId||service.Principal==Guid.Empty)throw Error("PRODUCER_NOT_ALLOWED",403);
        return service;
    }
    private Task<Registration?> Registered(ApprovalSubmitCommandV2 e,CancellationToken ct)=>db.Database.SqlQuery<Registration>($"""
        SELECT owner AS "Owner",revision AS "Revision",snapshot_schema AS "SnapshotSchema",snapshot_fields AS "SnapshotFields",enabled AS "Enabled",
          service_principal AS "Principal",audience AS "Audience",evidence_mode AS "EvidenceMode" FROM cp6_approval_binding_v2.registrations
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND biz_type={e.Subject.BizType} AND subject_kind={e.Subject.SubjectKind}
        """).SingleOrDefaultAsync(ct);
    private static void Match(Registration? r,ApprovalServiceIdentityV2 service,ApprovalSubmitCommandV2 e,bool write)
    {
        if(r==null||r.Owner!=e.Subject.BusinessOwner||r.Principal!=service.Principal||r.Audience!=service.Audience||r.EvidenceMode!=service.EvidenceMode)throw Error("PRODUCER_NOT_ALLOWED",403);
        if(write&&(!r.Enabled||!service.CanSubmit))throw Error("COMMAND_FORBIDDEN",403);
        if(e.Snapshot.Schema!=r.SnapshotSchema)throw Error("SNAPSHOT_SCHEMA_INVALID");
    }
    private Task<Original?> Command(Guid principal,Guid id,CancellationToken ct)=>db.Database.SqlQuery<Original>($"""
        SELECT full_hash AS "Hash",result_json AS "Ack",owner AS "Owner" FROM cp6_approval_intake_v2.commands
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND principal={principal} AND command_id={id}
        """).SingleOrDefaultAsync(ct);
    private IQueryable<Saved> Submissions(string owner)=>db.Database.SqlQuery<Saved>($"""
        SELECT id AS "Id",owner AS "Owner",core_hash AS "Core",intent_core_hash AS "IntentCore",intent_hash AS "Intent",payload AS "Payload",ack_json AS "Ack"
        FROM cp6_approval_intake_v2.submissions WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND owner={owner}
        """);
    private Task<Alias?> AliasFor(string owner,Guid requested,CancellationToken ct)=>db.Database.SqlQuery<Alias>($"""
        SELECT id AS "Id",canonical_id AS "Canonical",core_hash AS "Core",intent_core_hash AS "IntentCore" FROM cp6_approval_intake_v2.aliases
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND owner={owner} AND requested_id={requested}
        """).SingleOrDefaultAsync(ct);
    private static ApprovalSubmissionAckV2 Ack(string value)=>JsonSerializer.Deserialize<ApprovalSubmissionAckV2>(value,ApprovalBindingOwnerV2.Json)??throw Error("ORIGINAL_RESULT_INVALID",503);
    private async Task Lock(string key,CancellationToken ct)
        =>await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key},0))",ct);
    private async Task<Guid> LockSubmission(Parsed parsed,CancellationToken ct)
    {
        var e=parsed.Value;
        var heads=await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM cp6_approval_binding_v2.heads
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND biz_type={e.Subject.BizType} AND subject_kind={e.Subject.SubjectKind} FOR UPDATE
            """).ToArrayAsync(ct);
        if(heads.Length!=1)throw Error("BINDING_NOT_FOUND");
        await Lock($"oa.app.intent/{fence.EnvironmentId}/{db.CurrentTenantId}/{e.Subject.BusinessOwner}/{parsed.Intent}",ct);
        return heads[0];
    }
    private async Task<Selection> Select(Parsed parsed,Registration registration,Guid id,DateTimeOffset receive,CancellationToken ct)
    {
        var e=parsed.Value;
        var b=await db.Database.SqlQuery<Binding>($"""
            SELECT h.id AS "Id",h.timeline_id AS "Timeline",i.revision_id AS "Revision",h.owner AS "Owner",r.registration_revision AS "RegistrationRevision",
              r.body AS "Body",t.body AS "TimelineBody"
            FROM cp6_approval_binding_v2.heads h JOIN cp6_approval_binding_v2.intervals i ON i.tenant_id=h.tenant_id AND i.environment_id=h.environment_id AND i.binding_id=h.id AND i.timeline_id=h.timeline_id
            JOIN cp6_approval_binding_v2.revisions r ON r.tenant_id=i.tenant_id AND r.environment_id=i.environment_id AND r.binding_id=i.binding_id AND r.id=i.revision_id
            JOIN cp6_approval_binding_v2.timelines t ON t.tenant_id=h.tenant_id AND t.environment_id=h.environment_id AND t.binding_id=h.id AND t.id=h.timeline_id
            WHERE h.tenant_id={db.CurrentTenantId} AND h.environment_id={fence.EnvironmentId} AND h.id={id}
              AND i.state='ACTIVE' AND i.starts_at<={receive} AND (i.ends_at IS NULL OR {receive}<i.ends_at)
            """).SingleOrDefaultAsync(ct)??throw Error("BINDING_NOT_FOUND");
        if(b.Owner!=registration.Owner||b.RegistrationRevision!=registration.Revision)throw Error("OWNER_REGISTRATION_CHANGED",409);
        var body=JsonSerializer.Deserialize<ApprovalBindingWriteV2>(b.Body,ApprovalBindingOwnerV2.Json)??throw Error("BINDING_NOT_FOUND");
        var fields=JsonSerializer.Deserialize<string[]>(registration.SnapshotFields,ApprovalBindingOwnerV2.Json)??throw Error("SNAPSHOT_SCHEMA_INVALID");
        using var snapshot=JsonDocument.Parse(Convert.FromBase64String(e.Snapshot.CanonicalBytesBase64));
        ApprovalBindingRuleV2? chosen=null;
        foreach(var rule in body.Rules)
        {
            if(!ApprovalExpressionV2.Evaluate(rule.When,snapshot.RootElement,fields))continue;
            if(chosen!=null)throw Error("MULTIPLE_RULE_MATCH",409);chosen=rule;
            if(body.RuleMode=="ORDERED_FIRST_MATCH")break;
        }
        if(body.RuleMode is not("EXCLUSIVE_MATCH" or "ORDERED_FIRST_MATCH"))throw Error("SNAPSHOT_SCHEMA_INVALID");
        var key=chosen?.FlowKey??body.FlowKey;var version=chosen==null?body.FlowDefVersionId:chosen.FlowDefVersionId;
        if(body.SelectionMode is not("EXACT_PINNED" or "RESOLVE_PUBLISHED_ON_SUBMIT")||(body.SelectionMode=="EXACT_PINNED")!=(version!=null))throw Error("FLOW_VERSION_UNAVAILABLE");
        var source=await flows.SubmissionSource(key,version,ct);
        var proof=ApprovalBindingOwnerV2.Serialize(new{receiveAt=receive,binding=b,registration,ruleId=chosen?.RuleId,snapshotDigest=e.Snapshot.Digest,publication=source});
        return new(b.Id,b.Timeline,b.Revision,source.DefinitionId,source.VersionId,chosen?.RuleId,key,proof);
    }
    private async Task Add(Parsed p,ApprovalServiceIdentityV2 s,Selection selected,ApprovalSubmissionAckV2 ack,DateTimeOffset at,CancellationToken ct)
    {
        var owner=p.Value.Subject.BusinessOwner;var id=p.Value.SubmissionId;var json=ApprovalBindingOwnerV2.Serialize(ack);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_intake_v2.submissions(tenant_id,environment_id,owner,id,principal,command_id,intent_hash,core_hash,intent_core_hash,
              payload,payload_digest,binding_id,binding_revision,timeline_id,flow_head,flow_version,selection_json,ack_json,received_at)
            VALUES({s.Tenant},{s.Environment},{owner},{id},{s.Principal},{p.Value.CommandId},{p.Intent},{p.Core},{p.IntentCore},
              {p.Canonical},{p.Full},{selected.BindingId},{selected.Revision},{selected.Timeline},{selected.FlowHead},{selected.FlowVersion},{selected.Evidence},{json},{at})
            """,ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_intake_v2.start_outbox(tenant_id,environment_id,owner,submission_id,engine_start_id,payload,payload_digest)
            VALUES({s.Tenant},{s.Environment},{owner},{id},{id},{p.Canonical},{p.Full})
            """,ct);
    }
    public async Task<ApprovalIntakeResultV2> SubmitAsync(byte[] raw,CancellationToken ct=default)
    {
        var p=Parse(raw);var e=p.Value;
        if(!db.Database.IsNpgsql()||db.Database.CurrentTransaction!=null||db.ChangeTracker.HasChanges()||System.Transactions.Transaction.Current!=null)throw Error("AUTHORITY_UNAVAILABLE",503);
        await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            await fence.Root(ct);await Ready(ct);var service=await Service(ct);
            if(e.Scope.TenantId!=service.Tenant||e.Scope.EnvironmentId!=service.Environment.ToString())throw Error("PRODUCER_NOT_ALLOWED",403);
            var registration=await Registered(e,ct);Match(registration,service,e,false);
            await Lock($"oa.app.command/{service.Environment}/{service.Tenant}/{service.Principal}/{e.CommandId}",ct);
            var original=await Command(service.Principal,e.CommandId,ct);
            if(original!=null)
            {
                if(original.Hash!=p.Full||original.Owner!=e.Subject.BusinessOwner)throw Error("COMMAND_PAYLOAD_CONFLICT",409);
                if(await Service(ct)!=service)throw Error("AUTHORITY_CHANGED",403);
                return new(200,Ack(original.Ack));
            }
            Match(registration,service,e,true);var lease=await fence.AcquireAsync(ct);
            var ownerProof=await evidence.VerifyIntakeEvidence(p,ct);
            // All new commands follow BindingHead -> Intent -> Submission/alias, including
            // aliases and original-submission retries. Only genuinely new submissions select a timeline.
            var bindingId=await LockSubmission(p,ct);
            var owner=e.Subject.BusinessOwner;var prior=await Submissions(owner).SingleOrDefaultAsync(x=>x.Id==e.SubmissionId,ct);
            var alias=await AliasFor(owner,e.SubmissionId,ct);ApprovalSubmissionAckV2 result;Selection? selected=null;var status=200;
            var at=DateTimeOffset.FromUnixTimeMilliseconds(ReceiveClock().ToUnixTimeMilliseconds());
            if(prior!=null)
            {if(prior.Core!=p.Core)throw Error("SUBMISSION_PAYLOAD_CONFLICT",409);result=Ack(prior.Ack);}
            else if(alias!=null)
            {
                if(alias.Core!=p.Core)throw Error("SUBMISSION_PAYLOAD_CONFLICT",409);
                prior=await Submissions(owner).SingleAsync(x=>x.Id==alias.Canonical,ct);
                result=Ack(prior.Ack) with{Alias=new(e.SubmissionId,prior.Id,alias.Id)};
            }
            else
            {
                prior=await Submissions(owner).SingleOrDefaultAsync(x=>x.Intent==p.Intent,ct);
                if(prior!=null)
                {
                    if(prior.IntentCore!=p.IntentCore||prior.Id!=ownerProof.FrozenSubmissionId)throw Error("INTENT_PAYLOAD_CONFLICT",409);
                    var aliasId=Guid.NewGuid();
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO cp6_approval_intake_v2.aliases(tenant_id,environment_id,owner,requested_id,id,canonical_id,core_hash,intent_core_hash,first_full_hash,reason,principal,command_id)
                        VALUES({service.Tenant},{service.Environment},{owner},{e.SubmissionId},{aliasId},{prior.Id},{p.Core},{p.IntentCore},{p.Full},{e.Reason},{service.Principal},{e.CommandId})
                        """,ct);
                    result=Ack(prior.Ack) with{Alias=new(e.SubmissionId,prior.Id,aliasId)};
                }
                else
                {
                    if(e.SubmissionId!=ownerProof.FrozenSubmissionId)throw Error("MISSING_EVIDENCE");
                    selected=await Select(p,registration!,bindingId,at,ct);
                    result=new(e.SubmissionId,null,"DISPATCH_PENDING",selected.Revision,selected.Timeline,selected.FlowVersion,e.Subject,e.Policy,ApprovalBindingTimelineBuilderV2.Version(1),null,null);
                    await Add(p,service,selected,result,at,ct);status=201;
                }
            }
            var audit=ApprovalBindingOwnerV2.Serialize(new{service,registration,originalAuthorization=ownerProof,requestedSubmission=e.SubmissionId,canonicalSubmission=result.SubmissionId,p.Full,p.Core,p.IntentCore,p.Intent,receivedAt=at});
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_approval_intake_v2.commands(tenant_id,environment_id,principal,command_id,owner,submission_id,full_hash,raw_body,result_json,audit_json,recorded_at)
                VALUES({service.Tenant},{service.Environment},{service.Principal},{e.CommandId},{owner},{result.SubmissionId},{p.Full},{raw},{ApprovalBindingOwnerV2.Serialize(result)},{audit},{at})
                """,ct);
            if(await Service(ct)!=service||await Registered(e,ct)!=registration)throw Error("AUTHORITY_CHANGED",403);
            if(await evidence.VerifyIntakeEvidence(p,ct)!=ownerProof)throw Error("AUTHORITY_CHANGED",403);
            if(selected!=null)await flows.SubmissionSource(selected.FlowKey,selected.FlowVersion,ct);
            await Ready(ct);await fence.ValidateAsync(lease,ct);ct.ThrowIfCancellationRequested();committing=true;await tx.CommitAsync(ct);
            return new(status,result);
        }
        catch(Exception error)
        {
            try{await tx.RollbackAsync(CancellationToken.None);}catch(Exception rollback){throw new ScopeAuthorizationExceptionV1("OUTCOME_UNKNOWN",504,new AggregateException(error,rollback));}
            if(committing)throw new ScopeAuthorizationExceptionV1("OUTCOME_UNKNOWN",504,error);
            throw;
        }
    }
    public async Task<ApprovalSubmissionAckV2> OriginalAsync(Guid command,CancellationToken ct=default)
    {
        if(command==Guid.Empty)throw Error("RESOURCE_NOT_FOUND",404);
        if(db.Database.CurrentTransaction!=null||db.ChangeTracker.HasChanges())throw Error("AUTHORITY_UNAVAILABLE",503);
        await using var tx=await db.Database.BeginTransactionAsync(ct);await fence.Root(ct);await Ready(ct);var service=await Service(ct);
        var original=await Command(service.Principal,command,ct)??throw Error("RESOURCE_NOT_FOUND",404);
        var ack=Ack(original.Ack);var row=await Submissions(original.Owner).SingleAsync(x=>x.Id==ack.SubmissionId,ct);
        var e=Parse(row.Payload).Value;Match(await Registered(e,ct),service,e,false);
        if(await Service(ct)!=service)throw Error("AUTHORITY_CHANGED",403);return ack;
    }
}
