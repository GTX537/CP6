using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using CP6.Core.Services.Common;
using CP6.WebApi.BackgroundServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Mes;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<OperationsOwnerV1> GrantMachine(Harness h)
    {
        var catalog=new AuthorizationCatalogStoreV1();
        foreach(var (key,id,actions) in new[]{("mes-machine",310,new[]{"query","add","edit","del","status","downtime"}),
            ("mes-work-order",302,new[]{"query"})})
        {
            var menu=await h.Db.Sys_Menus.SingleOrDefaultAsync(m=>m.MenuId==id);
            if(menu==null)h.Db.Sys_Menus.Add(new(){MenuId=id,MenuKey=key,MenuName=key});else menu.MenuKey=key;
            foreach(var action in actions)
            {
                if(!await h.Db.Sys_MenuActions.AnyAsync(a=>a.MenuId==id && a.ActionCode==action))h.Db.Sys_MenuActions.Add(new(){MenuId=id,ActionCode=action,ActionName=action});
                h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=id,ActionCode=action});
            }
            h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey=key,ScopeType=5});
            catalog.RegisterScope(key,key,[1,2,3,4,5],1);catalog.RegisterFields(key,[]);
        }
        catalog.Seal();await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        return new(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),catalog.Capture);
    }
    private static MachineService Machines(Harness h,OperationsOwnerV1 owner,IMesNotifier? notifier=null)
        =>new(h.Db,new MesSequenceService(h.Db),notifier??Mock.Of<IMesNotifier>(),owner);
    private static async Task SeedMachine(Harness h,string code="MC1",string creator="price-owner")
    {
        h.Db.Machines.Add(new Machine{MachineCd=code,MachineName="Machine "+code,Creator=creator,Status=0,ActiveFlg=true});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
    }

    [CurrentPermissionPostgresFact]
    public async Task Machine_owner_missing_service_authority_rejects_independent_read()
    {
        await using var h=await Seed();await SeedMachine(h);
        var machine=new MachineService(h.Db,new MesSequenceService(h.Db),Mock.Of<IMesNotifier>());
        await Assert.ThrowsAnyAsync<Exception>(()=>machine.SearchAsync(new()));
    }

    [CurrentPermissionPostgresFact]
    public async Task Machine_owner_missing_service_authority_cannot_change_status_with_forged_actor()
    {
        await using var h=await Seed();await SeedMachine(h);
        var machine=new MachineService(h.Db,new MesSequenceService(h.Db),Mock.Of<IMesNotifier>());
        await Assert.ThrowsAnyAsync<Exception>(()=>machine.ChangeStatusAsync("MC1",3,"forged-admin"));
        Assert.Equal(0,await h.Db.Machines.Select(x=>x.Status).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Machine_owner_actual_HTTP_requires_current_query_action()
    {
        await using var h=await Seed();await SeedMachine(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies=await Login(host,h);
        using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/mes/machines"));
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Machine_owner_actual_HTTP_rechecks_grants_and_never_caches_protected_rows()
    {
        await using var h=await Seed();await GrantMachine(h);await SeedMachine(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        using var allowed=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/mes/machines"));
        Assert.Equal(HttpStatusCode.OK,allowed.StatusCode);Assert.True(allowed.Headers.CacheControl?.NoStore);
        Assert.Contains("MC1",await allowed.Content.ReadAsStringAsync());
        using var changed=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/mes/machines/MC1/status",new{status=2}));
        Assert.Equal(HttpStatusCode.OK,changed.StatusCode);
        var audit=await h.Db.Sys_FieldAuditLogs.AsNoTracking().Where(a=>a.EntityName=="Machine" && a.Operation==2).SingleAsync();
        Assert.Equal(h.User,audit.UserId);Assert.Equal("price-owner",audit.UserName);
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==310 && a.ActionCode=="query").ExecuteDeleteAsync();
        using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/mes/machines/downtimes"));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==310 && a.ActionCode=="status").ExecuteDeleteAsync();
        using var request=OperationsRequest(cookies,HttpMethod.Post,"api/mes/machines/MC1/status");request.Content=JsonContent.Create(new{status=3});
        using var change=await host.Send(request);Assert.Equal(HttpStatusCode.Forbidden,change.StatusCode);
        Assert.Equal(2,await h.Db.Machines.Select(m=>m.Status).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Machine_owner_native_binding_rejects_foreign_device_and_department()
    {
        await using var h=await Seed();await GrantMachine(h);await SeedMachine(h);var admin=await MappingAdmin(h);
        await using var foreign=await Seed();await SeedMachine(foreign);var foreignAdmin=await MappingAdmin(foreign);
        var foreignId=await foreign.Db.Machines.Select(m=>m.Id).SingleAsync();
        var failure=await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_operations_owner_v1.department_mappings(id,tenant_id,object_kind,object_id,department_id,enabled)
            VALUES({Guid.NewGuid()},{h.Tenant},'machine',{foreignId},{admin.Child},true)
            """));Assert.Equal("23514",failure.SqlState);
        var id=await h.Db.Machines.Select(m=>m.Id).SingleAsync();
        var mapping=await BindStandalone(h,admin,"machine",id);
        var wrongDepartment=await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_operations_owner_v1.department_mappings SET department_id={foreignAdmin.Child} WHERE id={mapping.EntityId}
            """));Assert.Equal("23514",wrongDepartment.SqlState);
    }

    [CurrentPermissionPostgresFact]
    public async Task Machine_owner_forward_migration_adds_missing_catalog_without_granting_existing_tenants()
    {
        await using var h=await Seed();await GrantMachine(h);
        var existing=await h.Db.Sys_MenuActions.Where(a=>a.MenuId==310 && a.ActionCode=="query").Select(a=>a.Id).SingleAsync();
        await using var other=await Seed();
        Assert.False(await other.Db.Sys_MenuActions.AnyAsync(a=>a.MenuId==310 && a.ActionCode=="query"));
        var migration=new CP6.Persistence.PostgreSql.Migrations.Core.MachineDepartmentOwnerV1();
        foreach(var operation in migration.UpOperations.Cast<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>())
            await h.Db.Database.ExecuteSqlRawAsync(operation.Sql);
        Assert.Equal(existing,await h.Db.Sys_MenuActions.Where(a=>a.MenuId==310 && a.ActionCode=="query").Select(a=>a.Id).SingleAsync());
        Assert.Single(await other.Db.Sys_MenuActions.Where(a=>a.MenuId==310 && a.ActionCode=="query").ToArrayAsync());
        Assert.False(await other.Db.Sys_RoleActions.AnyAsync(a=>a.MenuId==310 && a.ActionCode=="query"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Machine_owner_existing_monitor_cannot_overwrite_a_concurrent_status_change()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);await SeedMachine(h);
        await h.Db.Machines.ExecuteUpdateAsync(s=>s.SetProperty(m=>m.Status,1));
        var raced=false;
        hook.After=async command=>
        {
            if(raced || !command.CommandText.Contains("FROM \"M_Machine\"") || !command.CommandText.Contains("\"MachineName\""))return;
            raced=true;hook.After=null;
            await using var writer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
            await writer.Machines.ExecuteUpdateAsync(s=>s.SetProperty(m=>m.Status,3));
        };
        var notifier=new Mock<IMesNotifier>();using var services=new ServiceCollection().AddSingleton(h.Db).AddSingleton(notifier.Object).BuildServiceProvider();
        var monitor=new MachineStatusMonitor(services.GetRequiredService<IServiceScopeFactory>(),NullLogger<MachineStatusMonitor>.Instance);
        var scan=(Task)typeof(MachineStatusMonitor).GetMethod("ScanTenantAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(monitor,[services,CancellationToken.None])!;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>scan);Assert.True(raced);
        h.Db.ChangeTracker.Clear();Assert.Equal(3,await h.Db.Machines.Select(m=>m.Status).SingleAsync());Assert.Empty(notifier.Invocations);
    }

    [CurrentPermissionPostgresFact]
    public async Task Machine_owner_current_actor_independent_actions_and_complete_downtime_transaction()
    {
        await using var h=await Seed();var owner=await GrantMachine(h);var service=Machines(h,owner);
        await service.CreateAsync(new(){MachineCd="NEW",MachineName="New machine",ActiveFlg=true},"forged");
        Assert.Equal("price-owner",await h.Db.Machines.Select(m=>m.Creator).SingleAsync());
        var no=await service.RegisterDowntimeAsync(new(){MachineCd="NEW",StartTime=DateTime.Now,DowntimeType=2},"forged");
        Assert.Equal(2,await h.Db.Machines.Select(m=>m.Status).SingleAsync());
        Assert.Equal("price-owner",await h.Db.MachineDowntimes.Select(m=>m.Creator).SingleAsync());
        await service.CloseDowntimeAsync(no,DateTime.Now.AddMinutes(5),"recovery-label","forged");
        Assert.Equal(1,await h.Db.Machines.Select(m=>m.Status).SingleAsync());
        Assert.Equal("price-owner",await h.Db.MachineDowntimes.Select(m=>m.Modifier).SingleAsync());
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==310 && a.ActionCode=="status").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.ChangeStatusAsync("NEW",3,"forged"))).Message);
        Assert.Single(await service.SearchAsync(new()));
    }

    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Machine_owner_explicit_department_binding_filters_before_downtime_count_and_rechecks_disabled_ancestry(int scope)
    {
        await using var h=await Seed();var owner=await GrantMachine(h);await SeedMachine(h);await SeedMachine(h,"OTHER");
        var service=Machines(h,owner);
        await service.RegisterDowntimeAsync(new(){MachineCd="MC1",DowntimeType=2,EndTime=DateTime.Now},null);
        await service.RegisterDowntimeAsync(new(){MachineCd="OTHER",DowntimeType=2,EndTime=DateTime.Now},null);
        var admin=await MappingAdmin(h);await StandaloneScope(h,admin,scope);
        Assert.Empty(await service.SearchAsync(new()));
        var id=await h.Db.Machines.Where(m=>m.MachineCd=="MC1").Select(m=>m.Id).SingleAsync();
        Assert.Single(await StandaloneAdmin(h,admin).FindObjectsAsync("machine","MC1",_=>Task.FromResult(h.User),default));
        await BindStandalone(h,admin,"machine",id);
        Assert.Equal("MC1",Assert.Single(await service.SearchAsync(new())).MachineCd);
        var page=await service.SearchDowntimesAsync(new(){PageSize=1});Assert.Equal(1,page.Total);Assert.Equal("MC1",Assert.Single(page.Items).MachineCd);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.ChangeStatusAsync("OTHER",3,null));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateAsync(new(){MachineCd="UNBOUND",MachineName="unbound"},null));
        Assert.False(await h.Db.Machines.AnyAsync(m=>m.MachineCd=="UNBOUND"));
        await h.Db.Sys_Depts.Where(d=>d.Id==admin.Root).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Enable,false));
        if(scope==4)Assert.Empty(await service.SearchAsync(new()));
        else await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.SearchAsync(new()));
    }

    [CurrentPermissionPostgresFact]
    public async Task Machine_owner_self_scope_and_linked_work_order_never_inherit_machine_authority()
    {
        await using var h=await Seed();var owner=await GrantMachine(h);await SeedMachine(h);await SeedMachine(h,"OTHER","other");
        h.Db.WorkOrders.Add(new(){WorkOrderNo="WO-MACHINE",ProductCd="P1",Creator="price-owner"});
        h.Db.WorkOrderProcesses.Add(new(){WorkOrderNo="WO-MACHINE",ProcessCd="P",TaskCd="T",MachineCd="MC1",ProcessStatus=1,Creator="price-owner"});
        h.Db.OeeDailies.Add(new(){MachineCd="MC1",OeeDate=DateTime.Today,Oee=99m});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="mes-machine").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
        var service=Machines(h,owner);var row=Assert.Single(await service.SearchAsync(new()));
        Assert.Equal("WO-MACHINE",row.CurrentWorkOrderNo);Assert.Null(row.TodayOee);
        await service.RegisterDowntimeAsync(new(){MachineCd="MC1",WorkOrderNo="WO-MACHINE",EndTime=DateTime.Now},null);
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==302 && a.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Null(Assert.Single(await service.SearchAsync(new())).CurrentWorkOrderNo);
        Assert.Equal(0,(await service.SearchDowntimesAsync(new())).Total);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.RegisterDowntimeAsync(new(){MachineCd="MC1",WorkOrderNo="WO-MACHINE"},null));
        Assert.Equal(1,await h.Db.MachineDowntimes.CountAsync());
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Machine_owner_final_failure_rolls_back_sequence_downtime_and_status_but_unknown_commit_sends_no_hint(bool lostAck)
    {
        var hook=new CommandHook();var commit=new CommitFault();await using var h=await Seed(hook,commit);
        var owner=await GrantMachine(h);await SeedMachine(h);var notifier=new Mock<IMesNotifier>();var service=Machines(h,owner,notifier.Object);
        var audits=await h.Db.Sys_FieldAuditLogs.CountAsync();
        var reads=0;
        if(lostAck)commit.Armed=true;
        else hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject") && ++reads==2)throw new IOException("machine final authority unavailable");return Task.CompletedTask;};
        var error=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.RegisterDowntimeAsync(new(){MachineCd="MC1",DowntimeType=2},"forged"));
        hook.Before=null;Assert.Equal(lostAck?"OPERATIONS_OUTCOME_UNKNOWN":"AUTHORITY_FENCE_UNAVAILABLE",error.Message);
        Assert.Equal(lostAck?1:0,await h.Db.MachineDowntimes.CountAsync());
        Assert.Equal(lostAck?2:0,await h.Db.Machines.Select(m=>m.Status).SingleAsync());
        Assert.Empty(notifier.Invocations);
        if(!lostAck)
        {
            Assert.False(await h.Db.MesSequences.AnyAsync(s=>s.SeqKey=="DT"));
            Assert.Equal(audits,await h.Db.Sys_FieldAuditLogs.CountAsync());
        }
        else Assert.True(await h.Db.Sys_FieldAuditLogs.CountAsync()>audits);
    }
}
