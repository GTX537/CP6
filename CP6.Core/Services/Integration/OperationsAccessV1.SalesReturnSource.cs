using CP6.Core.Services.Erp;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal async Task<SalesReturnCurrentV1?> ReadHistoricalOrderLine(Guid orderId, Guid lineId, CancellationToken ct)
    {
        var source = await owner.AuthorityAsync("order", "query", ct); Authorities.Add(source);
        if (!source.Fields.Fields["UnitPrice"].CanRead || !source.Fields.Fields["Amount"].CanRead)
            throw new OrderPriceCorrectionException("FIELD_READ_FORBIDDEN", 403);
        // Historical ERP reads include cancelled/deleted rows, but still use today's exact object scope.
        var header = await OrderDepartmentScopeV1.Headers(db, source.Actor, source.Scope, true).AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == orderId, ct) ?? throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
        var line = await OrderDepartmentScopeV1.Query(db, source.Actor, source.Scope, includeDeleted: true).AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == lineId && x.WebOrderNo == header.WebOrderNo, ct);
        if (line == null)
        {
            // Department/full scopes follow the authorized header. Self scope needs the actual line creator.
            if (source.Scope == 1 || await db.OrderDetails.AnyAsync(x => x.Id == lineId, ct))
                throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
            return null;
        }
        return new(header.CustomerCd, line.ProductCd, header.IsDeleted, line.IsDeleted, header.CancelledAt,
            SalesReturnSourceReaderV1.Terms(header, line));
    }
}
