# P10 S06 completed-workflow chronology implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. The owner selected inline sequential execution; do not dispatch subagents.

**Goal:** Bind the existing S06 evidence graph to the observed S04, validation, and publication time windows without treating a timestamp check as authentication.

**Architecture:** One fixed workflow profile selects the two real S04 jobs and the single protected validation/publication jobs. A pure chronology checker consumes independently read workflow observations, enforces validation-before-publication, and binds statements, evidence records, gate, image build/signing, and formal-package retrieval times. It returns no acceptance capability.

**Tech Stack:** .NET 8.0.424, the pinned formal CP6.Platform.Release 0.10.1 package, xUnit, existing bounded GitHub source.

---

## Scope and decisions

- Continue the existing clean `codex/p10-s06-platform-candidate` worktree; do not touch the root workspace.
- Parent design sections 12, 13, 16.3 and 17 require a completed validation workflow before publication starts. Candidate time cannot stand in for publisher start.
- The already approved protected Environment remains `p10-platform-candidate`. This module fixes job names `validate` and `publish` for their future YAML; it adds no Environment, approval, authority or secret.
- S04 selection is the actual immutable `34126521193/1` workflow at Platform `3ff27e...`, with `sign-publish` and `verify-linux`.
- The GitHub API measures job completion in whole seconds. An exact millisecond CP6 event within that final reported second belongs to the reported interval; the following second does not. CP6-to-CP6 ordering remains exact.
- The pure tests use unsigned selected-field vectors, not mock formal acceptance. The S04 profile also gets a real authenticated GitHub read test. Existing native signature and transport gates are not replaced.
- No S06 workflow execution, OCI/R2 write, remote push, candidate acceptance or P10 completion is claimed by this module.

## Files

- Create: `tools/p10/ReleaseVerifier/S06WorkflowProfile.cs`
- Create: `tools/p10/ReleaseVerifier/S06EvidenceChronology.cs`
- Create: `tools/p10/ReleaseVerifier.Tests/S06EvidenceChronologyTests.cs`
- Create: `docs/superpowers/plans/2026-09-08-p10-s06-evidence-chronology.md`.

### Task 1: Test the fixed profiles and temporal boundary

- [x] Write the following test file, add only throwing API scaffolds for compilation, then run the focused command in Task 3.
- [x] Confirm every new test fails with `NotImplementedException`, with no unexpected compilation failure or skip.

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Temporal selected-field vectors are intentionally unsigned. No test below claims S06 acceptance.
// The S04 publication bytes and live S04 workflow check are actual historical evidence.
public sealed class S06EvidenceChronologyTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-08T00:00:00Z", CultureInfo.InvariantCulture);
    private static GitHubWorkflowIdentity Producer => new("GTX537/CP6", S06ReleaseIdentity.ValidationPath,
        new string('5', 40), 999991, 1, new string('c', 40));
    private static GitHubWorkflowIdentity Publisher => new("GTX537/CP6", S06ReleaseIdentity.PublicationPath,
        new string('4', 40), 999992, 1, Producer.CommitSha);
    private static string Digest => "sha256:" + new string('a', 64);

    [Fact]
    public void Ordered_temporal_vectors_pass_only_the_pure_partial_order_check()
    {
        var vector = Vector();
        S06EvidenceChronology.RequireValidation(Element(vector.Root), Element(vector.Gate), vector.Evidence,
            vector.Validation, vector.Formal);
        S06EvidenceChronology.RequirePublication(Element(vector.Root), vector.Validation, Publisher,
            Start.AddMinutes(11), Start.AddMinutes(13));
    }

    [Fact]
    public async Task The_fixed_S04_profile_matches_the_actual_completed_run_jobs_and_workflow_file()
    {
        var observed = await GitHubEvidenceSource.ReadWorkflowAsync(S06WorkflowProfile.FormalPublication,
            S06WorkflowProfile.FormalJobs, Start, Token());
        Assert.Equal(2, observed.Jobs.Count);
        Assert.Equal(new[] { "sign-publish", "verify-linux" }, observed.Jobs.Select(j => j.Name));
        Assert.True(observed.CompletedAtUtc < Start);
        Assert.Equal(S06ReleaseIdentity.Source, observed.Workflow.CommitSha);
    }

    [Theory]
    [InlineData("repository")]
    [InlineData("path")]
    [InlineData("blob")]
    [InlineData("run")]
    [InlineData("attempt")]
    [InlineData("source")]
    [InlineData("environment")]
    [InlineData("extra")]
    [InlineData("unknown-profile")]
    public void The_public_workflow_profile_has_no_environment_or_identity_fallback(string mutation)
    {
        var node = JsonNode.Parse(S06InToto.Workflow(Producer).GetRawText())!;
        if (mutation == "repository") node["repository"] = "GTX537/CP6.Platform";
        if (mutation == "path") node["workflowPath"] = S06ReleaseIdentity.PublicationPath;
        if (mutation == "blob") node["workflowFileSha"] = "main";
        if (mutation == "run") node["runId"] = 0;
        if (mutation == "attempt") node["runAttempt"] = 0;
        if (mutation == "source") node["commitSha"] = "main";
        if (mutation == "environment") node["environment"] = "none";
        if (mutation == "extra") node["conclusion"] = "success";
        Assert.Throws<Cp6ReleaseContractException>(() => S06WorkflowProfile.Read(Element(node),
            mutation == "unknown-profile" ? ".github/workflows/other.yml" : S06ReleaseIdentity.ValidationPath));
    }

    [Theory]
    [InlineData("formal-after-validation")]
    [InlineData("formal-identity")]
    [InlineData("formal-job")]
    [InlineData("formal-time")]
    [InlineData("validation-identity")]
    [InlineData("validation-job")]
    [InlineData("validation-event")]
    [InlineData("validation-observed-before-end")]
    [InlineData("gate-before-job")]
    [InlineData("gate-after-job")]
    [InlineData("record-before-job")]
    [InlineData("record-after-gate")]
    [InlineData("statement-before-job")]
    [InlineData("statement-after-record")]
    [InlineData("retrieval-before-job")]
    [InlineData("retrieval-after-statement")]
    [InlineData("build-before-job")]
    [InlineData("build-reversed")]
    [InlineData("signature-before-build-end")]
    [InlineData("signature-after-wrapper")]
    public void A_detached_or_reordered_evidence_time_cannot_pass(string mutation)
    {
        var vector = Vector();
        var validation = vector.Validation;
        var formal = vector.Formal;
        if (mutation == "formal-after-validation") formal = formal with { CompletedAtUtc = Start.AddSeconds(1) };
        if (mutation == "formal-identity") formal = formal with { Workflow = formal.Workflow with { RunId = 1 } };
        if (mutation == "formal-job") formal = formal with { Jobs = [formal.Jobs[0]] };
        if (mutation == "formal-time") formal = formal with
        {
            Jobs = formal.Jobs.Select(j => j with { CompletedAtUtc = j.StartedAtUtc }).ToArray()
        };
        if (mutation == "validation-identity") validation = validation with { Workflow = Producer with { RunId = 1 } };
        if (mutation == "validation-job") validation = validation with { Jobs = [validation.Jobs[0] with { Name = "publish" }] };
        if (mutation == "validation-event") validation = validation with { Event = "pull_request" };
        if (mutation == "validation-observed-before-end") validation = validation with { ObservedAtUtc = Start };
        if (mutation == "gate-before-job") vector.Gate["createdAtUtc"] = Time(-1);
        if (mutation == "gate-after-job") vector.Gate["createdAtUtc"] = Time(11);
        if (mutation == "record-before-job") RecordTime(vector, "SourceReference", -1);
        if (mutation == "record-after-gate") RecordTime(vector, "SourceReference", 9);
        if (mutation == "statement-before-job") ChangeStatement(vector, "SourceReference", n => n["predicate"]!["createdAtUtc"] = Time(-1));
        if (mutation == "statement-after-record") ChangeStatement(vector, "SourceReference", n => n["predicate"]!["createdAtUtc"] = Time(9));
        if (mutation == "retrieval-before-job") ChangeStatement(vector, "FormalPackageVerification",
            n => n["predicate"]!["details"]!["packages"]![0]!["retrievedAtUtc"] = Time(-1));
        if (mutation == "retrieval-after-statement") ChangeStatement(vector, "FormalPackageVerification",
            n => n["predicate"]!["details"]!["packages"]![0]!["retrievedAtUtc"] = Time(7));
        if (mutation == "build-before-job") ChangeStatement(vector, "ImageProvenance",
            n => n["predicate"]!["details"]!["buildStartedAtUtc"] = Time(-1));
        if (mutation == "build-reversed") ChangeStatement(vector, "ImageProvenance",
            n => n["predicate"]!["details"]!["buildStartedAtUtc"] = Time(5));
        if (mutation == "signature-before-build-end") ChangeStatement(vector, "OciSignature",
            n => n["predicate"]!["details"]!["signedAtUtc"] = Time(3));
        if (mutation == "signature-after-wrapper") ChangeStatement(vector, "OciSignature",
            n => n["predicate"]!["details"]!["signedAtUtc"] = Time(7));
        var error = Assert.Throws<Cp6ReleaseContractException>(() => S06EvidenceChronology.RequireValidation(
            Element(vector.Root), Element(vector.Gate), vector.Evidence, validation, formal));
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void A_subsecond_event_in_GitHubs_reported_final_second_is_inside_the_job_interval()
    {
        var vector = Vector();
        vector.Gate["createdAtUtc"] = S06InToto.FormatTime(Start.AddMinutes(10).AddMilliseconds(999));
        S06EvidenceChronology.RequireValidation(Element(vector.Root), Element(vector.Gate), vector.Evidence,
            vector.Validation, vector.Formal);
        vector.Gate["createdAtUtc"] = S06InToto.FormatTime(Start.AddMinutes(10).AddSeconds(1));
        Assert.Throws<Cp6ReleaseContractException>(() => S06EvidenceChronology.RequireValidation(
            Element(vector.Root), Element(vector.Gate), vector.Evidence, vector.Validation, vector.Formal));
    }

    [Theory]
    [InlineData("publisher-identity")]
    [InlineData("source")]
    [InlineData("same-run")]
    [InlineData("started-before-validation-end")]
    [InlineData("created-before-publisher")]
    [InlineData("created-after-observation")]
    [InlineData("offset")]
    public void Publication_needs_a_separate_later_run_not_a_predicted_conclusion(string mutation)
    {
        var vector = Vector();
        var publisher = Publisher;
        var started = Start.AddMinutes(11);
        var end = Start.AddMinutes(13);
        if (mutation == "publisher-identity") publisher = publisher with { RunId = 1 };
        if (mutation == "source")
        {
            publisher = publisher with { CommitSha = new string('d', 40) };
            vector.Root["publisher"]!["commitSha"] = publisher.CommitSha;
        }
        if (mutation == "same-run")
        {
            publisher = publisher with { RunId = Producer.RunId };
            vector.Root["publisher"]!["runId"] = publisher.RunId;
        }
        if (mutation == "started-before-validation-end") started = Start.AddMinutes(9);
        if (mutation == "created-before-publisher") vector.Root["createdAtUtc"] = Time(10);
        if (mutation == "created-after-observation") end = Start.AddMinutes(11);
        if (mutation == "offset") started = started.ToOffset(TimeSpan.FromHours(1));
        Assert.Throws<Cp6ReleaseContractException>(() => S06EvidenceChronology.RequirePublication(
            Element(vector.Root), vector.Validation, publisher, started, end));
    }

    private static TemporalVector Vector()
    {
        var root = new JsonObject
        {
            ["verifier"] = JsonNode.Parse(S06InToto.Workflow(Producer).GetRawText()),
            ["publisher"] = JsonNode.Parse(S06InToto.Workflow(Publisher).GetRawText()),
            ["createdAtUtc"] = Time(12),
            ["images"] = new JsonArray(new JsonObject { ["sha256OrDigest"] = Digest })
        };
        var gate = new JsonObject { ["createdAtUtc"] = Time(8) };
        var evidence = new Dictionary<string, InspectedEvidence>(StringComparer.Ordinal);
        Add(evidence, "FormalPackagePublication", EvidenceGraphBaseline.Publication());
        foreach (var kind in new[] { "SourceReference", "CrmConsumer", "FormalPackageVerification", "ImageProvenance", "OciSignature" })
        {
            var details = kind switch
            {
                "FormalPackageVerification" => JsonSerializer.SerializeToElement(new { packages = new[] { new { retrievedAtUtc = Time(2) } } }),
                "ImageProvenance" => JsonSerializer.SerializeToElement(new { buildStartedAtUtc = Time(1), buildCompletedAtUtc = Time(4) }),
                "OciSignature" => JsonSerializer.SerializeToElement(new { signedAtUtc = Time(5) }),
                _ => JsonSerializer.SerializeToElement(new { observation = "unsigned-temporal-vector-only" })
            };
            Add(evidence, kind, S06InToto.Create(kind, Producer, Start.AddMinutes(6), details,
                kind is "ImageProvenance" or "OciSignature" ? Digest : null));
        }
        var validation = new GitHubWorkflowObservation(Producer, "workflow_dispatch", Start, Start.AddMinutes(10),
            new(new string('a', 64), 123), [new(1, "validate", Start, Start.AddMinutes(10))], Start.AddMinutes(11));
        var formalStart = Start.AddDays(-1).AddHours(13);
        var formal = new GitHubWorkflowObservation(S06WorkflowProfile.FormalPublication, "workflow_dispatch",
            formalStart, formalStart.AddMinutes(30), new(new string('a', 64), 123),
            [new(2, "sign-publish", formalStart, formalStart.AddMinutes(20)),
                new(3, "verify-linux", formalStart.AddMinutes(21), formalStart.AddMinutes(25))], Start);
        return new(root, gate, evidence, validation, formal);
    }

    private static void Add(Dictionary<string, InspectedEvidence> evidence, string kind, byte[] bytes) =>
        evidence.Add(kind, new(JsonSerializer.SerializeToElement(new { createdAtUtc = Time(8) }),
            ContentAddress.Create(bytes, S06EvidenceBindings.MediaTypes[kind], "temporal-vector.json"), bytes));
    private static void RecordTime(TemporalVector vector, string kind, int minute)
    {
        var old = vector.Evidence[kind];
        vector.Evidence[kind] = new(JsonSerializer.SerializeToElement(new { createdAtUtc = Time(minute) }), old.Payload, old.CopyPayloadBytes());
    }
    private static void ChangeStatement(TemporalVector vector, string kind, Action<JsonNode> mutate)
    {
        var old = vector.Evidence[kind];
        var node = JsonNode.Parse(old.CopyPayloadBytes())!;
        mutate(node);
        var bytes = Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(node));
        vector.Evidence[kind] = new(old.Record, ContentAddress.Create(bytes, old.Payload.MediaType, "temporal-vector.json"), bytes);
    }
    private static string Time(int minute) => S06InToto.FormatTime(Start.AddMinutes(minute));
    private static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);
    private static string Token() => Environment.GetEnvironmentVariable("P10_GITHUB_READ_TOKEN") ??
        throw new InvalidOperationException("Actual S04 GitHub read authorization is required; no skip or fabricated workflow proof.");
    private sealed record TemporalVector(JsonObject Root, JsonObject Gate, Dictionary<string, InspectedEvidence> Evidence,
        GitHubWorkflowObservation Validation, GitHubWorkflowObservation Formal);
}
```

### Task 2: Implement the fixed profile and chronology checks

- [x] Replace the throwing scaffolds with the following code only after recording RED.

#### tools/p10/ReleaseVerifier/S06WorkflowProfile.cs

```csharp
using System.Text.Json;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Fixed job profiles shared by live reads and temporal checks. No workflow success is inferred here.
internal static class S06WorkflowProfile
{
    internal static GitHubWorkflowIdentity FormalPublication => new("GTX537/CP6.Platform",
        ".github/workflows/p10-formal-packages.yml", "01c94e11213231ef6f73d8f9a9cccd9f1563e185",
        34126521193, 1, S06ReleaseIdentity.Source);
    internal static IReadOnlyList<string> FormalJobs { get; } = Array.AsReadOnly(new[] { "sign-publish", "verify-linux" });
    internal static IReadOnlyList<string> ValidationJobs { get; } = Array.AsReadOnly(new[] { "validate" });
    internal static IReadOnlyList<string> PublicationJobs { get; } = Array.AsReadOnly(new[] { "publish" });

    internal static GitHubWorkflowIdentity Read(JsonElement value, string expectedPath)
    {
        try
        {
            Require(expectedPath is S06ReleaseIdentity.ValidationPath or S06ReleaseIdentity.PublicationPath,
                "s06-workflow-profile");
            Exact(value, "repository", "workflowPath", "workflowFileSha", "runId", "runAttempt", "commitSha", "environment");
            var result = new GitHubWorkflowIdentity(Text(value, "repository"), Text(value, "workflowPath"),
                Text(value, "workflowFileSha"), value.GetProperty("runId").GetInt64(),
                value.GetProperty("runAttempt").GetInt64(), Text(value, "commitSha"));
            result.RequireValid();
            Require(result.Repository == "GTX537/CP6" && result.WorkflowPath == expectedPath &&
                Text(value, "environment") == OciSignatureProfile.EnvironmentName, "s06-workflow-profile");
            return result;
        }
        catch (CP6.Platform.Release.Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("s06-workflow-profile"); }
    }
}
```

#### tools/p10/ReleaseVerifier/S06EvidenceChronology.cs

```csharp
using System.Text.Json;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Pure temporal binding only. Callers obtain observations through the fixed live GitHub source.
// Passing selected-field vectors here is never evidence authentication or candidate acceptance.
internal static class S06EvidenceChronology
{
    private static readonly string[] StatementKinds =
        ["SourceReference", "FormalPackageVerification", "CrmConsumer", "ImageProvenance", "OciSignature"];

    internal static void RequireValidation(JsonElement root, JsonElement gate,
        IReadOnlyDictionary<string, InspectedEvidence> evidence, GitHubWorkflowObservation validation,
        GitHubWorkflowObservation formal)
    {
        try
        {
            var producer = S06WorkflowProfile.Read(root.GetProperty("verifier"), S06ReleaseIdentity.ValidationPath);
            RequireRun(validation, producer, S06WorkflowProfile.ValidationJobs);
            RequireRun(formal, S06WorkflowProfile.FormalPublication, S06WorkflowProfile.FormalJobs);
            Require(formal.CompletedAtUtc <= validation.StartedAtUtc, "s06-validation-order");
            var job = validation.Jobs.Single();
            var gateTime = Time(gate, "createdAtUtc");
            InJob(gateTime, job);
            foreach (var item in evidence.Values)
            {
                var recorded = Time(item.Record, "createdAtUtc");
                InJob(recorded, job);
                Require(recorded <= gateTime, "s06-evidence-order");
            }
            var original = GitHubApiJson.Parse(evidence["FormalPackagePublication"].CopyPayloadBytes());
            var originalTime = Time(original, "createdAtUtc");
            Require(formal.Jobs.Any(j => Contains(j, originalTime)), "s06-formal-time");
            var digest = Text(root.GetProperty("images")[0], "sha256OrDigest");
            var statements = new Dictionary<string, S06Attestation>(StringComparer.Ordinal);
            foreach (var kind in StatementKinds)
            {
                var item = evidence[kind];
                var statement = S06InToto.Read(item.CopyPayloadBytes(), kind, producer, Time(item.Record, "createdAtUtc"),
                    kind is "ImageProvenance" or "OciSignature" ? digest : null);
                InJob(statement.CreatedAtUtc, job);
                statements.Add(kind, statement);
            }
            var packages = statements["FormalPackageVerification"];
            foreach (var package in packages.Details.GetProperty("packages").EnumerateArray())
            {
                var retrieved = Time(package, "retrievedAtUtc");
                InJob(retrieved, job);
                Require(retrieved <= packages.CreatedAtUtc, "s06-evidence-order");
            }
            var image = statements["ImageProvenance"];
            var start = Time(image.Details, "buildStartedAtUtc");
            var end = Time(image.Details, "buildCompletedAtUtc");
            var signature = statements["OciSignature"];
            var signed = Time(signature.Details, "signedAtUtc");
            InJob(start, job);
            InJob(end, job);
            InJob(signed, job);
            Require(start <= end && end <= image.CreatedAtUtc && end <= signed &&
                signed <= signature.CreatedAtUtc, "s06-image-order");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("s06-chronology-shape"); }
    }

    // windowEnd is a completed publisher's observed end, or a live current-run context's actual observation time.
    // There is no caller-selectable "publisher succeeded" flag and no authentication result from this pure check.
    internal static void RequirePublication(JsonElement candidate, GitHubWorkflowObservation validation,
        GitHubWorkflowIdentity publisher, DateTimeOffset publisherStarted, DateTimeOffset windowEnd)
    {
        try
        {
            var expected = S06WorkflowProfile.Read(candidate.GetProperty("publisher"), S06ReleaseIdentity.PublicationPath);
            var producer = S06WorkflowProfile.Read(candidate.GetProperty("verifier"), S06ReleaseIdentity.ValidationPath);
            RequireRun(validation, producer, S06WorkflowProfile.ValidationJobs);
            GitHubEvidenceChecks.RequireCutoff(publisherStarted);
            GitHubEvidenceChecks.RequireCutoff(windowEnd);
            var created = Time(candidate, "createdAtUtc");
            Require(publisher == expected && publisher.CommitSha == producer.CommitSha &&
                publisher.RunId != producer.RunId, "s06-publication-identity");
            Require(validation.CompletedAtUtc <= publisherStarted && publisherStarted <= created &&
                created <= windowEnd, "s06-publication-order");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("s06-chronology-shape"); }
    }

    private static void RequireRun(GitHubWorkflowObservation run, GitHubWorkflowIdentity expected,
        IReadOnlyCollection<string> names)
    {
        Require(run.Workflow == expected && run.Event == "workflow_dispatch" &&
            run.StartedAtUtc <= run.CompletedAtUtc && run.CompletedAtUtc <= run.ObservedAtUtc &&
            run.Jobs.Select(j => j.Name).Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal),
                StringComparer.Ordinal), "s06-workflow-observation");
        foreach (var job in run.Jobs)
            Require(job.Id > 0 && run.StartedAtUtc <= job.StartedAtUtc && job.StartedAtUtc <= job.CompletedAtUtc &&
                job.CompletedAtUtc <= run.CompletedAtUtc, "s06-workflow-observation");
    }

    // GitHub job endpoints expose second-resolution timestamps; retain that measurement interval.
    private static bool Contains(GitHubJobObservation job, DateTimeOffset time) =>
        job.StartedAtUtc <= time && time.ToUnixTimeSeconds() <= job.CompletedAtUtc.ToUnixTimeSeconds();

    private static void InJob(DateTimeOffset time, GitHubJobObservation job) =>
        Require(Contains(job, time), "s06-evidence-window");
}
```

### Task 3: Verify and save the module

- [x] Run focused tests, the full existing test suite, and formatting from `tools/p10`. Keep credentials in process environment only; do not print or write them.
- [x] Confirm all tests pass with zero skips. Record any failure honestly and diagnose before changing code.
- [x] Compare plan code blocks with the exact implementation, inspect the complete four-file diff, and check for secret or scope residue.
- [ ] Stage only the four named files and commit `feat(p10): bind evidence to completed workflow time windows`.
- [ ] Continue with real proof orchestration and the S06 workflows; this module alone does not satisfy S06.

```powershell
$env:DOTNET_ROOT = 'C:/Users/tt/.dotnet'
$env:DOTNET_HOST_PATH = 'C:/Users/tt/.dotnet/dotnet.exe'
$env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH
$env:P10_COSIGN_PATH = 'C:/Users/tt/AppData/Local/Temp/cp6-p10-cosign-c4ab1329239f4721a3f8f5ac741ce3c9/cosign-windows-amd64.exe'
$env:P10_FORMAL_PACKAGE_ROOT = 'D:/CP6.Platform-worktrees/p10-formal-schema-parity/artifacts/p10-0.10.1-publication/windows/feed-readback-packages'
$env:P10_GITHUB_READ_TOKEN = gh auth token
$env:P10_FEED_READ_TOKEN = $env:P10_GITHUB_READ_TOKEN
try {
  & $env:DOTNET_HOST_PATH test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~S06EvidenceChronologyTests
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
git add -- docs/superpowers/plans/2026-09-08-p10-s06-evidence-chronology.md tools/p10/ReleaseVerifier/S06WorkflowProfile.cs tools/p10/ReleaseVerifier/S06EvidenceChronology.cs tools/p10/ReleaseVerifier.Tests/S06EvidenceChronologyTests.cs
git diff --cached --check
git diff --cached --stat
git commit -m "feat(p10): bind evidence to completed workflow time windows"
```

## Self-review

- [x] Covers parent temporal-separation and evidence-binding requirements without adding candidate acceptance to a pure helper.
- [x] Exact signatures, code, commands and file scope are provided.
- [x] No production deployment, registry migration, signature bypass, replacement trust root or current-publisher conclusion is introduced.

## Verification outcome (2026-09-08)

- Throwing scaffolds: 39 failed, 0 passed, 0 skipped; expected `NotImplementedException` failures after successful compilation.
- Implemented focused suite: 39 passed, 0 failed, 0 skipped, including the real S04 API/file/jobs read.
- Full Release suite: 1,250 passed, 0 failed, 0 skipped in 47 seconds.
- `dotnet format --verify-no-changes --no-restore`: exit 0.
- Only the three new code/test files and this plan are in scope. No S06 remote execution or publication occurred.
