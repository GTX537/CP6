using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

/// <summary>Current-Owner history projection. Native audit rows are never modified by a read.</summary>
public sealed class FieldAuditReadOwnerV1(CP6Context db, OperationsOwnerV1 owner)
{
    public const string MappingVersion = "Main.FieldAudit.Machine.Status.v1";
    public sealed record Row(Guid Id, int Operation, DateTime ChangedAt,
        IReadOnlyList<FieldAuditChangesV1.Change> Changes, string EvidenceStatus);
    public sealed record Page(IReadOnlyList<Row> Rows, Guid? NextCursor, string ProjectionVersion);
    public bool Owns(CP6Context context) => ReferenceEquals(context, db) && owner.Owns(db);

    public Task<Page> ReadAsync(string entityName, string entityKey, Guid? afterId = null,
        int pageSize = 100, CancellationToken ct = default)
    {
        // No generic fallback: a log-menu grant cannot authorize arbitrary business history.
        if (entityName != "Machine" || !Owns(db) || !db.Database.IsNpgsql())
            throw new OrderPriceCorrectionException("DETAIL_UNAVAILABLE", 503);
        if (!Guid.TryParseExact(entityKey, "D", out var id))
            throw new OrderPriceCorrectionException("OBJECT_NOT_VISIBLE", 404);
        var key = id.ToString("D"); var take = Math.Clamp(pageSize, 1, 100);
        return owner.ExecuteAsync(OperationsOwnerV1.Machine, "query", false, async (access, token) =>
        {
            await RequireAuditAction(access.ActorId, token);
            // Existing Machine owner applies current self/department/custom/all scope to actual rows.
            // Deleted and missing objects have no historical-read policy in this mapping.
            if (!await access.Machines().AnyAsync(m => m.Id == id, token))
                throw new OrderPriceCorrectionException("OBJECT_NOT_VISIBLE", 404);
            access.AtCommit(t => RequireAuditAction(access.ActorId, t));
            var rows = db.Sys_FieldAuditLogs.AsNoTracking()
                .Where(a => a.EntityName == "Machine" && a.EntityKey == key);
            var page = rows;
            if (afterId is Guid cursor)
            {
                var anchor = await rows.Where(a => a.Id == cursor).Select(a => new { a.Id, a.ChangedAt }).SingleOrDefaultAsync(token);
                if (anchor == null) throw new OrderPriceCorrectionException("AUDIT_CURSOR_INVALID", 400);
                page = rows.Where(a => a.ChangedAt > anchor.ChangedAt
                    || a.ChangedAt == anchor.ChangedAt && a.Id.CompareTo(anchor.Id) > 0);
            }
            var native = await page.OrderBy(a => a.ChangedAt).ThenBy(a => a.Id).Take(take + 1)
                .Select(a => new { a.Id, a.Operation, a.ChangedAt, a.Changes }).ToListAsync(token);
            var projected = native.Take(take).Select(a =>
            {
                var safe = FieldAuditChangesV1.MachineStatus(a.Changes);
                return new Row(a.Id, a.Operation, a.ChangedAt, safe.Changes, safe.EvidenceStatus);
            }).ToArray();
            return new Page(projected, native.Count > take ? projected[^1].Id : null, MappingVersion);
        }, ct);
    }

    private async Task RequireAuditAction(Guid subject, CancellationToken ct)
    {
        var actor = await PostgreSqlPermissionReader.ReadAsync(db, subject, ct);
        if (!actor.ActionKeys.Contains("sys-field-audit:query")
            || !await (from action in db.Sys_MenuActions.AsNoTracking()
                       join menu in db.Sys_Menus.AsNoTracking() on action.MenuId equals menu.MenuId
                       where menu.Enable && menu.MenuKey == "sys-field-audit" && action.ActionCode == "query"
                       select action.Id).AnyAsync(ct))
            throw new OrderPriceCorrectionException("ACTION_FORBIDDEN", 403);
    }
}
