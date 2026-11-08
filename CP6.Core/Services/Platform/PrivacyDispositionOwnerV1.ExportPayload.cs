using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Platform;
public sealed partial class PrivacyDispositionOwnerV1
{
    internal const string PayloadOwner="Main.Privacy.ExportPayload.v1",PayloadAction="DELETE_NATIVE_EXPORT_PAYLOAD";
    private static readonly string[] PayloadUncovered=["MAIN.IDENTITY_PROFILE_AND_SECURITY_OR_BUSINESS_HISTORY","FILE_AND_OTHER_OWNER_COPIES","WAL_AND_BACKUPS","EXTERNAL_DOWNLOADED_COPIES","UNREGISTERED_OWNERS"];
    public Task<PayloadDispositionContextV1> PayloadContextAsync(TenantManagementSessionV1 session,CancellationToken ct=default)
        =>Transaction(false,session,(a,_)=>{Can(a,"payload-read");return Task.FromResult(new PayloadDispositionContextV1(a.Environment,a.Actor.TenantId,a.Actor.Id,PayloadOwner,PayloadAction,
            a.Permissions.ActionKeys.Where(x=>x.StartsWith(Menu+":payload-",StringComparison.Ordinal)||x==Menu+":hold").Select(x=>x[(Menu.Length+1)..]).Order().ToArray(),36500,PayloadUncovered));},ct);
    private string PayloadJournal(Authority a)=>PayloadOwner+"/"+a.Environment.ToString("D")+"/"+a.Actor.Id.ToString("D");
    private Task PayloadReady(CancellationToken ct)=>PrivacyExportPayloadEffectV1.Ready(db,ct);
    private Task<PayloadDispositionPolicyV1?> PayloadPolicy(Authority a,Guid id,CancellationToken ct)=>db.Database.SqlQuery<PayloadDispositionPolicyV1>($"""
        SELECT policy_id AS "PolicyId",version AS "Version",target_tenant AS "TargetTenantId",basis_ref AS "BasisRef",enabled AS "Enabled",
          minimum_days AS "MinimumRetentionDays",require_access_closed AS "RequireAccessClosed",owner_id AS "OwnerId",at AS "At"
        FROM cp6_privacy_export_disposition_v1.policies WHERE environment_id={a.Environment} AND actor_tenant={a.Actor.TenantId} AND policy_id={id} ORDER BY sequence DESC LIMIT 1
        """).SingleOrDefaultAsync(ct);
    public Task<PayloadDispositionPolicyV1?> PayloadPolicyAsync(Guid id,TenantManagementSessionV1 session,CancellationToken ct=default)
    {Id(id);return Transaction(false,session,(a,t)=>{Can(a,"payload-read");return PayloadPolicy(a,id,t);},ct);}
    public Task<PrivacyDispositionHoldV1?> PayloadHoldAsync(Guid artifact,TenantManagementSessionV1 session,CancellationToken ct=default)
    {Id(artifact);return Transaction(false,session,async(a,t)=>{Can(a,"payload-read");var row=await PayloadArtifact(a,artifact,t);return await Hold(a,row.TargetTenantId,row.SubjectId,t);},ct);}
    private sealed record PayloadArtifactRow(Guid TargetTenantId,Guid SubjectId,DateTimeOffset ExpiresAt,DateTimeOffset? RevokedAt,byte[] Receipt,Guid? Version,long? Length,bool Disposed);
    private async Task<PayloadArtifactRow> PayloadArtifact(Authority a,Guid id,CancellationToken ct)=>await db.Database.SqlQuery<PayloadArtifactRow>($"""
        SELECT a.target_tenant AS "TargetTenantId",a.subject_id AS "SubjectId",a.expires_at AS "ExpiresAt",a.revoked_at AS "RevokedAt",a.receipt_bytes AS "Receipt",
          p.revision AS "Version",octet_length(p.bytes)::bigint AS "Length",EXISTS(SELECT FROM cp6_privacy_export_disposition_v1.tombstones t WHERE t.artifact_id=a.id) AS "Disposed"
        FROM cp6_privacy_export_v1.artifacts a LEFT JOIN cp6_privacy_export_v1.payloads p ON p.artifact_id=a.id
        WHERE a.id={id} AND a.environment_id={a.Environment} AND a.actor_tenant={a.Actor.TenantId}
        """).SingleOrDefaultAsync(ct)??throw new PrivacyDispositionExceptionV1("PRIVACY_EXPORT_PAYLOAD_ARTIFACT_NOT_FOUND",404);
    private static string PayloadAccess(PayloadArtifactRow row,DateTimeOffset at)=>row.Disposed?"DISPOSED":row.RevokedAt!=null?"REVOKED":row.ExpiresAt<=at?"EXPIRED":"AVAILABLE";
    private async Task<PayloadDispositionPreviewV1> PayloadPreview(Authority a,PayloadDispositionTargetV1 target,CancellationToken ct,bool forPlan=false)
    {
        var artifact=await PayloadArtifact(a,target.ArtifactId,ct);
        var policy=await PayloadPolicy(a,target.PolicyId,ct)??throw new PrivacyDispositionExceptionV1("PRIVACY_EXPORT_PAYLOAD_POLICY_NOT_FOUND",404);
        var targetChanged=policy.TargetTenantId!=artifact.TargetTenantId;
        if(targetChanged&&!forPlan)throw new PrivacyDispositionExceptionV1("PRIVACY_EXPORT_PAYLOAD_POLICY_TARGET_CHANGED",412);
        var receipt=JsonSerializer.Deserialize<PrivacyExportReceiptV1>(artifact.Receipt)!;
        var hold=await Hold(a,artifact.TargetTenantId,artifact.SubjectId,ct);var at=await Clock(ct);
        var earliest=receipt.CapturedAt.AddSeconds((long)policy.MinimumRetentionDays*86400);
        var access=PayloadAccess(artifact,at);var blockers=new List<string>();
        if(targetChanged)blockers.Add("POLICY_TARGET_CHANGED");
        if(!policy.Enabled)blockers.Add("POLICY_DISABLED");
        if(artifact.Disposed)blockers.Add("PAYLOAD_ALREADY_DISPOSED");
        if(!artifact.Disposed)
        {if(artifact.Version==null)blockers.Add("PAYLOAD_MISSING");else if(artifact.Length!=receipt.Length)blockers.Add("PAYLOAD_LENGTH_MISMATCH");}
        if(at<earliest)blockers.Add("MINIMUM_RETENTION_NOT_REACHED");
        if(policy.RequireAccessClosed&&access=="AVAILABLE")blockers.Add("ACCESS_MUST_BE_CLOSED");
        if(hold?.Active==true)blockers.Add("ACTIVE_HOLD");
        var scope=new PayloadDispositionScopeV1(a.Environment,a.Actor.TenantId,target.ArtifactId,artifact.TargetTenantId,artifact.SubjectId,artifact.Version,artifact.Length,
            receipt.Sha256,receipt.Length,receipt.Purpose,receipt.CapturedAt,receipt.ExpiresAt,policy.PolicyId,policy.Version,policy.BasisRef,policy.MinimumRetentionDays,policy.RequireAccessClosed,earliest,hold?.Version);
        return new(scope,MainOwnerOperationJournalV1.Digest(MainOwnerOperationJournalV1.Encode(scope)),hold,hold?.Active==true&&hold.ReviewAt<=at,access,blockers.ToArray());
    }
    public Task<PayloadDispositionPreviewV1> PayloadPreviewAsync(PayloadDispositionTargetV1 target,TenantManagementSessionV1 session,CancellationToken ct=default)
    {Id(target.ArtifactId);Id(target.PolicyId);return Transaction(false,session,async(a,t)=>{Can(a,"payload-read");await PayloadReady(t);return await PayloadPreview(a,target,t);},ct);}
    private Task<PlanRow?> PayloadPlan(Authority a,Guid id,CancellationToken ct)=>db.Database.SqlQuery<PlanRow>($"""
        SELECT id AS "Id",request_ref AS "RequestRef",requester_id AS "Requester",at AS "At",scope_json AS "Scope",scope_digest AS "Digest"
        FROM cp6_privacy_export_disposition_v1.plans WHERE id={id} AND environment_id={a.Environment} AND actor_tenant={a.Actor.TenantId}
        """).SingleOrDefaultAsync(ct);
    private Task<PlanEvent> PayloadEvent(Guid id,CancellationToken ct)=>db.Database.SqlQuery<PlanEvent>($"""
        SELECT version AS "Version",state AS "State",actor_id AS "Actor",decision_ref AS "DecisionRef"
        FROM cp6_privacy_export_disposition_v1.events WHERE plan_id={id} ORDER BY sequence DESC LIMIT 1
        """).SingleAsync(ct);
    private async Task<PayloadDispositionExecutionV1?> PayloadExecution(Authority a,Guid id,CancellationToken ct)
    {
        var json=await db.Database.SqlQuery<string>($"""
            SELECT receipt_json AS "Value" FROM cp6_privacy_export_disposition_v1.executions WHERE plan_id={id} AND environment_id={a.Environment} AND actor_tenant={a.Actor.TenantId}
            """).SingleOrDefaultAsync(ct);return json==null?null:JsonSerializer.Deserialize<PayloadDispositionExecutionV1>(json)!;
    }
    private async Task<PayloadDispositionPlanV1> PayloadView(Authority a,PlanRow row,CancellationToken ct)
    {
        var scope=JsonSerializer.Deserialize<PayloadDispositionScopeV1>(row.Scope)!;var last=await PayloadEvent(row.Id,ct);
        var execution=await PayloadExecution(a,row.Id,ct);var current=await PayloadPreview(a,new(scope.ArtifactId,scope.PolicyId),ct,forPlan:true);var blockers=current.Blockers.ToList();
        if(execution==null&&current.Scope.PayloadVersion!=scope.PayloadVersion)blockers.Add("PAYLOAD_VERSION_CHANGED");
        if(current.Scope.PolicyVersion!=scope.PolicyVersion)blockers.Add("POLICY_VERSION_CHANGED");
        if(current.Scope.HoldVersion!=scope.HoldVersion)blockers.Add("HOLD_VERSION_CHANGED");
        if(last.State=="APPROVED")
        {
            var eligible=await db.Sys_Users.AsNoTracking().AnyAsync(x=>x.Id==last.Actor&&x.Enable&&x.IsPlatformAdmin,ct);
            var actions=eligible?(await new PermissionAggregator(db).BuildAsync(last.Actor,ct)).ActionKeys:[];
            if(!eligible||!actions.Contains(Menu+":payload-approve")||!actions.Contains(Menu+":payload-read"))blockers.Add("APPROVER_AUTHORITY_CHANGED");
        }
        return new(row.Id,row.RequestRef,row.Requester,row.At,last.Version,last.State,last.State=="APPROVED"?last.Actor:null,last.DecisionRef,scope,row.Digest,current.Hold,current.AccessState,
            blockers.ToArray(),last.State=="APPROVED"&&blockers.Count==0,execution);
    }
    public Task<PayloadDispositionPlanV1> PayloadPlanAsync(Guid id,TenantManagementSessionV1 session,CancellationToken ct=default)
    {Id(id);return Transaction(false,session,async(a,t)=>{Can(a,"payload-read");return await PayloadView(a,await PayloadPlan(a,id,t)??throw new PrivacyDispositionExceptionV1("PRIVACY_EXPORT_PAYLOAD_PLAN_NOT_FOUND",404),t);},ct);}
    private async Task<PayloadDispositionOperationV1?> PayloadOriginal(Authority a,Guid operation,byte[]? input,CancellationToken ct)
    {
        var old=await new MainOwnerOperationJournalV1(db).Read(PayloadJournal(a),operation,ct);if(old==null)return null;
        if(input!=null&&old.State!="CLOSED"&&!MainOwnerOperationJournalV1.Matches(old,input))throw new PrivacyDispositionExceptionV1("PRIVACY_EXPORT_PAYLOAD_OPERATION_CONFLICT");
        return JsonSerializer.Deserialize<PayloadDispositionOperationV1>(old.Result)!;
    }
    private Task PayloadStore(Authority a,Guid operation,byte[]? input,PayloadDispositionOperationV1 result,CancellationToken ct)
        =>new MainOwnerOperationJournalV1(db).Store(PayloadJournal(a),"Main.Privacy.ExportPayload.commands.v1",operation,a.Actor.Id,input,result,
            result.PlanId is{} id?[id]:[],new{result.Kind,result.PlanId,result.PolicyId,result.ArtifactId},ct);
    private Task<PayloadDispositionOperationV1> PayloadChange(Guid operation,string kind,object input,string capability,TenantManagementSessionV1 session,
        Func<Authority,CancellationToken,Task<PayloadDispositionOperationV1>> effect,CancellationToken ct)
    {
        Id(operation);var bytes=MainOwnerOperationJournalV1.Encode(new{kind,input});return Transaction(true,session,async(a,t)=>
        {
            Can(a,"payload-read");var old=await PayloadOriginal(a,operation,bytes,t);if(old!=null)return old;Can(a,capability);await PayloadReady(t);
            var result=await effect(a,t);await PayloadStore(a,operation,bytes,result,t);return result;
        },ct);
    }
    public Task<PayloadDispositionOperationV1> PayloadOperationAsync(Guid operation,bool close,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(operation);return Transaction(close,session,async(a,t)=>
        {
            Can(a,"payload-read");var old=await PayloadOriginal(a,operation,null,t);if(old!=null)return old;
            var result=new PayloadDispositionOperationV1(operation,close?"CLOSED":"NOT_FOUND","CLOSE");if(close)await PayloadStore(a,operation,null,result,t);return result;
        },ct);
    }
}
