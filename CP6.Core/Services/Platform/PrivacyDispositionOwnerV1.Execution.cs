using System.Text.Json;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Platform;

public sealed partial class PrivacyDispositionOwnerV1
{
    private async Task<PrivacyDispositionExecutionV1?> Execution(Authority a, Guid plan, CancellationToken ct)
    {
        var json = await db.Database.SqlQuery<string>($"""
            SELECT receipt_json AS "Value" FROM cp6_privacy_disposition_v1.executions
            WHERE environment_id={a.Environment} AND actor_tenant={a.Actor.TenantId} AND plan_id={plan}
            """).SingleOrDefaultAsync(ct);
        return json is null ? null : JsonSerializer.Deserialize<PrivacyDispositionExecutionV1>(json);
    }

    public Task<PrivacyDispositionExecutionV1?> ExecutionAsync(Guid plan, TenantManagementSessionV1 session, CancellationToken ct = default)
    {
        Id(plan);
        return Transaction(false, session, (a, token) =>
        {
            Can(a, "read");
            return Execution(a, plan, token);
        }, ct);
    }

    public Task<PrivacyDispositionOperationV1> ExecuteAsync(Guid id, PrivacyDispositionExecuteV1 input,
        TenantManagementSessionV1 session, CancellationToken ct = default)
    {
        Id(id);
        Id(input.OperationId);
        Id(input.ExpectedVersion);
        Id(input.ConfirmationRef);
        if (string.IsNullOrEmpty(input.ExpectedScopeDigest))
            throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_PREVIEW_REQUIRED", 400);
        var bytes = MainOwnerOperationJournalV1.Encode(new { kind = "EXECUTE", id, input });
        return ExecuteDispositionAsync(id, input, bytes, session, ct);
    }

    private async Task<PrivacyDispositionPlanV1> RequireExecutionApprovalAsync(Authority a, Guid id, PrivacyDispositionExecuteV1 input,
        CancellationToken ct)
    {
        var row = await Plan(a, id, ct) ?? throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_PLAN_NOT_FOUND", 404);
        if (await Execution(a, id, ct) is not null)
            throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_ALREADY_APPLIED");
        var current = await View(a, row, ct);
        if (current.Version != input.ExpectedVersion || current.ScopeDigest != input.ExpectedScopeDigest)
            throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_APPROVAL_CHANGED", 412);
        if (!current.ApprovalCurrent)
            throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_EXECUTION_BLOCKED");
        return current;
    }

    private async Task<PrivacyDispositionOperationV1> StoreExecutionAsync(Authority a, PrivacyDispositionPlanV1 plan,
        PrivacyDispositionExecuteV1 input, byte[] bytes, Guid version, CancellationToken ct)
    {
        var scope = plan.Scope;
        var receipt = new PrivacyDispositionExecutionV1(plan.PlanId, input.OperationId, a.Environment, a.Actor.TenantId, a.Actor.Id,
            plan.RequesterId, plan.ApproverId!.Value, input.ConfirmationRef, scope.TargetTenantId, scope.SubjectId,
            "Main.Identity.Profile.v1", scope.Action, scope.PolicyVersion, scope.TargetVersion, version, await Clock(ct),
            "APPLIED", "PARTIAL", scope.Fields, scope.UncoveredOwners);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_privacy_disposition_v1.executions(plan_id,environment_id,actor_tenant,target_tenant,subject_id,receipt_json)
            VALUES({plan.PlanId},{a.Environment},{a.Actor.TenantId},{scope.TargetTenantId},{scope.SubjectId},{JsonSerializer.Serialize(receipt)})
            """, ct);
        var result = new PrivacyDispositionOperationV1(input.OperationId, "COMMITTED", "EXECUTE", plan.PlanId, plan.Version,
            scope.PolicyId, scope.TargetTenantId, scope.SubjectId, receipt);
        await Store(a, input.OperationId, bytes, result, ct);
        return result;
    }
}
