using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Production_inbound_actual_Program_login_owner_csrf_native_receipt_and_report_contracts()
    {
        await using var h=await Seed();var wo=await SeedProduction(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        foreach(var path in new[]{"api/mes/production-results","api/wms/inbound-order","api/wms/inbound-receipt"})
        {using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,path));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);}
        await GrantProductionInbound(h);
        using(var read=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/mes/production-results/work-order/"+wo)))
        {Assert.True(read.StatusCode==HttpStatusCode.OK,await OperationsFailure(read));Assert.True(read.Headers.CacheControl?.NoStore);}
        using(var csrf=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/inbound-receipt",ReceiptRequest(),csrf:false)))
            Assert.Equal(HttpStatusCode.Forbidden,csrf.StatusCode);
        string no;
        using(var create=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/inbound-order",Plan())))
        {Assert.True(create.StatusCode==HttpStatusCode.OK,await OperationsFailure(create));no=JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("inboundNo").GetString()!;}
        using(var confirm=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/inbound-order/"+no+"/confirm")))
            Assert.True(confirm.StatusCode==HttpStatusCode.OK,await OperationsFailure(confirm));
        var req=ReceiptRequest();req.InboundNo=no;req.Details.RemoveAt(1);req.Details[0].RefOrderLineNo=1;
        using(var receive=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/inbound-receipt",req)))
            Assert.True(receive.StatusCode==HttpStatusCode.OK,await OperationsFailure(receive));
        using(var start=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/mes/production-results/start",new{workOrderNo=wo,processCd="P",operatorCd="OP"})))
            Assert.True(start.StatusCode==HttpStatusCode.OK,await OperationsFailure(start));
        using(var report=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/mes/production-results",new{workOrderNo=wo,processCd="P",operatorCd="OP",goodQty=0.12345678m})))
            Assert.True(report.StatusCode==HttpStatusCode.OK,await OperationsFailure(report));
        Assert.Equal(2,await h.Db.ProductionResults.CountAsync());Assert.Equal(1,await h.Db.StockTransactions.CountAsync());
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-inbound-receipt",FieldName="UnitPrice",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using(var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/inbound-receipt",ReceiptRequest())))
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        await h.Db.Sys_RoleFieldPerms.ExecuteUpdateAsync(s=>s.SetProperty(f=>f.FieldName,"unknown"));
        using(var unavailable=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/inbound-receipt")))
            Assert.Equal(HttpStatusCode.ServiceUnavailable,unavailable.StatusCode);
        await h.Db.Sys_Users.Where(u=>u.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.AuthenticationEpoch,Guid.NewGuid()));
        using(var expired=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/mes/production-results")))
            Assert.Equal(HttpStatusCode.Unauthorized,expired.StatusCode);
    }

    private sealed class FailMaterialUsage:SaveChangesInterceptor
    {
        internal bool Enabled;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct=default)
        {
            if(Enabled && data.Context!.ChangeTracker.Entries<WorkOrderMaterial>().Any(e=>e.Entity.MaterialCd=="BAD" && e.State is EntityState.Added or EntityState.Modified))
            {Enabled=false;throw new InvalidOperationException("usage write failed after stock");}
            return ValueTask.FromResult(result);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task Production_inbound_backflush_material_stock_usage_are_atomic_and_retry_keeps_other_successes()
    {
        var failure=new FailMaterialUsage();await using var h=await Seed(failure);var owner=await GrantProductionInbound(h);var no=await SeedProduction(h);
        var product="F"+h.Tenant.ToString("N")[..12];
        await h.Db.WorkOrders.ExecuteUpdateAsync(s=>s.SetProperty(w=>w.Status,WorkOrderStatus.Completed).SetProperty(w=>w.CompletedQty,2m).SetProperty(w=>w.ProductCd,product));
        h.Db.Set<ProductMaster>().Add(new(){ProductCd=product});
        foreach(var material in new[]{"GOOD","BAD"})h.Db.Set<ProductMaterial>().Add(new(){ProductCd=product,ProcessCd="P",MaterialCd=material,UsageType=2,UnitUsage=3m,MaterialTypeDiv="3"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var seq=new WmsSequenceService(h.Db);var backflush=new BackflushService(h.Db,new StockMovementService(h.Db,seq),new MaterialUsageCalculator(),owner:owner);
        failure.Enabled=true;Assert.Equal("BACKFLUSH_PARTIAL_FAILURE",(await Assert.ThrowsAsync<InvalidOperationException>(()=>backflush.BackflushAsync(no,null))).Message);
        Assert.False(failure.Enabled);Assert.Equal(1,await h.Db.StockTransactions.CountAsync());Assert.Equal("GOOD",await h.Db.WorkOrderMaterials.Select(m=>m.MaterialCd).SingleAsync());
        Assert.Equal(-6m,await h.Db.Stocks.Select(s=>s.PhysicalQty).SingleAsync());
        await backflush.BackflushAsync(no,null);await backflush.BackflushAsync(no,null);
        Assert.Equal(2,await h.Db.StockTransactions.CountAsync());Assert.All(await h.Db.WorkOrderMaterials.ToArrayAsync(),m=>Assert.Equal(6m,m.ActualQty));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Production_inbound_raw_role_revocation_serializes_receipt_and_denies_next_intent(bool before)
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantProductionInbound(h);Task? revoker=null;var observed=false;
        async Task Revoke()
        {await using var writer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});await writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Roles\" SET \"Enable\"=false WHERE \"TenantId\"={h.Tenant} AND \"RoleId\"=72");}
        if(before)hook.Before=async c=>{if(!c.CommandText.Contains("cp6_authentication_v1.acquire()"))return;hook.Before=null;await Revoke();observed=true;};
        else hook.After=async c=>
        {
            if(!c.CommandText.Contains("WITH field_subject"))return;hook.After=null;revoker=Task.Run(Revoke);
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database()
                  AND wait_event_type='Lock' AND query LIKE 'UPDATE public."Sys_Roles"%') AS "Value"
                """).SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            observed=true;Assert.False(revoker.IsCompleted);
        };
        try{if(before)await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Inbound(h,owner).ConfirmReceiptAsync(ReceiptRequest(),null));else await Inbound(h,owner).ConfirmReceiptAsync(ReceiptRequest(),null);}
        finally{if(revoker!=null)await revoker;}
        Assert.True(observed);Assert.Equal(before?0:2,await h.Db.StockTransactions.CountAsync());
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Inbound(h,owner).SearchReceiptsAsync(new()));
    }

    [CurrentPermissionPostgresFact]
    public async Task Production_inbound_dimensional_backflush_resolves_product_and_flute_using_native_keys()
    {
        await using var h=await Seed();var owner=await GrantProductionInbound(h);var no=await SeedProduction(h);
        var product="F"+h.Tenant.ToString("N")[..12];var variant=product.ToLowerInvariant()+" ";
        await h.Db.WorkOrders.ExecuteUpdateAsync(s=>s.SetProperty(w=>w.Status,WorkOrderStatus.Inspected).SetProperty(w=>w.CompletedQty,2m).SetProperty(w=>w.ProductCd,variant));
        h.Db.Set<ProductMaster>().Add(new(){ProductCd=product,SheetDimW=1000m,SheetDimF=1000m,SheetFlute="a "});
        h.Db.Set<CP6.Entity.DomainModels.Common.MasterGenericCode>().Add(new(){GroupCode="M067",Code="A",Num1=1.5m});
        h.Db.Set<ProductMaterial>().Add(new(){ProductCd=product,ProcessCd="P",MaterialCd="DIM",UsageType=1,UnitUsage=1m,MaterialTypeDiv="4"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var seq=new WmsSequenceService(h.Db);
        await new BackflushService(h.Db,new StockMovementService(h.Db,seq),new MaterialUsageCalculator(),owner:owner).BackflushAsync(no,null);
        Assert.Equal(-3m,await h.Db.Stocks.Select(s=>s.PhysicalQty).SingleAsync());Assert.Equal(3m,await h.Db.WorkOrderMaterials.Select(m=>m.ActualQty).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Production_inbound_purchase_adapter_maps_noncontiguous_PO_lines_to_its_actual_plan()
    {
        await using var h=await Seed();var owner=await GrantProductionInbound(h);
        var adapter=new CP6.Core.Services.Pur.Contracts.WmsReceiveServiceAdapter(Inbound(h,owner));
        var result=await adapter.ReceiveAsync(new(){PoNo="PO",WarehouseCd="W01",SupplierId="S",Lines=[new(){PoLineNo=7,ItemId="P7",Qty=2m},new(){PoLineNo=19,ItemId="P19",Qty=3m}]},"forged");
        Assert.Equal(new[]{7,19},result.LineRefs.Select(l=>l.PoLineNo));
        Assert.Equal(InboundOrderStatus.Completed,await h.Db.InboundOrders.Select(o=>o.Status).SingleAsync());
        Assert.Equal(new[]{2m,3m},await h.Db.InboundOrderDetails.OrderBy(d=>d.LineNo).Select(d=>d.ReceivedQty).ToArrayAsync());
        Assert.Equal(2,await h.Db.StockTransactions.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Production_inbound_unplanned_lines_actual_lots_and_unused_hidden_source_price_preserve_legal_receipts()
    {
        await using var h=await Seed();var owner=await GrantProductionInbound(h);var wms=Inbound(h,owner);var plan=Plan();plan.Details[0].LotNo="EXPECTED";
        var no=await wms.CreateOrderAsync(plan,null);await wms.ConfirmOrderAsync(no,null);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-inbound-order",FieldName="UnitPrice",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var receipt=ReceiptRequest();receipt.InboundNo=no;receipt.Details[0].RefOrderLineNo=1;receipt.Details[0].LotNo="ACTUAL";
        await wms.ConfirmReceiptAsync(receipt,null);
        Assert.Equal(2m,await h.Db.InboundOrderDetails.Select(d=>d.ReceivedQty).SingleAsync());
        Assert.Equal(2,await h.Db.StockTransactions.CountAsync());Assert.Null(await h.Db.StockTransactions.Where(t=>t.ProductCd=="P1").Select(t=>t.UnitPrice).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Production_inbound_department_uses_real_order_binding_and_rejects_unmapped_plan()
    {
        await using var h=await Seed();var owner=await GrantProductionInbound(h);var wo=await SeedProduction(h);var wms=Inbound(h,owner);var no=await wms.CreateOrderAsync(Plan(),null);
        var linked=ReceiptRequest();linked.WorkOrderNo=wo;await wms.ConfirmReceiptAsync(linked,null);await wms.ConfirmReceiptAsync(ReceiptRequest(),null);
        var mapping=await MappingAdmin(h);await SaveMapping(h,mapping,await MappingCommand(h,mapping));
        await h.Db.Orders.ExecuteUpdateAsync(s=>s.SetProperty(o=>o.OrderDepartment,"sales "));
        await h.Db.Sys_Users.Where(u=>u.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.DeptId,mapping.Root));
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey!="order").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,3));
        Assert.Single(await wms.SearchReceiptsAsync(new()));Assert.Empty(await wms.SearchOrdersAsync(new()));Assert.NotNull(await Production(h,owner).GetWorkOrderSummaryAsync(wo));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.GetOrderAsync(no));
        await h.Db.WorkOrders.ExecuteUpdateAsync(s=>s.SetProperty(w=>w.WebOrderNo,(string?)null));
        Assert.Empty(await wms.SearchReceiptsAsync(new()));await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Production(h,owner).GetWorkOrderSummaryAsync(wo));
    }
}
