# P10 S06 Retained CRM Consumer Evidence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Use the already selected sequential mode in the existing S06 worktree, without agents.

**Goal:** Verify the exact retained S05 CRM index and four OS consumer records against real workflow/job observations and the tested source-tree relation.

**Architecture:** The existing immutable archive reader authenticates pinned original bytes before any selected-field checks. The consumer source then binds the records/index to both reviewed PR/main pairs, verifies the original PR checkout and main commit share the same reviewed tree and ordered parents, and observes the archive commit's current main reachability. It returns owned raw bytes plus selected live observations; it is not a candidate acceptance API.

**Tech Stack:** .NET 8 BCL, existing bounded GitHub reader and archive/forward-binding modules, existing xUnit suite.

## Fixed scope and semantic distinctions

Continue after 745c6513 on the same S06 branch. No CRM CI rerun, private logs/TRX download, package republishing, workflow change, trust change, remote write or production action is part of this module.

- Index and four records remain at CRM bb1fd8b4f250fabde4476b6a450435de2d07c03f with the existing pinned raw SHA-256/length/Git blob identities. Never canonicalize their bytes.
- Each record must identify all seven exact 0.10.1 packages and their formal published hashes, Platform source, original publication, NuGet trust, signer/SPKI, internally trusted but not public-CA-trusted status, required RFC3161 policy, successful registry/locked restore, no ProjectReference, and 16/16 passed with zero failed/skipped.
- Require exact observed OS/SDK and OS-specific lock identity. A record's millisecond UTC timestamp must fall inside its corresponding formal OS job, not merely somewhere in the overall workflow.
- The index must bind the two S05 original runs' exact source/checkouts, attempts, events, job IDs/names/conclusions and retained record metadata. Its completedAtUtc is the last job completion, not workflow updated_at; actual values differ by one second. Its creation follows completed S05 main and precedes the evidence-only PR's validation.
- PR checkout 5d307d520a3bd7d5628d65cd98500869c9e7b101 differs from PR API head 7b099200165e46f613a2689806f4a2a928425a8f. Actual Git API inspection found checkout and S05 main a31ca0e323418f7e4108cc6220c0f5fa132e7fc2 share tree d66ba8fd85e8d82856aa525cdf0b2560ce47b78b and ordered parents [4a340042f3596e82e7d358a39ad3106933c4395d, 7b099200165e46f613a2689806f4a2a928425a8f]. Read only those two fixed Git objects; never let evidence choose a URL or commit.
- Archived Artifact IDs/digests/expiry/TRX hashes are historical metadata only. This module checks their binding/shape without claiming present availability, redownloading expiring artifacts or claiming it reverified private TRX contents. The prior accepted S05 retained evidence remains the input; real seven-package cryptographic verification is the separate existing NuGet component.
- A five-minute whole-operation deadline covers five archived documents, the two full PR/main pairs, the two fixed commit reads and archive main reachability. All response bounds, no-redirect rules and read-only scopes remain unchanged.
- Tests mutate actual authenticated retained records and use actual live observations cached only in test-process memory, not fabricated success reports or a new copied private fixture. The one integration test performs the whole real read path. Missing credentials, missing objects or failed reads fail the tests; no skip fallback.

References checked for the added read endpoint: [GitHub Get a Git commit](https://docs.github.com/en/rest/git/commits#get-a-commit). The existing Contents/Actions read scope remains the intended credential.

## Task 1: Tests and observed RED

**Create:** tools/p10/ReleaseVerifier.Tests/CrmConsumerEvidenceTests.cs.

- [x] Write the exact tests and only throwing missing-API scaffolds.
- [x] Run focused Release tests with the existing read credential only in process environment. Inspect every RED failure for the missing implementation; do not accept compilation errors, network failures or skips as RED.

From tools/p10 using the selected .NET 8 SDK:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~CrmConsumerEvidenceTests --logger "trx;LogFileName=crm-records-red.trx" --results-directory ../../artifacts/p10/crm-records-red
```

```csharp
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
```

## Task 2: Minimal implementation and GREEN

- [x] Apply the three new production files and the one complete updated target file below only after RED inspection.
- [x] Re-run focused tests using crm-records-green.trx and a separate results directory. All tests, including the full real CRM evidence read, must pass.

### tools/p10/ReleaseVerifier/CrmConsumerRecordChecks.cs

```csharp
using System.Globalization;
using System.Text.Json;
using static CP6.P10.ReleaseVerifier.GitHubEvidenceChecks;

namespace CP6.P10.ReleaseVerifier;

internal static class CrmConsumerRecordChecks
{
    internal const string CheckoutSha = "5d307d520a3bd7d5628d65cd98500869c9e7b101";
    internal const string TreeSha = "d66ba8fd85e8d82856aa525cdf0b2560ce47b78b";

    internal static DateTimeOffset Record(string name, JsonElement value, CrmForwardBindingObservation consumer)
    {
        var (pullRequest, runner, os, lockHash) = name switch
        {
            "crm-pr-linux" => (true, "ubuntu-latest", "Ubuntu 24.04.4 LTS", LinuxLock),
            "crm-pr-windows" => (true, "windows-latest", "Microsoft Windows 10.0.26100", WindowsLock),
            "crm-main-linux" => (false, "ubuntu-latest", "Ubuntu 24.04.4 LTS", LinuxLock),
            "crm-main-windows" => (false, "windows-latest", "Microsoft Windows 10.0.26100", WindowsLock),
            _ => throw GitHubWirePolicy.Error("github-crm-record-name")
        };
        Require(consumer.Selection.Number == 46, "github-crm-selection");
        var run = pullRequest ? consumer.PullRequest : consumer.Main;
        var producer = Property(value, "producer");
        Require(Text(value, "schemaId") == "cp6.crm.p10-s05-consumer-evidence.v1" &&
            Text(producer, "repository") == CrmPullRequestSelection.Repository &&
            Text(producer, "commitSha") == (pullRequest ? CheckoutSha : S06ReleaseIdentity.CrmSource) &&
            Text(producer, "workflowPath") == run.Workflow.WorkflowPath &&
            Text(producer, "workflowFileSha") == run.Workflow.WorkflowFileSha &&
            Number(producer, "runId") == run.Workflow.RunId && Number(producer, "runAttempt") == run.Workflow.RunAttempt,
            "github-crm-record-producer");
        Require(Text(value, "invocationKind") == "GitHubActions" && Text(value, "runnerOs") == os &&
            Text(value, "dotnetSdk") == "8.0.424" && Text(value, "lockSha256") == lockHash, "github-crm-record-runtime");
        Require(Text(value, "platformSourceSha") == S06ReleaseIdentity.Source &&
            Text(value, "publicationRecordSha256") == S06ReleaseIdentity.PublicationHash &&
            Text(value, "packageVersion") == S06ReleaseIdentity.Version, "github-crm-record-package");
        var packages = Property(value, "packageSubjects");
        Require(packages.ValueKind == JsonValueKind.Array && packages.GetArrayLength() == S06ReleaseIdentity.PackageHashes.Count,
            "github-crm-record-package");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in packages.EnumerateArray())
        {
            var id = Text(package, "packageId");
            Require(seen.Add(id) && S06ReleaseIdentity.PackageHashes.TryGetValue(id, out var hash) &&
                Text(package, "version") == S06ReleaseIdentity.Version && Text(package, "sha256") == hash, "github-crm-record-package");
        }
        Require(Text(value, "trustPolicySha256") == S06ReleaseIdentity.NuGetTrustHash &&
            Text(value, "signerFingerprint") == S06ReleaseIdentity.Signer &&
            Text(value, "spkiKeyId") == "sha256:27ecc2239a1b3c2368610d3602aadc5260b44e26baffe896b9a2449662c696d6" &&
            Property(value, "publicCaTrusted").ValueKind == JsonValueKind.False &&
            Property(value, "internallyTrusted").ValueKind == JsonValueKind.True &&
            Property(value, "deployable").ValueKind == JsonValueKind.False &&
            Text(value, "timestampPolicy") == "Rfc3161Required", "github-crm-record-trust");
        Require(Text(value, "registryRestore") == "Success" && Text(value, "lockedRestore") == "Success" &&
            Number(value, "projectReferences") == 0 && Number(value, "testCount") == 16 && Number(value, "passed") == 16 &&
            Number(value, "failed") == 0 && Number(value, "skipped") == 0 && Text(value, "testConclusion") == "success",
            "github-crm-record-result");
        Require(DateTimeOffset.TryParseExact(Text(value, "createdAtUtc"), "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var created), "github-proof-time");
        var jobs = run.Jobs.Where(j => j.Name == "platform-p10-formal-" + runner).ToArray();
        Require(jobs.Length == 1 && jobs[0].StartedAtUtc <= created && created <= jobs[0].CompletedAtUtc, "github-proof-time");
        return created;
    }

    internal static void Commit(JsonElement value, bool checkout)
    {
        Require(Text(value, "sha") == (checkout ? CheckoutSha : S06ReleaseIdentity.CrmSource) &&
            Text(Property(value, "tree"), "sha") == TreeSha, "github-crm-commit");
        var parents = Property(value, "parents");
        var selected = CrmPullRequestSelection.Get(46);
        Require(parents.ValueKind == JsonValueKind.Array && parents.GetArrayLength() == 2 &&
            Text(parents[0], "sha") == selected.BaseSha &&
            Text(parents[1], "sha") == selected.PullRequestWorkflow.CommitSha, "github-crm-commit");
    }

    private const string LinuxLock = "6ab560211c3bca108b1f981f35f1160d90d692fd88ee39c3ebfe3c66084fcd88";
    private const string WindowsLock = "dca0cbc0e84527934bb08e74291b0fcc067a7446cbbcb530d04ad8d8152187de";
}
```

### tools/p10/ReleaseVerifier/CrmConsumerIndexChecks.cs

```csharp
using System.Text.Json;
using static CP6.P10.ReleaseVerifier.GitHubEvidenceChecks;

namespace CP6.P10.ReleaseVerifier;

internal static class CrmConsumerIndexChecks
{
    internal static void Index(JsonElement value, CrmForwardBindingObservation consumer, CrmForwardBindingObservation archive)
    {
        Require(consumer.Selection.Number == 46 && archive.Selection.Number == 47 &&
            archive.Selection.BaseSha == consumer.Main.Workflow.CommitSha, "github-crm-selection");
        Require(Text(value, "schemaId") == "cp6.crm.p10-s05-verification-index.v1" &&
            Text(value, "repository") == CrmPullRequestSelection.Repository &&
            Text(value, "consumerState") == "VerifiedFormal" && Property(value, "deployable").ValueKind == JsonValueKind.False &&
            Text(value, "packageVersion") == S06ReleaseIdentity.Version && Text(value, "platformSourceSha") == S06ReleaseIdentity.Source &&
            Text(value, "publicationRecordSha256") == S06ReleaseIdentity.PublicationHash &&
            Text(value, "platformEvidenceMainSha") == "808a201f0cf6f877f8ca9c804e304585a29c446a" &&
            Text(value, "implementationMergeCommitSha") == consumer.Main.Workflow.CommitSha &&
            Text(value, "implementationPullRequest") == "https://github.com/GTX537/CP6.CRM/pull/46" &&
            Text(value, "pullRequestBaseCommitSha") == consumer.Selection.BaseSha &&
            Text(value, "sourceTreeSha") == CrmConsumerRecordChecks.TreeSha &&
            Text(value, "workflowPath") == CrmPullRequestSelection.WorkflowPath &&
            Text(value, "workflowFileSha") == CrmPullRequestSelection.WorkflowFileSha &&
            Number(value, "testCountPerOs") == 16, "github-crm-index-identity");
        var forward = Property(value, "forwardBinding");
        Require(Text(forward, "authority") == "GTX537/CP6" && Text(forward, "stage") == "P10-S06", "github-crm-index-forward");
        var created = Time(value, "createdAtUtc");
        Require(consumer.Main.CompletedAtUtc <= created && created <= archive.PullRequest.StartedAtUtc, "github-proof-time");
        var runs = Property(value, "runs");
        Require(runs.ValueKind == JsonValueKind.Array && runs.GetArrayLength() == 2, "github-crm-index-runs");
        var phases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var run in runs.EnumerateArray())
        {
            var phase = Text(run, "phase");
            Require(phase is "pr" or "main" && phases.Add(phase), "github-crm-index-runs");
            var observed = phase == "pr" ? consumer.PullRequest : consumer.Main;
            var checkout = phase == "pr" ? CrmConsumerRecordChecks.CheckoutSha : S06ReleaseIdentity.CrmSource;
            Require(Number(run, "runId") == observed.Workflow.RunId && Number(run, "runAttempt") == observed.Workflow.RunAttempt &&
                Text(run, "sourceCommitSha") == observed.Workflow.CommitSha && Text(run, "checkoutCommitSha") == checkout &&
                Text(run, "event") == observed.Event && Text(run, "status") == "completed" &&
                Text(run, "conclusion") == "success", "github-crm-index-runs");
            // This retained index records the final job completion, not the run API's updated_at.
            Require(Time(run, "startedAtUtc") == observed.StartedAtUtc &&
                Time(run, "completedAtUtc") == observed.Jobs.Max(j => j.CompletedAtUtc), "github-proof-time");
            Jobs(Property(run, "jobs"), observed);
            Artifacts(Property(run, "artifacts"), phase, checkout, created);
        }
    }

    private static void Jobs(JsonElement jobs, GitHubWorkflowObservation observed)
    {
        Require(jobs.ValueKind == JsonValueKind.Array && jobs.GetArrayLength() == observed.Jobs.Count, "github-crm-index-jobs");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var job in jobs.EnumerateArray())
        {
            var name = Text(job, "name");
            var selected = observed.Jobs.Where(j => j.Name == name).ToArray();
            Require(names.Add(name) && selected.Length == 1 && Number(job, "id") == selected[0].Id &&
                Text(job, "conclusion") == "success", "github-crm-index-jobs");
        }
    }

    private static void Artifacts(JsonElement artifacts, string phase, string checkout, DateTimeOffset indexCreated)
    {
        Require(artifacts.ValueKind == JsonValueKind.Array && artifacts.GetArrayLength() == 2, "github-crm-index-artifacts");
        var runners = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<long>();
        foreach (var artifact in artifacts.EnumerateArray())
        {
            var runner = Text(artifact, "runner");
            Require(runner is "ubuntu-latest" or "windows-latest" && runners.Add(runner), "github-crm-index-artifacts");
            var os = runner == "ubuntu-latest" ? "linux" : "windows";
            var pinned = PinnedReleaseDocument.Get("crm-" + phase + "-" + os);
            Require(Text(artifact, "recordFile") == phase + "-" + os + ".consumer-evidence.v1.json" &&
                Text(artifact, "recordSha256") == pinned.Sha256 && Number(artifact, "recordByteLength") == pinned.ByteLength &&
                Text(artifact, "artifactName") == "crm-platform-p10-formal-" + runner + "-" + checkout + "-1",
                "github-crm-index-artifacts");
            var id = Number(artifact, "artifactId");
            Require(id > 0 && ids.Add(id) && Time(artifact, "artifactExpiryUtc") > indexCreated &&
                IsHash(Text(artifact, "trxSha256")), "github-crm-index-artifacts");
            var digest = Text(artifact, "artifactDigest");
            Require(digest.StartsWith("sha256:", StringComparison.Ordinal) && IsHash(digest[7..]), "github-crm-index-artifacts");
        }
        // Artifact IDs/digests/expiry/TRX hashes are retained historical metadata, not live availability or TRX verification.
    }

    private static bool IsHash(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
```

### tools/p10/ReleaseVerifier/CrmConsumerEvidenceSource.cs

```csharp
using System.Collections.Frozen;
using static CP6.P10.ReleaseVerifier.GitHubEvidenceChecks;

namespace CP6.P10.ReleaseVerifier;

internal static class CrmConsumerEvidenceSource
{
    internal static async Task<CrmConsumerEvidenceObservation> ReadAsync(DateTimeOffset completedBeforeUtc, string token,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireCutoff(completedBeforeUtc);
        Require(completedBeforeUtc <= DateTimeOffset.UtcNow, "github-proof-time");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var documents = new Dictionary<string, DownloadedReleaseDocument>(StringComparer.Ordinal);
        foreach (var name in new[] { "crm-index", "crm-pr-linux", "crm-pr-windows", "crm-main-linux", "crm-main-windows" })
            documents.Add(name, await ReleaseArchiveSource.ReadAsync(name, token, deadline.Token));
        var consumer = await CrmForwardBindingSource.ReadAsync(46, completedBeforeUtc, token, deadline.Token);
        var archive = await CrmForwardBindingSource.ReadAsync(47, completedBeforeUtc, token, deadline.Token);
        CrmConsumerIndexChecks.Index(GitHubApiJson.Parse(documents["crm-index"].CopyBytes()), consumer, archive);
        foreach (var document in documents.Where(d => d.Key != "crm-index"))
            _ = CrmConsumerRecordChecks.Record(document.Key, GitHubApiJson.Parse(document.Value.CopyBytes()), consumer);
        using var client = new GitHubReadClient(token);
        foreach (var checkout in new[] { false, true })
            CrmConsumerRecordChecks.Commit(await client.ReadAsync(GitHubReadTarget.CrmConsumerCommit(checkout), deadline.Token), checkout);
        var reachability = await GitHubEvidenceSource.ReadSourceAsync(
            CrmPullRequestSelection.Repository, PinnedReleaseDocument.CommitSha, token, deadline.Token);
        return new(consumer, archive, reachability, documents.ToFrozenDictionary(StringComparer.Ordinal), DateTimeOffset.UtcNow);
    }
}

internal sealed record CrmConsumerEvidenceObservation(CrmForwardBindingObservation Consumer, CrmForwardBindingObservation Archive,
    GitHubSourceObservation ArchiveSource, IReadOnlyDictionary<string, DownloadedReleaseDocument> Documents, DateTimeOffset ObservedAtUtc);
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

    internal static GitHubReadTarget CrmConsumerCommit(bool checkout) =>
        new(RepositoryPath(CrmPullRequestSelection.Repository) + "/git/commits/" +
            (checkout ? CrmConsumerRecordChecks.CheckoutSha : S06ReleaseIdentity.CrmSource));

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

- [x] Run locked restore, full Release regression and format verification with the existing required real cosign/package/feed/GitHub inputs. Keep credentials out of files, command arguments and output.
- [x] Review every new/changed source, exact plan parity, full scoped diff and secret/whitespace hygiene. Confirm old evidence, workflows, trust, dependency and runtime files are untouched.
- [ ] Stage only this plan, the test and the four production files above, then commit feat(p10): verify retained CRM consumer evidence.
- [ ] Continue S06 OCI/report/envelope/workflow/publication integration. This module does not complete P10 or publish a candidate.

```powershell
dotnet restore ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --configfile ../../eng/p10/NuGet.formal.config --packages ../../artifacts/p10/foundation-restore/packages --locked-mode --verbosity minimal
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore
dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --verify-no-changes --no-restore
git diff --check
```

## Verification outcome (2026-09-08)

All 90 focused tests first failed with the expected NotImplementedException and zero unrelated failures or skips. The planned implementation passed all 90, including the complete real archive/PR/main/commit-tree/readability path. Mutation tests used real authenticated archived records and live workflow observations held only in test memory. Full Release regression passed 730/730 with zero skips, locked restore reported no changes, and format verification passed. Exact five-file plan parity, full scoped review, secret hygiene and whitespace checks passed. No old evidence, dependency, trust, runtime, workflow, package, OCI or R2 object was changed. This is retained consumer-evidence verification, not candidate acceptance or P10 completion.
