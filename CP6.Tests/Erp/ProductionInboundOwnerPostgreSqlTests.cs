using CP6.Core.Services.Erp;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Mes;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<OperationsOwnerV1> GrantProductionInbound(Harness h)
    {
        var catalog=new AuthorizationCatalogStoreV1();
        foreach(var (key,id,actions) in new[]{("mes-production-result",304,new[]{"query","start","suspend","complete","report","recalculate","backflush"}),
            ("wms-inbound-order",405,new[]{"query","add","edit","del","confirm","cancel"}),
            ("wms-inbound-receipt",406,new[]{"query","post"}), ("mes-work-order",302,new[]{"query"})})
        {
            if(!await h.Db.Sys_Menus.AnyAsync(m=>m.MenuId==id))h.Db.Sys_Menus.Add(new(){MenuId=id,MenuKey=key,MenuName=key});
            foreach(var action in actions)
            {
                if(!await h.Db.Sys_MenuActions.AnyAsync(a=>a.MenuId==id && a.ActionCode==action))h.Db.Sys_MenuActions.Add(new(){MenuId=id,ActionCode=action,ActionName=action});
                h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=id,ActionCode=action});
            }
            h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey=key,ScopeType=5});
            catalog.RegisterScope(key,key,[1,2,3,4,5],1);
            catalog.RegisterFields(key,key.StartsWith("wms-")?[new("UnitPrice","Price")]:[]);
        }
        h.Db.WmsRoleScopes.Add(new(){RoleId=72,WarehouseCd="*",AreaCd="*"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();catalog.Seal();
        return new(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),catalog.Capture);
    }
    [CurrentPermissionPostgresFact]
    public async Task Production_inbound_reject_missing_Owner_before_disclosing_rows()
    {
        await using var h=await Seed();
        var mes=new ProductionResultService(h.Db);
        var seq=new WmsSequenceService(h.Db);
        var inbound=new InboundService(h.Db,seq,new StockMovementService(h.Db,seq));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mes.SearchAsync(new()));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>inbound.SearchOrdersAsync(new()));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>inbound.SearchReceiptsAsync(new()));
    }
    private sealed class FailSecondInbound : SaveChangesInterceptor
    {
        internal bool Enabled;
        private int count;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct=default)
        {
            if(Enabled && data.Context!.ChangeTracker.Entries<StockTransaction>().Any(e=>e.State==EntityState.Added && e.Entity.TxnType==WmsTxnType.IN) && ++count==2)
            {Enabled=false;throw new IOException("second receipt stock write");}
            return ValueTask.FromResult(result);
        }
    }
    private static InboundReceiptDto ReceiptRequest()=>new(){WarehouseCd="W01",SourceType=InboundSourceType.Purchase,
        Details=[new(){ProductCd="P1",LocationCd="L",ReceivedQty=2m},new(){ProductCd="P2",LocationCd="L",ReceivedQty=3m}]};
    [CurrentPermissionPostgresFact]
    public async Task Production_inbound_second_stock_failure_rolls_back_entire_receipt()
    {
        var failure=new FailSecondInbound();await using var h=await Seed(failure);
        var owner=await GrantProductionInbound(h);
        var seq=new WmsSequenceService(h.Db);var inbound=new InboundService(h.Db,seq,new StockMovementService(h.Db,seq),owner:owner);
        failure.Enabled=true;
        await Assert.ThrowsAnyAsync<Exception>(()=>inbound.ConfirmReceiptAsync(ReceiptRequest(),"forged"));
        Assert.False(failure.Enabled);
        h.Db.ChangeTracker.Clear();
        Assert.Equal(0,await h.Db.StockTransactions.CountAsync());
        Assert.Equal(0,await h.Db.Stocks.CountAsync());
        Assert.Equal(0,await h.Db.InboundReceipts.CountAsync());
        Assert.Equal(0,await h.Db.InboundReceiptDetails.CountAsync());
    }
}
