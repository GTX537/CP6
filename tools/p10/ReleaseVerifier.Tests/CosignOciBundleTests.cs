using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Real ephemeral P-256 DSSE signatures exercise cosign, never formal OCI acceptance.
// No image is built, uploaded, downloaded or claimed to exist by these tests.
public sealed class CosignOciBundleTests
{
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string PayloadType = "application/vnd.in-toto+json";
    private static CosignBlobVerifier Verifier() => new(Environment.GetEnvironmentVariable("P10_COSIGN_PATH") ??
        throw new InvalidOperationException("Checksum-pinned cosign v3.1.3-cp6.1 is required."));
    private static CosignBlobVerifier MissingVerifier() => new(Path.Combine(Path.GetTempPath(), "not-a-cosign-tool"));

    [Fact]
    public async Task Actual_cosign_authenticates_DSSE_and_returns_the_exact_unreserialized_payload()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var payload = Payload();
        var bundle = Bundle(signer, payload);
        var verified = await Verifier().VerifyOciBundleAsync(Digest, bundle, PublicKey(signer));
        Assert.Equal(payload, verified);
        Assert.Contains("\n", Encoding.UTF8.GetString(verified));
        Array.Fill(bundle, (byte)0);
        Assert.Equal(payload, verified);
    }

    [Fact]
    public async Task Actual_cosign_rejects_a_valid_signature_by_a_different_key()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Equal("cosign-signature", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Verifier().VerifyOciBundleAsync(Digest, Bundle(signer, Payload()), PublicKey(other)))).Code);
    }

    [Theory]
    [InlineData("body")]
    [InlineData("signature")]
    [InlineData("wrong-subject")]
    [InlineData("wrong-predicate")]
    public async Task Actual_cosign_rejects_signature_digest_or_predicate_substitution(string mutation)
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var payload = Payload(node =>
        {
            if (mutation == "wrong-subject") node["subject"]![0]!["digest"]!["sha256"] = new string('b', 64);
            if (mutation == "wrong-predicate") node["predicateType"] = "https://example.invalid/not-an-image-signature";
        });
        var bundle = JsonNode.Parse(Bundle(signer, payload))!;
        if (mutation == "body")
            bundle["dsseEnvelope"]!["payload"] = Convert.ToBase64String(
                Payload(node => node["subject"]![0]!["annotations"]!["fixture"] = "changed"));
        if (mutation == "signature")
            bundle["dsseEnvelope"]!["signatures"]![0]!["sig"] = Convert.ToBase64String(new byte[72]);
        Assert.Equal("cosign-signature", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Verifier().VerifyOciBundleAsync(Digest, JsonSerializer.SerializeToUtf8Bytes(bundle), PublicKey(signer)))).Code);
    }

    [Theory]
    [InlineData("media")]
    [InlineData("payload-type")]
    [InlineData("payload-base64")]
    [InlineData("empty-payload")]
    [InlineData("payload-json")]
    [InlineData("payload-duplicate")]
    [InlineData("no-signature")]
    [InlineData("multiple-signatures")]
    [InlineData("blob-signature")]
    [InlineData("envelope-duplicate")]
    public async Task Ambiguous_or_wrong_bundle_shapes_fail_before_starting_cosign(string mutation)
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var bundle = JsonNode.Parse(Bundle(signer, Payload()))!;
        var envelope = bundle["dsseEnvelope"]!;
        if (mutation == "media") bundle["mediaType"] = "application/json";
        if (mutation == "payload-type") envelope["payloadType"] = "text/plain";
        if (mutation == "payload-base64") envelope["payload"] = "invalid base64";
        if (mutation == "empty-payload") envelope["payload"] = "";
        if (mutation == "payload-json") envelope["payload"] = Convert.ToBase64String("not-json"u8);
        if (mutation == "payload-duplicate")
            envelope["payload"] = Convert.ToBase64String("{\"subject\":[],\"subject\":[]}"u8);
        if (mutation == "no-signature") envelope["signatures"] = new JsonArray();
        if (mutation == "multiple-signatures")
            envelope["signatures"]!.AsArray().Add(envelope["signatures"]![0]!.DeepClone());
        if (mutation == "blob-signature") bundle["messageSignature"] = new JsonObject();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(bundle);
        if (mutation == "envelope-duplicate")
        {
            var json = Encoding.UTF8.GetString(bytes);
            bytes = Encoding.UTF8.GetBytes("{\"dsseEnvelope\":{}," + json[1..]);
        }
        Assert.Equal("cosign-bundle", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            MissingVerifier().VerifyOciBundleAsync(Digest, bytes, PublicKey(signer)))).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sha256:abcd")]
    [InlineData("SHA256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("sha256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n")]
    [InlineData("ghcr.io/gtx537/cp6-p10-verifier:latest")]
    public async Task Only_an_exact_lowercase_digest_can_reach_the_process(string digest)
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Equal("cosign-digest", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            MissingVerifier().VerifyOciBundleAsync(digest, Bundle(signer, Payload()), PublicKey(signer)))).Code);
    }

    [Fact]
    public async Task Locator_purpose_cannot_be_used_to_authenticate_an_OCI_bundle()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Equal("cosign-key", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            MissingVerifier().VerifyOciBundleAsync(Digest, Bundle(signer, Payload()),
                PublicKey(signer) with { Purpose = "candidate-locator" }))).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4194305)]
    public async Task Bundle_size_is_checked_before_parsing_or_process_start(int length)
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Equal("cosign-input", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            MissingVerifier().VerifyOciBundleAsync(Digest, new byte[length], PublicKey(signer)))).Code);
    }

    [Fact]
    public async Task Precancelled_bundle_verification_never_starts_cosign()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MissingVerifier().VerifyOciBundleAsync(
            Digest, Bundle(signer, Payload()), PublicKey(signer), cancellation.Token));
    }

    private static Cp6PinnedTrustKey PublicKey(ECDsa key)
    {
        var spki = key.ExportSubjectPublicKeyInfo();
        return new("sha256:" + Cp6DeterministicJson.Sha256Hex(spki), "oci",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2028-01-01T00:00:00Z"),
            PemEncoding.WriteString("PUBLIC KEY", spki), null, null);
    }

    private static byte[] Payload(Action<JsonObject>? mutate = null)
    {
        var node = new JsonObject
        {
            ["_type"] = "https://in-toto.io/Statement/v1",
            ["subject"] = new JsonArray(new JsonObject
            {
                ["digest"] = new JsonObject { ["sha256"] = Digest[7..] },
                ["annotations"] = new JsonObject { ["fixture"] = "not formal release evidence" }
            }),
            ["predicateType"] = "https://sigstore.dev/cosign/sign/v1",
            ["predicate"] = new JsonObject()
        };
        mutate?.Invoke(node);
        return JsonSerializer.SerializeToUtf8Bytes(node, new JsonSerializerOptions { WriteIndented = true });
    }

    private static byte[] Bundle(ECDsa key, byte[] payload)
    {
        var prefix = Encoding.UTF8.GetBytes("DSSEv1 " +
            Encoding.UTF8.GetByteCount(PayloadType).ToString(CultureInfo.InvariantCulture) + " " + PayloadType + " " +
            payload.Length.ToString(CultureInfo.InvariantCulture) + " ");
        var signature = key.SignData(prefix.Concat(payload).ToArray(), HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            mediaType = "application/vnd.dev.sigstore.bundle.v0.3+json",
            verificationMaterial = new
            {
                publicKey = new { hint = Convert.ToBase64String(SHA256.HashData(key.ExportSubjectPublicKeyInfo())) }
            },
            dsseEnvelope = new
            {
                payloadType = PayloadType,
                payload = Convert.ToBase64String(payload),
                signatures = new[] { new { sig = Convert.ToBase64String(signature) } }
            }
        });
    }
}
