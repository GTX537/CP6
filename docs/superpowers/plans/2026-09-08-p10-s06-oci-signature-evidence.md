# P10 S06 OCI Signature Evidence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans sequentially in this task. The user selected no delegation.

**Goal:** Preserve the exact native cosign OCI bundle inside typed public S06 evidence and bind it to independent OCI authentication.

**Architecture:** The existing AuthenticatedOciSignature and checksum-pinned cosign process retain all signature, signer-policy and native claim checks. The new wrapper carries original bundle bytes as canonical 32-KiB base64 chunks and binds hashes, identity, key, policy and signed time to the common S06 envelope. Extracting those bytes is explicitly not authentication; the normal caller must authenticate with compiled trust, then require that exact proof before checking the actual completed workflow.

**Tech Stack:** .NET SDK 8.0.424, CP6.Platform.Release [0.10.1], cosign 3.1.3, Sigstore bundle v0.3, native DSSE, canonical S06 in-toto envelope, xUnit.

---

## Scope and acceptance boundary

The wrapper enforces a 2-MiB raw bundle cap so base64 expansion stays within the existing 4-MiB canonical document limit. Every chunk is at most 32768 bytes (43692 encoded characters), below the existing 65536-character scalar limit; all non-final chunks have exact full size. This tightens this payload's bounds and does not relax any common JSON, transport or cosign policy.

Create accepts an actual authenticated signature object. ReadBundleForAuthentication returns untrusted original bytes only. The normal full verifier must call AuthenticatedOciSignature.AuthenticateAsync, which loads compiled trust and current time, then RequireAuthenticated, then RequireCompletedWorkflow with the actual completed validation run and required jobs. This module adds no alternate trust-policy input or CLI bypass.

Tests use in-memory ephemeral P-256 keys and the actual pinned cosign binary. Their image digest and producer IDs are unit vectors, not an image publication or successful GitHub run. The normal compiled-trust entry must reject those ephemeral signers. A corrupted signature with recomputed wrapper hashes must still fail actual cosign authentication. Larger whitespace-padded bundles are codec boundary vectors only, not claimed upstream production output.

## File map

- Create tools/p10/ReleaseVerifier/OciSignatureEvidence.cs: bounded wrapper, untrusted raw extraction and exact independently authenticated proof binding.
- Create tools/p10/ReleaseVerifier.Tests/OciSignatureEvidenceTests.cs: 21 tests using real ephemeral cryptography, explicit non-authentication checks and byte/size boundaries.
- Create this plan. No changes to compiled trust, existing signature implementation, old R2 or deployment.

## Task 1: Establish the failing behavior

- [x] Add this exact test file first.

### tools/p10/ReleaseVerifier.Tests/OciSignatureEvidenceTests.cs

```csharp
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
```

- [x] Add only these throwing entry declarations, then run all 21 cases. Expected: every case fails with NotImplementedException, zero skips and no compile errors or missing actual cosign.

### Throwing declarations

```csharp
namespace CP6.P10.ReleaseVerifier;

internal static class OciSignatureEvidence
{
    internal static byte[] Create(AuthenticatedOciSignature signature) => throw new NotImplementedException();
    internal static byte[] ReadBundleForAuthentication(ReadOnlyMemory<byte> bytes, GitHubWorkflowIdentity producer,
        DateTimeOffset cutoff, string digest) => throw new NotImplementedException();
    internal static S06Attestation RequireAuthenticated(ReadOnlyMemory<byte> bytes, GitHubWorkflowIdentity producer,
        DateTimeOffset cutoff, string digest, AuthenticatedOciSignature independentlyAuthenticatedSignature) =>
        throw new NotImplementedException();
}
```

Run from tools/p10 using the established SDK and checksum-pinned P10_COSIGN_PATH:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~OciSignatureEvidenceTests --logger "trx;LogFileName=oci-evidence-red.trx" --results-directory ../../artifacts/p10/oci-evidence-red
```

## Task 2: Implement the typed wrapper

- [x] Replace the throwing declarations with this complete implementation.

### tools/p10/ReleaseVerifier/OciSignatureEvidence.cs

```csharp
using System.Text.Json;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Native signed bytes stay native. The public wrapper never substitutes for independently authenticated OCI proof.
internal static class OciSignatureEvidence
{
    private const int ChunkBytes = 32768;
    private const int MaximumBundleBytes = 2 * 1024 * 1024;

    internal static byte[] Create(AuthenticatedOciSignature signature)
    {
        RequireProducer(signature.Workflow);
        var bundle = signature.CopyBundle();
        Require(bundle.Length is > 0 and <= MaximumBundleBytes, "s06-oci-size");
        var chunks = new List<string>();
        for (var offset = 0; offset < bundle.Length; offset += ChunkBytes)
            chunks.Add(Convert.ToBase64String(bundle.AsSpan(offset, Math.Min(ChunkBytes, bundle.Length - offset))));
        var details = JsonSerializer.SerializeToElement(new
        {
            imageRepository = signature.Repository,
            imageDigest = signature.Digest,
            signerKeyId = signature.SignerKeyId,
            policyVersion = signature.PolicyVersion,
            signedAtUtc = FormatTime(signature.SignedAtUtc),
            bundleSha256 = signature.BundleSha256,
            payloadSha256 = signature.PayloadSha256,
            bundleByteLength = bundle.Length,
            bundleChunks = chunks,
            verificationMode = "PinnedKeyOffline"
        });
        var bytes = S06InToto.Create("OciSignature", signature.Workflow, DateTimeOffset.UtcNow, details, signature.Digest);
        _ = RequireAuthenticated(bytes, signature.Workflow, DateTimeOffset.UtcNow, signature.Digest, signature);
        return bytes;
    }

    // Untrusted data only. The caller must run AuthenticatedOciSignature.AuthenticateAsync with compiled trust.
    internal static byte[] ReadBundleForAuthentication(ReadOnlyMemory<byte> bytes, GitHubWorkflowIdentity producer,
        DateTimeOffset cutoff, string digest) => Parse(bytes, producer, cutoff, digest).Bundle.ToArray();

    internal static S06Attestation RequireAuthenticated(ReadOnlyMemory<byte> bytes, GitHubWorkflowIdentity producer,
        DateTimeOffset cutoff, string digest, AuthenticatedOciSignature independentlyAuthenticatedSignature)
    {
        var parsed = Parse(bytes, producer, cutoff, digest);
        var signature = independentlyAuthenticatedSignature;
        var details = parsed.Statement.Details;
        Require(signature.Workflow == producer && signature.Digest == digest &&
            signature.Repository == S06ReleaseIdentity.ImageRepository &&
            signature.SignerKeyId == Text(details, "signerKeyId") &&
            signature.PolicyVersion == details.GetProperty("policyVersion").GetInt32() &&
            signature.SignedAtUtc == Time(details, "signedAtUtc") &&
            signature.BundleSha256 == Text(details, "bundleSha256") &&
            signature.PayloadSha256 == Text(details, "payloadSha256") &&
            signature.CopyBundle().AsSpan().SequenceEqual(parsed.Bundle), "s06-oci-proof");
        return parsed.Statement;
    }

    private static Parsed Parse(ReadOnlyMemory<byte> bytes, GitHubWorkflowIdentity producer, DateTimeOffset cutoff, string digest)
    {
        var statement = S06InToto.Read(bytes, "OciSignature", producer, cutoff, digest);
        try
        {
            var details = statement.Details;
            Exact(details, "imageRepository", "imageDigest", "signerKeyId", "policyVersion", "signedAtUtc",
                "bundleSha256", "payloadSha256", "bundleByteLength", "bundleChunks", "verificationMode");
            Require(Text(details, "imageRepository") == S06ReleaseIdentity.ImageRepository &&
                Text(details, "imageDigest") == digest && Text(details, "verificationMode") == "PinnedKeyOffline",
                "s06-oci-identity");
            var length = details.GetProperty("bundleByteLength").GetInt32();
            Require(length is > 0 and <= MaximumBundleBytes, "s06-oci-size");
            var chunks = details.GetProperty("bundleChunks");
            Require(chunks.ValueKind == JsonValueKind.Array && chunks.GetArrayLength() == (length + ChunkBytes - 1) / ChunkBytes,
                "s06-oci-chunks");
            var bundle = new byte[length];
            for (var index = 0; index < chunks.GetArrayLength(); index++)
            {
                var size = Math.Min(ChunkBytes, length - index * ChunkBytes);
                var encoded = chunks[index].GetString()!;
                Require(encoded.Length == 4 * ((size + 2) / 3), "s06-oci-chunks");
                var part = Convert.FromBase64String(encoded);
                Require(part.Length == size && Convert.ToBase64String(part) == encoded, "s06-oci-chunks");
                part.CopyTo(bundle, index * ChunkBytes);
            }
            Require(Cp6DeterministicJson.Sha256Hex(bundle) == Text(details, "bundleSha256"), "s06-oci-bundle");
            var payload = OciBundlePayload.Read(bundle);
            Require(Cp6DeterministicJson.Sha256Hex(payload) == Text(details, "payloadSha256"), "s06-oci-bundle");
            var native = GitHubApiJson.Parse(payload);
            var selectors = OciSignatureProfile.Selectors(native, statement.CreatedAtUtc);
            OciSignatureProfile.RequireClaims(native, digest, producer, selectors.KeyId, selectors.Version, selectors.SignedAtUtc);
            Require(Text(details, "signerKeyId") == selectors.KeyId &&
                details.GetProperty("policyVersion").GetInt32() == selectors.Version &&
                Time(details, "signedAtUtc") == selectors.SignedAtUtc, "s06-oci-selector");
            return new(statement, bundle);
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("s06-oci-shape"); }
    }

    private sealed record Parsed(S06Attestation Statement, byte[] Bundle);
}
```

- [x] Run focused and full Release tests with all existing actual inputs, then verify formatting. Expected: 21 focused and 1075 total cases pass, zero skips.

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~OciSignatureEvidenceTests
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore
dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --verify-no-changes --no-restore
```

## Task 3: Review and checkpoint

- [x] Compare both implementation/test files with the complete code blocks and review the three-file scope for byte preservation, bounded allocation, proof separation and secret/path safety.
- [ ] Record actual red/green/full/format results, stage precisely this scope and commit with explicit native exit checks.

```powershell
git diff --check
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
git add -- docs/superpowers/plans/2026-09-08-p10-s06-oci-signature-evidence.md tools/p10/ReleaseVerifier/OciSignatureEvidence.cs tools/p10/ReleaseVerifier.Tests/OciSignatureEvidenceTests.cs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
git diff --cached --check
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
git commit -m "feat(p10): preserve and bind OCI signature evidence"
```

- [ ] Complete image provenance, full acceptance, actual protected workflows, immutable publication and pre/post audit before claiming P10 completion.

## Observed component verification

On 2026-09-08 all 21 cases first failed with the deliberate NotImplementedException entries (21 expected failures, zero unexpected failures or skips), then all 21 passed using real ephemeral DSSE signatures and the checksum-pinned cosign binary. The complete Release suite passed 1075/1075 with zero skips in 47 seconds. Formatting found three missing line breaks in test-only initializers; after those whitespace-only corrections, format verification and the 21 focused cases passed again. The two final files exactly match the plan blocks, and the three-file scope passed whitespace and sensitive/path checks.

The negative crypto case demonstrated that rebinding wrapper hashes does not authenticate a corrupted signature. Normal compiled trust rejected the ephemeral signer. No formal OCI image, protected workflow, R2 object or Locator was created, and P10 remains open.
