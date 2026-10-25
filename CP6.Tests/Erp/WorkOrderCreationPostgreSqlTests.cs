using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Mes;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Http;
using CP6.Entity.DTOs.Mes;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static WorkOrderDto NewCreationWorkOrder(string? order=null)=>new()
    {
        ProductCd="CREATION-P",ProductName="Explicit creation",ProductionQty=12.12345678m,WebOrderNo=order,
        Processes=[new(){ProcessCd="P1",TaskCd="T1",PlanQty=12.12345678m}],
        Materials=[new(){ProcessCd="P1",MaterialCd="M1",PlanQty=3.12345678m,Unit="EA"}]
    };

    [CurrentPermissionPostgresFact]
    public async Task Work_order_creation_actual_HTTP_department_graph_issue_and_original_recovery()
    {
        await using var h=await Seed();await GrantOperations(h);var admin=await MappingAdmin(h);
        await StandaloneScope(h,admin);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies=await Login(host,h);
        using var context=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/mes/work-orders/creation-context"));
        Assert.Equal(HttpStatusCode.OK,context.StatusCode);
        var data=(await context.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.False(data.GetProperty("allowsUnbound").GetBoolean());
        Assert.Equal(admin.Child,Assert.Single(data.GetProperty("departments").EnumerateArray()).GetProperty("id").GetGuid());
        var operation=Guid.NewGuid();
        var input=new{operationId=operation,expectedAuthorityVersion=data.GetProperty("authorityVersion").GetString(),
            departmentId=admin.Child,workOrder=NewCreationWorkOrder()};
        using var created=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/mes/work-orders/operations",input));
        Assert.Equal(HttpStatusCode.OK,created.StatusCode);
        var original=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var id=original.GetProperty("workOrderId").GetGuid();var no=original.GetProperty("workOrderNo").GetString();
        var header=await h.Db.WorkOrders.AsNoTracking().SingleAsync(w=>w.Id==id);
        Assert.Equal(h.Tenant,header.TenantId);Assert.Equal("price-owner",header.Creator);
        Assert.Equal(12.12345678m,header.ProductionQty);
        Assert.Equal(no,Assert.Single(await h.Db.WorkOrderProcesses.AsNoTracking().ToArrayAsync()).WorkOrderNo);
        Assert.Equal(3.12345678m,Assert.Single(await h.Db.WorkOrderMaterials.AsNoTracking().ToArrayAsync()).PlanQty);
        using var repeated=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/mes/work-orders/operations",input));
        Assert.Equal(HttpStatusCode.OK,repeated.StatusCode);
        Assert.Equal(original.GetRawText(),(await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetRawText());
        using var issued=await host.Send(OperationsRequest(cookies,HttpMethod.Post,$"api/mes/work-orders/{no}/issue"));
        Assert.Equal(HttpStatusCode.OK,issued.StatusCode);
        Assert.Equal(2,await h.Db.WorkOrders.Where(w=>w.Id==id).Select(w=>w.Status).SingleAsync());
        using var recovered=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"api/mes/work-orders/operations/{operation}"));
        Assert.Equal(HttpStatusCode.OK,recovered.StatusCode);
        var receipt=(await recovered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal("COMMITTED",receipt.GetProperty("status").GetString());
        Assert.Equal(original.GetRawText(),receipt.GetProperty("result").GetRawText());
        Assert.Single(await h.Db.WorkOrders.AsNoTracking().ToArrayAsync());
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==302 && a.ActionCode=="query").ExecuteDeleteAsync();
        using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"api/mes/work-orders/operations/{operation}"));
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }

    private static WorkOrderService ManufacturingCreation(Harness h,CP6Context? db=null,Guid? actor=null)
    {
        db??=h.Db;var catalog=new AuthorizationCatalogStoreV1();catalog.RegisterFields("mes-work-order",[]);
        catalog.RegisterScope("mes-work-order","Manufacturing",[1,2,3,4,5],1);catalog.Seal();var subject=actor??h.User;
        var owner=new OperationsOwnerV1(db,new OperationsHttp{HttpContext=actor==null?h.Http:new DefaultHttpContext
            {User=CurrentPermissionContextTests.Principal(subject,h.Tenant)}},_=>Task.FromResult(subject),catalog.Capture);
        return new(db,new MesSequenceService(db),new NoOpWmsBridgeHook(),owner);
    }
    private static async Task<WorkOrderCreationCommandV1> ManufacturingCommand(WorkOrderService service,Guid? department=null,string? order=null)
        =>new(){OperationId=Guid.NewGuid(),ExpectedAuthorityVersion=(await service.CreationContextAsync()).AuthorityVersion,
            DepartmentId=department,WorkOrder=NewCreationWorkOrder(order)};
    private static async Task<int> ManufacturingSequence(Harness h)
        =>await h.Db.MesSequences.Where(s=>s.SeqKey=="WO" && s.SeqDate=="*").Select(s=>(int?)s.CurrentValue).SingleOrDefaultAsync()??0;
    private static Task<int> ManufacturingMappings(Harness h)=>h.Db.Database.SqlQuery<int>($"""
        SELECT count(*)::integer AS "Value" FROM cp6_operations_owner_v1.department_mappings
        WHERE tenant_id={h.Tenant} AND object_kind='work-order'
        """).SingleAsync();

    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Work_order_creation_requires_explicit_current_department_and_binds_only_new_graph(int scope)
    {
        await using var h=await Seed();var sequenceBefore=await ManufacturingSequence(h);await GrantOperations(h);var admin=await MappingAdmin(h);await StandaloneScope(h,admin,scope);
        var service=ManufacturingCreation(h);var command=await ManufacturingCommand(service,admin.Child);
        command.WorkOrder.Id=Guid.NewGuid();command.WorkOrder.OrderNo1=h.Number; // Display references never confer ownership.
        Assert.Equal("CREATION_DEPARTMENT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(command with{DepartmentId=null}))).Message);
        await using var other=await Seed();var foreign=await MappingAdmin(other);
        Assert.Equal("CREATION_DEPARTMENT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(command with{DepartmentId=foreign.Child}))).Message);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateAsync(command.WorkOrder,"forged"));
        Assert.Empty(await h.Db.WorkOrders.AsNoTracking().ToArrayAsync());Assert.Equal(sequenceBefore,await ManufacturingSequence(h));
        var result=await service.CreateCommandAsync(command);Assert.NotEqual(command.WorkOrder.Id,result.WorkOrderId);Assert.Equal(admin.Child,result.DepartmentId);
        Assert.Equal(result.WorkOrderId,(await service.GetByNoAsync(result.WorkOrderNo))!.Id);
        Assert.Equal("price-owner",Assert.Single(await h.Db.WorkOrderProcesses.AsNoTracking().ToArrayAsync()).Creator);
        Assert.Equal("price-owner",Assert.Single(await h.Db.WorkOrderMaterials.AsNoTracking().ToArrayAsync()).Creator);
        Assert.Equal(1,await ManufacturingMappings(h));Assert.Equal(1,await Operations(h));
    }

    [TenantManagementPostgresTheory]
    [InlineData(1)] [InlineData(5)]
    public async Task Work_order_creation_preserves_unbound_legacy_and_first_add_without_query(int scope)
    {
        await using var h=await Seed();await GrantOperations(h);
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="mes-work-order").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,scope));
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==302 && a.ActionCode=="query").ExecuteDeleteAsync();
        var service=ManufacturingCreation(h);var command=await ManufacturingCommand(service);
        Assert.Null((await service.CreateCommandAsync(command)).DepartmentId);
        await service.CreateAsync(NewCreationWorkOrder(),"forged");Assert.Equal(2,await h.Db.WorkOrders.CountAsync());
        Assert.Equal(0,await ManufacturingMappings(h));
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.ReadCreationAsync(command.OperationId))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Work_order_creation_linked_order_cannot_be_replaced_by_independent_department()
    {
        await using var h=await Seed();var sequenceBefore=await ManufacturingSequence(h);await GrantOperations(h);var admin=await MappingAdmin(h);await StandaloneScope(h,admin);
        var service=ManufacturingCreation(h);var command=await ManufacturingCommand(service,admin.Child,h.Number);
        Assert.Equal("CREATION_DEPARTMENT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(command))).Message);
        Assert.Equal("OBJECT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(command with{DepartmentId=null}))).Message);
        Assert.Empty(await h.Db.WorkOrders.AsNoTracking().ToArrayAsync());Assert.Equal(sequenceBefore,await ManufacturingSequence(h));
        await h.Db.Orders.Where(o=>o.WebOrderNo==h.Number).ExecuteUpdateAsync(s=>s.SetProperty(o=>o.OrderDepartment,"SALES"));
        await SaveMapping(h,admin,await MappingCommand(h,admin));command=await ManufacturingCommand(service,null,h.Number);
        var created=await service.CreateCommandAsync(command);Assert.Null(created.DepartmentId);Assert.Equal(0,await ManufacturingMappings(h));
        Assert.NotNull(await service.GetByNoAsync(created.WorkOrderNo));
        await service.CreateAsync(NewCreationWorkOrder(h.Number),"forged"); // Accepted linked legacy path retained.
        await h.Db.Orders.Where(o=>o.WebOrderNo==h.Number).ExecuteUpdateAsync(s=>s.SetProperty(o=>o.CancelledAt,new DateTime(2026,10,25)));
        command=await ManufacturingCommand(service,null,h.Number);
        Assert.Equal("SOURCE_NOT_ACTIVE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(command))).Message);
        Assert.Equal(2,await h.Db.WorkOrders.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Work_order_creation_stale_disabled_department_fails_without_graph_or_sequence()
    {
        await using var h=await Seed();var sequenceBefore=await ManufacturingSequence(h);await GrantOperations(h);var admin=await MappingAdmin(h);await StandaloneScope(h,admin,3);
        var service=ManufacturingCreation(h);var command=await ManufacturingCommand(service,admin.Child);
        await h.Db.Sys_Depts.Where(d=>d.Id==admin.Child).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Enable,false));
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(command))).Status);
        var context=await service.CreationContextAsync();Assert.DoesNotContain(context.Departments,d=>d.Id==admin.Child);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(command with{ExpectedAuthorityVersion=context.AuthorityVersion}))).Status);
        Assert.Empty(await h.Db.WorkOrders.AsNoTracking().ToArrayAsync());Assert.Equal(sequenceBefore,await ManufacturingSequence(h));Assert.Equal(0,await Operations(h));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Work_order_creation_extra_mapping_changes_are_not_absorbed_before_or_after_own_transition(bool after)
    {
        var hook=new CreationWriteHook();await using var h=await Seed(hook);var sequenceBefore=await ManufacturingSequence(h);await GrantOperations(h);var admin=await MappingAdmin(h);
        var service=ManufacturingCreation(h);var existing=await service.CreateAsync(NewCreationWorkOrder(),null);
        var existingId=await h.Db.WorkOrders.Select(w=>w.Id).SingleAsync();var mapping=await BindStandalone(h,admin,"work-order",existingId);
        await StandaloneScope(h,admin);var command=await ManufacturingCommand(service,admin.Child);var fired=false;
        hook.Before=async sql=>
        {
            if(!sql.CommandText.Contains("INSERT INTO") || !sql.CommandText.Contains(after?"cp6_main_owner_v1.operations":"T_WorkOrder"))return;
            hook.Before=null;fired=true;await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE id={mapping.EntityId}");
        };
        Assert.Equal("AUTHORITY_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(command))).Message);
        Assert.True(fired);Assert.Single(await h.Db.WorkOrders.AsNoTracking().ToArrayAsync());Assert.Single(await h.Db.WorkOrderProcesses.AsNoTracking().ToArrayAsync());
        Assert.Single(await h.Db.WorkOrderMaterials.AsNoTracking().ToArrayAsync());Assert.Equal(sequenceBefore+1,await ManufacturingSequence(h));
        Assert.Equal(1,await ManufacturingMappings(h));Assert.Equal(0,await Operations(h));Assert.NotNull(await service.GetByNoAsync(existing));
    }

    [TenantManagementPostgresTheory]
    [InlineData("revoke")] [InlineData("journal")]
    public async Task Work_order_creation_final_failure_rolls_back_header_children_sequence_mapping_and_audit(string fault)
    {
        var hook=new CreationWriteHook();await using var h=await Seed(hook);var sequenceBefore=await ManufacturingSequence(h);await GrantOperations(h);var admin=await MappingAdmin(h);await StandaloneScope(h,admin);
        var service=ManufacturingCreation(h);var command=await ManufacturingCommand(service,admin.Child);var fired=false;
        var audits=await h.Db.Sys_FieldAuditLogs.CountAsync();
        hook.Before=async sql=>
        {
            if(!sql.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))return;
            hook.Before=null;fired=true;
            if(fault=="journal")throw new IOException("original journal persistence fault");
            await h.Db.Sys_RoleActions.Where(a=>a.MenuId==302 && a.ActionCode=="add").ExecuteDeleteAsync();
        };
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(command));Assert.True(fired);
        Assert.Empty(await h.Db.WorkOrders.AsNoTracking().ToArrayAsync());Assert.Empty(await h.Db.WorkOrderProcesses.AsNoTracking().ToArrayAsync());Assert.Empty(await h.Db.WorkOrderMaterials.AsNoTracking().ToArrayAsync());
        Assert.Equal(sequenceBefore,await ManufacturingSequence(h));Assert.Equal(0,await ManufacturingMappings(h));Assert.Equal(0,await Operations(h));Assert.Equal(audits,await h.Db.Sys_FieldAuditLogs.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Work_order_creation_lost_commit_ack_recovers_original_and_rechecks_current_query()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);await GrantOperations(h);var admin=await MappingAdmin(h);await StandaloneScope(h,admin);
        var service=ManufacturingCreation(h);var command=await ManufacturingCommand(service,admin.Child);fault.Armed=true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(command))).Message);
        var original=await service.ReadCreationAsync(command.OperationId);Assert.Equal("COMMITTED",original.Status);Assert.Equal(original.Result,await service.CreateCommandAsync(command));
        var changed=NewCreationWorkOrder();changed.Materials[0].PlanQty=99;
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(command with{WorkOrder=changed}))).Message);
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==302 && a.ActionCode=="add").ExecuteDeleteAsync();Assert.Equal(original.Result,await service.CreateCommandAsync(command));
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==302 && a.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.ReadCreationAsync(command.OperationId))).Message);
        Assert.Single(await h.Db.WorkOrders.AsNoTracking().ToArrayAsync());Assert.Equal(1,await Operations(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Work_order_creation_recovery_requires_original_actor_tenant_and_exact_header_identity()
    {
        await using var h=await Seed();await GrantOperations(h);var service=ManufacturingCreation(h);var command=await ManufacturingCommand(service);
        var result=await service.CreateCommandAsync(command);
        await using var foreign=await Seed();await GrantOperations(foreign);Assert.Equal("UNKNOWN",(await ManufacturingCreation(foreign).ReadCreationAsync(command.OperationId)).Status);
        var actor=new CP6.Entity.DomainModels.Sys.Sys_User{UserName="other-wo-reader",Password="unused",RoleId=72};h.Db.Sys_Users.Add(actor);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("ORIGINAL_OPERATION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>ManufacturingCreation(h,actor:actor.Id).ReadCreationAsync(command.OperationId))).Message);
        var header=await h.Db.WorkOrders.AsNoTracking().SingleAsync();await h.Db.WorkOrders.ExecuteDeleteAsync();header.Id=Guid.NewGuid();h.Db.WorkOrders.Add(header);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("OBJECT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.ReadCreationAsync(command.OperationId))).Message);
        Assert.NotEqual(result.WorkOrderId,await h.Db.WorkOrders.Select(w=>w.Id).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Work_order_creation_concurrent_duplicates_commit_once_and_close_blocks_late_create()
    {
        await using var h=await Seed();await GrantOperations(h);var admin=await MappingAdmin(h);await StandaloneScope(h,admin);
        var service=ManufacturingCreation(h);var command=await ManufacturingCommand(service,admin.Child);
        async Task<WorkOrderCreationResultV1> Create()
        {await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});return await ManufacturingCreation(h,db).CreateCommandAsync(command);}
        var results=await Task.WhenAll(Create(),Create());Assert.Equal(results[0],results[1]);Assert.Single(await h.Db.WorkOrders.AsNoTracking().ToArrayAsync());Assert.Equal(1,await ManufacturingMappings(h));
        var later=await ManufacturingCommand(service,admin.Child);Assert.Equal("UNKNOWN",(await service.ReadCreationAsync(later.OperationId)).Status);
        Assert.Equal("CLOSED",(await service.CloseCreationAsync(later.OperationId)).Status);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(later))).Message);
        Assert.Single(await h.Db.WorkOrders.AsNoTracking().ToArrayAsync());Assert.Equal(2,await Operations(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Work_order_creation_rechecks_linked_source_activity_after_original_record_write()
    {
        var hook=new CreationWriteHook();await using var h=await Seed(hook);await GrantOperations(h);var admin=await MappingAdmin(h);
        await h.Db.Orders.Where(o=>o.WebOrderNo==h.Number).ExecuteUpdateAsync(s=>s.SetProperty(o=>o.OrderDepartment,"SALES"));
        await SaveMapping(h,admin,await MappingCommand(h,admin));await StandaloneScope(h,admin);
        var service=ManufacturingCreation(h);var command=await ManufacturingCommand(service,null,h.Number);var fired=false;
        var sequenceBefore=await ManufacturingSequence(h);
        hook.Before=async sql=>
        {
            if(!sql.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))return;
            hook.Before=null;fired=true;
            await h.Db.Orders.Where(o=>o.WebOrderNo==h.Number).ExecuteUpdateAsync(s=>s.SetProperty(o=>o.CancelledAt,new DateTime(2026,10,25)));
        };
        Assert.Equal("SOURCE_NOT_ACTIVE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(command))).Message);
        Assert.True(fired);Assert.Equal(sequenceBefore,await ManufacturingSequence(h));Assert.Empty(await h.Db.WorkOrders.ToArrayAsync());
        Assert.Empty(await h.Db.WorkOrderProcesses.ToArrayAsync());Assert.Empty(await h.Db.WorkOrderMaterials.ToArrayAsync());Assert.Equal(0,await Operations(h));
        Assert.Null(await h.Db.Orders.Where(o=>o.WebOrderNo==h.Number).Select(o=>o.CancelledAt).SingleAsync());
    }
}
