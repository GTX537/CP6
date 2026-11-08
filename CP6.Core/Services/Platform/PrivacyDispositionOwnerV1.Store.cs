using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Platform;
public sealed partial class PrivacyDispositionOwnerV1
{
    public Task<PrivacyDispositionContextV1> ContextAsync(TenantManagementSessionV1 session,CancellationToken ct=default)
        =>Transaction(false,session,(a,_)=>{Can(a,"read");return Task.FromResult(new PrivacyDispositionContextV1(a.Environment,a.Actor.TenantId,a.Actor.Id,
            a.Permissions.ActionKeys.Where(x=>x.StartsWith(Menu+":",StringComparison.Ordinal)).Select(x=>x[(Menu.Length+1)..]).Order().ToArray(),Action,Fields,Effects,Uncovered));},ct);
    private Task<PrivacyDispositionPolicyV1?> Policy(Authority a,Guid id,CancellationToken ct)=>db.Database.SqlQuery<PrivacyDispositionPolicyV1>($"""
        SELECT policy_id AS "PolicyId",version AS "Version",target_tenant AS "TargetTenantId",basis_ref AS "BasisRef",enabled AS "Enabled",owner_id AS "OwnerId",at AS "At"
        FROM cp6_privacy_disposition_v1.policies WHERE environment_id={a.Environment} AND actor_tenant={a.Actor.TenantId} AND policy_id={id} ORDER BY sequence DESC LIMIT 1
        """).SingleOrDefaultAsync(ct);
    public Task<PrivacyDispositionPolicyV1?> PolicyAsync(Guid id,TenantManagementSessionV1 session,CancellationToken ct=default)
    {Id(id);return Transaction(false,session,(a,t)=>{Can(a,"read");return Policy(a,id,t);},ct);}
    private Task<PrivacyDispositionHoldV1?> Hold(Authority a,Guid tenant,Guid subject,CancellationToken ct)=>db.Database.SqlQuery<PrivacyDispositionHoldV1>($"""
        SELECT version AS "Version",active AS "Active",basis_ref AS "BasisRef",review_at AS "ReviewAt",owner_id AS "OwnerId",actor_tenant AS "OwnerTenantId",at AS "At"
        FROM cp6_privacy_disposition_v1.holds WHERE environment_id={a.Environment} AND target_tenant={tenant} AND subject_id={subject} ORDER BY sequence DESC LIMIT 1
        """).SingleOrDefaultAsync(ct);
    public Task<PrivacyDispositionHoldV1?> HoldAsync(Guid tenant,Guid subject,TenantManagementSessionV1 session,CancellationToken ct=default)
    {Id(tenant);Id(subject);return Transaction(false,session,(a,t)=>{Can(a,"read");return Hold(a,tenant,subject,t);},ct);}
    private async Task<PrivacyDispositionPreviewV1> Preview(Authority a,PrivacyDispositionTargetV1 target,CancellationToken ct)
    {
        var policy=await Policy(a,target.PolicyId,ct)??throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_POLICY_NOT_FOUND",404);
        if(policy.TargetTenantId!=target.TargetTenantId)throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_POLICY_TARGET_CHANGED",412);
        var user=await db.Sys_Users.IgnoreQueryFilters().AsNoTracking().Where(x=>x.Id==target.SubjectId&&x.TenantId==target.TargetTenantId)
            .Select(x=>new{x.Id,x.IsPlatformAdmin}).SingleOrDefaultAsync(ct)??throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_TARGET_NOT_FOUND",404);
        var revision=await db.Database.SqlQuery<Guid?>($"SELECT revision AS \"Value\" FROM cp6_authentication_v1.identity_admission_revisions WHERE tenant_id={target.TargetTenantId} AND object_key={"user:"+target.SubjectId.ToString("D")}").SingleOrDefaultAsync(ct)
            ??throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_TARGET_VERSION_UNAVAILABLE",503);
        var hold=await Hold(a,target.TargetTenantId,target.SubjectId,ct);var at=await Clock(ct);var blockers=new List<string>();
        if(!policy.Enabled)blockers.Add("POLICY_DISABLED");
        if(user.IsPlatformAdmin)blockers.Add("PLATFORM_AUTHORITY_MUST_BE_REVOKED_SEPARATELY");
        if(target.TargetTenantId==TenantContext.DefaultTenant)blockers.Add("PLATFORM_TENANT_PROTECTED");
        if(hold?.Active==true)blockers.Add("ACTIVE_HOLD");
        if(await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_privacy_disposition_v1.executions WHERE environment_id={a.Environment} AND target_tenant={target.TargetTenantId} AND subject_id={target.SubjectId}) AS \"Value\"").SingleAsync(ct))blockers.Add("SUBJECT_PROFILE_ALREADY_ANONYMIZED");
        var scope=new PrivacyDispositionScopeV1(a.Environment,target.TargetTenantId,target.SubjectId,revision,policy.PolicyId,policy.Version,policy.BasisRef,hold?.Version,Action,Fields,Effects,Uncovered);
        return new(scope,MainOwnerOperationJournalV1.Digest(MainOwnerOperationJournalV1.Encode(scope)),hold,hold?.Active==true&&hold.ReviewAt<=at,blockers.ToArray());
    }
    public Task<PrivacyDispositionPreviewV1> PreviewAsync(PrivacyDispositionTargetV1 target,TenantManagementSessionV1 session,CancellationToken ct=default)
    {Target(target);return Transaction(false,session,(a,t)=>{Can(a,"read");return Preview(a,target,t);},ct);}
    private sealed record PlanRow(Guid Id,Guid RequestRef,Guid Requester,DateTimeOffset At,string Scope,string Digest);
    private sealed record PlanEvent(Guid Version,string State,Guid Actor,Guid? DecisionRef);
    private Task<PlanRow?> Plan(Authority a,Guid id,CancellationToken ct)=>db.Database.SqlQuery<PlanRow>($"""
        SELECT id AS "Id",request_ref AS "RequestRef",requester_id AS "Requester",at AS "At",scope_json AS "Scope",scope_digest AS "Digest"
        FROM cp6_privacy_disposition_v1.plans WHERE environment_id={a.Environment} AND actor_tenant={a.Actor.TenantId} AND id={id}
        """).SingleOrDefaultAsync(ct);
    private Task<PlanEvent> Event(Guid id,CancellationToken ct)=>db.Database.SqlQuery<PlanEvent>($"""
        SELECT version AS "Version",state AS "State",actor_id AS "Actor",decision_ref AS "DecisionRef"
        FROM cp6_privacy_disposition_v1.plan_events WHERE plan_id={id} ORDER BY sequence DESC LIMIT 1
        """).SingleAsync(ct);
    private async Task<PrivacyDispositionPlanV1> View(Authority a,PlanRow row,CancellationToken ct)
    {
        var scope=JsonSerializer.Deserialize<PrivacyDispositionScopeV1>(row.Scope)!;var last=await Event(row.Id,ct);
        PrivacyDispositionHoldV1? hold;bool overdue;var blockers=new List<string>();
        try
        {
            var current=await Preview(a,new(scope.TargetTenantId,scope.SubjectId,scope.PolicyId),ct);hold=current.Hold;overdue=current.HoldReviewOverdue;blockers.AddRange(current.Blockers);
            if(current.Scope.TargetVersion!=scope.TargetVersion)blockers.Add("TARGET_VERSION_CHANGED");
            if(current.Scope.PolicyVersion!=scope.PolicyVersion)blockers.Add("POLICY_VERSION_CHANGED");
            if(current.Scope.HoldVersion!=scope.HoldVersion)blockers.Add("HOLD_VERSION_CHANGED");
        }
        catch(PrivacyDispositionExceptionV1 e)when(e.Status is 404 or 412)
        {hold=await Hold(a,scope.TargetTenantId,scope.SubjectId,ct);overdue=hold?.Active==true&&hold.ReviewAt<=await Clock(ct);blockers.Add(e.Message);}
        if(last.State=="APPROVED"&&(!await db.Sys_Users.AsNoTracking().AnyAsync(x=>x.Id==last.Actor&&x.Enable&&x.IsPlatformAdmin,ct)
            ||!(await new PermissionAggregator(db).BuildAsync(last.Actor,ct)).ActionKeys.Contains(Menu+":approve")))blockers.Add("APPROVER_AUTHORITY_CHANGED");
        return new(row.Id,row.RequestRef,row.Requester,row.At,last.Version,last.State,last.State=="APPROVED"?last.Actor:null,last.DecisionRef,
            scope,row.Digest,hold,overdue,blockers.ToArray(),last.State=="APPROVED"&&blockers.Count==0,await Execution(a,row.Id,ct));
    }
    public Task<PrivacyDispositionPlanV1> PlanAsync(Guid id,TenantManagementSessionV1 session,CancellationToken ct=default)
    {Id(id);return Transaction(false,session,async(a,t)=>{Can(a,"read");return await View(a,await Plan(a,id,t)??throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_PLAN_NOT_FOUND",404),t);},ct);}
    private async Task<PrivacyDispositionOperationV1?> Original(Authority a,Guid operation,byte[]? input,CancellationToken ct)
    {
        var old=await new MainOwnerOperationJournalV1(db).Read(Journal(a),operation,ct);if(old==null)return null;
        if(input!=null&&old.State!="CLOSED"&&!MainOwnerOperationJournalV1.Matches(old,input))throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_OPERATION_CONFLICT");
        return JsonSerializer.Deserialize<PrivacyDispositionOperationV1>(old.Result)!;
    }
    private Task Store(Authority a,Guid operation,byte[]? input,PrivacyDispositionOperationV1 result,CancellationToken ct)
        =>new MainOwnerOperationJournalV1(db).Store(Journal(a),"Main.Privacy.Disposition.commands.v1",operation,a.Actor.Id,input,result,
            result.PlanId is {} id?[id]:[],new{result.Kind,result.PlanId,result.Version},ct);
    private Task<PrivacyDispositionOperationV1> Change(Guid operation,string kind,object input,string capability,TenantManagementSessionV1 session,
        Func<Authority,CancellationToken,Task<(Guid? Plan,Guid Version)>> effect,CancellationToken ct)
    {
        Id(operation);var bytes=MainOwnerOperationJournalV1.Encode(new{kind,input});return Transaction(true,session,async(a,t)=>
        {
            Can(a,"read");var old=await Original(a,operation,bytes,t);if(old!=null)return old;Can(a,capability);
            var effectResult=await effect(a,t);
            var target=input switch{PrivacyDispositionPolicyCommandV1 p=>(Policy:(Guid?)p.PolicyId,Tenant:(Guid?)p.TargetTenantId,Subject:(Guid?)null),
                PrivacyDispositionHoldCommandV1 h=>(Policy:(Guid?)null,Tenant:(Guid?)h.TargetTenantId,Subject:(Guid?)h.SubjectId),
                PrivacyDispositionPlanCommandV1 p=>(Policy:(Guid?)p.Target.PolicyId,Tenant:(Guid?)p.Target.TargetTenantId,Subject:(Guid?)p.Target.SubjectId),
                _=>(Policy:(Guid?)null,Tenant:(Guid?)null,Subject:(Guid?)null)};
            var result=new PrivacyDispositionOperationV1(operation,"COMMITTED",kind,effectResult.Plan,effectResult.Version,target.Policy,target.Tenant,target.Subject);
            await Store(a,operation,bytes,result,t);return result;
        },ct);
    }
    public Task<PrivacyDispositionOperationV1> OperationAsync(Guid operation,bool close,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(operation);return Transaction(close,session,async(a,t)=>
        {
            Can(a,"read");var old=await Original(a,operation,null,t);if(old!=null)return old;
            var result=new PrivacyDispositionOperationV1(operation,close?"CLOSED":"NOT_FOUND","CLOSE",null,null);
            if(close)await Store(a,operation,null,result,t);return result;
        },ct);
    }
}
