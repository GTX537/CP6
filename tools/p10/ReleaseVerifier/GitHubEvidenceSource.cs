using static CP6.P10.ReleaseVerifier.GitHubEvidenceChecks;

namespace CP6.P10.ReleaseVerifier;

// Live observations, not a serialized candidate acceptance capability. No network success/clock/policy injection.
internal static class GitHubEvidenceSource
{
    internal static async Task<GitHubSourceObservation> ReadSourceAsync(string repository, string source, string token,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = GitHubReadTarget.MainBranch(repository);
        GitHubReadTarget.RequireSha(source);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        using var client = new GitHubReadClient(token);
        var main = MainBranch(await client.ReadAsync(target, deadline.Token));
        var comparison = await client.ReadAsync(GitHubReadTarget.Compare(repository, source, main.Sha), deadline.Token);
        Reachability(repository, source, main, comparison);
        return new(repository, source, main.Sha, main.Protected, DateTimeOffset.UtcNow);
    }

    internal static async Task<GitHubWorkflowObservation> ReadWorkflowAsync(GitHubWorkflowIdentity expected,
        IReadOnlyCollection<string> requiredJobs, DateTimeOffset completedBeforeUtc, string token,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        expected.RequireValid();
        RequireCutoff(completedBeforeUtc);
        Require(completedBeforeUtc <= DateTimeOffset.UtcNow, "github-proof-time");
        // Copy the caller's job profile before asynchronous work.
        var jobNames = requiredJobs.ToArray();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        using var client = new GitHubReadClient(token);
        var run = await client.ReadAsync(GitHubReadTarget.Run(expected.Repository, expected.RunId, expected.RunAttempt), deadline.Token);
        var firstAttempt = await GitHubRunChronology.ReadFirstAttemptAsync(client, expected, deadline.Token);
        var timing = GitHubWorkflowChecks.CompletedRun(run, expected, completedBeforeUtc, firstAttempt);
        var file = GitHubWorkflowChecks.File(await client.ReadAsync(
            GitHubReadTarget.Workflow(expected.Repository, expected.WorkflowPath, expected.CommitSha), deadline.Token), expected);
        var jobs = GitHubWorkflowChecks.Jobs(await client.ReadAsync(
            GitHubReadTarget.Jobs(expected.Repository, expected.RunId, expected.RunAttempt), deadline.Token),
            expected, jobNames, timing.Started, timing.Completed);
        return new(expected, timing.Event, timing.Started, timing.Completed, file, jobs, DateTimeOffset.UtcNow);
    }
}
