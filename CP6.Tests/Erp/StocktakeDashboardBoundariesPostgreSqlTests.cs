using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Mes;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Localization;
using Moq;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Stocktake_dashboard_freeze_serializes_raw_stock_writer_and_rejects_changed_snapshot()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var take=InventoryTake(h,owner);var no=await take.CreatePlanAsync(new(){TargetWarehouseCd="W01"},null);
        Task? writerTask=null;var observed=false;
        hook.After=async c=>
        {
            if(!c.CommandText.Contains("WITH field_subject"))return;hook.After=null;
            writerTask=Task.Run(async()=>
            {
                await using var writer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
                await writer.Database.ExecuteSqlInterpolatedAsync($"/* W39 stock race */ UPDATE public.\"T_Stock\" SET \"PhysicalQty\"=12 WHERE \"TenantId\"={h.Tenant} AND \"LocationCd\"='L1'");
            });
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database()
                    AND wait_event_type='Lock' AND query LIKE '/* W39 stock race */ UPDATE%') AS "Value"
                """).SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            observed=true;Assert.False(writerTask.IsCompleted);
        };
        try{await take.StartCountAsync(no,null);}finally{if(writerTask!=null)await writerTask;}
        Assert.True(observed);Assert.Equal(10.12345678m,(await take.GetAsync(no))!.Details[0].BookQty);
        await take.UpdateCountsAsync(no,[new(){LineNo=1,CountedQty=9m,DiffReasonCd="COUNT"},new(){LineNo=2,CountedQty=20m}],null);
        await take.SubmitForReviewAsync(no,null);
        Assert.Equal("STOCKTAKE_SNAPSHOT_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>take.ApproveAndApplyAsync(no,null))).Message);
        Assert.Equal(0,await h.Db.StockTransactions.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Stocktake_dashboard_WMS_alerts_history_and_totals_scope_before_aggregating()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey!="order").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
        await h.Db.Stocks.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ExpiryDate,DateTime.Today.AddDays(5).AddHours(12)));
        await h.Db.Stocks.Where(s=>s.LocationCd=="L2").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Creator,"other"));
        h.Db.InboundOrders.AddRange(new(){InboundNo="OWN",WarehouseCd="W01",ExpectedArrivalDate=DateTime.Today.AddDays(-3).AddHours(12),Status=1,Creator="price-owner"},
            new(){InboundNo="HIDDEN",WarehouseCd="W01",ExpectedArrivalDate=DateTime.Today.AddDays(-9),Status=1,Creator="other"});
        h.Db.StockTransactions.Add(new(){TxnNo="HIDDEN",TxnType="ADJ",WarehouseCd="W01",LocationCd="L1",ProductCd="P1",LotNo="LOT",Qty=999m,TxnDateTime=DateTime.Now,Creator="other"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();await InventoryMover(h,owner).ApplyAsync(InventoryAdjustment());
        var read=new StockOperationsService(h.Db,owner);var page=await read.SearchAsync(new(){PageSize=1});Assert.Equal(1,page.Total);
        Assert.Single((await read.HistoryAsync(page.Items[0].Id)).Transactions);Assert.Equal(1,(await read.TransactionsAsync(new())).Total);
        var dash=new WmsDashboardService(h.Db,owner);var alerts=await dash.GetAlertsAsync();
        Assert.Equal(5,Assert.Single(alerts.ExpiryAlerts).DaysUntilExpiry);Assert.Equal(3,Assert.Single(alerts.DelayedInbounds).DelayDays);
        Assert.Equal(1.12345678m,(await dash.GetTransactionTrendAsync(1)).Single().AdjQty);
        await using var other=await Seed();await GrantStocktakeDashboard(other);await SeedInventory(other);
        Assert.Equal(33.74074068m,(await dash.GetKpiAsync()).TotalStockValue);
    }
    [CurrentPermissionPostgresFact]
    public async Task Stocktake_dashboard_MES_queries_and_SP_use_scoped_graph_and_database_calendar()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);var no=await SeedProduction(h);
        await h.Db.WorkOrders.ExecuteUpdateAsync(s=>s.SetProperty(w=>w.Status,3).SetProperty(w=>w.PlanEndDate,DateTime.Today.AddDays(-2)));
        h.Db.ProductionResults.Add(new(){ResultNo="R1",WorkOrderNo=no,ProcessCd="P",OperatorCd="OP",GoodQty=3.12345678m,DefectQty=1m,MachineCd="M",Creator="price-owner",CreateDate=DateTime.Now});
        h.Db.DefectRecords.Add(new(){DefectNo="D1",WorkOrderNo=no,CategoryCd="C",DefectDescription="observed",DefectQty=1m,Creator="price-owner"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await using var other=await Seed();var otherNo=await SeedProduction(other);
        other.Db.ProductionResults.Add(new(){ResultNo="FOREIGN",WorkOrderNo=otherNo,ProcessCd="P",OperatorCd="OP",GoodQty=999m,Creator="price-owner",CreateDate=DateTime.Now});
        await other.Db.SaveChangesAsync();other.Db.ChangeTracker.Clear();
        var dash=new MesDashboardService(h.Db,owner);var sp=new MesDashboardDapperService(h.Db.Database.GetDbConnection(),dash);
        var result=await dash.GetSummaryAsync();Assert.Equal(1,result.InProgressCount);Assert.Equal(3.12345678m,result.TotalGoodQty);
        Assert.Single(await dash.GetProcessProgressAsync());Assert.Single(await dash.GetDelayAlertsAsync());Assert.Single(await dash.GetDefectTop5Async());Assert.Single(await dash.GetMachineHeatmapAsync());
        Assert.Equal(3.12345678m,(await dash.GetDailyTrendAsync(1)).Single().GoodQty);Assert.Empty(await dash.GetRecentCompletedAsync());
        Assert.Equal(result.TotalGoodQty,(await sp.GetSummaryAsync()).TotalGoodQty);Assert.Single(await sp.GetProcessProgressAsync());
        Assert.Equal(3.12345678m,(await sp.GetDailyTrendAsync(1)).Single().GoodQty);
        var today=await h.Db.Database.SqlQueryRaw<DateTime>("SELECT clock_timestamp()::date::timestamp AS \"Value\"").SingleAsync();
        Assert.Equal(today.AddDays(2).ToString("yyyy-MM-dd"),(await sp.GetDailyTrendAsync(-1)).Single().Date);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>sp.GetDailyTrendAsync(368));
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="mes-dashboard").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
        h.Db.ProductionResults.Add(new(){ResultNo="HIDDEN",WorkOrderNo=no,ProcessCd="P",OperatorCd="OP",Creator="other",IsDeleted=true});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal(0,(await sp.GetSummaryAsync()).TotalGoodQty);Assert.Empty(await dash.GetDefectTop5Async());
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==304 && a.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>dash.GetDailyTrendAsync())).Message);
    }
    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Stocktake_dashboard_final_authority_and_unknown_commit_preserve_whole_inventory(bool lostAck)
    {
        var hook=new CommandHook();var commit=new CommitFault();await using var h=await Seed(hook,commit);var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var no=await CountInventory(h,owner);var reads=0;
        if(lostAck)commit.Armed=true;
        else hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject") && ++reads==2)throw new IOException("final stocktake authority unavailable");return Task.CompletedTask;};
        var error=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>InventoryTake(h,owner).ApproveAndApplyAsync(no,null));hook.Before=null;
        Assert.Equal(lostAck?"OPERATIONS_OUTCOME_UNKNOWN":"AUTHORITY_FENCE_UNAVAILABLE",error.Message);
        Assert.Equal(lostAck?2:0,await h.Db.StockTransactions.CountAsync());
        Assert.Equal(lostAck?4:3,(await InventoryTake(h,owner).GetAsync(no))!.Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Stocktake_dashboard_current_actions_prefetched_graph_and_old_transactions_are_not_authority()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);var mover=InventoryMover(h,owner);
        await using(var transaction=await h.Db.Database.BeginTransactionAsync())
            await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyAsync(InventoryAdjustment()));
        await h.Db.Stocks.FirstAsync();await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyAsync(InventoryAdjustment()));h.Db.ChangeTracker.Clear();
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==403 && a.ActionCode=="adjust").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyAsync(InventoryAdjustment()))).Message);
        Assert.Equal(0,await h.Db.StockTransactions.CountAsync());
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="wms-stock").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,4));
        Assert.Empty((await new StockOperationsService(h.Db,owner).SearchAsync(new())).Items);
    }
    [CurrentPermissionPostgresFact]
    public async Task Stocktake_dashboard_actual_Program_login_CSRF_all_routes_source_fields_and_revoked_session()
    {
        await using var h=await Seed();await SeedInventory(h);await SeedProduction(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        using(var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/stock-take")))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        await GrantStocktakeDashboard(h);
        foreach(var path in new[]{"api/wms/stock","api/wms/stock/transactions","api/wms/stock-take","api/wms/dashboard/kpi","api/wms/dashboard/trend","api/wms/dashboard/warehouse-value","api/wms/dashboard/alerts",
            "api/mes/dashboard/summary","api/mes/dashboard/process-progress","api/mes/dashboard/delay-alerts","api/mes/dashboard/daily-trend","api/mes/dashboard/defect-top5","api/mes/dashboard/recent-completed","api/mes/dashboard/machine-heatmap",
            "api/mes/dashboard/summary/sp","api/mes/dashboard/daily-trend/sp","api/mes/dashboard/process-progress/sp"})
        {using var read=await host.Send(OperationsRequest(cookies,HttpMethod.Get,path));Assert.True(read.StatusCode==HttpStatusCode.OK,path+" "+await OperationsFailure(read));Assert.True(read.Headers.CacheControl?.NoStore);}
        using(var csrf=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/stock/apply",InventoryAdjustment(),csrf:false)))Assert.Equal(HttpStatusCode.Forbidden,csrf.StatusCode);
        using(var write=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/stock/apply",InventoryAdjustment())))Assert.True(write.StatusCode==HttpStatusCode.OK,await OperationsFailure(write));
        string no;using(var plan=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/stock-take/plan",new{targetWarehouseCd="W01"})))
        {Assert.True(plan.StatusCode==HttpStatusCode.OK,await OperationsFailure(plan));no=JsonDocument.Parse(await plan.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("stockTakeNo").GetString()!;}
        using(var count=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/stock-take/"+no+"/start-count")))Assert.True(count.StatusCode==HttpStatusCode.OK,await OperationsFailure(count));
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==415 && a.ActionCode=="cancel").ExecuteDeleteAsync();
        using(var forbidden=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/stock-take/"+no+"/cancel")))Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-stock",FieldName="UnitPrice",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using(var hidden=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/dashboard/kpi")))Assert.Equal(HttpStatusCode.Forbidden,hidden.StatusCode);
        await h.Db.Sys_RoleFieldPerms.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.FieldName,"unknown"));
        using(var unavailable=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/stock")))Assert.Equal(HttpStatusCode.ServiceUnavailable,unavailable.StatusCode);
        await h.Db.Sys_Users.Where(u=>u.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.AuthenticationEpoch,Guid.NewGuid()));
        using(var expired=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/mes/dashboard/summary/sp")))Assert.Equal(HttpStatusCode.Unauthorized,expired.StatusCode);
    }
    [CurrentPermissionPostgresFact]
    public async Task Stocktake_dashboard_PG_realtime_joins_only_current_tenant_and_never_pushes_business_payloads()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);
        var caller=new Mock<HubCallerContext>();caller.SetupGet(c=>c.ConnectionId).Returns("owned-connection");caller.SetupGet(c=>c.User).Returns(h.Http.User);
        var groups=new Mock<IGroupManager>();var hub=new CP6.WebApi.Hubs.WmsHub(NullLogger<CP6.WebApi.Hubs.WmsHub>.Instance,h.Db,new(h.Db,owner)){Context=caller.Object,Groups=groups.Object};
        await hub.OnConnectedAsync();await hub.SubscribeWarehouse("FOREIGN");
        groups.Verify(g=>g.AddToGroupAsync("owned-connection",CP6.WebApi.Hubs.WmsHub.InventoryGroup(h.Tenant),It.IsAny<CancellationToken>()),Times.Once);
        groups.VerifyNoOtherCalls();
        var destination=new Mock<IClientProxy>();var clients=new Mock<IHubClients>(MockBehavior.Strict);
        clients.Setup(c=>c.Group(CP6.WebApi.Hubs.WmsHub.InventoryGroup(h.Tenant))).Returns(destination.Object);
        var context=new Mock<IHubContext<CP6.WebApi.Hubs.WmsHub>>();context.SetupGet(c=>c.Clients).Returns(clients.Object);
        var notify=new CP6.WebApi.Services.SignalRWmsNotifier(context.Object,Mock.Of<CP6.Core.Utilities.INotificationPublisher>(),NullLogger<CP6.WebApi.Services.SignalRWmsNotifier>.Instance,Mock.Of<IStringLocalizer>(),h.Db);
        await notify.NotifyStockChangedAsync(new(){WarehouseCd="PRIVATE",ProductCd="PRIVATE",Qty=999});
        await notify.NotifyInboundReceivedAsync("PRIVATE","PRIVATE");await notify.NotifyOutboundShippedAsync("PRIVATE","PRIVATE");await notify.NotifyStockTakeCompletedAsync("PRIVATE",99);
        destination.Verify(d=>d.SendCoreAsync("InventoryInvalidatedV1",It.Is<object[]>(a=>a.Length==0),It.IsAny<CancellationToken>()),Times.Exactly(4));destination.VerifyNoOtherCalls();
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==403 && a.ActionCode=="query").ExecuteDeleteAsync();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>hub.SubscribeProduct("P"));
    }
}
