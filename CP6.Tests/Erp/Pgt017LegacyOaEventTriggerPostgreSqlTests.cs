using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Options;
using CP6.Core.Services;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Integration;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using CP6.Tests.Wf;
using CP6.WebApi.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string OaFixtureEventKey = "QA|OnEchoAsync";

    [TenantManagementPostgresTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PGT017_legacy_OA_event_trigger_orders_pause_for_http_and_retry(bool replay, bool pauseFirst)
    {
        await using var h = await Seed();
        await using var administrator = await TenantManagementSeed();
        var trigger = await SeedOaEventTrigger(h);
        const string eventId = "fixture-event-pause";
        const string payload = """{"OutboundNo":"OB-29"}""";
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var manager = await Login(host, administrator);
        var actor = await Login(host, h);
        var before = await ManualEffects(h, trigger);
        using var read = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(h.Tenant)));
        var snapshot = await NumberHttpData<TenantManagementSnapshotV1>(read);
        var pauseCommand = new TenantManagementCommandV1("event-trigger-pause", snapshot.Revision, "suspend", null);
        await using var services = OaEventRetryProvider(h.Tenant);
        var worker = new IntegrationEventRetryWorker(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new IntegrationEventOptions { Enabled = true, MaxAttempts = 3 }),
            NullLogger<IntegrationEventRetryWorker>.Instance);

        var due = DateTime.UtcNow.AddMinutes(-1);
        if (replay)
        {
            h.Db.IntegrationEvents.Add(new()
            {
                SourceModule = "QA", TargetModule = "WF", HookName = nameof(IWfTriggerBridgeHook.OnEventAsync),
                SourceNo = eventId, Status = IntegrationEventStatus.Failed, Attempts = 1,
                NextRetryAt = due, CorrelationId = Guid.NewGuid(), Creator = "fixture-owner", CreateDate = DateTime.Now,
                PayloadJson = JsonSerializer.Serialize(new WfTriggerEventPayload(OaFixtureEventKey, eventId, payload, "fixture-owner")),
            });
            await h.Db.SaveChangesAsync();
            h.Db.ChangeTracker.Clear();
        }
        var beforeRetry = await ReadEvent();
        var originalAuthority = await ReadPauseAuthority(h, administrator.User);
        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> pause;
        Task<HttpStatusCode?> action;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            var blocker = pauseFirst ? """LOCK TABLE public."Sys_Tenants" IN SHARE MODE"""
                : """LOCK TABLE public."Wf_FlowInstance" IN SHARE MODE""";
            await using var block = new NpgsqlCommand(blocker, barrier, hold);
            await block.ExecuteNonQueryAsync();
            if (pauseFirst)
            {
                pause = Pause();
                var authority = await WaitForFence(observer, barrier.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                action = Start();
                var pending = await WaitForFence(observer, authority.Backend, "%cp6_authentication_v1.acquire()%", null, action);
                output.WriteLine($"Pause backend {authority.Backend}; event backend {pending.Backend}; {pending.WaitEvent}");
            }
            else
            {
                action = Start();
                var writer = await WaitForFence(observer, barrier.ProcessID, "%INSERT%Wf_FlowInstance%", null, action);
                pause = Pause();
                var waiting = await WaitForFence(observer, writer.Backend, "%cp6_authentication_v1.acquire()%", null, pause);
                output.WriteLine($"Event backend {writer.Backend}; pause backend {waiting.Backend}; {waiting.WaitEvent}");
            }
            Assert.Equal(before, await ManualEffects(h, trigger));
            Assert.Equal(beforeRetry, await ReadEvent());
            Assert.Equal(originalAuthority, await ReadPauseAuthority(h, administrator.User));
            await hold.RollbackAsync();
        }

        using var paused = await pause;
        Assert.True(paused.StatusCode == HttpStatusCode.OK, await OperationsFailure(paused));
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(paused);
        CheckPause(originalAuthority, await ReadPauseAuthority(h, administrator.User), receipt, pauseCommand);
        Assert.Equal(replay ? null : pauseFirst ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, await action);
        if (pauseFirst)
        {
            Assert.Equal(before, await ManualEffects(h, trigger));
            Assert.Equal(beforeRetry, await ReadEvent());
            var resume = new TenantManagementCommandV1("event-trigger-resume", receipt.Snapshot.Revision, "reactivate", null);
            using var resumed = await host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), resume));
            Assert.True(resumed.StatusCode == HttpStatusCode.OK, await OperationsFailure(resumed));
            if (!replay) actor = await Login(host, h);
            Assert.Equal(replay ? null : HttpStatusCode.OK, await Start());
        }

        var committed = await ManualEffects(h, trigger);
        Assert.Equal((1, 1, 1, 1, 1), (committed.Fires, committed.Instances, committed.Tasks, committed.Submissions, committed.ActiveTokens));
        Assert.NotNull(committed.Watermark);
        var row = await h.Db.Wf_TriggerFires.AsNoTracking().SingleAsync(f => f.TriggerId == trigger);
        Assert.Equal($"{eventId}:{trigger}", row.IdempotencyKey);
        Assert.NotNull(row.InstanceId);
        var vars = await h.Db.Wf_FlowInstances.Where(x => x.Id == row.InstanceId).Select(x => x.VarsJson).SingleAsync();
        using var json = JsonDocument.Parse(vars!);
        Assert.Equal("OB-29", json.RootElement.GetProperty("orderNo").GetString());
        var saved = await ReadEvent();
        Assert.NotNull(saved);
        Assert.Equal((IntegrationEventStatus.Success, replay ? 2 : 1, null), (saved.Status, saved.Attempts, saved.NextRetryAt));
        if (replay)
        {
            Assert.Equal(beforeRetry!.Id, saved.Id);
            Assert.Equal(beforeRetry.Payload, saved.Payload);
            await worker.ProcessOnceAsync();
            Assert.Equal(saved, await ReadEvent());
        }
        Assert.Equal(committed, await ManualEffects(h, trigger));

        Task<HttpResponseMessage> Pause() =>
            host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), pauseCommand));
        async Task<HttpStatusCode?> Start()
        {
            if (replay)
            {
                await worker.ProcessOnceAsync();
                return null;
            }
            using var response = await host.Send(OperationsRequest(actor, HttpMethod.Post, "/api/oa/wf-trigger-echo/fire",
                new { EventKey = OaFixtureEventKey, EventId = eventId, PayloadJson = payload }));
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(1, result.GetProperty("data").GetProperty("firedCount").GetInt32());
            }
            return response.StatusCode;
        }
        Task<OaEventAttempt?> ReadEvent() => h.Db.IntegrationEvents.AsNoTracking()
            .Where(x => x.TargetModule == "WF" && x.SourceNo == eventId)
            .Select(x => new OaEventAttempt(x.Id, x.Status, x.Attempts, x.NextRetryAt, x.PayloadJson)).SingleOrDefaultAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_legacy_OA_event_trigger_retries_only_missing_effect_with_original_long_key()
    {
        await using var h = await Seed();
        var first = await SeedOaEventTrigger(h);
        const string unavailableFlow = "fixture-unavailable-event-flow";
        var second = new Wf_FlowTrigger
        {
            TriggerType = WfTriggerType.Event, EventKey = OaFixtureEventKey, Enabled = true,
            FlowKey = unavailableFlow, StarterUserId = h.User,
        };
        h.Db.Wf_FlowTriggers.Add(second);
        await h.Db.SaveChangesAsync();
        // U+1F642 crosses the old ledger's 30 UTF-16 unit boundary. The serialized event keeps the full key.
        var id = new string('a', 29) + "🙂" + new string('b', 119);
        Assert.Equal(150, id.Length);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var actor = await Login(host, h);
        using (var response = await host.Send(OperationsRequest(actor, HttpMethod.Post, "/api/oa/wf-trigger-echo/fire",
            new { EventKey = OaFixtureEventKey, EventId = id, PayloadJson = "{}" })))
        {
            Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
            var result = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
            Assert.False(result.GetProperty("success").GetBoolean());
            Assert.Contains("1/2", result.GetProperty("message").GetString());
        }

        var eventRow = await h.Db.IntegrationEvents.AsNoTracking().SingleAsync(x => x.TargetModule == "WF");
        Assert.Equal(IntegrationEventStatus.Failed, eventRow.Status);
        Assert.Equal(id, JsonSerializer.Deserialize<WfTriggerEventPayload>(eventRow.PayloadJson)!.EventId);
        var original = await h.Db.Wf_TriggerFires.AsNoTracking().SingleAsync(x => x.TriggerId == first);
        var rejected = await h.Db.Wf_TriggerFires.AsNoTracking().SingleAsync(x => x.TriggerId == second.Id);
        Assert.NotNull(original.InstanceId);
        Assert.Null(rejected.InstanceId);
        Assert.Equal((id + ":" + first, id + ":" + second.Id), (original.IdempotencyKey, rejected.IdempotencyKey));

        await h.Db.Wf_FlowTriggers.Where(x => x.Id == second.Id).ExecuteUpdateAsync(x =>
            x.SetProperty(t => t.FlowKey, ManualFlow));
        await h.Db.IntegrationEvents.Where(x => x.Id == eventRow.Id).ExecuteUpdateAsync(x =>
            x.SetProperty(t => t.NextRetryAt, DateTime.UtcNow.AddMinutes(-1)));
        await using var services = OaEventRetryProvider(h.Tenant);
        var worker = new IntegrationEventRetryWorker(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new IntegrationEventOptions { Enabled = true, MaxAttempts = 3 }),
            NullLogger<IntegrationEventRetryWorker>.Instance);
        await worker.ProcessOnceAsync();
        await worker.ProcessOnceAsync();

        var reloaded = await h.Db.IntegrationEvents.AsNoTracking().SingleAsync(x => x.TargetModule == "WF");
        Assert.Equal((eventRow.Id, eventRow.PayloadJson, IntegrationEventStatus.Success, 2),
            (reloaded.Id, reloaded.PayloadJson, reloaded.Status, reloaded.Attempts));
        var completed = await h.Db.Wf_TriggerFires.AsNoTracking().SingleAsync(x => x.TriggerId == second.Id);
        Assert.Equal(rejected.Id, completed.Id);
        Assert.NotNull(completed.InstanceId);
        Assert.NotEqual(original.InstanceId, completed.InstanceId);
        Assert.Null(completed.Error);
        Assert.Equal(original.InstanceId,
            await h.Db.Wf_TriggerFires.Where(x => x.TriggerId == first).Select(x => x.InstanceId).SingleAsync());
        Assert.Equal(2, (await ManualEffects(h, first)).Instances);
    }

    private static async Task<Guid> SeedOaEventTrigger(Harness h)
    {
        var trigger = await SeedManualTrigger(h);
        await h.Db.Wf_FlowTriggers.Where(x => x.Id == trigger).ExecuteUpdateAsync(x => x
            .SetProperty(t => t.TriggerType, WfTriggerType.Event).SetProperty(t => t.EventKey, OaFixtureEventKey)
            .SetProperty(t => t.ConfigJson, """{"varsMap":{"orderNo":"$.OutboundNo"}}"""));
        return trigger;
    }

    private ServiceProvider OaEventRetryProvider(Guid tenant)
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped(sp => server.Context((TenantContext)sp.GetRequiredService<ITenantContext>()));
        services.AddScoped<TenantEnumerator>();
        services.AddScoped<ITenantEnumerator>(sp => new OaEventTenantEnumerator(sp.GetRequiredService<TenantEnumerator>(), tenant));
        services.AddScoped<IDeadLetterNotifier>(_ => new Mock<IDeadLetterNotifier>(MockBehavior.Strict).Object);
        services.AddScoped<IIntegrationEventDispatcher>(sp =>
        {
            var db = sp.GetRequiredService<CP6Context>();
            var hook = new WfTriggerBridgeHook(db, new FlowTriggerService(db, SubFlowTestHarness.Engine(db)),
                NullLogger<WfTriggerBridgeHook>.Instance);
            return new IntegrationEventDispatcher(new NoOpMesBridgeHook(), new NoOpWmsBridgeHook(), new NoOpErpBridgeHook(),
                new NoOpOrderCancelBridgeHook(), new NoOpFinBridgeHook(), new Mock<ISpaceBridgeHook>(MockBehavior.Strict).Object, hook);
        });
        return services.BuildServiceProvider();
    }

    private sealed class OaEventTenantEnumerator(TenantEnumerator inner, Guid tenant) : ITenantEnumerator
    {
        public async Task<IReadOnlyList<Guid>> ListActiveAsync(CancellationToken ct) =>
            (await inner.ListActiveAsync(ct)).Where(x => x == tenant).ToArray();
    }

    private sealed record OaEventAttempt(Guid Id, string Status, int Attempts, DateTime? NextRetryAt, string Payload);
}
