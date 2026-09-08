using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// No public credential fields, serialization contract, logging, network access or fallback credential chain.
// Caller-provided environment strings cannot be erased; owned secret byte arrays are zeroed on disposal.
internal sealed class R2Credentials : IDisposable
{
    private readonly byte[] _secret;
    private bool _disposed;
    private R2Credentials(string accessKeyId, byte[] secret, string? token, DateTimeOffset? issued, DateTimeOffset? expires)
    {
        AccessKeyId = accessKeyId;
        _secret = secret;
        SessionToken = token;
        IssuedAtUtc = issued;
        ExpiresAtUtc = expires;
    }

    internal string AccessKeyId { get; }
    internal string? SessionToken { get; private set; }
    internal DateTimeOffset? IssuedAtUtc { get; }
    internal DateTimeOffset? ExpiresAtUtc { get; }
    internal bool CanCreate => ExpiresAtUtc is not null;

    internal static R2Credentials Consumer(string accessKeyId, string secret)
    {
        RequirePair(accessKeyId, secret);
        _ = VerifierTrust.Load().RequireStorageAuthority(ContentAddress.StorageAuthority);
        return new(accessKeyId, Encoding.ASCII.GetBytes(secret), null, null, null);
    }

    internal static R2Credentials Publisher(string accessKeyId, string parentSecret) =>
        PublisherAt(accessKeyId, parentSecret, DateTimeOffset.UtcNow);

    // The production factory always supplies current UTC; transport also uses actual UTC for each signature.
    internal static R2Credentials PublisherAt(string accessKeyId, string parentSecret, DateTimeOffset issuedAtUtc)
    {
        RequirePair(accessKeyId, parentSecret);
        var authority = VerifierTrust.Load().RequireStorageAuthority(ContentAddress.StorageAuthority);
        var issued = issuedAtUtc.ToUnixTimeSeconds();
        var expires = checked(issued + 900);
        var header = Encode("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"u8);
        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            bucket = authority.Bucket,
            actions = new[] { "GetBucketLocation", "GetObject", "HeadObject", "PutObject" },
            paths = new { prefixPaths = authority.AllowedPrefixes, objectPaths = Array.Empty<string>() },
            iss = accessKeyId,
            sub = authority.AccountId,
            aud = new Uri(authority.Endpoint).Host,
            iat = issued,
            exp = expires
        }));
        var unsigned = header + "." + payload;
        var parent = Encoding.ASCII.GetBytes(parentSecret);
        try
        {
            var jwt = unsigned + "." + Encode(HMACSHA256.HashData(parent, Encoding.ASCII.GetBytes(unsigned)));
            var temporarySecret = Encoding.ASCII.GetBytes(Cp6DeterministicJson.Sha256Hex(Encoding.ASCII.GetBytes(jwt)));
            return new(accessKeyId, temporarySecret, Convert.ToBase64String(Encoding.ASCII.GetBytes("jwt/" + jwt)),
                DateTimeOffset.FromUnixTimeSeconds(issued), DateTimeOffset.FromUnixTimeSeconds(expires));
        }
        finally { CryptographicOperations.ZeroMemory(parent); }
    }

    internal string Sign(string method, string path, IReadOnlyDictionary<string, string> headers, DateTimeOffset nowUtc)
    {
        if (_disposed) throw Error("r2-credentials-disposed");
        if (IssuedAtUtc is not null && (nowUtc < IssuedAtUtc || nowUtc >= ExpiresAtUtc))
            throw Error("r2-session-time");
        if (!CanCreate && method == "PUT") throw Error("r2-read-only");
        if (!headers.TryGetValue("x-amz-date", out var date) ||
            date != nowUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) ||
            (SessionToken is null ? headers.ContainsKey("x-amz-security-token") :
                !headers.TryGetValue("x-amz-security-token", out var token) || token != SessionToken))
            throw Error("r2-credential-headers");
        var authority = VerifierTrust.Load().RequireStorageAuthority(ContentAddress.StorageAuthority);
        if (!headers.TryGetValue("host", out var host) || host != new Uri(authority.Endpoint).Host ||
            !path.StartsWith("/" + authority.Bucket + "/", StringComparison.Ordinal) ||
            !authority.AllowedPrefixes.Any(prefix => path.StartsWith("/" + authority.Bucket + "/" + prefix, StringComparison.Ordinal)))
            throw Error("r2-credential-authority");
        if (!headers.TryGetValue("x-amz-content-sha256", out var hash)) throw Error("r2-credential-headers");
        return S3RequestSignature.Authorization(method, path, headers, hash, "auto", AccessKeyId, _secret);
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_secret);
        SessionToken = null;
        _disposed = true;
    }

    public override string ToString() => "R2 credentials [redacted]";
    private static string Encode(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static void RequirePair(string id, string secret)
    {
        if (!IsHex(id, 32) || !IsHex(secret, 64)) throw Error("r2-credential-format");
    }
    private static bool IsHex(string value, int length) => value.Length == length &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static Cp6ReleaseContractException Error(string code) => new(code, "R2 credential use violates the fixed authority and lifetime policy.");
}
