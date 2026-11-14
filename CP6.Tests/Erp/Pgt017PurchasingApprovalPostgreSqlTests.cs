using System.Net;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Pur;
using CP6.Tests.Platform;
using CP6.Tests.Wf;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("pr", true)]
    [InlineData("pr", false)]
    [InlineData("po", true)]
    [InlineData("po", false)]
    public async Task PGT017_purchase_approval_starts_within_current_authority(string document, bool revokeFirst)
    {
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        var db = h.Db;
        var request = document == "pr";
        var schema = """
            {"start":"start","nodes":[{"id":"start","type":"start"},{"id":"approve","type":"approval",
            "approverStrategy":"Specified","approverUserId":"__WRITER__"},{"id":"end","type":"end"}],
            "edges":[{"from":"start","to":"approve"},{"from":"approve","to":"end"}]}
            """.Replace("__WRITER__", h.User.ToString("D"), StringComparison.Ordinal);
        var (number, bizType, definition) = await SeedPurchaseApproval(h, document, schema);

        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", h.Tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, "submit");
        var before = await Effects();
        Assert.Equal((0, (string?)null, 0, 0, 0, 0), before);
        var authority = await ReadPolicyWrites(h, revoke.OperationId);

        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> submit;
        Task<HttpResponseMessage> revokeWrite;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            var lockSql = revokeFirst
                ? """LOCK TABLE public."Sys_RoleAction" IN SHARE MODE"""
                : """LOCK TABLE public."Wf_FlowInstance" IN SHARE MODE""";
            await using var table = new NpgsqlCommand(lockSql, barrier, hold);
            await table.ExecuteNonQueryAsync();
            if (revokeFirst)
            {
                revokeWrite = Revoke();
                var iam = await WaitForFence(observer, barrier.ProcessID, "%DELETE%Sys_RoleAction%", null, revokeWrite);
                submit = Submit();
                // The original DML trigger can wait on the same root after an
                // earlier permission check. Only the final decision can deny it.
                await WaitForFence(observer, iam.Backend, "%", null, submit);
            }
            else
            {
                submit = Submit();
                var workflow = await WaitForFence(observer, barrier.ProcessID, "%INSERT%Wf_FlowInstance%", null, submit);
                revokeWrite = Revoke();
                await WaitForFence(observer, workflow.Backend, "%cp6_authentication_v1.acquire()%", null, revokeWrite);
            }
            Assert.Equal(before, await Effects());
            Assert.Equal(authority, await ReadPolicyWrites(h, revoke.OperationId));
            await hold.RollbackAsync();
        }

        using var revoked = await revokeWrite;
        Assert.True(revoked.StatusCode == HttpStatusCode.OK, await OperationsFailure(revoked));
        var after = await ReadPolicyWrites(h, revoke.OperationId);
        Assert.NotEqual(authority.RoleRevision, after.RoleRevision);
        Assert.Equal(authority.Generation + 1, after.Generation);
        Assert.Equal(authority.Snapshot + 1, after.Snapshot);
        Assert.Equal(authority.Outbox + 1, after.Outbox);
        Assert.Equal((1L, 1L, 1L), (after.CurrentOutbox, after.Audits, after.Receipts));

        using var result = await submit;
        if (revokeFirst)
        {
            Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
            Assert.Contains("IAM_FORBIDDEN", await OperationsFailure(result), StringComparison.Ordinal);
            Assert.Equal(before, await Effects());
        }
        else
        {
            Assert.True(result.StatusCode == HttpStatusCode.OK, await OperationsFailure(result));
            var instance = await db.Wf_FlowInstances.AsNoTracking().SingleAsync(row => row.BizId == number);
            Assert.Equal(bizType, instance.BizType);
            Assert.Equal(h.User, instance.StarterId);
            Assert.Equal(FlowInstanceStatus.Running, instance.Status);
            Assert.Equal(definition, await db.Wf_FlowDefVersions.Where(row => row.Id == instance.FlowDefVersionId)
                .Select(row => row.FlowDefId).SingleAsync());
            Assert.True(await db.Wf_FlowHistories.AnyAsync(row => row.InstanceId == instance.Id && row.Action == "submit" && row.ActorId == h.User));
            var pending = request ? (int)PrStatus.Submitted : (int)PoStatus.PendingApproval;
            var committed = (pending, instance.Id.ToString(), 1, 1, 1, 1);
            Assert.Equal(committed, await Effects());
            using var stale = await Submit();
            Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
            Assert.Equal(committed, await Effects());
        }

        Task<HttpResponseMessage> Submit() => host.Send(OperationsRequest(writer, HttpMethod.Post, $"api/pur/{document}/{number}/submit"));
        Task<HttpResponseMessage> Revoke() => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
        async Task<(int, string?, int, int, int, int)> Effects()
        {
            var head = request
                ? await db.PurchaseRequests.AsNoTracking().Where(row => row.PrNo == number)
                    .Select(row => new { Status = (int)row.Status, row.ApprovalRef }).SingleAsync()
                : await db.PurchaseOrders.AsNoTracking().Where(row => row.PoNo == number)
                    .Select(row => new { Status = (int)row.Status, row.ApprovalRef }).SingleAsync();
            var instances = db.Wf_FlowInstances.AsNoTracking().Where(row => row.BizId == number).Select(row => row.Id);
            return (head.Status, head.ApprovalRef, await instances.CountAsync(),
                await db.Wf_FlowTasks.CountAsync(row => instances.Contains(row.InstanceId)),
                await db.Wf_FlowHistories.CountAsync(row => instances.Contains(row.InstanceId)),
                await db.Wf_Notifications.CountAsync(row => row.InstanceId != null && instances.Contains(row.InstanceId.Value)));
        }
    }

    [TenantManagementPostgresTheory]
    [InlineData("pr")]
    [InlineData("po")]
    public async Task PGT017_purchase_approval_preserves_immediate_callback_result(string document)
    {
        await using var h = await Seed();
        var db = h.Db;
        var (number, bizType, _) = await SeedPurchaseApproval(h, document, """
            {"start":"start","nodes":[{"id":"start","type":"start"},{"id":"end","type":"end"}],
             "edges":[{"from":"start","to":"end"}]}
            """);
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);
        using var response = await host.Send(OperationsRequest(writer, HttpMethod.Post,
            $"api/pur/{document}/{number}/submit"));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
        var instance = await db.Wf_FlowInstances.AsNoTracking().SingleAsync(row => row.BizId == number);
        Assert.Equal(bizType, instance.BizType);
        Assert.Equal(h.User, instance.StarterId);
        Assert.Equal(FlowInstanceStatus.Approved, instance.Status);
        if (document == "pr")
        {
            var pr = await db.PurchaseRequests.AsNoTracking().SingleAsync(row => row.PrNo == number);
            Assert.Equal((PrStatus.Approved, instance.Id.ToString()), (pr.Status, pr.ApprovalRef));
        }
        else
        {
            var po = await db.PurchaseOrders.AsNoTracking().SingleAsync(row => row.PoNo == number);
            Assert.Equal((PoStatus.Confirmed, instance.Id.ToString()), (po.Status, po.ApprovalRef));
        }
        Assert.Equal(1, await db.Wf_Notifications.CountAsync(row => row.InstanceId == instance.Id));
        Assert.False(await db.Wf_ServiceJobs.AnyAsync(row => row.InstanceId == instance.Id));
    }

    private static async Task<(string Number, string BizType, Guid Definition)> SeedPurchaseApproval(
        Harness h, string document, string schema)
    {
        var db = h.Db;
        var request = document == "pr";
        var resource = $"pur-{document}";
        if (!await db.Sys_Menus.AnyAsync(row => row.MenuKey == resource))
        {
            db.Sys_Menus.Add(new() { MenuId = request ? 32746 : 32747, MenuKey = resource, MenuName = resource });
            await db.SaveChangesAsync();
        }
        await GrantLegacyAuthorityAction(h, resource, "submit");
        var number = document.ToUpperInvariant() + h.Tenant.ToString("N")[..12];
        var flow = "purchasing-" + h.Tenant.ToString("N");
        var bizType = request ? "PUR_PR" : "PUR_PO";
        var definition = await LegacyDefinitionFixture.Flow(db, flow, "Simulated purchase approval", null, schema);
        db.Wf_ApprovalBindings.Add(new() { BizType = bizType, FlowKey = flow, Enable = true });
        if (request)
        {
            db.PurchaseRequests.Add(new()
            {
                PrNo = number, RequestDate = DateTime.Today, RequesterId = "price-owner", Creator = "price-owner",
                Lines = [new() { PrNo = number, LineNo = 1, ItemId = "part", Qty = 3.12345678m, EstPrice = 12.1234m }]
            });
        }
        else
        {
            db.PurchaseOrders.Add(new()
            {
                PoNo = number, SupplierId = "supplier", OrderDate = DateTime.Today, GrossAmount = 12.1234m
            });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (number, bizType, definition);
    }
}
