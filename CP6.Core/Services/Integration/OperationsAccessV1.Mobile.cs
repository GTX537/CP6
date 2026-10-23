using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal IQueryable<MobileTask> MobileTasks()
    {
        var rows = db.MobileTasks.AsNoTracking().Where(t => !t.IsDeleted);
        if (authority.Scope == 1) rows = rows.Where(t => t.Creator == Actor);
        else if (authority.Scope != 5)
        {
            var ids = StandaloneDepartmentScopeV1.Objects(db, authority, "mobile-task");
            rows = rows.Where(t => ids.Contains(t.Id));
        }
        var locations = db.Locations.AsNoTracking().Where(l => !l.IsDeleted);
        if (!authority.Actor.RoleIds.Contains(1))
        {
            var grants = db.WmsRoleScopes.Where(g => authority.Actor.RoleIds.Contains(g.RoleId));
            locations = locations.Where(l => grants.Any(g => (g.WarehouseCd == "*" || g.WarehouseCd == l.WarehouseCd)
                && (g.AreaCd == null || g.AreaCd == "" || g.AreaCd == "*" || g.AreaCd == l.AreaCd)));
        }
        // The task's display AreaCd and a source display number do not establish authority.
        return rows.Where(t => locations.Any(l => l.WarehouseCd == t.WarehouseCd && l.LocationCd == t.FromLocationCd)
            && locations.Any(l => l.WarehouseCd == t.WarehouseCd && l.LocationCd == t.ToLocationCd));
    }

    internal async Task<MobileTask> RequireMobileTask(string number, int? version, CancellationToken ct)
    {
        var row = await MobileTasks().Where(t => version == null || t.ContractVersion == version)
            .SingleOrDefaultAsync(t => t.MobileTaskNo == number, ct);
        return row ?? throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
    }

    internal void RequireMobileAssigneeFilter(string? assignedTo)
    {
        if (!string.IsNullOrWhiteSpace(assignedTo) && !MobileField("AssignedTo"))
            throw new OrderPriceCorrectionException("FIELD_FORBIDDEN", 403);
    }
    private bool MobileField(string name) => authority.Fields.Fields.TryGetValue(name, out var grant) && grant.CanRead;

    internal static void RequireLegacyMoveKind(MobileTask row)
    {
        if (row.TaskType != MobileTaskType.Move)
            throw new OrderPriceCorrectionException("MOBILE_KIND_READ_UNPROVEN", 503);
    }
    internal void ProjectMobile(MobileTask row, bool list)
    {
        RequireLegacyMoveKind(row);
        row.Creator = null; row.Modifier = null;
        // No source/parent/device or raw-event authority is implied by task visibility.
        row.RelatedNo = null; row.RelatedType = null; row.ParentTaskNo = null; row.RemainderTaskNo = null;
        row.LastDeviceId = null; row.OutTxnNo = null; row.InTxnNo = null;
        row.PauseReason = null; row.PartialReason = null; row.ExceptionReasonCd = null;
        if (!MobileField("AssignedTo")) row.AssignedTo = null;
        if (list || !MobileField("Instruction")) row.Instruction = null;
        if (list || !MobileField("Remarks")) row.Remarks = null;
        if (list || !MobileField("ExceptionDescription")) row.ExceptionDescription = null;
    }
}
