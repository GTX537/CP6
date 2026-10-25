using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Operations_area_grants_use_native_actual_locations_and_include_deleted_siblings()
    {
        await using var h=await Seed();await SeedCancelGraph(h);var owner=await GrantOperations(h);var wms=OperationsWms(h,owner);
        var number=await h.Db.OutboundOrders.Select(o=>o.OutboundNo).SingleAsync();
        var warehouse=await h.Db.OutboundOrders.Select(o=>o.WarehouseCd).SingleAsync();
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync();
        h.Db.WmsRoleScopes.Add(new(){RoleId=72,WarehouseCd=warehouse.ToLowerInvariant(),AreaCd="a "});
        h.Db.Locations.Add(new(){WarehouseCd=warehouse,LocationCd="L",AreaCd="A"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();Assert.NotNull(await wms.GetOrderAsync(number));
        h.Db.OutboundOrderDetails.Add(new(){OutboundNo=number,LineNo=3,ProductCd="HIDDEN",WarehouseCd=warehouse,LocationCd="OTHER",Creator="origin",IsDeleted=true});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("OBJECT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.CancelOrderAsync(number,null))).Message);
        Assert.Equal(0,await h.Db.StockTransactions.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Operations_partial_allocation_records_shortage_and_cancel_releases_only_actual_reservation()
    {
        await using var h=await Seed();var owner=await GrantOperations(h);var seq=new WmsSequenceService(h.Db);
        var wms=new OutboundService(h.Db,seq,new StockMovementService(h.Db,seq),shortage:new MaterialShortageService(h.Db),owner:owner);
        h.Db.Stocks.Add(new(){WarehouseCd="W01",LocationCd="L",ProductCd="P",LotNo="",PhysicalQty=10m,AvailableQty=10m});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var admin=await MappingAdmin(h);await BindStandalone(h,admin,"stock",await h.Db.Stocks.Select(s=>s.Id).SingleAsync());
        // The accepted shortage Owner requires an actual material work-order source.
        var draft=NewOutbound();draft.WorkOrderNo=await SeedProduction(h);draft.Details.Add(new(){ProductCd="MISSING",RequiredQty=3m});
        var no=await wms.CreateOrderAsync(draft,null);await wms.ConfirmOrderAsync(no,null);await wms.AllocateAsync(no,null);
        Assert.Equal(OutboundOrderStatus.PartialAllocated,await h.Db.OutboundOrders.Select(o=>o.Status).SingleAsync());
        Assert.Equal(1,await h.Db.MaterialShortages.CountAsync());
        await wms.CancelOrderAsync(no,null);await wms.CancelOrderAsync(no,null);
        Assert.Equal(2,await h.Db.StockTransactions.CountAsync());Assert.Equal(0m,await h.Db.Stocks.Select(s=>s.AllocatedQty).SingleAsync());
        Assert.Equal(10m,await h.Db.Stocks.Select(s=>s.PhysicalQty).SingleAsync());
        Assert.Equal(2,await InheritedMappings(h));
    }

    private sealed class OperationsNotification(Func<Task> notify):IWmsNotifier
    {
        public Task NotifyStockChangedAsync(StockChangedEvent evt)=>notify();
        public Task NotifyOutboundShippedAsync(string number,string? package)=>notify();
        public Task NotifyInboundReceivedAsync(string no,string wh)=>Task.CompletedTask;
        public Task NotifyStockTakeCompletedAsync(string no,int count)=>Task.CompletedTask;
    }
    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Operations_external_notifications_only_see_confirmed_commit_and_never_finally_denied_stock(bool deny)
    {
        var hook=new CommandHook();await using var h=await Seed(hook);await SeedCancelGraph(h);var owner=await GrantOperations(h);
        var number=await h.Db.OutboundOrders.Select(o=>o.OutboundNo).SingleAsync();var calls=0;var allCommitted=true;
        var notify=new OperationsNotification(async()=>
        {
            calls++;await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
            allCommitted &= h.Db.Database.CurrentTransaction==null && await observer.StockTransactions.CountAsync()==2;
            throw new IOException("delivery unavailable after known commit");
        });
        var seq=new WmsSequenceService(h.Db);var wms=new OutboundService(h.Db,seq,new StockMovementService(h.Db,seq,notify),notify,owner:owner);
        var reads=0;if(deny)hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject") && ++reads==2)throw new IOException("final authority failure");return Task.CompletedTask;};
        if(deny)await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.ShipAsync(number,new(),null));
        else await wms.ShipAsync(number,new(),null);
        hook.Before=null;Assert.Equal(deny?0:3,calls);Assert.True(allCommitted);Assert.Equal(deny?0:2,await h.Db.StockTransactions.CountAsync());
    }
}
