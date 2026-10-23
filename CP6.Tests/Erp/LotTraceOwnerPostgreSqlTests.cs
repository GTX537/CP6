using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Common;
using CP6.Entity.DTOs.Wms;
using System.Net;
using System.Text.Json;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static OperationsOwnerV1 LotOwner(Harness h)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var (resource, fields) in new[] {
            ("wms-lot-trace", new[] { "OperatorCd", "Remark", "CustomerName", "SupplierName" }),
            ("wms-stock", new[] { "UnitPrice" }), ("wms-outbound-order", new[] { "UnitPrice" }),
            ("wms-inbound-order", new[] { "UnitPrice" }), ("wms-inbound-receipt", new[] { "UnitPrice" }),
            ("mes-work-order", Array.Empty<string>()), ("order", new[] { "UnitPrice", "Amount" }) })
        {
            catalog.RegisterScope(resource, resource, [1, 2, 3, 4, 5], 1);
            catalog.RegisterFields(resource, fields.Select(f => new FieldRegistry.Field(f, f)));
        }
        catalog.Seal(); return new(h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
    }
    private static async Task<OperationsOwnerV1> GrantLot(Harness h)
    {
        await GrantOperations(h);
        foreach (var (key, id, actions) in new[] { ("wms-lot-trace", 427, new[] { "query", "recall" }),
            ("wms-stock", 403, new[] { "query" }), ("wms-inbound-order", 405, new[] { "query" }), ("wms-inbound-receipt", 406, new[] { "query" }) })
        {
            if (!await h.Db.Sys_Menus.AnyAsync(m => m.MenuId == id)) h.Db.Sys_Menus.Add(new() { MenuId = id, MenuKey = key, MenuName = key });
            foreach (var action in actions)
            {
                if (!await h.Db.Sys_MenuActions.AnyAsync(a => a.MenuId == id && a.ActionCode == action)) h.Db.Sys_MenuActions.Add(new() { MenuId = id, ActionCode = action, ActionName = action });
                h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = id, ActionCode = action });
            }
            h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = key, ScopeType = 5 });
        }
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); return LotOwner(h);
    }
    private static async Task SeedLot(Harness h)
    {
        var prefix = "L" + h.Tenant.ToString("N")[..8]; var wh = prefix; var loc = prefix + "A"; var second = prefix + "B";
        const decimal max = 9999999999999.99999999m;
        h.Db.Warehouses.Add(new() { WarehouseCd = wh, WarehouseName = wh });
        h.Db.Locations.AddRange(new Location { WarehouseCd = wh, LocationCd = loc, AreaCd = "A" }, new Location { WarehouseCd = wh, LocationCd = second, AreaCd = "B" });
        foreach (var location in new[] { loc, second }) h.Db.Stocks.Add(new() { WarehouseCd = wh, LocationCd = location, ProductCd = "MAT", LotNo = "LOT",
            PhysicalQty = max, AvailableQty = max, Creator = "price-owner", UnitPrice = 99m, QcStatus = StockQcStatus.Passed, ExpiryDate = new(2027, 1, 1) });
        h.Db.StockTransactions.AddRange(new StockTransaction { TxnNo = prefix + "OUT", TxnType = WmsTxnType.OUT, WarehouseCd = wh, LocationCd = loc,
            ProductCd = "MAT", LotNo = "LOT", Qty = max, RelatedType = "OUTBOUND", RelatedNo = prefix + "O", OperatorCd = "private-operator", Remark = "private remark", Creator = "price-owner" },
            new StockTransaction { TxnNo = prefix + "IN", TxnType = WmsTxnType.IN, WarehouseCd = wh, LocationCd = loc,
                ProductCd = "MAT", LotNo = "LOT", Qty = 0.00000001m, RelatedType = "INBOUND", RelatedNo = prefix + "R", Creator = "price-owner" });
        h.Db.OutboundOrders.Add(new() { OutboundNo = prefix + "O", WarehouseCd = wh, OutboundType = OutboundType.Shipping, Status = OutboundOrderStatus.Completed,
            CustomerCd = "CUSTOMER", CustomerName = "private customer", Creator = "price-owner" });
        h.Db.OutboundOrderDetails.Add(new() { OutboundNo = prefix + "O", LineNo = 1, ProductCd = "MAT", LotNo = "LOT", LocationCd = loc, ShipTxnNo = prefix + "OUT", RequiredQty = max, ShippedQty = max, Creator = "price-owner" });
        h.Db.InboundOrders.Add(new() { InboundNo = prefix + "I", WarehouseCd = wh, SupplierCd = "SUPPLIER", SupplierName = "private supplier", ExpectedArrivalDate = new(2026, 1, 1), Creator = "price-owner" });
        h.Db.InboundOrderDetails.Add(new() { InboundNo = prefix + "I", LineNo = 1, ProductCd = "MAT", ExpectedLocationCd = loc, ExpectedQty = 1, Creator = "price-owner" });
        h.Db.InboundReceipts.Add(new() { ReceiptNo = prefix + "R", InboundNo = prefix + "I", WarehouseCd = wh, Status = 1, Creator = "price-owner" });
        h.Db.InboundReceiptDetails.Add(new() { ReceiptNo = prefix + "R", LineNo = 1, ProductCd = "MAT", LotNo = "LOT", LocationCd = loc, StockTxnNo = prefix + "IN", ReceivedQty = 0.00000001m, Creator = "price-owner" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }
    private static LotRecallCommandV1 Recall(LotTraceSnapshotV1 snapshot, bool flag = true) => new()
        { ProductCd = snapshot.ProductCd, LotNo = snapshot.LotNo, RecallVersion = snapshot.Stock!.RecallVersion, Flag = flag };
    [CurrentPermissionPostgresFact]
    public async Task Lot_trace_owner_missing_authority_rejects_even_empty_trace()
    {
        await using var h = await Seed();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new LotTraceService(h.Db).TraceForwardAsync("MAT", "LOT"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Lot_trace_owner_missing_authority_rejects_even_empty_summary()
    {
        await using var h = await Seed();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new LotTraceService(h.Db).GetStockSummaryAsync("MAT", "LOT"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Lot_trace_owner_missing_authority_cannot_mark_all_current_stock_recalled()
    {
        await using var h = await Seed(); await GrantOperations(h);
        h.Db.Stocks.Add(new Stock { WarehouseCd = "W01", LocationCd = "L01", ProductCd = "MAT", LotNo = "LOT",
            PhysicalQty = 10, AvailableQty = 10, Creator = "other" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new LotTraceService(h.Db).SetRecallFlagAsync("MAT", "LOT", true, "forged"));
        Assert.False(await h.Db.Stocks.Select(x => x.RecallFlag).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Lot_trace_owner_exact_quantities_actual_sources_and_whole_set_recall_preserve_inventory()
    {
        await using var h = await Seed(); var owner = await GrantLot(h); await SeedLot(h); var service = new LotTraceService(h.Db, owner);
        var forward = await service.ReadOwnerAsync("MAT", "LOT", "FORWARD"); Assert.Equal(2, forward.Nodes.Count);
        Assert.Equal("19999999999999.99999998", forward.Stock!.PhysicalQty); Assert.Equal("19999999999999.99999998", forward.Stock.AvailableQty);
        Assert.Equal("9999999999999.99999999", Assert.Single(forward.Customers).Qty);
        var backward = await service.ReadOwnerAsync("MAT", "LOT", "BACKWARD"); Assert.Equal("0.00000001", Assert.Single(backward.Suppliers).Qty);
        Assert.NotEqual(backward.Suppliers[0].ReceiptNo, backward.Suppliers[0].InboundNo);
        var saved = await service.RecallOwnerAsync(Recall(forward)); Assert.Equal(2, saved.AffectedStocks);
        Assert.All(await h.Db.Stocks.AsNoTracking().ToArrayAsync(), s => { Assert.True(s.RecallFlag); Assert.Equal("price-owner", s.Modifier); Assert.Equal(9999999999999.99999999m, s.PhysicalQty); Assert.Equal(0m, s.AllocatedQty); Assert.Equal(99m, s.UnitPrice); Assert.Equal(StockQcStatus.Passed, s.QcStatus); });
        Assert.Equal(2, await h.Db.StockTransactions.CountAsync());
        Assert.Equal("ROW_VERSION_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.RecallOwnerAsync(Recall(forward, false)))).Message);
        var current = await service.ReadOwnerAsync("MAT", "LOT", "FORWARD"); Assert.Equal("ALL", current.Stock!.RecallState);
        await service.RecallOwnerAsync(Recall(current, false)); Assert.All(await h.Db.Stocks.AsNoTracking().ToArrayAsync(), s => Assert.False(s.RecallFlag));
    }

    [CurrentPermissionPostgresFact]
    public async Task Lot_trace_owner_tenant_self_source_and_warehouse_intersect_before_counts_and_whole_write()
    {
        await using var h = await Seed(); var owner = await GrantLot(h); await SeedLot(h); await using var foreign = await Seed(); await SeedLot(foreign);
        var service = new LotTraceService(h.Db, owner); var full = await service.ReadOwnerAsync("MAT", "LOT", "FORWARD"); Assert.Equal(2, full.Stock!.StockCount);
        var id = await h.Db.Stocks.OrderBy(s => s.LocationCd).Select(s => s.Id).FirstAsync();
        await h.Db.Stocks.Where(s => s.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Creator, "other"));
        await h.Db.Sys_RoleDataScopes.Where(s => s.ResourceKey == "wms-lot-trace").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
        var partial = await service.ReadOwnerAsync("MAT", "LOT", "FORWARD"); Assert.Equal(1, partial.Stock!.StockCount);
        Assert.Equal("OBJECT_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.RecallOwnerAsync(Recall(partial)))).Message);
        Assert.All(await h.Db.Stocks.AsNoTracking().ToArrayAsync(), s => Assert.False(s.RecallFlag));
        await h.Db.Sys_RoleDataScopes.Where(s => s.ResourceKey == "wms-lot-trace").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 5));
        await h.Db.Sys_RoleDataScopes.Where(s => s.ResourceKey == "wms-stock").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
        Assert.Equal(1, (await service.ReadOwnerAsync("MAT", "LOT", "FORWARD")).Stock!.StockCount);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.AreaCd, "OTHER"));
        var hidden = await service.ReadOwnerAsync("MAT", "LOT", "FORWARD"); Assert.Null(hidden.Stock); Assert.Empty(hidden.Nodes); Assert.Empty(hidden.Customers);
        await h.Db.Sys_RoleActions.Where(s => s.MenuId == 403 && s.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.ReadOwnerAsync("MAT", "LOT", "FORWARD"))).Message);
    }

    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Lot_trace_owner_department_binding_uses_stock_and_transaction_ids_independently(int scope)
    {
        await using var h = await Seed(); var owner = await GrantLot(h); await SeedLot(h); var admin = await MappingAdmin(h); await StandaloneScope(h, admin, scope);
        var service = new LotTraceService(h.Db, owner); var empty = await service.ReadOwnerAsync("MAT", "LOT", "FORWARD"); Assert.Null(empty.Stock); Assert.Empty(empty.Nodes);
        var stock = await h.Db.Stocks.Select(s => s.Id).FirstAsync(); var transaction = await h.Db.StockTransactions.Select(s => s.Id).FirstAsync();
        await BindStandalone(h, admin, "stock", stock); var one = await service.ReadOwnerAsync("MAT", "LOT", "FORWARD"); Assert.Equal(1, one.Stock!.StockCount); Assert.Empty(one.Nodes);
        await BindStandalone(h, admin, "stock-transaction", transaction); Assert.Single((await service.ReadOwnerAsync("MAT", "LOT", "FORWARD")).Nodes);
        Assert.Equal("OBJECT_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.RecallOwnerAsync(Recall(one)))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Lot_trace_owner_current_fields_and_actual_document_links_control_party_projection()
    {
        await using var h = await Seed(); var owner = await GrantLot(h); await SeedLot(h); var service = new LotTraceService(h.Db, owner);
        foreach (var field in new[] { "OperatorCd", "Remark", "CustomerName", "SupplierName" })
            h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-lot-trace", FieldName = field, Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var forward = await service.ReadOwnerAsync("MAT", "LOT", "FORWARD"); Assert.False(forward.OperatorReadable); Assert.False(forward.RemarkReadable);
        Assert.All(forward.Nodes, n => { Assert.Null(n.OperatorCd); Assert.Null(n.Remark); }); Assert.Null(Assert.Single(forward.Customers).CustomerName);
        Assert.Equal("CUSTOMER", forward.Customers[0].CustomerCd);
        var backward = await service.ReadOwnerAsync("MAT", "LOT", "BACKWARD"); Assert.Null(Assert.Single(backward.Suppliers).SupplierName);
        await h.Db.OutboundOrderDetails.ExecuteUpdateAsync(s => s.SetProperty(x => x.ShipTxnNo, "not-the-transaction"));
        Assert.Empty((await service.ReadOwnerAsync("MAT", "LOT", "FORWARD")).Customers);
        await h.Db.Sys_RoleActions.Where(s => s.MenuId == 405 && s.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Empty((await service.ReadOwnerAsync("MAT", "LOT", "BACKWARD")).Suppliers);
        await h.Db.Sys_RoleActions.Where(s => s.MenuId == 408 && s.ActionCode == "query").ExecuteDeleteAsync();
        Assert.NotEmpty((await service.ReadOwnerAsync("MAT", "LOT", "FORWARD")).Nodes);
    }

    [CurrentPermissionPostgresFact]
    public async Task Lot_trace_owner_changed_stock_set_and_existing_stock_version_reject_without_partial_flags()
    {
        await using var h = await Seed(); var owner = await GrantLot(h); await SeedLot(h); var service = new LotTraceService(h.Db, owner);
        var original = await service.ReadOwnerAsync("MAT", "LOT", "FORWARD"); var wh = await h.Db.Stocks.Select(s => s.WarehouseCd).FirstAsync();
        var added = new Stock { WarehouseCd = wh, LocationCd = "NEW", ProductCd = "MAT", LotNo = "LOT", Creator = "price-owner" };
        h.Db.Stocks.Add(added); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal("ROW_VERSION_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.RecallOwnerAsync(Recall(original)))).Message);
        var newer = await service.ReadOwnerAsync("MAT", "LOT", "FORWARD"); await h.Db.Stocks.Where(s => s.Id == added.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.QcStatus, StockQcStatus.Hold));
        Assert.Equal("ROW_VERSION_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.RecallOwnerAsync(Recall(newer)))).Message);
        Assert.All(await h.Db.Stocks.AsNoTracking().ToArrayAsync(), s => Assert.False(s.RecallFlag));
        await h.Db.Stocks.Where(s => s.Id == added.Id).ExecuteDeleteAsync();
        Assert.Equal("ROW_VERSION_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.RecallOwnerAsync(Recall(newer)))).Message);
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Lot_trace_owner_final_failure_rolls_back_and_lost_commit_is_unknown(bool lostAck)
    {
        var hook = new CommandHook(); var commit = new CommitFault(); await using var h = await Seed(hook, commit);
        var owner = await GrantLot(h); await SeedLot(h); var service = new LotTraceService(h.Db, owner); var snapshot = await service.ReadOwnerAsync("MAT", "LOT", "FORWARD");
        var reads = 0;
        if (lostAck) commit.Armed = true;
        else hook.Before = c => { if (c.CommandText.Contains("WITH field_subject") && ++reads == 3) throw new IOException("final recall authority unavailable"); return Task.CompletedTask; };
        var error = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.RecallOwnerAsync(Recall(snapshot))); hook.Before = null;
        Assert.Equal(lostAck ? "OPERATIONS_OUTCOME_UNKNOWN" : "AUTHORITY_FENCE_UNAVAILABLE", error.Message);
        Assert.All(await h.Db.Stocks.AsNoTracking().ToArrayAsync(), s => Assert.Equal(lostAck, s.RecallFlag));
    }

    [CurrentPermissionPostgresFact]
    public async Task Lot_trace_owner_commands_freeze_and_legacy_dirty_or_outer_context_cannot_bypass()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var owner = await GrantLot(h); await SeedLot(h); var service = new LotTraceService(h.Db, owner);
        var snapshot = await service.ReadOwnerAsync("MAT", "LOT", "FORWARD"); var command = Recall(snapshot);
        hook.Before = c => { if (c.CommandText.Contains("WITH field_subject")) { hook.Before = null; command.ProductCd = "FORGED"; command.Flag = false; } return Task.CompletedTask; };
        await service.RecallOwnerAsync(command); Assert.All(await h.Db.Stocks.AsNoTracking().ToArrayAsync(), s => Assert.True(s.RecallFlag));
        Assert.Equal("ROW_VERSION_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SetRecallFlagAsync("MAT", "LOT", false, "forged"))).Message);
        Assert.Equal("OWNER_READ_VERSION_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.TraceBackwardAsync("MAT", "LOT"))).Message);
        await using (var tx = await h.Db.Database.BeginTransactionAsync()) await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.ReadOwnerAsync("MAT", "LOT", "FORWARD"));
        await h.Db.Stocks.FirstAsync(); await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.ReadOwnerAsync("MAT", "LOT", "FORWARD")); h.Db.ChangeTracker.Clear();
    }

    [CurrentPermissionPostgresFact]
    public async Task Lot_trace_owner_actual_program_http_preserves_exact_version_and_current_revoke()
    {
        await using var h = await Seed(); await GrantLot(h); await SeedLot(h); await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/lot-trace/owner-v1?productCd=MAT&lotNo=LOT&direction=FORWARD"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var stock = body.RootElement.GetProperty("data").GetProperty("stock");
        Assert.Equal("19999999999999.99999998", stock.GetProperty("physicalQty").GetString());
        var command = new { productCd = "MAT", lotNo = "LOT", flag = true, recallVersion = stock.GetProperty("recallVersion").GetString() };
        using var saved = await host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/wms/lot-trace/owner-v1/recall", command)); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var stale = await host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/wms/lot-trace/owner-v1/recall", command)); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await h.Db.Stocks.ExecuteUpdateAsync(s => s.SetProperty(x => x.LotNo, ""));
        using var noLot = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/lot-trace/owner-v1?productCd=MAT&lotNo=")); Assert.Equal(HttpStatusCode.OK, noLot.StatusCode);
        using var noLotBody = JsonDocument.Parse(await noLot.Content.ReadAsStringAsync()); var noLotData = noLotBody.RootElement.GetProperty("data");
        Assert.Equal("", noLotData.GetProperty("lotNo").GetString());
        using var cleared = await host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/wms/lot-trace/owner-v1/recall", new {
            productCd = "MAT", lotNo = "", flag = false, recallVersion = noLotData.GetProperty("stock").GetProperty("recallVersion").GetString() }));
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode); Assert.All(await h.Db.Stocks.AsNoTracking().ToArrayAsync(), s => Assert.False(s.RecallFlag));
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 427 && a.ActionCode == "query").ExecuteDeleteAsync();
        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/lot-trace/owner-v1?productCd=MAT&lotNo=LOT")); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Lot_trace_owner_real_concurrent_connections_cannot_reverse_the_winning_version()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var owner = await GrantLot(h); await SeedLot(h);
        var service = new LotTraceService(h.Db, owner); var snapshot = await service.ReadOwnerAsync("MAT", "LOT", "FORWARD");
        Task<string>? second = null; var blocked = false;
        hook.After = async command =>
        {
            if (!command.CommandText.Contains("WITH field_subject")) return; hook.After = null;
            second = Task.Run(async () =>
            {
                await using var db = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); var other = h with { Db = db };
                try { await new LotTraceService(db, LotOwner(other)).RecallOwnerAsync(Recall(snapshot, false)); return "COMMITTED"; }
                catch (OrderPriceCorrectionException error) { return error.Message; }
            });
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                    AND query LIKE '%cp6_authentication_v1.acquire()%') AS "Value"
                """).SingleAsync(timeout.Token)) await Task.Delay(20, timeout.Token);
            blocked = true; Assert.False(second.IsCompleted);
        };
        string? outcome = null;
        try { await service.RecallOwnerAsync(Recall(snapshot)); }
        finally { if (second != null) outcome = await second; }
        Assert.True(blocked); Assert.Equal("ROW_VERSION_CONFLICT", outcome);
        Assert.All(await h.Db.Stocks.AsNoTracking().ToArrayAsync(), s => Assert.True(s.RecallFlag));
    }

    [CurrentPermissionPostgresFact]
    public async Task Lot_trace_owner_recall_mark_blocks_actual_future_allocation_until_current_clear()
    {
        await using var h = await Seed(); var owner = await GrantLot(h); await SeedLot(h); var service = new LotTraceService(h.Db, owner);
        var snapshot = await service.ReadOwnerAsync("MAT", "LOT", "FORWARD"); await service.RecallOwnerAsync(Recall(snapshot));
        var warehouse = await h.Db.Stocks.Select(s => s.WarehouseCd).FirstAsync(); var no = "A" + h.Tenant.ToString("N")[..12];
        h.Db.OutboundOrders.Add(new() { OutboundNo = no, WarehouseCd = warehouse, OutboundType = OutboundType.Shipping, Status = OutboundOrderStatus.Confirmed, Creator = "price-owner" });
        h.Db.OutboundOrderDetails.Add(new() { OutboundNo = no, LineNo = 1, ProductCd = "MAT", RequiredQty = 1, Creator = "price-owner" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); var seq = new WmsSequenceService(h.Db);
        var outbound = new OutboundService(h.Db, seq, new StockMovementService(h.Db, seq), owner: owner);
        await Assert.ThrowsAsync<InsufficientStockException>(() => outbound.AllocateAsync(no, "forged"));
        Assert.Equal(0m, await h.Db.OutboundOrderDetails.Where(d => d.OutboundNo == no).Select(d => d.AllocatedQty).SingleAsync());
        await service.RecallOwnerAsync(Recall(await service.ReadOwnerAsync("MAT", "LOT", "FORWARD"), false));
        await outbound.AllocateAsync(no, "forged"); Assert.Equal(1m, await h.Db.OutboundOrderDetails.Where(d => d.OutboundNo == no).Select(d => d.AllocatedQty).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Lot_trace_owner_invalid_input_and_catalog_registration_never_grant_access()
    {
        await using var h = await Seed(); var owner = await GrantLot(h); var service = new LotTraceService(h.Db, owner);
        foreach (var (product, lot, direction) in new[] { ("", "LOT", "FORWARD"), (new string('P', 21), "LOT", "FORWARD"), ("MAT", new string('L', 31), "FORWARD"), ("MAT", "LOT", "recursive") })
            Assert.Equal("LOT_QUERY_INVALID", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.ReadOwnerAsync(product, lot, direction))).Message);
        Assert.Equal("ROW_VERSION_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.RecallOwnerAsync(new() { ProductCd = "MAT", LotNo = "LOT" }))).Message);
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 427 && a.ActionCode == "query").ExecuteDeleteAsync();
        await h.Db.Sys_MenuActions.Where(a => a.MenuId == 427 && a.ActionCode == "query").ExecuteDeleteAsync();
        var migration = new CP6.Persistence.PostgreSql.Migrations.Core.LotTraceOwnerV1();
        var sql = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(Assert.Single(migration.UpOperations)).Sql;
        await h.Db.Database.ExecuteSqlRawAsync(sql); await h.Db.Database.ExecuteSqlRawAsync(sql);
        Assert.Equal(1, await h.Db.Sys_MenuActions.CountAsync(a => a.MenuId == 427 && a.ActionCode == "query"));
        Assert.False(await h.Db.Sys_RoleActions.AnyAsync(a => a.MenuId == 427 && a.ActionCode == "query"));
        Assert.Equal("ACTION_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.ReadOwnerAsync("MAT", "LOT", "FORWARD"))).Message);
    }
}
