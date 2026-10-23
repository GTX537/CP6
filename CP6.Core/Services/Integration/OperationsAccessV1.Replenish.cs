using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal IQueryable<ReplenishOrder> Replenishments()
    {
        var rows = db.ReplenishOrders.AsNoTracking().Where(r => !r.IsDeleted);
        if (authority.Scope == 1) rows = rows.Where(r => r.Creator == Actor);
        else if (authority.Scope != 5)
        {
            var ids = StandaloneDepartmentScopeV1.Objects(db, authority, "replenish-order");
            rows = rows.Where(r => ids.Contains(r.Id));
        }
        var locations = db.Locations.AsNoTracking().Where(l => !l.IsDeleted);
        if (!authority.Actor.RoleIds.Contains(1))
        {
            var grants = db.WmsRoleScopes.Where(g => authority.Actor.RoleIds.Contains(g.RoleId));
            locations = locations.Where(l => grants.Any(g => (g.WarehouseCd == "*" || g.WarehouseCd == l.WarehouseCd) &&
                (g.AreaCd == null || g.AreaCd == "" || g.AreaCd == "*" || g.AreaCd == l.AreaCd)));
        }
        // Destination permission is not source permission. Both physical locations must exist
        // in this tenant/warehouse and be independently visible; no PIK/RES name inference.
        return rows.Where(r => locations.Any(l => l.WarehouseCd == r.WarehouseCd && l.LocationCd == r.FromLocationCd) &&
            locations.Any(l => l.WarehouseCd == r.WarehouseCd && l.LocationCd == r.ToLocationCd));
    }

    internal async Task RequireReplenishment(string number, CancellationToken ct)
    {
        if (!await Replenishments().AnyAsync(r => r.ReplenishNo == number, ct))
            throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
    }

    internal void ProjectReplenishment(ReplenishOrder row, bool list)
    {
        bool Read(string field) => authority.Fields.Fields.TryGetValue(field, out var grant) && grant.CanRead;
        row.Creator = null; row.Modifier = null;
        if (list || !Read("OperatorCd")) row.OperatorCd = null;
        if (list || !Read("Remarks")) row.Remarks = null;
    }
}
