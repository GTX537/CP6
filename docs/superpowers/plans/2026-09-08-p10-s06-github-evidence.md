# P10 S06 GitHub Evidence Checks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. The user selected sequential execution; no agent delegation.

**Goal:** Produce sanitized live source and completed-workflow observations for S06 without treating an HTTP success or a JSON assertion as candidate acceptance.

**Architecture:** A small pure checker validates selected API fields; a live composition layer fetches only the fixed targets implemented in the prior module. The source check requires current protected main for public CP6 and Platform, while reporting CRM protection truthfully without changing it. Workflow checks bind an exact attempt, both repositories, source SHA, main, event, reviewed workflow blob, exact required job set and completed-before-publication cutoff.

**Tech Stack:** .NET 8 BCL, System.Text.Json, existing xUnit, pinned CP6.Platform.Release 0.10.1.

## Boundaries and decisions

This is an internal component in the existing isolated S06 worktree. It does not create a public test-policy/clock/network-success override, does not mutate GitHub, and does not expose candidate acceptance. The later candidate composition selects its own fixed required job names; they are not CLI-supplied optional gates. The live reader returns only selected IDs, names, timestamps, hashes, counts and current source-protection observations, not private logs, response URLs or runner metadata.

Current protection is not proof of historical protection. Platform and public CP6 must be protected at the live observation. The approved CRM consumer requires main reachability and its exact successful run, not a branch-protection change. Source reachability is based on the API comparison summary for pinned source...observed-main SHA: base and merge-base equal source, zero behind count, and consistent ahead/identical status and counts. The short paginated commits array is never used as proof.

The Contents endpoint's type, exact path, declared size, encoding and Git SHA-1 blob identity must match the reviewed expected workflow. A separate SHA-256 is computed over raw bytes for content-addressed evidence. Files over 48 KiB fail; no download_url is followed. The API's UTC-second timestamp representation is checked explicitly. All jobs must be completed/success with exact run/attempt/SHA/main and unique IDs/names. Missing, extra, skipped or paginated-away jobs fail. Run updated_at must not exceed the already fixed candidate/gate cutoff; job completions must not exceed it either. The publication workflow is never asked to prove its own future completion.

Official endpoint contracts were read in the previous module: [attempt runs](https://docs.github.com/en/rest/actions/workflow-runs?apiVersion=2022-11-28#get-a-workflow-run-attempt), [attempt jobs](https://docs.github.com/en/rest/actions/workflow-jobs?apiVersion=2022-11-28#list-jobs-for-a-workflow-run-attempt), [contents](https://docs.github.com/en/rest/repos/contents?apiVersion=2022-11-28#get-repository-content), [comparison](https://docs.github.com/en/rest/commits/commits?apiVersion=2022-11-28#compare-two-commits). New tests include real CRM run 34134695003 attempt 1 and real source reachability in all three repositories, without mocking network or skipping a missing token.

## Task 1: Write tests and observe RED

**Create:** tools/p10/ReleaseVerifier.Tests/GitHubEvidenceTests.cs.

- [x] Add the exact tests below. API declarations and throwing scaffolds may be added to make the tests compile.
- [x] Run the focused suite with P10_GITHUB_READ_TOKEN populated only in process memory from an existing authorized identity and removed in finally; inspect every failure before implementation. Expected: NotImplementedException, no unexpected failures or skipped tests.

From tools/p10 using the selected .NET 8 SDK:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~GitHubEvidenceTests --logger "trx;LogFileName=github-evidence-red.trx" --results-directory ../../artifacts/p10/github-evidence-red
```

```csharp
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
```

## Task 2: Apply the minimal code and verify GREEN

- [x] Apply the complete three files below only after inspecting RED.
- [x] Re-run the focused suite with github-evidence-green.trx and its distinct results directory; all tests must pass, including four real live evidence checks.

### tools/p10/ReleaseVerifier/GitHubEvidenceChecks.cs

```csharp
using System.Globalization;
using System.Text.Json;

namespace CP6.P10.ReleaseVerifier;

// Pure selected-field checks. The live source binds these inputs to fixed HTTPS requests.
internal static class GitHubEvidenceChecks
{
    internal static void Require(bool condition, string code)
    {
        if (!condition) throw GitHubWirePolicy.Error(code);
    }

    internal static JsonElement Property(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var property))
            throw GitHubWirePolicy.Error("github-proof-shape");
        return property;
    }

    internal static string Text(JsonElement value, string name)
    {
        var property = Property(value, name);
        if (property.ValueKind != JsonValueKind.String) throw GitHubWirePolicy.Error("github-proof-shape");
        return property.GetString()!;
    }

    internal static long Number(JsonElement value, string name)
    {
        var property = Property(value, name);
        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt64(out var result) || result < 0)
            throw GitHubWirePolicy.Error("github-proof-shape");
        return result;
    }

    internal static DateTimeOffset Time(JsonElement value, string name)
    {
        if (!DateTimeOffset.TryParseExact(Text(value, name), "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result) || result < DateTimeOffset.UnixEpoch)
            throw GitHubWirePolicy.Error("github-proof-time");
        return result;
    }

    internal static GitHubMainObservation MainBranch(JsonElement main)
    {
        Require(Text(main, "name") == "main", "github-source-branch");
        var sha = Text(Property(main, "commit"), "sha");
        GitHubReadTarget.RequireSha(sha);
        var protection = Property(main, "protected");
        Require(protection.ValueKind is JsonValueKind.True or JsonValueKind.False, "github-source-protection");
        return new(sha, protection.GetBoolean());
    }

    internal static void Reachability(string repository, string source, GitHubMainObservation main, JsonElement comparison)
    {
        _ = GitHubReadTarget.Compare(repository, source, main.Sha);
        if (repository is "GTX537/CP6" or "GTX537/CP6.Platform")
            Require(main.Protected, "github-source-protection");
        var status = Text(comparison, "status");
        var ahead = Number(comparison, "ahead_by");
        Require(Text(Property(comparison, "base_commit"), "sha") == source &&
            Text(Property(comparison, "merge_base_commit"), "sha") == source &&
            Number(comparison, "behind_by") == 0 && Number(comparison, "total_commits") == ahead &&
            (source == main.Sha ? status == "identical" && ahead == 0 : status == "ahead" && ahead > 0),
            "github-source-reachability");
    }

    internal static void RequireCutoff(DateTimeOffset cutoff)
    {
        Require(cutoff.Offset == TimeSpan.Zero && cutoff >= DateTimeOffset.UnixEpoch, "github-proof-time");
    }
}

internal sealed record GitHubMainObservation(string Sha, bool Protected);
internal sealed record GitHubSourceObservation(string Repository, string SourceGitSha, string ObservedMainSha,
    bool MainProtectedAtObservation, DateTimeOffset ObservedAtUtc);
internal sealed record GitHubWorkflowIdentity(string Repository, string WorkflowPath, string WorkflowFileSha,
    long RunId, long RunAttempt, string CommitSha)
{
    internal void RequireValid()
    {
        _ = GitHubReadTarget.Run(Repository, RunId, RunAttempt);
        _ = GitHubReadTarget.Workflow(Repository, WorkflowPath, CommitSha);
        GitHubReadTarget.RequireSha(WorkflowFileSha);
    }
}

internal sealed record GitHubWorkflowFileObservation(string Sha256, int ByteLength);
internal sealed record GitHubJobObservation(long Id, string Name, DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc);
internal sealed record GitHubWorkflowObservation(GitHubWorkflowIdentity Workflow, string Event,
    DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc, GitHubWorkflowFileObservation File,
    IReadOnlyList<GitHubJobObservation> Jobs, DateTimeOffset ObservedAtUtc);
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
    {
        expected.RequireValid();
        RequireCutoff(cutoff);
        var expectedEvent = expected.Repository == "GTX537/CP6.CRM" ? "push" : "workflow_dispatch";
        Require(Number(run, "id") == expected.RunId && Number(run, "run_attempt") == expected.RunAttempt &&
            Text(run, "head_sha") == expected.CommitSha && Text(run, "head_branch") == "main" &&
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
        IReadOnlyCollection<string> requiredNames, DateTimeOffset runStarted, DateTimeOffset runCompleted)
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
                Text(job, "head_sha") == expected.CommitSha && Text(job, "head_branch") == "main", "github-job-identity");
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

### tools/p10/ReleaseVerifier/GitHubEvidenceSource.cs

```csharp
using static CP6.P10.ReleaseVerifier.GitHubEvidenceChecks;

namespace CP6.P10.ReleaseVerifier;

// Live observations, not a serialized candidate acceptance capability. No network success/clock/policy injection.
internal static class GitHubEvidenceSource
{
    internal static async Task<GitHubSourceObservation> ReadSourceAsync(string repository, string source, string token,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = GitHubReadTarget.MainBranch(repository);
        GitHubReadTarget.RequireSha(source);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        using var client = new GitHubReadClient(token);
        var main = MainBranch(await client.ReadAsync(target, deadline.Token));
        var comparison = await client.ReadAsync(GitHubReadTarget.Compare(repository, source, main.Sha), deadline.Token);
        Reachability(repository, source, main, comparison);
        return new(repository, source, main.Sha, main.Protected, DateTimeOffset.UtcNow);
    }

    internal static async Task<GitHubWorkflowObservation> ReadWorkflowAsync(GitHubWorkflowIdentity expected,
        IReadOnlyCollection<string> requiredJobs, DateTimeOffset completedBeforeUtc, string token,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        expected.RequireValid();
        RequireCutoff(completedBeforeUtc);
        Require(completedBeforeUtc <= DateTimeOffset.UtcNow, "github-proof-time");
        // Copy the caller's job profile before asynchronous work.
        var jobNames = requiredJobs.ToArray();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        using var client = new GitHubReadClient(token);
        var run = await client.ReadAsync(GitHubReadTarget.Run(expected.Repository, expected.RunId, expected.RunAttempt), deadline.Token);
        var timing = GitHubWorkflowChecks.CompletedRun(run, expected, completedBeforeUtc);
        var file = GitHubWorkflowChecks.File(await client.ReadAsync(
            GitHubReadTarget.Workflow(expected.Repository, expected.WorkflowPath, expected.CommitSha), deadline.Token), expected);
        var jobs = GitHubWorkflowChecks.Jobs(await client.ReadAsync(
            GitHubReadTarget.Jobs(expected.Repository, expected.RunId, expected.RunAttempt), deadline.Token),
            expected, jobNames, timing.Started, timing.Completed);
        return new(expected, timing.Event, timing.Started, timing.Completed, file, jobs, DateTimeOffset.UtcNow);
    }
}
```

## Task 3: Full regression and scoped commit

- [x] Run locked restore, the full Release suite and format verification, supplying the existing cosign, real formal-package archive and feed inputs plus the GitHub read token. No secrets are written to disk or arguments.
- [x] Review all new files, confirm exact plan/source parity and unchanged existing workflows/trust/locks/runtime, and perform whitespace/secret hygiene checks.
- [ ] Stage exactly this plan, its test and three production files, then commit `feat(p10): verify live workflow and source evidence`.
- [ ] Continue S06 proof payloads, OCI and protected publication integration. These observations do not complete P10 or create a Frozen candidate.

```powershell
dotnet restore ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --configfile ../../eng/p10/NuGet.formal.config --packages ../../artifacts/p10/foundation-restore/packages --locked-mode --verbosity minimal
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore
dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --verify-no-changes --no-restore
git diff --check
```

## Verification outcome (2026-09-08)

All 78 focused tests first failed with NotImplementedException; TRX inspection found zero unexpected failures or skips. The planned code passed all 78, including the exact live S05 five-job run and three live source checks. During review, pre-network rejection assertions were strengthened to require their exact diagnostic codes rather than accepting any contract exception. The complete Release suite then passed 557/557 with zero skipped tests, and format verification passed. Locked restore had no dependency or lock changes.

All four code/test files match the complete plan, and the full staged diff was reviewed with whitespace and secret hygiene checks. No existing workflows, runtime, trust values or gates changed. Source protection is a live observation only; no historical-protection claim was introduced. S06 publication and candidate acceptance remain outstanding.
