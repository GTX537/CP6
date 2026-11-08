using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Platform;
public sealed partial class PrivacyDispositionOwnerV1
{
    private PrivacyRestoreOptionsV1 RestoreOptions=>restoration?.Value??throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_CONFIGURATION_REQUIRED",503);
    private PrivacyRestoreCandidateOptionsV1 RestoreCandidate(Guid id)=>RestoreOptions.Candidates.TryGetValue(id,out var value)?value:throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_CANDIDATE_UNREGISTERED",503);
    private string RestoreMetaJournal(Authority a)=>Journal(a)+"/restore-metadata";
    public Task<PrivacyRestoreContextV1> RestoreContextAsync(TenantManagementSessionV1 session,CancellationToken ct=default)
        =>Transaction(false,session,async(a,t)=>{Can(a,"read");return new PrivacyRestoreContextV1(a.Environment,a.Actor.TenantId,a.Actor.Id,
            await PrivacyRestoreNativeV1.Epoch(db,t),a.Permissions.ActionKeys.Where(x=>x.StartsWith(Menu+":",StringComparison.Ordinal)).Select(x=>x[(Menu.Length+1)..]).Order().ToArray(),
            RestoreOptions.Candidates.OrderBy(x=>x.Key).Select(x=>new PrivacyRestoreCandidateRefV1(x.Key,x.Value.RegistrationVersion,x.Value.BackupSha256,x.Value.DisplayName)).ToArray());},ct);
    private Task<PrivacyRestoreTombstoneV1[]> RestoreTombstones(Authority a,CancellationToken ct)=>db.Database.SqlQuery<PrivacyRestoreTombstoneV1>($"""
        SELECT plan_id AS "PlanId",actor_tenant AS "ActorTenantId",target_tenant AS "TargetTenantId",subject_id AS "SubjectId",policy_id AS "PolicyId",receipt_json AS "Receipt"
        FROM cp6_privacy_disposition_v1.tombstones WHERE environment_id={a.Environment} ORDER BY plan_id
        """).ToArrayAsync(ct);
    private sealed record RestorePolicy(bool Enabled,Guid TargetTenant);
    private Task<PrivacyExportPayloadTombstoneV1[]> RestorePayloadTombstones(Authority a,CancellationToken ct)=>db.Database.SqlQuery<PrivacyExportPayloadTombstoneV1>($"""
        SELECT artifact_id AS "ArtifactId",source_plan_id AS "SourcePlanId",operator_tenant AS "OperatorTenantId",target_tenant AS "TargetTenantId",subject_id AS "SubjectId",
          policy_id AS "PolicyId",scope_json AS "Scope",receipt_json AS "Receipt" FROM cp6_privacy_export_disposition_v1.tombstones WHERE environment_id={a.Environment} ORDER BY artifact_id
        """).ToArrayAsync(ct);
    private sealed record RestorePayloadPolicy(bool Enabled,Guid TargetTenant,int MinimumDays);
    private Task<PrivacySecurityFieldTombstoneV1[]> RestoreSecurityTombstones(Authority a,CancellationToken ct)=>db.Database.SqlQuery<PrivacySecurityFieldTombstoneV1>($"""
        SELECT log_id AS "LogId",source_plan_id AS "SourcePlanId",operator_tenant AS "OperatorTenantId",target_tenant AS "TargetTenantId",subject_id AS "SubjectId",
          policy_id AS "PolicyId",scope_json AS "Scope",receipt_json AS "Receipt" FROM cp6_privacy_security_fields_v1.tombstones WHERE environment_id={a.Environment} ORDER BY log_id
        """).ToArrayAsync(ct);
    private sealed record RestoreSecurityPolicy(bool Enabled,Guid TargetTenant,DateTimeOffset NotBeforeUtc);
    private async Task<PrivacyRestorePreviewV1> RestorePreview(Authority a,Guid candidateId,CancellationToken ct)
    {
        await RequireTenantPayloadRestoreSupport(a,ct);
        var registration=RestoreCandidate(candidateId);var source=await PrivacyRestoreNativeV1.Database(db,ct);var version=await PrivacyRestoreNativeV1.Epoch(db,ct);
        var tombstones=await RestoreTombstones(a,ct);var blockers=new List<string>();
        foreach(var tomb in tombstones)
        {
            var receipt=JsonSerializer.Deserialize<PrivacyDispositionExecutionV1>(tomb.Receipt)!;
            if(receipt.Owner!="Main.Identity.Profile.v1"||receipt.Action!=Action)throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_OWNER_UNSUPPORTED",503);
            var policy=await db.Database.SqlQuery<RestorePolicy>($"SELECT enabled AS \"Enabled\",target_tenant AS \"TargetTenant\" FROM cp6_privacy_disposition_v1.policies WHERE environment_id={a.Environment} AND actor_tenant={tomb.ActorTenantId} AND policy_id={tomb.PolicyId} ORDER BY sequence DESC LIMIT 1").SingleAsync(ct);
            if(!policy.Enabled)blockers.Add("CURRENT_POLICY_DISABLED:"+tomb.PlanId.ToString("D"));
            if(policy.TargetTenant!=tomb.TargetTenantId)blockers.Add("CURRENT_POLICY_TARGET_CHANGED:"+tomb.PlanId.ToString("D"));
            if((await Hold(a,tomb.TargetTenantId,tomb.SubjectId,ct))?.Active==true)blockers.Add("CURRENT_HOLD_REQUIRES_RESTORE_DECISION:"+tomb.PlanId.ToString("D"));
        }
        var payloads=await RestorePayloadTombstones(a,ct);var at=await Clock(ct);
        if(payloads.Length>0)await PayloadReady(ct);
        foreach(var tomb in payloads)
        {
            var policy=await db.Database.SqlQuery<RestorePayloadPolicy>($"""
                SELECT enabled AS "Enabled",target_tenant AS "TargetTenant",minimum_days AS "MinimumDays" FROM cp6_privacy_export_disposition_v1.policies
                WHERE environment_id={a.Environment} AND actor_tenant={tomb.OperatorTenantId} AND policy_id={tomb.PolicyId} ORDER BY sequence DESC LIMIT 1
                """).SingleAsync(ct);
            if(!policy.Enabled)blockers.Add("CURRENT_PAYLOAD_POLICY_DISABLED:"+tomb.SourcePlanId.ToString("D"));
            if(policy.TargetTenant!=tomb.TargetTenantId)blockers.Add("CURRENT_PAYLOAD_POLICY_TARGET_CHANGED:"+tomb.SourcePlanId.ToString("D"));
            var payloadScope=JsonSerializer.Deserialize<PayloadDispositionScopeV1>(tomb.Scope)!;
            if(at<payloadScope.CapturedAt.AddSeconds((long)policy.MinimumDays*86400))blockers.Add("CURRENT_PAYLOAD_MINIMUM_RETENTION_NOT_REACHED:"+tomb.SourcePlanId.ToString("D"));
            if((await Hold(a,tomb.TargetTenantId,tomb.SubjectId,ct))?.Active==true)blockers.Add("CURRENT_PAYLOAD_HOLD_REQUIRES_RESTORE_DECISION:"+tomb.SourcePlanId.ToString("D"));
        }
        var security=await RestoreSecurityTombstones(a,ct);
        if(security.Length>0)await SecurityReady(ct);
        foreach(var tomb in security)
        {
            var policy=await db.Database.SqlQuery<RestoreSecurityPolicy>($"""
                SELECT enabled AS "Enabled",target_tenant AS "TargetTenant",not_before AS "NotBeforeUtc" FROM cp6_privacy_security_fields_v1.policies
                WHERE environment_id={a.Environment} AND actor_tenant={tomb.OperatorTenantId} AND policy_id={tomb.PolicyId} ORDER BY sequence DESC LIMIT 1
                """).SingleAsync(ct);
            if(!policy.Enabled)blockers.Add("CURRENT_SECURITY_POLICY_DISABLED:"+tomb.SourcePlanId.ToString("D"));
            if(policy.TargetTenant!=tomb.TargetTenantId)blockers.Add("CURRENT_SECURITY_POLICY_TARGET_CHANGED:"+tomb.SourcePlanId.ToString("D"));
            if(at<policy.NotBeforeUtc)blockers.Add("CURRENT_SECURITY_NOT_BEFORE_NOT_REACHED:"+tomb.SourcePlanId.ToString("D"));
            if((await Hold(a,tomb.TargetTenantId,tomb.SubjectId,ct))?.Active==true)blockers.Add("CURRENT_SECURITY_HOLD_REQUIRES_RESTORE_DECISION:"+tomb.SourcePlanId.ToString("D"));
        }
        await using var candidate=PrivacyRestoreNativeV1.Context(registration.ConnectionString,a.Actor.TenantId,identity);
        await candidate.Database.OpenConnectionAsync(ct);var target=await PrivacyRestoreNativeV1.Database(candidate,ct);
        if(PrivacyRestoreNativeV1.SameDatabase(target,source))throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_SOURCE_CANNOT_BE_TARGET",412);
        var uncovered=payloads.Length==0?Uncovered:Uncovered.Select(x=>x=="FILE_AND_PRIOR_EXPORT_COPIES"?"FILE_AND_OTHER_OWNER_COPIES":x).ToArray();
        if(security.Length>0)uncovered=uncovered.Select(x=>x=="MAIN.SECURITY_LOG_CONTENT"?"MAIN.SECURITY_LOG_UNCLASSIFIED_CONTENT":x).ToArray();
        var scope=new PrivacyRestoreScopeV1(a.Environment,candidateId,registration.RegistrationVersion,registration.BackupSha256,version,source,target,tombstones.Length,uncovered,payloads.Length,security.Length);
        await using var gate=await PrivacyRestoreNativeV1.CandidateGate(candidate,scope,ct);
        return new(scope,MainOwnerOperationJournalV1.Digest(MainOwnerOperationJournalV1.Encode(scope)),blockers.ToArray());
    }
    public Task<PrivacyRestorePreviewV1> RestorePreviewAsync(Guid candidate,TenantManagementSessionV1 session,CancellationToken ct=default)
    {Id(candidate);return Transaction(false,session,(a,t)=>{Can(a,"read");return RestorePreview(a,candidate,t);},ct);}
    private Task<PlanRow?> RestorePlan(Authority a,Guid id,CancellationToken ct)=>db.Database.SqlQuery<PlanRow>($"""
        SELECT id AS "Id",request_ref AS "RequestRef",requester_id AS "Requester",at AS "At",scope_json AS "Scope",scope_digest AS "Digest"
        FROM cp6_privacy_disposition_v1.restore_plans WHERE environment_id={a.Environment} AND actor_tenant={a.Actor.TenantId} AND id={id}
        """).SingleOrDefaultAsync(ct);
    private Task<PlanEvent> RestoreEvent(Guid plan,CancellationToken ct)=>db.Database.SqlQuery<PlanEvent>($"""
        SELECT version AS "Version",state AS "State",actor_id AS "Actor",decision_ref AS "DecisionRef"
        FROM cp6_privacy_disposition_v1.restore_events WHERE plan_id={plan} ORDER BY sequence DESC LIMIT 1
        """).SingleAsync(ct);
    private async Task<PrivacyRestorePlanV1> RestoreView(Authority a,PlanRow row,CancellationToken ct)
    {
        var scope=JsonSerializer.Deserialize<PrivacyRestoreScopeV1>(row.Scope)!;var last=await RestoreEvent(row.Id,ct);var blockers=new List<string>();
        if(await PrivacyRestoreNativeV1.Epoch(db,ct)!=scope.SourceVersion)blockers.Add("SOURCE_VERSION_CHANGED");
        if(!RestoreOptions.Candidates.TryGetValue(scope.CandidateId,out var registration)||registration.RegistrationVersion!=scope.RegistrationVersion||registration.BackupSha256!=scope.BackupSha256)blockers.Add("CANDIDATE_REGISTRATION_CHANGED");
        if(last.State=="APPROVED"&&(!await db.Sys_Users.AsNoTracking().AnyAsync(x=>x.Id==last.Actor&&x.Enable&&x.IsPlatformAdmin,ct)
            ||!(await new PermissionAggregator(db).BuildAsync(last.Actor,ct)).ActionKeys.Contains(Menu+":restore-approve")))blockers.Add("RESTORE_APPROVER_AUTHORITY_CHANGED");
        var outcome=await RestorePlanOutcome(a,row.Id,ct);
        if(outcome!=null)blockers.Add("RESTORE_ALREADY_APPLIED");
        return new(row.Id,row.RequestRef,row.Requester,last.Version,last.State,last.State=="APPROVED"?last.Actor:null,scope,row.Digest,
            last.State=="APPROVED"&&blockers.Count==0,blockers.ToArray(),outcome);
    }
    public Task<PrivacyRestorePlanV1> RestorePlanAsync(Guid id,TenantManagementSessionV1 session,CancellationToken ct=default)
    {Id(id);return Transaction(false,session,async(a,t)=>{Can(a,"read");return await RestoreView(a,await RestorePlan(a,id,t)??throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_PLAN_NOT_FOUND",404),t);},ct);}
    private async Task<PrivacyRestoreMetaOperationV1?> RestoreMetaOriginal(Authority a,Guid operation,byte[]? bytes,CancellationToken ct)
    {
        var old=await new MainOwnerOperationJournalV1(db).Read(RestoreMetaJournal(a),operation,ct);if(old==null)return null;
        if(bytes!=null&&old.State!="CLOSED"&&!MainOwnerOperationJournalV1.Matches(old,bytes))throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_OPERATION_CONFLICT");
        return JsonSerializer.Deserialize<PrivacyRestoreMetaOperationV1>(old.Result)!;
    }
    private Task StoreRestoreMeta(Authority a,Guid operation,byte[]? input,PrivacyRestoreMetaOperationV1 result,CancellationToken ct)
        =>new MainOwnerOperationJournalV1(db).Store(RestoreMetaJournal(a),"Main.Privacy.Restore.Metadata.v1",operation,a.Actor.Id,input,result,
            result.PlanId is {} id?[id]:[],new{result.PlanId,result.Version,result.State},ct);
    private Task<PrivacyRestoreMetaOperationV1> ChangeRestoreMeta(Guid operation,string kind,object input,string capability,TenantManagementSessionV1 session,
        Func<Authority,CancellationToken,Task<(Guid Plan,Guid Version,string State)>> effect,CancellationToken ct)
    {
        Id(operation);var bytes=MainOwnerOperationJournalV1.Encode(new{kind,input});return Transaction(true,session,async(a,t)=>
        {
            Can(a,"read");var old=await RestoreMetaOriginal(a,operation,bytes,t);if(old!=null)return old;Can(a,capability);
            var (plan,version,state)=await effect(a,t);var result=new PrivacyRestoreMetaOperationV1(operation,plan,version,state);
            await StoreRestoreMeta(a,operation,bytes,result,t);return result;
        },ct);
    }
    public Task<PrivacyRestoreMetaOperationV1?> RestoreMetadataOperationAsync(Guid operation,bool close,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(operation);return Transaction(close,session,async(a,t)=>{Can(a,"read");var old=await RestoreMetaOriginal(a,operation,null,t);if(old!=null||!close)return old;
            var result=new PrivacyRestoreMetaOperationV1(operation,null,null,"CLOSED");await StoreRestoreMeta(a,operation,null,result,t);return result;},ct);
    }
    public Task<PrivacyRestoreMetaOperationV1> RequestRestoreAsync(PrivacyRestorePlanRequestV1 input,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(input.CandidateId);Id(input.RequestRef);if(string.IsNullOrEmpty(input.ExpectedScopeDigest))throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_PREVIEW_REQUIRED",400);
        return ChangeRestoreMeta(input.OperationId,"RESTORE_PLAN",input,"restore-prepare",session,async(a,t)=>
        {
            var preview=await RestorePreview(a,input.CandidateId,t);if(preview.ScopeDigest!=input.ExpectedScopeDigest)throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_PREVIEW_CHANGED",412);
            if(preview.Blockers.Length!=0)throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_POLICY_BLOCKED");
            var plan=Guid.NewGuid();var version=Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_privacy_disposition_v1.restore_plans(id,environment_id,actor_tenant,requester_id,request_ref,scope_json,scope_digest)
                VALUES({plan},{a.Environment},{a.Actor.TenantId},{a.Actor.Id},{input.RequestRef},{JsonSerializer.Serialize(preview.Scope)},{preview.ScopeDigest});
                INSERT INTO cp6_privacy_disposition_v1.restore_events(plan_id,version,state,actor_id) VALUES({plan},{version},'REQUESTED',{a.Actor.Id});
                """,t);return(plan,version,"REQUESTED");
        },ct);
    }
    public Task<PrivacyRestoreMetaOperationV1> DecideRestoreAsync(Guid id,PrivacyDispositionDecisionV1 input,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(id);Id(input.ExpectedVersion);Id(input.DecisionRef);if(input.Decision is not("APPROVE" or "REJECT" or "REVOKE"))throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_DECISION_INVALID",400);
        return ChangeRestoreMeta(input.OperationId,"RESTORE_"+input.Decision,new{id,input},"restore-approve",session,async(a,t)=>
        {
            var row=await RestorePlan(a,id,t)??throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_PLAN_NOT_FOUND",404);var current=await RestoreView(a,row,t);
            if(current.Outcome!=null)throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_ALREADY_APPLIED");
            if(current.Version!=input.ExpectedVersion)throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_APPROVAL_CHANGED",412);
            if(input.Decision!="REVOKE"&&row.Requester==a.Actor.Id)throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_INDEPENDENT_APPROVER_REQUIRED",403);
            if(current.State!="REQUESTED"&&!(input.Decision=="REVOKE"&&current.State=="APPROVED"))throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_STATE_CONFLICT");
            if(input.Decision=="APPROVE")
            {
                var preview=await RestorePreview(a,current.Scope.CandidateId,t);
                if(preview.ScopeDigest!=row.Digest||preview.Blockers.Length!=0)throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_SCOPE_CHANGED",412);
            }
            var version=Guid.NewGuid();var state=input.Decision=="APPROVE"?"APPROVED":input.Decision=="REJECT"?"REJECTED":"REVOKED";
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_privacy_disposition_v1.restore_events(plan_id,version,previous_version,state,actor_id,decision_ref) VALUES({id},{version},{current.Version},{state},{a.Actor.Id},{input.DecisionRef})",t);
            return(id,version,state);
        },ct);
    }
}
