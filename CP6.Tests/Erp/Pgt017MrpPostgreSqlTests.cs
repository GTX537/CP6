using System.Net;
using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Plan;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("run", true)]
    [InlineData("run", false)]
    [InlineData("confirm", true)]
    [InlineData("ignore", true)]
    [InlineData("convert", true)]
    [InlineData("run", false, true)]
    public async Task PGT017_mrp_mutation_and_iam_revocation_share_the_commit(string operation, bool revokeFirst, bool failLastWrite = false)
    {
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        const string resource = "plan-mrp";
        if (!await h.Db.Sys_Menus.AnyAsync(menu => menu.MenuKey == resource))
        {
            h.Db.Sys_Menus.Add(new() { MenuId = 731, MenuKey = resource, MenuName = "MRP fixture" });
            await h.Db.SaveChangesAsync();
        }
        await GrantLegacyAuthorityAction(h, resource, operation);

        var date = new DateTime(2026, 11, 14);
        const decimal quantity = 7.12345678m;
        var item = "sim" + h.Tenant.ToString("N")[..8];
        var previous = new Plan_MrpRun { Id = Guid.NewGuid(), RunNo = item, RunAt = date, Status = (int)MrpRunStatus.Completed };
        var suggestion = new Plan_PlannedOrder
        {
            Id = Guid.NewGuid(), MrpRunId = previous.Id, ItemCd = item, Qty = 2m,
            Type = (int)PlannedOrderType.Purchase, RequiredDate = date, ReleaseDate = date
        };
        h.Db.AddRange(previous, suggestion, new Plan_Pegging { PlannedOrderId = suggestion.Id, Qty = 2m });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", h.Tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, operation);
        var before = await Effects();
        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();

        if (failLastWrite)
        {
            await using (var setup = new NpgsqlCommand("""
                ALTER TABLE public."Plan_Pegging"
                ADD CONSTRAINT pgt017_fixture_pegging CHECK ("SourceRefNo" IS DISTINCT FROM 'fixture-failed');
                """, barrier))
                await setup.ExecuteNonQueryAsync();
            try
            {
                using var failed = await Write();
                Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
                Assert.Equal(before, await Effects());
            }
            finally
            {
                await using var cleanup = new NpgsqlCommand(
                    """ALTER TABLE public."Plan_Pegging" DROP CONSTRAINT pgt017_fixture_pegging""", barrier);
                await cleanup.ExecuteNonQueryAsync();
            }
            using var retry = await Write();
            Assert.True(retry.StatusCode == HttpStatusCode.OK, await OperationsFailure(retry));
            await AssertCompletedRun();
            return;
        }

        var authority = await ReadPolicyWrites(h, revoke.OperationId);

        Task<HttpResponseMessage> change;
        Task<HttpResponseMessage> write;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            if (revokeFirst)
            {
                await using var lockActions = new NpgsqlCommand("""LOCK TABLE public."Sys_RoleAction" IN SHARE MODE""", barrier, hold);
                await lockActions.ExecuteNonQueryAsync();
                change = Revoke();
                var iam = await WaitForFence(observer, barrier.ProcessID, "%DELETE%Sys_RoleAction%", null, change);
                write = Write();
                await WaitForFence(observer, iam.Backend, "%cp6_authentication_v1.acquire()%", null, write);
            }
            else
            {
                await using var lockFinal = new NpgsqlCommand("""LOCK TABLE public."Plan_NetRequirement" IN SHARE MODE""", barrier, hold);
                await lockFinal.ExecuteNonQueryAsync();
                write = Write();
                var mrp = await WaitForFence(observer, barrier.ProcessID, "%INSERT%Plan_NetRequirement%", null, write);
                change = Revoke();
                await WaitForFence(observer, mrp.Backend, "%cp6_authentication_v1.acquire()%", null, change);
            }
            Assert.Equal(before, await Effects());
            Assert.Equal(authority, await ReadPolicyWrites(h, revoke.OperationId));
            await hold.RollbackAsync();
        }

        using var changed = await change;
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));
        var after = await ReadPolicyWrites(h, revoke.OperationId);
        Assert.NotEqual(authority.RoleRevision, after.RoleRevision);
        Assert.Equal(authority.Generation + 1, after.Generation);
        Assert.Equal(authority.Snapshot + 1, after.Snapshot);
        Assert.Equal(authority.Outbox + 1, after.Outbox);
        Assert.Equal(1, after.CurrentOutbox);
        Assert.Equal(1, after.Audits);
        Assert.Equal(1, after.Receipts);

        using var result = await write;
        if (revokeFirst)
        {
            Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
            Assert.Equal(before, await Effects());
            await GrantLegacyAuthorityAction(h, resource, operation);
            using var allowed = await Write();
            Assert.True(allowed.StatusCode == HttpStatusCode.OK, await OperationsFailure(allowed));
        }
        else
        {
            Assert.True(result.StatusCode == HttpStatusCode.OK, await OperationsFailure(result));
            var committed = await Effects();
            using var stale = await Write();
            Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
            Assert.Equal(committed, await Effects());
        }

        if (operation == "run") await AssertCompletedRun();
        else
        {
            var order = await h.Db.PlannedOrders.AsNoTracking().SingleAsync();
            var expected = operation switch
            {
                "confirm" => PlannedOrderStatus.Confirmed,
                "ignore" => PlannedOrderStatus.Ignored,
                _ => PlannedOrderStatus.Converted
            };
            Assert.Equal((int)expected, order.Status);
            Assert.Equal(operation == "convert" ? $"PR-STUB-{item}" : null, order.ConvertedDocNo);
        }

        Task<HttpResponseMessage> Revoke() => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
        Task<HttpResponseMessage> Write() => operation == "run"
            ? host.Send(OperationsRequest(writer, HttpMethod.Post, "api/plan/mrp/run", new
                { demands = new[] { new { itemCd = item, qty = quantity, requiredDate = date, sourceRefNo = failLastWrite ? "fixture-failed" : "fixture" } } }))
            : host.Send(OperationsRequest(writer, HttpMethod.Post, $"api/plan/mrp/planned-order/{suggestion.Id}/{operation}"));

        async Task AssertCompletedRun()
        {
            var fresh = await h.Db.MrpRuns.AsNoTracking().SingleAsync(x => x.Id != previous.Id);
            Assert.Equal((int)MrpRunStatus.Completed, fresh.Status);
            var orders = await h.Db.PlannedOrders.AsNoTracking().ToArrayAsync();
            Assert.True(orders.Single(x => x.Id == suggestion.Id).IsDeleted);
            var proposed = Assert.Single(orders, x => x.MrpRunId == fresh.Id);
            Assert.Equal(quantity, proposed.Qty);
            Assert.Equal(quantity, (await h.Db.NetRequirements.AsNoTracking().SingleAsync()).Net);
            var oldPeg = await h.Db.Peggings.AsNoTracking().SingleAsync(x => x.PlannedOrderId == suggestion.Id);
            Assert.True(oldPeg.IsDeleted);
            Assert.Equal(quantity, (await h.Db.Peggings.AsNoTracking().SingleAsync(x => x.PlannedOrderId == proposed.Id)).Qty);
        }

        async Task<string> Effects() => JsonSerializer.Serialize(new
        {
            Runs = await h.Db.MrpRuns.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Orders = await h.Db.PlannedOrders.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Net = await h.Db.NetRequirements.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Pegging = await h.Db.Peggings.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Number = await h.Db.DocSequences.AsNoTracking().Where(x => x.FuncCode == "MRP").Select(x => x.LastSeq).SingleOrDefaultAsync()
        });
    }
}
