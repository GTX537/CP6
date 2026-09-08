using System.Runtime.InteropServices;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// An observed running job, never a completed-success publisher. This object is never deserialized from an artifact.
// A normal candidate consumer cannot select this path or supply a "currently publishing" boolean.
internal sealed class S06CurrentWorkflow
{
    private S06CurrentWorkflow(GitHubWorkflowIdentity workflow, S06CurrentTiming timing,
        GitHubWorkflowFileObservation file, DateTimeOffset observedAtUtc)
    {
        Workflow = workflow;
        RunStartedAtUtc = timing.RunStartedAtUtc;
        JobStartedAtUtc = timing.JobStartedAtUtc;
        File = file;
        ObservedAtUtc = observedAtUtc;
    }

    internal GitHubWorkflowIdentity Workflow { get; }
    internal DateTimeOffset RunStartedAtUtc { get; }
    internal DateTimeOffset JobStartedAtUtc { get; }
    internal GitHubWorkflowFileObservation File { get; }
    internal DateTimeOffset ObservedAtUtc { get; }

    internal static async Task<S06CurrentWorkflow> CaptureAsync(string workflowPath, string githubReadToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = S06CurrentWorkflowChecks.JobName(workflowPath);
        var values = S06CurrentWorkflowChecks.EnvironmentNames.ToDictionary(n => n, Environment.GetEnvironmentVariable,
            StringComparer.Ordinal);
        var selected = S06CurrentWorkflowChecks.ReadEnvironment(values, workflowPath);
        GitHubEvidenceChecks.Require(OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "s06-current-host");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            using var client = new GitHubReadClient(githubReadToken);
            var contents = await client.ReadAsync(GitHubReadTarget.Workflow("GTX537/CP6",
                selected.WorkflowPath, selected.CommitSha), deadline.Token);
            var workflow = new GitHubWorkflowIdentity("GTX537/CP6", selected.WorkflowPath,
                GitHubEvidenceChecks.Text(contents, "sha"), selected.RunId, selected.RunAttempt, selected.CommitSha);
            var file = GitHubWorkflowChecks.File(contents, workflow);
            _ = await GitHubEvidenceSource.ReadSourceAsync("GTX537/CP6", workflow.CommitSha, githubReadToken, deadline.Token);
            var run = await client.ReadAsync(GitHubReadTarget.Run("GTX537/CP6", workflow.RunId, workflow.RunAttempt), deadline.Token);
            var firstAttempt = await GitHubRunChronology.ReadFirstAttemptAsync(client, workflow, deadline.Token);
            var jobs = await client.ReadAsync(GitHubReadTarget.Jobs("GTX537/CP6", workflow.RunId, workflow.RunAttempt), deadline.Token);
            var observed = DateTimeOffset.UtcNow;
            var timing = S06CurrentWorkflowChecks.ReadTiming(run, jobs, workflow, observed, firstAttempt);
            return new(workflow, timing, file, observed);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw GitHubWirePolicy.Error("s06-current-timeout");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw GitHubWirePolicy.Error("s06-current-read");
        }
    }
}
