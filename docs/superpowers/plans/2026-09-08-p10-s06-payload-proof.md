# P10 S06 real payload proof orchestration implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans task-by-task. The owner selected inline sequential execution; no subagents.

**Goal:** Join the implemented formal NuGet, OCI, source, CRM-public-evidence, SBOM and scan checks behind one real-transport entry point.

**Architecture:** A sealed result with a private constructor is produced only after all eleven payloads satisfy compiled release pins and actual external cryptographic/registry/source checks. The result does not authenticate a completed S06 producer or a Locator; chronology, producer completion and authoritative discovery remain mandatory in the candidate entry point.

**Tech Stack:** .NET 8.0.424, pinned CP6.Platform.Release 0.10.1, native pinned cosign 3.1.3, existing exact-authority transports, xUnit.

---

## Scope

- Continue the existing isolated S06 branch. No package/lock/trust/workflow/runtime changes in this module.
- Freshly download and verify all seven packages on every independent read. Do not reuse producer claims as a substitute for current package bytes.
- Authenticate the native OCI bundle under compiled trust before registry/feed access, read the actual GHCR manifest by digest, and verify the bound native SPDX/SARIF plus image provenance.
- The ordinary consumer needs public GitHub and package/registry read credentials, not the private CRM credential. It consumes the pinned sanitized CRM public payload.
- Check the exact S04 completed workflow and current protected-main ancestry for Platform/public CP6 using real bounded read transports.
- No injected handler, trust policy, clock, success boolean or unsigned positive acceptance test is added.
- Negative orchestration tests are executable now. A positive S06 orchestration test requires the still-unpublished real protected S06 image/evidence and will be exercised in validation/publication; unit vectors cannot close that acceptance item.
- No external writes, deployment, new credentials or old R2 gate changes are authorized by this module.

## File map

- Create: `tools/p10/ReleaseVerifier/S06PayloadProof.cs` — bounded real proof orchestration and its sealed result.
- Create: `tools/p10/ReleaseVerifier.Tests/S06PayloadProofTests.cs` — fail-closed orchestration boundaries.
- Create: `docs/superpowers/plans/2026-09-08-p10-s06-payload-proof.md` — this plan and execution evidence.

### Task 1: Write rejection tests and observe RED

- [x] Write this test file and a throwing `VerifyAsync` scaffold with the exact implementation signature.
- [x] Run the focused suite; require every new test to fail because of `NotImplementedException`, not compilation or environment errors.

```csharp
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// These are rejection-boundary tests only. The fixture's unsigned envelopes cannot authenticate a candidate.
// Full positive orchestration must pass against the real protected S06 run and OCI artifact.
public sealed class S06PayloadProofTests
{
    private static GitHubWorkflowIdentity Producer => new("GTX537/CP6", S06ReleaseIdentity.ValidationPath,
        new string('5', 40), 999991, 1, EvidenceGraphFixture.PublicSource);
    private static string Digest => "sha256:" + new string('a', 64);

    [Theory]
    [InlineData("missing", "s06-proof-set")]
    [InlineData("extra", "s06-proof-set")]
    [InlineData("empty", "s06-proof-size")]
    [InlineData("oversize", "s06-proof-size")]
    [InlineData("publication", "s06-proof-baseline")]
    [InlineData("provenance", "s06-proof-baseline")]
    [InlineData("nuget-trust", "s06-proof-baseline")]
    [InlineData("trust", "s06-proof-baseline")]
    public async Task Incomplete_or_unpinned_inputs_fail_before_external_proof_work(string mutation, string code)
    {
        var payloads = Payloads();
        if (mutation == "missing") payloads.Remove("ImageScan");
        if (mutation == "extra") payloads.Add("Optional", "{}"u8.ToArray());
        if (mutation == "empty") payloads["ImageScan"] = ReadOnlyMemory<byte>.Empty;
        if (mutation == "oversize") payloads["ImageScan"] = new byte[4194305];
        if (mutation == "publication") payloads["FormalPackagePublication"] = "{}"u8.ToArray();
        if (mutation == "provenance") payloads["PackageProvenance"] = "{}"u8.ToArray();
        if (mutation == "nuget-trust") payloads["NuGetTrustPolicy"] = "{}"u8.ToArray();
        if (mutation == "trust") payloads["TrustPolicy"] = "{}"u8.ToArray();
        var error = await Assert.ThrowsAsync<Cp6ReleaseContractException>(() => Verify(payloads));
        Assert.Equal(code, error.Code);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task A_structurally_consistent_assembled_artifact_does_not_authenticate_unsigned_evidence()
    {
        var payloads = Payloads();
        var artifact = S06ArtifactAssembly.Create(Producer, Digest, payloads);
        Assert.False(artifact.EvidenceAuthenticated);
        var error = await Assert.ThrowsAsync<Cp6ReleaseContractException>(() => Verify(payloads));
        Assert.StartsWith("s06-attestation-", error.Code, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task The_future_cannot_be_selected_as_the_evidence_cutoff() =>
        Assert.Equal("s06-proof-cutoff", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            S06PayloadProof.VerifyAsync(Producer, Digest, Payloads(), DateTimeOffset.UtcNow.AddDays(1),
                new("missing-cosign"), "not-a-token", "not-a-token"))).Code);

    [Fact]
    public async Task A_publication_job_cannot_claim_to_be_the_validation_producer() =>
        Assert.Equal("s06-attestation-producer", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            S06PayloadProof.VerifyAsync(Producer with { WorkflowPath = S06ReleaseIdentity.PublicationPath },
                Digest, Payloads(), DateTimeOffset.UtcNow, new("missing-cosign"), "not-a-token", "not-a-token"))).Code);

    [Fact]
    public async Task A_precancelled_proof_performs_no_external_reads()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            S06PayloadProof.VerifyAsync(Producer, Digest, Payloads(), DateTimeOffset.UtcNow,
                new("missing-cosign"), "not-a-token", "not-a-token", cancellation.Token));
    }

    private static Task<S06PayloadProof> Verify(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> payloads) =>
        S06PayloadProof.VerifyAsync(Producer, Digest, payloads, DateTimeOffset.UtcNow,
            new("missing-cosign"), "not-a-token", "not-a-token");

    private static Dictionary<string, ReadOnlyMemory<byte>> Payloads()
    {
        var fixture = EvidenceGraphFixture.Build();
        return EvidenceGraphInspection.Inspect(fixture.Candidate, fixture.Objects).Evidence
            .ToDictionary(p => p.Key, p => (ReadOnlyMemory<byte>)p.Value.CopyPayloadBytes(), StringComparer.Ordinal);
    }
}
```

### Task 2: Connect the existing real verifiers

- [x] Replace the scaffold with this implementation after recording RED.

```csharp
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Real payload proofs only. This is not proof of a completed S06 producer or a committed Locator.
// No caller-supplied trust policy, transport, clock, success flag or private CRM token is accepted.
internal sealed class S06PayloadProof
{
    private S06PayloadProof(GitHubWorkflowIdentity producer, string digest,
        AuthenticatedOciSignature signature, GitHubWorkflowObservation formalWorkflow)
    {
        Producer = producer;
        ImageDigest = digest;
        OciSignature = signature;
        FormalWorkflow = formalWorkflow;
        ObservedAtUtc = DateTimeOffset.UtcNow;
    }

    internal GitHubWorkflowIdentity Producer { get; }
    internal string ImageDigest { get; }
    internal AuthenticatedOciSignature OciSignature { get; }
    internal GitHubWorkflowObservation FormalWorkflow { get; }
    internal DateTimeOffset ObservedAtUtc { get; }

    internal static async Task<S06PayloadProof> VerifyAsync(GitHubWorkflowIdentity producer, string imageDigest,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> payloads, DateTimeOffset createdBeforeUtc,
        CosignBlobVerifier cosign, string githubReadToken, string feedReadToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireProducer(producer);
        OciWirePolicy.RequireDigest(imageDigest);
        GitHubEvidenceChecks.RequireCutoff(createdBeforeUtc);
        Require(createdBeforeUtc <= DateTimeOffset.UtcNow, "s06-proof-cutoff");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(8));
        try
        {
            Require(payloads.Count == S06EvidenceBindings.MediaTypes.Count &&
                payloads.Keys.Order(StringComparer.Ordinal).SequenceEqual(
                    S06EvidenceBindings.MediaTypes.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal),
                "s06-proof-set");
            Require(payloads.Values.All(p => p.Length is > 0 and <= Cp6DeterministicJson.MaximumBytes),
                "s06-proof-size");
            var owned = payloads.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal);
            RequireHash(owned["FormalPackagePublication"], S06ReleaseIdentity.PublicationHash);
            RequireHash(owned["PackageProvenance"], S06ReleaseIdentity.ProvenanceHash);
            RequireHash(owned["TrustPolicy"], VerifierTrust.Load().ValidatedDocument.Sha256);
            var nugetTrust = PinnedNuGetTrust.Load();
            RequireHash(owned["NuGetTrustPolicy"], nugetTrust.ValidatedDocument.Sha256);
            _ = Cp6FormalPackagePublicationValidator.ValidateFormalPackagePublication(
                owned["FormalPackagePublication"], nugetTrust, DateTimeOffset.UtcNow);
            _ = Cp6SupportingContractValidator.ValidateBuildInvocationProvenance(owned["PackageProvenance"]);

            // Reject malformed/unsigned public statements before reading either registry or feed.
            _ = SourceReferenceEvidence.Read(owned["SourceReference"], producer, createdBeforeUtc);
            _ = CrmPublicEvidence.Read(owned["CrmConsumer"], producer, createdBeforeUtc);
            _ = S06InToto.Read(owned["FormalPackageVerification"], "FormalPackageVerification", producer, createdBeforeUtc);
            _ = S06InToto.Read(owned["ImageProvenance"], "ImageProvenance", producer, createdBeforeUtc, imageDigest);
            var bundle = OciSignatureEvidence.ReadBundleForAuthentication(owned["OciSignature"],
                producer, createdBeforeUtc, imageDigest);
            var signature = await AuthenticatedOciSignature.AuthenticateAsync(
                imageDigest, producer, bundle, cosign, deadline.Token);
            _ = OciSignatureEvidence.RequireAuthenticated(owned["OciSignature"], producer,
                createdBeforeUtc, imageDigest, signature);

            // Every independent verification downloads fresh bytes from the sole formal feed.
            var packages = new List<DownloadedNuGetPackage>();
            foreach (var id in S06ReleaseIdentity.PackageHashes.Keys.Order(StringComparer.Ordinal))
                packages.Add(await FormalPackageSource.DownloadAndVerifyAsync(id, feedReadToken, deadline.Token));
            _ = FormalVerificationEvidence.Read(owned["FormalPackageVerification"], producer, createdBeforeUtc, packages);
            var release = packages.Single(p => p.Proof.PackageId == S06ReleaseIdentity.ReleasePackage).Proof;
            var image = await OciImageSource.ReadAsync(imageDigest, feedReadToken, deadline.Token);
            _ = ImageProvenanceEvidence.Read(owned["ImageProvenance"], producer, createdBeforeUtc,
                image.CopyBytes(), imageDigest, image.Description.MediaType, release, owned["ImageSbom"], owned["ImageScan"]);

            _ = await GitHubEvidenceSource.ReadSourceAsync("GTX537/CP6.Platform",
                S06ReleaseIdentity.Source, githubReadToken, deadline.Token);
            _ = await GitHubEvidenceSource.ReadSourceAsync("GTX537/CP6", producer.CommitSha,
                githubReadToken, deadline.Token);
            var formal = await GitHubEvidenceSource.ReadWorkflowAsync(S06WorkflowProfile.FormalPublication,
                S06WorkflowProfile.FormalJobs, createdBeforeUtc, githubReadToken, deadline.Token);
            return new(producer, imageDigest, signature, formal);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw Error("s06-proof-timeout");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { throw Error("s06-proof-failed"); }
    }

    private static void RequireHash(ReadOnlyMemory<byte> bytes, string expected) =>
        Require(Cp6DeterministicJson.Sha256Hex(bytes.Span) == expected, "s06-proof-baseline");
}
```

### Task 3: Verify and commit the bounded module

- [x] Run the commands below from `tools/p10`; require focused/full tests with zero failures/skips and clean formatting.
- [x] Compare plan code blocks to source, review the complete three-file diff and scan for secret/scope residue.
- [ ] Stage only the three named files and commit `feat(p10): verify all payloads through real trust and read paths`.
- [ ] Complete real positive orchestration in the protected S06 workflows. This remains an acceptance dependency, not an assumed unit-test success.

```powershell
$env:DOTNET_ROOT = 'C:/Users/tt/.dotnet'
$env:DOTNET_HOST_PATH = 'C:/Users/tt/.dotnet/dotnet.exe'
$env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH
$env:P10_COSIGN_PATH = 'C:/Users/tt/AppData/Local/Temp/cp6-p10-cosign-c4ab1329239f4721a3f8f5ac741ce3c9/cosign-windows-amd64.exe'
$env:P10_FORMAL_PACKAGE_ROOT = 'D:/CP6.Platform-worktrees/p10-formal-schema-parity/artifacts/p10-0.10.1-publication/windows/feed-readback-packages'
$env:P10_GITHUB_READ_TOKEN = gh auth token
$env:P10_FEED_READ_TOKEN = $env:P10_GITHUB_READ_TOKEN
try {
  & $env:DOTNET_HOST_PATH test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~S06PayloadProofTests
  if ($LASTEXITCODE -ne 0) { throw 'Focused verification failed.' }
  & $env:DOTNET_HOST_PATH test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore
  if ($LASTEXITCODE -ne 0) { throw 'Full verification failed.' }
  & $env:DOTNET_HOST_PATH format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --verify-no-changes
  if ($LASTEXITCODE -ne 0) { throw 'Format verification failed.' }
} finally {
  Remove-Item Env:P10_GITHUB_READ_TOKEN -ErrorAction SilentlyContinue
  Remove-Item Env:P10_FEED_READ_TOKEN -ErrorAction SilentlyContinue
}
```

```powershell
git diff --check
git add -- docs/superpowers/plans/2026-09-08-p10-s06-payload-proof.md tools/p10/ReleaseVerifier/S06PayloadProof.cs tools/p10/ReleaseVerifier.Tests/S06PayloadProofTests.cs
git diff --cached --check
git diff --cached --stat
git commit -m "feat(p10): verify all payloads through real trust and read paths"
```

## Self-review

- [x] Parent design sections 12 and 16 covered at the payload layer; workflow completion/discovery remain separate explicit prerequisites.
- [x] Real feed, native crypto, GHCR and GitHub paths are reused without trust or transport injection.
- [x] Tests do not manufacture a successful S06 proof or confuse structure with authentication.

## Verification outcome (2026-09-08)

- RED: 12 failed, 0 passed, 0 skipped, all expected `NotImplementedException` after compilation.
- Focused GREEN: 12 passed, 0 failed, 0 skipped.
- Initial full suite: 1,148 passed, 114 failed, 0 skipped. Failures originated in the unchanged live NuGet `feed-timeout` and GitHub `github-timeout` boundaries; shared lazy historical reads propagated those failures across dependent cases. The full suite was not treated as successful and formatting had not yet run.
- Investigation confirmed no changes in the transports or affected existing tests. GitHub rate/API readback remained available. Without changing code or timeout settings, isolated real package/CRM reads passed 8/8 in 1 minute 31 seconds.
- Unchanged full rerun: 1,262 passed, 0 failed, 0 skipped in 1 minute 3 seconds; formatting then exited 0.
- Both plan code blocks exactly match the new source/test files. No S06 positive remote proof, OCI/R2 write or publication is claimed here.
