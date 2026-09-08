using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Successful current verification of a non-deployable reference candidate, not an audit-ledger Frozen transition.
public sealed class VerifiedPlatformCandidate
{
    private VerifiedPlatformCandidate(FetchedCandidateGraph fetched, GitHubWorkflowObservation validation,
        GitHubWorkflowObservation publisher)
    {
        ReleaseTag = fetched.Locator.ReleaseTag;
        Sha256 = fetched.Graph.Sha256;
        ValidationRunId = validation.Workflow.RunId;
        PublicationRunId = publisher.Workflow.RunId;
        VerifiedAtUtc = DateTimeOffset.UtcNow;
    }

    public string ReleaseTag { get; }
    public string Sha256 { get; }
    public long ValidationRunId { get; }
    public long PublicationRunId { get; }
    public DateTimeOffset VerifiedAtUtc { get; }
    public string State => "VerifiedNonDeployable";
    public bool CandidateAccepted => true;
    public bool Deployable => false;

    // Always discovers through fixed R2 keys and compiled current Locator trust. No intended/local-root mode.
    public static async Task<VerifiedPlatformCandidate> VerifyAsync(string releaseTag, string readAccessKeyId,
        string readSecret, CosignBlobVerifier cosign, string githubReadToken, string feedReadToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(20));
        try
        {
            var fetched = await PlatformCandidateSource.DiscoverAsync(releaseTag, readAccessKeyId, readSecret, cosign, deadline.Token);
            var root = fetched.Graph.Candidate;
            var identity = S06WorkflowProfile.Read(root.GetProperty("publisher"), S06ReleaseIdentity.PublicationPath);
            var publisher = await GitHubEvidenceSource.ReadWorkflowAsync(identity, S06WorkflowProfile.PublicationJobs,
                DateTimeOffset.UtcNow, githubReadToken, deadline.Token);
            var validation = await S06CandidateEvidence.VerifyAsync(fetched.Graph, publisher.StartedAtUtc,
                cosign, githubReadToken, feedReadToken, deadline.Token);
            S06EvidenceChronology.RequirePublication(root, validation, publisher.Workflow,
                publisher.StartedAtUtc, publisher.CompletedAtUtc);
            Require(Time(root, "createdAtUtc") >= publisher.Jobs.Single().StartedAtUtc, "s06-publication-job");
            return new(fetched, validation, publisher);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw Error("s06-candidate-timeout");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { throw Error("s06-candidate-verification"); }
    }
}
