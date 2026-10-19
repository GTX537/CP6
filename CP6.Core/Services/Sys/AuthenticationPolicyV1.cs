using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CP6.Core.Services.Sys;

public enum AuthenticationPrimaryMethod { Password, Sso, QuickSwitchPin }
public enum AuthenticationDecisionV1 { SessionAllowed, EnrollmentRequired, SecondFactorPending, PasswordChangeRequired }

/// <summary>Server-only evidence. Never bind this record from an HTTP request or reconstruct it from current user data.</summary>
public sealed record AuthenticationProofV1(
    Guid CompletionRef, Guid TenantId, Guid UserId, string ClientKind, string ClientBinding,
    AuthenticationPrimaryMethod PrimaryMethod, DateTimeOffset PrimaryVerifiedAtUtc, DateTimeOffset ExpiresAtUtc,
    string AuthenticationVersion, string PolicyVersion, string? SsoConfigurationVersion,
    string PrimaryEvidenceRef);

public sealed record AuthenticationFactorEvidenceV1(
    Guid CompletionRef, Guid TenantId, Guid UserId, string ClientBinding,
    string AuthenticationVersion, DateTimeOffset VerifiedAtUtc, string EvidenceRef);

public sealed record AuthenticationPolicyV1(
    Guid TenantId, string Version, int TwoFactorMode, bool? QuickSwitchAllowed,
    bool SsoEnabled, bool SsoEnforced, string? SsoConfigurationVersion, string? SsoAuthority, string? SsoClientId,
    int PasswordExpiryDays, int InteractionMinutes, bool EmailFallbackEnabled)
{
    public AuthenticationProofV1 VerifiedPrimary(Sys_User user, AuthenticationPrimaryMethod method,
        string clientKind, string clientBinding, string evidenceRef, DateTimeOffset verifiedAtUtc)
    {
        if (user.TenantId != TenantId || user.Id == Guid.Empty || string.IsNullOrWhiteSpace(clientBinding)
            || string.IsNullOrWhiteSpace(evidenceRef) || clientKind is not ("Web" or "Windows" or "Android")
            || !Enum.IsDefined(method) || verifiedAtUtc > DateTimeOffset.UtcNow || verifiedAtUtc.Offset != TimeSpan.Zero)
            throw new InvalidOperationException("REAUTH_REQUIRED");
        return new(Guid.NewGuid(), TenantId, user.Id, clientKind, clientBinding, method, verifiedAtUtc,
            verifiedAtUtc.AddMinutes(InteractionMinutes), AuthSessionVersion.For(user), Version,
            method == AuthenticationPrimaryMethod.Sso ? SsoConfigurationVersion : null, evidenceRef);
    }
}

/// <summary>Reads the existing tenant Owner's policy, plus this process's immutable security options.</summary>
public sealed class AuthenticationPolicyServiceV1(CP6Context db, IOptions<SecurityOptions> options)
{
    // Snapshot the configured policy, so mutation of an options object cannot change a gate mid-transaction.
    private readonly SecurityOptions security = JsonSerializer.Deserialize<SecurityOptions>(JsonSerializer.Serialize(options.Value))!;

    public async Task<AuthenticationPolicyV1> ReadAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (!db.Database.IsNpgsql()) throw new InvalidOperationException("AUTH_SESSION_AUTHORITY_UNAVAILABLE");
        var tenant = await db.Sys_Tenants.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == tenantId, ct);
        if (tenant == null || tenant.AuthenticationPolicyRevision == Guid.Empty || tenant.TwoFactorMode is < 0 or > 2
            || security.Password.ExpiryDays < 0 || security.TwoFactor.PendingTokenMinutes <= 0
            || security.TwoFactor.EmailOtpMinutes <= 0 || security.TwoFactor.CodeWindow < 0)
            throw new InvalidOperationException("AUTH_POLICY_UNAVAILABLE");
        var cfg = await db.Sys_TenantSsoConfigs.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);
        if (cfg is { Enabled: true } && (string.IsNullOrWhiteSpace(cfg.Authority) || string.IsNullOrWhiteSpace(cfg.ClientId)))
            throw new InvalidOperationException("AUTH_POLICY_UNAVAILABLE");
        var ssoVersion = cfg == null ? null : Hash(new object?[] { "cp6-sso-policy-v1", tenant.AuthenticationPolicyRevision,
            cfg.Id, cfg.TenantId, cfg.Authority, cfg.ClientId, cfg.ClientSecretProtected, cfg.Scopes, cfg.EmailClaim,
            cfg.Enabled, cfg.Enforced, cfg.AutoProvision, cfg.DefaultRoleId });
        var version = Hash(new object?[] { "cp6-auth-policy-v1", tenantId, tenant.AuthenticationPolicyRevision,
            tenant.TwoFactorMode, tenant.QuickSwitchAllowed, ssoVersion, security.Password, security.TwoFactor });
        return new(tenantId, version, tenant.TwoFactorMode, tenant.QuickSwitchAllowed, cfg?.Enabled == true,
            cfg?.Enforced == true, ssoVersion, cfg?.Authority, cfg?.ClientId, security.Password.ExpiryDays,
            security.TwoFactor.PendingTokenMinutes, security.TwoFactor.EmailFallbackEnabled);
    }

    internal static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}

public sealed class AuthenticationAuthorityV1(AuthenticationProofV1 proof, AuthenticationPolicyV1 policy, Sys_User user)
{
    public AuthenticationProofV1 Proof { get; } = proof;
    public AuthenticationPolicyV1 Policy { get; } = policy;
    public Sys_User User { get; } = user;

    public AuthenticationDecisionV1 Decide(AuthenticationFactorEvidenceV1? factor = null, bool refresh = false, bool persistedSession = false)
    {
        var now = DateTimeOffset.UtcNow;
        if (Proof.PrimaryMethod == AuthenticationPrimaryMethod.QuickSwitchPin
            && (Policy.QuickSwitchAllowed != true || Policy.TwoFactorMode == 2 || User.TwoFactorEnabled || Policy.SsoEnforced))
            throw new InvalidOperationException("REAUTH_REQUIRED");
        if (User.TwoFactorEnabled || Policy.TwoFactorMode == 2)
        {
            if (factor == null)
            {
                if (refresh) throw new InvalidOperationException("REAUTH_REQUIRED");
                return User.TwoFactorEnabled ? AuthenticationDecisionV1.SecondFactorPending : AuthenticationDecisionV1.EnrollmentRequired;
            }
            if (!User.TwoFactorEnabled || factor.CompletionRef != Proof.CompletionRef || factor.TenantId != User.TenantId
                || factor.UserId != User.Id || factor.ClientBinding != Proof.ClientBinding
                || factor.AuthenticationVersion != AuthSessionVersion.For(User) || string.IsNullOrWhiteSpace(factor.EvidenceRef)
                || factor.VerifiedAtUtc < Proof.PrimaryVerifiedAtUtc || factor.VerifiedAtUtc > now
                || (!refresh && !persistedSession && factor.VerifiedAtUtc.AddMinutes(Policy.InteractionMinutes) <= now))
                throw new InvalidOperationException("REAUTH_REQUIRED");
        }
        var passwordExpired = Proof.PrimaryMethod != AuthenticationPrimaryMethod.Sso && Policy.PasswordExpiryDays > 0
            && User.PasswordChangedAt is { } changed && (DateTime.Now - changed).TotalDays > Policy.PasswordExpiryDays;
        if (User.MustChangePassword || passwordExpired)
        {
            if (Proof.PrimaryMethod == AuthenticationPrimaryMethod.Sso)
                throw new InvalidOperationException("ACCOUNT_REMEDIATION_REQUIRED");
            if (refresh || Proof.PrimaryMethod == AuthenticationPrimaryMethod.QuickSwitchPin)
                throw new InvalidOperationException("REAUTH_REQUIRED");
            return AuthenticationDecisionV1.PasswordChangeRequired;
        }
        return AuthenticationDecisionV1.SessionAllowed;
    }
}
