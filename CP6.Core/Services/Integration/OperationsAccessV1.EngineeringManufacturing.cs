using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal IQueryable<EngineeringManufacturingHeadV1> EngineeringManufacturings()
    {
        if (authority.Source.TenantId != db.CurrentTenantId || authority.Resource != OperationsOwnerV1.EngineeringManufacturing
            || authority.Action is not ("query" or "add" or "edit")) throw new OrderPriceCorrectionException("ACTION_FORBIDDEN", 403);
        var rows = EngineeringManufacturingStoreV1.Heads(db);
        if (authority.Scope == 1) return rows.Where(x => x.CreatorId == ActorId);
        if (authority.Scope == 5) return rows;
        // The immutable parent revision is the trusted department source. No guessed creator department or duplicate mapping.
        var allowed = StandaloneDepartmentScopeV1.Objects(db, authority, "plm-engineering-revision");
        return rows.Where(x => allowed.Contains(x.RevisionId));
    }
    internal async Task RequireEngineeringManufacturing(Guid id, CancellationToken ct)
    { if (!await EngineeringManufacturings().AnyAsync(x => x.Id == id, ct)) throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403); }
    internal async Task RequireEngineeringManufacturingCreation(Guid parent, CancellationToken ct)
    {
        if (authority.Resource != OperationsOwnerV1.EngineeringManufacturing || authority.Action != "add" || parent == Guid.Empty)
            throw new OrderPriceCorrectionException("ACTION_FORBIDDEN", 403);
        if (authority.Scope is 1 or 5) return;
        if (!await StandaloneDepartmentScopeV1.Objects(db, authority, "plm-engineering-revision").AnyAsync(x => x == parent, ct))
            throw new OrderPriceCorrectionException("CREATION_DEPARTMENT_FORBIDDEN", 403);
    }
}
