using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    // Assembly-internal admission to the original Order Owner, never a replacement authority.
    internal IDbContextTransaction RequireToolOrderTransaction(CP6Context context, bool write)
    {
        if (!ReferenceEquals(db, context) || !db.Database.IsNpgsql() || db.Database.CurrentTransaction == null
            || authority.Source.TenantId != db.CurrentTenantId || authority.Source.SubjectId != ActorId)
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
        if (authority.Resource != OperationsOwnerV1.PlateMold || (write ? authority.Action is not ("add" or "edit" or "del") : authority.Action is not ("query" or "add" or "edit" or "del")))
            throw new OrderPriceCorrectionException("ACTION_FORBIDDEN", 403);
        return db.Database.CurrentTransaction;
    }
}
