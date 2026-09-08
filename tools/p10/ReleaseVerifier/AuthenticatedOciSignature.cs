using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Authenticated claims only, not image availability, workflow success or candidate acceptance.
internal sealed class AuthenticatedOciSignature
{
    private readonly byte[] _bundle;

    private AuthenticatedOciSignature(string digest, GitHubWorkflowIdentity workflow, string signerKeyId,
        int policyVersion, DateTimeOffset signedAtUtc, byte[] bundle, byte[] payload)
    {
        Digest = digest;
        Workflow = workflow;
        SignerKeyId = signerKeyId;
        PolicyVersion = policyVersion;
        SignedAtUtc = signedAtUtc;
        _bundle = bundle;
        BundleSha256 = Cp6DeterministicJson.Sha256Hex(bundle);
        PayloadSha256 = Cp6DeterministicJson.Sha256Hex(payload);
    }

    internal string Repository => S06ReleaseIdentity.ImageRepository;
    internal string Digest { get; }
    internal GitHubWorkflowIdentity Workflow { get; }
    internal string SignerKeyId { get; }
    internal int PolicyVersion { get; }
    internal DateTimeOffset SignedAtUtc { get; }
    internal string BundleSha256 { get; }
    internal string PayloadSha256 { get; }
    internal byte[] CopyBundle() => _bundle.ToArray();

    internal static Task<AuthenticatedOciSignature> AuthenticateAsync(string digest,
        GitHubWorkflowIdentity workflow, ReadOnlyMemory<byte> bundle, CosignBlobVerifier verifier,
        CancellationToken cancellationToken = default) =>
        AuthenticateWithPolicyAsync(VerifierTrust.Load(), DateTimeOffset.UtcNow,
            digest, workflow, bundle, verifier, cancellationToken);

    // The normal entry always loads compiled trust and the actual current time.
    internal static async Task<AuthenticatedOciSignature> AuthenticateWithPolicyAsync(Cp6PinnedTrustPolicy policy,
        DateTimeOffset evaluationUtc, string digest, GitHubWorkflowIdentity workflow,
        ReadOnlyMemory<byte> bundle, CosignBlobVerifier verifier, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OciWirePolicy.RequireDigest(digest);
        workflow.RequireValid();
        if (workflow.Repository != "GTX537/CP6" || workflow.WorkflowPath != S06ReleaseIdentity.ValidationPath)
            throw OciSignatureProfile.Error("oci-signature-workflow");
        if (bundle.Length is < 1 or > Cp6DeterministicJson.MaximumBytes) throw OciSignatureProfile.Error("oci-signature-size");
        var owned = bundle.ToArray();
        var selectors = OciSignatureProfile.Selectors(GitHubApiJson.Parse(OciBundlePayload.Read(owned)), evaluationUtc);
        var key = policy.RequireKey(selectors.KeyId, "oci", selectors.Version, selectors.SignedAtUtc,
            evaluationUtc, Cp6ReleaseValidationMode.Current);
        var payload = await verifier.VerifyOciBundleAsync(digest, owned, key, cancellationToken);
        OciSignatureProfile.RequireClaims(GitHubApiJson.Parse(payload), digest, workflow,
            selectors.KeyId, selectors.Version, selectors.SignedAtUtc);
        return new(digest, workflow, selectors.KeyId, selectors.Version, selectors.SignedAtUtc, owned, payload);
    }

    // Called after the live source has proved completed-success run, jobs and workflow bytes.
    // This also prevents reusing an authentic signature from another invocation or time window.
    internal void RequireCompletedWorkflow(GitHubWorkflowObservation observation, DateTimeOffset cutoff)
    {
        if (cutoff.Offset != TimeSpan.Zero || observation.Workflow != Workflow ||
            observation.StartedAtUtc > SignedAtUtc || SignedAtUtc > observation.CompletedAtUtc ||
            observation.CompletedAtUtc > cutoff || !observation.Jobs.Any(job =>
                job.StartedAtUtc <= SignedAtUtc && SignedAtUtc <= job.CompletedAtUtc))
            throw OciSignatureProfile.Error("oci-signature-workflow-time");
    }
}
