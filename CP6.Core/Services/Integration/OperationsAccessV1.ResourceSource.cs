using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal async Task RequireResourceSource(CP6Context context, string kind, Guid id, CancellationToken ct)
    {
        if (!ReferenceEquals(db, context) || db.Database.CurrentTransaction == null || authority.Source.TenantId != db.CurrentTenantId)
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
        var resource = kind switch { "MACHINE" => OperationsOwnerV1.Machine, "WORK_CENTER" => OperationsOwnerV1.WorkCenter, _ => "" };
        if (resource.Length == 0 || authority.Resource != resource || authority.Action is not ("query" or "edit" or "capability-edit"))
            throw new OrderPriceCorrectionException("ACTION_FORBIDDEN", 403);
        var found = kind == "MACHINE" ? await Machines(true).AnyAsync(row => row.Id == id, ct)
            : await WorkCenters(true).AnyAsync(row => row.Id == id, ct);
        if (!found) throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
    }
}
