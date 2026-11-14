using System.Net;
using System.Text.Json;
using CP6.Entity.DomainModels.Pur;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task PGT017_purchase_request_conversion_commits_the_order_and_its_source_lines_together(
        bool revokeFirst, bool failSourceWrite)
    {
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        await GrantLegacyAuthorityAction(h, "pur-pr", "convert");
        var db = h.Db;
        var suffix = h.Tenant.ToString("N")[..12];
        var number = "PR" + suffix;
        var firstSupplier = "A" + suffix;
        var secondSupplier = "B" + suffix;
        const decimal quantity = 3.12345678m;
        const decimal price = 12.1234m;
        var orderDate = DateTime.Today;
        db.BusinessPartners.AddRange(
            new() { BpCd = firstSupplier, BpName = "first supplier", SupplierFlg = true },
            new() { BpCd = secondSupplier, BpName = "second supplier", SupplierFlg = true });
        db.Pub_DocSequences.Add(new() { BizKey = "PO", Prefix = "PO" + suffix, SeqLength = 4 });
        db.PurchaseRequests.Add(new()
        {
            PrNo = number, RequestDate = orderDate, Status = PrStatus.Approved,
            Lines = [
                new()
                {
                    PrNo = number, LineNo = 1, ItemId = "first", Qty = quantity, EstPrice = price,
                    SuggestSupplierId = firstSupplier, RequiredDate = orderDate.AddDays(3)
                },
                new() { PrNo = number, LineNo = 2, ItemId = "second", Qty = 9m, EstPrice = 7.5m, SuggestSupplierId = secondSupplier }
            ]
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", h.Tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, "convert");
        var before = await Effects();
        var policy = await ReadPolicyWrites(h, revoke.OperationId);

        if (failSourceWrite)
        {
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE public."Pur_PurchaseRequestLine" ADD CONSTRAINT fixture_source_not_written
                CHECK ("ConvertedPoNo" IS NULL)
                """);
            try
            {
                using var failed = await Convert();
                Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
                Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await Effects()));
                Assert.Equal(policy, await ReadPolicyWrites(h, revoke.OperationId));
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync("""
                    ALTER TABLE public."Pur_PurchaseRequestLine" DROP CONSTRAINT fixture_source_not_written
                    """);
            }

            using var retried = await Convert();
            Assert.True(retried.StatusCode == HttpStatusCode.OK, await OperationsFailure(retried));
            var recovered = await Effects();
            Assert.Equal(2, recovered.Number);
            Assert.Equal(2, recovered.Lines.Length);
            Assert.Equal(recovered.Orders.Select(row => row.PoNo), recovered.Source);
            Assert.Equal(PrStatus.Converted, recovered.Status);
            return;
        }

        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> write;
        Task<HttpResponseMessage> revokeWrite;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            if (revokeFirst)
            {
                await using var table = new NpgsqlCommand(
                    """LOCK TABLE public."Sys_RoleAction" IN SHARE MODE""", barrier, hold);
                await table.ExecuteNonQueryAsync();
                revokeWrite = Revoke();
                var iam = await WaitForFence(observer, barrier.ProcessID, "%DELETE%Sys_RoleAction%", null, revokeWrite);
                write = Convert();
                await WaitForFence(observer, iam.Backend, "%cp6_authentication_v1.acquire()%", null, write);
            }
            else
            {
                await using var table = new NpgsqlCommand(
                    """LOCK TABLE public."Pur_PurchaseRequestLine" IN SHARE MODE""", barrier, hold);
                await table.ExecuteNonQueryAsync();
                write = Convert();
                var purchasing = await WaitForFence(observer, barrier.ProcessID, "%UPDATE%Pur_PurchaseRequestLine%", null, write);
                revokeWrite = Revoke();
                await WaitForFence(observer, purchasing.Backend, "%cp6_authentication_v1.acquire()%", null, revokeWrite);
            }

            Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await Effects()));
            Assert.Equal(policy, await ReadPolicyWrites(h, revoke.OperationId));
            await hold.RollbackAsync();
        }

        using var revoked = await revokeWrite;
        Assert.True(revoked.StatusCode == HttpStatusCode.OK, await OperationsFailure(revoked));
        using var conversion = await write;
        Assert.True(conversion.StatusCode == HttpStatusCode.Forbidden, await OperationsFailure(conversion));
        var after = await ReadPolicyWrites(h, revoke.OperationId);
        Assert.NotEqual(policy.RoleRevision, after.RoleRevision);
        Assert.Equal(policy.Generation + 1, after.Generation);
        Assert.Equal(policy.Snapshot + 1, after.Snapshot);
        Assert.Equal(policy.Outbox + 1, after.Outbox);
        Assert.Equal(1, after.CurrentOutbox);
        Assert.Equal(1, after.Audits);
        Assert.Equal(1, after.Receipts);

        var committed = await Effects();
        if (revokeFirst)
        {
            Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(committed));
        }
        else
        {
            var order = Assert.Single(committed.Orders);
            var line = Assert.Single(committed.Lines);
            Assert.Equal(firstSupplier, order.SupplierId);
            Assert.Equal(order.PoNo, line.PoNo);
            Assert.Equal(order.PoNo, committed.Source[0]);
            Assert.Null(committed.Source[1]);
            Assert.Equal(orderDate, order.OrderDate);
            Assert.Equal("first", line.ItemId);
            Assert.Equal(quantity, line.Qty);
            Assert.Equal(price, line.UnitPrice);
            Assert.Equal(orderDate.AddDays(3), line.RequiredDate);
            Assert.Equal(1, committed.Number);
            Assert.Equal(PrStatus.Approved, committed.Status);
        }

        using var stale = await Convert();
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        Assert.Equal(JsonSerializer.Serialize(committed), JsonSerializer.Serialize(await Effects()));
        await GrantLegacyAuthorityAction(h, "pur-pr", "convert");
        using var resumed = await Convert();
        Assert.True(resumed.StatusCode == HttpStatusCode.OK, await OperationsFailure(resumed));
        var final = await Effects();
        Assert.Equal(2, final.Orders.Length);
        Assert.Equal(2, final.Lines.Length);
        Assert.Equal(final.Orders.Select(row => row.PoNo), final.Source);
        Assert.Equal(2, final.Number);
        Assert.Equal(PrStatus.Converted, final.Status);
        if (!revokeFirst)
            Assert.Equal(Assert.Single(committed.Orders).Id, final.Orders[0].Id);

        Task<HttpResponseMessage> Convert() => host.Send(OperationsRequest(writer, HttpMethod.Post, $"api/pur/pr/{number}/convert"));
        Task<HttpResponseMessage> Revoke() => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
        async Task<(PurchaseOrder[] Orders, PurchaseOrderLine[] Lines, string?[] Source, int Number, PrStatus Status)> Effects()
        {
            return (
                await db.PurchaseOrders.AsNoTracking().OrderBy(x => x.PoNo).ToArrayAsync(),
                await db.PurchaseOrderLines.AsNoTracking().OrderBy(x => x.PoNo).ThenBy(x => x.LineNo).ToArrayAsync(),
                await db.PurchaseRequestLines.Where(x => x.PrNo == number).OrderBy(x => x.LineNo).Select(x => x.ConvertedPoNo).ToArrayAsync(),
                await db.Pub_DocSequences.Where(x => x.BizKey == "PO").Select(x => x.CurrentValue).SingleAsync(),
                await db.PurchaseRequests.Where(x => x.PrNo == number).Select(x => x.Status).SingleAsync());
        }
    }
}
