# P10 S06 complete candidate verification entry implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans task-by-task. The owner selected inline sequential execution; no subagents.

**Goal:** Expose current non-deployable candidate verification only through authenticated R2 discovery, complete real evidence and completed successful producer/publisher workflows.

**Architecture:** A common evidence method joins real payload proofs with completed validation and chronology. A public sealed result with a private constructor always discovers the fixed signed R2 Locator and requires a completed publisher; a separate internal confirmation captures the actual in-progress publisher for clean pre/post-commit checks and never claims its final success.

**Tech Stack:** .NET 8.0.424, pinned CP6.Platform.Release 0.10.1, existing native cosign and fixed R2/GitHub/feed transports, xUnit.

---

## Scope

- Continue the isolated S06 branch. Do not change existing pinned contracts, transport policies, workflow profiles or credentials.
- Normal verification has no local/intended Locator mode, current-publisher flag, trust override, clock override or injected transport.
- Require the completed publication job, completed validation before publication start, all real payload proofs, exact workflow identities and evidence time windows.
- The normal result is `VerifiedNonDeployable` with `deployable=false`. It is not an external audit-ledger transition to `Frozen`; the cross-repository audit and project records remain separate required work.
- Current-publication confirmation captures its own live context and does not permit a caller to supply a serialized context or completion assertion.
- Unit coverage is rejection-boundary coverage. Positive S06 acceptance still requires the actual protected workflows, signed OCI evidence and authoritative R2 Locator; no fixture may close that gate.
- No S06 external writes, production deployment, old R2 gate changes or release identity fabrication in this module.

## Files

- Create: `tools/p10/ReleaseVerifier/S06CandidateEvidence.cs`
- Create: `tools/p10/ReleaseVerifier/VerifiedPlatformCandidate.cs`
- Create: `tools/p10/ReleaseVerifier/S06PublicationConfirmation.cs`
- Create: `tools/p10/ReleaseVerifier.Tests/S06CandidateVerifierTests.cs`
- Create: `docs/superpowers/plans/2026-09-08-p10-s06-candidate-verifier.md`.

### Task 1: Write rejection tests and observe RED

- [x] Write the test file and throwing API scaffolds with the exact method signatures below.
- [x] Run the focused suite and confirm every new test fails from missing behavior, not a compilation error or skip.

```csharp
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Rejection boundaries only. Positive acceptance needs a real signed R2 Locator plus completed protected workflows.
public sealed class S06CandidateVerifierTests
{
    [Theory]
    [InlineData("../v0.10.1")]
    [InlineData("v0.10.1\n")]
    [InlineData("v0.10.1/other")]
    [InlineData("")]
    public async Task Normal_verification_rejects_unsafe_discovery_before_any_external_read(string tag) =>
        await Assert.ThrowsAsync<Cp6ReleaseContractException>(() => VerifiedPlatformCandidate.VerifyAsync(
            tag, "not-an-access-id", "not-a-secret", new("missing-cosign"), "not-a-token", "not-a-token"));

    [Fact]
    public async Task Normal_verification_has_no_anonymous_credential_fallback() =>
        await Assert.ThrowsAsync<Cp6ReleaseContractException>(() => VerifiedPlatformCandidate.VerifyAsync(
            "v0.10.1", "", "", new("missing-cosign"), "not-a-token", "not-a-token"));

    [Fact]
    public async Task Structural_inspection_cannot_substitute_for_the_real_payload_and_completed_producer_proofs()
    {
        var graph = Graph();
        Assert.False(graph.CandidateAccepted);
        var error = await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            S06CandidateEvidence.VerifyAsync(graph, DateTimeOffset.UtcNow, new("missing-cosign"), "not-a-token", "not-a-token"));
        Assert.StartsWith("s06-attestation-", error.Code, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task A_future_publication_start_does_not_authorize_evidence_verification() =>
        Assert.Equal("s06-candidate-cutoff", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            S06CandidateEvidence.VerifyAsync(Graph(), DateTimeOffset.UtcNow.AddDays(1),
                new("missing-cosign"), "not-a-token", "not-a-token"))).Code);

    [Fact]
    public async Task Precancelled_normal_verification_performs_no_external_read()
    {
        using var cancellation = Cancelled();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VerifiedPlatformCandidate.VerifyAsync(
            "v0.10.1", "", "", new("missing-cosign"), "", "", cancellation.Token));
    }

    [Fact]
    public async Task Precancelled_evidence_verification_performs_no_external_read()
    {
        using var cancellation = Cancelled();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => S06CandidateEvidence.VerifyAsync(
            Graph(), DateTimeOffset.UtcNow, new("missing-cosign"), "", "", cancellation.Token));
    }

    [Fact]
    public async Task Precancelled_publication_confirmation_does_not_capture_or_predict_a_publisher()
    {
        using var cancellation = Cancelled();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => S06PublicationConfirmation.VerifyAsync(
            null!, new("missing-cosign"), "", "", cancellation.Token));
    }

    private static InspectedEvidenceGraph Graph()
    {
        var fixture = EvidenceGraphFixture.Build();
        return EvidenceGraphInspection.Inspect(fixture.Candidate, fixture.Objects);
    }

    private static CancellationTokenSource Cancelled()
    {
        var result = new CancellationTokenSource();
        result.Cancel();
        return result;
    }
}
```

### Task 2: Connect the proof paths

- [x] Replace the scaffolds with the following code only after RED.

#### tools/p10/ReleaseVerifier/S06CandidateEvidence.cs

```csharp
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Common real evidence/producer verification. The caller must independently authenticate the discovery root.
// This method accepts neither a successful-workflow assertion nor replacement trust/transport components.
internal static class S06CandidateEvidence
{
    internal static async Task<GitHubWorkflowObservation> VerifyAsync(InspectedEvidenceGraph graph,
        DateTimeOffset publicationStartedAtUtc, CosignBlobVerifier cosign, string githubReadToken,
        string feedReadToken, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GitHubEvidenceChecks.RequireCutoff(publicationStartedAtUtc);
        Require(publicationStartedAtUtc <= DateTimeOffset.UtcNow, "s06-candidate-cutoff");
        var root = graph.Candidate;
        var producer = S06WorkflowProfile.Read(root.GetProperty("verifier"), S06ReleaseIdentity.ValidationPath);
        var digest = Text(root.GetProperty("images")[0], "sha256OrDigest");
        var payloads = graph.Evidence.ToDictionary(p => p.Key,
            p => (ReadOnlyMemory<byte>)p.Value.CopyPayloadBytes(), StringComparer.Ordinal);
        var proof = await S06PayloadProof.VerifyAsync(producer, digest, payloads, Time(graph.Gate, "createdAtUtc"),
            cosign, githubReadToken, feedReadToken, cancellationToken);
        var validation = await GitHubEvidenceSource.ReadWorkflowAsync(producer, S06WorkflowProfile.ValidationJobs,
            publicationStartedAtUtc, githubReadToken, cancellationToken);
        S06EvidenceChronology.RequireValidation(root, graph.Gate, graph.Evidence, validation, proof.FormalWorkflow);
        proof.OciSignature.RequireCompletedWorkflow(validation, publicationStartedAtUtc);
        return validation;
    }
}
```

#### tools/p10/ReleaseVerifier/VerifiedPlatformCandidate.cs

```csharp
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Successful current verification of a non-deployable reference candidate, not an audit-ledger Frozen transition.
public sealed class VerifiedPlatformCandidate
{
    private VerifiedPlatformCandidate(FetchedCandidateGraph fetched, GitHubWorkflowObservation validation,
        GitHubWorkflowObservation publisher)
    {
        ReleaseTag = fetched.Locator.ReleaseTag;
        Sha256 = fetched.Graph.Sha256;
        ValidationRunId = validation.Workflow.RunId;
        PublicationRunId = publisher.Workflow.RunId;
        VerifiedAtUtc = DateTimeOffset.UtcNow;
    }

    public string ReleaseTag { get; }
    public string Sha256 { get; }
    public long ValidationRunId { get; }
    public long PublicationRunId { get; }
    public DateTimeOffset VerifiedAtUtc { get; }
    public string State => "VerifiedNonDeployable";
    public bool CandidateAccepted => true;
    public bool Deployable => false;

    // Always discovers through fixed R2 keys and compiled current Locator trust. No intended/local-root mode.
    public static async Task<VerifiedPlatformCandidate> VerifyAsync(string releaseTag, string readAccessKeyId,
        string readSecret, CosignBlobVerifier cosign, string githubReadToken, string feedReadToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(20));
        try
        {
            var fetched = await PlatformCandidateSource.DiscoverAsync(releaseTag, readAccessKeyId, readSecret, cosign, deadline.Token);
            var root = fetched.Graph.Candidate;
            var identity = S06WorkflowProfile.Read(root.GetProperty("publisher"), S06ReleaseIdentity.PublicationPath);
            var publisher = await GitHubEvidenceSource.ReadWorkflowAsync(identity, S06WorkflowProfile.PublicationJobs,
                DateTimeOffset.UtcNow, githubReadToken, deadline.Token);
            var validation = await S06CandidateEvidence.VerifyAsync(fetched.Graph, publisher.StartedAtUtc,
                cosign, githubReadToken, feedReadToken, deadline.Token);
            S06EvidenceChronology.RequirePublication(root, validation, publisher.Workflow,
                publisher.StartedAtUtc, publisher.CompletedAtUtc);
            Require(Time(root, "createdAtUtc") >= publisher.Jobs.Single().StartedAtUtc, "s06-publication-job");
            return new(fetched, validation, publisher);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw Error("s06-candidate-timeout");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { throw Error("s06-candidate-verification"); }
    }
}
```

#### tools/p10/ReleaseVerifier/S06PublicationConfirmation.cs

```csharp
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// A clean pre/post-commit confirmation inside the actual running publisher. Never normal-consumer acceptance.
internal sealed class S06PublicationConfirmation
{
    private S06PublicationConfirmation(FetchedCandidateGraph fetched, S06CurrentWorkflow current,
        GitHubWorkflowObservation validation)
    {
        ReleaseTag = fetched.Locator.ReleaseTag;
        CandidateSha256 = fetched.Graph.Sha256;
        Publication = current;
        Validation = validation;
    }

    internal string ReleaseTag { get; }
    internal string CandidateSha256 { get; }
    internal S06CurrentWorkflow Publication { get; }
    internal GitHubWorkflowObservation Validation { get; }
    internal bool PublicationWorkflowCompleted => false;

    internal static async Task<S06PublicationConfirmation> VerifyAsync(FetchedCandidateGraph fetched,
        CosignBlobVerifier cosign, string githubReadToken, string feedReadToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Capture fresh live context here; it cannot be supplied as serialized metadata or a success flag.
        var current = await S06CurrentWorkflow.CaptureAsync(S06ReleaseIdentity.PublicationPath, githubReadToken, cancellationToken);
        var validation = await S06CandidateEvidence.VerifyAsync(fetched.Graph, current.RunStartedAtUtc,
            cosign, githubReadToken, feedReadToken, cancellationToken);
        S06EvidenceChronology.RequirePublication(fetched.Graph.Candidate, validation, current.Workflow,
            current.RunStartedAtUtc, current.ObservedAtUtc);
        Require(Time(fetched.Graph.Candidate, "createdAtUtc") >= current.JobStartedAtUtc, "s06-publication-job");
        return new(fetched, current, validation);
    }
}
```


### Task 3: Verify and commit

- [x] Run the focused/full suites and format check from `tools/p10`; require no failures or skips.
- [x] Verify exact plan/source agreement, review the five-file diff and scan for secret/debug/scope residue.
- [ ] Stage only these five files and commit `feat(p10): separate completed candidate acceptance from publication confirmation`.
- [ ] Complete the positive real S06 workflow/OCI/R2 acceptance path before recording S06 or P10 complete.

```powershell
$env:DOTNET_ROOT = 'C:/Users/tt/.dotnet'
$env:DOTNET_HOST_PATH = 'C:/Users/tt/.dotnet/dotnet.exe'
$env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH
$env:P10_COSIGN_PATH = 'C:/Users/tt/AppData/Local/Temp/cp6-p10-cosign-c4ab1329239f4721a3f8f5ac741ce3c9/cosign-windows-amd64.exe'
$env:P10_FORMAL_PACKAGE_ROOT = 'D:/CP6.Platform-worktrees/p10-formal-schema-parity/artifacts/p10-0.10.1-publication/windows/feed-readback-packages'
$env:P10_GITHUB_READ_TOKEN = gh auth token
$env:P10_FEED_READ_TOKEN = $env:P10_GITHUB_READ_TOKEN
try {
  & $env:DOTNET_HOST_PATH test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~S06CandidateVerifierTests
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
git add -- docs/superpowers/plans/2026-09-08-p10-s06-candidate-verifier.md tools/p10/ReleaseVerifier/S06CandidateEvidence.cs tools/p10/ReleaseVerifier/VerifiedPlatformCandidate.cs tools/p10/ReleaseVerifier/S06PublicationConfirmation.cs tools/p10/ReleaseVerifier.Tests/S06CandidateVerifierTests.cs
git diff --cached --check
git diff --cached --stat
git commit -m "feat(p10): separate completed candidate acceptance from publication confirmation"
```

## Self-review

- [x] Covers the parent design's signed discovery, completed validation/publication separation, current consumption and non-deployable boundaries.
- [x] No boolean or input parameter can select an unfinished publisher for normal candidate acceptance.
- [x] All signatures, code, test inputs, commands and exact file paths are specified; real positive acceptance remains explicitly open.

## Verification outcome (2026-09-08)

- RED: 10 failed, 0 passed, 0 skipped, all expected throwing-scaffold failures after compilation.
- Focused GREEN: 10 passed, 0 failed, 0 skipped.
- Full Release suite: 1,317 passed, 0 failed, 0 skipped in 47 seconds.
- Formatting exited 0. Four plan code blocks match the implementation and tests.
- No positive S06 acceptance was synthesized; no S06 OCI/R2 write or workflow execution has yet occurred.
