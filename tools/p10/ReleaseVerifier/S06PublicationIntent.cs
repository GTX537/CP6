using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Actual current-run immutable intent transport and Locator authentication only.
// The caller must still fetch the R2 graph and verify the candidate against this running publisher.
internal sealed class S06PublicationIntent
{
    internal const string LocatorName = "locator.json";
    internal const string BundleName = "locator.sigstore.json";
    private readonly byte[] _locatorBytes;
    private readonly byte[] _bundleBytes;

    private S06PublicationIntent(byte[] locatorBytes, byte[] bundleBytes, AuthenticatedLocator locator,
        S06CurrentWorkflow current, WorkflowArtifactMetadata metadata)
    {
        _locatorBytes = locatorBytes.ToArray();
        _bundleBytes = bundleBytes.ToArray();
        Locator = locator;
        Publication = current;
        Metadata = metadata;
    }

    internal AuthenticatedLocator Locator { get; }
    internal S06CurrentWorkflow Publication { get; }
    internal WorkflowArtifactMetadata Metadata { get; }
    internal byte[] CopyLocatorBytes() => _locatorBytes.ToArray();
    internal byte[] CopyBundleBytes() => _bundleBytes.ToArray();

    internal static async Task<S06PublicationIntent> ReadAsync(string releaseTag, long artifactId,
        CosignBlobVerifier cosign, string githubReadToken, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = R2ObjectTarget.Discovery(releaseTag, R2DiscoveryPart.Locator);
        Require(artifactId > 0, "s06-intent-id");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            var current = await S06CurrentWorkflow.CaptureAsync(S06ReleaseIdentity.PublicationPath, githubReadToken, deadline.Token);
            var selection = WorkflowArtifactSelection.PublicationIntent(current.Workflow, artifactId);
            var downloaded = await WorkflowArtifactSource.ReadAsync(selection, current.JobStartedAtUtc,
                current.ObservedAtUtc, githubReadToken, deadline.Token);
            var payloads = ReadPayloads(downloaded);
            var locator = await AuthenticatedLocator.AuthenticateAsync(releaseTag, payloads.Locator,
                payloads.Bundle, cosign, deadline.Token);
            // Metadata parsing already requires a past whole-second UTC time from the fixed GitHub API.
            var artifactEnd = downloaded.Metadata.CreatedAtUtc.AddSeconds(1).AddTicks(-1);
            Require(current.JobStartedAtUtc <= locator.CreatedAtUtc && locator.CreatedAtUtc <= artifactEnd &&
                locator.CreatedAtUtc <= downloaded.ObservedAtUtc, "s06-intent-time");
            return new(payloads.Locator, payloads.Bundle, locator, current, downloaded.Metadata);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw Error("s06-intent-timeout");
        }
    }

    // Plain bytes, not a trusted intent capability. The real entry point first authenticates the API/archive.
    internal static (byte[] Locator, byte[] Bundle) ReadPayloads(DownloadedWorkflowArtifact artifact)
    {
        Require(artifact.Names.SequenceEqual(new[] { LocatorName, BundleName }, StringComparer.Ordinal), "s06-intent-files");
        var locator = artifact.CopyFile(LocatorName);
        var bundle = artifact.CopyFile(BundleName);
        Require(locator.Length is > 0 and <= Cp6DeterministicJson.MaximumBytes &&
            bundle.Length is > 0 and <= Cp6DeterministicJson.MaximumBytes, "s06-intent-size");
        return (locator, bundle);
    }
}
