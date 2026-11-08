using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<Guid> HistoricalPrivacyArtifact(Harness h,PrivacyExportReceiptV1 receipt,byte[] payload,DateTimeOffset expiry)
    {
        var id=Guid.NewGuid();var historical=receipt with{ArtifactId=id,ExpiresAt=expiry};var bytes=JsonSerializer.SerializeToUtf8Bytes(historical);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_privacy_export_v1.artifacts(id,environment_id,actor_tenant,actor_id,recipient_id,target_tenant,subject_id,policy,expires_at,receipt_bytes)
            VALUES({id},{ScopeEnvironment},{h.Tenant},{h.User},{h.User},{h.Tenant},{h.User},{PrivacyExportOwnerV1.Policy},{expiry},{bytes});
            INSERT INTO cp6_privacy_export_v1.payloads(artifact_id,bytes) VALUES({id},{payload});
            """);return id;
    }
    [CurrentPermissionPostgresFact]
    public async Task PrivacyExport_expired_corrupt_missing_payload_and_expiry_during_final_session_never_release_bytes()
    {
        await using var h=await Seed();await PrivacyAuthority(h);var owner=PrivacyOwner(h);var session=PrivacySession(h);
        var result=await owner.GenerateAsync(PrivacyCommand(h),session);var receipt=result.Receipt!;
        var bytes=(await owner.DownloadAsync(receipt.ArtifactId,session)).Bytes;
        var expired=await HistoricalPrivacyArtifact(h,receipt,bytes,DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Equal("EXPIRED",(await owner.ReadArtifactAsync(expired,session)).Availability);
        Assert.Equal(410,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>owner.DownloadAsync(expired,session))).Status);
        var corrupt=await HistoricalPrivacyArtifact(h,receipt,[1,2,3],DateTimeOffset.UtcNow.AddMinutes(5));
        Assert.Equal("PRIVACY_ARTIFACT_INTEGRITY_FAILED",(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>owner.DownloadAsync(corrupt,session))).Message);
        // Owned fault injection: normal native DELETE now requires an actual disposition/restore parent.
        await using(var fault=await h.Db.Database.BeginTransactionAsync())
        {
            await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_privacy_export_v1.payloads DISABLE TRIGGER payload_owned_delete");
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_privacy_export_v1.payloads WHERE artifact_id={corrupt}");
            await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_privacy_export_v1.payloads ENABLE TRIGGER payload_owned_delete");
            await fault.CommitAsync();
        }
        Assert.Equal("PRIVACY_PAYLOAD_UNAVAILABLE",(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>owner.DownloadAsync(corrupt,session))).Message);
        var shortly=await HistoricalPrivacyArtifact(h,receipt,bytes,DateTimeOffset.UtcNow.AddSeconds(2));
        var auditBefore=await PrivacyCount(h,"audit");
        Assert.Equal("PRIVACY_ARTIFACT_EXPIRED",(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>owner.DownloadAsync(shortly,PrivacySession(h,async _=>await Task.Delay(2200))))).Message);
        Assert.Equal(auditBefore,await PrivacyCount(h,"audit"));
    }
    [CurrentPermissionPostgresFact]
    public async Task PrivacyExport_required_capture_failure_rolls_back_artifact_receipt_and_audit()
    {
        var fault=new PrivacyReadFailure();await using var h=await Seed(fault);await PrivacyAuthority(h);var owner=PrivacyOwner(h);var session=PrivacySession(h);var command=PrivacyCommand(h);
        fault.Fail=true;await Assert.ThrowsAsync<InvalidOperationException>(()=>owner.GenerateAsync(command,session));fault.Fail=false;
        Assert.Equal("NOT_FOUND",(await owner.OperationAsync(command.OperationId,session)).State);
        Assert.Equal(0,await PrivacyCount(h,"commands"));Assert.Equal(0,await PrivacyCount(h,"artifacts"));Assert.Equal(0,await PrivacyCount(h,"audit"));
    }
}
