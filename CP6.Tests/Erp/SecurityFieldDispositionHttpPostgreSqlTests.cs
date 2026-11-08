using System.Net;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task SecurityFieldDisposition_actual_HTTP_two_current_sessions_strict_commands_and_original_after_withdrawal_clear_only_classified_fields()
    {
        await using var h=await Seed();var setup=await SecurityFieldSetup(h);await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,PrivacyExecutionEnvironment(h.Tenant));
        var cookies=await Login(host,h);var approverCookies=await DocumentReviewerLogin(host,h,setup.Approver.User);const string root="/api/platform/privacy-dispositions-v1/security-fields";
        Assert.True(await h.Db.Sys_SecurityLogs.AsNoTracking().AnyAsync(x=>x.UserId==h.User&&x.UserName=="price-owner"));
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        using var bad=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/policies",new{operationId=Guid.NewGuid(),policyId=Guid.NewGuid(),targetTenantId=h.Tenant,expectedVersion=(Guid?)null,basisRef=Guid.NewGuid(),enabled=true}));
        Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode);
        using var preview=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/preview",new SecurityFieldTargetV1(setup.Log,setup.Policy)));
        Assert.Equal(HttpStatusCode.OK,preview.StatusCode);var scope=JsonSerializer.Deserialize<SecurityFieldPreviewV1>(await preview.Content.ReadAsStringAsync(),options)!;
        using var requested=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/plans",new SecurityFieldRequestV1(Guid.NewGuid(),Guid.NewGuid(),new(setup.Log,setup.Policy),scope.ScopeDigest)));
        var plan=JsonSerializer.Deserialize<SecurityFieldOperationV1>(await requested.Content.ReadAsStringAsync(),options)!;Assert.Equal(HttpStatusCode.OK,requested.StatusCode);
        var decision=new SecurityFieldDecisionV1(Guid.NewGuid(),plan.Version!.Value,"APPROVE",Guid.NewGuid());var id=plan.PlanId!.Value;
        using var self=await host.Send(OperationsRequest(cookies,HttpMethod.Post,$"{root}/plans/{id}/decisions",decision));Assert.Equal(HttpStatusCode.Forbidden,self.StatusCode);
        using var approved=await host.Send(OperationsRequest(approverCookies,HttpMethod.Post,$"{root}/plans/{id}/decisions",decision));Assert.Equal(HttpStatusCode.OK,approved.StatusCode);
        var version=JsonSerializer.Deserialize<SecurityFieldOperationV1>(await approved.Content.ReadAsStringAsync(),options)!.Version!.Value;
        var input=new SecurityFieldExecuteV1(Guid.NewGuid(),version,scope.ScopeDigest,Guid.NewGuid());
        using var forged=await host.Send(OperationsRequest(cookies,HttpMethod.Post,$"{root}/plans/{id}/execute",new{input.OperationId,input.ExpectedVersion,input.ExpectedScopeDigest,input.ConfirmationRef,operatorId=setup.Approver.User}));Assert.Equal(HttpStatusCode.BadRequest,forged.StatusCode);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==73&&x.ActionCode=="security-approve").ExecuteDeleteAsync();
        using var revoked=await host.Send(OperationsRequest(cookies,HttpMethod.Post,$"{root}/plans/{id}/execute",input));Assert.Equal(HttpStatusCode.Conflict,revoked.StatusCode);
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="privacy-disposition");h.Db.Sys_RoleActions.Add(new(){RoleId=73,MenuId=menu.MenuId,ActionCode="security-approve"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using var executed=await host.Send(OperationsRequest(cookies,HttpMethod.Post,$"{root}/plans/{id}/execute",input));var text=await executed.Content.ReadAsStringAsync();Assert.True(executed.StatusCode==HttpStatusCode.OK,text);Assert.True(executed.Headers.CacheControl!.NoStore);
        Assert.DoesNotContain("simulated-sensitive-name",text);Assert.DoesNotContain("198.51.100.7",text);var saved=JsonSerializer.Deserialize<SecurityFieldOperationV1>(text,options)!;Assert.Equal("APPLIED",saved.Execution!.OwnerOutcome);
        var row=await h.Db.Sys_SecurityLogs.AsNoTracking().SingleAsync(x=>x.Id==setup.Log);Assert.Null(row.UserName);Assert.Null(row.ClientIp);Assert.Equal("unclassified-device-remains",row.UserAgent);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="security-execute").ExecuteDeleteAsync();
        using var original=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"{root}/operations/{input.OperationId}"));Assert.Equal(HttpStatusCode.OK,original.StatusCode);Assert.Equal(text,await original.Content.ReadAsStringAsync());
        using var replay=await host.Send(OperationsRequest(cookies,HttpMethod.Post,$"{root}/plans/{id}/execute",input));Assert.Equal(HttpStatusCode.OK,replay.StatusCode);Assert.Equal(text,await replay.Content.ReadAsStringAsync());
        using var conflict=await host.Send(OperationsRequest(cookies,HttpMethod.Post,$"{root}/plans/{id}/execute",input with{ConfirmationRef=Guid.NewGuid()}));Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode);
        using var wrongActor=await host.Send(OperationsRequest(approverCookies,HttpMethod.Get,$"{root}/operations/{input.OperationId}"));Assert.Contains("NOT_FOUND",await wrongActor.Content.ReadAsStringAsync());
        await using var foreign=await Seed();await PrivacyDispositionSetup(foreign);await EngineeringAction(foreign,"privacy-disposition","security-read");
        Assert.Equal(404,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyDispositionOwner(foreign).SecurityPreviewAsync(new(setup.Log,setup.Policy),PrivacySession(foreign)))).Status);
    }
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_security_fields_disposition_independent_approval_cancel_lost_reply_and_reload_original()
    {
        await using var h=await Seed();var setup=await SecurityFieldSetup(h);var home=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="erp-order-price-correction");home.RoutePath="/order-price-correction";
        h.Db.Sys_RoleMenus.AddRange(new(){RoleId=72,MenuId=home.MenuId},new(){RoleId=73,MenuId=home.MenuId});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using var result=await BrowserScenario(h,setup.Approver.User,"security-field-disposition-browser.mjs","security-field-disposition-browser",PrivacyExecutionEnvironment(h.Tenant),
            new(){["CP6_SECURITY_LOG"]=setup.Log.ToString("D"),["CP6_PRIVACY_POLICY"]=setup.Policy.ToString("D")});
        var row=await h.Db.Sys_SecurityLogs.AsNoTracking().SingleAsync(x=>x.Id==setup.Log);Assert.Null(row.UserName);Assert.Null(row.ClientIp);Assert.Equal("unclassified-device-remains",row.UserAgent);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_security_fields_v1.executions WHERE log_id={setup.Log}").SingleAsync());
    }
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_security_fields_restore_independent_approval_lost_reply_and_exact_owner_result()
    {
        await using var h=await Seed();var setup=await SecurityFieldSetup(h);await RestorePermissions(h);var home=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="erp-order-price-correction");home.RoutePath="/order-price-correction";
        h.Db.Sys_RoleMenus.AddRange(new(){RoleId=72,MenuId=home.MenuId},new(){RoleId=73,MenuId=home.MenuId});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await SecurityFieldDispose(h,setup);var options=RestoreRegistration(candidate);
        using var result=await BrowserScenario(h,setup.Approver.User,"privacy-restore-browser.mjs","security-field-restore-browser",await RestoreHostSettings(h,options),
            new(){["CP6_PRIVACY_RESTORE_CANDIDATE"]=candidate.Id.ToString("D"),["CP6_SECURITY_RESTORE_LOG"]=setup.Log.ToString("D")});
        var plan=await PrivacyRestorer(h,options,await RestoreRuntime(h)).RestorePlanAsync(result.RootElement.GetProperty("planId").GetGuid(),PrivacySession(h));
        Assert.Equal("APPLIED",plan.Outcome!.State);var unit=Assert.Single(plan.Outcome.Units!,x=>x.LogId==setup.Log);Assert.Equal("Main.SecurityLog.Fields.v1",unit.Owner);Assert.Equal(2,unit.ClearedValueCount);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);
        var row=await target.Sys_SecurityLogs.AsNoTracking().SingleAsync(x=>x.Id==setup.Log);Assert.Null(row.UserName);Assert.Null(row.ClientIp);Assert.Equal("unclassified-reason-remains",row.Reason);
    }
}
