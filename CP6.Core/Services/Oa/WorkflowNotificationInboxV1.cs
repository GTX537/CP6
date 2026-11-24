using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed record WorkflowNotificationItemV1(Guid Id, int Type, string Title, string Body, Guid? InstanceId,
    Guid? TaskId, string? FlowKey, bool IsRead, DateTime? ReadAt, DateTime CreateDate);
public sealed record WorkflowNotificationPageV1(IReadOnlyList<WorkflowNotificationItemV1> Items, string AsOf,
    string Watermark, string? NextCursor);
public sealed class WorkflowNotificationInboxV1(CP6Context db, ScopeAuthorizationFenceV1 scope, IHttpContextAccessor http,
    IDataProtectionProvider protection)
{
    private sealed record Point(Guid Tenant, Guid Environment, Guid User, string Snapshot,
        DateTime? LastCreated = null, Guid? LastId = null, bool? UnreadOnly = null);

    private ITimeLimitedDataProtector Tokens => protection.CreateProtector("CP6.OA.Notification.Observed.V1").ToTimeLimitedDataProtector();
    public bool IsAvailable => db.Database.IsNpgsql() && scope.HasDeploymentEnvironment;

    public async Task<WorkflowNotificationPageV1> ListAsync(bool unreadOnly, int take, string? asOf, string? cursor,
        CancellationToken ct, int offset = 0)
    {
        if (take is < 1 or > 100 || offset < 0) throw Error("NOTIFICATION_PAGE_INVALID", 422);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var lease = await scope.AcquireAsync(ct);
        var user = await WorkflowNotificationAuthorityV1.ActualUserAsync(db, http, ct);
        var known = asOf is null ? null : ReadToken(asOf, user, false);
        var next = cursor is null ? null : ReadToken(cursor, user, true);
        if (next is not null && (next.UnreadOnly != unreadOnly || known is not null && known.Snapshot != next.Snapshot))
            throw Error("NOTIFICATION_CURSOR_INVALID", 422);
        var point = known ?? (next is not null ? next with { LastCreated = null, LastId = null, UnreadOnly = null }
            : await Observe(user, ct));
        var lastCreated = next?.LastCreated?.ToString("O", CultureInfo.InvariantCulture);
        var rows = await db.Database.SqlQuery<WorkflowNotificationItemV1>($"""
            SELECT n."Id",n."Type",CASE WHEN s.kind='DEADLINE_RECALCULATED'
              THEN '待办期限已重算' ELSE '' END AS "Title",''::text AS "Body",
              CASE WHEN s.notification_id IS NOT NULL THEN n."InstanceId" END AS "InstanceId",
              CASE WHEN s.notification_id IS NOT NULL THEN n."TaskId" END AS "TaskId",
              NULL::text AS "FlowKey",n."IsRead",n."ReadAt",n."CreateDate"
            FROM public."Wf_Notification" n
            LEFT JOIN cp6_oa_notification_v1.owner_sources s ON s.notification_id=n."Id"
            WHERE n."TenantId"={db.CurrentTenantId} AND n."UserId"={user} AND n."InAppRequested"
              AND (s.notification_id IS NULL OR (s.tenant_id=n."TenantId" AND s.environment_id={scope.EnvironmentId}
                AND s.recipient_id={user} AND s.task_id IS NOT DISTINCT FROM n."TaskId"))
              AND (NOT {unreadOnly} OR NOT n."IsRead")
              AND (n."NotificationInsertXid" IS NULL OR pg_visible_in_snapshot(n."NotificationInsertXid", CAST({point.Snapshot} AS pg_snapshot)))
              AND ({next == null} OR (n."CreateDate",n."Id") < (CAST({lastCreated} AS timestamp), CAST({next?.LastId} AS uuid)))
            ORDER BY n."CreateDate" DESC,n."Id" DESC
            OFFSET {offset} LIMIT {take + 1}
            """).ToListAsync(ct);
        var items = rows.Take(take).Select(row =>
        {
            var summary = row.Title.Length > 0 ? row.Title : NotifyMatrix.Summary(row.Type);
            return row with { Title = summary, Body = summary };
        }).ToArray();
        var nextCursor = rows.Count > take
            ? Issue(point with { LastCreated = items[^1].CreateDate, LastId = items[^1].Id, UnreadOnly = unreadOnly })
            : null;
        var checkpoint = Issue(point);
        await scope.ValidateAsync(lease, ct);
        return new(items, checkpoint, checkpoint, nextCursor);
    }

    public async Task<int> UnreadCountAsync(CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var lease = await scope.AcquireAsync(ct);
        var user = await WorkflowNotificationAuthorityV1.ActualUserAsync(db, http, ct);
        var count = await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value"
            FROM public."Wf_Notification" n LEFT JOIN cp6_oa_notification_v1.owner_sources s ON s.notification_id=n."Id"
            WHERE n."TenantId"={db.CurrentTenantId} AND n."UserId"={user} AND n."InAppRequested" AND NOT n."IsRead"
              AND (s.notification_id IS NULL OR (s.tenant_id=n."TenantId" AND s.environment_id={scope.EnvironmentId}
                AND s.recipient_id={user} AND s.task_id IS NOT DISTINCT FROM n."TaskId"))
            """).SingleAsync(ct);
        await scope.ValidateAsync(lease, ct);
        return count;
    }

    public Task<int> MarkReadAsync(Guid id, CancellationToken ct) => MarkAsync(id, null, ct);

    public Task<int> ReadThroughAsync(string? watermark, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(watermark)) throw Error("NOTIFICATION_WATERMARK_REQUIRED", 428);
        return MarkAsync(null, watermark, ct);
    }

    private async Task<int> MarkAsync(Guid? id, string? watermark, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var lease = await scope.AcquireAsync(ct);
        var user = await WorkflowNotificationAuthorityV1.ActualUserAsync(db, http, ct);
        await WorkflowNotificationAuthorityV1.RequireActionAsync(db, user, "oa-inbox", "read", ct);
        var snapshot = watermark is null ? null : ReadToken(watermark, user, false).Snapshot;
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE public."Wf_Notification" n SET "IsRead"=true,"ReadAt"=clock_timestamp()
            WHERE n."TenantId"={db.CurrentTenantId} AND n."UserId"={user} AND n."InAppRequested" AND NOT n."IsRead"
              AND (CAST({id} AS uuid) IS NULL OR n."Id"={id})
              AND (CAST({snapshot} AS pg_snapshot) IS NULL OR n."NotificationInsertXid" IS NULL
                OR pg_visible_in_snapshot(n."NotificationInsertXid", CAST({snapshot} AS pg_snapshot)))
              AND NOT EXISTS (SELECT 1 FROM cp6_oa_notification_v1.owner_sources s WHERE s.notification_id=n."Id"
                AND (s.tenant_id<>n."TenantId" OR s.environment_id<>{scope.EnvironmentId}
                     OR s.recipient_id<>{user} OR s.task_id IS DISTINCT FROM n."TaskId"))
            """, ct);
        await scope.ValidateAsync(lease, ct);
        await transaction.CommitAsync(ct);
        return changed;
    }

    private async Task<Point> Observe(Guid user, CancellationToken ct)
    {
        var snapshot = await db.Database.SqlQueryRaw<string>("SELECT pg_current_snapshot()::text AS \"Value\"").SingleAsync(ct);
        return new(db.CurrentTenantId, scope.EnvironmentId, user, snapshot);
    }

    private string Issue(Point point) => Tokens.Protect(JsonSerializer.Serialize(point), TimeSpan.FromMinutes(30));

    private Point ReadToken(string value, Guid user, bool isCursor)
    {
        if (value.Length > 16384) throw Error("NOTIFICATION_WATERMARK_INVALID", 422);
        try
        {
            var point = JsonSerializer.Deserialize<Point>(Tokens.Unprotect(value));
            if (point is null || point.Tenant != db.CurrentTenantId || point.Environment != scope.EnvironmentId || point.User != user ||
                isCursor != (point.LastCreated is not null && point.LastId is not null && point.UnreadOnly is not null))
                throw Error("NOTIFICATION_WATERMARK_INVALID", 422);
            return point;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        { throw Error("NOTIFICATION_WATERMARK_INVALID", 422); }
    }

    private static ScopeAuthorizationExceptionV1 Error(string code, int status) => new(code, status);
}
