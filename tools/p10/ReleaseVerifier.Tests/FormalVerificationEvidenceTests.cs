using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Producer IDs are unsigned unit vectors. All package proofs below come from actual authenticated feed reads.
public sealed class FormalVerificationEvidenceTests
{
    private static GitHubWorkflowIdentity Producer => new("GTX537/CP6", S06ReleaseIdentity.ValidationPath,
        new string('b', 40), 12345, 1, new string('c', 40));
    private static readonly Lazy<Task<CollectedFormalEvidence>> Actual = new(() =>
        FormalVerificationEvidence.CollectAsync(Producer, Token()));

    [Fact]
    public async Task Seven_actual_downloads_and_signature_proofs_are_bound_to_the_public_statement()
    {
        var actual = await Actual.Value;
        var bytes = actual.CopyBytes();
        var statement = FormalVerificationEvidence.Read(bytes, Producer, DateTimeOffset.UtcNow, actual.Packages);
        Assert.Equal(7, actual.Packages.Count);
        Assert.Equal(S06ReleaseIdentity.PackageHashes.Keys.Order(StringComparer.Ordinal),
            actual.Packages.Select(p => p.Proof.PackageId));
        Assert.Equal(Cp6DeterministicJson.Sha256Hex(bytes), statement.Sha256);
        Assert.Equal(8, JsonNode.Parse(bytes)!["subject"]!.AsArray().Count);
        Assert.False(Encoding.UTF8.GetString(bytes).Contains(Token(), StringComparison.Ordinal));
        foreach (var package in actual.Packages)
        {
            Assert.Equal(S06ReleaseIdentity.PackageHashes[package.Proof.PackageId], package.Proof.Sha256);
            Assert.True(package.Proof.InternallyTrusted);
            Assert.False(package.Proof.PublicCaTrusted);
        }
        bytes[0] = 0;
        Assert.Equal((byte)'{', actual.CopyBytes()[0]);
    }

    [Theory]
    [InlineData("feed")]
    [InlineData("source")]
    [InlineData("trust")]
    [InlineData("missing-package")]
    [InlineData("duplicate-package")]
    [InlineData("package-order")]
    [InlineData("id")]
    [InlineData("version")]
    [InlineData("package-source")]
    [InlineData("author-hash")]
    [InlineData("published-hash")]
    [InlineData("signer")]
    [InlineData("spki")]
    [InlineData("public-ca")]
    [InlineData("internal")]
    [InlineData("timestamp-policy")]
    [InlineData("timestamp-time")]
    [InlineData("timestamp-chain-missing")]
    [InlineData("timestamp-chain-changed")]
    [InlineData("timestamp-chain-order")]
    [InlineData("transformation")]
    [InlineData("retrieved-after")]
    [InlineData("retrieved-before")]
    [InlineData("retrieved-offset")]
    [InlineData("extra-detail")]
    [InlineData("extra-package-field")]
    [InlineData("missing-package-field")]
    [InlineData("packages-type")]
    public async Task Changed_claims_fail_against_the_independently_verified_package_proofs(string mutation)
    {
        var actual = await Actual.Value;
        var root = JsonNode.Parse(actual.CopyBytes())!;
        var details = root["predicate"]!["details"]!;
        var packages = details["packages"]!.AsArray();
        var package = packages[0]!;
        if (mutation == "feed") details["feedServiceIndex"] = "https://example.invalid/index.json";
        if (mutation == "source") details["sourceGitSha"] = new string('a', 40);
        if (mutation == "trust") details["trustPolicySha256"] = new string('a', 64);
        if (mutation == "missing-package") packages.RemoveAt(6);
        if (mutation == "duplicate-package") packages[1] = packages[0]!.DeepClone();
        if (mutation == "package-order")
        {
            var first = packages[0]!.DeepClone();
            packages[0] = packages[6]!.DeepClone();
            packages[6] = first;
        }
        if (mutation == "id") package["packageId"] = "CP6.Platform.Testing";
        if (mutation == "version") package["version"] = "0.10.0";
        if (mutation == "package-source") package["sourceGitSha"] = new string('a', 40);
        if (mutation == "author-hash") package["authorSignedPackageSha256"] = new string('a', 64);
        if (mutation == "published-hash") package["publishedPackageSha256"] = new string('a', 64);
        if (mutation == "signer") package["signerFingerprint"] = new string('a', 64);
        if (mutation == "spki") package["spkiKeyId"] = "sha256:" + new string('a', 64);
        if (mutation == "public-ca") package["publicCaTrusted"] = true;
        if (mutation == "internal") package["internallyTrusted"] = false;
        if (mutation == "timestamp-policy") package["timestampPolicyOid"] = "1.2.3.4";
        if (mutation == "timestamp-time")
            package["timestampUtc"] = S06InToto.FormatTime(actual.Packages[0].Proof.TimestampUtc.AddSeconds(1));
        if (mutation == "timestamp-chain-missing") package["timestampCertificateChainSha256"] = new JsonArray();
        if (mutation == "timestamp-chain-changed") package["timestampCertificateChainSha256"]![0] = new string('a', 64);
        if (mutation == "timestamp-chain-order")
        {
            var chain = package["timestampCertificateChainSha256"]!.AsArray();
            var first = chain[0]!.DeepClone();
            chain[0] = chain[chain.Count - 1]!.DeepClone();
            chain[chain.Count - 1] = first;
        }
        if (mutation == "transformation") package["feedTransformation"] = "Unknown";
        if (mutation == "retrieved-after") package["retrievedAtUtc"] = S06InToto.FormatTime(DateTimeOffset.UtcNow.AddDays(1));
        if (mutation == "retrieved-before") package["retrievedAtUtc"] = S06InToto.FormatTime(DateTimeOffset.UnixEpoch);
        if (mutation == "retrieved-offset") package["retrievedAtUtc"] = "2026-09-08T00:00:00.000+00:00";
        if (mutation == "extra-detail") details["unreviewed"] = true;
        if (mutation == "extra-package-field") package["unreviewed"] = true;
        if (mutation == "missing-package-field") package.AsObject().Remove("internallyTrusted");
        if (mutation == "packages-type") details["packages"] = "not-an-array";
        var failure = Assert.Throws<Cp6ReleaseContractException>(() =>
            FormalVerificationEvidence.Read(Canonical(root), Producer, DateTimeOffset.UtcNow, actual.Packages));
        Assert.Null(failure.InnerException);
    }

    [Fact]
    public async Task A_later_independent_read_compares_immutable_proofs_without_requiring_equal_retrieval_times()
    {
        var actual = await Actual.Value;
        var later = await FormalPackageSource.DownloadAndVerifyAsync(S06ReleaseIdentity.ReleasePackage, Token());
        var original = Assert.Single(actual.Packages, p => p.Proof.PackageId == S06ReleaseIdentity.ReleasePackage);
        Assert.True(later.RetrievedAtUtc > original.RetrievedAtUtc);
        var current = actual.Packages.Select(p => p.Proof.PackageId == later.Proof.PackageId ? later : p).ToArray();
        _ = FormalVerificationEvidence.Read(actual.CopyBytes(), Producer, DateTimeOffset.UtcNow, current);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    public async Task A_statement_cannot_stand_in_for_a_missing_independent_package_set(string mutation)
    {
        var actual = await Actual.Value;
        var packages = mutation == "empty" ? [] : actual.Packages.Take(mutation == "missing" ? 6 : 7).ToArray();
        if (mutation == "duplicate") packages[1] = packages[0];
        Assert.Equal("s06-packages-set", Assert.Throws<Cp6ReleaseContractException>(() =>
            FormalVerificationEvidence.Read(actual.CopyBytes(), Producer, DateTimeOffset.UtcNow, packages)).Code);
    }

    [Fact]
    public async Task An_invalid_producer_is_rejected_before_any_feed_request() =>
        Assert.Equal("s06-attestation-producer", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            FormalVerificationEvidence.CollectAsync(Producer with { WorkflowPath = S06ReleaseIdentity.PublicationPath },
                "not-a-real-token"))).Code);

    [Fact]
    public async Task Precancelled_collection_does_not_read_the_feed()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            FormalVerificationEvidence.CollectAsync(Producer, "not-a-real-token", cancellation.Token));
    }

    private static byte[] Canonical(JsonNode root) =>
        Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(root));
    private static string Token() => Environment.GetEnvironmentVariable("P10_FEED_READ_TOKEN") ??
        throw new InvalidOperationException("Actual formal feed reads are required; no skipped or substituted package proof.");
}
