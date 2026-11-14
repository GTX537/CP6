using System.Collections.Concurrent;
using System.Net;
using CP6.Core.Services.Wf;
using CP6.Core.Services.Wf.Executors;
using CP6.Entity.DomainModels.Sys;
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
    [TenantManagementPostgresTheory]
    [InlineData("api/wf/advanced/sendback", true)]
    [InlineData("api/wf/advanced/sendback", false)]
    [InlineData("api/oa/inbox/sendback", true)]
    [InlineData("api/oa/inbox/sendback", false)]
    public async Task PGT017_legacy_OA_sendback_commits_with_current_authority(string route, bool revokeFirst)
    {
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        await GrantLegacyAuthorityAction(h, "oa-inbox", "sendback");
        var db = h.Db;
        var reviewer = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = reviewer, UserName = "return-target", Password = "owned-fixture", Enable = true });
        SubFlowTestHarness.SeedDef(db, "owned-return", new FlowSchema
        {
            Start = "review",
            Nodes =
            [
                new() { Id = "review", ApproverStrategy = "Specified", ApproverUserId = reviewer },
                new() { Id = "confirm", ApproverStrategy = "Specified", ApproverUserId = h.User }
            ],
            Edges = [new() { From = "review", To = "confirm" }]
        });
        await db.SaveChangesAsync();
        await LegacyDefinitionFixture.ArrangePublishedHistory(db);
        var engine = SubFlowTestHarness.Engine(db);
        var id = await engine.SubmitAsync("owned-return", h.User, "{}");
        var prior = await db.Wf_FlowTasks.Where(t => t.InstanceId == id && t.NodeId == "review").Select(t => t.Id).SingleAsync();
        await engine.ActAsync(prior, reviewer, approve: true);
        var task = await db.Wf_FlowTasks.AsNoTracking().SingleAsync(t => t.InstanceId == id && t.NodeId == "confirm");
        db.ChangeTracker.Clear();

        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", h.Tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, "sendback");
        var authority = await ReadPolicyWrites(h, revoke.OperationId);
        var before = ("confirm", FlowTaskStatus.Pending, FlowTokenStatus.Active, FlowFormToStatus.Pending, 0, 0, 0, 1);
        Assert.Equal(before, await Effects());

        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> change;
        Task<HttpResponseMessage> write;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            var sql = revokeFirst
                ? """LOCK TABLE public."Sys_RoleAction" IN SHARE MODE"""
                : """LOCK TABLE public."Wf_FlowTask" IN SHARE MODE""";
            await using var block = new NpgsqlCommand(sql, barrier, hold);
            await block.ExecuteNonQueryAsync();
            if (revokeFirst)
            {
                change = Revocation();
                var iam = await WaitForFence(observer, barrier.ProcessID, "%DELETE%Sys_RoleAction%", null, change);
                write = SendBack();
                var waiting = await WaitForFence(observer, iam.Backend, "%", null, write);
                output.WriteLine($"IAM backend {iam.Backend}, waiting return {waiting.Backend} at {waiting.WaitEvent}");
            }
            else
            {
                write = SendBack();
                var work = await WaitForFence(observer, barrier.ProcessID, "%", null, write);
                change = Revocation();
                var waiting = await WaitForFence(observer, work.Backend, "%cp6_authentication_v1.acquire()%", null, change);
                output.WriteLine($"Return backend {work.Backend}, waiting IAM {waiting.Backend} at {waiting.WaitEvent}");
            }
            Assert.Equal(before, await Effects());
            Assert.Equal(authority, await ReadPolicyWrites(h, revoke.OperationId));
            await hold.RollbackAsync();
        }

        using var changed = await change;
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));
        using var result = await write;
        var wanted = revokeFirst ? HttpStatusCode.Forbidden : HttpStatusCode.OK;
        Assert.True(result.StatusCode == wanted, await OperationsFailure(result));
        var expected = revokeFirst ? before :
            ("review", FlowTaskStatus.Cancelled, FlowTokenStatus.Cancelled, FlowFormToStatus.Voided, 1, 1, 1, 1);
        Assert.Equal(expected, await Effects());
        using var stale = await SendBack();
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        Assert.Equal(expected, await Effects());

        var after = await ReadPolicyWrites(h, revoke.OperationId);
        Assert.NotEqual(authority.RoleRevision, after.RoleRevision);
        Assert.Equal(authority.Generation + 1, after.Generation);
        Assert.Equal(authority.Snapshot + 1, after.Snapshot);
        Assert.Equal(authority.Outbox + 1, after.Outbox);
        Assert.Equal((1L, 1L, 1L), (after.CurrentOutbox, after.Audits, after.Receipts));

        Task<HttpResponseMessage> SendBack() => host.Send(OperationsRequest(writer, HttpMethod.Post, route,
            new { TaskId = task.Id, Kind = "node", NodeId = "review", TargetNodeId = "review" }));
        Task<HttpResponseMessage> Revocation() => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
        async Task<(string Node, int Task, int Token, int Step, int NewTasks, int NewNotices, int Returns, int PreviousApprovals)> Effects() =>
            ((await db.Wf_FlowInstances.Where(x => x.Id == id).Select(x => x.CurrentNode).SingleAsync())!,
             await db.Wf_FlowTasks.Where(x => x.Id == task.Id).Select(x => x.Status).SingleAsync(),
             await db.Wf_FlowTokens.Where(x => x.Id == task.TokenId).Select(x => x.Status).SingleAsync(),
             await db.Wf_FlowFormTos.Where(x => x.InstanceId == id && x.NodeId == "confirm").Select(x => x.Status).SingleAsync(),
             await db.Wf_FlowTasks.CountAsync(x => x.InstanceId == id && x.NodeId == "review" && x.Id != prior
                 && x.AssigneeId == reviewer && x.Status == FlowTaskStatus.Pending),
             await db.Wf_Notifications.CountAsync(x => x.InstanceId == id && x.UserId == reviewer),
             await db.Wf_FlowHistories.CountAsync(x => x.InstanceId == id && x.ActorId == h.User && x.Action == "sendback"),
             await db.Wf_FlowHistories.CountAsync(x => x.InstanceId == id && x.ActorId == reviewer && x.Action == "approve"));
    }

    [TenantManagementPostgresTheory]
    [InlineData("api/wf/advanced/sendback")]
    public async Task PGT017_legacy_OA_sendback_dispatches_external_work_after_commit(string route)
    {
        await using var h = await Seed();
        await GrantLegacyAuthorityAction(h, "oa-inbox", "sendback");
        var db = h.Db;
        SubFlowTestHarness.SeedDef(db, "owned-return-from-start", new FlowSchema
        {
            Start = "start",
            Nodes =
            [
                new() { Id = "start", Type = "start" },
                new() { Id = "external", Type = "serviceTask", ServiceKind = ServiceKind.WebApi, ServiceMode = ServiceMode.Sync,
                    ServiceConnectorName = "owned-return-http", ServicePath = "returned", ServiceHttpMethod = "POST" },
                new() { Id = "review", ApproverStrategy = "Specified", ApproverUserId = h.User }
            ],
            Edges = [new() { From = "start", To = "external" }, new() { From = "external", To = "review" }]
        });
        var id = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        db.Wf_FlowInstances.Add(new() { Id = id, FlowKey = "owned-return-from-start", CurrentNode = "review",
            StarterId = h.User, Status = FlowInstanceStatus.Running });
        db.Wf_FlowTokens.Add(new() { Id = tokenId, InstanceId = id, NodeId = "review", Status = FlowTokenStatus.Active });
        db.Wf_FlowTasks.Add(new() { Id = taskId, InstanceId = id, TokenId = tokenId, NodeId = "review",
            AssigneeId = h.User, Status = FlowTaskStatus.Pending });
        await db.SaveChangesAsync();
        await LegacyDefinitionFixture.ArrangePublishedHistory(db);

        var deliveries = new ConcurrentQueue<(string IdempotencyKey, bool Committed)>();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var remote = builder.Build();
        remote.MapPost("/returned", async (HttpContext ctx) =>
        {
            await using var observed = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            var committed = await observed.Wf_FlowHistories.AnyAsync(x => x.InstanceId == id && x.Action == "sendback");
            deliveries.Enqueue((ctx.Request.Headers["Idempotency-Key"].ToString(), committed));
            await ctx.Response.WriteAsJsonAsync(new { accepted = true });
        });
        await remote.StartAsync();
        var remoteOrigin = remote.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        db.Wf_Connectors.Add(new() { Name = "owned-return-http", BaseUrl = remoteOrigin, TimeoutSec = 5, Enabled = true });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.cp6_test_sendback_commit_reject() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'sendback fixture commit rejected' USING ERRCODE='23514'; END $$;
            CREATE CONSTRAINT TRIGGER cp6_test_sendback_commit_reject
            AFTER INSERT ON public."Wf_FlowHistory" DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW WHEN (NEW."Action" = 'sendback') EXECUTE FUNCTION public.cp6_test_sendback_commit_reject();
            """);
        try
        {
            using var rejected = await SendBack();
            Assert.True(rejected.StatusCode == HttpStatusCode.ServiceUnavailable, await OperationsFailure(rejected));
            Assert.Empty(deliveries);
            Assert.Empty(await db.Wf_ServiceJobs.Where(x => x.InstanceId == id).ToListAsync());
            Assert.False(await db.Wf_FlowHistories.AnyAsync(x => x.InstanceId == id && x.Action == "sendback"));
            Assert.Equal(FlowTaskStatus.Pending, await db.Wf_FlowTasks.Where(x => x.Id == taskId).Select(x => x.Status).SingleAsync());
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER cp6_test_sendback_commit_reject ON public."Wf_FlowHistory";
                DROP FUNCTION public.cp6_test_sendback_commit_reject();
                """);
        }

        using var returned = await SendBack();
        Assert.True(returned.StatusCode == HttpStatusCode.OK, await OperationsFailure(returned));
        Assert.Empty(deliveries);
        Assert.Equal("external", await db.Wf_FlowInstances.Where(x => x.Id == id).Select(x => x.CurrentNode).SingleAsync());
        var pending = await db.Wf_ServiceJobs.AsNoTracking().SingleAsync(x => x.InstanceId == id);
        Assert.Equal((ServiceJobStatus.Pending, 0), (pending.Status, pending.AttemptCount));

        await using var workerDb = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        using var services = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var executor = new WebApiExecutor(new TenantConnectorResolver(workerDb, [],
            httpFactory: services.GetRequiredService<IHttpClientFactory>()));
        var worker = new WfServiceJobService(workerDb, SubFlowTestHarness.Engine(workerDb), [executor]);
        var clock = pending.NextAttemptAtUtc.AddSeconds(1);
        Assert.Equal(1, await worker.ScanOnceAsync(clock, "sendback-fixture"));
        Assert.Equal(($"wf-service-job-{pending.Id}", true), Assert.Single(deliveries));
        Assert.Equal(ServiceJobStatus.Succeeded, await db.Wf_ServiceJobs.Where(x => x.Id == pending.Id).Select(x => x.Status).SingleAsync());
        Assert.Equal("review", await db.Wf_FlowInstances.Where(x => x.Id == id).Select(x => x.CurrentNode).SingleAsync());
        Assert.Equal(0, await worker.ScanOnceAsync(clock.AddHours(1), "sendback-fixture"));
        Assert.Single(deliveries);

        Task<HttpResponseMessage> SendBack() => host.Send(OperationsRequest(writer, HttpMethod.Post, route,
            new { TaskId = taskId, TargetNodeId = "start" }));
    }
}
