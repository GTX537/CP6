using System.Globalization;
using System.Text.Json;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06ReleaseIdentity;

namespace CP6.P10.ReleaseVerifier;

// Fetching and structural inspection only. Live evidence proof validation is a separate mandatory layer.
public static class PlatformCandidateSource
{
    public static async Task<FetchedCandidateGraph> DiscoverAsync(string releaseTag, string readAccessKeyId,
        string readSecret, CosignBlobVerifier verifier, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var locatorTarget = R2ObjectTarget.Discovery(releaseTag, R2DiscoveryPart.Locator);
        var bundleTarget = R2ObjectTarget.Discovery(releaseTag, R2DiscoveryPart.Bundle);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            using var client = R2ObjectClient.Consumer(readAccessKeyId, readSecret);
            var locatorBytes = await client.ReadAsync(locatorTarget, deadline.Token) ?? throw Error("source-locator-missing");
            var bundleBytes = await client.ReadAsync(bundleTarget, deadline.Token) ?? throw Error("source-bundle-missing");
            var locator = await AuthenticatedLocator.AuthenticateAsync(releaseTag, locatorBytes, bundleBytes, verifier, deadline.Token);
            return await FetchAuthenticatedAsync(locator, (reference, token) =>
                client.ReadAsync(R2ObjectTarget.Addressed(reference), token), deadline.Token);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw Error("source-timeout");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { throw Error("source-read"); }
    }

    // For the clean pre-commit job: intended immutable artifact bytes are authenticated before any R2 read.
    public static async Task<FetchedCandidateGraph> IntendedAsync(string releaseTag, ReadOnlyMemory<byte> locatorBytes,
        ReadOnlyMemory<byte> bundleBytes, string readAccessKeyId, string readSecret, CosignBlobVerifier verifier,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            var locator = await AuthenticatedLocator.AuthenticateAsync(releaseTag, locatorBytes, bundleBytes, verifier, deadline.Token);
            using var client = R2ObjectClient.Consumer(readAccessKeyId, readSecret);
            return await FetchAuthenticatedAsync(locator, (reference, token) =>
                client.ReadAsync(R2ObjectTarget.Addressed(reference), token), deadline.Token);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw Error("source-timeout");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { throw Error("source-read"); }
    }

    // This core requires a sealed authenticated root, not a caller-provided authentication boolean.
    // Public entry points always use compiled trust, actual cosign and the fixed real R2 transport.
    internal static async Task<FetchedCandidateGraph> FetchAuthenticatedAsync(AuthenticatedLocator locator,
        Func<ContentAddress, CancellationToken, Task<byte[]?>> read, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var downloads = new GraphObjectDownloads(read);
        var bytes = await downloads.ReadAsync(locator.Subject, Cp6ReleaseMediaTypes.PlatformReleaseCandidate, cancellationToken);
        _ = Cp6ReleaseValidator.ValidatePlatformCandidate(bytes);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        RequireCandidate(root);
        Require(Text(root, "createdAtUtc") == locator.CreatedAtUtc.UtcDateTime.ToString(
            "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture), "source-locator-time");
        var records = root.GetProperty("evidence");
        Require(records.GetArrayLength() == S06EvidenceBindings.MediaTypes.Count, "graph-evidence-set");
        foreach (var reference in records.EnumerateArray())
        {
            var recordBytes = await downloads.ReadAsync(ContentAddress.Parse(reference),
                Cp6ReleaseMediaTypes.EvidenceRecord, cancellationToken);
            _ = Cp6SupportingContractValidator.ValidateEvidenceRecord(recordBytes);
            using var record = JsonDocument.Parse(recordBytes);
            var kind = Text(record.RootElement, "evidenceKind");
            Require(S06EvidenceBindings.MediaTypes.TryGetValue(kind, out var media), "graph-evidence-set");
            _ = await downloads.ReadAsync(ContentAddress.Parse(record.RootElement.GetProperty("object")), media!, cancellationToken);
        }
        _ = await downloads.ReadAsync(ContentAddress.Parse(root.GetProperty("buildProvenance")),
            Cp6ReleaseMediaTypes.BuildInvocationProvenance, cancellationToken);
        _ = await downloads.ReadAsync(ContentAddress.Parse(root.GetProperty("releaseGateResult")),
            Cp6ReleaseMediaTypes.ReleaseGateResult, cancellationToken);
        var graph = EvidenceGraphInspection.Inspect(bytes, downloads.SnapshotExcept(locator.Subject.Key));
        return new(locator, graph);
    }

    private static Cp6ReleaseContractException Error(string code) => new(code, "Platform candidate discovery did not complete under pinned trust.");
}

public sealed class FetchedCandidateGraph
{
    internal FetchedCandidateGraph(AuthenticatedLocator locator, InspectedEvidenceGraph graph)
    {
        Locator = locator;
        Graph = graph;
    }
    public AuthenticatedLocator Locator { get; }
    public InspectedEvidenceGraph Graph { get; }
    public bool CandidateAccepted => false;
}
