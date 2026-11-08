using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Platform;
internal sealed record PrivacyExportPayloadTombstoneV1(Guid ArtifactId,Guid SourcePlanId,Guid OperatorTenantId,Guid TargetTenantId,Guid SubjectId,Guid PolicyId,string Scope,string Receipt);
internal static class PrivacyExportPayloadEffectV1
{
    private sealed record CandidateRow(Guid Environment,Guid OperatorTenant,Guid TargetTenant,Guid Subject,byte[] Receipt,Guid? Version,long? Length,string? Sha256);
    private sealed record ExistingTomb(Guid Plan,string Scope,string Receipt);
    internal static async Task Ready(CP6Context db,CancellationToken ct)
    {if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_privacy_export_disposition_v1.ready() AS \"Value\"").SingleAsync(ct))throw new PrivacyDispositionExceptionV1("PRIVACY_EXPORT_PAYLOAD_BOUNDARY_UNAVAILABLE",503);}
    // Candidate is an external restored database. Qualify its actual immutable identity/revision once under its IAM root.
    internal static async Task<PrivacyRestoreUnitV1> Qualify(CP6Context candidate,PrivacyExportPayloadTombstoneV1 tomb,Guid environment,CancellationToken ct)
    {
        var existing=await candidate.Database.SqlQuery<ExistingTomb>($"""
            SELECT source_plan_id AS "Plan",scope_json AS "Scope",receipt_json AS "Receipt" FROM cp6_privacy_export_disposition_v1.tombstones WHERE artifact_id={tomb.ArtifactId}
            """).SingleOrDefaultAsync(ct);
        if(existing!=null&&(existing.Plan!=tomb.SourcePlanId||existing.Scope!=tomb.Scope||existing.Receipt!=tomb.Receipt))throw new PrivacyDispositionExceptionV1("PRIVACY_EXPORT_PAYLOAD_RESTORE_TOMBSTONE_CONFLICT",412);
        var row=await candidate.Database.SqlQuery<CandidateRow>($"""
            SELECT a.environment_id AS "Environment",a.actor_tenant AS "OperatorTenant",a.target_tenant AS "TargetTenant",a.subject_id AS "Subject",a.receipt_bytes AS "Receipt",
              p.revision AS "Version",octet_length(p.bytes)::bigint AS "Length",encode(sha256(p.bytes),'hex') AS "Sha256" FROM cp6_privacy_export_v1.artifacts a
            LEFT JOIN cp6_privacy_export_v1.payloads p ON p.artifact_id=a.id WHERE a.id={tomb.ArtifactId}
            """).SingleOrDefaultAsync(ct);
        if(row!=null)
        {
            var scope=JsonSerializer.Deserialize<PayloadDispositionScopeV1>(tomb.Scope)!;var receipt=JsonSerializer.Deserialize<PrivacyExportReceiptV1>(row.Receipt)!;
            if(row.Environment!=environment||row.OperatorTenant!=tomb.OperatorTenantId||row.TargetTenant!=tomb.TargetTenantId||row.Subject!=tomb.SubjectId
                ||receipt.Sha256!=scope.OriginalSha256||receipt.Length!=scope.OriginalLength||receipt.Purpose!=scope.Purpose||receipt.CapturedAt!=scope.CapturedAt||receipt.ExpiresAt!=scope.AccessExpiresAt
                ||row.Version!=null&&(row.Version!=scope.PayloadVersion||row.Length!=scope.PayloadLength||row.Sha256!=scope.OriginalSha256))
                throw new PrivacyDispositionExceptionV1("PRIVACY_EXPORT_PAYLOAD_RESTORE_IDENTITY_CHANGED",412);
        }
        return new(tomb.SourcePlanId,tomb.TargetTenantId,tomb.SubjectId,row?.Version==null?"ABSENT_FENCED":"REAPPLIED",row?.Version,null,
            PrivacyDispositionOwnerV1.PayloadOwner,tomb.ArtifactId,row?.Length??0);
    }
    // The caller owns current policy/hold/approval; SQL owns same-transaction origin and resurrection fences.
    internal static async Task Apply(CP6Context db,PrivacyExportPayloadTombstoneV1 tomb,Guid environment,CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_privacy_export_disposition_v1.tombstones(artifact_id,source_plan_id,environment_id,operator_tenant,target_tenant,subject_id,policy_id,scope_json,receipt_json)
            SELECT {tomb.ArtifactId},{tomb.SourcePlanId},{environment},{tomb.OperatorTenantId},{tomb.TargetTenantId},{tomb.SubjectId},{tomb.PolicyId},{tomb.Scope},{tomb.Receipt}
            WHERE NOT EXISTS(SELECT FROM cp6_privacy_export_disposition_v1.tombstones WHERE artifact_id={tomb.ArtifactId});
            UPDATE cp6_privacy_export_v1.artifacts SET revoked_at=clock_timestamp() WHERE id={tomb.ArtifactId} AND revoked_at IS NULL;
            DELETE FROM cp6_privacy_export_v1.payloads WHERE artifact_id={tomb.ArtifactId};
            """,ct);
    }
}
