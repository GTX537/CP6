using CP6.Entity.DomainModels.Erp;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal void RequireProductSourceAccess(CP6.Core.EFDbContext.CP6Context context)
    {
        if (!ReferenceEquals(db, context) || db.Database.CurrentTransaction == null || authority.Source.TenantId != db.CurrentTenantId)
            throw new CP6.Core.Services.Erp.OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
        if (authority.Resource != OperationsOwnerV1.Product || authority.Action is not ("query" or "edit" or "confirm-engineering"))
            throw new CP6.Core.Services.Erp.OrderPriceCorrectionException("ACTION_FORBIDDEN", 403);
    }
    internal IQueryable<ProductMaster> Products(bool includeDeleted = false)
    {
        var rows = db.ProductMasters.AsNoTracking().Where(p => includeDeleted || !p.IsDeleted);
        if (authority.Scope == 1) return rows.Where(p => p.Creator == Actor);
        if (authority.Scope == 5) return rows;
        var allowed = StandaloneDepartmentScopeV1.Objects(db, authority, "product-master");
        return rows.Where(p => allowed.Contains(p.Id));
    }
}
