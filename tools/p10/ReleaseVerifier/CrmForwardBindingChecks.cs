using System.Text.Json;
using static CP6.P10.ReleaseVerifier.GitHubEvidenceChecks;

namespace CP6.P10.ReleaseVerifier;

internal static class CrmForwardBindingChecks
{
    internal static DateTimeOffset MergedPullRequest(JsonElement value, CrmPullRequestSelection selected, DateTimeOffset cutoff)
    {
        RequireCutoff(cutoff);
        Require(Number(value, "number") == selected.Number && Text(value, "state") == "closed" &&
            Property(value, "merged").ValueKind == JsonValueKind.True &&
            Text(value, "merge_commit_sha") == selected.MainWorkflow.CommitSha, "github-crm-merge");
        var head = Property(value, "head");
        var source = Property(value, "base");
        Require(Text(head, "sha") == selected.PullRequestWorkflow.CommitSha && Text(head, "ref") == selected.HeadBranch &&
            Text(Property(head, "repo"), "full_name") == CrmPullRequestSelection.Repository &&
            Text(source, "sha") == selected.BaseSha && Text(source, "ref") == "main" &&
            Text(Property(source, "repo"), "full_name") == CrmPullRequestSelection.Repository, "github-crm-merge");
        var merged = Time(value, "merged_at");
        Require(merged <= cutoff, "github-proof-time");
        return merged;
    }

    internal static void ForwardOrder(DateTimeOffset pullRequestCompleted, DateTimeOffset merged,
        DateTimeOffset mainStarted, DateTimeOffset mainCompleted, DateTimeOffset cutoff)
    {
        RequireCutoff(cutoff);
        RequireCutoff(pullRequestCompleted);
        RequireCutoff(merged);
        RequireCutoff(mainStarted);
        RequireCutoff(mainCompleted);
        Require(pullRequestCompleted <= merged && merged <= mainStarted &&
            mainStarted <= mainCompleted && mainCompleted <= cutoff, "github-proof-time");
    }
}
