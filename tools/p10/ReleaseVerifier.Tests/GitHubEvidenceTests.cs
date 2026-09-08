using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

public sealed class GitHubEvidenceTests
{
    private const string Repo = "GTX537/CP6.CRM";
    private const string Path = ".github/workflows/crm-validation.yml";
    private const string Sha = S06ReleaseIdentity.CrmSource;
    private const long RunId = 34134695003;
    private static readonly DateTimeOffset Cutoff = DateTimeOffset.Parse("2026-09-07T15:00:00Z", CultureInfo.InvariantCulture);
    private static readonly string[] JobNames =
    [
        "crm-validation", "platform-p06-sql-consumer", "platform-p10-formal-ubuntu-latest",
        "platform-p10-formal-windows-latest", "platform-p10-test-candidate"
    ];
    private static readonly byte[] FileBytes = "name: isolated-parser-fixture\n"u8.ToArray();

    [Theory]
    [InlineData("GTX537/CP6.Platform", S06ReleaseIdentity.Source)]
    [InlineData("GTX537/CP6", "6f9d09f4e3b1627a25ec7859b748eba8cd66f621")]
    [InlineData(Repo, Sha)]
    public async Task Live_source_reachability_uses_current_protection_without_claiming_history(string repo, string source)
    {
        var before = DateTimeOffset.UtcNow;
        var proof = await GitHubEvidenceSource.ReadSourceAsync(repo, source, LiveToken());
        Assert.Equal(repo, proof.Repository);
        Assert.Equal(source, proof.SourceGitSha);
        Assert.Matches("\\A[0-9a-f]{40}\\z", proof.ObservedMainSha);
        Assert.InRange(proof.ObservedAtUtc, before, DateTimeOffset.UtcNow);
        if (repo != Repo) Assert.True(proof.MainProtectedAtObservation);
    }

    [Fact]
    public async Task Live_S05_workflow_proves_the_exact_five_successful_jobs_and_file()
    {
        var expected = new GitHubWorkflowIdentity(Repo, Path, "924014cb1231824a9b57ab82a6f9638f76329919", RunId, 1, Sha);
        var before = DateTimeOffset.UtcNow;
        var proof = await GitHubEvidenceSource.ReadWorkflowAsync(expected, JobNames, Cutoff, LiveToken());
        Assert.Equal(expected, proof.Workflow);
        Assert.Equal("push", proof.Event);
        Assert.Equal(DateTimeOffset.Parse("2026-09-07T14:45:24Z", CultureInfo.InvariantCulture), proof.StartedAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-09-07T14:53:04Z", CultureInfo.InvariantCulture), proof.CompletedAtUtc);
        Assert.Equal(14913, proof.File.ByteLength);
        Assert.Equal(JobNames.Order(StringComparer.Ordinal), proof.Jobs.Select(j => j.Name));
        Assert.All(proof.Jobs, j =>
        {
            Assert.InRange(j.StartedAtUtc, proof.StartedAtUtc, proof.CompletedAtUtc);
            Assert.InRange(j.CompletedAtUtc, j.StartedAtUtc, proof.CompletedAtUtc);
        });
        Assert.InRange(proof.ObservedAtUtc, before, DateTimeOffset.UtcNow);
        Assert.Throws<NotSupportedException>(() => ((IList<GitHubJobObservation>)proof.Jobs).Clear());
        Assert.DoesNotContain("runner_name", JsonSerializer.Serialize(proof), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("sha")]
    [InlineData("protected-string")]
    [InlineData("protected-missing")]
    [InlineData("commit-missing")]
    public void Malformed_main_observation_fails_closed(string mutation)
    {
        var main = MainNode();
        if (mutation == "name") main["name"] = "release";
        if (mutation == "sha") main["commit"]!["sha"] = "main";
        if (mutation == "protected-string") main["protected"] = "true";
        if (mutation == "protected-missing") main.Remove("protected");
        if (mutation == "commit-missing") main.Remove("commit");
        Reject(() => GitHubEvidenceChecks.MainBranch(Element(main)));
    }

    [Theory]
    [InlineData("GTX537/CP6")]
    [InlineData("GTX537/CP6.Platform")]
    public void Required_protection_cannot_be_replaced_by_ancestry(string repo)
    {
        var main = GitHubEvidenceChecks.MainBranch(Element(MainNode(false)));
        Reject(() => GitHubEvidenceChecks.Reachability(repo, Sha, main, Element(Comparison())), "github-source-protection");
    }

    [Fact]
    public void CRM_unprotected_main_is_reported_truthfully_and_is_not_a_protection_bypass_for_other_repositories()
    {
        var main = GitHubEvidenceChecks.MainBranch(Element(MainNode(false)));
        GitHubEvidenceChecks.Reachability(Repo, Sha, main, Element(Comparison()));
        Assert.False(main.Protected);
    }

    [Theory]
    [InlineData("behind")]
    [InlineData("diverged")]
    [InlineData("unknown-status")]
    [InlineData("wrong-base")]
    [InlineData("wrong-merge-base")]
    [InlineData("zero-ahead")]
    [InlineData("total-mismatch")]
    [InlineData("negative-count")]
    [InlineData("fraction-count")]
    [InlineData("same-sha-ahead")]
    [InlineData("different-sha-identical")]
    public void Inconsistent_or_unreachable_source_cannot_pass(string mutation)
    {
        var mainNode = MainNode();
        var compare = Comparison();
        if (mutation == "behind") compare["behind_by"] = 1;
        if (mutation == "diverged") compare["status"] = "diverged";
        if (mutation == "unknown-status") compare["status"] = "success";
        if (mutation == "wrong-base") compare["base_commit"]!["sha"] = new string('c', 40);
        if (mutation == "wrong-merge-base") compare["merge_base_commit"]!["sha"] = new string('c', 40);
        if (mutation == "zero-ahead") { compare["ahead_by"] = 0; compare["total_commits"] = 0; }
        if (mutation == "total-mismatch") compare["total_commits"] = 7;
        if (mutation == "negative-count") compare["ahead_by"] = -1;
        if (mutation == "fraction-count") compare["ahead_by"] = 8.5;
        if (mutation == "same-sha-ahead") mainNode["commit"]!["sha"] = Sha;
        if (mutation == "different-sha-identical") { compare["status"] = "identical"; compare["ahead_by"] = 0; compare["total_commits"] = 0; }
        var main = GitHubEvidenceChecks.MainBranch(Element(mainNode));
        Reject(() => GitHubEvidenceChecks.Reachability(Repo, Sha, main, Element(compare)));
    }

    [Fact]
    public void Identical_source_requires_zero_distance()
    {
        var mainNode = MainNode();
        mainNode["commit"]!["sha"] = Sha;
        var compare = Comparison();
        compare["status"] = "identical";
        compare["ahead_by"] = 0;
        compare["total_commits"] = 0;
        GitHubEvidenceChecks.Reachability(Repo, Sha, GitHubEvidenceChecks.MainBranch(Element(mainNode)), Element(compare));
    }

    [Fact]
    public void Selected_workflow_fields_and_raw_blob_are_checked_without_following_download_urls()
    {
        var expected = Identity();
        var file = FileNode();
        file["download_url"] = "https://attacker.invalid/do-not-follow";
        var proof = GitHubWorkflowChecks.File(Element(file), expected);
        Assert.Equal(Cp6DeterministicJson.Sha256Hex(FileBytes), proof.Sha256);
        Assert.Equal(FileBytes.Length, proof.ByteLength);
        var timing = GitHubWorkflowChecks.CompletedRun(Element(RunNode()), expected, Cutoff);
        var jobs = GitHubWorkflowChecks.Jobs(Element(JobsNode()), expected, JobNames, timing.Started, timing.Completed);
        Assert.Equal(JobNames.Order(StringComparer.Ordinal), jobs.Select(j => j.Name));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("path")]
    [InlineData("sha")]
    [InlineData("encoding")]
    [InlineData("size")]
    [InlineData("zero-size")]
    [InlineData("oversize")]
    [InlineData("invalid-base64")]
    [InlineData("tampered-bytes")]
    [InlineData("missing-content")]
    public void Workflow_blob_cannot_be_substituted_or_misdeclared(string mutation)
    {
        var file = FileNode();
        if (mutation == "type") file["type"] = "symlink";
        if (mutation == "path") file["path"] = ".github/workflows/another.yml";
        if (mutation == "sha") file["sha"] = new string('a', 40);
        if (mutation == "encoding") file["encoding"] = "none";
        if (mutation == "size") file["size"] = FileBytes.Length + 1;
        if (mutation == "zero-size") file["size"] = 0;
        if (mutation == "oversize") file["size"] = 49153;
        if (mutation == "invalid-base64") file["content"] = "%invalid";
        if (mutation == "tampered-bytes") { var changed = FileBytes.ToArray(); changed[0] ^= 1; file["content"] = Convert.ToBase64String(changed); }
        if (mutation == "missing-content") file.Remove("content");
        Reject(() => GitHubWorkflowChecks.File(Element(file), Identity()));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("attempt")]
    [InlineData("sha")]
    [InlineData("branch")]
    [InlineData("path")]
    [InlineData("event")]
    [InlineData("repository")]
    [InlineData("head-repository")]
    [InlineData("status")]
    [InlineData("conclusion")]
    [InlineData("null-conclusion")]
    [InlineData("start-before-created")]
    [InlineData("completed-before-start")]
    [InlineData("completed-after-cutoff")]
    [InlineData("offset-date")]
    [InlineData("missing-date")]
    [InlineData("wrong-type")]
    public void Workflow_identity_conclusion_and_temporal_order_fail_closed(string mutation)
    {
        var run = RunNode();
        if (mutation == "id") run["id"] = RunId + 1;
        if (mutation == "attempt") run["run_attempt"] = 2;
        if (mutation == "sha") run["head_sha"] = new string('a', 40);
        if (mutation == "branch") run["head_branch"] = "pull-request";
        if (mutation == "path") run["path"] = ".github/workflows/other.yml";
        if (mutation == "event") run["event"] = "pull_request";
        if (mutation == "repository") run["repository"]!["full_name"] = "fork/CP6.CRM";
        if (mutation == "head-repository") run["head_repository"]!["full_name"] = "fork/CP6.CRM";
        if (mutation == "status") run["status"] = "in_progress";
        if (mutation == "conclusion") run["conclusion"] = "failure";
        if (mutation == "null-conclusion") run["conclusion"] = null;
        if (mutation == "start-before-created") run["run_started_at"] = "2026-09-07T14:00:00Z";
        if (mutation == "completed-before-start") run["updated_at"] = "2026-09-07T14:00:00Z";
        if (mutation == "completed-after-cutoff") run["updated_at"] = "2026-09-07T15:00:01Z";
        if (mutation == "offset-date") run["created_at"] = "2026-09-07T14:45:24+00:00";
        if (mutation == "missing-date") run.Remove("created_at");
        if (mutation == "wrong-type") run["id"] = "34134695003";
        Reject(() => GitHubWorkflowChecks.CompletedRun(Element(run), Identity(), Cutoff));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("total")]
    [InlineData("duplicate-name")]
    [InlineData("duplicate-id")]
    [InlineData("zero-id")]
    [InlineData("name")]
    [InlineData("run")]
    [InlineData("attempt")]
    [InlineData("sha")]
    [InlineData("branch")]
    [InlineData("in-progress")]
    [InlineData("failure")]
    [InlineData("skipped")]
    [InlineData("started-before-run")]
    [InlineData("completed-before-start")]
    [InlineData("completed-after-run")]
    public void Exact_job_set_must_be_complete_successful_and_bound_to_the_attempt(string mutation)
    {
        var jobs = JobsNode();
        var array = jobs["jobs"]!.AsArray();
        var job = array[0]!;
        if (mutation == "missing") array.RemoveAt(4);
        if (mutation == "extra") array.Add(job.DeepClone());
        if (mutation == "total") jobs["total_count"] = 101;
        if (mutation == "duplicate-name") array[1]!["name"] = JobNames[0];
        if (mutation == "duplicate-id") array[1]!["id"] = 1;
        if (mutation == "zero-id") job["id"] = 0;
        if (mutation == "name") job["name"] = "not-required";
        if (mutation == "run") job["run_id"] = RunId + 1;
        if (mutation == "attempt") job["run_attempt"] = 2;
        if (mutation == "sha") job["head_sha"] = new string('a', 40);
        if (mutation == "branch") job["head_branch"] = "other";
        if (mutation == "in-progress") job["status"] = "in_progress";
        if (mutation == "failure") job["conclusion"] = "failure";
        if (mutation == "skipped") job["conclusion"] = "skipped";
        if (mutation == "started-before-run") job["started_at"] = "2026-09-07T14:00:00Z";
        if (mutation == "completed-before-start") job["completed_at"] = "2026-09-07T14:00:00Z";
        if (mutation == "completed-after-run") job["completed_at"] = "2026-09-07T14:54:00Z";
        var timing = GitHubWorkflowChecks.CompletedRun(Element(RunNode()), Identity(), Cutoff);
        Reject(() => GitHubWorkflowChecks.Jobs(Element(jobs), Identity(), JobNames, timing.Started, timing.Completed));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("blank")]
    [InlineData("control")]
    [InlineData("long")]
    [InlineData("count")]
    public void An_empty_duplicate_or_oversized_job_profile_is_not_a_gate(string mutation)
    {
        var names = mutation switch
        {
            "empty" => Array.Empty<string>(),
            "duplicate" => new[] { "x", "x" },
            "blank" => new[] { "" },
            "control" => new[] { "x\ny" },
            "long" => new[] { new string('x', 129) },
            _ => Enumerable.Range(0, 101).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray()
        };
        Reject(() => GitHubWorkflowChecks.Jobs(Element(JobsNode()), Identity(), names, Cutoff, Cutoff), "github-job-profile");
    }

    [Theory]
    [InlineData("GTX537/CP6", S06ReleaseIdentity.ValidationPath)]
    [InlineData("GTX537/CP6.Platform", ".github/workflows/p10-formal-packages.yml")]
    public void CP6_and_Platform_validation_require_main_manual_run_identity(string repo, string path)
    {
        var expected = Identity() with { Repository = repo, WorkflowPath = path };
        var run = RunNode();
        run["repository"]!["full_name"] = repo;
        run["head_repository"]!["full_name"] = repo;
        run["path"] = path;
        run["event"] = "workflow_dispatch";
        Assert.Equal("workflow_dispatch", GitHubWorkflowChecks.CompletedRun(Element(run), expected, Cutoff).Event);
        run["event"] = "push";
        Reject(() => GitHubWorkflowChecks.CompletedRun(Element(run), expected, Cutoff), "github-run-identity");
    }

    [Fact]
    public async Task Future_cutoff_invalid_identity_and_precancellation_do_not_call_GitHub()
    {
        Assert.Equal("github-proof-time", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            GitHubEvidenceSource.ReadWorkflowAsync(Identity(), JobNames, DateTimeOffset.UtcNow.AddDays(1), ""))).Code);
        Assert.Equal("github-run", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            GitHubEvidenceSource.ReadWorkflowAsync(Identity() with { RunAttempt = 0 }, JobNames, Cutoff, ""))).Code);
        Assert.Equal("github-repository", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            GitHubEvidenceSource.ReadSourceAsync("attacker/repo", Sha, ""))).Code);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            GitHubEvidenceSource.ReadSourceAsync("", "", "", cancel.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            GitHubEvidenceSource.ReadWorkflowAsync(Identity(), JobNames, Cutoff, "", cancel.Token));
    }

    // These deliberately local parser vectors are not raw GitHub evidence or candidate acceptance fixtures.
    private static GitHubWorkflowIdentity Identity()
    {
        var blob = Encoding.ASCII.GetBytes("blob " + FileBytes.Length + "\0").Concat(FileBytes).ToArray();
        return new(Repo, Path, Convert.ToHexString(SHA1.HashData(blob)).ToLowerInvariant(), RunId, 1, Sha);
    }

    private static JsonObject MainNode(bool protection = true) =>
        new() { ["name"] = "main", ["commit"] = new JsonObject { ["sha"] = new string('b', 40) }, ["protected"] = protection };

    private static JsonObject Comparison() => new()
    {
        ["status"] = "ahead",
        ["ahead_by"] = 8,
        ["behind_by"] = 0,
        ["total_commits"] = 8,
        ["base_commit"] = new JsonObject { ["sha"] = Sha },
        ["merge_base_commit"] = new JsonObject { ["sha"] = Sha }
    };

    private static JsonObject FileNode() => new()
    {
        ["type"] = "file",
        ["path"] = Path,
        ["sha"] = Identity().WorkflowFileSha,
        ["size"] = FileBytes.Length,
        ["encoding"] = "base64",
        ["content"] = Convert.ToBase64String(FileBytes)
    };

    private static JsonObject RunNode() => new()
    {
        ["id"] = RunId,
        ["run_attempt"] = 1,
        ["head_sha"] = Sha,
        ["head_branch"] = "main",
        ["path"] = Path,
        ["event"] = "push",
        ["repository"] = new JsonObject { ["full_name"] = Repo },
        ["head_repository"] = new JsonObject { ["full_name"] = Repo },
        ["status"] = "completed",
        ["conclusion"] = "success",
        ["created_at"] = "2026-09-07T14:45:24Z",
        ["run_started_at"] = "2026-09-07T14:45:24Z",
        ["updated_at"] = "2026-09-07T14:53:04Z"
    };

    private static JsonObject JobsNode() => new()
    {
        ["total_count"] = JobNames.Length,
        ["jobs"] = new JsonArray(JobNames.Select((name, index) => (JsonNode)new JsonObject
        {
            ["id"] = index + 1,
            ["name"] = name,
            ["run_id"] = RunId,
            ["run_attempt"] = 1,
            ["head_sha"] = Sha,
            ["head_branch"] = "main",
            ["status"] = "completed",
            ["conclusion"] = "success",
            ["started_at"] = "2026-09-07T14:45:28Z",
            ["completed_at"] = "2026-09-07T14:46:00Z"
        }).ToArray())
    };

    private static JsonElement Element(JsonNode value) => GitHubApiJson.Parse(Encoding.UTF8.GetBytes(value.ToJsonString()));
    private static string LiveToken() => Environment.GetEnvironmentVariable("P10_GITHUB_READ_TOKEN")
        ?? throw new InvalidOperationException("Live GitHub evidence is mandatory; this test does not skip.");
    private static void Reject(Action action, string? expected = null)
    {
        var error = Assert.Throws<Cp6ReleaseContractException>(action);
        if (expected is not null) Assert.Equal(expected, error.Code);
        Assert.Null(error.InnerException);
    }
}
