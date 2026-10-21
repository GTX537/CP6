using CP6.Core.Services.Erp;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Mes;
using Microsoft.AspNetCore.Http;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed class OperationsHttp : IHttpContextAccessor { public HttpContext? HttpContext {get;set;} }
    private static async Task<OperationsOwnerV1> GrantOperations(Harness h)
    {
        foreach(var (key,id,actions) in new[]{("mes-work-order",302,new[]{"query","add","edit","del","issue","cancel"}),
            ("wms-outbound-order",408,new[]{"query","add","edit","del","confirm","cancel","allocate","pick","ship"})})
        {
            var menu=await h.Db.Sys_Menus.SingleOrDefaultAsync(m=>m.MenuKey==key);
            if(menu==null){menu=new(){MenuId=id,MenuKey=key,MenuName=key};h.Db.Sys_Menus.Add(menu);}
            foreach(var action in actions)
            {
                if(!await h.Db.Sys_MenuActions.AnyAsync(a=>a.MenuId==menu.MenuId && a.ActionCode==action))
                    h.Db.Sys_MenuActions.Add(new(){MenuId=menu.MenuId,ActionCode=action,ActionName=action});
                h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=menu.MenuId,ActionCode=action});
            }
            h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey=key,ScopeType=5});
        }
        h.Db.WmsRoleScopes.Add(new(){RoleId=72,WarehouseCd="*",AreaCd="*"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var catalog=new AuthorizationCatalogStoreV1();
        catalog.RegisterFields("order",[new("UnitPrice","Price"),new("Amount","Amount")]);catalog.RegisterScope("order","Order",[1,2,3,4,5],2);
        catalog.RegisterFields("mes-work-order",[]);catalog.RegisterScope("mes-work-order","Manufacturing",[1,2,3,4,5],1);
        catalog.RegisterFields("wms-outbound-order",[new("UnitPrice","Price")]);catalog.RegisterScope("wms-outbound-order","Outbound",[1,2,3,4,5],1);catalog.Seal();
        return new(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),catalog.Capture);
    }
    [CurrentPermissionPostgresFact]
    public async Task Independent_operations_reject_missing_service_Owner_before_read_or_write()
    {
        await using var h=await Seed();await SeedCancelGraph(h);
        var wo=await h.Db.WorkOrders.Select(x=>x.WorkOrderNo).SingleAsync();
        var outbound=await h.Db.OutboundOrders.Select(x=>x.OutboundNo).SingleAsync();
        var mes=new WorkOrderService(h.Db,new MesSequenceService(h.Db),new NoOpWmsBridgeHook());
        var wms=new OutboundService(h.Db,new WmsSequenceService(h.Db),new StockMovementService(h.Db,new WmsSequenceService(h.Db)));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mes.GetByNoAsync(wo));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.CancelOrderAsync(outbound,"forged"));
        Assert.All(await h.Db.Stocks.AsNoTracking().ToArrayAsync(),x=>Assert.Equal(2m,x.AllocatedQty));
    }

    [CurrentPermissionPostgresFact]
    public async Task Independent_outbound_failure_does_not_leave_first_line_stock_release_committed()
    {
        var failure=new FailSecondCancelRelease();await using var h=await Seed(failure);await SeedCancelGraph(h);
        var owner=await GrantOperations(h);
        var outbound=await h.Db.OutboundOrders.Select(x=>x.OutboundNo).SingleAsync();
        var wms=new OutboundService(h.Db,new WmsSequenceService(h.Db),new StockMovementService(h.Db,new WmsSequenceService(h.Db)),owner:owner);
        failure.Enabled=true;
        await Assert.ThrowsAnyAsync<Exception>(()=>wms.CancelOrderAsync(outbound,"forged"));
        Assert.False(failure.Enabled); // Proves the second stock write was reached, rather than an earlier authorization refusal.
        h.Db.ChangeTracker.Clear();
        Assert.All(await h.Db.Stocks.AsNoTracking().ToArrayAsync(),x=>Assert.Equal(2m,x.AllocatedQty));
        Assert.Equal(0,await h.Db.StockTransactions.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Independent_operations_current_owner_allows_real_issue_and_whole_cancel_with_bound_actor()
    {
        await using var h=await Seed();await SeedCancelGraph(h);var owner=await GrantOperations(h);
        var wo=await h.Db.WorkOrders.Select(x=>x.WorkOrderNo).SingleAsync();
        var outbound=await h.Db.OutboundOrders.Select(x=>x.OutboundNo).SingleAsync();
        h.Db.WorkOrderProcesses.Add(new(){WorkOrderNo=wo,ProcessCd="P",TaskCd="T",Creator="origin"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var mes=new WorkOrderService(h.Db,new MesSequenceService(h.Db),new NoOpWmsBridgeHook(),owner);
        await mes.IssueAsync(wo,"forged");
        Assert.Equal(WorkOrderStatus.Issued,await h.Db.WorkOrders.Select(w=>w.Status).SingleAsync());
        var wms=new OutboundService(h.Db,new WmsSequenceService(h.Db),new StockMovementService(h.Db,new WmsSequenceService(h.Db)),owner:owner);
        await wms.CancelOrderAsync(outbound,"forged");
        Assert.All(await h.Db.Stocks.AsNoTracking().ToArrayAsync(),s=>Assert.Equal(0m,s.AllocatedQty));
        Assert.All(await h.Db.StockTransactions.AsNoTracking().ToArrayAsync(),s=>Assert.Equal("price-owner",s.OperatorCd));
        Assert.Equal("origin",await h.Db.WorkOrders.Select(w=>w.Creator).SingleAsync());
    }
}
