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
    [InlineData("before")]
    [InlineData("between")]
    [InlineData("final")]
    [InlineData("none")]
    public async Task PGT017_rfq_conversion_fences_each_purchase_order_and_the_final_rfq_update(string revocation)
    {
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        await GrantLegacyAuthorityAction(h, "pur-rfq", "convert");
        var db = h.Db;
        var suffix = h.Tenant.ToString("N")[..12];
        var number = "RFQ" + suffix;
        var firstSupplier = "A" + suffix;
        var secondSupplier = "B" + suffix;
        const decimal quantity = 3.12345678m;
        const decimal price = 12.1234m;
        var orderDate = DateTime.Today;
        db.BusinessPartners.AddRange(
            new() { BpCd = firstSupplier, BpName = "first supplier", SupplierFlg = true },
            new() { BpCd = secondSupplier, BpName = "second supplier", SupplierFlg = true });
        db.Pub_DocSequences.Add(new() { BizKey = "PO", Prefix = "PO" + suffix, SeqLength = 4 });
        db.Rfqs.Add(new()
        {
            RfqNo = number, RfqDate = orderDate, Status = RfqStatus.Selected,
            Lines = [
                new() { RfqNo = number, LineNo = 1, ItemId = "first", Qty = quantity, RequiredDate = orderDate.AddDays(3) },
                new() { RfqNo = number, LineNo = 2, ItemId = "second", Qty = 9m }
            ],
            Quotes = [
                new() { RfqNo = number, LineNo = 1, SupplierId = firstSupplier, QuotedPrice = price, IsSelected = true },
                new() { RfqNo = number, LineNo = 2, SupplierId = secondSupplier, QuotedPrice = 7.5m, IsSelected = true }
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
        if (revocation == "none")
        {
            using var result = await Convert();
            Assert.True(result.StatusCode == HttpStatusCode.OK, await OperationsFailure(result));
            var converted = await Effects();
            Assert.Equal(2, converted.Orders.Length);
            Assert.Equal(2, converted.Lines.Length);
            Assert.Equal(2, converted.Number);
            Assert.Equal(RfqStatus.Converted, converted.Status);
            Assert.NotNull(converted.ModifiedAt);
            Assert.Equal(policy, await ReadPolicyWrites(h, revoke.OperationId));
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
            if (revocation == "before")
            {
                await using var table = new NpgsqlCommand(
                    """LOCK TABLE public."Sys_RoleAction" IN SHARE MODE""", barrier, hold);
                await table.ExecuteNonQueryAsync();
                revokeWrite = Revoke();
                var iam = await WaitForFence(observer, barrier.ProcessID, "%DELETE%Sys_RoleAction%", null, revokeWrite);
                write = Convert();
                await WaitForFence(observer, iam.Backend, "%cp6_authentication_v1.acquire()%", null, write);
            }
            else if (revocation == "between")
            {
                await using var table = new NpgsqlCommand(
                    """LOCK TABLE public."Pur_PurchaseOrder" IN SHARE MODE""", barrier, hold);
                await table.ExecuteNonQueryAsync();
                write = Convert();
                var purchasing = await WaitForFence(observer, barrier.ProcessID, "%INSERT%Pur_PurchaseOrder%", null, write);
                revokeWrite = Revoke();
                await WaitForFence(observer, purchasing.Backend, "%cp6_authentication_v1.acquire()%", null, revokeWrite);
            }
            else
            {
                await using var table = new NpgsqlCommand(
                    """LOCK TABLE public."Pur_Rfq" IN SHARE MODE""", barrier, hold);
                await table.ExecuteNonQueryAsync();
                write = Convert();
                var rfq = await WaitForFence(observer, barrier.ProcessID, "%UPDATE%Pur_Rfq%", null, write);
                revokeWrite = Revoke();
                await WaitForFence(observer, rfq.Backend, "%cp6_authentication_v1.acquire()%", null, revokeWrite);
            }

            if (revocation == "final")
            {
                var ordersCommitted = await Effects();
                Assert.Equal(2, ordersCommitted.Orders.Length);
                Assert.Equal(2, ordersCommitted.Lines.Length);
                Assert.Equal(2, ordersCommitted.Number);
                Assert.Equal(RfqStatus.Selected, ordersCommitted.Status);
                Assert.Null(ordersCommitted.ModifiedAt);
            }
            else
            {
                Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await Effects()));
            }
            Assert.Equal(policy, await ReadPolicyWrites(h, revoke.OperationId));
            await hold.RollbackAsync();
        }

        using var revoked = await revokeWrite;
        Assert.True(revoked.StatusCode == HttpStatusCode.OK, await OperationsFailure(revoked));
        using var conversion = await write;
        var expected = revocation == "final" ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
        Assert.True(conversion.StatusCode == expected, await OperationsFailure(conversion));
        var after = await ReadPolicyWrites(h, revoke.OperationId);
        Assert.NotEqual(policy.RoleRevision, after.RoleRevision);
        Assert.Equal(policy.Generation + 1, after.Generation);
        Assert.Equal(policy.Snapshot + 1, after.Snapshot);
        Assert.Equal(policy.Outbox + 1, after.Outbox);
        Assert.Equal(1, after.CurrentOutbox);
        Assert.Equal(1, after.Audits);
        Assert.Equal(1, after.Receipts);

        var committed = await Effects();
        if (revocation == "before")
        {
            Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(committed));
        }
        else if (revocation == "between")
        {
            var order = Assert.Single(committed.Orders);
            var line = Assert.Single(committed.Lines);
            Assert.Equal(firstSupplier, order.SupplierId);
            Assert.Equal(order.PoNo, line.PoNo);
            Assert.Equal(orderDate, order.OrderDate);
            Assert.Equal(number, order.SourceRfqNo);
            Assert.Equal("first", line.ItemId);
            Assert.Equal(quantity, line.Qty);
            Assert.Equal(price, line.UnitPrice);
            Assert.Equal(orderDate.AddDays(3), line.RequiredDate);
            Assert.Equal(1, committed.Number);
            Assert.Equal(RfqStatus.Selected, committed.Status);
            Assert.Null(committed.ModifiedAt);
        }
        else
        {
            Assert.Equal(2, committed.Orders.Length);
            Assert.Equal(2, committed.Lines.Length);
            Assert.Equal(2, committed.Number);
            Assert.Equal(RfqStatus.Converted, committed.Status);
            Assert.NotNull(committed.ModifiedAt);
        }

        using var stale = await Convert();
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        Assert.Equal(JsonSerializer.Serialize(committed), JsonSerializer.Serialize(await Effects()));

        Task<HttpResponseMessage> Convert() => host.Send(OperationsRequest(writer, HttpMethod.Post, $"api/pur/rfq/{number}/convert"));
        Task<HttpResponseMessage> Revoke() => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
        async Task<(PurchaseOrder[] Orders, PurchaseOrderLine[] Lines, int Number, RfqStatus Status, DateTime? ModifiedAt)> Effects()
        {
            var head = await db.Rfqs.AsNoTracking().SingleAsync(x => x.RfqNo == number);
            return (
                await db.PurchaseOrders.AsNoTracking().OrderBy(x => x.PoNo).ToArrayAsync(),
                await db.PurchaseOrderLines.AsNoTracking().OrderBy(x => x.PoNo).ThenBy(x => x.LineNo).ToArrayAsync(),
                await db.Pub_DocSequences.Where(x => x.BizKey == "PO").Select(x => x.CurrentValue).SingleAsync(),
                head.Status, head.ModifyDate);
        }
    }
}
