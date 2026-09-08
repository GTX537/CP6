# P10 S06 Read-Only GHCR Manifest Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans sequentially in this task. The user selected no delegation.

**Goal:** Independently read the approved verifier image manifest by exact digest and verify its raw hash, media type and image descriptors.

**Architecture:** A fixed GHCR pull-token request reuses the intended consumer's existing GitHub Packages read credential. A second fixed GET reads only ghcr.io/gtx537/cp6-p10-verifier/manifests/<digest>. Reuse the hardened no-redirect/no-proxy/no-cookie transport and bounded byte reader; never follow token realms, tags, descriptor URLs or image blobs.

**Tech Stack:** .NET 8.0.424, CP6.Platform.Release [0.10.1], HttpClient, GHCR Distribution API, OCI/Docker v2 single-image manifests, xUnit.

---

## Scope and evidence boundary

The selected repository is the independently approved non-deployable verifier image, not cp6-api or cp6-web. Only GET is implemented. The consumer credential enters only the fixed HTTPS token endpoint; the requested scope is pull for that one repository. The returned short-lived token is used only for the fixed manifest GET. No process/environment credential fallback, injected transport, arbitrary endpoint, write, delete, redirect, blob pull or local Docker operation is introduced.

The manifest must match the caller's exact lowercase SHA-256 digest in raw bytes. Docker-Content-Digest is optional per the Distribution specification; if present, it must be exactly one matching value. Support only OCI or Docker v2 single-image manifests with matching media types, nonempty image config/layer descriptors, positive declared sizes, standard distributable layer media and no inline data/external URLs. A multi-platform index or artifact attachment is not the selected tool image. This does not claim config/layer bytes, architecture, signatures, SBOM, scan, provenance or candidate acceptance have been verified.

Current read-only preflight found the not-yet-created repository's anonymous token request returns 403. This module therefore has a real GHCR negative test only. Unit HTTP messages/manifest examples are not mocked network acceptance evidence. The actual successful image read must be exercised after the protected workflow produces its authorized image; it cannot be replaced with an upstream image, fixture or skipped gate.

No extra Platform read token is needed. CP6.Platform is public. Private CRM evidence remains confined to the protected collector; this code uses only the same GitHub Packages read credential needed for the formal NuGet packages.

Official sources:

- [OCI image manifest v1.1.1](https://raw.githubusercontent.com/opencontainers/image-spec/v1.1.1/manifest.md)
- [OCI Distribution v1.1.1 pull protocol](https://raw.githubusercontent.com/opencontainers/distribution-spec/v1.1.1/spec.md)
- [Distribution token authentication](https://distribution.github.io/distribution/spec/auth/token/)

## File map

Create this plan; tools/p10/ReleaseVerifier/OciWirePolicy.cs (fixed requests and bounded response rules); OciManifestChecks.cs (raw hash and descriptors); OciImageSource.cs (actual two-request read and owned observations); and tools/p10/ReleaseVerifier.Tests/OciImageSourceTests.cs (60 unit/live-negative cases).

## Task 1: Write tests and observe red

- [x] Add the complete test file first.

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// HTTP/manifest examples are unit vectors, not mocked network or formal image evidence.
// The only live case reads an unavailable digest and cannot claim successful OCI verification.
public sealed class OciImageSourceTests
{
    private const string MissingDigest = "sha256:0000000000000000000000000000000000000000000000000000000000000000";
    private const string Token = "fixture-read-token-not-a-secret";

    [Fact]
    public void Requests_are_GET_only_and_bind_credentials_to_the_two_fixed_GHCR_targets()
    {
        using var token = OciWirePolicy.TokenRequest(Token);
        Assert.Equal(HttpMethod.Get, token.Method);
        Assert.Equal("https://ghcr.io/token?service=ghcr.io&scope=repository%3Agtx537%2Fcp6-p10-verifier%3Apull",
            token.RequestUri!.OriginalString);
        Assert.Equal("Basic", token.Headers.Authorization!.Scheme);
        Assert.Equal("GTX537:" + Token, Encoding.UTF8.GetString(Convert.FromBase64String(token.Headers.Authorization.Parameter!)));
        using var manifest = OciWirePolicy.ManifestRequest(MissingDigest, "ephemeral-pull-token");
        Assert.Equal(HttpMethod.Get, manifest.Method);
        Assert.Equal("https://ghcr.io/v2/gtx537/cp6-p10-verifier/manifests/" + MissingDigest, manifest.RequestUri!.OriginalString);
        Assert.Equal("Bearer", manifest.Headers.Authorization!.Scheme);
        Assert.Equal("ephemeral-pull-token", manifest.Headers.Authorization.Parameter);
        Assert.Equal(new[] { OciWirePolicy.OciManifest, OciWirePolicy.DockerManifest }, manifest.Headers.Accept.Select(a => a.MediaType));
        Assert.Null(manifest.Content);
        Assert.True(manifest.Headers.CacheControl!.NoStore);
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("sha256:0000")]
    [InlineData("SHA256:0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("sha256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("sha256:000000000000000000000000000000000000000000000000000000000000000/")]
    [InlineData("https://attacker.invalid/manifest")]
    public async Task Invalid_image_selection_fails_before_authentication_or_network_access(string digest)
    {
        Assert.Equal("oci-digest", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            OciImageSource.ReadAsync(digest, Token))).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("contains space")]
    [InlineData("contains\nnewline")]
    public async Task Malformed_caller_credentials_cannot_reach_GHCR(string token)
    {
        Assert.Equal("oci-credential", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            OciImageSource.ReadAsync(MissingDigest, token))).Code);
    }

    [Theory]
    [InlineData("{\"token\":\"abc\"}")]
    [InlineData("{\"token\":\"abc\",\"access_token\":\"abc\"}")]
    [InlineData("{\"token\":\"abc\",\"realm\":\"https://attacker.invalid/\"}")]
    public void Pull_token_is_extracted_without_following_response_locations(string json) =>
        Assert.Equal("abc", OciWirePolicy.PullToken(Encoding.UTF8.GetBytes(json)));

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"token\":\"\"}")]
    [InlineData("{\"token\":null}")]
    [InlineData("{\"token\":\"a b\"}")]
    [InlineData("{\"token\":\"a\",\"access_token\":\"b\"}")]
    [InlineData("{\"token\":\"a\",\"token\":\"b\"}")]
    [InlineData("not-json")]
    public void Ambiguous_or_malformed_pull_credentials_fail_closed(string json) =>
        Assert.Equal("oci-credential", Assert.Throws<Cp6ReleaseContractException>(() =>
            OciWirePolicy.PullToken(Encoding.UTF8.GetBytes(json))).Code);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_OCI_or_Docker_manifest_bytes_are_hashed_and_described_without_pulling_layers(bool docker)
    {
        var bytes = Manifest(docker);
        using var response = Response(bytes, docker ? OciWirePolicy.DockerManifest : OciWirePolicy.OciManifest);
        response.Headers.TryAddWithoutValidation("Docker-Content-Digest", Hash(bytes));
        var result = await OciManifestChecks.ResponseAsync(response, Hash(bytes), CancellationToken.None);
        Assert.Equal(bytes, result.Bytes);
        Assert.Equal("sha256:" + new string('c', 64), result.Description.Config.Digest);
        Assert.Equal(2, result.Description.Layers.Count);
        Assert.Equal(123, result.Description.Config.ByteLength);
        Assert.Equal(345, result.Description.Layers[1].ByteLength);
        Assert.Contains("\n", Encoding.UTF8.GetString(result.Bytes));
    }

    [Fact]
    public async Task A_missing_optional_registry_digest_header_does_not_replace_body_hash_verification()
    {
        var bytes = Manifest();
        using var response = Response(bytes);
        _ = await OciManifestChecks.ResponseAsync(response, Hash(bytes), CancellationToken.None);
    }

    [Theory]
    [InlineData("body")]
    [InlineData("header")]
    [InlineData("duplicate-header")]
    public async Task Manifest_hash_or_header_substitution_is_rejected(string mutation)
    {
        var bytes = Manifest();
        using var response = Response(bytes);
        if (mutation == "header") response.Headers.TryAddWithoutValidation("Docker-Content-Digest", MissingDigest);
        if (mutation == "duplicate-header")
            response.Headers.TryAddWithoutValidation("Docker-Content-Digest", new[] { Hash(bytes), Hash(bytes) });
        Assert.Equal("oci-digest", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            OciManifestChecks.ResponseAsync(response, mutation == "body" ? MissingDigest : Hash(bytes), CancellationToken.None))).Code);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("media")]
    [InlineData("index")]
    [InlineData("artifact")]
    [InlineData("subject")]
    [InlineData("config-media")]
    [InlineData("config-digest")]
    [InlineData("config-size")]
    [InlineData("config-urls")]
    [InlineData("layer-data")]
    [InlineData("layer-media")]
    [InlineData("layer-digest")]
    [InlineData("layer-size")]
    [InlineData("empty-layers")]
    [InlineData("duplicate-json")]
    public void A_matching_hash_alone_does_not_make_arbitrary_JSON_an_image_manifest(string mutation)
    {
        var bytes = Manifest(change: root =>
        {
            if (mutation == "schema") root["schemaVersion"] = 1;
            if (mutation == "media") root["mediaType"] = OciWirePolicy.DockerManifest;
            if (mutation == "index") root["manifests"] = new JsonArray();
            if (mutation == "artifact") root["artifactType"] = "application/example";
            if (mutation == "subject") root["subject"] = new JsonObject();
            if (mutation == "config-media") root["config"]!["mediaType"] = "application/vnd.oci.empty.v1+json";
            if (mutation == "config-digest") root["config"]!["digest"] = "sha256:short";
            if (mutation == "config-size") root["config"]!["size"] = 0;
            if (mutation == "config-urls") root["config"]!["urls"] = new JsonArray("https://attacker.invalid/config");
            if (mutation == "layer-data") root["layers"]![0]!["data"] = "e30=";
            if (mutation == "layer-media") root["layers"]![0]!["mediaType"] = "application/vnd.oci.empty.v1+json";
            if (mutation == "layer-digest") root["layers"]![0]!["digest"] = "sha256:short";
            if (mutation == "layer-size") root["layers"]![0]!["size"] = -1;
            if (mutation == "empty-layers") root["layers"] = new JsonArray();
        });
        if (mutation == "duplicate-json")
            bytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":2," + Encoding.UTF8.GetString(bytes)[1..]);
        Assert.Equal("oci-manifest", Assert.Throws<Cp6ReleaseContractException>(() =>
            OciManifestChecks.Read(bytes, Hash(bytes), OciWirePolicy.OciManifest)).Code);
    }

    [Theory]
    [InlineData("redirect", "oci-http-status")]
    [InlineData("partial", "oci-http-status")]
    [InlineData("denied", "oci-http-status")]
    [InlineData("encoding", "oci-media-type")]
    [InlineData("range", "oci-media-type")]
    [InlineData("media", "oci-media-type")]
    [InlineData("charset", "oci-media-type")]
    [InlineData("empty", "oci-body")]
    [InlineData("truncated", "oci-body")]
    [InlineData("oversize", "oci-body")]
    public async Task Transfer_shape_failures_do_not_return_successful_manifest_metadata(string mutation, string expected)
    {
        var bytes = mutation == "empty" ? Array.Empty<byte>() :
            mutation == "oversize" ? new byte[4194305] : Manifest();
        using var response = Response(bytes);
        if (mutation == "redirect")
        {
            response.StatusCode = HttpStatusCode.Redirect;
            response.Headers.Location = new Uri("https://attacker.invalid/credential");
        }
        if (mutation == "partial") response.StatusCode = HttpStatusCode.PartialContent;
        if (mutation == "denied") response.StatusCode = HttpStatusCode.Forbidden;
        if (mutation == "encoding") response.Content.Headers.ContentEncoding.Add("gzip");
        if (mutation == "range") response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, bytes.Length - 1, bytes.Length);
        if (mutation == "media") response.Content.Headers.ContentType = new("application/json");
        if (mutation == "charset") response.Content.Headers.ContentType!.CharSet = "utf-16";
        if (mutation == "truncated") response.Content.Headers.ContentLength = bytes.Length + 1;
        Assert.Equal(expected, (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            OciWirePolicy.ReadBodyAsync(response, true, CancellationToken.None))).Code);
    }

    [Fact]
    public async Task Precancelled_registry_reads_do_not_open_a_connection()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            OciImageSource.ReadAsync(MissingDigest, Token, cancellation.Token));
    }

    [Fact]
    public async Task Actual_GHCR_cannot_turn_an_unavailable_digest_into_verified_evidence()
    {
        var token = Environment.GetEnvironmentVariable("P10_FEED_READ_TOKEN") ??
            throw new InvalidOperationException("An actual GitHub Packages read token is required.");
        var error = await Assert.ThrowsAsync<Cp6ReleaseContractException>(() => OciImageSource.ReadAsync(MissingDigest, token));
        Assert.Equal("oci-http-status", error.Code);
        Assert.Null(error.InnerException);
        Assert.True(!error.ToString().Contains(token, StringComparison.Ordinal));
        Assert.DoesNotContain("ghcr.io/token", error.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Oversized_credentials_are_rejected_before_request_construction(bool pull)
    {
        Assert.Equal("oci-credential", Assert.Throws<Cp6ReleaseContractException>(() =>
        {
            using var request = pull ? OciWirePolicy.ManifestRequest(MissingDigest, new string('x', 4097)) :
                OciWirePolicy.TokenRequest(new string('x', 4097));
        }).Code);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("media")]
    [InlineData("size")]
    public async Task Token_HTTP_response_is_bounded_JSON(string mutation)
    {
        var bytes = mutation == "size" ? new byte[65537] : "{\"token\":\"abc\"}"u8.ToArray();
        using var response = Response(bytes, mutation == "media" ? "text/plain" : "application/json");
        if (mutation == "valid")
            Assert.Equal("abc", OciWirePolicy.PullToken(await OciWirePolicy.ReadBodyAsync(response, false, CancellationToken.None)));
        else
            Assert.Equal(mutation == "media" ? "oci-media-type" : "oci-body",
                (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
                    OciWirePolicy.ReadBodyAsync(response, false, CancellationToken.None))).Code);
    }

    [Fact]
    public void Downloaded_manifest_owns_its_bytes_without_claiming_config_or_layer_downloads()
    {
        var bytes = Manifest();
        var digest = Hash(bytes);
        var description = OciManifestChecks.Read(bytes, digest, OciWirePolicy.OciManifest);
        var observed = new DownloadedOciManifest(bytes, digest, description, DateTimeOffset.UnixEpoch);
        Array.Fill(bytes, (byte)0);
        var copied = observed.CopyBytes();
        Assert.Equal(digest, Hash(copied));
        Array.Fill(copied, (byte)0);
        Assert.Equal(digest, Hash(observed.CopyBytes()));
        Assert.Equal("ghcr.io/gtx537/cp6-p10-verifier", observed.Repository);
        Assert.Equal(DateTimeOffset.UnixEpoch, observed.RetrievedAtUtc);
        Assert.Same(description, observed.Description);
    }

    private static string Hash(byte[] bytes) => "sha256:" + Cp6DeterministicJson.Sha256Hex(bytes);

    private static HttpResponseMessage Response(byte[] bytes, string mediaType = OciWirePolicy.OciManifest)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new(mediaType);
        return response;
    }

    private static byte[] Manifest(bool docker = false, Action<JsonObject>? change = null)
    {
        var layerType = docker ? "application/vnd.docker.image.rootfs.diff.tar.gzip" : "application/vnd.oci.image.layer.v1.tar+gzip";
        var node = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["mediaType"] = docker ? OciWirePolicy.DockerManifest : OciWirePolicy.OciManifest,
            ["config"] = Descriptor(docker ? "application/vnd.docker.container.image.v1+json" : "application/vnd.oci.image.config.v1+json", 'c', 123),
            ["layers"] = new JsonArray(Descriptor(layerType, 'd', 234), Descriptor(layerType, 'e', 345))
        };
        change?.Invoke(node);
        return JsonSerializer.SerializeToUtf8Bytes(node, new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject Descriptor(string media, char digest, long size) => new()
    {
        ["mediaType"] = media,
        ["digest"] = "sha256:" + new string(digest, 64),
        ["size"] = size
    };
}
```

- [x] Add the following throwing scaffolds at the three listed production paths so the tests compile without implementing the behavior.

### OciWirePolicy.cs

```csharp
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

internal static class OciWirePolicy
{
    internal const string OciManifest = "application/vnd.oci.image.manifest.v1+json";
    internal const string DockerManifest = "application/vnd.docker.distribution.manifest.v2+json";
    internal static Cp6ReleaseContractException Error(string code) => throw new NotImplementedException();
    internal static void RequireDigest(string digest) => throw new NotImplementedException();
    internal static void RequireToken(string token) => throw new NotImplementedException();
    internal static HttpRequestMessage TokenRequest(string readToken) => throw new NotImplementedException();
    internal static HttpRequestMessage ManifestRequest(string digest, string pullToken) => throw new NotImplementedException();
    internal static Task<byte[]> ReadBodyAsync(HttpResponseMessage response, bool manifest,
        CancellationToken cancellationToken) => throw new NotImplementedException();
    internal static string PullToken(ReadOnlyMemory<byte> bytes) => throw new NotImplementedException();
}
```

### OciManifestChecks.cs

```csharp
namespace CP6.P10.ReleaseVerifier;

internal static class OciManifestChecks
{
    internal static Task<(byte[] Bytes, OciManifestDescription Description)> ResponseAsync(
        HttpResponseMessage response, string digest, CancellationToken cancellationToken) => throw new NotImplementedException();
    internal static OciManifestDescription Read(ReadOnlyMemory<byte> bytes, string digest, string mediaType) =>
        throw new NotImplementedException();
}

internal sealed record OciDescriptor(string MediaType, string Digest, long ByteLength);
internal sealed record OciManifestDescription(string MediaType, OciDescriptor Config, IReadOnlyList<OciDescriptor> Layers);
```

### OciImageSource.cs

```csharp
namespace CP6.P10.ReleaseVerifier;

internal static class OciImageSource
{
    internal static Task<DownloadedOciManifest> ReadAsync(string digest, string readToken,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

internal sealed class DownloadedOciManifest
{
    internal DownloadedOciManifest(byte[] bytes, string digest, OciManifestDescription description, DateTimeOffset retrievedAtUtc) =>
        throw new NotImplementedException();
    internal string Repository => throw new NotImplementedException();
    internal string Digest => throw new NotImplementedException();
    internal OciManifestDescription Description => throw new NotImplementedException();
    internal DateTimeOffset RetrievedAtUtc => throw new NotImplementedException();
    internal byte[] CopyBytes() => throw new NotImplementedException();
}
```

- [x] Run the focused suite with the existing actual GitHub Packages read token. Inspect the TRX for 60 expected NotImplementedException failures, zero skips and no compilation errors.

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~OciImageSourceTests --logger 'trx;LogFileName=oci-image-red.trx' --results-directory ../../artifacts/p10/oci-image-red
```

## Task 2: Implement the fixed read path

- [x] Replace each scaffold with its complete implementation below.

### OciWirePolicy.cs

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Two fixed GET targets only; no challenge-selected realm, redirects, tags or registry writes.
internal static class OciWirePolicy
{
    internal const string OciManifest = "application/vnd.oci.image.manifest.v1+json";
    internal const string DockerManifest = "application/vnd.docker.distribution.manifest.v2+json";

    internal static Cp6ReleaseContractException Error(string code) => new(code, "Pinned OCI manifest read did not complete successfully.");

    internal static void RequireDigest(string digest)
    {
        if (digest is null || digest.Length != 71 || !digest.StartsWith("sha256:", StringComparison.Ordinal) ||
            digest[7..].Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f'))) throw Error("oci-digest");
    }

    internal static void RequireToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 4096 || token.Any(c => c is < '!' or > '~'))
            throw Error("oci-credential");
    }

    internal static HttpRequestMessage TokenRequest(string readToken)
    {
        RequireToken(readToken);
        var request = Request("https://ghcr.io/token?service=ghcr.io&scope=repository%3Agtx537%2Fcp6-p10-verifier%3Apull");
        request.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("GTX537:" + readToken)));
        request.Headers.Accept.Add(new("application/json"));
        return request;
    }

    internal static HttpRequestMessage ManifestRequest(string digest, string pullToken)
    {
        RequireDigest(digest);
        RequireToken(pullToken);
        var request = Request("https://ghcr.io/v2/gtx537/cp6-p10-verifier/manifests/" + digest);
        request.Headers.Authorization = new("Bearer", pullToken);
        request.Headers.Accept.Add(new(OciManifest));
        request.Headers.Accept.Add(new(DockerManifest));
        return request;
    }

    private static HttpRequestMessage Request(string uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        request.Headers.UserAgent.ParseAdd("CP6-P10-ReleaseVerifier/1");
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        return request;
    }

    internal static async Task<byte[]> ReadBodyAsync(HttpResponseMessage response, bool manifest,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (response.StatusCode != HttpStatusCode.OK) throw Error("oci-http-status");
        var type = response.Content.Headers.ContentType;
        if (type is null || (manifest ? type.MediaType is not (OciManifest or DockerManifest) : type.MediaType != "application/json") ||
            response.Content.Headers.ContentEncoding.Count != 0 || response.Content.Headers.ContentRange is not null ||
            type.Parameters.Count > 1 || type.Parameters.Any(p => !string.Equals(p.Name, "charset", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(p.Value, "utf-8", StringComparison.OrdinalIgnoreCase)))
            throw Error("oci-media-type");
        try
        {
            return await StrictHttpBody.ReadAsync(response, type.MediaType!, manifest ? 4 * 1024 * 1024 : 65536, cancellationToken);
        }
        catch (Cp6ReleaseContractException) { throw Error("oci-body"); }
    }

    internal static string PullToken(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            var root = GitHubApiJson.Parse(bytes);
            var token = root.GetProperty("token").GetString()!;
            RequireToken(token);
            if (root.TryGetProperty("access_token", out var alternate) && alternate.GetString() != token)
                throw Error("oci-credential");
            return token;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw Error("oci-credential");
        }
    }
}
```

### OciManifestChecks.cs

```csharp
using System.Text.Json;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Manifest hashes and descriptors only. Config/layer bytes are not downloaded or declared verified here.
internal static class OciManifestChecks
{
    internal static async Task<(byte[] Bytes, OciManifestDescription Description)> ResponseAsync(
        HttpResponseMessage response, string digest, CancellationToken cancellationToken)
    {
        OciWirePolicy.RequireDigest(digest);
        var bytes = await OciWirePolicy.ReadBodyAsync(response, true, cancellationToken);
        if (response.Headers.TryGetValues("Docker-Content-Digest", out var declared) &&
            !declared.SequenceEqual(new[] { digest }, StringComparer.Ordinal)) throw OciWirePolicy.Error("oci-digest");
        return (bytes, Read(bytes, digest, response.Content.Headers.ContentType!.MediaType!));
    }

    internal static OciManifestDescription Read(ReadOnlyMemory<byte> bytes, string digest, string mediaType)
    {
        OciWirePolicy.RequireDigest(digest);
        if (bytes.Length is < 1 or > 4 * 1024 * 1024) throw OciWirePolicy.Error("oci-body");
        if ("sha256:" + Cp6DeterministicJson.Sha256Hex(bytes.Span) != digest) throw OciWirePolicy.Error("oci-digest");
        try
        {
            if (mediaType is not (OciWirePolicy.OciManifest or OciWirePolicy.DockerManifest))
                throw OciWirePolicy.Error("oci-manifest");
            var root = GitHubApiJson.Parse(bytes);
            if (root.GetProperty("schemaVersion").GetInt32() != 2 || root.GetProperty("mediaType").GetString() != mediaType ||
                root.TryGetProperty("artifactType", out _) || root.TryGetProperty("subject", out _) ||
                root.TryGetProperty("manifests", out _)) throw OciWirePolicy.Error("oci-manifest");
            var config = Descriptor(root.GetProperty("config"));
            var oci = mediaType == OciWirePolicy.OciManifest;
            if (config.MediaType != (oci ? "application/vnd.oci.image.config.v1+json" : "application/vnd.docker.container.image.v1+json"))
                throw OciWirePolicy.Error("oci-manifest");
            var layers = root.GetProperty("layers");
            if (layers.ValueKind != JsonValueKind.Array || layers.GetArrayLength() is < 1 or > 128)
                throw OciWirePolicy.Error("oci-manifest");
            var descriptions = layers.EnumerateArray().Select(Descriptor).ToArray();
            foreach (var layer in descriptions)
                if (oci ? layer.MediaType is not ("application/vnd.oci.image.layer.v1.tar" or
                    "application/vnd.oci.image.layer.v1.tar+gzip" or "application/vnd.oci.image.layer.v1.tar+zstd") :
                    layer.MediaType != "application/vnd.docker.image.rootfs.diff.tar.gzip")
                    throw OciWirePolicy.Error("oci-manifest");
            return new(mediaType, config, Array.AsReadOnly(descriptions));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw OciWirePolicy.Error("oci-manifest");
        }
    }

    private static OciDescriptor Descriptor(JsonElement descriptor)
    {
        var result = new OciDescriptor(descriptor.GetProperty("mediaType").GetString()!,
            descriptor.GetProperty("digest").GetString()!, descriptor.GetProperty("size").GetInt64());
        OciWirePolicy.RequireDigest(result.Digest);
        if (string.IsNullOrEmpty(result.MediaType) || result.ByteLength < 1 ||
            descriptor.TryGetProperty("urls", out _) || descriptor.TryGetProperty("data", out _))
            throw OciWirePolicy.Error("oci-manifest");
        return result;
    }
}

internal sealed record OciDescriptor(string MediaType, string Digest, long ByteLength);
internal sealed record OciManifestDescription(string MediaType, OciDescriptor Config, IReadOnlyList<OciDescriptor> Layers);
```

### OciImageSource.cs

```csharp
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Uses the existing intended consumer's GitHub Packages read credential; no new credential authority.
// Only two fixed GHCR GETs are permitted. Returned metadata never confers deployment/candidate acceptance.
internal static class OciImageSource
{
    internal static async Task<DownloadedOciManifest> ReadAsync(string digest, string readToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OciWirePolicy.RequireDigest(digest);
        using var tokenRequest = OciWirePolicy.TokenRequest(readToken);
        using var client = new HttpClient(GitHubWirePolicy.CreateHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            using var tokenResponse = await client.SendAsync(tokenRequest, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            var token = OciWirePolicy.PullToken(await OciWirePolicy.ReadBodyAsync(tokenResponse, false, deadline.Token));
            using var manifestRequest = OciWirePolicy.ManifestRequest(digest, token);
            using var response = await client.SendAsync(manifestRequest, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            var result = await OciManifestChecks.ResponseAsync(response, digest, deadline.Token);
            return new(result.Bytes, digest, result.Description, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw OciWirePolicy.Error("oci-timeout");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw OciWirePolicy.Error("oci-transfer");
        }
    }
}

internal sealed class DownloadedOciManifest
{
    private readonly byte[] _bytes;

    internal DownloadedOciManifest(byte[] bytes, string digest, OciManifestDescription description, DateTimeOffset retrievedAtUtc)
    {
        _bytes = bytes.ToArray();
        Digest = digest;
        Description = description;
        RetrievedAtUtc = retrievedAtUtc;
    }

    internal string Repository => S06ReleaseIdentity.ImageRepository;
    internal string Digest { get; }
    internal OciManifestDescription Description { get; }
    internal DateTimeOffset RetrievedAtUtc { get; }
    internal byte[] CopyBytes() => _bytes.ToArray();
}
```

- [x] Run focused tests and the complete suite using actual cosign, formal package, feed and GitHub read inputs.

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~OciImageSourceTests
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore
dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --verify-no-changes --no-restore
```

Expected: 60 focused and 818 total pass, no skips. The live unavailable digest produces a sanitized HTTP failure. No actual positive image availability is claimed.

## Task 3: Review and checkpoint

- [x] Compare all four implementation/test files with this plan, review the full five-file scope, and verify existing runtime/workflows/trust/locks remain unchanged.
- [x] Record the actual red/green and hygiene results, stage only these files, and commit normally.

```powershell
git diff --check
git add -- docs/superpowers/plans/2026-09-08-p10-s06-oci-manifest-read.md tools/p10/ReleaseVerifier/OciWirePolicy.cs tools/p10/ReleaseVerifier/OciManifestChecks.cs tools/p10/ReleaseVerifier/OciImageSource.cs tools/p10/ReleaseVerifier.Tests/OciImageSourceTests.cs
git diff --cached --check
git commit -m "feat(p10): read approved OCI manifests by exact digest"
```

- [ ] Verify the actual protected-workflow image and complete all S06 evidence/publication/audit gates before claiming P10 complete.

## Observed results (2026-09-08 UTC)

- Red TRX: 60 failures, all 60 caused by NotImplementedException, zero unexpected failures or skipped/not-executed tests.
- Green: 60/60 focused tests passed, including actual GHCR failure for the unavailable all-zero digest. No successful real image read is claimed.
- Complete Release suite: 818/818 passed, zero skips, no build warnings/errors; full format verification exited 0. Four-file exact plan parity, five-file scope review and hygiene checks passed.
- During validation, remote main advanced independently to 80cd15deb05922c63fb8849b6142ba7e98331cf3 through CRM disclosure PR #84. Its nine changed files do not overlap this module; preserve that work and incorporate the current main before S06 landing. No R2 workflow, trust policy, package lock, runtime or remote artifact was modified by this module.
