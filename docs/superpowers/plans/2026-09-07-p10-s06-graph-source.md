# P10 S06 authenticated candidate graph fetching Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans.
> Continue the owner's sequential current-task implementation without agents.

**Goal:** Fetch an exact bounded S06 evidence graph only after a fixed-root
Locator passes real cosign and pinned-policy authentication.

**Architecture:** The public discovery entry reads only the known Locator and
bundle before authentication; the intended-artifact entry authenticates before
creating any R2 client. An internal composition boundary requires the sealed
AuthenticatedLocator and supplies referenced bytes through the existing fixed R2
client. A shared byte-download owner independently checks every reference,
deduplicates only identical metadata and bounds total bytes/object count before
I/O. Existing package-owned schemas and S06 graph inspection remain authoritative.

**Tech Stack:** .NET 8, pinned CP6.Platform.Release 0.10.1, previously verified
cosign v3.1.3, fixed R2 consumer transport. No new package or lock change.

## Scope and required boundaries

- Public methods accept no alternate policy, evaluation time, network endpoint,
  HTTP handler, object supplier or authentication-success flag.
- Both methods have a five-minute linked deadline; each R2 request retains its
  sixty-second maximum and the cosign process retains its independent bound.
- The clean pre-commit job will call IntendedAsync with immutable artifact bytes.
  Post-commit discovery will call DiscoverAsync using the fixed path.
- Subject bytes must match the signed SHA-256/length and Platform media type.
  Candidate creation time must exactly equal the Locator's frozen creation time.
- Candidate schema and fixed release identity are validated before any evidence
  read; exactly eleven evidence records are required. Only known record payload
  references, provenance and gate references are fetched. URLs embedded inside
  SBOM, SARIF or third-party statements are never followed.
- All reads are sequential. The byte owner is scoped to one fetch and does not
  expose a concurrent cache. Aggregate limits include the candidate: at most
  33 objects (one candidate plus 32 references), 64 MiB and 4 MiB per object.
- The internal supplier seam is production composition used by the real R2
  adapter, not a public or CLI override. It supplies bytes only, never proof
  validity. Unit tests use it to assert byte limits/order independently from
  service availability; these fixtures are not R2 acceptance evidence.
- Tests create real ephemeral ECDSA signatures and invoke the actual pinned
  cosign binary through the existing internal policy-regression entry. The
  compiled public trust deliberately rejects those fixture roots.
- FetchedCandidateGraph always exposes CandidateAccepted=false. Structural
  completion is not full release acceptance; remote workflow/package/OCI/CRM
  proof verification remains mandatory.
- No publication operation is added or executed. Existing workflows, trust
  instances, schemas, dependency locks, runtime and deployment remain unchanged.

## File map

- Create `tools/p10/ReleaseVerifier/GraphObjectDownloads.cs`: bounded byte owner.
- Create `tools/p10/ReleaseVerifier/PlatformCandidateSource.cs`: authenticated
  discovery/intended-artifact entry points and closed-graph fetch composition.
- Create `tools/p10/ReleaseVerifier.Tests/GraphObjectDownloadsTests.cs`.
- Create `tools/p10/ReleaseVerifier.Tests/PlatformCandidateSourceTests.cs`.
- Create this plan.

## Task 1: Actual tests, then RED

- [x] Add the two complete test files below.

### GraphObjectDownloadsTests.cs

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

public sealed class GraphObjectDownloadsTests
{
    [Fact]
    public async Task Exact_reference_is_read_once_and_owned_copies_are_isolated()
    {
        var raw = "{}"u8.ToArray();
        var reference = ContentAddress.Create(raw, Cp6ReleaseMediaTypes.InToto, "proof.json");
        var reads = 0;
        var downloads = new GraphObjectDownloads((_, _) => { reads++; return Task.FromResult<byte[]?>(raw); });
        var first = await downloads.ReadAsync(reference, reference.MediaType, CancellationToken.None);
        first[0] ^= 1;
        raw[0] ^= 1;
        Assert.Equal("{}"u8.ToArray(), await downloads.ReadAsync(reference, reference.MediaType, CancellationToken.None));
        Assert.Equal(1, reads);
        var snapshot = downloads.SnapshotExcept("absent-candidate-key");
        Assert.Equal("{}"u8.ToArray(), snapshot[reference.Key].ToArray());
        Assert.Empty(downloads.SnapshotExcept(reference.Key));
    }

    [Fact]
    public async Task Wrong_expected_media_fails_before_the_byte_supplier()
    {
        var calls = 0;
        var reference = ContentAddress.Create("{}"u8, Cp6ReleaseMediaTypes.InToto, "proof.json");
        var downloads = new GraphObjectDownloads((_, _) => { calls++; return Task.FromResult<byte[]?>(null); });
        await Error("graph-download-media", () => downloads.ReadAsync(reference, Cp6ReleaseMediaTypes.Spdx, CancellationToken.None));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("missing", "graph-download-missing")]
    [InlineData("length", "graph-download-size")]
    [InlineData("hash", "graph-download-hash")]
    public async Task Missing_changed_or_truncated_bytes_cannot_pass(string change, string code)
    {
        var reference = ContentAddress.Create("{}"u8, Cp6ReleaseMediaTypes.InToto, "proof.json");
        byte[]? supplied = change == "missing" ? null : change == "length" ? "{"u8.ToArray() : "[]"u8.ToArray();
        var downloads = new GraphObjectDownloads((_, _) => Task.FromResult(supplied));
        await Error(code, () => downloads.ReadAsync(reference, reference.MediaType, CancellationToken.None));
    }

    [Fact]
    public async Task Duplicate_key_with_conflicting_signed_metadata_is_rejected_without_refetch()
    {
        var raw = "{}"u8.ToArray();
        var reference = ContentAddress.Create(raw, Cp6ReleaseMediaTypes.InToto, "proof.json");
        var calls = 0;
        var downloads = new GraphObjectDownloads((_, _) => { calls++; return Task.FromResult<byte[]?>(raw); });
        _ = await downloads.ReadAsync(reference, reference.MediaType, CancellationToken.None);
        var node = JsonNode.Parse(reference.ToJson().GetRawText())!.AsObject();
        node["mediaType"] = Cp6ReleaseMediaTypes.Spdx;
        var changed = ContentAddress.Parse(JsonSerializer.SerializeToElement(node));
        await Error("graph-download-reference", () => downloads.ReadAsync(changed, changed.MediaType, CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Object_count_is_reserved_before_the_next_external_read()
    {
        var raw = "{}"u8.ToArray();
        var calls = 0;
        var downloads = new GraphObjectDownloads((_, _) => { calls++; return Task.FromResult<byte[]?>(raw); });
        for (var index = 0; index < 33; index++)
            _ = await downloads.ReadAsync(ContentAddress.Create(raw, Cp6ReleaseMediaTypes.InToto,
                "proof-" + index + ".json"), Cp6ReleaseMediaTypes.InToto, CancellationToken.None);
        var extra = ContentAddress.Create(raw, Cp6ReleaseMediaTypes.InToto, "extra.json");
        await Error("graph-download-count", () => downloads.ReadAsync(extra, extra.MediaType, CancellationToken.None));
        Assert.Equal(33, calls);
    }

    [Fact]
    public async Task Aggregate_64MiB_limit_is_reserved_before_the_next_read()
    {
        var raw = new byte[Cp6DeterministicJson.MaximumBytes];
        var calls = 0;
        var downloads = new GraphObjectDownloads((_, _) => { calls++; return Task.FromResult<byte[]?>(raw); });
        for (var index = 0; index < 16; index++)
            _ = await downloads.ReadAsync(ContentAddress.Create(raw, Cp6ReleaseMediaTypes.InToto,
                "proof-" + index + ".json"), Cp6ReleaseMediaTypes.InToto, CancellationToken.None);
        var extra = ContentAddress.Create("x"u8, Cp6ReleaseMediaTypes.InToto, "extra.json");
        await Error("graph-download-budget", () => downloads.ReadAsync(extra, extra.MediaType, CancellationToken.None));
        Assert.Equal(16, calls);
    }

    [Fact]
    public async Task Cancellation_is_propagated_and_prevents_any_byte_read()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;
        var downloads = new GraphObjectDownloads((_, _) => { calls++; return Task.FromResult<byte[]?>(null); });
        var reference = ContentAddress.Create("{}"u8, Cp6ReleaseMediaTypes.InToto, "proof.json");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloads.ReadAsync(reference, reference.MediaType, cancellation.Token));
        Assert.Equal(0, calls);
    }

    private static async Task Error(string code, Func<Task> action) =>
        Assert.Equal(code, (await Assert.ThrowsAsync<Cp6ReleaseContractException>(action)).Code);
}
```

### PlatformCandidateSourceTests.cs

```csharp
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
```

- [x] Run the focused tests with the previously hash-verified cosign path in
  `P10_COSIGN_PATH`. Missing APIs are expected initially. Add only throwing
  API scaffolds to obtain actual execution failures; inspect the RED TRX and
  require every failure to be missing behavior, with zero skips or argument errors.

From `tools/p10` using the installed .NET 8 SDK selected by global.json:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --configuration Release --filter 'FullyQualifiedName~GraphObjectDownloadsTests|FullyQualifiedName~PlatformCandidateSourceTests' --logger 'trx;LogFileName=s06-graph-source-red.trx' --results-directory ../../artifacts/p10/graph-source-red --verbosity minimal
```

## Task 2: Exact implementation

- [x] Replace the throwing scaffolds with these two complete files.

### GraphObjectDownloads.cs

```csharp
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Shared production byte boundary for the R2 adapter; the delegate supplies bytes, never verification success.
internal sealed class GraphObjectDownloads(Func<ContentAddress, CancellationToken, Task<byte[]?>> read)
{
    private readonly Dictionary<string, (ContentAddress Address, byte[] Bytes)> _objects = new(StringComparer.Ordinal);
    private long _reservedBytes;

    internal async Task<byte[]> ReadAsync(ContentAddress address, string mediaType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (address.MediaType != mediaType) throw Error("graph-download-media");
        if (_objects.TryGetValue(address.Key, out var existing))
        {
            if (existing.Address.MediaType != address.MediaType || existing.Address.Sha256 != address.Sha256 ||
                existing.Address.ByteLength != address.ByteLength) throw Error("graph-download-reference");
            return existing.Bytes.ToArray();
        }
        // One candidate plus at most 32 referenced objects; reserve the entire declared size before I/O.
        if (_objects.Count >= 33) throw Error("graph-download-count");
        if (_reservedBytes + address.ByteLength > 64L * 1024 * 1024) throw Error("graph-download-budget");
        _reservedBytes += address.ByteLength;
        var received = await read(address, cancellationToken) ?? throw Error("graph-download-missing");
        cancellationToken.ThrowIfCancellationRequested();
        if (received.Length != address.ByteLength) throw Error("graph-download-size");
        var bytes = received.ToArray();
        if (Cp6DeterministicJson.Sha256Hex(bytes) != address.Sha256) throw Error("graph-download-hash");
        _objects.Add(address.Key, (address, bytes));
        return bytes.ToArray();
    }

    internal IReadOnlyDictionary<string, ReadOnlyMemory<byte>> SnapshotExcept(string candidateKey) =>
        _objects.Where(pair => pair.Key != candidateKey).ToDictionary(pair => pair.Key,
            pair => (ReadOnlyMemory<byte>)pair.Value.Bytes.ToArray(), StringComparer.Ordinal);

    private static Cp6ReleaseContractException Error(string code) => new(code, "Graph download violates the signed reference and aggregate limits.");
}
```

### PlatformCandidateSource.cs

```csharp
using System.Globalization;
using System.Text.Json;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06ReleaseIdentity;

namespace CP6.P10.ReleaseVerifier;

// Fetching and structural inspection only. Live evidence proof validation is a separate mandatory layer.
public static class PlatformCandidateSource
{
    public static async Task<FetchedCandidateGraph> DiscoverAsync(string releaseTag, string readAccessKeyId,
        string readSecret, CosignBlobVerifier verifier, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var locatorTarget = R2ObjectTarget.Discovery(releaseTag, R2DiscoveryPart.Locator);
        var bundleTarget = R2ObjectTarget.Discovery(releaseTag, R2DiscoveryPart.Bundle);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            using var client = R2ObjectClient.Consumer(readAccessKeyId, readSecret);
            var locatorBytes = await client.ReadAsync(locatorTarget, deadline.Token) ?? throw Error("source-locator-missing");
            var bundleBytes = await client.ReadAsync(bundleTarget, deadline.Token) ?? throw Error("source-bundle-missing");
            var locator = await AuthenticatedLocator.AuthenticateAsync(releaseTag, locatorBytes, bundleBytes, verifier, deadline.Token);
            return await FetchAuthenticatedAsync(locator, (reference, token) =>
                client.ReadAsync(R2ObjectTarget.Addressed(reference), token), deadline.Token);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw Error("source-timeout");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { throw Error("source-read"); }
    }

    // For the clean pre-commit job: intended immutable artifact bytes are authenticated before any R2 read.
    public static async Task<FetchedCandidateGraph> IntendedAsync(string releaseTag, ReadOnlyMemory<byte> locatorBytes,
        ReadOnlyMemory<byte> bundleBytes, string readAccessKeyId, string readSecret, CosignBlobVerifier verifier,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            var locator = await AuthenticatedLocator.AuthenticateAsync(releaseTag, locatorBytes, bundleBytes, verifier, deadline.Token);
            using var client = R2ObjectClient.Consumer(readAccessKeyId, readSecret);
            return await FetchAuthenticatedAsync(locator, (reference, token) =>
                client.ReadAsync(R2ObjectTarget.Addressed(reference), token), deadline.Token);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw Error("source-timeout");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { throw Error("source-read"); }
    }

    // This core requires a sealed authenticated root, not a caller-provided authentication boolean.
    // Public entry points always use compiled trust, actual cosign and the fixed real R2 transport.
    internal static async Task<FetchedCandidateGraph> FetchAuthenticatedAsync(AuthenticatedLocator locator,
        Func<ContentAddress, CancellationToken, Task<byte[]?>> read, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var downloads = new GraphObjectDownloads(read);
        var bytes = await downloads.ReadAsync(locator.Subject, Cp6ReleaseMediaTypes.PlatformReleaseCandidate, cancellationToken);
        _ = Cp6ReleaseValidator.ValidatePlatformCandidate(bytes);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        RequireCandidate(root);
        Require(Text(root, "createdAtUtc") == locator.CreatedAtUtc.UtcDateTime.ToString(
            "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture), "source-locator-time");
        var records = root.GetProperty("evidence");
        Require(records.GetArrayLength() == S06EvidenceBindings.MediaTypes.Count, "graph-evidence-set");
        foreach (var reference in records.EnumerateArray())
        {
            var recordBytes = await downloads.ReadAsync(ContentAddress.Parse(reference),
                Cp6ReleaseMediaTypes.EvidenceRecord, cancellationToken);
            _ = Cp6SupportingContractValidator.ValidateEvidenceRecord(recordBytes);
            using var record = JsonDocument.Parse(recordBytes);
            var kind = Text(record.RootElement, "evidenceKind");
            Require(S06EvidenceBindings.MediaTypes.TryGetValue(kind, out var media), "graph-evidence-set");
            _ = await downloads.ReadAsync(ContentAddress.Parse(record.RootElement.GetProperty("object")), media!, cancellationToken);
        }
        _ = await downloads.ReadAsync(ContentAddress.Parse(root.GetProperty("buildProvenance")),
            Cp6ReleaseMediaTypes.BuildInvocationProvenance, cancellationToken);
        _ = await downloads.ReadAsync(ContentAddress.Parse(root.GetProperty("releaseGateResult")),
            Cp6ReleaseMediaTypes.ReleaseGateResult, cancellationToken);
        var graph = EvidenceGraphInspection.Inspect(bytes, downloads.SnapshotExcept(locator.Subject.Key));
        return new(locator, graph);
    }

    private static Cp6ReleaseContractException Error(string code) => new(code, "Platform candidate discovery did not complete under pinned trust.");
}

public sealed class FetchedCandidateGraph
{
    internal FetchedCandidateGraph(AuthenticatedLocator locator, InspectedEvidenceGraph graph)
    {
        Locator = locator;
        Graph = graph;
    }
    public AuthenticatedLocator Locator { get; }
    public InspectedEvidenceGraph Graph { get; }
    public bool CandidateAccepted => false;
}
```

## Task 3: Verify and commit

- [x] Rerun all new tests with a GREEN TRX path and zero skipped cases.
- [x] Run all tests with the existing mandatory cosign, seven real S04 package
  archives and in-memory GitHub feed-read credential. Never print the credential.
- [x] Verify formatting, locked restore, exact plan/source equality and complete
  scoped diff/hygiene. Existing dependency and trust hashes remain unchanged.
- [ ] Stage only the five scoped files and create an audited normal commit.

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --configuration Release --verbosity minimal
dotnet format whitespace ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --verify-no-changes
dotnet restore ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --configfile ../../eng/p10/NuGet.formal.config --packages ../../artifacts/p10/foundation-restore/packages --locked-mode --verbosity minimal
```

From the task root:

```powershell
git diff --check
git add -- docs/superpowers/plans/2026-09-07-p10-s06-graph-source.md tools/p10/ReleaseVerifier/GraphObjectDownloads.cs tools/p10/ReleaseVerifier/PlatformCandidateSource.cs tools/p10/ReleaseVerifier.Tests/GraphObjectDownloadsTests.cs tools/p10/ReleaseVerifier.Tests/PlatformCandidateSourceTests.cs
git diff --cached --check
git diff --cached --stat
git commit -m "feat(p10): fetch evidence only through authenticated locators"
```

## Remaining S06, not a completion claim

- [ ] Finish live evidence-proof validators, assembly, immutable OCI proof and
  protected workflows, real R2 create/conflict/readback/pre-post verification,
  cross-repository audit and state closure before declaring P10 complete.

## Observed component verification (2026-09-08 UTC)

- Initial compile reported the missing APIs and one nullable Task inference in
  a test supplier; its return type was made explicit before execution. Throwing
  scaffolds then produced 21 actual NotImplementedException failures, with no
  unexpected failure or skipped case. The implementation passed all 21 tests.
- The positive composition fixture invoked actual pinned cosign verification,
  fetched 24 distinct bounded objects and inspected 11 evidence records, while
  retaining CandidateAccepted=false. The compiled-trust public entry rejected
  that same fixture identity before creating an R2 client.
- Full Release regression passed 407/407, zero skips, warnings or errors.
  Formatting, locked restore and all four exact source/plan comparisons passed.
- No new R2 request or write, OCI push, candidate or Locator publication occurred.
  External acceptance remains a separate outstanding gate.
