# P10 S06 Formal Package Evidence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans sequentially in this task. The user selected no delegation.

**Goal:** Bind the seven actual formal NuGet download/signature/timestamp proofs into typed public S06 evidence and require independent proofs when reading it.

**Architecture:** The existing FormalPackageSource and FormalNuGetVerifier remain the only authenticated package reader and cryptographic verifier. This codec collects their seven immutable outputs, serializes only selected safe fields using S06InToto, and compares every claimed proof field with an independently verified package set supplied by the consumer. Historical retrieval time is bounded by the package timestamp and statement creation; it must not be confused with the consumer's later download time.

**Tech Stack:** .NET SDK 8.0.424, CP6.Platform.Release [0.10.1], NuGet.Packaging, fixed GitHub Packages feed, deterministic JSON, xUnit.

---

## Scope and acceptance boundary

The approved S06 plan already requires fresh authenticated reads of all seven formal 0.10.1 packages, exact original signed byte hashes, pinned internal author trust and RFC3161 timestamp validation. This module does not alter those checks, invent a success capability or initiate publication. The collector has a five-minute overall bound and inherits each package reader's fixed endpoint, transfer and signature policy.

The common statement binds the immutable Platform Git source and seven package hashes. Details carry the fixed feed/source/trust hash and seven package claims sorted by ID. Each claim includes exact signed and published hashes, byte-preserving feed behavior, pinned signer/SPKI, internal/public-CA trust distinction, timestamp policy/time/certificate-chain hashes and historical retrieval time. Raw package bytes, credentials, signed redirect URLs and machine paths are not embedded.

Read does not initiate another network request itself: its required independentlyVerifiedPackages input must come from the normal consumer's current authenticated FormalPackageSource calls. The later full candidate verifier must perform those reads before invoking this codec. Neither parser output nor a unit-test producer ID is candidate acceptance.

## File map

- Create tools/p10/ReleaseVerifier/FormalVerificationEvidence.cs: live collector, typed proof comparison and defensive evidence/package container.
- Create tools/p10/ReleaseVerifier.Tests/FormalVerificationEvidenceTests.cs: 35 cases using actual downloaded packages, 28 field mutations, a later independent download, missing proof sets and pre-network guards.
- Create this plan. Do not modify Platform packages/source/trust, old R2 or app runtime.

## Task 1: Observe the missing behavior

- [x] Add this exact test file before implementing the codec.

### tools/p10/ReleaseVerifier.Tests/FormalVerificationEvidenceTests.cs

```csharp
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
```

- [x] Add the following throwing declarations, then run the 35 focused cases. Expected: all fail with NotImplementedException, zero skips and no compilation or missing-credential failures.

### Throwing declaration stage

```csharp
namespace CP6.P10.ReleaseVerifier;

internal static class FormalVerificationEvidence
{
    internal static Task<CollectedFormalEvidence> CollectAsync(GitHubWorkflowIdentity producer, string feedReadToken,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();

    internal static S06Attestation Read(ReadOnlyMemory<byte> bytes, GitHubWorkflowIdentity producer,
        DateTimeOffset cutoff, IReadOnlyCollection<DownloadedNuGetPackage> independentlyVerifiedPackages) =>
        throw new NotImplementedException();
}

internal sealed class CollectedFormalEvidence
{
    internal IReadOnlyList<DownloadedNuGetPackage> Packages => throw new NotImplementedException();
    internal byte[] CopyBytes() => throw new NotImplementedException();
}
```

Run from tools/p10 with the established .NET 8 SDK and actual P10_FEED_READ_TOKEN in the process environment only:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~FormalVerificationEvidenceTests --logger "trx;LogFileName=formal-evidence-red.trx" --results-directory ../../artifacts/p10/formal-evidence-red
```

## Task 2: Implement the actual collector and typed reader

- [x] Replace the throwing declarations with this complete implementation.

### tools/p10/ReleaseVerifier/FormalVerificationEvidence.cs

```csharp
using System.Text.Json;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// A typed public summary of actual authenticated NuGet reads, not an acceptance capability.
// Read requires the caller's independently downloaded/verified package set; it never trusts a success flag alone.
internal static class FormalVerificationEvidence
{
    internal static async Task<CollectedFormalEvidence> CollectAsync(GitHubWorkflowIdentity producer, string feedReadToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireProducer(producer);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            var packages = new List<DownloadedNuGetPackage>();
            foreach (var id in S06ReleaseIdentity.PackageHashes.Keys.Order(StringComparer.Ordinal))
                packages.Add(await FormalPackageSource.DownloadAndVerifyAsync(id, feedReadToken, deadline.Token));
            var created = DateTimeOffset.UtcNow;
            var details = JsonSerializer.SerializeToElement(new
            {
                feedServiceIndex = FormalFeedPolicy.Index,
                sourceGitSha = S06ReleaseIdentity.Source,
                trustPolicySha256 = S06ReleaseIdentity.NuGetTrustHash,
                packages = packages.Select(p => Claim(p, p.RetrievedAtUtc)).ToArray()
            });
            var bytes = Create("FormalPackageVerification", producer, created, details);
            _ = Read(bytes, producer, DateTimeOffset.UtcNow, packages);
            return new(bytes, packages);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw Error("s06-packages-timeout");
        }
    }

    internal static S06Attestation Read(ReadOnlyMemory<byte> bytes, GitHubWorkflowIdentity producer,
        DateTimeOffset cutoff, IReadOnlyCollection<DownloadedNuGetPackage> independentlyVerifiedPackages)
    {
        var statement = S06InToto.Read(bytes, "FormalPackageVerification", producer, cutoff);
        try
        {
            var actual = Select(independentlyVerifiedPackages);
            var details = statement.Details;
            Exact(details, "feedServiceIndex", "sourceGitSha", "trustPolicySha256", "packages");
            Require(Text(details, "feedServiceIndex") == FormalFeedPolicy.Index &&
                Text(details, "sourceGitSha") == S06ReleaseIdentity.Source &&
                Text(details, "trustPolicySha256") == S06ReleaseIdentity.NuGetTrustHash, "s06-packages-identity");
            var claims = details.GetProperty("packages");
            Require(claims.ValueKind == JsonValueKind.Array && claims.GetArrayLength() == actual.Length, "s06-packages-set");
            for (var index = 0; index < actual.Length; index++)
            {
                var retrieved = Time(claims[index], "retrievedAtUtc");
                Require(retrieved >= actual[index].Proof.TimestampUtc && retrieved <= statement.CreatedAtUtc,
                    "s06-packages-time");
                // Retrieval time is a historical signed observation, not part of the immutable NuGet proof.
                // A clean consumer's current download will have a later retrieval time.
                Require(Canonical(claims[index]).AsSpan().SequenceEqual(Canonical(Claim(actual[index], retrieved))),
                    "s06-packages-proof");
            }
            return statement;
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("s06-packages-shape"); }
    }

    private static DownloadedNuGetPackage[] Select(IReadOnlyCollection<DownloadedNuGetPackage> packages)
    {
        Require(packages is not null && packages.Count == 7 && packages.All(p => p is not null), "s06-packages-set");
        var selected = packages!.OrderBy(p => p.Proof.PackageId, StringComparer.Ordinal).ToArray();
        Require(selected.Select(p => p.Proof.PackageId).SequenceEqual(
            S06ReleaseIdentity.PackageHashes.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal), "s06-packages-set");
        foreach (var package in selected)
            Require(package.Proof.Sha256 == S06ReleaseIdentity.PackageHashes[package.Proof.PackageId] &&
                package.FeedServiceIndex == FormalFeedPolicy.Index, "s06-packages-proof");
        return selected;
    }

    private static JsonElement Claim(DownloadedNuGetPackage package, DateTimeOffset retrievedAtUtc)
    {
        var proof = package.Proof;
        return JsonSerializer.SerializeToElement(new
        {
            packageId = proof.PackageId,
            version = proof.Version,
            sourceGitSha = proof.SourceGitSha,
            authorSignedPackageSha256 = proof.Sha256,
            publishedPackageSha256 = proof.Sha256,
            feedTransformation = "BytePreserving",
            signerFingerprint = proof.SignerFingerprint,
            spkiKeyId = proof.SpkiKeyId,
            publicCaTrusted = proof.PublicCaTrusted,
            internallyTrusted = proof.InternallyTrusted,
            timestampPolicyOid = proof.TimestampPolicyOid,
            timestampUtc = FormatTime(proof.TimestampUtc),
            timestampCertificateChainSha256 = proof.TimestampCertificateChainSha256,
            retrievedAtUtc = FormatTime(retrievedAtUtc)
        });
    }

    private static byte[] Canonical(JsonElement value) =>
        Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(value));
}

internal sealed class CollectedFormalEvidence
{
    private readonly byte[] _bytes;
    internal CollectedFormalEvidence(byte[] bytes, IEnumerable<DownloadedNuGetPackage> packages)
    {
        _bytes = bytes.ToArray();
        Packages = Array.AsReadOnly(packages.ToArray());
    }

    internal IReadOnlyList<DownloadedNuGetPackage> Packages { get; }
    internal byte[] CopyBytes() => _bytes.ToArray();
}
```

- [x] Run the focused and full Release tests with the existing actual GitHub/feed/package/cosign inputs, then format verification. Expected: 35 focused and 1015 total cases pass with zero skips.

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~FormalVerificationEvidenceTests
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore
dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --verify-no-changes --no-restore
```

## Task 3: Review and checkpoint

- [x] Compare both final files with their complete code blocks and review the three-file diff, including credential/path hygiene and the unchanged external authority boundary.
- [ ] Record actual red/green/full/format results, stage only this module and commit with native-command exit checks.

```powershell
git diff --check
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
git add -- docs/superpowers/plans/2026-09-08-p10-s06-formal-package-evidence.md tools/p10/ReleaseVerifier/FormalVerificationEvidence.cs tools/p10/ReleaseVerifier.Tests/FormalVerificationEvidenceTests.cs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
git diff --cached --check
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
git commit -m "feat(p10): bind formal package evidence to verified downloads"
```

- [ ] Complete the remaining typed evidence, full graph acceptance, protected validation/publication workflows, actual immutable candidate publication and pre/post cross-repository audit before claiming P10 completion.

## Observed component verification

On 2026-09-08 all 35 focused cases first failed with the deliberate NotImplementedException declarations (35 expected failures, zero unexpected failures or skips). The implementation then passed all 35 cases in 48 seconds using seven actual authenticated formal package reads plus a later independent Release package download. The complete Release suite passed 1015/1015 with zero skips in 47 seconds; format verification passed. Both code files exactly match their final plan blocks, and the three-file scope passed whitespace and sensitive/path checks.

This is a local, unsigned component checkpoint. No image, workflow, R2 object or Locator was created or published, no formal package was republished, and the complete P10 acceptance chain remains open.
