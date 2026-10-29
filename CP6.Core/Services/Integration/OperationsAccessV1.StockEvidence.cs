using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    // Coverage describes authority over this native location, never counts hidden rows.
    // Creator/department scope cannot prove an empty location from an empty filtered query.
    internal async Task<bool> HasWholeStockLocationScope(Location location,CancellationToken ct)
    {
        if(authority.Resource!=OperationsOwnerV1.Stock)throw new OrderPriceCorrectionException("ACTION_FORBIDDEN",403);
        if(authority.Scope!=5)return false;
        if(authority.Actor.RoleIds.Contains(1))return true;
        return await db.WmsRoleScopes.AnyAsync(g=>authority.Actor.RoleIds.Contains(g.RoleId)
            &&(g.WarehouseCd=="*"||g.WarehouseCd==location.WarehouseCd)
            &&(g.AreaCd==null||g.AreaCd==""||g.AreaCd=="*"
                ||!location.IsInitializationQuarantined&&g.AreaCd==location.AreaCd),ct);
    }
}
