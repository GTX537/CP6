using CP6.Core.Services.Erp;
using CP6.Core.Services.Mes;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal void RequireQualitySpecificationFields(bool write)
    {
        if (authority.Resource != OperationsOwnerV1.QualitySpecification) throw new OrderPriceCorrectionException("ACTION_FORBIDDEN",403);
        foreach (var field in new[] { "Metadata", "Requirements" })
            if (!authority.Fields.Fields.TryGetValue(field, out var value) || !value.CanRead || write && !value.CanWrite)
                throw new OrderPriceCorrectionException(write ? "FIELD_WRITE_FORBIDDEN" : "FIELD_READ_FORBIDDEN",403);
    }
    internal IQueryable<QualitySpecificationHeadV1> QualitySpecifications(Guid environment)
    {
        RequireQualitySpecificationFields(false);
        if (authority.Source.TenantId != db.CurrentTenantId || authority.Action is not ("query" or "add" or "edit" or "review-policy" or "review-submit" or "review-decide" or "withdraw"))
            throw new OrderPriceCorrectionException("ACTION_FORBIDDEN",403);
        var rows = QualitySpecificationStoreV1.Heads(db, environment);
        if (authority.Scope == 1) return rows.Where(x => x.CreatorId == ActorId);
        if (authority.Scope == 5) return rows;
        var products = StandaloneDepartmentScopeV1.Objects(db, authority, "plm-engineering-product");
        return rows.Where(x => products.Contains(x.ProductId));
    }
    internal async Task RequireQualitySpecification(Guid environment, Guid id, CancellationToken ct)
    { if (!await QualitySpecifications(environment).AnyAsync(x => x.Id == id, ct)) throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403); }
    internal async Task RequireQualitySpecificationCreation(Guid product, CancellationToken ct)
    {
        if (authority.Resource != OperationsOwnerV1.QualitySpecification || authority.Action != "add" || product == Guid.Empty)
            throw new OrderPriceCorrectionException("ACTION_FORBIDDEN",403);
        RequireQualitySpecificationFields(true);
        if (authority.Scope is 1 or 5) return;
        if (!await StandaloneDepartmentScopeV1.Objects(db, authority, "plm-engineering-product").AnyAsync(x => x == product,ct))
            throw new OrderPriceCorrectionException("CREATION_DEPARTMENT_FORBIDDEN",403);
    }
}
