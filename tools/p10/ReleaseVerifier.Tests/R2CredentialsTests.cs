using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

public sealed class R2CredentialsTests
{
    // Deliberate local unit inputs; no Cloudflare token or service acceptance is represented.
    private static readonly string FixtureId = new('a', 32);
    private static readonly string FixtureSecret = new('b', 64);
    private static readonly DateTimeOffset Issued = new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
    private const string Host = "30c4a8d1697ffd3de6a1e0a88376607c.r2.cloudflarestorage.com";
    private const string Path = "/cp6-release/candidates/platform/v0.10.1/candidate-locator.v1.json";

    [Fact]
    public void Session_has_exact_authority_actions_prefixes_and_fifteen_minute_lifetime()
    {
        using var credential = R2Credentials.PublisherAt(FixtureId, FixtureSecret, Issued.AddMilliseconds(750));
        Assert.True(credential.CanCreate);
        Assert.Equal(FixtureId, credential.AccessKeyId);
        Assert.Equal(Issued, credential.IssuedAtUtc);
        Assert.Equal(Issued.AddSeconds(900), credential.ExpiresAtUtc);
        var jwt = Jwt(credential);
        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);
        using var header = JsonDocument.Parse(Decode(parts[0]));
        Assert.Equal(2, header.RootElement.EnumerateObject().Count());
        Assert.Equal("HS256", header.RootElement.GetProperty("alg").GetString());
        Assert.Equal("JWT", header.RootElement.GetProperty("typ").GetString());
        using var document = JsonDocument.Parse(Decode(parts[1]));
        var root = document.RootElement;
        Assert.Equal(new[] { "actions", "aud", "bucket", "exp", "iat", "iss", "paths", "sub" },
            root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("cp6-release", root.GetProperty("bucket").GetString());
        Assert.Equal(Host, root.GetProperty("aud").GetString());
        Assert.Equal("30c4a8d1697ffd3de6a1e0a88376607c", root.GetProperty("sub").GetString());
        Assert.Equal(FixtureId, root.GetProperty("iss").GetString());
        Assert.Equal(Issued.ToUnixTimeSeconds(), root.GetProperty("iat").GetInt64());
        Assert.Equal(900, root.GetProperty("exp").GetInt64() - root.GetProperty("iat").GetInt64());
        Assert.Equal(new[] { "GetBucketLocation", "GetObject", "HeadObject", "PutObject" },
            root.GetProperty("actions").EnumerateArray().Select(x => x.GetString()));
        var paths = root.GetProperty("paths");
        Assert.Equal(2, paths.EnumerateObject().Count());
        Assert.Equal(new[] { "candidates/platform/", "objects/sha256/" },
            paths.GetProperty("prefixPaths").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(0, paths.GetProperty("objectPaths").GetArrayLength());
        var independent = HMACSHA256.HashData(Encoding.ASCII.GetBytes(FixtureSecret),
            Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]));
        Assert.Equal(independent, Decode(parts[2]));
    }

    [Fact]
    public void SigV4_uses_derived_temporary_secret_not_the_parent()
    {
        using var credential = R2Credentials.PublisherAt(FixtureId, FixtureSecret, Issued);
        var headers = Headers(credential, Issued);
        var derived = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(Jwt(credential)))).ToLowerInvariant();
        var actual = credential.Sign("GET", Path, headers, Issued);
        Assert.Equal(S3RequestSignature.Authorization("GET", Path, headers, headers["x-amz-content-sha256"],
            "auto", FixtureId, Encoding.ASCII.GetBytes(derived)), actual);
        Assert.NotEqual(S3RequestSignature.Authorization("GET", Path, headers, headers["x-amz-content-sha256"],
            "auto", FixtureId, Encoding.ASCII.GetBytes(FixtureSecret)), actual);
    }

    [Fact]
    public void Publisher_factory_uses_actual_current_time()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var credential = R2Credentials.Publisher(FixtureId, FixtureSecret);
        var after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.InRange(credential.IssuedAtUtc!.Value.ToUnixTimeSeconds(), before, after);
        Assert.Equal(TimeSpan.FromSeconds(900), credential.ExpiresAtUtc - credential.IssuedAtUtc);
    }

    [Fact]
    public void Consumer_is_read_only_without_a_session_token()
    {
        using var credential = R2Credentials.Consumer(FixtureId, FixtureSecret);
        Assert.False(credential.CanCreate);
        Assert.Null(credential.SessionToken);
        Assert.Null(credential.ExpiresAtUtc);
        var headers = Headers(credential, Issued);
        Assert.Contains("/auto/s3/aws4_request,", credential.Sign("GET", Path, headers, Issued));
        Assert.Contains("/auto/s3/aws4_request,", credential.Sign("HEAD", Path, headers, Issued));
        headers["if-none-match"] = "*";
        Error("r2-read-only", () => credential.Sign("PUT", Path, headers, Issued));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(900)]
    [InlineData(901)]
    public void Session_cannot_be_used_before_issue_or_at_or_after_expiry(int offset)
    {
        using var credential = R2Credentials.PublisherAt(FixtureId, FixtureSecret, Issued);
        var now = Issued.AddSeconds(offset);
        Error("r2-session-time", () => credential.Sign("GET", Path, Headers(credential, now), now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(899)]
    public void Session_signs_only_inside_the_interval(int offset)
    {
        using var credential = R2Credentials.PublisherAt(FixtureId, FixtureSecret, Issued);
        var now = Issued.AddSeconds(offset);
        Assert.Contains("/auto/s3/aws4_request,", credential.Sign("GET", Path, Headers(credential, now), now));
    }

    [Fact]
    public void Publisher_signs_conditional_create_and_content_addressed_paths()
    {
        using var credential = R2Credentials.PublisherAt(FixtureId, FixtureSecret, Issued);
        var address = ContentAddress.Create("{}"u8, Cp6ReleaseMediaTypes.InToto, "proof.json");
        var headers = Headers(credential, Issued);
        headers["if-none-match"] = "*";
        headers["content-type"] = address.MediaType;
        headers["x-amz-content-sha256"] = address.Sha256;
        Assert.Contains("if-none-match;", credential.Sign("PUT", "/cp6-release/" + address.Key, headers, Issued));
    }

    [Fact]
    public void Session_and_request_time_must_match_signed_headers()
    {
        using var credential = R2Credentials.PublisherAt(FixtureId, FixtureSecret, Issued);
        var headers = Headers(credential, Issued);
        headers.Remove("x-amz-security-token");
        Error("r2-credential-headers", () => credential.Sign("GET", Path, headers, Issued));
        headers["x-amz-security-token"] = "different";
        Error("r2-credential-headers", () => credential.Sign("GET", Path, headers, Issued));
        headers = Headers(credential, Issued.AddSeconds(1));
        Error("r2-credential-headers", () => credential.Sign("GET", Path, headers, Issued));
        using var consumer = R2Credentials.Consumer(FixtureId, FixtureSecret);
        Error("r2-credential-headers", () => consumer.Sign("GET", Path, headers, Issued.AddSeconds(1)));
    }

    [Theory]
    [InlineData("attacker.example", "/cp6-release/candidates/platform/v0.10.1/a.json")]
    [InlineData(Host, "/other-bucket/candidates/platform/v0.10.1/a.json")]
    [InlineData(Host, "/cp6-release/candidates/system/v0.10.1/a.json")]
    [InlineData(Host, "/cp6-release/objects/sha256-evil/a.json")]
    [InlineData(Host, "/cp6-release/")]
    public void Cannot_sign_another_authority_or_unapproved_prefix(string host, string path)
    {
        using var credential = R2Credentials.Consumer(FixtureId, FixtureSecret);
        var headers = Headers(credential, Issued);
        headers["host"] = host;
        Error("r2-credential-authority", () => credential.Sign("GET", path, headers, Issued));
    }

    [Fact]
    public void Dispose_zeroes_owned_secret_and_prevents_reuse_without_exposing_values()
    {
        var credential = R2Credentials.PublisherAt(FixtureId, FixtureSecret, Issued);
        var headers = Headers(credential, Issued);
        var secret = (byte[])typeof(R2Credentials).GetField("_secret", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(credential)!;
        Assert.Contains(secret, value => value != 0);
        Assert.Equal("{}", JsonSerializer.Serialize(credential));
        Assert.Equal("R2 credentials [redacted]", credential.ToString());
        credential.Dispose();
        Assert.All(secret, value => Assert.Equal(0, value));
        Assert.Null(credential.SessionToken);
        Error("r2-credentials-disposed", () => credential.Sign("GET", Path, headers, Issued));
        credential.Dispose();
    }

    [Theory]
    [InlineData("", "valid")]
    [InlineData("short", "valid")]
    [InlineData("uppercase", "valid")]
    [InlineData("valid", "")]
    [InlineData("valid", "short")]
    [InlineData("valid", "uppercase")]
    [InlineData("valid", "newline")]
    public void Invalid_credential_format_fails_without_echoing_input(string idCase, string secretCase)
    {
        var id = idCase == "valid" ? FixtureId : idCase == "uppercase" ? FixtureId.ToUpperInvariant() : idCase;
        var secret = secretCase == "valid" ? FixtureSecret : secretCase == "uppercase" ? FixtureSecret.ToUpperInvariant() :
            secretCase == "newline" ? FixtureSecret + "\n" : secretCase;
        Error("r2-credential-format", () => R2Credentials.Consumer(id, secret));
        var error = Assert.Throws<Cp6ReleaseContractException>(() => R2Credentials.PublisherAt(id, secret, Issued));
        Assert.Equal("r2-credential-format", error.Code);
        Assert.Null(error.InnerException);
        Assert.Equal("R2 credential use violates the fixed authority and lifetime policy.", error.Message);
    }

    private static Dictionary<string, string> Headers(R2Credentials credential, DateTimeOffset now)
    {
        var headers = new Dictionary<string, string>
        {
            ["host"] = Host,
            ["x-amz-date"] = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture),
            ["x-amz-content-sha256"] = Cp6DeterministicJson.Sha256Hex([])
        };
        if (credential.SessionToken is not null) headers.Add("x-amz-security-token", credential.SessionToken);
        return headers;
    }
    private static string Jwt(R2Credentials credential)
    {
        var token = Encoding.ASCII.GetString(Convert.FromBase64String(credential.SessionToken!));
        Assert.StartsWith("jwt/", token);
        return token[4..];
    }
    private static byte[] Decode(string text)
    {
        var base64 = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '='));
    }
    private static void Error(string code, Action action) => Assert.Equal(code, Assert.Throws<Cp6ReleaseContractException>(action).Code);
}
