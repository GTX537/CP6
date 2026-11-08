using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Platform;
public sealed partial class PrivacyDispositionOwnerV1
{
    public Task<TenantPayloadDispositionOperationV1> ConfigureTenantPayloadPolicyAsync(TenantPayloadDispositionPolicyCommandV1 input,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(input.PolicyId);Id(input.TargetTenantId);Id(input.BasisRef);
        if(input.ExpectedVersion==Guid.Empty||input.MinimumRetentionDays is <0 or >36500)throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_POLICY_INPUT_INVALID",400);
        return TenantPayloadChange(input.OperationId,"POLICY",input,"tenant-payload-policy",session,async(a,t)=>
        {
            var previous=await TenantPayloadPolicy(a,input.PolicyId,t);if(previous?.Version!=input.ExpectedVersion)throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_POLICY_CHANGED",412);
            if(!await db.Sys_Tenants.IgnoreQueryFilters().AnyAsync(x=>x.Id==input.TargetTenantId,t))throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_TENANT_NOT_FOUND",404);
            var version=Guid.NewGuid();await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_privacy_tenant_payload_disposition_v1.policies(environment_id,actor_tenant,policy_id,version,target_tenant,basis_ref,enabled,minimum_days,require_access_closed,owner_id)
                VALUES({a.Environment},{a.Actor.TenantId},{input.PolicyId},{version},{input.TargetTenantId},{input.BasisRef},{input.Enabled},{input.MinimumRetentionDays},{input.RequireAccessClosed},{a.Actor.Id})
                """,t);return new(input.OperationId,"COMMITTED","POLICY",Version:version,PolicyId:input.PolicyId);
        },ct);
    }
    public Task<TenantPayloadDispositionOperationV1> RequestTenantPayloadDispositionAsync(TenantPayloadDispositionRequestV1 input,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(input.RequestRef);if(input.Target==null||string.IsNullOrEmpty(input.ExpectedScopeDigest))throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_PREVIEW_REQUIRED",400);
        Id(input.Target.ArtifactId);Id(input.Target.PolicyId);
        return TenantPayloadChange(input.OperationId,"PLAN",input,"tenant-payload-prepare",session,async(a,t)=>
        {
            var preview=await TenantPayloadPreview(a,input.Target,t);
            if(preview.ScopeDigest!=input.ExpectedScopeDigest)throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_PREVIEW_CHANGED",412);
            if(preview.Blockers.Length!=0)throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_PLAN_BLOCKED");
            var id=Guid.NewGuid();var version=Guid.NewGuid();var scope=JsonSerializer.Serialize(preview.Scope);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_privacy_tenant_payload_disposition_v1.plans(id,environment_id,actor_tenant,request_ref,requester_id,scope_json,scope_digest)
                VALUES({id},{a.Environment},{a.Actor.TenantId},{input.RequestRef},{a.Actor.Id},{scope},{preview.ScopeDigest});
                INSERT INTO cp6_privacy_tenant_payload_disposition_v1.events(plan_id,version,previous_version,state,actor_id,operation_id)
                VALUES({id},{version},NULL,'REQUESTED',{a.Actor.Id},{input.OperationId})
                """,t);return new(input.OperationId,"COMMITTED","PLAN",id,version,input.Target.PolicyId,input.Target.ArtifactId);
        },ct);
    }
    public Task<TenantPayloadDispositionOperationV1> DecideTenantPayloadDispositionAsync(Guid id,TenantPayloadDispositionDecisionV1 input,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(id);Id(input.ExpectedVersion);Id(input.DecisionRef);
        if(input.Decision is not("APPROVE" or "REJECT" or "REVOKE"))throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_DECISION_INVALID",400);
        return TenantPayloadChange(input.OperationId,input.Decision,new{id,input},input.Decision=="REVOKE"?"tenant-payload-revoke":"tenant-payload-approve",session,async(a,t)=>
        {
            var row=await TenantPayloadPlan(a,id,t)??throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_PLAN_NOT_FOUND",404);var previous=await TenantPayloadEvent(id,t);
            if(await TenantPayloadExecution(a,id,t)!=null)throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_ALREADY_APPLIED");
            if(previous.Version!=input.ExpectedVersion)throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_PLAN_CHANGED",412);
            if(input.Decision!="REVOKE"&&row.Requester==a.Actor.Id)throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_INDEPENDENT_APPROVER_REQUIRED",403);
            if(previous.State!="REQUESTED"&&!(input.Decision=="REVOKE"&&previous.State=="APPROVED"))throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_STATE_CONFLICT");
            var scope=JsonSerializer.Deserialize<TenantPayloadDispositionScopeV1>(row.Scope)!;
            if(input.Decision=="APPROVE")
            {
                var current=await TenantPayloadPreview(a,new(scope.ArtifactId,scope.PolicyId),t);
                if(current.ScopeDigest!=row.Digest)throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_SCOPE_CHANGED",412);
                if(current.Blockers.Length!=0)throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_PLAN_BLOCKED");
            }
            var version=Guid.NewGuid();var state=input.Decision=="APPROVE"?"APPROVED":input.Decision=="REJECT"?"REJECTED":"REVOKED";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_privacy_tenant_payload_disposition_v1.events(plan_id,version,previous_version,state,actor_id,decision_ref,operation_id)
                VALUES({id},{version},{previous.Version},{state},{a.Actor.Id},{input.DecisionRef},{input.OperationId})
                """,t);return new(input.OperationId,"COMMITTED",input.Decision,id,version,scope.PolicyId,scope.ArtifactId);
        },ct);
    }
    public Task<TenantPayloadDispositionOperationV1> ExecuteTenantPayloadDispositionAsync(Guid id,TenantPayloadDispositionExecuteV1 input,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(id);Id(input.ExpectedVersion);Id(input.ConfirmationRef);
        if(string.IsNullOrEmpty(input.ExpectedScopeDigest))throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_PREVIEW_REQUIRED",400);
        return TenantPayloadChange(input.OperationId,"EXECUTE",new{id,input},"tenant-payload-execute",session,async(a,t)=>
        {
            var row=await TenantPayloadPlan(a,id,t)??throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_PLAN_NOT_FOUND",404);
            var current=await TenantPayloadView(a,row,t);
            if(current.Execution!=null)throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_ALREADY_APPLIED");
            if(current.Version!=input.ExpectedVersion||current.ScopeDigest!=input.ExpectedScopeDigest)throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_APPROVAL_CHANGED",412);
            if(!current.ApprovalCurrent)throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_EXECUTION_BLOCKED");
            var scope=current.Scope;
            var receipt=new TenantPayloadDispositionExecutionV1(id,input.OperationId,input.ConfirmationRef,TenantPayloadOwner,"APPLIED","PARTIAL",a.Environment,a.Actor.TenantId,a.Actor.Id,row.Requester,current.ApproverId!.Value,
                scope.ArtifactId,scope.TargetTenantId,scope.PolicyId,scope.PolicyVersion,scope.PayloadVersion!.Value,scope.PayloadLength!.Value,row.Digest,await Clock(t),TenantPayloadUncovered);
            var json=JsonSerializer.Serialize(receipt);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_privacy_tenant_payload_disposition_v1.executions(plan_id,approval_version,artifact_id,environment_id,actor_tenant,operator_id,operation_id,scope_json,receipt_json)
                VALUES({id},{current.Version},{scope.ArtifactId},{a.Environment},{a.Actor.TenantId},{a.Actor.Id},{input.OperationId},{row.Scope},{json})
                """,t);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_privacy_tenant_payload_disposition_v1.tombstones(artifact_id,source_plan_id,environment_id,operator_tenant,target_tenant,policy_id,scope_json,receipt_json)
                VALUES({scope.ArtifactId},{id},{a.Environment},{a.Actor.TenantId},{scope.TargetTenantId},{scope.PolicyId},{row.Scope},{json});
                UPDATE cp6_privacy_tenant_export_v1.artifacts SET revoked_at=clock_timestamp() WHERE id={scope.ArtifactId} AND revoked_at IS NULL;
                DELETE FROM cp6_privacy_tenant_export_v1.payloads WHERE artifact_id={scope.ArtifactId};
                """,t);
            return new(input.OperationId,"COMMITTED","EXECUTE",id,current.Version,scope.PolicyId,scope.ArtifactId,receipt);
        },ct);
    }
}
