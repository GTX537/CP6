using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

public sealed record SessionAuthenticationEvidenceV1(AuthenticationProofV1 Primary,
    string AuthenticationVersion, AuthenticationFactorEvidenceV1? Factor, Guid? EnrollmentTransition,
    bool Restricted, DateTimeOffset IssuedAtUtc, DateTimeOffset OriginalExpiresAtUtc, AuthenticationDeviceEvidenceV1? Device = null);

/// <summary>Private PostgreSQL authority records. No raw password, OTP, JWT or refresh token is stored.</summary>
public sealed class AuthenticationInteractionStoreV1(CP6Context db)
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string NewChallenge() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private void RequireTransaction()
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction == null)
            throw new InvalidOperationException("AUTH_SESSION_AUTHORITY_UNAVAILABLE");
    }

    public async Task<int> InsertAsync(AuthenticationProofV1 proof, string challenge, string purpose, CancellationToken ct)
    {
        RequireTransaction();
        if (proof.ClientKind == "Web") await new BrowserAuthenticationRecoveryV1(db).RequireOpenAsync(challenge, ct);
        if (await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_authentication_v1.interactions(completion_id,challenge_hash,proof,purpose,state) VALUES ({proof.CompletionRef},{Hash(challenge)},{JsonSerializer.Serialize(proof)}::jsonb,{purpose},'pending') ON CONFLICT DO NOTHING", ct) != 1)
            throw new InvalidOperationException("REAUTH_REQUIRED");
        return 1;
    }

    public async Task<AuthenticationInteractionV1> ReadAsync(string challenge, string binding, string? purpose, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<InteractionRow>($"SELECT proof::text AS \"Proof\", purpose AS \"Purpose\", state AS \"State\", preparation_hash AS \"Preparation\" FROM cp6_authentication_v1.interactions WHERE challenge_hash={Hash(challenge)}")
            .SingleOrDefaultAsync(ct);
        if (row == null || row.State != "pending" || (purpose != null && purpose != row.Purpose))
            throw new InvalidOperationException("E-SEC-013");
        var proof = JsonSerializer.Deserialize<AuthenticationProofV1>(row.Proof)!;
        if (proof.ClientBinding != binding || proof.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("E-SEC-013");
        return new(proof, row.Purpose, row.Preparation);
    }

    public async Task ClaimAsync(Guid completion, Guid lease, CancellationToken ct)
    {
        RequireTransaction();
        if (await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_authentication_v1.interactions SET state='completing',lease={lease} WHERE completion_id={completion} AND state='pending'", ct) != 1)
            throw new InvalidOperationException("E-SEC-013");
    }

    public async Task RequireLeaseAsync(Guid completion, Guid lease, CancellationToken ct)
    {
        RequireTransaction();
        if (!await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM cp6_authentication_v1.interactions WHERE completion_id={completion} AND state='completing' AND lease={lease}) AS \"Value\"").SingleAsync(ct))
            throw new InvalidOperationException("E-SEC-013");
    }

    private async Task<string> PreparationKeyAsync(Guid user, string secret, CancellationToken ct)
    {
        var revision = await db.Database.SqlQuery<string>($"SELECT revision::text AS \"Value\" FROM cp6_authentication_v1.preparation_versions WHERE user_id={user}").SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("AUTH_SESSION_AUTHORITY_UNAVAILABLE");
        return Hash(secret + ":" + revision);
    }

    public async Task<bool> PreparationMatchesAsync(Guid user, string? secret, string? expected, CancellationToken ct)
        => secret != null && expected != null && expected == await PreparationKeyAsync(user, secret, ct);

    public async Task SetPreparationAsync(Guid completion, Guid user, string secret, CancellationToken ct)
    {
        RequireTransaction();
        var key = await PreparationKeyAsync(user, secret, ct);
        if (await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_authentication_v1.interactions SET preparation_hash={key} WHERE completion_id={completion} AND state='pending'", ct) != 1)
            throw new InvalidOperationException("E-SEC-013");
    }

    public Task<int> FinishAsync(Guid completion, Guid lease, bool success, CancellationToken ct)
    {
        RequireTransaction();
        var state = success ? "completed" : "pending";
        return db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_authentication_v1.interactions SET state={state},lease=NULL WHERE completion_id={completion} AND state='completing' AND lease={lease}", ct);
    }

    public async Task<int> SaveSessionAsync(string refresh, string jti, SessionAuthenticationEvidenceV1 evidence, CancellationToken ct, string? parentHash = null)
    {
        RequireTransaction();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_authentication_v1.session_evidence(refresh_hash,access_jti,completion_id,evidence,parent_refresh_hash) VALUES ({Hash(refresh)},{jti},{evidence.Primary.CompletionRef},{JsonSerializer.Serialize(evidence)}::jsonb,{parentHash})", ct);
        return await AliasAccessAsync(jti, Hash(refresh), ct);
    }

    public async Task<bool> ConsumeTotpAsync(AuthenticationProofV1 proof, string secret, long counter, CancellationToken ct)
    {
        RequireTransaction();
        return await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_authentication_v1.consumed_totp(user_id,secret_hash,counter,completion_id) VALUES ({proof.UserId},{Hash(secret)},{counter},{proof.CompletionRef}) ON CONFLICT DO NOTHING", ct) == 1;
    }

    public async Task RecordFactorAsync(Guid lease, AuthenticationFactorEvidenceV1 factor, CancellationToken ct)
    {
        RequireTransaction();
        if (await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_authentication_v1.interactions SET factor={JsonSerializer.Serialize(factor)}::jsonb WHERE completion_id={factor.CompletionRef} AND state='completing' AND lease={lease} AND factor IS NULL", ct) != 1)
            throw new InvalidOperationException("E-SEC-013");
    }

    public async Task<AuthenticationFactorEvidenceV1> ReadFactorAsync(Guid completion, Guid lease, CancellationToken ct)
    {
        RequireTransaction();
        var value = await db.Database.SqlQuery<string>($"SELECT factor::text AS \"Value\" FROM cp6_authentication_v1.interactions WHERE completion_id={completion} AND state='completing' AND lease={lease} AND factor IS NOT NULL").SingleOrDefaultAsync(ct);
        return value == null ? throw new InvalidOperationException("E-SEC-013") : JsonSerializer.Deserialize<AuthenticationFactorEvidenceV1>(value)!;
    }

    // A failed response quarantines this issuance only, never another healthy device's session.
    public Task<int> RejectAsync(Guid completion, CancellationToken ct = default) => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_authentication_v1.session_evidence SET rejected_at=clock_timestamp() WHERE completion_id={completion} AND rejected_at IS NULL", ct);

    public Task<AuthenticationSessionRow?> ReadSessionAsync(string jti, CancellationToken ct = default)
        => db.Database.SqlQuery<AuthenticationSessionRow>($"SELECT s.refresh_hash AS \"RefreshHash\", s.evidence::text AS \"Evidence\", (s.rejected_at IS NOT NULL OR i.state<>'completed' OR r.\"CompletionPending\" OR r.\"CompletionRejectedAtUtc\" IS NOT NULL OR (r.\"RevokedAt\" IS NOT NULL AND r.\"ReplacedByTokenHash\" IS NULL)) AS \"Rejected\" FROM cp6_authentication_v1.session_evidence s JOIN cp6_authentication_v1.access_sessions a ON a.refresh_hash=s.refresh_hash JOIN cp6_authentication_v1.interactions i ON i.completion_id=s.completion_id JOIN public.\"Sys_RefreshTokens\" r ON r.\"TokenHash\"=s.refresh_hash WHERE a.access_jti={jti}").SingleOrDefaultAsync(ct);

    public Task<AuthenticationSessionRow?> ReadSessionByRefreshAsync(string hash, CancellationToken ct = default)
        => db.Database.SqlQuery<AuthenticationSessionRow>($"SELECT s.refresh_hash AS \"RefreshHash\", s.evidence::text AS \"Evidence\", (s.rejected_at IS NOT NULL OR i.state<>'completed') AS \"Rejected\" FROM cp6_authentication_v1.session_evidence s JOIN cp6_authentication_v1.interactions i ON i.completion_id=s.completion_id WHERE s.refresh_hash={hash}").SingleOrDefaultAsync(ct);

    public Task<int> AliasAccessAsync(string jti, string refreshHash, CancellationToken ct)
    {
        RequireTransaction();
        return db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_authentication_v1.access_sessions(access_jti,refresh_hash) VALUES({jti},{refreshHash})", ct);
    }

    public async Task<int> RejectGenerationAsync(string hash, bool wholeOrigin, CancellationToken ct)
    {
        RequireTransaction();
        await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
        return await db.Database.ExecuteSqlInterpolatedAsync($"WITH RECURSIVE affected AS (SELECT refresh_hash FROM cp6_authentication_v1.session_evidence WHERE refresh_hash={hash} OR ({wholeOrigin} AND completion_id=(SELECT completion_id FROM cp6_authentication_v1.session_evidence WHERE refresh_hash={hash})) UNION SELECT child.refresh_hash FROM cp6_authentication_v1.session_evidence child JOIN affected parent ON child.parent_refresh_hash=parent.refresh_hash), closed AS (UPDATE cp6_authentication_v1.session_evidence s SET rejected_at=COALESCE(rejected_at,clock_timestamp()) FROM affected a WHERE s.refresh_hash=a.refresh_hash RETURNING s.refresh_hash) UPDATE public.\"Sys_RefreshTokens\" r SET \"RevokedAt\"=COALESCE(r.\"RevokedAt\",clock_timestamp()),\"CompletionPending\"=false,\"CompletionRejectedAtUtc\"=COALESCE(r.\"CompletionRejectedAtUtc\",clock_timestamp()) WHERE r.\"TokenHash\" IN (SELECT refresh_hash FROM closed) OR r.\"TokenHash\"={hash}", ct);
    }

    public sealed class AuthenticationSessionRow
    {
        public string RefreshHash { get; set; } = "";
        public string Evidence { get; set; } = "";
        public bool Rejected { get; set; }
        public SessionAuthenticationEvidenceV1 Parse() => JsonSerializer.Deserialize<SessionAuthenticationEvidenceV1>(Evidence)!;
    }

    public sealed class InteractionRow
    {
        public string Proof { get; set; } = "";
        public string Purpose { get; set; } = "";
        public string State { get; set; } = "";
        public string? Preparation { get; set; }
    }
}

public sealed record AuthenticationInteractionV1(AuthenticationProofV1 Proof, string Purpose, string? PreparationHash);
