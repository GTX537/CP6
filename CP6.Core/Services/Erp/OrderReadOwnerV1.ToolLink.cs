using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Erp;

public sealed partial class OrderReadOwnerV1
{
    // Minimal native identity only; this does not publish a formal Sales schedule or release source.
    internal async Task<ToolNativeOrderRefV1> EnlistToolRead(OperationsAccessV1 root, Guid lineId, CancellationToken ct)
    {
        var tx = root.RequireToolOrderTransaction(db, false);
        var initial = await AuthorityAsync("erp-order", FieldPermissionUseV1.Read, ct);
        if (initial.Source.SubjectId != root.ActorId) throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
        async Task<ToolNativeOrderRefV1> Read(Authority authority, CancellationToken token)
        {
            var allowed = OrderDepartmentScopeV1.Query(db, authority.Actor, authority.Scope, includeDeleted:true).Select(d => d.Id);
            var line = await db.OrderDetails.AsNoTracking().SingleOrDefaultAsync(d => d.Id == lineId, token)
                ?? throw new OrderPriceCorrectionException("ORIGINAL_ORDER_UNAVAILABLE", 409);
            var header = await OrderDepartmentScopeV1.Headers(db, authority.Actor, authority.Scope, true).AsNoTracking()
                .SingleOrDefaultAsync(o => o.WebOrderNo == line.WebOrderNo, token);
            if (header == null || await db.OrderDetails.AnyAsync(d => d.WebOrderNo == line.WebOrderNo && !allowed.Contains(d.Id), token))
                throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
            return new(header.Id, line.Id, header.WebOrderNo, line.WebOrderDetailNo, line.HaibaiNo1 ?? "",
                Convert.ToBase64String(header.RowVersion), Convert.ToBase64String(line.RowVersion), header.IsDeleted || line.IsDeleted);
        }
        var result = await Read(initial, ct);
        root.AtCommit(async token =>
        {
            if (!ReferenceEquals(tx, root.RequireToolOrderTransaction(db, false)))
                throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
            var final = await AuthorityAsync("erp-order", FieldPermissionUseV1.Read, token);
            if (final.Source.SubjectId != initial.Source.SubjectId || final.Source.SourcePermissionVersion != initial.Source.SourcePermissionVersion
                || final.Source.RegistryVersion != initial.Source.RegistryVersion || final.Scope != initial.Scope
                || final.OrganizationVersion != initial.OrganizationVersion || final.Source.TenantExpiresAt <= DateTime.Now)
                throw new OrderPriceCorrectionException("AUTHORITY_CHANGED", 403);
            if (await Read(final, token) != result) throw new OrderPriceCorrectionException("ORIGINAL_ORDER_CHANGED", 412);
        });
        return result;
    }
}
