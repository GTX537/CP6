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
