using System.Globalization;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services;

public class OtdReportService : IOtdReportService
{
    private static readonly string[] CsvHeader =
    [
        "GroupKey",
        "GroupLabel",
        "TotalShippedOrders",
        "OnTimeCount",
        "LateCount",
        "OnTimeRate",
        "AvgLateDays",
    ];

    private readonly CP6Context _db;

    public OtdReportService(CP6Context db)
    {
        _db = db;
    }

    public async Task<OtdReportSummaryDto> GetSummaryAsync(OtdReportQuery query, CancellationToken ct = default)
    {
        query ??= new OtdReportQuery();
        var groupBy = NormalizeGroupBy(query.GroupBy);

        var orderQuery = _db.Orders.AsNoTracking()
            .Where(x => !x.IsDeleted && x.ShipStatus >= 5 && x.CustomerDeliveryDate != null && x.OrderDate != null);

        if (query.DateFrom.HasValue)
        {
            var from = query.DateFrom.Value.Date;
            orderQuery = orderQuery.Where(x => x.OrderDate >= from);
        }

        if (query.DateTo.HasValue)
        {
            var toExclusive = query.DateTo.Value.Date.AddDays(1);
            orderQuery = orderQuery.Where(x => x.OrderDate < toExclusive);
        }

        if (!string.IsNullOrWhiteSpace(query.CustomerCd))
        {
            var customerCd = query.CustomerCd.Trim();
            orderQuery = orderQuery.Where(x => x.CustomerCd == customerCd);
        }

        var orders = await orderQuery.ToListAsync(ct);
        var orderNos = orders.Select(x => x.WebOrderNo).ToList();
        var lastShipDates = await LoadDetailLastShipDatesAsync(orderNos, ct);
        var customerNames = await LoadCustomerNamesAsync(orders.Select(x => x.CustomerCd).Distinct().ToList(), ct);

        var facts = orders
            .Select(order => ToFact(order, lastShipDates, customerNames, groupBy))
            .Where(x => x != null)
            .Select(x => x!)
            .ToList();

        var rows = facts
            .GroupBy(x => new { x.GroupKey, x.GroupLabel })
            .Select(g => BuildRow(g.Key.GroupKey, g.Key.GroupLabel, g))
            .OrderByDescending(x => x.OnTimeRate)
            .ThenBy(x => x.GroupKey)
            .ToList();

        var total = facts.Count;
        var onTime = facts.Count(x => x.LateDays <= 0);
        var late = facts.Count(x => x.LateDays > 0);

        return new OtdReportSummaryDto
        {
            TotalShippedOrders = total,
            OnTimeCount = onTime,
            LateCount = late,
            OnTimeRate = Rate(onTime, total),
            Rows = rows,
        };
    }

    public async Task<byte[]> ExportCsvAsync(OtdReportQuery query, CancellationToken ct = default)
    {
        var summary = await GetSummaryAsync(query, ct);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", CsvHeader));

        foreach (var row in summary.Rows)
        {
            sb.AppendLine(string.Join(",", new[]
            {
                Csv(row.GroupKey),
                Csv(row.GroupLabel),
                row.TotalShippedOrders.ToString(CultureInfo.InvariantCulture),
                row.OnTimeCount.ToString(CultureInfo.InvariantCulture),
                row.LateCount.ToString(CultureInfo.InvariantCulture),
                FormatDecimal(row.OnTimeRate),
                FormatDecimal(row.AvgLateDays),
            }));
        }

        var content = Encoding.UTF8.GetBytes(sb.ToString());
        return Encoding.UTF8.GetPreamble().Concat(content).ToArray();
    }

    private async Task<Dictionary<string, DateTime?>> LoadDetailLastShipDatesAsync(List<string> orderNos, CancellationToken ct)
    {
        if (orderNos.Count == 0)
        {
            return new Dictionary<string, DateTime?>();
        }

        return await _db.OrderDetails.AsNoTracking()
            .Where(x => !x.IsDeleted && orderNos.Contains(x.WebOrderNo))
            .GroupBy(x => x.WebOrderNo)
            .Select(g => new { WebOrderNo = g.Key, LastShipDate = g.Max(x => x.LastShipDate) })
            .ToDictionaryAsync(x => x.WebOrderNo, x => x.LastShipDate, ct);
    }

    private async Task<Dictionary<string, string?>> LoadCustomerNamesAsync(List<string> customerCds, CancellationToken ct)
    {
        if (customerCds.Count == 0)
        {
            return new Dictionary<string, string?>();
        }

        return await _db.BusinessPartners.AsNoTracking()
            .Where(x => customerCds.Contains(x.BpCd) && !x.IsDeleted)
            .GroupBy(x => x.BpCd)
            .Select(g => new { CustomerCd = g.Key, CustomerName = g.Select(x => x.BpName).FirstOrDefault() })
            .ToDictionaryAsync(x => x.CustomerCd, x => x.CustomerName, ct);
    }

    private static OtdFact? ToFact(
        Order order,
        IReadOnlyDictionary<string, DateTime?> lastShipDates,
        IReadOnlyDictionary<string, string?> customerNames,
        string groupBy)
    {
        var promiseDate = order.CustomerDeliveryDate?.Date;
        var orderDate = order.OrderDate?.Date;
        var actualDate = (order.ActualShipDate ?? (lastShipDates.TryGetValue(order.WebOrderNo, out var last) ? last : null))?.Date;

        if (!promiseDate.HasValue || !actualDate.HasValue || !orderDate.HasValue)
        {
            return null;
        }

        var lateDays = (int)(actualDate.Value - promiseDate.Value).TotalDays;
        if (groupBy == OtdReportGroupBy.Month)
        {
            return new OtdFact(orderDate.Value.ToString("yyyyMM", CultureInfo.InvariantCulture), orderDate.Value.ToString("yyyy-MM", CultureInfo.InvariantCulture), lateDays);
        }

        var customerName = customerNames.TryGetValue(order.CustomerCd, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name!
            : order.CustomerCd;
        return new OtdFact(order.CustomerCd, customerName, lateDays);
    }

    private static OtdReportRowDto BuildRow(string groupKey, string groupLabel, IEnumerable<OtdFact> facts)
    {
        var list = facts.ToList();
        var total = list.Count;
        var onTime = list.Count(x => x.LateDays <= 0);
        var lateDays = list.Where(x => x.LateDays > 0).Select(x => x.LateDays).ToList();
        var late = lateDays.Count;

        return new OtdReportRowDto
        {
            GroupKey = groupKey,
            GroupLabel = groupLabel,
            TotalShippedOrders = total,
            OnTimeCount = onTime,
            LateCount = late,
            OnTimeRate = Rate(onTime, total),
            AvgLateDays = late == 0 ? 0m : decimal.Round(lateDays.Sum() / (decimal)late, 2, MidpointRounding.AwayFromZero),
        };
    }

    private static string NormalizeGroupBy(string? groupBy)
    {
        return string.Equals(groupBy, OtdReportGroupBy.Month, StringComparison.OrdinalIgnoreCase)
            ? OtdReportGroupBy.Month
            : OtdReportGroupBy.Customer;
    }

    private static decimal Rate(int numerator, int denominator)
    {
        return denominator == 0 ? 0m : decimal.Round(numerator / (decimal)denominator, 4, MidpointRounding.AwayFromZero);
    }

    private static string Csv(string? value)
    {
        var text = value ?? string.Empty;
        return text.Contains(',') || text.Contains('"') || text.Contains('\r') || text.Contains('\n')
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }

    private static string FormatDecimal(decimal value)
    {
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private sealed record OtdFact(string GroupKey, string GroupLabel, int LateDays);
}
