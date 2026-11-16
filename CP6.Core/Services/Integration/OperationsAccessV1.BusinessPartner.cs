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
    internal bool BusinessPartnerNameWritable => authority.Fields.Fields["BpName"].CanWrite;
    internal bool BusinessPartnerNameExportable => authority.Fields.Fields["BpName"].CanExport;

    internal Task<OperationsAccessV1?> BusinessPartnerReadbackAsync(CancellationToken ct)
        => LinkedSource(OperationsOwnerV1.BusinessPartner, ct, OperationsOwnerV1.BusinessPartnerProfileQuery);

    internal void RequireBusinessPartnerCreation()
    {
        // 旧新建请求没有可信的部门归属输入，不能从业务字段或人员名称猜测。
        if (authority.Scope is not (1 or 5))
            throw new OrderPriceCorrectionException("CREATION_DEPARTMENT_REQUIRED", 403);
    }

    internal void RequireBusinessPartnerAdministrator()
    {
        if (!authority.Actor.RoleIds.Contains(1))
            throw new OrderPriceCorrectionException("BUSINESS_PARTNER_ADMIN_REQUIRED", 403);
    }
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
