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
/// Phase 6 IntegrationEvent retry/dead-letter E2E tests.
/// </summary>
public class IntegrationEventRetryDeadLetterE2ETests
{
    private static DbContextOptions<CP6Context> NewOptions()
    {
        return new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private static IntegrationEvent NewFailedEvent()
    {
        return new IntegrationEvent
        {
            Id = Guid.NewGuid(),
            SourceModule = "ERP",
            TargetModule = "MES",
            HookName = "OnOrderCreatedAsync",
            SourceNo = "WEB-RETRY-001",
            Status = IntegrationEventStatus.Failed,
            Attempts = 0,
            NextRetryAt = DateTime.UtcNow.AddSeconds(-1),
            CorrelationId = Guid.NewGuid(),
            PayloadJson = """{"webOrderNo":"WEB-RETRY-001","userName":"u"}""",
            Creator = "test",
            CreateDate = DateTime.Now,
        };
    }

    private static ServiceProvider BuildProvider(
        DbContextOptions<CP6Context> options,
        Mock<IIntegrationEventDispatcher> dispatcher)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new CP6Context(options));
        services.AddScoped(_ => dispatcher.Object);
        services.AddScoped<IDeadLetterNotifier>(sp => new DeadLetterNotifier(
            sp,
            sp.GetRequiredService<CP6Context>(),
            NullLogger<DeadLetterNotifier>.Instance));
        // 章10 后台按租户循环：TenantScopeRunner 解析这两个服务（空 Sys_Tenants → 回退默认租户跑一遍）
        services.AddScoped<CP6.Core.Services.Common.ITenantContext, CP6.Core.Services.Common.TenantContext>();
        services.AddScoped<CP6.Core.Services.Common.ITenantEnumerator, CP6.Core.Services.Common.TenantEnumerator>();
        return services.BuildServiceProvider();
    }

    private static IntegrationEventRetryWorker NewWorker(ServiceProvider provider)
    {
        return new IntegrationEventRetryWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new IntegrationEventOptions
            {
                Enabled = true,
                MaxAttempts = 3,
                BackoffSeconds = new[] { 1, 2, 3 },
                PollIntervalSeconds = 60,
            }),
            NullLogger<IntegrationEventRetryWorker>.Instance);
    }

    private static async Task SeedAsync(DbContextOptions<CP6Context> options, IntegrationEvent evt)
    {
        await using var db = new CP6Context(options);
        db.IntegrationEvents.Add(evt);
        await db.SaveChangesAsync();
    }

    private static async Task RewindAsync(DbContextOptions<CP6Context> options)
    {
        await using var db = new CP6Context(options);
        var evt = await db.IntegrationEvents.SingleAsync();
        if (evt.Status == IntegrationEventStatus.Failed)
        {
            evt.NextRetryAt = DateTime.UtcNow.AddSeconds(-1);
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task IntegrationEventRetry_ExhaustedAttempts_DeadLettersAndWritesAlertLog()
    {
        var options = NewOptions();
        await SeedAsync(options, NewFailedEvent());

        var dispatcher = new Mock<IIntegrationEventDispatcher>();
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("network down"));

        await using var provider = BuildProvider(options, dispatcher);
        var worker = NewWorker(provider);

        for (var i = 0; i < 4; i++)
        {
            await RewindAsync(options);
            await worker.ProcessOnceAsync(CancellationToken.None);
        }

        await using var assertDb = new CP6Context(options);
        var evt = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(3, evt.Attempts);
        Assert.Equal(IntegrationEventStatus.DeadLetter, evt.Status);
        Assert.Null(evt.NextRetryAt);
        Assert.Contains("network down", evt.LastError ?? "");

        var log = await assertDb.Sys_OperLogs.SingleAsync(l =>
            l.IsAlert
            && l.HttpMethod == "BACKGROUND"
            && l.Action == "OnOrderCreatedAsync");
        Assert.Equal(500, log.StatusCode);
        Assert.Contains("WEB-RETRY-001", log.RequestBody ?? "");
    }

    [Fact]
    public async Task IntegrationEventRetry_SecondAttemptSucceeds_MarksSuccessWithoutAlertLog()
    {
        var options = NewOptions();
        await SeedAsync(options, NewFailedEvent());

        var dispatcher = new Mock<IIntegrationEventDispatcher>();
        dispatcher.SetupSequence(d => d.DispatchAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("network down"))
            .ReturnsAsync(true);

        await using var provider = BuildProvider(options, dispatcher);
        var worker = NewWorker(provider);

        await worker.ProcessOnceAsync(CancellationToken.None);
        await RewindAsync(options);
        await worker.ProcessOnceAsync(CancellationToken.None);

        await using var assertDb = new CP6Context(options);
        var evt = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(2, evt.Attempts);
        Assert.Equal(IntegrationEventStatus.Success, evt.Status);
        Assert.Null(evt.NextRetryAt);
        Assert.False(await assertDb.Sys_OperLogs.AnyAsync(l => l.IsAlert));
    }
}
