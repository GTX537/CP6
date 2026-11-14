using System.Net;
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
    public async Task PGT017_legacy_OA_transfer_commits_with_current_authority(bool revokeFirst)
    {
        await using var fixture = await Seed();
        await SeedFenceAdministrator(fixture);
        await GrantLegacyAuthorityAction(fixture, "oa-inbox", "transfer");

        var db = fixture.Db;
        var tenant = fixture.Tenant;
        var recipient = Guid.NewGuid();
        var instance = Guid.NewGuid();
        var task = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = recipient, UserName = "legacy-oa-target", Password = "owned-fixture", Enable = true, RoleId = 72 });
        db.Wf_FlowInstances.Add(new Wf_FlowInstance
        {
            Id = instance, FlowKey = "owned-oa-fence", CurrentNode = "review",
            StarterId = fixture.User, Status = FlowInstanceStatus.Running
        });
        db.Wf_FlowTasks.Add(new Wf_FlowTask
        {
            Id = task, InstanceId = instance, AssigneeId = fixture.User,
            NodeId = "review", Status = FlowTaskStatus.Pending
        });
        db.Wf_FlowFormTos.Add(new Wf_FlowFormTo
        {
            Id = Guid.NewGuid(), InstanceId = instance, StepSeq = 1, NodeId = "review",
            ExpectedHandlerId = fixture.User, Status = FlowFormToStatus.Pending
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(tenant));
        var login = await Login(host, fixture);
        var manager = await Login(host, "fence-manager", "Fence1!Password", tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, "transfer");
        var authority = await ReadPolicyWrites(fixture, revoke.OperationId);
        var before = await Effects();
        Assert.Equal((fixture.User, 0, 0, 0, false), before);
        var body = new { TaskId = task, ToUserId = recipient, Comment = "Simulated legacy OA transfer" };

        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> change;
        Task<HttpResponseMessage> transfer;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            var blocker = revokeFirst
                ? """LOCK TABLE public."Sys_RoleAction" IN SHARE MODE"""
                : """LOCK TABLE public."Wf_FlowTask" IN SHARE MODE""";
            await using var block = new NpgsqlCommand(blocker, barrier, hold);
            await block.ExecuteNonQueryAsync();
            if (revokeFirst)
            {
                change = Revoke();
                var iam = await WaitForFence(observer, barrier.ProcessID, "%DELETE%Sys_RoleAction%", null, change);
                transfer = Transfer();
                var waiting = await WaitForFence(observer, iam.Backend, "%cp6_authentication_v1.acquire()%", null, transfer);
                output.WriteLine($"Revocation backend {iam.Backend}, waiting transfer {waiting.Backend}, {waiting.WaitEvent}");
            }
            else
            {
                transfer = Transfer();
                var business = await WaitForFence(observer, barrier.ProcessID, "%", null, transfer);
                change = Revoke();
                var waiting = await WaitForFence(observer, business.Backend, "%cp6_authentication_v1.acquire()%", null, change);
                output.WriteLine($"Transfer backend {business.Backend}, waiting revocation {waiting.Backend}, {waiting.WaitEvent}");
            }
            Assert.Equal(before, await Effects());
            Assert.Equal(authority, await ReadPolicyWrites(fixture, revoke.OperationId));
            await hold.RollbackAsync();
        }
        using var changed = await change;
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));
        var afterAuthority = await ReadPolicyWrites(fixture, revoke.OperationId);
        Assert.NotEqual(authority.RoleRevision, afterAuthority.RoleRevision);
        Assert.Equal(authority.Generation + 1, afterAuthority.Generation);
        Assert.Equal(authority.Snapshot + 1, afterAuthority.Snapshot);
        Assert.Equal(authority.Outbox + 1, afterAuthority.Outbox);
        Assert.Equal((1L, 1L, 1L), (afterAuthority.CurrentOutbox, afterAuthority.Audits, afterAuthority.Receipts));

        using var result = await transfer;
        var expected = revokeFirst ? HttpStatusCode.Forbidden : HttpStatusCode.OK;
        Assert.True(result.StatusCode == expected, await OperationsFailure(result));
        var committed = revokeFirst ? before : (recipient, 1, 1, 1, true);
        Assert.Equal(committed, await Effects());
        using var stale = await Transfer();
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        Assert.Equal(committed, await Effects());

        Task<HttpResponseMessage> Revoke() => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
        Task<HttpResponseMessage> Transfer() => host.Send(OperationsRequest(login, HttpMethod.Post, "api/oa/inbox/transfer", body));
        async Task<(Guid Assignee, int History, int Notifications, int RecipientSteps, bool SourceTransferred)> Effects() =>
            (await db.Wf_FlowTasks.AsNoTracking().Where(row => row.Id == task).Select(row => row.AssigneeId).SingleAsync(),
             await db.Wf_FlowHistories.CountAsync(row => row.InstanceId == instance && row.Action == "transfer" && row.ActorId == fixture.User),
             await db.Wf_Notifications.CountAsync(row => row.InstanceId == instance && row.TaskId == task && row.UserId == recipient),
             await db.Wf_FlowFormTos.CountAsync(row => row.InstanceId == instance && row.StepSeq == 2 &&
                 row.ExpectedHandlerId == recipient && row.Status == FlowFormToStatus.Pending),
             await db.Wf_FlowFormTos.AnyAsync(row => row.InstanceId == instance && row.StepSeq == 1 &&
                 row.ActualHandlerId == fixture.User && row.Status == FlowFormToStatus.Transferred));
    }
}
