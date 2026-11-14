using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Wf;
using CP6.Core.Services.Wf.Executors;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using CP6.Tests.Wf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string ManualAction = "FlowTrigger.Edit";
    private const string ManualFlow = "simulated-manual-trigger";

    [TenantManagementPostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PGT017_legacy_OA_manual_trigger_uses_current_authority(bool revokeFirst)
    {
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        var trigger = await SeedManualTrigger(h);
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var actor = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", h.Tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, ManualAction);
        var before = await ManualEffects(h, trigger);
        Assert.Equal(new ManualTriggerEffects(0, 0, 0, 0, 0, 0, 0, null), before);
        var beforePolicy = await ReadPolicyWrites(h, revoke.OperationId);

        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> revocation;
        Task<HttpResponseMessage> firing;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            var blocker = revokeFirst
                ? """LOCK TABLE public."Sys_RoleAction" IN SHARE MODE"""
                : """LOCK TABLE public."Wf_TriggerFire" IN SHARE MODE""";
            await using var block = new NpgsqlCommand(blocker, barrier, hold);
            await block.ExecuteNonQueryAsync();
            if (revokeFirst)
            {
                revocation = Revoke();
                var iam = await WaitForFence(observer, barrier.ProcessID, "%DELETE%Sys_RoleAction%", null, revocation);
                firing = FireManual(host, actor, trigger);
                var waiting = await WaitForFence(observer, iam.Backend, "%cp6_authentication_v1.acquire()%", null, firing);
                output.WriteLine($"Revocation backend {iam.Backend}; manual fire backend {waiting.Backend}; {waiting.WaitEvent}");
            }
            else
            {
                firing = FireManual(host, actor, trigger);
                var writer = await WaitForFence(observer, barrier.ProcessID, "%INSERT%Wf_TriggerFire%", null, firing);
                revocation = Revoke();
                var waiting = await WaitForFence(observer, writer.Backend, "%cp6_authentication_v1.acquire()%", null, revocation);
                output.WriteLine($"Manual fire backend {writer.Backend}; revocation backend {waiting.Backend}; {waiting.WaitEvent}");
            }
            Assert.Equal(before, await ManualEffects(h, trigger));
            Assert.Equal(beforePolicy, await ReadPolicyWrites(h, revoke.OperationId));
            await hold.RollbackAsync();
        }

        using var changed = await revocation;
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));
        using var result = await firing;
        var expected = revokeFirst ? HttpStatusCode.Forbidden : HttpStatusCode.OK;
        Assert.True(result.StatusCode == expected, await OperationsFailure(result));
        var committed = await ManualEffects(h, trigger);
        if (revokeFirst)
            Assert.Equal(before, committed);
        else
        {
            Assert.Equal(new ManualTriggerEffects(1, 1, 1, 1, 1, 1, 0, committed.Watermark), committed);
            Assert.NotNull(committed.Watermark);
            var receipt = await h.Db.Wf_TriggerFires.AsNoTracking().SingleAsync(f => f.TriggerId == trigger);
            var envelope = await result.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(envelope.GetProperty("data").GetProperty("instanceId").GetGuid(), receipt.InstanceId);
            Assert.StartsWith("manual:", receipt.IdempotencyKey);
            Assert.Null(receipt.Error);
        }
        using var stale = await FireManual(host, actor, trigger);
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        Assert.Equal(committed, await ManualEffects(h, trigger));
        var policy = await ReadPolicyWrites(h, revoke.OperationId);
        Assert.NotEqual(beforePolicy.RoleRevision, policy.RoleRevision);
        Assert.Equal(beforePolicy.Generation + 1, policy.Generation);
        Assert.Equal((1L, 1L, 1L), (policy.CurrentOutbox, policy.Audits, policy.Receipts));

        Task<HttpResponseMessage> Revoke() => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_legacy_OA_manual_trigger_persists_external_delivery_only_after_commit()
    {
        await using var h = await Seed();
        var trigger = await SeedManualTrigger(h, external: true);
        var db = h.Db;
        var deliveries = new ConcurrentQueue<(string Key, bool Committed)>();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var remote = builder.Build();
        remote.MapPost("/manual-start", async (HttpContext ctx) =>
        {
            await using var observe = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            var committed = await observe.Wf_TriggerFires.AnyAsync(x => x.TriggerId == trigger && x.InstanceId != null);
            deliveries.Enqueue((ctx.Request.Headers["Idempotency-Key"].ToString(), committed));
            await ctx.Response.WriteAsJsonAsync(new { accepted = true });
        });
        await remote.StartAsync();
        var origin = remote.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        db.Wf_Connectors.Add(new() { Name = "manual-trigger-fixture", BaseUrl = origin, TimeoutSec = 5, Enabled = true });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var actor = await Login(host, h);
        var before = await ManualEffects(h, trigger);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.cp6_test_manual_commit_reject() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'manual fire fixture commit rejected' USING ERRCODE='23514'; END $$;
            CREATE CONSTRAINT TRIGGER cp6_test_manual_commit_reject
            AFTER INSERT ON public."Wf_FlowHistory" DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW WHEN (NEW."Action" = 'submit') EXECUTE FUNCTION public.cp6_test_manual_commit_reject();
            """);
        try
        {
            using var rejected = await FireManual(host, actor, trigger);
            Assert.Empty(deliveries);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
            Assert.Equal(before, await ManualEffects(h, trigger));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER cp6_test_manual_commit_reject ON public."Wf_FlowHistory";
                DROP FUNCTION public.cp6_test_manual_commit_reject();
                """);
        }

        using var accepted = await FireManual(host, actor, trigger);
        Assert.True(accepted.StatusCode == HttpStatusCode.OK, await OperationsFailure(accepted));
        Assert.Empty(deliveries);
        var fire = await db.Wf_TriggerFires.AsNoTracking().SingleAsync(f => f.TriggerId == trigger);
        var pending = await db.Wf_ServiceJobs.AsNoTracking().SingleAsync(j => j.InstanceId == fire.InstanceId);
        Assert.Equal((ServiceJobStatus.Pending, 0), (pending.Status, pending.AttemptCount));

        await using var workerDb = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        using var services = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var executor = new WebApiExecutor(new TenantConnectorResolver(workerDb, [],
            httpFactory: services.GetRequiredService<IHttpClientFactory>()));
        var worker = new WfServiceJobService(workerDb, SubFlowTestHarness.Engine(workerDb), [executor]);
        var clock = pending.NextAttemptAtUtc.AddSeconds(1);
        Assert.Equal(1, await worker.ScanOnceAsync(clock, "manual-fixture"));
        Assert.Equal(($"wf-service-job-{pending.Id}", true), Assert.Single(deliveries));
        Assert.Equal(ServiceJobStatus.Succeeded, await db.Wf_ServiceJobs.Where(j => j.Id == pending.Id)
            .Select(j => j.Status).SingleAsync());
        Assert.Equal("review", await db.Wf_FlowInstances.Where(x => x.Id == fire.InstanceId).Select(x => x.CurrentNode).SingleAsync());
        Assert.Equal(0, await worker.ScanOnceAsync(clock.AddHours(1), "manual-fixture"));
        Assert.Single(deliveries);
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_legacy_OA_manual_trigger_keeps_admitted_business_failure_receipt()
    {
        await using var h = await Seed();
        var trigger = await SeedManualTrigger(h, published: false);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var actor = await Login(host, h);
        using var rejected = await FireManual(host, actor, trigger);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains("E-WF-024", await OperationsFailure(rejected));
        var fire = await h.Db.Wf_TriggerFires.AsNoTracking().SingleAsync(f => f.TriggerId == trigger);
        Assert.Null(fire.InstanceId);
        Assert.StartsWith("manual:", fire.IdempotencyKey);
        Assert.Contains("E-WF-024", fire.Error);
        Assert.Equal(new ManualTriggerEffects(1, 0, 0, 0, 0, 0, 0, null), await ManualEffects(h, trigger));
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_legacy_OA_manual_trigger_placeholder_cannot_start_as_timer()
    {
        await using var h = await Seed();
        var trigger = await SeedManualTrigger(h, timer: true);
        var now = DateTime.UtcNow;
        var timer = new Wf_TriggerFire
        {
            TriggerId = trigger, Source = WfTriggerType.Timer,
            IdempotencyKey = $"{trigger}:{now.AddMinutes(-10):O}", FiredUtc = now.AddMinutes(-10)
        };
        var manual = new Wf_TriggerFire
        {
            TriggerId = trigger, Source = WfTriggerType.Timer,
            IdempotencyKey = $"manual:{Guid.NewGuid():N}", FiredUtc = now.AddMinutes(-10)
        };
        h.Db.Wf_TriggerFires.AddRange(timer, manual);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var service = new FlowTriggerService(h.Db, SubFlowTestHarness.Engine(h.Db));
        Assert.Equal(1, await service.ScanTimersOnceAsync(now, default));
        var manualResult = await h.Db.Wf_TriggerFires.AsNoTracking().SingleAsync(row => row.Id == manual.Id);
        var timerResult = await h.Db.Wf_TriggerFires.AsNoTracking().SingleAsync(row => row.Id == timer.Id);
        Assert.Null(manualResult.InstanceId);
        Assert.Null(manualResult.Error);
        Assert.NotNull(timerResult.InstanceId);
        Assert.Null(timerResult.Error);
        Assert.Equal(1, await h.Db.Wf_FlowInstances.CountAsync(row => row.FlowKey == ManualFlow));
        Assert.Equal(0, await service.ScanTimersOnceAsync(now.AddHours(1), default));
    }

    private static Task<HttpResponseMessage> FireManual(AuthenticationApiProcess host, Dictionary<string, string> login, Guid trigger)
        => host.Send(OperationsRequest(login, HttpMethod.Post, $"api/oa/flow-triggers/{trigger}/manual-fire"));

    private static async Task<Guid> SeedManualTrigger(Harness h, bool external = false, bool published = true, bool timer = false)
    {
        await GrantLegacyAuthorityAction(h, "oa-flow-admin", ManualAction);
        var schema = new FlowSchema
        {
            Start = "start",
            Nodes =
            [
                new() { Id = "start", Type = "start" },
                new() { Id = "review", ApproverStrategy = "Specified", ApproverUserId = h.User }
            ],
            Edges = [new() { From = "start", To = external ? "external" : "review" }]
        };
        if (external)
        {
            schema.Nodes.Add(new() { Id = "external", Type = "serviceTask", ServiceKind = ServiceKind.WebApi,
                ServiceMode = ServiceMode.Sync, ServiceConnectorName = "manual-trigger-fixture", ServicePath = "manual-start",
                ServiceHttpMethod = "POST" });
            schema.Edges.Add(new() { From = "external", To = "review" });
        }
        var db = h.Db;
        SubFlowTestHarness.SeedDef(db, ManualFlow, schema);
        var trigger = new Wf_FlowTrigger
        {
            Id = Guid.NewGuid(), FlowKey = ManualFlow, TriggerType = timer ? WfTriggerType.Timer : WfTriggerType.Message,
            ConfigJson = timer ? """{"cron":"0 9 * * *"}""" : "{}",
            Enabled = true, StarterUserId = h.User,
            ApiKeyHash = timer ? null : WfApiKeyHelper.HashOf("simulated-manual-fixture")
        };
        db.Wf_FlowTriggers.Add(trigger);
        await db.SaveChangesAsync();
        if (published)
            await LegacyDefinitionFixture.ArrangePublishedHistory(db);
        db.ChangeTracker.Clear();
        return trigger.Id;
    }

    private static async Task<ManualTriggerEffects> ManualEffects(Harness h, Guid trigger)
    {
        var db = h.Db;
        var instances = db.Wf_FlowInstances.Where(x => x.FlowKey == ManualFlow && x.StarterId == h.User);
        var ids = instances.Select(x => x.Id);
        return new(
            await db.Wf_TriggerFires.CountAsync(f => f.TriggerId == trigger),
            await instances.CountAsync(),
            await db.Wf_FlowTasks.CountAsync(t => ids.Contains(t.InstanceId) && t.AssigneeId == h.User && t.Status == FlowTaskStatus.Pending),
            await db.Wf_FlowHistories.CountAsync(x => ids.Contains(x.InstanceId) && x.Action == "submit" && x.ActorId == h.User),
            await db.Wf_Notifications.CountAsync(x => x.InstanceId.HasValue && ids.Contains(x.InstanceId.Value) && x.UserId == h.User),
            await db.Wf_FlowTokens.CountAsync(x => ids.Contains(x.InstanceId) && x.Status == FlowTokenStatus.Active),
            await db.Wf_ServiceJobs.CountAsync(j => ids.Contains(j.InstanceId)),
            await db.Wf_FlowTriggers.Where(t => t.Id == trigger).Select(t => t.LastFiredUtc).SingleAsync());
    }

    private sealed record ManualTriggerEffects(int Fires, int Instances, int Tasks, int Submissions,
        int Notifications, int ActiveTokens, int ServiceJobs, DateTime? Watermark);
}
