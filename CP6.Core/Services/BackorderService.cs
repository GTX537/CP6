using System.Reflection;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services;

public class BackorderService : IBackorderService
{
    private static readonly string[] OpenOrderStatuses =
    [
        OrderLifecycleStatus.Confirmed,
        OrderLifecycleStatus.InProduction,
        OrderLifecycleStatus.PartiallyCancelled,
    ];

    private static readonly HashSet<string> DetailCloneExclusions = new(StringComparer.Ordinal)
    {
        nameof(OrderDetail.Id),
        nameof(OrderDetail.RowVersion),
        nameof(OrderDetail.WebOrderNo),
        nameof(OrderDetail.WebOrderDetailNo),
        nameof(OrderDetail.Quantity),
        nameof(OrderDetail.ShippedQty),
        nameof(OrderDetail.BackorderQty),
        nameof(OrderDetail.ShipStatus),
        nameof(OrderDetail.LastShipDate),
        nameof(OrderDetail.LastOutboundNo),
        nameof(OrderDetail.ReturnedQty),
        nameof(OrderDetail.Status),
        nameof(OrderDetail.McTransferFlg),
        nameof(OrderDetail.Creator),
        nameof(OrderDetail.CreateDate),
        nameof(OrderDetail.Modifier),
        nameof(OrderDetail.ModifyDate),
        nameof(OrderDetail.IsDeleted),
        nameof(OrderDetail.Order),
        nameof(OrderDetail.Processes),
        nameof(OrderDetail.ProcessNotes),
        nameof(OrderDetail.Materials),
    };

    private readonly CP6Context _db;

    public BackorderService(CP6Context db)
    {
        _db = db;
    }

    public async Task<List<BackorderQueueItemDto>> GetQueueAsync(BackorderQueueQuery query, CancellationToken ct = default)
    {
        query ??= new BackorderQueueQuery();

        var rows =
            from d in _db.OrderDetails.AsNoTracking()
            join o in _db.Orders.AsNoTracking() on d.WebOrderNo equals o.WebOrderNo
            where !d.IsDeleted
                && !o.IsDeleted
                && OpenOrderStatuses.Contains(o.OrderStatus)
            select new
            {
                Order = o,
                Detail = d,
                RemainingQty = (d.Quantity ?? 0m) - (d.ShippedQty ?? 0m) - (d.BackorderQty ?? 0m),
            };

        rows = rows.Where(x => x.RemainingQty > 0m);

        if (!string.IsNullOrWhiteSpace(query.CustomerCd))
        {
            var customerCd = query.CustomerCd.Trim();
            rows = rows.Where(x => x.Order.CustomerCd == customerCd);
        }

        if (query.DateFrom.HasValue)
        {
            var from = query.DateFrom.Value.Date;
            rows = rows.Where(x => x.Detail.LastShipDate >= from);
        }

        if (query.DateTo.HasValue)
        {
            var toExclusive = query.DateTo.Value.Date.AddDays(1);
            rows = rows.Where(x => x.Detail.LastShipDate < toExclusive);
        }

        var materialized = await rows
            .OrderBy(x => x.Detail.LastShipDate)
            .ThenBy(x => x.Order.WebOrderNo)
            .ThenBy(x => x.Detail.WebOrderDetailNo)
            .ToListAsync(ct);

        var customerNames = await LoadCustomerNamesAsync(
            materialized.Select(x => x.Order.CustomerCd).Distinct().ToList(),
            ct);

        return materialized.Select(x => new BackorderQueueItemDto
        {
            WebOrderNo = x.Order.WebOrderNo,
            CustomerCd = x.Order.CustomerCd,
            CustomerName = customerNames.TryGetValue(x.Order.CustomerCd, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name
                : x.Order.CustomerCd,
            DetailNo = x.Detail.WebOrderDetailNo,
            ProductCd = x.Detail.ProductCd,
            OrderedQty = x.Detail.Quantity ?? 0m,
            ShippedQty = x.Detail.ShippedQty ?? 0m,
            BackorderQty = x.Detail.BackorderQty ?? 0m,
            RemainingQty = x.RemainingQty,
            LastShipDate = x.Detail.LastShipDate,
        }).ToList();
    }

    public async Task<BackorderActionResultDto> CloseRemainingAsync(
        string webOrderNo,
        int detailNo,
        BackorderActionRequest request,
        string? userName = null,
        CancellationToken ct = default)
    {
        ValidateReason(request);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var (_, detail, remainingQty) = await LoadOpenDetailAsync(webOrderNo, detailNo, ct);
        MarkParentDetailClosed(detail, remainingQty, request.Reason!, userName);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new BackorderActionResultDto
        {
            WebOrderNo = detail.WebOrderNo,
            DetailNo = detail.WebOrderDetailNo,
            BackorderQty = remainingQty,
            RemainingQty = 0m,
        };
    }

    public async Task<BackorderActionResultDto> SplitToNewOrderAsync(
        string webOrderNo,
        int detailNo,
        BackorderActionRequest request,
        string? userName = null,
        CancellationToken ct = default)
    {
        ValidateReason(request);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var (order, detail, remainingQty) = await LoadOpenDetailAsync(webOrderNo, detailNo, ct);
        var newWebOrderNo = (await DocNumber.NextAsync(_db, "ORD")).No;
        var now = DateTime.Now;

        _db.Orders.Add(CloneOrderForBackorder(order, detail, newWebOrderNo, remainingQty, request.Reason!, userName, now));
        _db.OrderDetails.Add(CloneDetailForBackorder(detail, newWebOrderNo, remainingQty, userName, now));
        MarkParentDetailClosed(detail, remainingQty, request.Reason!, userName);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new BackorderActionResultDto
        {
            WebOrderNo = detail.WebOrderNo,
            DetailNo = detail.WebOrderDetailNo,
            BackorderQty = remainingQty,
            RemainingQty = 0m,
            NewWebOrderNo = newWebOrderNo,
        };
    }

    private async Task<(Order Order, OrderDetail Detail, decimal RemainingQty)> LoadOpenDetailAsync(
        string webOrderNo,
        int detailNo,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(webOrderNo))
        {
            throw new KeyNotFoundException("WebOrderNo is required.");
        }

        var order = await _db.Orders
            .FirstOrDefaultAsync(x => x.WebOrderNo == webOrderNo && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException($"Order '{webOrderNo}' was not found.");

        if (!OpenOrderStatuses.Contains(order.OrderStatus))
        {
            throw new InvalidOperationException("Order status is not eligible for backorder handling.");
        }

        var detail = await _db.OrderDetails
            .FirstOrDefaultAsync(x => x.WebOrderNo == webOrderNo && x.WebOrderDetailNo == detailNo && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException($"Order detail '{webOrderNo}/{detailNo}' was not found.");

        if (detail.BackorderQty.GetValueOrDefault() > 0m)
        {
            throw new InvalidOperationException("Order detail is already closed for backorder.");
        }

        var remainingQty = CalculateRemainingQty(detail);
        if (remainingQty <= 0m)
        {
            throw new InvalidOperationException("Order detail has no remaining quantity.");
        }

        return (order, detail, remainingQty);
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

    private static decimal CalculateRemainingQty(OrderDetail detail)
    {
        return (detail.Quantity ?? 0m) - (detail.ShippedQty ?? 0m) - (detail.BackorderQty ?? 0m);
    }

    private static void ValidateReason(BackorderActionRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new InvalidOperationException("Reason is required.");
        }
    }

    private static void MarkParentDetailClosed(OrderDetail detail, decimal remainingQty, string reason, string? userName)
    {
        detail.BackorderQty = remainingQty;
        detail.ShippedQty = detail.Quantity;
        detail.ShipStatus = 9;
        detail.SlipNote = AppendNote(detail.SlipNote, $"BO: {reason}");
        detail.Modifier = userName;
        detail.ModifyDate = DateTime.Now;
    }

    private static Order CloneOrderForBackorder(
        Order source,
        OrderDetail sourceDetail,
        string newWebOrderNo,
        decimal remainingQty,
        string reason,
        string? userName,
        DateTime now)
    {
        return new Order
        {
            WebOrderNo = newWebOrderNo,
            CustomerCd = source.CustomerCd,
            OrderType = source.OrderType,
            OrderDepartment = source.OrderDepartment,
            OrderDate = DateTime.Today,
            CustomerDeliveryDate = sourceDetail.CustomerDeliveryDate ?? source.CustomerDeliveryDate,
            Quantity = remainingQty,
            OrderSheetNo = source.OrderSheetNo,
            CustomerContact = source.CustomerContact,
            Addressee = source.Addressee,
            Carrier = source.Carrier,
            ShipDateTime = source.ShipDateTime,
            ShipCondition = source.ShipCondition,
            SalesPriceDiv = source.SalesPriceDiv,
            Status = 0,
            McTransferFlg = false,
            ShipStatus = 0,
            ActualShipDate = null,
            OrderStatus = OrderLifecycleStatus.Confirmed,
            Memo1 = $"BO from {source.WebOrderNo}",
            Memo2 = source.Memo2,
            Memo3 = Truncate(reason, 100),
            Creator = userName,
            CreateDate = now,
        };
    }

    private static OrderDetail CloneDetailForBackorder(
        OrderDetail source,
        string newWebOrderNo,
        decimal remainingQty,
        string? userName,
        DateTime now)
    {
        var clone = new OrderDetail();
        foreach (var property in typeof(OrderDetail).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite || DetailCloneExclusions.Contains(property.Name))
            {
                continue;
            }

            property.SetValue(clone, property.GetValue(source));
        }

        clone.WebOrderNo = newWebOrderNo;
        clone.WebOrderDetailNo = 1;
        clone.Quantity = remainingQty;
        clone.ShippedQty = 0m;
        clone.BackorderQty = null;
        clone.ShipStatus = 0;
        clone.LastShipDate = null;
        clone.LastOutboundNo = null;
        clone.ReturnedQty = null;
        clone.Status = 0;
        clone.McTransferFlg = false;
        clone.Creator = userName;
        clone.CreateDate = now;
        clone.Modifier = null;
        clone.ModifyDate = null;
        clone.IsDeleted = false;

        return clone;
    }

    private static string? AppendNote(string? current, string note)
    {
        if (string.IsNullOrWhiteSpace(current))
        {
            return Truncate(note, 100);
        }

        return Truncate($"{current}; {note}", 100);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
