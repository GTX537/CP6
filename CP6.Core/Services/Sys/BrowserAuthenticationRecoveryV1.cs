using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

public sealed record BrowserAuthenticationStatusV1(string State, string? Purpose = null, DateTimeOffset? ExpiresAt = null);

/// <summary>Browser-held capabilities fence a late initial request and identify one exact rotation.</summary>
public sealed class BrowserAuthenticationRecoveryV1(CP6Context db)
{
    public static string KeyHash(string key)
    {
        if (key is not { Length: 64 } || !key.All(Uri.IsHexDigit)) throw new InvalidOperationException("REAUTH_REQUIRED");
        return AuthenticationInteractionStoreV1.Hash(key);
    }

    public async Task RequireOpenAsync(string key, CancellationToken ct)
    {
        var hash = KeyHash(key);
        if (await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM cp6_authentication_v1.browser_closures WHERE challenge_hash={hash}) AS \"Value\"").SingleAsync(ct))
            throw new InvalidOperationException("REAUTH_REQUIRED");
    }

    // Called inside the refresh Owner transaction, after the replacement receipt was registered.
    public async Task RecordRotationAsync(string key, string rawReplacement, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("AUTH_SESSION_AUTHORITY_UNAVAILABLE");
        await RequireOpenAsync(key, ct); var hash = KeyHash(key);
        if (await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM cp6_authentication_v1.interactions WHERE challenge_hash={hash}) AS \"Value\"").SingleAsync(ct))
            throw new InvalidOperationException("REAUTH_REQUIRED");
        if (await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_authentication_v1.browser_rotations(challenge_hash,refresh_hash) VALUES({hash},{AuthenticationInteractionStoreV1.Hash(rawReplacement)}) ON CONFLICT DO NOTHING", ct) != 1)
            throw new InvalidOperationException("REAUTH_REQUIRED");
    }

    public async Task RequireProfileAsync(string key, string? accessJti, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || string.IsNullOrEmpty(accessJti)) throw new InvalidOperationException("REAUTH_REQUIRED");
        var hash = KeyHash(key); await RequireOpenAsync(key, ct);
        var current = await new AuthenticationInteractionStoreV1(db).ReadSessionAsync(accessJti, ct);
        if (current == null || current.Parse().Primary.ClientKind != "Web") throw new InvalidOperationException("REAUTH_REQUIRED");
        var match = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM cp6_authentication_v1.access_sessions a
              JOIN cp6_authentication_v1.session_evidence s ON s.refresh_hash=a.refresh_hash
              WHERE a.access_jti={accessJti} AND s.rejected_at IS NULL AND (
                EXISTS(SELECT 1 FROM cp6_authentication_v1.interactions i WHERE i.challenge_hash={hash}
                  AND i.completion_id=s.completion_id AND i.state='completed') OR
                EXISTS(SELECT 1 FROM cp6_authentication_v1.browser_rotations r WHERE r.challenge_hash={hash} AND r.refresh_hash=s.refresh_hash))) AS "Value"
            """).SingleAsync(ct);
        if (!match) throw new InvalidOperationException("REAUTH_REQUIRED");
    }

    public async Task<BrowserAuthenticationStatusV1> ResolveAsync(string key, bool close, CancellationToken ct)
    {
        var hash = KeyHash(key);
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("AUTH_SESSION_AUTHORITY_UNAVAILABLE");
        await using var tx = await db.Database.BeginTransactionAsync(ct); var committing = false;
        try
        {
            await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
            var row = await db.Database.SqlQuery<Interaction>($"SELECT proof::text AS \"Proof\",purpose AS \"Purpose\",state AS \"State\" FROM cp6_authentication_v1.interactions WHERE challenge_hash={hash}").SingleOrDefaultAsync(ct);
            var proof = row == null ? null : JsonSerializer.Deserialize<AuthenticationProofV1>(row.Proof)!;
            if (proof != null && (proof.ClientKind != "Web" || proof.ClientBinding != hash)) throw new InvalidOperationException("REAUTH_REQUIRED");
            var rotation = await db.Database.SqlQuery<string>($"SELECT refresh_hash AS \"Value\" FROM cp6_authentication_v1.browser_rotations WHERE challenge_hash={hash}").SingleOrDefaultAsync(ct);
            var closed = await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM cp6_authentication_v1.browser_closures WHERE challenge_hash={hash}) AS \"Value\"").SingleAsync(ct);
            string state;
            if (close)
            {
                // Unlike 'not found', this durable fence proves a late request cannot register the key.
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_authentication_v1.browser_closures(challenge_hash) VALUES({hash}) ON CONFLICT DO NOTHING", ct);
                var store = new AuthenticationInteractionStoreV1(db);
                if (rotation != null) await store.RejectGenerationAsync(rotation, false, ct);
                if (proof != null)
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_authentication_v1.interactions SET state='rejected',lease=NULL WHERE completion_id={proof.CompletionRef}", ct);
                    var roots = await db.Database.SqlQuery<string>($"SELECT refresh_hash AS \"Value\" FROM cp6_authentication_v1.session_evidence WHERE completion_id={proof.CompletionRef} AND parent_refresh_hash IS NULL").ToListAsync(ct);
                    foreach (var root in roots) await store.RejectGenerationAsync(root, false, ct);
                }
                var original = proof ?? (rotation == null ? null : (await store.ReadSessionByRefreshAsync(rotation, ct))?.Parse().Primary);
                if (!closed && original != null)
                {
                    var name = await db.Sys_Users.IgnoreQueryFilters().AsNoTracking()
                        .Where(x => x.Id == original.UserId && x.TenantId == original.TenantId).Select(x => x.UserName).SingleOrDefaultAsync(ct);
                    db.Sys_SecurityLogs.Add(new Sys_SecurityLog { TenantId = original.TenantId, UserId = original.UserId, UserName = name,
                        EventType = (int)SecurityEventType.Logout, CreatedAt = DateTime.Now, Reason = "browser authentication closure",
                        ContextJson = JsonSerializer.Serialize(new { original.CompletionRef, scope = rotation == null ? "original-interaction" : "exact-rotation" }) });
                    await db.SaveChangesAsync(ct);
                }
                state = "CLOSED";
            }
            else if (closed || row?.State == "rejected") state = "CLOSED";
            else if (rotation != null)
            {
                var target = await db.Sys_RefreshTokens.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.TokenHash == rotation, ct);
                state = target.CompletionRejectedAtUtc != null || target.RevokedAt != null ? "CLOSED" : target.CompletionPending ? "COMPLETION_UNKNOWN" : "COMPLETED";
            }
            else if (row == null) state = "UNAVAILABLE";
            else if (row.State == "pending") state = proof!.ExpiresAtUtc <= DateTimeOffset.UtcNow ? "REAUTH_REQUIRED" : "PENDING";
            else if (row.State == "completed")
            {
                var flags = await db.Database.SqlQuery<SessionFlags>($"""
                    SELECT bool_or(s.rejected_at IS NOT NULL OR r."CompletionRejectedAtUtc" IS NOT NULL) AS "Rejected",
                      bool_or(r."CompletionPending") AS "Pending"
                    FROM cp6_authentication_v1.session_evidence s JOIN public."Sys_RefreshTokens" r ON r."TokenHash"=s.refresh_hash
                    WHERE s.completion_id={proof!.CompletionRef} AND s.parent_refresh_hash IS NULL
                    """).SingleAsync(ct);
                state = flags.Rejected == true ? "CLOSED" : flags.Pending == false ? "COMPLETED" : "COMPLETION_UNKNOWN";
            }
            else state = "COMPLETION_UNKNOWN";
            ct.ThrowIfCancellationRequested(); committing = true; await tx.CommitAsync(ct);
            return new(state, state == "PENDING" ? row?.Purpose : null, state == "PENDING" ? proof?.ExpiresAtUtc : null);
        }
        catch (Exception error)
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rollback) { throw new RefreshCompletionUnknownException(new AggregateException(error, rollback)); }
            if (committing) throw new RefreshCompletionUnknownException(error);
            throw;
        }
    }
    public sealed class Interaction
    {
        public string Proof { get; set; } = "";
        public string Purpose { get; set; } = "";
        public string State { get; set; } = "";
    }
    public sealed class SessionFlags
    {
        public bool? Rejected { get; set; }
        public bool? Pending { get; set; }
    }
}
