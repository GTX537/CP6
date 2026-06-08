using CP6.Core.EFDbContext;
using CP6.Core.Services;
using CP6.Entity.DomainModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class BridgeHealth_AggregationE2ETests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    [Fact]
    public async Task GetMetrics_Aggregates24hWindow_GroupsByHook_ComputesSuccessRate()
    {
        await using var db = NewDb();
        var now = new DateTime(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);

        db.IntegrationEvents.AddRange(
            NewEvent(now.AddHours(-1), IntegrationEventStatus.Success, "OnOrderCreatedAsync", "ERP", "MES", sourceNo: "ORD-SUCCESS-1"),
            NewEvent(now.AddHours(-2), IntegrationEventStatus.Success, "OnOrderCreatedAsync", "ERP", "MES", sourceNo: "ORD-SUCCESS-2"),
            NewEvent(now.AddHours(-3), IntegrationEventStatus.Success, "OnOrderCreatedAsync", "ERP", "MES", sourceNo: "ORD-SUCCESS-3"),
            NewEvent(now.AddHours(-4), IntegrationEventStatus.Failed, "OnOrderCreatedAsync", "ERP", "MES", attempts: 2, lastError: "timeout", sourceNo: "ORD-FAILED"),
            NewEvent(now.AddHours(-5), IntegrationEventStatus.DeadLetter, "OnOrderCreatedAsync", "ERP", "MES", attempts: 5, lastError: "retry exhausted", sourceNo: "ORD-DEAD"),
            NewEvent(now.AddHours(-1), IntegrationEventStatus.Success, "OnWorkOrderIssuedAsync", "MES", "WMS", sourceNo: "WO-SUCCESS-1"),
            NewEvent(now.AddHours(-2), IntegrationEventStatus.Success, "OnWorkOrderIssuedAsync", "MES", "WMS", sourceNo: "WO-SUCCESS-2"),
            NewEvent(now.AddHours(-3), IntegrationEventStatus.Success, "OnWorkOrderIssuedAsync", "MES", "WMS", sourceNo: "WO-SUCCESS-3"),
            NewEvent(now.AddHours(-25), IntegrationEventStatus.Success, "OnOrderCreatedAsync", "ERP", "MES", sourceNo: "ORD-OLD-SUCCESS"),
            NewEvent(now.AddHours(-25), IntegrationEventStatus.Skipped, "OnWorkOrderIssuedAsync", "MES", "WMS", sourceNo: "WO-OLD-SKIP"));
        await db.SaveChangesAsync();

        var metrics = await new BridgeHealthService(db).GetMetricsAsync(now);

        Assert.Equal(now.AddHours(-24), metrics.WindowStartUtc);
        Assert.Equal(now, metrics.WindowEndUtc);
        Assert.Equal(2, metrics.Hooks.Count);

        var orderHook = Assert.Single(metrics.Hooks, h => h.HookName == "OnOrderCreatedAsync");
        Assert.Equal("ERP", orderHook.SourceModule);
        Assert.Equal("MES", orderHook.TargetModule);
        Assert.Equal(5, orderHook.TotalCount);
        Assert.Equal(3, orderHook.SuccessCount);
        Assert.Equal(1, orderHook.FailedCount);
        Assert.Equal(1, orderHook.DeadLetterCount);
        Assert.Equal(0.6m, orderHook.SuccessRate);

        var workOrderHook = Assert.Single(metrics.Hooks, h => h.HookName == "OnWorkOrderIssuedAsync");
        Assert.Equal("MES", workOrderHook.SourceModule);
        Assert.Equal("WMS", workOrderHook.TargetModule);
        Assert.Equal(3, workOrderHook.TotalCount);
        Assert.Equal(3, workOrderHook.SuccessCount);
        Assert.Equal(0, workOrderHook.FailedCount);
        Assert.Equal(0, workOrderHook.DeadLetterCount);
        Assert.Equal(1.0m, workOrderHook.SuccessRate);

        Assert.Equal(1, metrics.QueueDepth);
        Assert.Equal(1, metrics.DeadLetterCount);

        var deadLetter = Assert.Single(metrics.DeadLetters);
        Assert.Equal("OnOrderCreatedAsync", deadLetter.HookName);
        Assert.Equal("ORD-DEAD", deadLetter.SourceNo);
        Assert.Equal(5, deadLetter.Attempts);
        Assert.Contains("retry exhausted", deadLetter.LastError);
    }

    private static IntegrationEvent NewEvent(
        DateTime createDate,
        string status,
        string hookName,
        string sourceModule,
        string targetModule,
        int attempts = 1,
        string? lastError = null,
        string sourceNo = "SRC-001")
    {
        return new IntegrationEvent
        {
            Id = Guid.NewGuid(),
            SourceModule = sourceModule,
            TargetModule = targetModule,
            HookName = hookName,
            SourceNo = sourceNo,
            Status = status,
            Attempts = attempts,
            LastError = lastError,
            CorrelationId = Guid.NewGuid(),
            PayloadJson = "{}",
            CreateDate = createDate,
        };
    }
}
