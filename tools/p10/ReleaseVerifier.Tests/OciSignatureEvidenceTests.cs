using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Real ephemeral DSSE signatures verified by the pinned cosign binary; no formal image or candidate is created.
public sealed class OciSignatureEvidenceTests
{
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly DateTimeOffset Signed = DateTimeOffset.Parse("2026-09-07T10:00:00Z", CultureInfo.InvariantCulture);
    private static GitHubWorkflowIdentity Producer => new("GTX537/CP6", S06ReleaseIdentity.ValidationPath,
        new string('b', 40), 12345, 1, new string('c', 40));
    private static readonly Lazy<Task<Sample>> Actual = new(CreateSampleAsync);

    [Fact]
    public async Task The_original_bundle_survives_the_public_envelope_and_matches_an_actual_signature_proof()
    {
        var sample = await Actual.Value;
        var bundle = OciSignatureEvidence.ReadBundleForAuthentication(sample.Envelope, Producer, DateTimeOffset.UtcNow, Digest);
        Assert.Equal(sample.Signature.CopyBundle(), bundle);
        var statement = OciSignatureEvidence.RequireAuthenticated(sample.Envelope, Producer, DateTimeOffset.UtcNow, Digest, sample.Signature);
        Assert.Equal("OciSignature", statement.Kind);
        Assert.Equal(sample.Signature.BundleSha256, statement.Details.GetProperty("bundleSha256").GetString());
        Assert.Equal(sample.Envelope, Cp6DeterministicJson.Canonicalize(sample.Envelope));
        Array.Fill(bundle, (byte)0);
        Assert.Equal(sample.Signature.CopyBundle(),
            OciSignatureEvidence.ReadBundleForAuthentication(sample.Envelope, Producer, DateTimeOffset.UtcNow, Digest));
    }

    [Theory]
    [InlineData("repository")]
    [InlineData("digest")]
    [InlineData("signer")]
    [InlineData("policy")]
    [InlineData("signed-time")]
    [InlineData("bundle-hash")]
    [InlineData("payload-hash")]
    [InlineData("length")]
    [InlineData("oversized-length")]
    [InlineData("missing-chunk")]
    [InlineData("extra-chunk")]
    [InlineData("chunk-base64")]
    [InlineData("chunk-spacing")]
    [InlineData("mode")]
    [InlineData("extra-field")]
    public async Task Changed_public_wrapper_fields_fail_before_any_authentication_claim(string mutation)
    {
        var sample = await Actual.Value;
        var root = JsonNode.Parse(sample.Envelope)!;
        var details = root["predicate"]!["details"]!;
        if (mutation == "repository") details["imageRepository"] = "ghcr.io/gtx537/cp6-api";
        if (mutation == "digest") details["imageDigest"] = "sha256:" + new string('d', 64);
        if (mutation == "signer") details["signerKeyId"] = "sha256:" + new string('d', 64);
        if (mutation == "policy") details["policyVersion"] = 2;
        if (mutation == "signed-time") details["signedAtUtc"] = S06InToto.FormatTime(Signed.AddSeconds(1));
        if (mutation == "bundle-hash") details["bundleSha256"] = new string('d', 64);
        if (mutation == "payload-hash") details["payloadSha256"] = new string('d', 64);
        if (mutation == "length") details["bundleByteLength"] = 1;
        if (mutation == "oversized-length") details["bundleByteLength"] = 2097153;
        if (mutation == "missing-chunk") details["bundleChunks"]!.AsArray().RemoveAt(0);
        if (mutation == "extra-chunk") details["bundleChunks"]!.AsArray().Add("");
        if (mutation == "chunk-base64") details["bundleChunks"]![0] = "!not-base64!";
        if (mutation == "chunk-spacing") details["bundleChunks"]![0] = details["bundleChunks"]![0]!.GetValue<string>() + "\n";
        if (mutation == "mode") details["verificationMode"] = "Keyless";
        if (mutation == "extra-field") details["unreviewed"] = true;
        Assert.Throws<Cp6ReleaseContractException>(() =>
            OciSignatureEvidence.ReadBundleForAuthentication(Canonical(root), Producer, DateTimeOffset.UtcNow, Digest));
    }

    [Fact]
    public async Task A_different_actual_signature_cannot_satisfy_the_wrapper_binding()
    {
        var sample = await Actual.Value;
        var other = await SignAsync();
        Assert.Equal("s06-oci-proof", Assert.Throws<Cp6ReleaseContractException>(() =>
            OciSignatureEvidence.RequireAuthenticated(sample.Envelope, Producer, DateTimeOffset.UtcNow, Digest, other.Signature)).Code);
    }

    [Fact]
    public async Task Decoding_does_not_grant_trust_to_an_ephemeral_signer()
    {
        var sample = await Actual.Value;
        var bundle = OciSignatureEvidence.ReadBundleForAuthentication(sample.Envelope, Producer, DateTimeOffset.UtcNow, Digest);
        Assert.Equal("trust-key", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            AuthenticatedOciSignature.AuthenticateAsync(Digest, Producer, bundle, Verifier()))).Code);
    }

    [Fact]
    public async Task Structurally_rebound_but_corrupt_crypto_is_still_rejected_by_actual_cosign()
    {
        var sample = await Actual.Value;
        var bundle = JsonNode.Parse(sample.Signature.CopyBundle())!;
        var signature = Convert.FromBase64String(bundle["dsseEnvelope"]!["signatures"]![0]!["sig"]!.GetValue<string>());
        signature[signature.Length - 1] ^= 1;
        bundle["dsseEnvelope"]!["signatures"]![0]!["sig"] = Convert.ToBase64String(signature);
        var raw = JsonSerializer.SerializeToUtf8Bytes(bundle);
        var root = JsonNode.Parse(sample.Envelope)!;
        var details = root["predicate"]!["details"]!;
        details["bundleChunks"] = new JsonArray(Convert.ToBase64String(raw));
        details["bundleSha256"] = Cp6DeterministicJson.Sha256Hex(raw);
        details["bundleByteLength"] = raw.Length;
        var extracted = OciSignatureEvidence.ReadBundleForAuthentication(Canonical(root), Producer, DateTimeOffset.UtcNow, Digest);
        Assert.Equal("cosign-signature", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            AuthenticatedOciSignature.AuthenticateWithPolicyAsync(sample.Policy, DateTimeOffset.UtcNow,
                Digest, Producer, extracted, Verifier()))).Code);
        Assert.Throws<Cp6ReleaseContractException>(() =>
            OciSignatureEvidence.RequireAuthenticated(Canonical(root), Producer, DateTimeOffset.UtcNow, Digest, sample.Signature));
    }

    [Fact]
    public async Task Multiple_chunks_preserve_exact_native_bytes_without_expanding_the_canonical_string_limit()
    {
        var signed = await SignAsync(40000);
        var bytes = OciSignatureEvidence.Create(signed.Signature);
        Assert.Equal(2, JsonNode.Parse(bytes)!["predicate"]!["details"]!["bundleChunks"]!.AsArray().Count);
        Assert.Equal(signed.Signature.CopyBundle(),
            OciSignatureEvidence.ReadBundleForAuthentication(bytes, Producer, DateTimeOffset.UtcNow, Digest));
    }

    [Fact]
    public async Task A_large_authentic_bundle_is_rejected_before_base64_expansion()
    {
        var signed = await SignAsync(2097153);
        Assert.Equal("s06-oci-size", Assert.Throws<Cp6ReleaseContractException>(() =>
            OciSignatureEvidence.Create(signed.Signature)).Code);
    }

    private static async Task<Sample> CreateSampleAsync()
    {
        var signed = await SignAsync();
        return new(signed.Signature, signed.Policy, OciSignatureEvidence.Create(signed.Signature));
    }

    private static async Task<(AuthenticatedOciSignature Signature, Cp6PinnedTrustPolicy Policy)> SignAsync(int minimumLength = 0)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var spki = key.ExportSubjectPublicKeyInfo();
        var keyId = "sha256:" + Cp6DeterministicJson.Sha256Hex(spki);
        var policyNode = JsonNode.Parse(VerifierTrust.Load().ValidatedDocument.CanonicalUtf8)!;
        policyNode["keys"] = new JsonArray(new JsonObject
        {
            ["keyId"] = keyId,
            ["purpose"] = "oci",
            ["validFromUtc"] = "2026-01-01T00:00:00.000Z",
            ["validUntilUtc"] = "2028-01-01T00:00:00.000Z",
            ["publicKey"] = PemEncoding.WriteString("PUBLIC KEY", spki)
        });
        var policy = Cp6PinnedTrustPolicy.Parse(Canonical(policyNode));
        const string payloadType = "application/vnd.in-toto+json";
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            _type = "https://in-toto.io/Statement/v1",
            subject = new[] { new { digest = new { sha256 = Digest[7..] },
                annotations = OciSignatureProfile.Annotations(Producer, keyId, 1, Signed) } },
            predicateType = OciBundlePayload.PredicateType,
            predicate = new { }
        });
        var prefix = Encoding.UTF8.GetBytes("DSSEv1 " + payloadType.Length.ToString(CultureInfo.InvariantCulture) + " " +
            payloadType + " " + payload.Length.ToString(CultureInfo.InvariantCulture) + " ");
        var bundle = JsonSerializer.SerializeToUtf8Bytes(new
        {
            mediaType = "application/vnd.dev.sigstore.bundle.v0.3+json",
            verificationMaterial = new { publicKey = new { hint = Convert.ToBase64String(SHA256.HashData(spki)) } },
            dsseEnvelope = new
            {
                payloadType,
                payload = Convert.ToBase64String(payload),
                signatures = new[] { new { sig = Convert.ToBase64String(key.SignData(prefix.Concat(payload).ToArray(),
                    HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)) } }
            }
        });
        // Whitespace is only a codec boundary vector; no claim that cosign normally produces large bundles.
        if (minimumLength > bundle.Length) bundle = bundle.Concat(Enumerable.Repeat((byte)' ', minimumLength - bundle.Length)).ToArray();
        var signature = await AuthenticatedOciSignature.AuthenticateWithPolicyAsync(policy, DateTimeOffset.UtcNow,
            Digest, Producer, bundle, Verifier());
        return (signature, policy);
    }

    private sealed record Sample(AuthenticatedOciSignature Signature, Cp6PinnedTrustPolicy Policy, byte[] Envelope);
    private static byte[] Canonical(JsonNode root) =>
        Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(root));
    private static CosignBlobVerifier Verifier() => new(Environment.GetEnvironmentVariable("P10_COSIGN_PATH") ??
        throw new InvalidOperationException("Checksum-pinned actual cosign is required."));
}
