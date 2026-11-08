using System.Net;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task ExportPayloadDisposition_actual_HTTP_two_current_sessions_strict_commands_and_original_after_withdrawal_delete_only_exact_payload()
    {
        await using var h=await Seed();var setup=await PayloadSetup(h);await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,PrivacyExecutionEnvironment(h.Tenant));
        var cookies=await Login(host,h);var approverCookies=await DocumentReviewerLogin(host,h,setup.Approver.User);const string root="/api/platform/privacy-dispositions-v1/export-payloads";
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        using var bad=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/policies",new{operationId=Guid.NewGuid(),policyId=Guid.NewGuid(),targetTenantId=h.Tenant,expectedVersion=(Guid?)null,basisRef=Guid.NewGuid(),enabled=true,requireAccessClosed=true}));
        Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode);
        using var preview=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/preview",new PayloadDispositionTargetV1(setup.Artifact,setup.Policy)));
        Assert.Equal(HttpStatusCode.OK,preview.StatusCode);var scope=JsonSerializer.Deserialize<PayloadDispositionPreviewV1>(await preview.Content.ReadAsStringAsync(),options)!;
        using var requested=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/plans",new PayloadDispositionRequestV1(Guid.NewGuid(),Guid.NewGuid(),new(setup.Artifact,setup.Policy),scope.ScopeDigest)));
        var plan=JsonSerializer.Deserialize<PayloadDispositionOperationV1>(await requested.Content.ReadAsStringAsync(),options)!;Assert.Equal(HttpStatusCode.OK,requested.StatusCode);
        var decision=new PayloadDispositionDecisionV1(Guid.NewGuid(),plan.Version!.Value,"APPROVE",Guid.NewGuid());var id=plan.PlanId!.Value;
        using var self=await host.Send(OperationsRequest(cookies,HttpMethod.Post,$"{root}/plans/{id}/decisions",decision));Assert.Equal(HttpStatusCode.Forbidden,self.StatusCode);
        using var approved=await host.Send(OperationsRequest(approverCookies,HttpMethod.Post,$"{root}/plans/{id}/decisions",decision));Assert.Equal(HttpStatusCode.OK,approved.StatusCode);
        var version=JsonSerializer.Deserialize<PayloadDispositionOperationV1>(await approved.Content.ReadAsStringAsync(),options)!.Version!.Value;
        var input=new PayloadDispositionExecuteV1(Guid.NewGuid(),version,scope.ScopeDigest,Guid.NewGuid());
        using var forged=await host.Send(OperationsRequest(cookies,HttpMethod.Post,$"{root}/plans/{id}/execute",new{input.OperationId,input.ExpectedVersion,input.ExpectedScopeDigest,input.ConfirmationRef,operatorId=setup.Approver.User}));Assert.Equal(HttpStatusCode.BadRequest,forged.StatusCode);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==73&&x.ActionCode=="payload-approve").ExecuteDeleteAsync();
        using var revoked=await host.Send(OperationsRequest(cookies,HttpMethod.Post,$"{root}/plans/{id}/execute",input));Assert.Equal(HttpStatusCode.Conflict,revoked.StatusCode);
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="privacy-disposition");h.Db.Sys_RoleActions.Add(new(){RoleId=73,MenuId=menu.MenuId,ActionCode="payload-approve"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using var executed=await host.Send(OperationsRequest(cookies,HttpMethod.Post,$"{root}/plans/{id}/execute",input));var text=await executed.Content.ReadAsStringAsync();Assert.True(executed.StatusCode==HttpStatusCode.OK,text);Assert.True(executed.Headers.CacheControl!.NoStore);
        Assert.DoesNotContain("simulated@example.invalid",text);Assert.DoesNotContain("simulated-private-subject",text);var saved=JsonSerializer.Deserialize<PayloadDispositionOperationV1>(text,options)!;Assert.Equal("APPLIED",saved.Execution!.OwnerOutcome);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_v1.payloads WHERE artifact_id={setup.Artifact}").SingleAsync());
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="payload-execute").ExecuteDeleteAsync();
        using var original=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"{root}/operations/{input.OperationId}"));Assert.Equal(HttpStatusCode.OK,original.StatusCode);Assert.Equal(text,await original.Content.ReadAsStringAsync());
        using var replay=await host.Send(OperationsRequest(cookies,HttpMethod.Post,$"{root}/plans/{id}/execute",input));Assert.Equal(HttpStatusCode.OK,replay.StatusCode);Assert.Equal(text,await replay.Content.ReadAsStringAsync());
        using var conflict=await host.Send(OperationsRequest(cookies,HttpMethod.Post,$"{root}/plans/{id}/execute",input with{ConfirmationRef=Guid.NewGuid()}));Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode);
        using var wrongActor=await host.Send(OperationsRequest(approverCookies,HttpMethod.Get,$"{root}/operations/{input.OperationId}"));Assert.Contains("NOT_FOUND",await wrongActor.Content.ReadAsStringAsync());
        await using var foreign=await Seed();await PrivacyDispositionSetup(foreign);await EngineeringAction(foreign,"privacy-disposition","payload-read");
        Assert.Equal(404,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyDispositionOwner(foreign).PayloadPreviewAsync(new(setup.Artifact,setup.Policy),PrivacySession(foreign)))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>PrivacyOwner(setup.Approver).DownloadAsync(setup.Artifact,PrivacySession(setup.Approver)))).Status);
    }
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_export_payload_disposition_independent_approval_cancel_lost_reply_and_reload_original()
    {
        await using var h=await Seed();var setup=await PayloadSetup(h);var home=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="erp-order-price-correction");home.RoutePath="/order-price-correction";
        h.Db.Sys_RoleMenus.AddRange(new(){RoleId=72,MenuId=home.MenuId},new(){RoleId=73,MenuId=home.MenuId});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using var result=await BrowserScenario(h,setup.Approver.User,"export-payload-disposition-browser.mjs","export-payload-disposition-browser",PrivacyExecutionEnvironment(h.Tenant),
            new(){["CP6_PAYLOAD_ARTIFACT"]=setup.Artifact.ToString("D"),["CP6_PRIVACY_POLICY"]=setup.Policy.ToString("D")});
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_v1.payloads WHERE artifact_id={setup.Artifact}").SingleAsync());
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_disposition_v1.executions WHERE artifact_id={setup.Artifact}").SingleAsync());
        Assert.Equal("DISPOSED",(await PrivacyOwner(h).ReadArtifactAsync(setup.Artifact,PrivacySession(h))).Availability);
    }
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_export_payload_restore_independent_approval_lost_reply_and_exact_owner_result()
    {
        await using var h=await Seed();var setup=await PayloadSetup(h);await RestorePermissions(h);var home=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="erp-order-price-correction");home.RoutePath="/order-price-correction";
        h.Db.Sys_RoleMenus.AddRange(new(){RoleId=72,MenuId=home.MenuId},new(){RoleId=73,MenuId=home.MenuId});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await PayloadDispose(h,setup);var options=RestoreRegistration(candidate);
        using var result=await BrowserScenario(h,setup.Approver.User,"privacy-restore-browser.mjs","export-payload-restore-browser",await RestoreHostSettings(h,options),
            new(){["CP6_PRIVACY_RESTORE_CANDIDATE"]=candidate.Id.ToString("D"),["CP6_PAYLOAD_RESTORE_ARTIFACT"]=setup.Artifact.ToString("D")});
        var plan=await PrivacyRestorer(h,options,await RestoreRuntime(h)).RestorePlanAsync(result.RootElement.GetProperty("planId").GetGuid(),PrivacySession(h));
        Assert.Equal("APPLIED",plan.Outcome!.State);var unit=Assert.Single(plan.Outcome.Units!,x=>x.ArtifactId==setup.Artifact);Assert.Equal("Main.Privacy.ExportPayload.v1",unit.Owner);Assert.True(unit.RemovedBytes>0);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);
        Assert.Equal(0,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_v1.payloads WHERE artifact_id={setup.Artifact}").SingleAsync());
    }
}
