using System.Net;
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
    public async Task PGT017_legacy_OA_withdrawal_commits_before_subflow_followup(bool revokeFirst)
    {
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        await GrantLegacyAuthorityAction(h, "oa-inbox", "withdraw");
        var db = h.Db;
        SubFlowTestHarness.SeedDef(db, "owned-withdraw-child", SubFlowTestHarness.ChildSchema(h.User));
        SubFlowTestHarness.SeedDef(db, "owned-withdraw-parent",
            SubFlowTestHarness.ParentSchema(h.User, "owned-withdraw-child", errorEdge: true));
        await db.SaveChangesAsync();
        await LegacyDefinitionFixture.ArrangePublishedHistory(db);
        var parentId = await SubFlowTestHarness.Engine(db).SubmitAsync("owned-withdraw-parent", h.User, "{}");
        var childId = await db.Wf_FlowInstances.Where(i => i.ParentInstanceId == parentId).Select(i => i.Id).SingleAsync();
        var childToken = await db.Wf_FlowTokens.Where(t => t.InstanceId == childId && t.Status == FlowTokenStatus.Active)
            .Select(t => t.Id).SingleAsync();
        var pendingJob = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.Wf_ServiceJobs.Add(new Wf_ServiceJob
        {
            Id = pendingJob, InstanceId = childId, TokenId = childToken, NodeId = "owned-simulated-job",
            Kind = ServiceKind.Timer, Status = ServiceJobStatus.Pending, DueAtUtc = now, NextAttemptAtUtc = now
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection, IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", h.Tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, "withdraw");
        var authority = await ReadPolicyWrites(h, revoke.OperationId);
        var before = await ChildEffects();
        Assert.Equal((FlowInstanceStatus.Running, FlowTaskStatus.Pending, FlowTokenStatus.Active,
            FlowFormToStatus.Pending, ServiceJobStatus.Pending, 0), before);
        Assert.Equal(("sub", (int?)null, 0), await ParentEffects());

        await using var barrier = new NpgsqlConnection(connection);
        await using var followup = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await followup.OpenAsync();
        await observer.OpenAsync();
        await using var keepParentPending = await followup.BeginTransactionAsync();
        if (!revokeFirst)
        {
            await using var holdParent = new NpgsqlCommand("""
                SELECT "Id" FROM public."Wf_FlowInstance" WHERE "TenantId" = @tenant AND "Id" = @parent FOR UPDATE
                """, followup, keepParentPending);
            holdParent.Parameters.AddWithValue("tenant", h.Tenant);
            holdParent.Parameters.AddWithValue("parent", parentId);
            Assert.Equal(parentId, await holdParent.ExecuteScalarAsync());
        }

        Task<HttpResponseMessage> write;
        Task<HttpResponseMessage> change;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            var blocker = revokeFirst
                ? """LOCK TABLE public."Sys_RoleAction" IN SHARE MODE"""
                : """LOCK TABLE public."Wf_FlowInstance" IN SHARE MODE""";
            await using var sql = new NpgsqlCommand(blocker, barrier, hold);
            await sql.ExecuteNonQueryAsync();
            if (revokeFirst)
            {
                change = Revoke();
                var iam = await WaitForFence(observer, barrier.ProcessID, "%DELETE%Sys_RoleAction%", null, change);
                write = Withdraw();
                var waiting = await WaitForFence(observer, iam.Backend, "%cp6_authentication_v1.acquire()%", null, write);
                output.WriteLine($"Revocation {iam.Backend}, waiting withdrawal {waiting.Backend}, {waiting.WaitEvent}");
            }
            else
            {
                write = Withdraw();
                var business = await WaitForFence(observer, barrier.ProcessID, "%UPDATE%Wf_FlowInstance%", null, write);
                change = Revoke();
                var waiting = await WaitForFence(observer, business.Backend, "%cp6_authentication_v1.acquire()%", null, change);
                output.WriteLine($"Withdrawal {business.Backend}, waiting revocation {waiting.Backend}, {waiting.WaitEvent}");
            }
            Assert.Equal(before, await ChildEffects());
            Assert.Equal(authority, await ReadPolicyWrites(h, revoke.OperationId));
            await hold.RollbackAsync();
        }

        if (!revokeFirst)
        {
            var waiting = await WaitForFence(observer, followup.ProcessID, "%UPDATE%Wf_FlowInstance%", null, write);
            output.WriteLine($"Parent row {followup.ProcessID}, waiting follow-up {waiting.Backend}, {waiting.WaitEvent}");
            Assert.Equal(Withdrawn(), await ChildEffects());
            Assert.Equal(("sub", (int?)ServiceJobStatus.Pending, 0), await ParentEffects());
        }
        await keepParentPending.RollbackAsync();

        using var changed = await change;
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));
        using var result = await write;
        var expected = revokeFirst ? HttpStatusCode.Forbidden : HttpStatusCode.OK;
        Assert.True(result.StatusCode == expected, await OperationsFailure(result));
        Assert.Equal(revokeFirst ? before : Withdrawn(), await ChildEffects());
        var parent = revokeFirst ? ("sub", (int?)null, 0) : ("err", (int?)ServiceJobStatus.Succeeded, 1);
        Assert.Equal(parent, await ParentEffects());
        using var stale = await Withdraw();
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        Assert.Equal(parent, await ParentEffects());
        Assert.Equal(revokeFirst ? before : Withdrawn(), await ChildEffects());

        var after = await ReadPolicyWrites(h, revoke.OperationId);
        Assert.NotEqual(authority.RoleRevision, after.RoleRevision);
        Assert.Equal(authority.Generation + 1, after.Generation);
        Assert.Equal(authority.Snapshot + 1, after.Snapshot);
        Assert.Equal(authority.Outbox + 1, after.Outbox);
        Assert.Equal((1L, 1L, 1L), (after.CurrentOutbox, after.Audits, after.Receipts));

        Task<HttpResponseMessage> Revoke() => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
        Task<HttpResponseMessage> Withdraw() => host.Send(OperationsRequest(writer, HttpMethod.Post, $"api/wf/flow/{childId}/withdraw", new { }));
        static (int Instance, int Task, int Token, int Step, int Job, int History) Withdrawn() =>
            (FlowInstanceStatus.Withdrawn, FlowTaskStatus.Cancelled, FlowTokenStatus.Cancelled,
                FlowFormToStatus.Voided, ServiceJobStatus.Cancelled, 1);

        async Task<(int Instance, int Task, int Token, int Step, int Job, int History)> ChildEffects() =>
            (await db.Wf_FlowInstances.Where(i => i.Id == childId).Select(i => i.Status).SingleAsync(),
             await db.Wf_FlowTasks.Where(t => t.InstanceId == childId).Select(t => t.Status).SingleAsync(),
             await db.Wf_FlowTokens.Where(t => t.Id == childToken).Select(t => t.Status).SingleAsync(),
             await db.Wf_FlowFormTos.Where(f => f.InstanceId == childId && f.NodeId == "ca").Select(f => f.Status).SingleAsync(),
             await db.Wf_ServiceJobs.Where(j => j.Id == pendingJob).Select(j => j.Status).SingleAsync(),
             await db.Wf_FlowHistories.CountAsync(a => a.InstanceId == childId && a.ActorId == h.User && a.Action == "withdraw"));

        async Task<(string Node, int? Resume, int ErrorHistory)> ParentEffects() =>
            (await db.Wf_FlowInstances.Where(i => i.Id == parentId).Select(i => i.CurrentNode).SingleAsync(),
             await db.Wf_ServiceJobs.Where(j => j.InstanceId == parentId && j.TokenId == childId && j.Kind == WfJobKind.SubFlowResume)
                 .Select(j => (int?)j.Status).SingleOrDefaultAsync(),
             await db.Wf_FlowHistories.CountAsync(a => a.InstanceId == parentId && a.Action == "subFlowError"));
    }
}
