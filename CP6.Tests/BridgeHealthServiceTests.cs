using CP6.Core.EFDbContext;
using CP6.Core.Services;
using CP6.Entity.DomainModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class BridgeHealthServiceTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    private static BridgeHealthService NewService(CP6Context db) => new(db);

    private static IntegrationEvent NewEvent(
        DateTime createDate,
        string status,
        string hookName = "OnOrderCreatedAsync",
        string sourceModule = "ERP",
        string targetModule = "MES",
        int attempts = 1,
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
            LastError = status == IntegrationEventStatus.DeadLetter ? "retry exhausted" : null,
            CorrelationId = Guid.NewGuid(),
            PayloadJson = "{}",
            CreateDate = createDate,
        };
    }

    [Fact]
    public async Task Metrics_GroupsByHook_Returns24hWindow()
    {
        await using var db = NewDb();
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        db.IntegrationEvents.AddRange(
            NewEvent(now.AddHours(-1), IntegrationEventStatus.Success),
            NewEvent(now.AddHours(-2), IntegrationEventStatus.Success),
            NewEvent(now.AddHours(-3), IntegrationEventStatus.Success),
            NewEvent(now.AddHours(-4), IntegrationEventStatus.Failed),
            NewEvent(now.AddHours(-5), IntegrationEventStatus.DeadLetter),
            NewEvent(now.AddHours(-1), IntegrationEventStatus.Success, "OnWorkOrderIssuedAsync", "MES", "WMS"),
            NewEvent(now.AddHours(-2), IntegrationEventStatus.Skipped, "OnWorkOrderIssuedAsync", "MES", "WMS"));
        await db.SaveChangesAsync();

        var metrics = await NewService(db).GetMetricsAsync(now);

        Assert.Equal(now.AddHours(-24), metrics.WindowStartUtc);
        Assert.Equal(now, metrics.WindowEndUtc);
        Assert.Equal(2, metrics.Hooks.Count);

        var hookA = Assert.Single(metrics.Hooks, h => h.HookName == "OnOrderCreatedAsync");
        Assert.Equal("ERP", hookA.SourceModule);
        Assert.Equal("MES", hookA.TargetModule);
        Assert.Equal(5, hookA.TotalCount);
        Assert.Equal(3, hookA.SuccessCount);
        Assert.Equal(1, hookA.FailedCount);
        Assert.Equal(1, hookA.DeadLetterCount);
        Assert.Equal(0.6m, hookA.SuccessRate);
    }

    [Fact]
    public async Task Metrics_EventsOlderThan24h_ExcludedFromCount()
    {
        await using var db = NewDb();
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        db.IntegrationEvents.AddRange(
            NewEvent(now.AddHours(-25), IntegrationEventStatus.Success),
            NewEvent(now.AddHours(-23), IntegrationEventStatus.Failed));
        await db.SaveChangesAsync();

        var metrics = await NewService(db).GetMetricsAsync(now);

        var hook = Assert.Single(metrics.Hooks);
        Assert.Equal(1, hook.TotalCount);
        Assert.Equal(0, hook.SuccessCount);
        Assert.Equal(1, hook.FailedCount);
    }

    [Fact]
    public async Task Metrics_QueueDepth_CountsFailedNotPending()
    {
        await using var db = NewDb();
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        db.IntegrationEvents.AddRange(
            NewEvent(now.AddMinutes(-1), IntegrationEventStatus.Failed),
            NewEvent(now.AddMinutes(-2), IntegrationEventStatus.Failed),
            NewEvent(now.AddMinutes(-3), IntegrationEventStatus.Failed),
            NewEvent(now.AddMinutes(-4), IntegrationEventStatus.Pending),
            NewEvent(now.AddMinutes(-5), IntegrationEventStatus.Success));
        await db.SaveChangesAsync();

        var metrics = await NewService(db).GetMetricsAsync(now);

        Assert.Equal(3, metrics.QueueDepth);
    }

    [Fact]
    public async Task Metrics_DeadLetters_LimitedTo10()
    {
        await using var db = NewDb();
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 15; i++)
        {
            db.IntegrationEvents.Add(NewEvent(
                now.AddMinutes(-i),
                IntegrationEventStatus.DeadLetter,
                attempts: i + 1,
                sourceNo: $"SRC-{i:00}"));
        }
        await db.SaveChangesAsync();

        var metrics = await NewService(db).GetMetricsAsync(now);

        Assert.Equal(15, metrics.DeadLetterCount);
        Assert.Equal(10, metrics.DeadLetters.Count);
        Assert.Equal("SRC-00", metrics.DeadLetters[0].SourceNo);
        Assert.Equal("SRC-09", metrics.DeadLetters[^1].SourceNo);
    }
}
