using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Erp;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal IQueryable<PlateMold> PlateMolds(bool includeDeleted = false)
    {
        var rows = db.PlateMolds.AsNoTracking().Where(p => includeDeleted || !p.IsDeleted);
        if (authority.Scope == 1) return rows.Where(p => p.Creator == Actor);
        if (authority.Scope == 5) return rows;
        var ids = StandaloneDepartmentScopeV1.Objects(db, authority, "plate-mold");
        return rows.Where(p => ids.Contains(p.Id));
    }
    internal async Task RequirePlateMoldSource(CP6Context context, Guid id, CancellationToken ct)
    {
        if (!ReferenceEquals(db, context) || db.Database.CurrentTransaction == null || authority.Source.TenantId != db.CurrentTenantId)
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
        if (authority.Resource != OperationsOwnerV1.PlateMold || authority.Action != "query")
            throw new OrderPriceCorrectionException("ACTION_FORBIDDEN", 403);
        if (!await PlateMolds(true).AnyAsync(p => p.Id == id, ct))
            throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
    }
}
