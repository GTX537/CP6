using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

/// <summary>Consumes a completed original interaction; never manufactures a proof from today's account state.</summary>
public sealed class AuthenticationSessionAuthorityV1(CP6Context db, AuthenticationPolicyServiceV1 policies)
{
    public async Task<AuthenticationAuthorityV1> ValidateAsync(
        AuthenticationInteractionStoreV1.AuthenticationSessionRow? receipt, Sys_User user,
        bool refresh, CancellationToken ct = default)
    {
        if (receipt == null || receipt.Rejected) throw new InvalidOperationException("REAUTH_REQUIRED");
        var evidence = receipt.Parse(); var proof = evidence.Primary; var now = DateTimeOffset.UtcNow;
        var policy = await policies.ReadAsync(user.TenantId, ct);
        if (proof.CompletionRef == Guid.Empty || proof.UserId != user.Id || proof.TenantId != user.TenantId
            || !user.Enable || user.LockedUntil > DateTime.Now || evidence.AuthenticationVersion != AuthSessionVersion.For(user)
            || proof.PolicyVersion != policy.Version || string.IsNullOrWhiteSpace(proof.ClientBinding)
            || string.IsNullOrWhiteSpace(proof.PrimaryEvidenceRef) || !Enum.IsDefined(proof.PrimaryMethod)
            || proof.ClientKind is not ("Web" or "Windows" or "Android")
            || proof.PrimaryVerifiedAtUtc > evidence.IssuedAtUtc || evidence.IssuedAtUtc > now
            || proof.ExpiresAtUtc <= proof.PrimaryVerifiedAtUtc
            || proof.ExpiresAtUtc > proof.PrimaryVerifiedAtUtc.AddMinutes(policy.InteractionMinutes)
            || evidence.OriginalExpiresAtUtc <= now
            || (proof.AuthenticationVersion != evidence.AuthenticationVersion
                && (evidence.EnrollmentTransition is null || evidence.EnrollmentTransition == Guid.Empty
                    || evidence.Factor == null || !user.TwoFactorEnabled)))
            throw new InvalidOperationException("REAUTH_REQUIRED");
        if (!await db.Sys_Tenants.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.Id == user.TenantId && x.Enable
            && (x.ExpireDate == null || x.ExpireDate > DateTime.Now), ct)) throw new InvalidOperationException("REAUTH_REQUIRED");
        if (proof.PrimaryMethod == AuthenticationPrimaryMethod.Sso)
        {
            if (!policy.SsoEnabled || string.IsNullOrWhiteSpace(proof.SsoConfigurationVersion)
                || proof.SsoConfigurationVersion != policy.SsoConfigurationVersion) throw new InvalidOperationException("REAUTH_REQUIRED");
        }
        else if (policy.SsoEnforced && (proof.PrimaryMethod != AuthenticationPrimaryMethod.Password || !user.AllowPasswordFallback))
            throw new InvalidOperationException("REAUTH_REQUIRED");
        var authority = new AuthenticationAuthorityV1(proof, policy, user);
        await new AuthenticationDeviceAuthorityV1(db).ValidateAsync(evidence, ct);
        var decision = authority.Decide(evidence.Factor, refresh, persistedSession: true);
        if (decision is not (AuthenticationDecisionV1.SessionAllowed or AuthenticationDecisionV1.PasswordChangeRequired)
            || evidence.Restricted != (decision == AuthenticationDecisionV1.PasswordChangeRequired)
            || refresh && evidence.Restricted) throw new InvalidOperationException("REAUTH_REQUIRED");
        return authority;
    }
}
