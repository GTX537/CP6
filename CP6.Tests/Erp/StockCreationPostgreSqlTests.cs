using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration.StockOutbox;
using CP6.Entity.DomainModels.Wms;
using CP6.Platform.EntityFramework.OutboxV2;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<StockApplyCommandV2> NewStockCommand(StockMovementService mover,Guid? department=null)
        =>new(){OperationId=Guid.NewGuid(),TxnType="ADJ",WarehouseCd="W01",LocationCd="L3",ProductCd="NEW",LotNo="LOT",
            Qty=1.12345678m,UnitCd="EA",OperatorCd="forged",
            NewStock=new((await mover.CreationContextAsync("adjust",new("W01","L3","NEW","LOT"))).AuthorityVersion,department)};
    private static Task<int> NewStockMappings(Harness h)=>h.Db.Database.SqlQuery<int>($"""
        SELECT count(*)::integer AS "Value" FROM cp6_operations_owner_v1.department_mappings
        WHERE tenant_id={h.Tenant} AND object_kind='stock'
        """).SingleAsync();
    [CurrentPermissionPostgresFact]
    public async Task Stock_creation_actual_HTTP_adjustment_and_new_move_target_are_department_readable()
    {
        await using var h=await Seed();await GrantStocktakeDashboard(h);await SeedInventory(h);
        var admin=await MappingAdmin(h);await StandaloneScope(h,admin);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies=await Login(host,h);const string root="api/wms/stock/";
        using var context=await host.Send(OperationsRequest(cookies,HttpMethod.Get,
            root+"creation-context/adjust?warehouseCd=W01&locationCd=L3&productCd=NEW&lotNo=LOT"));
        Assert.Equal(HttpStatusCode.OK,context.StatusCode);
        var data=(await context.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.True(data.GetProperty("requiresCreation").GetBoolean());
        Assert.False(data.GetProperty("allowsUnbound").GetBoolean());
        Assert.Equal(admin.Child,Assert.Single(data.GetProperty("departments").EnumerateArray()).GetProperty("id").GetGuid());
        var id=Guid.NewGuid();
        var input=new{operationId=id,txnType="ADJ",warehouseCd="W01",locationCd="L3",productCd="NEW",lotNo="LOT",qty="9007199254740.12345678",
            newStock=new{departmentId=admin.Child,expectedAuthorityVersion=data.GetProperty("authorityVersion").GetString()}};
        using var created=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"commands/v2/apply",input));
        Assert.Equal(HttpStatusCode.OK,created.StatusCode);
        var result=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        using var replay=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"commands/v2/apply",input));
        Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
        Assert.Equal(result.GetRawText(),(await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetRawText());
        Assert.Equal(9007199254740.12345678m,await h.Db.Stocks.Where(s=>s.ProductCd=="NEW").Select(s=>s.PhysicalQty).SingleAsync());
        using var moveContext=await host.Send(OperationsRequest(cookies,HttpMethod.Get,
            root+"creation-context/move?warehouseCd=W01&locationCd=L1&productCd=NEW&lotNo=LOT"));
        Assert.Equal(HttpStatusCode.OK,moveContext.StatusCode);
        var moveData=(await moveContext.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");var moveId=Guid.NewGuid();
        using var moved=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"commands/v2/move",
            new{operationId=moveId,warehouseCd="W01",fromLocationCd="L3",toLocationCd="L1",productCd="NEW",lotNo="LOT",qty="0.12345678",
                newStock=new{departmentId=admin.Child,expectedAuthorityVersion=moveData.GetProperty("authorityVersion").GetString()}}));
        Assert.Equal(HttpStatusCode.OK,moved.StatusCode);
        using var recovered=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root+$"operations/v1/{moveId}"));
        Assert.Equal(HttpStatusCode.OK,recovered.StatusCode);
        Assert.Equal("COMMITTED",(await recovered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("state").GetString());
        Assert.Equal(3,await InheritedMappings(h));Assert.Equal(2,await Operations(h));Assert.Equal(2,await StockNotifications(h));
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==403 && a.ActionCode=="query").ExecuteDeleteAsync();
        using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root+$"operations/v1/{moveId}"));
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }

    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Stock_creation_explicit_healthy_department_is_required_and_original_inherits_exact_source(int scope)
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var admin=await MappingAdmin(h);await StandaloneScope(h,admin,scope);var mover=InventoryMover(h,owner);
        var command=await NewStockCommand(mover,admin.Child);var intent=command.NewStock!;
        command.NewStock=null;
        Assert.Equal("STOCK_CREATION_INTENT_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyCommandAsync(command))).Message);
        command.NewStock=intent with{DepartmentId=null};
        Assert.Equal("CREATION_DEPARTMENT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyCommandAsync(command))).Message);
        await using var foreign=await Seed();var foreignAdmin=await MappingAdmin(foreign);
        command.NewStock=intent with{DepartmentId=foreignAdmin.Child};
        Assert.Equal("CREATION_DEPARTMENT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyCommandAsync(command))).Message);
        command.NewStock=intent;var result=await mover.ApplyCommandAsync(command);
        Assert.Equal(result,(await mover.ReadOperationAsync(command.OperationId)).Result);
        var stock=await h.Db.Stocks.AsNoTracking().SingleAsync(s=>s.ProductCd=="NEW");Assert.Equal("price-owner",stock.Creator);
        Assert.Equal(1.12345678m,stock.PhysicalQty);Assert.Equal(stock.PhysicalQty,stock.AvailableQty);
        var audit=Assert.Single((await h.Db.Sys_SecurityLogs.Select(s=>s.ContextJson).ToArrayAsync())
            .Where(s=>s?.Contains("stock-transaction-inheritance.v1")==true));
        Assert.Equal(stock.Id,JsonDocument.Parse(audit!).RootElement.GetProperty("stockId").GetGuid());
        Assert.Equal(1,await NewStockMappings(h));Assert.Equal(1,await InheritedMappings(h));Assert.Equal(1,await StockNotifications(h));
        Assert.Equal(stock.Id,Assert.Single((await new StockOperationsService(h.Db,owner).SearchAsync(new())).Items).Id);
    }

    [TenantManagementPostgresTheory]
    [InlineData(1)] [InlineData(5)]
    public async Task Stock_creation_self_and_tenant_keep_legal_unbound_and_v1_original_compatibility(int scope)
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var admin=await MappingAdmin(h);await StandaloneScope(h,admin,scope);var mover=InventoryMover(h,owner);
        var command=await NewStockCommand(mover);var context=await mover.CreationContextAsync("adjust",new("W01","L3","NEW","LOT"));
        Assert.True(context.AllowsUnbound);
        if(scope==1)
        {
            Assert.Empty(context.Departments);command.NewStock=command.NewStock! with{DepartmentId=admin.Child};
            Assert.Equal("CREATION_DEPARTMENT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyCommandAsync(command))).Message);
            command.NewStock=command.NewStock with{DepartmentId=null};
        }
        var created=await mover.ApplyCommandAsync(command);Assert.Equal(created,(await mover.ReadOperationAsync(command.OperationId)).Result);
        var old=StockApply();var original=await mover.ApplyCommandAsync(old);Assert.Equal(original,await mover.ApplyCommandAsync(old));
        Assert.Equal(0,await NewStockMappings(h));Assert.Equal(0,await InheritedMappings(h));
        var v1SameId=StockApply(command.OperationId);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyCommandAsync(v1SameId))).Message);
        command.OperationId=old.OperationId;
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyCommandAsync(command))).Message);
        Assert.Equal(2,await Operations(h));Assert.Equal(2,await StockNotifications(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Stock_creation_stale_or_disabled_department_cannot_create_or_rebind_an_existing_key()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var admin=await MappingAdmin(h);await StandaloneScope(h,admin,3);var mover=InventoryMover(h,owner);
        var command=await NewStockCommand(mover,admin.Child);
        await h.Db.Sys_Depts.Where(d=>d.Id==admin.Child).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Enable,false));
        Assert.Equal("CREATION_AUTHORITY_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyCommandAsync(command))).Message);
        command.NewStock=new((await mover.CreationContextAsync("adjust",new("W01","L3","NEW","LOT"))).AuthorityVersion,admin.Child);
        Assert.Equal("CREATION_DEPARTMENT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyCommandAsync(command))).Message);
        await h.Db.Sys_Depts.Where(d=>d.Id==admin.Child).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Enable,true));
        command=await NewStockCommand(mover,admin.Child);await mover.ApplyCommandAsync(command);command.OperationId=Guid.NewGuid();
        Assert.Equal("STOCK_ALREADY_EXISTS",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyCommandAsync(command))).Message);
        command.NewStock=null;await mover.ApplyCommandAsync(command);
        Assert.Equal(2.24691356m,await h.Db.Stocks.Where(s=>s.ProductCd=="NEW").Select(s=>s.PhysicalQty).SingleAsync());
        await h.Db.Stocks.Where(s=>s.ProductCd=="NEW").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsDeleted,true));
        command.OperationId=Guid.NewGuid();
        Assert.Equal("OBJECT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyCommandAsync(command))).Message);
        Assert.Equal(1,await NewStockMappings(h));Assert.Equal(2,await Operations(h));
    }

    [TenantManagementPostgresTheory]
    [InlineData("price")] [InlineData("area")] [InlineData("negative")] [InlineData("frozen")] [InlineData("override")]
    public async Task Stock_creation_preserves_field_warehouse_quantity_freeze_and_internal_capability_gates(string gate)
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        if(gate=="frozen")
        {
            await h.Db.Stocks.Where(s=>s.ProductCd=="P2").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.LocationCd,"L3"));
            await CountInventory(h,owner);
        }
        var sequenceCount=await h.Db.WmsSequences.CountAsync();
        var admin=await MappingAdmin(h);await StandaloneScope(h,admin);var mover=InventoryMover(h,owner);
        var command=await NewStockCommand(mover,admin.Child);
        if(gate=="price")h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-stock",FieldName="UnitPrice",Access=3});
        if(gate=="area") {await h.Db.WmsRoleScopes.ExecuteDeleteAsync();h.Db.WmsRoleScopes.Add(new(){RoleId=72,WarehouseCd="OTHER"});}
        if(gate=="negative")command.Qty=-1m;
        if(gate=="override")command.AllowNegativeOverride=true;
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await Assert.ThrowsAnyAsync<Exception>(()=>mover.ApplyCommandAsync(command));
        Assert.Equal(2,await h.Db.Stocks.CountAsync());Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
        Assert.Equal(0,await NewStockMappings(h));Assert.Equal(0,await InheritedMappings(h));Assert.Equal(0,await Operations(h));
        Assert.Equal(0,await StockNotifications(h));Assert.Equal(sequenceCount,await h.Db.WmsSequences.CountAsync());
    }

    [TenantManagementPostgresTheory]
    [InlineData("before")] [InlineData("after")] [InlineData("own-aba")] [InlineData("revoke")] [InlineData("replace-key")]
    public async Task Stock_creation_does_not_absorb_other_writes_and_final_failures_roll_back_all_effects(string fault)
    {
        var hook=new CreationWriteHook();await using var h=await Seed(hook);var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var admin=await MappingAdmin(h);var existing=await BindStandalone(h,admin,"stock",await h.Db.Stocks.Where(s=>s.ProductCd=="P1").Select(s=>s.Id).SingleAsync());
        await StandaloneScope(h,admin);var mover=InventoryMover(h,owner);var command=await NewStockCommand(mover,admin.Child);var fired=false;
        hook.Before=async sql=>
        {
            if(!sql.CommandText.Contains("INSERT INTO") || !sql.CommandText.Contains(fault=="before"?"T_Stock":"cp6_main_owner_v1.operations"))return;
            hook.Before=null;fired=true;
            if(fault=="revoke")await h.Db.Sys_RoleActions.Where(a=>a.MenuId==403 && a.ActionCode=="adjust").ExecuteDeleteAsync();
            else if(fault=="replace-key")await h.Db.Stocks.Where(s=>s.ProductCd=="NEW").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ProductCd,"REPLACED"));
            else if(fault=="own-aba")
            {
                await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant} AND object_kind='stock'");
                await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=true WHERE tenant_id={h.Tenant} AND object_kind='stock'");
            }
            else await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE id={existing.EntityId}");
        };
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyCommandAsync(command));Assert.True(fired);
        Assert.Equal(2,await h.Db.Stocks.CountAsync());Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
        Assert.Equal(1,await NewStockMappings(h));Assert.Equal(0,await InheritedMappings(h));Assert.Equal(0,await Operations(h));
        Assert.Equal(0,await StockNotifications(h));Assert.Equal(0,await h.Db.WmsSequences.CountAsync());
        Assert.DoesNotContain(await h.Db.Sys_SecurityLogs.Select(s=>s.ContextJson).ToArrayAsync(),s=>s?.Contains("stock-creation.v2")==true);
    }

    [CurrentPermissionPostgresFact]
    public async Task Stock_creation_move_preserves_custody_and_each_leg_department_then_dispatches_v2_original()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedMoveCustody(h);
        var admin=await MappingAdmin(h);await BindStandalone(h,admin,"stock",await h.Db.Stocks.Where(s=>s.ProductCd=="P1").Select(s=>s.Id).SingleAsync());
        await StandaloneScope(h,admin,4);
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="wms-stock").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CustomDeptIds,admin.Child+","+admin.Other));
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-stock",FieldName="UnitPrice",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var mover=InventoryMover(h,owner);var context=await mover.CreationContextAsync("move",new("W01","L3","P1","LOT"));
        var command=new StockMoveCommandV2{OperationId=Guid.NewGuid(),WarehouseCd="W01",FromLocationCd="L1",ToLocationCd="L3",
            ProductCd="P1",LotNo="LOT",Qty=.12345678m,NewStock=new(context.AuthorityVersion,admin.Other)};
        var result=await mover.MoveCommandAsync(command);Assert.Equal(result,(await mover.ReadOperationAsync(command.OperationId)).Result);
        AssertMoveCustody(await h.Db.Stocks.AsNoTracking().SingleAsync(s=>s.LocationCd=="L3"));
        foreach(var txn in await h.Db.StockTransactions.AsNoTracking().ToArrayAsync())
        {
            var department=await h.Db.Database.SqlQuery<Guid>($"""
                SELECT department_id AS "Value" FROM cp6_operations_owner_v1.department_mappings
                WHERE tenant_id={h.Tenant} AND object_kind='stock-transaction' AND object_id={txn.Id}
                """).SingleAsync();
            Assert.Equal(txn.Qty<0?admin.Child:admin.Other,department);
        }
        var publisher=new StockPublisher(OutboxTransportDecisionV2.Confirmed);
        Assert.Equal(1,(await new StockOutboxWorkerV1(h.Db,StockHost(),publisher).RunAsync(h.Tenant,default)).Completed);
        Assert.Equal("{}"u8.ToArray(),Assert.Single(publisher.Messages).Payload.ToArray());
        Assert.Equal(result,await mover.MoveCommandAsync(command));Assert.Single(publisher.Messages);
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="wms-stock").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CustomDeptIds,admin.Child.ToString()));
        Assert.Equal("OBJECT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ReadOperationAsync(command.OperationId))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Stock_creation_move_second_leg_failure_removes_new_stock_both_mappings_sequence_and_original()
    {
        var failure=new FailSecondAdjustment();await using var h=await Seed(failure);var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var admin=await MappingAdmin(h);await BindStandalone(h,admin,"stock",await h.Db.Stocks.Where(s=>s.ProductCd=="P1").Select(s=>s.Id).SingleAsync());
        await StandaloneScope(h,admin);var mover=InventoryMover(h,owner);
        var context=await mover.CreationContextAsync("move",new("W01","L3","P1","LOT"));
        var command=new StockMoveCommandV2{OperationId=Guid.NewGuid(),WarehouseCd="W01",FromLocationCd="L1",ToLocationCd="L3",
            ProductCd="P1",LotNo="LOT",Qty=1m,NewStock=new(context.AuthorityVersion,admin.Child)};failure.Enabled=true;
        await Assert.ThrowsAnyAsync<Exception>(()=>mover.MoveCommandAsync(command));Assert.False(failure.Enabled);
        Assert.Equal(2,await h.Db.Stocks.CountAsync());Assert.Equal(10.12345678m,await h.Db.Stocks.Where(s=>s.ProductCd=="P1").Select(s=>s.PhysicalQty).SingleAsync());
        Assert.Equal(1,await NewStockMappings(h));Assert.Equal(0,await InheritedMappings(h));Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
        Assert.Equal(0,await h.Db.WmsSequences.CountAsync());Assert.Equal(0,await Operations(h));Assert.Equal(0,await StockNotifications(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Stock_creation_ack_loss_keeps_one_original_and_requires_current_actor_tenant_and_exact_stock()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var admin=await MappingAdmin(h);await StandaloneScope(h,admin);var mover=InventoryMover(h,owner);
        var command=await NewStockCommand(mover,admin.Child);fault.Armed=true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyCommandAsync(command))).Message);
        var receipt=await mover.ReadOperationAsync(command.OperationId);Assert.Equal("COMMITTED",receipt.State);
        Assert.Equal(receipt.Result,await mover.ApplyCommandAsync(command));command.Qty=2m;
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyCommandAsync(command))).Message);
        command.Qty=1.12345678m;await h.Db.Sys_RoleActions.Where(a=>a.MenuId==403 && a.ActionCode=="adjust").ExecuteDeleteAsync();
        Assert.Equal(receipt.Result,await mover.ApplyCommandAsync(command));
        await using var other=await Seed();var otherOwner=await GrantStocktakeDashboard(other);
        Assert.Equal("UNKNOWN",(await InventoryMover(other,otherOwner).ReadOperationAsync(command.OperationId)).State);
        var actor=new CP6.Entity.DomainModels.Sys.Sys_User{UserName="other-creator",Password="unused",RoleId=72,DeptId=admin.Child};h.Db.Sys_Users.Add(actor);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("ORIGINAL_OPERATION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>OriginalStockOwner(h,h.Db,actor.Id).ReadOperationAsync(command.OperationId))).Message);
        var id=await h.Db.Stocks.Where(s=>s.ProductCd=="NEW").Select(s=>s.Id).SingleAsync();
        await h.Db.Stocks.Where(s=>s.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Id,Guid.NewGuid()));
        Assert.Equal("OBJECT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ReadOperationAsync(command.OperationId))).Message);
        Assert.Equal(1,await Operations(h));Assert.Equal(1,await StockNotifications(h));Assert.Equal(1,await h.Db.StockTransactions.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Stock_creation_concurrent_same_key_is_single_effect_and_closed_unknown_blocks_v2()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);var admin=await MappingAdmin(h);
        await StandaloneScope(h,admin);var mover=InventoryMover(h,owner);var command=await NewStockCommand(mover,admin.Child);
        async Task<StockOperationResultV1> Create()
        {await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});return await OriginalStockOwner(h,db).ApplyCommandAsync(command);}
        var results=await Task.WhenAll(Create(),Create());Assert.Equal(results[0],results[1]);
        Assert.Equal(3,await h.Db.Stocks.CountAsync());Assert.Equal(1,await h.Db.StockTransactions.CountAsync());Assert.Equal(1,await StockNotifications(h));
        command.OperationId=Guid.NewGuid();Assert.Equal("UNKNOWN",(await mover.ReadOperationAsync(command.OperationId)).State);
        Assert.Equal("CLOSED",(await mover.CloseOperationAsync(command.OperationId)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ApplyCommandAsync(command))).Message);
        Assert.Equal(1,await NewStockMappings(h));Assert.Equal(1,await InheritedMappings(h));Assert.Equal(2,await Operations(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Stock_creation_first_adjustment_does_not_require_query_but_recovery_does()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);var admin=await MappingAdmin(h);
        await StandaloneScope(h,admin);await h.Db.Sys_RoleActions.Where(a=>a.MenuId==403 && a.ActionCode=="query").ExecuteDeleteAsync();
        var mover=InventoryMover(h,owner);var command=await NewStockCommand(mover,admin.Child);await mover.ApplyCommandAsync(command);
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mover.ReadOperationAsync(command.OperationId))).Message);
        Assert.Equal(1,await StockNotifications(h));Assert.Equal(1,await NewStockMappings(h));
    }
}
