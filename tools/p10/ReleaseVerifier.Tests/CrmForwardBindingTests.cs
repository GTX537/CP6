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
