using System.Globalization;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public sealed record OrderFulfilmentLineV1(Guid Id, int Number, string ProductCode, string? Quantity, string? UnitCode);
public sealed record OrderFulfilmentSourceV1(Guid Id, string Number, string CustomerCode,
    DateTimeOffset CapturedAt, IReadOnlyList<OrderFulfilmentLineV1> Lines);

internal static class OrderFulfilmentSourceReaderV1
{
    internal const string Owner = "Main.Erp.OrderFulfilment.v1";

    // The shipping Owner has authorized this order and holds the native order graph lock.
    // This is a new, immutable fulfilment source version, not a claim about a legacy order revision.
    internal static async Task<OrderFulfilmentSourceV1> Read(CP6Context db, string number, DateTimeOffset at, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().SingleAsync(x => x.WebOrderNo == number, ct);
        var lines = await db.OrderDetails.AsNoTracking().Where(x => x.WebOrderNo == number && !x.IsDeleted)
            .OrderBy(x => x.WebOrderDetailNo).ToArrayAsync(ct);
        return new(order.Id, number, order.CustomerCd, at, lines.Select(x => new OrderFulfilmentLineV1(
            x.Id, x.WebOrderDetailNo, x.ProductCd, x.Quantity?.ToString(CultureInfo.InvariantCulture), x.QtyUnit)).ToArray());
    }
}
