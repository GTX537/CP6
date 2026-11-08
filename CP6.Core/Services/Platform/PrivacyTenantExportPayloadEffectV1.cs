using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Platform;
internal sealed record PrivacyTenantExportPayloadTombstoneV1(Guid ArtifactId,Guid SourcePlanId,Guid OperatorTenantId,Guid TargetTenantId,Guid PolicyId,string Scope,string Receipt);
internal static class PrivacyTenantExportPayloadEffectV1
{
    private sealed record CandidateRow(bool MetadataPresent,bool PayloadPresent,Guid? Environment,Guid? OperatorTenant,Guid? TargetTenant,byte[]? Receipt,Guid? Version,long? Length,string? Sha256);
    private sealed record ExistingTomb(Guid Plan,string Scope,string Receipt);
    // Only the external restored candidate is checked here; source/live internal contracts are directly trusted.
    internal static async Task Ready(CP6Context candidate,CancellationToken ct)
    {
        try
        {
            if(!await candidate.Database.SqlQueryRaw<bool>("SELECT cp6_privacy_tenant_payload_disposition_v1.restore_ready() AS \"Value\"").SingleAsync(ct))
                throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_RESTORE_BOUNDARY_UNAVAILABLE",503);
        }
        catch(Npgsql.PostgresException e)when(e.SqlState is "42P01" or "3F000" or "42883")
        {throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_RESTORE_BOUNDARY_UNAVAILABLE",503,e);}
    }
    internal static async Task<PrivacyRestoreTenantPayloadUnitV1> Qualify(CP6Context candidate,PrivacyTenantExportPayloadTombstoneV1 tomb,Guid environment,CancellationToken ct)
    {
        var existing=await candidate.Database.SqlQuery<ExistingTomb>($"""
            SELECT source_plan_id AS "Plan",scope_json AS "Scope",receipt_json AS "Receipt" FROM cp6_privacy_tenant_payload_disposition_v1.tombstones WHERE artifact_id={tomb.ArtifactId}
            """).SingleOrDefaultAsync(ct);
        if(existing!=null&&(existing.Plan!=tomb.SourcePlanId||existing.Scope!=tomb.Scope||existing.Receipt!=tomb.Receipt))throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_RESTORE_TOMBSTONE_CONFLICT",412);
        var row=await candidate.Database.SqlQuery<CandidateRow>($"""
            SELECT (a.id IS NOT NULL) AS "MetadataPresent",(p.artifact_id IS NOT NULL) AS "PayloadPresent",a.environment_id AS "Environment",a.actor_tenant AS "OperatorTenant",a.target_tenant AS "TargetTenant",a.receipt_bytes AS "Receipt",
              p.revision AS "Version",octet_length(p.bytes)::bigint AS "Length",encode(sha256(p.bytes),'hex') AS "Sha256"
            FROM cp6_privacy_tenant_export_v1.artifacts a FULL JOIN cp6_privacy_tenant_export_v1.payloads p ON p.artifact_id=a.id
            WHERE a.id={tomb.ArtifactId} OR p.artifact_id={tomb.ArtifactId}
            """).SingleOrDefaultAsync(ct);
        if(row!=null)
        {
            if(!row.MetadataPresent||row.Receipt==null)throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_RESTORE_IDENTITY_CHANGED",412);
            TenantPrivacyExportReceiptV1 receipt;
            try{receipt=JsonSerializer.Deserialize<TenantPrivacyExportReceiptV1>(row.Receipt)!;}
            catch(JsonException e){throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_RESTORE_IDENTITY_CHANGED",412,e);}
            var scope=JsonSerializer.Deserialize<TenantPayloadDispositionScopeV1>(tomb.Scope)!;
            if(receipt==null||row.Environment!=environment||row.OperatorTenant!=tomb.OperatorTenantId||row.TargetTenant!=tomb.TargetTenantId
                ||receipt.ArtifactId!=tomb.ArtifactId||receipt.EnvironmentId!=environment||receipt.RequesterTenantId!=tomb.OperatorTenantId||receipt.TargetTenantId!=tomb.TargetTenantId
                ||receipt.Sha256!=scope.OriginalSha256||receipt.Length!=scope.OriginalLength||receipt.Purpose!=scope.Purpose||receipt.CapturedAt!=scope.CapturedAt||receipt.ExpiresAt!=scope.AccessExpiresAt
                ||row.PayloadPresent&&(row.Version!=scope.PayloadVersion||row.Length!=scope.PayloadLength||row.Sha256!=scope.OriginalSha256))
                throw new PrivacyDispositionExceptionV1("PRIVACY_TENANT_PAYLOAD_RESTORE_IDENTITY_CHANGED",412);
        }
        return new(tomb.SourcePlanId,tomb.TargetTenantId,tomb.ArtifactId,PrivacyDispositionOwnerV1.TenantPayloadOwner,row?.PayloadPresent==true?"REAPPLIED":"ABSENT_FENCED",
            row==null?"ARTIFACT_ABSENT":row.PayloadPresent?"PRESENT":"PAYLOAD_ABSENT",row?.Version,row?.Length??0);
    }
    internal static Task Apply(CP6Context candidate,PrivacyTenantExportPayloadTombstoneV1 tomb,Guid environment,CancellationToken ct)
        =>candidate.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_privacy_tenant_payload_disposition_v1.tombstones(artifact_id,source_plan_id,environment_id,operator_tenant,target_tenant,policy_id,scope_json,receipt_json)
            SELECT {tomb.ArtifactId},{tomb.SourcePlanId},{environment},{tomb.OperatorTenantId},{tomb.TargetTenantId},{tomb.PolicyId},{tomb.Scope},{tomb.Receipt}
            WHERE NOT EXISTS(SELECT FROM cp6_privacy_tenant_payload_disposition_v1.tombstones WHERE artifact_id={tomb.ArtifactId});
            UPDATE cp6_privacy_tenant_export_v1.artifacts SET revoked_at=clock_timestamp() WHERE id={tomb.ArtifactId} AND revoked_at IS NULL;
            DELETE FROM cp6_privacy_tenant_export_v1.payloads WHERE artifact_id={tomb.ArtifactId};
            """,ct);
}
