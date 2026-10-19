using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

public sealed record AuthenticationRecoveryRequestV1(string Kind, string Credential, string ClientKind,
    string? DeviceId, string? InteractionBinding);
public sealed record AuthenticationRecoveryStatusV1(string State);

/// <summary>Owner recovery authenticates possession of the original interaction secret, never a completion id.</summary>
public sealed class AuthenticationCompletionRecoveryV1(CP6Context db)
{
    public async Task<AuthenticationRecoveryStatusV1> ResolveAsync(AuthenticationRecoveryRequestV1 request,
        bool close, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("AUTH_SESSION_AUTHORITY_UNAVAILABLE");
        if (request.Credential == null || request.Credential.Length is < 32 or > 512 || request.Kind is not ("refresh" or "interaction"))
            throw new InvalidOperationException("REAUTH_REQUIRED");
        await using var tx = await db.Database.BeginTransactionAsync(ct); var commitAttempted = false;
        try
        {
            await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
            var store = new AuthenticationInteractionStoreV1(db);
            string? targetHash = null; Guid completion; string state;
            if (request.Kind == "refresh")
            {
                var hash = AuthenticationInteractionStoreV1.Hash(request.Credential);
                var row = await db.Sys_RefreshTokens.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
                var receipt = await store.ReadSessionByRefreshAsync(hash, ct);
                if (row == null || receipt == null || row.ClientKind != request.ClientKind
                    || row.DeviceId != request.DeviceId) throw new InvalidOperationException("REAUTH_REQUIRED");
                var proof = receipt.Parse(); completion = proof.Primary.CompletionRef;
                if (proof.Primary.UserId != row.UserId || proof.Primary.TenantId != row.TenantId
                    || proof.Primary.ClientKind != request.ClientKind
                    || request.ClientKind != "Web" && proof.Device?.DeviceId != request.DeviceId)
                    throw new InvalidOperationException("REAUTH_REQUIRED");
                // The source credential identifies its one rotation successor. No arbitrary generation/id can be submitted.
                targetHash = row.ReplacedByTokenHash ?? hash;
                var target = await store.ReadSessionByRefreshAsync(targetHash, ct);
                if (target == null) throw new InvalidOperationException("AUTH_SESSION_AUTHORITY_UNAVAILABLE");
                var targetRow = await db.Sys_RefreshTokens.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.TokenHash == targetHash, ct);
                state = target.Rejected || targetRow.CompletionRejectedAtUtc != null ? "CLOSED"
                    : targetRow.CompletionPending ? "COMPLETION_UNKNOWN" : "COMPLETED";
            }
            else
            {
                var hash = AuthenticationInteractionStoreV1.Hash(request.Credential);
                var row = await db.Database.SqlQuery<RecoveryRow>($"SELECT proof::text AS \"Proof\", state AS \"State\" FROM cp6_authentication_v1.interactions WHERE challenge_hash={hash}").SingleOrDefaultAsync(ct);
                // Absence does not prove that an in-flight caller will never register this attempt.
                if (row == null) throw new InvalidOperationException("AUTH_SESSION_AUTHORITY_UNAVAILABLE");
                var proof = JsonSerializer.Deserialize<AuthenticationProofV1>(row.Proof)!;
                if (proof.ClientKind != request.ClientKind || proof.ClientBinding != request.InteractionBinding)
                    throw new InvalidOperationException("REAUTH_REQUIRED");
                completion = proof.CompletionRef;
                state = row.State switch { "completed" => "COMPLETED", "rejected" => "CLOSED", "pending" => "PENDING", _ => "COMPLETION_UNKNOWN" };
                if (state == "COMPLETED")
                {
                    var pending = await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM cp6_authentication_v1.session_evidence s JOIN public.\"Sys_RefreshTokens\" r ON r.\"TokenHash\"=s.refresh_hash WHERE s.completion_id={completion} AND r.\"CompletionPending\") AS \"Value\"").SingleAsync(ct);
                    var rejected = await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM cp6_authentication_v1.session_evidence s WHERE s.completion_id={completion} AND s.parent_refresh_hash IS NULL AND s.rejected_at IS NOT NULL) AS \"Value\"").SingleAsync(ct);
                    state = rejected ? "CLOSED" : pending ? "COMPLETION_UNKNOWN" : state;
                }
            }
            if (close)
            {
                if (targetHash != null) await store.RejectGenerationAsync(targetHash, false, ct);
                else
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_authentication_v1.interactions SET state='rejected',lease=NULL WHERE completion_id={completion}", ct);
                    var roots = await db.Database.SqlQuery<string>($"SELECT refresh_hash AS \"Value\" FROM cp6_authentication_v1.session_evidence WHERE completion_id={completion} AND parent_refresh_hash IS NULL").ToListAsync(ct);
                    foreach (var root in roots) await store.RejectGenerationAsync(root, false, ct);
                }
                state = "CLOSED";
            }
            ct.ThrowIfCancellationRequested(); commitAttempted = true; await tx.CommitAsync(ct);
            return new(state);
        }
        catch (Exception error)
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rollback) { throw new RefreshCompletionUnknownException(new AggregateException(error, rollback)); }
            if (commitAttempted) throw new RefreshCompletionUnknownException(error);
            throw;
        }
    }

    public sealed class RecoveryRow
    {
        public string Proof { get; set; } = "";
        public string State { get; set; } = "";
    }
}
