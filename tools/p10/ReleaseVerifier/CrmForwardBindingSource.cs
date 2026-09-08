using static CP6.P10.ReleaseVerifier.GitHubEvidenceChecks;

namespace CP6.P10.ReleaseVerifier;

// Live selected observations. This is not candidate acceptance and does not rewrite or rerun CRM.
internal static class CrmForwardBindingSource
{
    internal static async Task<CrmForwardBindingObservation> ReadAsync(int number, DateTimeOffset completedBeforeUtc,
        string token, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selected = CrmPullRequestSelection.Get(number);
        RequireCutoff(completedBeforeUtc);
        Require(completedBeforeUtc <= DateTimeOffset.UtcNow, "github-proof-time");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        using var client = new GitHubReadClient(token);
        var merged = CrmForwardBindingChecks.MergedPullRequest(
            await client.ReadAsync(GitHubReadTarget.CrmPullRequest(selected), deadline.Token), selected, completedBeforeUtc);
        var expected = selected.PullRequestWorkflow;
        var run = await client.ReadAsync(GitHubReadTarget.Run(expected.Repository, expected.RunId, expected.RunAttempt), deadline.Token);
        var timing = GitHubWorkflowChecks.CompletedPullRequestRun(run, selected, merged);
        var file = GitHubWorkflowChecks.File(await client.ReadAsync(
            GitHubReadTarget.Workflow(expected.Repository, expected.WorkflowPath, expected.CommitSha), deadline.Token), expected);
        var jobs = GitHubWorkflowChecks.PullRequestJobs(await client.ReadAsync(
            GitHubReadTarget.Jobs(expected.Repository, expected.RunId, expected.RunAttempt), deadline.Token),
            selected, timing.Started, timing.Completed);
        var pullRequest = new GitHubWorkflowObservation(expected, timing.Event, timing.Started, timing.Completed,
            file, jobs, DateTimeOffset.UtcNow);
        var main = await GitHubEvidenceSource.ReadWorkflowAsync(selected.MainWorkflow,
            CrmPullRequestSelection.RequiredJobs, completedBeforeUtc, token, deadline.Token);
        CrmForwardBindingChecks.ForwardOrder(pullRequest.CompletedAtUtc, merged, main.StartedAtUtc, main.CompletedAtUtc, completedBeforeUtc);
        return new(selected, merged, pullRequest, main, DateTimeOffset.UtcNow);
    }
}

internal sealed record CrmForwardBindingObservation(CrmPullRequestSelection Selection, DateTimeOffset MergedAtUtc,
    GitHubWorkflowObservation PullRequest, GitHubWorkflowObservation Main, DateTimeOffset ObservedAtUtc);
