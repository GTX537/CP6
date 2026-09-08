# P10 S06 Public CRM Evidence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans sequentially in this task. The user selected no delegation.

**Goal:** Carry the already verified private CRM S05 evidence in a safe public S06 payload that normal consumers can read without CRM credentials.

**Architecture:** The protected collector invokes the existing live CRM reader, including immutable archive checks, both reviewed PR/main deliveries, all required jobs, commit-tree equivalence and source reachability. Only the five approved hash-pinned JSON records and selected safe observations are serialized. The read-only codec rechecks the fixed identities, exact raw document hashes, selected workflow/file/job facts, timing/order and existing index/record semantics without calling CRM; the later authenticated Locator and protected producer checks remain mandatory before trusting this payload.

**Tech Stack:** .NET SDK 8.0.424, CP6.Platform.Release [0.10.1], S06 in-toto envelope, existing fixed-host GitHub reader, deterministic JSON, xUnit.

---

## Scope and evidence boundary

The public payload does not retain raw GitHub API responses, runner metadata, TRX files, logs, tokens or signed URLs. Five approved JSON files are preserved byte-for-byte inside canonical base64 strings, with their compiled SHA-256 and length independently enforced. Public selected observations do not claim that historical artifact IDs imply current artifact availability or that old CRM source was protected.

Four selected run summaries use the fixed S05 PR 46/47 workflow identities, environment=none, exact completed/success outcomes and five sorted required jobs. The decoded summaries must agree with the immutable consumer index and per-OS records. PR completion precedes merge, merge precedes main start, and observation times follow the collection order. The full normal verifier must authenticate the enclosing Locator and graph, including the protected public validation producer, before treating this private-evidence attestation as authoritative.

An authenticated metadata read of the pinned archive commit on 2026-09-08 confirmed the CRM workflow Git blob 924014cb1231824a9b57ab82a6f9638f76329919, 14913 bytes, and SHA-256 43864e5b88b516bf23d8395ace8f8e1646ca1efa70829e093b7bd7b833f490ef. Only that hash/length, not the private workflow text, is added to this codec.

This module performs no CRM rerun, package publication, OCI/R2 write, deployment or approval action.

## File map

- Create tools/p10/ReleaseVerifier/CrmPublicObservations.cs: selected safe run/source serialization and strict fixed-profile decoding.
- Create tools/p10/ReleaseVerifier/CrmPublicEvidence.cs: live private collection, exact retained documents and public statement validation.
- Create tools/p10/ReleaseVerifier.Tests/CrmPublicEvidenceTests.cs: 39 cases with a real private collection, 36 binding mutations and two pre-network guards.
- Create this plan. Do not modify existing private records, workflow identities or R2 authority.

## Task 1: Establish the missing behavior

- [x] Add this exact test file first.

### tools/p10/ReleaseVerifier.Tests/CrmPublicEvidenceTests.cs

```csharp
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// The producer is an unsigned unit vector. The five records and four workflow observations are actual CRM reads.
public sealed class CrmPublicEvidenceTests
{
    private static GitHubWorkflowIdentity Producer => new("GTX537/CP6", S06ReleaseIdentity.ValidationPath,
        new string('b', 40), 12345, 1, new string('c', 40));
    private static readonly Lazy<Task<byte[]>> Actual = new(() => CrmPublicEvidence.CreateAsync(Producer, Token()));

    [Fact]
    public async Task Actual_private_evidence_becomes_a_bounded_public_summary_read_without_a_private_token()
    {
        var bytes = await Actual.Value;
        var statement = CrmPublicEvidence.Read(bytes, Producer, DateTimeOffset.UtcNow);
        Assert.Equal("CrmConsumer", statement.Kind);
        Assert.Equal(9, JsonNode.Parse(bytes)!["subject"]!.AsArray().Count);
        var documents = statement.Details.GetProperty("documents");
        Assert.Equal(5, documents.GetArrayLength());
        foreach (var document in documents.EnumerateArray())
        {
            var pinned = PinnedReleaseDocument.Get(document.GetProperty("name").GetString()!);
            var raw = Convert.FromBase64String(document.GetProperty("contentBase64").GetString()!);
            Assert.Equal(pinned.ByteLength, raw.Length);
            Assert.Equal(pinned.Sha256, Cp6DeterministicJson.Sha256Hex(raw));
        }
        Assert.Equal(2, statement.Details.GetProperty("deliveries").GetArrayLength());
        var text = Encoding.UTF8.GetString(bytes);
        Assert.False(text.Contains(Token(), StringComparison.Ordinal));
        Assert.False(text.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes(Token())), StringComparison.Ordinal));
        Assert.Equal(bytes, Cp6DeterministicJson.Canonicalize(bytes));
    }

    [Theory]
    [InlineData("archive-repository")]
    [InlineData("archive-commit")]
    [InlineData("reviewed-tree")]
    [InlineData("source-repository")]
    [InlineData("source-commit")]
    [InlineData("source-main")]
    [InlineData("source-protection-type")]
    [InlineData("source-relationship")]
    [InlineData("source-observed")]
    [InlineData("source-extra")]
    [InlineData("document-missing")]
    [InlineData("document-order")]
    [InlineData("document-name")]
    [InlineData("document-bytes")]
    [InlineData("document-base64-spacing")]
    [InlineData("document-extra")]
    [InlineData("delivery-missing")]
    [InlineData("delivery-order")]
    [InlineData("delivery-number")]
    [InlineData("merge-after-main")]
    [InlineData("delivery-observed")]
    [InlineData("run-identity")]
    [InlineData("run-file-hash")]
    [InlineData("run-file-length")]
    [InlineData("run-event")]
    [InlineData("run-conclusion")]
    [InlineData("run-status")]
    [InlineData("run-observed")]
    [InlineData("run-time")]
    [InlineData("job-missing")]
    [InlineData("job-name")]
    [InlineData("job-id")]
    [InlineData("job-conclusion")]
    [InlineData("job-time")]
    [InlineData("job-order")]
    [InlineData("job-extra")]
    public async Task Selected_public_fields_cannot_change_the_fixed_private_evidence_bindings(string mutation)
    {
        var root = JsonNode.Parse(await Actual.Value)!;
        var details = root["predicate"]!["details"]!;
        var source = details["archiveSource"]!;
        var documents = details["documents"]!.AsArray();
        var deliveries = details["deliveries"]!.AsArray();
        var delivery = deliveries[0]!;
        var run = delivery["main"]!;
        var jobs = run["jobs"]!.AsArray();
        var job = jobs[0]!;
        if (mutation == "archive-repository") details["archiveRepository"] = "Other/Repo";
        if (mutation == "archive-commit") details["archiveCommitSha"] = new string('a', 40);
        if (mutation == "reviewed-tree") details["reviewedConsumerTreeSha"] = new string('a', 40);
        if (mutation == "source-repository") source["repository"] = "GTX537/CP6.Platform";
        if (mutation == "source-commit") source["sourceGitSha"] = new string('a', 40);
        if (mutation == "source-main") source["observedMainSha"] = "main";
        if (mutation == "source-protection-type") source["mainProtectedAtObservation"] = "false";
        if (mutation == "source-relationship") source["relationship"] = "Unknown";
        if (mutation == "source-observed") source["observedAtUtc"] = S06InToto.FormatTime(DateTimeOffset.UtcNow.AddDays(1));
        if (mutation == "source-extra") source["privateLog"] = "not-allowed";
        if (mutation == "document-missing") documents.RemoveAt(4);
        if (mutation == "document-order") Swap(documents, 0, 1);
        if (mutation == "document-name") documents[0]!["name"] = "publication";
        if (mutation == "document-bytes") documents[0]!["contentBase64"] = Convert.ToBase64String("{}"u8);
        if (mutation == "document-base64-spacing")
            documents[0]!["contentBase64"] = documents[0]!["contentBase64"]!.GetValue<string>() + "\n";
        if (mutation == "document-extra") documents[0]!["unreviewed"] = true;
        if (mutation == "delivery-missing") deliveries.RemoveAt(1);
        if (mutation == "delivery-order") Swap(deliveries, 0, 1);
        if (mutation == "delivery-number") delivery["number"] = 48;
        if (mutation == "merge-after-main") delivery["mergedAtUtc"] = S06InToto.FormatTime(DateTimeOffset.UtcNow);
        if (mutation == "delivery-observed") delivery["observedAtUtc"] = S06InToto.FormatTime(DateTimeOffset.UnixEpoch);
        if (mutation == "run-identity") run["workflow"]!["runId"] = 1;
        if (mutation == "run-file-hash") run["file"]!["sha256"] = new string('a', 64);
        if (mutation == "run-file-length") run["file"]!["byteLength"] = 1;
        if (mutation == "run-event") run["event"] = "workflow_dispatch";
        if (mutation == "run-conclusion") run["conclusion"] = "failure";
        if (mutation == "run-status") run["status"] = "in_progress";
        if (mutation == "run-observed") run["observedAtUtc"] = S06InToto.FormatTime(DateTimeOffset.UtcNow.AddDays(1));
        if (mutation == "run-time") run["startedAtUtc"] = S06InToto.FormatTime(DateTimeOffset.UtcNow);
        if (mutation == "job-missing") jobs.RemoveAt(4);
        if (mutation == "job-name") job["name"] = "not-a-required-job";
        if (mutation == "job-id") job["id"] = 0;
        if (mutation == "job-conclusion") job["conclusion"] = "skipped";
        if (mutation == "job-time") job["startedAtUtc"] = S06InToto.FormatTime(DateTimeOffset.UnixEpoch);
        if (mutation == "job-order") Swap(jobs, 0, 1);
        if (mutation == "job-extra") job["runnerName"] = "not-allowed";
        var failure = Assert.Throws<Cp6ReleaseContractException>(() =>
            CrmPublicEvidence.Read(Canonical(root), Producer, DateTimeOffset.UtcNow));
        Assert.Null(failure.InnerException);
    }

    [Fact]
    public async Task An_invalid_public_producer_fails_before_private_collection() =>
        Assert.Equal("s06-attestation-producer", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            CrmPublicEvidence.CreateAsync(Producer with { WorkflowPath = S06ReleaseIdentity.PublicationPath },
                "not-a-real-token"))).Code);

    [Fact]
    public async Task Precancelled_collection_uses_no_private_credential()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CrmPublicEvidence.CreateAsync(Producer, "not-a-real-token", cancellation.Token));
    }

    private static void Swap(JsonArray array, int left, int right)
    {
        var value = array[left]!.DeepClone();
        array[left] = array[right]!.DeepClone();
        array[right] = value;
    }

    private static byte[] Canonical(JsonNode root) =>
        Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(root));
    private static string Token() => Environment.GetEnvironmentVariable("P10_GITHUB_READ_TOKEN") ??
        throw new InvalidOperationException("Actual CRM read authorization is required; no skip or synthetic CRM evidence.");
}
```

- [x] Add only these throwing entry declarations, then run the 39 cases. Expected: every case fails with NotImplementedException, zero skips and no missing-input or compile errors.

### Throwing declarations

```csharp
namespace CP6.P10.ReleaseVerifier;

internal static class CrmPublicEvidence
{
    internal static Task<byte[]> CreateAsync(GitHubWorkflowIdentity producer, string crmReadToken,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();

    internal static S06Attestation Read(ReadOnlyMemory<byte> bytes, GitHubWorkflowIdentity producer, DateTimeOffset cutoff) =>
        throw new NotImplementedException();
}
```

Run from tools/p10 with the established .NET SDK and actual P10_GITHUB_READ_TOKEN in the process environment only:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~CrmPublicEvidenceTests --logger "trx;LogFileName=crm-public-red.trx" --results-directory ../../artifacts/p10/crm-public-red
```

## Task 2: Implement the safe public codec

- [x] Add both complete implementations.

### tools/p10/ReleaseVerifier/CrmPublicObservations.cs

```csharp
using System.Text.Json;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Only selected safe facts are serialized. Raw private API responses, runner metadata and logs are excluded.
internal static class CrmPublicObservations
{
    // Independently read from the pinned workflow Git blob at the S05 archive commit.
    private const string WorkflowSha256 = "43864e5b88b516bf23d8395ace8f8e1646ca1efa70829e093b7bd7b833f490ef";
    private const int WorkflowByteLength = 14913;

    internal static JsonElement Delivery(CrmForwardBindingObservation observed) => JsonSerializer.SerializeToElement(new
    {
        number = observed.Selection.Number,
        mergedAtUtc = FormatTime(observed.MergedAtUtc),
        observedAtUtc = FormatTime(observed.ObservedAtUtc),
        pullRequest = Run(observed.PullRequest),
        main = Run(observed.Main)
    });

    internal static CrmForwardBindingObservation ReadDelivery(JsonElement value, int number, DateTimeOffset cutoff)
    {
        Exact(value, "number", "mergedAtUtc", "observedAtUtc", "pullRequest", "main");
        var selected = CrmPullRequestSelection.Get(number);
        Require(value.GetProperty("number").GetInt32() == number, "s06-crm-delivery");
        var merged = Time(value, "mergedAtUtc");
        var observed = Time(value, "observedAtUtc");
        var pr = ReadRun(value.GetProperty("pullRequest"), selected.PullRequestWorkflow, "pull_request", cutoff);
        var main = ReadRun(value.GetProperty("main"), selected.MainWorkflow, "push", cutoff);
        CrmForwardBindingChecks.ForwardOrder(pr.CompletedAtUtc, merged, main.StartedAtUtc, main.CompletedAtUtc, cutoff);
        Require(pr.ObservedAtUtc <= main.ObservedAtUtc && main.ObservedAtUtc <= observed && observed <= cutoff,
            "s06-crm-time");
        return new(selected, merged, pr, main, observed);
    }

    internal static JsonElement Source(GitHubSourceObservation observed) => JsonSerializer.SerializeToElement(new
    {
        repository = observed.Repository,
        sourceGitSha = observed.SourceGitSha,
        observedMainSha = observed.ObservedMainSha,
        mainProtectedAtObservation = observed.MainProtectedAtObservation,
        observedAtUtc = FormatTime(observed.ObservedAtUtc),
        relationship = "SourceReachableFromMain"
    });

    internal static GitHubSourceObservation ReadSource(JsonElement value, DateTimeOffset cutoff)
    {
        Exact(value, "repository", "sourceGitSha", "observedMainSha", "mainProtectedAtObservation", "observedAtUtc", "relationship");
        var main = Text(value, "observedMainSha");
        GitHubReadTarget.RequireSha(main);
        var protection = value.GetProperty("mainProtectedAtObservation");
        Require(Text(value, "repository") == CrmPullRequestSelection.Repository &&
            Text(value, "sourceGitSha") == PinnedReleaseDocument.CommitSha &&
            Text(value, "relationship") == "SourceReachableFromMain" &&
            protection.ValueKind is JsonValueKind.True or JsonValueKind.False, "s06-crm-source");
        var observed = Time(value, "observedAtUtc");
        Require(observed <= cutoff, "s06-crm-time");
        // CRM protection is recorded honestly; the collector does not claim public Platform's policy applies to CRM.
        return new(CrmPullRequestSelection.Repository, PinnedReleaseDocument.CommitSha, main, protection.GetBoolean(), observed);
    }

    private static JsonElement Run(GitHubWorkflowObservation observed) => JsonSerializer.SerializeToElement(new
    {
        workflow = Identity(observed.Workflow),
        @event = observed.Event,
        status = "completed",
        conclusion = "success",
        startedAtUtc = FormatTime(observed.StartedAtUtc),
        completedAtUtc = FormatTime(observed.CompletedAtUtc),
        observedAtUtc = FormatTime(observed.ObservedAtUtc),
        file = new { sha256 = observed.File.Sha256, byteLength = observed.File.ByteLength },
        jobs = observed.Jobs.OrderBy(j => j.Name, StringComparer.Ordinal).Select(j => new
        {
            id = j.Id,
            name = j.Name,
            conclusion = "success",
            startedAtUtc = FormatTime(j.StartedAtUtc),
            completedAtUtc = FormatTime(j.CompletedAtUtc)
        }).ToArray()
    });

    private static GitHubWorkflowObservation ReadRun(JsonElement value, GitHubWorkflowIdentity expected,
        string expectedEvent, DateTimeOffset cutoff)
    {
        Exact(value, "workflow", "event", "status", "conclusion", "startedAtUtc", "completedAtUtc", "observedAtUtc", "file", "jobs");
        Require(Equal(value.GetProperty("workflow"), Identity(expected)) && Text(value, "event") == expectedEvent &&
            Text(value, "status") == "completed" && Text(value, "conclusion") == "success", "s06-crm-run");
        var started = Time(value, "startedAtUtc");
        var completed = Time(value, "completedAtUtc");
        var observed = Time(value, "observedAtUtc");
        Require(started <= completed && completed <= observed && observed <= cutoff, "s06-crm-time");
        var file = value.GetProperty("file");
        Exact(file, "sha256", "byteLength");
        Require(Text(file, "sha256") == WorkflowSha256 && file.GetProperty("byteLength").GetInt32() == WorkflowByteLength,
            "s06-crm-workflow-file");
        var jobs = value.GetProperty("jobs");
        var names = CrmPullRequestSelection.RequiredJobs.Order(StringComparer.Ordinal).ToArray();
        Require(jobs.ValueKind == JsonValueKind.Array && jobs.GetArrayLength() == names.Length, "s06-crm-jobs");
        var ids = new HashSet<long>();
        var results = new List<GitHubJobObservation>();
        for (var index = 0; index < names.Length; index++)
        {
            var job = jobs[index];
            Exact(job, "id", "name", "conclusion", "startedAtUtc", "completedAtUtc");
            var id = job.GetProperty("id").GetInt64();
            Require(id > 0 && ids.Add(id) && Text(job, "name") == names[index] && Text(job, "conclusion") == "success",
                "s06-crm-jobs");
            var jobStarted = Time(job, "startedAtUtc");
            var jobCompleted = Time(job, "completedAtUtc");
            Require(started <= jobStarted && jobStarted <= jobCompleted && jobCompleted <= completed, "s06-crm-time");
            results.Add(new(id, names[index], jobStarted, jobCompleted));
        }
        return new(expected, expectedEvent, started, completed, new(WorkflowSha256, WorkflowByteLength),
            results.AsReadOnly(), observed);
    }

    private static JsonElement Identity(GitHubWorkflowIdentity workflow) => JsonSerializer.SerializeToElement(new
    {
        repository = workflow.Repository,
        workflowPath = workflow.WorkflowPath,
        workflowFileSha = workflow.WorkflowFileSha,
        runId = workflow.RunId,
        runAttempt = workflow.RunAttempt,
        commitSha = workflow.CommitSha,
        environment = "none"
    });

    private static bool Equal(JsonElement left, JsonElement right) =>
        CP6.Platform.Release.Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(left)).AsSpan().SequenceEqual(
            CP6.Platform.Release.Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(right)));
}
```

### tools/p10/ReleaseVerifier/CrmPublicEvidence.cs

```csharp
using System.Text.Json;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// The protected collector reads CRM. A normal consumer reads only this authenticated public evidence payload.
// This unsigned codec is not the authentication boundary; the full consumer must verify the Locator/graph/producer first.
internal static class CrmPublicEvidence
{
    private static readonly string[] Names =
        ["crm-index", "crm-main-linux", "crm-main-windows", "crm-pr-linux", "crm-pr-windows"];

    internal static async Task<byte[]> CreateAsync(GitHubWorkflowIdentity producer, string crmReadToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireProducer(producer);
        var observed = await CrmConsumerEvidenceSource.ReadAsync(DateTimeOffset.UtcNow, crmReadToken, cancellationToken);
        var details = JsonSerializer.SerializeToElement(new
        {
            archiveRepository = CrmPullRequestSelection.Repository,
            archiveCommitSha = observed.ArchiveSource.SourceGitSha,
            reviewedConsumerTreeSha = CrmConsumerRecordChecks.TreeSha,
            archiveSource = CrmPublicObservations.Source(observed.ArchiveSource),
            documents = observed.Documents.OrderBy(d => d.Key, StringComparer.Ordinal).Select(d => new
            {
                name = d.Key,
                contentBase64 = Convert.ToBase64String(d.Value.CopyBytes())
            }).ToArray(),
            deliveries = new[] { CrmPublicObservations.Delivery(observed.Consumer), CrmPublicObservations.Delivery(observed.Archive) }
        });
        var bytes = Create("CrmConsumer", producer, observed.ObservedAtUtc, details);
        _ = Read(bytes, producer, DateTimeOffset.UtcNow);
        return bytes;
    }

    internal static S06Attestation Read(ReadOnlyMemory<byte> bytes, GitHubWorkflowIdentity producer, DateTimeOffset cutoff)
    {
        var statement = S06InToto.Read(bytes, "CrmConsumer", producer, cutoff);
        try
        {
            var details = statement.Details;
            Exact(details, "archiveRepository", "archiveCommitSha", "reviewedConsumerTreeSha", "archiveSource", "documents", "deliveries");
            Require(Text(details, "archiveRepository") == CrmPullRequestSelection.Repository &&
                Text(details, "archiveCommitSha") == PinnedReleaseDocument.CommitSha &&
                Text(details, "reviewedConsumerTreeSha") == CrmConsumerRecordChecks.TreeSha, "s06-crm-archive");
            var source = CrmPublicObservations.ReadSource(details.GetProperty("archiveSource"), statement.CreatedAtUtc);
            var deliveries = details.GetProperty("deliveries");
            Require(deliveries.ValueKind == JsonValueKind.Array && deliveries.GetArrayLength() == 2, "s06-crm-delivery");
            var consumer = CrmPublicObservations.ReadDelivery(deliveries[0], 46, statement.CreatedAtUtc);
            var archive = CrmPublicObservations.ReadDelivery(deliveries[1], 47, statement.CreatedAtUtc);
            Require(consumer.ObservedAtUtc <= archive.ObservedAtUtc && archive.ObservedAtUtc <= source.ObservedAtUtc,
                "s06-crm-time");
            var documents = Documents(details.GetProperty("documents"));
            CrmConsumerIndexChecks.Index(documents["crm-index"], consumer, archive);
            foreach (var name in Names.Where(n => n != "crm-index"))
                _ = CrmConsumerRecordChecks.Record(name, documents[name], consumer);
            return statement;
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("s06-crm-shape"); }
    }

    private static Dictionary<string, JsonElement> Documents(JsonElement values)
    {
        Require(values.ValueKind == JsonValueKind.Array && values.GetArrayLength() == Names.Length, "s06-crm-documents");
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        for (var index = 0; index < Names.Length; index++)
        {
            var value = values[index];
            Exact(value, "name", "contentBase64");
            Require(Text(value, "name") == Names[index], "s06-crm-documents");
            var pinned = PinnedReleaseDocument.Get(Names[index]);
            var encoded = Text(value, "contentBase64");
            Require(encoded.Length == 4 * ((pinned.ByteLength + 2) / 3), "s06-crm-document-bytes");
            var bytes = Convert.FromBase64String(encoded);
            Require(bytes.Length == pinned.ByteLength && Convert.ToBase64String(bytes) == encoded &&
                Cp6DeterministicJson.Sha256Hex(bytes) == pinned.Sha256, "s06-crm-document-bytes");
            result.Add(Names[index], GitHubApiJson.Parse(bytes));
        }
        return result;
    }
}
```

- [x] Run focused and full Release tests with all existing actual inputs, then verify formatting. Expected: 39 focused and 1054 total tests pass with zero skips.

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~CrmPublicEvidenceTests
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore
dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --verify-no-changes --no-restore
```

## Task 3: Review and checkpoint

- [x] Verify all three code blocks exactly match their files; read the four-file scope for timing, identity, data-minimization and secret/path safety.
- [ ] Record actual results, stage only this scope and commit using explicit native-command exit gates.

```powershell
git diff --check
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
git add -- docs/superpowers/plans/2026-09-08-p10-s06-crm-public-evidence.md tools/p10/ReleaseVerifier/CrmPublicObservations.cs tools/p10/ReleaseVerifier/CrmPublicEvidence.cs tools/p10/ReleaseVerifier.Tests/CrmPublicEvidenceTests.cs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
git diff --cached --check
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
git commit -m "feat(p10): expose bound CRM evidence without private consumer access"
```

- [ ] Finish the remaining typed image evidence, full graph acceptance, protected workflows, real immutable publication and pre/post audit before P10 completion.

## Observed component verification

On 2026-09-08 all 39 new cases first failed with the deliberate NotImplementedException entries (39 expected failures, zero unexpected failures or skips). After implementation, all 39 passed in 18 seconds using actual private CRM archive, run, job, tree and reachability reads. The complete Release suite passed 1054/1054 with zero skips in 46 seconds, and format verification passed. All three implementation/test files exactly match their plan code blocks; the four-file scope passed whitespace and sensitive/path checks.

No private logs, raw private API responses or signing credentials were retained in the public payload. These unsigned component vectors do not establish an actual protected validation run, candidate publication or P10 completion. No OCI/R2 object, workflow or deployment was created or changed by this module.
