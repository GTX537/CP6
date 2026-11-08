using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadDisposition_new_tomb_invalidates_admitted_backup_and_explicitly_rejects_unsupported_restore_before_candidate_disclosure()
    {
        // Run in its own isolated source: a newly disposed unsupported Owner intentionally blocks every candidate of that source.
        await using var h=await Seed();var setup=await TenantPayloadSetup(h);await RestorePermissions(h);var s=PrivacySession(h);
        await using var already=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);var options=RestoreRegistration(already);var runtime=await RestoreRuntime(h);var restore=PrivacyRestorer(h,options,runtime);
        var first=await ApprovedRestore(h,setup.Approver,options,runtime,already.Id);var originalCommand=RestoreCommand(first);var original=await restore.ExecuteRestoreAsync(first.PlanId,originalCommand,s);
        await using var opened=PrivacyRestoreNativeV1.Context(already.Connection,h.Tenant);await using(var admission=await PrivacyRestoreAdmissionV1.AcquireAsync(opened,new(){ControlConnectionString=h.Db.Database.GetConnectionString()!},default))Assert.NotNull(admission);
        await using var pending=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);options.Candidates.Add(pending.Id,RestoreRegistration(pending).Candidates[pending.Id]);var before=await ApprovedRestore(h,setup.Approver,options,runtime,pending.Id);
        var plan=await TenantPayloadApproved(h,setup);await PrivacyDispositionOwner(h).ExecuteTenantPayloadDispositionAsync(plan.PlanId,new(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid()),s);
        var stale=await restore.RestorePlanAsync(before.PlanId,s);Assert.Contains("SOURCE_VERSION_CHANGED",stale.CurrentBlockers);Assert.False(stale.ApprovalCurrent);
        Assert.Equal("PRIVACY_RESTORE_TENANT_PAYLOAD_OWNER_UNSUPPORTED",(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>restore.RestorePreviewAsync(pending.Id,s))).Message);
        Assert.Equal("PRIVACY_RESTORE_TENANT_PAYLOAD_OWNER_UNSUPPORTED",(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>restore.ExecuteRestoreAsync(before.PlanId,RestoreCommand(before),s))).Message);
        Assert.Equal("PRIVACY_RESTORE_SOURCE_PROOF_NOT_CURRENT",(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyRestoreAdmissionV1.AcquireAsync(opened,new(){ControlConnectionString=h.Db.Database.GetConnectionString()!},default))).Message);
        await using var quarantined=PrivacyRestoreNativeV1.Context(pending.Connection,h.Tenant);Assert.Equal(1,await quarantined.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_tenant_export_v1.payloads WHERE artifact_id={setup.Artifact}").SingleAsync());
        Assert.Equal("PRIVACY_RESTORE_QUARANTINED",(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyRestoreAdmissionV1.AcquireAsync(quarantined,new(){ControlConnectionString=h.Db.Database.GetConnectionString()!},default))).Message);
        // Resolving a historical committed original remains possible; it cannot reopen an old source version.
        Assert.Equal(JsonSerializer.Serialize(original),JsonSerializer.Serialize(await restore.ExecuteRestoreAsync(first.PlanId,originalCommand,s)));
    }
}
