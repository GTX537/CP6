# P10 S06 Authenticated Formal Feed Readback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans task-by-task in the current task. The user selected sequential execution, with no delegation or repeated mode question.

**Goal:** Fetch each selected 0.10.1 package from the sole authenticated GitHub Packages feed and independently verify its exact raw bytes, author signature and RFC3161 timestamp.

**Architecture:** A narrow feed policy owns all request construction and transport configuration. Authenticate the fixed service index and require its exact approved PackageBaseAddress, then fetch only one of the seven fixed package identities. Permit at most one explicit 302 hop to the observed GitHub NuGet Blob host with no GitHub authorization forwarded; bound raw bytes and call the existing fixed-hash cryptographic verifier.

**Tech Stack:** .NET 8.0.424, System.Net.Http, System.Text.Json, existing CP6.Platform.Release/NuGet cryptography modules and xUnit; no new dependencies.

---

## Scope and observed protocol

- Continue the public S06 branch after 9269857f. No package build, upload, overwrite, unlist, delete, OCI/R2 publication or deployment is part of this module.
- Fixed source: https://nuget.pkg.github.com/GTX537/index.json. An authenticated read returned version 3.0.0-beta.1 (the officially equivalent predecessor of 3.0.0) and PackageBaseAddress/3.0.0 at https://nuget.pkg.github.com/GTX537/download. An alternate source/owner/base/query or duplicate selector is rejected, not followed.
- An actual selected Release-package request returned 302 to nugetregistryv2prod.blob.core.windows.net with a temporary signed query. A new, unauthenticated request to that exact HTTPS host returned 200 application/octet-stream, 116325 bytes and no Content-Encoding. This is a storage transfer hop, not a second package authority. Its temporary URL must never enter logs or evidence.
- The handler disables automatic redirects, cookies, proxies, decompression and activity propagation. It has no credential store or certificate-validation override and uses online TLS certificate revocation. Authentication is a request-local Basic header for only the two fixed feed request shapes, never a default client header.
- Both transfer headers and body reads use a linked 60-second deadline; connection establishment is limited to 10 seconds. Index bytes are limited to 64 KiB and 128 resources; packages are limited to the existing 8 MiB package cap. Reject partial/error responses, media/encoding changes and declared/actual length mismatch.
- Only seven live feed tests count as new remote readback in this module. HTTP-message/byte and untrusted-index fixtures test parser/transport policy, not remote acceptance. No fake crypto provider, local source fallback, cached package substitute or skipped live test is allowed.
- Read token is supplied through the process environment in local tests and the appropriate read-only workflow secret/token later. No token appears in a command argument, repository file, output, request URL or returned proof. Managed header strings are short-lived/disposed, not falsely claimed to be securely erased.
- [NuGet package content protocol](https://learn.microsoft.com/en-us/nuget/api/package-base-address-resource) specifies index-discovered base URLs and lowercase ID/version download paths. [Microsoft redirect behavior](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclienthandler.allowautoredirect?view=net-8.0) motivates explicit handling. [ResponseHeadersRead timeout scope](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpcompletionoption?view=net-9.0) requires the same explicit cancellation deadline during body consumption.

## File map

| File | Responsibility |
|---|---|
| tools/p10/ReleaseVerifier/FormalFeedPolicy.cs | Fixed index/package/credential/redirect boundaries and owned secure handler |
| tools/p10/ReleaseVerifier/StrictHttpBody.cs | Bounded unchanged binary/JSON response-body consumption |
| tools/p10/ReleaseVerifier/FormalPackageSource.cs | Actual authenticated readback, one credential-free hop, crypto composition and immutable byte/proof result |
| tools/p10/ReleaseVerifier.Tests/FormalPackageSourceTests.cs | Seven real live downloads and focused policy/body failure cases |
| This plan | Complete implementation and observed verification record |

## Task 1: Tests first

- [x] Add the full test file before any implementation:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

public sealed class FormalPackageSourceTests
{
    private const string TestToken = "not-a-real-token";
    private const string ValidIndex = """{"version":"3.0.0","resources":[{"@type":"PackageBaseAddress/3.0.0","@id":"https://nuget.pkg.github.com/GTX537/download"}]}""";
    private const string Blob = "https://nugetregistryv2prod.blob.core.windows.net/unit-test/object?sig=not-real";

    [Theory]
    [InlineData("CP6.Platform.Abstractions")]
    [InlineData("CP6.Platform.AspNetCore")]
    [InlineData("CP6.Platform.Contracts")]
    [InlineData("CP6.Platform.Deployment")]
    [InlineData("CP6.Platform.EntityFramework")]
    [InlineData("CP6.Platform.Messaging")]
    [InlineData("CP6.Platform.Release")]
    public async Task Actual_GitHub_Packages_download_passes_independent_hash_author_and_timestamp_checks(string id)
    {
        var token = Environment.GetEnvironmentVariable("P10_FEED_READ_TOKEN")
            ?? throw new InvalidOperationException("Live authenticated formal feed read is required; this test does not skip.");
        var before = DateTimeOffset.UtcNow;
        var result = await FormalPackageSource.DownloadAndVerifyAsync(id, token);
        Assert.Equal(S06ReleaseIdentity.PackageHashes[id], result.Proof.Sha256);
        Assert.Equal(id, result.Proof.PackageId);
        Assert.Equal("0.10.1", result.Proof.Version);
        Assert.Equal(S06ReleaseIdentity.Source, result.Proof.SourceGitSha);
        Assert.Equal(FormalFeedPolicy.Index, result.FeedServiceIndex);
        Assert.InRange(result.RetrievedAtUtc, before, DateTimeOffset.UtcNow);
        var bytes = result.CopyPackageBytes();
        Assert.Equal(result.Proof.Sha256, Cp6DeterministicJson.Sha256Hex(bytes));
        bytes[0] = 0;
        Assert.Equal((byte)'P', result.CopyPackageBytes()[0]);
        Assert.False(result.Proof.PublicCaTrusted);
        Assert.True(result.Proof.InternallyTrusted);
    }

    [Theory]
    [InlineData("3.0.0")]
    [InlineData("3.0.0-beta.1")]
    public void Observed_service_index_base_is_required_before_constructing_a_package_request(string version)
    {
        FormalFeedPolicy.RequireIndex(Encoding.UTF8.GetBytes(ValidIndex.Replace(
            "\"version\":\"3.0.0\"", "\"version\":\"" + version + "\"", StringComparison.Ordinal)));
        using var request = FormalFeedPolicy.PackageRequest("CP6.Platform.Release", TestToken);
        Assert.Equal("https://nuget.pkg.github.com/GTX537/download/cp6.platform.release/0.10.1/cp6.platform.release.0.10.1.nupkg",
            request.RequestUri!.AbsoluteUri);
        Assert.Equal(HttpMethod.Get, request.Method);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("foreign-host")]
    [InlineData("other-owner")]
    [InlineData("query")]
    [InlineData("duplicate-resource")]
    [InlineData("duplicate-id")]
    [InlineData("duplicate-root")]
    [InlineData("version")]
    [InlineData("missing")]
    [InlineData("invalid")]
    public void Service_index_cannot_select_another_download_authority(string mutation)
    {
        var raw = mutation switch
        {
            "http" => ValidIndex.Replace("https://", "http://", StringComparison.Ordinal),
            "foreign-host" => ValidIndex.Replace("nuget.pkg.github.com", "attacker.invalid", StringComparison.Ordinal),
            "other-owner" => ValidIndex.Replace("GTX537", "AnotherOwner", StringComparison.Ordinal),
            "query" => ValidIndex.Replace("/download", "/download?token=untrusted", StringComparison.Ordinal),
            "duplicate-resource" => ValidIndex.Replace("}]}", "}," + ValidIndex[(ValidIndex.IndexOf("[", StringComparison.Ordinal) + 1)..], StringComparison.Ordinal),
            "duplicate-id" => ValidIndex.Replace("\"@id\":", "\"@id\":\"https://attacker.invalid\",\"@id\":", StringComparison.Ordinal),
            "duplicate-root" => ValidIndex.Replace("\"version\":", "\"version\":\"3.0.0\",\"version\":", StringComparison.Ordinal),
            "version" => ValidIndex.Replace("\"version\":\"3.0.0\"", "\"version\":\"2.0.0\"", StringComparison.Ordinal),
            "missing" => """{"version":"3.0.0","resources":[]}""",
            _ => "{"
        };
        Assert.Throws<Cp6ReleaseContractException>(() => FormalFeedPolicy.RequireIndex(Encoding.UTF8.GetBytes(raw)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65537)]
    public void Service_index_bytes_have_a_small_preparse_limit(int count) =>
        Assert.Equal("feed-index-size", Assert.Throws<Cp6ReleaseContractException>(() =>
            FormalFeedPolicy.RequireIndex(new byte[count])).Code);

    [Theory]
    [InlineData("")]
    [InlineData("cp6.platform.release")]
    [InlineData("../CP6.Platform.Release")]
    [InlineData("CP6.Platform.Testing")]
    public async Task Public_download_rejects_unselected_package_before_any_network_request(string id) =>
        await Reject(() => FormalPackageSource.DownloadAndVerifyAsync(id, TestToken), "feed-package-id");

    [Theory]
    [InlineData("")]
    [InlineData("contains space")]
    [InlineData("line\nbreak")]
    [InlineData("é")]
    public async Task Credential_shape_is_checked_before_transport(string token) =>
        await Reject(() => FormalPackageSource.DownloadAndVerifyAsync("CP6.Platform.Release", token), "feed-credential");

    [Fact]
    public async Task Credential_length_is_bounded_before_transport() =>
        await Reject(() => FormalPackageSource.DownloadAndVerifyAsync("CP6.Platform.Release", new string('a', 4097)), "feed-credential");

    [Fact]
    public void Only_the_fixed_feed_requests_carry_authorization()
    {
        using var index = FormalFeedPolicy.IndexRequest(TestToken);
        using var package = FormalFeedPolicy.PackageRequest("CP6.Platform.Release", TestToken);
        using var blob = FormalFeedPolicy.BlobRequest(new Uri(Blob));
        Assert.Equal("Basic", index.Headers.Authorization!.Scheme);
        Assert.Equal("GTX537:" + TestToken, Encoding.UTF8.GetString(Convert.FromBase64String(index.Headers.Authorization.Parameter!)));
        Assert.Equal(index.Headers.Authorization, package.Headers.Authorization);
        Assert.Null(blob.Headers.Authorization);
        Assert.DoesNotContain(blob.Headers, h => h.Key is "Cookie" or "Referer" or "Proxy-Authorization");
        Assert.Equal(HttpMethod.Get, blob.Method);
        Assert.True(blob.Headers.CacheControl!.NoCache);
        Assert.True(blob.Headers.CacheControl.NoStore);
    }

    [Theory]
    [InlineData("https://attacker.invalid/file?sig=not-real")]
    [InlineData("http://nugetregistryv2prod.blob.core.windows.net/file?sig=not-real")]
    [InlineData("https://nugetregistryv2prod.blob.core.windows.net.attacker.invalid/file?sig=not-real")]
    [InlineData("https://nugetregistryv2prod.blob.core.windows.net:444/file?sig=not-real")]
    [InlineData("https://user@nugetregistryv2prod.blob.core.windows.net/file?sig=not-real")]
    [InlineData("https://nugetregistryv2prod.blob.core.windows.net/file?sig=not-real#fragment")]
    [InlineData("https://nugetregistryv2prod.blob.core.windows.net/file")]
    [InlineData("/relative?sig=not-real")]
    public void Redirect_targets_cannot_expand_the_storage_host_boundary(string location)
    {
        var failure = Assert.Throws<Cp6ReleaseContractException>(() =>
            FormalFeedPolicy.BlobRequest(new Uri(location, UriKind.RelativeOrAbsolute)));
        Assert.Equal("feed-redirect", failure.Code);
        Assert.DoesNotContain(location, failure.ToString(), StringComparison.Ordinal);
        Assert.Null(failure.InnerException);
    }

    [Fact]
    public void Missing_or_oversized_redirects_are_rejected()
    {
        Assert.Equal("feed-redirect", Assert.Throws<Cp6ReleaseContractException>(() => FormalFeedPolicy.BlobRequest(null)).Code);
        Assert.Equal("feed-redirect", Assert.Throws<Cp6ReleaseContractException>(() =>
            FormalFeedPolicy.BlobRequest(new Uri(Blob + new string('a', 8192)))).Code);
    }

    [Fact]
    public void Production_handler_has_no_redirect_proxy_cookie_decompression_or_certificate_bypass()
    {
        using var handler = FormalFeedPolicy.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
        Assert.False(handler.UseCookies);
        Assert.False(handler.PreAuthenticate);
        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.Null(handler.Credentials);
        Assert.Null(handler.ActivityHeadersPropagator);
        Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback);
        Assert.Equal(X509RevocationMode.Online, handler.SslOptions.CertificateRevocationCheckMode);
        Assert.Equal(TimeSpan.FromSeconds(10), handler.ConnectTimeout);
        Assert.Equal(16, handler.MaxResponseHeadersLength);
    }

    [Theory]
    [InlineData(206)]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task Body_reader_rejects_partial_redirect_and_error_responses(int status)
    {
        using var response = Response("abc"u8.ToArray());
        response.StatusCode = (HttpStatusCode)status;
        await Reject(() => StrictHttpBody.ReadAsync(response, "application/octet-stream", 8, CancellationToken.None), "feed-http-status");
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("missing")]
    [InlineData("gzip")]
    public async Task Body_reader_requires_exact_media_type_and_unchanged_content_encoding(string mutation)
    {
        using var response = Response("abc"u8.ToArray());
        if (mutation == "wrong") response.Content.Headers.ContentType = new("text/html");
        if (mutation == "missing") response.Content.Headers.ContentType = null;
        if (mutation == "gzip") response.Content.Headers.ContentEncoding.Add("gzip");
        await Reject(() => StrictHttpBody.ReadAsync(response, "application/octet-stream", 8, CancellationToken.None), "feed-media-type");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(9, 9)]
    [InlineData(9, 3)]
    [InlineData(3, 2)]
    [InlineData(3, 4)]
    [InlineData(3, 9)]
    public async Task Body_reader_rejects_empty_oversized_truncated_or_misdeclared_bytes(int count, int declared)
    {
        using var response = Response(new byte[count]);
        response.Content.Headers.ContentLength = declared;
        await Reject(() => StrictHttpBody.ReadAsync(response, "application/octet-stream", 8, CancellationToken.None), "feed-size");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8388609)]
    public async Task Body_limit_cannot_expand_the_global_binary_limit(int maximum)
    {
        using var response = Response("abc"u8.ToArray());
        await Reject(() => StrictHttpBody.ReadAsync(response, "application/octet-stream", maximum, CancellationToken.None), "feed-size");
    }

    [Fact]
    public async Task Exact_body_bytes_are_preserved()
    {
        var bytes = "raw\nbytes\0"u8.ToArray();
        using var response = Response(bytes);
        Assert.Equal(bytes, await StrictHttpBody.ReadAsync(response, "application/octet-stream", 64, CancellationToken.None));
    }

    [Fact]
    public async Task Pre_cancelled_calls_do_not_send_or_read()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            FormalPackageSource.DownloadAndVerifyAsync("", "", cancel.Token));
        using var response = Response("abc"u8.ToArray());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            StrictHttpBody.ReadAsync(response, "application/octet-stream", 8, cancel.Token));
    }

    // HTTP message/byte unit tests are not remote Registry or candidate acceptance evidence.
    private static HttpResponseMessage Response(byte[] bytes)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return response;
    }

    private static async Task Reject(Func<Task> action, string expected) =>
        Assert.Equal(expected, (await Assert.ThrowsAsync<Cp6ReleaseContractException>(action)).Code);
}
```

- [x] Configure the approved .NET 8 host and existing real cosign/readback inputs through the process environment. Load only the already-authorized GitHub read token into P10_FEED_READ_TOKEN without printing it or passing it as a command argument; clear that environment variable in finally. Run from tools/p10:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --configuration Release --filter FullyQualifiedName~FormalPackageSourceTests
```

Expected: missing-API compile failure, not a green test suite.

- [x] Add these complete throwing scaffolds, repeat the command with the mandatory live-read token available, and inspect all 60 actual missing-behavior failures (zero skips). A missing credential or bad test setup is not a valid Red result.

### Throwing scaffold: tools/p10/ReleaseVerifier/FormalFeedPolicy.cs

```csharp
namespace CP6.P10.ReleaseVerifier;

internal static class FormalFeedPolicy
{
    internal const string Index = "https://nuget.pkg.github.com/GTX537/index.json";
    internal static SocketsHttpHandler CreateHandler() => throw new NotImplementedException();
    internal static void RequireIndex(ReadOnlyMemory<byte> raw) => throw new NotImplementedException();
    internal static HttpRequestMessage IndexRequest(string token) => throw new NotImplementedException();
    internal static HttpRequestMessage PackageRequest(string id, string token) => throw new NotImplementedException();
    internal static HttpRequestMessage BlobRequest(Uri? location) => throw new NotImplementedException();
}
```

### Throwing scaffold: tools/p10/ReleaseVerifier/StrictHttpBody.cs

```csharp
namespace CP6.P10.ReleaseVerifier;

internal static class StrictHttpBody
{
    internal static Task<byte[]> ReadAsync(HttpResponseMessage response, string mediaType, int maximumBytes,
        CancellationToken cancellationToken) => throw new NotImplementedException();
}
```

### Throwing scaffold: tools/p10/ReleaseVerifier/FormalPackageSource.cs

```csharp
namespace CP6.P10.ReleaseVerifier;

public static class FormalPackageSource
{
    public static Task<DownloadedNuGetPackage> DownloadAndVerifyAsync(string packageId, string readToken,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

public sealed class DownloadedNuGetPackage
{
    public VerifiedNuGetPackage Proof => throw new NotImplementedException();
    public DateTimeOffset RetrievedAtUtc => throw new NotImplementedException();
    public string FeedServiceIndex => throw new NotImplementedException();
    public byte[] CopyPackageBytes() => throw new NotImplementedException();
}
```


## Task 2: Implement the bounded feed adapter

- [x] Replace only the throwing scaffolds with the complete implementation after Red. The live index-version correction adds a direct 3.0.0-beta.1 regression (61 total); observe it fail against the initial stable-only check before accepting the two officially equivalent versions. [NuGet index versioning](https://learn.microsoft.com/en-us/nuget/api/service-index).

### tools/p10/ReleaseVerifier/FormalFeedPolicy.cs

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// The service index is authenticated data, never authority to move credentials to another host.
internal static class FormalFeedPolicy
{
    internal const string Index = "https://nuget.pkg.github.com/GTX537/index.json";
    internal const string BaseAddress = "https://nuget.pkg.github.com/GTX537/download";
    private const string BlobPrefix = "https://nugetregistryv2prod.blob.core.windows.net/";

    internal static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        UseProxy = false,
        Credentials = null,
        PreAuthenticate = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        MaxConnectionsPerServer = 2,
        MaxResponseHeadersLength = 16,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ActivityHeadersPropagator = null,
        SslOptions = new() { CertificateRevocationCheckMode = X509RevocationMode.Online }
    };

    internal static void RequirePackageId(string id)
    {
        if (string.IsNullOrEmpty(id) || !S06ReleaseIdentity.PackageHashes.ContainsKey(id)) throw Error("feed-package-id");
    }

    internal static void RequireIndex(ReadOnlyMemory<byte> raw)
    {
        if (raw.Length is < 1 or > 65536) throw Error("feed-index-size");
        try
        {
            using var document = JsonDocument.Parse(raw, new() { MaxDepth = 16 });
            var root = document.RootElement;
            if (Single(root, "version").GetString() is not ("3.0.0" or "3.0.0-beta.1")) throw Error("feed-index");
            var resources = Single(root, "resources");
            if (resources.ValueKind != JsonValueKind.Array || resources.GetArrayLength() is < 1 or > 128)
                throw Error("feed-index");
            var matches = 0;
            foreach (var resource in resources.EnumerateArray())
            {
                var type = Single(resource, "@type");
                if (type.ValueKind != JsonValueKind.String) throw Error("feed-index");
                if (type.GetString() != "PackageBaseAddress/3.0.0") continue;
                if (Single(resource, "@id").GetString() != BaseAddress) throw Error("feed-index-authority");
                matches++;
            }
            if (matches != 1) throw Error("feed-index");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw Error("feed-index");
        }
    }

    internal static HttpRequestMessage IndexRequest(string token) => AuthenticatedRequest(new Uri(Index), token, "application/json");

    internal static HttpRequestMessage PackageRequest(string id, string token)
    {
        RequirePackageId(id);
        var lower = id.ToLowerInvariant();
        return AuthenticatedRequest(new Uri($"{BaseAddress}/{lower}/0.10.1/{lower}.0.10.1.nupkg"),
            token, "application/octet-stream");
    }

    internal static HttpRequestMessage BlobRequest(Uri? location)
    {
        if (location is null || !location.IsAbsoluteUri || location.OriginalString.Length > 8192 ||
            !location.OriginalString.StartsWith(BlobPrefix, StringComparison.Ordinal) ||
            !location.IsWellFormedOriginalString() || location.OriginalString.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '\\') ||
            location.Scheme != Uri.UriSchemeHttps || location.Host != "nugetregistryv2prod.blob.core.windows.net" ||
            !location.IsDefaultPort || location.UserInfo.Length != 0 || location.Fragment.Length != 0 || location.Query.Length < 2)
            throw Error("feed-redirect");
        return Request(location, "application/octet-stream");
    }

    private static HttpRequestMessage AuthenticatedRequest(Uri uri, string token, string mediaType)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 4096 || token.Any(c => c is < '!' or > '~'))
            throw Error("feed-credential");
        var request = Request(uri, mediaType);
        request.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("GTX537:" + token)));
        return request;
    }

    private static HttpRequestMessage Request(Uri uri, string mediaType)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new(mediaType));
        request.Headers.UserAgent.ParseAdd("CP6-P10-Verifier/1");
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        return request;
    }

    private static JsonElement Single(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Error("feed-index");
        var matches = value.EnumerateObject().Where(p => p.Name == name).ToArray();
        if (matches.Length != 1) throw Error("feed-index");
        return matches[0].Value;
    }

    internal static Cp6ReleaseContractException Error(string code) =>
        new(code, "Formal feed transfer violates the fixed authenticated-read policy.");
}
```

### tools/p10/ReleaseVerifier/StrictHttpBody.cs

```csharp
using System.Net;

namespace CP6.P10.ReleaseVerifier;

// Binary transfer bounds differ from the 4 MiB CP6 control-object contract.
internal static class StrictHttpBody
{
    internal static async Task<byte[]> ReadAsync(HttpResponseMessage response, string mediaType, int maximumBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumBytes is < 1 or > FormalNuGetVerifier.MaximumPackageBytes) throw FormalFeedPolicy.Error("feed-size");
        if (response.StatusCode != HttpStatusCode.OK) throw FormalFeedPolicy.Error("feed-http-status");
        var content = response.Content;
        if (content.Headers.ContentType?.MediaType != mediaType || content.Headers.ContentEncoding.Count != 0)
            throw FormalFeedPolicy.Error("feed-media-type");
        var declared = content.Headers.ContentLength;
        if (declared is not null && (declared < 1 || declared > maximumBytes)) throw FormalFeedPolicy.Error("feed-size");
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[maximumBytes + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken);
            if (read == 0) break;
            count += read;
        }
        if (count == 0 || count > maximumBytes || (declared is not null && declared != count))
            throw FormalFeedPolicy.Error("feed-size");
        return buffer.AsSpan(0, count).ToArray();
    }
}
```

### tools/p10/ReleaseVerifier/FormalPackageSource.cs

```csharp
using System.Net;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

public static class FormalPackageSource
{
    // No endpoint, cache, handler, trust policy or version override is accepted.
    public static async Task<DownloadedNuGetPackage> DownloadAndVerifyAsync(string packageId, string readToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FormalFeedPolicy.RequirePackageId(packageId);
        using var indexRequest = FormalFeedPolicy.IndexRequest(readToken);
        using var client = new HttpClient(FormalFeedPolicy.CreateHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            using var indexResponse = await client.SendAsync(indexRequest, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            FormalFeedPolicy.RequireIndex(await StrictHttpBody.ReadAsync(indexResponse, "application/json", 65536, deadline.Token));
            using var packageRequest = FormalFeedPolicy.PackageRequest(packageId, readToken);
            using var packageResponse = await client.SendAsync(packageRequest, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            byte[] bytes;
            if (packageResponse.StatusCode == HttpStatusCode.Redirect)
            {
                // A single explicitly allowed storage hop; never forward GitHub credentials.
                using var blobRequest = FormalFeedPolicy.BlobRequest(packageResponse.Headers.Location);
                using var blobResponse = await client.SendAsync(blobRequest, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                bytes = await StrictHttpBody.ReadAsync(blobResponse, "application/octet-stream",
                    FormalNuGetVerifier.MaximumPackageBytes, deadline.Token);
            }
            else
            {
                bytes = await StrictHttpBody.ReadAsync(packageResponse, "application/octet-stream",
                    FormalNuGetVerifier.MaximumPackageBytes, deadline.Token);
            }
            var retrievedAt = DateTimeOffset.UtcNow;
            var proof = await FormalNuGetVerifier.VerifyAsync(packageId, bytes, deadline.Token);
            return new(bytes, proof, retrievedAt);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw FormalFeedPolicy.Error("feed-timeout");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Neither signed redirect URLs nor response/error bodies enter diagnostics.
            throw FormalFeedPolicy.Error("feed-transfer");
        }
    }
}

public sealed class DownloadedNuGetPackage
{
    private readonly byte[] _bytes;

    internal DownloadedNuGetPackage(byte[] bytes, VerifiedNuGetPackage proof, DateTimeOffset retrievedAtUtc)
    {
        _bytes = bytes.ToArray();
        Proof = proof;
        RetrievedAtUtc = retrievedAtUtc;
    }

    public VerifiedNuGetPackage Proof { get; }
    public DateTimeOffset RetrievedAtUtc { get; }
    public string FeedServiceIndex => FormalFeedPolicy.Index;
    public byte[] CopyPackageBytes() => _bytes.ToArray();
}
```


- [x] Repeat the focused command with the real read token. Expected: 61 passed, zero failed/skipped, including seven newly downloaded packages verified through actual cryptography and online TSA chain checks.
- [x] Run the full suite, formatting and locked restore:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --configuration Release
dotnet format whitespace ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --verify-no-changes
dotnet restore ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --configfile ../../eng/p10/NuGet.formal.config --packages ../../artifacts/p10/foundation-restore/packages --locked-mode
```

Expected: 269 passed, zero skipped, format exits 0, locked restore succeeds with both lock files unchanged. Genuine TLS, author, TSA, access, network or feed transformation failures stop verification; they are not converted to skips or bypass flags.

## Task 3: Review and checkpoint

- [x] Compare the three implementation files and full test file with this plan. Review the complete five-file diff and verify no source/trust/lock/workflow/runtime mutation or secret/temporary-URL disclosure.
- [x] Record actual test, live readback and hygiene results below only after observing completion.
- [ ] Stage only these five files and make a normal checkpoint commit:

```powershell
git diff --check
git add -- tools/p10/ReleaseVerifier/FormalFeedPolicy.cs tools/p10/ReleaseVerifier/StrictHttpBody.cs tools/p10/ReleaseVerifier/FormalPackageSource.cs tools/p10/ReleaseVerifier.Tests/FormalPackageSourceTests.cs docs/superpowers/plans/2026-09-07-p10-s06-formal-feed-readback.md
git diff --cached --check
git commit -m "feat(p10): read and verify packages through restricted authenticated feed"
```

This module proves package readback, not candidate acceptance. R2 authenticated graph reads/writes, source/workflow/CRM proof, OCI evidence, assembly, pre/post-commit verification, workflows and cross-repository closure remain required for full S06.

## Self-review

The service index cannot reassign credential authority. An explicit single-hop Blob policy accommodates the observed protocol without automatic redirect or credential forwarding. The fixed package hashes and current pinned signature policy remain the final byte/crypto authority. Response fixtures and unpublished graph tests remain distinct from actual live package readback; no success boolean or caller-selected network/trust implementation is exposed.

## Observed execution

- Initial missing-API compile failure was followed by three throwing scaffolds. The test analyzer first required a collection-rejection assertion instead of an empty filtered collection; after that test-only correction, all 60 actual cases failed with NotImplementedException, zero skipped, as confirmed from the Red TRX.
- First live implementation run passed 53 policy/body cases but rejected all seven live reads at the index version check. A sanitized read established the actual version 3.0.0-beta.1; the official NuGet index contract explicitly documents its equivalence to 3.0.0. An added direct beta-version case failed against the stable-only check before the narrow two-version correction. No endpoint, identity, TLS, author or timestamp trust boundary changed.
- Corrected focused run passed 61/61 with zero skips. Its seven real network cases each downloaded the selected package afresh, verified the independent publication SHA-256, actual author/integrity signature and current pinned policy, and validated the RFC3161 token and online system-root TSA chain. The local TRX is under artifacts/p10/feed-green; no package or temporary signed URL was written into delivery evidence.
- Full Release suite passed 269/269, zero failed/skipped and no build warnings/errors. Whitespace verification and locked restore both exited 0. Existing dependency locks, public policies, certificate, workflows and application runtime files remain unchanged.
- All four source/test files exactly match their complete plan blocks. Scoped review and hygiene checks found no real token, signing key, machine-specific configuration, storage mutation or claimed candidate acceptance. GitHub credentials were held only in the test process environment/request-local headers and cleared from that process environment in finally.
