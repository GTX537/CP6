# P10 S06 R2 session and SigV4 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans.
> The owner selected sequential work in the current task with no agents.

**Goal:** Implement fixed-authority, fifteen-minute publisher credentials and a
bounded, signed-payload, conditional-create S3 request signer without performing
network I/O or claiming remote acceptance.

**Architecture:** An internal credential owner loads the independently compiled
trust anchor, derives an actions-only R2 JWT session and refuses out-of-lifetime,
cross-authority or read-only writes. A pure internal SigV4 primitive signs the
exact unescaped ASCII paths and selected headers that the later fixed R2
transport will send. No caller-controlled endpoint, default credential chain,
unsigned payload, list/delete/multipart, or query-signing capability is exposed.

**Tech Stack:** .NET 8, BCL HMAC-SHA256/SHA-256/System.Text.Json, the already pinned
CP6.Platform.Release 0.10.1 package, existing xUnit. No new dependency or lock change.

## Scope and sources

- Parent S06 prerequisites pin `p10-platform-candidate`, the real authority,
  a publisher parent and a separate permanent read-only credential pair.
- Preserve the real preflight finding: explicit four actions, no simultaneous
  `scope`, two prefix restrictions, no individual object grants, 900 seconds.
- The permanent consumer wrapper blocks PUT locally; its actual service-side
  permissions must independently pass the protected-workflow test.
- Caller environment strings and immutable session-token strings cannot be
  reliably zeroed in managed memory. Owned secret byte arrays and intermediate
  SigV4 keys are zeroed; nothing is written to files or logs. The parent is not
  retained by the returned publisher credential.
- This layer deliberately cannot send GetBucketLocation even though the already
  approved delegated actions include it: only object-path GET/HEAD/conditional
  PUT is implemented. The original real preflight is not replayed here.
- Transport will use actual UTC for signing and bound its timeout to remaining
  session lifetime. Its discovery entry will derive package-owned Locator keys;
  content-addressed reads require validated references, not arbitrary paths.
- Pure unit fixtures below are deliberately non-operational. They are not R2
  readback, publication, credential-scope acceptance or full candidate acceptance.
- Existing GitHub R2 gates, NuGet timestamps, deployment and runtime are unchanged.

Primary references inspected:
[Cloudflare temporary-credential signing](https://developers.cloudflare.com/r2/examples/authenticate-r2-temp-credentials/)
and [AWS S3 signed-payload reference and public test vector](https://docs.aws.amazon.com/AmazonS3/latest/developerguide/sig-v4-header-based-auth.html).
The general JWT derivation is per Cloudflare; actions-only selection is based on
the recorded real CP6 prerequisite result, not the conflicting scope-plus-actions
example in current documentation.

## Files

- Create `tools/p10/ReleaseVerifier/S3RequestSignature.cs`: pure bounded SigV4.
- Create `tools/p10/ReleaseVerifier/R2Credentials.cs`: credential ownership,
  fixed-authority JWT derivation and per-request lifetime/authority enforcement.
- Create `tools/p10/ReleaseVerifier.Tests/S3RequestSignatureTests.cs`.
- Create `tools/p10/ReleaseVerifier.Tests/R2CredentialsTests.cs`.
- Create this plan. Do not change schemas, lockfiles, trust or workflows.

## Task 1: Write the actual regression tests first

- [x] Add the following two complete test files.

### S3RequestSignatureTests.cs

```csharp
using System.Text;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

public sealed class S3RequestSignatureTests
{
    private const string EmptyHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    // Public AWS documentation example, not an operational credential.
    private const string ExampleId = "AKIAIOSFODNN7EXAMPLE";
    private const string ExampleSecret = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";

    [Fact]
    public void Matches_the_official_AWS_S3_single_chunk_GET_vector()
    {
        var headers = Headers();
        headers.Add("range", "bytes=0-9");
        var result = Sign("GET", "/test.txt", headers);
        Assert.Equal("AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20130524/us-east-1/s3/aws4_request," +
            "SignedHeaders=host;range;x-amz-content-sha256;x-amz-date," +
            "Signature=f0e8bdb87c964420e857bd35b5d6ed310bd44f0170aba48dd91039c6036bdb41", result);
    }

    [Fact]
    public void Header_insertion_order_does_not_change_signature()
    {
        var headers = Headers();
        headers.Add("range", "bytes=0-9");
        var reversed = headers.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Equal(Sign("GET", "/test.txt", headers), Sign("GET", "/test.txt", reversed));
    }

    [Theory]
    [InlineData("POST", "/test.txt")]
    [InlineData("DELETE", "/test.txt")]
    [InlineData("GET", "/test.txt?location")]
    [InlineData("GET", "/test%2Ftxt")]
    [InlineData("GET", "/a/../test.txt")]
    [InlineData("GET", "/a//test.txt")]
    [InlineData("GET", "/test txt")]
    [InlineData("GET", "/测试")]
    [InlineData("GET", "test.txt")]
    [InlineData("GET", "/a\\b")]
    public void Rejects_unsupported_methods_and_noncanonical_paths(string method, string path) =>
        Error("r2-signature-input", () => Sign(method, path, Headers()));

    [Theory]
    [InlineData("Host", "examplebucket.s3.amazonaws.com")]
    [InlineData("x-amz-date", "20130524T000000Z\r\nx-injected: yes")]
    [InlineData("x-amz-date", "20130230T000000Z")]
    [InlineData("host", "examplebucket.s3.amazonaws.com:443")]
    [InlineData("range", " bytes=0-9")]
    [InlineData("range", "bytes=0-9  ")]
    [InlineData("range", "bytes=0-9\t")]
    [InlineData("x-amz-content-sha256", "UNSIGNED-PAYLOAD")]
    [InlineData("authorization", "already-present")]
    public void Rejects_ambiguous_or_unsupported_headers(string name, string value)
    {
        var headers = Headers();
        headers[name] = value;
        Error("r2-signature-headers", () => Sign("GET", "/test.txt", headers));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0123")]
    [InlineData("UNSIGNED-PAYLOAD")]
    public void Rejects_non_SHA256_payload_hashes(string hash) =>
        Error("r2-signature-input", () => S3RequestSignature.Authorization("GET", "/test.txt", Headers(),
            hash, "us-east-1", ExampleId, Encoding.ASCII.GetBytes(ExampleSecret)));

    [Fact]
    public void Requires_conditional_create_for_every_PUT()
    {
        Error("r2-create-condition", () => Sign("PUT", "/test.txt", Headers()));
        var headers = Headers();
        headers["if-none-match"] = "\"some-etag\"";
        Error("r2-create-condition", () => Sign("PUT", "/test.txt", headers));
    }

    [Fact]
    public void Signed_PUT_binds_condition_media_body_and_session_token()
    {
        var headers = Headers();
        headers["if-none-match"] = "*";
        headers["content-type"] = "application/json";
        headers["x-amz-security-token"] = "unit-fixture-session";
        var first = Sign("PUT", "/test.txt", headers);
        Assert.Contains("SignedHeaders=content-type;host;if-none-match;x-amz-content-sha256;x-amz-date;x-amz-security-token,", first);
        headers["x-amz-security-token"] = "different-unit-session";
        Assert.NotEqual(first, Sign("PUT", "/test.txt", headers));
        headers["x-amz-security-token"] = "unit-fixture-session";
        headers["content-type"] = "application/sarif+json";
        Assert.NotEqual(first, Sign("PUT", "/test.txt", headers));
        headers["content-type"] = "application/json";
        var changed = Cp6DeterministicJson.Sha256Hex("{}"u8);
        headers["x-amz-content-sha256"] = changed;
        Assert.NotEqual(first, Sign("PUT", "/test.txt", headers, changed));
    }

    [Fact]
    public void HEAD_and_object_path_are_bound()
    {
        var first = Sign("GET", "/test.txt", Headers());
        Assert.NotEqual(first, Sign("HEAD", "/test.txt", Headers()));
        Assert.NotEqual(first, Sign("GET", "/other.txt", Headers()));
    }

    [Fact]
    public void Reads_cannot_hide_a_payload()
    {
        var hash = Cp6DeterministicJson.Sha256Hex("{}"u8);
        var headers = Headers();
        headers["x-amz-content-sha256"] = hash;
        Error("r2-read-payload", () => Sign("GET", "/test.txt", headers, hash));
    }

    [Theory]
    [InlineData("host")]
    [InlineData("x-amz-date")]
    [InlineData("x-amz-content-sha256")]
    public void Required_headers_cannot_be_missing(string name)
    {
        var headers = Headers();
        headers.Remove(name);
        Error("r2-signature-headers", () => Sign("GET", "/test.txt", headers));
    }

    [Fact]
    public void Rejects_oversized_values_and_invalid_credentials()
    {
        var headers = Headers();
        headers["range"] = new string('a', 4097);
        Error("r2-signature-headers", () => Sign("GET", "/test.txt", headers));
        Error("r2-signature-input", () => Sign("GET", "/" + new string('a', 512), Headers()));
        Error("r2-signature-input", () => S3RequestSignature.Authorization("GET", "/test.txt", Headers(),
            EmptyHash, "other-region", ExampleId, Encoding.ASCII.GetBytes(ExampleSecret)));
        Error("r2-signature-input", () => S3RequestSignature.Authorization("GET", "/test.txt", Headers(),
            EmptyHash, "auto", "invalid/id", Encoding.ASCII.GetBytes(ExampleSecret)));
        Error("r2-signature-input", () => S3RequestSignature.Authorization("GET", "/test.txt", Headers(),
            EmptyHash, "auto", ExampleId, "short"u8));
    }

    private static Dictionary<string, string> Headers() => new()
    {
        ["x-amz-date"] = "20130524T000000Z",
        ["host"] = "examplebucket.s3.amazonaws.com",
        ["x-amz-content-sha256"] = EmptyHash
    };
    private static string Sign(string method, string path, IReadOnlyDictionary<string, string> headers, string hash = EmptyHash) =>
        S3RequestSignature.Authorization(method, path, headers, hash, "us-east-1", ExampleId, Encoding.ASCII.GetBytes(ExampleSecret));
    private static void Error(string code, Action action) => Assert.Equal(code, Assert.Throws<Cp6ReleaseContractException>(action).Code);
}
```

### R2CredentialsTests.cs

```csharp
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
```

- [x] Run focused tests and observe missing implementation. Add only throwing
  API scaffolds if compilation is blocked; rerun and record actual assertion
  failures with NotImplementedException, no skipped cases.

Run from `tools/p10` with the installed .NET 8 SDK on PATH; global.json selects
8.0.424. Keep host-specific SDK/cache paths out of committed configuration.

```powershell
dotnet --version
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --configuration Release --filter 'FullyQualifiedName~S3RequestSignatureTests|FullyQualifiedName~R2CredentialsTests' --logger 'trx;LogFileName=s06-r2-auth-red.trx' --results-directory ../../artifacts/p10/r2-auth-red --verbosity minimal
```

Expected RED: the new signing/credential behavior is absent, not network failures,
wrong API names, unknown media-type constants or invalid test setup.

## Task 2: Implement exactly the scoped behavior

- [x] Replace throwing scaffolds with the two following complete source files.

### S3RequestSignature.cs

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Pure signing primitive. It opens no network connection and cannot select a storage authority.
internal static class S3RequestSignature
{
    internal static string Authorization(string method, string path, IReadOnlyDictionary<string, string> headers,
        string payloadHash, string region, string accessKeyId, ReadOnlySpan<byte> secret)
    {
        if (method is not ("GET" or "HEAD" or "PUT") || region is not ("auto" or "us-east-1") ||
            path.Length is < 2 or > 512 || path[0] != '/' ||
            path.Contains("..", StringComparison.Ordinal) || path.Contains("//", StringComparison.Ordinal) ||
            path.Any(c => !IsPathCharacter(c)) || !IsHash(payloadHash) ||
            accessKeyId.Length is < 16 or > 128 || accessKeyId.Any(c => !char.IsAsciiLetterOrDigit(c)) ||
            secret.Length is < 16 or > 128 || secret.ContainsAnyExceptInRange((byte)'!', (byte)'~'))
            throw Error("r2-signature-input");
        if (headers.Count is < 3 or > 8 || headers.Any(pair =>
                !AllowedHeaders.Contains(pair.Key, StringComparer.Ordinal) ||
                pair.Value.Length is < 1 or > 4096 || pair.Value.Trim() != pair.Value ||
                pair.Value.Contains("  ", StringComparison.Ordinal) ||
                pair.Value.Any(c => c is < ' ' or > '~')) ||
            !headers.TryGetValue("host", out var host) || host.Length > 253 ||
            host.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '.' and not '-') ||
            !headers.TryGetValue("x-amz-date", out var date) ||
            !DateTimeOffset.TryParseExact(date, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _) ||
            !headers.TryGetValue("x-amz-content-sha256", out var suppliedHash) || suppliedHash != payloadHash)
            throw Error("r2-signature-headers");
        if (method == "PUT" && (!headers.TryGetValue("if-none-match", out var condition) || condition != "*"))
            throw Error("r2-create-condition");
        if (method != "PUT" && payloadHash != Cp6DeterministicJson.Sha256Hex([]))
            throw Error("r2-read-payload");

        var sorted = headers.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
        var signedHeaders = string.Join(";", sorted.Select(pair => pair.Key));
        var canonicalHeaders = string.Concat(sorted.Select(pair => pair.Key + ":" + pair.Value + "\n"));
        var canonical = method + "\n" + path + "\n\n" + canonicalHeaders + "\n" + signedHeaders + "\n" + payloadHash;
        var scope = date[..8] + "/" + region + "/s3/aws4_request";
        var toSign = "AWS4-HMAC-SHA256\n" + date + "\n" + scope + "\n" +
            Cp6DeterministicJson.Sha256Hex(Encoding.UTF8.GetBytes(canonical));
        var initial = new byte[secret.Length + 4];
        "AWS4"u8.CopyTo(initial);
        secret.CopyTo(initial.AsSpan(4));
        byte[] day = [], regional = [], service = [], signing = [];
        try
        {
            day = Mac(initial, date[..8]);
            regional = Mac(day, region);
            service = Mac(regional, "s3");
            signing = Mac(service, "aws4_request");
            var signature = Convert.ToHexString(Mac(signing, toSign)).ToLowerInvariant();
            return $"AWS4-HMAC-SHA256 Credential={accessKeyId}/{scope},SignedHeaders={signedHeaders},Signature={signature}";
        }
        finally
        {
            foreach (var key in new[] { initial, day, regional, service, signing }) CryptographicOperations.ZeroMemory(key);
        }
    }

    private static readonly string[] AllowedHeaders =
        ["host", "range", "content-type", "if-none-match", "x-amz-content-sha256", "x-amz-date", "x-amz-security-token"];

    private static byte[] Mac(byte[] key, string text) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(text));
    private static bool IsPathCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.' or '~';
    private static bool IsHash(string value) => value.Length == 64 &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static Cp6ReleaseContractException Error(string code) => new(code, "R2 signing input violates the bounded request policy.");
}
```

### R2Credentials.cs

```csharp
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
```

## Task 3: Verify and commit

- [x] Rerun the focused command with GREEN result path and confirm every new
  case passes with no skipped tests, compiler warnings or errors.
- [x] Run all tests with the existing mandatory live test inputs:
  `P10_COSIGN_PATH` is the previously hash-verified official cosign v3.1.3 binary;
  `P10_FORMAL_PACKAGE_ROOT` contains the seven actual immutable S04 readback
  archives; `P10_FEED_READ_TOKEN` is loaded only in memory from the existing
  authenticated gh process. Never print it or pass it as an argument.
- [x] Verify formatting, locked restore, source/plan equality and full scoped
  diff. Existing exact package versions and trust hashes remain unchanged.
- [ ] Stage only the five explicitly named files and create the audited commit.

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --configuration Release --verbosity minimal
dotnet format whitespace ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --verify-no-changes
dotnet restore ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --configfile ../../eng/p10/NuGet.formal.config --packages ../../artifacts/p10/foundation-restore/packages --locked-mode --verbosity minimal
```

From the task worktree:

```powershell
git diff --check
git add -- docs/superpowers/plans/2026-09-07-p10-s06-r2-authentication.md tools/p10/ReleaseVerifier/S3RequestSignature.cs tools/p10/ReleaseVerifier/R2Credentials.cs tools/p10/ReleaseVerifier.Tests/S3RequestSignatureTests.cs tools/p10/ReleaseVerifier.Tests/R2CredentialsTests.cs
git diff --cached --check
git diff --cached --stat
git commit -m "feat(p10): bind R2 session signing to fixed authority"
```

## Remaining S06 gate, not a success claim

- [ ] Complete fixed transport, full live evidence proofs, assembler, protected
  validation/publication workflows, immutable OCI/SBOM/scan/provenance proofs,
  actual conditional-create/conflict/readback/Locator-last verification and
  cross-repository audited state closure before calling P10 complete.

## Observed component verification (2026-09-08 UTC)

- Both new APIs were absent on the first compile. Throwing scaffolds then gave
  56 actual failing tests; TRX confirmed every failure was NotImplementedException,
  with no passed or skipped case. The planned implementation gave 56/56 GREEN.
- Full Release regression passed 325/325 with zero skips, warnings or errors,
  including the existing seven live feed downloads and real NuGet/RFC3161 checks.
  Whitespace verification and locked restore passed.
- All four source/test bodies match the reviewed plan exactly. Trust instances,
  dependency locks, workflow files and runtime code remain unchanged.
- Only non-operational unit inputs were used for the new R2 component. No R2
  request, cloud-object write, OCI push, candidate or Locator publication occurred.
