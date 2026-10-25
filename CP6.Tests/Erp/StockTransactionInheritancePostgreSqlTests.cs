using System.Text.Json;
using System.Net;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Mes;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static Task<int> InheritedMappings(Harness h)=>h.Db.Database.SqlQuery<int>($"""
        SELECT count(*)::integer AS "Value" FROM cp6_operations_owner_v1.department_mappings
        WHERE tenant_id={h.Tenant} AND object_kind='stock-transaction'
        """).SingleAsync();

    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Stock_inheritance_department_adjustment_has_readable_original_and_exact_source_audit(int scope)
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var admin=await MappingAdmin(h);var stock=await h.Db.Stocks.SingleAsync(s=>s.ProductCd=="P1");h.Db.ChangeTracker.Clear();
        var binding=await BindStandalone(h,admin,"stock",stock.Id);await StandaloneScope(h,admin,scope);
        var mover=InventoryMover(h,owner);var command=StockApply();var result=await mover.ApplyCommandAsync(command);
        Assert.Equal(result,(await mover.ReadOperationAsync(command.OperationId)).Result);
        Assert.Equal(result,await mover.ApplyCommandAsync(command));
        var txn=Assert.Single((await new StockOperationsService(h.Db,owner).HistoryAsync(stock.Id)).Transactions);
        Assert.Equal(result.TxnNo,txn.TxnNo);Assert.Equal(1,await h.Db.StockTransactions.CountAsync());
        var audit=Assert.Single((await h.Db.Sys_SecurityLogs.Select(x=>x.ContextJson).ToArrayAsync())
            .Where(x=>x?.Contains("stock-transaction-inheritance.v1")==true));
        using var json=JsonDocument.Parse(audit!);var root=json.RootElement;
        Assert.Equal(stock.Id,root.GetProperty("stockId").GetGuid());
        Assert.Equal(binding.EntityId,root.GetProperty("sourceMappingId").GetGuid());
        Assert.Equal(txn.Id,root.GetProperty("transactionId").GetGuid());
        Assert.Equal(admin.Child,root.GetProperty("departmentId").GetGuid());
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==403 && a.ActionCode=="adjust").ExecuteDeleteAsync();
        Assert.Equal(result,(await mover.ReadOperationAsync(command.OperationId)).Result);
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==403 && a.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ReadOperationAsync(command.OperationId))).Message);
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Stock_inheritance_move_legs_use_each_actual_stock_department_and_recovery_requires_both(bool original)
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        h.Db.Stocks.Add(new(){WarehouseCd="W01",LocationCd="L3",ProductCd="P1",LotNo="LOT",UnitPrice=3m,Creator="price-owner"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var admin=await MappingAdmin(h);
        var from=await h.Db.Stocks.Where(s=>s.LocationCd=="L1").Select(s=>s.Id).SingleAsync();
        var to=await h.Db.Stocks.Where(s=>s.LocationCd=="L3").Select(s=>s.Id).SingleAsync();
        await BindStandalone(h,admin,"stock",from);await BindStandalone(h,admin,"stock",to,admin.Other);
        await StandaloneScope(h,admin,4);
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="wms-stock").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CustomDeptIds,admin.Child+","+admin.Other));
        var mover=InventoryMover(h,owner);var command=StockMove();
        if(original)await mover.MoveCommandAsync(command);else await mover.MoveAsync(command);
        Assert.Equal(2,await InheritedMappings(h));
        foreach(var txn in await h.Db.StockTransactions.AsNoTracking().ToArrayAsync())
        {
            var department=await h.Db.Database.SqlQuery<Guid>($"""
                SELECT department_id AS "Value" FROM cp6_operations_owner_v1.department_mappings
                WHERE tenant_id={h.Tenant} AND object_kind='stock-transaction' AND object_id={txn.Id}
                """).SingleAsync();
            Assert.Equal(txn.LocationCd.Trim()=="L1"?admin.Child:admin.Other,department);
        }
        if(original)
        {
            Assert.Equal("COMMITTED",(await mover.ReadOperationAsync(command.OperationId)).State);
            await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="wms-stock").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CustomDeptIds,admin.Child.ToString()));
            Assert.Equal("OBJECT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ReadOperationAsync(command.OperationId))).Message);
        }
    }

    [TenantManagementPostgresTheory]
    [InlineData("before")] [InlineData("after")] [InlineData("source-aba")] [InlineData("revoke")] [InlineData("replace-stock")]
    public async Task Stock_inheritance_does_not_absorb_other_changes_and_rolls_back_all_effects(string fault)
    {
        var hook=new CreationWriteHook();await using var h=await Seed(hook);var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var admin=await MappingAdmin(h);var source=await h.Db.Stocks.AsNoTracking().SingleAsync(s=>s.ProductCd=="P1");
        var binding=await BindStandalone(h,admin,"stock",source.Id);
        var other=await BindStandalone(h,admin,"stock",await h.Db.Stocks.Where(s=>s.ProductCd=="P2").Select(s=>s.Id).SingleAsync());
        await StandaloneScope(h,admin);var fired=false;
        hook.Before=async sql=>
        {
            var marker=fault=="before"?"T_StockTransaction":fault=="source-aba"?"INSERT INTO cp6_operations_owner_v1.department_mappings":"INSERT INTO cp6_main_owner_v1.operations";
            if(!sql.CommandText.Contains("INSERT INTO") || !sql.CommandText.Contains(marker))return;
            hook.Before=null;fired=true;
            if(fault=="revoke")await h.Db.Sys_RoleActions.Where(a=>a.MenuId==403 && a.ActionCode=="adjust").ExecuteDeleteAsync();
            else if(fault=="replace-stock")
            {
                await h.Db.Stocks.Where(s=>s.Id==source.Id).ExecuteDeleteAsync();
                var clone=new Stock{WarehouseCd=source.WarehouseCd,LocationCd=source.LocationCd,ProductCd=source.ProductCd,
                    LotNo=source.LotNo,PhysicalQty=source.PhysicalQty,AvailableQty=source.AvailableQty,Creator=source.Creator};
                h.Db.Stocks.Add(clone);await h.Db.SaveChangesAsync();
            }
            else
            {
                var id=fault=="source-aba"?binding.EntityId:other.EntityId;
                await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE id={id}");
                if(fault=="source-aba")await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=true WHERE id={id}");
            }
        };
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>InventoryMover(h,owner).ApplyCommandAsync(StockApply()));
        Assert.True(fired);Assert.Equal(0,await InheritedMappings(h));Assert.Equal(0,await Operations(h));Assert.Equal(0,await StockNotifications(h));
        Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
        Assert.Equal(source.PhysicalQty,await h.Db.Stocks.Where(s=>s.Id==source.Id).Select(s=>s.PhysicalQty).SingleAsync());
        Assert.DoesNotContain(await h.Db.Sys_SecurityLogs.Select(s=>s.ContextJson).ToArrayAsync(),s=>s?.Contains("stock-transaction-inheritance.v1")==true);
    }

    [CurrentPermissionPostgresFact]
    public async Task Stock_inheritance_second_leg_failure_removes_first_mapping_audit_original_and_outbox()
    {
        var failure=new FailSecondAdjustment();await using var h=await Seed(failure);var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var admin=await MappingAdmin(h);await BindStandalone(h,admin,"stock",await h.Db.Stocks.Where(s=>s.ProductCd=="P1").Select(s=>s.Id).SingleAsync());
        failure.Enabled=true;await Assert.ThrowsAnyAsync<Exception>(()=>InventoryMover(h,owner).MoveCommandAsync(StockMove()));
        Assert.False(failure.Enabled);Assert.Equal(0,await InheritedMappings(h));Assert.Equal(0,await Operations(h));Assert.Equal(0,await StockNotifications(h));
        Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());Assert.Equal(2,await h.Db.Stocks.CountAsync());
        Assert.DoesNotContain(await h.Db.Sys_SecurityLogs.Select(s=>s.ContextJson).ToArrayAsync(),s=>s?.Contains("stock-transaction-inheritance.v1")==true);
    }

    [CurrentPermissionPostgresFact]
    public async Task Stock_inheritance_commit_ack_loss_recovers_once_and_never_binds_recreated_business_keys()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var admin=await MappingAdmin(h);var source=await h.Db.Stocks.AsNoTracking().SingleAsync(s=>s.ProductCd=="P1");
        await BindStandalone(h,admin,"stock",source.Id);await StandaloneScope(h,admin);var mover=InventoryMover(h,owner);var command=StockApply();fault.Armed=true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyCommandAsync(command))).Message);
        var original=await mover.ReadOperationAsync(command.OperationId);Assert.Equal("COMMITTED",original.State);
        Assert.Equal(original.Result,await mover.ApplyCommandAsync(command));Assert.Equal(1,await InheritedMappings(h));
        await h.Db.Stocks.Where(s=>s.Id==source.Id).ExecuteDeleteAsync();source.Id=Guid.NewGuid();h.Db.Stocks.Add(source);
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();await BindStandalone(h,admin,"stock",source.Id);
        Assert.Equal("OBJECT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ReadOperationAsync(command.OperationId))).Message);
        Assert.Equal(1,await h.Db.StockTransactions.CountAsync());
    }

    [TenantManagementPostgresTheory]
    [InlineData(1,false)] [InlineData(5,false)] [InlineData(5,true)]
    public async Task Stock_inheritance_preserves_unbound_or_disabled_source_without_actor_department_guess(int scope,bool disabled)
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);var admin=await MappingAdmin(h);
        if(disabled)
        {
            var binding=await BindStandalone(h,admin,"stock",await h.Db.Stocks.Where(s=>s.ProductCd=="P1").Select(s=>s.Id).SingleAsync());
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE id={binding.EntityId}");
        }
        await StandaloneScope(h,admin,scope);var mover=InventoryMover(h,owner);var command=StockApply();var result=await mover.ApplyCommandAsync(command);
        Assert.Equal(result,(await mover.ReadOperationAsync(command.OperationId)).Result);
        await mover.MoveCommandAsync(StockMove()); // New target Stock remains a legal unbound creation for self/tenant scope.
        Assert.Equal(0,await InheritedMappings(h));Assert.Equal(3,await h.Db.StockTransactions.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Stock_inheritance_stocktake_parent_uses_exact_counted_stock_and_commits_complete_adjustments()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);var no=await CountInventory(h,owner);
        var admin=await MappingAdmin(h);foreach(var id in await h.Db.Stocks.Select(s=>s.Id).ToArrayAsync())await BindStandalone(h,admin,"stock",id);
        await BindStandalone(h,admin,"stocktake",await h.Db.StockTakes.Select(s=>s.Id).SingleAsync());await StandaloneScope(h,admin);
        await InventoryTake(h,owner).ApproveAndApplyAsync(no,null);Assert.Equal(2,await InheritedMappings(h));
        Assert.Equal(StockTakeStatus.Completed,(await InventoryTake(h,owner).GetAsync(no))!.Status);
        Assert.Equal(2,(await new StockOperationsService(h.Db,owner).TransactionsAsync(new())).Total);
    }

    [CurrentPermissionPostgresFact]
    public async Task Stock_inheritance_receipt_and_quality_parent_do_not_require_new_independent_stock_grants()
    {
        await using(var h=await Seed())
        {
            var owner=await GrantProductionInbound(h);await SeedInventory(h);var admin=await MappingAdmin(h);
            await BindStandalone(h,admin,"stock",await h.Db.Stocks.Where(s=>s.ProductCd=="P1").Select(s=>s.Id).SingleAsync());
            var req=ReceiptRequest();req.Details.RemoveAt(1);req.Details[0].LocationCd="L1";req.Details[0].LotNo="LOT";
            await Inbound(h,owner).ConfirmReceiptAsync(req,null);Assert.Equal(1,await InheritedMappings(h));
        }
        await using(var h=await Seed())
        {
            var owner=await GrantQuality(h);h.Db.Stocks.Add(new(){WarehouseCd="W01",LocationCd="L1",ProductCd="P1",LotNo=$"QC{DateTime.Today:yyyyMMdd}-001",PhysicalQty=4m,AvailableQty=4m});
            await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var admin=await MappingAdmin(h);
            await BindStandalone(h,admin,"stock",await h.Db.Stocks.Select(s=>s.Id).SingleAsync());
            var qc=InboundQuality(h,owner);var no=await qc.CreateDirectAsync(new(){Items=[new(){ProductCd="P1",ReceivedQty=2m,ExpectedQty=2m}]},null);
            await qc.SaveItemsAsync(no,[new(){LineNo=1,ReceivedQty=2m,AcceptedQty=2m}],null);
            var result=await qc.JudgeAsync(no,new(){FinalJudgement="PASS",AcceptWarehouseCd="W01",AcceptLocations=["L1"]},null);
            Assert.NotNull(result.GeneratedReceiptNo);Assert.Equal(1,await InheritedMappings(h));
            Assert.Equal(6m,await h.Db.Stocks.Select(s=>s.PhysicalQty).SingleAsync());
        }
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Stock_inheritance_outbound_reservation_ship_and_cancel_keep_same_stock_provenance(bool cancel)
    {
        await using var h=await Seed();var owner=await GrantOperations(h);var wms=OperationsWms(h,owner);
        h.Db.Stocks.Add(new(){WarehouseCd="W01",LocationCd="L",ProductCd="P",LotNo="",PhysicalQty=10m,AvailableQty=10m,UnitPrice=2m});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var admin=await MappingAdmin(h);
        await BindStandalone(h,admin,"stock",await h.Db.Stocks.Select(s=>s.Id).SingleAsync());
        var no=await wms.CreateOrderAsync(NewOutbound(),null);await wms.ConfirmOrderAsync(no,null);await wms.AllocateAsync(no,null);
        if(cancel)await wms.CancelOrderAsync(no,null);else {await wms.StartPickingAsync(no,null);await wms.ShipAsync(no,new(),null);}
        Assert.Equal(2,await InheritedMappings(h));Assert.Equal(2,await h.Db.StockTransactions.CountAsync());
        Assert.Equal(0m,await h.Db.Stocks.Select(s=>s.AllocatedQty).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Stock_inheritance_completed_production_receipt_and_backflush_use_their_actual_stock()
    {
        await using var h=await Seed();var owner=await GrantProductionInbound(h);var no=await SeedProduction(h);var wms=Inbound(h,owner);
        var product="F"+h.Tenant.ToString("N")[..12];await h.Db.WorkOrders.ExecuteUpdateAsync(s=>s.SetProperty(w=>w.ProductCd,product));
        h.Db.Set<ProductMaster>().Add(new(){ProductCd=product});
        h.Db.Set<ProductMaterial>().Add(new(){ProductCd=product,ProcessCd="P",MaterialCd="RAW",UsageType=2,UnitUsage=2m,MaterialTypeDiv="3"});
        h.Db.Stocks.AddRange(new Stock{WarehouseCd="W01",LocationCd="W01-FG",ProductCd=product,LotNo=no},
            new Stock{WarehouseCd="W01",LocationCd="W01-RM",ProductCd="RAW",LotNo="RAWLOT",PhysicalQty=10m,AvailableQty=10m});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var admin=await MappingAdmin(h);
        foreach(var id in await h.Db.Stocks.Select(s=>s.Id).ToArrayAsync())await BindStandalone(h,admin,"stock",id);
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==405 || a.MenuId==406 || a.MenuId==304 && a.ActionCode=="backflush").ExecuteDeleteAsync();
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync();
        var seq=new WmsSequenceService(h.Db);var backflush=new BackflushService(h.Db,new StockMovementService(h.Db,seq),new MaterialUsageCalculator(),owner:owner);
        var bridge=new WmsBridgeHook(h.Db,null!,wms,NullLogger<WmsBridgeHook>.Instance);var mes=Production(h,owner,bridge,backflush);
        var req=new ProductionResultRequest{WorkOrderNo=no,ProcessCd="P",OperatorCd="worker"};await mes.StartAsync(req,null);req.GoodQty=3m;await mes.CompleteAsync(req,null);
        Assert.Equal(2,await InheritedMappings(h));Assert.Equal(2,await h.Db.StockTransactions.CountAsync());
        Assert.Equal(4m,await h.Db.Stocks.Where(s=>s.ProductCd=="RAW").Select(s=>s.PhysicalQty).SingleAsync());
        Assert.Equal(6m,await h.Db.WorkOrderMaterials.Select(s=>s.ActualQty).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Stock_inheritance_actual_HTTP_department_recovery_and_revocation()
    {
        await using var h=await Seed();await GrantStocktakeDashboard(h);await SeedInventory(h);var admin=await MappingAdmin(h);
        await BindStandalone(h,admin,"stock",await h.Db.Stocks.Where(s=>s.ProductCd=="P1").Select(s=>s.Id).SingleAsync());await StandaloneScope(h,admin);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);var command=StockApply();
        const string root="api/wms/stock/";
        using(var write=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"commands/v1/apply",command)))Assert.True(write.IsSuccessStatusCode,await OperationsFailure(write));
        using(var read=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root+$"operations/v1/{command.OperationId}")))
        {Assert.Equal(HttpStatusCode.OK,read.StatusCode);Assert.True(read.Headers.CacheControl?.NoStore);Assert.Contains("COMMITTED",await read.Content.ReadAsStringAsync());}
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==403 && a.ActionCode=="query").ExecuteDeleteAsync();
        using(var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root+$"operations/v1/{command.OperationId}")))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        Assert.Equal(1,await h.Db.StockTransactions.CountAsync());Assert.Equal(1,await InheritedMappings(h));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Stock_inheritance_order_cancel_preserves_child_savepoint_rollback_and_retry(bool failChild)
    {
        var failure=new FailSecondCancelRelease();await using var h=await Seed(failure);await GrantOrderCancel(h);await SeedCancelGraph(h);
        var admin=await MappingAdmin(h);foreach(var id in await h.Db.Stocks.Select(s=>s.Id).ToArrayAsync())await BindStandalone(h,admin,"stock",id);
        failure.Enabled=failChild;var service=CancelService(h);await service.CancelAsync(h.Number,"reason",true,null);
        Assert.False(failure.Enabled);Assert.Equal(failChild?0:2,await InheritedMappings(h));
        Assert.Equal(failChild?0:2,await h.Db.StockTransactions.CountAsync());
        Assert.Equal(failChild?0:2,(await h.Db.Sys_SecurityLogs.Select(s=>s.ContextJson).ToArrayAsync()).Count(s=>s?.Contains("stock-transaction-inheritance.v1")==true));
        if(failChild)
        {
            Assert.All(await h.Db.Stocks.AsNoTracking().ToArrayAsync(),s=>Assert.Equal(2m,s.AllocatedQty));
            var result=await service.CancelAsync(h.Number,"retry remaining",true,null);
            Assert.Equal(CP6.Entity.DTOs.Erp.CancelOutcome.Cancelled,result.Outcome);
            Assert.Equal(2,await InheritedMappings(h));Assert.Equal(2,await h.Db.StockTransactions.CountAsync());
        }
    }
}
