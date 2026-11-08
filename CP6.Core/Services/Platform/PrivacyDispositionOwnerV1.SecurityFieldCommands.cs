using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Platform;
public sealed partial class PrivacyDispositionOwnerV1
{
    public Task<SecurityFieldOperationV1> ConfigureSecurityPolicyAsync(SecurityFieldPolicyCommandV1 input,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(input.PolicyId);Id(input.TargetTenantId);Id(input.BasisRef);
        if(input.ExpectedVersion==Guid.Empty||input.NotBeforeUtc.Ticks%10!=0)throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_POLICY_INPUT_INVALID",400);
        var command=input with{NotBeforeUtc=input.NotBeforeUtc.ToUniversalTime()};
        return SecurityChange(input.OperationId,"POLICY",command,"security-policy",session,async(a,t)=>
        {
            var previous=await SecurityPolicy(a,command.PolicyId,t);if(previous?.Version!=command.ExpectedVersion)throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_POLICY_CHANGED",412);
            if(!await db.Sys_Tenants.IgnoreQueryFilters().AnyAsync(x=>x.Id==command.TargetTenantId,t))throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_TENANT_NOT_FOUND",404);
            var version=Guid.NewGuid();await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_privacy_security_fields_v1.policies(environment_id,actor_tenant,policy_id,version,target_tenant,basis_ref,enabled,not_before,owner_id)
                VALUES({a.Environment},{a.Actor.TenantId},{command.PolicyId},{version},{command.TargetTenantId},{command.BasisRef},{command.Enabled},{command.NotBeforeUtc},{a.Actor.Id})
                """,t);return new(input.OperationId,"COMMITTED","POLICY",Version:version,PolicyId:command.PolicyId);
        },ct);
    }
    public Task<SecurityFieldOperationV1> RequestSecurityDispositionAsync(SecurityFieldRequestV1 input,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(input.RequestRef);if(input.Target==null||string.IsNullOrEmpty(input.ExpectedScopeDigest))throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_PREVIEW_REQUIRED",400);Id(input.Target.LogId);Id(input.Target.PolicyId);
        return SecurityChange(input.OperationId,"PLAN",input,"security-prepare",session,async(a,t)=>
        {
            var preview=await SecurityPreview(a,input.Target,t);if(preview.ScopeDigest!=input.ExpectedScopeDigest)throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_PREVIEW_CHANGED",412);
            if(preview.Blockers.Length!=0)throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_PLAN_BLOCKED");
            var id=Guid.NewGuid();var version=Guid.NewGuid();var json=JsonSerializer.Serialize(preview.Scope);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_privacy_security_fields_v1.plans(id,environment_id,actor_tenant,request_ref,requester_id,scope_json,scope_digest)
                VALUES({id},{a.Environment},{a.Actor.TenantId},{input.RequestRef},{a.Actor.Id},{json},{preview.ScopeDigest});
                INSERT INTO cp6_privacy_security_fields_v1.events(plan_id,version,previous_version,state,actor_id,operation_id) VALUES({id},{version},NULL,'REQUESTED',{a.Actor.Id},{input.OperationId})
                """,t);return new(input.OperationId,"COMMITTED","PLAN",id,version,input.Target.PolicyId,input.Target.LogId);
        },ct);
    }
    public Task<SecurityFieldOperationV1> DecideSecurityDispositionAsync(Guid id,SecurityFieldDecisionV1 input,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(id);Id(input.ExpectedVersion);Id(input.DecisionRef);if(input.Decision is not("APPROVE" or "REJECT" or "REVOKE"))throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_DECISION_INVALID",400);
        return SecurityChange(input.OperationId,input.Decision,new{id,input},input.Decision=="REVOKE"?"security-revoke":"security-approve",session,async(a,t)=>
        {
            var row=await SecurityPlan(a,id,t)??throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_PLAN_NOT_FOUND",404);var current=await SecurityView(a,row,t);
            if(current.Execution!=null)throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_ALREADY_APPLIED");
            if(current.Version!=input.ExpectedVersion)throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_PLAN_CHANGED",412);
            if(input.Decision!="REVOKE"&&row.Requester==a.Actor.Id)throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_INDEPENDENT_APPROVER_REQUIRED",403);
            if(current.State!="REQUESTED"&&!(input.Decision=="REVOKE"&&current.State=="APPROVED"))throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_STATE_CONFLICT");
            if(input.Decision=="APPROVE"&&current.CurrentBlockers.Length!=0)throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_PLAN_BLOCKED");
            var version=Guid.NewGuid();var state=input.Decision=="APPROVE"?"APPROVED":input.Decision=="REJECT"?"REJECTED":"REVOKED";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_privacy_security_fields_v1.events(plan_id,version,previous_version,state,actor_id,decision_ref,operation_id)
                VALUES({id},{version},{current.Version},{state},{a.Actor.Id},{input.DecisionRef},{input.OperationId})
                """,t);return new(input.OperationId,"COMMITTED",input.Decision,id,version,current.Scope.PolicyId,current.Scope.LogId);
        },ct);
    }
    public Task<SecurityFieldOperationV1> ExecuteSecurityDispositionAsync(Guid id,SecurityFieldExecuteV1 input,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(id);Id(input.ExpectedVersion);Id(input.ConfirmationRef);if(string.IsNullOrEmpty(input.ExpectedScopeDigest))throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_PREVIEW_REQUIRED",400);
        return SecurityChange(input.OperationId,"EXECUTE",new{id,input},"security-execute",session,async(a,t)=>
        {
            var row=await SecurityPlan(a,id,t)??throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_PLAN_NOT_FOUND",404);var current=await SecurityView(a,row,t);
            if(current.Execution!=null)throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_ALREADY_APPLIED");
            if(current.Version!=input.ExpectedVersion||current.ScopeDigest!=input.ExpectedScopeDigest)throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_APPROVAL_CHANGED",412);
            if(!current.ApprovalCurrent)throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_EXECUTION_BLOCKED");
            var scope=current.Scope;var count=current.CurrentClassifiedValueCount!.Value;
            var after=Guid.NewGuid();var receipt=new SecurityFieldExecutionV1(id,input.OperationId,input.ConfirmationRef,SecurityOwner,SecurityAction,"APPLIED","PARTIAL",a.Environment,a.Actor.TenantId,a.Actor.Id,row.Requester,current.ApproverId!.Value,
                scope.LogId,scope.TargetTenantId,scope.SubjectId,scope.PolicyId,scope.PolicyVersion,scope.TargetVersion,after,row.Digest,await Clock(t),count,SecurityFields,SecurityUncovered);
            var json=JsonSerializer.Serialize(receipt);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_privacy_security_fields_v1.executions(plan_id,approval_version,log_id,environment_id,actor_tenant,operator_id,operation_id,scope_json,receipt_json)
                VALUES({id},{current.Version},{scope.LogId},{a.Environment},{a.Actor.TenantId},{a.Actor.Id},{input.OperationId},{row.Scope},{json})
                """,t);
            await PrivacySecurityFieldEffectV1.Apply(db,new(scope.LogId,id,a.Actor.TenantId,scope.TargetTenantId,scope.SubjectId,scope.PolicyId,row.Scope,json),a.Environment,after,t);
            return new(input.OperationId,"COMMITTED","EXECUTE",id,current.Version,scope.PolicyId,scope.LogId,receipt);
        },ct);
    }
    public Task<SecurityFieldOperationV1> SetSecurityHoldAsync(Guid log,SecurityFieldHoldV1 input,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(log);ValidateHold(input.ExpectedVersion,input.Active,input.BasisRef,input.ReviewAt);var command=input with{ReviewAt=input.ReviewAt?.ToUniversalTime()};
        return SecurityChange(input.OperationId,"HOLD",new{log,input=command},"hold",session,async(a,t)=>
        {
            var row=await SecurityLog(log,false,t)??throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_LOG_NOT_FOUND",404);if(row.Subject==null)throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_UNATTRIBUTABLE_EVENT");
            var version=await AppendHold(a,row.Tenant,row.Subject.Value,command.ExpectedVersion,command.Active,command.BasisRef,command.ReviewAt,t);return new(input.OperationId,"COMMITTED","HOLD",Version:version,LogId:log);
        },ct);
    }
}
