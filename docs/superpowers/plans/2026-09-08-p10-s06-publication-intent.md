# P10 S06 immutable publication intent read implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans task-by-task. The owner selected inline sequential execution; no subagents.

**Goal:** Let clean publication checks obtain intended Locator bytes only from the current running publisher's authenticated immutable GitHub artifact.

**Architecture:** A private-constructor result captures live current publication identity, downloads the exact intent artifact by ID/name/run/attempt/source/digest, requires its two fixed files and authenticates their raw bytes under compiled Locator trust. It does not accept a candidate or predict publication success; existing graph and publication confirmation remain mandatory afterward.

**Tech Stack:** .NET 8.0.424, CP6.Platform.Release 0.10.1, existing fixed artifact transport and native cosign, xUnit.

---

## Scope and self-review

- Existing artifact ZIP grammar uses `locator.json` and `locator.sigstore.json`; these are job transport filenames, not the distinct R2 discovery names. Keep that existing grammar.
- No caller-selected local files, workflow context, repository, endpoint, trust policy, clock or verification-success flag.
- Capture the current protected publication job; require artifact creation within that observed job and the authenticated Locator creation no later than the artifact's measured creation second.
- Return exact owned raw bytes. Plain payload-copying vectors cannot construct the private authenticated result.
- The candidate's publisher binding is verified by the existing complete graph/current-publication confirmation, not inferred from a Locator that has no publisher fields.
- This module performs reads only. No Locator write, signing key access, package/OCI publication, deployment or old R2 changes.
- Full positive acceptance remains dependent on the real protected S06 publication run.

## Files

- Create: `tools/p10/ReleaseVerifier/S06PublicationIntent.cs`
- Test: `tools/p10/ReleaseVerifier.Tests/S06PublicationIntentTests.cs`
- Plan: `docs/superpowers/plans/2026-09-08-p10-s06-publication-intent.md`

### Task 1: Test and RED

- [x] Add tests and throwing scaffolds with the signatures below.
- [x] Run focused tests; confirm missing-implementation failures after successful compilation, with no skips.

```csharp
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Payload copying and rejection vectors only. They cannot produce an authenticated current-run intent.
public sealed class S06PublicationIntentTests
{
    [Fact]
    public void Intent_payloads_are_exact_raw_owned_copies()
    {
        var locator = new byte[] { 1, 2, 3 };
        var bundle = new byte[] { 4, 5, 6 };
        var artifact = Archive(new() { ["locator.json"] = locator, ["locator.sigstore.json"] = bundle });
        locator[0] = 99;
        var first = S06PublicationIntent.ReadPayloads(artifact);
        Assert.Equal(new byte[] { 1, 2, 3 }, first.Locator);
        Assert.Equal(bundle, first.Bundle);
        first.Locator[0] = 88;
        first.Bundle[0] = 77;
        var second = S06PublicationIntent.ReadPayloads(artifact);
        Assert.Equal(new byte[] { 1, 2, 3 }, second.Locator);
        Assert.Equal(new byte[] { 4, 5, 6 }, second.Bundle);
    }

    [Theory]
    [InlineData("missing-locator")]
    [InlineData("missing-bundle")]
    [InlineData("extra")]
    [InlineData("case")]
    [InlineData("discovery-name")]
    public void Only_the_two_fixed_job_artifact_names_are_allowed(string mutation)
    {
        var files = Files();
        if (mutation == "missing-locator") files.Remove("locator.json");
        if (mutation == "missing-bundle") files.Remove("locator.sigstore.json");
        if (mutation == "extra") files.Add("artifact-index.json", "{}"u8.ToArray());
        if (mutation == "case")
        {
            files.Remove("locator.json");
            files.Add("Locator.json", "{}"u8.ToArray());
        }
        if (mutation == "discovery-name")
        {
            files.Remove("locator.json");
            files.Add("candidate-locator.v1.json", "{}"u8.ToArray());
        }
        Assert.Equal("s06-intent-files", Assert.Throws<Cp6ReleaseContractException>(() =>
            S06PublicationIntent.ReadPayloads(Archive(files))).Code);
    }

    [Theory]
    [InlineData("locator.json", 0)]
    [InlineData("locator.sigstore.json", 0)]
    [InlineData("locator.json", 4194305)]
    [InlineData("locator.sigstore.json", 4194305)]
    public void Payload_sizes_remain_bounded(string name, int length)
    {
        var files = Files();
        files[name] = new byte[length];
        Assert.Equal("s06-intent-size", Assert.Throws<Cp6ReleaseContractException>(() =>
            S06PublicationIntent.ReadPayloads(Archive(files))).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Invalid_artifact_IDs_fail_before_capturing_a_current_workflow(long id) =>
        Assert.Equal("s06-intent-id", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            S06PublicationIntent.ReadAsync("v0.10.1-p10.1", id, new("missing-cosign"), ""))).Code);

    [Theory]
    [InlineData("../v0.10.1")]
    [InlineData("v0.10.1\n")]
    public async Task Unsafe_tags_fail_before_capturing_a_current_workflow(string tag) =>
        await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            S06PublicationIntent.ReadAsync(tag, 1, new("missing-cosign"), ""));

    [Fact]
    public async Task Precancelled_intent_read_performs_no_external_work()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            S06PublicationIntent.ReadAsync("v0.10.1-p10.1", 1, new("missing-cosign"), "", cancellation.Token));
    }

    private static Dictionary<string, ReadOnlyMemory<byte>> Files() => new()
    {
        ["locator.json"] = "{}"u8.ToArray(),
        ["locator.sigstore.json"] = "{}"u8.ToArray()
    };

    private static DownloadedWorkflowArtifact Archive(Dictionary<string, ReadOnlyMemory<byte>> files) =>
        new(new(1, "unit-vector-only", "sha256:" + new string('a', 64), 1,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1)), files, DateTimeOffset.UnixEpoch);
}
```

### Task 2: Implement and GREEN

- [x] Replace scaffolds with the implementation below.
- [x] Rerun focused tests; require all passing.

```csharp
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Actual current-run immutable intent transport and Locator authentication only.
// The caller must still fetch the R2 graph and verify the candidate against this running publisher.
internal sealed class S06PublicationIntent
{
    internal const string LocatorName = "locator.json";
    internal const string BundleName = "locator.sigstore.json";
    private readonly byte[] _locatorBytes;
    private readonly byte[] _bundleBytes;

    private S06PublicationIntent(byte[] locatorBytes, byte[] bundleBytes, AuthenticatedLocator locator,
        S06CurrentWorkflow current, WorkflowArtifactMetadata metadata)
    {
        _locatorBytes = locatorBytes.ToArray();
        _bundleBytes = bundleBytes.ToArray();
        Locator = locator;
        Publication = current;
        Metadata = metadata;
    }

    internal AuthenticatedLocator Locator { get; }
    internal S06CurrentWorkflow Publication { get; }
    internal WorkflowArtifactMetadata Metadata { get; }
    internal byte[] CopyLocatorBytes() => _locatorBytes.ToArray();
    internal byte[] CopyBundleBytes() => _bundleBytes.ToArray();

    internal static async Task<S06PublicationIntent> ReadAsync(string releaseTag, long artifactId,
        CosignBlobVerifier cosign, string githubReadToken, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = R2ObjectTarget.Discovery(releaseTag, R2DiscoveryPart.Locator);
        Require(artifactId > 0, "s06-intent-id");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            var current = await S06CurrentWorkflow.CaptureAsync(S06ReleaseIdentity.PublicationPath, githubReadToken, deadline.Token);
            var selection = WorkflowArtifactSelection.PublicationIntent(current.Workflow, artifactId);
            var downloaded = await WorkflowArtifactSource.ReadAsync(selection, current.JobStartedAtUtc,
                current.ObservedAtUtc, githubReadToken, deadline.Token);
            var payloads = ReadPayloads(downloaded);
            var locator = await AuthenticatedLocator.AuthenticateAsync(releaseTag, payloads.Locator,
                payloads.Bundle, cosign, deadline.Token);
            // Metadata parsing already requires a past whole-second UTC time from the fixed GitHub API.
            var artifactEnd = downloaded.Metadata.CreatedAtUtc.AddSeconds(1).AddTicks(-1);
            Require(current.JobStartedAtUtc <= locator.CreatedAtUtc && locator.CreatedAtUtc <= artifactEnd &&
                locator.CreatedAtUtc <= downloaded.ObservedAtUtc, "s06-intent-time");
            return new(payloads.Locator, payloads.Bundle, locator, current, downloaded.Metadata);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw Error("s06-intent-timeout");
        }
    }

    // Plain bytes, not a trusted intent capability. The real entry point first authenticates the API/archive.
    internal static (byte[] Locator, byte[] Bundle) ReadPayloads(DownloadedWorkflowArtifact artifact)
    {
        Require(artifact.Names.SequenceEqual(new[] { LocatorName, BundleName }, StringComparer.Ordinal), "s06-intent-files");
        var locator = artifact.CopyFile(LocatorName);
        var bundle = artifact.CopyFile(BundleName);
        Require(locator.Length is > 0 and <= Cp6DeterministicJson.MaximumBytes &&
            bundle.Length is > 0 and <= Cp6DeterministicJson.MaximumBytes, "s06-intent-size");
        return (locator, bundle);
    }
}
```

### Task 3: Verify and commit

- [x] Run focused/full Release suites and formatting with existing real test inputs.
- [x] Check plan/source agreement, three-file diff and secret/debug hygiene.
- [ ] Stage only these three files and commit `feat(p10): read immutable intent from the current publication artifact`.
- [ ] Exercise the actual protected S06 intent handoff before final S06 acceptance.

```powershell
$env:DOTNET_ROOT = 'C:/Users/tt/.dotnet'
$env:DOTNET_HOST_PATH = 'C:/Users/tt/.dotnet/dotnet.exe'
$env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH
$env:P10_COSIGN_PATH = 'C:/Users/tt/AppData/Local/Temp/cp6-p10-cosign-c4ab1329239f4721a3f8f5ac741ce3c9/cosign-windows-amd64.exe'
$env:P10_FORMAL_PACKAGE_ROOT = 'D:/CP6.Platform-worktrees/p10-formal-schema-parity/artifacts/p10-0.10.1-publication/windows/feed-readback-packages'
$env:P10_GITHUB_READ_TOKEN = gh auth token
$env:P10_FEED_READ_TOKEN = $env:P10_GITHUB_READ_TOKEN
try {
  & $env:DOTNET_HOST_PATH test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~S06PublicationIntentTests
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
git add -- docs/superpowers/plans/2026-09-08-p10-s06-publication-intent.md tools/p10/ReleaseVerifier/S06PublicationIntent.cs tools/p10/ReleaseVerifier.Tests/S06PublicationIntentTests.cs
git diff --cached --check
git diff --cached --stat
git commit -m "feat(p10): read immutable intent from the current publication artifact"
```

## Verification outcome (2026-09-08)

- RED: 15 expected missing-implementation failures after compilation; 0 passes/skips.
- Focused GREEN: 15 passed, 0 failed, 0 skipped.
- Full Release suite: 1,350 passed, 0 failed, 0 skipped in 47 seconds; formatting exited 0.
- Both plan code blocks match source/tests. This is not positive protected S06 publication acceptance; no external writes were made.
