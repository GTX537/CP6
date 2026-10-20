using System.Net;
using System.Net.Http.Json;
using CP6.Core.Services.Sys;
using CP6.Entity.DTOs.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private static async Task GrantPolicyHttpPermissions(Harness h)
    {
        await GrantRoleHttpPermissions(h);
        foreach(var resource in new[]{"pub-role-perm","pub-data-scope","pub-field-perm"})
        {
            var menu=await h.Db.Sys_Menus.SingleOrDefaultAsync(x=>x.MenuKey==resource);
            if(menu==null){menu=new(){MenuId=Interlocked.Increment(ref policyMenuId),MenuKey=resource,MenuName=resource};h.Db.Sys_Menus.Add(menu);}
            foreach(var action in new[]{"query","edit"})h.Db.Sys_RoleActions.Add(new(){RoleId=71,MenuId=menu.MenuId,ActionCode=action});
        }
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
    }
    [CurrentPermissionPostgresFact]
    public async Task Actual_policy_HTTP_closes_legacy_bypasses_checks_CSRF_and_recovers_exact_target()
    {
        await using var h=await Seed();await GrantPolicyHttpPermissions(h);var domain=await PolicyDomain(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,IdentityEnvironment(h.Tenant));var cookies=await Login(host,h);
        using var read=await host.Send(Request(HttpMethod.Get,"api/iam/role-policies/actions/editor?roleId=72",cookies));
        Assert.Equal(HttpStatusCode.OK,read.StatusCode);var e=(await read.Content.ReadFromJsonAsync<IamRolePolicyEditorV1>())!;
        var command=new IamRolePolicyCommandV1{OperationId=Guid.NewGuid(),ExpectedVersion=e.Version,ExpectedReferencesVersion=e.ReferencesVersion,
            ExpectedCatalogVersion=e.Catalog.Version,Reason="actual HTTP",MenuIds=[domain.Menu],Actions=[new(){MenuId=domain.Menu,ActionCode="read"}]};
        using(var missing=await host.Send(Request(HttpMethod.Put,"api/iam/role-policies/actions/72",cookies,command,false)))Assert.Equal(HttpStatusCode.Forbidden,missing.StatusCode);
        using(var legacy=await host.Send(Request(HttpMethod.Put,"api/pub/role-perm/72",cookies,new RolePermDto{MenuIds=[domain.Menu]})))Assert.Equal(HttpStatusCode.BadRequest,legacy.StatusCode);
        using(var menus=await host.Send(Request(HttpMethod.Post,"api/role/72/menus",cookies,new[]{domain.Menu})))Assert.Equal(HttpStatusCode.BadRequest,menus.StatusCode);
        using(var definitions=await host.Send(Request(HttpMethod.Put,$"api/pub/role-perm/menu-action/{domain.Menu}",cookies,new[]{new MenuActionDto{ActionCode="read",ActionName="Read"}})))Assert.Equal(HttpStatusCode.BadRequest,definitions.StatusCode);
        using(var saved=await host.Send(Request(HttpMethod.Put,"api/iam/role-policies/actions/72",cookies,command)))
        {Assert.True(saved.StatusCode==HttpStatusCode.OK,await saved.Content.ReadAsStringAsync());Assert.Equal(72,(await saved.Content.ReadFromJsonAsync<IamOperationV1>())!.RoleId);}
        using(var cross=await host.Send(Request(HttpMethod.Put,"api/iam/role-policies/actions/71",cookies,command)))Assert.Equal(HttpStatusCode.Conflict,cross.StatusCode);
        using(var original=await host.Send(Request(HttpMethod.Get,$"api/iam/role-policies/operations/{command.OperationId}?kind=role-policy-actions",cookies)))
        {Assert.Equal(HttpStatusCode.OK,original.StatusCode);Assert.Equal("COMMITTED",(await original.Content.ReadFromJsonAsync<IamOperationV1>())!.State);}
        using var catalogRead=await host.Send(Request(HttpMethod.Get,$"api/iam/role-policies/menu-actions/{domain.Menu}/editor",cookies));
        var catalog=(await catalogRead.Content.ReadFromJsonAsync<IamMenuActionEditorV1>())!;
        var change=new IamMenuActionCommandV1{MenuId=domain.Menu,OperationId=Guid.NewGuid(),ExpectedVersion=catalog.Version,
            ExpectedReferencesVersion=catalog.ReferencesVersion,ExpectedCatalogVersion=catalog.CatalogVersion,Reason="actual definition maintenance",
            Actions=[new(){ActionCode="read",ActionName="Read revised"}]};
        using(var updated=await host.Send(Request(HttpMethod.Put,$"api/iam/role-policies/menu-actions/{domain.Menu}",cookies,change)))Assert.Equal(HttpStatusCode.OK,updated.StatusCode);
        var grantMenu=await h.Db.Sys_Menus.Where(x=>x.MenuKey=="pub-role-perm").Select(x=>x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==71 && x.MenuId==grantMenu && x.ActionCode=="edit").ExecuteDeleteAsync();
        using(var denied=await host.Send(Request(HttpMethod.Get,$"api/iam/role-policies/operations/{command.OperationId}?kind=role-policy-actions",cookies)))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }
    [CurrentPermissionPostgresFact]
    public async Task Actual_policy_HTTP_rejects_unknown_resource_and_invalid_field_level_with_real_catalog()
    {
        await using var h=await Seed();await GrantPolicyHttpPermissions(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,IdentityEnvironment(h.Tenant));var cookies=await Login(host,h);
        using var read=await host.Send(Request(HttpMethod.Get,"api/iam/role-policies/scopes/editor?roleId=72",cookies));
        var e=(await read.Content.ReadFromJsonAsync<IamRolePolicyEditorV1>())!;
        var scopes=e.Catalog.Scopes.Select(x=>new RoleDataScopeDto{ResourceKey=x.Key,ScopeType=x.Default}).ToList();scopes.Add(new(){ResourceKey="not-registered",ScopeType=5});
        var command=new IamRolePolicyCommandV1{OperationId=Guid.NewGuid(),ExpectedVersion=e.Version,ExpectedReferencesVersion=e.ReferencesVersion,
            ExpectedCatalogVersion=e.Catalog.Version,Reason="invalid HTTP resource",Scopes=scopes};
        using(var invalid=await host.Send(Request(HttpMethod.Put,"api/iam/role-policies/scopes/72",cookies,command)))Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        using var fieldsRead=await host.Send(Request(HttpMethod.Get,"api/iam/role-policies/fields/editor?roleId=72",cookies));
        var fields=(await fieldsRead.Content.ReadFromJsonAsync<IamRolePolicyEditorV1>())!;var resource=fields.Catalog.Fields.First(x=>x.Fields.Count>0);
        command=new(){OperationId=Guid.NewGuid(),ExpectedVersion=fields.Version,ExpectedReferencesVersion=fields.ReferencesVersion,
            ExpectedCatalogVersion=fields.Catalog.Version,Reason="invalid HTTP field access",ResourceKey=resource.Key,
            Fields=resource.Fields.Select(x=>new RoleFieldPermDto{ResourceKey=resource.Key,FieldName=x.Name,Access=0}).ToList()};
        using(var invalid=await host.Send(Request(HttpMethod.Put,"api/iam/role-policies/fields/72",cookies,command)))Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        Assert.False(await h.Db.Sys_RoleFieldPerms.AnyAsync(x=>x.RoleId==72));
    }
}
