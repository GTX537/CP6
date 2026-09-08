using System.Text.Json;
using static CP6.P10.ReleaseVerifier.GitHubEvidenceChecks;

namespace CP6.P10.ReleaseVerifier;

// An attempt's record can be created after its execution starts. Bind reruns to
// the original completed attempt instead of inventing clock-skew tolerances.
internal static class GitHubRunChronology
{
    internal static async Task<JsonElement?> ReadFirstAttemptAsync(GitHubReadClient client,
        GitHubWorkflowIdentity expected, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        expected.RequireValid();
        return expected.RunAttempt == 1 ? null : await client.ReadAsync(
            GitHubReadTarget.Run(expected.Repository, expected.RunId, 1), cancellationToken);
    }

    internal static (DateTimeOffset Started, DateTimeOffset Updated) Read(JsonElement run,
        GitHubWorkflowIdentity expected, DateTimeOffset cutoff, JsonElement? firstAttempt, string errorCode)
    {
        var created = Time(run, "created_at");
        var started = Time(run, "run_started_at");
        var updated = Time(run, "updated_at");
        Require(created <= updated && started <= updated && updated <= cutoff, errorCode);
        if (expected.RunAttempt == 1)
        {
            Require(firstAttempt is null && created <= started, errorCode);
            return (started, updated);
        }

        Require(firstAttempt.HasValue, errorCode);
        var original = firstAttempt!.Value;
        Require(Number(original, "id") == expected.RunId && Number(original, "run_attempt") == 1 &&
            Text(original, "head_sha") == expected.CommitSha && Text(original, "path") == expected.WorkflowPath &&
            Text(original, "head_branch") == Text(run, "head_branch") && Text(original, "event") == Text(run, "event") &&
            Text(Property(original, "repository"), "full_name") == expected.Repository &&
            Text(Property(original, "head_repository"), "full_name") == expected.Repository &&
            Text(original, "status") == "completed" && Text(original, "conclusion").Length > 0, errorCode);
        var originalCreated = Time(original, "created_at");
        var originalStarted = Time(original, "run_started_at");
        var originalEnded = Time(original, "updated_at");
        Require(originalCreated <= originalStarted && originalStarted <= originalEnded && originalEnded <= started &&
            originalCreated <= created, errorCode);
        return (started, updated);
    }
}
