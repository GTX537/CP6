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
