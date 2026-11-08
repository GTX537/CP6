using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadRestore_actual_native_backup_deletes_exact_bytes_and_installs_absent_artifact_fence_without_fake_subject()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h);await RestorePermissions(h);var s=PrivacySession(h);var original=(await PrivacyOwner(h).TenantArtifactAsync(setup.Artifact,s)).Receipt;
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);
        var first=await TenantPayloadApproved(h,setup);await PrivacyDispositionOwner(h).ExecuteTenantPayloadDispositionAsync(first.PlanId,new(Guid.NewGuid(),first.Version,first.ScopeDigest,Guid.NewGuid()),s);
        var late=(await PrivacyOwner(h).GenerateTenantAsync(TenantPrivacyCommand(h),s)).Receipt!.ArtifactId;await PrivacyOwner(h).RevokeTenantAsync(late,s);var second=await TenantPayloadApproved(h,(late,setup.Policy,setup.Approver));await PrivacyDispositionOwner(h).ExecuteTenantPayloadDispositionAsync(second.PlanId,new(Guid.NewGuid(),second.Version,second.ScopeDigest,Guid.NewGuid()),s);
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var restore=PrivacyRestorer(h,options,runtime);await restore.RestorePreviewAsync(candidate.Id,s);
        var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);var result=await restore.ExecuteRestoreAsync(plan.PlanId,RestoreCommand(plan),s);
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(result));var units=json.RootElement.GetProperty("TenantPayloadUnits").EnumerateArray().ToArray();var actual=Assert.Single(units,x=>x.GetProperty("ArtifactId").GetGuid()==setup.Artifact);var absent=Assert.Single(units,x=>x.GetProperty("ArtifactId").GetGuid()==late);
        Assert.Equal("REAPPLIED",actual.GetProperty("Outcome").GetString());Assert.Equal(original.Length,actual.GetProperty("RemovedBytes").GetInt64());Assert.Equal("ABSENT_FENCED",absent.GetProperty("Outcome").GetString());Assert.Equal(0,absent.GetProperty("RemovedBytes").GetInt64());Assert.All(units,u=>Assert.False(u.TryGetProperty("SubjectId",out _)));
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);Assert.Equal(0,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_tenant_export_v1.payloads WHERE artifact_id={setup.Artifact}").SingleAsync());Assert.True(await target.Database.SqlQuery<bool>($"SELECT revoked_at IS NOT NULL AS \"Value\" FROM cp6_privacy_tenant_export_v1.artifacts WHERE id={setup.Artifact}").SingleAsync());
        await using(var admission=await PrivacyRestoreAdmissionV1.AcquireAsync(target,new(){ControlConnectionString=h.Db.Database.GetConnectionString()!},default))Assert.NotNull(admission);
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>target.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_privacy_tenant_export_v1.payloads(artifact_id,bytes) VALUES({setup.Artifact},{new byte[]{1}})"))).SqlState);
    }
}
