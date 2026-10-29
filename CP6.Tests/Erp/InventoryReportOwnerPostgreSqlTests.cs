using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Entity.DTOs;
using CP6.Tests.Platform;
using Moq;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Net;
using System.Text.Json;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Inventory_report_owner_missing_dwell_authority_rejects_even_empty_result()
    {
        await using var h = await Seed();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new StockDwellService(h.Db)
            .GetSummaryAsync(new StockDwellQuery { AsOfDate = new(2026, 10, 23) }));
    }

    [CurrentPermissionPostgresFact]
    public async Task Inventory_report_owner_missing_expiry_authority_rejects_even_empty_result()
    {
        await using var h = await Seed();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new ExpiryService(h.Db, Mock.Of<IStockMovementService>())
            .GetExpiringStocksAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Inventory_report_dispose_empty_request_cannot_claim_success_without_disposition_owner()
    {
        await using var h = await Seed(); var stock = new Mock<IStockMovementService>(MockBehavior.Strict);
        var failure = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new ExpiryService(h.Db, stock.Object)
            .DisposeAsync(new DisposeRequest(), "forged-approver"));
        Assert.Equal("DISPOSITION_OWNER_REQUIRED", failure.Message);
        stock.VerifyNoOtherCalls();
    }

    private static readonly DateTime ObservationDate = new(2026, 10, 23);
    private static StockDwellQuery DwellQuery() => new() { AsOfDate = ObservationDate };
    private static ExpiryObservationQueryV1 ExpiryQuery() => new() { AsOfDate = ObservationDate };
    private static async Task<OperationsOwnerV1> GrantInventoryReport(Harness h)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var (key, id, action, fields) in new[] {
            ("wms-stock", 403, "query", new[] { "UnitPrice" }),
            ("wms-stock-dwell", 483, "view", new[] { "Amount", "CustomerName" }),
            ("wms-expiry", 428, "view", new[] { "Amount", "UnitPrice" }) })
        {
            var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(m => m.MenuId == id);
            if (menu == null) h.Db.Sys_Menus.Add(new() { MenuId = id, MenuKey = key, MenuName = key }); else menu.MenuKey = key;
            if (!await h.Db.Sys_MenuActions.AnyAsync(a => a.MenuId == id && a.ActionCode == action))
                h.Db.Sys_MenuActions.Add(new() { MenuId = id, ActionCode = action, ActionName = action });
            h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = id, ActionCode = action });
            h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = key, ScopeType = 5 });
            catalog.RegisterScope(key, key, [1, 2, 3, 4, 5], 1); catalog.RegisterFields(key, fields.Select(f => new FieldRegistry.Field(f, f)));
        }
        h.Db.WmsRoleScopes.Add(new() { RoleId = 72, WarehouseCd = "*", AreaCd = "*" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); catalog.Seal();
        return new(h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
    }
    private static async Task SeedInventoryReport(Harness h)
    {
        await SeedInventory(h);
        await h.Db.Stocks.ExecuteUpdateAsync(s => s.SetProperty(x => x.ReceiveDate, ObservationDate.AddDays(-100))
            .SetProperty(x => x.ExpiryDate, ObservationDate.AddHours(23)));
    }
    private static ExpiryService ExpiryReport(Harness h, OperationsOwnerV1 owner) => new(h.Db, Mock.Of<IStockMovementService>(), owner);

    [CurrentPermissionPostgresFact]
    public async Task Inventory_report_exact_maximum_product_and_sum_never_round_or_invent_finance_valuation()
    {
        await using var h = await Seed(); var owner = await GrantInventoryReport(h); await SeedInventoryReport(h);
        await h.Db.Stocks.ExecuteUpdateAsync(s => s.SetProperty(x => x.PhysicalQty, 9999999999999.99999999m)
            .SetProperty(x => x.AvailableQty, 9999999999999.99999999m).SetProperty(x => x.UnitPrice, 99999999999999.9999m));
        var dwell = new StockDwellService(h.Db, owner); var result = await dwell.GetObservationAsync(DwellQuery());
        Assert.Equal("19999999999999.99999998", result.TotalQty);
        Assert.Equal("1999999999999999997998000000.000000000002", result.ReferenceValue);
        Assert.Equal(result.TotalQty, result.Over90Qty); Assert.Equal("UNKNOWN", result.ValuationStatus); Assert.Null(result.Currency);
        var row = (await ExpiryReport(h, owner).GetObservationAsync(ExpiryQuery())).Rows[0];
        Assert.Equal("999999999999999998999000000.000000000001", row.ReferenceValue);
        Assert.Equal("99999999999999.9999", row.UnitPrice); Assert.Equal(0, row.DaysUntilExpiry);
        Assert.Equal("OWNER_READ_VERSION_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => dwell.GetSummaryAsync(DwellQuery()))).Message);
        Assert.All(await ExpiryReport(h, owner).GetExpiringStocksAsync(), x => Assert.Null(x.LossAmount));
    }

    [CurrentPermissionPostgresFact]
    public async Task Inventory_report_missing_price_and_hidden_fields_do_not_manufacture_zero_or_customer_names()
    {
        await using var h = await Seed(); var owner = await GrantInventoryReport(h); await SeedInventoryReport(h);
        h.Db.BusinessPartners.Add(new() { BpCd = "C", BpName = "Protected customer" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await h.Db.Stocks.ExecuteUpdateAsync(s => s.SetProperty(x => x.OwnerType, StockOwnerType.Customer).SetProperty(x => x.OwnerCd, "C"));
        await h.Db.Stocks.Where(x => x.ProductCd == "P2").ExecuteUpdateAsync(s => s.SetProperty(x => x.UnitPrice, (decimal?)null));
        var query = DwellQuery(); query.GroupBy = "customer"; var service = new StockDwellService(h.Db, owner);
        var value = await service.GetObservationAsync(query); Assert.Null(value.ReferenceValue); Assert.Equal(1, value.MissingPriceCount);
        Assert.Equal("Protected customer", Assert.Single(value.Rows).GroupLabel);
        Assert.Null((await ExpiryReport(h, owner).GetObservationAsync(ExpiryQuery())).Rows.Single(x => x.ProductCd == "P2").ReferenceValue);
        foreach (var (resource, field) in new[] { ("wms-stock-dwell", "Amount"), ("wms-stock-dwell", "CustomerName"), ("wms-expiry", "UnitPrice") })
            h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = resource, FieldName = field, Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        value = await service.GetObservationAsync(query); Assert.False(value.ReferenceReadable); Assert.Null(value.MissingPriceCount);
        Assert.Null(value.ReferenceValue); Assert.Equal("C", Assert.Single(value.Rows).GroupLabel);
        var expiry = await ExpiryReport(h, owner).GetObservationAsync(ExpiryQuery()); Assert.False(expiry.UnitPriceReadable);
        Assert.All(expiry.Rows, x => { Assert.Null(x.UnitPrice); Assert.Null(x.ReferenceValue); });
        h.Db.Sys_Roles.Add(new() { RoleId = 73, RoleName = "Explicit permissive role" }); h.Db.Sys_UserRoles.Add(new() { UserId = h.User, RoleId = 73 });
        foreach (var (resource, field) in new[] { ("wms-stock-dwell", "Amount"), ("wms-stock-dwell", "CustomerName"), ("wms-expiry", "UnitPrice") })
            h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 73, ResourceKey = resource, FieldName = field, Access = 1 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        value = await service.GetObservationAsync(query); Assert.True(value.ReferenceReadable); Assert.Equal("Protected customer", Assert.Single(value.Rows).GroupLabel);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-stock", FieldName = "UnitPrice", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.False((await service.GetObservationAsync(query)).ReferenceReadable);
        Assert.False((await ExpiryReport(h, owner).GetObservationAsync(ExpiryQuery())).ReferenceReadable);
    }

    [CurrentPermissionPostgresFact]
    public async Task Inventory_report_self_source_and_area_intersections_are_applied_before_aggregation()
    {
        await using var h = await Seed(); var owner = await GrantInventoryReport(h); await SeedInventoryReport(h);
        await using var foreign = await Seed(); await SeedInventoryReport(foreign);
        await h.Db.Stocks.Where(x => x.ProductCd == "P2").ExecuteUpdateAsync(s => s.SetProperty(x => x.Creator, "other"));
        var dwell = new StockDwellService(h.Db, owner); var expiry = ExpiryReport(h, owner);
        foreach (var source in new[] { "wms-stock", "wms-stock-dwell", "wms-expiry" })
        {
            await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey != "order").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 5));
            await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == source).ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
            if (source != "wms-expiry") Assert.Equal("10.12345678", (await dwell.GetObservationAsync(DwellQuery())).TotalQty);
            if (source != "wms-stock-dwell") Assert.Equal(1, (await expiry.GetObservationAsync(ExpiryQuery())).Total);
        }
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey != "order").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 5));
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync(); h.Db.WmsRoleScopes.Add(new() { RoleId = 72, WarehouseCd = "W01", AreaCd = "A" });
        await h.Db.Locations.Where(l=>l.WarehouseCd=="W01" && l.LocationCd=="L2").ExecuteUpdateAsync(s=>s.SetProperty(l=>l.AreaCd,"B"));
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal("10.12345678", (await dwell.GetObservationAsync(DwellQuery())).TotalQty); Assert.Equal(1, (await expiry.GetObservationAsync(ExpiryQuery())).Total);
    }

    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Inventory_report_department_scopes_require_actual_stock_uuid_and_healthy_ancestry(int scope)
    {
        await using var h = await Seed(); var owner = await GrantInventoryReport(h); await SeedInventoryReport(h);
        var admin = await MappingAdmin(h); await StandaloneScope(h, admin, scope);
        var dwell = new StockDwellService(h.Db, owner); var expiry = ExpiryReport(h, owner);
        Assert.Empty((await dwell.GetObservationAsync(DwellQuery())).Rows); Assert.Equal(0, (await expiry.GetObservationAsync(ExpiryQuery())).Total);
        await BindStandalone(h, admin, "stock", await h.Db.Stocks.Where(s => s.ProductCd == "P1").Select(s => s.Id).SingleAsync());
        Assert.Equal("10.12345678", (await dwell.GetObservationAsync(DwellQuery())).TotalQty); Assert.Equal(1, (await expiry.GetObservationAsync(ExpiryQuery())).Total);
        await h.Db.Sys_Depts.Where(x => x.Id == admin.Root).ExecuteUpdateAsync(s => s.SetProperty(x => x.Enable, false));
        if (scope == 4) { Assert.Empty((await dwell.GetObservationAsync(DwellQuery())).Rows); Assert.Equal(0, (await expiry.GetObservationAsync(ExpiryQuery())).Total); }
        else { await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => dwell.GetObservationAsync(DwellQuery())); await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => expiry.GetObservationAsync(ExpiryQuery())); }
    }

    [CurrentPermissionPostgresFact]
    public async Task Inventory_report_explicit_calendar_range_and_pagination_do_not_silently_truncate_at_500()
    {
        await using var h = await Seed(); var owner = await GrantInventoryReport(h);
        for (var i = 0; i < 503; i++) h.Db.Stocks.Add(new() { WarehouseCd = "W", LocationCd = "L", ProductCd = i.ToString("D4"), LotNo = "L",
            PhysicalQty = 1, AvailableQty = 1, ExpiryDate = ObservationDate.AddHours(23), Creator = "price-owner" });
        h.Db.Stocks.Add(new() { WarehouseCd = "W", LocationCd = "L", ProductCd = "UNKNOWN", PhysicalQty = 1 });
        h.Db.Stocks.Add(new() { WarehouseCd = "W", LocationCd = "L", ProductCd = "LATER", PhysicalQty = 1, ExpiryDate = ObservationDate.AddDays(1) });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); var service = ExpiryReport(h, owner);
        var q = ExpiryQuery(); q.Days = 0; q.PageSize = 500; var first = await service.GetObservationAsync(q);
        Assert.Equal(503, first.Total); Assert.Equal(500, first.Rows.Count); q.Page = 2; var second = await service.GetObservationAsync(q);
        Assert.Equal(503, second.Total); Assert.Equal(3, second.Rows.Count); Assert.Empty(first.Rows.Select(x => x.StockId).Intersect(second.Rows.Select(x => x.StockId)));
        q.Page = 1; q.Days = 1; Assert.Equal(504, (await service.GetObservationAsync(q)).Total);
        q.Days = -1; await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.GetObservationAsync(q)); q.Days = 366;
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.GetObservationAsync(q)); q.Days = 0; q.AsOfDate = null;
        Assert.Equal("OBSERVATION_DATE_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.GetObservationAsync(q))).Message);
        q.AsOfDate = DateTime.MaxValue.Date; q.Days = 0; Assert.Equal(504, (await service.GetObservationAsync(q)).Total);
        q.Days = 1; await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.GetObservationAsync(q));
    }

    [CurrentPermissionPostgresFact]
    public async Task Inventory_report_calendar_buckets_filters_and_legacy_signed_window_preserve_their_distinct_meanings()
    {
        await using var h = await Seed(); var owner = await GrantInventoryReport(h);
        foreach (var age in new[] { -1, 0, 30, 31, 60, 61, 90, 91 }) h.Db.Stocks.Add(new() {
            WarehouseCd = "W", LocationCd = "L", ProductCd = "P", LotNo = age.ToString(), OwnerCd = "C", OwnerType = StockOwnerType.Customer,
            PhysicalQty = 0.00000001m, AvailableQty = 0.00000001m, UnitPrice = 0.0001m, ReceiveDate = ObservationDate.AddDays(-age).AddHours(12),
            ExpiryDate = DateTime.Today.AddDays(-1), Creator = "price-owner" });
        h.Db.Stocks.Add(new() { WarehouseCd = "W", LocationCd = "L", ProductCd = "MISSING_RECEIVE", PhysicalQty = 100, UnitPrice = 9 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var q = DwellQuery(); q.WarehouseCd = " W "; q.ProductCd = "P"; q.OwnerCd = " C ";
        var value = await new StockDwellService(h.Db, owner).GetObservationAsync(q); var row = Assert.Single(value.Rows);
        Assert.Equal("0.00000008", value.TotalQty); Assert.Equal("0.000000000008", value.ReferenceValue);
        Assert.Equal("0.00000003", row.Bucket0To30Qty); Assert.Equal("0.00000002", row.Bucket31To60Qty);
        Assert.Equal("0.00000002", row.Bucket61To90Qty); Assert.Equal("0.00000001", row.BucketOver90Qty); Assert.Equal(91, row.OldestAgeDays);
        Assert.Equal(8, (await ExpiryReport(h, owner).GetExpiringStocksAsync(-1)).Count);
        Assert.Empty(await ExpiryReport(h, owner).GetExpiringStocksAsync(-2));
        await h.Db.Stocks.ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiryDate, DateTime.Today.AddHours(12)));
        Assert.Empty(await ExpiryReport(h, owner).GetExpiringStocksAsync(0));
        Assert.Equal(9, (await ExpiryReport(h, owner).GetObservationAsync(new() { AsOfDate = DateTime.Today, Days = 0 })).Total);
    }

    [CurrentPermissionPostgresFact]
    public async Task Inventory_report_current_actions_identity_outer_transaction_and_tracked_stock_cannot_be_bypassed()
    {
        await using var h = await Seed(); var owner = await GrantInventoryReport(h); await SeedInventoryReport(h);
        var dwell = new StockDwellService(h.Db, owner); var expiry = ExpiryReport(h, owner);
        await h.Db.Stocks.FirstAsync(); await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => dwell.GetObservationAsync(DwellQuery())); h.Db.ChangeTracker.Clear();
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        { await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => expiry.GetObservationAsync(ExpiryQuery())); Assert.Same(tx, h.Db.Database.CurrentTransaction); await tx.RollbackAsync(); }
        h.Http.User = CurrentPermissionContextTests.Principal(h.User, Guid.NewGuid());
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => dwell.GetObservationAsync(DwellQuery()));
        h.Http.User = CurrentPermissionContextTests.Principal(h.User, h.Tenant);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 403).ExecuteDeleteAsync();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => dwell.GetObservationAsync(DwellQuery())); await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => expiry.GetObservationAsync(ExpiryQuery()));
    }

    [CurrentPermissionPostgresFact]
    public async Task Inventory_report_request_is_frozen_native_stock_write_waits_and_final_authority_failure_discards_result()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var owner = await GrantInventoryReport(h); await SeedInventoryReport(h);
        var input = DwellQuery(); var attempted = false; var service = new StockDwellService(h.Db, owner);
        hook.After = async c => {
            if (!c.CommandText.Contains("WITH field_subject")) return; hook.After = null; input.WarehouseCd = "LATE"; attempted = true;
            await using var writer = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); await using var tx = await writer.Database.BeginTransactionAsync();
            await writer.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '200ms'");
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.Stocks.ExecuteUpdateAsync(s => s.SetProperty(x => x.PhysicalQty, 999m)));
            Assert.Equal("55P03", Assert.IsType<PostgresException>(failure.InnerException).SqlState); await tx.RollbackAsync();
        };
        Assert.Equal("30.12345678", (await service.GetObservationAsync(input)).TotalQty); Assert.True(attempted);
        var reads = 0; hook.Before = c => { if (c.CommandText.Contains("WITH field_subject") && ++reads == 3) throw new IOException("final observation authority"); return Task.CompletedTask; };
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.GetObservationAsync(DwellQuery())); hook.Before = null;
        Assert.Equal(3, reads); Assert.Equal(0, await h.Db.StockTransactions.CountAsync()); Assert.Null(h.Db.Database.CurrentTransaction);
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Inventory_report_raw_revocation_is_ordered_before_or_after_the_whole_read(bool before)
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var owner = await GrantInventoryReport(h); await SeedInventoryReport(h);
        Task? revoker = null; var observed = false;
        async Task Revoke() { await using var db = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Roles\" SET \"Enable\"=false WHERE \"TenantId\"={h.Tenant} AND \"RoleId\"=72"); }
        if (before) hook.Before = async c => { if (!c.CommandText.Contains("cp6_authentication_v1.acquire()")) return; hook.Before = null; await Revoke(); observed = true; };
        else hook.After = async c => {
            if (!c.CommandText.Contains("WITH field_subject")) return; hook.After = null; revoker = Task.Run(Revoke);
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                  AND query LIKE 'UPDATE public."Sys_Roles"%') AS "Value"
                """).SingleAsync(timeout.Token)) await Task.Delay(20, timeout.Token);
            observed = true; Assert.False(revoker.IsCompleted);
        };
        var service = ExpiryReport(h, owner);
        try { if (before) await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.GetObservationAsync(ExpiryQuery()));
            else Assert.Equal(2, (await service.GetObservationAsync(ExpiryQuery())).Total); }
        finally { if (revoker != null) await revoker; }
        Assert.True(observed); await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.GetObservationAsync(ExpiryQuery()));
    }

    [CurrentPermissionPostgresFact]
    public async Task Inventory_report_dispose_nonempty_is_zero_database_commands_and_zero_downstream_effects()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); await SeedInventoryReport(h);
        var stock = new Mock<IStockMovementService>(MockBehavior.Strict); var commands = 0;
        hook.Before = _ => { commands++; return Task.CompletedTask; };
        var error = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new ExpiryService(h.Db, stock.Object)
            .DisposeAsync(new() { StockIds = [Guid.NewGuid(), Guid.NewGuid()], Reason = "approved-by-caller" }, "forged"));
        Assert.Equal("DISPOSITION_OWNER_REQUIRED", error.Message); Assert.Equal(0, commands); stock.VerifyNoOtherCalls(); hook.Before = null;
        Assert.Equal(0, await h.Db.StockTransactions.CountAsync()); Assert.Equal(2, await h.Db.Stocks.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Inventory_report_actual_program_http_preserves_strings_no_store_and_does_not_call_legacy_disposal()
    {
        await using var h = await Seed(); await GrantInventoryReport(h); await SeedInventoryReport(h);
        if (!await h.Db.Sys_MenuActions.AnyAsync(x => x.MenuId == 428 && x.ActionCode == "dispose")) h.Db.Sys_MenuActions.Add(new() { MenuId = 428, ActionCode = "dispose", ActionName = "Dispose" });
        h.Db.Sys_RoleActions.Add(new() { MenuId = 428, RoleId = 72, ActionCode = "dispose" }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        foreach (var path in new[] { "api/wms/stock-dwell/observation-v1", "api/wms/expiry/observation-v1" })
        {
            using var get = await host.Send(OperationsRequest(cookies, HttpMethod.Post, path, new { asOfDate = "2026-10-23", days = 0 }));
            Assert.Equal(HttpStatusCode.OK, get.StatusCode); Assert.True(get.Headers.CacheControl?.NoStore);
            using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync()); var data = json.RootElement.GetProperty("data");
            Assert.Equal(InventoryObservationV1.Version, data.GetProperty("version").GetString()); Assert.Equal(JsonValueKind.Null, data.GetProperty("currency").ValueKind);
            var field = path.Contains("stock-dwell") ? data.GetProperty("totalQty") : data.GetProperty("rows")[0].GetProperty("physicalQty");
            Assert.Equal(JsonValueKind.String, field.ValueKind);
        }
        using var dispose = await host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/wms/expiry/dispose", new { stockIds = Array.Empty<Guid>() }));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, dispose.StatusCode); Assert.True(dispose.Headers.CacheControl?.NoStore);
        Assert.Contains("DISPOSITION_OWNER_REQUIRED", await dispose.Content.ReadAsStringAsync()); Assert.Equal(0, await h.Db.StockTransactions.CountAsync());
        await h.Db.Sys_RoleActions.Where(x => (x.MenuId == 428 || x.MenuId == 483) && x.ActionCode == "view").ExecuteDeleteAsync();
        foreach (var path in new[] { "api/wms/stock-dwell/observation-v1", "api/wms/expiry/observation-v1", "api/wms/stock-dwell/summary" })
        { using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Post, path, new { asOfDate = "2026-10-23" })); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); }
        using var old = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/expiry/expiring")); Assert.Equal(HttpStatusCode.Forbidden, old.StatusCode);
    }
}
