using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Common;
using CP6.Entity.DTOs.Wms;
using Moq;
using Npgsql;
using System.Net;
using System.Text.Json;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static OperationsOwnerV1 CrossDockOwner(Harness h)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var (key, fields) in new[] { ("wms-cross-dock", new[] { "CustomerCd", "OperatorCd", "Remarks", "SupplierCd" }),
            ("wms-inbound-order", new[] { "UnitPrice" }), ("wms-outbound-order", new[] { "UnitPrice" }),
            ("mes-work-order", Array.Empty<string>()), ("order", new[] { "UnitPrice", "Amount" }) })
        { catalog.RegisterScope(key, key, [1, 2, 3, 4, 5], 1); catalog.RegisterFields(key, fields.Select(f => new FieldRegistry.Field(f, f))); }
        catalog.Seal(); return new(h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
    }
    private static async Task<OperationsOwnerV1> GrantCrossDock(Harness h)
    {
        await GrantOperations(h);
        foreach (var (key, id, actions) in new[] { ("wms-cross-dock", 424, new[] { "query", "add", "execute", "cancel" }),
            ("wms-inbound-order", 405, new[] { "query" }) })
        {
            if (!await h.Db.Sys_Menus.AnyAsync(m => m.MenuId == id)) h.Db.Sys_Menus.Add(new() { MenuId = id, MenuKey = key, MenuName = key });
            foreach (var action in actions)
            {
                if (!await h.Db.Sys_MenuActions.AnyAsync(a => a.MenuId == id && a.ActionCode == action)) h.Db.Sys_MenuActions.Add(new() { MenuId = id, ActionCode = action, ActionName = action });
                h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = id, ActionCode = action });
            }
            h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = key, ScopeType = 5 });
        }
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); return CrossDockOwner(h);
    }
    private static CrossDockService CrossDockServiceFor(Harness h, OperationsOwnerV1 owner)
    { var seq = new WmsSequenceService(h.Db); return new(h.Db, seq, new StockMovementService(h.Db, seq), owner); }
    private static async Task SeedCrossDock(Harness h, int count = 2, bool linked = false)
    {
        var key = "D" + h.Tenant.ToString("N")[..8];
        h.Db.Warehouses.Add(new() { WarehouseCd = key, WarehouseName = key });
        h.Db.Locations.AddRange(new Location { WarehouseCd = key, LocationCd = key + "A", AreaCd = "A" }, new Location { WarehouseCd = key, LocationCd = key + "B", AreaCd = "B" });
        for (var i = 0; i < count; i++) h.Db.CrossDockOrders.Add(new() { XDockNo = key + i.ToString("D4"), ProductCd = "MAT", ProductName = "Material",
            WarehouseCd = key, TempLocationCd = key + (i % 2 == 0 ? "A" : "B"), Qty = 9999999999999.99999999m, LotNo = "",
            InboundNo = linked && i == 0 ? key + "I" : null, OutboundNo = linked && i == 0 ? key + "O" : null,
            InTxnNo = i == 0 ? "OLD-IN" : null, SupplierCd = "legacy-supplier", CustomerCd = "legacy-customer", OperatorCd = "legacy-operator", Remarks = "legacy remarks",
            Creator = "price-owner", CreateDate = new(2026, 1, 1), FromDock = "old free text", Status = 0 });
        if (linked)
        {
            h.Db.InboundOrders.Add(new() { InboundNo = key + "I", WarehouseCd = key, ExpectedArrivalDate = new(2026, 1, 1), Creator = "price-owner" });
            h.Db.InboundOrderDetails.Add(new() { InboundNo = key + "I", LineNo = 1, ProductCd = "MAT", ExpectedLocationCd = key + "A", ExpectedQty = 1, Creator = "price-owner" });
            h.Db.OutboundOrders.Add(new() { OutboundNo = key + "O", WarehouseCd = key, OutboundType = OutboundType.Shipping, Creator = "price-owner" });
            h.Db.OutboundOrderDetails.Add(new() { OutboundNo = key + "O", LineNo = 1, ProductCd = "MAT", LocationCd = key + "A", RequiredQty = 1, Creator = "price-owner" });
        }
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }
    [CurrentPermissionPostgresFact]
    public async Task Cross_dock_owner_missing_authority_cannot_read_even_empty()
    {
        await using var h = await Seed(); var seq = new WmsSequenceService(h.Db);
        var service = new CrossDockService(h.Db, seq, new StockMovementService(h.Db, seq));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchAsync(new()));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.GetAsync("missing"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Cross_dock_owner_missing_authority_cannot_create_with_forged_actor()
    {
        await using var h = await Seed(); var seq = new WmsSequenceService(h.Db);
        var service = new CrossDockService(h.Db, seq, new StockMovementService(h.Db, seq));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.CreateAsync(new() {
            ProductCd = "MAT", WarehouseCd = "W01", TempLocationCd = "TEMP", Qty = 1 }, "forged"));
        Assert.False(await h.Db.CrossDockOrders.AnyAsync());
        Assert.False(await h.Db.WmsSequences.AnyAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Cross_dock_owner_missing_authority_cannot_cancel_legacy_partial_fact()
    {
        await using var h = await Seed(); var seq = new WmsSequenceService(h.Db);
        h.Db.CrossDockOrders.Add(new() { XDockNo = "XD" + h.Tenant.ToString("N")[..12], ProductCd = "MAT",
            WarehouseCd = "W01", TempLocationCd = "TEMP", Qty = 1, InTxnNo = "OLD-IN", Creator = "other" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); var no = await h.Db.CrossDockOrders.Select(x => x.XDockNo).SingleAsync();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new CrossDockService(h.Db, seq, new StockMovementService(h.Db, seq)).CancelAsync(no, "forged"));
        Assert.Equal(CrossDockStatus.Planned, await h.Db.CrossDockOrders.Select(x => x.Status).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Cross_dock_owner_exact_history_and_real_paging_do_not_invent_physical_results()
    {
        await using var h = await Seed(); var owner = await GrantCrossDock(h); await SeedCrossDock(h, 503); var service = CrossDockServiceFor(h, owner);
        var first = await service.SearchOwnerAsync(new() { PageSize = 500 }); var second = await service.SearchOwnerAsync(new() { Page = 2, PageSize = 500 });
        Assert.Equal(503, first.Total); Assert.Equal(500, first.Items.Count); Assert.Equal(3, second.Items.Count);
        Assert.Equal(503, first.Items.Concat(second.Items).Select(x => x.Id).Distinct().Count()); Assert.False(first.WritesAvailable);
        Assert.All(first.Items, x => { Assert.Equal("9999999999999.99999999", x.Qty); Assert.Equal("", x.LotNo); Assert.Equal("UNKNOWN", x.Knowledge); Assert.Equal("UNRESOLVED_SOURCE", x.SourceState); });
        var legacy = await h.Db.CrossDockOrders.AsNoTracking().SingleAsync(x => x.InTxnNo != null); var detail = (await service.GetOwnerAsync(legacy.XDockNo))!;
        Assert.Equal("OLD-IN", detail.InTxnNo); Assert.Null(detail.OutTxnNo); Assert.Equal(0, detail.LegacyStatus);
        var json = JsonSerializer.Serialize(detail); Assert.DoesNotContain("TenantId", json); Assert.DoesNotContain("Creator", json); Assert.DoesNotContain("ReceivedQty", json);
        Assert.Equal(first.Items.Select(x => x.Id), (await service.SearchOwnerAsync(new() { PageSize = 500 })).Items.Select(x => x.Id));
    }

    [CurrentPermissionPostgresFact]
    public async Task Cross_dock_owner_tenant_self_sources_and_physical_area_filter_before_total()
    {
        await using var h = await Seed(); var owner = await GrantCrossDock(h); await SeedCrossDock(h, linked: true);
        await using var foreign = await Seed(); await SeedCrossDock(foreign); var service = CrossDockServiceFor(h, owner);
        Assert.Equal(2, (await service.SearchOwnerAsync(new())).Total);
        var foreignNo = await foreign.Db.CrossDockOrders.Select(x => x.XDockNo).FirstAsync(); Assert.Null(await service.GetOwnerAsync(foreignNo));
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 405 && x.ActionCode == "query").ExecuteDeleteAsync();
        var one = Assert.Single((await service.SearchOwnerAsync(new())).Items); Assert.Null(one.InboundNo);
        await h.Db.CrossDockOrders.Where(x => x.Id == one.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Creator, "other"));
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "wms-cross-dock").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
        Assert.Equal(0, (await service.SearchOwnerAsync(new())).Total);
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "wms-cross-dock").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 5));
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.AreaCd, "A")); Assert.Equal(0, (await service.SearchOwnerAsync(new())).Total);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.AreaCd, "*"));
        await h.Db.Locations.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true)); Assert.Equal(0, (await service.SearchOwnerAsync(new())).Total);
    }

    [CurrentPermissionPostgresFact]
    public async Task Cross_dock_owner_source_self_wrong_product_and_soft_deleted_links_never_fall_back()
    {
        await using var h = await Seed(); var owner = await GrantCrossDock(h); await SeedCrossDock(h, 1, true); var service = CrossDockServiceFor(h, owner);
        Assert.Single((await service.SearchOwnerAsync(new())).Items);
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "wms-inbound-order").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
        await h.Db.InboundOrderDetails.ExecuteUpdateAsync(s => s.SetProperty(x => x.Creator, "other")); Assert.Empty((await service.SearchOwnerAsync(new())).Items);
        await h.Db.InboundOrderDetails.ExecuteUpdateAsync(s => s.SetProperty(x => x.Creator, "price-owner"));
        await h.Db.OutboundOrderDetails.ExecuteUpdateAsync(s => s.SetProperty(x => x.ProductCd, "WRONG")); Assert.Empty((await service.SearchOwnerAsync(new())).Items);
        await h.Db.OutboundOrderDetails.ExecuteUpdateAsync(s => s.SetProperty(x => x.ProductCd, "MAT"));
        await h.Db.OutboundOrders.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true)); Assert.Empty((await service.SearchOwnerAsync(new())).Items);
    }

    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Cross_dock_owner_actual_department_uuid_binding_is_required(int scope)
    {
        await using var h = await Seed(); var owner = await GrantCrossDock(h); await SeedCrossDock(h); var admin = await MappingAdmin(h);
        await StandaloneScope(h, admin, scope); var service = CrossDockServiceFor(h, owner); Assert.Empty((await service.SearchOwnerAsync(new())).Items);
        var row = await h.Db.CrossDockOrders.AsNoTracking().FirstAsync();
        var choices = await StandaloneAdmin(h, admin).FindObjectsAsync("cross-dock", row.XDockNo, _ => Task.FromResult(h.User), default); Assert.Equal(row.Id, Assert.Single(choices).Id);
        var mapping = await BindStandalone(h, admin, "cross-dock", row.Id); Assert.Equal(row.Id, Assert.Single((await service.SearchOwnerAsync(new())).Items).Id);
        var command = (await StandaloneCommand(h, admin, "cross-dock", row.Id, mapping.EntityId)) with { Enabled = false };
        await SaveStandalone(h, admin, command); Assert.Empty((await service.SearchOwnerAsync(new())).Items);
    }

    [CurrentPermissionPostgresFact]
    public async Task Cross_dock_owner_current_min_fields_hide_values_without_modifying_legacy_storage()
    {
        await using var h = await Seed(); var owner = await GrantCrossDock(h); await SeedCrossDock(h, 1);
        foreach (var f in new[] { "SupplierCd", "CustomerCd", "OperatorCd", "Remarks" }) h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-cross-dock", FieldName = f, Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); var service = CrossDockServiceFor(h, owner); var row = Assert.Single((await service.SearchOwnerAsync(new())).Items);
        Assert.False(row.SupplierCdReadable); Assert.False(row.CustomerCdReadable); Assert.False(row.OperatorCdReadable); Assert.False(row.RemarksReadable);
        Assert.Null(row.SupplierCd); Assert.Null(row.CustomerCd); Assert.Null(row.OperatorCd); Assert.Null(row.Remarks);
        Assert.Null((await service.GetOwnerAsync(row.XDockNo))!.Remarks); Assert.Equal("legacy remarks", await h.Db.CrossDockOrders.Select(x => x.Remarks).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Cross_dock_owner_unadopted_writes_preserve_partial_facts_without_sequence_or_stock_calls()
    {
        await using var h = await Seed(); var owner = await GrantCrossDock(h); await SeedCrossDock(h, 1);
        var seq = new Mock<IWmsSequenceService>(MockBehavior.Strict); var stock = new Mock<IStockMovementService>(MockBehavior.Strict);
        var service = new CrossDockService(h.Db, seq.Object, stock.Object, owner); var no = await h.Db.CrossDockOrders.Select(x => x.XDockNo).SingleAsync();
        foreach (var command in new Func<Task>[] { () => service.CreateAsync(new() { ProductCd = "MAT", WarehouseCd = "W01", TempLocationCd = "TEMP", Qty = 1 }, "forged"),
            () => service.ExecuteAsync(no, "forged"), () => service.CancelAsync(no, "forged") })
            Assert.Equal("CROSS_DOCK_OWNER_ADOPTION_UNPROVEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(command)).Message);
        Assert.Empty(seq.Invocations); Assert.Empty(stock.Invocations); Assert.False(await h.Db.WmsSequences.AnyAsync()); Assert.False(await h.Db.StockTransactions.AnyAsync()); Assert.False(await h.Db.Stocks.AnyAsync());
        var row = await h.Db.CrossDockOrders.AsNoTracking().SingleAsync(); Assert.Equal(0, row.Status); Assert.Equal("OLD-IN", row.InTxnNo); Assert.Null(row.OutTxnNo); Assert.Equal("", row.LotNo);
        Assert.Equal("OWNER_READ_VERSION_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchAsync(new()))).Message);
        Assert.Equal("OWNER_READ_VERSION_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.GetAsync(no))).Message);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 424 && x.ActionCode == "execute").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.ExecuteAsync(no, "forged"))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Cross_dock_owner_frozen_query_final_failure_and_dirty_or_outer_context_are_closed()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var owner = await GrantCrossDock(h); await SeedCrossDock(h, 3); var service = CrossDockServiceFor(h, owner);
        var query = new CrossDockSearchQuery { PageSize = 1 };
        hook.Before = c => { if (c.CommandText.Contains("WITH field_subject")) { hook.Before = null; query.ProductCd = "WRONG"; query.Page = 20; } return Task.CompletedTask; };
        var page = await service.SearchOwnerAsync(query); Assert.Single(page.Items); Assert.Equal(1, page.Page); Assert.Equal(3, page.Total);
        var reads = 0; hook.Before = c => { if (c.CommandText.Contains("WITH field_subject") && ++reads == 4) throw new IOException("final cross-dock authority unavailable"); return Task.CompletedTask; };
        Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchOwnerAsync(new()))).Message); hook.Before = null;
        await using (var tx = await h.Db.Database.BeginTransactionAsync()) await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchOwnerAsync(new()));
        await h.Db.CrossDockOrders.FirstAsync(); await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchOwnerAsync(new())); h.Db.ChangeTracker.Clear();
    }

    [CurrentPermissionPostgresFact]
    public async Task Cross_dock_owner_invalid_query_native_foreign_binding_and_catalog_do_not_grant_roles()
    {
        await using var h = await Seed(); var owner = await GrantCrossDock(h); await SeedCrossDock(h); var admin = await MappingAdmin(h); var service = CrossDockServiceFor(h, owner);
        foreach (var q in new[] { new CrossDockSearchQuery { Page = 0 }, new() { PageSize = 501 }, new() { Page = int.MaxValue }, new() { Status = 3 }, new() { ProductCd = new string('P', 21) }, new() { DateFrom = new(2026, 2, 1), DateTo = new(2026, 1, 1) } })
            Assert.Equal("CROSS_DOCK_QUERY_INVALID", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchOwnerAsync(q))).Message);
        await using var foreign = await Seed(); await SeedCrossDock(foreign); var id = await foreign.Db.CrossDockOrders.Select(x => x.Id).FirstAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_operations_owner_v1.department_mappings(id,tenant_id,object_kind,object_id,department_id,enabled)
            VALUES({Guid.NewGuid()},{h.Tenant},'cross-dock',{id},{admin.Child},true)
            """)); Assert.Equal("23514", error.SqlState);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 424 && x.ActionCode == "query").ExecuteDeleteAsync();
        await h.Db.Sys_MenuActions.Where(x => x.MenuId == 424 && x.ActionCode == "query").ExecuteDeleteAsync();
        var migration = new CP6.Persistence.PostgreSql.Migrations.Core.CrossDockDepartmentOwnerV1();
        foreach (var op in migration.UpOperations.Cast<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>()) { await h.Db.Database.ExecuteSqlRawAsync(op.Sql); await h.Db.Database.ExecuteSqlRawAsync(op.Sql); }
        Assert.Equal(1, await h.Db.Sys_MenuActions.CountAsync(x => x.MenuId == 424 && x.ActionCode == "query")); Assert.False(await h.Db.Sys_RoleActions.AnyAsync(x => x.MenuId == 424 && x.ActionCode == "query"));
        Assert.Equal("ACTION_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchOwnerAsync(new()))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Cross_dock_owner_actual_program_http_denies_old_effects_and_preserves_exact_current_read()
    {
        await using var h = await Seed(); await GrantCrossDock(h); await SeedCrossDock(h, 1);
        var no = await h.Db.CrossDockOrders.Select(x => x.XDockNo).SingleAsync(); await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        using var read = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/cross-dock/owner-v1")); Assert.Equal(HttpStatusCode.OK, read.StatusCode); Assert.True(read.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync()); var data = json.RootElement.GetProperty("data"); Assert.False(data.GetProperty("writesAvailable").GetBoolean());
        Assert.Equal("9999999999999.99999999", data.GetProperty("items")[0].GetProperty("qty").GetString());
        using var detail = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/cross-dock/owner-v1/" + no)); Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        foreach (var route in new[] { "api/wms/cross-dock", "api/wms/cross-dock/" + no + "/execute", "api/wms/cross-dock/" + no + "/cancel" })
        {
            using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Post, route, new { productCd = "MAT", warehouseCd = "W01", tempLocationCd = "TEMP", qty = 1 }));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); Assert.Contains("CROSS_DOCK_OWNER_ADOPTION_UNPROVEN", await response.Content.ReadAsStringAsync());
        }
        using var missing = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/cross-dock/owner-v1/missing")); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 424 && x.ActionCode == "query").ExecuteDeleteAsync();
        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/cross-dock/owner-v1")); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(0, await h.Db.CrossDockOrders.Select(x => x.Status).SingleAsync()); Assert.False(await h.Db.WmsSequences.AnyAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Cross_dock_owner_native_fence_keeps_raw_legacy_writer_out_of_the_read_cut()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var owner = await GrantCrossDock(h); await SeedCrossDock(h, 1);
        Task<int>? writer = null; var blocked = false;
        hook.After = async command =>
        {
            if (!command.CommandText.Contains("WITH field_subject")) return; hook.After = null;
            writer = Task.Run(async () =>
            {
                await using var other = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
                return await other.CrossDockOrders.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, CrossDockStatus.Cancelled));
            });
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                    AND query LIKE 'UPDATE %T_CrossDockOrder%') AS "Value"
                """).SingleAsync(timeout.Token)) await Task.Delay(20, timeout.Token);
            blocked = true; Assert.False(writer.IsCompleted);
        };
        CrossDockPageV1? page = null;
        try { page = await CrossDockServiceFor(h, owner).SearchOwnerAsync(new()); }
        finally { if (writer != null) await writer; }
        Assert.True(blocked); Assert.Equal(0, Assert.Single(page!.Items).LegacyStatus);
        Assert.Equal(9, await h.Db.CrossDockOrders.Select(x => x.Status).SingleAsync());
    }
}
