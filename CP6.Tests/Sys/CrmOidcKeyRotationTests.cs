using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.WebApi.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace CP6.Tests.Sys;

public sealed class CrmOidcKeyRotationTests
{
    [Fact]
    public void Published_keys_overlap_until_last_old_token_expires_then_retire()
    {
        using var oldKey = RSA.Create(2048);
        using var newKey = RSA.Create(2048);
        var clock = new TestClock();
        using var services = new ServiceCollection().AddSingleton<TimeProvider>(clock).BuildServiceProvider();
        CrmOidcCrypto Create(CrmOidcOptions options) => ActivatorUtilities.CreateInstance<CrmOidcCrypto>(services, options);
        string Sign(CrmOidcCrypto issuer) => issuer.Sign("CP6.Services",
            [new Claim("sub", "service:worker"), new Claim("jti", Guid.NewGuid().ToString())], clock.GetUtcNow().AddSeconds(300), "at+jwt");

        // 1: old signing key; 2: publish both keys, continuing to sign old.
        using var oldIssuer = Create(Options("old", oldKey, newKey, includeNew: false));
        var first = Sign(oldIssuer);
        using var publish = Create(Options("old", oldKey, newKey));
        Assert.Equal("old", Read(first).Header.Kid);
        publish.Validate(first, "CP6.Services", "at+jwt");
        clock.Advance(TimeSpan.FromSeconds(60));
        var lastOld = Sign(publish);
        Assert.Equal(clock.GetUtcNow().ToUnixTimeSeconds(), long.Parse(Read(lastOld).Claims.Single(c => c.Type == "iat").Value));

        // 3: new signer, old public key retained. Every unexpired old token remains accepted.
        using var activate = Create(Options("new", oldKey, newKey));
        activate.Validate(lastOld, "CP6.Services", "at+jwt");
        clock.Advance(TimeSpan.FromSeconds(299));
        activate.Validate(lastOld, "CP6.Services", "at+jwt");
        var current = Sign(activate);
        Assert.Equal("new", Read(current).Header.Kid);
        publish.Validate(current, "CP6.Services", "at+jwt");

        // 4: retained key cannot revive an expired token. 5: retire old, keep new tokens valid.
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Throws<SecurityTokenExpiredException>(() => activate.Validate(lastOld, "CP6.Services", "at+jwt"));
        using var retire = Create(Options("new", oldKey, newKey, includeOld: false));
        retire.Validate(current, "CP6.Services", "at+jwt");
        Assert.ThrowsAny<SecurityTokenException>(() => retire.Validate(lastOld, "CP6.Services", "at+jwt", validateLifetime: false));
        using var jwks = JsonDocument.Parse(JsonSerializer.Serialize(retire.Jwks()));
        Assert.Equal("new", Assert.Single(jwks.RootElement.GetProperty("keys").EnumerateArray()).GetProperty("kid").GetString());
        foreach (var field in new[] { "d", "p", "q", "dp", "dq", "qi" })
            Assert.False(jwks.RootElement.GetProperty("keys")[0].TryGetProperty(field, out _));
    }

    [Fact]
    public void Running_crypto_keeps_its_configuration_snapshot_until_recreated()
    {
        using var oldKey = RSA.Create(2048);
        using var newKey = RSA.Create(2048);
        var options = Options("old", oldKey, newKey);
        options.Keys[1].Pem = newKey.ExportRSAPrivateKeyPem();
        using var issuer = new CrmOidcCrypto(options);
        options.ActiveKeyId = "new";
        options.Issuer = "https://changed.example";
        options.Keys = [new() { Kid = "new", Pem = newKey.ExportRSAPrivateKeyPem() }];
        var raw = issuer.Sign("CP6.Web", [], DateTimeOffset.UtcNow.AddMinutes(1), "at+jwt");
        Assert.Equal("old", Read(raw).Header.Kid);
        Assert.Equal("https://cp6.example", Read(raw).Issuer);
        issuer.Validate(raw, "CP6.Web", "at+jwt");
        using var restarted = new CrmOidcCrypto(options);
        var next = restarted.Sign("CP6.Web", [], DateTimeOffset.UtcNow.AddMinutes(1), "at+jwt");
        Assert.Equal("new", Read(next).Header.Kid);
        Assert.Equal(options.Issuer, Read(next).Issuer);
        restarted.Validate(next, "CP6.Web", "at+jwt");
        Assert.ThrowsAny<SecurityTokenException>(() => restarted.Validate(raw, "CP6.Web", "at+jwt"));
    }

    [Theory]
    [InlineData("missing-active")]
    [InlineData("duplicate-kid")]
    [InlineData("empty-kid")]
    [InlineData("newline-kid")]
    [InlineData("weak-key")]
    [InlineData("public-active")]
    public void Invalid_key_configuration_fails_before_serving_requests(string problem)
    {
        using var oldKey = RSA.Create(2048);
        using var newKey = RSA.Create(2048);
        using var weakKey = RSA.Create(1024);
        var options = Options("old", oldKey, newKey);
        switch (problem)
        {
            case "missing-active": options.ActiveKeyId = "absent"; break;
            case "duplicate-kid": options.Keys[1].Kid = "old"; break;
            case "empty-kid": options.Keys[1].Kid = ""; break;
            case "newline-kid": options.Keys[1].Kid = "new\n"; break;
            case "weak-key": options.Keys[0].Pem = weakKey.ExportRSAPrivateKeyPem(); break;
            case "public-active": options.Keys[0].Pem = oldKey.ExportSubjectPublicKeyInfoPem(); break;
        }
        Assert.ThrowsAny<Exception>(() => options.Validate(false));
    }

    private static JwtSecurityToken Read(string raw) => new JwtSecurityTokenHandler().ReadJwtToken(raw);

    [Theory]
    [InlineData("no-expiration", typeof(SecurityTokenNoExpirationException))]
    [InlineData("future", typeof(SecurityTokenNotYetValidException))]
    [InlineData("expired", typeof(SecurityTokenExpiredException))]
    [InlineData("invalid-range", typeof(SecurityTokenInvalidLifetimeException))]
    public void Injected_clock_preserves_lifetime_rejection_rules(string problem, Type exceptionType)
    {
        using var key = RSA.Create(2048);
        using var other = RSA.Create(2048);
        var clock = new TestClock();
        var options = Options("old", key, other, includeNew: false);
        using var services = new ServiceCollection().AddSingleton<TimeProvider>(clock).BuildServiceProvider();
        using var crypto = ActivatorUtilities.CreateInstance<CrmOidcCrypto>(services, options);
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var payload = new JwtPayload { ["iss"] = options.Issuer, ["aud"] = "CP6.Web", ["nbf"] = now, ["exp"] = now + 60 };
        switch (problem)
        {
            case "no-expiration": payload.Remove("exp"); break;
            case "future": payload["nbf"] = now + 30; break;
            case "expired": payload["nbf"] = now - 120; payload["exp"] = now - 1; break;
            case "invalid-range": payload["nbf"] = now + 120; break;
        }
        var header = new JwtHeader(new SigningCredentials(new RsaSecurityKey(key) { KeyId = "old" }, SecurityAlgorithms.RsaSha256)) { ["typ"] = "at+jwt" };
        var raw = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));
        Assert.IsType(exceptionType, Record.Exception(() => crypto.Validate(raw, "CP6.Web", "at+jwt")));
    }

    private static CrmOidcOptions Options(string active, RSA oldKey, RSA newKey, bool includeOld = true, bool includeNew = true)
    {
        var options = new CrmOidcOptions
        {
            Enabled = true,
            Issuer = "https://cp6.example",
            ActiveKeyId = active,
            Clients = [new() { SecretSha256 = CrmOidcCrypto.Hash("test-only-client-secret"), RedirectUris = ["https://crm.example/callback"] }]
        };
        if (includeOld) options.Keys.Add(new() { Kid = "old", Pem = active == "old" ? oldKey.ExportRSAPrivateKeyPem() : oldKey.ExportSubjectPublicKeyInfoPem() });
        if (includeNew) options.Keys.Add(new() { Kid = "new", Pem = active == "new" ? newKey.ExportRSAPrivateKeyPem() : newKey.ExportSubjectPublicKeyInfoPem() });
        options.Validate(false);
        return options;
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
