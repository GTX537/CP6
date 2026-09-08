using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// A completed producer plus its independently downloaded and verified payloads. Not a published candidate.
internal sealed class S06CompletedValidation
{
    private S06CompletedValidation(InspectedValidationArtifact artifact, GitHubWorkflowObservation workflow,
        WorkflowArtifactMetadata metadata)
    {
        Artifact = artifact;
        Workflow = workflow;
        Metadata = metadata;
        VerifiedAtUtc = DateTimeOffset.UtcNow;
    }

    internal InspectedValidationArtifact Artifact { get; }
    internal GitHubWorkflowObservation Workflow { get; }
    internal WorkflowArtifactMetadata Metadata { get; }
    internal DateTimeOffset VerifiedAtUtc { get; }

    internal static async Task<S06CompletedValidation> ReadAsync(GitHubWorkflowIdentity producer, long artifactId,
        DateTimeOffset completedBeforeUtc, CosignBlobVerifier cosign, string githubReadToken, string feedReadToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireProducer(producer);
        var selection = WorkflowArtifactSelection.Validation(producer, artifactId);
        GitHubEvidenceChecks.RequireCutoff(completedBeforeUtc);
        Require(completedBeforeUtc <= DateTimeOffset.UtcNow, "s06-validation-cutoff");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(15));
        try
        {
            var workflow = await GitHubEvidenceSource.ReadWorkflowAsync(producer, S06WorkflowProfile.ValidationJobs,
                completedBeforeUtc, githubReadToken, deadline.Token);
            var job = workflow.Jobs.Single();
            var downloaded = await WorkflowArtifactSource.ReadAsync(selection, job.StartedAtUtc, job.CompletedAtUtc,
                githubReadToken, deadline.Token);
            var cutoff = S06ArtifactTiming.ContentCutoff(downloaded.Metadata.CreatedAtUtc,
                job.CompletedAtUtc, downloaded.ObservedAtUtc);
            var files = downloaded.Names.ToDictionary(n => n,
                n => (ReadOnlyMemory<byte>)downloaded.CopyFile(n), StringComparer.Ordinal);
            var artifact = S06ArtifactInspection.Read(files, producer, cutoff);
            var payloads = artifact.Evidence.ToDictionary(p => p.Key,
                p => (ReadOnlyMemory<byte>)p.Value.CopyPayloadBytes(), StringComparer.Ordinal);
            var digest = Text(artifact.Index.GetProperty("images")[0], "sha256OrDigest");
            var proof = await S06PayloadProof.VerifyAsync(producer, digest, payloads, Time(artifact.Gate, "createdAtUtc"),
                cosign, githubReadToken, feedReadToken, deadline.Token);
            S06EvidenceChronology.RequireValidation(artifact.Index, artifact.Gate, artifact.Evidence,
                workflow, proof.FormalWorkflow);
            proof.OciSignature.RequireCompletedWorkflow(workflow, completedBeforeUtc);
            return new(artifact, workflow, downloaded.Metadata);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw Error("s06-validation-timeout");
        }
    }
}
