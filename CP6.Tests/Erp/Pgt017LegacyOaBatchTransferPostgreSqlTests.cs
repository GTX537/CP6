using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public async Task PGT017_legacy_OA_batch_transfer_checks_each_commit(bool? revokeFirst)
    {
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        await GrantLegacyAuthorityAction(h, "oa-inbox", "batch-transfer");
        var db = h.Db;
        var recipient = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var instances = new[] { Guid.NewGuid(), Guid.NewGuid() };
        db.Sys_Users.Add(new Sys_User
        {
            Id = recipient, UserName = "owned-batch-recipient", Password = "owned-fixture", Enable = true, RoleId = 72
        });
        var reassigned = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User
        {
            Id = reassigned, UserName = "owned-batch-reassigned", Password = "owned-fixture", Enable = true, RoleId = 72
        });
        var ids = new[] { first, second };
        var created = new DateTime(2026, 10, 15);
        for (var i = 0; i < ids.Length; i++)
        {
            db.Wf_FlowInstances.Add(new Wf_FlowInstance
            {
                Id = instances[i], FlowKey = "owned-batch-fence", CurrentNode = "review",
                StarterId = h.User, Status = FlowInstanceStatus.Running
            });
            db.Wf_FlowTasks.Add(new Wf_FlowTask
            {
                Id = ids[i], InstanceId = instances[i], AssigneeId = h.User, NodeId = "review",
                Status = FlowTaskStatus.Pending, CreateDate = created.AddMinutes(i)
            });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection, IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", h.Tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, "batch-transfer");
        var authority = await ReadPolicyWrites(h, revoke.OperationId);
        var before = await Effects();
        Assert.Equal((h.User, h.User, 0, 0, 0), before);
        var body = new { FromUserId = h.User, ToUserId = recipient, Comment = "Simulated batch race", Filter = new { TaskIds = ids } };

        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        if (revokeFirst is null)
        {
            await using var peer = await barrier.BeginTransactionAsync();
            await using var sql = new NpgsqlCommand("""
                UPDATE public."Wf_FlowTask" SET "AssigneeId" = @assignee WHERE "Id" = @task
                """, barrier, peer);
            sql.Parameters.AddWithValue("task", first);
            sql.Parameters.AddWithValue("assignee", reassigned);
            Assert.Equal(1, await sql.ExecuteNonQueryAsync());
            var pending = Transfer();
            var waiting = await WaitForFence(observer, barrier.ProcessID, "%cp6_authentication_v1.acquire()%", null, pending);
            output.WriteLine($"Reassignment {barrier.ProcessID}, waiting batch transfer {waiting.Backend}, {waiting.WaitEvent}");
            Assert.Equal(before, await Effects());
            await peer.CommitAsync();
            using var response = await pending;
            var report = await Report(response);
            Assert.Equal(2, report.Total);
            Assert.Equal(1, report.Succeeded);
            var failed = Assert.Single(report.Failed);
            Assert.Equal(first, failed.TaskId);
            Assert.Equal("E-WF-002", failed.Error);
            Assert.Equal((reassigned, recipient, 1, 1, 1), await Effects());
            return;
        }

        Task<HttpResponseMessage> write;
        Task<HttpResponseMessage> change;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            var blocker = revokeFirst.Value
                ? """LOCK TABLE public."Sys_RoleAction" IN SHARE MODE"""
                : """LOCK TABLE public."Wf_FlowTask" IN SHARE MODE""";
            await using var sql = new NpgsqlCommand(blocker, barrier, hold);
            await sql.ExecuteNonQueryAsync();
            if (revokeFirst.Value)
            {
                change = Revoke();
                var iam = await WaitForFence(observer, barrier.ProcessID, "%DELETE%Sys_RoleAction%", null, change);
                write = Transfer();
                var waiting = await WaitForFence(observer, iam.Backend, "%cp6_authentication_v1.acquire()%", null, write);
                output.WriteLine($"Revocation {iam.Backend}, waiting batch transfer {waiting.Backend}, {waiting.WaitEvent}");
            }
            else
            {
                write = Transfer();
                var business = await WaitForFence(observer, barrier.ProcessID, "%", null, write);
                change = Revoke();
                var waiting = await WaitForFence(observer, business.Backend, "%cp6_authentication_v1.acquire()%", null, change);
                output.WriteLine($"First transfer {business.Backend}, waiting revocation {waiting.Backend}, {waiting.WaitEvent}");
            }
            Assert.Equal(before, await Effects());
            Assert.Equal(authority, await ReadPolicyWrites(h, revoke.OperationId));
            await hold.RollbackAsync();
        }
        using var changed = await change;
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));
        using var result = await write;
        var batch = await Report(result);
        Assert.Equal(2, batch.Total);
        Assert.Equal(revokeFirst.Value ? 0 : 1, batch.Succeeded);
        Assert.Equal(revokeFirst.Value ? ids : [second], batch.Failed.Select(item => item.TaskId));
        Assert.All(batch.Failed, item => { Assert.False(item.Ok); Assert.Equal("IAM_FORBIDDEN", item.Error); });
        var expected = revokeFirst.Value ? before : (recipient, h.User, 1, 1, 1);
        Assert.Equal(expected, await Effects());
        using var stale = await Transfer();
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        Assert.Equal(expected, await Effects());
        var after = await ReadPolicyWrites(h, revoke.OperationId);
        Assert.NotEqual(authority.RoleRevision, after.RoleRevision);
        Assert.Equal(authority.Generation + 1, after.Generation);
        Assert.Equal(authority.Snapshot + 1, after.Snapshot);
        Assert.Equal(authority.Outbox + 1, after.Outbox);
        Assert.Equal((1L, 1L, 1L), (after.CurrentOutbox, after.Audits, after.Receipts));

        Task<HttpResponseMessage> Revoke() => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
        Task<HttpResponseMessage> Transfer() => host.Send(OperationsRequest(writer, HttpMethod.Post, "api/oa/inbox/batch-transfer", body));
        static async Task<BatchTransferReport> Report(HttpResponseMessage response)
        {
            Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
            using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();
            return payload!.RootElement.GetProperty("data").Deserialize<BatchTransferReport>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }

        async Task<(Guid First, Guid Second, int History, int Steps, int Notices)> Effects()
        {
            var assignments = await db.Wf_FlowTasks.AsNoTracking().Where(row => ids.Contains(row.Id))
                .OrderBy(row => row.CreateDate).Select(row => row.AssigneeId).ToArrayAsync();
            return (assignments[0], assignments[1],
                await db.Wf_FlowHistories.CountAsync(row => instances.Contains(row.InstanceId) && row.Action == "transfer" && row.ActorId == h.User),
                await db.Wf_FlowFormTos.CountAsync(row => instances.Contains(row.InstanceId) && row.ExpectedHandlerId == recipient && row.Status == FlowFormToStatus.Pending),
                await db.Wf_Notifications.CountAsync(row => ids.Contains(row.TaskId!.Value) && row.UserId == recipient));
        }
    }
}
