using System.Globalization;
using System.Numerics;
using CP6.Core.Services.Erp;
using CP6.Entity.Contracts.FinInventory;

namespace CP6.Core.Services.Wms;

internal static class InventoryReportValuesV1
{
    // Stock quantity is numeric(21,8), price numeric(18,4). Their product need not fit System.Decimal.
    internal static InventoryDecimalValue Exact(decimal value)
    {
        var bits = decimal.GetBits(value);
        var coefficient = (BigInteger)(uint)bits[0] | (BigInteger)(uint)bits[1] << 32 | (BigInteger)(uint)bits[2] << 64;
        if (bits[3] < 0) coefficient = -coefficient;
        return InventoryDecimalValue.FromParts(coefficient, (bits[3] >> 16) & 0xFF);
    }
    internal static InventoryDecimalValue Sum(IEnumerable<decimal> values)
        => values.Aggregate(Exact(0), (total, value) => total.Add(Exact(value)));
    internal static string? Reference(IEnumerable<(decimal Qty, decimal? Price)> rows, bool readable)
    {
        if (!readable) return null;
        var total = Exact(0);
        foreach (var (qty, price) in rows)
        {
            if (price == null) return null;
            total = total.Add(Exact(qty).Multiply(Exact(price.Value)));
        }
        return total.ToString();
    }
    internal static decimal Legacy(string? value)
    {
        if (value == null || !decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var parsed) || Exact(parsed).ToString() != value)
            throw new OrderPriceCorrectionException("OWNER_READ_VERSION_REQUIRED", 409);
        return parsed;
    }
    internal static DateTime ObservationDate(DateTime? date)
    {
        if (date == null || date.Value.TimeOfDay != TimeSpan.Zero)
            throw new OrderPriceCorrectionException("OBSERVATION_DATE_REQUIRED", 400);
        return date.Value.Date;
    }
}
