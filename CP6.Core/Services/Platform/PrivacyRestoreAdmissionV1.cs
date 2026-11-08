using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
namespace CP6.Core.Services.Platform;
public static class PrivacyRestoreAdmissionV1
{
    private sealed record Admission(Guid CandidateId,string SourceIdentity,string TargetIdentity,Guid EnvironmentId,Guid SourceVersion,Guid ActorTenantId,Guid ActorId,Guid OperationId);
    private sealed class Lease(CP6Context source,IDbContextTransaction transaction):IAsyncDisposable
    {
        public async ValueTask DisposeAsync(){try{await transaction.DisposeAsync();}finally{await source.DisposeAsync();}}
    }
    public static async Task<IAsyncDisposable?> AcquireAsync(CP6Context target,PrivacyRestoreOptionsV1 options,CancellationToken ct)
    {
        var configured=!string.IsNullOrWhiteSpace(options.ControlConnectionString);
        if(!target.Database.IsNpgsql())
        {if(configured)throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_POSTGRESQL_REQUIRED",503);return null;}
        var marker=await PrivacyRestoreNativeV1.Marker(target,ct);
        if(!marker.StartsWith(PrivacyRestoreNativeV1.MarkerPrefix,StringComparison.Ordinal))
        {if(configured)throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_TARGET_MARKER_REQUIRED",503);return null;}
        if(!Guid.TryParseExact(marker[PrivacyRestoreNativeV1.MarkerPrefix.Length..],"D",out var candidateId)||candidateId==Guid.Empty||string.IsNullOrWhiteSpace(options.ControlConnectionString))
            throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_QUARANTINED",503);
        var admission=await target.Database.SqlQueryRaw<Admission>("""
            SELECT candidate_id AS "CandidateId",source_identity AS "SourceIdentity",target_identity AS "TargetIdentity",environment_id AS "EnvironmentId",
              source_version AS "SourceVersion",actor_tenant AS "ActorTenantId",actor_id AS "ActorId",operation_id AS "OperationId"
            FROM cp6_privacy_disposition_v1.restore_admission WHERE singleton
            """).SingleOrDefaultAsync(ct)??throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_QUARANTINED",503);
        var targetIdentity=await PrivacyRestoreNativeV1.Database(target,ct);
        if(admission.CandidateId!=candidateId||admission.TargetIdentity!=PrivacyRestoreNativeV1.IdentityJson(targetIdentity))
            throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_TARGET_IDENTITY_CHANGED",503);
        var source=PrivacyRestoreNativeV1.Context(options.ControlConnectionString,admission.ActorTenantId);IDbContextTransaction? tx=null;
        try
        {
            await source.Database.OpenConnectionAsync(ct);var sourceIdentity=await PrivacyRestoreNativeV1.Database(source,ct);
            if(PrivacyRestoreNativeV1.SameDatabase(sourceIdentity,targetIdentity)||admission.SourceIdentity!=PrivacyRestoreNativeV1.IdentityJson(sourceIdentity))
                throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_CONTROL_IDENTITY_CHANGED",503);
            tx=await source.Database.BeginTransactionAsync(ct);await source.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='10s'",ct);
            await AuthenticationCompletionGateV1.AcquireAsync(source,ct);
            var environment=await source.Database.SqlQueryRaw<Guid?>("SELECT environment_id AS \"Value\" FROM cp6_scope_authorization_v1.environment WHERE singleton").SingleOrDefaultAsync(ct);
            var current=await PrivacyRestoreNativeV1.Epoch(source,ct);
            var adopted=await source.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT FROM cp6_privacy_disposition_v1.restore_outcomes
                  WHERE environment_id={admission.EnvironmentId} AND actor_tenant={admission.ActorTenantId} AND actor_id={admission.ActorId}
                    AND operation_id={admission.OperationId} AND candidate_id={candidateId} AND source_version={admission.SourceVersion}
                    AND target_identity={admission.TargetIdentity} AND state='APPLIED') AS "Value"
                """).SingleAsync(ct);
            if(environment!=admission.EnvironmentId||current!=admission.SourceVersion||!adopted)
                throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_SOURCE_PROOF_NOT_CURRENT",503);
            return new Lease(source,tx);
        }
        catch{if(tx!=null)await tx.DisposeAsync();await source.DisposeAsync();throw;}
    }
}
