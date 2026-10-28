using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Oa;

/// <summary>Native Tx W. The immutable Owner fact is the only actor source; service identities never become human requesters.</summary>
public sealed class ApprovalWithdrawalOwnerV2(CP6Context db,ScopeAuthorizationFenceV1 scope,IApprovalServiceAuthorityV2 serviceAuthority,
    ApprovalBusinessEvidenceV2 business)
{
    public const string QueryRoute="/internal/oa/app/v2/withdraw-commands/";
    private static ScopeAuthorizationExceptionV1 Error(string code,int status=503)=>new(code,status);
    private static string Json(object v)=>Encoding.UTF8.GetString(ApprovalCanonicalV2.Bytes(v));
    private static T Parse<T>(string v)=>JsonSerializer.Deserialize<T>(v,ApprovalBindingOwnerV2.Json)!;
    private sealed record Source(byte[] Payload,string Digest,string Ack,Guid? Instance,int? Status,byte[] Version);
    private sealed record Registration(string Owner,string FactOwner,Guid Revision,bool Enabled,bool Query,bool Relay,string Mode);
    private sealed record Grant(Guid Revision,bool Enabled,bool Query,bool Relay);
    private sealed record Saved(Guid Submission,string Owner,string Digest,string State,string? Ack);
    private sealed record Decision(Guid Id,byte[] Payload);
    private void Entry()
    {if(!db.Database.IsNpgsql() || db.Database.CurrentTransaction!=null || db.ChangeTracker.HasChanges() || System.Transactions.Transaction.Current!=null)throw Error("WITHDRAW_TRANSACTION_REQUIRED");}
    private async Task Ready(CancellationToken ct)
    {if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_approval_withdraw_v2.ready() AS \"Value\"").SingleAsync(ct))throw Error("WITHDRAW_STORE_UNAVAILABLE");}
    private async Task<(ApprovalServiceIdentityV2 Service,Registration Registration)> Service(CancellationToken ct)
    {
        var s=await serviceAuthority.CurrentAsync(ct);
        if(s.Environment!=scope.EnvironmentId || s.Tenant!=db.CurrentTenantId || s.Audience!="CP6.Services" || s.Principal==Guid.Empty)throw Error("PRODUCER_NOT_ALLOWED",403);
        var r=await db.Database.SqlQuery<Registration>($"""
            SELECT owner AS "Owner",fact_owner AS "FactOwner",revision AS "Revision",enabled AS "Enabled",can_query AS "Query",can_relay AS "Relay",evidence_mode AS "Mode"
            FROM cp6_approval_withdraw_v2.registrations WHERE environment_id={s.Environment} AND tenant_id={s.Tenant} AND principal_id={s.Principal}
            """).SingleOrDefaultAsync(ct);
        if(r==null || !r.Enabled || !r.Query || r.Mode!=s.EvidenceMode)throw Error("PRODUCER_NOT_ALLOWED",403);return(s,r);
    }
    private async Task<Grant> Subject((ApprovalServiceIdentityV2 Service,Registration Registration) auth,Guid submission,bool write,CancellationToken ct)
    {
        var s=auth.Service;var r=auth.Registration;
        var g=await db.Database.SqlQuery<Grant>($"""
            SELECT revision AS "Revision",enabled AS "Enabled",can_query AS "Query",can_relay AS "Relay"
            FROM cp6_approval_withdraw_v2.subject_grants WHERE environment_id={s.Environment} AND tenant_id={s.Tenant}
              AND principal_id={s.Principal} AND owner={r.Owner} AND submission_id={submission}
            """).SingleOrDefaultAsync(ct);
        if(g==null || !g.Enabled || !g.Query)throw Error("SUBJECT_FORBIDDEN",403);
        if(write && (!g.Relay || !r.Relay || !s.CanSubmit))throw Error("COMMAND_FORBIDDEN",403);return g;
    }
    private async Task<Source> Original(string owner,Guid id,CancellationToken ct)
    {
        var s=await db.Database.SqlQuery<Source>($"""
            SELECT p.payload AS "Payload",p.payload_digest AS "Digest",p.ack_json AS "Ack",e.instance_id AS "Instance",i."Status" AS "Status",v.row_version AS "Version"
            FROM cp6_approval_intake_v2.submissions p JOIN cp6_approval_withdraw_v2.submission_versions v
              ON (v.environment_id,v.tenant_id,v.owner,v.submission_id)=(p.environment_id,p.tenant_id,p.owner,p.id)
            LEFT JOIN cp6_approval_start_v2.starts e ON (e.environment_id,e.tenant_id,e.owner,e.submission_id)=(p.environment_id,p.tenant_id,p.owner,p.id)
            LEFT JOIN public."Wf_FlowInstance" i ON i."Id"=e.instance_id AND i."TenantId"=p.tenant_id
            WHERE p.environment_id={scope.EnvironmentId} AND p.tenant_id={db.CurrentTenantId} AND p.owner={owner} AND p.id={id} FOR UPDATE OF p,v
            """).SingleOrDefaultAsync(ct)??throw Error("RESOURCE_NOT_FOUND",404);
        var e=ApprovalSubmissionWireV2.Parse(s.Payload).Value;
        if(ApprovalCanonicalV2.Hash(s.Payload)!=s.Digest || e.SubmissionId!=id || e.Subject.BusinessOwner!=owner || s.Version.Length!=8)throw Error("WITHDRAW_SUBMISSION_UNAVAILABLE");
        return s;
    }
    private Task<Saved?> Command(string owner,Guid id,CancellationToken ct)=>db.Database.SqlQuery<Saved>($"""
        SELECT submission_id AS "Submission",owner AS "Owner",digest AS "Digest",state AS "State",response_json AS "Ack"
        FROM cp6_approval_withdraw_v2.commands WHERE environment_id={scope.EnvironmentId} AND tenant_id={db.CurrentTenantId} AND owner={owner} AND id={id} FOR UPDATE
        """).SingleOrDefaultAsync(ct);
    private async Task Final(ScopeAuthorizationLeaseV1 lease,(ApprovalServiceIdentityV2 Service,Registration Registration) auth,
        Guid submission,Grant grant,bool write,CancellationToken ct)
    {
        await Ready(ct);await scope.ValidateAsync(lease,ct);
        if(await Service(ct)!=auth || await Subject(auth,submission,write,ct)!=grant)throw Error("AUTHORITY_CHANGED",403);
        var s=auth.Service;
        if(!await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM cp6_approval_binding_v2.registrations r JOIN cp6_approval_intake_v2.submissions p
              ON p.environment_id=r.environment_id AND p.tenant_id=r.tenant_id AND p.owner=r.owner
              AND convert_from(p.payload,'UTF8')::jsonb->'subject'->>'bizType'=r.biz_type AND convert_from(p.payload,'UTF8')::jsonb->'subject'->>'subjectKind'=r.subject_kind
              WHERE r.environment_id={s.Environment} AND r.tenant_id={s.Tenant} AND r.owner={auth.Registration.Owner} AND r.service_principal={s.Principal}
                AND r.audience={s.Audience} AND r.evidence_mode={s.EvidenceMode} AND (NOT {write} OR r.enabled) AND p.id={submission}) AS "Value"
            """).SingleAsync(ct))throw Error("PRODUCER_NOT_ALLOWED",403);
    }
    private static ApprovalWithdrawResultV2 Result(ApprovalWithdrawAckV2 ack)=>new(ack.State=="BLOCKED"?202:ack.State=="TERMINAL_CONFLICT"?409:200,ack);
    public async Task<ApprovalWithdrawContextV2> ContextAsync(Guid id,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await scope.AcquireAsync(ct);await Ready(ct);
        var auth=await Service(ct);var grant=await Subject(auth,id,false,ct);var source=await Original(auth.Registration.Owner,id,ct);
        var e=ApprovalSubmissionWireV2.Parse(source.Payload).Value;var state=source.Status switch{0=>"RUNNING",1=>"APPROVED",2=>"REJECTED",3=>"WITHDRAWN",4=>"SUSPENDED",null=>"NOT_STARTED",_=>throw Error("WITHDRAW_ENGINE_STATE_UNAVAILABLE")};
        var can=grant.Relay && auth.Registration.Relay && auth.Service.CanSubmit && state is "RUNNING" or "SUSPENDED";
        var result=new ApprovalWithdrawContextV2(id,e.Subject,e.Policy.Ref,source.Instance,state,Convert.ToBase64String(source.Version),can,can?[]:["WITHDRAW_CURRENT_STATE_OR_AUTHORITY_REQUIRED"]);
        await Final(lease,auth,id,grant,false,ct);await tx.CommitAsync(ct);return result;
    }
    public async Task<ApprovalWithdrawResultV2> OriginalAsync(Guid id,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await scope.AcquireAsync(ct);await Ready(ct);var auth=await Service(ct);
        var saved=await Command(auth.Registration.Owner,id,ct)??throw Error("RESOURCE_NOT_FOUND",404);var grant=await Subject(auth,saved.Submission,false,ct);
        if(saved.Ack==null)throw Error("WITHDRAW_RESULT_UNAVAILABLE");var result=Result(Parse<ApprovalWithdrawAckV2>(saved.Ack));
        await Final(lease,auth,saved.Submission,grant,false,ct);await tx.CommitAsync(ct);return result;
    }
    public async Task<ApprovalWithdrawResultV2> WithdrawAsync(Guid id,string? ifMatch,byte[] raw,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await scope.AcquireAsync(ct);await Ready(ct);var auth=await Service(ct);
            var parsed=ApprovalWithdrawalWireV2.Parse(raw);var e=parsed.Value;var f=e.Fact;
            if(e.Scope!=new ApprovalScopeV2(scope.EnvironmentId.ToString(),db.CurrentTenantId) || e.ProducerOwner!=auth.Registration.Owner)throw Error("PRODUCER_NOT_ALLOWED",403);
            if(e.SubmissionId!=id || f.OwnerWithdrawalRef.Owner!=auth.Registration.FactOwner)throw Error("WITHDRAW_FACT_MISMATCH",422);
            var grant=await Subject(auth,id,false,ct);var saved=await Command(e.ProducerOwner,e.CommandId,ct);
            if(saved!=null)
            {
                if(saved.Submission!=id || saved.Digest!=parsed.Hash)throw Error("WITHDRAW_COMMAND_PAYLOAD_CONFLICT",409);
                if(saved.State is "WITHDRAWN" or "TERMINAL_CONFLICT")
                {var result=Result(Parse<ApprovalWithdrawAckV2>(saved.Ack!));await Final(lease,auth,id,grant,false,ct);await tx.CommitAsync(ct);return result;}
            }
            grant=await Subject(auth,id,true,ct);
            var source=await Original(e.ProducerOwner,id,ct);var original=ApprovalSubmissionWireV2.Parse(source.Payload).Value;
            if(f.Subject!=original.Subject || f.PolicyRef!=original.Policy.Ref)throw Error("WITHDRAW_FACT_MISMATCH",422);
            await Final(lease,auth,id,grant,true,ct);
            var sourceEvidence=await business.Withdrawal(original,e,false,ct);
            if(string.IsNullOrEmpty(ifMatch))throw Error("PRECONDITION_REQUIRED",428);
            if(ifMatch!="\""+Convert.ToBase64String(source.Version)+"\"")throw Error("PRECONDITION_FAILED",412);
            if(saved==null)
            {
                if(await db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS(SELECT FROM cp6_approval_withdraw_v2.commands WHERE environment_id={scope.EnvironmentId} AND tenant_id={db.CurrentTenantId}
                      AND owner={e.ProducerOwner} AND (owner_command_id={f.OwnerCommandId} OR outbox_id={e.OwnerOutboxId})) AS "Value"
                    """).SingleAsync(ct))throw Error("WITHDRAW_COMMAND_MAPPING_CONFLICT",409);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_approval_withdraw_v2.commands(environment_id,tenant_id,owner,id,owner_command_id,outbox_id,submission_id,instance_id,payload,digest,raw,raw_digest,state)
                    VALUES({scope.EnvironmentId},{db.CurrentTenantId},{e.ProducerOwner},{e.CommandId},{f.OwnerCommandId},{e.OwnerOutboxId},{id},{source.Instance},
                      {parsed.Canonical},{parsed.Hash},{raw},{ApprovalCanonicalV2.Hash(raw)},'RECEIVED')
                    """,ct);
            }
            else await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_approval_withdraw_v2.commands SET state='RECEIVED',active_xid=pg_current_xact_id(),instance_id={source.Instance},response_json=NULL
                WHERE environment_id={scope.EnvironmentId} AND tenant_id={db.CurrentTenantId} AND owner={e.ProducerOwner} AND id={e.CommandId}
                """,ct);
            string? blocked=null;string? current=null;
            try{current=await business.Withdrawal(original,e,true,ct);}
            catch(CP6.Core.Services.Erp.OrderPriceCorrectionException error)when(error.Status is 403 or 409 or 422){blocked=error.Message;}
            catch(ScopeAuthorizationExceptionV1 error)when(error.Status is 403 or 409 or 422){blocked=error.Message;}
            ApprovalWithdrawAckV2 ack;Guid? decision=null;
            var prior=await db.Database.SqlQuery<Decision>($"""
                SELECT id AS "Id",payload AS "Payload" FROM cp6_approval_decision_v2.decisions WHERE environment_id={scope.EnvironmentId}
                  AND tenant_id={db.CurrentTenantId} AND submission_id={id}
                """).SingleOrDefaultAsync(ct);
            if(prior!=null)
            {
                var d=ApprovalDecisionWireV2.Parse(prior.Payload).Value;
                ack=new(e.CommandId,f.OwnerCommandId,id,f.OwnerWithdrawalRef,e.FactDigest,"TERMINAL_CONFLICT",d.DecisionId,d.ResultRef,
                    d.Decision=="WITHDRAWN"?"WITHDRAW_ALREADY_DECIDED":"WORKFLOW_ALREADY_DECIDED",QueryRoute+e.CommandId);
            }
            else if(blocked!=null || source.Instance==null)
                ack=new(e.CommandId,f.OwnerCommandId,id,f.OwnerWithdrawalRef,e.FactDigest,"BLOCKED",null,null,blocked??"WORKFLOW_NOT_STARTED",QueryRoute+e.CommandId);
            else
            {
                if(source.Status is not(FlowInstanceStatus.Running or FlowInstanceStatus.Suspended))throw Error("WITHDRAW_ENGINE_STATE_UNAVAILABLE");
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    SELECT 1 FROM public."Wf_FlowInstance" WHERE "TenantId"={db.CurrentTenantId} AND "Id"={source.Instance} FOR UPDATE;
                    SELECT 1 FROM public."Wf_FlowTask" WHERE "TenantId"={db.CurrentTenantId} AND "InstanceId"={source.Instance} ORDER BY "Id" FOR UPDATE
                    """,ct);
                await CancelRuntime(source.Instance.Value,f.Actor.ActualHandlerId,f.Reason,ct);
                decision=await Terminal(e,source,ct);
                if(await business.Withdrawal(original,e,true,ct)!=current)throw Error("AUTHORITY_CHANGED",403);
                ack=new(e.CommandId,f.OwnerCommandId,id,f.OwnerWithdrawalRef,e.FactDigest,"WITHDRAWN",decision,new("Main Workflow",decision.Value.ToString(),"1"),null,QueryRoute+e.CommandId);
            }
            var audit=Json(new{profile="oa.app.txw/2.0",sourceEvidence,currentAuthority=current,service=auth.Service,auth.Registration,grant,ifMatch,source.Digest,ack,scope=lease.Snapshot});
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_approval_withdraw_v2.commands SET state={ack.State},response_json={Json(ack)},decision_id={ack.DecisionId}
                WHERE environment_id={scope.EnvironmentId} AND tenant_id={db.CurrentTenantId} AND owner={e.ProducerOwner} AND id={e.CommandId};
                INSERT INTO cp6_approval_withdraw_v2.attempts(environment_id,tenant_id,owner,command_id,id,state,result_json,audit_json)
                VALUES({scope.EnvironmentId},{db.CurrentTenantId},{e.ProducerOwner},{e.CommandId},{Guid.NewGuid()},{ack.State},{Json(ack)},{audit})
                """,ct);
            await Final(lease,auth,id,grant,true,ct);committing=true;await tx.CommitAsync(ct);db.ChangeTracker.Clear();return Result(ack);
        }
        catch(Exception error)
        {
            try{await tx.RollbackAsync(CancellationToken.None);}catch(Exception rollback){throw new ScopeAuthorizationExceptionV1("OUTCOME_UNKNOWN",504,new AggregateException(error,rollback));}
            finally{db.ChangeTracker.Clear();}
            if(committing)throw new ScopeAuthorizationExceptionV1("OUTCOME_UNKNOWN",504,error);throw;
        }
    }
    private async Task CancelRuntime(Guid instance,Guid actor,string reason,CancellationToken ct)
    {
        var i=await db.Wf_FlowInstances.SingleAsync(x=>x.Id==instance,ct);
        if(i.Creator!=FlowEngine.ApprovalRuntimeV2)throw Error("WITHDRAW_ENGINE_STATE_UNAVAILABLE");
        // Native runtime suspends service/subflow nodes before their legacy effects. Fail closed if a future runtime expands that boundary.
        if(await db.Wf_ServiceJobs.AnyAsync(x=>x.InstanceId==instance && (x.Status==ServiceJobStatus.Pending || x.Status==ServiceJobStatus.Running),ct))throw Error("WITHDRAW_ENGINE_CAPABILITY_UNAVAILABLE",422);
        i.Status=FlowInstanceStatus.Withdrawn;i.Modifier=actor.ToString();i.ModifyDate=DateTime.Now;
        foreach(var task in await db.Wf_FlowTasks.Where(x=>x.InstanceId==instance && (x.Status==FlowTaskStatus.Pending || x.Status==FlowTaskStatus.Suspended)).ToArrayAsync(ct))
        {task.Status=FlowTaskStatus.Cancelled;task.Comment=reason;task.Modifier=actor.ToString();task.ModifyDate=DateTime.Now;}
        foreach(var token in await db.Wf_FlowTokens.Where(x=>x.InstanceId==instance && x.Status==FlowTokenStatus.Active).ToArrayAsync(ct))token.Status=FlowTokenStatus.Cancelled;
        foreach(var form in await db.Wf_FlowFormTos.Where(x=>x.InstanceId==instance && x.Status==FlowFormToStatus.Pending).ToArrayAsync(ct))form.Status=FlowFormToStatus.Voided;
        db.Wf_FlowHistories.Add(new(){Id=Guid.NewGuid(),InstanceId=instance,NodeId=i.CurrentNode,ActorId=actor,Action="withdraw",Comment=reason});
        await db.SaveChangesAsync(ct);
    }
    private async Task<Guid> Terminal(ApprovalWithdrawCommandV2 e,Source source,CancellationToken ct)
    {
        var original=ApprovalSubmissionWireV2.Parse(source.Payload).Value;var ack=Parse<ApprovalSubmissionAckV2>(source.Ack);var id=Guid.NewGuid();var eventId=Guid.NewGuid();
        var at=await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT date_trunc('milliseconds',clock_timestamp()) AS \"Value\"").SingleAsync(ct);
        var value=new ApprovalDecisionEnvelopeV2("oa.app/2.0",e.Scope,eventId,id,e.SubmissionId,"1","Main Workflow",e.ProducerOwner,e.SubmissionId,source.Instance!.Value,
            original.Subject,original.Policy,ack.BindingRevisionId??throw Error("WITHDRAW_SUBMISSION_UNAVAILABLE"),
            ack.BindingTimelineRevisionId??throw Error("WITHDRAW_SUBMISSION_UNAVAILABLE"),ack.FlowDefVersionId??throw Error("WITHDRAW_SUBMISSION_UNAVAILABLE"),original.RoundId,original.Engineering,
            "WITHDRAWN","WITHDRAWAL",at,JsonSerializer.SerializeToElement(e.Fact.Actor,ApprovalBindingOwnerV2.Json),null,null,[],e.Fact.Reason,
            new("Main Workflow",id.ToString(),"1"),new("sha256",ApprovalCanonicalV2.Codec,new string('0',64)));
        var node=JsonSerializer.SerializeToNode(value,ApprovalBindingOwnerV2.Json)!.AsObject();node.Remove("payloadDigest");
        value=value with{PayloadDigest=ApprovalCanonicalV2.Digest(ApprovalCanonicalV2.Bytes(node))};var parsed=ApprovalDecisionWireV2.Parse(ApprovalCanonicalV2.Bytes(value));
        var evidence=Json(new{profile="oa.app.withdrawal-evidence/2.0",command=e,decisionId=id});
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_decision_v2.decisions(tenant_id,environment_id,id,command_id,withdraw_command_id,withdraw_owner,instance_id,submission_id,event_id,decision,evidence_json,evidence_digest,payload,payload_digest)
            VALUES({db.CurrentTenantId},{scope.EnvironmentId},{id},NULL,{e.CommandId},{e.ProducerOwner},{source.Instance},{e.SubmissionId},{eventId},'WITHDRAWN',
              {evidence},{ApprovalCanonicalV2.Hash(Encoding.UTF8.GetBytes(evidence))},{parsed.Canonical},{parsed.Full});
            INSERT INTO cp6_approval_decision_v2.outbox(tenant_id,environment_id,id,decision_id,submission_id,consumer_owner,sequence,payload,payload_digest)
            VALUES({db.CurrentTenantId},{scope.EnvironmentId},{eventId},{id},{e.SubmissionId},{e.ProducerOwner},1,{parsed.Canonical},{parsed.Full})
            """,ct);return id;
    }
}
