using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task GrantReportWarehouseName(Harness h)
    {
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(m => m.MenuId == 401);
        if (menu == null) h.Db.Sys_Menus.Add(new() { MenuId = 401, MenuKey = "wms-warehouse", MenuName = "仓库" });
        else menu.MenuKey = "wms-warehouse";
        if (!await h.Db.Sys_MenuActions.AnyAsync(a => a.MenuId == 401 && a.ActionCode == "query"))
            h.Db.Sys_MenuActions.Add(new() { MenuId = 401, ActionCode = "query", ActionName = "查询" });
        h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = 401, ActionCode = "query" });
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "wms-warehouse", ScopeType = 5 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }

    private static async Task SeedDwellCustomerName(Harness h)
    {
        h.Db.BusinessPartners.Add(new() { BpCd = "C", BpName = "受控客户名称", Creator = "price-owner" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await h.Db.Stocks.ExecuteUpdateAsync(s => s.SetProperty(x => x.OwnerType, StockOwnerType.Customer).SetProperty(x => x.OwnerCd, "C"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Inventory_report_warehouse_name_obeys_source_fields_action_and_scope_without_hiding_stock_value()
    {
        await using var h = await Seed(); var owner = await GrantStocktakeDashboard(h); await SeedInventory(h);
        await GrantReportWarehouseName(h);
        var service = new WmsDashboardService(h.Db, owner);
        var original = Assert.Single(await service.GetWarehouseValueAsync()); Assert.Equal("Inventory", original.WarehouseName);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-warehouse", FieldName = "WarehouseName", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        async Task Hidden()
        {
            var row = Assert.Single(await service.GetWarehouseValueAsync()); Assert.Null(row.WarehouseName);
            Assert.Equal(original.WarehouseCd, row.WarehouseCd); Assert.Equal(original.StockValue, row.StockValue); Assert.Equal(original.SkuCount, row.SkuCount);
        }
        await Hidden();
        await h.Db.Sys_RoleFieldPerms.Where(x => x.ResourceKey == "wms-warehouse").ExecuteDeleteAsync();
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "wms-warehouse").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
        await h.Db.Warehouses.ExecuteUpdateAsync(s => s.SetProperty(x => x.Creator, "other")); await Hidden();
        await h.Db.Warehouses.ExecuteUpdateAsync(s => s.SetProperty(x => x.Creator, "price-owner"));
        Assert.Equal("Inventory", Assert.Single(await service.GetWarehouseValueAsync()).WarehouseName);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 401 && x.ActionCode == "query").ExecuteDeleteAsync(); await Hidden();
        h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = 401, ActionCode = "query" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await using var foreign = await Seed(); await SeedInventory(foreign);
        await foreign.Db.Warehouses.ExecuteUpdateAsync(s => s.SetProperty(x => x.WarehouseName, "其他租户名称"));
        await h.Db.Warehouses.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true)); await Hidden();
    }

    [CurrentPermissionPostgresFact]
    public async Task Inventory_report_customer_name_intersects_own_and_source_fields_action_and_scope()
    {
        await using var h = await Seed(); var owner = await GrantInventoryReport(h); await SeedInventoryReport(h);
        await GrantBusinessPartnerIdentity(h); await SeedDwellCustomerName(h);
        var service = new StockDwellService(h.Db, owner); var query = DwellQuery(); query.GroupBy = "customer";
        var original = await service.GetObservationAsync(query); Assert.Equal("受控客户名称", Assert.Single(original.Rows).GroupLabel);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "erp-business-partner", FieldName = "BpName", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        async Task Hidden(bool fieldReadable)
        {
            var value = await service.GetObservationAsync(query); Assert.Equal("C", Assert.Single(value.Rows).GroupLabel);
            Assert.Equal(fieldReadable, value.CustomerNameReadable); Assert.Equal(original.TotalQty, value.TotalQty); Assert.Equal(original.ReferenceValue, value.ReferenceValue);
        }
        await Hidden(false);
        await h.Db.Sys_RoleFieldPerms.Where(x => x.ResourceKey == "erp-business-partner").ExecuteDeleteAsync();
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "erp-business-partner").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
        await h.Db.BusinessPartners.ExecuteUpdateAsync(s => s.SetProperty(x => x.Creator, "other")); await Hidden(true);
        await h.Db.BusinessPartners.ExecuteUpdateAsync(s => s.SetProperty(x => x.Creator, "price-owner"));
        Assert.Equal("受控客户名称", Assert.Single((await service.GetObservationAsync(query)).Rows).GroupLabel);
        var menu = await h.Db.Sys_Menus.SingleAsync(x => x.MenuKey == "erp-business-partner");
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu.MenuId && x.ActionCode == "query").ExecuteDeleteAsync(); await Hidden(false);
        h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = "query" });
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-stock-dwell", FieldName = "CustomerName", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); await Hidden(false);
        await h.Db.Sys_RoleFieldPerms.Where(x => x.ResourceKey == "wms-stock-dwell").ExecuteDeleteAsync();
        await using var foreign = await Seed(); await SeedDwellCustomerName(foreign);
        await foreign.Db.BusinessPartners.ExecuteUpdateAsync(s => s.SetProperty(x => x.BpName, "其他租户客户"));
        await h.Db.BusinessPartners.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true)); await Hidden(true);
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Inventory_report_name_source_final_failure_discards_the_whole_result(bool warehouse)
    {
        var hook = new CommandHook(); await using var h = await Seed(hook);
        var owner = warehouse ? await GrantStocktakeDashboard(h) : await GrantInventoryReport(h);
        await SeedInventoryReport(h);
        if (warehouse) await GrantReportWarehouseName(h);
        else { await GrantBusinessPartnerIdentity(h); await SeedDwellCustomerName(h); }
        var resource = warehouse ? "wms-warehouse" : "erp-business-partner"; var reads = 0;
        hook.Before = command =>
        {
            if (command.CommandText.Contains("WITH field_subject") && Equals(command.Parameters["field_resource"].Value, resource) && ++reads == 2)
                throw new IOException("名称来源最终权限读取失败");
            return Task.CompletedTask;
        };
        var error = await Assert.ThrowsAsync<OrderPriceCorrectionException>(async () =>
        {
            if (warehouse) await new WmsDashboardService(h.Db, owner).GetWarehouseValueAsync();
            else { var query = DwellQuery(); query.GroupBy = "customer"; await new StockDwellService(h.Db, owner).GetObservationAsync(query); }
        });
        hook.Before = null; Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", error.Message); Assert.Equal(2, reads);
        Assert.Null(h.Db.Database.CurrentTransaction); Assert.Equal(0, await h.Db.StockTransactions.CountAsync());
    }
}
