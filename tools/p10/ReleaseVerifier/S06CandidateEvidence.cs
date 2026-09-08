using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Common real evidence/producer verification. The caller must independently authenticate the discovery root.
// This method accepts neither a successful-workflow assertion nor replacement trust/transport components.
internal static class S06CandidateEvidence
{
    internal static async Task<GitHubWorkflowObservation> VerifyAsync(InspectedEvidenceGraph graph,
        DateTimeOffset publicationStartedAtUtc, CosignBlobVerifier cosign, string githubReadToken,
        string feedReadToken, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GitHubEvidenceChecks.RequireCutoff(publicationStartedAtUtc);
        Require(publicationStartedAtUtc <= DateTimeOffset.UtcNow, "s06-candidate-cutoff");
        var root = graph.Candidate;
        var producer = S06WorkflowProfile.Read(root.GetProperty("verifier"), S06ReleaseIdentity.ValidationPath);
        var digest = Text(root.GetProperty("images")[0], "sha256OrDigest");
        var payloads = graph.Evidence.ToDictionary(p => p.Key,
            p => (ReadOnlyMemory<byte>)p.Value.CopyPayloadBytes(), StringComparer.Ordinal);
        var proof = await S06PayloadProof.VerifyAsync(producer, digest, payloads, Time(graph.Gate, "createdAtUtc"),
            cosign, githubReadToken, feedReadToken, cancellationToken);
        var validation = await GitHubEvidenceSource.ReadWorkflowAsync(producer, S06WorkflowProfile.ValidationJobs,
            publicationStartedAtUtc, githubReadToken, cancellationToken);
        S06EvidenceChronology.RequireValidation(root, graph.Gate, graph.Evidence, validation, proof.FormalWorkflow);
        proof.OciSignature.RequireCompletedWorkflow(validation, publicationStartedAtUtc);
        return validation;
    }
}
