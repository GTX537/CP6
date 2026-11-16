using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task GrantAllocationStockQuery(Harness h, int scope = 5)
    {
        const string resource = "wms-stock";
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(x => x.MenuKey == resource);
        if (menu == null)
        {
            menu = new() { MenuId = 403, MenuKey = resource, MenuName = "库存" };
            h.Db.Sys_Menus.Add(menu);
        }
        if (!await h.Db.Sys_MenuActions.AnyAsync(x => x.MenuId == menu.MenuId && x.ActionCode == "query"))
            h.Db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = "query", ActionName = "查询库存" });
        h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = "query" });
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = resource, ScopeType = scope });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
    }

    private static async Task SeedAllocationStock(Harness h, string product, decimal? price, string creator)
    {
        if (!await h.Db.Locations.AnyAsync(x => x.WarehouseCd == "W01" && x.LocationCd == "L"))
            h.Db.Locations.Add(new() { WarehouseCd = "W01", LocationCd = "L", AreaCd = "A" });
        h.Db.Stocks.Add(new()
        {
            WarehouseCd = "W01", LocationCd = "L", ProductCd = product, LotNo = "",
            AvailableQty = 10m, PhysicalQty = 10m, UnitPrice = price, Creator = creator
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
    }

    private static async Task AssertNoAllocation(Harness h, string number)
    {
        Assert.Equal(OutboundOrderStatus.Confirmed, await h.Db.OutboundOrders.Where(x => x.OutboundNo == number).Select(x => x.Status).SingleAsync());
        Assert.All(await h.Db.OutboundOrderDetails.Where(x => x.OutboundNo == number).AsNoTracking().ToArrayAsync(), line =>
        {
            Assert.Equal(0m, line.AllocatedQty);
            Assert.Null(line.AllocateTxnNo);
        });
        Assert.All(await h.Db.Stocks.AsNoTracking().ToArrayAsync(), stock => Assert.Equal(0m, stock.AllocatedQty));
        Assert.Equal(0, await h.Db.StockTransactions.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Operations_allocate_requires_current_stock_query_even_when_candidate_price_is_empty()
    {
        await using var h = await Seed();
        var owner = await GrantOperations(h);
        await SeedAllocationStock(h, "P", null, "price-owner");
        var service = OperationsWms(h, owner);
        var number = await service.CreateOrderAsync(NewOutbound(), null);
        await service.ConfirmOrderAsync(number, null);

        var error = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.AllocateAsync(number, null));
        Assert.Equal("ACTION_FORBIDDEN", error.Message);
        await AssertNoAllocation(h, number);
    }

    [CurrentPermissionPostgresFact]
    public async Task Operations_allocate_stock_price_uses_its_current_field_and_object_scope_before_copying()
    {
        await using var h = await Seed();
        var owner = await GrantOperations(h);
        await GrantAllocationStockQuery(h, scope: 1);
        await SeedAllocationStock(h, "P", 12.3456m, "price-owner");
        await SeedAllocationStock(h, "P2", 99999999999999.9999m, "other");
        var service = OperationsWms(h, owner);
        var draft = NewOutbound();
        draft.Details.Add(new() { ProductCd = "P2", RequiredQty = 2.12345678m });
        var number = await service.CreateOrderAsync(draft, null);
        await service.ConfirmOrderAsync(number, null);

        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-stock", FieldName = "UnitPrice", Access = 3 });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var hidden = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.AllocateAsync(number, null));
        Assert.Equal("FIELD_READ_FORBIDDEN", hidden.Message);
        await AssertNoAllocation(h, number);

        await h.Db.Sys_RoleFieldPerms.Where(x => x.ResourceKey == "wms-stock").ExecuteDeleteAsync();
        var outside = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.AllocateAsync(number, null));
        Assert.Equal("OBJECT_FORBIDDEN", outside.Message);
        await AssertNoAllocation(h, number);
        Assert.Null((await service.GetOrderAsync(number))!.Details.Single(x => x.ProductCd == "P2").UnitPrice);

        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "wms-stock")
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ScopeType, 5));
        await service.AllocateAsync(number, null);
        var details = (await service.GetOrderAsync(number))!.Details.OrderBy(x => x.LineNo).ToArray();
        Assert.Equal(draft.Details[0].UnitPrice, details[0].UnitPrice);
        Assert.Equal(99999999999999.9999m, details[1].UnitPrice);
        var movements = await h.Db.StockTransactions.OrderBy(x => x.ProductCd).ToArrayAsync();
        Assert.Equal(2, movements.Length);
        Assert.Equal((12.3456m, 1.12345678m), (movements[0].UnitPrice!.Value, movements[0].Qty));
        Assert.Equal((99999999999999.9999m, 2.12345678m), (movements[1].UnitPrice!.Value, movements[1].Qty));
    }

    [CurrentPermissionPostgresFact]
    public async Task Operations_allocate_final_stock_source_failure_rolls_back_reservation_and_price()
    {
        var hook = new CommandHook();
        await using var h = await Seed(hook);
        var owner = await GrantOperations(h);
        await GrantAllocationStockQuery(h);
        await SeedAllocationStock(h, "P", 12.3456m, "price-owner");
        var service = OperationsWms(h, owner);
        var draft = NewOutbound();
        draft.Details[0].UnitPrice = null;
        var number = await service.CreateOrderAsync(draft, null);
        await service.ConfirmOrderAsync(number, null);
        var reads = 0;
        hook.Before = command =>
        {
            if (command.CommandText.Contains("WITH field_subject") &&
                Equals(command.Parameters["field_resource"].Value, "wms-stock") && ++reads == 2)
                throw new IOException("库存单价来源最终权限检查失败");
            return Task.CompletedTask;
        };

        try
        {
            var error = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.AllocateAsync(number, null));
            Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", error.Message);
        }
        finally { hook.Before = null; }
        Assert.Equal(2, reads);
        await AssertNoAllocation(h, number);
        Assert.Null(Assert.Single((await service.GetOrderAsync(number))!.Details).UnitPrice);
    }
}
