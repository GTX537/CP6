using System.Net;
using System.Net.Http.Json;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Platform;

// Reuses the real authenticated process fixture and current cookie/CSRF chain from user administration.
public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Actual_program_organization_routes_require_versions_preview_CSRF_and_preserve_exact_recovery()
    {
        await using var h=await Seed();await GrantHttpPermissions(h);
        var menu=await h.Db.Sys_Menus.SingleOrDefaultAsync(x=>x.MenuKey=="pub-dept");
        if(menu==null){menu=new(){MenuId=28201,MenuKey="pub-dept",MenuName="Departments"};h.Db.Sys_Menus.Add(menu);}
        h.Db.Sys_RoleActions.AddRange(new[]{"query","add","edit","delete"}.Select(a=>new Sys_RoleAction{RoleId=71,MenuId=menu.MenuId,ActionCode=a}));await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,IdentityEnvironment(h.Tenant));var cookies=await Login(host,h);
        using var read=await host.Send(Request(HttpMethod.Get,"api/iam/departments/editor",cookies));Assert.Equal(HttpStatusCode.OK,read.StatusCode);
        var editor=(await read.Content.ReadFromJsonAsync<IamDepartmentEditorV1>())!;var suppliedId=Guid.NewGuid();
        var create=new {Id=suppliedId,TenantId=Guid.NewGuid(),Path="/forged/",OperationId=Guid.NewGuid(),ExpectedVersion="absent",
            editor.ReferencesVersion,ExpectedReferencesVersion=editor.ReferencesVersion,ExpectedTreeVersion=editor.TreeVersion,Reason="actual organization HTTP",DeptCode="http-destination",DeptName="destination"};
        using(var csrf=await host.Send(Request(HttpMethod.Post,"api/iam/departments",cookies,create,false)))Assert.Equal(HttpStatusCode.Forbidden,csrf.StatusCode);
        using(var old=await host.Send(Request(HttpMethod.Post,"api/pub/dept",cookies,new{DeptCode="no-version",DeptName="closed"})))Assert.Equal(HttpStatusCode.BadRequest,old.StatusCode);
        using var saved=await host.Send(Request(HttpMethod.Post,"api/iam/departments",cookies,create));Assert.True(saved.StatusCode==HttpStatusCode.OK,await saved.Content.ReadAsStringAsync());
        var result=(await saved.Content.ReadFromJsonAsync<IamOperationV1>())!;Assert.NotEqual(suppliedId,result.EntityId);
        Assert.Equal($"/{result.EntityId}/",await h.Db.Sys_Depts.Where(x=>x.Id==result.EntityId).Select(x=>x.Path).SingleAsync());
        using var previewRead=await host.Send(Request(HttpMethod.Get,$"api/iam/departments/{h.Child}/preview?parentId={result.EntityId}",cookies));Assert.Equal(HttpStatusCode.OK,previewRead.StatusCode);
        var preview=(await previewRead.Content.ReadFromJsonAsync<IamDepartmentPreviewV1>())!;
        var operation=Guid.NewGuid();var move=new{OperationId=operation,ExpectedVersion=preview.Version,ExpectedTreeVersion=preview.TreeVersion,
            ExpectedReferencesVersion=preview.ReferencesVersion,preview.PreviewVersion,NewParentId=result.EntityId,Reason="versioned legacy move"};
        using var moved=await host.Send(Request(HttpMethod.Post,$"api/pub/dept/{h.Child}/move",cookies,move));Assert.True(moved.StatusCode==HttpStatusCode.OK,await moved.Content.ReadAsStringAsync());
        var movedResult=(await moved.Content.ReadFromJsonAsync<IamOperationV1>())!;
        using(var recovery=await host.Send(Request(HttpMethod.Get,$"api/iam/departments/operations/{operation}?kind=dept-move",cookies)))Assert.Equal(movedResult,await recovery.Content.ReadFromJsonAsync<IamOperationV1>());
        using(var deletion=await host.Send(Request(HttpMethod.Delete,$"api/pub/dept/{h.Child}",cookies)))Assert.Equal(HttpStatusCode.Conflict,deletion.StatusCode);
        using(var references=await host.Send(Request(HttpMethod.Get,$"api/iam/departments/{h.Child}/references",cookies)))Assert.False((await references.Content.ReadFromJsonAsync<IamDepartmentReferencesV1>())!.DeletionAllowed);
        var unknown=Guid.NewGuid();
        using(var close=await host.Send(Request(HttpMethod.Post,$"api/iam/departments/operations/{unknown}/close?kind=dept-move",cookies)))Assert.Equal("CLOSED",(await close.Content.ReadFromJsonAsync<IamOperationV1>())!.State);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==71 && x.MenuId==menu.MenuId && x.ActionCode=="query").ExecuteDeleteAsync();
        using var forbidden=await host.Send(Request(HttpMethod.Get,"api/iam/departments/editor",cookies));Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
    }
}
