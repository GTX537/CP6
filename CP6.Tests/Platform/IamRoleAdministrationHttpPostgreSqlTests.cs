using System.Net;
using System.Net.Http.Json;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Actual_role_versioned_HTTP_enforces_CSRF_whitelist_CAS_recovery_and_revoked_permission()
    {
        await using var h=await Seed();await GrantRoleHttpPermissions(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,IdentityEnvironment(h.Tenant));var cookies=await Login(host,h);
        using var read=await host.Send(Request(HttpMethod.Get,"api/iam/roles/editor",cookies));
        var editor=(await read.Content.ReadFromJsonAsync<IamRoleEditorV1>())!;
        var id=Guid.NewGuid();var foreign=Guid.NewGuid();
        var body=new{OperationId=id,RoleId=29110,RoleName="HTTP role",Enable=true,OrderNo=7,Reason="actual role HTTP",
            ExpectedVersion="absent",ExpectedReferencesVersion=editor.ReferencesVersion,TenantId=foreign,CreateDate=DateTime.MinValue,IsPlatformAdmin=true};
        using(var csrf=await host.Send(Request(HttpMethod.Post,"api/iam/roles",cookies,body,false)))Assert.Equal(HttpStatusCode.Forbidden,csrf.StatusCode);
        using(var saved=await host.Send(Request(HttpMethod.Post,"api/iam/roles",cookies,body)))
        {Assert.True(saved.StatusCode==HttpStatusCode.OK,await saved.Content.ReadAsStringAsync());Assert.Equal(29110,(await saved.Content.ReadFromJsonAsync<IamOperationV1>())!.RoleId);}
        var row=await h.Db.Sys_Roles.AsNoTracking().SingleAsync(x=>x.RoleId==29110);Assert.Equal(h.Tenant,row.TenantId);Assert.NotEqual(DateTime.MinValue,row.CreateDate);
        using(var recovered=await host.Send(Request(HttpMethod.Get,$"api/iam/roles/operations/{id}?kind=role-create",cookies)))
        {Assert.Equal(HttpStatusCode.OK,recovered.StatusCode);Assert.Equal("COMMITTED",(await recovered.Content.ReadFromJsonAsync<IamOperationV1>())!.State);}
        var command=await RoleCommand(h,29110);
        using(var mismatch=await host.Send(Request(HttpMethod.Put,"api/iam/roles/72",cookies,command)))Assert.Equal(HttpStatusCode.BadRequest,mismatch.StatusCode);
        await h.Db.Sys_Roles.Where(x=>x.RoleId==29110).ExecuteUpdateAsync(x=>x.SetProperty(r=>r.Description,"ordinary writer"));
        using(var stale=await host.Send(Request(HttpMethod.Put,"api/iam/roles/29110",cookies,command)))Assert.Equal(HttpStatusCode.PreconditionFailed,stale.StatusCode);
        var menu=await h.Db.Sys_Menus.Where(x=>x.MenuKey=="role").Select(x=>x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==71 && x.MenuId==menu && x.ActionCode=="add").ExecuteDeleteAsync();
        using(var denied=await host.Send(Request(HttpMethod.Get,$"api/iam/roles/operations/{id}?kind=role-create",cookies)))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        Assert.False(await h.Db.Sys_Users.AnyAsync(x=>x.IsPlatformAdmin));
    }
    private static async Task GrantRoleHttpPermissions(Harness h)
    {
        await GrantHttpPermissions(h);
        var menu=await h.Db.Sys_Menus.SingleOrDefaultAsync(x=>x.MenuKey=="role");
        if(menu==null){menu=new(){MenuId=29101,MenuKey="role",MenuName="Roles"};h.Db.Sys_Menus.Add(menu);}
        h.Db.Sys_RoleActions.AddRange(new[]{"query","add","edit","delete"}.Select(a=>new Sys_RoleAction{RoleId=71,MenuId=menu.MenuId,ActionCode=a}));
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
    }
    [CurrentPermissionPostgresFact]
    public async Task Actual_role_legacy_update_without_version_cannot_overwrite_current_role()
    {
        await using var h=await Seed();await GrantRoleHttpPermissions(h);
        var before=await h.Db.Sys_Roles.AsNoTracking().SingleAsync(x=>x.RoleId==72);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,IdentityEnvironment(h.Tenant));var cookies=await Login(host,h);
        using var response=await host.Send(Request(HttpMethod.Put,"api/role",cookies,new{RoleId=72,RoleName="stale overwrite",Enable=false}));
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        var after=await h.Db.Sys_Roles.AsNoTracking().SingleAsync(x=>x.RoleId==72);Assert.Equal(before.RoleName,after.RoleName);Assert.Equal(before.Enable,after.Enable);
    }
    [CurrentPermissionPostgresFact]
    public async Task Actual_role_delete_does_not_remove_referenced_identity_and_leave_dangling_primary_role()
    {
        await using var h=await Seed();await GrantRoleHttpPermissions(h);
        h.Db.Sys_Users.Add(new(){UserName="role-dependent",Password="owned",RoleId=72});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,IdentityEnvironment(h.Tenant));var cookies=await Login(host,h);
        using var response=await host.Send(Request(HttpMethod.Delete,"api/role",cookies,new[]{72}));
        Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);Assert.True(await h.Db.Sys_Roles.AnyAsync(x=>x.RoleId==72));
        Assert.Equal(72,await h.Db.Sys_Users.Where(x=>x.UserName=="role-dependent").Select(x=>x.RoleId).SingleAsync());
    }
}
