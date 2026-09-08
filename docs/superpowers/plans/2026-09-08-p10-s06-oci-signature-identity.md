# P10 S06 Authenticated OCI Identity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans sequentially in this task. The user selected no delegation.

**Goal:** Bind the approved verifier image's real cosign signature to the current compiled trust policy and the independently expected validation invocation.

**Architecture:** Keep raw Sigstore bundle bytes unchanged. Select only key ID, policy version and signing time before invoking the checksum-pinned cosign verifier; authorize the exact signed repository/source/workflow/run/environment claims only after cryptography succeeds. A separate completed-observation binding checks that the same signature time falls inside a successful live workflow job and before the candidate cutoff; this does not itself fetch GitHub or accept a candidate.

**Tech Stack:** .NET 8.0.424, CP6.Platform.Release [0.10.1], cosign v3.1.3, xUnit, real ephemeral P-256 DSSE component signatures.

---

## Scope and provenance

Parent design sections 9 and 12 require current pinned OCI trust, exact image/workflow identity and completed validation before publication begins. The existing protected environment remains p10-platform-candidate. This module changes no Environment, Secret, R2 object, package, runtime, workflow, registry content or existing R2 gate.

The chosen exact profile matches [cosign v3.1.3 signDigestBundle](https://raw.githubusercontent.com/sigstore/cosign/v3.1.3/cmd/cosign/cli/sign/sign.go): in-toto Statement v1, one digest/annotations subject without a subject name, empty predicate, and https://sigstore.dev/cosign/sign/v1. The 12 cp6.* string annotations below are the S06 signer/reader agreement; they are public immutable identities only. No default repository identity is assumed from an absent subject name. Signed time is a pinned-key claim, not an RFC3161 timestamp; actual workflow/job observations constrain it. NuGet RFC3161 verification remains unchanged.

The normal entry loads compiled trust and current UTC; no CLI custom policy, public trust override, caller success boolean or historical-mode bypass is added. The internal core is the normal implementation seam used for deterministic policy regression with actual cryptography. Its result authenticates signed claims only. It cannot authorize publication or consumption without real manifest availability, completed live workflow, SBOM/scan/provenance and the rest of the candidate graph. No missing real image/run is replaced with these unit vectors.

## File map

- Create tools/p10/ReleaseVerifier/OciSignatureProfile.cs: exact annotations, three bounded selectors and post-crypto claims.
- Create tools/p10/ReleaseVerifier/AuthenticatedOciSignature.cs: compiled current-trust entry, real cosign verification, owned bundle result and completed-workflow binding.
- Create tools/p10/ReleaseVerifier.Tests/OciSignatureAuthenticationTests.cs: actual cryptographic tests, independent signed-claim vectors, current policy and temporal failures.
- Create this plan.

## Task 1: Observe failures

- [x] Add the complete test file first.

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Real ephemeral signatures and selected-field vectors, not formal image or GitHub acceptance.
public sealed class OciSignatureAuthenticationTests
{
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly DateTimeOffset Evaluation = DateTimeOffset.Parse("2026-09-08T12:00:00Z");
    private static readonly DateTimeOffset SignedAt = DateTimeOffset.Parse("2026-09-07T10:00:00Z");
    private static GitHubWorkflowIdentity Workflow => new("GTX537/CP6", S06ReleaseIdentity.ValidationPath,
        new string('b', 40), 12345, 1, new string('c', 40));
    private static CosignBlobVerifier Verifier() => new(Environment.GetEnvironmentVariable("P10_COSIGN_PATH") ??
        throw new InvalidOperationException("Checksum-pinned cosign v3.1.3 is required."));
    private static CosignBlobVerifier MissingVerifier() => new(Path.Combine(Path.GetTempPath(), "not-a-cosign-tool"));

    [Fact]
    public async Task Actual_signature_binds_all_claims_and_preserves_owned_bundle_bytes()
    {
        using var fixture = new Fixture();
        var payload = fixture.Payload();
        var bundle = fixture.Bundle(payload);
        var result = await Verify(fixture, bundle);
        Assert.Equal(Digest, result.Digest);
        Assert.Equal(S06ReleaseIdentity.ImageRepository, result.Repository);
        Assert.Equal(Workflow, result.Workflow);
        Assert.Equal(fixture.KeyId, result.SignerKeyId);
        Assert.Equal(1, result.PolicyVersion);
        Assert.Equal(SignedAt, result.SignedAtUtc);
        Assert.Equal(Cp6DeterministicJson.Sha256Hex(payload), result.PayloadSha256);
        Assert.Equal(Cp6DeterministicJson.Sha256Hex(bundle), result.BundleSha256);
        Assert.Equal(bundle, result.CopyBundle());
        Array.Fill(bundle, (byte)0);
        var copy = result.CopyBundle();
        Array.Fill(copy, (byte)0);
        Assert.Equal(result.BundleSha256, Cp6DeterministicJson.Sha256Hex(result.CopyBundle()));
        result.RequireCompletedWorkflow(Observation(), Evaluation);
    }

    [Fact]
    public async Task Normal_entry_rejects_a_valid_ephemeral_signature_not_in_compiled_trust()
    {
        using var fixture = new Fixture();
        var error = await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            AuthenticatedOciSignature.AuthenticateAsync(Digest, Workflow, fixture.Bundle(fixture.Payload()), MissingVerifier()));
        Assert.Equal("trust-key", error.Code);
    }

    [Theory]
    [InlineData("purpose", "trust-purpose")]
    [InlineData("revoked", "trust-revoked")]
    [InlineData("validity", "trust-validity")]
    [InlineData("minimum-version", "trust-policy-downgrade")]
    public async Task Current_policy_is_enforced_before_cosign(string mutation, string code)
    {
        using var fixture = new Fixture();
        var policy = fixture.PolicyNode();
        var key = policy["keys"]![0]!;
        if (mutation == "purpose") key["purpose"] = "candidate-locator";
        if (mutation == "validity") key["validUntilUtc"] = "2026-08-01T00:00:00.000Z";
        if (mutation == "revoked")
        {
            key["revokedAtUtc"] = "2026-09-08T11:00:00.000Z";
            key["revocationReason"] = "Regression fixture only";
        }
        if (mutation == "minimum-version")
        {
            policy["policyVersion"] = 2;
            policy["minimumAcceptedPolicyVersion"] = 2;
        }
        Assert.Equal(code, (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Verify(fixture, fixture.Bundle(fixture.Payload()), policy: fixture.Policy(policy), verifier: MissingVerifier()))).Code);
    }

    [Theory]
    [InlineData("future", "oci-signature-time")]
    [InlineData("fraction", "oci-signature-selector")]
    [InlineData("offset", "oci-signature-selector")]
    [InlineData("missing-key", "oci-signature-selector")]
    [InlineData("unknown-key", "trust-key")]
    [InlineData("uppercase-key", "oci-signature-selector")]
    [InlineData("numeric-version", "oci-signature-selector")]
    [InlineData("zero-version", "oci-signature-selector")]
    [InlineData("leading-zero", "oci-signature-selector")]
    [InlineData("future-version", "trust-policy-version")]
    public async Task Untrusted_selectors_fail_before_process_or_claim_authorization(string mutation, string code)
    {
        using var fixture = new Fixture();
        var payload = fixture.Payload(root =>
        {
            var annotations = root["subject"]![0]!["annotations"]!.AsObject();
            if (mutation == "future") annotations["cp6.signedAtUtc"] = "2026-09-09T00:00:00.000Z";
            if (mutation == "fraction") annotations["cp6.signedAtUtc"] = "2026-09-08T10:00:00Z";
            if (mutation == "offset") annotations["cp6.signedAtUtc"] = "2026-09-08T10:00:00.000+00:00";
            if (mutation == "missing-key") annotations.Remove("cp6.signerKeyId");
            if (mutation == "unknown-key") annotations["cp6.signerKeyId"] = "sha256:" + new string('e', 64);
            if (mutation == "uppercase-key") annotations["cp6.signerKeyId"] = fixture.KeyId.ToUpperInvariant();
            if (mutation == "numeric-version") annotations["cp6.trustPolicyVersion"] = 1;
            if (mutation == "zero-version") annotations["cp6.trustPolicyVersion"] = "0";
            if (mutation == "leading-zero") annotations["cp6.trustPolicyVersion"] = "01";
            if (mutation == "future-version") annotations["cp6.trustPolicyVersion"] = "2";
            annotations["cp6.imageRepository"] = "not-authorized-before-signature";
        });
        Assert.Equal(code, (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Verify(fixture, fixture.Bundle(payload), verifier: MissingVerifier()))).Code);
    }

    [Theory]
    [InlineData("cp6.imageRepository", "ghcr.io/gtx537/cp6-api")]
    [InlineData("cp6.sourceGitSha", "dddddddddddddddddddddddddddddddddddddddd")]
    [InlineData("cp6.workflowRepository", "GTX537/CP6.CRM")]
    [InlineData("cp6.workflowPath", ".github/workflows/p10-platform-candidate.yml")]
    [InlineData("cp6.workflowFileSha", "dddddddddddddddddddddddddddddddddddddddd")]
    [InlineData("cp6.runId", "12346")]
    [InlineData("cp6.runAttempt", "2")]
    [InlineData("cp6.runId", "012345")]
    [InlineData("cp6.environment", "none")]
    [InlineData("cp6.deployable", "true")]
    [InlineData("cp6.deployable", "False")]
    public async Task Real_signatures_cannot_authorize_substituted_identity_claims(string name, string value)
    {
        using var fixture = new Fixture();
        var payload = fixture.Payload(root => root["subject"]![0]!["annotations"]![name] = value);
        Assert.Equal("oci-signature-claims", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Verify(fixture, fixture.Bundle(payload)))).Code);
    }

    [Theory]
    [InlineData("extra-root")]
    [InlineData("extra-subject")]
    [InlineData("extra-digest")]
    [InlineData("extra-annotation")]
    [InlineData("missing-annotation")]
    [InlineData("typed-annotation")]
    [InlineData("predicate-content")]
    [InlineData("multiple-subjects")]
    public async Task Even_authentic_bytes_must_match_the_exact_cosign_and_S06_claim_profile(string mutation)
    {
        using var fixture = new Fixture();
        var payload = fixture.Payload(root =>
        {
            var subject = root["subject"]![0]!;
            if (mutation == "extra-root") root["unreviewed"] = true;
            if (mutation == "extra-subject") subject["name"] = S06ReleaseIdentity.ImageRepository;
            if (mutation == "extra-digest") subject["digest"]!["sha512"] = new string('a', 128);
            if (mutation == "extra-annotation") subject["annotations"]!["unreviewed"] = "value";
            if (mutation == "missing-annotation") subject["annotations"]!.AsObject().Remove("cp6.environment");
            if (mutation == "typed-annotation") subject["annotations"]!["cp6.deployable"] = false;
            if (mutation == "predicate-content") root["predicate"]!["unreviewed"] = true;
            if (mutation == "multiple-subjects") root["subject"]!.AsArray().Add(subject.DeepClone());
        });
        // The pinned cosign reader rejects an unknown Statement root member itself.
        var expected = mutation == "extra-root" ? "cosign-signature" : "oci-signature-claims";
        Assert.Equal(expected, (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Verify(fixture, fixture.Bundle(payload)))).Code);
    }

    [Fact]
    public async Task Invalid_crypto_precedes_authorization_of_wrong_claims()
    {
        using var fixture = new Fixture();
        var bundle = JsonNode.Parse(fixture.Bundle(fixture.Payload()))!;
        bundle["dsseEnvelope"]!["payload"] = Convert.ToBase64String(fixture.Payload(root =>
            root["subject"]![0]!["annotations"]!["cp6.environment"] = "wrong"));
        Assert.Equal("cosign-signature", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Verify(fixture, JsonSerializer.SerializeToUtf8Bytes(bundle)))).Code);
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("crm")]
    [InlineData("zero-run")]
    [InlineData("bad-sha")]
    public async Task Caller_expected_identity_is_validated_before_processing_the_bundle(string mutation)
    {
        using var fixture = new Fixture();
        var workflow = mutation switch
        {
            "publish" => Workflow with { WorkflowPath = S06ReleaseIdentity.PublicationPath },
            "crm" => Workflow with { Repository = "GTX537/CP6.CRM", WorkflowPath = ".github/workflows/crm-validation.yml" },
            "zero-run" => Workflow with { RunId = 0 },
            _ => Workflow with { CommitSha = "bad" }
        };
        await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            AuthenticatedOciSignature.AuthenticateWithPolicyAsync(fixture.Policy(), Evaluation,
                Digest, workflow, "{}"u8.ToArray(), MissingVerifier()));
    }

    [Theory]
    [InlineData("workflow")]
    [InlineData("before-run")]
    [InlineData("after-run")]
    [InlineData("after-cutoff")]
    [InlineData("outside-jobs")]
    [InlineData("no-jobs")]
    public async Task Authentic_claims_still_require_the_matching_completed_workflow_and_signing_window(string mutation)
    {
        using var fixture = new Fixture();
        var result = await Verify(fixture, fixture.Bundle(fixture.Payload()));
        var observation = Observation();
        var cutoff = Evaluation;
        if (mutation == "workflow") observation = observation with { Workflow = Workflow with { RunAttempt = 2 } };
        if (mutation == "before-run") observation = observation with { StartedAtUtc = SignedAt.AddSeconds(1) };
        if (mutation == "after-run") observation = observation with { CompletedAtUtc = SignedAt.AddSeconds(-1) };
        if (mutation == "after-cutoff") cutoff = observation.CompletedAtUtc.AddSeconds(-1);
        if (mutation == "outside-jobs") observation = observation with
        {
            Jobs = [new(1, "unit-vector", SignedAt.AddSeconds(1), SignedAt.AddMinutes(1))]
        };
        if (mutation == "no-jobs") observation = observation with { Jobs = [] };
        Assert.Equal("oci-signature-workflow-time",
            Assert.Throws<Cp6ReleaseContractException>(() => result.RequireCompletedWorkflow(observation, cutoff)).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4194305)]
    public async Task Bundle_size_fails_before_any_json_or_crypto_work(int length)
    {
        using var fixture = new Fixture();
        Assert.Equal("oci-signature-size", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Verify(fixture, new byte[length], verifier: MissingVerifier()))).Code);
    }

    [Fact]
    public async Task Cancellation_precedes_authentication()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AuthenticatedOciSignature.AuthenticateWithPolicyAsync(fixture.Policy(), Evaluation,
                Digest, Workflow, "{}"u8.ToArray(), MissingVerifier(), cancellation.Token));
    }

    private static Task<AuthenticatedOciSignature> Verify(Fixture fixture, byte[] bundle,
        Cp6PinnedTrustPolicy? policy = null, CosignBlobVerifier? verifier = null) =>
        AuthenticatedOciSignature.AuthenticateWithPolicyAsync(policy ?? fixture.Policy(), Evaluation,
            Digest, Workflow, bundle, verifier ?? Verifier());

    private static GitHubWorkflowObservation Observation() => new(Workflow, "workflow_dispatch",
        SignedAt.AddMinutes(-1), SignedAt.AddMinutes(2), new(new string('d', 64), 100),
        [new(1, "unit-vector", SignedAt.AddSeconds(-30), SignedAt.AddMinutes(1))], Evaluation);

    private sealed class Fixture : IDisposable
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        internal string KeyId => "sha256:" + Cp6DeterministicJson.Sha256Hex(_key.ExportSubjectPublicKeyInfo());

        internal JsonObject PolicyNode()
        {
            var root = JsonNode.Parse(VerifierTrust.Load().ValidatedDocument.CanonicalUtf8)!;
            root["keys"] = new JsonArray(new JsonObject
            {
                ["keyId"] = KeyId,
                ["purpose"] = "oci",
                ["validFromUtc"] = "2026-01-01T00:00:00.000Z",
                ["validUntilUtc"] = "2028-01-01T00:00:00.000Z",
                ["publicKey"] = PemEncoding.WriteString("PUBLIC KEY", _key.ExportSubjectPublicKeyInfo())
            });
            return root.AsObject();
        }

        internal Cp6PinnedTrustPolicy Policy(JsonObject? node = null) => Cp6PinnedTrustPolicy.Parse(
            Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(node ?? PolicyNode())));

        internal byte[] Payload(Action<JsonObject>? mutate = null)
        {
            var root = new JsonObject
            {
                ["_type"] = "https://in-toto.io/Statement/v1",
                ["subject"] = new JsonArray(new JsonObject
                {
                    ["digest"] = new JsonObject { ["sha256"] = Digest[7..] },
                    ["annotations"] = new JsonObject
                    {
                        ["cp6.imageRepository"] = S06ReleaseIdentity.ImageRepository,
                        ["cp6.sourceGitSha"] = Workflow.CommitSha,
                        ["cp6.workflowRepository"] = Workflow.Repository,
                        ["cp6.workflowPath"] = Workflow.WorkflowPath,
                        ["cp6.workflowFileSha"] = Workflow.WorkflowFileSha,
                        ["cp6.runId"] = "12345",
                        ["cp6.runAttempt"] = "1",
                        ["cp6.environment"] = "p10-platform-candidate",
                        ["cp6.signerKeyId"] = KeyId,
                        ["cp6.trustPolicyVersion"] = "1",
                        ["cp6.signedAtUtc"] = "2026-09-07T10:00:00.000Z",
                        ["cp6.deployable"] = "false"
                    }
                }),
                ["predicateType"] = OciBundlePayload.PredicateType,
                ["predicate"] = new JsonObject()
            };
            mutate?.Invoke(root);
            return JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions { WriteIndented = true });
        }

        internal byte[] Bundle(byte[] payload)
        {
            const string payloadType = "application/vnd.in-toto+json";
            var prefix = Encoding.UTF8.GetBytes("DSSEv1 " + Encoding.UTF8.GetByteCount(payloadType)
                .ToString(CultureInfo.InvariantCulture) + " " + payloadType + " " +
                payload.Length.ToString(CultureInfo.InvariantCulture) + " ");
            return JsonSerializer.SerializeToUtf8Bytes(new
            {
                mediaType = "application/vnd.dev.sigstore.bundle.v0.3+json",
                verificationMaterial = new
                {
                    publicKey = new { hint = Convert.ToBase64String(SHA256.HashData(_key.ExportSubjectPublicKeyInfo())) }
                },
                dsseEnvelope = new
                {
                    payloadType,
                    payload = Convert.ToBase64String(payload),
                    signatures = new[] { new { sig = Convert.ToBase64String(_key.SignData(prefix.Concat(payload).ToArray(),
                        HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)) } }
                }
            });
        }

        public void Dispose() => _key.Dispose();
    }
}
```

- [x] Add only throwing declarations matching the entry/result signatures, and run the focused suite. Every test must fail because the implementation is absent, with no unexpected failures/skips.

Run from tools/p10 with SDK 8.0.424, P10_COSIGN_PATH pointing to the already SHA-pinned official executable, and existing locked restore assets:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~OciSignatureAuthenticationTests --logger "trx;LogFileName=oci-auth-red.trx" --results-directory ../../artifacts/p10/oci-auth-red
```

## Task 2: Implement the exact identity profile

- [x] Add the profile and replace throwing declarations with the implementation below.

### tools/p10/ReleaseVerifier/OciSignatureProfile.cs

```csharp
using System.Globalization;
using System.Text.Json;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Exact cosign v3.1.3 image-signature claims; no registry or workflow acceptance here.
internal static class OciSignatureProfile
{
    internal const string EnvironmentName = "p10-platform-candidate";

    internal static Dictionary<string, string> Annotations(GitHubWorkflowIdentity workflow,
        string keyId, int version, DateTimeOffset signedAtUtc)
    {
        workflow.RequireValid();
        Require(workflow.Repository == "GTX537/CP6" && workflow.WorkflowPath == S06ReleaseIdentity.ValidationPath);
        return new(StringComparer.Ordinal)
        {
            ["cp6.imageRepository"] = S06ReleaseIdentity.ImageRepository,
            ["cp6.sourceGitSha"] = workflow.CommitSha,
            ["cp6.workflowRepository"] = workflow.Repository,
            ["cp6.workflowPath"] = workflow.WorkflowPath,
            ["cp6.workflowFileSha"] = workflow.WorkflowFileSha,
            ["cp6.runId"] = workflow.RunId.ToString(CultureInfo.InvariantCulture),
            ["cp6.runAttempt"] = workflow.RunAttempt.ToString(CultureInfo.InvariantCulture),
            ["cp6.environment"] = EnvironmentName,
            ["cp6.signerKeyId"] = keyId,
            ["cp6.trustPolicyVersion"] = version.ToString(CultureInfo.InvariantCulture),
            ["cp6.signedAtUtc"] = signedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            ["cp6.deployable"] = "false"
        };
    }

    // These three untrusted selectors only choose an already-pinned key and policy.
    internal static (string KeyId, int Version, DateTimeOffset SignedAtUtc) Selectors(JsonElement root,
        DateTimeOffset evaluationUtc)
    {
        try
        {
            var annotations = root.GetProperty("subject")[0].GetProperty("annotations");
            var keyId = annotations.GetProperty("cp6.signerKeyId").GetString()!;
            var versionText = annotations.GetProperty("cp6.trustPolicyVersion").GetString()!;
            var time = annotations.GetProperty("cp6.signedAtUtc").GetString()!;
            if (keyId.Length != 71 || !keyId.StartsWith("sha256:", StringComparison.Ordinal) ||
                keyId[7..].Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')) ||
                !int.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out var version) ||
                version < 1 || version.ToString(CultureInfo.InvariantCulture) != versionText ||
                !DateTimeOffset.TryParseExact(time, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var signedAtUtc))
                throw new FormatException();
            if (evaluationUtc.Offset != TimeSpan.Zero || signedAtUtc < DateTimeOffset.UnixEpoch || signedAtUtc > evaluationUtc)
                throw Error("oci-signature-time");
            return (keyId, version, signedAtUtc);
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("oci-signature-selector"); }
    }

    internal static void RequireClaims(JsonElement root, string digest, GitHubWorkflowIdentity workflow,
        string keyId, int version, DateTimeOffset signedAtUtc)
    {
        try
        {
            Exact(root, "_type", "subject", "predicateType", "predicate");
            Require(root.GetProperty("_type").GetString() == "https://in-toto.io/Statement/v1" &&
                root.GetProperty("predicateType").GetString() == OciBundlePayload.PredicateType && version == 1);
            Exact(root.GetProperty("predicate"));
            var subjects = root.GetProperty("subject");
            Require(subjects.ValueKind == JsonValueKind.Array && subjects.GetArrayLength() == 1);
            var subject = subjects[0];
            // cosign sign --new-bundle-format emits digest and annotations, without a subject name.
            Exact(subject, "digest", "annotations");
            Exact(subject.GetProperty("digest"), "sha256");
            Require(subject.GetProperty("digest").GetProperty("sha256").GetString() == digest[7..]);
            var expected = Annotations(workflow, keyId, version, signedAtUtc);
            var actual = subject.GetProperty("annotations");
            Exact(actual, expected.Keys.ToArray());
            foreach (var pair in expected) Require(actual.GetProperty(pair.Key).GetString() == pair.Value);
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("oci-signature-claims"); }
    }

    private static void Exact(JsonElement value, params string[] names) =>
        Require(value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Select(p => p.Name)
            .Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal), StringComparer.Ordinal));

    private static void Require(bool condition)
    {
        if (!condition) throw Error("oci-signature-claims");
    }

    internal static Cp6ReleaseContractException Error(string code) =>
        new(code, "OCI signature violates the pinned S06 identity and trust profile.");
}
```

### tools/p10/ReleaseVerifier/AuthenticatedOciSignature.cs

```csharp
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Authenticated claims only, not image availability, workflow success or candidate acceptance.
internal sealed class AuthenticatedOciSignature
{
    private readonly byte[] _bundle;

    private AuthenticatedOciSignature(string digest, GitHubWorkflowIdentity workflow, string signerKeyId,
        int policyVersion, DateTimeOffset signedAtUtc, byte[] bundle, byte[] payload)
    {
        Digest = digest;
        Workflow = workflow;
        SignerKeyId = signerKeyId;
        PolicyVersion = policyVersion;
        SignedAtUtc = signedAtUtc;
        _bundle = bundle;
        BundleSha256 = Cp6DeterministicJson.Sha256Hex(bundle);
        PayloadSha256 = Cp6DeterministicJson.Sha256Hex(payload);
    }

    internal string Repository => S06ReleaseIdentity.ImageRepository;
    internal string Digest { get; }
    internal GitHubWorkflowIdentity Workflow { get; }
    internal string SignerKeyId { get; }
    internal int PolicyVersion { get; }
    internal DateTimeOffset SignedAtUtc { get; }
    internal string BundleSha256 { get; }
    internal string PayloadSha256 { get; }
    internal byte[] CopyBundle() => _bundle.ToArray();

    internal static Task<AuthenticatedOciSignature> AuthenticateAsync(string digest,
        GitHubWorkflowIdentity workflow, ReadOnlyMemory<byte> bundle, CosignBlobVerifier verifier,
        CancellationToken cancellationToken = default) =>
        AuthenticateWithPolicyAsync(VerifierTrust.Load(), DateTimeOffset.UtcNow,
            digest, workflow, bundle, verifier, cancellationToken);

    // The normal entry always loads compiled trust and the actual current time.
    internal static async Task<AuthenticatedOciSignature> AuthenticateWithPolicyAsync(Cp6PinnedTrustPolicy policy,
        DateTimeOffset evaluationUtc, string digest, GitHubWorkflowIdentity workflow,
        ReadOnlyMemory<byte> bundle, CosignBlobVerifier verifier, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OciWirePolicy.RequireDigest(digest);
        workflow.RequireValid();
        if (workflow.Repository != "GTX537/CP6" || workflow.WorkflowPath != S06ReleaseIdentity.ValidationPath)
            throw OciSignatureProfile.Error("oci-signature-workflow");
        if (bundle.Length is < 1 or > Cp6DeterministicJson.MaximumBytes) throw OciSignatureProfile.Error("oci-signature-size");
        var owned = bundle.ToArray();
        var selectors = OciSignatureProfile.Selectors(GitHubApiJson.Parse(OciBundlePayload.Read(owned)), evaluationUtc);
        var key = policy.RequireKey(selectors.KeyId, "oci", selectors.Version, selectors.SignedAtUtc,
            evaluationUtc, Cp6ReleaseValidationMode.Current);
        var payload = await verifier.VerifyOciBundleAsync(digest, owned, key, cancellationToken);
        OciSignatureProfile.RequireClaims(GitHubApiJson.Parse(payload), digest, workflow,
            selectors.KeyId, selectors.Version, selectors.SignedAtUtc);
        return new(digest, workflow, selectors.KeyId, selectors.Version, selectors.SignedAtUtc, owned, payload);
    }

    // Called after the live source has proved completed-success run, jobs and workflow bytes.
    // This also prevents reusing an authentic signature from another invocation or time window.
    internal void RequireCompletedWorkflow(GitHubWorkflowObservation observation, DateTimeOffset cutoff)
    {
        if (cutoff.Offset != TimeSpan.Zero || observation.Workflow != Workflow ||
            observation.StartedAtUtc > SignedAtUtc || SignedAtUtc > observation.CompletedAtUtc ||
            observation.CompletedAtUtc > cutoff || !observation.Jobs.Any(job =>
                job.StartedAtUtc <= SignedAtUtc && SignedAtUtc <= job.CompletedAtUtc))
            throw OciSignatureProfile.Error("oci-signature-workflow-time");
    }
}
```

- [x] Run focused and full Release tests and formatting. The full suite uses actual cosign, formal package bytes, GitHub/feed read access and the established environment setup; no new network stubs or secret fixtures.

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~OciSignatureAuthenticationTests
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore
dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --verify-no-changes --no-restore
```

Expected: all focused tests pass and baseline 818 plus the new cases pass without skips. The ephemeral key must fail at the compiled-trust normal entry. No real image or validation-run success is claimed here.

## Task 3: Review and checkpoint

- [x] Confirm exact three-file plan parity, inspect the complete four-file scope, scan for sensitive/machine-specific content and verify no workflow/runtime/lock/trust changes.
- [x] Record actual red/green results, stage these four files only and commit normally.

```powershell
git diff --check
git add -- docs/superpowers/plans/2026-09-08-p10-s06-oci-signature-identity.md tools/p10/ReleaseVerifier/OciSignatureProfile.cs tools/p10/ReleaseVerifier/AuthenticatedOciSignature.cs tools/p10/ReleaseVerifier.Tests/OciSignatureAuthenticationTests.cs
git diff --cached --check
git commit -m "feat(p10): bind OCI signatures to current trust and validation identity"
```

- [ ] Complete actual protected-workflow OCI, reports, publication and cross-repository audit before P10 acceptance.

## Observed results (2026-09-08 UTC)

- RED: 49 failures, all NotImplementedException, zero unexpected failures and zero skipped/not-executed cases.
- Initial GREEN: 48/49. The pinned cosign process rejects an unknown Statement root member before CP6 claim checking. Repeating the eight signed-shape cases confirmed only that precise error-code expectation was wrong; the test and this plan now expect cosign-signature for extra-root, and oci-signature-claims for the other seven. No production check was relaxed.
- Corrected focused suite: 49/49 passed. Full Release suite: 867/867 passed, zero skips, no build warnings/errors. Format verification exited 0.
- Exact three-file plan parity, complete four-file scope review and secret/path hygiene passed. No existing workflow, runtime, trust, lock, remote registry or R2 content changed.
- These are real cryptographic component tests and temporal/identity unit vectors only. The approved real verifier image, successful protected validation, published Locator and cross-repository audit remain required for P10.
