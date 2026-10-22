using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Net;
using System.Net.Http.Json;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static IamStandaloneDepartmentMappingV1 StandaloneAdmin(Harness h, MappingHarness admin) => new(h.Db,admin.Authority);
    private static async Task<IamStandaloneDepartmentCommandV1> StandaloneCommand(Harness h, MappingHarness admin,
        string kind, Guid objectId, Guid? mappingId=null, Guid? department=null)
    {
        var editor=await StandaloneAdmin(h,admin).ReadAsync(_=>Task.FromResult(h.User),default);
        return new(){Id=mappingId,ObjectKind=kind,ObjectId=objectId,DepartmentId=department??admin.Child,
            OperationId=Guid.NewGuid(),ExpectedVersion=editor.Mappings.SingleOrDefault(x=>x.Id==mappingId)?.Version??"absent",
            ExpectedReferencesVersion=editor.ReferencesVersion,Reason="Explicitly verified fixture ownership"};
    }
    private static Task<IamOperationV1> SaveStandalone(Harness h, MappingHarness admin,IamStandaloneDepartmentCommandV1 command)
        =>StandaloneAdmin(h,admin).ChangeAsync(command,_=>Task.FromResult(h.User),default);
    private static async Task<IamOperationV1> BindStandalone(Harness h,MappingHarness admin,string kind,Guid id,Guid? dept=null)
        =>await SaveStandalone(h,admin,await StandaloneCommand(h,admin,kind,id,department:dept));
    private static async Task StandaloneScope(Harness h,MappingHarness admin,int type=2)
    {
        await h.Db.Sys_Users.Where(u=>u.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.DeptId,type==3?admin.Root:admin.Child));
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey!="order").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,type)
            .SetProperty(x=>x.CustomDeptIds,admin.Child.ToString()));
    }

    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Standalone_bindings_filter_before_paging_aggregates_and_adjust_only_bound_stock(int type)
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);var admin=await MappingAdmin(h);
        await StandaloneScope(h,admin,type);var read=new StockOperationsService(h.Db,owner);
        Assert.Empty((await read.SearchAsync(new())).Items); // No inference from creator or current department.
        var stock=await h.Db.Stocks.SingleAsync(s=>s.ProductCd=="P1");h.Db.ChangeTracker.Clear();
        await BindStandalone(h,admin,"stock",stock.Id);
        var page=await read.SearchAsync(new(){PageSize=1});Assert.Equal(1,page.Total);Assert.Equal(stock.Id,Assert.Single(page.Items).Id);
        Assert.Equal(30.37037034m,(await new WmsDashboardService(h.Db,owner).GetKpiAsync()).TotalStockValue);
        await InventoryMover(h,owner).ApplyAsync(InventoryAdjustment());
        Assert.Equal(11.24691356m,await h.Db.Stocks.Where(s=>s.Id==stock.Id).Select(s=>s.PhysicalQty).SingleAsync());
        var other=InventoryAdjustment();other.ProductCd="P2";other.LocationCd="L2";
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>InventoryMover(h,owner).ApplyAsync(other));
        Assert.Equal(1,await h.Db.StockTransactions.CountAsync());
        Assert.Empty((await read.TransactionsAsync(new())).Items); // A matching stock key is not historical transaction ownership.
        var txn=await h.Db.StockTransactions.Select(x=>x.Id).SingleAsync();await BindStandalone(h,admin,"stock-transaction",txn);
        Assert.Single((await read.HistoryAsync(stock.Id)).Transactions);
        await h.Db.Sys_Depts.Where(d=>d.Id==admin.Root).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Enable,false));
        if(type==4) { Assert.Equal(0,(await read.SearchAsync(new())).Total);Assert.Equal(0,(await read.TransactionsAsync(new())).Total); }
        else { await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>read.SearchAsync(new()));
            await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>read.TransactionsAsync(new())); }
    }

    [CurrentPermissionPostgresFact]
    public async Task Standalone_administration_native_identity_tenant_CAS_audit_and_original_receipt()
    {
        await using var h=await Seed();await SeedInventory(h);var admin=await MappingAdmin(h);var service=StandaloneAdmin(h,admin);
        var stock=await h.Db.Stocks.Select(x=>x.Id).FirstAsync();
        var choices=await service.FindObjectsAsync("stock","P1",_=>Task.FromResult(h.User),default);Assert.Single(choices);
        var command=await StandaloneCommand(h,admin,"stock",stock);var saved=await SaveStandalone(h,admin,command);
        Assert.Equal(saved,await SaveStandalone(h,admin,command));
        Assert.Equal(saved,await admin.Authority.Resolve(command.OperationId,"dept-standalone-mapping",false,_=>Task.FromResult(h.User),default));
        Assert.Single((await h.Db.Sys_SecurityLogs.Select(x=>x.ContextJson).ToArrayAsync()).Where(x=>x?.Contains(command.OperationId.ToString())==true));
        Assert.Equal(409,(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(async()=>await SaveStandalone(h,admin,
            await StandaloneCommand(h,admin,"stock",stock)))).Status);
        var stale=await StandaloneCommand(h,admin,"stock",stock,saved.EntityId);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE id={saved.EntityId}");
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=true WHERE id={saved.EntityId}");
        Assert.Equal(412,(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>SaveStandalone(h,admin,stale))).Status);
        await using var foreign=await Seed();await SeedInventory(foreign);var foreignId=await foreign.Db.Stocks.Select(x=>x.Id).FirstAsync();
        Assert.Equal("STANDALONE_OBJECT_UNAVAILABLE",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(async()=>
            await BindStandalone(h,admin,"stock",foreignId))).Message);
        var error=await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_operations_owner_v1.department_mappings(id,tenant_id,object_kind,object_id,department_id,enabled)
            VALUES({Guid.NewGuid()},{h.Tenant},'stock',{foreignId},{admin.Child},true)
            """));Assert.Equal("23514",error.SqlState);
        var foreignAdmin=await MappingAdmin(foreign);
        Assert.Equal("IAM_DEPARTMENT_UNAVAILABLE",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(async()=>
            await SaveStandalone(h,admin,(await StandaloneCommand(h,admin,"stock",stock,saved.EntityId)) with {DepartmentId=foreignAdmin.Child}))).Message);
        var rawDepartment=await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_operations_owner_v1.department_mappings SET department_id={foreignAdmin.Child} WHERE id={saved.EntityId}
            """));Assert.Equal("23514",rawDepartment.SqlState);
        var changed=(await StandaloneCommand(h,admin,"stock",stock,saved.EntityId)) with {ObjectKind="stock-transaction"};
        Assert.Equal("STANDALONE_OBJECT_IMMUTABLE",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>SaveStandalone(h,admin,changed))).Message);
        Assert.Equal(400,(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>SaveStandalone(h,admin,command with {ObjectKind="Stock"}))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Standalone_stocktake_requires_exact_complete_stock_graph_and_cannot_create_unbound_objects()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);var take=InventoryTake(h,owner);
        var no=await take.CreatePlanAsync(new(){TargetWarehouseCd="W01"},null);var admin=await MappingAdmin(h);
        await BindStandalone(h,admin,"stocktake",await h.Db.StockTakes.Select(s=>s.Id).SingleAsync());
        await StandaloneScope(h,admin);Assert.Empty(await take.SearchAsync(new()));
        foreach(var id in await h.Db.Stocks.Select(s=>s.Id).ToArrayAsync())await BindStandalone(h,admin,"stock",id);
        Assert.Single(await take.SearchAsync(new()));await take.StartCountAsync(no,null);
        await take.UpdateCountsAsync(no,[new(){LineNo=1,CountedQty=9m},new(){LineNo=2,CountedQty=20m}],null);
        var first=await h.Db.Stocks.Where(s=>s.ProductCd=="P1").Select(s=>s.Id).SingleAsync();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE object_id={first}");
        Assert.Empty(await take.SearchAsync(new()));await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>take.SubmitForReviewAsync(no,null));
        Assert.Equal(0,await h.Db.StockTransactions.CountAsync());
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>take.CreatePlanAsync(new(){TargetWarehouseCd="W01",TargetLocationPrefix="L2"},null));
        Assert.Equal(1,await h.Db.StockTakes.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Standalone_work_order_cannot_override_a_current_order_association()
    {
        await using var h=await Seed();var owner=await GrantOperations(h);var no=await SeedProduction(h);var admin=await MappingAdmin(h);
        var id=await h.Db.WorkOrders.Where(x=>x.WorkOrderNo==no).Select(x=>x.Id).SingleAsync();
        await h.Db.WorkOrders.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.WebOrderNo,(string?)null));
        await BindStandalone(h,admin,"work-order",id);await StandaloneScope(h,admin);
        var service=new WorkOrderService(h.Db,new MesSequenceService(h.Db),new NoOpWmsBridgeHook(),owner);
        Assert.NotNull(await service.GetByNoAsync(no.ToLowerInvariant()+" "));
        await h.Db.WorkOrders.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.WebOrderNo,h.Number));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.GetByNoAsync(no));
        Assert.False((await StandaloneAdmin(h,admin).ReadAsync(_=>Task.FromResult(h.User),default)).Mappings.Single().Available);
        await Assert.ThrowsAsync<IamAdministrationExceptionV1>(async()=>await BindStandalone(h,admin,"work-order",id));
        await h.Db.Orders.Where(x=>x.WebOrderNo==h.Number).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.OrderDepartment,"SALES"));
        await SaveMapping(h,admin,await MappingCommand(h,admin));
        Assert.NotNull(await service.GetByNoAsync(no)); // Original accepted order binding still controls linked objects.
    }

    [CurrentPermissionPostgresFact]
    public async Task Standalone_stocktake_and_source_custom_department_scopes_intersect_without_parameter_aliasing()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);var take=InventoryTake(h,owner);
        var no=await take.CreatePlanAsync(new(){TargetWarehouseCd="W01"},null);var admin=await MappingAdmin(h);
        await BindStandalone(h,admin,"stocktake",await h.Db.StockTakes.Select(s=>s.Id).SingleAsync());
        foreach(var id in await h.Db.Stocks.Select(s=>s.Id).ToArrayAsync())await BindStandalone(h,admin,"stock",id);
        await StandaloneScope(h,admin,4);
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="wms-stock").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CustomDeptIds,admin.Other.ToString()));
        Assert.Single(await take.SearchAsync(new())); // Header Owner allows; source still independently denies snapshot read.
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>take.StartCountAsync(no,null));
        Assert.Equal(StockTakeStatus.Planned,await h.Db.StockTakes.Select(s=>s.Status).SingleAsync());
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="wms-stock").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CustomDeptIds,admin.Child.ToString()));
        await take.StartCountAsync(no,null);Assert.Equal(StockTakeStatus.Counting,await h.Db.StockTakes.Select(s=>s.Status).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Standalone_rebind_empty_custom_and_recreated_stock_do_not_retain_old_allow()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);var admin=await MappingAdmin(h);
        var stock=await h.Db.Stocks.AsNoTracking().SingleAsync(s=>s.ProductCd=="P1");var mapping=await BindStandalone(h,admin,"stock",stock.Id);
        await StandaloneScope(h,admin,4);var read=new StockOperationsService(h.Db,owner);Assert.Equal(1,(await read.SearchAsync(new())).Total);
        await SaveStandalone(h,admin,(await StandaloneCommand(h,admin,"stock",stock.Id,mapping.EntityId)) with {DepartmentId=admin.Other});
        Assert.Equal(0,(await read.SearchAsync(new())).Total);
        await SaveStandalone(h,admin,await StandaloneCommand(h,admin,"stock",stock.Id,mapping.EntityId));
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="wms-stock").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CustomDeptIds,""));
        Assert.Equal(0,(await read.SearchAsync(new())).Total);await StandaloneScope(h,admin);
        await h.Db.Stocks.Where(s=>s.Id==stock.Id).ExecuteDeleteAsync();stock.Id=Guid.NewGuid();h.Db.Stocks.Add(stock);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal(0,(await read.SearchAsync(new())).Total); // Exact object identity, not a reused business key.
    }

    [CurrentPermissionPostgresFact]
    public async Task Standalone_raw_revoke_waits_for_owner_commit_then_denies_next_read_and_write()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);var admin=await MappingAdmin(h);
        var id=await h.Db.Stocks.Where(s=>s.ProductCd=="P1").Select(s=>s.Id).SingleAsync();await BindStandalone(h,admin,"stock",id);await StandaloneScope(h,admin);
        Task? revoker=null;var reads=0;var waited=false;
        hook.Before=async c=>
        {
            if(!c.CommandText.Contains("WITH field_subject") || ++reads!=2)return;hook.Before=null;
            revoker=Task.Run(async()=>{await using var writer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
                await writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE object_id={id}");});
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                AND query LIKE 'UPDATE cp6_operations_owner_v1.department_mappings%') AS "Value"
                """).SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            waited=true;Assert.False(revoker.IsCompleted);
        };
        try{await InventoryMover(h,owner).ApplyAsync(InventoryAdjustment());}finally{if(revoker!=null)await revoker;}
        Assert.True(waited);Assert.Equal(1,await h.Db.StockTransactions.CountAsync());
        Assert.Equal(0,(await new StockOperationsService(h.Db,owner).SearchAsync(new())).Total);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>InventoryMover(h,owner).ApplyAsync(InventoryAdjustment()));
        Assert.Equal(1,await h.Db.StockTransactions.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Standalone_final_binding_change_rolls_back_stock_and_binding()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantStocktakeDashboard(h);await SeedInventory(h);var admin=await MappingAdmin(h);
        var id=await h.Db.Stocks.Where(s=>s.ProductCd=="P1").Select(s=>s.Id).SingleAsync();await BindStandalone(h,admin,"stock",id);await StandaloneScope(h,admin);
        var reads=0;var changed=false;hook.Before=async c=>
        {
            if(!c.CommandText.Contains("WITH field_subject") || ++reads!=2)return;hook.Before=null;changed=true;
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE object_id={id}");
        };
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>InventoryMover(h,owner).ApplyAsync(InventoryAdjustment()));Assert.True(changed);
        Assert.Equal(0,await h.Db.StockTransactions.CountAsync());Assert.Equal(10.12345678m,await h.Db.Stocks.Where(s=>s.Id==id).Select(s=>s.PhysicalQty).SingleAsync());
        Assert.Equal(1,(await new StockOperationsService(h.Db,owner).SearchAsync(new())).Total);
    }

    [CurrentPermissionPostgresFact]
    public async Task Standalone_lost_commit_ack_recovers_exact_original_and_never_reapplies()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);await SeedInventory(h);var admin=await MappingAdmin(h);
        var id=await h.Db.Stocks.Select(s=>s.Id).FirstAsync();var command=await StandaloneCommand(h,admin,"stock",id);fault.Armed=true;
        Assert.Equal("IAM_OPERATION_UNKNOWN",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>SaveStandalone(h,admin,command))).Message);
        var result=await admin.Authority.Resolve(command.OperationId,"dept-standalone-mapping",false,_=>Task.FromResult(h.User),default);
        Assert.Equal("COMMITTED",result.State);Assert.Equal(result,await SaveStandalone(h,admin,command));
        Assert.Single((await StandaloneAdmin(h,admin).ReadAsync(_=>Task.FromResult(h.User),default)).Mappings);
    }

    [CurrentPermissionPostgresFact]
    public async Task Standalone_actual_HTTP_lookup_binding_CSRF_CAS_and_revoked_original()
    {
        await using var h=await Seed();await SeedInventory(h);await GrantStocktakeDashboard(h);var admin=await MappingAdmin(h);await StandaloneScope(h,admin);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        const string path="api/iam/departments/standalone-mappings";
        using(var anonymous=await host.Send(new(HttpMethod.Get,path)))Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
        using var lookup=await host.Send(BoundRequest(cookies,"api/iam/departments/standalone-objects?kind=stock&search=P1"));
        Assert.True(lookup.IsSuccessStatusCode,await lookup.Content.ReadAsStringAsync());Assert.True(lookup.Headers.CacheControl?.NoStore);
        var selected=Assert.Single((await lookup.Content.ReadFromJsonAsync<IamStandaloneDepartmentMappingV1.ObjectChoice[]>())!);
        using var editorResponse=await host.Send(BoundRequest(cookies,path));Assert.True(editorResponse.Headers.CacheControl?.NoStore);
        var editor=(await editorResponse.Content.ReadFromJsonAsync<IamStandaloneDepartmentEditorV1>())!;
        var command=new IamStandaloneDepartmentCommandV1{OperationId=Guid.NewGuid(),ObjectKind="stock",ObjectId=selected.Id,
            DepartmentId=admin.Child,ExpectedVersion="absent",ExpectedReferencesVersion=editor.ReferencesVersion,Reason="HTTP verified"};
        using(var csrf=await host.Send(BoundRequest(cookies,path,command,false)))Assert.Equal(HttpStatusCode.Forbidden,csrf.StatusCode);
        using var saved=await host.Send(BoundRequest(cookies,path,command));Assert.True(saved.IsSuccessStatusCode,await saved.Content.ReadAsStringAsync());
        var receipt=(await saved.Content.ReadFromJsonAsync<IamOperationV1>())!;Assert.Equal("COMMITTED",receipt.State);
        using(var stale=await host.Send(BoundRequest(cookies,path,command with {OperationId=Guid.NewGuid()})))Assert.Equal(HttpStatusCode.PreconditionFailed,stale.StatusCode);
        var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey=="pub-dept").Select(m=>m.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==menu && a.ActionCode=="edit").ExecuteDeleteAsync();
        using var revoked=await host.Send(BoundRequest(cookies,$"api/iam/departments/operations/{command.OperationId}?kind=dept-standalone-mapping"));
        Assert.Equal(HttpStatusCode.Forbidden,revoked.StatusCode);
    }
}
