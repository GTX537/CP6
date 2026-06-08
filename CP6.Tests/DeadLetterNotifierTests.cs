using CP6.Core.EFDbContext;
using CP6.Core.Services;
using CP6.Entity.DomainModels;
using CP6.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CP6.Tests;

/// <summary>
/// DeadLetter notification side effect tests.
/// </summary>
public class DeadLetterNotifierTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    private static IntegrationEvent NewDeadEvent()
    {
        return new IntegrationEvent
        {
            Id = Guid.NewGuid(),
            SourceModule = "MES",
            TargetModule = "WMS",
            HookName = "OnWorkOrderIssuedAsync",
            SourceNo = "WO-DLQ",
            Status = IntegrationEventStatus.DeadLetter,
            Attempts = 5,
            LastError = "retry exhausted",
            CorrelationId = Guid.NewGuid(),
            PayloadJson = "{}",
        };
    }

    private static (DeadLetterNotifier Notifier, Mock<IClientProxy> ClientProxy) NewNotifier(CP6Context db)
    {
        var clientProxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        var hub = new Mock<IHubContext<WmsHub>>();
        clients.Setup(c => c.All).Returns(clientProxy.Object);
        hub.Setup(h => h.Clients).Returns(clients.Object);

        var services = new ServiceCollection();
        services.AddSingleton(hub.Object);
        var provider = services.BuildServiceProvider();

        return (new DeadLetterNotifier(
            provider,
            db,
            NullLogger<DeadLetterNotifier>.Instance), clientProxy);
    }

    [Fact]
    public async Task Notify_WritesOperLogWithIsAlertTrue()
    {
        await using var db = NewDb();
        var (notifier, _) = NewNotifier(db);

        await notifier.NotifyAsync(NewDeadEvent());

        var log = await db.Sys_OperLogs.SingleAsync();
        Assert.True(log.IsAlert);
        Assert.Equal("BACKGROUND", log.HttpMethod);
        Assert.Equal(500, log.StatusCode);
        Assert.Equal("system", log.UserName);
        Assert.Contains("retry exhausted", log.RequestBody);
    }

    [Fact]
    public async Task Notify_PushesSignalRMessage()
    {
        await using var db = NewDb();
        var (notifier, clientProxy) = NewNotifier(db);

        await notifier.NotifyAsync(NewDeadEvent());

        clientProxy.Verify(c => c.SendCoreAsync(
            "IntegrationDeadLetter",
            It.IsAny<object[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
