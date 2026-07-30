using CP6.Core.EFDbContext;
using CP6.Core.Services;
using CP6.Core.Services.Space.Observability;
using CP6.Entity.DomainModels;
using CP6.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    private static IntegrationEvent NewSpaceDeadEvent(
        Guid notificationLeaseId)
    {
        var evt = NewDeadEvent();
        evt.SourceModule = "SPACE";
        evt.HookName = "OnLocationPublishedAsync";
        evt.SourceNo = "LPUB-DLQ";
        evt.LastError = "SPACE_RETRY_DEAD_LETTER";
        evt.PayloadJson =
            """{"secret":"raw payload must not be logged"}""";
        evt.DeadLetterNotificationLeaseId =
            notificationLeaseId;
        evt.DeadLetterNotificationLeaseUntilUtc =
            DateTime.UtcNow.AddMinutes(5);
        return evt;
    }

    private static (
        DeadLetterNotifier Notifier,
        Mock<IClientProxy> ClientProxy)
        NewNotifier(
            CP6Context db,
            ILogger<DeadLetterNotifier>? logger = null)
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
            logger ??
                NullLogger<DeadLetterNotifier>.Instance),
            clientProxy);
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
        var evt = NewDeadEvent();
        evt.LastError =
            "Bearer legacy-secret with stack detail";
        object? payload = null;
        clientProxy
            .Setup(client => client.SendCoreAsync(
                "IntegrationDeadLetter",
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>(
                (_, args, _) => payload =
                    Assert.Single(args))
            .Returns(Task.CompletedTask);

        await notifier.NotifyAsync(evt);

        clientProxy.Verify(c => c.SendCoreAsync(
            "IntegrationDeadLetter",
            It.IsAny<object[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(
            evt.LastError,
            ReadPayloadLastError(payload));
    }

    [Fact]
    public async Task Space_notify_is_durable_idempotent_and_omits_payload()
    {
        await using var db = NewDb();
        var leaseId = Guid.NewGuid();
        var evt = NewSpaceDeadEvent(leaseId);
        evt.LastError =
            "System.InvalidOperationException: raw exception secret";
        db.IntegrationEvents.Add(evt);
        await db.SaveChangesAsync();
        var (notifier, clientProxy) = NewNotifier(db);
        object? payload = null;
        clientProxy
            .Setup(client => client.SendCoreAsync(
                "IntegrationDeadLetter",
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>(
                (_, args, _) => payload =
                    Assert.Single(args))
            .Returns(Task.CompletedTask);

        Assert.True(
            await notifier.TryNotifyDurablyAsync(
                evt,
                leaseId));
        Assert.True(
            await notifier.TryNotifyDurablyAsync(
                evt,
                leaseId));

        var log = await db.Sys_OperLogs.SingleAsync();
        Assert.True(log.IsAlert);
        Assert.Equal(
            $"/integration-event/{evt.Id}/dead-letter",
            log.RequestUrl);
        Assert.Contains(
            "SPACE_RETRY_DEAD_LETTER",
            log.RequestBody);
        Assert.DoesNotContain(
            "raw exception secret",
            log.RequestBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "raw payload must not be logged",
            log.RequestBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            evt.PayloadJson,
            log.RequestBody,
            StringComparison.Ordinal);
        Assert.Equal(
            "SPACE_RETRY_DEAD_LETTER",
            ReadPayloadLastError(payload));
    }

    [Fact]
    public async Task Space_notify_returns_false_on_log_failure_then_retries_cleanly()
    {
        var databaseName =
            $"space-notifier-{Guid.NewGuid():N}";
        var options =
            new DbContextOptionsBuilder<CP6Context>()
                .UseInMemoryDatabase(databaseName)
                .ConfigureWarnings(w => w.Ignore(
                    InMemoryEventId
                        .TransactionIgnoredWarning))
                .Options;
        var leaseId = Guid.NewGuid();
        IntegrationEvent evt;
        await using (var seed = new CP6Context(options))
        {
            evt = NewSpaceDeadEvent(leaseId);
            seed.IntegrationEvents.Add(evt);
            await seed.SaveChangesAsync();
        }

        await using (var failingDb =
                     new OperLogSaveFailingContext(options))
        {
            var pending = await failingDb.IntegrationEvents
                .AsNoTracking()
                .SingleAsync();
            var logger =
                new RecordingLogger<DeadLetterNotifier>();
            var (notifier, _) = NewNotifier(
                failingDb,
                logger);
            Assert.False(
                await notifier.TryNotifyDurablyAsync(
                    pending,
                    leaseId));
            var expected = SpaceErrorSanitizer.Classify(
                OperLogSaveFailingContext.Failure,
                "SPACE_DEAD_LETTER_OPERLOG_FAILED");
            var entry = Assert.Single(
                logger.Entries,
                value => value.Message.Contains(
                    expected.ReasonCode,
                    StringComparison.Ordinal));
            Assert.Null(entry.Exception);
            Assert.Contains(
                expected.ExceptionType,
                entry.Message);
            Assert.Contains(
                expected.Fingerprint,
                entry.Message);
            Assert.Contains(
                pending.Id.ToString(),
                entry.Message);
            Assert.DoesNotContain(
                "Bearer operlog-secret",
                entry.Message,
                StringComparison.Ordinal);
        }

        await using var retryDb = new CP6Context(options);
        var retryEvent = await retryDb.IntegrationEvents
            .AsNoTracking()
            .SingleAsync();
        var (retryNotifier, _) = NewNotifier(retryDb);
        Assert.True(
            await retryNotifier.TryNotifyDurablyAsync(
                retryEvent,
                leaseId));
        Assert.Single(await retryDb.Sys_OperLogs.ToListAsync());
    }

    [Fact]
    public async Task Space_signalr_failure_does_not_undo_durable_success()
    {
        await using var db = NewDb();
        var leaseId = Guid.NewGuid();
        var evt = NewSpaceDeadEvent(leaseId);
        db.IntegrationEvents.Add(evt);
        await db.SaveChangesAsync();
        var logger =
            new RecordingLogger<DeadLetterNotifier>();
        var (notifier, clientProxy) = NewNotifier(
            db,
            logger);
        var failure = new InvalidOperationException(
            "Bearer signalr-secret at Secret.Stack()");
        clientProxy
            .Setup(client => client.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        Assert.True(
            await notifier.TryNotifyDurablyAsync(
                evt,
                leaseId));
        Assert.Single(await db.Sys_OperLogs.ToListAsync());
        var expected = SpaceErrorSanitizer.Classify(
            failure,
            "SPACE_DEAD_LETTER_SIGNALR_FAILED");
        var entry = Assert.Single(
            logger.Entries,
            value => value.Message.Contains(
                expected.ReasonCode,
                StringComparison.Ordinal));
        Assert.Null(entry.Exception);
        Assert.Contains(expected.ExceptionType, entry.Message);
        Assert.Contains(expected.Fingerprint, entry.Message);
        Assert.Contains(evt.Id.ToString(), entry.Message);
        Assert.DoesNotContain(
            "Bearer signalr-secret",
            entry.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_space_signalr_failure_keeps_legacy_exception_logging()
    {
        await using var db = NewDb();
        var logger =
            new RecordingLogger<DeadLetterNotifier>();
        var (notifier, clientProxy) = NewNotifier(
            db,
            logger);
        var failure = new InvalidOperationException(
            "legacy SignalR diagnostic");
        clientProxy
            .Setup(client => client.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        await notifier.NotifyAsync(NewDeadEvent());

        var entry = Assert.Single(
            logger.Entries,
            value => ReferenceEquals(
                failure,
                value.Exception));
        Assert.Contains(
            "SignalR dead-letter push failed",
            entry.Message);
    }

    [Fact]
    public async Task Non_space_operlog_failure_keeps_legacy_exception_logging()
    {
        var options =
            new DbContextOptionsBuilder<CP6Context>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(
                    InMemoryEventId
                        .TransactionIgnoredWarning))
                .Options;
        await using var db =
            new OperLogSaveFailingContext(options);
        var logger =
            new RecordingLogger<DeadLetterNotifier>();
        var (notifier, _) = NewNotifier(db, logger);

        await notifier.NotifyAsync(NewDeadEvent());

        var entry = Assert.Single(
            logger.Entries,
            value => ReferenceEquals(
                OperLogSaveFailingContext.Failure,
                value.Exception));
        Assert.Contains(
            "OperLog dead-letter write failed",
            entry.Message);
    }

    private sealed class OperLogSaveFailingContext :
        CP6Context
    {
        public static readonly DbUpdateException Failure =
            new("Bearer operlog-secret at Secret.Stack()");

        public OperLogSaveFailingContext(
            DbContextOptions<CP6Context> options)
            : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            if (ChangeTracker.Entries<Sys_OperLog>()
                .Any(entry =>
                    entry.State == EntityState.Added))
            {
                throw Failure;
            }
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private static string? ReadPayloadLastError(
        object? payload) =>
        payload?.GetType()
            .GetProperty("LastError")?
            .GetValue(payload) as string;

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception);

    private sealed class RecordingLogger<T> :
        ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(
            TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) =>
            true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(
                logLevel,
                formatter(state, exception),
                exception));
        }
    }
}
