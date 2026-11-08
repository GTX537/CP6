using System.Net;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadDisposition_actual_HTTP_two_current_sessions_strict_commands_and_original_after_withdrawal_delete_only_exact_payload()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h);await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,PrivacyExecutionEnvironment(h.Tenant));
        var cookies=await Login(host,h);var approverCookies=await DocumentReviewerLogin(host,h,setup.Approver.User);const string root="/api/platform/privacy-dispositions-v1/tenant-export-payloads";
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        using var bad=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/policies",new{operationId=Guid.NewGuid(),policyId=Guid.NewGuid(),targetTenantId=h.Tenant,expectedVersion=(Guid?)null,basisRef=Guid.NewGuid(),enabled=true,requireAccessClosed=true}));
        Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode);
        using var preview=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/preview",new TenantPayloadDispositionTargetV1(setup.Artifact,setup.Policy)));
        Assert.Equal(HttpStatusCode.OK,preview.StatusCode);var scope=JsonSerializer.Deserialize<TenantPayloadDispositionPreviewV1>(await preview.Content.ReadAsStringAsync(),options)!;
        using var requested=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/plans",new TenantPayloadDispositionRequestV1(Guid.NewGuid(),Guid.NewGuid(),new(setup.Artifact,setup.Policy),scope.ScopeDigest)));
        var plan=JsonSerializer.Deserialize<TenantPayloadDispositionOperationV1>(await requested.Content.ReadAsStringAsync(),options)!;Assert.Equal(HttpStatusCode.OK,requested.StatusCode);
        var decision=new TenantPayloadDispositionDecisionV1(Guid.NewGuid(),plan.Version!.Value,"APPROVE",Guid.NewGuid());var id=plan.PlanId!.Value;
        using var self=await host.Send(OperationsRequest(cookies,HttpMethod.Post,$"{root}/plans/{id}/decisions",decision));Assert.Equal(HttpStatusCode.Forbidden,self.StatusCode);
        using var approved=await host.Send(OperationsRequest(approverCookies,HttpMethod.Post,$"{root}/plans/{id}/decisions",decision));Assert.Equal(HttpStatusCode.OK,approved.StatusCode);
        var version=JsonSerializer.Deserialize<TenantPayloadDispositionOperationV1>(await approved.Content.ReadAsStringAsync(),options)!.Version!.Value;
        var input=new TenantPayloadDispositionExecuteV1(Guid.NewGuid(),version,scope.ScopeDigest,Guid.NewGuid());
        using var forged=await host.Send(OperationsRequest(cookies,HttpMethod.Post,$"{root}/plans/{id}/execute",new{input.OperationId,input.ExpectedVersion,input.ExpectedScopeDigest,input.ConfirmationRef,operatorId=setup.Approver.User}));Assert.Equal(HttpStatusCode.BadRequest,forged.StatusCode);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==73&&x.ActionCode=="tenant-payload-approve").ExecuteDeleteAsync();
        using var revoked=await host.Send(OperationsRequest(cookies,HttpMethod.Post,$"{root}/plans/{id}/execute",input));Assert.Equal(HttpStatusCode.Conflict,revoked.StatusCode);
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="privacy-disposition");h.Db.Sys_RoleActions.Add(new(){RoleId=73,MenuId=menu.MenuId,ActionCode="tenant-payload-approve"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using var executed=await host.Send(OperationsRequest(cookies,HttpMethod.Post,$"{root}/plans/{id}/execute",input));var text=await executed.Content.ReadAsStringAsync();Assert.True(executed.StatusCode==HttpStatusCode.OK,text);Assert.True(executed.Headers.CacheControl!.NoStore);
        Assert.DoesNotContain("simulated@example.invalid",text);Assert.DoesNotContain("simulated-private-subject",text);var saved=JsonSerializer.Deserialize<TenantPayloadDispositionOperationV1>(text,options)!;Assert.Equal("APPLIED",saved.Execution!.OwnerOutcome);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_tenant_export_v1.payloads WHERE artifact_id={setup.Artifact}").SingleAsync());
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="tenant-payload-execute").ExecuteDeleteAsync();
        using var original=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"{root}/operations/{input.OperationId}"));Assert.Equal(HttpStatusCode.OK,original.StatusCode);Assert.Equal(text,await original.Content.ReadAsStringAsync());
        using var replay=await host.Send(OperationsRequest(cookies,HttpMethod.Post,$"{root}/plans/{id}/execute",input));Assert.Equal(HttpStatusCode.OK,replay.StatusCode);Assert.Equal(text,await replay.Content.ReadAsStringAsync());
        using var conflict=await host.Send(OperationsRequest(cookies,HttpMethod.Post,$"{root}/plans/{id}/execute",input with{ConfirmationRef=Guid.NewGuid()}));Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode);
        using var wrongActor=await host.Send(OperationsRequest(approverCookies,HttpMethod.Get,$"{root}/operations/{input.OperationId}"));Assert.Contains("NOT_FOUND",await wrongActor.Content.ReadAsStringAsync());
        await using var foreign=await Seed();await PrivacyDispositionSetup(foreign);await EngineeringAction(foreign,"privacy-disposition","tenant-payload-read");
        Assert.Equal(404,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyDispositionOwner(foreign).TenantPayloadPreviewAsync(new(setup.Artifact,setup.Policy),PrivacySession(foreign)))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>PrivacyOwner(setup.Approver).DownloadTenantAsync(setup.Artifact,PrivacySession(setup.Approver)))).Status);
    }
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_tenant_payload_disposition_independent_approval_cancel_lost_reply_and_original_keeps_restore_remainder()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h);var home=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="erp-order-price-correction");home.RoutePath="/order-price-correction";
        h.Db.Sys_RoleMenus.AddRange(new(){RoleId=72,MenuId=home.MenuId},new(){RoleId=73,MenuId=home.MenuId});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using var result=await BrowserScenario(h,setup.Approver.User,"tenant-export-payload-disposition-browser.mjs","tenant-export-payload-disposition-browser",PrivacyExecutionEnvironment(h.Tenant),
            new(){["CP6_TENANT_PAYLOAD_ARTIFACT"]=setup.Artifact.ToString("D"),["CP6_PRIVACY_POLICY"]=setup.Policy.ToString("D")});
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_tenant_export_v1.payloads WHERE artifact_id={setup.Artifact}").SingleAsync());
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_tenant_payload_disposition_v1.executions WHERE artifact_id={setup.Artifact}").SingleAsync());
        Assert.Equal("DISPOSED",(await PrivacyOwner(h).TenantArtifactAsync(setup.Artifact,PrivacySession(h))).Availability);
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadDisposition_actual_HTTP_missing_native_storage_fails_503_without_available_or_empty_success()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h,false);await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,PrivacyExecutionEnvironment(h.Tenant));var cookies=await Login(host,h);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_privacy_tenant_payload_disposition_v1.tombstones RENAME TO w323_unavailable_tombs");
        try
        {
            using var disposition=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"/api/platform/privacy-dispositions-v1/tenant-export-payloads/preview",new TenantPayloadDispositionTargetV1(setup.Artifact,setup.Policy)));Assert.Equal(HttpStatusCode.ServiceUnavailable,disposition.StatusCode);Assert.Contains("PRIVACY_DISPOSITION_STORAGE_UNAVAILABLE",await disposition.Content.ReadAsStringAsync());
            using var artifact=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"/api/platform/privacy-tenant-exports-v1/artifacts/{setup.Artifact}"));Assert.Equal(HttpStatusCode.ServiceUnavailable,artifact.StatusCode);Assert.Contains("PRIVACY_TENANT_STORAGE_UNAVAILABLE",await artifact.Content.ReadAsStringAsync());Assert.True(artifact.Headers.CacheControl!.NoStore);
        }
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_privacy_tenant_payload_disposition_v1.w323_unavailable_tombs RENAME TO tombstones");}
        Assert.Equal("AVAILABLE",(await PrivacyOwner(h).TenantArtifactAsync(setup.Artifact,PrivacySession(h))).Availability);
    }
}
