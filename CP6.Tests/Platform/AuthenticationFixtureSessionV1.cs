using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs.Client;
using CP6.WebApi.Services;
using Microsoft.Extensions.Options;

namespace CP6.Tests.Platform;

// A fresh, controlled Owner issuance fixture for Core transaction tests. HTTP behavior tests use actual login.
// This never repairs an existing token: verifies the fixture password/factor, then registers a NEW proof/session.
internal static class AuthenticationFixtureSessionV1
{
    internal const string Password = "Fixture1!OriginalPassword";

    internal static async Task<string> IssueAsync(CP6Context db, RefreshTokenService service, Sys_User user,
        RefreshTokenClientContext? client = null, string? jti = null)
    {
        Assert.True(new BCryptPasswordHasher(4).Verify(Password, user.Password));
        client ??= RefreshTokenClientContext.Web;
        var options = Options.Create(new SecurityOptions()); var policies = new AuthenticationPolicyServiceV1(db, options);
        var challenge = AuthenticationInteractionStoreV1.NewChallenge();
        var native = client.ClientKind == "Web" ? null : new ClientContextDto { ClientKind = client.ClientKind,
            DeviceId = client.DeviceId!, AppVersion = client.AppVersion ?? "1.0.0" };
        var proof = (await policies.ReadAsync(user.TenantId)).VerifiedPrimary(user, AuthenticationPrimaryMethod.Password,
            client.ClientKind, native == null ? AuthenticationEntryServiceV1.WebBinding(challenge) : AuthenticationEntryServiceV1.Binding(native),
            "verified-fixture-password", DateTimeOffset.UtcNow);
        return await new AuthenticationCompletionGateV1(db, policies).ExecuteAsync(proof, async (a, ct) =>
        {
            var store = new AuthenticationInteractionStoreV1(db); var lease = Guid.NewGuid();
            await store.InsertAsync(proof, challenge, "primary", ct); await store.ClaimAsync(proof.CompletionRef, lease, ct);
            AuthenticationFactorEvidenceV1? factor = null;
            if (a.User.TwoFactorEnabled)
            {
                var code = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(a.User.TwoFactorSecret!)).ComputeTotp();
                Assert.True(new TotpService(options).TryVerify(a.User.TwoFactorSecret!, code, out var counter));
                Assert.True(await store.ConsumeTotpAsync(proof, a.User.TwoFactorSecret!, counter, ct));
                factor = new(proof.CompletionRef, user.TenantId, user.Id, proof.ClientBinding,
                    AuthSessionVersion.For(a.User), DateTimeOffset.UtcNow, "verified-fixture-totp");
                await store.RecordFactorAsync(lease, factor, ct);
            }
            Assert.Equal(AuthenticationDecisionV1.SessionAllowed, a.Decide(factor));
            var raw = await service.IssueWithinAuthenticationAsync(a.User, null, null, client);
            var row = db.Sys_RefreshTokens.Local.Single(x => x.TokenHash == AuthenticationInteractionStoreV1.Hash(raw));
            var device = native == null ? null : await new AuthenticationDeviceAuthorityV1(db).CaptureAsync(user.TenantId, native, ct);
            await store.SaveSessionAsync(raw, jti ?? Guid.NewGuid().ToString(), new(proof, AuthSessionVersion.For(a.User), factor, null,
                false, DateTimeOffset.UtcNow, new DateTimeOffset(row.ExpiresAt).ToUniversalTime(), device), ct);
            await store.FinishAsync(proof.CompletionRef, lease, true, ct); return raw;
        });
    }
}
