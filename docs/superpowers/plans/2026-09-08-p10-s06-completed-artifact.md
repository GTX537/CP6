# P10 S06 completed validation Artifact handoff implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans task-by-task. The owner selected inline sequential execution; no subagents.

**Goal:** Join actual completed validation, immutable GitHub Artifact transport and complete payload proof before publication may consume the artifact.

**Architecture:** A sealed private-constructor handoff result reads the exact successful producer and job first, downloads the fixed artifact ID/name/SHA/digest, then validates all contents and real evidence. A pure timing helper retains the API's second-resolution measurement interval while bounding it by actual observation; it does not change canonical timestamp requirements or general workflow checks.

**Tech Stack:** .NET 8.0.424, pinned CP6.Platform.Release 0.10.1, existing fixed GitHub/OCI/feed transports and native cosign, xUnit.

---

## Scope and self-review

- Continue the existing isolated S06 branch; no root worktree changes.
- The result is not a published or accepted candidate. No replacement trust, transport, successful-workflow boolean or serialized capability is accepted.
- Read the actual completed producer before downloading. Artifact transport still enforces exact ID, name, run, SHA, digest, size, expiry and entry set.
- The content cutoff is the minimum of the artifact creation second's end, job completion second's end and actual download observation. Reject fractional API inputs, non-UTC/future/inverted timing. Do not globally add a second to any precise cutoff.
- Join existing structural artifact inspection, actual payload proof, chronology and authenticated OCI completion.
- Positive complete S06 handoff remains subject to actual protected workflow/OCI acceptance; selected timing vectors and boundary tests are not formal acceptance.
- No external writes, new credentials, deployment, old R2 gate changes or package republication.

## Files

- Create: `tools/p10/ReleaseVerifier/S06CompletedValidation.cs`
- Create: `tools/p10/ReleaseVerifier/S06ArtifactTiming.cs`
- Test: `tools/p10/ReleaseVerifier.Tests/S06CompletedValidationTests.cs`

### Task 1: Tests and RED

- [x] Write these tests and throwing scaffolds with the signatures below.
- [x] Run the focused suite; require compilation followed by failures from missing implementation, not skipped tests.

```csharp
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Timing vectors are not acceptance evidence. Positive S06 handoff needs a real completed protected producer.
public sealed class S06CompletedValidationTests
{
    private static DateTimeOffset Time => new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static GitHubWorkflowIdentity Producer => new("GTX537/CP6", S06ReleaseIdentity.ValidationPath,
        new string('5', 40), 999991, 1, new string('a', 40));

    [Fact]
    public void Same_second_content_is_bounded_by_the_reported_artifact_second() =>
        Assert.Equal(Time.AddSeconds(1).AddTicks(-1),
            S06ArtifactTiming.ContentCutoff(Time, Time.AddSeconds(3), Time.AddSeconds(4)));

    [Fact]
    public void A_completed_job_in_the_same_second_keeps_the_full_measured_interval() =>
        Assert.Equal(Time.AddSeconds(1).AddTicks(-1),
            S06ArtifactTiming.ContentCutoff(Time, Time, Time.AddSeconds(4)));

    [Fact]
    public void Actual_observation_clips_the_measurement_interval() =>
        Assert.Equal(Time.AddMilliseconds(350),
            S06ArtifactTiming.ContentCutoff(Time, Time, Time.AddMilliseconds(350)));

    [Theory]
    [InlineData("created-after-job")]
    [InlineData("observed-before-job")]
    [InlineData("created-offset")]
    [InlineData("job-offset")]
    [InlineData("observed-offset")]
    [InlineData("created-fraction")]
    [InlineData("job-fraction")]
    [InlineData("before-epoch")]
    [InlineData("future-observation")]
    public void Inconsistent_or_non_API_timing_is_rejected(string mutation)
    {
        var created = Time;
        var completed = Time.AddSeconds(2);
        var observed = Time.AddSeconds(3);
        if (mutation == "created-after-job") created = Time.AddSeconds(4);
        if (mutation == "observed-before-job") observed = Time.AddSeconds(1);
        if (mutation == "created-offset") created = created.ToOffset(TimeSpan.FromHours(1));
        if (mutation == "job-offset") completed = completed.ToOffset(TimeSpan.FromHours(1));
        if (mutation == "observed-offset") observed = observed.ToOffset(TimeSpan.FromHours(1));
        if (mutation == "created-fraction") created = created.AddMilliseconds(1);
        if (mutation == "job-fraction") completed = completed.AddMilliseconds(1);
        if (mutation == "before-epoch") created = DateTimeOffset.UnixEpoch.AddSeconds(-1);
        if (mutation == "future-observation") observed = DateTimeOffset.UtcNow.AddDays(1);
        Assert.Throws<Cp6ReleaseContractException>(() => S06ArtifactTiming.ContentCutoff(created, completed, observed));
    }

    [Fact]
    public async Task Publication_cannot_be_selected_as_a_validation_producer() =>
        Assert.Equal("s06-attestation-producer", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Read(Producer with { WorkflowPath = S06ReleaseIdentity.PublicationPath }, 1, Time))).Code);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task An_invalid_artifact_identity_fails_before_external_reads(long id) =>
        Assert.Equal("artifact-selection", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Read(Producer, id, Time))).Code);

    [Fact]
    public async Task A_future_completion_cutoff_is_refused() =>
        Assert.Equal("s06-validation-cutoff", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Read(Producer, 1, DateTimeOffset.UtcNow.AddDays(1)))).Code);

    [Fact]
    public async Task Missing_GitHub_credentials_never_fall_back_to_ambient_authentication() =>
        Assert.Equal("github-credential", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Read(Producer, 1, Time))).Code);

    [Fact]
    public async Task Precancelled_handoff_performs_no_external_read()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => S06CompletedValidation.ReadAsync(
            Producer, 1, Time, new("missing-cosign"), "", "", cancellation.Token));
    }

    private static Task<S06CompletedValidation> Read(GitHubWorkflowIdentity producer, long artifactId, DateTimeOffset cutoff) =>
        S06CompletedValidation.ReadAsync(producer, artifactId, cutoff, new("missing-cosign"), "", "");
}
```

### Task 2: Minimal implementation and GREEN

- [x] Implement the time-bound helper exactly as follows.

```csharp
namespace CP6.P10.ReleaseVerifier;

// GitHub metadata is measured in whole seconds; CP6 control objects retain millisecond precision.
// This bounds content time only and never proves a workflow or artifact authentic.
internal static class S06ArtifactTiming
{
    internal static DateTimeOffset ContentCutoff(DateTimeOffset artifactCreated, DateTimeOffset jobCompleted,
        DateTimeOffset observed)
    {
        GitHubEvidenceChecks.RequireCutoff(artifactCreated);
        GitHubEvidenceChecks.RequireCutoff(jobCompleted);
        GitHubEvidenceChecks.RequireCutoff(observed);
        GitHubEvidenceChecks.Require(artifactCreated <= jobCompleted && jobCompleted <= observed &&
            observed <= DateTimeOffset.UtcNow && artifactCreated.Ticks % TimeSpan.TicksPerSecond == 0 &&
            jobCompleted.Ticks % TimeSpan.TicksPerSecond == 0, "s06-artifact-time");
        var artifactEnd = artifactCreated.AddSeconds(1).AddTicks(-1);
        var jobEnd = jobCompleted.AddSeconds(1).AddTicks(-1);
        return new[] { artifactEnd, jobEnd, observed }.Min();
    }
}
```

- [x] Implement the completed handoff exactly as follows.

```csharp
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// A completed producer plus its independently downloaded and verified payloads. Not a published candidate.
internal sealed class S06CompletedValidation
{
    private S06CompletedValidation(InspectedValidationArtifact artifact, GitHubWorkflowObservation workflow,
        WorkflowArtifactMetadata metadata)
    {
        Artifact = artifact;
        Workflow = workflow;
        Metadata = metadata;
        VerifiedAtUtc = DateTimeOffset.UtcNow;
    }

    internal InspectedValidationArtifact Artifact { get; }
    internal GitHubWorkflowObservation Workflow { get; }
    internal WorkflowArtifactMetadata Metadata { get; }
    internal DateTimeOffset VerifiedAtUtc { get; }

    internal static async Task<S06CompletedValidation> ReadAsync(GitHubWorkflowIdentity producer, long artifactId,
        DateTimeOffset completedBeforeUtc, CosignBlobVerifier cosign, string githubReadToken, string feedReadToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireProducer(producer);
        var selection = WorkflowArtifactSelection.Validation(producer, artifactId);
        GitHubEvidenceChecks.RequireCutoff(completedBeforeUtc);
        Require(completedBeforeUtc <= DateTimeOffset.UtcNow, "s06-validation-cutoff");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(15));
        try
        {
            var workflow = await GitHubEvidenceSource.ReadWorkflowAsync(producer, S06WorkflowProfile.ValidationJobs,
                completedBeforeUtc, githubReadToken, deadline.Token);
            var job = workflow.Jobs.Single();
            var downloaded = await WorkflowArtifactSource.ReadAsync(selection, job.StartedAtUtc, job.CompletedAtUtc,
                githubReadToken, deadline.Token);
            var cutoff = S06ArtifactTiming.ContentCutoff(downloaded.Metadata.CreatedAtUtc,
                job.CompletedAtUtc, downloaded.ObservedAtUtc);
            var files = downloaded.Names.ToDictionary(n => n,
                n => (ReadOnlyMemory<byte>)downloaded.CopyFile(n), StringComparer.Ordinal);
            var artifact = S06ArtifactInspection.Read(files, producer, cutoff);
            var payloads = artifact.Evidence.ToDictionary(p => p.Key,
                p => (ReadOnlyMemory<byte>)p.Value.CopyPayloadBytes(), StringComparer.Ordinal);
            var digest = Text(artifact.Index.GetProperty("images")[0], "sha256OrDigest");
            var proof = await S06PayloadProof.VerifyAsync(producer, digest, payloads, Time(artifact.Gate, "createdAtUtc"),
                cosign, githubReadToken, feedReadToken, deadline.Token);
            S06EvidenceChronology.RequireValidation(artifact.Index, artifact.Gate, artifact.Evidence,
                workflow, proof.FormalWorkflow);
            proof.OciSignature.RequireCompletedWorkflow(workflow, completedBeforeUtc);
            return new(artifact, workflow, downloaded.Metadata);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw Error("s06-validation-timeout");
        }
    }
}
```

- [x] Run focused tests; require all passing.

### Task 3: Verify and commit

- [x] Run the full Release suite with actual existing cosign, downloaded formal packages and GitHub/feed read credentials.
- [x] Run formatting, inspect exact four-file diff, verify plan/code agreement and scan for secret/debug residue.
- [ ] Stage only the four task files and commit `feat(p10): verify completed validation artifact handoff`.
- [ ] Complete the real protected S06 handoff before declaring S06 complete.

```powershell
$env:DOTNET_ROOT = 'C:/Users/tt/.dotnet'
$env:DOTNET_HOST_PATH = 'C:/Users/tt/.dotnet/dotnet.exe'
$env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH
$env:P10_COSIGN_PATH = 'C:/Users/tt/AppData/Local/Temp/cp6-p10-cosign-c4ab1329239f4721a3f8f5ac741ce3c9/cosign-windows-amd64.exe'
$env:P10_FORMAL_PACKAGE_ROOT = 'D:/CP6.Platform-worktrees/p10-formal-schema-parity/artifacts/p10-0.10.1-publication/windows/feed-readback-packages'
$env:P10_GITHUB_READ_TOKEN = gh auth token
$env:P10_FEED_READ_TOKEN = $env:P10_GITHUB_READ_TOKEN
try {
  & $env:DOTNET_HOST_PATH test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~S06CompletedValidationTests
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
git add -- docs/superpowers/plans/2026-09-08-p10-s06-completed-artifact.md tools/p10/ReleaseVerifier/S06CompletedValidation.cs tools/p10/ReleaseVerifier/S06ArtifactTiming.cs tools/p10/ReleaseVerifier.Tests/S06CompletedValidationTests.cs
git diff --cached --check
git diff --cached --stat
git commit -m "feat(p10): verify completed validation artifact handoff"
```

## Verification outcome (2026-09-08)

- RED: all 18 tests failed from the throwing scaffolds after successful compilation; no skips.
- Initial GREEN exposed one test expectation typo: the existing credential policy uses `github-credential`, not `github-token`. Corrected only the test and this plan after tracing the actual policy.
- Focused GREEN: 18 passed, 0 failed, 0 skipped.
- Full Release suite: 1,335 passed, 0 failed, 0 skipped in 47 seconds; formatting exited 0.
- All three plan code blocks match source/tests. Positive protected S06 acceptance and external publication remain open.
