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
        => CompletedRunCore(run, expected, cutoff, "main",
            expected.Repository == "GTX537/CP6.CRM" ? "push" : "workflow_dispatch");

    internal static (DateTimeOffset Started, DateTimeOffset Completed, string Event) CompletedPullRequestRun(
        JsonElement run, CrmPullRequestSelection selected, DateTimeOffset cutoff) =>
        CompletedRunCore(run, selected.PullRequestWorkflow, cutoff, selected.HeadBranch, "pull_request");

    private static (DateTimeOffset Started, DateTimeOffset Completed, string Event) CompletedRunCore(
        JsonElement run, GitHubWorkflowIdentity expected, DateTimeOffset cutoff, string branch, string expectedEvent)
    {
        expected.RequireValid();
        RequireCutoff(cutoff);
        Require(Number(run, "id") == expected.RunId && Number(run, "run_attempt") == expected.RunAttempt &&
            Text(run, "head_sha") == expected.CommitSha && Text(run, "head_branch") == branch &&
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
        IReadOnlyCollection<string> requiredNames, DateTimeOffset runStarted, DateTimeOffset runCompleted) =>
        JobsCore(jobs, expected, requiredNames, runStarted, runCompleted, "main");

    internal static IReadOnlyList<GitHubJobObservation> PullRequestJobs(JsonElement jobs, CrmPullRequestSelection selected,
        DateTimeOffset runStarted, DateTimeOffset runCompleted) =>
        JobsCore(jobs, selected.PullRequestWorkflow, CrmPullRequestSelection.RequiredJobs, runStarted, runCompleted, selected.HeadBranch);

    private static IReadOnlyList<GitHubJobObservation> JobsCore(JsonElement jobs, GitHubWorkflowIdentity expected,
        IReadOnlyCollection<string> requiredNames, DateTimeOffset runStarted, DateTimeOffset runCompleted, string branch)
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
                Text(job, "head_sha") == expected.CommitSha && Text(job, "head_branch") == branch, "github-job-identity");
            Require(Text(job, "status") == "completed" && Text(job, "conclusion") == "success", "github-job-conclusion");
            var started = Time(job, "started_at");
            var completed = Time(job, "completed_at");
            Require(runStarted <= started && started <= completed && completed <= runCompleted, "github-proof-time");
            result.Add(new(id, name, started, completed));
        }
        return Array.AsReadOnly(result.OrderBy(j => j.Name, StringComparer.Ordinal).ToArray());
    }
}
