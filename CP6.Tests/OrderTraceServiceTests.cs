using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs;
using CP6.WebApi.Controllers;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace CP6.Tests;

public class OrderTraceServiceTests
{
    private const string WebOrderNo = "WEB-TRACE-001";

    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    [Fact]
    public async Task Trace_AggregatesEventsByOrderRelatedWoOutbound()
    {
        await using var db = NewDb();
        var chain = Guid.NewGuid();
        SeedOrder(db);
        db.WorkOrders.AddRange(
            NewWorkOrder("WO-TRACE-001"),
            NewWorkOrder("WO-TRACE-002"));
        db.OutboundOrders.Add(NewOutbound("OUT-TRACE-001"));
        db.IntegrationEvents.AddRange(
            NewEvent(DateTime.UtcNow.AddMinutes(3), WebOrderNo, IntegrationEventStatus.Success, chain),
            NewEvent(DateTime.UtcNow.AddMinutes(1), WebOrderNo, IntegrationEventStatus.Pending, chain),
            NewEvent(DateTime.UtcNow.AddMinutes(5), WebOrderNo, IntegrationEventStatus.Skipped, chain),
            NewEvent(DateTime.UtcNow.AddMinutes(2), "WO-TRACE-002", IntegrationEventStatus.Success, chain),
            NewEvent(DateTime.UtcNow.AddMinutes(4), "OUT-TRACE-001", IntegrationEventStatus.Failed, chain),
            NewEvent(DateTime.UtcNow.AddMinutes(6), "UNRELATED", IntegrationEventStatus.DeadLetter, chain));
        await db.SaveChangesAsync();

        var result = await new OrderTraceService(db).GetAsync(WebOrderNo);

        Assert.NotNull(result);
        Assert.Equal(WebOrderNo, result.WebOrderNo);
        Assert.Equal(5, result.Timeline.Count);
        Assert.Equal(
            result.Timeline.OrderBy(x => x.EventTime).Select(x => x.SourceNo),
            result.Timeline.Select(x => x.SourceNo));
        Assert.Contains(result.Timeline, x => x.SourceNo == "WO-TRACE-002");
        Assert.Contains(result.Timeline, x => x.SourceNo == "OUT-TRACE-001");
        Assert.DoesNotContain(result.Timeline, x => x.SourceNo == "UNRELATED");
    }

    [Fact]
    public async Task Trace_OrderNotFound_Returns404()
    {
        var service = new Mock<IOrderTraceService>();
        service.Setup(x => x.GetAsync("WEB-MISSING", It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderTraceDto?)null);
        var controller = new OrderTraceController(service.Object);

        var result = await controller.Get("WEB-MISSING", CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var json = JsonSerializer.Serialize(notFound.Value);
        Assert.Contains("\"code\":1", json);
    }

    [Fact]
    public async Task Trace_SummaryStats_Correct()
    {
        await using var db = NewDb();
        SeedOrder(db);
        var chain = Guid.NewGuid();
        db.IntegrationEvents.AddRange(
            NewEvent(DateTime.UtcNow.AddMinutes(1), WebOrderNo, IntegrationEventStatus.Success, chain),
            NewEvent(DateTime.UtcNow.AddMinutes(2), WebOrderNo, IntegrationEventStatus.Success, chain),
            NewEvent(DateTime.UtcNow.AddMinutes(3), WebOrderNo, IntegrationEventStatus.Failed, chain),
            NewEvent(DateTime.UtcNow.AddMinutes(4), WebOrderNo, IntegrationEventStatus.Skipped, chain),
            NewEvent(DateTime.UtcNow.AddMinutes(5), WebOrderNo, IntegrationEventStatus.DeadLetter, chain));
        await db.SaveChangesAsync();

        var result = await new OrderTraceService(db).GetAsync(WebOrderNo);

        Assert.NotNull(result);
        Assert.Equal(5, result.Summary.TotalEvents);
        Assert.Equal(2, result.Summary.SuccessCount);
        Assert.Equal(1, result.Summary.FailedCount);
        Assert.Equal(1, result.Summary.SkippedCount);
        Assert.Equal(1, result.Summary.DeadCount);
    }

    [Fact]
    public async Task Trace_DistinctCorrelationIds_Counted()
    {
        await using var db = NewDb();
        SeedOrder(db);
        var chainA = Guid.NewGuid();
        var chainB = Guid.NewGuid();
        db.IntegrationEvents.AddRange(
            NewEvent(DateTime.UtcNow.AddMinutes(1), WebOrderNo, IntegrationEventStatus.Success, chainA),
            NewEvent(DateTime.UtcNow.AddMinutes(2), WebOrderNo, IntegrationEventStatus.Success, chainA),
            NewEvent(DateTime.UtcNow.AddMinutes(3), WebOrderNo, IntegrationEventStatus.Success, chainA),
            NewEvent(DateTime.UtcNow.AddMinutes(4), WebOrderNo, IntegrationEventStatus.Failed, chainB),
            NewEvent(DateTime.UtcNow.AddMinutes(5), WebOrderNo, IntegrationEventStatus.Skipped, chainB));
        await db.SaveChangesAsync();

        var result = await new OrderTraceService(db).GetAsync(WebOrderNo);

        Assert.NotNull(result);
        Assert.Equal(2, result.Summary.DistinctCorrelationIds);
    }

    private static void SeedOrder(CP6Context db)
    {
        db.BusinessPartners.Add(new BusinessPartner
        {
            BpCd = "C-TRACE",
            BpName = "Trace Customer",
            BaseCd = "B01",
            CustomerFlg = true,
        });
        db.Orders.Add(new Order
        {
            WebOrderNo = WebOrderNo,
            CustomerCd = "C-TRACE",
            OrderType = "01",
            OrderDate = new DateTime(2026, 6, 6),
            Status = 1,
        });
    }

    private static WorkOrder NewWorkOrder(string workOrderNo) => new()
    {
        WorkOrderNo = workOrderNo,
        WebOrderNo = WebOrderNo,
        ProductCd = "PROD-TRACE",
        ProductionQty = 10m,
        Status = WorkOrderStatus.Issued,
    };

    private static OutboundOrder NewOutbound(string outboundNo) => new()
    {
        OutboundNo = outboundNo,
        OutboundType = OutboundType.Shipping,
        WebOrderNo = WebOrderNo,
        WarehouseCd = "W01",
        Status = OutboundOrderStatus.Completed,
    };

    private static IntegrationEvent NewEvent(
        DateTime createDate,
        string sourceNo,
        string status,
        Guid correlationId)
    {
        return new IntegrationEvent
        {
            Id = Guid.NewGuid(),
            SourceModule = "ERP",
            TargetModule = "MES",
            HookName = "OnOrderCreatedAsync",
            SourceNo = sourceNo,
            TargetNo = sourceNo == WebOrderNo ? "WO-TRACE-001" : null,
            Status = status,
            LastError = status is IntegrationEventStatus.Failed or IntegrationEventStatus.DeadLetter ? "bridge failure" : null,
            CorrelationId = correlationId,
            PayloadJson = "{}",
            CreateDate = createDate,
        };
    }
}
