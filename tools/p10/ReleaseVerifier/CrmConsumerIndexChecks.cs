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
