# P10 S06 CRM Forward Binding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Continue the already selected sequential mode in the existing S06 worktree, without agents.

**Goal:** Verify live successful PR/main workflow pairs for the original S05 consumer delivery and its evidence-forward-binding delivery.

**Architecture:** A sealed private-constructor selection fixes the two reviewed PRs, their source/base/merge commits, workflow blob, attempt and five-job profile. PR-specific entry points share private run/job checking cores with existing main-only checks; the main entry points retain their original branch and event policy. The live source reads fixed PR metadata plus the PR and exact-main workflows and requires PR completion <= merge <= main start <= main completion <= cutoff.

**Tech Stack:** .NET 8 BCL, existing bounded GitHub client, existing xUnit project.

## Scope and verified inputs

This module reads GTX537/CP6.CRM only; it never reruns or modifies CRM, changes protections, publishes packages, writes OCI/R2 objects, or accepts an S06 candidate. It returns selected internal live observations, not a public acceptance capability. All existing repository/event/file/attempt/job/time checks remain in force. All network calls retain the existing fixed-host, bounded-response, no-redirect policy. A whole-operation three-minute deadline includes both workflow reads. Cutoffs cannot be caller-selected future times.

Reviewed immutable pairs, rechecked via live GitHub before this plan:

| PR | PR workflow run / attempt | main workflow run / attempt | merge |
| --- | --- | --- | --- |
| 46 | 34133944140 / 1 | 34134695003 / 1 | a31ca0e323418f7e4108cc6220c0f5fa132e7fc2 |
| 47 | 34137355910 / 1 | 34138142163 / 1 | bb1fd8b4f250fabde4476b6a450435de2d07c03f |

Both use .github/workflows/crm-validation.yml with Git blob 924014cb1231824a9b57ab82a6f9638f76329919 and size 14,913. Each existing workflow has exactly five completed successful jobs. This historical job profile is not a new job requirement for current CRM CI.

GitHub's actual run responses have empty pull_requests arrays. The live source therefore binds the explicit pinned PR metadata (number, closed/merged status, head/base repository, branch and SHA, merge commit/time), PR workflow identity and exact-main workflow; it does not pretend the empty array proves PR membership. Raw consumer record producer checkout 5d307d520a3bd7d5628d65cd98500869c9e7b101 is distinct from PR 46 head 7b099200165e46f613a2689806f4a2a928425a8f. This module must reject using the checkout SHA as the run's head SHA. The retained raw-record semantics and checkout tree relation are separate subsequent checks.

GitHub documents Get a pull request as accepting either Contents read or Pull requests read; the existing CRM Contents/Actions read credential is the intended scope. Do not request broader access preemptively or use this module to rewrite credentials. References: [Get a pull request](https://docs.github.com/en/rest/pulls/pulls#get-a-pull-request), [Get a workflow run attempt](https://docs.github.com/en/rest/actions/workflow-runs#get-a-workflow-run-attempt). These APIs were checked on 2026-09-08.

## Task 1: Exact tests and RED

**Create:** tools/p10/ReleaseVerifier.Tests/CrmForwardBindingTests.cs.

- [x] Write the tests below and only throwing scaffolds for missing APIs.
- [x] Run the focused Release tests and inspect every failure for the missing implementation, not compilation or credentials. No skips are permitted.

From tools/p10 with the selected .NET 8 SDK; P10_GITHUB_READ_TOKEN is passed only through process environment:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~CrmForwardBindingTests --logger "trx;LogFileName=crm-forward-red.trx" --results-directory ../../artifacts/p10/crm-forward-red
```

```csharp
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

public sealed class CrmForwardBindingTests
{
    private static readonly DateTimeOffset Cutoff = Utc("2026-09-07T16:00:00Z");

    [Theory]
    [InlineData(46, 34133944140, 34134695003, "2026-09-07T14:45:21Z")]
    [InlineData(47, 34137355910, 34138142163, "2026-09-07T15:24:24Z")]
    public async Task Live_PR_and_main_runs_bind_exact_commits_files_jobs_and_merge_order(
        int number, long prRun, long mainRun, string merged)
    {
        var token = Environment.GetEnvironmentVariable("P10_GITHUB_READ_TOKEN")
            ?? throw new InvalidOperationException("Live CRM forward binding is mandatory; this test does not skip.");
        var before = DateTimeOffset.UtcNow;
        var result = await CrmForwardBindingSource.ReadAsync(number, Cutoff, token);
        var selected = CrmPullRequestSelection.Get(number);
        Assert.Same(selected, result.Selection);
        Assert.Equal(Utc(merged), result.MergedAtUtc);
        Assert.Equal(selected.PullRequestWorkflow, result.PullRequest.Workflow);
        Assert.Equal(selected.MainWorkflow, result.Main.Workflow);
        Assert.Equal(prRun, result.PullRequest.Workflow.RunId);
        Assert.Equal(mainRun, result.Main.Workflow.RunId);
        Assert.Equal("pull_request", result.PullRequest.Event);
        Assert.Equal("push", result.Main.Event);
        Assert.Equal(14913, result.PullRequest.File.ByteLength);
        Assert.Equal(result.Main.File, result.PullRequest.File);
        Assert.Equal(CrmPullRequestSelection.RequiredJobs, result.PullRequest.Jobs.Select(j => j.Name));
        Assert.Equal(CrmPullRequestSelection.RequiredJobs, result.Main.Jobs.Select(j => j.Name));
        Assert.True(result.PullRequest.CompletedAtUtc <= result.MergedAtUtc);
        Assert.True(result.MergedAtUtc <= result.Main.StartedAtUtc);
        Assert.True(result.Main.CompletedAtUtc <= Cutoff);
        Assert.InRange(result.ObservedAtUtc, before, DateTimeOffset.UtcNow);
        Assert.DoesNotContain("runner_name", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(46, "7b099200165e46f613a2689806f4a2a928425a8f", S06ReleaseIdentity.CrmSource)]
    [InlineData(47, "574c2d59b28141e7912573f53d9b99a6c902798e", PinnedReleaseDocument.CommitSha)]
    public void Selection_is_fixed_and_PR_target_cannot_be_caller_supplied(int number, string head, string merge)
    {
        var selected = CrmPullRequestSelection.Get(number);
        Assert.Equal(head, selected.PullRequestWorkflow.CommitSha);
        Assert.Equal(merge, selected.MainWorkflow.CommitSha);
        Assert.Equal(1, selected.PullRequestWorkflow.RunAttempt);
        Assert.Equal(1, selected.MainWorkflow.RunAttempt);
        Assert.Equal("/repos/GTX537/CP6.CRM/pulls/" + number.ToString(CultureInfo.InvariantCulture),
            GitHubReadTarget.CrmPullRequest(selected).Path);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)CrmPullRequestSelection.RequiredJobs).Clear());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(48)]
    [InlineData(int.MaxValue)]
    public async Task Unreviewed_PR_cannot_select_a_network_target(int number)
    {
        Reject(() => CrmPullRequestSelection.Get(number), "github-crm-selection");
        Assert.Equal("github-crm-selection", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            CrmForwardBindingSource.ReadAsync(number, Cutoff, ""))).Code);
    }

    [Theory]
    [InlineData("number")]
    [InlineData("state")]
    [InlineData("unmerged")]
    [InlineData("merged-string")]
    [InlineData("merge-sha")]
    [InlineData("head-sha")]
    [InlineData("head-ref")]
    [InlineData("head-repo")]
    [InlineData("base-sha")]
    [InlineData("base-ref")]
    [InlineData("base-repo")]
    [InlineData("merged-null")]
    [InlineData("merged-after-cutoff")]
    [InlineData("head-missing")]
    public void Metadata_cannot_substitute_a_fork_unmerged_PR_or_different_commit(string mutation)
    {
        var value = PullRequestNode(47);
        if (mutation == "number") value["number"] = 46;
        if (mutation == "state") value["state"] = "open";
        if (mutation == "unmerged") value["merged"] = false;
        if (mutation == "merged-string") value["merged"] = "true";
        if (mutation == "merge-sha") value["merge_commit_sha"] = S06ReleaseIdentity.CrmSource;
        if (mutation == "head-sha") value["head"]!["sha"] = S06ReleaseIdentity.CrmSource;
        if (mutation == "head-ref") value["head"]!["ref"] = "main";
        if (mutation == "head-repo") value["head"]!["repo"]!["full_name"] = "fork/CP6.CRM";
        if (mutation == "base-sha") value["base"]!["sha"] = new string('c', 40);
        if (mutation == "base-ref") value["base"]!["ref"] = "release";
        if (mutation == "base-repo") value["base"]!["repo"]!["full_name"] = "fork/CP6.CRM";
        if (mutation == "merged-null") value["merged_at"] = null;
        if (mutation == "merged-after-cutoff") value["merged_at"] = "2026-09-07T16:00:01Z";
        if (mutation == "head-missing") value.Remove("head");
        Reject(() => CrmForwardBindingChecks.MergedPullRequest(Element(value), CrmPullRequestSelection.Get(47), Cutoff));
    }

    [Theory]
    [InlineData(46)]
    [InlineData(47)]
    public void PR_binding_does_not_relax_main_only_checks_or_follow_empty_PR_links(int number)
    {
        var selected = CrmPullRequestSelection.Get(number);
        var merged = CrmForwardBindingChecks.MergedPullRequest(Element(PullRequestNode(number)), selected, Cutoff);
        var run = Element(RunNode(number));
        var timing = GitHubWorkflowChecks.CompletedPullRequestRun(run, selected, merged);
        var jobs = Element(JobsNode(number));
        Assert.Equal("pull_request", timing.Event);
        Assert.Equal(5, GitHubWorkflowChecks.PullRequestJobs(jobs, selected, timing.Started, timing.Completed).Count);
        Reject(() => GitHubWorkflowChecks.CompletedRun(run, selected.PullRequestWorkflow, merged), "github-run-identity");
        Reject(() => GitHubWorkflowChecks.Jobs(jobs, selected.PullRequestWorkflow,
            CrmPullRequestSelection.RequiredJobs, timing.Started, timing.Completed), "github-job-identity");
    }

    [Theory]
    [InlineData("id")]
    [InlineData("attempt")]
    [InlineData("tested-merge-instead-of-head")]
    [InlineData("branch")]
    [InlineData("event")]
    [InlineData("path")]
    [InlineData("repository")]
    [InlineData("head-repository")]
    [InlineData("status")]
    [InlineData("conclusion")]
    [InlineData("completed-after-merge")]
    public void PR_run_must_be_successful_and_bound_to_the_reviewed_head_attempt_and_event(string mutation)
    {
        var run = RunNode(46);
        if (mutation == "id") run["id"] = 34137355910;
        if (mutation == "attempt") run["run_attempt"] = 2;
        if (mutation == "tested-merge-instead-of-head") run["head_sha"] = "5d307d520a3bd7d5628d65cd98500869c9e7b101";
        if (mutation == "branch") run["head_branch"] = "main";
        if (mutation == "event") run["event"] = "pull_request_target";
        if (mutation == "path") run["path"] = ".github/workflows/another.yml";
        if (mutation == "repository") run["repository"]!["full_name"] = "fork/CP6.CRM";
        if (mutation == "head-repository") run["head_repository"]!["full_name"] = "fork/CP6.CRM";
        if (mutation == "status") run["status"] = "in_progress";
        if (mutation == "conclusion") run["conclusion"] = "failure";
        if (mutation == "completed-after-merge") run["updated_at"] = "2026-09-07T14:45:22Z";
        Reject(() => GitHubWorkflowChecks.CompletedPullRequestRun(Element(run),
            CrmPullRequestSelection.Get(46), Utc("2026-09-07T14:45:21Z")));
    }

    [Theory]
    [InlineData("run")]
    [InlineData("attempt")]
    [InlineData("head")]
    [InlineData("branch")]
    [InlineData("name")]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("duplicate")]
    [InlineData("skipped")]
    [InlineData("failed")]
    [InlineData("after-run")]
    public void PR_job_set_cannot_be_incomplete_substituted_or_skipped(string mutation)
    {
        var jobs = JobsNode(47);
        var array = jobs["jobs"]!.AsArray();
        var job = array[0]!;
        if (mutation == "run") job["run_id"] = 34133944140;
        if (mutation == "attempt") job["run_attempt"] = 2;
        if (mutation == "head") job["head_sha"] = S06ReleaseIdentity.CrmSource;
        if (mutation == "branch") job["head_branch"] = "main";
        if (mutation == "name") job["name"] = "unrequired-job";
        if (mutation == "missing") array.RemoveAt(4);
        if (mutation == "extra") array.Add(job.DeepClone());
        if (mutation == "duplicate") array[1]!["id"] = 1;
        if (mutation == "skipped") job["conclusion"] = "skipped";
        if (mutation == "failed") job["conclusion"] = "failure";
        if (mutation == "after-run") job["completed_at"] = "2026-09-07T15:23:06Z";
        Reject(() => GitHubWorkflowChecks.PullRequestJobs(Element(jobs), CrmPullRequestSelection.Get(47),
            Utc("2026-09-07T15:15:22Z"), Utc("2026-09-07T15:23:05Z")));
    }

    [Theory]
    [InlineData("pr-after-merge")]
    [InlineData("main-before-merge")]
    [InlineData("main-after-cutoff")]
    [InlineData("non-utc")]
    public void Forward_order_is_independent_of_success_strings(string mutation)
    {
        var prCompleted = Utc("2026-09-07T15:23:05Z");
        var merged = Utc("2026-09-07T15:24:24Z");
        var mainStarted = Utc("2026-09-07T15:24:26Z");
        var mainCompleted = Utc("2026-09-07T15:33:00Z");
        var cutoff = Cutoff;
        if (mutation == "pr-after-merge") prCompleted = merged.AddSeconds(1);
        if (mutation == "main-before-merge") mainStarted = merged.AddSeconds(-1);
        if (mutation == "main-after-cutoff") mainCompleted = cutoff.AddSeconds(1);
        if (mutation == "non-utc") cutoff = cutoff.ToOffset(TimeSpan.FromHours(1));
        Reject(() => CrmForwardBindingChecks.ForwardOrder(prCompleted, merged, mainStarted, mainCompleted, cutoff),
            "github-proof-time");
    }

    [Fact]
    public async Task Future_cutoff_and_precancellation_fail_before_credentials()
    {
        Assert.Equal("github-proof-time", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            CrmForwardBindingSource.ReadAsync(47, DateTimeOffset.UtcNow.AddDays(1), ""))).Code);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CrmForwardBindingSource.ReadAsync(0, Cutoff, "", cancel.Token));
    }

    // Local selected-field parser vectors only. They are not candidate acceptance or archived raw evidence.
    private static JsonObject PullRequestNode(int number)
    {
        var selected = CrmPullRequestSelection.Get(number);
        return new()
        {
            ["number"] = number,
            ["state"] = "closed",
            ["merged"] = true,
            ["merge_commit_sha"] = selected.MainWorkflow.CommitSha,
            ["merged_at"] = number == 46 ? "2026-09-07T14:45:21Z" : "2026-09-07T15:24:24Z",
            ["head"] = new JsonObject
            {
                ["sha"] = selected.PullRequestWorkflow.CommitSha,
                ["ref"] = selected.HeadBranch,
                ["repo"] = new JsonObject { ["full_name"] = CrmPullRequestSelection.Repository }
            },
            ["base"] = new JsonObject
            {
                ["sha"] = selected.BaseSha,
                ["ref"] = "main",
                ["repo"] = new JsonObject { ["full_name"] = CrmPullRequestSelection.Repository }
            }
        };
    }

    private static JsonObject RunNode(int number)
    {
        var selected = CrmPullRequestSelection.Get(number);
        var start = number == 46 ? "2026-09-07T14:37:13Z" : "2026-09-07T15:15:22Z";
        return new()
        {
            ["id"] = selected.PullRequestWorkflow.RunId,
            ["run_attempt"] = 1,
            ["head_sha"] = selected.PullRequestWorkflow.CommitSha,
            ["head_branch"] = selected.HeadBranch,
            ["path"] = CrmPullRequestSelection.WorkflowPath,
            ["event"] = "pull_request",
            ["repository"] = new JsonObject { ["full_name"] = CrmPullRequestSelection.Repository },
            ["head_repository"] = new JsonObject { ["full_name"] = CrmPullRequestSelection.Repository },
            ["status"] = "completed",
            ["conclusion"] = "success",
            ["created_at"] = start,
            ["run_started_at"] = start,
            ["updated_at"] = number == 46 ? "2026-09-07T14:44:17Z" : "2026-09-07T15:23:05Z",
            ["pull_requests"] = new JsonArray()
        };
    }

    private static JsonObject JobsNode(int number)
    {
        var selected = CrmPullRequestSelection.Get(number);
        var start = number == 46 ? "2026-09-07T14:37:20Z" : "2026-09-07T15:15:30Z";
        var end = number == 46 ? "2026-09-07T14:44:10Z" : "2026-09-07T15:23:00Z";
        return new()
        {
            ["total_count"] = 5,
            ["jobs"] = new JsonArray(CrmPullRequestSelection.RequiredJobs.Select((name, index) => (JsonNode)new JsonObject
            {
                ["id"] = index + 1,
                ["name"] = name,
                ["run_id"] = selected.PullRequestWorkflow.RunId,
                ["run_attempt"] = 1,
                ["head_sha"] = selected.PullRequestWorkflow.CommitSha,
                ["head_branch"] = selected.HeadBranch,
                ["status"] = "completed",
                ["conclusion"] = "success",
                ["started_at"] = start,
                ["completed_at"] = end
            }).ToArray())
        };
    }

    private static DateTimeOffset Utc(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    private static JsonElement Element(JsonNode value) => GitHubApiJson.Parse(Encoding.UTF8.GetBytes(value.ToJsonString()));
    private static void Reject(Action action, string? code = null)
    {
        var error = Assert.Throws<Cp6ReleaseContractException>(action);
        if (code is not null) Assert.Equal(code, error.Code);
        Assert.Null(error.InnerException);
    }
}
```

## Task 2: Minimal implementation and GREEN

- [x] Apply the following three new files and two complete updated files only after inspecting RED.
- [x] Re-run the focused command with crm-forward-green.trx and a separate results directory. Both live cases must read actual PR metadata and all four actual workflow runs, their files and jobs.

### tools/p10/ReleaseVerifier/CrmPullRequestSelection.cs

```csharp
namespace CP6.P10.ReleaseVerifier;

// The two reviewed S05 deliveries, not a caller-controlled PR/workflow selector.
internal sealed class CrmPullRequestSelection
{
    internal const string Repository = "GTX537/CP6.CRM";
    internal const string WorkflowPath = ".github/workflows/crm-validation.yml";
    internal const string WorkflowFileSha = "924014cb1231824a9b57ab82a6f9638f76329919";
    private CrmPullRequestSelection(int number, string headBranch, string headSha, string baseSha,
        string mergeSha, long pullRequestRunId, long mainRunId)
    {
        Number = number;
        HeadBranch = headBranch;
        BaseSha = baseSha;
        PullRequestWorkflow = new(Repository, WorkflowPath, WorkflowFileSha, pullRequestRunId, 1, headSha);
        MainWorkflow = new(Repository, WorkflowPath, WorkflowFileSha, mainRunId, 1, mergeSha);
    }

    internal int Number { get; }
    internal string HeadBranch { get; }
    internal string BaseSha { get; }
    internal GitHubWorkflowIdentity PullRequestWorkflow { get; }
    internal GitHubWorkflowIdentity MainWorkflow { get; }
    internal static IReadOnlyList<string> RequiredJobs { get; } = Array.AsReadOnly(new[]
    {
        "crm-validation", "platform-p06-sql-consumer", "platform-p10-formal-ubuntu-latest",
        "platform-p10-formal-windows-latest", "platform-p10-test-candidate"
    });

    private static readonly CrmPullRequestSelection Consumer = new(46, "codex/p10-s05-formal-package-consumer",
        "7b099200165e46f613a2689806f4a2a928425a8f", "4a340042f3596e82e7d358a39ad3106933c4395d",
        S06ReleaseIdentity.CrmSource, 34133944140, 34134695003);
    private static readonly CrmPullRequestSelection Archive = new(47, "codex/p10-s05-formal-consumer-evidence",
        "574c2d59b28141e7912573f53d9b99a6c902798e", S06ReleaseIdentity.CrmSource,
        PinnedReleaseDocument.CommitSha, 34137355910, 34138142163);

    internal static CrmPullRequestSelection Get(int number) => number switch
    {
        46 => Consumer,
        47 => Archive,
        _ => throw GitHubWirePolicy.Error("github-crm-selection")
    };
}
```

### tools/p10/ReleaseVerifier/CrmForwardBindingChecks.cs

```csharp
using System.Text.Json;
using static CP6.P10.ReleaseVerifier.GitHubEvidenceChecks;

namespace CP6.P10.ReleaseVerifier;

internal static class CrmForwardBindingChecks
{
    internal static DateTimeOffset MergedPullRequest(JsonElement value, CrmPullRequestSelection selected, DateTimeOffset cutoff)
    {
        RequireCutoff(cutoff);
        Require(Number(value, "number") == selected.Number && Text(value, "state") == "closed" &&
            Property(value, "merged").ValueKind == JsonValueKind.True &&
            Text(value, "merge_commit_sha") == selected.MainWorkflow.CommitSha, "github-crm-merge");
        var head = Property(value, "head");
        var source = Property(value, "base");
        Require(Text(head, "sha") == selected.PullRequestWorkflow.CommitSha && Text(head, "ref") == selected.HeadBranch &&
            Text(Property(head, "repo"), "full_name") == CrmPullRequestSelection.Repository &&
            Text(source, "sha") == selected.BaseSha && Text(source, "ref") == "main" &&
            Text(Property(source, "repo"), "full_name") == CrmPullRequestSelection.Repository, "github-crm-merge");
        var merged = Time(value, "merged_at");
        Require(merged <= cutoff, "github-proof-time");
        return merged;
    }

    internal static void ForwardOrder(DateTimeOffset pullRequestCompleted, DateTimeOffset merged,
        DateTimeOffset mainStarted, DateTimeOffset mainCompleted, DateTimeOffset cutoff)
    {
        RequireCutoff(cutoff);
        RequireCutoff(pullRequestCompleted);
        RequireCutoff(merged);
        RequireCutoff(mainStarted);
        RequireCutoff(mainCompleted);
        Require(pullRequestCompleted <= merged && merged <= mainStarted &&
            mainStarted <= mainCompleted && mainCompleted <= cutoff, "github-proof-time");
    }
}
```

### tools/p10/ReleaseVerifier/CrmForwardBindingSource.cs

```csharp
using static CP6.P10.ReleaseVerifier.GitHubEvidenceChecks;

namespace CP6.P10.ReleaseVerifier;

// Live selected observations. This is not candidate acceptance and does not rewrite or rerun CRM.
internal static class CrmForwardBindingSource
{
    internal static async Task<CrmForwardBindingObservation> ReadAsync(int number, DateTimeOffset completedBeforeUtc,
        string token, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selected = CrmPullRequestSelection.Get(number);
        RequireCutoff(completedBeforeUtc);
        Require(completedBeforeUtc <= DateTimeOffset.UtcNow, "github-proof-time");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        using var client = new GitHubReadClient(token);
        var merged = CrmForwardBindingChecks.MergedPullRequest(
            await client.ReadAsync(GitHubReadTarget.CrmPullRequest(selected), deadline.Token), selected, completedBeforeUtc);
        var expected = selected.PullRequestWorkflow;
        var run = await client.ReadAsync(GitHubReadTarget.Run(expected.Repository, expected.RunId, expected.RunAttempt), deadline.Token);
        var timing = GitHubWorkflowChecks.CompletedPullRequestRun(run, selected, merged);
        var file = GitHubWorkflowChecks.File(await client.ReadAsync(
            GitHubReadTarget.Workflow(expected.Repository, expected.WorkflowPath, expected.CommitSha), deadline.Token), expected);
        var jobs = GitHubWorkflowChecks.PullRequestJobs(await client.ReadAsync(
            GitHubReadTarget.Jobs(expected.Repository, expected.RunId, expected.RunAttempt), deadline.Token),
            selected, timing.Started, timing.Completed);
        var pullRequest = new GitHubWorkflowObservation(expected, timing.Event, timing.Started, timing.Completed,
            file, jobs, DateTimeOffset.UtcNow);
        var main = await GitHubEvidenceSource.ReadWorkflowAsync(selected.MainWorkflow,
            CrmPullRequestSelection.RequiredJobs, completedBeforeUtc, token, deadline.Token);
        CrmForwardBindingChecks.ForwardOrder(pullRequest.CompletedAtUtc, merged, main.StartedAtUtc, main.CompletedAtUtc, completedBeforeUtc);
        return new(selected, merged, pullRequest, main, DateTimeOffset.UtcNow);
    }
}

internal sealed record CrmForwardBindingObservation(CrmPullRequestSelection Selection, DateTimeOffset MergedAtUtc,
    GitHubWorkflowObservation PullRequest, GitHubWorkflowObservation Main, DateTimeOffset ObservedAtUtc);
```

### tools/p10/ReleaseVerifier/GitHubWorkflowChecks.cs

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.GitHubEvidenceChecks;

namespace CP6.P10.ReleaseVerifier;

internal static class GitHubWorkflowChecks
{
    internal static GitHubWorkflowFileObservation File(JsonElement file, GitHubWorkflowIdentity expected)
    {
        expected.RequireValid();
        Require(Text(file, "type") == "file" && Text(file, "path") == expected.WorkflowPath &&
            Text(file, "sha") == expected.WorkflowFileSha && Text(file, "encoding") == "base64", "github-workflow-file");
        var declared = Number(file, "size");
        Require(declared is > 0 and <= 49152, "github-workflow-file");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(Text(file, "content")); }
        catch (FormatException) { throw GitHubWirePolicy.Error("github-workflow-file"); }
        Require(bytes.Length == declared, "github-workflow-file");
        // Git's SHA-1 is an exact file identity check, not the evidence content-address hash.
        var header = Encoding.ASCII.GetBytes("blob " + bytes.Length.ToString(CultureInfo.InvariantCulture) + "\0");
        var blob = header.Concat(bytes).ToArray();
        Require(Convert.ToHexString(SHA1.HashData(blob)).ToLowerInvariant() == expected.WorkflowFileSha, "github-workflow-file");
        return new(Cp6DeterministicJson.Sha256Hex(bytes), bytes.Length);
    }

    internal static (DateTimeOffset Started, DateTimeOffset Completed, string Event) CompletedRun(
        JsonElement run, GitHubWorkflowIdentity expected, DateTimeOffset cutoff)
        => CompletedRunCore(run, expected, cutoff, "main",
            expected.Repository == "GTX537/CP6.CRM" ? "push" : "workflow_dispatch");

    internal static (DateTimeOffset Started, DateTimeOffset Completed, string Event) CompletedPullRequestRun(
        JsonElement run, CrmPullRequestSelection selected, DateTimeOffset cutoff) =>
        CompletedRunCore(run, selected.PullRequestWorkflow, cutoff, selected.HeadBranch, "pull_request");

    private static (DateTimeOffset Started, DateTimeOffset Completed, string Event) CompletedRunCore(
        JsonElement run, GitHubWorkflowIdentity expected, DateTimeOffset cutoff, string branch, string expectedEvent)
    {
        expected.RequireValid();
        RequireCutoff(cutoff);
        Require(Number(run, "id") == expected.RunId && Number(run, "run_attempt") == expected.RunAttempt &&
            Text(run, "head_sha") == expected.CommitSha && Text(run, "head_branch") == branch &&
            Text(run, "path") == expected.WorkflowPath && Text(run, "event") == expectedEvent &&
            Text(Property(run, "repository"), "full_name") == expected.Repository &&
            Text(Property(run, "head_repository"), "full_name") == expected.Repository, "github-run-identity");
        Require(Text(run, "status") == "completed" && Text(run, "conclusion") == "success", "github-run-conclusion");
        var created = Time(run, "created_at");
        var started = Time(run, "run_started_at");
        var completed = Time(run, "updated_at");
        Require(created <= started && started <= completed && completed <= cutoff, "github-proof-time");
        return (started, completed, expectedEvent);
    }

    internal static IReadOnlyList<GitHubJobObservation> Jobs(JsonElement jobs, GitHubWorkflowIdentity expected,
        IReadOnlyCollection<string> requiredNames, DateTimeOffset runStarted, DateTimeOffset runCompleted) =>
        JobsCore(jobs, expected, requiredNames, runStarted, runCompleted, "main");

    internal static IReadOnlyList<GitHubJobObservation> PullRequestJobs(JsonElement jobs, CrmPullRequestSelection selected,
        DateTimeOffset runStarted, DateTimeOffset runCompleted) =>
        JobsCore(jobs, selected.PullRequestWorkflow, CrmPullRequestSelection.RequiredJobs, runStarted, runCompleted, selected.HeadBranch);

    private static IReadOnlyList<GitHubJobObservation> JobsCore(JsonElement jobs, GitHubWorkflowIdentity expected,
        IReadOnlyCollection<string> requiredNames, DateTimeOffset runStarted, DateTimeOffset runCompleted, string branch)
    {
        expected.RequireValid();
        Require(requiredNames.Count is > 0 and <= 100 && requiredNames.Distinct(StringComparer.Ordinal).Count() == requiredNames.Count &&
            requiredNames.All(n => !string.IsNullOrEmpty(n) && n.Length <= 128 && !n.Any(char.IsControl)), "github-job-profile");
        var array = Property(jobs, "jobs");
        Require(array.ValueKind == JsonValueKind.Array, "github-job-set");
        Require(Number(jobs, "total_count") == requiredNames.Count && array.GetArrayLength() == requiredNames.Count, "github-job-set");
        var names = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<long>();
        var result = new List<GitHubJobObservation>();
        foreach (var job in array.EnumerateArray())
        {
            var name = Text(job, "name");
            var id = Number(job, "id");
            Require(id > 0 && ids.Add(id) && names.Add(name) && requiredNames.Contains(name, StringComparer.Ordinal), "github-job-set");
            Require(Number(job, "run_id") == expected.RunId && Number(job, "run_attempt") == expected.RunAttempt &&
                Text(job, "head_sha") == expected.CommitSha && Text(job, "head_branch") == branch, "github-job-identity");
            Require(Text(job, "status") == "completed" && Text(job, "conclusion") == "success", "github-job-conclusion");
            var started = Time(job, "started_at");
            var completed = Time(job, "completed_at");
            Require(runStarted <= started && started <= completed && completed <= runCompleted, "github-proof-time");
            result.Add(new(id, name, started, completed));
        }
        return Array.AsReadOnly(result.OrderBy(j => j.Name, StringComparer.Ordinal).ToArray());
    }
}
```

### tools/p10/ReleaseVerifier/GitHubReadTarget.cs

```csharp
using System.Globalization;

namespace CP6.P10.ReleaseVerifier;

// No caller-selected URL, HTTP method, response link, pagination URL or repository owner.
internal sealed class GitHubReadTarget
{
    private GitHubReadTarget(string path) => Path = path;
    internal string Path { get; }

    internal static GitHubReadTarget Run(string repository, long runId, long attempt) =>
        new(RunPath(repository, runId, attempt));
    internal static GitHubReadTarget Jobs(string repository, long runId, long attempt) =>
        new(RunPath(repository, runId, attempt) + "/jobs?per_page=100&page=1");
    internal static GitHubReadTarget MainBranch(string repository) => new(RepositoryPath(repository) + "/branches/main");
    internal static GitHubReadTarget Compare(string repository, string source, string observedMain)
    {
        RequireSha(source);
        RequireSha(observedMain);
        return new(RepositoryPath(repository) + "/compare/" + source + "..." + observedMain + "?per_page=1&page=2");
    }

    internal static GitHubReadTarget Workflow(string repository, string path, string source)
    {
        RequireSha(source);
        var allowed = repository switch
        {
            "GTX537/CP6" => path is S06ReleaseIdentity.ValidationPath or S06ReleaseIdentity.PublicationPath,
            "GTX537/CP6.Platform" => path == ".github/workflows/p10-formal-packages.yml",
            "GTX537/CP6.CRM" => path == ".github/workflows/crm-validation.yml",
            _ => false
        };
        if (!allowed) throw GitHubWirePolicy.Error("github-workflow");
        return new(RepositoryPath(repository) + "/contents/" + path + "?ref=" + source);
    }

    internal static GitHubReadTarget Archive(PinnedReleaseDocument document) =>
        new(RepositoryPath(PinnedReleaseDocument.Repository) + "/contents/" + document.Path + "?ref=" + PinnedReleaseDocument.CommitSha);

    internal static GitHubReadTarget CrmPullRequest(CrmPullRequestSelection selected) =>
        new(RepositoryPath(CrmPullRequestSelection.Repository) + "/pulls/" + selected.Number.ToString(CultureInfo.InvariantCulture));

    internal static void RequireSha(string value)
    {
        if (value is null || value.Length != 40 || value.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw GitHubWirePolicy.Error("github-sha");
    }

    private static string RunPath(string repository, long runId, long attempt)
    {
        if (runId <= 0 || attempt <= 0 || attempt > int.MaxValue) throw GitHubWirePolicy.Error("github-run");
        return RepositoryPath(repository) + "/actions/runs/" + runId.ToString(CultureInfo.InvariantCulture) +
            "/attempts/" + attempt.ToString(CultureInfo.InvariantCulture);
    }

    private static string RepositoryPath(string repository)
    {
        if (repository is not ("GTX537/CP6" or "GTX537/CP6.Platform" or "GTX537/CP6.CRM"))
            throw GitHubWirePolicy.Error("github-repository");
        return "/repos/" + repository;
    }
}
```

## Task 3: Regression, review and commit

- [x] Run locked restore, full Release regression and format verification using the existing required real cosign/package/feed/GitHub inputs; keep credentials out of files, arguments and output.
- [x] Review the full scoped diff and exact plan parity. Confirm no runtime, workflow, dependency, old evidence, trust or unrelated file changed.
- [ ] Stage only this plan, the one test file and the five production files listed above, then commit feat(p10): bind CRM PR and main validation evidence.
- [ ] Continue retained CRM record semantics and S06 candidate integration. This module is not P10 completion.

```powershell
dotnet restore ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --configfile ../../eng/p10/NuGet.formal.config --packages ../../artifacts/p10/foundation-restore/packages --locked-mode --verbosity minimal
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore
dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --verify-no-changes --no-restore
git diff --check
```

## Verification outcome (2026-09-08)

All 52 focused tests failed first with the expected missing implementation, with zero unrelated failures or skips, then passed with both real PR/main pairs. Full Release regression passed 640/640, zero skipped; locked restore reported no changes. Format verification identified only new test JSON-initializer whitespace; the formatter was restricted to that file, the plan was synchronized, and format verification then passed. Fresh post-format full regression again passed 640/640. Exact six-file plan/source parity, scoped diff and secret hygiene checks passed. No credential was printed or written, and no workflow, package, old archive, runtime, OCI or R2 object was modified. This is verified live forward binding, not S06 candidate acceptance or P10 completion.
