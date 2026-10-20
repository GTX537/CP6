using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Actual_program_requires_current_cookie_CSRF_versions_and_preserves_atomic_user_recovery()
    {
        await using var h=await Seed();await GrantHttpPermissions(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,IdentityEnvironment(h.Tenant));
        var cookies=await Login(host,h);
        using var read=await host.Send(Request(HttpMethod.Get,"api/iam/users/editor",cookies));Assert.Equal(HttpStatusCode.OK,read.StatusCode);
        var editor=(await read.Content.ReadFromJsonAsync<IamUserEditorV1>())!;
        var command=new IamUserCommandV1{OperationId=Guid.NewGuid(),ExpectedVersion="absent",ExpectedReferencesVersion=editor.ReferencesVersion,
            Reason="actual HTTP",UserName="http-new",Password="Initial1!Password",RoleId=71,RoleIds=[71,72],DeptId=h.Child};
        using(var missingCsrf=await host.Send(Request(HttpMethod.Post,"api/iam/users",cookies,command,false)))Assert.Equal(HttpStatusCode.Forbidden,missingCsrf.StatusCode);
        using(var legacy=await host.Send(Request(HttpMethod.Post,"api/user",cookies,new {UserName="legacy",Password="Initial1!Password"})))Assert.Equal(HttpStatusCode.BadRequest,legacy.StatusCode);
        using var saved=await host.Send(Request(HttpMethod.Post,"api/iam/users",cookies,command));
        Assert.True(saved.StatusCode==HttpStatusCode.OK,await saved.Content.ReadAsStringAsync());
        var result=(await saved.Content.ReadFromJsonAsync<IamOperationV1>())!;
        using(var recovered=await host.Send(Request(HttpMethod.Get,$"api/iam/users/operations/{command.OperationId}?kind=user-create",cookies)))
        {Assert.Equal(HttpStatusCode.OK,recovered.StatusCode);Assert.Equal(result,await recovered.Content.ReadFromJsonAsync<IamOperationV1>());}
        using(var edit=await host.Send(Request(HttpMethod.Get,$"api/iam/users/editor?id={result.EntityId}",cookies)))
        {Assert.Equal(HttpStatusCode.OK,edit.StatusCode);Assert.DoesNotContain(command.Password!,await edit.Content.ReadAsStringAsync());}
        using(var roleRead=await host.Send(Request(HttpMethod.Get,$"api/pub/user-role/{result.EntityId}",cookies)))
        {
            var document=System.Text.Json.JsonDocument.Parse(await roleRead.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
            var roles=new CP6.Entity.DTOs.Sys.UserRolesDto{OperationId=Guid.NewGuid(),ExpectedVersion=document.GetProperty("expectedVersion").GetString(),
                ExpectedReferencesVersion=document.GetProperty("expectedReferencesVersion").GetString(),Reason="versioned original route",RoleIds=[72],PrimaryRoleId=72};
            using var savedRoles=await host.Send(Request(HttpMethod.Put,$"api/pub/user-role/{result.EntityId}",cookies,roles));Assert.Equal(HttpStatusCode.OK,savedRoles.StatusCode);
            Assert.Equal(72,await h.Db.Sys_Users.Where(x=>x.Id==result.EntityId).Select(x=>x.RoleId).SingleAsync());
        }
        using(var latest=await host.Send(Request(HttpMethod.Get,"api/iam/users/editor",cookies)))
        {
            var current=(await latest.Content.ReadFromJsonAsync<IamUserEditorV1>())!;
            using var compatibility=await host.Send(Request(HttpMethod.Post,"api/user",cookies,command with {OperationId=Guid.NewGuid(),UserName="versioned-original",ExpectedReferencesVersion=current.ReferencesVersion}));
            Assert.Equal(HttpStatusCode.OK,compatibility.StatusCode);
        }
        using(var wrongKind=await host.Send(Request(HttpMethod.Get,$"api/iam/users/operations/{command.OperationId}?kind=user-update",cookies)))Assert.Equal(HttpStatusCode.Conflict,wrongKind.StatusCode);
        using(var deletion=await host.Send(Request(HttpMethod.Delete,"api/user",cookies,new[]{result.EntityId!.Value})))Assert.Equal(HttpStatusCode.Conflict,deletion.StatusCode);
        Assert.True(await h.Db.Sys_Users.AnyAsync(x=>x.Id==result.EntityId));
        Assert.False(await h.Db.Sys_OperLogs.AnyAsync(x=>x.RequestBody!=null && x.RequestBody.Contains(command.Password!)));
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_program_rechecks_actor_permission_after_waiting_for_root()
    {
        await using var h=await Seed();await GrantHttpPermissions(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,IdentityEnvironment(h.Tenant));var cookies=await Login(host,h);
        using var read=await host.Send(Request(HttpMethod.Get,"api/iam/users/editor",cookies));Assert.Equal(HttpStatusCode.OK,read.StatusCode);
        var editor=(await read.Content.ReadFromJsonAsync<IamUserEditorV1>())!;
        var command=new IamUserCommandV1{OperationId=Guid.NewGuid(),ExpectedVersion="absent",ExpectedReferencesVersion=editor.ReferencesVersion,
            Reason="revoked in flight",UserName="must-not-save",Password="Initial1!Password",RoleIds=[]};
        await using var tx=await h.Db.Database.BeginTransactionAsync();await AuthenticationCompletionGateV1.AcquireAsync(h.Db);
        var attempt=host.Send(Request(HttpMethod.Post,"api/iam/users",cookies,command));
        try
        {
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE '%cp6_authentication_v1.acquire()%') AS \"Value\"").SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            await h.Db.Sys_RoleActions.Where(x=>x.RoleId==71 && x.ActionCode=="add").ExecuteDeleteAsync();
        }
        finally{await tx.CommitAsync();}
        using var response=await attempt;Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.False(await h.Db.Sys_Users.AnyAsync(x=>x.UserName=="must-not-save"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_program_without_identity_writer_refuses_effect_instead_of_claiming_propagation()
    {
        await using var h=await Seed();await GrantHttpPermissions(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        using var read=await host.Send(Request(HttpMethod.Get,"api/iam/users/editor",cookies));var editor=(await read.Content.ReadFromJsonAsync<IamUserEditorV1>())!;
        var command=new IamUserCommandV1{OperationId=Guid.NewGuid(),ExpectedVersion="absent",ExpectedReferencesVersion=editor.ReferencesVersion,
            Reason="missing writer",UserName="must-not-save",Password="Initial1!Password",RoleIds=[]};
        using var response=await host.Send(Request(HttpMethod.Post,"api/iam/users",cookies,command));Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);
        Assert.Contains("IAM_IDENTITY_WRITER_UNAVAILABLE",await response.Content.ReadAsStringAsync());Assert.Equal(1,await h.Db.Sys_Users.CountAsync());
    }

    private static async Task GrantHttpPermissions(Harness h)
    {
        var menu=await h.Db.Sys_Menus.SingleOrDefaultAsync(x=>x.MenuKey=="user");
        if(menu==null){menu=new Sys_Menu{MenuId=28101,MenuKey="user",MenuName="Users"};h.Db.Sys_Menus.Add(menu);}
        h.Db.Sys_RoleActions.AddRange(new[]{"query","add","edit","delete"}.Select(action=>new Sys_RoleAction{RoleId=71,MenuId=menu.MenuId,ActionCode=action}));
        var user=await h.Db.Sys_Users.SingleAsync(x=>x.Id==h.ActorId);user.RoleId=71;await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_versioned_user_route_cannot_disable_last_platform_administrator()
    {
        await using var h=await Seed();await GrantHttpPermissions(h);
        // Isolated fixture database, same survivor setup as PlatformAdminGuardPostgreSqlTests.
        await h.Db.Sys_Users.Where(x=>x.Id==h.ActorId).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.IsPlatformAdmin,true));
        await h.Db.Sys_Users.IgnoreQueryFilters().Where(x=>x.IsPlatformAdmin && x.Id!=h.ActorId).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.IsPlatformAdmin,false));
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,IdentityEnvironment(h.Tenant));var cookies=await Login(host,h);
        using var read=await host.Send(Request(HttpMethod.Get,$"api/iam/users/editor?id={h.ActorId}",cookies));var editor=(await read.Content.ReadFromJsonAsync<IamUserEditorV1>())!;
        var command=new IamUserCommandV1{Id=h.ActorId,UserName="iam-admin",Enable=false,RoleId=71,RoleIds=[71],OperationId=Guid.NewGuid(),
            ExpectedVersion=editor.User!.Version,ExpectedReferencesVersion=editor.ReferencesVersion,Reason="controlled survivor regression"};
        using var response=await host.Send(Request(HttpMethod.Put,"api/user",cookies,command));Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);
        Assert.Contains("E-SEC-037",await response.Content.ReadAsStringAsync());
        Assert.True(await h.Db.Sys_Users.AnyAsync(x=>x.Id==h.ActorId && x.Enable && x.IsPlatformAdmin));
    }
    internal static Dictionary<string,string> IdentityEnvironment(Guid tenant)
    {
        using var rsa=RSA.Create(2048);var hash=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        return new(){["CrmOidc__Enabled"]="true",["CrmOidc__Issuer"]="https://iam-writer.example.test",["CrmOidc__ActiveKeyId"]="fixture",
            ["CrmOidc__Keys__0__Kid"]="fixture",["CrmOidc__Keys__0__Pem"]=rsa.ExportRSAPrivateKeyPem(),
            ["CrmOidc__Clients__0__ClientId"]="CP6.Web",["CrmOidc__Clients__0__SecretSha256"]=hash,["CrmOidc__Clients__0__RedirectUris__0"]="https://iam.example.test/callback",
            ["CrmOidc__Organizations__0__TenantId"]=tenant.ToString("D"),["CrmOidc__Organizations__0__Slug"]="iam-fixture",["CrmOidc__Organizations__0__Region"]="us",["CrmOidc__Organizations__0__CrmEnabled"]="true",
            ["CrmOidc__ServiceClients__0__ClientId"]="fixture-reader",["CrmOidc__ServiceClients__0__TenantId"]=tenant.ToString("D"),["CrmOidc__ServiceClients__0__Enabled"]="true",
            ["CrmOidc__ServiceClients__0__SecretSha256"]=hash,["CrmOidc__ServiceClients__0__AllowedScopes__0"]="cp6.services",
            ["CrmIdentity__Enabled"]="true",["CrmIdentity__Tenants__"+tenant.ToString("D")]="us",["CrmIdentity__ProjectionReaderClientIds__0"]="fixture-reader"};
    }
    private static async Task<Dictionary<string,string>> Login(AuthenticationApiProcess host,Harness h)
    {
        using var response=await host.Send(new HttpRequestMessage(HttpMethod.Post,"api/auth/login")
            {Content=JsonContent.Create(new{UserName="iam-admin",Password="Iam1!Password",TenantCode="iam_"+h.Tenant.ToString("N")})});
        Assert.True(response.StatusCode==HttpStatusCode.OK,await response.Content.ReadAsStringAsync());
        return response.Headers.GetValues("Set-Cookie").Select(x=>x.Split(';')[0].Split('=',2)).Where(x=>x[1].Length>0).ToDictionary(x=>x[0],x=>x[1]);
    }
    private static HttpRequestMessage Request(HttpMethod method,string path,Dictionary<string,string> cookies,object? body=null,bool csrf=true)
    {
        var request=new HttpRequestMessage(method,path);if(body!=null)request.Content=JsonContent.Create(body);
        request.Headers.Add("Cookie",string.Join("; ",cookies.Select(x=>x.Key+"="+x.Value)));
        if(csrf)request.Headers.Add("X-CSRF-Token",cookies["cp6_csrf"]);return request;
    }
}
