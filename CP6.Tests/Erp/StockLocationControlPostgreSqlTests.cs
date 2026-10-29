using System.Net;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<(StockMovementService mover,Guid source,Guid destination)> ControlledInventory(Harness h)
    {
        var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync();await GrantMaster(h);
        await EngineeringAction(h,"wms-location","block");await EngineeringAction(h,"wms-location","unblock");
        return(InventoryMover(h,owner),await h.Db.Locations.Where(l=>l.LocationCd=="L1").Select(l=>l.Id).SingleAsync(),
            await h.Db.Locations.Where(l=>l.LocationCd=="L3").Select(l=>l.Id).SingleAsync());
    }
    private static async Task<LocationControlResultV1> BlockInventory(Harness h,Guid id)
        =>await Controls(h).BlockAsync(id,(await Controls(h).ReadAsync(id)).Etag,Block());
    private static async Task NoInventoryEffects(Harness h)
    {
        Assert.Equal(0,await h.Db.StockTransactions.CountAsync());Assert.Equal(0,await h.Db.WmsSequences.CountAsync());
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_main_owner_v1.operations WHERE tenant_id={h.Tenant} AND owner='Main.Wms.StockMovement'").SingleAsync());Assert.Equal(0,await StockNotifications(h));
        Assert.Equal(new[]{10.12345678m,20m},await h.Db.Stocks.OrderBy(s=>s.ProductCd).Select(s=>s.PhysicalQty).ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task StockLocationControl_blocks_all_independent_new_use_but_not_pure_release()
    {
        await using var h=await Seed();var f=await ControlledInventory(h);await BlockInventory(h,f.source);
        foreach(var type in new[]{WmsTxnType.IN,WmsTxnType.OUT,WmsTxnType.MOVE,WmsTxnType.ADJ,WmsTxnType.RSV})
        {
            var c=StockApply();c.TxnType=type;c.RelatedType="OLD_OBLIGATION";c.RelatedNo="not-a-capability";
            Assert.Equal("LOCATION_OPERATION_BLOCKED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.mover.ApplyCommandAsync(c))).Message);
        }
        Assert.Equal("LOCATION_OPERATION_BLOCKED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.mover.ApplyAsync(InventoryAdjustment()))).Message);
        await NoInventoryEffects(h);
        await h.Db.Stocks.Where(s=>s.ProductCd=="P1").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AllocatedQty,2m).SetProperty(x=>x.AvailableQty,8.12345678m));
        var release=StockApply();release.TxnType=WmsTxnType.UNRSV;release.Qty=1m;
        await f.mover.ApplyCommandAsync(release);
        var stock=await h.Db.Stocks.AsNoTracking().SingleAsync(s=>s.ProductCd=="P1");
        Assert.Equal(10.12345678m,stock.PhysicalQty);Assert.Equal(1m,stock.AllocatedQty);Assert.Equal(9.12345678m,stock.AvailableQty);
        Assert.True((await Controls(h).ReadAsync(f.source)).IsBlocked);
    }
    [CurrentPermissionPostgresFact]
    public async Task StockLocationControl_move_checks_both_legs_all_entries_and_preserves_exact_original()
    {
        await using var h=await Seed();var f=await ControlledInventory(h);
        foreach(var id in new[]{f.source,f.destination})
        {
            var b=await BlockInventory(h,id);
            Assert.Equal("LOCATION_OPERATION_BLOCKED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.mover.MoveCommandAsync(StockMove()))).Message);
            Assert.Equal("LOCATION_OPERATION_BLOCKED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.mover.MoveAsync(StockMove()))).Message);
            var context=await f.mover.CreationContextAsync("move",new("W01","L3","P1","LOT"));
            var v2=new StockMoveCommandV2{OperationId=Guid.NewGuid(),WarehouseCd="W01",FromLocationCd="L1",ToLocationCd="L3",ProductCd="P1",LotNo="LOT",Qty=.12345678m,NewStock=new(context.AuthorityVersion,null)};
            Assert.Equal("LOCATION_OPERATION_BLOCKED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.mover.MoveCommandAsync(v2))).Message);
            await NoInventoryEffects(h);Assert.Equal(2,await h.Db.Stocks.CountAsync());
            await Unblock(Controls(h),id,b.BlockId);
        }
        var original=StockMove();var result=await f.mover.MoveCommandAsync(original);
        await BlockInventory(h,f.source);await BlockInventory(h,f.destination);
        Assert.Equal(result,await f.mover.MoveCommandAsync(original));Assert.Equal(result,(await f.mover.ReadOperationAsync(original.OperationId)).Result);
        Assert.Equal(2,await h.Db.StockTransactions.CountAsync());Assert.Equal(1,await StockNotifications(h));
        await RevokeTransferAction(h,"wms-stock","query");
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.mover.MoveCommandAsync(original))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task StockLocationControl_v2_creation_rolls_back_bindings_and_multiple_blocks_require_full_release()
    {
        await using var h=await Seed();var f=await ControlledInventory(h);
        var admin=await MappingAdmin(h);await StandaloneScope(h,admin);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="wms-location" || x.ResourceKey=="wms-warehouse").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,5));
        var command=await NewStockCommand(f.mover,admin.Child);
        var b1=await BlockInventory(h,f.destination);var b2=await BlockInventory(h,f.destination);
        Assert.Equal("LOCATION_OPERATION_BLOCKED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.mover.ApplyCommandAsync(command))).Message);
        Assert.Equal(0,await NewStockMappings(h));Assert.Equal(0,await InheritedMappings(h));await NoInventoryEffects(h);
        Assert.Equal("UNKNOWN",(await f.mover.ReadOperationAsync(command.OperationId)).State);
        await Unblock(Controls(h),f.destination,b1.BlockId);
        Assert.Equal("LOCATION_OPERATION_BLOCKED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.mover.ApplyCommandAsync(command))).Message);
        await Unblock(Controls(h),f.destination,b2.BlockId);var result=await f.mover.ApplyCommandAsync(command);
        Assert.Equal(result,await f.mover.ApplyCommandAsync(command));Assert.Equal(1,await NewStockMappings(h));
        Assert.Equal(1.12345678m,await h.Db.Stocks.Where(s=>s.ProductCd=="NEW").Select(s=>s.PhysicalQty).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task StockLocationControl_final_control_change_rolls_back_both_legs_journal_and_outbox()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await ControlledInventory(h);var hit=false;var command=StockMove();
        hook.Before=async sql=>
        {
            if(!sql.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))return;hook.Before=null;hit=true;
            await using var update=new NpgsqlCommand("UPDATE public.\"T_Location\" SET \"IsBlocked\"=true WHERE \"Id\"=@id",(NpgsqlConnection)sql.Connection!,(NpgsqlTransaction)sql.Transaction!);
            update.Parameters.AddWithValue("id",f.destination);await update.ExecuteNonQueryAsync();
        };
        Assert.Equal("LOCATION_OPERATION_BLOCKED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.mover.MoveCommandAsync(command))).Message);
        Assert.True(hit);await NoInventoryEffects(h);Assert.Equal(2,await h.Db.Stocks.CountAsync());
        Assert.False((await Controls(h).ReadAsync(f.destination)).IsBlocked);
        Assert.Equal("UNKNOWN",(await f.mover.ReadOperationAsync(command.OperationId)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task StockLocationControl_actual_block_serializes_after_stock_commit_and_fences_next_command()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await ControlledInventory(h);
        var etag=(await Controls(h).ReadAsync(f.source)).Etag;Task? competing=null;var waited=false;
        hook.Before=async sql=>
        {
            if(!sql.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))return;hook.Before=null;
            using(ExecutionContext.SuppressFlow())competing=Task.Run(async()=>
            {await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});await Controls(h,db).BlockAsync(f.source,etag,Block());});
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                AND query LIKE '%cp6_authentication_v1.acquire()%') AS "Value"
                """).SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            Assert.False(competing.IsCompleted);waited=true;
        };
        var c=StockApply();StockOperationResultV1 result;
        try{result=await f.mover.ApplyCommandAsync(c);}finally{if(competing!=null)await competing;}
        Assert.True(waited);Assert.True((await Controls(h).ReadAsync(f.source)).IsBlocked);
        Assert.Equal(result,await f.mover.ApplyCommandAsync(c));
        Assert.Equal("LOCATION_OPERATION_BLOCKED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.mover.ApplyCommandAsync(StockApply()))).Message);
        Assert.Equal(1,await h.Db.StockTransactions.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task StockLocationControl_foreign_restrictions_missing_and_other_tenant_controls_are_not_permission()
    {
        await using var h=await Seed();var f=await ControlledInventory(h);var b=await BlockInventory(h,f.source);
        await h.Db.Locations.Where(l=>l.Id==f.source).ExecuteUpdateAsync(s=>s.SetProperty(l=>l.IsBlocked,true));
        Assert.True((await Unblock(Controls(h),f.source,b.BlockId)).IsBlocked);
        Assert.Equal("LOCATION_OPERATION_BLOCKED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.mover.ApplyCommandAsync(StockApply()))).Message);
        await using var other=await Seed();var foreign=await ControlledInventory(other);
        await other.Db.Locations.Where(l=>l.Id==foreign.source).ExecuteDeleteAsync();
        Assert.Equal("LOCATION_CONTROL_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>foreign.mover.ApplyCommandAsync(StockApply()))).Message);
        await NoInventoryEffects(other);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AreaCd,"NOT-ALLOWED"));
        Assert.Equal("OBJECT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.mover.ApplyCommandAsync(StockApply()))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task StockLocationControl_parent_count_correction_remains_an_authenticated_fact_path()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);var no=await CountInventory(h,owner);
        await h.Db.Locations.ExecuteUpdateAsync(s=>s.SetProperty(l=>l.IsBlocked,true));
        await InventoryTake(h,owner).ApproveAndApplyAsync(no,"forged");
        Assert.Equal(StockTakeStatus.Completed,(await InventoryTake(h,owner).GetAsync(no))!.Status);
        Assert.Equal(new[]{9m,19m},await h.Db.Stocks.OrderBy(s=>s.ProductCd).Select(s=>s.PhysicalQty).ToArrayAsync());
        Assert.Equal(2,await h.Db.StockTransactions.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task StockLocationControl_real_HTTP_returns_conflict_and_recovers_preexisting_original()
    {
        await using var h=await Seed();var f=await ControlledInventory(h);var command=StockApply();await f.mover.ApplyCommandAsync(command);
        await BlockInventory(h,f.source);await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        const string root="api/wms/stock/";
        using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"commands/v1/apply",StockApply()));
        Assert.Equal(HttpStatusCode.Conflict,denied.StatusCode);Assert.Contains("LOCATION_OPERATION_BLOCKED",await denied.Content.ReadAsStringAsync());
        using var replay=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"commands/v1/apply",command));Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
        using var recovered=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root+$"operations/v1/{command.OperationId}"));Assert.Equal(HttpStatusCode.OK,recovered.StatusCode);
        Assert.True(recovered.Headers.CacheControl?.NoStore);Assert.Equal(1,await h.Db.StockTransactions.CountAsync());
    }
}
