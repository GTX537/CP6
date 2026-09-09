using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.IdentityModel.Tokens;

namespace CP6.WebApi.Services;

public sealed class CrmOidcCrypto : IDisposable
{
    private readonly string _issuer;
    private readonly string _activeKeyId;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, RSA> _keys;
    public CrmOidcCrypto(CrmOidcOptions options, TimeProvider? time = null)
    {
        _issuer = options.Issuer;
        _activeKeyId = options.ActiveKeyId;
        _time = time ?? TimeProvider.System;
        _keys = options.Enabled ? options.Keys.ToDictionary(k => k.Kid, k =>
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(k.Pem);
            return rsa;
        }) : [];
    }
    public static string RandomToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Challenge(string verifier) => Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    public static bool ValidVerifier(string verifier) => Regex.IsMatch(verifier, "^[A-Za-z0-9._~-]{43,128}$");
    public static bool EqualsSecret(string raw, string hash) => CryptographicOperations.FixedTimeEquals(
        Encoding.ASCII.GetBytes(Hash(raw)), Encoding.ASCII.GetBytes(hash.ToUpperInvariant()));
    public object Jwks() => new { keys = _keys.Select(pair =>
    {
        var p = pair.Value.ExportParameters(false);
        return new { kty = "RSA", use = "sig", alg = "RS256", kid = pair.Key,
            n = Base64UrlEncoder.Encode(p.Modulus!), e = Base64UrlEncoder.Encode(p.Exponent!) };
    }).ToArray() };
    public string Sign(string audience, IEnumerable<Claim> claims, DateTimeOffset expires, string tokenType)
    {
        var key = new RsaSecurityKey(_keys[_activeKeyId]) { KeyId = _activeKeyId };
        var header = new JwtHeader(new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        header["typ"] = tokenType;
        var now = _time.GetUtcNow().UtcDateTime;
        var payload = new JwtPayload(_issuer, audience, claims, now, expires.UtcDateTime, now);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));
    }
    public ClaimsPrincipal Validate(string raw, string audience, string tokenType, bool validateLifetime = true)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        return handler.ValidateToken(raw, new TokenValidationParameters
        {
            ValidIssuer = _issuer, ValidAudience = audience, ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidTypes = [tokenType], ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = validateLifetime,
            ValidateIssuerSigningKey = true, RequireSignedTokens = true, RequireExpirationTime = true,
            ClockSkew = TimeSpan.Zero,
            // IdentityModel's TimeProvider is not public in the pinned package. Keep the
            // standard zero-skew lifetime rules while sharing the signing clock.
            LifetimeValidator = (notBefore, expires, _, _) =>
            {
                if (!validateLifetime) return true;
                if (!expires.HasValue) throw new SecurityTokenNoExpirationException("Token has no expiration.");
                if (notBefore > expires) throw new SecurityTokenInvalidLifetimeException("Token lifetime is invalid.");
                var now = _time.GetUtcNow().UtcDateTime;
                if (notBefore > now) throw new SecurityTokenNotYetValidException("Token is not yet valid.");
                if (expires < now) throw new SecurityTokenExpiredException("Token has expired.");
                return true;
            },
            IssuerSigningKeyResolver = (_, _, kid, _) => kid != null && _keys.TryGetValue(kid, out var rsa)
                ? [new RsaSecurityKey(rsa) { KeyId = kid }] : []
        }, out _);
    }
    public void Dispose() { foreach (var key in _keys.Values) key.Dispose(); }
}
