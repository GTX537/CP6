using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Platform;
public sealed partial class PrivacyDispositionOwnerV1
{
    internal const string SecurityOwner="Main.SecurityLog.Fields.v1",SecurityAction="CLEAR_CLASSIFIED_SECURITY_LOG_FIELDS";
    internal static readonly string[] SecurityFields=["UserName","ClientIp"];
    internal static readonly string[] SecurityUncovered=["SECURITY_UNCLASSIFIED_CONTEXT_REASON_DEVICE_AND_ACTOR_METADATA","ANONYMOUS_OR_UNATTRIBUTABLE_EVENTS","IDENTITY_AND_BUSINESS_HISTORY","FILE_AND_EXPORT_COPIES","WAL_BACKUPS_AND_EXTERNAL_COPIES","UNREGISTERED_OWNERS"];
    public Task<SecurityFieldContextV1> SecurityContextAsync(TenantManagementSessionV1 session,CancellationToken ct=default)
        =>Transaction(false,session,(a,_)=>{Can(a,"security-read");return Task.FromResult(new SecurityFieldContextV1(a.Environment,a.Actor.TenantId,a.Actor.Id,SecurityOwner,SecurityAction,
            a.Permissions.ActionKeys.Where(x=>x.StartsWith(Menu+":security-",StringComparison.Ordinal)||x==Menu+":hold").Select(x=>x[(Menu.Length+1)..]).Order().ToArray(),SecurityFields,SecurityUncovered));},ct);
    private string SecurityJournal(Authority a)=>SecurityOwner+"/"+a.Environment.ToString("D")+"/"+a.Actor.Id.ToString("D");
    private Task SecurityReady(CancellationToken ct)=>PrivacySecurityFieldEffectV1.Ready(db,ct);
    private Task<SecurityFieldPolicyV1?> SecurityPolicy(Authority a,Guid id,CancellationToken ct)=>db.Database.SqlQuery<SecurityFieldPolicyV1>($"""
        SELECT policy_id AS "PolicyId",version AS "Version",target_tenant AS "TargetTenantId",basis_ref AS "BasisRef",enabled AS "Enabled",not_before AS "NotBeforeUtc",owner_id AS "OwnerId",at AS "At"
        FROM cp6_privacy_security_fields_v1.policies WHERE environment_id={a.Environment} AND actor_tenant={a.Actor.TenantId} AND policy_id={id} ORDER BY sequence DESC LIMIT 1
        """).SingleOrDefaultAsync(ct);
    public Task<SecurityFieldPolicyV1?> SecurityPolicyAsync(Guid id,TenantManagementSessionV1 session,CancellationToken ct=default)
    {Id(id);return Transaction(false,session,(a,t)=>{Can(a,"security-read");return SecurityPolicy(a,id,t);},ct);}
    private sealed record SecurityRow(Guid Tenant,Guid? Subject,Guid Version,int EventType,DateTime CreatedAt,int ValueCount,string? Digest,bool Disposed);
    private Task<SecurityRow?> SecurityLog(Guid id,bool digest,CancellationToken ct)=>db.Database.SqlQuery<SecurityRow>($"""
        SELECT l."TenantId" AS "Tenant",l."UserId" AS "Subject",l.privacy_revision AS "Version",l."EventType",l."CreatedAt",
          (l."UserName" IS NOT NULL)::integer+(l."ClientIp" IS NOT NULL)::integer AS "ValueCount",
          CASE WHEN {digest} THEN cp6_privacy_security_fields_v1.field_digest(l."UserName"::text,l."ClientIp"::text) END AS "Digest",
          EXISTS(SELECT FROM cp6_privacy_security_fields_v1.tombstones t WHERE t.log_id=l."Id") AS "Disposed"
        FROM public."Sys_SecurityLogs" l WHERE l."Id"={id}
        """).SingleOrDefaultAsync(ct);
    private async Task<(PrivacyDispositionHoldV1? Hold,bool Overdue,List<string> Blockers)> SecurityEligibility(Authority a,SecurityFieldPolicyV1 policy,Guid tenant,Guid subject,CancellationToken ct)
    {
        var hold=await Hold(a,tenant,subject,ct);var at=await Clock(ct);var blockers=new List<string>();
        if(policy.TargetTenantId!=tenant)blockers.Add("POLICY_TARGET_CHANGED");
        if(!policy.Enabled)blockers.Add("POLICY_DISABLED");
        if(at<policy.NotBeforeUtc)blockers.Add("POLICY_NOT_BEFORE_NOT_REACHED");
        if(hold?.Active==true)blockers.Add("ACTIVE_HOLD");
        return(hold,hold?.Active==true&&hold.ReviewAt<=at,blockers);
    }
    private async Task<SecurityFieldPreviewV1> SecurityPreview(Authority a,SecurityFieldTargetV1 target,CancellationToken ct)
    {
        var row=await SecurityLog(target.LogId,true,ct)??throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_LOG_NOT_FOUND",404);
        if(row.Subject==null)throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_UNATTRIBUTABLE_EVENT");
        var policy=await SecurityPolicy(a,target.PolicyId,ct)??throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_POLICY_NOT_FOUND",404);
        if(policy.TargetTenantId!=row.Tenant)throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_POLICY_TARGET_CHANGED",412);
        var eligibility=await SecurityEligibility(a,policy,row.Tenant,row.Subject.Value,ct);
        if(row.Disposed)eligibility.Blockers.Add("FIELDS_ALREADY_DISPOSED");else if(row.ValueCount==0)eligibility.Blockers.Add("NO_CLASSIFIED_CONTENT");
        var scope=new SecurityFieldScopeV1(a.Environment,a.Actor.TenantId,target.LogId,row.Tenant,row.Subject.Value,row.EventType,row.CreatedAt,row.Version,row.Digest!,policy.PolicyId,policy.Version,policy.BasisRef,policy.NotBeforeUtc,eligibility.Hold?.Version,SecurityFields,SecurityUncovered);
        return new(scope,MainOwnerOperationJournalV1.Digest(MainOwnerOperationJournalV1.Encode(scope)),row.ValueCount,eligibility.Hold,eligibility.Overdue,eligibility.Blockers.ToArray());
    }
    public Task<SecurityFieldPreviewV1> SecurityPreviewAsync(SecurityFieldTargetV1 target,TenantManagementSessionV1 session,CancellationToken ct=default)
    {Id(target.LogId);Id(target.PolicyId);return Transaction(false,session,async(a,t)=>{Can(a,"security-read");await SecurityReady(t);return await SecurityPreview(a,target,t);},ct);}
    private Task<PlanRow?> SecurityPlan(Authority a,Guid id,CancellationToken ct)=>db.Database.SqlQuery<PlanRow>($"""
        SELECT id AS "Id",request_ref AS "RequestRef",requester_id AS "Requester",at AS "At",scope_json AS "Scope",scope_digest AS "Digest"
        FROM cp6_privacy_security_fields_v1.plans WHERE id={id} AND environment_id={a.Environment} AND actor_tenant={a.Actor.TenantId}
        """).SingleOrDefaultAsync(ct);
    private Task<PlanEvent> SecurityEvent(Guid id,CancellationToken ct)=>db.Database.SqlQuery<PlanEvent>($"""
        SELECT version AS "Version",state AS "State",actor_id AS "Actor",decision_ref AS "DecisionRef" FROM cp6_privacy_security_fields_v1.events WHERE plan_id={id} ORDER BY sequence DESC LIMIT 1
        """).SingleAsync(ct);
    private async Task<SecurityFieldExecutionV1?> SecurityExecution(Authority a,Guid id,CancellationToken ct)
    {
        var json=await db.Database.SqlQuery<string>($"""
            SELECT receipt_json AS "Value" FROM cp6_privacy_security_fields_v1.executions WHERE plan_id={id} AND environment_id={a.Environment} AND actor_tenant={a.Actor.TenantId}
            """).SingleOrDefaultAsync(ct);return json==null?null:JsonSerializer.Deserialize<SecurityFieldExecutionV1>(json)!;
    }
    private async Task<SecurityFieldPlanV1> SecurityView(Authority a,PlanRow row,CancellationToken ct)
    {
        var scope=JsonSerializer.Deserialize<SecurityFieldScopeV1>(row.Scope)!;var last=await SecurityEvent(row.Id,ct);var execution=await SecurityExecution(a,row.Id,ct);
        var policy=(await SecurityPolicy(a,scope.PolicyId,ct))!;var current=await SecurityLog(scope.LogId,false,ct);var eligibility=await SecurityEligibility(a,policy,scope.TargetTenantId,scope.SubjectId,ct);var blockers=eligibility.Blockers;
        if(current==null)blockers.Add("LOG_MISSING");
        else
        {
            if(current.Tenant!=scope.TargetTenantId||current.Subject!=scope.SubjectId)blockers.Add("LOG_BINDING_CHANGED");
            if(execution==null&&current.Version!=scope.TargetVersion)blockers.Add("LOG_VERSION_CHANGED");
            if(current.Disposed)blockers.Add("FIELDS_ALREADY_DISPOSED");else if(current.ValueCount==0)blockers.Add("NO_CLASSIFIED_CONTENT");
        }
        if(policy.Version!=scope.PolicyVersion)blockers.Add("POLICY_VERSION_CHANGED");
        if(eligibility.Hold?.Version!=scope.HoldVersion)blockers.Add("HOLD_VERSION_CHANGED");
        if(last.State=="APPROVED")
        {
            var eligible=await db.Sys_Users.AsNoTracking().AnyAsync(x=>x.Id==last.Actor&&x.Enable&&x.IsPlatformAdmin,ct);
            var actions=eligible?(await new PermissionAggregator(db).BuildAsync(last.Actor,ct)).ActionKeys:[];
            if(!eligible||!actions.Contains(Menu+":security-read")||!actions.Contains(Menu+":security-approve"))blockers.Add("APPROVER_AUTHORITY_CHANGED");
        }
        return new(row.Id,row.RequestRef,row.Requester,row.At,last.Version,last.State,last.State=="APPROVED"?last.Actor:null,last.DecisionRef,scope,row.Digest,eligibility.Hold,blockers.ToArray(),last.State=="APPROVED"&&blockers.Count==0,current?.ValueCount,execution);
    }
    public Task<SecurityFieldPlanV1> SecurityPlanAsync(Guid id,TenantManagementSessionV1 session,CancellationToken ct=default)
    {Id(id);return Transaction(false,session,async(a,t)=>{Can(a,"security-read");return await SecurityView(a,await SecurityPlan(a,id,t)??throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_PLAN_NOT_FOUND",404),t);},ct);}
    private async Task<SecurityFieldOperationV1?> SecurityOriginal(Authority a,Guid operation,byte[]? input,CancellationToken ct)
    {
        var old=await new MainOwnerOperationJournalV1(db).Read(SecurityJournal(a),operation,ct);if(old==null)return null;
        if(input!=null&&old.State!="CLOSED"&&!MainOwnerOperationJournalV1.Matches(old,input))throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_OPERATION_CONFLICT");
        return JsonSerializer.Deserialize<SecurityFieldOperationV1>(old.Result)!;
    }
    private Task SecurityStore(Authority a,Guid operation,byte[]? input,SecurityFieldOperationV1 result,CancellationToken ct)
        =>new MainOwnerOperationJournalV1(db).Store(SecurityJournal(a),"Main.SecurityLog.Fields.commands.v1",operation,a.Actor.Id,input,result,result.PlanId is{} id?[id]:[],new{result.Kind,result.PlanId,result.PolicyId,result.LogId},ct);
    private Task<SecurityFieldOperationV1> SecurityChange(Guid operation,string kind,object input,string capability,TenantManagementSessionV1 session,Func<Authority,CancellationToken,Task<SecurityFieldOperationV1>> effect,CancellationToken ct)
    {
        Id(operation);var bytes=MainOwnerOperationJournalV1.Encode(new{kind,input});return Transaction(true,session,async(a,t)=>
        {Can(a,"security-read");var old=await SecurityOriginal(a,operation,bytes,t);if(old!=null)return old;Can(a,capability);await SecurityReady(t);var result=await effect(a,t);await SecurityStore(a,operation,bytes,result,t);return result;},ct);
    }
    public Task<SecurityFieldOperationV1> SecurityOperationAsync(Guid operation,bool close,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(operation);return Transaction(close,session,async(a,t)=>{Can(a,"security-read");var old=await SecurityOriginal(a,operation,null,t);if(old!=null)return old;var result=new SecurityFieldOperationV1(operation,close?"CLOSED":"NOT_FOUND","CLOSE");if(close)await SecurityStore(a,operation,null,result,t);return result;},ct);
    }
    public Task<PrivacyDispositionHoldV1?> SecurityHoldAsync(Guid log,TenantManagementSessionV1 session,CancellationToken ct=default)
    {Id(log);return Transaction(false,session,async(a,t)=>{Can(a,"security-read");var row=await SecurityLog(log,false,t)??throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_LOG_NOT_FOUND",404);if(row.Subject==null)throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_UNATTRIBUTABLE_EVENT");return await Hold(a,row.Tenant,row.Subject.Value,t);},ct);}
}
