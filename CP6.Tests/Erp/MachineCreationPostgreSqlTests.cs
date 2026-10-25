using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static MachineService CreationService(Harness h,CP6Context db,Guid? actor=null)
    {
        var catalog=new AuthorizationCatalogStoreV1();catalog.RegisterFields("mes-machine",[]);
        catalog.RegisterScope("mes-machine","mes-machine",[1,2,3,4,5],1);catalog.Seal();
        var subject=actor??h.User;
        var owner=new OperationsOwnerV1(db,new OperationsHttp{HttpContext=actor==null?h.Http:new DefaultHttpContext
            {User=CurrentPermissionContextTests.Principal(subject,h.Tenant)}},_=>Task.FromResult(subject),catalog.Capture);
        return new(db,new MesSequenceService(db),Mock.Of<IMesNotifier>(),owner);
    }
    private static async Task<MachineCreationCommandV1> CreationCommand(MachineService service,Guid? department=null,string code="NEW")
        =>new(){OperationId=Guid.NewGuid(),DepartmentId=department,ExpectedAuthorityVersion=(await service.CreationContextAsync()).AuthorityVersion,
            Machine=new(){MachineCd=code,MachineName="New machine",ActiveFlg=true,Id=Guid.NewGuid()}};
    private static Task<int> MachineMappings(Harness h)=>h.Db.Database.SqlQuery<int>($"""
        SELECT count(*)::integer AS "Value" FROM cp6_operations_owner_v1.department_mappings
        WHERE tenant_id={h.Tenant} AND object_kind='machine'
        """).SingleAsync();

    private sealed class CreationWriteHook : DbCommandInterceptor
    {
        internal Func<DbCommand,Task>? Before;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken ct=default)
        {if(Before is {} hook)await hook(command);return result;}
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData data,InterceptionResult<int> result,CancellationToken ct=default)
        {if(Before is {} hook)await hook(command);return result;}
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Machine_creation_unrelated_mapping_changes_before_or_after_own_transition_roll_back(bool after)
    {
        var hook=new CreationWriteHook();await using var h=await Seed(hook);var owner=await GrantMachine(h);var admin=await MappingAdmin(h);
        await SeedMachine(h,"EXISTING");var old=await h.Db.Machines.Select(m=>m.Id).SingleAsync();
        var mapping=await BindStandalone(h,admin,"machine",old);await StandaloneScope(h,admin);
        var service=Machines(h,owner);var command=await CreationCommand(service,admin.Child);var fired=false;
        hook.Before=async sql=>
        {
            if(!sql.CommandText.Contains("INSERT INTO") || !sql.CommandText.Contains(after?"cp6_main_owner_v1.operations":"M_Machine"))return;
            hook.Before=null;fired=true;
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE id={mapping.EntityId}");
        };
        Assert.Equal("AUTHORITY_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(command))).Message);
        Assert.True(fired);Assert.Equal(1,await h.Db.Machines.CountAsync());Assert.Equal(1,await MachineMappings(h));
        Assert.Equal(0,await Operations(h));Assert.NotNull(await service.GetByCdAsync("EXISTING"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Machine_creation_own_binding_does_not_accept_a_same_transaction_action_revoke()
    {
        var hook=new CreationWriteHook();await using var h=await Seed(hook);var owner=await GrantMachine(h);var admin=await MappingAdmin(h);
        await StandaloneScope(h,admin);var service=Machines(h,owner);var command=await CreationCommand(service,admin.Child);var fired=false;
        hook.Before=async sql=>
        {
            if(!sql.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))return;
            hook.Before=null;fired=true;
            await h.Db.Sys_RoleActions.Where(a=>a.MenuId==310 && a.ActionCode=="add").ExecuteDeleteAsync();
        };
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(command))).Message);
        Assert.True(fired);Assert.Empty(await h.Db.Machines.ToArrayAsync());Assert.Equal(0,await MachineMappings(h));Assert.Equal(0,await Operations(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Machine_creation_retains_concurrent_revoker_until_atomic_commit_then_denies_recovery()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantMachine(h);var admin=await MappingAdmin(h);
        await StandaloneScope(h,admin);var service=Machines(h,owner);var command=await CreationCommand(service,admin.Child);
        Task? revoke=null;var blocked=false;
        hook.After=async sql=>
        {
            if(!sql.CommandText.Contains("WITH field_subject"))return;hook.After=null;
            using(ExecutionContext.SuppressFlow())revoke=Task.Run(async()=>
            {
                await using var writer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
                await writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Roles\" SET \"Enable\"=false WHERE \"TenantId\"={h.Tenant} AND \"RoleId\"=72");
            });
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                AND query LIKE 'UPDATE public."Sys_Roles"%') AS "Value"
                """).SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            Assert.False(revoke.IsCompleted);blocked=true;
        };
        try{await service.CreateCommandAsync(command);}finally{if(revoke!=null)await revoke;}
        Assert.True(blocked);Assert.Equal(1,await h.Db.Machines.CountAsync());Assert.Equal(1,await MachineMappings(h));Assert.Equal(1,await Operations(h));
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.ReadCreationAsync(command.OperationId))).Message);
    }

    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Machine_creation_requires_explicit_current_department_without_actor_inference(int scope)
    {
        await using var h=await Seed();var owner=await GrantMachine(h);var admin=await MappingAdmin(h);await StandaloneScope(h,admin,scope);
        var service=Machines(h,owner);var command=await CreationCommand(service,admin.Child);
        Assert.Equal("CREATION_DEPARTMENT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>
            service.CreateCommandAsync(command with{DepartmentId=null}))).Message);
        await using var foreign=await Seed();var foreignAdmin=await MappingAdmin(foreign);
        Assert.Equal("CREATION_DEPARTMENT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>
            service.CreateCommandAsync(command with{DepartmentId=foreignAdmin.Child}))).Message);
        Assert.Empty(await h.Db.Machines.ToArrayAsync());Assert.Equal(0,await MachineMappings(h));
        var saved=await service.CreateCommandAsync(command);
        Assert.NotEqual(command.Machine.Id,saved.MachineId);Assert.Equal(admin.Child,saved.DepartmentId);
        Assert.Equal(saved.MachineId,(await service.GetByCdAsync("NEW"))!.Id);
        Assert.Equal(1,await MachineMappings(h));Assert.Equal(1,await Operations(h));
        Assert.Equal("price-owner",await h.Db.Machines.Select(m=>m.Creator).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Machine_creation_stale_or_disabled_department_cannot_be_reused()
    {
        await using var h=await Seed();var owner=await GrantMachine(h);var admin=await MappingAdmin(h);await StandaloneScope(h,admin,3);
        var service=Machines(h,owner);var command=await CreationCommand(service,admin.Child);
        await h.Db.Sys_Depts.Where(d=>d.Id==admin.Child).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Enable,false));
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(command))).Status);
        var current=await service.CreationContextAsync();Assert.DoesNotContain(current.Departments,d=>d.Id==admin.Child);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(command with{ExpectedAuthorityVersion=current.AuthorityVersion}))).Status);
        Assert.Empty(await h.Db.Machines.ToArrayAsync());Assert.Equal(0,await Operations(h));
    }

    [TenantManagementPostgresTheory]
    [InlineData(1)] [InlineData(5)]
    public async Task Machine_creation_preserves_explicit_unbound_self_and_tenant_paths(int scope)
    {
        await using var h=await Seed();var owner=await GrantMachine(h);
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="mes-machine").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,scope));
        var service=Machines(h,owner);var context=await service.CreationContextAsync();Assert.True(context.AllowsUnbound);
        var result=await service.CreateCommandAsync(await CreationCommand(service));Assert.Null(result.DepartmentId);
        await service.CreateAsync(new(){MachineCd="LEGACY",MachineName="Legacy"},"forged");
        Assert.Equal(2,await h.Db.Machines.CountAsync());Assert.Equal(0,await MachineMappings(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Machine_creation_lost_commit_ack_replays_exact_original_and_rechecks_current_query()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var owner=await GrantMachine(h);var admin=await MappingAdmin(h);
        await StandaloneScope(h,admin);var service=Machines(h,owner);var command=await CreationCommand(service,admin.Child);fault.Armed=true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(command))).Message);
        var original=await service.ReadCreationAsync(command.OperationId);Assert.Equal("COMMITTED",original.Status);
        Assert.Equal(original.Result,await service.CreateCommandAsync(command));
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>
            service.CreateCommandAsync(command with{Machine=new(){MachineCd="DIFFERENT",MachineName="Changed"}}))).Message);
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==310 && a.ActionCode=="add").ExecuteDeleteAsync();
        Assert.Equal(original.Result,await service.CreateCommandAsync(command)); // Recovery needs current query, not obsolete add.
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==310 && a.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.ReadCreationAsync(command.OperationId))).Message);
        Assert.Equal(1,await h.Db.Machines.CountAsync());Assert.Equal(1,await MachineMappings(h));Assert.Equal(1,await Operations(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Machine_creation_recovery_requires_original_actor_tenant_and_exact_object_identity()
    {
        await using var h=await Seed();var owner=await GrantMachine(h);var service=Machines(h,owner);
        var command=await CreationCommand(service);var result=await service.CreateCommandAsync(command);
        await using var foreign=await Seed();await GrantMachine(foreign);
        Assert.Equal("UNKNOWN",(await CreationService(foreign,foreign.Db).ReadCreationAsync(command.OperationId)).Status);
        var actor=new CP6.Entity.DomainModels.Sys.Sys_User{UserName="other-machine-reader",Password="unused",RoleId=72};
        h.Db.Sys_Users.Add(actor);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("ORIGINAL_OPERATION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>
            CreationService(h,h.Db,actor.Id).ReadCreationAsync(command.OperationId))).Message);
        var machine=await h.Db.Machines.AsNoTracking().SingleAsync();await h.Db.Machines.ExecuteDeleteAsync();
        machine.Id=Guid.NewGuid();h.Db.Machines.Add(machine);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("OBJECT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.ReadCreationAsync(command.OperationId))).Message);
        Assert.NotEqual(result.MachineId,await h.Db.Machines.Select(m=>m.Id).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Machine_creation_concurrent_duplicates_commit_once_and_closed_unknown_cannot_create_later()
    {
        await using var h=await Seed();var owner=await GrantMachine(h);var admin=await MappingAdmin(h);await StandaloneScope(h,admin);
        var service=Machines(h,owner);var command=await CreationCommand(service,admin.Child);
        async Task<MachineCreationResultV1> Create()
        {await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});return await CreationService(h,db).CreateCommandAsync(command);}
        var results=await Task.WhenAll(Create(),Create());Assert.Equal(results[0],results[1]);
        Assert.Equal(1,await h.Db.Machines.CountAsync());Assert.Equal(1,await MachineMappings(h));
        var closed=await CreationCommand(service,admin.Child,"LATE");Assert.Equal("UNKNOWN",(await service.ReadCreationAsync(closed.OperationId)).Status);
        Assert.Equal("CLOSED",(await service.CloseCreationAsync(closed.OperationId)).Status);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(closed))).Message);
        Assert.Equal(1,await h.Db.Machines.CountAsync());Assert.Equal(2,await Operations(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Machine_creation_final_authority_failure_rolls_back_machine_mapping_audit_and_original()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantMachine(h);var admin=await MappingAdmin(h);
        await StandaloneScope(h,admin);var service=Machines(h,owner);var command=await CreationCommand(service,admin.Child);
        var before=await h.Db.Sys_FieldAuditLogs.CountAsync();var reads=0;
        hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject") && ++reads==2)throw new IOException("creation final authority fault");return Task.CompletedTask;};
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateCommandAsync(command));hook.Before=null;
        Assert.Equal(2,reads);Assert.Empty(await h.Db.Machines.ToArrayAsync());Assert.Equal(0,await MachineMappings(h));
        Assert.Equal(0,await Operations(h));Assert.Equal(before,await h.Db.Sys_FieldAuditLogs.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Machine_creation_actual_HTTP_explicit_department_and_original_recovery()
    {
        await using var h=await Seed();await GrantMachine(h);var admin=await MappingAdmin(h);
        await StandaloneScope(h,admin);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies=await Login(host,h);
        using var context=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/mes/machines/creation-context"));
        Assert.Equal(HttpStatusCode.OK,context.StatusCode);
        var data=(await context.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.False(data.GetProperty("allowsUnbound").GetBoolean());
        var department=Assert.Single(data.GetProperty("departments").EnumerateArray());
        Assert.Equal(admin.Child,department.GetProperty("id").GetGuid());
        var operation=Guid.NewGuid();
        var input=new{operationId=operation,expectedAuthorityVersion=data.GetProperty("authorityVersion").GetString(),
            departmentId=admin.Child,machine=new{machineCd="CREATED",machineName="Explicit department",activeFlg=true,id=Guid.NewGuid()}};
        using var created=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/mes/machines/operations",input));
        Assert.Equal(HttpStatusCode.OK,created.StatusCode);
        var original=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var id=original.GetProperty("machineId").GetGuid();
        Assert.Equal(h.Tenant,await h.Db.Machines.Where(m=>m.Id==id).Select(m=>m.TenantId).SingleAsync());
        using var repeated=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/mes/machines/operations",input));
        Assert.Equal(HttpStatusCode.OK,repeated.StatusCode);
        Assert.Equal(original.GetRawText(),(await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetRawText());
        using var recovered=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"api/mes/machines/operations/{operation}"));
        Assert.Equal(HttpStatusCode.OK,recovered.StatusCode);
        var receipt=(await recovered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal("COMMITTED",receipt.GetProperty("status").GetString());
        Assert.Equal(original.GetRawText(),receipt.GetProperty("result").GetRawText());
        Assert.Single(await h.Db.Machines.ToArrayAsync());
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==310 && a.ActionCode=="query").ExecuteDeleteAsync();
        using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"api/mes/machines/operations/{operation}"));
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }
}
