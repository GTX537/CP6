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
