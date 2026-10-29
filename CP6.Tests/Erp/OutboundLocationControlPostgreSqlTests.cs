using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<(OutboundService service,Guid first,Guid second)> AllocationControls(Harness h,bool routed=true)
    {
        var owner=await GrantOperations(h);await h.Db.WmsRoleScopes.ExecuteDeleteAsync();await GrantMaster(h);
        await EngineeringAction(h,"wms-location","block");await EngineeringAction(h,"wms-location","unblock");
        foreach(var code in new[]{"W01","W02"})
        {
            h.Db.Warehouses.Add(new(){WarehouseCd=code,WarehouseName=code,Creator="price-owner"});
            var location=code=="W01"?"L":"L2";
            h.Db.Locations.Add(new(){WarehouseCd=code,LocationCd=location,AreaCd="A",Creator="price-owner"});
            h.Db.Stocks.Add(new(){WarehouseCd=code,LocationCd=location,ProductCd="P",LotNo="LOT",PhysicalQty=10m,AvailableQty=10m,Creator="price-owner"});
        }
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var seq=new WmsSequenceService(h.Db);
        IOutboundRoutingService routing=routed?new OutboundRoutingService(h.Db):new NoOpOutboundRoutingService(h.Db);
        var service=new OutboundService(h.Db,seq,new StockMovementService(h.Db,seq),shortage:new MaterialShortageService(h.Db),routing:routing,owner:owner);
        return(service,await h.Db.Locations.Where(l=>l.WarehouseCd=="W01").Select(l=>l.Id).SingleAsync(),
            await h.Db.Locations.Where(l=>l.WarehouseCd=="W02").Select(l=>l.Id).SingleAsync());
    }
    private static async Task<string> ConfirmAllocation(OutboundService service,OutboundOrderDto? input=null)
    {var no=await service.CreateOrderAsync(input??NewOutbound(),null);await service.ConfirmOrderAsync(no,null);return no;}
    private static async Task<string> AllocationEffects(Harness h)=>JsonSerializer.Serialize(new{
        stocks=await h.Db.Stocks.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync(),
        headers=await h.Db.OutboundOrders.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync(),
        details=await h.Db.OutboundOrderDetails.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync(),
        transactions=await h.Db.StockTransactions.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync(),
        sequences=await h.Db.WmsSequences.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync(),
        shortages=await h.Db.MaterialShortages.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync()});
    [CurrentPermissionPostgresFact]
    public async Task OutboundLocationControl_selects_same_warehouse_then_cross_warehouse_eligible_alternative()
    {
        await using var h=await Seed();var f=await AllocationControls(h);await BlockInventory(h,f.first);
        var alternative=new Location{WarehouseCd="W01",LocationCd="NEXT",AreaCd="A",Creator="price-owner"};h.Db.Locations.Add(alternative);
        h.Db.Stocks.Add(new(){WarehouseCd="W01",LocationCd="NEXT",ProductCd="P",LotNo="LATER",PhysicalQty=10m,AvailableQty=10m,Creator="price-owner"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var no=await ConfirmAllocation(f.service);await f.service.AllocateAsync(no,null);
        var first=await h.Db.OutboundOrderDetails.AsNoTracking().SingleAsync(d=>d.OutboundNo==no);Assert.Equal("W01",first.WarehouseCd);Assert.Equal("NEXT",first.LocationCd);
        await f.service.CancelOrderAsync(no,null);await BlockInventory(h,alternative.Id);
        var next=await ConfirmAllocation(f.service);await f.service.AllocateAsync(next,null);
        var second=await h.Db.OutboundOrderDetails.AsNoTracking().SingleAsync(d=>d.OutboundNo==next);Assert.Equal("W02",second.WarehouseCd);Assert.Equal("L2",second.LocationCd);
        Assert.Equal(1.12345678m,second.AllocatedQty);Assert.Equal(0m,await h.Db.Stocks.Where(s=>s.WarehouseCd=="W01").SumAsync(s=>s.AllocatedQty));
    }
    [CurrentPermissionPostgresFact]
    public async Task OutboundLocationControl_blocked_and_unknown_candidates_never_manufacture_shortage()
    {
        await using var h=await Seed();var f=await AllocationControls(h);await BlockInventory(h,f.first);var b=await BlockInventory(h,f.second);
        var no=await ConfirmAllocation(f.service);var before=await AllocationEffects(h);
        Assert.Equal("LOCATION_OPERATION_BLOCKED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.service.AllocateAsync(no,null))).Message);
        Assert.Equal(before,await AllocationEffects(h));Assert.Equal(0,await h.Db.MaterialShortages.CountAsync());
        await Unblock(Controls(h),f.second,b.BlockId);await f.service.AllocateAsync(no,null);Assert.Equal(1,await h.Db.StockTransactions.CountAsync());
        await using var other=await Seed();var u=await AllocationControls(other);await BlockInventory(other,u.first);
        await other.Db.Locations.Where(l=>l.Id==u.second).ExecuteDeleteAsync();var unknown=await ConfirmAllocation(u.service);var original=await AllocationEffects(other);
        Assert.Equal("LOCATION_CONTROL_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>u.service.AllocateAsync(unknown,null))).Message);
        Assert.Equal(original,await AllocationEffects(other));
    }
    [CurrentPermissionPostgresFact]
    public async Task OutboundLocationControl_final_change_rolls_back_all_lines_sequences_and_bindings()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var f=await AllocationControls(h,false);
        h.Db.Stocks.Add(new(){WarehouseCd="W01",LocationCd="L",ProductCd="Q",LotNo="LOT",PhysicalQty=10m,AvailableQty=10m,Creator="price-owner"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var admin=await MappingAdmin(h);
        foreach(var id in await h.Db.Stocks.Where(s=>s.WarehouseCd=="W01").Select(s=>s.Id).ToArrayAsync())await BindStandalone(h,admin,"stock",id);
        var draft=NewOutbound();draft.Details.Add(new(){ProductCd="Q",RequiredQty=2m});var no=await ConfirmAllocation(f.service,draft);var before=await AllocationEffects(h);var hit=false;
        hook.Before=async sql=>
        {
            if(!sql.CommandText.Contains("UPDATE") || !sql.CommandText.Contains("T_OutboundOrder"))return;hook.Before=null;hit=true;
            await using var update=new NpgsqlCommand("UPDATE public.\"T_Location\" SET \"IsBlocked\"=true WHERE \"Id\"=@id",(NpgsqlConnection)sql.Connection!,(NpgsqlTransaction)sql.Transaction!);
            update.Parameters.AddWithValue("id",f.first);await update.ExecuteNonQueryAsync();
        };
        Assert.Equal("LOCATION_OPERATION_BLOCKED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.service.AllocateAsync(no,null))).Message);
        Assert.True(hit);Assert.Equal(before,await AllocationEffects(h));Assert.Equal(0,await InheritedMappings(h));
        Assert.False((await Controls(h).ReadAsync(f.first)).IsBlocked);
    }
    [CurrentPermissionPostgresFact]
    public async Task OutboundLocationControl_later_blocked_line_rolls_back_earlier_shortage_and_reservation()
    {
        await using var h=await Seed();var f=await AllocationControls(h,false);
        h.Db.Locations.Add(new(){WarehouseCd="W01",LocationCd="BLOCKED",IsBlocked=true});
        h.Db.Stocks.Add(new(){WarehouseCd="W01",LocationCd="BLOCKED",ProductCd="Q",PhysicalQty=10m,AvailableQty=10m,Creator="price-owner"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var draft=NewOutbound();draft.WorkOrderNo=await SeedProduction(h);
        draft.Details.Insert(0,new(){ProductCd="MISSING",RequiredQty=3m});draft.Details.Add(new(){ProductCd="Q",RequiredQty=2m});
        var no=await ConfirmAllocation(f.service,draft);var before=await AllocationEffects(h);
        Assert.Equal("LOCATION_OPERATION_BLOCKED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.service.AllocateAsync(no,null))).Message);
        Assert.Equal(before,await AllocationEffects(h));Assert.Equal(0,await h.Db.MaterialShortages.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task OutboundLocationControl_actual_competing_block_preserves_old_reservation_cancellation()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var f=await AllocationControls(h,false);
        var no=await ConfirmAllocation(f.service);var etag=(await Controls(h).ReadAsync(f.first)).Etag;Task? competing=null;var waited=false;
        hook.Before=async sql=>
        {
            if(!sql.CommandText.Contains("UPDATE") || !sql.CommandText.Contains("T_OutboundOrder"))return;hook.Before=null;
            using(ExecutionContext.SuppressFlow())competing=Task.Run(async()=>
            {await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});await Controls(h,db).BlockAsync(f.first,etag,Block());});
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                AND query LIKE '%cp6_authentication_v1.acquire()%') AS "Value"
                """).SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            waited=true;Assert.False(competing.IsCompleted);
        };
        try{await f.service.AllocateAsync(no,null);}finally{if(competing!=null)await competing;}
        Assert.True(waited);Assert.True((await Controls(h).ReadAsync(f.first)).IsBlocked);
        var next=await ConfirmAllocation(f.service);
        Assert.Equal("LOCATION_OPERATION_BLOCKED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.service.AllocateAsync(next,null))).Message);
        await f.service.CancelOrderAsync(no,null);await f.service.CancelOrderAsync(no,null);
        Assert.Equal(0m,await h.Db.Stocks.SumAsync(s=>s.AllocatedQty));Assert.Equal(2,await h.Db.StockTransactions.CountAsync());
        Assert.All(await h.Db.Stocks.AsNoTracking().ToArrayAsync(),s=>Assert.Equal(10m,s.PhysicalQty));
    }
    [CurrentPermissionPostgresFact]
    public async Task OutboundLocationControl_uses_current_tenant_and_does_not_disclose_hidden_candidate_controls()
    {
        await using var h=await Seed();var f=await AllocationControls(h,false);await using var other=await Seed();await AllocationControls(other);
        await h.Db.Locations.Where(l=>l.Id==f.first).ExecuteDeleteAsync();var no=await ConfirmAllocation(f.service);
        Assert.Equal("LOCATION_CONTROL_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.service.AllocateAsync(no,null))).Message);
        // The other tenant's same code is not evidence; only exact current WMS grants may select alternatives.
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.WarehouseCd,"W01").SetProperty(x=>x.AreaCd,"A"));
        Assert.Equal("OBJECT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.service.AllocateAsync(no,null))).Message);
        Assert.Equal(0,await h.Db.StockTransactions.CountAsync());
    }
    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task OutboundLocationControl_real_HTTP_allocation_conflict_and_cancel_work_with_both_routing_modes(bool enabled)
    {
        await using var h=await Seed();var f=await AllocationControls(h);var no=await ConfirmAllocation(f.service);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{{"OutboundRouting__Enabled",enabled.ToString()}});var cookies=await Login(host,h);
        const string root="api/wms/outbound-order/";
        using var allocate=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+no+"/allocate"));Assert.True(allocate.IsSuccessStatusCode,await OperationsFailure(allocate));
        await BlockInventory(h,f.first);await BlockInventory(h,f.second);
        using var cancel=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+no+"/cancel"));Assert.Equal(HttpStatusCode.OK,cancel.StatusCode);
        var next=await ConfirmAllocation(f.service);
        using var blocked=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+next+"/allocate"));Assert.Equal(HttpStatusCode.Conflict,blocked.StatusCode);
        Assert.Contains("LOCATION_OPERATION_BLOCKED",await blocked.Content.ReadAsStringAsync());Assert.Equal(0m,await h.Db.Stocks.SumAsync(s=>s.AllocatedQty));
        Assert.Equal(2,await h.Db.StockTransactions.CountAsync());
    }
}
