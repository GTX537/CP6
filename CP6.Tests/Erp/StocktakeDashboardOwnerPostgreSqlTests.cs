using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<OperationsOwnerV1> GrantStocktakeDashboard(Harness h)
    {
        var catalog=new AuthorizationCatalogStoreV1();
        foreach(var (key,id,actions,fields) in new[]{
            ("wms-stock",403,new[]{"query","adjust","move"},new[]{"UnitPrice"}),
            ("wms-stocktake",415,new[]{"query","add","count","submit","approve","cancel"},new[]{"Amount","UnitPrice"}),
            ("wms-dashboard",416,new[]{"query"},new[]{"Amount"}),
            ("mes-dashboard",309,new[]{"query"},Array.Empty<string>()),
            ("mes-work-order",302,new[]{"query"},Array.Empty<string>()),
            ("mes-production-result",304,new[]{"query"},Array.Empty<string>()),
            ("mes-defect",308,new[]{"query"},Array.Empty<string>()),
            ("wms-inbound-order",405,new[]{"query"},new[]{"UnitPrice"}),
            ("wms-outbound-order",408,new[]{"query"},new[]{"UnitPrice"})})
        {
            var menu=await h.Db.Sys_Menus.SingleOrDefaultAsync(m=>m.MenuId==id);
            if(menu==null)h.Db.Sys_Menus.Add(new(){MenuId=id,MenuKey=key,MenuName=key});else menu.MenuKey=key;
            foreach(var action in actions)
            {
                if(!await h.Db.Sys_MenuActions.AnyAsync(a=>a.MenuId==id && a.ActionCode==action))h.Db.Sys_MenuActions.Add(new(){MenuId=id,ActionCode=action,ActionName=action});
                h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=id,ActionCode=action});
            }
            h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey=key,ScopeType=5});
            catalog.RegisterScope(key,key,[1,2,3,4,5],1);catalog.RegisterFields(key,fields.Select(f=>new FieldRegistry.Field(f,f)));
        }
        h.Db.WmsRoleScopes.Add(new(){RoleId=72,WarehouseCd="*",AreaCd="*"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();catalog.Seal();
        return new(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),catalog.Capture);
    }
    private static StockMovementService InventoryMover(Harness h,OperationsOwnerV1 owner)
        =>new(h.Db,new WmsSequenceService(h.Db),owner:owner);
    private static StockTakeService InventoryTake(Harness h,OperationsOwnerV1 owner)
        =>new(h.Db,new WmsSequenceService(h.Db),InventoryMover(h,owner),owner);
    private static async Task SeedInventory(Harness h)
    {
        h.Db.Stocks.AddRange(new Stock{WarehouseCd="W01",LocationCd="L1",ProductCd="P1",LotNo="LOT",PhysicalQty=10.12345678m,AvailableQty=10.12345678m,UnitPrice=3m,Creator="price-owner"},
            new Stock{WarehouseCd="W01",LocationCd="L2",ProductCd="P2",LotNo="LOT",PhysicalQty=20m,AvailableQty=20m,UnitPrice=5m,Creator="price-owner"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
    }
    private static StockMovementRequest InventoryAdjustment()=>new(){TxnType=WmsTxnType.ADJ,WarehouseCd="W01",LocationCd="L1",ProductCd="P1",LotNo="LOT",Qty=1.12345678m,UnitPrice=3m,OperatorCd="forged"};
    private static async Task<string> CountInventory(Harness h,OperationsOwnerV1 owner)
    {
        var take=InventoryTake(h,owner);var no=await take.CreatePlanAsync(new(){TargetWarehouseCd="W01",ApprovalThresholdAmount=1m},"forged");
        await take.StartCountAsync(no,"forged");
        await take.UpdateCountsAsync(no,[new(){LineNo=1,CountedQty=9m,DiffReasonCd="COUNT"},new(){LineNo=2,CountedQty=19m,DiffReasonCd="COUNT"}],"forged");
        await take.SubmitForReviewAsync(no,"forged");return no;
    }
    [CurrentPermissionPostgresFact]
    public async Task Stocktake_dashboard_missing_Owner_rejects_service_reads()
    {
        await using var h=await Seed();var seq=new WmsSequenceService(h.Db);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new StockTakeService(h.Db,seq,new StockMovementService(h.Db,seq)).SearchAsync(new()));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new WmsDashboardService(h.Db).GetKpiAsync());
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new MesDashboardService(h.Db).GetSummaryAsync());
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new MesDashboardDapperService(h.Db.Database.GetDbConnection()).GetSummaryAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Stocktake_dashboard_missing_Owner_rejects_independent_adjustment()
    {
        await using var h=await Seed();var seq=new WmsSequenceService(h.Db);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new StockMovementService(h.Db,seq).ApplyAsync(new(){
            TxnType=WmsTxnType.ADJ,WarehouseCd="W",LocationCd="L",ProductCd="P",Qty=2m,OperatorCd="forged"}));
        Assert.Equal(0,await h.Db.Stocks.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Stocktake_dashboard_native_count_adjust_and_move_preserve_actor_precision_and_freeze()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var mover=InventoryMover(h,owner);var take=InventoryTake(h,owner);
        var no=await take.CreatePlanAsync(new(){TargetWarehouseCd="W01"},"forged");
        await mover.ApplyAsync(InventoryAdjustment());
        await take.StartCountAsync(no.ToLowerInvariant(),"forged");
        Assert.Equal(11.24691356m,(await take.GetAsync(no))!.Details[0].BookQty);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>mover.ApplyAsync(InventoryAdjustment()));
        foreach(var fake in new[]{true,false})
        {
            var req=InventoryAdjustment();if(fake)req.AllowNegativeOverride=true;else req.RelatedType="STOCKTAKE";
            Assert.Equal("INTERNAL_STOCK_CAPABILITY_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyAsync(req))).Message);
        }
        await take.UpdateCountsAsync(no,[new(){LineNo=1,CountedQty=9.12345678m,DiffReasonCd="COUNT"},new(){LineNo=2,CountedQty=20m}],"forged");
        await take.SubmitForReviewAsync(no,null);await take.ApproveAndApplyAsync(no,"forged");
        Assert.Equal(9.12345678m,await h.Db.Stocks.Where(s=>s.ProductCd=="P1").Select(s=>s.PhysicalQty).SingleAsync());
        Assert.Equal(StockTakeStatus.Completed,(await take.GetAsync(no))!.Status);
        Assert.All(await h.Db.StockTransactions.AsNoTracking().ToArrayAsync(),s=>Assert.Equal("price-owner",s.Creator));
        var pair=await mover.MoveAsync(new(){WarehouseCd="W01",FromLocationCd="L1",ToLocationCd="L3",ProductCd="P1",LotNo="LOT",Qty=.12345678m,OperatorCd="forged"});
        Assert.NotEqual(pair.InTxnNo,pair.OutTxnNo);Assert.Equal(9m,await h.Db.Stocks.Where(s=>s.LocationCd=="L1").Select(s=>s.PhysicalQty).SingleAsync());
        Assert.Equal(.12345678m,await h.Db.Stocks.Where(s=>s.LocationCd=="L3").Select(s=>s.PhysicalQty).SingleAsync());
    }
    private sealed class FailSecondAdjustment:SaveChangesInterceptor
    {
        internal bool Enabled;private int count;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e,InterceptionResult<int> result,CancellationToken ct=default)
        {
            if(Enabled && e.Context!.ChangeTracker.Entries<StockTransaction>().Any(x=>x.State==EntityState.Added) && ++count==2)
            {Enabled=false;throw new IOException("second stock write");}
            return ValueTask.FromResult(result);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task Stocktake_dashboard_second_adjustment_or_move_failure_rolls_back_entire_intent()
    {
        var failure=new FailSecondAdjustment();await using var h=await Seed(failure);var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var no=await CountInventory(h,owner);failure.Enabled=true;
        await Assert.ThrowsAnyAsync<Exception>(()=>InventoryTake(h,owner).ApproveAndApplyAsync(no,null));Assert.False(failure.Enabled);
        Assert.Equal(new[]{10.12345678m,20m},await h.Db.Stocks.OrderBy(s=>s.ProductCd).Select(s=>s.PhysicalQty).ToArrayAsync());
        Assert.Equal(0,await h.Db.StockTransactions.CountAsync());Assert.Equal(StockTakeStatus.AwaitingApproval,(await InventoryTake(h,owner).GetAsync(no))!.Status);
        await InventoryTake(h,owner).ApproveAndApplyAsync(no,null);Assert.Equal(2,await h.Db.StockTransactions.CountAsync());
        await using var other=await Seed(new FailSecondAdjustment{Enabled=true});var otherOwner=await GrantStocktakeDashboard(other);await SeedInventory(other);
        await Assert.ThrowsAnyAsync<Exception>(()=>InventoryMover(other,otherOwner).MoveAsync(new(){WarehouseCd="W01",FromLocationCd="L1",ToLocationCd="L3",ProductCd="P1",LotNo="LOT",Qty=1m}));
        Assert.Equal(0,await other.Db.StockTransactions.CountAsync());Assert.Equal(2,await other.Db.Stocks.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Stocktake_dashboard_self_soft_deleted_graph_and_whole_snapshot_are_enforced()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);var take=InventoryTake(h,owner);
        var no=await take.CreatePlanAsync(new(){TargetWarehouseCd="W01"},null);
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey!="order").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
        h.Db.StockTakeDetails.Add(new(){StockTakeNo=no,LineNo=99,WarehouseCd="W01",LocationCd="L1",ProductCd="P1",StockId=Guid.NewGuid(),Creator="other",IsDeleted=true});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();Assert.Empty(await take.SearchAsync(new()));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>take.StartCountAsync(no,null));
        await h.Db.Stocks.Where(s=>s.LocationCd=="L2").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Creator,"other"));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>take.CreatePlanAsync(new(){TargetWarehouseCd="W01"},null));
        Assert.Single((await new StockOperationsService(h.Db,owner).SearchAsync(new())).Items);
        var narrow=await take.CreatePlanAsync(new(){TargetWarehouseCd="W01",TargetLocationPrefix="L1"},null);Assert.Single((await take.GetAsync(narrow))!.Details);
    }
    [CurrentPermissionPostgresFact]
    public async Task Stocktake_dashboard_area_and_price_sources_restrict_rows_mutation_and_value_inference()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync();h.Db.WmsRoleScopes.Add(new(){RoleId=72,WarehouseCd="w01 ",AreaCd="a "});
        h.Db.Locations.AddRange(new(){WarehouseCd="W01",LocationCd="L1",AreaCd="A"},new(){WarehouseCd="W01",LocationCd="L2",AreaCd="B"});
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-stock",FieldName="UnitPrice",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var read=new StockOperationsService(h.Db,owner);var row=Assert.Single((await read.SearchAsync(new())).Items);Assert.Null(row.UnitPrice);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>InventoryMover(h,owner).ApplyAsync(InventoryAdjustment()));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>InventoryMover(h,owner).MoveAsync(new(){WarehouseCd="W01",FromLocationCd="L1",ToLocationCd="L2",ProductCd="P1",LotNo="LOT",Qty=1m}));
        var dash=new WmsDashboardService(h.Db,owner);await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>dash.GetKpiAsync());
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>dash.GetWarehouseValueAsync());Assert.Empty((await dash.GetAlertsAsync()).ExpiryAlerts);
        await h.Db.Sys_RoleFieldPerms.ExecuteDeleteAsync();Assert.Equal(30.37037034m,(await dash.GetKpiAsync()).TotalStockValue);
        Assert.Single(await dash.GetWarehouseValueAsync());
        await h.Db.Sys_RoleFieldPerms.AddAsync(new(){RoleId=72,ResourceKey="wms-dashboard",FieldName="Amount",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>dash.GetKpiAsync());Assert.Equal(3,(await dash.GetTransactionTrendAsync(3)).Count);
    }
}
