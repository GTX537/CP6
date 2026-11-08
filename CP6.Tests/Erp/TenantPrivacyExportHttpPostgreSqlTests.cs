using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task TenantPrivacyExport_actual_HTTP_two_sessions_strict_scope_original_and_current_recipient_download_are_separate_from_subject_artifacts()
    {
        await using var h=await Seed();var recipient=await TenantPrivacyRecipient(h);await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,PrivacyExecutionEnvironment(h.Tenant));
        var cookies=await Login(host,h);var recipientCookies=await DocumentReviewerLogin(host,h,recipient.User);const string root="/api/platform/privacy-tenant-exports-v1";var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var input=TenantPrivacyCommand(h) with{RecipientId=recipient.User};
        using var forged=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/commands",new{input.OperationId,input.RequestRef,input.TargetTenantId,input.Purpose,input.Sources,input.RecipientId,input.PolicyVersion,input.AccessMinutes,subjectId=h.User,operatorId=recipient.User}));Assert.Equal(HttpStatusCode.BadRequest,forged.StatusCode);
        using var missing=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/commands",new{input.OperationId,input.RequestRef,input.TargetTenantId,input.Purpose,input.Sources,input.PolicyVersion,input.AccessMinutes}));Assert.Equal(HttpStatusCode.BadRequest,missing.StatusCode);
        using var generated=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/commands",input));var text=await generated.Content.ReadAsStringAsync();Assert.True(generated.StatusCode==HttpStatusCode.OK,text);Assert.True(generated.Headers.CacheControl!.NoStore);
        var saved=JsonSerializer.Deserialize<TenantPrivacyExportOperationV1>(text,options)!;var receipt=saved.Receipt!;Assert.Equal(h.User,receipt.OperatorId);Assert.Equal(recipient.User,receipt.RecipientId);
        using var wrong=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"{root}/artifacts/{receipt.ArtifactId}/download"));Assert.Equal(HttpStatusCode.Forbidden,wrong.StatusCode);
        using var read=await host.Send(OperationsRequest(recipientCookies,HttpMethod.Get,$"{root}/artifacts/{receipt.ArtifactId}"));Assert.Equal(HttpStatusCode.OK,read.StatusCode);
        using var download=await host.Send(OperationsRequest(recipientCookies,HttpMethod.Get,$"{root}/artifacts/{receipt.ArtifactId}/download"));var bytes=await download.Content.ReadAsByteArrayAsync();Assert.Equal(HttpStatusCode.OK,download.StatusCode);Assert.True(download.Headers.CacheControl!.NoStore);
        Assert.Equal(receipt.Sha256,Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());using var data=JsonDocument.Parse(bytes);Assert.False(data.RootElement.GetProperty("manifest").TryGetProperty("subjectId",out _));Assert.All(data.RootElement.GetProperty("users").EnumerateArray(),row=>{Assert.False(row.TryGetProperty("Password",out _));Assert.False(row.TryGetProperty("TwoFactorSecret",out _));});
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==73&&x.ActionCode=="tenant-download").ExecuteDeleteAsync();using var withdrawn=await host.Send(OperationsRequest(recipientCookies,HttpMethod.Get,$"{root}/artifacts/{receipt.ArtifactId}/download"));Assert.Equal(HttpStatusCode.Forbidden,withdrawn.StatusCode);
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="privacy-export");h.Db.Sys_RoleActions.Add(new(){RoleId=73,MenuId=menu.MenuId,ActionCode="tenant-download"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="tenant-export").ExecuteDeleteAsync();using var original=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"{root}/operations/{input.OperationId}"));Assert.Equal(HttpStatusCode.OK,original.StatusCode);Assert.Equal(text,await original.Content.ReadAsStringAsync());
        using var replay=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/commands",input));Assert.Equal(HttpStatusCode.OK,replay.StatusCode);Assert.Equal(text,await replay.Content.ReadAsStringAsync());
        using var conflict=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/commands",input with{RequestRef=Guid.NewGuid()}));Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode);
        using var wrongOriginal=await host.Send(OperationsRequest(recipientCookies,HttpMethod.Get,$"{root}/operations/{input.OperationId}"));Assert.Contains("NOT_FOUND",await wrongOriginal.Content.ReadAsStringAsync());
        using var subjectLookup=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"/api/platform/privacy-exports-v1/artifacts/{receipt.ArtifactId}"));Assert.Equal(HttpStatusCode.NotFound,subjectLookup.StatusCode);
        var subject=(await PrivacyOwner(h).GenerateAsync(PrivacyCommand(h),PrivacySession(h))).Receipt!;using var tenantLookup=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"{root}/artifacts/{subject.ArtifactId}"));Assert.Equal(HttpStatusCode.NotFound,tenantLookup.StatusCode);
        using var revoke=await host.Send(OperationsRequest(cookies,HttpMethod.Post,$"{root}/artifacts/{receipt.ArtifactId}/revoke"));Assert.Equal(HttpStatusCode.OK,revoke.StatusCode);
        using var revoked=await host.Send(OperationsRequest(recipientCookies,HttpMethod.Get,$"{root}/artifacts/{receipt.ArtifactId}/download"));Assert.Equal(HttpStatusCode.Gone,revoked.StatusCode);Assert.Equal(1,await TenantPrivacyCount(h,"artifacts"));
    }
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_tenant_privacy_export_two_actors_lost_response_original_download_and_revoke_show_classified_scope()
    {
        await using var h=await Seed();var recipient=await TenantPrivacyRecipient(h);var home=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="erp-order-price-correction");home.RoutePath="/order-price-correction";
        h.Db.Sys_RoleMenus.AddRange(new(){RoleId=72,MenuId=home.MenuId},new(){RoleId=73,MenuId=home.MenuId});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await SecurityFieldLog(h,h.User);
        using var result=await BrowserScenario(h,recipient.User,"tenant-privacy-export-browser.mjs","tenant-privacy-export-browser",PrivacyExecutionEnvironment(h.Tenant),new(){["CP6_TENANT_PRIVACY_RECIPIENT"]=recipient.User.ToString("D"),["CP6_TENANT_PRIVACY_TARGET"]=h.Tenant.ToString("D")});
        Assert.Equal(1,await TenantPrivacyCount(h,"artifacts"));Assert.Equal(1,await TenantPrivacyCount(h,"commands"));var id=result.RootElement.GetProperty("artifactId").GetGuid();Assert.Equal("REVOKED",(await PrivacyOwner(h).TenantArtifactAsync(id,PrivacySession(h))).Availability);
    }
}
