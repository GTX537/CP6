using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using CP6.Tests.Wf;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PGT017_legacy_OA_batch_approval_rechecks_each_task(bool revokeFirst)
    {
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        await GrantLegacyAuthorityAction(h, "oa-inbox", "approve");
        var db = h.Db;
        SubFlowTestHarness.SeedDef(db, "owned-batch-vote", new FlowSchema
        {
            Start = "review",
            Nodes = [new() { Id = "review", ApproverStrategy = "Specified", ApproverUserId = h.User }, new() { Id = "done", Type = "end" }],
            Edges = [new() { From = "review", To = "done" }]
        });
        await db.SaveChangesAsync();
        await LegacyDefinitionFixture.ArrangePublishedHistory(db);
        var engine = SubFlowTestHarness.Engine(db);
        var instances = new[] { await engine.SubmitAsync("owned-batch-vote", h.User, "{}"),
            await engine.SubmitAsync("owned-batch-vote", h.User, "{}") };
        var ids = new[]
        {
            await db.Wf_FlowTasks.Where(t => t.InstanceId == instances[0]).Select(t => t.Id).SingleAsync(),
            await db.Wf_FlowTasks.Where(t => t.InstanceId == instances[1]).Select(t => t.Id).SingleAsync()
        };
        db.ChangeTracker.Clear();

        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", h.Tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, "approve");
        var authority = await ReadPolicyWrites(h, revoke.OperationId);
        var before = await Effects();
        Assert.Equal((FlowTaskStatus.Pending, FlowTaskStatus.Pending, 0, 0, 0), before);

        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> change;
        Task<HttpResponseMessage> vote;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            var sql = revokeFirst
                ? """LOCK TABLE public."Sys_RoleAction" IN SHARE MODE"""
                : """LOCK TABLE public."Wf_FlowTask" IN SHARE MODE""";
            await using var block = new NpgsqlCommand(sql, barrier, hold);
            await block.ExecuteNonQueryAsync();
            if (revokeFirst)
            {
                change = Revoke();
                var iam = await WaitForFence(observer, barrier.ProcessID, "%DELETE%Sys_RoleAction%", null, change);
                vote = Approve();
                var waiting = await WaitForFence(observer, iam.Backend, "%", null, vote);
                output.WriteLine($"IAM backend {iam.Backend}, batch backend {waiting.Backend}, {waiting.WaitEvent}");
            }
            else
            {
                vote = Approve();
                var business = await WaitForFence(observer, barrier.ProcessID, "%", null, vote);
                change = Revoke();
                var waiting = await WaitForFence(observer, business.Backend, "%cp6_authentication_v1.acquire()%", null, change);
                output.WriteLine($"Batch backend {business.Backend}, IAM backend {waiting.Backend}, {waiting.WaitEvent}");
            }
            Assert.Equal(before, await Effects());
            Assert.Equal(authority, await ReadPolicyWrites(h, revoke.OperationId));
            await hold.RollbackAsync();
        }

        using var changed = await change;
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));
        using var result = await vote;
        Assert.True(result.StatusCode == HttpStatusCode.OK, await OperationsFailure(result));
        using var payload = await result.Content.ReadFromJsonAsync<JsonDocument>();
        var items = payload!.RootElement.GetProperty("data").Deserialize<BatchActResultItem[]>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(ids, items.Select(x => x.TaskId));
        Assert.Equal(!revokeFirst, items[0].Ok);
        Assert.False(items[1].Ok);
        Assert.All(items.Where(x => !x.Ok), x => Assert.Equal("IAM_FORBIDDEN", x.Error));
        var expected = revokeFirst ? before : (FlowTaskStatus.Approved, FlowTaskStatus.Pending, 1, 1, 1);
        Assert.Equal(expected, await Effects());
        using var stale = await Approve();
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        Assert.Equal(expected, await Effects());

        var after = await ReadPolicyWrites(h, revoke.OperationId);
        Assert.NotEqual(authority.RoleRevision, after.RoleRevision);
        Assert.Equal(authority.Generation + 1, after.Generation);
        Assert.Equal(authority.Snapshot + 1, after.Snapshot);
        Assert.Equal(authority.Outbox + 1, after.Outbox);
        Assert.Equal((1L, 1L, 1L), (after.CurrentOutbox, after.Audits, after.Receipts));

        Task<HttpResponseMessage> Revoke() => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
        Task<HttpResponseMessage> Approve() => host.Send(OperationsRequest(writer, HttpMethod.Post, "api/oa/inbox/batch",
            new { TaskIds = new[] { ids[0], ids[0], ids[1] }, Approve = true, Comment = "simulated batch vote" }));

        async Task<(int First, int Second, int Approvals, int Finished, int Notifications)> Effects() =>
            (await db.Wf_FlowTasks.Where(x => x.Id == ids[0]).Select(x => x.Status).SingleAsync(),
             await db.Wf_FlowTasks.Where(x => x.Id == ids[1]).Select(x => x.Status).SingleAsync(),
             await db.Wf_FlowHistories.CountAsync(x => instances.Contains(x.InstanceId) && x.ActorId == h.User && x.Action == "approve"),
             await db.Wf_FlowInstances.CountAsync(x => instances.Contains(x.Id) && x.Status == FlowInstanceStatus.Approved),
             await db.Wf_Notifications.CountAsync(x => instances.Contains(x.InstanceId!.Value) && x.UserId == h.User));
    }
}
