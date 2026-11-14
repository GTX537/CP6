using System.Net;
using CP6.Entity.DomainModels.Pur;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PGT017_goods_receipt_quality_application_and_revocation_use_one_transaction(bool revokeFirst)
    {
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        await GrantLegacyAuthorityAction(h, "pur-gr", "qc");
        var db = h.Db;
        var suffix = h.Tenant.ToString("N")[..12];
        var po = "PO" + suffix;
        var gr = "GR" + suffix;
        var inbound = "IN" + suffix;
        var inspection = "QC" + suffix;
        const decimal received = 3.12345678m;
        const decimal rejected = 0.00000002m;
        const decimal accepted = received - rejected;

        db.PurchaseOrders.Add(new()
        {
            PoNo = po, SupplierId = "supplier", Status = PoStatus.Confirmed, OrderDate = DateTime.Today,
            Lines = [new() { PoNo = po, LineNo = 1, ItemId = "part", Qty = received, ReceivedQty = received }]
        });
        db.GoodsReceipts.Add(new()
        {
            GrNo = gr, PoNo = po, SupplierId = "supplier", Status = GrStatus.Inspecting,
            ReceiptDate = DateTime.Today, WmsInboundNo = inbound,
            Lines = [new() { GrNo = gr, LineNo = 1, PoLineNo = 1, ItemId = "part", ReceivedQty = received }]
        });
        db.QcInspections.Add(new() { InspectionNo = inspection, InboundNo = inbound, Status = 2 });
        db.QcInspectionItems.Add(new()
        {
            InspectionNo = inspection, LineNo = 1, ProductCd = "part",
            ReceivedQty = received, AcceptedQty = accepted, RejectedQty = rejected
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", h.Tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, "qc");
        var before = await Effects();
        Assert.Equal((GrStatus.Inspecting, "PENDING", 0m, 0m, 0m, received, 0m, PoStatus.Confirmed), before);
        var authority = await ReadPolicyWrites(h, revoke.OperationId);

        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> write;
        Task<HttpResponseMessage> revokeWrite;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            var lockSql = revokeFirst
                ? """LOCK TABLE public."Sys_RoleAction" IN SHARE MODE"""
                : """LOCK TABLE public."Pur_PurchaseOrder" IN SHARE MODE""";
            await using var table = new NpgsqlCommand(lockSql, barrier, hold);
            await table.ExecuteNonQueryAsync();
            if (revokeFirst)
            {
                revokeWrite = Revoke();
                var iam = await WaitForFence(observer, barrier.ProcessID, "%DELETE%Sys_RoleAction%", null, revokeWrite);
                write = Write();
                await WaitForFence(observer, iam.Backend, "%cp6_authentication_v1.acquire()%", null, write);
            }
            else
            {
                write = Write();
                var purchasing = await WaitForFence(observer, barrier.ProcessID, "%UPDATE%Pur_PurchaseOrder%", null, write);
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
        Assert.Equal((1L, 1L, 1L), (after.CurrentOutbox, after.Audits, after.Receipts));

        using var result = await write;
        if (revokeFirst)
        {
            Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
            Assert.Contains("IAM_FORBIDDEN", await OperationsFailure(result), StringComparison.Ordinal);
            Assert.Equal(before, await Effects());
        }
        else
        {
            Assert.True(result.StatusCode == HttpStatusCode.OK, await OperationsFailure(result));
            var committed = (GrStatus.Completed, "PASS", accepted, rejected, accepted, received, 0m, PoStatus.Received);
            Assert.Equal(committed, await Effects());
            using var stale = await Write();
            Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
            Assert.Equal(committed, await Effects());
        }

        Task<HttpResponseMessage> Write() => host.Send(OperationsRequest(writer, HttpMethod.Post, $"api/pur/gr/{gr}/apply-qc"));
        Task<HttpResponseMessage> Revoke() => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
        async Task<(GrStatus, string, decimal, decimal, decimal, decimal, decimal, PoStatus)> Effects()
        {
            var receipt = await db.GoodsReceipts.AsNoTracking().SingleAsync(row => row.GrNo == gr);
            var line = await db.GoodsReceiptLines.AsNoTracking().SingleAsync(row => row.GrNo == gr);
            var order = await db.PurchaseOrders.AsNoTracking().SingleAsync(row => row.PoNo == po);
            var orderLine = await db.PurchaseOrderLines.AsNoTracking().SingleAsync(row => row.PoNo == po);
            return (receipt.Status, line.QcStatus, line.AcceptedQty, line.RejectedQty,
                orderLine.AcceptedQty, orderLine.ReceivedQty, orderLine.InvoicedQty, order.Status);
        }
    }
}
