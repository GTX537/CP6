using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs.Sys;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private static int policyMenuId=29200;
    private static (IamRolePolicyAdministrationV1 Service,IamAdministrationV1 Authority) PolicySession(Harness h)
    {
        var permission=Mock.Of<ICurrentPermissionContext>(x=>x.GetAsync()==Task.FromResult(new UserPermissionContext{UserId=h.ActorId,
            ActionKeys=new(){"pub-role-perm:query","pub-role-perm:edit","pub-data-scope:query","pub-data-scope:edit","pub-field-perm:query","pub-field-perm:edit"}}));
        var authority=new IamAdministrationV1(h.Db,permission,new EphemeralDataProtectionProvider());return(new(h.Db,authority),authority);
    }
    private static async Task<(int Menu,string Resource)> PolicyDomain(Harness h)
    {
        var menu=Interlocked.Increment(ref policyMenuId);var resource="crm-iam-"+h.Tenant.ToString("N");
        DataScopeRegistry.Register(resource,"Owned resource",[1,2,3,4,5],1);
        FieldRegistry.Register(resource,new FieldRegistry.Field("Amount","Amount"),new FieldRegistry.Field("Name","Name"));
        h.Db.Sys_Menus.Add(new(){MenuId=menu,MenuName="owned",MenuKey=resource});
        h.Db.Sys_MenuActions.AddRange(new Sys_MenuAction{MenuId=menu,ActionCode="read",ActionName="Read"},new Sys_MenuAction{MenuId=menu,ActionCode="export",ActionName="Export"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();return(menu,resource);
    }
    private static async Task<IamRolePolicyCommandV1> PolicyCommand(Harness h,IamRolePolicyAdministrationV1 service,string mode,int role=72)
    {
        var e=await service.ReadAsync(mode,role,h.Actor,default);
        return new(){OperationId=Guid.NewGuid(),ExpectedVersion=e.Version,ExpectedReferencesVersion=e.ReferencesVersion,ExpectedCatalogVersion=e.Catalog.Version,
            Reason="owned policy command",MenuIds=e.Function.MenuIds,Actions=e.Function.Actions,
            Scopes=e.Catalog.Scopes.Select(x=>new RoleDataScopeDto{ResourceKey=x.Key,ScopeType=x.Default}).ToList()};
    }
    [CurrentPermissionPostgresFact]
    public async Task Policy_actions_use_registered_tenant_catalog_and_exact_original_route_identity()
    {
        await using var h=await Seed();var s=PolicySession(h);var domain=await PolicyDomain(h);
        var command=(await PolicyCommand(h,s.Service,"actions")) with{MenuIds=[domain.Menu],Actions=[new(){MenuId=domain.Menu,ActionCode="read"}]};
        var result=await s.Service.ChangeAsync("actions",72,command,h.Actor,default);
        Assert.Equal("COMMITTED",result.State);Assert.Equal(72,result.RoleId);
        Assert.True(await h.Db.Sys_RoleActions.AnyAsync(x=>x.RoleId==72 && x.MenuId==domain.Menu && x.ActionCode=="read"));
        Assert.Equal(result,await s.Service.ChangeAsync("actions",72,command,h.Actor,default));
        Assert.Equal("IAM_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("actions",71,command,h.Actor,default))).Message);
        Assert.Equal(result,await s.Authority.Resolve(command.OperationId,"role-policy-actions",false,h.Actor,default));
        var invalid=(await PolicyCommand(h,s.Service,"actions")) with{MenuIds=[domain.Menu],Actions=[new(){MenuId=domain.Menu,ActionCode="unknown"}]};
        Assert.Equal("IAM_ACTION_UNREGISTERED",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("actions",72,invalid,h.Actor,default))).Message);
        Assert.Equal("E-PUB-021",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("actions",72,invalid with{MenuIds=[]},h.Actor,default))).Message);
        var duplicate=invalid with{Actions=[new(){MenuId=domain.Menu,ActionCode="read"},new(){MenuId=domain.Menu,ActionCode="read"}]};
        Assert.Equal(400,(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("actions",72,duplicate,h.Actor,default))).Status);
        var missing=invalid with{MenuIds=[int.MinValue],Actions=[]};
        Assert.Equal("IAM_MENU_UNAVAILABLE",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("actions",72,missing,h.Actor,default))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Policy_scope_full_matrix_rejects_unknown_domain_foreign_or_disabled_department_and_invalid_type()
    {
        await using var h=await Seed();var s=PolicySession(h);var domain=await PolicyDomain(h);
        var command=await PolicyCommand(h,s.Service,"scopes");
        command.Scopes!.Single(x=>x.ResourceKey==domain.Resource).ScopeType=4;
        command.Scopes.Single(x=>x.ResourceKey==domain.Resource).CustomDeptIds=[h.Child,h.Child];
        await s.Service.ChangeAsync("scopes",72,command,h.Actor,default);
        Assert.Equal(h.Child.ToString("D"),await h.Db.Sys_RoleDataScopes.Where(x=>x.RoleId==72 && x.ResourceKey==domain.Resource).Select(x=>x.CustomDeptIds).SingleAsync());
        var invalid=await PolicyCommand(h,s.Service,"scopes");invalid.Scopes!.Add(new(){ResourceKey="unregistered",ScopeType=99});
        Assert.Equal(400,(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("scopes",72,invalid,h.Actor,default))).Status);
        invalid=await PolicyCommand(h,s.Service,"scopes");invalid.Scopes!.Single(x=>x.ResourceKey==domain.Resource).ScopeType=99;
        Assert.Equal("E-PUB-031",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("scopes",72,invalid,h.Actor,default))).Message);
        invalid=await PolicyCommand(h,s.Service,"scopes");var row=invalid.Scopes!.Single(x=>x.ResourceKey==domain.Resource);row.ScopeType=4;
        Assert.Equal("E-PUB-032",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("scopes",72,invalid,h.Actor,default))).Message);
        row.CustomDeptIds=[Guid.NewGuid()];
        Assert.Equal("IAM_DEPARTMENT_UNAVAILABLE",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("scopes",72,invalid,h.Actor,default))).Message);
        await h.Db.Sys_Depts.Where(x=>x.Id==h.Parent).ExecuteUpdateAsync(x=>x.SetProperty(d=>d.Enable,false));
        invalid=await PolicyCommand(h,s.Service,"scopes");row=invalid.Scopes!.Single(x=>x.ResourceKey==domain.Resource);row.ScopeType=4;row.CustomDeptIds=[h.Child];
        Assert.Equal("IAM_DEPARTMENT_UNAVAILABLE",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("scopes",72,invalid,h.Actor,default))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Policy_fields_require_complete_one_resource_matrix_and_default_clear_preserves_other_resources()
    {
        await using var h=await Seed();var s=PolicySession(h);var domain=await PolicyDomain(h);
        var other=domain.Resource+"-other";FieldRegistry.Register(other,new FieldRegistry.Field("Cost","Cost"));
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey=other,FieldName="Cost",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        IamRolePolicyCommandV1 Fields(IamRolePolicyCommandV1 c,int access)=>c with{ResourceKey=domain.Resource,
            Fields=[new(){ResourceKey=domain.Resource,FieldName="Amount",Access=access},new(){ResourceKey=domain.Resource,FieldName="Name",Access=1}]};
        var command=Fields(await PolicyCommand(h,s.Service,"fields"),2);
        await s.Service.ChangeAsync("fields",72,command,h.Actor,default);
        Assert.Equal(2,await h.Db.Sys_RoleFieldPerms.CountAsync(x=>x.RoleId==72));
        foreach(var access in new[]{0,4})
        {
            command=Fields(await PolicyCommand(h,s.Service,"fields"),access);
            Assert.Equal("IAM_FIELD_MATRIX_INCOMPLETE",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("fields",72,command,h.Actor,default))).Message);
        }
        command=Fields(await PolicyCommand(h,s.Service,"fields"),1);
        Assert.Equal(400,(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("fields",72,command with{Fields=[]},h.Actor,default))).Status);
        Assert.Equal(400,(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("fields",72,command with{Fields=[command.Fields![0],command.Fields[0]]},h.Actor,default))).Status);
        await s.Service.ChangeAsync("fields",72,command,h.Actor,default);
        var retained=await h.Db.Sys_RoleFieldPerms.Where(x=>x.RoleId==72).SingleAsync();Assert.Equal(other,retained.ResourceKey);Assert.Equal(3,retained.Access);
    }
    [CurrentPermissionPostgresFact]
    public async Task Policy_catalog_raw_writer_ABA_and_new_registration_invalidate_pending_commands()
    {
        await using var h=await Seed();var s=PolicySession(h);var domain=await PolicyDomain(h);var command=await PolicyCommand(h,s.Service,"actions");
        await h.Db.Sys_MenuActions.Where(x=>x.MenuId==domain.Menu).ExecuteUpdateAsync(x=>x.SetProperty(a=>a.ActionName,"changed"));
        await h.Db.Sys_MenuActions.Where(x=>x.MenuId==domain.Menu && x.ActionCode=="read").ExecuteUpdateAsync(x=>x.SetProperty(a=>a.ActionName,"Read"));
        await h.Db.Sys_MenuActions.Where(x=>x.MenuId==domain.Menu && x.ActionCode=="export").ExecuteUpdateAsync(x=>x.SetProperty(a=>a.ActionName,"Export"));
        Assert.Equal(412,(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("actions",72,command,h.Actor,default))).Status);
        command=await PolicyCommand(h,s.Service,"actions");
        await h.Db.Sys_Menus.Where(x=>x.MenuId==domain.Menu).ExecuteUpdateAsync(x=>x.SetProperty(a=>a.MenuName,"other"));
        await h.Db.Sys_Menus.Where(x=>x.MenuId==domain.Menu).ExecuteUpdateAsync(x=>x.SetProperty(a=>a.MenuName,"owned"));
        Assert.Equal(412,(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("actions",72,command,h.Actor,default))).Status);
        command=await PolicyCommand(h,s.Service,"fields");
        FieldRegistry.Register(domain.Resource,new FieldRegistry.Field("NewField","New"));
        Assert.Equal(412,(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("fields",72,command,h.Actor,default))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Policy_receipt_fault_rolls_back_permissions_identity_audit_and_versions()
    {
        var fault=new ReceiptFault();await using var h=await Seed(fault);var s=PolicySession(h);var domain=await PolicyDomain(h);
        var command=(await PolicyCommand(h,s.Service,"actions")) with{MenuIds=[domain.Menu],Actions=[new(){MenuId=domain.Menu,ActionCode="read"}]};
        var before=await h.Db.CrmIdentitySnapshots.Where(x=>x.TenantId==h.Tenant && x.AggregateId=="permission:role:72").Select(x=>x.Version).SingleAsync();
        fault.Armed=true;await Assert.ThrowsAsync<IOException>(()=>s.Service.ChangeAsync("actions",72,command,h.Actor,default));Assert.True(fault.Fired);
        Assert.False(await h.Db.Sys_RoleActions.AnyAsync(x=>x.RoleId==72));Assert.Equal(command.ExpectedVersion,await s.Authority.Version("role:72",default));
        Assert.Equal(before,await h.Db.CrmIdentitySnapshots.Where(x=>x.TenantId==h.Tenant && x.AggregateId=="permission:role:72").Select(x=>x.Version).SingleAsync());
        Assert.False(await h.Db.Sys_SecurityLogs.AnyAsync(x=>x.EventType==(int)SecurityEventType.IamAdministrationChanged));
        Assert.Equal("UNKNOWN",(await s.Authority.Resolve(command.OperationId,"role-policy-actions",false,h.Actor,default)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task Policy_menu_action_maintenance_is_versioned_and_cannot_delete_granted_definition()
    {
        await using var h=await Seed();var s=PolicySession(h);var domain=await PolicyDomain(h);
        var permissions=(await PolicyCommand(h,s.Service,"actions")) with{MenuIds=[domain.Menu],Actions=[new(){MenuId=domain.Menu,ActionCode="read"}]};
        await s.Service.ChangeAsync("actions",72,permissions,h.Actor,default);
        var e=await s.Service.ReadMenuActionsAsync(domain.Menu,h.Actor,default);
        var command=new IamMenuActionCommandV1{OperationId=Guid.NewGuid(),MenuId=domain.Menu,ExpectedVersion=e.Version,ExpectedReferencesVersion=e.ReferencesVersion,
            ExpectedCatalogVersion=e.CatalogVersion,Reason="catalog edit",Actions=[]};
        Assert.Equal("IAM_MENU_ACTION_REFERENCED",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeMenuActionsAsync(command,h.Actor,default))).Message);
        command=command with{Actions=[new(){ActionCode="read",ActionName=""}]};
        var result=await s.Service.ChangeMenuActionsAsync(command,h.Actor,default);
        Assert.Equal(result,await s.Authority.Resolve(command.OperationId,"menu-actions",false,h.Actor,default));
        Assert.Equal("read",await h.Db.Sys_MenuActions.Where(x=>x.MenuId==domain.Menu).Select(x=>x.ActionCode).SingleAsync());
        Assert.Equal("",await h.Db.Sys_MenuActions.Where(x=>x.MenuId==domain.Menu).Select(x=>x.ActionName).SingleAsync());
        Assert.Equal(412,(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("actions",72,permissions with{OperationId=Guid.NewGuid()},h.Actor,default))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Policy_new_menu_action_writer_waits_for_actual_root_and_changes_catalog_revision()
    {
        await using var h=await Seed();var s=PolicySession(h);var domain=await PolicyDomain(h);
        var before=await s.Authority.Version("permission-catalog",default);
        await using var writer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});await writer.Database.OpenConnectionAsync();
        var pid=await writer.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
        await using var tx=await h.Db.Database.BeginTransactionAsync();await AuthenticationCompletionGateV1.AcquireAsync(h.Db);
        var pending=writer.Sys_MenuActions.Where(x=>x.MenuId==domain.Menu).ExecuteUpdateAsync(x=>x.SetProperty(a=>a.Sort,8));
        try
        {
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while(!await observer.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE pid={pid} AND wait_event_type='Lock') AS \"Value\"").SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            Assert.False(pending.IsCompleted);
        }
        finally{await tx.CommitAsync();}
        await pending;Assert.NotEqual(before,await s.Authority.Version("permission-catalog",default));
    }
    [CurrentPermissionPostgresFact]
    public async Task Policy_lost_commit_ack_retains_exact_receipt_and_closed_operation_blocks_late_write()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var s=PolicySession(h);var domain=await PolicyDomain(h);
        var command=(await PolicyCommand(h,s.Service,"actions")) with{MenuIds=[domain.Menu],Actions=[new(){MenuId=domain.Menu,ActionCode="read"}]};
        fault.Armed=true;
        Assert.Equal("IAM_OPERATION_UNKNOWN",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("actions",72,command,h.Actor,default))).Message);
        var original=await s.Authority.Resolve(command.OperationId,"role-policy-actions",true,h.Actor,default);Assert.Equal("COMMITTED",original.State);
        Assert.Equal(original,await s.Service.ChangeAsync("actions",72,command,h.Actor,default));
        var next=await PolicyCommand(h,s.Service,"actions");
        await s.Authority.Resolve(next.OperationId,"role-policy-actions",true,h.Actor,default);
        Assert.Equal("IAM_OPERATION_CLOSED",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("actions",72,next,h.Actor,default))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Policy_saved_configuration_preserves_union_MAX_and_explicit_MIN_before_default()
    {
        await using var h=await Seed();var s=PolicySession(h);var domain=await PolicyDomain(h);
        await h.Db.Sys_Users.Where(x=>x.Id==h.ActorId).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.RoleId,72));
        h.Db.Sys_UserRoles.Add(new(){UserId=h.ActorId,RoleId=71});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        foreach(var role in new[]{71,72})
        {
            var command=(await PolicyCommand(h,s.Service,"actions",role)) with{MenuIds=[domain.Menu],Actions=[new(){MenuId=domain.Menu,ActionCode=role==71?"export":"read"}]};
            await s.Service.ChangeAsync("actions",role,command,h.Actor,default);
            command=await PolicyCommand(h,s.Service,"scopes",role);var scope=command.Scopes!.Single(x=>x.ResourceKey==domain.Resource);scope.ScopeType=role==71?5:4;
            scope.CustomDeptIds=role==71?[]:[h.Child];await s.Service.ChangeAsync("scopes",role,command,h.Actor,default);
        }
        var fields=(await PolicyCommand(h,s.Service,"fields")) with{ResourceKey=domain.Resource,
            Fields=[new(){ResourceKey=domain.Resource,FieldName="Amount",Access=3},new(){ResourceKey=domain.Resource,FieldName="Name",Access=1}]};
        await s.Service.ChangeAsync("fields",72,fields,h.Actor,default);
        var effective=await new PermissionAggregator(h.Db).BuildAsync(h.ActorId);
        Assert.Contains(domain.Resource+":read",effective.ActionKeys);Assert.Contains(domain.Resource+":export",effective.ActionKeys);
        Assert.Equal(5,effective.DataScopes[domain.Resource]);Assert.Equal(3,effective.FieldPerms[domain.Resource]["Amount"]);
        fields=(await PolicyCommand(h,s.Service,"fields",71)) with{ResourceKey=domain.Resource,
            Fields=[new(){ResourceKey=domain.Resource,FieldName="Amount",Access=2},new(){ResourceKey=domain.Resource,FieldName="Name",Access=1}]};
        await s.Service.ChangeAsync("fields",71,fields,h.Actor,default);
        effective=await new PermissionAggregator(h.Db).BuildAsync(h.ActorId);Assert.Equal(2,effective.FieldPerms[domain.Resource]["Amount"]);
    }
}
