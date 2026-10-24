using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Moq;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Common;
using CP6.Entity.DTOs.Wms;
using CP6.Tests.Platform;
using Npgsql;
using System.Net;
using System.Text.Json;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static KittingService LegacyKit(Harness h) => new(h.Db, new WmsSequenceService(h.Db), new Mock<IStockMovementService>(MockBehavior.Strict).Object);

    [CurrentPermissionPostgresFact]
    public async Task Kitting_owner_missing_authority_cannot_read_even_empty()
    {
        await using var h = await Seed(); var svc = LegacyKit(h);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => svc.SearchMastersAsync(null));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => svc.GetMasterAsync("missing"));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => svc.SearchOrdersAsync(new()));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => svc.GetOrderAsync("missing"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Kitting_owner_missing_authority_cannot_create_with_forged_actor()
    {
        await using var h = await Seed();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => LegacyKit(h).CreateMasterAsync(new()
        { KitSku = "K" + h.Tenant.ToString("N")[..12], KitName = "private", Components = [new() { ComponentProductCd = "C", RequiredQty = 1 }] }, "forged"));
        Assert.False(await h.Db.KitMasters.AnyAsync()); Assert.False(await h.Db.KitMasterComponents.AnyAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Kitting_owner_missing_authority_cannot_cancel_legacy_order()
    {
        await using var h = await Seed(); var no = "K" + h.Tenant.ToString("N")[..12];
        h.Db.KitOrders.Add(new() { KitOrderNo = no, KitSku = no, WarehouseCd = "W01", KitLocationCd = "L01", Qty = 1, Creator = "other" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => LegacyKit(h).CancelAsync(no, "forged"));
        Assert.Equal(KitOrderStatus.Draft, await h.Db.KitOrders.Select(x => x.Status).SingleAsync());
    }

    private static async Task<OperationsOwnerV1> GrantKit(Harness h)
    {
        await GrantOperations(h);
        foreach (var (key, id, actions) in new[] { ("wms-kitting", 425, new[] { "query", "add", "edit", "del", "execute", "cancel" }), ("wms-stock", 403, new[] { "query" }) })
        {
            if (!await h.Db.Sys_Menus.AnyAsync(m => m.MenuId == id)) h.Db.Sys_Menus.Add(new() { MenuId = id, MenuKey = key, MenuName = key });
            foreach (var action in actions)
            {
                if (!await h.Db.Sys_MenuActions.AnyAsync(a => a.MenuId == id && a.ActionCode == action)) h.Db.Sys_MenuActions.Add(new() { MenuId = id, ActionCode = action, ActionName = action });
                h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = id, ActionCode = action });
            }
            h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = key, ScopeType = 5 });
        }
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var (key, fields) in new[] { ("wms-kitting", new[] { "ComponentName", "ExecutedTxnNos", "KitName", "OperatorCd", "Remarks" }), ("wms-stock", new[] { "UnitPrice" }) })
        { catalog.RegisterScope(key, key, [1, 2, 3, 4, 5], 1); catalog.RegisterFields(key, fields.Select(f => new FieldRegistry.Field(f, f))); }
        catalog.Seal(); return new(h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
    }
    private static KittingService KitService(Harness h, OperationsOwnerV1 owner) => new(h.Db, new WmsSequenceService(h.Db), new Mock<IStockMovementService>(MockBehavior.Strict).Object, owner);
    private static async Task<string> SeedKit(Harness h, int count = 1)
    {
        var key = "K" + h.Tenant.ToString("N")[..8];
        h.Db.Warehouses.Add(new() { WarehouseCd = key, WarehouseName = key });
        h.Db.Locations.AddRange(new Location { WarehouseCd = key, LocationCd = key + "A", LocationName = "A", AreaCd = "A" },
            new Location { WarehouseCd = key, LocationCd = key + "B", LocationName = "B", AreaCd = "B" });
        for (var i = 0; i < count; i++)
        {
            var no = key + i.ToString("D4");
            h.Db.KitMasters.Add(new() { KitSku = no, KitName = "current secret", DefaultWarehouseCd = key, Creator = "price-owner", Remarks = "recipe notes", ActiveFlg = i != 1 });
            h.Db.KitMasterComponents.Add(new() { KitSku = no, LineNo = 1, ComponentProductCd = "C1", ComponentName = "component secret", RequiredQty = 9999999999999.99999999m, UnitCd = "EA", Creator = "price-owner", Remarks = "component notes" });
            h.Db.KitOrders.Add(new() { KitOrderNo = no, KitSku = no, KitName = "historical name", Qty = 9999999999999.99999999m,
                Direction = "ASSEMBLE", WarehouseCd = key, KitLocationCd = key + "A", KitLotNo = "old-lot", Status = 1, Creator = "price-owner",
                OperatorCd = "old-operator", Remarks = "old notes", CreateDate = new(2026, 1, 1), ExecutedTxnNos = i == 0 ? key + "T1;" + key + "T2" : null });
        }
        foreach (var n in new[] { 1, 2 }) h.Db.StockTransactions.Add(new() { TxnNo = key + "T" + n, TxnType = n == 1 ? "OUT" : "IN",
            WarehouseCd = key, LocationCd = key + (n == 1 ? "B" : "A"), ProductCd = n == 1 ? "C1" : key + "0000", LotNo = "old-lot",
            Qty = 1, UnitCd = "EA", RelatedNo = key + "0000", RelatedType = "KIT_ASSEMBLE", Creator = n == 1 ? "other" : "price-owner" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); return key;
    }
    [CurrentPermissionPostgresFact]
    public async Task Kitting_owner_exact_paging_current_recipe_does_not_invent_historical_composition_or_units()
    {
        await using var h = await Seed(); var owner = await GrantKit(h); var key = await SeedKit(h, 203); var svc = KitService(h, owner);
        var page = await svc.SearchMastersOwnerAsync(new() { Page = 5 }); Assert.Equal(203, page.Total); Assert.Equal(3, page.Items.Count); Assert.False(page.WritesAvailable);
        var master = (await svc.GetMasterOwnerAsync(key + "0000"))!; Assert.Equal("CURRENT_LEGACY_RECIPE", master.Master.RecipeState);
        Assert.Equal("9999999999999.99999999", Assert.Single(master.Components).RequiredQty); Assert.Equal("EA", master.Components[0].UnitCd);
        var order = (await svc.GetOrderOwnerAsync(key + "0000"))!; Assert.Equal("9999999999999.99999999", order.Qty); Assert.Null(order.UnitCd);
        Assert.Equal("UNPROVEN", order.CompositionState); Assert.Equal("historical name", order.KitName);
        await h.Db.KitMasters.ExecuteUpdateAsync(s => s.SetProperty(x => x.KitName, "new recipe"));
        Assert.Equal("historical name", (await svc.GetOrderOwnerAsync(key + "0000"))!.KitName);
        Assert.Equal(203, (await svc.SearchOrdersOwnerAsync(new() { Page = 5 })).Total);
        Assert.Single((await svc.SearchMastersOwnerAsync(new() { Active = false })).Items);
    }
    [CurrentPermissionPostgresFact]
    public async Task Kitting_owner_tenant_self_warehouse_location_and_soft_delete_filter_before_total()
    {
        await using var h = await Seed(); var owner = await GrantKit(h); var key = await SeedKit(h, 2); var svc = KitService(h, owner);
        await using var foreign = await Seed(); var otherKey = await SeedKit(foreign); Assert.Null(await svc.GetMasterOwnerAsync(otherKey + "0000")); Assert.Null(await svc.GetOrderOwnerAsync(otherKey + "0000"));
        await h.Db.KitMasters.Where(x => x.KitSku == key + "0000").ExecuteUpdateAsync(s => s.SetProperty(x => x.Creator, "other"));
        await h.Db.KitOrders.Where(x => x.KitOrderNo == key + "0000").ExecuteUpdateAsync(s => s.SetProperty(x => x.Creator, "other"));
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "wms-kitting").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
        Assert.Single((await svc.SearchMastersOwnerAsync(new())).Items); Assert.Single((await svc.SearchOrdersOwnerAsync(new())).Items);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.AreaCd, "A")); Assert.Empty((await svc.SearchMastersOwnerAsync(new())).Items); Assert.Single((await svc.SearchOrdersOwnerAsync(new())).Items);
        await h.Db.Locations.Where(x => x.LocationCd == key + "A").ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true)); Assert.Empty((await svc.SearchOrdersOwnerAsync(new())).Items);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.AreaCd, "*"));
        await h.Db.KitMasters.Where(x => x.KitSku == key + "0001").ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true)); Assert.Empty((await svc.SearchMastersOwnerAsync(new())).Items);
    }
    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Kitting_owner_master_and_order_department_ids_are_independent_and_current(int scope)
    {
        await using var h = await Seed(); var owner = await GrantKit(h); var key = await SeedKit(h); var admin = await MappingAdmin(h); await StandaloneScope(h, admin, scope); var svc = KitService(h, owner);
        Assert.Empty((await svc.SearchMastersOwnerAsync(new())).Items); Assert.Empty((await svc.SearchOrdersOwnerAsync(new())).Items);
        var master = await h.Db.KitMasters.Select(x => x.Id).SingleAsync(); var order = await h.Db.KitOrders.Select(x => x.Id).SingleAsync();
        Assert.Equal(master, Assert.Single(await StandaloneAdmin(h, admin).FindObjectsAsync("kit-master", key, _ => Task.FromResult(h.User), default)).Id);
        Assert.Equal(order, Assert.Single(await StandaloneAdmin(h, admin).FindObjectsAsync("kit-order", key, _ => Task.FromResult(h.User), default)).Id);
        await BindStandalone(h, admin, "kit-master", master); Assert.Single((await svc.SearchMastersOwnerAsync(new())).Items); Assert.Empty((await svc.SearchOrdersOwnerAsync(new())).Items);
        var mapping = await BindStandalone(h, admin, "kit-order", order); Assert.Single((await svc.SearchOrdersOwnerAsync(new())).Items);
        await SaveStandalone(h, admin, (await StandaloneCommand(h, admin, "kit-order", order, mapping.EntityId)) with { Enabled = false });
        Assert.Empty((await svc.SearchOrdersOwnerAsync(new())).Items); Assert.Single((await svc.SearchMastersOwnerAsync(new())).Items);
    }
    [CurrentPermissionPostgresFact]
    public async Task Kitting_owner_historical_references_require_every_actual_source_transaction_and_location()
    {
        await using var h = await Seed(); var owner = await GrantKit(h); var key = await SeedKit(h); var svc = KitService(h, owner); var no = key + "0000";
        Assert.Equal(key + "T1;" + key + "T2", (await svc.GetOrderOwnerAsync(no))!.ExecutedTxnNos);
        await h.Db.KitOrders.ExecuteUpdateAsync(s => s.SetProperty(x => x.Direction, "DISASSEMBLE"));
        Assert.Null((await svc.GetOrderOwnerAsync(no))!.ExecutedTxnNos);
        await h.Db.KitOrders.ExecuteUpdateAsync(s => s.SetProperty(x => x.Direction, "ASSEMBLE"));
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.AreaCd, "A"));
        var partial = (await svc.GetOrderOwnerAsync(no))!; Assert.Null(partial.ExecutedTxnNos); Assert.False(partial.ExecutedTxnNosReadable);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.AreaCd, "*"));
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "wms-stock").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1)); Assert.Null((await svc.GetOrderOwnerAsync(no))!.ExecutedTxnNos);
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "wms-stock").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 5));
        foreach (var raw in new[] { key + "T1;MISSING", key + "T1;" + key + "T1", "malformed;", "foreign-txn" })
        {
            await h.Db.KitOrders.ExecuteUpdateAsync(s => s.SetProperty(x => x.ExecutedTxnNos, raw)); Assert.Null((await svc.GetOrderOwnerAsync(no))!.ExecutedTxnNos);
            Assert.Equal(raw, await h.Db.KitOrders.Select(x => x.ExecutedTxnNos).SingleAsync());
        }
        await h.Db.KitOrders.ExecuteUpdateAsync(s => s.SetProperty(x => x.ExecutedTxnNos, key + "T1;" + key + "T2"));
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 403 && x.ActionCode == "query").ExecuteDeleteAsync(); Assert.Null((await svc.GetOrderOwnerAsync(no))!.ExecutedTxnNos);
    }
    [CurrentPermissionPostgresFact]
    public async Task Kitting_owner_optional_default_warehouse_requires_explicit_full_warehouse_authority()
    {
        await using var h = await Seed(); var owner = await GrantKit(h); var key = await SeedKit(h); var svc = KitService(h, owner);
        foreach (var warehouse in new string?[] { null, "" })
        {
            await h.Db.KitMasters.ExecuteUpdateAsync(s => s.SetProperty(x => x.DefaultWarehouseCd, warehouse));
            await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.WarehouseCd, "*").SetProperty(x => x.AreaCd, "*"));
            Assert.Single((await svc.SearchMastersOwnerAsync(new())).Items);
            await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.WarehouseCd, key)); Assert.Empty((await svc.SearchMastersOwnerAsync(new())).Items);
            await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.WarehouseCd, "")); Assert.Empty((await svc.SearchMastersOwnerAsync(new())).Items);
            await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.WarehouseCd, "*").SetProperty(x => x.AreaCd, "A")); Assert.Empty((await svc.SearchMastersOwnerAsync(new())).Items);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task Kitting_owner_current_fields_hide_values_without_modifying_originals_or_name_filter_leaks()
    {
        await using var h = await Seed(); var owner = await GrantKit(h); var key = await SeedKit(h); var svc = KitService(h, owner);
        foreach (var f in new[] { "ComponentName", "ExecutedTxnNos", "KitName", "OperatorCd", "Remarks" }) h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-kitting", FieldName = f, Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); var m = (await svc.GetMasterOwnerAsync(key + "0000"))!; var o = (await svc.GetOrderOwnerAsync(key + "0000"))!;
        Assert.Null(m.Master.KitName); Assert.Null(m.Master.Remarks); Assert.Null(m.Components[0].ComponentName); Assert.Null(m.Components[0].Remarks);
        Assert.Null(o.KitName); Assert.Null(o.OperatorCd); Assert.Null(o.Remarks); Assert.Null(o.ExecutedTxnNos); Assert.False(o.KitNameReadable);
        Assert.Empty((await svc.SearchMastersOwnerAsync(new() { Keyword = "secret" })).Items);
        Assert.Equal("historical name", await h.Db.KitOrders.Select(x => x.KitName).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Kitting_owner_all_unadopted_writes_reject_before_sequence_stock_or_entity_changes()
    {
        await using var h = await Seed(); var owner = await GrantKit(h); var key = await SeedKit(h); var seq = new Mock<IWmsSequenceService>(MockBehavior.Strict); var stock = new Mock<IStockMovementService>(MockBehavior.Strict);
        var svc = new KittingService(h.Db, seq.Object, stock.Object, owner); var no = key + "0000";
        foreach (var command in new Func<Task>[] { () => svc.CreateMasterAsync(new(), "forged"), () => svc.UpdateMasterAsync(no, new(), "forged"),
            () => svc.DeleteMasterAsync(no, "forged"), () => svc.CreateOrderAsync(new(), "forged"), () => svc.ExecuteAsync(no, "forged"), () => svc.CancelAsync(no, "forged") })
            Assert.Equal("KIT_OWNER_ADOPTION_UNPROVEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(command)).Message);
        Assert.Empty(seq.Invocations); Assert.Empty(stock.Invocations); Assert.False(await h.Db.WmsSequences.AnyAsync()); Assert.Equal(2, await h.Db.StockTransactions.CountAsync());
        Assert.Equal(1, await h.Db.KitMasters.CountAsync()); Assert.Equal(1, await h.Db.KitMasterComponents.CountAsync()); Assert.Equal(1, await h.Db.KitOrders.Select(x => x.Status).SingleAsync());
        Assert.Equal("OWNER_READ_VERSION_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => svc.GetOrderAsync(no))).Message);
        Assert.Equal("OBJECT_NOT_FOUND", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => svc.CancelAsync("missing", "forged"))).Message);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 425 && x.ActionCode == "execute").ExecuteDeleteAsync(); Assert.Equal("ACTION_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => svc.ExecuteAsync(no, "forged"))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Kitting_owner_frozen_query_final_failure_and_outer_or_tracked_context_reject()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var owner = await GrantKit(h); await SeedKit(h, 3); var svc = KitService(h, owner); var q = new KitQueryV1 { PageSize = 1 };
        hook.Before = c => { if (c.CommandText.Contains("WITH field_subject")) { hook.Before = null; q.Page = 99; q.Keyword = "wrong"; } return Task.CompletedTask; };
        var page = await svc.SearchMastersOwnerAsync(q); Assert.Equal(1, page.Page); Assert.Equal(3, page.Total);
        var count = 0; hook.Before = c => { if (c.CommandText.Contains("WITH field_subject") && ++count == 2) throw new IOException("final kit authority unavailable"); return Task.CompletedTask; };
        Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => svc.SearchMastersOwnerAsync(new()))).Message); hook.Before = null;
        await using (var tx = await h.Db.Database.BeginTransactionAsync()) await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => svc.SearchMastersOwnerAsync(new()));
        await h.Db.KitMasterComponents.FirstAsync(); await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => svc.SearchMastersOwnerAsync(new())); h.Db.ChangeTracker.Clear();
    }
    [CurrentPermissionPostgresFact]
    public async Task Kitting_owner_native_identity_bounds_and_catalog_replay_do_not_grant_permissions()
    {
        await using var h = await Seed(); var owner = await GrantKit(h); await SeedKit(h); var svc = KitService(h, owner); var admin = await MappingAdmin(h);
        foreach (var q in new[] { new KitQueryV1 { Page = 0 }, new() { PageSize = 201 }, new() { Page = int.MaxValue }, new() { Status = 8 }, new() { WarehouseCd = new string('W', 11) }, new() { Direction = "bad" } })
            Assert.Equal("KIT_QUERY_INVALID", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => svc.SearchOrdersOwnerAsync(q))).Message);
        await using var foreign = await Seed(); await SeedKit(foreign);
        foreach (var (kind, id) in new[] { ("kit-master", await foreign.Db.KitMasters.Select(x => x.Id).SingleAsync()), ("kit-order", await foreign.Db.KitOrders.Select(x => x.Id).SingleAsync()) })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_operations_owner_v1.department_mappings(id,tenant_id,object_kind,object_id,department_id,enabled)
                VALUES({Guid.NewGuid()},{h.Tenant},{kind},{id},{admin.Child},true)
                """)); Assert.Equal("23514", error.SqlState);
        }
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 425 && x.ActionCode == "query").ExecuteDeleteAsync(); await h.Db.Sys_MenuActions.Where(x => x.MenuId == 425 && x.ActionCode == "query").ExecuteDeleteAsync();
        foreach (var op in new CP6.Persistence.PostgreSql.Migrations.Core.KittingDepartmentOwnerV1().UpOperations.Cast<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>())
        { await h.Db.Database.ExecuteSqlRawAsync(op.Sql); await h.Db.Database.ExecuteSqlRawAsync(op.Sql); }
        Assert.Equal(1, await h.Db.Sys_MenuActions.CountAsync(x => x.MenuId == 425 && x.ActionCode == "query")); Assert.False(await h.Db.Sys_RoleActions.AnyAsync(x => x.MenuId == 425 && x.ActionCode == "query"));
        Assert.Equal("ACTION_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => svc.SearchMastersOwnerAsync(new()))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Kitting_owner_actual_program_http_current_reads_and_all_legacy_writes_are_safe()
    {
        await using var h = await Seed(); await GrantKit(h); var key = await SeedKit(h); var no = key + "0000";
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        foreach (var route in new[] { "masters", "masters/" + no, "orders", "orders/" + no })
        { using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/kit/owner-v1/" + route)); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore); }
        using var read = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/kit/owner-v1/orders/" + no));
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync()); Assert.Equal("9999999999999.99999999", json.RootElement.GetProperty("data").GetProperty("qty").GetString());
        foreach (var (method, route) in new[] { (HttpMethod.Post, "masters"), (HttpMethod.Put, "masters/" + no), (HttpMethod.Delete, "masters/" + no),
            (HttpMethod.Post, "orders"), (HttpMethod.Post, "orders/" + no + "/execute"), (HttpMethod.Post, "orders/" + no + "/cancel") })
        {
            using var response = await host.Send(OperationsRequest(cookies, method, "api/wms/kit/" + route, new { kitSku = no, kitName = "forged", qty = 1, warehouseCd = key, kitLocationCd = key + "A", direction = "ASSEMBLE" }));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); Assert.Contains("KIT_OWNER_ADOPTION_UNPROVEN", await response.Content.ReadAsStringAsync());
        }
        using var legacy = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/kit/orders")); Assert.Equal(HttpStatusCode.Conflict, legacy.StatusCode);
        using var absent = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/kit/owner-v1/orders/missing")); Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 425 && x.ActionCode == "query").ExecuteDeleteAsync();
        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/kit/owner-v1/masters")); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(1, await h.Db.KitOrders.Select(x => x.Status).SingleAsync()); Assert.False(await h.Db.WmsSequences.AnyAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Kitting_owner_native_fence_prevents_raw_component_writer_changing_a_read()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var owner = await GrantKit(h); var key = await SeedKit(h); Task<int>? writer = null; var blocked = false;
        hook.After = async c =>
        {
            if (!c.CommandText.Contains("WITH field_subject")) return; hook.After = null;
            writer = Task.Run(async () => { await using var db = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); return await db.KitMasterComponents.ExecuteUpdateAsync(s => s.SetProperty(x => x.RequiredQty, 2)); });
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                    AND query LIKE 'UPDATE %T_KitMasterComponent%') AS "Value"
                """).SingleAsync(timeout.Token)) await Task.Delay(20, timeout.Token);
            blocked = true; Assert.False(writer.IsCompleted);
        };
        KitMasterDetailV1? detail = null;
        try { detail = await KitService(h, owner).GetMasterOwnerAsync(key + "0000"); }
        finally { if (writer != null) await writer; }
        Assert.True(blocked); Assert.Equal("9999999999999.99999999", detail!.Components[0].RequiredQty);
        Assert.Equal(2, await h.Db.KitMasterComponents.Select(x => x.RequiredQty).SingleAsync());
    }
}
