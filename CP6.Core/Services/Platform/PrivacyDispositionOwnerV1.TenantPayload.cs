using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Platform;
public sealed partial class PrivacyDispositionOwnerV1
{
    internal const string TenantPayloadOwner="Main.Privacy.TenantExportPayload.v1",TenantPayloadAction="DELETE_NATIVE_TENANT_EXPORT_PAYLOAD";
    private static readonly string[] TenantPayloadUncovered=["MAIN.IDENTITY_PROFILE_AND_SECURITY_OR_BUSINESS_HISTORY","FILE_AND_OTHER_OWNER_COPIES","WAL_AND_BACKUPS","EXTERNAL_DOWNLOADED_COPIES","UNREGISTERED_OWNERS"];
    public Task<TenantPayloadDispositionContextV1> TenantPayloadContextAsync(TenantManagementSessionV1 session,CancellationToken ct=default)
        =>Transaction(false,session,(a,_)=>{Can(a,"tenant-payload-read");return Task.FromResult(new TenantPayloadDispositionContextV1(a.Environment,a.Actor.TenantId,a.Actor.Id,TenantPayloadOwner,TenantPayloadAction,
            a.Permissions.ActionKeys.Where(x=>x.StartsWith(Menu+":tenant-payload-",StringComparison.Ordinal)).Select(x=>x[(Menu.Length+1)..]).Order().ToArray(),36500,TenantPayloadUncovered));},ct);
    private string TenantPayloadJournal(Authority a)=>TenantPayloadOwner+"/"+a.Environment.ToString("D")+"/"+a.Actor.Id.ToString("D");
    private Task<TenantPayloadDispositionPolicyV1?> TenantPayloadPolicy(Authority a,Guid id,CancellationToken ct)=>db.Database.SqlQuery<TenantPayloadDispositionPolicyV1>($"""
        SELECT policy_id AS "PolicyId",version AS "Version",target_tenant AS "TargetTenantId",basis_ref AS "BasisRef",enabled AS "Enabled",
          minimum_days AS "MinimumRetentionDays",require_access_closed AS "RequireAccessClosed",owner_id AS "OwnerId",at AS "At"
        FROM cp6_privacy_tenant_payload_disposition_v1.policies WHERE environment_id={a.Environment} AND actor_tenant={a.Actor.TenantId} AND policy_id={id} ORDER BY sequence DESC LIMIT 1
        """).SingleOrDefaultAsync(ct);
    public Task<TenantPayloadDispositionPolicyV1?> TenantPayloadPolicyAsync(Guid id,TenantManagementSessionV1 session,CancellationToken ct=default)
    {Id(id);return Transaction(false,session,(a,t)=>{Can(a,"tenant-payload-read");return TenantPayloadPolicy(a,id,t);},ct);}
    private Task<TenantPayloadHoldV1> TenantPayloadHold(Authority a,Guid tenant,CancellationToken ct)=>db.Database.SqlQuery<TenantPayloadHoldV1>($"""
        WITH current AS (SELECT DISTINCT ON(subject_id) version,active,review_at FROM cp6_privacy_disposition_v1.holds
          WHERE environment_id={a.Environment} AND target_tenant={tenant} ORDER BY subject_id,sequence DESC)
        SELECT (SELECT version FROM cp6_privacy_disposition_v1.holds WHERE environment_id={a.Environment} AND target_tenant={tenant} ORDER BY sequence DESC LIMIT 1) AS "Version",
          count(*) FILTER(WHERE active)::integer AS "ActiveCount",count(*) FILTER(WHERE active AND review_at<=clock_timestamp())::integer AS "OverdueCount" FROM current
        """).SingleAsync(ct);
    public Task<TenantPayloadHoldV1> TenantPayloadHoldAsync(Guid artifact,TenantManagementSessionV1 session,CancellationToken ct=default)
    {Id(artifact);return Transaction(false,session,async(a,t)=>{Can(a,"tenant-payload-read");var row=await TenantPayloadArtifact(a,artifact,t);return await TenantPayloadHold(a,row.TargetTenantId,t);},ct);}
    private sealed record TenantPayloadArtifactRow(Guid TargetTenantId,DateTimeOffset ExpiresAt,DateTimeOffset? RevokedAt,byte[] Receipt,Guid? Version,long? Length,bool Disposed);
    private async Task<TenantPayloadArtifactRow> TenantPayloadArtifact(Authority a,Guid id,CancellationToken ct)=>await db.Database.SqlQuery<TenantPayloadArtifactRow>($"""
        SELECT a.target_tenant AS "TargetTenantId",a.expires_at AS "ExpiresAt",a.revoked_at AS "RevokedAt",a.receipt_bytes AS "Receipt",
          p.revision AS "Version",octet_length(p.bytes)::bigint AS "Length",EXISTS(SELECT FROM cp6_privacy_tenant_payload_disposition_v1.tombstones t WHERE t.artifact_id=a.id) AS "Disposed"
        FROM cp6_privacy_tenant_export_v1.artifacts a LEFT JOIN cp6_privacy_tenant_export_v1.payloads p ON p.artifact_id=a.id
        WHERE a.id={id} AND a.environment_id={a.Environment} AND a.actor_tenant={a.Actor.TenantId}
        """).SingleOrDefaultAsync(ct)??throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_ARTIFACT_NOT_FOUND",404);
    private static string TenantPayloadAccess(TenantPayloadArtifactRow row,DateTimeOffset at)=>row.Disposed?"DISPOSED":row.RevokedAt!=null?"REVOKED":row.ExpiresAt<=at?"EXPIRED":"AVAILABLE";
    private async Task<TenantPayloadDispositionPreviewV1> TenantPayloadPreview(Authority a,TenantPayloadDispositionTargetV1 target,CancellationToken ct,bool forPlan=false)
    {
        var artifact=await TenantPayloadArtifact(a,target.ArtifactId,ct);
        var policy=await TenantPayloadPolicy(a,target.PolicyId,ct)??throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_POLICY_NOT_FOUND",404);
        var targetChanged=policy.TargetTenantId!=artifact.TargetTenantId;
        if(targetChanged&&!forPlan)throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_POLICY_TARGET_CHANGED",412);
        var receipt=JsonSerializer.Deserialize<TenantPrivacyExportReceiptV1>(artifact.Receipt)!;
        var hold=await TenantPayloadHold(a,artifact.TargetTenantId,ct);var at=await Clock(ct);
        var earliest=receipt.CapturedAt.AddSeconds((long)policy.MinimumRetentionDays*86400);
        var access=TenantPayloadAccess(artifact,at);var blockers=new List<string>();
        if(targetChanged)blockers.Add("POLICY_TARGET_CHANGED");
        if(!policy.Enabled)blockers.Add("POLICY_DISABLED");
        if(artifact.Disposed)blockers.Add("PAYLOAD_ALREADY_DISPOSED");
        if(!artifact.Disposed)
        {if(artifact.Version==null)blockers.Add("PAYLOAD_MISSING");else if(artifact.Length!=receipt.Length)blockers.Add("PAYLOAD_LENGTH_MISMATCH");}
        if(at<earliest)blockers.Add("MINIMUM_RETENTION_NOT_REACHED");
        if(policy.RequireAccessClosed&&access=="AVAILABLE")blockers.Add("ACCESS_MUST_BE_CLOSED");
        if(hold.ActiveCount>0)blockers.Add("ACTIVE_HOLD");
        var scope=new TenantPayloadDispositionScopeV1(a.Environment,a.Actor.TenantId,target.ArtifactId,artifact.TargetTenantId,artifact.Version,artifact.Length,
            receipt.Sha256,receipt.Length,receipt.Purpose,receipt.CapturedAt,receipt.ExpiresAt,policy.PolicyId,policy.Version,policy.BasisRef,policy.MinimumRetentionDays,policy.RequireAccessClosed,earliest,hold.Version);
        return new(scope,MainOwnerOperationJournalV1.Digest(MainOwnerOperationJournalV1.Encode(scope)),hold,hold.OverdueCount>0,access,blockers.ToArray());
    }
    public Task<TenantPayloadDispositionPreviewV1> TenantPayloadPreviewAsync(TenantPayloadDispositionTargetV1 target,TenantManagementSessionV1 session,CancellationToken ct=default)
    {Id(target.ArtifactId);Id(target.PolicyId);return Transaction(false,session,async(a,t)=>{Can(a,"tenant-payload-read");return await TenantPayloadPreview(a,target,t);},ct);}
    private Task<PlanRow?> TenantPayloadPlan(Authority a,Guid id,CancellationToken ct)=>db.Database.SqlQuery<PlanRow>($"""
        SELECT id AS "Id",request_ref AS "RequestRef",requester_id AS "Requester",at AS "At",scope_json AS "Scope",scope_digest AS "Digest"
        FROM cp6_privacy_tenant_payload_disposition_v1.plans WHERE id={id} AND environment_id={a.Environment} AND actor_tenant={a.Actor.TenantId}
        """).SingleOrDefaultAsync(ct);
    private Task<PlanEvent> TenantPayloadEvent(Guid id,CancellationToken ct)=>db.Database.SqlQuery<PlanEvent>($"""
        SELECT version AS "Version",state AS "State",actor_id AS "Actor",decision_ref AS "DecisionRef"
        FROM cp6_privacy_tenant_payload_disposition_v1.events WHERE plan_id={id} ORDER BY sequence DESC LIMIT 1
        """).SingleAsync(ct);
    private async Task<TenantPayloadDispositionExecutionV1?> TenantPayloadExecution(Authority a,Guid id,CancellationToken ct)
    {
        var json=await db.Database.SqlQuery<string>($"""
            SELECT receipt_json AS "Value" FROM cp6_privacy_tenant_payload_disposition_v1.executions WHERE plan_id={id} AND environment_id={a.Environment} AND actor_tenant={a.Actor.TenantId}
            """).SingleOrDefaultAsync(ct);return json==null?null:JsonSerializer.Deserialize<TenantPayloadDispositionExecutionV1>(json)!;
    }
    private async Task<TenantPayloadDispositionPlanV1> TenantPayloadView(Authority a,PlanRow row,CancellationToken ct)
    {
        var scope=JsonSerializer.Deserialize<TenantPayloadDispositionScopeV1>(row.Scope)!;var last=await TenantPayloadEvent(row.Id,ct);
        var execution=await TenantPayloadExecution(a,row.Id,ct);var current=await TenantPayloadPreview(a,new(scope.ArtifactId,scope.PolicyId),ct,forPlan:true);var blockers=current.Blockers.ToList();
        if(execution==null&&current.Scope.PayloadVersion!=scope.PayloadVersion)blockers.Add("PAYLOAD_VERSION_CHANGED");
        if(current.Scope.PolicyVersion!=scope.PolicyVersion)blockers.Add("POLICY_VERSION_CHANGED");
        if(current.Scope.HoldVersion!=scope.HoldVersion)blockers.Add("HOLD_VERSION_CHANGED");
        if(last.State=="APPROVED")
        {
            var eligible=await db.Sys_Users.AsNoTracking().AnyAsync(x=>x.Id==last.Actor&&x.Enable&&x.IsPlatformAdmin,ct);
            var actions=eligible?(await new PermissionAggregator(db).BuildAsync(last.Actor,ct)).ActionKeys:[];
            if(!eligible||!actions.Contains(Menu+":tenant-payload-approve")||!actions.Contains(Menu+":tenant-payload-read"))blockers.Add("APPROVER_AUTHORITY_CHANGED");
        }
        return new(row.Id,row.RequestRef,row.Requester,row.At,last.Version,last.State,last.State=="APPROVED"?last.Actor:null,last.DecisionRef,scope,row.Digest,current.Hold,current.AccessState,
            blockers.ToArray(),last.State=="APPROVED"&&blockers.Count==0,execution);
    }
    public Task<TenantPayloadDispositionPlanV1> TenantPayloadPlanAsync(Guid id,TenantManagementSessionV1 session,CancellationToken ct=default)
    {Id(id);return Transaction(false,session,async(a,t)=>{Can(a,"tenant-payload-read");return await TenantPayloadView(a,await TenantPayloadPlan(a,id,t)??throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_PLAN_NOT_FOUND",404),t);},ct);}
    private async Task<TenantPayloadDispositionOperationV1?> TenantPayloadOriginal(Authority a,Guid operation,byte[]? input,CancellationToken ct)
    {
        var old=await new MainOwnerOperationJournalV1(db).Read(TenantPayloadJournal(a),operation,ct);if(old==null)return null;
        if(input!=null&&old.State!="CLOSED"&&!MainOwnerOperationJournalV1.Matches(old,input))throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_OPERATION_CONFLICT");
        return JsonSerializer.Deserialize<TenantPayloadDispositionOperationV1>(old.Result)!;
    }
    private Task TenantPayloadStore(Authority a,Guid operation,byte[]? input,TenantPayloadDispositionOperationV1 result,CancellationToken ct)
        =>new MainOwnerOperationJournalV1(db).Store(TenantPayloadJournal(a),"Main.Privacy.TenantExportPayload.commands.v1",operation,a.Actor.Id,input,result,
            result.PlanId is{} id?[id]:[],new{result.Kind,result.PlanId,result.PolicyId,result.ArtifactId},ct);
    private Task<TenantPayloadDispositionOperationV1> TenantPayloadChange(Guid operation,string kind,object input,string capability,TenantManagementSessionV1 session,
        Func<Authority,CancellationToken,Task<TenantPayloadDispositionOperationV1>> effect,CancellationToken ct)
    {
        Id(operation);var bytes=MainOwnerOperationJournalV1.Encode(new{kind,input});return Transaction(true,session,async(a,t)=>
        {
            Can(a,"tenant-payload-read");var old=await TenantPayloadOriginal(a,operation,bytes,t);if(old!=null)return old;Can(a,capability);
            var result=await effect(a,t);await TenantPayloadStore(a,operation,bytes,result,t);return result;
        },ct);
    }
    public Task<TenantPayloadDispositionOperationV1> TenantPayloadOperationAsync(Guid operation,bool close,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(operation);return Transaction(close,session,async(a,t)=>
        {
            Can(a,"tenant-payload-read");var old=await TenantPayloadOriginal(a,operation,null,t);if(old!=null)return old;
            var result=new TenantPayloadDispositionOperationV1(operation,close?"CLOSED":"NOT_FOUND","CLOSE");if(close)await TenantPayloadStore(a,operation,null,result,t);return result;
        },ct);
    }
}
