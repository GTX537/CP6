using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

public sealed class OperationLogOwnerV1(CP6Context db, IamAdministrationV1? administration,
    TenantManagementSessionV1? session, CancellationToken requestCancellation = default, Guid? actualActor = null)
{
    private readonly AuditRetentionV1 retention = new(db);
    internal bool IsImpersonated => actualActor.HasValue;

    public async Task<AuditDisposalV1> ClearAsync(Guid? planId, CancellationToken ct = default)
    {
        var preview = await Retention("operlog:delete", (actor, token) => retention.PreviewAsync(db.CurrentTenantId,
            AuditRetentionV1.Telemetry, actor, ValidId(planId ?? Guid.NewGuid()), planId.HasValue ? "EXPLICIT" : "LEGACY", token), ct);
        return await Retention("operlog:delete", (actor, token) => retention.ExecuteAsync(db.CurrentTenantId, preview.PlanId, actor, token), ct);
    }

    public Task<AuditRetentionPolicyV1> PublishPolicyAsync(string key, AuditRetentionPolicyChangeV1 policy, CancellationToken ct = default)
        => Retention(AuditRetentionV1.ManagePermission, (actor, token) => retention.PublishAsync(db.CurrentTenantId,
            actor.Effective!.Value, key, policy, token), ct);

    public Task<AuditRetentionPolicyV1?> PolicyAsync(string key, CancellationToken ct = default)
        => Retention(AuditRetentionV1.ManagePermission, (_, token) => retention.PolicyAsync(db.CurrentTenantId, key, token), ct);

    public Task<Guid> SafeguardAsync(string key, AuditSafeguardChangeV1 change, CancellationToken ct = default)
        => Retention(AuditRetentionV1.ManagePermission, (actor, token) => retention.SafeguardAsync(db.CurrentTenantId,
            actor.Effective!.Value, key, change, token), ct);

    public Task<AuditDisposalV1> PreviewAsync(string key, Guid id, CancellationToken ct = default)
        => Retention(AuditRetentionV1.ManagePermission, (actor, token) => retention.PreviewAsync(db.CurrentTenantId,
            key, actor, ValidId(id), "EXPLICIT", token), ct);

    public Task<AuditDisposalV1> ExecutePlanAsync(Guid id, CancellationToken ct = default)
        => Retention(AuditRetentionV1.ManagePermission, (actor, token) => retention.ExecuteAsync(db.CurrentTenantId,
            ValidId(id), actor, token), ct);

    public Task<AuditDisposalV1> ReadPlanAsync(Guid id, CancellationToken ct = default)
        => Retention(AuditRetentionV1.ManagePermission, (actor, token) => retention.ReadAsync(db.CurrentTenantId,
            ValidId(id), actor, token), ct);

    public Task<AuditDisposalV1> ReadLegacyAsync(CancellationToken ct = default)
        => Retention("operlog:delete", (actor, token) => retention.ReadLegacyAsync(db.CurrentTenantId, actor, token), ct);

    private Task<T> Retention<T>(string permission, Func<AuditRetentionV1.Actor, CancellationToken, Task<T>> run, CancellationToken ct)
        => Execute(permission, async (current, token) =>
        {
            if (actualActor.HasValue)
                throw new OrderPriceCorrectionException("AUDIT_RETENTION_IMPERSONATION_FORBIDDEN", 403);
            await retention.LockAsync(token);
            return await run(new(current.UserId, current.UserId, null), token);
        }, ct);

    private static Guid ValidId(Guid id) => id != Guid.Empty ? id : throw new OrderPriceCorrectionException("AUDIT_RETENTION_PLAN_ID_INVALID", 400);

    internal async Task<T> Execute<T>(string permission, Func<UserPermissionContext, CancellationToken, Task<T>> body,
        CancellationToken ct)
    {
        if (administration?.Owns(db) != true || session == null)
            throw new OrderPriceCorrectionException("OPERLOG_AUTHORITY_UNAVAILABLE", 503);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, requestCancellation);
        try
        {
            return await administration.ExecuteCurrentAsync(permission, session.CurrentActor,
                async (actor, token) =>
                {
                    var result = await body(actor, token);
                    if (await session.CurrentActor(token) != actor.UserId)
                        throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
                    return result;
                }, linked.Token);
        }
        catch (IamAdministrationExceptionV1 error)
        {
            throw new OrderPriceCorrectionException(error.Message == "IAM_OPERATION_UNKNOWN"
                ? "OPERLOG_OUTCOME_UNKNOWN" : error.Message, error.Status, error);
        }
        catch (Exception error) when (error is DbException or DbUpdateException)
        { throw new OrderPriceCorrectionException("OPERLOG_AUTHORITY_UNAVAILABLE", 503, error); }
    }
}
