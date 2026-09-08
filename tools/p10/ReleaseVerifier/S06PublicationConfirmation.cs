using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// A clean pre/post-commit confirmation inside the actual running publisher. Never normal-consumer acceptance.
internal sealed class S06PublicationConfirmation
{
    private S06PublicationConfirmation(FetchedCandidateGraph fetched, S06CurrentWorkflow current,
        GitHubWorkflowObservation validation)
    {
        ReleaseTag = fetched.Locator.ReleaseTag;
        CandidateSha256 = fetched.Graph.Sha256;
        Publication = current;
        Validation = validation;
    }

    internal string ReleaseTag { get; }
    internal string CandidateSha256 { get; }
    internal S06CurrentWorkflow Publication { get; }
    internal GitHubWorkflowObservation Validation { get; }
    internal bool PublicationWorkflowCompleted => false;

    internal static async Task<S06PublicationConfirmation> VerifyAsync(FetchedCandidateGraph fetched,
        CosignBlobVerifier cosign, string githubReadToken, string feedReadToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Capture fresh live context here; it cannot be supplied as serialized metadata or a success flag.
        var current = await S06CurrentWorkflow.CaptureAsync(S06ReleaseIdentity.PublicationPath, githubReadToken, cancellationToken);
        var validation = await S06CandidateEvidence.VerifyAsync(fetched.Graph, current.RunStartedAtUtc,
            cosign, githubReadToken, feedReadToken, cancellationToken);
        S06EvidenceChronology.RequirePublication(fetched.Graph.Candidate, validation, current.Workflow,
            current.RunStartedAtUtc, current.ObservedAtUtc);
        Require(Time(fetched.Graph.Candidate, "createdAtUtc") >= current.JobStartedAtUtc, "s06-publication-job");
        return new(fetched, current, validation);
    }
}
