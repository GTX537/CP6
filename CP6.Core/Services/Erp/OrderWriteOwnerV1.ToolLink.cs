using CP6.Core.Services.Integration;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Erp;

public sealed partial class OrderWriteOwnerV1
{
    internal async Task<string> EnlistToolCreate(OperationsAccessV1 root, OrderDto request,
        Func<OrderDto, string, Task<string>> body, CancellationToken ct)
    {
        var tx = root.RequireToolOrderTransaction(db, true);
        if (db.ChangeTracker.HasChanges() || db.ChangeTracker.Entries().Any(e => Business(e.Entity)))
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
        var frozen = OperationsOwnerV1.Freeze(request);
        if (frozen?.Details == null || frozen.Details.Count != 1 || frozen.Details.Any(d => d == null
            || d.Processes == null || d.ProcessNotes == null || d.Materials == null
            || d.Processes.Count != 0 || d.ProcessNotes.Count != 0 || d.Materials.Count != 0)
            || frozen.OrderType != "30" || string.IsNullOrWhiteSpace(frozen.CustomerCd))
            throw new OrderPriceCorrectionException("ORDER_WRITE_INVALID", 400);
        var initial = await AuthorityAsync("add", ct);
        if (initial.Source.SubjectId != root.ActorId || string.IsNullOrWhiteSpace(initial.Actor.UserName))
            throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
        RequireFields(initial, "add");
        var number = await body(frozen, initial.Actor.UserName);
        async Task Validate(CancellationToken token)
        {
            if (!ReferenceEquals(tx, root.RequireToolOrderTransaction(db, true)))
                throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
            var final = await AuthorityAsync("add", token);
            if (final.Source.SubjectId != initial.Source.SubjectId || final.Source.SourcePermissionVersion != initial.Source.SourcePermissionVersion
                || final.Source.RegistryVersion != initial.Source.RegistryVersion || final.Scope != initial.Scope
                || final.OrganizationVersion != initial.OrganizationVersion || final.Source.TenantExpiresAt <= DateTime.Now)
                throw new OrderPriceCorrectionException("AUTHORITY_CHANGED", 403);
            RequireFields(final, "add"); await RequireObject(final, number, false, token);
        }
        await Validate(ct); root.AtCommit(Validate);
        return number;
    }
}
