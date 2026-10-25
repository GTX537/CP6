using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Http;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Stocktake_creation_actual_HTTP_explicit_department_and_original_recovery()
    {
        await using var h=await Seed();await GrantStocktakeDashboard(h);await SeedInventory(h);var admin=await MappingAdmin(h);
        foreach(var stockId in await h.Db.Stocks.Select(s=>s.Id).ToArrayAsync())await BindStandalone(h,admin,"stock",stockId);
        await StandaloneScope(h,admin);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies=await Login(host,h);
        using var context=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/stock-take/creation-context"));
        Assert.Equal(HttpStatusCode.OK,context.StatusCode);
        var data=(await context.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.False(data.GetProperty("allowsUnbound").GetBoolean());
        Assert.Equal(admin.Child,Assert.Single(data.GetProperty("departments").EnumerateArray()).GetProperty("id").GetGuid());
        var operation=Guid.NewGuid();
        var input=new{operationId=operation,expectedAuthorityVersion=data.GetProperty("authorityVersion").GetString(),
            departmentId=admin.Child,plan=new{targetWarehouseCd="W01",plannedDate="2026-10-25",stockTakeType=1}};
        using var created=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/stock-take/operations",input));
        Assert.Equal(HttpStatusCode.OK,created.StatusCode);
        var original=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var id=original.GetProperty("stockTakeId").GetGuid();
        Assert.Equal(h.Tenant,await h.Db.StockTakes.Where(s=>s.Id==id).Select(s=>s.TenantId).SingleAsync());
        Assert.Equal(2,await h.Db.StockTakeDetails.CountAsync());
        using var repeated=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/stock-take/operations",input));
        Assert.Equal(HttpStatusCode.OK,repeated.StatusCode);
        Assert.Equal(original.GetRawText(),(await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetRawText());
        using var recovered=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"api/wms/stock-take/operations/{operation}"));
        Assert.Equal(HttpStatusCode.OK,recovered.StatusCode);
        var receipt=(await recovered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal("COMMITTED",receipt.GetProperty("status").GetString());
        Assert.Equal(original.GetRawText(),receipt.GetProperty("result").GetRawText());
        Assert.Single(await h.Db.StockTakes.ToArrayAsync());
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==415 && a.ActionCode=="query").ExecuteDeleteAsync();
        using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"api/wms/stock-take/operations/{operation}"));
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }

    private static StockTakeService StocktakeCreationService(Harness h,CP6Context db,Guid? actor=null)
    {
        var catalog=new AuthorizationCatalogStoreV1();
        catalog.RegisterFields("wms-stocktake",[new("UnitPrice","UnitPrice"),new("Amount","Amount")]);
        catalog.RegisterFields("wms-stock",[new("UnitPrice","UnitPrice")]);
        foreach(var resource in new[]{"wms-stock","wms-stocktake"})catalog.RegisterScope(resource,resource,[1,2,3,4,5],1);
        catalog.Seal();var subject=actor??h.User;
        var owner=new OperationsOwnerV1(db,new OperationsHttp{HttpContext=actor==null?h.Http:new DefaultHttpContext
            {User=CurrentPermissionContextTests.Principal(subject,h.Tenant)}},_=>Task.FromResult(subject),catalog.Capture);
        var seq=new WmsSequenceService(db);return new(db,seq,new StockMovementService(db,seq,owner:owner),owner);
    }
    private static async Task<StockTakeCreationCommandV1> StocktakeCommand(StockTakeService service,Guid? department=null)
        =>new(){OperationId=Guid.NewGuid(),DepartmentId=department,ExpectedAuthorityVersion=(await service.CreationContextAsync()).AuthorityVersion,
            Plan=new(){TargetWarehouseCd="W01",PlannedDate=new DateTime(2026,10,25),ApprovalThresholdAmount=1m}};
    private static async Task BindInventoryForCreation(Harness h,MappingHarness admin)
    {
        foreach(var id in await h.Db.Stocks.Select(s=>s.Id).ToArrayAsync())await BindStandalone(h,admin,"stock",id);
    }
    private static Task<int> StocktakeMappings(Harness h)=>h.Db.Database.SqlQuery<int>($"""
        SELECT count(*)::integer AS "Value" FROM cp6_operations_owner_v1.department_mappings
        WHERE tenant_id={h.Tenant} AND object_kind='stocktake'
        """).SingleAsync();

    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Stocktake_creation_requires_explicit_department_and_retains_complete_inventory_scope(int scope)
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);var admin=await MappingAdmin(h);
        await BindInventoryForCreation(h,admin);await StandaloneScope(h,admin,scope);var service=InventoryTake(h,owner);
        var command=await StocktakeCommand(service,admin.Child);
        Assert.Equal("CREATION_DEPARTMENT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>
            service.CreateCommandAsync(command with{DepartmentId=null}))).Message);
        await using var foreign=await Seed();var foreignAdmin=await MappingAdmin(foreign);
        Assert.Equal("CREATION_DEPARTMENT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>
            service.CreateCommandAsync(command with{DepartmentId=foreignAdmin.Child}))).Message);
        Assert.Empty(await h.Db.StockTakes.ToArrayAsync());Assert.Equal(0,await StocktakeMappings(h));
        var saved=await service.CreateCommandAsync(command);
        Assert.Equal(admin.Child,saved.DepartmentId);Assert.Equal(1,await StocktakeMappings(h));
        Assert.Equal(saved.StockTakeId,Assert.Single(await service.SearchAsync(new())).Id);
        Assert.Equal(new[]{10.12345678m,20m},(await service.GetAsync(saved.StockTakeNo))!.Details.Select(s=>s.BookQty));
        await service.StartCountAsync(saved.StockTakeNo,null);await service.UpdateCountsAsync(saved.StockTakeNo,
            [new(){LineNo=1,CountedQty=9m,DiffReasonCd="COUNT"},new(){LineNo=2,CountedQty=19m,DiffReasonCd="COUNT"}],null);
        await service.SubmitForReviewAsync(saved.StockTakeNo,null);await service.ApproveAndApplyAsync(saved.StockTakeNo,null);
        Assert.Equal(4,(await service.GetAsync(saved.StockTakeNo))!.Status);
        Assert.Equal(saved,(await service.ReadCreationAsync(command.OperationId)).Result);
        Assert.Equal(2,await h.Db.StockTransactions.CountAsync());
    }

    [TenantManagementPostgresTheory]
    [InlineData(1)] [InlineData(5)]
    public async Task Stocktake_creation_preserves_unbound_self_and_tenant_paths_without_arbitrary_assignment(int scope)
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);var admin=await MappingAdmin(h);
        await StandaloneScope(h,admin,scope);var service=InventoryTake(h,owner);
        var context=await service.CreationContextAsync();Assert.True(context.AllowsUnbound);
        if(scope==1)Assert.Empty(context.Departments);
        var result=await service.CreateCommandAsync(await StocktakeCommand(service));Assert.Null(result.DepartmentId);
        await service.CreatePlanAsync(new(){TargetWarehouseCd="W01"},"forged");
        Assert.Equal(2,await h.Db.StockTakes.CountAsync());Assert.Equal(0,await StocktakeMappings(h));
        Assert.All(await h.Db.StockTakes.Select(s=>s.Creator).ToArrayAsync(),s=>Assert.Equal("price-owner",s));
    }

    [CurrentPermissionPostgresFact]
    public async Task Stocktake_creation_stale_disabled_department_and_partial_inventory_are_not_accepted()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);var admin=await MappingAdmin(h);
        await BindStandalone(h,admin,"stock",await h.Db.Stocks.Where(s=>s.ProductCd=="P1").Select(s=>s.Id).SingleAsync());
        await StandaloneScope(h,admin,3);var service=InventoryTake(h,owner);var command=await StocktakeCommand(service,admin.Child);
        Assert.Equal("OBJECT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(command))).Message);
        Assert.Empty(await h.Db.StockTakes.ToArrayAsync());Assert.Empty(await h.Db.StockTakeDetails.ToArrayAsync());
        command.Plan.TargetProductCd="P1";var valid=await service.CreateCommandAsync(command);Assert.Single((await service.GetAsync(valid.StockTakeNo))!.Details);
        var stale=await StocktakeCommand(service,admin.Child);
        await h.Db.Sys_Depts.Where(d=>d.Id==admin.Child).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Enable,false));
        Assert.Equal("CREATION_AUTHORITY_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(stale))).Message);
        var current=await service.CreationContextAsync();Assert.DoesNotContain(current.Departments,d=>d.Id==admin.Child);
        Assert.Equal("CREATION_DEPARTMENT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>
            service.CreateCommandAsync(stale with{ExpectedAuthorityVersion=current.AuthorityVersion}))).Message);
        Assert.Equal(1,await StocktakeMappings(h));
    }

    [TenantManagementPostgresTheory]
    [InlineData("source-query")] [InlineData("source-price")] [InlineData("target-price")] [InlineData("target-amount")] [InlineData("area")]
    public async Task Stocktake_creation_cannot_bypass_current_source_field_or_warehouse_gates(string gate)
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);var admin=await MappingAdmin(h);
        await BindInventoryForCreation(h,admin);await StandaloneScope(h,admin);var service=InventoryTake(h,owner);
        var command=await StocktakeCommand(service,admin.Child);
        if(gate=="source-query")await h.Db.Sys_RoleActions.Where(a=>a.MenuId==403 && a.ActionCode=="query").ExecuteDeleteAsync();
        else if(gate=="area")
        {
            await h.Db.WmsRoleScopes.ExecuteDeleteAsync();h.Db.WmsRoleScopes.Add(new(){RoleId=72,WarehouseCd="W01",AreaCd="A"});
            h.Db.Locations.AddRange(new(){WarehouseCd="W01",LocationCd="L1",AreaCd="A"},new(){WarehouseCd="W01",LocationCd="L2",AreaCd="B"});
            await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        }
        else
        {
            h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey=gate=="source-price"?"wms-stock":"wms-stocktake",
                FieldName=gate=="target-amount"?"Amount":"UnitPrice",Access=3});
            await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        }
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(command));
        Assert.Empty(await h.Db.StockTakes.ToArrayAsync());Assert.Empty(await h.Db.StockTakeDetails.ToArrayAsync());
        Assert.Equal(0,await StocktakeMappings(h));Assert.Equal(0,await Operations(h));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Stocktake_creation_does_not_absorb_other_mapping_changes_before_or_after_its_own_insert(bool after)
    {
        var hook=new CreationWriteHook();await using var h=await Seed(hook);var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var admin=await MappingAdmin(h);await BindInventoryForCreation(h,admin);await StandaloneScope(h,admin);
        var service=InventoryTake(h,owner);var command=await StocktakeCommand(service,admin.Child);var fired=false;
        hook.Before=async sql=>
        {
            if(!sql.CommandText.Contains("INSERT INTO") || !sql.CommandText.Contains(after?"cp6_main_owner_v1.operations":"T_StockTake"))return;
            hook.Before=null;fired=true;
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant} AND object_kind='stock'");
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=true WHERE tenant_id={h.Tenant} AND object_kind='stock'");
        };
        Assert.Equal("AUTHORITY_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(command))).Message);
        Assert.True(fired);Assert.Empty(await h.Db.StockTakes.ToArrayAsync());Assert.Empty(await h.Db.StockTakeDetails.ToArrayAsync());
        Assert.Equal(0,await StocktakeMappings(h));Assert.Equal(0,await Operations(h));
    }

    [TenantManagementPostgresTheory]
    [InlineData("source-revoke")] [InlineData("target-revoke")] [InlineData("source-quantity")] [InlineData("source-identity")]
    public async Task Stocktake_creation_final_source_and_authority_changes_roll_back_all_effects(string change)
    {
        var hook=new CreationWriteHook();await using var h=await Seed(hook);var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var admin=await MappingAdmin(h);await BindInventoryForCreation(h,admin);await StandaloneScope(h,admin);
        var service=InventoryTake(h,owner);var command=await StocktakeCommand(service,admin.Child);var fired=false;
        var before=await h.Db.Sys_FieldAuditLogs.CountAsync();var old=await h.Db.Stocks.Where(s=>s.ProductCd=="P1").Select(s=>s.Id).SingleAsync();
        hook.Before=async sql=>
        {
            if(!sql.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))return;hook.Before=null;fired=true;
            if(change.EndsWith("revoke"))await h.Db.Sys_RoleActions.Where(a=>a.MenuId==(change=="source-revoke"?403:415)
                && a.ActionCode==(change=="source-revoke"?"query":"add")).ExecuteDeleteAsync();
            else if(change=="source-quantity")await h.Db.Stocks.Where(s=>s.Id==old).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.PhysicalQty,99m));
            else await h.Db.Stocks.Where(s=>s.Id==old).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Id,Guid.NewGuid()));
        };
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(command));Assert.True(fired);
        Assert.Empty(await h.Db.StockTakes.ToArrayAsync());Assert.Empty(await h.Db.StockTakeDetails.ToArrayAsync());Assert.Equal(0,await StocktakeMappings(h));
        Assert.Equal(0,await Operations(h));Assert.Equal(before,await h.Db.Sys_FieldAuditLogs.CountAsync());
        Assert.Equal(10.12345678m,await h.Db.Stocks.Where(s=>s.Id==old).Select(s=>s.PhysicalQty).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Stocktake_creation_lost_commit_ack_recovers_original_once_under_current_query()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var admin=await MappingAdmin(h);await BindInventoryForCreation(h,admin);await StandaloneScope(h,admin);
        var service=InventoryTake(h,owner);var command=await StocktakeCommand(service,admin.Child);fault.Armed=true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(command))).Message);
        var original=await service.ReadCreationAsync(command.OperationId);Assert.Equal("COMMITTED",original.Status);
        Assert.Equal(original.Result,await service.CreateCommandAsync(command));
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>
            service.CreateCommandAsync(command with{Plan=new(){TargetWarehouseCd="OTHER"}}))).Message);
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==415 && a.ActionCode=="add").ExecuteDeleteAsync();
        Assert.Equal(original.Result,await service.CreateCommandAsync(command));
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==415 && a.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.ReadCreationAsync(command.OperationId))).Message);
        Assert.Equal(1,await h.Db.StockTakes.CountAsync());Assert.Equal(2,await h.Db.StockTakeDetails.CountAsync());Assert.Equal(1,await Operations(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Stocktake_creation_recovery_requires_original_actor_tenant_and_exact_header_identity()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);var service=InventoryTake(h,owner);
        var command=await StocktakeCommand(service);var result=await service.CreateCommandAsync(command);
        await using var foreign=await Seed();await GrantStocktakeDashboard(foreign);
        Assert.Equal("UNKNOWN",(await StocktakeCreationService(foreign,foreign.Db).ReadCreationAsync(command.OperationId)).Status);
        var actor=new CP6.Entity.DomainModels.Sys.Sys_User{UserName="other-stocktake-reader",Password="unused",RoleId=72};
        h.Db.Sys_Users.Add(actor);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("ORIGINAL_OPERATION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>
            StocktakeCreationService(h,h.Db,actor.Id).ReadCreationAsync(command.OperationId))).Message);
        await h.Db.StockTakes.Where(s=>s.Id==result.StockTakeId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Id,Guid.NewGuid()));
        Assert.Equal("OBJECT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.ReadCreationAsync(command.OperationId))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Stocktake_creation_concurrent_duplicate_and_closed_unknown_do_not_duplicate_snapshot()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);
        var admin=await MappingAdmin(h);await BindInventoryForCreation(h,admin);await StandaloneScope(h,admin);
        var service=InventoryTake(h,owner);var command=await StocktakeCommand(service,admin.Child);
        async Task<StockTakeCreationResultV1> Create()
        {await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});return await StocktakeCreationService(h,db).CreateCommandAsync(command);}
        var results=await Task.WhenAll(Create(),Create());Assert.Equal(results[0],results[1]);
        Assert.Equal(1,await h.Db.StockTakes.CountAsync());Assert.Equal(2,await h.Db.StockTakeDetails.CountAsync());
        var closed=await StocktakeCommand(service,admin.Child);Assert.Equal("UNKNOWN",(await service.ReadCreationAsync(closed.OperationId)).Status);
        Assert.Equal("CLOSED",(await service.CloseCreationAsync(closed.OperationId)).Status);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(closed))).Message);
        Assert.Equal(1,await StocktakeMappings(h));Assert.Equal(2,await Operations(h));
    }
}
