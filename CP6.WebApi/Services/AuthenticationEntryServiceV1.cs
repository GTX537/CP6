using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs.Client;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Services;

public sealed record AuthenticationEntryResultV1(AuthenticationProofV1 Proof, string? Challenge,
    string? Purpose, TokenSessionDto? Session);

/// <summary>All PostgreSQL interactive issuers retain the original primary proof through one completion transaction.</summary>
public sealed class AuthenticationEntryServiceV1(CP6Context db, ITenantContext tenant,
    AuthenticationCompletionGateV1 gate, AuthenticationInteractionStoreV1 store,
    IAuthSessionService sessions, IRefreshTokenService refresh, ITwoFactorService factors,
    ILoginSecurityService login, IClientDeviceService devices)
{
    public static string Binding(ClientContextDto client) => AuthenticationInteractionStoreV1.Hash(
        JsonSerializer.Serialize(new[] { client.ClientKind, client.DeviceId, client.AppVersion, client.PlatformVersion }));
    public static string WebBinding(string challenge) => AuthenticationInteractionStoreV1.Hash(challenge);

    public Task<AuthenticationEntryResultV1> BeginAsync(AuthenticationProofV1 proof, string challenge,
        ClientContextDto? client, string? ip, string? ua, Action<AuthenticationEntryResultV1>? stage, CancellationToken ct)
    {
        tenant.CurrentTenantId = proof.TenantId;
        RequireClient(proof, challenge, client);
        return gate.ExecuteAsync(proof, async (authority, token) =>
        {
            var decision = authority.Decide();
            var purpose = decision switch
            {
                AuthenticationDecisionV1.EnrollmentRequired => "2fa_enroll",
                AuthenticationDecisionV1.SecondFactorPending => "2fa_verify",
                _ => "primary"
            };
            await store.InsertAsync(proof, challenge, purpose, token);
            AuthenticationEntryResultV1 result;
            if (purpose != "primary")
            {
                Audit(authority.User, SecurityEventType.TwoFactorChallenged, proof, ip, ua);
                result = new(proof, challenge, purpose, null);
            }
            else
            {
                var lease = Guid.NewGuid();
                await store.ClaimAsync(proof.CompletionRef, lease, token);
                result = await IssueAsync(authority, null, null, client, ip, ua, token);
                await store.FinishAsync(proof.CompletionRef, lease, true, token);
            }
            stage?.Invoke(result);
            return result;
        }, ct);
    }

    public async Task<AuthenticationInteractionV1> ReadAsync(string challenge, ClientContextDto? client,
        string? purpose, CancellationToken ct)
    {
        if (client != null) ClientContextValidator.Validate(client);
        var pending = await store.ReadAsync(challenge, client == null ? WebBinding(challenge) : Binding(client), purpose, ct);
        RequireClient(pending.Proof, challenge, client);
        tenant.CurrentTenantId = pending.Proof.TenantId;
        return pending;
    }

    public async Task<object> SetupAsync(string challenge, ClientContextDto? client, CancellationToken ct)
    {
        var pending = await ReadAsync(challenge, client, "2fa_enroll", ct);
        return await gate.ExecuteAsync<object>(pending.Proof, async (a, token) =>
        {
            await ReadAsync(challenge, client, "2fa_enroll", token);
            if (a.Decide() != AuthenticationDecisionV1.EnrollmentRequired) throw new InvalidOperationException("E-SEC-013");
            var uri = factors.BeginEnrollment(a.User);
            await db.SaveChangesAsync(token);
            await store.SetPreparationAsync(a.Proof.CompletionRef, a.User.Id, a.User.TwoFactorSecret!, token);
            return new { otpauthUri = uri, secret = a.User.TwoFactorSecret };
        }, ct);
    }

    public async Task SendEmailAsync(string challenge, ClientContextDto? client, CancellationToken ct)
    {
        var pending = await ReadAsync(challenge, client, "2fa_verify", ct);
        await gate.ExecuteAsync(pending.Proof, async (a, token) =>
        {
            await ReadAsync(challenge, client, "2fa_verify", token);
            if (!a.Policy.EmailFallbackEnabled || a.Decide() != AuthenticationDecisionV1.SecondFactorPending)
                throw new InvalidOperationException("E-SEC-014");
            await factors.SendEmailOtpAsync(a.User, challenge);
            return true;
        }, ct);
    }

    public async Task<AuthenticationEntryResultV1> CompleteFactorAsync(string challenge, ClientContextDto? client,
        bool enroll, string? method, string code, string? ip, string? ua,
        Action<AuthenticationEntryResultV1>? stage, CancellationToken ct)
    {
        var pending = await ReadAsync(challenge, client, enroll ? "2fa_enroll" : "2fa_verify", ct);
        var lease = Guid.NewGuid();
        // Claim commits before any externally consumed OTP. A later failure never restores this lease.
        await gate.ExecuteAsync(pending.Proof, async (a, token) =>
        {
            pending = await ReadAsync(challenge, client, pending.Purpose, token);
            var expected = enroll ? AuthenticationDecisionV1.EnrollmentRequired : AuthenticationDecisionV1.SecondFactorPending;
            if (a.Decide() != expected || enroll && !await store.PreparationMatchesAsync(a.User.Id, a.User.TwoFactorSecret, pending.PreparationHash, token))
                throw new InvalidOperationException("E-SEC-013");
            await store.ClaimAsync(a.Proof.CompletionRef, lease, token);
            return true;
        }, ct);
        try
        {
            await gate.ExecuteAsync(pending.Proof, async (a, token) =>
            {
                await store.RequireLeaseAsync(a.Proof.CompletionRef, lease, token);
                if (enroll && !await store.PreparationMatchesAsync(a.User.Id, a.User.TwoFactorSecret, pending.PreparationHash, token))
                    throw new InvalidOperationException("E-SEC-013");
                var email = string.Equals(method, "email", StringComparison.OrdinalIgnoreCase);
                if (enroll && email || email && !a.Policy.EmailFallbackEnabled || !email && method is not (null or "totp"))
                    throw new InvalidOperationException("E-SEC-014");
                long counter = -1;
                var valid = email ? await factors.VerifyEmailOtpAsync(challenge, code) : factors.TryVerifyTotp(a.User, code, out counter);
                if (valid && !email) valid = await store.ConsumeTotpAsync(a.Proof, a.User.TwoFactorSecret!, counter, token);
                if (!valid) throw new FactorRejectedException();
                var verified = new AuthenticationFactorEvidenceV1(a.Proof.CompletionRef, a.User.TenantId, a.User.Id,
                    a.Proof.ClientBinding, AuthSessionVersion.For(a.User), DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"));
                await store.RecordFactorAsync(lease, verified, token);
                return true;
            }, ct);
            // A successful factor has its own durable receipt before enrollment/session/output.
            return await gate.ExecuteAsync(pending.Proof, async (a, token) =>
            {
                var verified = await store.ReadFactorAsync(a.Proof.CompletionRef, lease, token);
                if (verified.AuthenticationVersion != a.Proof.AuthenticationVersion || verified.ClientBinding != a.Proof.ClientBinding
                    || verified.UserId != a.User.Id || verified.TenantId != a.User.TenantId
                    || enroll && !await store.PreparationMatchesAsync(a.User.Id, a.User.TwoFactorSecret, pending.PreparationHash, token))
                    throw new InvalidOperationException("E-SEC-013");
                Guid? transition = null;
                if (enroll)
                {
                    // The original proof is retained; only this claimed preparation may advance the epoch.
                    a.User.TwoFactorEnabled = true;
                    a.User.TwoFactorEnrolledAt = DateTime.Now;
                    a.User.AuthenticationEpoch = Guid.NewGuid();
                    await db.SaveChangesAsync(token); // reads generated CredentialRevision back
                    transition = Guid.NewGuid();
                }
                var factor = verified with { AuthenticationVersion = AuthSessionVersion.For(a.User) };
                Audit(a.User, enroll ? SecurityEventType.TwoFactorEnrolled : SecurityEventType.TwoFactorVerified, a.Proof, ip, ua);
                var result = await IssueAsync(a, factor, transition, client, ip, ua, token);
                await store.FinishAsync(a.Proof.CompletionRef, lease, true, token);
                stage?.Invoke(result);
                return result;
            }, ct);
        }
        catch (FactorRejectedException)
        {
            // Persist the real failure, including lockout. This negative result does not issue a session.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
            var user = await db.Sys_Users.IgnoreQueryFilters().SingleAsync(x => x.Id == pending.Proof.UserId, ct);
            await db.Entry(user).ReloadAsync(ct);
            if (user.TenantId != pending.Proof.TenantId || AuthSessionVersion.For(user) != pending.Proof.AuthenticationVersion)
                throw new InvalidOperationException("REAUTH_REQUIRED");
            await login.RecordFailureAsync(user);
            Audit(user, SecurityEventType.TwoFactorFailed, pending.Proof, ip, ua);
            await store.FinishAsync(pending.Proof.CompletionRef, lease, false, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            throw new InvalidOperationException("E-SEC-011");
        }
    }

    private async Task<AuthenticationEntryResultV1> IssueAsync(AuthenticationAuthorityV1 authority,
        AuthenticationFactorEvidenceV1? factor, Guid? transition, ClientContextDto? client,
        string? ip, string? ua, CancellationToken ct)
    {
        var decision = authority.Decide(factor);
        if (decision is not (AuthenticationDecisionV1.SessionAllowed or AuthenticationDecisionV1.PasswordChangeRequired))
            throw new InvalidOperationException("REAUTH_REQUIRED");
        var restricted = decision == AuthenticationDecisionV1.PasswordChangeRequired;
        // Existing native clients have no restricted-session flow; they must never enter business UI with one.
        if (client != null && restricted) throw new InvalidOperationException("AUTH_RESTRICTED_SESSION_UNAVAILABLE");
        if (client != null) await devices.EnsureLoginAllowedAsync(client, authority.User.TenantId, ct);
        var jti = Guid.NewGuid().ToString();
        var access = sessions.BuildAccessToken(authority.User, jti, restricted, authority.User.IsPlatformAdmin);
        var profile = await sessions.BuildProfileAsync(authority.User, restricted);
        await login.RecordSuccessAsync(authority.User, ip);
        var raw = await refresh.IssueWithinAuthenticationAsync(authority.User, ip, ua,
            client == null ? RefreshTokenClientContext.Web : new(client.ClientKind, client.DeviceId, client.AppVersion));
        var issuedRow = db.Sys_RefreshTokens.Local.Single(x => x.TokenHash == AuthenticationInteractionStoreV1.Hash(raw));
        var expires = new DateTimeOffset(issuedRow.ExpiresAt).ToUniversalTime();
        await store.SaveSessionAsync(raw, jti, new(authority.Proof, AuthSessionVersion.For(authority.User), factor,
            transition, restricted, DateTimeOffset.UtcNow, expires), ct);
        if (client != null && !restricted)
            await devices.MarkFullAuthenticationAsync(client, authority.User.TenantId, authority.User.UserName, ct);
        Audit(authority.User, authority.Proof.PrimaryMethod == AuthenticationPrimaryMethod.Sso
            ? SecurityEventType.SsoLoginSuccess : SecurityEventType.LoginSuccess, authority.Proof, ip, ua);
        return new(authority.Proof, null, null, new TokenSessionDto { AccessToken = access, AccessExpiresAt = sessions.AccessExpiresAt,
            RefreshToken = raw, RefreshExpiresAt = expires, Profile = profile });
    }

    public async Task RejectOutputAsync(AuthenticationEntryResultV1 result)
    {
        if (result.Session == null) return;
        await store.RejectAsync(result.Proof.CompletionRef);
        await refresh.RejectCompletionAsync(result.Session.RefreshToken);
    }

    private void Audit(Sys_User user, SecurityEventType type, AuthenticationProofV1 proof, string? ip, string? ua)
        => db.Sys_SecurityLogs.Add(new Sys_SecurityLog { TenantId = user.TenantId, UserId = user.Id, UserName = user.UserName,
            EventType = (int)type, CreatedAt = DateTime.Now, ClientIp = ip, UserAgent = ua?.Length > 256 ? ua[..256] : ua,
            ContextJson = JsonSerializer.Serialize(new { proof.CompletionRef, proof.PrimaryMethod, proof.PrimaryVerifiedAtUtc, proof.PolicyVersion }) });

    private static void RequireClient(AuthenticationProofV1 proof, string challenge, ClientContextDto? client)
    {
        if (client != null) ClientContextValidator.Validate(client);
        if (proof.ClientKind != (client?.ClientKind ?? "Web") || proof.ClientBinding != (client == null ? WebBinding(challenge) : Binding(client)))
            throw new InvalidOperationException("E-SEC-013");
    }
    private sealed class FactorRejectedException : Exception;
}
