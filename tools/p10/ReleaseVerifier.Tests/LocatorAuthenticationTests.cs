using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// These ephemeral signed Locators exercise ordering and policy; none is release evidence.
public sealed class LocatorAuthenticationTests
{
    private const string Tag = "v0.10.1-test.991";
    private static readonly DateTimeOffset Evaluation = DateTimeOffset.Parse("2026-09-07T12:00:00Z");
    private static byte[] TrustBytes() => File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "trust", "pinned-trust-store.v1.json"));
    private static CosignBlobVerifier Verifier() => new(
        Environment.GetEnvironmentVariable("P10_COSIGN_PATH") ?? throw new InvalidOperationException("Pinned cosign is required."));
    private static CosignBlobVerifier MissingVerifier() => new(Path.Combine(Path.GetTempPath(), "not-a-cosign-tool"));

    [Fact]
    public void Embedded_and_disk_trust_are_bound_to_the_independent_bootstrap_hash()
    {
        const string expected = "0a6e72951c196e612a593cc8831e294bb538c9ba8a79eada4538771a3811d8e9";
        Assert.Equal(expected, VerifierTrust.Load().ValidatedDocument.Sha256);
        Assert.Equal(expected, VerifierTrust.Parse(TrustBytes()).ValidatedDocument.Sha256);
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("policy")]
    [InlineData("key")]
    public void Even_well_formed_replacement_trust_cannot_become_a_new_bootstrap(string mutation)
    {
        using var fixture = new Fixture();
        var node = JsonNode.Parse(TrustBytes())!.AsObject();
        if (mutation == "policy") node["minimumAcceptedPolicyVersion"] = 1 + node["policyVersion"]!.GetValue<int>();
        if (mutation == "key") node["keys"] = fixture.PolicyNode()["keys"]!.DeepClone();
        var bytes = mutation == "bytes" ? TrustBytes().Concat(new byte[] { 10 }).ToArray() : Canonical(node);
        Assert.Equal("trust-bootstrap-hash", Assert.Throws<Cp6ReleaseContractException>(() => VerifierTrust.Parse(bytes)).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4194305)]
    public void Bootstrap_size_is_checked_before_hashing(int length) =>
        Assert.Equal("trust-bootstrap-size", Assert.Throws<Cp6ReleaseContractException>(() =>
            VerifierTrust.Parse(new byte[length])).Code);

    [Fact]
    public async Task Real_signature_then_contract_validation_returns_only_the_authenticated_reference()
    {
        using var fixture = new Fixture();
        var bytes = fixture.Locator();
        var result = await Verify(fixture, bytes);
        Assert.Equal(Tag, result.ReleaseTag);
        Assert.Equal(Cp6DeterministicJson.Sha256Hex(bytes), result.Sha256);
        Assert.Equal(fixture.KeyId, result.SignerKeyId);
        Assert.Equal(1, result.PolicyVersion);
        Assert.Equal(Cp6DeterministicJson.Sha256Hex("{}"u8), result.Subject.Sha256);
        Assert.Equal($"candidates/platform/{Tag}/candidate-locator.v1.json", result.DiscoveryKeys.LocatorKey);
    }

    [Fact]
    public async Task Public_entry_rejects_a_valid_self_signed_fixture_not_in_the_compiled_trust()
    {
        using var fixture = new Fixture();
        var bytes = fixture.Locator();
        Assert.Equal("trust-key", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            AuthenticatedLocator.AuthenticateAsync(Tag, bytes, fixture.Bundle(bytes), Verifier()))).Code);
    }

    [Theory]
    [InlineData("purpose", "trust-purpose")]
    [InlineData("revoked", "trust-revoked")]
    [InlineData("validity", "trust-validity")]
    public async Task Current_policy_failures_precede_any_signature_process(string mutation, string expected)
    {
        using var fixture = new Fixture();
        var node = fixture.PolicyNode();
        var key = node["keys"]![0]!;
        if (mutation == "purpose") key["purpose"] = "oci";
        if (mutation == "validity") key["validUntilUtc"] = "2026-08-01T00:00:00.000Z";
        if (mutation == "revoked")
        {
            key["revokedAtUtc"] = "2026-09-07T11:00:00.000Z";
            key["revocationReason"] = "Regression fixture only";
        }
        var bytes = fixture.Locator();
        Assert.Equal(expected, (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            AuthenticatedLocator.AuthenticateWithPolicyAsync(Cp6PinnedTrustPolicy.Parse(Canonical(node)),
                Evaluation, Tag, bytes, fixture.Bundle(bytes), MissingVerifier()))).Code);
    }

    [Theory]
    [InlineData("future", "locator-time")]
    [InlineData("fraction", "locator-selector")]
    [InlineData("version-zero", "locator-selector")]
    [InlineData("version-future", "trust-policy-version")]
    [InlineData("missing-key", "locator-selector")]
    [InlineData("unknown-key", "trust-key")]
    public async Task Invalid_selectors_fail_before_executable_or_subject_access(string mutation, string expected)
    {
        using var fixture = new Fixture();
        var bytes = fixture.Locator(node =>
        {
            if (mutation == "future") node["createdAtUtc"] = "2026-09-08T00:00:00.000Z";
            if (mutation == "fraction") node["createdAtUtc"] = "2026-09-07T00:00:00Z";
            if (mutation == "version-zero") node["trustPolicyVersion"] = 0;
            if (mutation == "version-future") node["trustPolicyVersion"] = 2;
            if (mutation == "missing-key") node.Remove("signerKeyId");
            if (mutation == "unknown-key") node["signerKeyId"] = "sha256:" + new string('a', 64);
            node["subject"] = "untrusted-subject-not-read";
        });
        Assert.Equal(expected, (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Verify(fixture, bytes, verifier: MissingVerifier()))).Code);
    }

    [Theory]
    [InlineData("tag", "locator-tag")]
    [InlineData("lane", "locator-lane")]
    [InlineData("key-binding", "object-key-binding")]
    public async Task A_real_signature_does_not_authorize_wrong_lane_tag_or_object_key(string mutation, string expected)
    {
        using var fixture = new Fixture();
        var bytes = fixture.Locator(node =>
        {
            if (mutation == "tag") node["releaseTag"] = "v0.10.1-test.992";
            if (mutation == "lane")
            {
                node["subjectKind"] = "SystemCandidateResult";
                node["subject"]!["mediaType"] = Cp6ReleaseMediaTypes.CandidateResult;
            }
            if (mutation == "key-binding")
                node["subject"]!["key"] = "objects/sha256/aa/" + new string('a', 64) + "/candidate.json";
        });
        Assert.Equal(expected, (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() => Verify(fixture, bytes))).Code);
    }

    [Fact]
    public async Task Invalid_signature_is_rejected_before_even_parsing_a_malformed_subject()
    {
        using var fixture = new Fixture();
        var original = fixture.Locator();
        var altered = fixture.Locator(node => node["subject"] = "not-an-object");
        Assert.Equal("cosign-signature", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Verify(fixture, altered, fixture.Bundle(original)))).Code);
    }

    [Fact]
    public async Task Correctly_signed_but_noncanonical_bytes_are_rejected()
    {
        using var fixture = new Fixture();
        var bytes = fixture.Locator().Concat(new byte[] { 10 }).ToArray();
        Assert.Equal("non-canonical-json", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Verify(fixture, bytes, verifier: MissingVerifier()))).Code);
    }

    [Theory]
    [InlineData("../v0.10.1")]
    [InlineData("v0.10.1\n")]
    [InlineData("")]
    public async Task Caller_discovery_tag_cannot_escape_its_fixed_prefix(string tag)
    {
        using var fixture = new Fixture();
        var bytes = fixture.Locator();
        await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            AuthenticatedLocator.AuthenticateWithPolicyAsync(fixture.Policy(), Evaluation, tag,
                bytes, fixture.Bundle(bytes), MissingVerifier()));
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(true, 4194305)]
    [InlineData(false, 4194305)]
    public async Task Locator_and_bundle_inputs_are_bounded_before_parsing(bool locator, int length)
    {
        using var fixture = new Fixture();
        var bytes = fixture.Locator();
        Assert.Equal("locator-size", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            AuthenticatedLocator.AuthenticateWithPolicyAsync(fixture.Policy(), Evaluation, Tag,
                locator ? new byte[length] : bytes, locator ? fixture.Bundle(bytes) : new byte[length], MissingVerifier()))).Code);
    }

    [Fact]
    public async Task Cancellation_precedes_authentication_work()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AuthenticatedLocator.AuthenticateWithPolicyAsync(fixture.Policy(), Evaluation, Tag,
                fixture.Locator(), "{}"u8.ToArray(), MissingVerifier(), cancellation.Token));
    }

    private static Task<AuthenticatedLocator> Verify(Fixture fixture, byte[] bytes, byte[]? bundle = null,
        CosignBlobVerifier? verifier = null) =>
        AuthenticatedLocator.AuthenticateWithPolicyAsync(fixture.Policy(), Evaluation, Tag, bytes,
            bundle ?? fixture.Bundle(bytes), verifier ?? Verifier());

    private static byte[] Canonical(JsonNode node) =>
        Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(node));

    private sealed class Fixture : IDisposable
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public string KeyId => "sha256:" + Cp6DeterministicJson.Sha256Hex(_key.ExportSubjectPublicKeyInfo());

        public JsonObject PolicyNode()
        {
            var root = JsonNode.Parse(TrustBytes())!.AsObject();
            root["keys"] = new JsonArray(new JsonObject
            {
                ["keyId"] = KeyId,
                ["purpose"] = "candidate-locator",
                ["validFromUtc"] = "2026-01-01T00:00:00.000Z",
                ["validUntilUtc"] = "2028-01-01T00:00:00.000Z",
                ["publicKey"] = PemEncoding.WriteString("PUBLIC KEY", _key.ExportSubjectPublicKeyInfo())
            });
            return root;
        }

        public Cp6PinnedTrustPolicy Policy() => Cp6PinnedTrustPolicy.Parse(Canonical(PolicyNode()));

        public byte[] Locator(Action<JsonObject>? change = null)
        {
            var reference = ContentAddress.Create("{}"u8, Cp6ReleaseMediaTypes.PlatformReleaseCandidate, "candidate.json");
            var node = new JsonObject
            {
                ["$schemaId"] = Cp6ReleaseContractIds.CandidateLocator,
                ["releaseTag"] = Tag,
                ["subjectKind"] = "PlatformReleaseCandidate",
                ["subject"] = JsonNode.Parse(reference.ToJson().GetRawText()),
                ["trustPolicyVersion"] = 1,
                ["signerKeyId"] = KeyId,
                ["createdAtUtc"] = "2026-09-07T10:00:00.000Z"
            };
            change?.Invoke(node);
            return Canonical(node);
        }

        public byte[] Bundle(byte[] bytes) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            mediaType = "application/vnd.dev.sigstore.bundle.v0.3+json",
            verificationMaterial = new
            {
                publicKey = new { hint = Convert.ToBase64String(SHA256.HashData(_key.ExportSubjectPublicKeyInfo())) }
            },
            messageSignature = new
            {
                messageDigest = new { algorithm = "SHA2_256", digest = Convert.ToBase64String(SHA256.HashData(bytes)) },
                signature = Convert.ToBase64String(_key.SignData(bytes, HashAlgorithmName.SHA256,
                    DSASignatureFormat.Rfc3279DerSequence))
            }
        });

        public void Dispose() => _key.Dispose();
    }
}
