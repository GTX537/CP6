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
    [InlineData("pr", "add", true)]
    [InlineData("po", "add", true)]
    [InlineData("po", "cancel", true)]
    [InlineData("po", "add", false)]
    public async Task PGT017_purchasing_creation_and_cancellation_share_the_authority_transaction(
        string document, string action, bool revokeFirst)
    {
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        var db = h.Db;
        var resource = $"pur-{document}";
        if (!await db.Sys_Menus.AnyAsync(row => row.MenuKey == resource))
        {
            db.Sys_Menus.Add(new() { MenuId = document == "pr" ? 32746 : 32747, MenuKey = resource, MenuName = resource });
            await db.SaveChangesAsync();
        }
        await GrantLegacyAuthorityAction(h, resource, action);
        var suffix = h.Tenant.ToString("N")[..12];
        var number = "PO" + suffix;
        var supplier = "S" + suffix;
        const decimal quantity = 3.12345678m;
        const decimal price = 12.1234m;
        var marker = $"PGT017 {suffix}";
        var date = DateTime.Today;
        foreach (var key in new[] { "PR", "PO" })
            db.Pub_DocSequences.Add(new() { BizKey = key, Prefix = key + suffix, SeqLength = 4 });
        db.BusinessPartners.Add(new() { BpCd = supplier, BpName = "fixture supplier", SupplierFlg = true });
        db.PurchaseOrders.Add(new() { PoNo = number, SupplierId = supplier, OrderDate = date, Status = PoStatus.Confirmed });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", h.Tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, action);
        var before = await Effects();
        var authority = await ReadPolicyWrites(h, revoke.OperationId);
        var payload = new
        {
            supplierId = supplier,
            requestDate = date,
            orderDate = date,
            remarks = marker,
            lines = new[] { new { itemId = "part", qty = quantity, estPrice = price, unitPrice = price } }
        };

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
                write = Write();
                await WaitForFence(observer, iam.Backend, "%cp6_authentication_v1.acquire()%", null, write);
            }
            else
            {
                await using var table = new NpgsqlCommand(
                    """LOCK TABLE public."Pur_PurchaseOrder" IN SHARE MODE""", barrier, hold);
                await table.ExecuteNonQueryAsync();
                write = Write();
                var purchasing = await WaitForFence(observer, barrier.ProcessID, "%INSERT%Pur_PurchaseOrder%", null, write);
                revokeWrite = Revoke();
                await WaitForFence(observer, purchasing.Backend, "%cp6_authentication_v1.acquire()%", null, revokeWrite);
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
        Assert.Equal(1, after.CurrentOutbox);
        Assert.Equal(1, after.Audits);
        Assert.Equal(1, after.Receipts);

        using var result = await write;
        if (revokeFirst)
        {
            Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
            Assert.Equal(before, await Effects());
            await GrantLegacyAuthorityAction(h, resource, action);
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

        if (action == "cancel")
        {
            var retained = await db.PurchaseOrders.AsNoTracking().SingleAsync();
            Assert.Equal(number, retained.PoNo);
            Assert.Equal(PoStatus.Cancelled, retained.Status);
            Assert.All(await db.Pub_DocSequences.ToArrayAsync(), row => Assert.Equal(0, row.CurrentValue));
        }
        else if (document == "pr")
        {
            var request = await db.PurchaseRequests.AsNoTracking().SingleAsync();
            var line = await db.PurchaseRequestLines.AsNoTracking().SingleAsync();
            Assert.Equal(marker, request.Remarks);
            Assert.Equal(request.PrNo, line.PrNo);
            Assert.Equal(PrStatus.Draft, request.Status);
            Assert.Equal(quantity, line.Qty);
            Assert.Equal(price, line.EstPrice);
            Assert.Equal(1, await db.Pub_DocSequences.Where(row => row.BizKey == "PR").Select(row => row.CurrentValue).SingleAsync());
        }
        else
        {
            var order = await db.PurchaseOrders.AsNoTracking().SingleAsync(row => row.Remarks == marker);
            var line = await db.PurchaseOrderLines.AsNoTracking().SingleAsync();
            Assert.Equal(supplier, order.SupplierId);
            Assert.Equal(order.PoNo, line.PoNo);
            Assert.Equal(PoStatus.Draft, order.Status);
            Assert.Equal(quantity, line.Qty);
            Assert.Equal(price, line.UnitPrice);
            Assert.Equal(1, await db.Pub_DocSequences.Where(row => row.BizKey == "PO").Select(row => row.CurrentValue).SingleAsync());
        }

        Task<HttpResponseMessage> Write() => action == "cancel"
            ? host.Send(OperationsRequest(writer, HttpMethod.Post, $"api/pur/po/{number}/cancel"))
            : host.Send(OperationsRequest(writer, HttpMethod.Post, $"api/pur/{document}", payload));
        Task<HttpResponseMessage> Revoke() => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
        async Task<string> Effects() => JsonSerializer.Serialize(new
        {
            Requests = await db.PurchaseRequests.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            RequestLines = await db.PurchaseRequestLines.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Orders = await db.PurchaseOrders.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            OrderLines = await db.PurchaseOrderLines.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Numbers = await db.Pub_DocSequences.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        });
    }
}
