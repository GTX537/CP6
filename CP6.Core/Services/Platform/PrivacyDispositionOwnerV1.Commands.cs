using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Platform;
public sealed partial class PrivacyDispositionOwnerV1
{
    public Task<PrivacyDispositionOperationV1> ConfigurePolicyAsync(PrivacyDispositionPolicyCommandV1 input,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(input.PolicyId);Id(input.TargetTenantId);Id(input.BasisRef);if(input.ExpectedVersion==Guid.Empty)throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_VERSION_REQUIRED",400);
        return Change(input.OperationId,"POLICY",input,"policy",session,async(a,t)=>
        {
            var old=await Policy(a,input.PolicyId,t);if(old?.Version!=input.ExpectedVersion)throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_POLICY_CHANGED",412);
            if(!await db.Sys_Tenants.IgnoreQueryFilters().AnyAsync(x=>x.Id==input.TargetTenantId,t))throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_TENANT_NOT_FOUND",404);
            var version=Guid.NewGuid();await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_privacy_disposition_v1.policies(environment_id,actor_tenant,policy_id,version,target_tenant,basis_ref,enabled,owner_id)
                VALUES({a.Environment},{a.Actor.TenantId},{input.PolicyId},{version},{input.TargetTenantId},{input.BasisRef},{input.Enabled},{a.Actor.Id})
                """,t);return(null,version);
        },ct);
    }
    public Task<PrivacyDispositionOperationV1> SetHoldAsync(PrivacyDispositionHoldCommandV1 input,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(input.TargetTenantId);Id(input.SubjectId);ValidateHold(input.ExpectedVersion,input.Active,input.BasisRef,input.ReviewAt);
        var command=input with{ReviewAt=input.ReviewAt?.ToUniversalTime()};
        return Change(command.OperationId,"HOLD",command,"hold",session,async(a,t)=>
        {
            if(!await db.Sys_Users.IgnoreQueryFilters().AnyAsync(x=>x.Id==command.SubjectId&&x.TenantId==command.TargetTenantId,t))throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_TARGET_NOT_FOUND",404);
            return(null,await AppendHold(a,command.TargetTenantId,command.SubjectId,command.ExpectedVersion,command.Active,command.BasisRef,command.ReviewAt,t));
        },ct);
    }
    private static void ValidateHold(Guid? expected,bool active,Guid basis,DateTimeOffset? review)
    {
        Id(basis);if(expected==Guid.Empty||active&&review==null||review?.Ticks%10!=0&&review!=null)
            throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_HOLD_INPUT_INVALID",400);
    }
    private async Task<Guid> AppendHold(Authority a,Guid tenant,Guid subject,Guid? expected,bool active,Guid basis,DateTimeOffset? review,CancellationToken ct)
    {
        var previous=await Hold(a,tenant,subject,ct);if(previous?.Version!=expected)throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_HOLD_CHANGED",412);
        if(active&&review<=await Clock(ct))throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_FUTURE_REVIEW_REQUIRED",422);
        var version=Guid.NewGuid();await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_privacy_disposition_v1.holds(environment_id,actor_tenant,target_tenant,subject_id,version,previous_version,active,basis_ref,review_at,owner_id)
            VALUES({a.Environment},{a.Actor.TenantId},{tenant},{subject},{version},{expected},{active},{basis},{review},{a.Actor.Id})
            """,ct);return version;
    }
    public Task<PrivacyDispositionOperationV1> RequestAsync(PrivacyDispositionPlanCommandV1 input,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(input.RequestRef);if(input.Target==null||string.IsNullOrEmpty(input.ExpectedScopeDigest))throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_PREVIEW_REQUIRED",400);Target(input.Target);
        return Change(input.OperationId,"PLAN",input,"prepare",session,async(a,t)=>
        {
            var preview=await Preview(a,input.Target,t);
            if(preview.ScopeDigest!=input.ExpectedScopeDigest)throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_PREVIEW_CHANGED",412);
            if(preview.Blockers.Length!=0)throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_PLAN_BLOCKED");
            var id=Guid.NewGuid();var version=Guid.NewGuid();var json=JsonSerializer.Serialize(preview.Scope);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_privacy_disposition_v1.plans(id,environment_id,actor_tenant,request_ref,requester_id,scope_json,scope_digest)
                VALUES({id},{a.Environment},{a.Actor.TenantId},{input.RequestRef},{a.Actor.Id},{json},{preview.ScopeDigest});
                INSERT INTO cp6_privacy_disposition_v1.plan_events(plan_id,version,previous_version,state,actor_id,operation_id)
                VALUES({id},{version},NULL,'REQUESTED',{a.Actor.Id},{input.OperationId});
                """,t);return(id,version);
        },ct);
    }
    public Task<PrivacyDispositionOperationV1> DecideAsync(Guid id,PrivacyDispositionDecisionV1 input,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(id);Id(input.ExpectedVersion);Id(input.DecisionRef);if(input.Decision is not("APPROVE" or "REJECT" or "REVOKE"))throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_DECISION_INVALID",400);
        return Change(input.OperationId,input.Decision,new{id,input},input.Decision=="REVOKE"?"revoke":"approve",session,async(a,t)=>
        {
            var row=await Plan(a,id,t)??throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_PLAN_NOT_FOUND",404);var previous=await Event(id,t);
            if(await Execution(a,id,t)!=null)throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_ALREADY_APPLIED");
            if(previous.Version!=input.ExpectedVersion)throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_PLAN_CHANGED",412);
            if(input.Decision!="REVOKE"&&row.Requester==a.Actor.Id)throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_INDEPENDENT_APPROVER_REQUIRED",403);
            if(previous.State!="REQUESTED"&&!(input.Decision=="REVOKE"&&previous.State=="APPROVED"))throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_STATE_CONFLICT");
            if(input.Decision=="APPROVE")
            {
                var scope=JsonSerializer.Deserialize<PrivacyDispositionScopeV1>(row.Scope)!;
                var current=await Preview(a,new(scope.TargetTenantId,scope.SubjectId,scope.PolicyId),t);
                if(current.ScopeDigest!=row.Digest)throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_SCOPE_CHANGED",412);
                if(current.Blockers.Length!=0)throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_PLAN_BLOCKED");
            }
            var state=input.Decision=="APPROVE"?"APPROVED":input.Decision=="REJECT"?"REJECTED":"REVOKED";var version=Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_privacy_disposition_v1.plan_events(plan_id,version,previous_version,state,actor_id,decision_ref,operation_id)
                VALUES({id},{version},{previous.Version},{state},{a.Actor.Id},{input.DecisionRef},{input.OperationId})
                """,t);return(id,version);
        },ct);
    }
}
