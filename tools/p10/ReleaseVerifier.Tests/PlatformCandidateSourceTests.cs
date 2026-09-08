using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Real ephemeral cosign signatures plus unpublished structural graph fixtures.
// No fixture is trusted by the public entry point or represented as release acceptance.
public sealed class PlatformCandidateSourceTests
{
    private static CosignBlobVerifier Verifier() => new(
        Environment.GetEnvironmentVariable("P10_COSIGN_PATH") ?? throw new InvalidOperationException("Pinned cosign is required."));

    [Fact]
    public async Task Authenticated_root_fetches_exact_closed_graph_with_no_acceptance_claim()
    {
        var input = EvidenceGraphFixture.Build();
        using var signed = new SignedRoot(input);
        var locator = await signed.Authenticate();
        var reads = new List<string>();
        var result = await PlatformCandidateSource.FetchAuthenticatedAsync(locator, (reference, _) =>
        {
            reads.Add(reference.Key);
            return Task.FromResult(signed.Bytes(reference));
        });
        Assert.Same(locator, result.Locator);
        Assert.False(result.CandidateAccepted);
        Assert.False(result.Graph.CandidateAccepted);
        Assert.Equal(Cp6DeterministicJson.Sha256Hex(input.Candidate), result.Graph.Sha256);
        Assert.Equal(23, result.Graph.ObjectCount);
        Assert.Equal(24, reads.Count);
        Assert.Equal(24, reads.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(locator.Subject.Key, reads[0]);
        Assert.Equal(11, result.Graph.Evidence.Count);
    }

    [Fact]
    public async Task Intended_public_entry_authenticates_before_even_constructing_R2_credentials()
    {
        using var signed = new SignedRoot(EvidenceGraphFixture.Build());
        var error = await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            PlatformCandidateSource.IntendedAsync(SignedRoot.Tag, signed.Locator, signed.Bundle,
                "invalid-no-network", "invalid-no-network", Verifier()));
        Assert.Equal("trust-key", error.Code);
    }

    [Fact]
    public async Task Public_entries_cancel_before_credentials_network_or_crypto()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PlatformCandidateSource.DiscoverAsync(
            "invalid", "invalid", "invalid", Verifier(), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PlatformCandidateSource.IntendedAsync(
            "invalid", ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, "invalid", "invalid", Verifier(), cancellation.Token));
    }

    [Fact]
    public async Task Discovery_rejects_path_injection_before_credentials_or_network()
    {
        var error = await Assert.ThrowsAsync<Cp6ReleaseContractException>(() => PlatformCandidateSource.DiscoverAsync(
            "v0.10.1\n", "invalid", "invalid", Verifier()));
        Assert.Equal("r2-discovery-tag", error.Code);
    }

    [Fact]
    public async Task Locator_creation_time_must_equal_the_frozen_candidate_time()
    {
        using var signed = new SignedRoot(EvidenceGraphFixture.Build(), "2026-09-07T23:59:59.000Z");
        var locator = await signed.Authenticate();
        var reads = 0;
        await Error("source-locator-time", () => PlatformCandidateSource.FetchAuthenticatedAsync(locator, (reference, _) =>
        {
            reads++;
            return Task.FromResult(signed.Bytes(reference));
        }));
        Assert.Equal(1, reads);
    }

    [Theory]
    [InlineData("deployable")]
    [InlineData("package-source")]
    [InlineData("evidence-set")]
    public async Task Invalid_candidate_stops_before_following_any_evidence(string mutation)
    {
        var input = EvidenceGraphFixture.Build(root =>
        {
            if (mutation == "deployable") root["deployable"] = true;
            if (mutation == "package-source") root["packages"]![0]!["sourceGitSha"] = new string('1', 40);
            if (mutation == "evidence-set") root["evidence"]!.AsArray().RemoveAt(0);
        });
        using var signed = new SignedRoot(input);
        var locator = await signed.Authenticate();
        var reads = 0;
        await Assert.ThrowsAsync<Cp6ReleaseContractException>(() => PlatformCandidateSource.FetchAuthenticatedAsync(locator, (reference, _) =>
        {
            reads++;
            return Task.FromResult(signed.Bytes(reference));
        }));
        Assert.Equal(1, reads);
    }

    [Theory]
    [InlineData("missing", "graph-download-missing")]
    [InlineData("hash", "graph-download-hash")]
    public async Task Referenced_object_must_be_present_and_byte_identical(string mutation, string code)
    {
        using var signed = new SignedRoot(EvidenceGraphFixture.Build());
        var locator = await signed.Authenticate();
        var reads = 0;
        await Error(code, () => PlatformCandidateSource.FetchAuthenticatedAsync(locator, (reference, _) =>
        {
            reads++;
            var bytes = signed.Bytes(reference);
            if (reads > 1)
            {
                if (mutation == "missing") return Task.FromResult<byte[]?>(null);
                bytes![0] ^= 1;
            }
            return Task.FromResult<byte[]?>(bytes);
        }));
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task Wrong_record_subject_bindings_are_not_accepted_after_download()
    {
        var input = EvidenceGraphFixture.Build(recordChange: records =>
            records["CrmConsumer"]["subjects"]!.AsArray().RemoveAt(0));
        using var signed = new SignedRoot(input);
        var locator = await signed.Authenticate();
        await Error("graph-evidence-subjects", () => PlatformCandidateSource.FetchAuthenticatedAsync(locator,
            (reference, _) => Task.FromResult(signed.Bytes(reference))));
    }

    [Fact]
    public async Task Embedded_third_party_URLs_are_not_discovery_paths()
    {
        var input = EvidenceGraphFixture.Build(payloadChange: payloads =>
            payloads["ImageSbom"] = "{\"externalDocumentRefs\":[{\"spdxDocument\":\"https://attacker.example/token\"}]}"u8.ToArray());
        using var signed = new SignedRoot(input);
        var locator = await signed.Authenticate();
        var paths = new List<string>();
        var result = await PlatformCandidateSource.FetchAuthenticatedAsync(locator, (reference, _) =>
        {
            paths.Add(reference.Key);
            return Task.FromResult(signed.Bytes(reference));
        });
        Assert.All(paths, path => Assert.StartsWith("objects/sha256/", path));
        Assert.False(result.CandidateAccepted);
    }

    private static async Task Error(string code, Func<Task> action) =>
        Assert.Equal(code, (await Assert.ThrowsAsync<Cp6ReleaseContractException>(action)).Code);

    private sealed class SignedRoot : IDisposable
    {
        internal const string Tag = "v0.10.1-test.997";
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly GraphInput _input;
        private readonly ContentAddress _subject;
        internal byte[] Locator { get; }
        internal byte[] Bundle { get; }

        internal SignedRoot(GraphInput input, string created = EvidenceGraphFixture.Created)
        {
            _input = input;
            _subject = ContentAddress.Create(input.Candidate, Cp6ReleaseMediaTypes.PlatformReleaseCandidate, "candidate.json");
            var keyId = "sha256:" + Cp6DeterministicJson.Sha256Hex(_key.ExportSubjectPublicKeyInfo());
            Locator = EvidenceGraphFixture.Canonical(new JsonObject
            {
                ["$schemaId"] = Cp6ReleaseContractIds.CandidateLocator,
                ["releaseTag"] = Tag,
                ["subjectKind"] = "PlatformReleaseCandidate",
                ["subject"] = JsonNode.Parse(_subject.ToJson().GetRawText()),
                ["trustPolicyVersion"] = 1,
                ["signerKeyId"] = keyId,
                ["createdAtUtc"] = created
            });
            Bundle = JsonSerializer.SerializeToUtf8Bytes(new
            {
                mediaType = Cp6ReleaseMediaTypes.SigstoreBundle,
                verificationMaterial = new { publicKey = new { hint = Convert.ToBase64String(SHA256.HashData(_key.ExportSubjectPublicKeyInfo())) } },
                messageSignature = new
                {
                    messageDigest = new { algorithm = "SHA2_256", digest = Convert.ToBase64String(SHA256.HashData(Locator)) },
                    signature = Convert.ToBase64String(_key.SignData(Locator, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                }
            });
        }

        internal async Task<AuthenticatedLocator> Authenticate()
        {
            var policy = JsonNode.Parse(EvidenceGraphBaseline.Trust())!.AsObject();
            policy["keys"] = new JsonArray(new JsonObject
            {
                ["keyId"] = "sha256:" + Cp6DeterministicJson.Sha256Hex(_key.ExportSubjectPublicKeyInfo()),
                ["purpose"] = "candidate-locator",
                ["validFromUtc"] = "2026-01-01T00:00:00.000Z",
                ["validUntilUtc"] = "2028-01-01T00:00:00.000Z",
                ["publicKey"] = PemEncoding.WriteString("PUBLIC KEY", _key.ExportSubjectPublicKeyInfo())
            });
            return await AuthenticatedLocator.AuthenticateWithPolicyAsync(
                Cp6PinnedTrustPolicy.Parse(EvidenceGraphFixture.Canonical(policy)),
                DateTimeOffset.Parse("2026-09-08T01:00:00Z"), Tag, Locator, Bundle, Verifier());
        }

        internal byte[]? Bytes(ContentAddress reference) => reference.Key == _subject.Key ? _input.Candidate.ToArray() :
            _input.Objects.TryGetValue(reference.Key, out var bytes) ? bytes.ToArray() : null;
        public void Dispose() => _key.Dispose();
    }
}
