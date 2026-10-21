using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Mes;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static InboundService Inbound(Harness h,OperationsOwnerV1 owner,IWmsNotifier? notifier=null)
    {var seq=new WmsSequenceService(h.Db);return new(h.Db,seq,new StockMovementService(h.Db,seq,notifier),notifier,owner);}
    private static ProductionResultService Production(Harness h,OperationsOwnerV1 owner,IWmsBridgeHook? bridge=null,IBackflushService? backflush=null)
        =>new(h.Db,new MesSequenceService(h.Db),OperationsMes(h,owner),new NoOpMesNotifier(),bridge??new NoOpWmsBridgeHook(),backflush,owner:owner);
    private static async Task<string> SeedProduction(Harness h)
    {
        var no="PR"+h.Tenant.ToString("N")[..16];
        h.Db.WorkOrders.Add(new(){WorkOrderNo=no,ProductCd="FG",WebOrderNo=h.Number,ProductionQty=5m,Status=WorkOrderStatus.Issued,Creator="price-owner"});
        h.Db.WorkOrderProcesses.Add(new(){WorkOrderNo=no,ProcessCd="P",TaskCd="T",Creator="price-owner"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();return no;
    }
    private static InboundOrderDto Plan()=>new(){WarehouseCd="W01",ExpectedArrivalDate=DateTime.Today,
        Details=[new(){ProductCd="P1",ExpectedQty=5.12345678m,UnitPrice=1234567890123.1234m,ExpectedLocationCd="L"}]};
    [CurrentPermissionPostgresFact]
    public async Task Production_inbound_real_actions_preserve_native_keys_actor_precision_and_plan_totals()
    {
        await using var h=await Seed();var owner=await GrantProductionInbound(h);var wms=Inbound(h,owner);
        var no=await wms.CreateOrderAsync(Plan(),"forged");var id=await h.Db.InboundOrderDetails.Select(d=>d.Id).SingleAsync();
        await wms.UpdateOrderAsync(no.ToLowerInvariant(),Plan(),"forged");
        Assert.Equal(id,await h.Db.InboundOrderDetails.Select(d=>d.Id).SingleAsync());
        Assert.Equal("price-owner",await h.Db.InboundOrderDetails.Select(d=>d.Creator).SingleAsync());
        await wms.ConfirmOrderAsync(no,null);
        var receipt=ReceiptRequest();receipt.InboundNo=no;receipt.Details.RemoveAt(1);receipt.Details[0].RefOrderLineNo=1;
        receipt.Details[0].ReceivedQty=1.12345678m;receipt.Details[0].UnitPrice=1234567890123.1234m;
        var rn=await wms.ConfirmReceiptAsync(receipt,"forged");
        Assert.Equal(1.12345678m,await h.Db.Stocks.Select(s=>s.PhysicalQty).SingleAsync());
        Assert.Equal(1.12345678m,await h.Db.InboundOrderDetails.Select(s=>s.ReceivedQty).SingleAsync());
        Assert.Equal(InboundOrderStatus.PartialReceived,await h.Db.InboundOrders.Select(s=>s.Status).SingleAsync());
        Assert.Equal(1234567890123.1234m,(await wms.GetReceiptAsync(rn))!.Details.Single().UnitPrice);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.UpdateOrderAsync(no,Plan(),null));
        await wms.CancelOrderAsync(no,null);Assert.Equal(1.12345678m,await h.Db.Stocks.Select(s=>s.PhysicalQty).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Production_inbound_self_includes_deleted_graph_and_current_actions()
    {
        await using var h=await Seed();var owner=await GrantProductionInbound(h);var wo=await SeedProduction(h);var mes=Production(h,owner);var wms=Inbound(h,owner);
        var no=await wms.CreateOrderAsync(Plan(),null);
        await h.Db.Sys_RoleDataScopes.ExecuteUpdateAsync(s=>s.SetProperty(d=>d.ScopeType,1));
        h.Db.InboundOrderDetails.Add(new(){InboundNo=no,LineNo=9,ProductCd="HIDDEN",Creator="other",IsDeleted=true});
        h.Db.ProductionResults.Add(new(){ResultNo="OLD",WorkOrderNo=wo,ProcessCd="P",OperatorCd="old",Creator="other",IsDeleted=true});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Empty(await wms.SearchOrdersAsync(new()));Assert.Empty((await mes.SearchAsync(new())).Items);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mes.StartAsync(new(){WorkOrderNo=wo,ProcessCd="P",OperatorCd="operator"},null));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.GetOrderAsync(no));
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==405 && a.ActionCode=="add").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.CreateOrderAsync(Plan(),null))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Production_inbound_warehouse_area_field_source_and_foreign_tenant_fail_closed()
    {
        await using var h=await Seed();var owner=await GrantProductionInbound(h);var wms=Inbound(h,owner);
        var no=await wms.CreateOrderAsync(Plan(),null);
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync();h.Db.WmsRoleScopes.Add(new(){RoleId=72,WarehouseCd="w01 ",AreaCd="a "});
        h.Db.Locations.Add(new(){WarehouseCd="W01",LocationCd="L",AreaCd="A"});
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-inbound-order",FieldName="UnitPrice",Access=3});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Null((await wms.GetOrderAsync(no))!.Details.Single().UnitPrice);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.UpdateOrderAsync(no,Plan(),null));
        var bad=ReceiptRequest();bad.Details[1].LocationCd="OUTSIDE";
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.ConfirmReceiptAsync(bad,null));Assert.Equal(0,await h.Db.StockTransactions.CountAsync());
        await using var other=await Seed();var otherOwner=await GrantProductionInbound(other);await Inbound(other,otherOwner).CreateOrderAsync(Plan(),null);
        var foreign=await Inbound(other,otherOwner).CreateOrderAsync(Plan(),null);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.GetOrderAsync(foreign));
        await h.Db.Sys_RoleFieldPerms.ExecuteUpdateAsync(s=>s.SetProperty(f=>f.FieldName,"unknown"));
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.SearchOrdersAsync(new()))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Production_inbound_start_suspend_resume_report_complete_have_current_state_and_quantity()
    {
        await using var h=await Seed();var owner=await GrantProductionInbound(h);var no=await SeedProduction(h);var mes=Production(h,owner);
        var req=new ProductionResultRequest{WorkOrderNo=no.ToLowerInvariant(),ProcessCd="p ",OperatorCd="worker",SuspendReasonCd="break"};
        await mes.StartAsync(req,"forged");await mes.SuspendAsync(req,null);await mes.ResumeAsync(req,null);
        req.GoodQty=0.12345678m;await mes.ReportAsync(req,null);req.GoodQty=1m;await mes.CompleteAsync(req,null);
        Assert.Equal(1.12345678m,await h.Db.WorkOrders.Select(w=>w.CompletedQty).SingleAsync());
        Assert.Equal(WorkOrderStatus.Completed,await h.Db.WorkOrders.Select(w=>w.Status).SingleAsync());
        Assert.Equal(5,(await mes.SearchAsync(new())).Total);
        Assert.All(await h.Db.ProductionResults.ToArrayAsync(),r=>Assert.Equal("price-owner",r.Creator));h.Db.ChangeTracker.Clear();
        await mes.ReportAsync(req,null); // Existing ME040 supplemental reporting remains legal after completion.
        Assert.Equal(2.12345678m,await h.Db.WorkOrders.Select(w=>w.CompletedQty).SingleAsync());
        await h.Db.WorkOrders.ExecuteUpdateAsync(s=>s.SetProperty(w=>w.Status,9));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mes.ReportAsync(req,null));
        Assert.Equal(6,await h.Db.ProductionResults.CountAsync());
    }
    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Production_inbound_final_authority_failure_and_lost_commit_ack_never_replay(bool lostAck)
    {
        var hook=new CommandHook();var commit=new CommitFault();await using var h=await Seed(hook,commit);var owner=await GrantProductionInbound(h);var wms=Inbound(h,owner);
        var reads=0;if(lostAck)commit.Armed=true;
        else hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject") && ++reads==2)throw new IOException("final source unavailable");return Task.CompletedTask;};
        var error=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.ConfirmReceiptAsync(ReceiptRequest(),null));
        hook.Before=null;h.Db.ChangeTracker.Clear();
        Assert.Equal(lostAck?"OPERATIONS_OUTCOME_UNKNOWN":"AUTHORITY_FENCE_UNAVAILABLE",error.Message);
        Assert.Equal(lostAck?2:0,await h.Db.StockTransactions.CountAsync());Assert.Equal(lostAck?1:0,await h.Db.InboundReceipts.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Production_inbound_completion_delegates_only_bound_real_effects_without_independent_grants()
    {
        await using var h=await Seed();var owner=await GrantProductionInbound(h);var no=await SeedProduction(h);var wms=Inbound(h,owner);
        var product="F"+h.Tenant.ToString("N")[..12];await h.Db.WorkOrders.ExecuteUpdateAsync(s=>s.SetProperty(w=>w.ProductCd,product));
        h.Db.Set<ProductMaster>().Add(new(){ProductCd=product});
        h.Db.Set<ProductMaterial>().Add(new(){ProductCd=product,ProcessCd="P",MaterialCd="RAW",UsageType=2,UnitUsage=2m,MaterialTypeDiv="3"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==405 || a.MenuId==406 || a.MenuId==304 && a.ActionCode=="backflush").ExecuteDeleteAsync();await h.Db.WmsRoleScopes.ExecuteDeleteAsync();
        var seq=new WmsSequenceService(h.Db);var backflush=new BackflushService(h.Db,new StockMovementService(h.Db,seq),new MaterialUsageCalculator(),owner:owner);
        var bridge=new WmsBridgeHook(h.Db,null!,wms,NullLogger<WmsBridgeHook>.Instance);var mes=Production(h,owner,bridge,backflush);
        var req=new ProductionResultRequest{WorkOrderNo=no,ProcessCd="P",OperatorCd="worker"};await mes.StartAsync(req,null);req.GoodQty=3m;await mes.CompleteAsync(req,null);
        Assert.Equal(1,await h.Db.InboundReceipts.CountAsync());Assert.Equal(3m,await h.Db.Stocks.Where(s=>s.ProductCd==product).Select(s=>s.PhysicalQty).SingleAsync());
        Assert.Equal(-6m,await h.Db.Stocks.Where(s=>s.ProductCd=="RAW").Select(s=>s.PhysicalQty).SingleAsync());
        Assert.Equal(6m,await h.Db.WorkOrderMaterials.Select(m=>m.ActualQty).SingleAsync());
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.CreateFinishedGoodsFromWorkOrderAsync(no,3m,null));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>backflush.BackflushAsync(no,null));Assert.Equal(2,await h.Db.StockTransactions.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Production_inbound_final_report_failure_rolls_back_quantity_result_and_derived_hours()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantProductionInbound(h);var no=await SeedProduction(h);var mes=Production(h,owner);
        await mes.StartAsync(new(){WorkOrderNo=no,ProcessCd="P",OperatorCd="OP"},null);var reads=0;
        hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject") && ++reads==2)throw new IOException("final report authority unavailable");return Task.CompletedTask;};
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mes.ReportAsync(new(){WorkOrderNo=no,ProcessCd="P",OperatorCd="OP",GoodQty=1m,ActualStartTime=DateTime.Today,ActualEndTime=DateTime.Today.AddHours(2)},null));
        hook.Before=null;h.Db.ChangeTracker.Clear();Assert.Equal(1,await h.Db.ProductionResults.CountAsync());Assert.Equal(0m,await h.Db.WorkOrders.Select(w=>w.CompletedQty).SingleAsync());
        Assert.Null(await h.Db.WorkOrderProcesses.Select(p=>p.ActualLaborHour).SingleAsync());
        await mes.ReportAsync(new(){WorkOrderNo=no,ProcessCd="P",OperatorCd="OP",GoodQty=1m,ActualStartTime=DateTime.Today,ActualEndTime=DateTime.Today.AddHours(2)},null);
        Assert.Equal(2m,await h.Db.WorkOrderProcesses.Select(p=>p.ActualLaborHour).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Production_inbound_stock_notifications_only_observe_confirmed_receipts()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantProductionInbound(h);var calls=0;var visible=true;
        var notifier=new OperationsNotification(async()=>
        {
            await using var observer=server.Context(new CP6.Core.Services.Common.TenantContext{CurrentTenantId=h.Tenant});
            calls++;visible &= await observer.InboundReceipts.CountAsync()==1 && await observer.StockTransactions.CountAsync()==2;
        });
        var wms=Inbound(h,owner,notifier);var reads=0;
        hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject") && ++reads==2)throw new IOException("final receipt authority unavailable");return Task.CompletedTask;};
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.ConfirmReceiptAsync(ReceiptRequest(),null));Assert.Equal(0,calls);
        hook.Before=null;await wms.ConfirmReceiptAsync(ReceiptRequest(),null);Assert.Equal(2,calls);Assert.True(visible);
    }
    [CurrentPermissionPostgresFact]
    public async Task Production_inbound_manual_partial_production_receipts_preserve_existing_input_contract()
    {
        await using var h=await Seed();var owner=await GrantProductionInbound(h);var wms=Inbound(h,owner);
        var req=ReceiptRequest();req.SourceType=InboundSourceType.Production;req.Details.RemoveAt(1);req.Details[0].ReceivedQty=0.12345678m;
        await wms.ConfirmReceiptAsync(req,null);await wms.ConfirmReceiptAsync(req,null);
        Assert.Equal(0.24691356m,await h.Db.Stocks.Select(s=>s.PhysicalQty).SingleAsync());
        Assert.All(await h.Db.StockTransactions.ToArrayAsync(),t=>Assert.Equal("INBOUND-FG",t.RelatedType));
    }
}
