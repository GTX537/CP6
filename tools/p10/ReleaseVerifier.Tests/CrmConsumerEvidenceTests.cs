using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

public sealed class CrmConsumerEvidenceTests
{
    private static readonly DateTimeOffset Cutoff = DateTimeOffset.Parse("2026-09-07T16:00:00Z", CultureInfo.InvariantCulture);
    private static readonly string[] Names = ["crm-index", "crm-pr-linux", "crm-pr-windows", "crm-main-linux", "crm-main-windows"];
    private static readonly Lazy<Task<Baseline>> Actual = new(ReadBaselineAsync);

    [Fact]
    public async Task Actual_retained_records_forward_runs_and_commit_trees_are_verified_together()
    {
        var before = DateTimeOffset.UtcNow;
        var result = await CrmConsumerEvidenceSource.ReadAsync(Cutoff, Token());
        Assert.Equal(46, result.Consumer.Selection.Number);
        Assert.Equal(47, result.Archive.Selection.Number);
        Assert.Equal(PinnedReleaseDocument.CommitSha, result.ArchiveSource.SourceGitSha);
        Assert.Equal(CrmPullRequestSelection.Repository, result.ArchiveSource.Repository);
        Assert.Equal(Names.Order(StringComparer.Ordinal), result.Documents.Keys.Order(StringComparer.Ordinal));
        foreach (var name in Names)
        {
            var pinned = PinnedReleaseDocument.Get(name);
            Assert.Same(pinned, result.Documents[name].Document);
            Assert.Equal(pinned.Sha256, Cp6DeterministicJson.Sha256Hex(result.Documents[name].CopyBytes()));
        }
        Assert.InRange(result.ObservedAtUtc, before, DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData("crm-pr-linux")]
    [InlineData("crm-pr-windows")]
    [InlineData("crm-main-linux")]
    [InlineData("crm-main-windows")]
    public async Task Actual_record_timestamps_are_inside_the_corresponding_OS_job(string name)
    {
        var baseline = await Actual.Value;
        var created = CrmConsumerRecordChecks.Record(name, Element(baseline.Node(name)), baseline.Consumer);
        var run = name.StartsWith("crm-pr-", StringComparison.Ordinal) ? baseline.Consumer.PullRequest : baseline.Consumer.Main;
        var jobName = "platform-p10-formal-" + (name.EndsWith("linux", StringComparison.Ordinal) ? "ubuntu-latest" : "windows-latest");
        var job = Assert.Single(run.Jobs, j => j.Name == jobName);
        Assert.InRange(created, job.StartedAtUtc, job.CompletedAtUtc);
    }

    [Fact]
    public async Task Retained_index_uses_last_job_completion_not_run_updated_time()
    {
        var baseline = await Actual.Value;
        var index = baseline.Node("crm-index");
        CrmConsumerIndexChecks.Index(Element(index), baseline.Consumer, baseline.Archive);
        var pr = index["runs"]![0]!;
        Assert.NotEqual(baseline.Consumer.PullRequest.CompletedAtUtc,
            DateTimeOffset.Parse(pr["completedAtUtc"]!.GetValue<string>(), CultureInfo.InvariantCulture));
        pr["completedAtUtc"] = baseline.Consumer.PullRequest.CompletedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        Reject(() => CrmConsumerIndexChecks.Index(Element(index), baseline.Consumer, baseline.Archive), "github-proof-time");
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("repository")]
    [InlineData("commit")]
    [InlineData("workflow")]
    [InlineData("file")]
    [InlineData("run")]
    [InlineData("attempt")]
    [InlineData("invocation")]
    [InlineData("os")]
    [InlineData("sdk")]
    [InlineData("lock")]
    [InlineData("source")]
    [InlineData("publication")]
    [InlineData("version")]
    [InlineData("package-missing")]
    [InlineData("package-duplicate")]
    [InlineData("package-hash")]
    [InlineData("package-version")]
    [InlineData("trust")]
    [InlineData("signer")]
    [InlineData("spki")]
    [InlineData("public-ca")]
    [InlineData("internal")]
    [InlineData("deployable")]
    [InlineData("boolean-string")]
    [InlineData("timestamp")]
    [InlineData("registry")]
    [InlineData("locked")]
    [InlineData("project-reference")]
    [InlineData("count")]
    [InlineData("passed")]
    [InlineData("failed")]
    [InlineData("skipped")]
    [InlineData("result")]
    [InlineData("time-before-job")]
    [InlineData("time-after-job")]
    [InlineData("time-offset")]
    public async Task Changed_record_claims_fail_semantic_checks_even_before_envelope_verification(string mutation)
    {
        var baseline = await Actual.Value;
        var record = baseline.Node("crm-main-linux");
        var producer = record["producer"]!;
        if (mutation == "schema") record["schemaId"] = "other";
        if (mutation == "repository") producer["repository"] = "fork/CP6.CRM";
        if (mutation == "commit") producer["commitSha"] = CrmConsumerRecordChecks.CheckoutSha;
        if (mutation == "workflow") producer["workflowPath"] = ".github/workflows/another.yml";
        if (mutation == "file") producer["workflowFileSha"] = new string('a', 40);
        if (mutation == "run") producer["runId"] = 34133944140;
        if (mutation == "attempt") producer["runAttempt"] = 2;
        if (mutation == "invocation") record["invocationKind"] = "local";
        if (mutation == "os") record["runnerOs"] = "Microsoft Windows 10.0.26100";
        if (mutation == "sdk") record["dotnetSdk"] = "8.0.100";
        if (mutation == "lock") record["lockSha256"] = new string('a', 64);
        if (mutation == "source") record["platformSourceSha"] = new string('a', 40);
        if (mutation == "publication") record["publicationRecordSha256"] = new string('a', 64);
        if (mutation == "version") record["packageVersion"] = "0.10.0";
        if (mutation == "package-missing") record["packageSubjects"]!.AsArray().RemoveAt(6);
        if (mutation == "package-duplicate") record["packageSubjects"]![1] = record["packageSubjects"]![0]!.DeepClone();
        if (mutation == "package-hash") record["packageSubjects"]![0]!["sha256"] = new string('a', 64);
        if (mutation == "package-version") record["packageSubjects"]![0]!["version"] = "0.10.0";
        if (mutation == "trust") record["trustPolicySha256"] = new string('a', 64);
        if (mutation == "signer") record["signerFingerprint"] = new string('a', 64);
        if (mutation == "spki") record["spkiKeyId"] = "sha256:" + new string('a', 64);
        if (mutation == "public-ca") record["publicCaTrusted"] = true;
        if (mutation == "internal") record["internallyTrusted"] = false;
        if (mutation == "deployable") record["deployable"] = true;
        if (mutation == "boolean-string") record["internallyTrusted"] = "true";
        if (mutation == "timestamp") record["timestampPolicy"] = "Optional";
        if (mutation == "registry") record["registryRestore"] = "Failure";
        if (mutation == "locked") record["lockedRestore"] = "Failure";
        if (mutation == "project-reference") record["projectReferences"] = 1;
        if (mutation == "count") record["testCount"] = 0;
        if (mutation == "passed") record["passed"] = 15;
        if (mutation == "failed") record["failed"] = 1;
        if (mutation == "skipped") record["skipped"] = 1;
        if (mutation == "result") record["testConclusion"] = "skipped";
        if (mutation == "time-before-job") record["createdAtUtc"] = "2026-09-07T14:00:00.000Z";
        if (mutation == "time-after-job") record["createdAtUtc"] = "2026-09-07T15:00:00.000Z";
        if (mutation == "time-offset") record["createdAtUtc"] = "2026-09-07T14:46:10.343+00:00";
        Reject(() => CrmConsumerRecordChecks.Record("crm-main-linux", Element(record), baseline.Consumer));
    }

    [Fact]
    public async Task PR_record_checkout_is_not_the_run_API_head_SHA()
    {
        var baseline = await Actual.Value;
        var record = baseline.Node("crm-pr-linux");
        record["producer"]!["commitSha"] = baseline.Consumer.PullRequest.Workflow.CommitSha;
        Reject(() => CrmConsumerRecordChecks.Record("crm-pr-linux", Element(record), baseline.Consumer), "github-crm-record-producer");
    }

    [Theory]
    [InlineData("crm-index")]
    [InlineData("publication")]
    [InlineData("")]
    public async Task Nonrecord_name_and_wrong_delivery_cannot_select_record_semantics(string name)
    {
        var baseline = await Actual.Value;
        Reject(() => CrmConsumerRecordChecks.Record(name, Element(baseline.Node("crm-main-linux")), baseline.Consumer), "github-crm-record-name");
        Reject(() => CrmConsumerRecordChecks.Record("crm-main-linux", Element(baseline.Node("crm-main-linux")), baseline.Archive),
            "github-crm-selection");
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("state")]
    [InlineData("deployable")]
    [InlineData("implementation")]
    [InlineData("source-tree")]
    [InlineData("workflow")]
    [InlineData("count")]
    [InlineData("forward-authority")]
    [InlineData("forward-stage")]
    [InlineData("created-too-early")]
    [InlineData("created-too-late")]
    [InlineData("missing-run")]
    [InlineData("duplicate-phase")]
    [InlineData("run-id")]
    [InlineData("run-attempt")]
    [InlineData("run-source")]
    [InlineData("run-checkout")]
    [InlineData("run-event")]
    [InlineData("run-conclusion")]
    [InlineData("job-missing")]
    [InlineData("job-id")]
    [InlineData("job-name")]
    [InlineData("job-conclusion")]
    [InlineData("artifact-missing")]
    [InlineData("artifact-runner")]
    [InlineData("artifact-file")]
    [InlineData("artifact-record-hash")]
    [InlineData("artifact-size")]
    [InlineData("artifact-name")]
    [InlineData("artifact-id")]
    [InlineData("artifact-digest")]
    [InlineData("artifact-trx")]
    [InlineData("artifact-expiry")]
    public async Task Changed_index_bindings_cannot_replace_live_runs_or_exact_retained_records(string mutation)
    {
        var baseline = await Actual.Value;
        var index = baseline.Node("crm-index");
        var run = index["runs"]![0]!;
        var job = run["jobs"]![0]!;
        var artifact = run["artifacts"]![0]!;
        if (mutation == "schema") index["schemaId"] = "other";
        if (mutation == "state") index["consumerState"] = "Candidate";
        if (mutation == "deployable") index["deployable"] = true;
        if (mutation == "implementation") index["implementationMergeCommitSha"] = PinnedReleaseDocument.CommitSha;
        if (mutation == "source-tree") index["sourceTreeSha"] = new string('a', 40);
        if (mutation == "workflow") index["workflowFileSha"] = new string('a', 40);
        if (mutation == "count") index["testCountPerOs"] = 0;
        if (mutation == "forward-authority") index["forwardBinding"]!["authority"] = "GTX537/CP6.CRM";
        if (mutation == "forward-stage") index["forwardBinding"]!["stage"] = "P10-S05";
        if (mutation == "created-too-early") index["createdAtUtc"] = "2026-09-07T14:00:00Z";
        if (mutation == "created-too-late") index["createdAtUtc"] = "2026-09-07T16:00:00Z";
        if (mutation == "missing-run") index["runs"]!.AsArray().RemoveAt(1);
        if (mutation == "duplicate-phase") index["runs"]![1]!["phase"] = "pr";
        if (mutation == "run-id") run["runId"] = 34137355910;
        if (mutation == "run-attempt") run["runAttempt"] = 2;
        if (mutation == "run-source") run["sourceCommitSha"] = CrmConsumerRecordChecks.CheckoutSha;
        if (mutation == "run-checkout") run["checkoutCommitSha"] = baseline.Consumer.PullRequest.Workflow.CommitSha;
        if (mutation == "run-event") run["event"] = "push";
        if (mutation == "run-conclusion") run["conclusion"] = "failure";
        if (mutation == "job-missing") run["jobs"]!.AsArray().RemoveAt(4);
        if (mutation == "job-id") job["id"] = 1;
        if (mutation == "job-name") job["name"] = "other";
        if (mutation == "job-conclusion") job["conclusion"] = "skipped";
        if (mutation == "artifact-missing") run["artifacts"]!.AsArray().RemoveAt(1);
        if (mutation == "artifact-runner") artifact["runner"] = "macos-latest";
        if (mutation == "artifact-file") artifact["recordFile"] = "../another.json";
        if (mutation == "artifact-record-hash") artifact["recordSha256"] = new string('a', 64);
        if (mutation == "artifact-size") artifact["recordByteLength"] = 1;
        if (mutation == "artifact-name") artifact["artifactName"] = "wrong";
        if (mutation == "artifact-id") artifact["artifactId"] = 0;
        if (mutation == "artifact-digest") artifact["artifactDigest"] = "sha256:bad";
        if (mutation == "artifact-trx") artifact["trxSha256"] = "bad";
        if (mutation == "artifact-expiry") artifact["artifactExpiryUtc"] = "2026-09-07T14:00:00Z";
        Reject(() => CrmConsumerIndexChecks.Index(Element(index), baseline.Consumer, baseline.Archive));
    }

    [Fact]
    public async Task Forward_binding_requires_both_distinct_reviewed_deliveries()
    {
        var baseline = await Actual.Value;
        Reject(() => CrmConsumerIndexChecks.Index(Element(baseline.Node("crm-index")), baseline.Archive, baseline.Consumer),
            "github-crm-selection");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Only_the_two_fixed_commit_targets_and_identical_reviewed_trees_are_allowed(bool checkout)
    {
        var target = GitHubReadTarget.CrmConsumerCommit(checkout);
        Assert.Equal("/repos/GTX537/CP6.CRM/git/commits/" +
            (checkout ? CrmConsumerRecordChecks.CheckoutSha : S06ReleaseIdentity.CrmSource), target.Path);
        CrmConsumerRecordChecks.Commit(Element(CommitNode(checkout)), checkout);
    }

    [Theory]
    [InlineData("sha")]
    [InlineData("tree")]
    [InlineData("parent-count")]
    [InlineData("parent-base")]
    [InlineData("parent-head")]
    [InlineData("reversed")]
    public void Source_tree_relation_cannot_hide_a_changed_test_merge(string mutation)
    {
        var value = CommitNode(true);
        if (mutation == "sha") value["sha"] = S06ReleaseIdentity.CrmSource;
        if (mutation == "tree") value["tree"]!["sha"] = new string('a', 40);
        if (mutation == "parent-count") value["parents"]!.AsArray().RemoveAt(1);
        if (mutation == "parent-base") value["parents"]![0]!["sha"] = new string('a', 40);
        if (mutation == "parent-head") value["parents"]![1]!["sha"] = new string('a', 40);
        if (mutation == "reversed")
        {
            var first = value["parents"]![0]!.DeepClone();
            value["parents"]![0] = value["parents"]![1]!.DeepClone();
            value["parents"]![1] = first;
        }
        Reject(() => CrmConsumerRecordChecks.Commit(Element(value), true), "github-crm-commit");
    }

    [Fact]
    public async Task No_future_cutoff_or_precancelled_read_uses_a_credential()
    {
        Assert.Equal("github-proof-time", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            CrmConsumerEvidenceSource.ReadAsync(DateTimeOffset.UtcNow.AddDays(1), ""))).Code);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CrmConsumerEvidenceSource.ReadAsync(Cutoff, "", cancel.Token));
    }

    // The baseline uses real authenticated retained bytes and live run observations, cached only in test memory.
    private static async Task<Baseline> ReadBaselineAsync()
    {
        var token = Token();
        var records = new Dictionary<string, DownloadedReleaseDocument>(StringComparer.Ordinal);
        foreach (var name in Names) records.Add(name, await ReleaseArchiveSource.ReadAsync(name, token));
        return new(records, await CrmForwardBindingSource.ReadAsync(46, Cutoff, token),
            await CrmForwardBindingSource.ReadAsync(47, Cutoff, token));
    }

    private sealed record Baseline(IReadOnlyDictionary<string, DownloadedReleaseDocument> Records,
        CrmForwardBindingObservation Consumer, CrmForwardBindingObservation Archive)
    {
        internal JsonObject Node(string name) => JsonNode.Parse(Records[name].CopyBytes())!.AsObject();
    }

    // Selected-field parser vector copied from observed immutable commit metadata, not a network replacement.
    private static JsonObject CommitNode(bool checkout) => new()
    {
        ["sha"] = checkout ? CrmConsumerRecordChecks.CheckoutSha : S06ReleaseIdentity.CrmSource,
        ["tree"] = new JsonObject { ["sha"] = CrmConsumerRecordChecks.TreeSha },
        ["parents"] = new JsonArray(
            new JsonObject { ["sha"] = "4a340042f3596e82e7d358a39ad3106933c4395d" },
            new JsonObject { ["sha"] = "7b099200165e46f613a2689806f4a2a928425a8f" })
    };

    private static string Token() => Environment.GetEnvironmentVariable("P10_GITHUB_READ_TOKEN")
        ?? throw new InvalidOperationException("Actual CRM evidence is required; no tests skip or substitute success.");
    private static JsonElement Element(JsonNode value) => GitHubApiJson.Parse(Encoding.UTF8.GetBytes(value.ToJsonString()));
    private static void Reject(Action action, string? code = null)
    {
        var error = Assert.Throws<Cp6ReleaseContractException>(action);
        if (code is not null) Assert.Equal(code, error.Code);
        Assert.Null(error.InnerException);
    }
}
