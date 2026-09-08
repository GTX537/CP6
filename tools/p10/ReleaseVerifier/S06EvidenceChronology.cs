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
