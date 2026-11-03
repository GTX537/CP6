using System.Net;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Pub;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static Dictionary<string,string> FormFileHostSettings()
    {
        var root=Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!;
        return new()
        {
            ["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D"),
            ["Storage__Provider"]="Local",["Storage__LocalRoot"]=Path.Combine(root,"form-file-store"),
            ["FileContent__Defender__Enabled"]="true",
            ["FileContent__Defender__Executable"]=Environment.GetEnvironmentVariable("CP6_FILE_DEFENDER_EXE")??throw new InvalidOperationException("ACTUAL_FILE_SCANNER_REQUIRED"),
            ["FileContent__Defender__TemporaryDirectory"]=Path.Combine(root,"form-file-scan")
        };
    }
    [CurrentPermissionPostgresFact]
    public async Task FormFiles_actual_HTTP_scans_binds_downloads_exact_bytes_and_denies_current_hidden_or_forged_scope()
    {
        await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h,FileFormSchema);await QueryPermission(h);
        await RemoveFormActions(h,"add","edit","del");
        var context=await owner.ContextAsync("personal");var bytes=Encoding.UTF8.GetBytes("CP6 simulated ordinary FORM original\n原件字节\n");var input=FormUpload(context,bytes);
        Assert.Contains("SUBMIT",context.Capabilities);Assert.DoesNotContain("CREATE_DRAFT",context.Capabilities);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,FormFileHostSettings());var cookies=await Login(host,h);
        var request=OperationsRequest(cookies,HttpMethod.Post,"/api/oa/v2/personal-forms/files/uploads");
        request.Headers.Add("X-File-Command",Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(input,PersonalFormOwnerV2.Json)));request.Content=new ByteArrayContent(bytes);
        using var uploaded=await host.Send(request);var receipt=await PersonalHttpResult<FormFileOperationV2>(uploaded);var file=receipt.Result!;
        Assert.Equal("COMMITTED",receipt.State);Assert.Equal("AVAILABLE",file.Availability);Assert.Equal("MicrosoftDefender",file.Inspection.Provider);
        Assert.DoesNotContain("storePath",await uploaded.Content.ReadAsStringAsync(),StringComparison.OrdinalIgnoreCase);Assert.True(uploaded.Headers.CacheControl?.NoStore);
        using var direct=await host.Send(PersonalHttp(cookies,"/api/oa/v2/forms/personal/submissions",PersonalCommand(PersonalDirect(context,FileData(file.Reference)),"http-file-submit")));
        var submission=PersonalResult<PersonalFormSubmissionV2>(await PersonalHttpResult<PersonalFormResultV2>(direct));
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.drafts WHERE tenant_id={h.Tenant}").SingleAsync());
        var url=$"/api/oa/v2/form-data/{submission.FormDataId}/files/download";var body=new FormBoundFileReadV2("files",file.Reference);
        using var downloaded=await host.Send(BoundRequest(cookies,url,body));Assert.Equal(HttpStatusCode.OK,downloaded.StatusCode);Assert.Equal(bytes,await downloaded.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment",downloaded.Content.Headers.ContentDisposition?.DispositionType);Assert.Equal("nosniff",Assert.Single(downloaded.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(input.Digest,Assert.Single(downloaded.Headers.GetValues("X-Content-SHA256")));Assert.True(downloaded.Headers.CacheControl?.NoStore);
        using var forged=await host.Send(BoundRequest(cookies,url,body with{Field="other"}));Assert.Equal(HttpStatusCode.Forbidden,forged.StatusCode);
        var policy=(await owner.PolicyAsync("personal"))!;await owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[new(72,new(){{"files","hidden"}})],policy.RetentionPolicyRef,30,true,"revoke current HTTP attachment read"));
        using var hidden=await host.Send(BoundRequest(cookies,url,body));Assert.Equal(HttpStatusCode.Forbidden,hidden.StatusCode);Assert.DoesNotContain(file.Reference,await hidden.Content.ReadAsStringAsync());
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_files_v2.bindings WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_FORM_upload_unknown_recovery_draft_submit_and_history_download()
    {
        await using var h=await Seed();await PreparePersonal(h,FileFormSchema);await QueryPermission(h);
        await h.Db.Sys_Tenants.Where(t=>t.Id==h.Tenant).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.TimeZoneId,"America/New_York"));
        foreach(var(menu,path)in new[]{("oa-form-catalog","/oa/form-catalog"),("oa-form-search","/oa/form-search"),("oa-inbox","/oa/inbox")})
            await h.Db.Sys_Menus.Where(m=>m.MenuKey==menu).ExecuteUpdateAsync(s=>s.SetProperty(m=>m.RoutePath,path));
        using var result=await BrowserScenario(h,h.User,"oa-form-files-browser.mjs","form-files-browser",FormFileHostSettings());
        var data=await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync();Assert.Contains(result.RootElement.GetProperty("reference").GetString()!,data.DataJson);
        Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_files_v2.contents WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_files_v2.bindings WHERE tenant_id={h.Tenant}").SingleAsync());
    }
}
