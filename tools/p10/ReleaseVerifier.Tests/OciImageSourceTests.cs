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
