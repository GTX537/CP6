using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public class OrderTraceService : IOrderTraceService
{
    private const string BridgeHookKind = "BRIDGE_HOOK";

    private readonly CP6Context _db;

    public OrderTraceService(CP6Context db)
    {
        _db = db;
    }

    public async Task<OrderTraceDto?> GetAsync(string webOrderNo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(webOrderNo))
        {
            return null;
        }

        var order = await _db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.WebOrderNo == webOrderNo && !x.IsDeleted, ct);
        if (order == null)
        {
            return null;
        }

        var workOrderNos = await _db.WorkOrders
            .AsNoTracking()
            .Where(x => x.WebOrderNo == webOrderNo && !x.IsDeleted)
            .Select(x => x.WorkOrderNo)
            .ToListAsync(ct);

        var outboundNos = await _db.OutboundOrders
            .AsNoTracking()
            .Where(x => x.WebOrderNo == webOrderNo && !x.IsDeleted)
            .Select(x => x.OutboundNo)
            .ToListAsync(ct);

        var sourceNos = new[] { webOrderNo }
            .Concat(workOrderNos)
            .Concat(outboundNos)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var events = await _db.IntegrationEvents
            .AsNoTracking()
            .Where(x => !x.IsDeleted && sourceNos.Contains(x.SourceNo))
            .OrderBy(x => x.CreateDate)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

        var customerName = await _db.BusinessPartners
            .AsNoTracking()
            .Where(x => x.BpCd == order.CustomerCd && !x.IsDeleted)
            .Select(x => x.BpName)
            .FirstOrDefaultAsync(ct);

        return new OrderTraceDto
        {
            WebOrderNo = order.WebOrderNo,
            CustomerName = customerName ?? order.CustomerCd,
            OrderDate = order.OrderDate,
            Summary = BuildSummary(events),
            Timeline = events.Select(ToTimelineItem).ToList(),
        };
    }

    private static OrderTraceSummaryDto BuildSummary(IReadOnlyCollection<IntegrationEvent> events)
    {
        return new OrderTraceSummaryDto
        {
            TotalEvents = events.Count,
            SuccessCount = events.Count(x => x.Status == IntegrationEventStatus.Success),
            FailedCount = events.Count(x => x.Status == IntegrationEventStatus.Failed),
            SkippedCount = events.Count(x => x.Status == IntegrationEventStatus.Skipped),
            DeadCount = events.Count(x => x.Status == IntegrationEventStatus.DeadLetter),
            DistinctCorrelationIds = events
                .Where(x => x.CorrelationId != Guid.Empty)
                .Select(x => x.CorrelationId)
                .Distinct()
                .Count(),
        };
    }

    private static OrderTraceTimelineItemDto ToTimelineItem(IntegrationEvent evt)
    {
        return new OrderTraceTimelineItemDto
        {
            EventTime = evt.CreateDate,
            EventKind = BridgeHookKind,
            SourceModule = evt.SourceModule,
            TargetModule = evt.TargetModule,
            HookName = evt.HookName,
            SourceNo = evt.SourceNo,
            TargetNo = evt.TargetNo,
            Status = evt.Status,
            Message = evt.LastError,
            CorrelationId = evt.CorrelationId,
        };
    }
}
