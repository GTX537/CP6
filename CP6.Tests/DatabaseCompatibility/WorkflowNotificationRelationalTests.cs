using System.Collections.Concurrent;
using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using CP6.WebApi.BackgroundServices;
using CP6.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace CP6.Tests.DatabaseCompatibility;

/// <summary>Actual migrated outbox and production dispatch batches; hub/email objects record the external delivery boundary.</summary>
[Collection(CoreBusinessRelationalCollection.Name)]
public sealed class WorkflowNotificationRelationalTests(CoreBusinessRelationalFixture fixture, ITestOutputHelper output)
{
    [CoreBusinessFact]
    public async Task Duplicate_event_key_creates_one_native_outbox_row_across_contexts()
    {
        var scope = await SeedAsync();
        try
        {
            await using (var db = fixture.CreateContext(scope.Tenant))
            {
                await CreateAsync(db, scope, email: true);
                await CreateAsync(db, scope, email: true);
                await db.SaveChangesAsync();
            }
            await using (var next = fixture.CreateContext(scope.Tenant))
            {
                await CreateAsync(next, scope, email: true);
                await next.SaveChangesAsync();
            }
            var row = await ReadAsync(scope);
            Assert.Equal(scope.EventKey, row.EventKey);
            Assert.Equal(scope.Recipient, row.UserId);
            Assert.Equal(0, row.DispatchStatus);
            Assert.Equal(0, row.DispatchAttempts);
            Assert.True(row.InAppRequested); Assert.True(row.EmailRequested);
        }
        finally { await RemoveOutboxAsync(scope); }
    }

    [CoreBusinessFact]
    public async Task Caller_transaction_rolls_back_native_business_change_and_outbox()
    {
        var scope = await SeedAsync();
        try
        {
            await using (var db = fixture.CreateContext(scope.Tenant))
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                (await db.Sys_Tenants.SingleAsync(row => row.Id == scope.Tenant)).Remark = "notification transaction mutation";
                await CreateAsync(db, scope, email: true);
                await db.SaveChangesAsync();
                Assert.Single(await db.Wf_Notifications.Where(row => row.EventKey == scope.EventKey).ToArrayAsync());
                await transaction.RollbackAsync();
            }
            await using var verify = fixture.CreateContext(scope.Tenant);
            Assert.False(await verify.Wf_Notifications.AnyAsync(row => row.EventKey == scope.EventKey));
            Assert.Null((await verify.Sys_Tenants.SingleAsync(row => row.Id == scope.Tenant)).Remark);
        }
        finally { await RemoveOutboxAsync(scope); }
    }

    [CoreBusinessFact]
    public async Task Actual_dispatch_targets_only_recipient_and_persists_delivery_without_repeat()
    {
        var scope = await SeedAsync();
        try
        {
            await EnqueueAsync(scope, email: true);
            await RequireIsolatedDueQueueAsync(scope);
            var hub = new RecordingHub();
            var email = new RecordingEmail();
            await using var services = Services(scope, hub, email);
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var worker = Worker(services);
            await worker.DispatchBatchAsync(budget.Token);
            var row = await ReadAsync(scope);
            AssertDelivered(row, attempts: 1);
            var delivery = Assert.Single(hub.Deliveries);
            Assert.Equal(scope.Recipient.ToString(), delivery.User);
            Assert.Equal(row.Id, delivery.Notification);
            Assert.Equal("WfNotification", delivery.Method);
            Assert.Equal(scope.RecipientAddress, Assert.Single(email.Recipients));
            Assert.Equal(0, hub.OtherRoutes);
            await worker.DispatchBatchAsync(budget.Token);
            Assert.Single(hub.Deliveries); Assert.Single(email.Recipients);
            AssertDelivered(await ReadAsync(scope), attempts: 1);
            output.WriteLine("Actual migrated notification worker persisted delivery; recording hub/email targeted only the recipient; no external transport acceptance.");
        }
        finally { await RemoveOutboxAsync(scope); }
    }

    [CoreBusinessFact]
    public async Task Two_actual_workers_selecting_same_due_row_publish_once()
    {
        var scope = await SeedAsync();
        var barrier = new CandidateBarrier();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var tasks = new List<Task>();
        var succeeded = false;
        try
        {
            await EnqueueAsync(scope, email: false);
            await RequireIsolatedDueQueueAsync(scope);
            var hub = new RecordingHub();
            var email = new RecordingEmail();
            // This lifetime surrounds the cleanup finally: all workers join before scoped contexts can be disposed.
            await using var services = Services(scope, hub, email, new CandidateInterceptor(barrier));
            try
            {
                tasks.Add(Worker(services).DispatchBatchAsync(budget.Token));
                tasks.Add(Worker(services).DispatchBatchAsync(budget.Token));
                await barrier.BothArrived.Task.WaitAsync(TimeSpan.FromSeconds(10), budget.Token);
                Assert.Equal(2, barrier.PhysicalConnections.Distinct(StringComparer.Ordinal).Count());
                Assert.Equal(2, barrier.Arrivals);
                Assert.Empty(hub.Deliveries);
                Assert.All(tasks, task => Assert.False(task.IsCompleted));
                barrier.Release();
                await Task.WhenAll(tasks);
                var row = await ReadAsync(scope);
                AssertDelivered(row, attempts: 1);
                var delivery = Assert.Single(hub.Deliveries);
                Assert.Equal(row.Id, delivery.Notification);
                Assert.Equal(scope.Recipient.ToString(), delivery.User);
                Assert.Empty(email.Recipients);
                Assert.Equal(0, hub.OtherRoutes);
                succeeded = true;
                output.WriteLine("Two physical connections completed the actual candidate SELECT before either claimed the only due owned row; one delivery and one persisted attempt.");
            }
            finally
            {
                barrier.Release();
                if (!succeeded) await budget.CancelAsync();
                try { await Task.WhenAll(tasks); }
                catch when (!succeeded) { /* All workers were joined; preserve the main failure. */ }
                Assert.All(tasks, task => Assert.True(task.IsCompleted));
            }
        }
        finally { await RemoveOutboxAsync(scope); }
    }

    [CoreBusinessFact]
    public async Task Failed_actual_send_persists_backoff_and_retries_only_after_due_time()
    {
        var scope = await SeedAsync();
        try
        {
            await EnqueueAsync(scope, email: false);
            await RequireIsolatedDueQueueAsync(scope);
            var hub = new RecordingHub(failFirst: true);
            var email = new RecordingEmail();
            await using var services = Services(scope, hub, email);
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            var worker = Worker(services);
            var before = DateTime.UtcNow;
            await worker.DispatchBatchAsync(budget.Token);
            var failed = await ReadAsync(scope);
            Assert.Equal(0, failed.DispatchStatus);
            Assert.Equal(1, failed.DispatchAttempts);
            Assert.Equal("dispatch-failed", failed.LastDispatchError);
            Assert.Null(failed.DispatchedAtUtc);
            Assert.NotNull(failed.NextAttemptAtUtc);
            Assert.InRange(failed.NextAttemptAtUtc.Value, before.AddSeconds(10), DateTime.UtcNow.AddSeconds(10));
            Assert.Equal(1, hub.SendCalls);
            Assert.Empty(hub.Deliveries);
            await worker.DispatchBatchAsync(budget.Token);
            Assert.Equal(1, hub.SendCalls);
            Assert.Equal(1, (await ReadAsync(scope)).DispatchAttempts);
            // Wait for the actual persisted backoff; do not rewrite the due time to manufacture eligibility.
            var delay = failed.NextAttemptAtUtc.Value - DateTime.UtcNow + TimeSpan.FromMilliseconds(100);
            Assert.InRange(delay, TimeSpan.Zero, TimeSpan.FromSeconds(15));
            await Task.Delay(delay, budget.Token);
            await worker.DispatchBatchAsync(budget.Token);
            var delivered = await ReadAsync(scope);
            AssertDelivered(delivered, attempts: 2);
            Assert.Equal(2, hub.SendCalls);
            Assert.Equal(scope.Recipient.ToString(), Assert.Single(hub.Deliveries).User);
            Assert.Empty(email.Recipients);
            Assert.Equal(0, hub.OtherRoutes);
            output.WriteLine("Native failure/backoff persisted; an immediate real batch did not send; a real batch after the persisted due time succeeded. Hub remains a recording boundary.");
        }
        finally { await RemoveOutboxAsync(scope); }
    }

    private async Task<Scope> SeedAsync()
    {
        var tenant = Guid.NewGuid();
        var recipient = Guid.NewGuid();
        var scope = new Scope(tenant, recipient, Guid.NewGuid(), "wp4-notification:" + Guid.NewGuid().ToString("N"),
            "wp4-" + recipient.ToString("N") + "@cp6.test", Guid.NewGuid());
        await using var db = fixture.CreateContext(tenant);
        db.Sys_Tenants.Add(new Sys_Tenant { Id = tenant, TenantCode = "wp4-notification-" + tenant.ToString("N"), TenantName = "WP4 notification fixture", Enable = true });
        db.Sys_Users.Add(new Sys_User { Id = recipient, UserName = "wp4-" + recipient.ToString("N"), Password = "fixture-only", Email = scope.RecipientAddress, Enable = true });
        db.Sys_Users.Add(new Sys_User { Id = scope.OtherUser, UserName = "wp4-" + scope.OtherUser.ToString("N"), Password = "fixture-only", Email = "other-" + scope.OtherUser.ToString("N") + "@cp6.test", Enable = true });
        db.Wf_FlowInstances.Add(new Wf_FlowInstance { Id = scope.Instance, FlowKey = "wp4-notify-" + Guid.NewGuid().ToString("N"), CurrentNode = "approval", StarterId = recipient, Status = 0 });
        await db.SaveChangesAsync();
        output.WriteLine(fixture.SetupSummary);
        return scope;
    }

    private static Task CreateAsync(CP6Context db, Scope scope, bool email) => new NotificationService(db).CreateOutboxAsync(
        scope.Recipient, WfNotificationType.TodoCreated, "WP4 native notification", "Fixture delivery boundary",
        scope.Instance, null, "wp4-notification", scope.EventKey, inAppRequested: true, emailRequested: email);

    private async Task EnqueueAsync(Scope scope, bool email)
    {
        await using var db = fixture.CreateContext(scope.Tenant);
        await CreateAsync(db, scope, email);
        await db.SaveChangesAsync();
    }

    private async Task<Wf_Notification> ReadAsync(Scope scope)
    {
        await using var db = fixture.CreateContext(scope.Tenant);
        return await db.Wf_Notifications.AsNoTracking().SingleAsync(row => row.EventKey == scope.EventKey);
    }

    private async Task RequireIsolatedDueQueueAsync(Scope scope)
    {
        await using var db = fixture.CreateContext(scope.Tenant);
        var now = DateTime.UtcNow;
        var due = await db.Wf_Notifications.IgnoreQueryFilters().AsNoTracking().Where(row =>
            (row.DispatchStatus == 0 || row.DispatchStatus == 2) && (row.NextAttemptAtUtc == null || row.NextAttemptAtUtc <= now))
            .Select(row => new { row.TenantId, row.EventKey }).ToArrayAsync();
        var owned = Assert.Single(due);
        Assert.Equal(scope.Tenant, owned.TenantId);
        Assert.Equal(scope.EventKey, owned.EventKey);
    }

    private async Task RemoveOutboxAsync(Scope scope)
    {
        await using var db = fixture.CreateContext(scope.Tenant);
        var owned = await db.Wf_Notifications.Where(row => row.TenantId == scope.Tenant && row.EventKey == scope.EventKey).ToArrayAsync();
        db.RemoveRange(owned);
        await db.SaveChangesAsync();
        Assert.False(await db.Wf_Notifications.AnyAsync(row => row.TenantId == scope.Tenant && row.EventKey == scope.EventKey));
    }

    private ServiceProvider Services(Scope scope, RecordingHub hub, RecordingEmail email, params IInterceptor[] interceptors) => new ServiceCollection()
        .AddScoped<CP6Context>(_ =>
        {
            var db = fixture.CreateContext(scope.Tenant, interceptors);
            db.Database.SetCommandTimeout(20);
            return db;
        })
        .AddSingleton<IHubContext<NotifyHub>>(hub).AddSingleton<IEmailSender>(email).BuildServiceProvider(validateScopes: true);

    private static WfNotificationDispatchWorker Worker(ServiceProvider services) => new(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<WfNotificationDispatchWorker>.Instance);
    private static void AssertDelivered(Wf_Notification row, int attempts)
    {
        Assert.Equal(1, row.DispatchStatus); Assert.Equal(attempts, row.DispatchAttempts);
        Assert.NotNull(row.DispatchedAtUtc); Assert.Null(row.NextAttemptAtUtc); Assert.Null(row.LastDispatchError);
    }

    private sealed record Scope(Guid Tenant, Guid Recipient, Guid OtherUser, string EventKey, string RecipientAddress, Guid Instance);
    private sealed record HubDelivery(string User, string Method, Guid Notification);
    private sealed class RecordingEmail : IEmailSender
    {
        public ConcurrentQueue<string> Recipients { get; } = new();
        public Task SendAsync(string to, string subject, string body) { Recipients.Enqueue(to); return Task.CompletedTask; }
    }
    private sealed class RecordingHub(bool failFirst = false) : IHubContext<NotifyHub>
    {
        private readonly bool shouldFailFirst = failFirst;
        private int calls, routes;
        public ConcurrentQueue<HubDelivery> Deliveries { get; } = new();
        public int SendCalls => Volatile.Read(ref calls);
        public int OtherRoutes => Volatile.Read(ref routes);
        public IHubClients Clients => new RecordingClients(this);
        public IGroupManager Groups => throw new InvalidOperationException("Fixture permits only recipient User routing.");
        private sealed class RecordingClients(RecordingHub owner) : IHubClients
        {
            private IClientProxy Other() { Interlocked.Increment(ref owner.routes); throw new InvalidOperationException("Non-recipient routing was requested."); }
            public IClientProxy User(string userId) => new RecordingProxy(owner, userId);
            public IClientProxy All => Other();
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Other();
            public IClientProxy Client(string connectionId) => Other();
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Other();
            public IClientProxy Group(string groupName) => Other();
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Other();
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => Other();
            public IClientProxy Users(IReadOnlyList<string> userIds) => Other();
        }
        private sealed class RecordingProxy(RecordingHub owner, string user) : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var call = Interlocked.Increment(ref owner.calls);
                if (owner.shouldFailFirst && call == 1) throw new InvalidOperationException("fixture first send failure");
                var payload = Assert.Single(args)!;
                var id = (Guid)(payload.GetType().GetProperty("notificationId")?.GetValue(payload)
                    ?? throw new InvalidOperationException("Actual dispatch notificationId is missing."));
                owner.Deliveries.Enqueue(new(user, method, id));
                return Task.CompletedTask;
            }
        }
    }

    private sealed class CandidateBarrier
    {
        private int arrivals;
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource BothArrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<string> PhysicalConnections { get; } = new();
        public int Arrivals => Volatile.Read(ref arrivals);
        public void Release() => release.TrySetResult();
        public async Task ArriveAsync(DbConnection connection, CancellationToken token)
        {
            PhysicalConnections.Enqueue(connection switch
            {
                Microsoft.Data.SqlClient.SqlConnection sql => "SqlServer:" + sql.ClientConnectionId,
                Npgsql.NpgsqlConnection pg => "PostgreSql:" + pg.ProcessID,
                _ => throw new InvalidOperationException("A native provider connection is required.")
            });
            var count = Interlocked.Increment(ref arrivals);
            Assert.InRange(count, 1, 2);
            if (count == 2) BothArrived.TrySetResult();
            await release.Task.WaitAsync(token);
        }
    }
    private sealed class CandidateInterceptor(CandidateBarrier barrier) : DbCommandInterceptor
    {
        private readonly ConcurrentDictionary<Guid, byte> seen = new();
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data,
            DbDataReader result, CancellationToken token = default)
        {
            if (command.CommandText.Contains("Wf_Notification", StringComparison.Ordinal)
                && command.CommandText.Contains("DispatchStatus", StringComparison.Ordinal)
                && command.CommandText.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase)
                && data.Context is { } context && seen.TryAdd(context.ContextId.InstanceId, 0))
            {
                Assert.True(result.HasRows);
                await barrier.ArriveAsync(command.Connection!, token);
            }
            return result;
        }
    }
}
