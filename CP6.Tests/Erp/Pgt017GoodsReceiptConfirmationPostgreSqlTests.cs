using System.Net;
using System.Text.Json;
using CP6.Core.Services.Pur;
using CP6.Entity.DomainModels.Pur;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const decimal ConfirmedReceiptQuantity = 3.12345678m;

    [TenantManagementPostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PGT017_goods_receipt_confirmation_and_revocation_use_one_transaction(bool revokeFirst)
    {
        await using var h = await Seed();
        var (menuId, request) = await SeedReceiptConfirmation(h);
        var db = h.Db;
        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", h.Tenant.ToString("N"));
        var original = await RevokeActions(host, manager);
        var revoke = original with { Actions = original.Actions!.Where(x => x.MenuId != menuId || x.ActionCode != "add").ToList() };
        var before = await ReadReceiptConfirmation(h);
        var authority = await ReadPolicyWrites(h, revoke.OperationId);

        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> write;
        Task<HttpResponseMessage> revokeWrite;
        ReceiptConfirmation whileBlocked;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            var lockSql = revokeFirst
                ? """LOCK TABLE public."Sys_RoleAction" IN SHARE MODE"""
                : """LOCK TABLE public."Pur_GoodsReceipt" IN SHARE MODE""";
            await using var table = new NpgsqlCommand(lockSql, barrier, hold);
            await table.ExecuteNonQueryAsync();
            if (revokeFirst)
            {
                revokeWrite = Revoke();
                var iam = await WaitForFence(observer, barrier.ProcessID, "%DELETE%Sys_RoleAction%", null, revokeWrite);
                write = Write();
                await WaitForFence(observer, iam.Backend, "%cp6_authentication_v1.acquire()%", null, write);
                whileBlocked = await ReadReceiptConfirmation(h);
            }
            else
            {
                write = Write();
                var purchase = await WaitForFence(observer, barrier.ProcessID, "%INSERT%Pur_GoodsReceipt%", null, write);
                whileBlocked = await ReadReceiptConfirmation(h);
                Assert.Equal(before, whileBlocked);
                revokeWrite = Revoke();
                await WaitForFence(observer, purchase.Backend, "%cp6_authentication_v1.acquire()%", null, revokeWrite);
            }
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
            Assert.True(result.StatusCode == HttpStatusCode.Forbidden, await OperationsFailure(result));
            Assert.Equal(before, whileBlocked);
            Assert.Equal(before, await ReadReceiptConfirmation(h));
        }
        else
        {
            Assert.True(result.StatusCode == HttpStatusCode.OK, await OperationsFailure(result));
            var effects = await ReadReceiptConfirmation(h);
            Assert.Equal(new(1, 1, 1, 1, 1, 1, 1, 1, 1, 3,
                ConfirmedReceiptQuantity, ConfirmedReceiptQuantity, ConfirmedReceiptQuantity, PoStatus.Received), effects);
            var gr = await db.GoodsReceipts.AsNoTracking().SingleAsync();
            var grLine = await db.GoodsReceiptLines.AsNoTracking().SingleAsync();
            var inbound = await db.InboundOrders.AsNoTracking().SingleAsync();
            var receipt = await db.InboundReceipts.AsNoTracking().SingleAsync();
            var receiptLine = await db.InboundReceiptDetails.AsNoTracking().SingleAsync();
            var stock = await db.StockTransactions.AsNoTracking().SingleAsync();
            Assert.Equal((request.PoNo, inbound.InboundNo, GrStatus.Completed), (gr.PoNo, gr.WmsInboundNo, gr.Status));
            Assert.Equal((7, receipt.ReceiptNo, ConfirmedReceiptQuantity), (grLine.PoLineNo, grLine.WmsReceiptDetailRef, grLine.AcceptedQty));
            Assert.Equal((inbound.InboundNo, stock.TxnNo, 1), (receipt.InboundNo, receiptLine.StockTxnNo, receiptLine.RefOrderLineNo));
            Assert.Equal((InboundOrderStatus.Completed, receipt.ReceiptNo, "RECV", "price-owner"),
                (inbound.Status, stock.RelatedNo, stock.LocationCd, stock.Creator));
            using var stale = await Write();
            Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
            Assert.Equal(effects, await ReadReceiptConfirmation(h));
        }

        Task<HttpResponseMessage> Write() => host.Send(OperationsRequest(writer, HttpMethod.Post, "api/pur/gr", request));
        Task<HttpResponseMessage> Revoke() => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_goods_receipt_confirmation_commit_failure_leaves_no_warehouse_effect()
    {
        await using var h = await Seed();
        var (_, request) = await SeedReceiptConfirmation(h);
        var db = h.Db;
        var before = await ReadReceiptConfirmation(h);
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.cp6_test_purchase_receipt_reject() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'purchase_receipt_fixture_commit_rejected' USING ERRCODE='23514'; END $$;
            CREATE CONSTRAINT TRIGGER cp6_test_purchase_receipt_reject
            AFTER INSERT ON public."Pur_GoodsReceipt" DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION public.cp6_test_purchase_receipt_reject();
            """);
        try
        {
            using var refused = await host.Send(OperationsRequest(writer, HttpMethod.Post, "api/pur/gr", request));
            Assert.Equal(before, await ReadReceiptConfirmation(h));
            Assert.True(refused.StatusCode == HttpStatusCode.ServiceUnavailable, await OperationsFailure(refused));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER cp6_test_purchase_receipt_reject ON public."Pur_GoodsReceipt";
                DROP FUNCTION public.cp6_test_purchase_receipt_reject();
                """);
        }
    }

    [TenantManagementPostgresTheory]
    [InlineData("post", "ACTION_FORBIDDEN")]
    [InlineData("warehouse", "OBJECT_FORBIDDEN")]
    [InlineData("price", "FIELD_WRITE_FORBIDDEN")]
    public async Task PGT017_goods_receipt_confirmation_preserves_warehouse_authority(string denied, string reason)
    {
        await using var h = await Seed();
        var (_, request) = await SeedReceiptConfirmation(h);
        var db = h.Db;
        switch (denied)
        {
            case "post":
                await db.Sys_RoleActions.Where(row => row.RoleId == 72 && row.MenuId == 406 && row.ActionCode == "post").ExecuteDeleteAsync();
                break;
            case "warehouse":
                await db.WmsRoleScopes.ExecuteUpdateAsync(set => set.SetProperty(row => row.WarehouseCd, "OTHER"));
                break;
            case "price":
                db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-inbound-receipt", FieldName = "UnitPrice", Access = 3 });
                await db.SaveChangesAsync();
                break;
        }
        var before = await ReadReceiptConfirmation(h);
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);
        using var refused = await host.Send(OperationsRequest(writer, HttpMethod.Post, "api/pur/gr", request));
        Assert.True(refused.StatusCode == HttpStatusCode.Forbidden, await OperationsFailure(refused));
        Assert.Contains(reason, await OperationsFailure(refused), StringComparison.Ordinal);
        Assert.Equal(before, await ReadReceiptConfirmation(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_goods_receipt_confirmation_recovers_lost_commit_by_original_key()
    {
        await using var h = await Seed();
        var (menuId, request) = await SeedReceiptConfirmation(h, orderedQuantity: 2 * ConfirmedReceiptQuantity);
        var db = h.Db;
        var settings = IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant);
        var key = Guid.NewGuid();
        var path = $"api/pur/gr/operations/{key:D}";
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!, settings);
        var writer = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", h.Tenant.ToString("N"));
        using (var scope = await host.Send(OperationsRequest(writer, HttpMethod.Get, "api/pur/gr/operations/scope")))
        {
            Assert.True(scope.StatusCode == HttpStatusCode.OK, await OperationsFailure(scope));
            Assert.True(scope.Headers.CacheControl?.NoStore);
            using var data = JsonDocument.Parse(await scope.Content.ReadAsStringAsync());
            var authority = data.RootElement.GetProperty("data");
            Assert.Equal(h.Tenant, authority.GetProperty("tenantId").GetGuid());
            Assert.Equal(h.User, authority.GetProperty("actorId").GetGuid());
        }
        using (var unread = await host.Send(OperationsRequest(writer, HttpMethod.Get, path)))
        {
            Assert.True(unread.StatusCode == HttpStatusCode.OK, await OperationsFailure(unread));
            using var data = JsonDocument.Parse(await unread.Content.ReadAsStringAsync());
            Assert.Equal("UNKNOWN", data.RootElement.GetProperty("data").GetProperty("state").GetString());
        }
        var connection = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()) { SslMode = SslMode.Disable };
        await using (var relay = new StockPostgresCommitRelay(connection.Port, connection.Username!))
        {
            connection.Port = relay.Port;
            await using var forwarded = await AuthenticationApiProcess.Start(connection.ConnectionString, settings);
            var session = await Login(forwarded, h);
            relay.Arm();
            using var unknown = await SendConfirmation(forwarded, session);
            Assert.True(unknown.StatusCode == HttpStatusCode.ServiceUnavailable, await OperationsFailure(unknown));
            Assert.Contains("OPERATIONS_OUTCOME_UNKNOWN", await OperationsFailure(unknown), StringComparison.Ordinal);
            await relay.Dropped.WaitAsync(TimeSpan.FromSeconds(5));
        }
        var before = await ReadReceiptConfirmation(h);
        Assert.Equal((1, 1, ConfirmedReceiptQuantity, PoStatus.PartiallyReceived),
            (before.ProcurementReceipts, before.StockMoves, before.Received, before.OrderStatus));
        var gr = await db.GoodsReceipts.AsNoTracking().SingleAsync();

        using (var replay = await SendConfirmation(host, writer))
        {
            Assert.True(replay.StatusCode == HttpStatusCode.OK, await OperationsFailure(replay));
            using var data = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
            Assert.Equal(gr.Id, data.RootElement.GetProperty("data").GetProperty("id").GetGuid());
        }
        request.Lines[0].ReceivedQty -= 0.00000001m;
        using (var conflict = await SendConfirmation(host, writer))
        {
            Assert.True(conflict.StatusCode == HttpStatusCode.Conflict, await OperationsFailure(conflict));
            Assert.Contains("ORIGINAL_OPERATION_INPUT_CONFLICT", await OperationsFailure(conflict), StringComparison.Ordinal);
        }
        var command = await RevokeActions(host, manager);
        var revoke = command with { Actions = command.Actions!.Where(x => x.MenuId != menuId || x.ActionCode != "add").ToList() };
        using (var revoked = await host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke)))
            Assert.True(revoked.StatusCode == HttpStatusCode.OK, await OperationsFailure(revoked));
        using (var read = await host.Send(OperationsRequest(writer, HttpMethod.Get, path)))
        {
            Assert.True(read.StatusCode == HttpStatusCode.OK, await OperationsFailure(read));
            using var data = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
            var operation = data.RootElement.GetProperty("data");
            Assert.Equal(key, operation.GetProperty("operationId").GetGuid());
            Assert.Equal("COMMITTED", operation.GetProperty("state").GetString());
            Assert.Equal(gr.Id, operation.GetProperty("receipt").GetProperty("id").GetGuid());
        }
        using var denied = await SendConfirmation(host, writer);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(before, await ReadReceiptConfirmation(h));
        Assert.Equal(1, await db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM cp6_main_owner_v1.operations
            WHERE tenant_id={h.Tenant} AND owner='Main.Pur.GoodsReceipt' AND operation_id={key}
            """).SingleAsync());

        async Task<HttpResponseMessage> SendConfirmation(AuthenticationApiProcess api, Dictionary<string, string> session)
        {
            using var message = OperationsRequest(session, HttpMethod.Post, "api/pur/gr", request);
            message.Headers.Add("Idempotency-Key", key.ToString("D"));
            return await api.Send(message);
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_goods_receipt_confirmation_combined_source_lines_cannot_overreceive()
    {
        await using var h = await Seed();
        var (_, request) = await SeedReceiptConfirmation(h);
        var before = await ReadReceiptConfirmation(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);
        request.Lines = [new() { PoLineNo = 7, ReceivedQty = 2m }, new() { PoLineNo = 7, ReceivedQty = 2m }];
        using (var rejected = await host.Send(OperationsRequest(writer, HttpMethod.Post, "api/pur/gr", request)))
        {
            Assert.True(rejected.StatusCode == HttpStatusCode.BadRequest, await OperationsFailure(rejected));
            Assert.Contains("E-PUR-031", await OperationsFailure(rejected), StringComparison.Ordinal);
            Assert.Equal(before, await ReadReceiptConfirmation(h));
        }
        request.Lines[1].ReceivedQty = ConfirmedReceiptQuantity - 2m;
        using var accepted = await host.Send(OperationsRequest(writer, HttpMethod.Post, "api/pur/gr", request));
        Assert.True(accepted.StatusCode == HttpStatusCode.OK, await OperationsFailure(accepted));
        var after = await ReadReceiptConfirmation(h);
        Assert.Equal((1, 2, 2, ConfirmedReceiptQuantity), (after.ProcurementReceipts, after.ProcurementLines, after.StockMoves, after.Received));
    }

    private static async Task<(int MenuId, GrCreateDto Request)> SeedReceiptConfirmation(Harness h, decimal orderedQuantity = ConfirmedReceiptQuantity)
    {
        await GrantProductionInbound(h);
        await SeedFenceAdministrator(h);
        await GrantLegacyAuthorityAction(h, "pur-gr", "add");
        var db = h.Db;
        var suffix = h.Tenant.ToString("N")[..12];
        var number = "PO" + suffix;
        db.Pub_DocSequences.Add(new() { BizKey = "GR", Prefix = "GR" + suffix, SeqLength = 4 });
        db.PurchaseOrders.Add(new()
        {
            PoNo = number, SupplierId = "supplier", PostingBasis = "1", Status = PoStatus.Confirmed, OrderDate = DateTime.Today,
            Lines = [new() { PoNo = number, LineNo = 7, ItemId = "PART", Qty = orderedQuantity }]
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var menuId = await db.Sys_Menus.Where(row => row.MenuKey == "pur-gr").Select(row => row.MenuId).SingleAsync();
        return (menuId, new() { PoNo = number, WarehouseCd = "W01", Lines = [new() { PoLineNo = 7, ReceivedQty = ConfirmedReceiptQuantity }] });
    }

    private sealed record ReceiptConfirmation(int ProcurementReceipts, int ProcurementLines, int Plans, int PlanLines,
        int WarehouseReceipts, int WarehouseLines, int Stocks, int StockMoves, int PurchaseSequence, int WarehouseSequences, decimal Received,
        decimal Accepted, decimal Physical, PoStatus OrderStatus);

    private static async Task<ReceiptConfirmation> ReadReceiptConfirmation(Harness h)
    {
        var db = h.Db;
        var line = await db.PurchaseOrderLines.AsNoTracking().SingleAsync();
        return new(await db.GoodsReceipts.CountAsync(), await db.GoodsReceiptLines.CountAsync(),
            await db.InboundOrders.CountAsync(), await db.InboundOrderDetails.CountAsync(),
            await db.InboundReceipts.CountAsync(), await db.InboundReceiptDetails.CountAsync(),
            await db.Stocks.CountAsync(), await db.StockTransactions.CountAsync(),
            await db.Pub_DocSequences.Where(row => row.BizKey == "GR").Select(row => row.CurrentValue).SingleAsync(),
            await db.WmsSequences.CountAsync(),
            line.ReceivedQty, line.AcceptedQty, await db.Stocks.SumAsync(row => row.PhysicalQty),
            await db.PurchaseOrders.Select(row => row.Status).SingleAsync());
    }
}
