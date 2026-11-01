using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal void RequireReplenishSimulationScope()
    {if(authority.Scope!=5)throw new OrderPriceCorrectionException("REP_SIMULATION_SCOPE_REQUIRED",403);}
    internal IQueryable<Location> ReplenishPlanningLocations()
    {
        var rows=db.Locations.AsNoTracking().Where(x=>!x.IsDeleted&&db.Warehouses.Any(w=>!w.IsDeleted&&w.WarehouseCd==x.WarehouseCd));
        if(authority.Actor.RoleIds.Contains(1))return rows;
        var grants=db.WmsRoleScopes.Where(g=>authority.Actor.RoleIds.Contains(g.RoleId));
        return rows.Where(x=>grants.Any(g=>(g.WarehouseCd=="*"||g.WarehouseCd==x.WarehouseCd)
            &&(g.AreaCd==null||g.AreaCd==""||g.AreaCd=="*"||g.AreaCd==x.AreaCd)));
    }
    internal async Task RequireReplenishPlanningDocument(Guid creator,Guid? department,CancellationToken ct)
    {
        if(authority.Scope==5||authority.Scope==1&&creator==ActorId)return;
        if(authority.Scope!=1&&department is {} id&&(await TransferDepartments(ct)).Departments.Any(x=>x.Id==id))return;
        throw new OrderPriceCorrectionException("REP_NOT_VISIBLE",404);
    }

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
