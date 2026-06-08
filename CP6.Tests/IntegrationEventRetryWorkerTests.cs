using CP6.Core.EFDbContext;
using CP6.Core.Options;
using CP6.Core.Services;
using CP6.Entity.DomainModels;
using CP6.WebApi.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CP6.Tests;

/// <summary>
/// IntegrationEvent retry worker state transition tests.
/// </summary>
public class IntegrationEventRetryWorkerTests
{
    private static DbContextOptions<CP6Context> NewOptions()
    {
        return new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private static async Task<CP6Context> SeedAsync(
        DbContextOptions<CP6Context> options,
        IntegrationEvent evt)
    {
        var db = new CP6Context(options);
        db.IntegrationEvents.Add(evt);
        await db.SaveChangesAsync();
        return db;
    }

    private static IntegrationEvent NewFailedEvent(int attempts, DateTime? nextRetryAt)
    {
        return new IntegrationEvent
        {
            Id = Guid.NewGuid(),
            SourceModule = "MES",
            TargetModule = "WMS",
            HookName = "OnWorkOrderIssuedAsync",
            SourceNo = "WO-001",
            Status = IntegrationEventStatus.Failed,
            Attempts = attempts,
            NextRetryAt = nextRetryAt,
            CorrelationId = Guid.NewGuid(),
            PayloadJson = """{"workOrderNo":"WO-001","userName":"worker"}""",
            Creator = "test",
            CreateDate = DateTime.Now,
        };
    }

    private static ServiceProvider BuildProvider(
        DbContextOptions<CP6Context> options,
        Mock<IIntegrationEventDispatcher> dispatcher,
        Mock<IDeadLetterNotifier> notifier)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new CP6Context(options));
        services.AddScoped(_ => dispatcher.Object);
        services.AddScoped(_ => notifier.Object);
        return services.BuildServiceProvider();
    }

    private static async Task RunWorkerBrieflyAsync(
        ServiceProvider provider,
        IntegrationEventOptions options)
    {
        var worker = new IntegrationEventRetryWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<IntegrationEventRetryWorker>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await worker.StartAsync(cts.Token);
        await Task.Delay(200, CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Worker_RetriesDueEvents_AndMarksSuccess()
    {
        var options = NewOptions();
        await using var seedDb = await SeedAsync(options, NewFailedEvent(1, DateTime.UtcNow.AddSeconds(-5)));
        var dispatcher = new Mock<IIntegrationEventDispatcher>();
        var notifier = new Mock<IDeadLetterNotifier>();
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        await using var provider = BuildProvider(options, dispatcher, notifier);

        await RunWorkerBrieflyAsync(provider, new IntegrationEventOptions { PollIntervalSeconds = 60 });

        await using var assertDb = new CP6Context(options);
        var evt = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.Success, evt.Status);
        Assert.Equal(2, evt.Attempts);
        Assert.Null(evt.NextRetryAt);
    }

    [Fact]
    public async Task Worker_AfterMaxAttempts_TransitionsToDeadLetter()
    {
        var options = NewOptions();
        await using var seedDb = await SeedAsync(options, NewFailedEvent(4, DateTime.UtcNow.AddSeconds(-5)));
        var dispatcher = new Mock<IIntegrationEventDispatcher>();
        var notifier = new Mock<IDeadLetterNotifier>();
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        await using var provider = BuildProvider(options, dispatcher, notifier);

        await RunWorkerBrieflyAsync(provider, new IntegrationEventOptions { MaxAttempts = 5, PollIntervalSeconds = 60 });

        await using var assertDb = new CP6Context(options);
        var evt = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.DeadLetter, evt.Status);
        Assert.Equal(5, evt.Attempts);
        Assert.Null(evt.NextRetryAt);
        notifier.Verify(n => n.NotifyAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Worker_DisabledViaOptions_DoesNothing()
    {
        var options = NewOptions();
        await using var seedDb = await SeedAsync(options, NewFailedEvent(1, DateTime.UtcNow.AddSeconds(-5)));
        var dispatcher = new Mock<IIntegrationEventDispatcher>();
        var notifier = new Mock<IDeadLetterNotifier>();
        await using var provider = BuildProvider(options, dispatcher, notifier);

        await RunWorkerBrieflyAsync(provider, new IntegrationEventOptions { Enabled = false, PollIntervalSeconds = 1 });

        await using var assertDb = new CP6Context(options);
        var evt = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.Failed, evt.Status);
        Assert.Equal(1, evt.Attempts);
        dispatcher.Verify(d => d.DispatchAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Worker_SkipsEventsNotYetDue()
    {
        var options = NewOptions();
        await using var seedDb = await SeedAsync(options, NewFailedEvent(1, DateTime.UtcNow.AddMinutes(10)));
        var dispatcher = new Mock<IIntegrationEventDispatcher>();
        var notifier = new Mock<IDeadLetterNotifier>();
        await using var provider = BuildProvider(options, dispatcher, notifier);

        await RunWorkerBrieflyAsync(provider, new IntegrationEventOptions { PollIntervalSeconds = 60 });

        await using var assertDb = new CP6Context(options);
        var evt = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.Failed, evt.Status);
        Assert.Equal(1, evt.Attempts);
        dispatcher.Verify(d => d.DispatchAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Worker_BackoffIncreasesAttemptByAttempt()
    {
        var options = NewOptions();
        var evtId = Guid.NewGuid();
        await using (var seedDb = new CP6Context(options))
        {
            var evt = NewFailedEvent(0, DateTime.UtcNow.AddSeconds(-5));
            evt.Id = evtId;
            seedDb.IntegrationEvents.Add(evt);
            await seedDb.SaveChangesAsync();
        }
        var dispatcher = new Mock<IIntegrationEventDispatcher>();
        var notifier = new Mock<IDeadLetterNotifier>();
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var retryOptions = new IntegrationEventOptions
        {
            MaxAttempts = 5,
            BackoffSeconds = new[] { 10, 20 },
            PollIntervalSeconds = 60,
        };

        await using (var provider = BuildProvider(options, dispatcher, notifier))
        {
            var beforeFirst = DateTime.UtcNow;
            await RunWorkerBrieflyAsync(provider, retryOptions);
            await using var assertDb = new CP6Context(options);
            var evt = await assertDb.IntegrationEvents.SingleAsync(e => e.Id == evtId);
            Assert.Equal(1, evt.Attempts);
            Assert.InRange(evt.NextRetryAt!.Value, beforeFirst.AddSeconds(9), DateTime.UtcNow.AddSeconds(12));
            evt.NextRetryAt = DateTime.UtcNow.AddSeconds(-1);
            await assertDb.SaveChangesAsync();
        }

        await using (var provider = BuildProvider(options, dispatcher, notifier))
        {
            var beforeSecond = DateTime.UtcNow;
            await RunWorkerBrieflyAsync(provider, retryOptions);
            await using var assertDb = new CP6Context(options);
            var evt = await assertDb.IntegrationEvents.SingleAsync(e => e.Id == evtId);
            Assert.Equal(2, evt.Attempts);
            Assert.InRange(evt.NextRetryAt!.Value, beforeSecond.AddSeconds(19), DateTime.UtcNow.AddSeconds(22));
        }
    }
}
