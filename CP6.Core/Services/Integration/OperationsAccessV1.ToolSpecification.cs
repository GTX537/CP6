using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Erp;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;
internal sealed partial class OperationsAccessV1
{
    internal IQueryable<PlateMold> SpecificationTools(bool includeDeleted = false)
    {
        if (authority.Resource != OperationsOwnerV1.ToolSpecification || authority.Action is not ("query" or "add" or "edit"))
            throw new OrderPriceCorrectionException("ACTION_FORBIDDEN", 403);
        // Same native object identity/mapping; professional action and custom scope are independent.
        return PlateMolds(includeDeleted);
    }
    internal async Task RequireSpecificationTool(Guid id, CancellationToken ct)
    {
        if (!await SpecificationTools(true).AnyAsync(p => p.Id == id, ct)) throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
        AtCommit(async token => { if (!await SpecificationTools(true).AnyAsync(p => p.Id == id, token)) throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403); });
    }
}
