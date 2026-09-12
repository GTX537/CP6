using System.Collections.Concurrent;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.ErpIntegration;
using CP6.Core.Services.Integration;
using CP6.WebApi.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.ErpIntegration.SqlTests;

/// <summary>
/// Real worker dispatch, SQL locking and durable retry evidence using controlled hook doubles.
/// These cases do not establish real WMS/MES business processing or external transport acceptance.
/// </summary>
[Collection(SqlDatabaseCollection.Name)]
public sealed class BridgeWorkerSqlTests(SqlDatabaseFixture database)
{
    [Fact]
    public async Task Worker_calls_each_hook_once_in_event_tenant_scope_after_order_commit()
    {
        var committed = await CommittedOrderAsync();
        var s = committed.Scenario;
        var probe = new HookProbe();
        await using var provider = Provider(probe);
        using var worker = Worker(provider, s);
        // The same key under another configured tenant must not dispatch the original row.
        await worker.DispatchOneAsync(s.OtherTenant, committed.Key, CancellationToken.None);
        Assert.Empty(probe.Observations);
        Assert.Equal(0, (await DispatchAsync(s)).AttemptCount);
        await worker.DispatchOneAsync(s.Tenant, committed.Key, CancellationToken.None);
        await worker.DispatchOneAsync(s.Tenant, committed.Key, CancellationToken.None);
        AssertObservations(committed, probe, wms: 1, mes: 1);
        Assert.Single(probe.Observations.Select(x => x.ScopeId).Distinct());
        var dispatch = await DispatchAsync(s);
        Assert.Equal(1, dispatch.AttemptCount);
        Assert.Equal(s.Clock.GetUtcNow(), dispatch.CompletedAtUtc);
        Assert.Null(dispatch.LastErrorCode);
        await using var unrelatedScope = provider.CreateAsyncScope();
        Assert.Equal(TenantContext.DefaultTenant,
            unrelatedScope.ServiceProvider.GetRequiredService<ITenantContext>().CurrentTenantId);
        await AssertOriginalOrderAsync(committed);
    }

    [Fact]
    public async Task Concurrent_workers_hold_sql_lock_across_hooks_and_do_not_dispatch_twice()
    {
        var committed = await CommittedOrderAsync();
        var s = committed.Scenario;
        var probe = new HookProbe { BlockWms = true };
        await using var provider = Provider(probe);
        using var firstWorker = Worker(provider, s);
        using var secondWorker = Worker(provider, s);
        var first = firstWorker.DispatchOneAsync(s.Tenant, committed.Key, CancellationToken.None);
        Task? second = null;
        try
        {
            await probe.WmsEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            // Advancing beyond a normal lease must not let another worker enter the held SQL lock.
            s.Clock.Advance(TimeSpan.FromMinutes(6));
            second = secondWorker.DispatchOneAsync(s.Tenant, committed.Key, CancellationToken.None);
            Assert.NotSame(second, await Task.WhenAny(second, Task.Delay(200)));
            Assert.Equal(1, probe.Observations.Count(x => x.Hook == "wms"));
            Assert.Empty(probe.Observations.Where(x => x.Hook == "mes"));
        }
        finally
        {
            probe.ReleaseWms.TrySetResult();
            if (second is null) await first.WaitAsync(TimeSpan.FromSeconds(15));
            else await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(15));
        }
        AssertObservations(committed, probe, wms: 1, mes: 1);
        var dispatch = await DispatchAsync(s);
        Assert.Equal(1, dispatch.AttemptCount);
        Assert.NotNull(dispatch.CompletedAtUtc);
        await AssertOriginalOrderAsync(committed);
    }

    [Theory]
    [InlineData("wms-throws")]
    [InlineData("mes-fails")]
    public async Task Ten_real_worker_failures_exhaust_then_admin_replay_completes_same_original_order(string failure)
    {
        var committed = await CommittedOrderAsync();
        var s = committed.Scenario;
        var probe = new HookProbe { Failure = failure };
        await using var provider = Provider(probe);
        using var worker = Worker(provider, s);
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            await worker.DispatchOneAsync(s.Tenant, committed.Key, CancellationToken.None);
            var dispatch = await DispatchAsync(s);
            Assert.Equal(attempt, dispatch.AttemptCount);
            Assert.Null(dispatch.CompletedAtUtc);
            Assert.Equal(attempt == 10 ? "C03_BRIDGE_REPLAY_REQUIRED" : "C03_BRIDGE_UNAVAILABLE", dispatch.LastErrorCode);
            var delay = dispatch.AvailableAtUtc - s.Clock.GetUtcNow();
            Assert.True(delay > TimeSpan.Zero && delay <= TimeSpan.FromMinutes(5));
            var observed = probe.Observations.Count;
            await worker.DispatchOneAsync(s.Tenant, committed.Key, CancellationToken.None);
            Assert.Equal(observed, probe.Observations.Count);
            Assert.Equal(attempt, (await DispatchAsync(s)).AttemptCount);
            s.Clock.Advance(delay);
        }
        var exhausted = await DispatchAsync(s);
        await worker.DispatchOneAsync(s.Tenant, committed.Key, CancellationToken.None);
        Assert.Equal(exhausted.RowVersion, (await DispatchAsync(s)).RowVersion);
        AssertObservations(committed, probe, wms: 10, mes: failure == "wms-throws" ? 0 : 10);
        await AssertOriginalOrderAsync(committed);

        probe.Failure = null;
        var input = new ErpReplayRequest(Guid.NewGuid(), exhausted.RowVersion.ToArray(),
            ErpEventContracts.Hash(Encoding.UTF8.GetBytes($"c03:bridge:{s.Tenant:D}:{committed.Key}")), "dependency-recovered");
        var replay = new ErpDeliveryReplayService(database, s.Runtime);
        var scheduled = await replay.ScheduleBridgeAsync(s.Tenant, committed.Key, input, "erp-bridge-operator");
        Assert.Equal(committed.Key, scheduled.TargetId);
        Assert.Equal(0, (await DispatchAsync(s)).AttemptCount);
        await worker.DispatchOneAsync(s.Tenant, committed.Key, CancellationToken.None);
        await worker.DispatchOneAsync(s.Tenant, committed.Key, CancellationToken.None);
        var completed = await DispatchAsync(s);
        Assert.Equal(1, completed.AttemptCount);
        Assert.Equal(s.Clock.GetUtcNow(), completed.CompletedAtUtc);
        Assert.Null(completed.LastErrorCode);
        AssertObservations(committed, probe, wms: 11, mes: failure == "wms-throws" ? 1 : 11);
        await using (var queue = s.Queue())
        {
            var audit = await queue.Set<ErpDeliveryReplayAudit>().SingleAsync(x => x.TenantId == s.Tenant);
            Assert.Equal(input.OperationId, audit.OperationId);
            Assert.Equal("bridge", audit.Kind);
            Assert.Equal(committed.Key, audit.TargetId);
            Assert.Equal(10, audit.PreviousAttemptCount);
            Assert.Equal(exhausted.RowVersion, audit.InputRowVersion);
        }
        await AssertOriginalOrderAsync(committed);
    }

    private ServiceProvider Provider(HookProbe probe)
    {
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddScoped<ITenantContext, TenantContext>();
        // Resolved by the actual worker after assigning the event tenant to its new scope.
        services.AddScoped(sp => database.CreateBusinessContext(sp.GetRequiredService<ITenantContext>().CurrentTenantId));
        services.AddScoped(sp => new ControlledHooks(sp.GetRequiredService<CP6Context>(),
            sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<HookProbe>()));
        services.AddScoped<IWmsBridgeHook>(sp => sp.GetRequiredService<ControlledHooks>());
        services.AddScoped<IMesBridgeHook>(sp => sp.GetRequiredService<ControlledHooks>());
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private ErpOrderBridgeWorker Worker(ServiceProvider provider, ErpScenario s) =>
        new(database, provider.GetRequiredService<IServiceScopeFactory>(), s.Runtime, NullLogger<ErpOrderBridgeWorker>.Instance);

    private async Task<CommittedOrder> CommittedOrderAsync()
    {
        var s = new ErpScenario(database);
        await s.InitializeAsync();
        var request = await s.ReadyOrderAsync();
        await s.ConsumeAsync(s.Envelope(request));
        Assert.Equal(0, s.ExternalCalls.Count);
        await using var db = s.Db();
        var order = await db.Orders.SingleAsync();
        var sequence = await db.DocSequences.Where(x => x.FuncCode == "ORD").Select(x => x.LastSeq).SingleAsync();
        return new(s, order.WebOrderNo, request.RequestId, ErpScenario.Version(order), sequence);
    }

    private static async Task<ErpOrderBridgeDispatch> DispatchAsync(ErpScenario s)
    {
        await using var queue = s.Queue();
        return await queue.OrderBridges.AsNoTracking().SingleAsync(x => x.TenantId == s.Tenant);
    }

    private static async Task AssertOriginalOrderAsync(CommittedOrder committed)
    {
        var s = committed.Scenario;
        await using var db = s.Db();
        var order = await db.Orders.SingleAsync();
        Assert.Equal(committed.Key, order.WebOrderNo);
        Assert.Equal(committed.RequestId, order.CrmRequestId);
        Assert.Equal(committed.RowVersion, ErpScenario.Version(order));
        Assert.Equal(300m, (await db.OrderDetails.SingleAsync()).Amount);
        Assert.Equal(committed.Sequence, await db.DocSequences.Where(x => x.FuncCode == "ORD").Select(x => x.LastSeq).SingleAsync());
        Assert.Single(await s.ResultsAsync(ErpEventContracts.OrderCreated));
        await DispatchAsync(s);
        Assert.Equal(0, s.ExternalCalls.Count);
    }

    private static void AssertObservations(CommittedOrder committed, HookProbe probe, int wms, int mes)
    {
        Assert.Empty(probe.ReadErrors);
        Assert.Equal(wms, probe.Observations.Count(x => x.Hook == "wms"));
        Assert.Equal(mes, probe.Observations.Count(x => x.Hook == "mes"));
        Assert.All(probe.Observations, observed =>
        {
            Assert.Equal(committed.Scenario.Tenant, observed.ScopeTenant);
            Assert.Equal(committed.Scenario.Tenant, observed.ContextTenant);
            Assert.Equal(committed.Scenario.Tenant, observed.OrderTenant);
            Assert.Equal(committed.Key, observed.Key);
            Assert.Equal(committed.RequestId, observed.RequestId);
            Assert.Equal(300m, observed.Amount);
            Assert.Equal("crm-integration", observed.Actor);
            Assert.False(observed.HasTransaction);
        });
    }

    private sealed record CommittedOrder(ErpScenario Scenario, string Key, Guid RequestId, byte[] RowVersion, int Sequence);
    private sealed record HookObservation(string Hook, Guid ScopeId, Guid ScopeTenant, Guid ContextTenant,
        Guid OrderTenant, string Key, Guid? RequestId, decimal? Amount, string? Actor, bool HasTransaction);

    private sealed class HookProbe
    {
        public ConcurrentQueue<HookObservation> Observations { get; } = new();
        public ConcurrentQueue<string> ReadErrors { get; } = new();
        public string? Failure { get; set; }
        public bool BlockWms { get; init; }
        public TaskCompletionSource WmsEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseWms { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ControlledHooks(CP6Context db, ITenantContext tenant, HookProbe probe) : IWmsBridgeHook, IMesBridgeHook
    {
        private readonly Guid scopeId = Guid.NewGuid();

        public async Task<WmsBridgeResult> OnOrderCreatedAsync(string key, string? actor)
        {
            await ObserveAsync("wms", key, actor);
            probe.WmsEntered.TrySetResult();
            if (probe.BlockWms) await probe.ReleaseWms.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if (probe.Failure == "wms-throws") throw new TimeoutException("C03 controlled WMS hook unavailable");
            return WmsBridgeResult.Ok("controlled-wms-observation");
        }

        async Task<MesBridgeResult> IMesBridgeHook.OnOrderCreatedAsync(string key, string? actor)
        {
            await ObserveAsync("mes", key, actor);
            return probe.Failure == "mes-fails"
                ? MesBridgeResult.Failed("C03 controlled MES hook unavailable")
                : MesBridgeResult.Ok(["controlled-mes-observation"]);
        }

        private async Task ObserveAsync(string hook, string key, string? actor)
        {
            try
            {
                // This separately opened scoped connection can read only committed ERP order data.
                // Record observations for assertions outside the worker, which intentionally catches hook errors.
                var order = await db.Orders.AsNoTracking().SingleAsync(x => x.WebOrderNo == key);
                var detail = await db.OrderDetails.AsNoTracking().SingleAsync(x => x.WebOrderNo == key);
                probe.Observations.Enqueue(new(hook, scopeId, tenant.CurrentTenantId, db.CurrentTenantId,
                    order.TenantId, order.WebOrderNo, order.CrmRequestId, detail.Amount, actor,
                    db.Database.CurrentTransaction is not null));
            }
            catch (Exception ex)
            {
                probe.ReadErrors.Enqueue(ex.GetType().Name);
                throw;
            }
        }

        public Task<WmsBridgeResult> OnWorkOrderIssuedAsync(string key, string? actor) =>
            throw new InvalidOperationException("Unexpected work-order hook during order dispatch");
        public Task<WmsBridgeResult> OnProductionCompletedAsync(string key, decimal quantity, string? actor) =>
            throw new InvalidOperationException("Unexpected production hook during order dispatch");
    }
}
