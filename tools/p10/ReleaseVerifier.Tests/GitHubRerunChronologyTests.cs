using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Parser vectors use the timestamps observed in run 34221083970/attempts/2.
// Running/success statuses below are deliberately synthetic, not live success evidence.
public sealed class GitHubRerunChronologyTests
{
    private const string OriginalCreated = "2026-09-08T11:30:49Z";
    private const string OriginalEnded = "2026-09-08T11:48:29Z";
    private const string Started = "2026-09-08T12:06:29Z";
    private const string Created = "2026-09-08T12:06:31Z";
    private const string Updated = "2026-09-08T12:08:30Z";
    private static readonly DateTimeOffset Cutoff = At("2026-09-08T12:09:00Z");

    [Theory]
    [InlineData(S06ReleaseIdentity.ValidationPath, Created)]
    [InlineData(S06ReleaseIdentity.PublicationPath, Created)]
    [InlineData(S06ReleaseIdentity.ValidationPath, "2026-09-08T12:06:28Z")]
    public void Current_rerun_binds_original_attempt_without_using_record_creation_as_start(string path, string created)
    {
        var expected = Identity(path);
        var run = Run(expected, false);
        run["created_at"] = created;
        var timing = S06CurrentWorkflowChecks.ReadTiming(Element(run), Element(Jobs(expected, false)), expected,
            Cutoff, Element(First(expected)));
        Assert.Equal(At(Started), timing.RunStartedAtUtc);
        Assert.Equal(At("2026-09-08T12:07:28Z"), timing.JobStartedAtUtc);
    }

    [Theory]
    [InlineData("GTX537/CP6", S06ReleaseIdentity.ValidationPath)]
    [InlineData("GTX537/CP6", S06ReleaseIdentity.PublicationPath)]
    [InlineData("GTX537/CP6.Platform", ".github/workflows/p10-formal-packages.yml")]
    [InlineData("GTX537/CP6.CRM", ".github/workflows/crm-validation.yml")]
    public void Completed_rerun_can_follow_a_failed_original_attempt_but_requires_its_own_success(string repo, string path)
    {
        var expected = Identity(path) with { Repository = repo };
        var timing = GitHubWorkflowChecks.CompletedRun(Element(Run(expected, true)), expected, Cutoff, Element(First(expected)));
        Assert.Equal(At(Started), timing.Started);
        Assert.Equal(At(Updated), timing.Completed);
        var name = path == S06ReleaseIdentity.PublicationPath ? "publish" : "validate";
        var jobs = GitHubWorkflowChecks.Jobs(Element(Jobs(expected, true)), expected, [name], timing.Started, timing.Completed);
        Assert.Single(jobs);
    }

    [Fact]
    public async Task Live_failed_rerun_has_valid_chronology_but_is_not_successful_workflow_evidence()
    {
        var token = Environment.GetEnvironmentVariable("P10_GITHUB_READ_TOKEN")
            ?? throw new InvalidOperationException("Live GitHub evidence is mandatory; this test does not skip.");
        using var client = new GitHubReadClient(token);
        var expected = Identity(S06ReleaseIdentity.ValidationPath);
        var run = await client.ReadAsync(GitHubReadTarget.Run(expected.Repository, expected.RunId, expected.RunAttempt));
        var first = await GitHubRunChronology.ReadFirstAttemptAsync(client, expected, CancellationToken.None);
        Assert.NotNull(first);
        Assert.Equal(OriginalCreated, first.Value.GetProperty("created_at").GetString());
        Assert.Equal("failure", run.GetProperty("conclusion").GetString());
        var timing = GitHubRunChronology.Read(run, expected, Cutoff, first, "github-proof-time");
        Assert.Equal(At(Started), timing.Started);
        Assert.Equal(At(Updated), timing.Updated);
        Assert.Equal("github-run-conclusion", Assert.Throws<Cp6ReleaseContractException>(() =>
            GitHubWorkflowChecks.CompletedRun(run, expected, Cutoff, first)).Code);
    }

    [Fact]
    public async Task Original_attempt_and_precancellation_do_not_make_an_additional_GitHub_request()
    {
        using var client = new GitHubReadClient("unused-token");
        client.Dispose(); // Any attempted transport use would fail, without network injection.
        var expected = Identity(S06ReleaseIdentity.ValidationPath);
        Assert.Null(await GitHubRunChronology.ReadFirstAttemptAsync(client, expected with { RunAttempt = 1 }, CancellationToken.None));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            GitHubRunChronology.ReadFirstAttemptAsync(client, expected, cancelled.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Original_attempt_keeps_strict_creation_order_and_reruns_always_need_original_evidence(bool completed)
    {
        var expected = Identity(S06ReleaseIdentity.ValidationPath) with { RunAttempt = 1 };
        Assert.Throws<Cp6ReleaseContractException>(() => Check(Run(expected, completed), Jobs(expected, completed), expected, completed));
        expected = expected with { RunAttempt = 2 };
        var run = Run(expected, completed);
        run["created_at"] = "2026-09-08T12:06:28Z";
        Assert.Throws<Cp6ReleaseContractException>(() => Check(run, Jobs(expected, completed), expected, completed));
    }

    private static void Check(JsonObject run, JsonObject jobs, GitHubWorkflowIdentity expected, bool completed)
    {
        if (completed) _ = GitHubWorkflowChecks.CompletedRun(Element(run), expected, Cutoff);
        else _ = S06CurrentWorkflowChecks.ReadTiming(Element(run), Element(jobs), expected, Cutoff);
    }

    public static IEnumerable<object[]> InvalidCases()
    {
        string[] mutations = ["missing-first", "first-id", "first-attempt", "first-source", "first-branch", "first-path",
            "first-event", "first-repository", "first-head-repository", "first-status", "first-null-conclusion", "first-empty-conclusion",
            "first-start-before-created", "first-end-before-start", "first-end-after-rerun", "first-missing-time",
            "first-offset-time", "created-before-original", "created-after-update", "start-after-update", "update-after-cutoff",
            "selected-attempt", "selected-source", "selected-status", "selected-conclusion", "job-before-start", "job-after-update"];
        foreach (var completed in new[] { false, true })
            foreach (var mutation in mutations) yield return [completed, mutation];
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Rerun_does_not_relax_identity_status_or_execution_bounds(bool completed, string mutation)
    {
        var expected = Identity(S06ReleaseIdentity.ValidationPath);
        var run = Run(expected, completed);
        var first = First(expected);
        var jobs = Jobs(expected, completed);
        if (mutation == "first-id") first["id"] = 1;
        if (mutation == "first-attempt") first["run_attempt"] = 2;
        if (mutation == "first-source") first["head_sha"] = new string('d', 40);
        if (mutation == "first-branch") first["head_branch"] = "other";
        if (mutation == "first-path") first["path"] = S06ReleaseIdentity.PublicationPath;
        if (mutation == "first-event") first["event"] = "push";
        if (mutation == "first-repository") first["repository"]!["full_name"] = "GTX537/CP6.CRM";
        if (mutation == "first-head-repository") first["head_repository"]!["full_name"] = "GTX537/CP6.CRM";
        if (mutation == "first-status") first["status"] = "in_progress";
        if (mutation == "first-null-conclusion") first["conclusion"] = null;
        if (mutation == "first-empty-conclusion") first["conclusion"] = "";
        if (mutation == "first-start-before-created") first["run_started_at"] = "2026-09-08T11:30:48Z";
        if (mutation == "first-end-before-start") first["updated_at"] = "2026-09-08T11:30:48Z";
        if (mutation == "first-end-after-rerun") first["updated_at"] = Created;
        if (mutation == "first-missing-time") first.Remove("created_at");
        if (mutation == "first-offset-time") first["created_at"] = "2026-09-08T11:30:49+00:00";
        if (mutation == "created-before-original") run["created_at"] = "2026-09-08T11:30:48Z";
        if (mutation == "created-after-update") run["created_at"] = "2026-09-08T12:08:31Z";
        if (mutation == "start-after-update") run["run_started_at"] = "2026-09-08T12:08:31Z";
        if (mutation == "update-after-cutoff") run["updated_at"] = "2026-09-08T12:09:01Z";
        if (mutation == "selected-attempt") run["run_attempt"] = 3;
        if (mutation == "selected-source") run["head_sha"] = new string('d', 40);
        if (mutation == "selected-status") run["status"] = "queued";
        if (mutation == "selected-conclusion") run["conclusion"] = "failure";
        if (mutation == "job-before-start") jobs["jobs"]![0]!["started_at"] = "2026-09-08T12:06:28Z";
        if (mutation == "job-after-update")
        {
            jobs["jobs"]![0]![completed ? "completed_at" : "started_at"] = "2026-09-08T12:09:01Z";
        }
        var error = Assert.Throws<Cp6ReleaseContractException>(() =>
        {
            JsonElement? original = mutation == "missing-first" ? null : Element(first);
            if (!completed)
                _ = S06CurrentWorkflowChecks.ReadTiming(Element(run), Element(jobs), expected, Cutoff, original);
            else
            {
                var timing = GitHubWorkflowChecks.CompletedRun(Element(run), expected, Cutoff, original);
                _ = GitHubWorkflowChecks.Jobs(Element(jobs), expected, ["validate"], timing.Started, timing.Completed);
            }
        });
        Assert.Null(error.InnerException);
    }

    private static GitHubWorkflowIdentity Identity(string path) => new("GTX537/CP6", path, new string('b', 40),
        34221083970, 2, "af0154782c8aa73eae47c083a884e2a404ef7b64");

    private static JsonObject Run(GitHubWorkflowIdentity expected, bool completed) => new()
    {
        ["id"] = expected.RunId,
        ["run_attempt"] = expected.RunAttempt,
        ["head_sha"] = expected.CommitSha,
        ["head_branch"] = "main",
        ["path"] = expected.WorkflowPath,
        ["event"] = expected.Repository == "GTX537/CP6.CRM" ? "push" : "workflow_dispatch",
        ["repository"] = new JsonObject { ["full_name"] = expected.Repository },
        ["head_repository"] = new JsonObject { ["full_name"] = expected.Repository },
        ["status"] = completed ? "completed" : "in_progress",
        ["conclusion"] = completed ? "success" : null,
        ["created_at"] = Created,
        ["run_started_at"] = Started,
        ["updated_at"] = Updated
    };

    private static JsonObject First(GitHubWorkflowIdentity expected)
    {
        var first = Run(expected, true);
        first["run_attempt"] = 1;
        first["conclusion"] = "failure";
        first["created_at"] = OriginalCreated;
        first["run_started_at"] = OriginalCreated;
        first["updated_at"] = OriginalEnded;
        return first;
    }

    private static JsonObject Jobs(GitHubWorkflowIdentity expected, bool completed) => new()
    {
        ["total_count"] = 1,
        ["jobs"] = new JsonArray(new JsonObject
        {
            ["id"] = 102054343889,
            ["run_id"] = expected.RunId,
            ["run_attempt"] = expected.RunAttempt,
            ["head_sha"] = expected.CommitSha,
            ["head_branch"] = "main",
            ["name"] = expected.WorkflowPath == S06ReleaseIdentity.PublicationPath ? "publish" : "validate",
            ["status"] = completed ? "completed" : "in_progress",
            ["conclusion"] = completed ? "success" : null,
            ["started_at"] = "2026-09-08T12:07:28Z",
            ["completed_at"] = completed ? "2026-09-08T12:08:29Z" : null
        })
    };

    private static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);
    private static DateTimeOffset At(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
