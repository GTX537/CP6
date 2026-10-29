using System.Net;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Common;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static Stock CustodyStock(string location) => new()
    {
        WarehouseCd="W01",LocationCd=location,ProductCd="P1",LotNo="LOT",Creator="price-owner",
        PhysicalQty=2m,AvailableQty=2m,OwnerType=StockOwnerType.Customer,OwnerCd="CUST1",UnitCd="BOX",
        UnitPrice=123.4567m,ReceiveDate=new(2026,10,1),ExpiryDate=new(2027,10,1),PaperRollNo="ROLL-1",
        QcStatus=StockQcStatus.Hold,RecallFlag=true
    };
    private static async Task SeedMoveCustody(Harness h)
    {
        await SeedInventory(h);
        var source=await h.Db.Stocks.SingleAsync(s=>s.ProductCd=="P1");
        source.OwnerType=StockOwnerType.Customer;source.OwnerCd="CUST1";source.UnitCd="BOX";
        source.UnitPrice=123.4567m;source.ReceiveDate=new(2026,10,1);source.ExpiryDate=new(2027,10,1);
        source.PaperRollNo="ROLL-1";source.QcStatus=StockQcStatus.Hold;source.RecallFlag=true;
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
    }
    private static void AssertMoveCustody(Stock stock)
    {
        Assert.Equal(StockOwnerType.Customer,stock.OwnerType);Assert.Equal("CUST1",stock.OwnerCd);
        Assert.Equal("BOX",stock.UnitCd);Assert.Equal(123.4567m,stock.UnitPrice);
        Assert.Equal(new DateTime(2026,10,1),stock.ReceiveDate);Assert.Equal(new DateTime(2027,10,1),stock.ExpiryDate);
        Assert.Equal("ROLL-1",stock.PaperRollNo);Assert.Equal(StockQcStatus.Hold,stock.QcStatus);Assert.True(stock.RecallFlag);
    }
    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Stock_move_custody_new_target_and_both_transactions_preserve_source_without_price_grant(bool legacy)
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedMoveCustody(h);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-stock",FieldName="UnitPrice",Access=3});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var mover=InventoryMover(h,owner);var command=StockMove();
        if(legacy)await mover.MoveAsync(command);else await mover.MoveCommandAsync(command);
        var source=await h.Db.Stocks.AsNoTracking().SingleAsync(s=>s.ProductCd=="P1" && s.LocationCd=="L1");
        var target=await h.Db.Stocks.AsNoTracking().SingleAsync(s=>s.LocationCd=="L3");
        AssertMoveCustody(source);AssertMoveCustody(target);
        Assert.Equal(10m,source.PhysicalQty);Assert.Equal(.12345678m,target.PhysicalQty);
        Assert.Equal(target.PhysicalQty,target.AvailableQty);Assert.Equal("price-owner",target.Creator);
        var legs=await h.Db.StockTransactions.AsNoTracking().OrderBy(t=>t.Qty).ToArrayAsync();Assert.Equal(2,legs.Length);
        Assert.Equal(-.12345678m,legs[0].Qty);Assert.Equal(.12345678m,legs[1].Qty);Assert.Equal(legs[0].TxnNo,legs[1].RelatedNo);
        Assert.All(legs,t=>{Assert.Equal("CUSTOMER",t.OwnerType);Assert.Equal("CUST1",t.OwnerCd);Assert.Equal("BOX",t.UnitCd);
            Assert.Equal(123.4567m,t.UnitPrice);Assert.Equal("ROLL-1",t.PaperRollNo);Assert.Equal("price-owner",t.OperatorCd);});
        if(!legacy)
        {
            Assert.Equal(1,await StockNotifications(h));Assert.Equal(1,await Operations(h));
            Assert.DoesNotContain("123.4567",JsonSerializer.Serialize(await mover.ReadOperationAsync(command.OperationId)));
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task Stock_move_custody_compatible_target_merges_without_rewriting_history_or_allocation()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedMoveCustody(h);
        var target=CustodyStock("L3");target.AllocatedQty=1m;target.AvailableQty=1m;target.Creator="original-creator";
        h.Db.Stocks.Add(target);await h.Db.SaveChangesAsync();var id=target.Id;h.Db.ChangeTracker.Clear();
        await InventoryMover(h,owner).MoveCommandAsync(StockMove());
        target=await h.Db.Stocks.AsNoTracking().SingleAsync(s=>s.LocationCd=="L3");AssertMoveCustody(target);
        Assert.Equal(id,target.Id);Assert.Equal("original-creator",target.Creator);
        Assert.Equal(2.12345678m,target.PhysicalQty);Assert.Equal(1m,target.AllocatedQty);Assert.Equal(1.12345678m,target.AvailableQty);
    }
    [TenantManagementPostgresTheory]
    [InlineData("OwnerType")] [InlineData("OwnerCd")] [InlineData("UnitCd")] [InlineData("UnitPrice")]
    [InlineData("ReceiveDate")] [InlineData("ExpiryDate")] [InlineData("PaperRollNo")] [InlineData("QcStatus")] [InlineData("RecallFlag")]
    public async Task Stock_move_custody_incompatible_target_rejects_before_any_effect(string attribute)
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedMoveCustody(h);
        var target=CustodyStock("L3");
        switch(attribute)
        {
            case "OwnerType":target.OwnerType=StockOwnerType.Self;break;
            case "OwnerCd":target.OwnerCd="CUST2";break;
            case "UnitCd":target.UnitCd=null;break;
            case "UnitPrice":target.UnitPrice=null;break;
            case "ReceiveDate":target.ReceiveDate=null;break;
            case "ExpiryDate":target.ExpiryDate=null;break;
            case "PaperRollNo":target.PaperRollNo="ROLL-2";break;
            case "QcStatus":target.QcStatus=StockQcStatus.Passed;break;
            case "RecallFlag":target.RecallFlag=false;break;
        }
        h.Db.Stocks.Add(target);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var before=JsonSerializer.Serialize(await h.Db.Stocks.AsNoTracking().OrderBy(s=>s.Id).ToArrayAsync());
        var mover=InventoryMover(h,owner);var command=StockMove();
        var error=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.MoveCommandAsync(command));
        Assert.Equal("STOCK_MOVE_METADATA_CONFLICT",error.Message);Assert.Equal(409,error.Status);
        Assert.Equal(before,JsonSerializer.Serialize(await h.Db.Stocks.AsNoTracking().OrderBy(s=>s.Id).ToArrayAsync()));
        Assert.Equal(0,await h.Db.WmsSequences.CountAsync());Assert.Equal(0,await h.Db.StockTransactions.CountAsync());
        Assert.Equal(0,await Operations(h));Assert.Equal(0,await StockNotifications(h));
        Assert.Equal("UNKNOWN",(await mover.ReadOperationAsync(command.OperationId)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task Stock_move_custody_second_leg_failure_rolls_back_all_attributes_and_original()
    {
        var failure=new FailSecondAdjustment{Enabled=true};await using var h=await Seed(failure);
        var owner=await GrantStocktakeDashboard(h);await SeedMoveCustody(h);
        var before=JsonSerializer.Serialize(await h.Db.Stocks.AsNoTracking().OrderBy(s=>s.Id).ToArrayAsync());
        await Assert.ThrowsAnyAsync<Exception>(()=>InventoryMover(h,owner).MoveCommandAsync(StockMove()));Assert.False(failure.Enabled);
        Assert.Equal(before,JsonSerializer.Serialize(await h.Db.Stocks.AsNoTracking().OrderBy(s=>s.Id).ToArrayAsync()));
        Assert.Equal(0,await h.Db.WmsSequences.CountAsync());Assert.Equal(0,await h.Db.StockTransactions.CountAsync());
        Assert.Equal(0,await Operations(h));Assert.Equal(0,await StockNotifications(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task Stock_move_custody_unknown_commit_recovery_and_old_replay_do_not_recheck_or_rewrite_metadata()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var owner=await GrantStocktakeDashboard(h);await SeedMoveCustody(h);
        var mover=InventoryMover(h,owner);var command=StockMove();fault.Armed=true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.MoveCommandAsync(command))).Message);
        AssertMoveCustody(await h.Db.Stocks.AsNoTracking().SingleAsync(s=>s.LocationCd=="L3"));
        // An already committed result remains readable after later legitimate quality/custody changes.
        await h.Db.Stocks.Where(s=>s.LocationCd=="L3").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.QcStatus,StockQcStatus.Passed));
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==403 && a.ActionCode=="move").ExecuteDeleteAsync();
        var original=await mover.ReadOperationAsync(command.OperationId);Assert.Equal("COMMITTED",original.State);
        Assert.Equal(original.Result,await mover.MoveCommandAsync(command));Assert.Equal(original.Result,(await mover.CloseOperationAsync(command.OperationId)).Result);
        Assert.Equal(StockQcStatus.Passed,await h.Db.Stocks.Where(s=>s.LocationCd=="L3").Select(s=>s.QcStatus).SingleAsync());
        Assert.Equal(2,await h.Db.StockTransactions.CountAsync());Assert.Equal(1,await Operations(h));Assert.Equal(1,await StockNotifications(h));
    }
    [TenantManagementPostgresTheory]
    [InlineData(false,false)] [InlineData(true,false)] [InlineData(false,true)] [InlineData(true,true)]
    public async Task Stock_move_custody_preserves_allow_negative_policy_with_and_without_existing_source(bool missingSource,bool allowNegative)
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedMoveCustody(h);
        await h.Db.Warehouses.Where(w=>w.WarehouseCd=="W01").ExecuteUpdateAsync(s=>s.SetProperty(w=>w.AllowNegative,allowNegative));
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        if(missingSource)await h.Db.Stocks.Where(s=>s.ProductCd=="P1").ExecuteDeleteAsync();
        var command=StockMove();command.Qty=11.12345678m;
        if(!allowNegative)
        {
            await Assert.ThrowsAsync<InsufficientStockException>(()=>InventoryMover(h,owner).MoveCommandAsync(command));
            Assert.False(await h.Db.Stocks.AnyAsync(s=>s.LocationCd=="L3"));
            Assert.Equal(missingSource?0m:10.12345678m,await h.Db.Stocks.Where(s=>s.ProductCd=="P1").SumAsync(s=>s.PhysicalQty));
            Assert.Equal(0,await h.Db.StockTransactions.CountAsync());Assert.Equal(0,await h.Db.WmsSequences.CountAsync());
            Assert.Equal(0,await Operations(h));Assert.Equal(0,await StockNotifications(h));return;
        }
        await InventoryMover(h,owner).MoveCommandAsync(command);
        var source=await h.Db.Stocks.AsNoTracking().SingleAsync(s=>s.ProductCd=="P1" && s.LocationCd=="L1");
        var target=await h.Db.Stocks.AsNoTracking().SingleAsync(s=>s.LocationCd=="L3");
        Assert.Equal(missingSource?-11.12345678m:-1m,source.PhysicalQty);Assert.Equal(11.12345678m,target.PhysicalQty);
        if(missingSource){Assert.Equal("SELF",source.OwnerType);Assert.Equal("SELF",target.OwnerType);Assert.Null(target.OwnerCd);
            Assert.Null(target.UnitPrice);Assert.Equal(StockQcStatus.Pending,target.QcStatus);Assert.False(target.RecallFlag);}
        else{AssertMoveCustody(source);AssertMoveCustody(target);}
    }
    [CurrentPermissionPostgresFact]
    public async Task Stock_move_custody_actual_http_preserves_source_and_returns_only_safe_conflict()
    {
        await using var h=await Seed();await GrantStocktakeDashboard(h);await SeedMoveCustody(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        const string path="api/wms/stock/commands/v1/move";
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Post,path,StockMove())))
        {Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.DoesNotContain("123.4567",await response.Content.ReadAsStringAsync());}
        AssertMoveCustody(await h.Db.Stocks.AsNoTracking().SingleAsync(s=>s.LocationCd=="L3"));
        await h.Db.Stocks.Where(s=>s.LocationCd=="L3").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.OwnerCd,"CUST2"));
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Post,path,StockMove())))
        {Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);var body=await response.Content.ReadAsStringAsync();
            Assert.Contains("STOCK_MOVE_METADATA_CONFLICT",body);Assert.DoesNotContain("123.4567",body);Assert.DoesNotContain("CUST2",body);}
        Assert.Equal(2,await h.Db.StockTransactions.CountAsync());Assert.Equal(1,await Operations(h));Assert.Equal(1,await StockNotifications(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task Stock_move_custody_source_change_cannot_interleave_with_metadata_capture_and_both_legs()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantStocktakeDashboard(h);await SeedMoveCustody(h);
        var sourceId=await h.Db.Stocks.Where(s=>s.ProductCd=="P1").Select(s=>s.Id).SingleAsync();Task? competing=null;var blocked=false;
        hook.Before=async c=>
        {
            if(competing!=null || !c.CommandText.Contains("FROM \"T_Stock\"") || !c.CommandText.Contains("\"OwnerType\""))return;
            competing=Task.Run(async()=>
            {
                await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"T_Stock\" SET \"OwnerCd\"='CUST2' WHERE \"Id\"={sourceId} /*w97-custody-writer*/");
            });
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                AND query LIKE 'UPDATE%w97-custody-writer%') AS "Value"
                """).SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            Assert.False(competing.IsCompleted);blocked=true;
        };
        try{await InventoryMover(h,owner).MoveCommandAsync(StockMove());}
        finally{hook.Before=null;if(competing!=null)await competing;}
        Assert.True(blocked);AssertMoveCustody(await h.Db.Stocks.AsNoTracking().SingleAsync(s=>s.LocationCd=="L3"));
        Assert.Equal("CUST2",await h.Db.Stocks.Where(s=>s.Id==sourceId).Select(s=>s.OwnerCd).SingleAsync());
        Assert.All(await h.Db.StockTransactions.ToArrayAsync(),t=>Assert.Equal("CUST1",t.OwnerCd));
    }
}
