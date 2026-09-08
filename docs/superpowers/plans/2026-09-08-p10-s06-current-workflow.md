# P10 S06 current protected-job context implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans task-by-task. The owner selected inline sequential execution; no subagents.

**Goal:** Observe a currently running S06 validation/publication job without predicting its final conclusion or adding a normal-consumer bypass.

**Architecture:** Pure selected-field checks bind the fixed workflow/job profile to GitHub's standard environment and live run/job responses. A sealed context with a private constructor reads only the actual process environment, exact public GitHub targets and current time; it is never deserialized from caller data or an artifact.

**Tech Stack:** .NET 8.0.424, existing GitHub transport and raw Git-blob checks, xUnit.

---

## Scope and reviewed decisions

- Continue the existing isolated S06 branch. No existing completed-run checker is weakened.
- The two workflows remain separate. Current-job context is only for collection and clean pre/post publication confirmation; normal candidate acceptance will still require a completed successful publisher.
- Run only on the already selected GitHub-hosted Linux x64 execution path. Do not run a build/sign/publish path from a developer PC.
- Read only the selected standard environment fields; never serialize all environment/context data, runner identity, credentials or raw API responses.
- Bind the actual workflow file at the exact source SHA, prove current protected-main ancestry, then require the exact run and sole selected job to be `in_progress` with null conclusions/completion.
- The standard `GITHUB_WORKFLOW_SHA` is a commit SHA, not a Git blob SHA; obtain and verify the latter from the exact workflow contents API.
- The Environment remains enforced by the protected workflow and scoped signing secrets. This context is not hardware attestation or independent proof of secret custody.
- No private constructor bypass, caller-supplied environment/clock/handler in the live entry, success flag, external write or deployment is introduced.
- Positive live capture remains a real protected-workflow acceptance step. Pure selected-field test vectors cannot stand in for that run.

Reference: [GitHub variables reference](https://docs.github.com/en/actions/reference/workflows-and-actions/variables), checked 2026-09-08.

## File map

- Create: `tools/p10/ReleaseVerifier/S06CurrentWorkflowChecks.cs` — selected environment/API identity and time checks.
- Create: `tools/p10/ReleaseVerifier/S06CurrentWorkflow.cs` — sealed live capture and safe observations.
- Create: `tools/p10/ReleaseVerifier.Tests/S06CurrentWorkflowTests.cs` — pure boundary and live-entry rejection tests.
- Create: `docs/superpowers/plans/2026-09-08-p10-s06-current-workflow.md` — plan and verification evidence.

### Task 1: Write tests and observe RED

- [x] Write this test file and throwing API scaffolds using the exact signatures and record types below.
- [x] Run the focused suite; require missing-behavior failures rather than compilation errors or skipped tests.

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Pure environment/API selected-field vectors are not a live workflow capability.
// CaptureAsync has no injected environment, clock, handler or success-result constructor.
public sealed class S06CurrentWorkflowTests
{
    private static readonly DateTimeOffset Observed = DateTimeOffset.Parse("2026-09-08T01:05:00Z", CultureInfo.InvariantCulture);
    private static GitHubWorkflowIdentity Identity(string path) => new("GTX537/CP6", path, new string('b', 40),
        999999, 1, new string('c', 40));

    [Theory]
    [InlineData(S06ReleaseIdentity.ValidationPath, "validate")]
    [InlineData(S06ReleaseIdentity.PublicationPath, "publish")]
    public void Both_selected_profiles_bind_only_their_own_running_job(string path, string job)
    {
        var expected = Identity(path);
        var selected = S06CurrentWorkflowChecks.ReadEnvironment(Variables(path), path);
        Assert.Equal(expected.CommitSha, selected.CommitSha);
        Assert.Equal(expected.RunId, selected.RunId);
        Assert.Equal(expected.RunAttempt, selected.RunAttempt);
        Assert.Equal(job, selected.JobName);
        var timing = S06CurrentWorkflowChecks.ReadTiming(Element(Run(path)), Element(Jobs(path)), expected, Observed);
        Assert.Equal(Observed.AddMinutes(-5), timing.RunStartedAtUtc);
        Assert.Equal(Observed.AddMinutes(-4), timing.JobStartedAtUtc);
    }

    [Theory]
    [InlineData("GITHUB_ACTIONS", "false")]
    [InlineData("GITHUB_REPOSITORY", "Other/CP6")]
    [InlineData("GITHUB_REPOSITORY_ID", "1")]
    [InlineData("GITHUB_REF", "refs/heads/topic")]
    [InlineData("GITHUB_EVENT_NAME", "pull_request")]
    [InlineData("GITHUB_SHA", "main")]
    [InlineData("GITHUB_WORKFLOW_REF", "GTX537/CP6/.github/workflows/p10-platform-candidate.yml@refs/heads/topic")]
    [InlineData("GITHUB_WORKFLOW_SHA", "0000000000000000000000000000000000000000")]
    [InlineData("GITHUB_RUN_ID", "0999999")]
    [InlineData("GITHUB_RUN_ATTEMPT", "0")]
    [InlineData("GITHUB_RUN_ATTEMPT", "2147483648")]
    [InlineData("GITHUB_JOB", "validate")]
    [InlineData("GITHUB_SERVER_URL", "https://example.invalid")]
    [InlineData("GITHUB_API_URL", "https://example.invalid")]
    [InlineData("RUNNER_ENVIRONMENT", "self-hosted")]
    [InlineData("RUNNER_OS", "Windows")]
    [InlineData("RUNNER_ARCH", "ARM64")]
    [InlineData("GITHUB_ACTIONS", null)]
    public void Unapproved_or_ambiguous_environment_identity_fails_before_live_reads(string name, string? value)
    {
        var values = Variables(S06ReleaseIdentity.PublicationPath);
        values[name] = value;
        Assert.Throws<Cp6ReleaseContractException>(() =>
            S06CurrentWorkflowChecks.ReadEnvironment(values, S06ReleaseIdentity.PublicationPath));
    }

    [Theory]
    [InlineData("run-id")]
    [InlineData("attempt")]
    [InlineData("source")]
    [InlineData("branch")]
    [InlineData("path")]
    [InlineData("event")]
    [InlineData("repository")]
    [InlineData("head-repository")]
    [InlineData("completed-success")]
    [InlineData("queued")]
    [InlineData("future-run-time")]
    [InlineData("extra-job")]
    [InlineData("job-id")]
    [InlineData("job-name")]
    [InlineData("job-run")]
    [InlineData("job-attempt")]
    [InlineData("job-source")]
    [InlineData("job-branch")]
    [InlineData("job-completed")]
    [InlineData("job-conclusion")]
    [InlineData("job-completed-time")]
    [InlineData("job-before-run")]
    [InlineData("job-future")]
    public void Live_API_identity_status_and_time_must_all_match_the_current_invocation(string mutation)
    {
        var path = S06ReleaseIdentity.PublicationPath;
        var run = Run(path);
        var jobs = Jobs(path);
        var job = jobs["jobs"]![0]!;
        if (mutation == "run-id") run["id"] = 1;
        if (mutation == "attempt") run["run_attempt"] = 2;
        if (mutation == "source") run["head_sha"] = new string('d', 40);
        if (mutation == "branch") run["head_branch"] = "topic";
        if (mutation == "path") run["path"] = S06ReleaseIdentity.ValidationPath;
        if (mutation == "event") run["event"] = "pull_request";
        if (mutation == "repository") run["repository"]!["full_name"] = "Other/CP6";
        if (mutation == "head-repository") run["head_repository"]!["full_name"] = "Other/CP6";
        if (mutation == "completed-success") { run["status"] = "completed"; run["conclusion"] = "success"; }
        if (mutation == "queued") run["status"] = "queued";
        if (mutation == "future-run-time") run["updated_at"] = ApiTime(Observed.AddMinutes(1));
        if (mutation == "extra-job") { jobs["total_count"] = 2; jobs["jobs"]!.AsArray().Add(job.DeepClone()); }
        if (mutation == "job-id") job["id"] = 0;
        if (mutation == "job-name") job["name"] = "validate";
        if (mutation == "job-run") job["run_id"] = 1;
        if (mutation == "job-attempt") job["run_attempt"] = 2;
        if (mutation == "job-source") job["head_sha"] = new string('d', 40);
        if (mutation == "job-branch") job["head_branch"] = "topic";
        if (mutation == "job-completed") job["status"] = "completed";
        if (mutation == "job-conclusion") job["conclusion"] = "success";
        if (mutation == "job-completed-time") job["completed_at"] = ApiTime(Observed);
        if (mutation == "job-before-run") job["started_at"] = ApiTime(Observed.AddMinutes(-6));
        if (mutation == "job-future") job["started_at"] = ApiTime(Observed.AddMinutes(1));
        var error = Assert.Throws<Cp6ReleaseContractException>(() =>
            S06CurrentWorkflowChecks.ReadTiming(Element(run), Element(jobs), Identity(path), Observed));
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task The_live_entry_has_no_unknown_workflow_profile_fallback() =>
        Assert.Equal("s06-current-profile", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            S06CurrentWorkflow.CaptureAsync(".github/workflows/other.yml", "not-a-token"))).Code);

    [Fact]
    public async Task A_precancelled_current_context_capture_has_no_live_reads()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            S06CurrentWorkflow.CaptureAsync(S06ReleaseIdentity.PublicationPath, "not-a-token", cancellation.Token));
    }

    private static Dictionary<string, string?> Variables(string path) => new(StringComparer.Ordinal)
    {
        ["GITHUB_ACTIONS"] = "true",
        ["GITHUB_REPOSITORY"] = "GTX537/CP6",
        ["GITHUB_REPOSITORY_ID"] = "1214929352",
        ["GITHUB_REF"] = "refs/heads/main",
        ["GITHUB_EVENT_NAME"] = "workflow_dispatch",
        ["GITHUB_SHA"] = Identity(path).CommitSha,
        ["GITHUB_WORKFLOW_REF"] = "GTX537/CP6/" + path + "@refs/heads/main",
        ["GITHUB_WORKFLOW_SHA"] = Identity(path).CommitSha,
        ["GITHUB_RUN_ID"] = "999999",
        ["GITHUB_RUN_ATTEMPT"] = "1",
        ["GITHUB_JOB"] = path == S06ReleaseIdentity.PublicationPath ? "publish" : "validate",
        ["GITHUB_SERVER_URL"] = "https://github.com",
        ["GITHUB_API_URL"] = "https://api.github.com",
        ["RUNNER_ENVIRONMENT"] = "github-hosted",
        ["RUNNER_OS"] = "Linux",
        ["RUNNER_ARCH"] = "X64"
    };

    private static JsonNode Run(string path) => JsonSerializer.SerializeToNode(new
    {
        id = 999999,
        run_attempt = 1,
        head_sha = Identity(path).CommitSha,
        head_branch = "main",
        path,
        @event = "workflow_dispatch",
        repository = new { full_name = "GTX537/CP6" },
        head_repository = new { full_name = "GTX537/CP6" },
        status = "in_progress",
        conclusion = (string?)null,
        created_at = ApiTime(Observed.AddMinutes(-5)),
        run_started_at = ApiTime(Observed.AddMinutes(-5)),
        updated_at = ApiTime(Observed.AddMinutes(-4))
    })!;

    private static JsonNode Jobs(string path) => JsonSerializer.SerializeToNode(new
    {
        total_count = 1,
        jobs = new[]
        {
            new
            {
                id = 111111, name = path == S06ReleaseIdentity.PublicationPath ? "publish" : "validate",
                run_id = 999999, run_attempt = 1, head_sha = Identity(path).CommitSha, head_branch = "main",
                status = "in_progress", conclusion = (string?)null, started_at = ApiTime(Observed.AddMinutes(-4)),
                completed_at = (string?)null
            }
        }
    })!;

    private static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);
    private static string ApiTime(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
```

### Task 2: Implement selected checks and live capture

- [x] Replace the throwing scaffolds only after recording RED.

#### tools/p10/ReleaseVerifier/S06CurrentWorkflowChecks.cs

```csharp
using System.Globalization;
using System.Text.Json;
using static CP6.P10.ReleaseVerifier.GitHubEvidenceChecks;

namespace CP6.P10.ReleaseVerifier;

// Selected-field checks only. Environment values and parser outputs alone do not create a live context.
internal static class S06CurrentWorkflowChecks
{
    internal static IReadOnlyList<string> EnvironmentNames { get; } = Array.AsReadOnly(new[]
    {
        "GITHUB_ACTIONS", "GITHUB_REPOSITORY", "GITHUB_REPOSITORY_ID", "GITHUB_REF", "GITHUB_EVENT_NAME",
        "GITHUB_SHA", "GITHUB_WORKFLOW_REF", "GITHUB_WORKFLOW_SHA", "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT",
        "GITHUB_JOB", "GITHUB_SERVER_URL", "GITHUB_API_URL", "RUNNER_ENVIRONMENT", "RUNNER_OS", "RUNNER_ARCH"
    });

    internal static string JobName(string path) => path switch
    {
        S06ReleaseIdentity.ValidationPath => S06WorkflowProfile.ValidationJobs.Single(),
        S06ReleaseIdentity.PublicationPath => S06WorkflowProfile.PublicationJobs.Single(),
        _ => throw GitHubWirePolicy.Error("s06-current-profile")
    };

    internal static S06CurrentSelection ReadEnvironment(IReadOnlyDictionary<string, string?> values, string path)
    {
        var job = JobName(path);
        string Value(string name) => values.TryGetValue(name, out var value) && value is { Length: > 0 and <= 256 } ?
            value : throw GitHubWirePolicy.Error("s06-current-environment");
        Require(Value("GITHUB_ACTIONS") == "true" && Value("GITHUB_REPOSITORY") == "GTX537/CP6" &&
            Value("GITHUB_REPOSITORY_ID") == WorkflowArtifactSelection.RepositoryId.ToString(CultureInfo.InvariantCulture) &&
            Value("GITHUB_REF") == "refs/heads/main" && Value("GITHUB_EVENT_NAME") == "workflow_dispatch" &&
            Value("GITHUB_WORKFLOW_REF") == "GTX537/CP6/" + path + "@refs/heads/main" &&
            Value("GITHUB_JOB") == job && Value("GITHUB_SERVER_URL") == "https://github.com" &&
            Value("GITHUB_API_URL") == "https://api.github.com" && Value("RUNNER_ENVIRONMENT") == "github-hosted" &&
            Value("RUNNER_OS") == "Linux" && Value("RUNNER_ARCH") == "X64", "s06-current-environment");
        var source = Value("GITHUB_SHA");
        GitHubReadTarget.RequireSha(source);
        Require(Value("GITHUB_WORKFLOW_SHA") == source, "s06-current-environment");
        var runId = Positive(Value("GITHUB_RUN_ID"));
        var attempt = Positive(Value("GITHUB_RUN_ATTEMPT"));
        Require(attempt <= int.MaxValue, "s06-current-environment");
        return new(path, job, source, runId, attempt);
    }

    internal static S06CurrentTiming ReadTiming(JsonElement run, JsonElement jobs,
        GitHubWorkflowIdentity expected, DateTimeOffset observedAtUtc)
    {
        expected.RequireValid();
        var name = JobName(expected.WorkflowPath);
        RequireCutoff(observedAtUtc);
        Require(expected.Repository == "GTX537/CP6" && Number(run, "id") == expected.RunId &&
            Number(run, "run_attempt") == expected.RunAttempt && Text(run, "head_sha") == expected.CommitSha &&
            Text(run, "head_branch") == "main" && Text(run, "path") == expected.WorkflowPath &&
            Text(run, "event") == "workflow_dispatch" && Text(Property(run, "repository"), "full_name") == expected.Repository &&
            Text(Property(run, "head_repository"), "full_name") == expected.Repository, "s06-current-run");
        Require(Text(run, "status") == "in_progress" && Property(run, "conclusion").ValueKind == JsonValueKind.Null,
            "s06-current-status");
        var created = Time(run, "created_at");
        var started = Time(run, "run_started_at");
        var updated = Time(run, "updated_at");
        Require(created <= started && started <= updated && updated <= observedAtUtc, "s06-current-time");
        var array = Property(jobs, "jobs");
        Require(Number(jobs, "total_count") == 1 && array.ValueKind == JsonValueKind.Array && array.GetArrayLength() == 1,
            "s06-current-jobs");
        var job = array[0];
        Require(Number(job, "id") > 0 && Text(job, "name") == name && Number(job, "run_id") == expected.RunId &&
            Number(job, "run_attempt") == expected.RunAttempt && Text(job, "head_sha") == expected.CommitSha &&
            Text(job, "head_branch") == "main", "s06-current-job");
        Require(Text(job, "status") == "in_progress" && Property(job, "conclusion").ValueKind == JsonValueKind.Null &&
            Property(job, "completed_at").ValueKind == JsonValueKind.Null, "s06-current-status");
        var jobStarted = Time(job, "started_at");
        Require(started <= jobStarted && jobStarted <= observedAtUtc, "s06-current-time");
        return new(started, jobStarted);
    }

    private static long Positive(string value)
    {
        Require(long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result > 0 &&
            result.ToString(CultureInfo.InvariantCulture) == value, "s06-current-environment");
        return result;
    }
}

internal sealed record S06CurrentSelection(string WorkflowPath, string JobName, string CommitSha, long RunId, long RunAttempt);
internal sealed record S06CurrentTiming(DateTimeOffset RunStartedAtUtc, DateTimeOffset JobStartedAtUtc);
```

#### tools/p10/ReleaseVerifier/S06CurrentWorkflow.cs

```csharp
using System.Runtime.InteropServices;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// An observed running job, never a completed-success publisher. This object is never deserialized from an artifact.
// A normal candidate consumer cannot select this path or supply a "currently publishing" boolean.
internal sealed class S06CurrentWorkflow
{
    private S06CurrentWorkflow(GitHubWorkflowIdentity workflow, S06CurrentTiming timing,
        GitHubWorkflowFileObservation file, DateTimeOffset observedAtUtc)
    {
        Workflow = workflow;
        RunStartedAtUtc = timing.RunStartedAtUtc;
        JobStartedAtUtc = timing.JobStartedAtUtc;
        File = file;
        ObservedAtUtc = observedAtUtc;
    }

    internal GitHubWorkflowIdentity Workflow { get; }
    internal DateTimeOffset RunStartedAtUtc { get; }
    internal DateTimeOffset JobStartedAtUtc { get; }
    internal GitHubWorkflowFileObservation File { get; }
    internal DateTimeOffset ObservedAtUtc { get; }

    internal static async Task<S06CurrentWorkflow> CaptureAsync(string workflowPath, string githubReadToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = S06CurrentWorkflowChecks.JobName(workflowPath);
        var values = S06CurrentWorkflowChecks.EnvironmentNames.ToDictionary(n => n, Environment.GetEnvironmentVariable,
            StringComparer.Ordinal);
        var selected = S06CurrentWorkflowChecks.ReadEnvironment(values, workflowPath);
        GitHubEvidenceChecks.Require(OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "s06-current-host");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            using var client = new GitHubReadClient(githubReadToken);
            var contents = await client.ReadAsync(GitHubReadTarget.Workflow("GTX537/CP6",
                selected.WorkflowPath, selected.CommitSha), deadline.Token);
            var workflow = new GitHubWorkflowIdentity("GTX537/CP6", selected.WorkflowPath,
                GitHubEvidenceChecks.Text(contents, "sha"), selected.RunId, selected.RunAttempt, selected.CommitSha);
            var file = GitHubWorkflowChecks.File(contents, workflow);
            _ = await GitHubEvidenceSource.ReadSourceAsync("GTX537/CP6", workflow.CommitSha, githubReadToken, deadline.Token);
            var run = await client.ReadAsync(GitHubReadTarget.Run("GTX537/CP6", workflow.RunId, workflow.RunAttempt), deadline.Token);
            var jobs = await client.ReadAsync(GitHubReadTarget.Jobs("GTX537/CP6", workflow.RunId, workflow.RunAttempt), deadline.Token);
            var observed = DateTimeOffset.UtcNow;
            var timing = S06CurrentWorkflowChecks.ReadTiming(run, jobs, workflow, observed);
            return new(workflow, timing, file, observed);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw GitHubWirePolicy.Error("s06-current-timeout");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw GitHubWirePolicy.Error("s06-current-read");
        }
    }
}
```

### Task 3: Verify, review and save

- [x] Run the focused/full suites and formatting below from `tools/p10`; require zero failed/skipped tests.
- [x] Check all plan code blocks against source, review the four-file scope and scan for sensitive/debug residue.
- [ ] Stage only the four named files and commit `feat(p10): observe current jobs without predicting publication success`.
- [ ] Exercise actual current-job capture in the protected S06 workflows; do not claim that acceptance before a real run.

```powershell
$env:DOTNET_ROOT = 'C:/Users/tt/.dotnet'
$env:DOTNET_HOST_PATH = 'C:/Users/tt/.dotnet/dotnet.exe'
$env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH
$env:P10_COSIGN_PATH = 'C:/Users/tt/AppData/Local/Temp/cp6-p10-cosign-c4ab1329239f4721a3f8f5ac741ce3c9/cosign-windows-amd64.exe'
$env:P10_FORMAL_PACKAGE_ROOT = 'D:/CP6.Platform-worktrees/p10-formal-schema-parity/artifacts/p10-0.10.1-publication/windows/feed-readback-packages'
$env:P10_GITHUB_READ_TOKEN = gh auth token
$env:P10_FEED_READ_TOKEN = $env:P10_GITHUB_READ_TOKEN
try {
  & $env:DOTNET_HOST_PATH test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~S06CurrentWorkflowTests
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
git add -- docs/superpowers/plans/2026-09-08-p10-s06-current-workflow.md tools/p10/ReleaseVerifier/S06CurrentWorkflowChecks.cs tools/p10/ReleaseVerifier/S06CurrentWorkflow.cs tools/p10/ReleaseVerifier.Tests/S06CurrentWorkflowTests.cs
git diff --cached --check
git diff --cached --stat
git commit -m "feat(p10): observe current jobs without predicting publication success"
```

## Self-review

- [x] Parent design 12.2/12.4 publication timing is handled without claiming the current workflow's final conclusion.
- [x] Required code, tests, signatures, paths and commands are complete.
- [x] Existing completed-success validation, protected approval, fixed trust, native signature and read-authority policies remain unchanged.

## Verification outcome (2026-09-08)

- RED: 45 failed, 0 passed, 0 skipped, expected throwing-scaffold failures after successful compilation.
- Focused GREEN: 45 passed, 0 failed, 0 skipped.
- Full Release suite: 1,307 passed, 0 failed, 0 skipped in 49 seconds.
- Initial format check found eight whitespace locations in the new anonymous run-response test vector. Reflowed those properties only, in both the test and plan; no production behavior or existing test changed.
- Format recheck exited 0; the rebuilt focused suite again passed 45/45 with zero skips.
- Positive live capture is still pending protected S06 execution. No current publisher success is claimed, and no remote S06 operation occurred.
