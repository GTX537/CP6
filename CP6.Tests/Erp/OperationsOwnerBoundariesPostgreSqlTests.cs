using CP6.Core.Services.Erp;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Mes;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Operations_public_source_generation_checks_separate_read_uses_and_does_not_disclose_other_owned_document_keys()
    {
        await using var h=await Seed();await GrantOrderReads(h);var owner=await GrantOperations(h);
        var wms=OperationsWms(h,owner);var mes=OperationsMes(h,owner);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="order",FieldName="UnitPrice",Access=3});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        // MES generation consumes quantity and product, not hidden source price.
        var workOrders=await mes.ExpandFromOrderAsync(new(){WebOrderNo=h.Number},null);Assert.Equal(2,workOrders.Count);
        Assert.Equal("FIELD_READ_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.CreateFromOrderAsync(h.Number,null))).Message);
        await h.Db.Sys_RoleFieldPerms.ExecuteDeleteAsync();
        var number=await wms.CreateFromOrderAsync(h.Number,null);Assert.Equal(2,(await wms.GetOrderAsync(number))!.Details.Count);
        await h.Db.OutboundOrders.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Creator,"another-owner"));
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="wms-outbound-order").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
        var conflict=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.CreateFromOrderAsync(h.Number,null));
        Assert.Equal("OUTBOUND_SOURCE_ALREADY_GENERATED",conflict.Message);Assert.DoesNotContain(number,conflict.Message);
        var menu=await h.Db.Sys_Menus.SingleAsync(m=>m.MenuKey=="mes-work-order");
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==menu.MenuId && a.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.CreateFromWorkOrderAsync(workOrders[0],null))).Message);
        h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=menu.MenuId,ActionCode="query"});
        h.Db.WorkOrderMaterials.Add(new(){WorkOrderNo=workOrders[0],ProcessCd="P",MaterialCd="M",PlanQty=2m,Creator="price-owner"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var material=await wms.CreateFromWorkOrderAsync(workOrders[0],null);
        Assert.Single((await wms.GetOrderAsync(material))!.Details);
    }
    private static WorkOrderService OperationsMes(Harness h,OperationsOwnerV1 owner)=>new(h.Db,new MesSequenceService(h.Db),new NoOpWmsBridgeHook(),owner);
    private static OutboundService OperationsWms(Harness h,OperationsOwnerV1 owner)=>new(h.Db,new WmsSequenceService(h.Db),new StockMovementService(h.Db,new WmsSequenceService(h.Db)),routing:new OutboundRoutingService(h.Db),owner:owner);
    private static WorkOrderDto NewManufacturing()=>new(){ProductCd="P",ProductionQty=1.12345678m,Status=0,
        Processes=[new(){ProcessCd="P",TaskCd="T",PlanQty=1.12345678m}],Materials=[new(){ProcessCd="P",MaterialCd="M",PlanQty=1.12345678m}]};
    private static OutboundOrderDto NewOutbound()=>new(){WarehouseCd="W01",OutboundType=OutboundType.Material,
        Details=[new(){ProductCd="P",RequiredQty=1.12345678m,UnitPrice=1234567890123.1234m}]};

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Operations_current_action_and_complete_object_scope_precede_reads_or_effects(bool manufacturing)
    {
        await using var h=await Seed();await SeedCancelGraph(h);var owner=await GrantOperations(h);
        var resource=manufacturing?"mes-work-order":"wms-outbound-order";
        var number=manufacturing?await h.Db.WorkOrders.Select(x=>x.WorkOrderNo).SingleAsync():await h.Db.OutboundOrders.Select(x=>x.OutboundNo).SingleAsync();
        async Task Read(){if(manufacturing)await OperationsMes(h,owner).GetByNoAsync(number);else await OperationsWms(h,owner).GetOrderAsync(number);}
        await Read();
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey==resource).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
        if(manufacturing)
        {
            await h.Db.WorkOrders.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Creator,"PRICE-OWNER "));
            h.Db.WorkOrderMaterials.Add(new(){WorkOrderNo=number,ProcessCd="P",MaterialCd="X",Creator="other",IsDeleted=true});
        }
        else await h.Db.OutboundOrders.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Creator,"PRICE-OWNER "));
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("OBJECT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(Read)).Message);
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey==resource).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,5));
        var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey==resource).Select(m=>m.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==menu && a.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(Read)).Message);
        Assert.Equal(0,await h.Db.StockTransactions.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Operations_crud_preserves_stored_child_owners_native_keys_precision_and_rejects_state_forgery()
    {
        await using var h=await Seed();var owner=await GrantOperations(h);var mes=OperationsMes(h,owner);var wms=OperationsWms(h,owner);
        var wo=await mes.CreateAsync(NewManufacturing(),"forged");
        var prior=await h.Db.WorkOrderProcesses.AsNoTracking().SingleAsync();
        var dto=(await mes.GetByNoAsync(wo))!;dto.Processes[0].ProcessCd="p ";dto.ProductionQty=2.12345678m;
        await mes.UpdateAsync(wo,dto,"forged");
        var after=await h.Db.WorkOrderProcesses.AsNoTracking().SingleAsync();
        Assert.Equal(prior.Id,after.Id);Assert.Equal("price-owner",after.Creator);
        Assert.Equal(2.12345678m,(await mes.GetByNoAsync(wo))!.ProductionQty);
        dto.Status=4;await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mes.UpdateAsync(wo,dto,null));
        await mes.DeleteAsync(wo,null,"forged");Assert.True(await h.Db.WorkOrders.Select(x=>x.IsDeleted).SingleAsync());
        var no=await wms.CreateOrderAsync(NewOutbound(),"forged");var before=await h.Db.OutboundOrderDetails.AsNoTracking().SingleAsync();
        var outDto=(await wms.GetOrderAsync(no))!;outDto.Details[0].RequiredQty=2.12345678m;
        await wms.UpdateOrderAsync(no,outDto,"forged");var line=await h.Db.OutboundOrderDetails.AsNoTracking().SingleAsync();
        Assert.Equal(before.Id,line.Id);Assert.Equal("price-owner",line.Creator);Assert.Equal(1234567890123.1234m,line.UnitPrice);
        Assert.Equal(2.12345678m,line.RequiredQty);await wms.DeleteOrderAsync(no,"forged");
        Assert.True(await h.Db.OutboundOrders.Select(x=>x.IsDeleted).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Operations_warehouse_grants_hidden_prices_and_unknown_field_registry_fail_closed()
    {
        await using var h=await Seed();await SeedCancelGraph(h);var owner=await GrantOperations(h);var wms=OperationsWms(h,owner);
        var no=await h.Db.OutboundOrders.Select(x=>x.OutboundNo).SingleAsync();
        await h.Db.OutboundOrderDetails.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.UnitPrice,99m));
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-outbound-order",FieldName="UnitPrice",Access=3});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.All((await wms.GetOrderAsync(no))!.Details,d=>Assert.Null(d.UnitPrice));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.CreateOrderAsync(NewOutbound(),null));
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync();
        Assert.Empty(await wms.SearchOrdersAsync(new()));
        Assert.Equal("OBJECT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.CancelOrderAsync(no,null))).Message);
        await h.Db.Sys_RoleFieldPerms.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.FieldName,"not-a-defined-field"));
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.SearchOrdersAsync(new()))).Status);
        Assert.Equal(0,await h.Db.StockTransactions.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Operations_confirm_allocate_pick_ship_commits_exact_stock_and_cannot_ship_twice()
    {
        await using var h=await Seed();var owner=await GrantOperations(h);var wms=OperationsWms(h,owner);
        h.Db.Stocks.Add(new(){WarehouseCd="W01",LocationCd="L",ProductCd="P",LotNo="",PhysicalQty=10m,AvailableQty=10m,UnitPrice=2m});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var number=await wms.CreateOrderAsync(NewOutbound(),"forged");await wms.ConfirmOrderAsync(number,null);
        await wms.AllocateAsync(number,null);await wms.StartPickingAsync(number,null);await wms.ShipAsync(number,new(),null);
        var stock=await h.Db.Stocks.AsNoTracking().SingleAsync();Assert.Equal(8.87654322m,stock.PhysicalQty);Assert.Equal(0m,stock.AllocatedQty);
        Assert.Equal(2,await h.Db.StockTransactions.CountAsync());
        Assert.All(await h.Db.StockTransactions.AsNoTracking().ToArrayAsync(),t=>Assert.Equal("price-owner",t.OperatorCd));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(()=>wms.ShipAsync(number,new(),null));
        Assert.Equal(2,await h.Db.StockTransactions.CountAsync());
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Operations_final_failure_rolls_back_or_reports_actual_unknown_without_replaying(bool lostAck)
    {
        var command=new CommandHook();var commit=new CommitFault();await using var h=await Seed(command,commit);
        await SeedCancelGraph(h);var owner=await GrantOperations(h);var wms=OperationsWms(h,owner);
        var no=await h.Db.OutboundOrders.Select(x=>x.OutboundNo).SingleAsync();var reads=0;
        if(lostAck)commit.Armed=true;
        else command.Before=c=>{if(c.CommandText.Contains("WITH field_subject") && ++reads==2)throw new IOException("final source unavailable");return Task.CompletedTask;};
        var error=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.CancelOrderAsync(no,null));
        Assert.Equal(lostAck?"OPERATIONS_OUTCOME_UNKNOWN":"AUTHORITY_FENCE_UNAVAILABLE",error.Message);
        command.Before=null;h.Db.ChangeTracker.Clear();
        Assert.Equal(lostAck?2:0,await h.Db.StockTransactions.CountAsync());
        Assert.All(await h.Db.Stocks.AsNoTracking().ToArrayAsync(),s=>Assert.Equal(lostAck?0m:2m,s.AllocatedQty));
    }

    [CurrentPermissionPostgresFact]
    public async Task Operations_actual_parent_bridges_only_delegate_exact_generated_children_without_independent_grants()
    {
        await using var h=await Seed();await GrantOrderWrites(h);var owner=await GrantOperations(h);
        var ids=await h.Db.Sys_Menus.Where(m=>m.MenuKey=="mes-work-order" || m.MenuKey=="wms-outbound-order").Select(m=>m.MenuId).ToArrayAsync();
        await h.Db.Sys_RoleActions.Where(a=>ids.Contains(a.MenuId)).ExecuteDeleteAsync();await h.Db.WmsRoleScopes.ExecuteDeleteAsync();
        var wms=OperationsWms(h,owner);var wmsBridge=new WmsBridgeHook(h.Db,wms,null!,NullLogger<WmsBridgeHook>.Instance);
        var mes=new WorkOrderService(h.Db,new MesSequenceService(h.Db),wmsBridge,owner);
        var mesBridge=new MesBridgeHook(h.Db,mes,NullLogger<MesBridgeHook>.Instance);
        var order=new OrderService(h.Db,h.Workflow,wmsBridge,mesBridge:mesBridge,
            writeOwner:new(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),h.Catalog.Capture));
        var no=await order.CreateAsync(NewOrder(),"forged");
        Assert.Equal(1,await h.Db.OutboundOrders.CountAsync(o=>o.WebOrderNo==no));
        Assert.Equal(1,await h.Db.WorkOrders.CountAsync(o=>o.WebOrderNo==no));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.CreateFromOrderAsync(h.Number,null));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mes.ExpandFromOrderAsync(new(){WebOrderNo=h.Number},null));
        // MES issue has a separate bounded automatic material draft path, with no WMS add or warehouse grant.
        var menu=await h.Db.Sys_Menus.SingleAsync(m=>m.MenuKey=="mes-work-order");
        h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=menu.MenuId,ActionCode="issue"});
        var wo=await h.Db.WorkOrders.AsNoTracking().SingleAsync(w=>w.WebOrderNo==no);
        h.Db.WorkOrderProcesses.Add(new(){WorkOrderNo=wo.WorkOrderNo,ProcessCd="P",TaskCd="T"});
        h.Db.WorkOrderMaterials.Add(new(){WorkOrderNo=wo.WorkOrderNo,ProcessCd="P",MaterialCd="M",PlanQty=2m});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();await mes.IssueAsync(wo.WorkOrderNo,"forged");
        Assert.Equal(1,await h.Db.OutboundOrders.CountAsync(o=>o.WorkOrderNo==wo.WorkOrderNo));
    }

    [CurrentPermissionPostgresFact]
    public async Task Operations_protects_active_dependencies_reservation_consistency_foreign_tenants_and_old_transactions()
    {
        await using var h=await Seed();await SeedCancelGraph(h);var owner=await GrantOperations(h);var mes=OperationsMes(h,owner);var wms=OperationsWms(h,owner);
        var wo=await h.Db.WorkOrders.Select(x=>x.WorkOrderNo).SingleAsync();
        Assert.Equal("WORK_ORDER_OUTBOUND_DEPENDENCY_ACTIVE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mes.DeleteAsync(wo,null,null))).Message);
        Assert.Equal("WORK_ORDER_OUTBOUND_DEPENDENCY_ACTIVE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mes.CancelAsync(wo,"reason",null))).Message);
        await h.Db.Stocks.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AllocatedQty,0m));
        var no=await h.Db.OutboundOrders.Select(x=>x.OutboundNo).SingleAsync();
        Assert.Equal("OUTBOUND_RESERVATION_INCONSISTENT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.CancelOrderAsync(no,null))).Message);
        await using var other=await Seed();await SeedCancelGraph(other);
        var foreign=await other.Db.WorkOrders.Select(x=>x.WorkOrderNo).SingleAsync();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mes.GetByNoAsync(foreign));
        await using var tx=await h.Db.Database.BeginTransactionAsync();
        Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mes.GetByNoAsync(wo))).Message);
    }
}
