using System.Net;
using System.Net.Http.Json;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Erp;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Http;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Order_cancel_requires_current_service_action_before_any_effect()
    {
        await using var h=await Seed();
        var error=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Service.CancelAsync(h.Number,"reason",true,"forged"));
        Assert.Equal(403,error.Status);
        Assert.Null(await h.Db.Orders.AsNoTracking().Select(o=>o.CancelledAt).SingleAsync());
    }

    private sealed class FailSecondCancelRelease : SaveChangesInterceptor
    {
        internal bool Enabled;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result,CancellationToken ct=default)
        {
            if(Enabled && data.Context!.ChangeTracker.Entries<StockTransaction>().Any(e=>e.State==EntityState.Added && e.Entity.ProductCd=="P2"))
            { Enabled=false; throw new InvalidOperationException("injected second release failure"); }
            return ValueTask.FromResult(result);
        }
    }
    private static async Task GrantOrderCancel(Harness h)
    {
        await GrantOrderWrites(h);
        var menu=await h.Db.Sys_Menus.SingleAsync(m=>m.MenuKey=="erp-order");
        h.Db.Sys_MenuActions.Add(new(){MenuId=menu.MenuId,ActionCode="cancel",ActionName="Cancel"});
        h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=menu.MenuId,ActionCode="cancel"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
    }
    private static OrderService CancelService(Harness h, IFinBridgeHook? fin=null)
    {
        var sequence=new WmsSequenceService(h.Db);
        var stock=new StockMovementService(h.Db,sequence);
        var outbound=new OutboundService(h.Db,sequence,stock,finBridge:fin);
        var wo=new WorkOrderService(h.Db,new MesSequenceService(h.Db),new NoOpWmsBridgeHook());
        var bridge=new OrderCancelBridgeHook(h.Db,wo,outbound,NullLogger<OrderCancelBridgeHook>.Instance);
        return new(h.Db,h.Workflow,new NoOpWmsBridgeHook(),cancelBridge:bridge,
            writeOwner:new OrderWriteOwnerV1(h.Db,new HttpContextAccessor{HttpContext=h.Http},_=>Task.FromResult(h.User),h.Catalog.Capture));
    }
    private static async Task SeedCancelGraph(Harness h)
    {
        h.Db.WorkOrders.Add(new(){WorkOrderNo="WO"+h.Tenant.ToString("N")[..16],WebOrderNo=h.Number,ProductCd="P",Status=WorkOrderStatus.Confirmed,Creator="origin"});
        h.Db.OutboundOrders.Add(new(){OutboundNo="OUT"+h.Tenant.ToString("N")[..16],WebOrderNo=h.Number,WorkOrderNo="WO"+h.Tenant.ToString("N")[..16],OutboundType=OutboundType.Material,WarehouseCd="WH"+h.Tenant.ToString("N")[..8],Status=OutboundOrderStatus.Allocated,Creator="origin"});
        for(int i=1;i<=2;i++)
        {
            h.Db.OutboundOrderDetails.Add(new(){OutboundNo="OUT"+h.Tenant.ToString("N")[..16],LineNo=i,ProductCd="P"+i,WarehouseCd="WH"+h.Tenant.ToString("N")[..8],LocationCd="L",LotNo="",RequiredQty=2m,AllocatedQty=2m,Creator="origin"});
            h.Db.Stocks.Add(new(){WarehouseCd="WH"+h.Tenant.ToString("N")[..8],LocationCd="L",ProductCd="P"+i,LotNo="",PhysicalQty=10m,AllocatedQty=2m,AvailableQty=8m,Creator="origin"});
        }
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
    }
    [CurrentPermissionPostgresFact]
    public async Task Order_cancel_failed_outbound_rolls_back_whole_release_and_keeps_dependent_work_order()
    {
        var failure=new FailSecondCancelRelease();await using var h=await Seed(failure);
        await GrantOrderCancel(h);await SeedCancelGraph(h);failure.Enabled=true;
        await CancelService(h).CancelAsync(h.Number,"reason",true,"forged");
        h.Db.ChangeTracker.Clear();
        Assert.All(await h.Db.Stocks.AsNoTracking().ToArrayAsync(),s=>Assert.Equal(2m,s.AllocatedQty));
        Assert.All(await h.Db.OutboundOrderDetails.AsNoTracking().ToArrayAsync(),d=>Assert.Equal(2m,d.AllocatedQty));
        Assert.Equal(0,await h.Db.StockTransactions.CountAsync());
        Assert.Equal(WorkOrderStatus.Confirmed,await h.Db.WorkOrders.Select(w=>w.Status).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Order_cancel_native_keys_release_exact_quantities_preserve_identity_and_retry_only_remaining()
    {
        await using var h=await Seed();await GrantOrderCancel(h);await SeedCancelGraph(h);
        const decimal quantity=1.12345678m;
        await h.Db.Stocks.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AllocatedQty,quantity).SetProperty(x=>x.AvailableQty,10m-quantity));
        await h.Db.OutboundOrderDetails.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AllocatedQty,quantity));
        await h.Db.WorkOrders.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,WorkOrderStatus.InProgress));
        var service=CancelService(h);
        var probe=await service.CancelAsync(h.Number.ToLowerInvariant()+" ","reason",false,"forged");
        Assert.Equal(CP6.Entity.DTOs.Erp.CancelOutcome.NeedsDecision,probe.Outcome);
        Assert.Equal(0,await h.Db.StockTransactions.CountAsync());Assert.Null(await h.Db.Orders.Select(o=>o.CancelledAt).SingleAsync());
        var partial=await service.CancelAsync(h.Number.ToLowerInvariant()+" ","reason",true,"forged");
        Assert.Equal(CP6.Entity.DTOs.Erp.CancelOutcome.PartiallyCancelled,partial.Outcome);
        Assert.All(await h.Db.Stocks.AsNoTracking().ToArrayAsync(),stock=>{Assert.Equal(10m,stock.PhysicalQty);Assert.Equal(0,stock.AllocatedQty);Assert.Equal("origin",stock.Creator);});
        Assert.All(await h.Db.StockTransactions.AsNoTracking().ToArrayAsync(),t=>{Assert.Equal(quantity,t.Qty);Assert.Equal("price-owner",t.OperatorCd);});
        Assert.Equal(WorkOrderStatus.InProgress,await h.Db.WorkOrders.Select(w=>w.Status).SingleAsync());
        // An explicitly reconciled state is retried by the caller, never an automatic UNKNOWN replay.
        await h.Db.WorkOrders.ExecuteUpdateAsync(s=>s.SetProperty(w=>w.Status,WorkOrderStatus.Confirmed));
        var completed=await service.CancelAsync(h.Number,"remaining",true,null);
        Assert.Equal(CP6.Entity.DTOs.Erp.CancelOutcome.Cancelled,completed.Outcome);
        Assert.Equal(2,await h.Db.StockTransactions.CountAsync());
        Assert.Equal(WorkOrderStatus.Cancelled,await h.Db.WorkOrders.Select(w=>w.Status).SingleAsync());
        Assert.Equal("origin",await h.Db.WorkOrders.Select(w=>w.Creator).SingleAsync());
        Assert.Equal(CP6.Entity.DTOs.Erp.CancelOutcome.Rejected,(await service.CancelAsync(h.Number,"again",true,null)).Outcome);
        Assert.Equal(2,await h.Db.StockTransactions.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Order_cancel_checks_complete_scope_and_conflicting_child_parent_before_probe_or_mutation()
    {
        await using var h=await Seed();await GrantOrderCancel(h);await SeedCancelGraph(h);
        await using var foreign=await Seed();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>CancelService(h).CancelAsync(foreign.Number,"reason",true,null));
        await h.Db.OutboundOrders.ExecuteUpdateAsync(s=>s.SetProperty(o=>o.WebOrderNo,"OTHER"));
        Assert.Equal("CASCADE_OWNER_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>CancelService(h).CancelAsync(h.Number,"reason",false,null))).Message);
        await h.Db.OutboundOrders.ExecuteUpdateAsync(s=>s.SetProperty(o=>o.WebOrderNo,h.Number));
        await h.Db.Sys_RoleDataScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
        await h.Db.OrderDetails.Where(d=>d.WebOrderDetailNo==2).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Creator,"other").SetProperty(d=>d.IsDeleted,true));
        Assert.Equal("OBJECT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>CancelService(h).CancelAsync(h.Number,"reason",true,"other"))).Message);
        Assert.Equal(0,await h.Db.StockTransactions.CountAsync());Assert.Null(await h.Db.Orders.Select(o=>o.CancelledAt).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Order_cancel_final_authority_failure_rolls_back_stock_ledger_sequence_children_and_parent()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);await GrantOrderCancel(h);await SeedCancelGraph(h);
        var reads=0;hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject")&&++reads==2)throw new IOException("final authority failure");return Task.CompletedTask;};
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>CancelService(h).CancelAsync(h.Number,"reason",true,null))).Status);
        Assert.Equal(2,reads);Assert.Equal(0,await h.Db.StockTransactions.CountAsync());Assert.Equal(0,await h.Db.WmsSequences.CountAsync());
        Assert.All(await h.Db.Stocks.AsNoTracking().ToArrayAsync(),s=>Assert.Equal(2m,s.AllocatedQty));
        Assert.Equal(OutboundOrderStatus.Allocated,await h.Db.OutboundOrders.Select(o=>o.Status).SingleAsync());
        Assert.Equal(WorkOrderStatus.Confirmed,await h.Db.WorkOrders.Select(o=>o.Status).SingleAsync());
        Assert.Null(await h.Db.Orders.Select(o=>o.CancelledAt).SingleAsync());Assert.False(h.Db.ChangeTracker.HasChanges());
    }
    [CurrentPermissionPostgresFact]
    public async Task Order_cancel_refuses_old_transaction_and_returns_unknown_after_lost_commit_ack_without_replay()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);await GrantOrderCancel(h);await SeedCancelGraph(h);
        var service=CancelService(h);
        await using(var tx=await h.Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead))
        {Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CancelAsync(h.Number,"reason",true,null))).Status);Assert.Same(tx,h.Db.Database.CurrentTransaction);}
        fault.Armed=true;
        Assert.Equal("ORDER_CANCEL_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CancelAsync(h.Number,"reason",true,null))).Message);
        Assert.Equal(OrderLifecycleStatus.Cancelled,await h.Db.Orders.Select(o=>o.OrderStatus).SingleAsync());
        Assert.Equal(2,await h.Db.StockTransactions.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Order_cancel_shortage_never_clamps_or_double_releases_shared_stock()
    {
        await using var h=await Seed();await GrantOrderCancel(h);await SeedCancelGraph(h);
        await h.Db.OutboundOrderDetails.ExecuteUpdateAsync(s=>s.SetProperty(d=>d.ProductCd,"P1"));
        var result=await CancelService(h).CancelAsync(h.Number,"reason",true,null);
        Assert.Equal(CP6.Entity.DTOs.Erp.CancelOutcome.PartiallyCancelled,result.Outcome);
        Assert.Equal("CANCEL_CHILD_FAILED",Assert.Single(result.RelatedOutbounds).Message);
        Assert.All(await h.Db.Stocks.AsNoTracking().ToArrayAsync(),s=>Assert.Equal(2m,s.AllocatedQty));Assert.Equal(0,await h.Db.StockTransactions.CountAsync());
        Assert.Equal(WorkOrderStatus.Confirmed,await h.Db.WorkOrders.Select(w=>w.Status).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Order_cancel_actual_program_requires_csrf_action_original_session_and_never_returns_prices()
    {
        await using var h=await Seed();await GrantOrderCancel(h);await SeedCancelGraph(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        HttpRequestMessage Request(bool csrf=true)
        {var r=new HttpRequestMessage(HttpMethod.Post,"api/orders/"+h.Number+"/cancel"){Content=JsonContent.Create(new {reason="request",force=true})};
         r.Headers.Add("Cookie",string.Join("; ",cookies.Select(x=>x.Key+"="+x.Value)));if(csrf)r.Headers.Add("X-CSRF-Token",cookies["cp6_csrf"]);return r;}
        using(var response=await host.Send(Request(false)))Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        await h.Db.Sys_RoleActions.Where(a=>a.ActionCode=="cancel").ExecuteDeleteAsync();
        using(var response=await host.Send(Request()))Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        var menu=await h.Db.Sys_Menus.SingleAsync(m=>m.MenuKey=="erp-order");h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=menu.MenuId,ActionCode="cancel"});
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="order",FieldName="UnitPrice",Access=3});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using(var response=await host.Send(Request()))
        {Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());Assert.True(response.Headers.CacheControl?.NoStore);Assert.DoesNotContain("unitPrice",await response.Content.ReadAsStringAsync(),StringComparison.OrdinalIgnoreCase);}
        Assert.Equal(OrderLifecycleStatus.Cancelled,await h.Db.Orders.Select(o=>o.OrderStatus).SingleAsync());Assert.Equal(2,await h.Db.StockTransactions.CountAsync());
        await h.Db.Sys_Users.Where(u=>u.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.Enable,false));
        using(var response=await host.Send(Request()))Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
    }

    private sealed class CancelFin(Func<string,string?,Task> callback):IFinBridgeHook
    {
        public Task<FinBridgeResult> OnShipmentConfirmedAsync(FinShipmentInvoiceRequest request,string? userName)=>Task.FromResult(FinBridgeResult.Skipped("n/a"));
        public Task<FinBridgeResult> OnWorkOrderCompletedAsync(string number,string? userName)=>Task.FromResult(FinBridgeResult.Skipped("n/a"));
        public async Task<FinBridgeResult> OnShipmentCancelledAsync(string number,string? userName)
        {await callback(number,userName);return FinBridgeResult.Skipped("test");}
    }
    [CurrentPermissionPostgresFact]
    public async Task Order_cancel_external_effects_observe_confirmed_commit_and_do_not_run_after_final_denial()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);await GrantOrderCancel(h);await SeedCancelGraph(h);
        await h.Db.OutboundOrders.ExecuteUpdateAsync(s=>s.SetProperty(o=>o.OutboundType,OutboundType.Shipping));
        int calls=0;var bridge=new CancelFin(async (number,actor)=>
        {calls++;await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
         Assert.Equal(OrderLifecycleStatus.Cancelled,await observer.Orders.Select(o=>o.OrderStatus).SingleAsync());
         Assert.Equal(2,await observer.StockTransactions.CountAsync());Assert.Equal("price-owner",actor);
         throw new IOException("post-commit transport failure");});
        int reads=0;hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject")&&++reads==2)throw new IOException("authority");return Task.CompletedTask;};
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>CancelService(h,bridge).CancelAsync(h.Number,"reason",true,null));Assert.Equal(0,calls);
        hook.Before=null;
        Assert.Equal(CP6.Entity.DTOs.Erp.CancelOutcome.Cancelled,(await CancelService(h,bridge).CancelAsync(h.Number,"reason",true,null)).Outcome);
        Assert.Equal(1,calls);Assert.Equal(2,await h.Db.StockTransactions.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Order_cancel_holds_raw_revocation_until_commit_then_rejects_reentry()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);await GrantOrderCancel(h);await SeedCancelGraph(h);
        Task? writer=null;var blocked=false;
        hook.After=async c=>
        {
            if(!c.CommandText.Contains("WITH field_subject"))return;hook.After=null;
            writer=Task.Run(async()=>{await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Roles\" SET \"Enable\"=false WHERE \"TenantId\"={h.Tenant} AND \"RoleId\"=72");});
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                AND query LIKE 'UPDATE public."Sys_Roles"%') AS "Value"
                """).SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            blocked=true;Assert.False(writer.IsCompleted);
        };
        try{await CancelService(h).CancelAsync(h.Number,"reason",true,null);}finally{if(writer!=null)await writer;}
        Assert.True(blocked);Assert.Equal(2,await h.Db.StockTransactions.CountAsync());
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>CancelService(h).CancelAsync(h.Number,"again",true,null));
    }

    [CurrentPermissionPostgresFact]
    public async Task Order_cancel_serializes_duplicate_calls_and_fences_raw_child_writers()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);await GrantOrderCancel(h);await SeedCancelGraph(h);
        Task? writer=null;var blocked=false;
        hook.After=async c=>
        {
            if(!c.CommandText.Contains("\"T_WorkOrder\"") || !c.CommandText.Contains("SELECT"))return;hook.After=null;
            writer=Task.Run(async()=>{await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"T_OutboundOrder\" SET \"Remarks\"='later' WHERE \"TenantId\"={h.Tenant}");});
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                AND query LIKE 'UPDATE public."T_OutboundOrder"%') AS "Value"
                """).SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            blocked=true;Assert.False(writer.IsCompleted);
        };
        try{Assert.Equal(CP6.Entity.DTOs.Erp.CancelOutcome.Cancelled,(await CancelService(h).CancelAsync(h.Number,"first",true,null)).Outcome);}finally{if(writer!=null)await writer;}
        Assert.True(blocked);Assert.Equal("later",await h.Db.OutboundOrders.Select(o=>o.Remarks).SingleAsync());
        // A separate pair of actual native callers has one cancellation and one state-machine rejection.
        await using var other=await Seed();await GrantOrderCancel(other);await SeedCancelGraph(other);
        async Task<CP6.Entity.DTOs.Erp.CancelOutcome> Run()
        {await using var db=server.Context(new TenantContext{CurrentTenantId=other.Tenant});return (await CancelService(other with {Db=db}).CancelAsync(other.Number,"same",true,"forged")).Outcome;}
        var outcomes=await Task.WhenAll(Task.Run(Run),Task.Run(Run));
        Assert.Single(outcomes,x=>x==CP6.Entity.DTOs.Erp.CancelOutcome.Cancelled);Assert.Single(outcomes,x=>x==CP6.Entity.DTOs.Erp.CancelOutcome.Rejected);
        Assert.Equal(2,await other.Db.StockTransactions.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Order_cancel_inconsistent_consumed_or_unallocated_child_never_claims_success()
    {
        await using var h=await Seed();await GrantOrderCancel(h);await SeedCancelGraph(h);
        await h.Db.OutboundOrders.ExecuteUpdateAsync(s=>s.SetProperty(o=>o.Status,OutboundOrderStatus.Confirmed));
        await h.Db.WorkOrders.ExecuteUpdateAsync(s=>s.SetProperty(w=>w.CompletedQty,1m));
        var result=await CancelService(h).CancelAsync(h.Number,"reason",true,null);
        Assert.Equal(CP6.Entity.DTOs.Erp.CancelOutcome.PartiallyCancelled,result.Outcome);
        Assert.Equal(OutboundOrderStatus.Confirmed,await h.Db.OutboundOrders.Select(o=>o.Status).SingleAsync());
        Assert.Equal(WorkOrderStatus.Confirmed,await h.Db.WorkOrders.Select(w=>w.Status).SingleAsync());Assert.Equal(0,await h.Db.StockTransactions.CountAsync());
        await h.Db.OutboundOrderDetails.ExecuteDeleteAsync();await h.Db.OutboundOrders.ExecuteDeleteAsync();
        var stillConsumed=await CancelService(h).CancelAsync(h.Number,"same",true,null);
        Assert.Equal(CP6.Entity.DTOs.Erp.CancelOutcome.PartiallyCancelled,stillConsumed.Outcome);
        Assert.Equal(WorkOrderStatus.Confirmed,await h.Db.WorkOrders.Select(w=>w.Status).SingleAsync());
    }
}
