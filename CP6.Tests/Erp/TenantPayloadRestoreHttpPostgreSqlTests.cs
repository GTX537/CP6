using System.Net;
using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_tenant_payload_restore_independent_approval_cancel_lost_execute_reply_and_exact_separate_tenant_result()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h);await RestorePermissions(h);var home=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="erp-order-price-correction");home.RoutePath="/order-price-correction";
        h.Db.Sys_RoleMenus.AddRange(new(){RoleId=72,MenuId=home.MenuId},new(){RoleId=73,MenuId=home.MenuId});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await TenantPayloadDispose(h,setup);var options=RestoreRegistration(candidate);
        using var result=await BrowserScenario(h,setup.Approver.User,"privacy-restore-browser.mjs","tenant-payload-restore-browser",await RestoreHostSettings(h,options),
            new(){["CP6_PRIVACY_RESTORE_CANDIDATE"]=candidate.Id.ToString("D"),["CP6_TENANT_PAYLOAD_RESTORE_ARTIFACT"]=setup.Artifact.ToString("D")});
        var plan=await PrivacyRestorer(h,options,await RestoreRuntime(h)).RestorePlanAsync(result.RootElement.GetProperty("planId").GetGuid(),PrivacySession(h));Assert.Equal("APPLIED",plan.Outcome!.State);var unit=Assert.Single(plan.Outcome.TenantPayloadUnits!,x=>x.ArtifactId==setup.Artifact);Assert.Equal("REAPPLIED",unit.Outcome);Assert.True(unit.RemovedBytes>0);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);Assert.Equal(0,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_tenant_export_v1.payloads WHERE artifact_id={setup.Artifact}").SingleAsync());await using(var admission=await PrivacyRestoreAdmissionV1.AcquireAsync(target,new(){ControlConnectionString=h.Db.Database.GetConnectionString()!},default))Assert.NotNull(admission);
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadRestore_actual_HTTP_candidate_missing_fence_returns_explicit_503_without_success_or_disclosure()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h);await RestorePermissions(h);await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await TenantPayloadDispose(h,setup);var options=RestoreRegistration(candidate);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,await RestoreHostSettings(h,options));var cookies=await Login(host,h);
        await target.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_privacy_disposition_v1.restore_receipts DISABLE TRIGGER tenant_payload_restore_complete");
        try{using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"/api/platform/privacy-dispositions-v1/restore/candidates/{candidate.Id}/preview"));Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);Assert.Contains("PRIVACY_TENANT_PAYLOAD_RESTORE_BOUNDARY_UNAVAILABLE",await response.Content.ReadAsStringAsync());Assert.True(response.Headers.CacheControl!.NoStore);}
        finally{await target.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_privacy_disposition_v1.restore_receipts ENABLE TRIGGER tenant_payload_restore_complete");}
    }
}
