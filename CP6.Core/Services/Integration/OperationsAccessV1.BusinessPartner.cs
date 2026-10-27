using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Erp;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal void RequireBusinessPartnerSourceAccess(CP6Context context)
    {
        if (!ReferenceEquals(db, context) || !owner.Owns(context) || db.Database.CurrentTransaction == null
            || authority.Source.TenantId != db.CurrentTenantId)
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
        if (authority.Resource != OperationsOwnerV1.BusinessPartner || authority.Action != "query")
            throw new OrderPriceCorrectionException("ACTION_FORBIDDEN", 403);
    }
    internal bool BusinessPartnerNameReadable => authority.Fields.Fields.TryGetValue("BpName", out var field) && field.CanRead;
    internal IQueryable<BusinessPartner> BusinessPartners(bool includeDeleted = false)
    {
        var rows = db.BusinessPartners.AsNoTracking().Where(p => includeDeleted || !p.IsDeleted);
        if (authority.Scope == 1) return rows.Where(p => p.Creator == Actor);
        if (authority.Scope == 5) return rows;
        var allowed = StandaloneDepartmentScopeV1.Objects(db, authority, "business-partner");
        return rows.Where(p => allowed.Contains(p.Id));
    }
    internal async Task RequireBusinessPartner(CP6Context context, Guid id, CancellationToken ct)
    {
        RequireBusinessPartnerSourceAccess(context);
        if (!await BusinessPartners(includeDeleted: true).AnyAsync(p => p.Id == id, ct))
            throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
    }
}
