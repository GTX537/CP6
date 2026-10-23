using System.Net;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static StockApplyCommandV1 StockApply(Guid? id=null)=>new(){OperationId=id??Guid.NewGuid(),TxnType="ADJ",
        WarehouseCd="W01",LocationCd="L1",ProductCd="P1",LotNo="LOT",Qty=1.12345678m,UnitPrice=3m,OperatorCd="forged"};
    private static StockMoveCommandV1 StockMove(Guid? id=null)=>new(){OperationId=id??Guid.NewGuid(),
        WarehouseCd="W01",FromLocationCd="L1",ToLocationCd="L3",ProductCd="P1",LotNo="LOT",Qty=.12345678m,OperatorCd="forged"};
    private static StockMovementService OriginalStockOwner(Harness h,CP6Context db,Guid? actor=null)
    {
        var catalog=new AuthorizationCatalogStoreV1();catalog.RegisterFields("wms-stock",[new("UnitPrice","UnitPrice")]);
        catalog.RegisterScope("wms-stock","wms-stock",[1,2,3,4,5],1);catalog.Seal();
        var subject=actor??h.User;
        var owner=new OperationsOwnerV1(db,new OperationsHttp{HttpContext=actor==null?h.Http:new DefaultHttpContext
            {User=CurrentPermissionContextTests.Principal(subject,h.Tenant)}},_=>Task.FromResult(subject),catalog.Capture);
        return new(db,new WmsSequenceService(db),owner:owner);
    }
    [CurrentPermissionPostgresFact]
    public async Task Stock_original_adjustment_commits_exact_original_and_audit_once_without_repeat_notifications()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var notifications=0;var notificationSawCommit=false;var notifier=new OperationsNotification(async()=>
        {
            notifications++;await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
            notificationSawCommit=await observer.StockTransactions.CountAsync()==1 && h.Db.Database.CurrentTransaction==null;
        });
        var mover=new StockMovementService(h.Db,new WmsSequenceService(h.Db),notifier,owner:owner);
        var command=StockApply();var result=await mover.ApplyCommandAsync(command);
        Assert.Equal(result,await mover.ApplyCommandAsync(command));Assert.Equal(1,notifications);Assert.True(notificationSawCommit);
        Assert.Equal(1,await Operations(h));Assert.Equal(1,await h.Db.StockTransactions.CountAsync());
        Assert.Equal(11.24691356m,await h.Db.Stocks.Where(s=>s.ProductCd=="P1").Select(s=>s.PhysicalQty).SingleAsync());
        Assert.Equal("price-owner",await h.Db.StockTransactions.Select(s=>s.OperatorCd).SingleAsync());
        var audit=await h.Db.Database.SqlQuery<string>($"SELECT audit_json::text AS \"Value\" FROM cp6_main_owner_v1.operations WHERE tenant_id={h.Tenant}").SingleAsync();
        Assert.Contains("10.12345678",audit);Assert.Contains("11.24691356",audit);Assert.Contains("before",audit);
        command.Qty=2m;
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyCommandAsync(command))).Message);
        Assert.Equal(1,notifications);
    }
    [CurrentPermissionPostgresFact]
    public async Task Stock_original_move_preserves_two_leg_identity_and_replay_never_moves_again()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var mover=InventoryMover(h,owner);var command=StockMove();var result=await mover.MoveCommandAsync(command);
        Assert.Equal(result,await mover.MoveCommandAsync(command));Assert.NotEqual(result.OutTxnNo,result.InTxnNo);
        Assert.Equal(1,await StockNotifications(h));
        Assert.Equal(2,await h.Db.StockTransactions.CountAsync());Assert.Equal(10m,await h.Db.Stocks.Where(s=>s.LocationCd=="L1").Select(s=>s.PhysicalQty).SingleAsync());
        Assert.Equal(.12345678m,await h.Db.Stocks.Where(s=>s.LocationCd=="L3").Select(s=>s.PhysicalQty).SingleAsync());
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyCommandAsync(StockApply(command.OperationId)))).Message);
        Assert.Equal(result,(await mover.CloseOperationAsync(command.OperationId)).Result);
        await h.Db.Sys_RoleFieldPerms.AddAsync(new(){RoleId=72,ResourceKey="wms-stock",FieldName="UnitPrice",Access=3});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal(result,(await mover.ReadOperationAsync(command.OperationId)).Result); // references contain no protected price.
        Assert.DoesNotContain("UnitPrice",JsonSerializer.Serialize(result));
    }
    [CurrentPermissionPostgresFact]
    public async Task Stock_original_second_leg_failure_rolls_back_sequence_stock_transactions_and_original()
    {
        var failure=new FailSecondAdjustment{Enabled=true};await using var h=await Seed(failure);
        var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);var mover=InventoryMover(h,owner);var command=StockMove();
        await Assert.ThrowsAnyAsync<Exception>(()=>mover.MoveCommandAsync(command));Assert.False(failure.Enabled);
        Assert.Equal(0,await StockNotifications(h));
        Assert.Equal(0,await Operations(h));Assert.Equal(0,await h.Db.StockTransactions.CountAsync());Assert.Equal(0,await h.Db.WmsSequences.CountAsync());
        Assert.Equal(2,await h.Db.Stocks.CountAsync());Assert.Equal(10.12345678m,await h.Db.Stocks.Where(s=>s.ProductCd=="P1").Select(s=>s.PhysicalQty).SingleAsync());
        Assert.Equal("UNKNOWN",(await mover.ReadOperationAsync(command.OperationId)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task Stock_original_commit_ack_loss_read_only_recovery_and_write_revocation_do_not_repeat_effects()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var mover=InventoryMover(h,owner);var command=StockApply();fault.Armed=true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyCommandAsync(command))).Message);
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==403 && a.ActionCode=="adjust").ExecuteDeleteAsync();
        var revision=await h.Db.Database.SqlQueryRaw<long>("SELECT revision AS \"Value\" FROM cp6_platform_admin_guard_v1.root").SingleAsync();
        var original=await mover.ReadOperationAsync(command.OperationId);Assert.Equal("COMMITTED",original.State);
        Assert.Equal(h.User,(await mover.OperationScopeAsync()).ActorId);
        Assert.Equal(revision,await h.Db.Database.SqlQueryRaw<long>("SELECT revision AS \"Value\" FROM cp6_platform_admin_guard_v1.root").SingleAsync());
        Assert.Equal(original.Result,await mover.ApplyCommandAsync(command));
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyCommandAsync(StockApply()))).Message);
        Assert.Equal(1,await Operations(h));Assert.Equal(1,await h.Db.StockTransactions.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Stock_original_final_authority_failure_rolls_back_the_complete_operation()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var reads=0;hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject") && ++reads==2)throw new IOException("stock final authorization fault");return Task.CompletedTask;};
        await Assert.ThrowsAnyAsync<Exception>(()=>InventoryMover(h,owner).ApplyCommandAsync(StockApply()));hook.Before=null;
        Assert.Equal(0,await StockNotifications(h));
        Assert.Equal(2,reads);Assert.Equal(0,await Operations(h));Assert.Equal(0,await h.Db.StockTransactions.CountAsync());Assert.Equal(0,await h.Db.WmsSequences.CountAsync());
        Assert.Equal(10.12345678m,await h.Db.Stocks.Where(s=>s.ProductCd=="P1").Select(s=>s.PhysicalQty).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Stock_original_recovery_requires_each_current_effect_id_actor_and_tenant()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);var mover=InventoryMover(h,owner);
        var command=StockMove();await mover.MoveCommandAsync(command);
        await using var other=await Seed();var otherOwner=await GrantStocktakeDashboard(other);
        Assert.Equal("UNKNOWN",(await InventoryMover(other,otherOwner).ReadOperationAsync(command.OperationId)).State);
        var actor=new CP6.Entity.DomainModels.Sys.Sys_User{UserName="other-stock-reader",Password="unused",RoleId=72};h.Db.Sys_Users.Add(actor);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("ORIGINAL_OPERATION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>OriginalStockOwner(h,h.Db,actor.Id).ReadOperationAsync(command.OperationId))).Message);
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="wms-stock").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
        await h.Db.StockTransactions.Where(s=>s.Qty>0).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Creator,"other"));
        Assert.Equal("OBJECT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ReadOperationAsync(command.OperationId))).Message);
        await h.Db.StockTransactions.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Creator,"price-owner"));
        var stock=await h.Db.Stocks.AsNoTracking().SingleAsync(s=>s.LocationCd=="L3");
        await h.Db.Stocks.Where(s=>s.Id==stock.Id).ExecuteDeleteAsync();stock.Id=Guid.NewGuid();h.Db.Stocks.Add(stock);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("OBJECT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ReadOperationAsync(command.OperationId))).Message);
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==403 && a.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.CloseOperationAsync(command.OperationId))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Stock_original_concurrent_same_key_applies_once_unknown_close_prevents_late_application()
    {
        await using var h=await Seed();await GrantStocktakeDashboard(h);await SeedInventory(h);var command=StockApply();
        async Task<StockOperationResultV1> Execute()
        {await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});return await OriginalStockOwner(h,db).ApplyCommandAsync(command);}
        var results=await Task.WhenAll(Execute(),Execute());Assert.Equal(results[0],results[1]);Assert.Equal(1,await h.Db.StockTransactions.CountAsync());
        var mover=OriginalStockOwner(h,h.Db);var missing=StockApply();Assert.Equal("UNKNOWN",(await mover.ReadOperationAsync(missing.OperationId)).State);
        Assert.Equal("CLOSED",(await mover.CloseOperationAsync(missing.OperationId)).State);Assert.Equal("CLOSED",(await mover.CloseOperationAsync(missing.OperationId)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyCommandAsync(missing))).Message);
        Assert.Equal(2,await Operations(h));Assert.Equal(1,await h.Db.StockTransactions.CountAsync());
    }
    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Stock_original_close_and_execute_compete_on_the_same_actual_authority_root(bool closeFirst)
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var mover=InventoryMover(h,owner);var command=StockApply();Task? competing=null;var blocked=false;
        hook.After=async sql=>
        {
            if(!sql.CommandText.Contains("actor_id AS"))return;hook.After=null;
            using(ExecutionContext.SuppressFlow())competing=Task.Run(async()=>
            {
                await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var other=OriginalStockOwner(h,db);
                if(closeFirst)Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>other.ApplyCommandAsync(command))).Message);
                else Assert.Equal("COMMITTED",(await other.CloseOperationAsync(command.OperationId)).State);
            });
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                AND query LIKE '%cp6_authentication_v1.acquire()%') AS "Value"
                """).SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            Assert.False(competing.IsCompleted);blocked=true;
        };
        Exception? failure=null;
        try{if(closeFirst)await mover.CloseOperationAsync(command.OperationId);else await mover.ApplyCommandAsync(command);}
        catch(Exception error){failure=error;throw;}
        finally{if(competing!=null)try{await competing;}catch(Exception second){if(failure!=null)throw new AggregateException(failure,second);throw;}}
        Assert.True(blocked);Assert.Equal(1,await Operations(h));Assert.Equal(closeFirst?0:1,await h.Db.StockTransactions.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Stock_original_actual_http_csrf_replay_recovery_and_current_session_are_enforced()
    {
        await using var h=await Seed();await GrantStocktakeDashboard(h);await SeedInventory(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);var command=StockApply();
        const string root="api/wms/stock/";
        using(var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"commands/v1/apply",command,csrf:false)))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var missing=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"commands/v1/apply",StockApply(Guid.Empty))))Assert.Equal(HttpStatusCode.BadRequest,missing.StatusCode);
        using(var write=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"commands/v1/apply",command)))Assert.True(write.IsSuccessStatusCode,await OperationsFailure(write));
        var move=StockMove();using(var write=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"commands/v1/move",move)))Assert.True(write.IsSuccessStatusCode,await OperationsFailure(write));
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==403 && a.ActionCode!="query").ExecuteDeleteAsync();
        using(var replay=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"commands/v1/apply",command)))Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
        using(var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"commands/v1/move",StockMove())))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var read=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root+$"operations/v1/{command.OperationId}")))
        {Assert.Equal(HttpStatusCode.OK,read.StatusCode);Assert.True(read.Headers.CacheControl?.NoStore);Assert.Contains("COMMITTED",await read.Content.ReadAsStringAsync());}
        using(var scope=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root+"operations/v1/scope")))Assert.Equal(HttpStatusCode.OK,scope.StatusCode);
        using(var close=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+$"operations/v1/{Guid.NewGuid()}/close",csrf:false)))Assert.Equal(HttpStatusCode.Forbidden,close.StatusCode);
        await h.Db.Sys_Users.Where(u=>u.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.AuthenticationEpoch,Guid.NewGuid()));
        using(var revoked=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root+$"operations/v1/{command.OperationId}")))Assert.Equal(HttpStatusCode.Unauthorized,revoked.StatusCode);
        Assert.Equal(2,await Operations(h));Assert.Equal(3,await h.Db.StockTransactions.CountAsync());
    }
}
