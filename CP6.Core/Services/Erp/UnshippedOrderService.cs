using System.Linq.Expressions;
using System.Globalization;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public class UnshippedOrderService : IUnshippedOrderService
{
    private const int ExportLimit = 5000;
    private readonly CP6Context _db;

    public UnshippedOrderService(CP6Context db)
    {
        _db = db;
    }

    public async Task<PagedResultDto<UnshippedOrderItemDto>> SearchAsync(UnshippedOrderQuery query)
    {
        var today = DateTime.Today;
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 50 : Math.Min(query.PageSize, 200);

        var orders = BuildFilteredOrders(query, today);
        var total = await orders.CountAsync();
        var pageOrders = await ApplySort(orders, query.SortField, query.SortOrder)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = await ProjectItemsAsync(pageOrders, today);

        return new PagedResultDto<UnshippedOrderItemDto>
        {
            Total = total,
            PageIndex = page,
            PageSize = pageSize,
            Items = items,
        };
    }

    public async Task<byte[]> ExportCsvAsync(UnshippedOrderQuery query)
    {
        var today = DateTime.Today;
        var orders = await ApplySort(BuildFilteredOrders(query, today), query.SortField, query.SortOrder)
            .Take(ExportLimit)
            .ToListAsync();
        var items = await ProjectItemsAsync(orders, today);
        return BuildCsv(items);
    }

    private IQueryable<Order> BuildFilteredOrders(UnshippedOrderQuery query, DateTime today)
    {
        var orders = _db.Orders.AsNoTracking()
            .Where(o => !o.IsDeleted
                && o.ShipStatus < 9
                && o.OrderStatus != OrderLifecycleStatus.Shipped
                && o.OrderStatus != OrderLifecycleStatus.Cancelled);

        if (!string.IsNullOrWhiteSpace(query.CustomerCd))
        {
            var customerCd = query.CustomerCd.Trim();
            orders = orders.Where(o => o.CustomerCd == customerCd);
        }

        if (query.OnlyOverdue == true)
        {
            orders = orders.Where(o => o.CustomerDeliveryDate != null && o.CustomerDeliveryDate < today);
        }

        return orders;
    }

    private async Task<List<UnshippedOrderItemDto>> ProjectItemsAsync(List<Order> pageOrders, DateTime today)
    {
        var webOrderNos = pageOrders.Select(o => o.WebOrderNo).ToList();
        var customerCds = pageOrders.Select(o => o.CustomerCd).Distinct().ToList();

        var customerNames = await _db.BusinessPartners.AsNoTracking()
            .Where(bp => customerCds.Contains(bp.BpCd) && !bp.IsDeleted)
            .GroupBy(bp => bp.BpCd)
            .Select(g => new { CustomerCd = g.Key, CustomerName = g.Select(bp => bp.BpName).FirstOrDefault() })
            .ToDictionaryAsync(x => x.CustomerCd, x => x.CustomerName);

        var detailSums = await _db.OrderDetails.AsNoTracking()
            .Where(d => webOrderNos.Contains(d.WebOrderNo) && !d.IsDeleted)
            .GroupBy(d => d.WebOrderNo)
            .Select(g => new
            {
                WebOrderNo = g.Key,
                OrderedQty = g.Sum(d => d.Quantity ?? 0m),
                ShippedQty = g.Sum(d => d.ShippedQty ?? 0m),
            })
            .ToDictionaryAsync(x => x.WebOrderNo);

        var workOrders = await _db.WorkOrders.AsNoTracking()
            .Where(wo => webOrderNos.Contains(wo.WebOrderNo!)
                && !wo.IsDeleted
                && wo.Status != WorkOrderStatus.Cancelled)
            .ToListAsync();

        var outbounds = await _db.OutboundOrders.AsNoTracking()
            .Where(ob => webOrderNos.Contains(ob.WebOrderNo!)
                && !ob.IsDeleted
                && ob.OutboundType == OutboundType.Shipping
                && ob.Status != OutboundOrderStatus.Cancelled)
            .ToListAsync();

        var items = pageOrders.Select(order =>
        {
            detailSums.TryGetValue(order.WebOrderNo, out var sums);
            var orderedQty = sums?.OrderedQty ?? 0m;
            var shippedQty = sums?.ShippedQty ?? 0m;
            var daysUntilDue = order.CustomerDeliveryDate.HasValue
                ? (int)(order.CustomerDeliveryDate.Value.Date - today).TotalDays
                : (int?)null;

            return new UnshippedOrderItemDto
            {
                WebOrderNo = order.WebOrderNo,
                CustomerCd = order.CustomerCd,
                CustomerName = customerNames.TryGetValue(order.CustomerCd, out var name) && !string.IsNullOrWhiteSpace(name)
                    ? name
                    : order.CustomerCd,
                OrderDate = order.OrderDate,
                CustomerDeliveryDate = order.CustomerDeliveryDate,
                OrderStatus = order.OrderStatus,
                ShipStatus = order.ShipStatus,
                OrderedQty = orderedQty,
                ShippedQty = shippedQty,
                RemainingQty = orderedQty - shippedQty,
                IsOverdue = order.CustomerDeliveryDate.HasValue && order.CustomerDeliveryDate.Value.Date < today,
                DaysUntilDue = daysUntilDue,
                MesStatusSummary = SummarizeWorkOrders(workOrders.Where(wo => wo.WebOrderNo == order.WebOrderNo).ToList()),
                WmsStatusSummary = SummarizeOutbounds(outbounds.Where(ob => ob.WebOrderNo == order.WebOrderNo).ToList()),
            };
        }).ToList();

        return items;
    }

    private IQueryable<Order> ApplySort(IQueryable<Order> orders, string? sortField, string? sortOrder)
    {
        var descending = string.Equals(sortOrder, "desc", StringComparison.OrdinalIgnoreCase);
        return (sortField ?? "deliveryDate").Trim().ToLowerInvariant() switch
        {
            "orderdate" => descending
                ? orders.OrderByDescending(o => o.OrderDate).ThenBy(o => o.WebOrderNo)
                : orders.OrderBy(o => o.OrderDate).ThenBy(o => o.WebOrderNo),
            "remainingqty" => descending
                ? orders.OrderByDescending(RemainingQtyExpression()).ThenBy(o => o.WebOrderNo)
                : orders.OrderBy(RemainingQtyExpression()).ThenBy(o => o.WebOrderNo),
            _ => descending
                ? orders.OrderByDescending(o => o.CustomerDeliveryDate).ThenBy(o => o.WebOrderNo)
                : orders.OrderBy(o => o.CustomerDeliveryDate).ThenBy(o => o.WebOrderNo),
        };
    }

    private Expression<Func<Order, decimal>> RemainingQtyExpression()
    {
        return order =>
            _db.OrderDetails
                .Where(d => d.WebOrderNo == order.WebOrderNo && !d.IsDeleted)
                .Sum(d => d.Quantity ?? 0m)
            - _db.OrderDetails
                .Where(d => d.WebOrderNo == order.WebOrderNo && !d.IsDeleted)
                .Sum(d => d.ShippedQty ?? 0m);
    }

    private static string SummarizeWorkOrders(List<WorkOrder> workOrders)
    {
        if (workOrders.Count == 0) return "No WO";

        var parts = workOrders
            .GroupBy(wo => wo.Status)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Count()} {WorkOrderStatusLabel(g.Key)}");

        return $"{workOrders.Count} WOs ({string.Join(", ", parts)})";
    }

    private static string SummarizeOutbounds(List<OutboundOrder> outbounds)
    {
        if (outbounds.Count == 0) return "No Outbound";

        var parts = outbounds
            .GroupBy(ob => ob.Status)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Count()} {OutboundOrderStatusLabel(g.Key)}");

        return $"{outbounds.Count} Outbounds ({string.Join(", ", parts)})";
    }

    private static string WorkOrderStatusLabel(int status) => status switch
    {
        WorkOrderStatus.Draft => "Draft",
        WorkOrderStatus.Confirmed => "Confirmed",
        WorkOrderStatus.Issued => "Issued",
        WorkOrderStatus.InProgress => "InProgress",
        WorkOrderStatus.Completed => "Completed",
        WorkOrderStatus.Interrupted => "Interrupted",
        WorkOrderStatus.Inspected => "Inspected",
        WorkOrderStatus.Cancelled => "Cancelled",
        _ => $"Status{status}",
    };

    private static string OutboundOrderStatusLabel(int status) => status switch
    {
        OutboundOrderStatus.Draft => "Draft",
        OutboundOrderStatus.Confirmed => "Confirmed",
        OutboundOrderStatus.Allocated => "Allocated",
        OutboundOrderStatus.Picking => "Picking",
        OutboundOrderStatus.Completed => "Completed",
        OutboundOrderStatus.Cancelled => "Cancelled",
        _ => $"Status{status}",
    };

    private static byte[] BuildCsv(List<UnshippedOrderItemDto> items)
    {
        var sb = new StringBuilder();
        AppendCsvRow(sb, new[]
        {
            "WebOrderNo",
            "CustomerCd",
            "CustomerName",
            "OrderDate",
            "CustomerDeliveryDate",
            "OrderStatus",
            "OrderedQty",
            "ShippedQty",
            "RemainingQty",
            "IsOverdue",
            "MesStatusSummary",
            "WmsStatusSummary",
        });

        foreach (var item in items)
        {
            AppendCsvRow(sb, new[]
            {
                item.WebOrderNo,
                item.CustomerCd,
                item.CustomerName ?? string.Empty,
                FormatDate(item.OrderDate),
                FormatDate(item.CustomerDeliveryDate),
                item.OrderStatus,
                FormatDecimal(item.OrderedQty),
                FormatDecimal(item.ShippedQty),
                FormatDecimal(item.RemainingQty),
                item.IsOverdue ? "true" : "false",
                item.MesStatusSummary ?? string.Empty,
                item.WmsStatusSummary ?? string.Empty,
            });
        }

        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var bytes = new byte[body.Length + 3];
        bytes[0] = 0xEF;
        bytes[1] = 0xBB;
        bytes[2] = 0xBF;
        Buffer.BlockCopy(body, 0, bytes, 3, body.Length);
        return bytes;
    }

    private static void AppendCsvRow(StringBuilder sb, IEnumerable<string> fields)
    {
        sb.Append(string.Join(",", fields.Select(EscapeCsv)));
        sb.Append("\r\n");
    }

    private static string EscapeCsv(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string FormatDate(DateTime? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FormatDecimal(decimal value) =>
        value.ToString("0.########", CultureInfo.InvariantCulture);
}
