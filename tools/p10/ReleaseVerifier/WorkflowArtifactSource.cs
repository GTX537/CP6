using System.Net;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Binds raw artifact bytes to GitHub's exact ID/name/run/SHA/digest/time window.
// Caller obtains the producer/job window from independent workflow checks. This layer never asserts workflow completion.
internal static class WorkflowArtifactSource
{
    internal static async Task<DownloadedWorkflowArtifact> ReadAsync(WorkflowArtifactSelection selection,
        DateTimeOffset earliest, DateTimeOffset latest, string token, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GitHubEvidenceChecks.RequireCutoff(earliest);
        GitHubEvidenceChecks.RequireCutoff(latest);
        GitHubEvidenceChecks.Require(earliest <= latest && latest <= DateTimeOffset.UtcNow, "artifact-time");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        using var metadataClient = new GitHubReadClient(token);
        try
        {
            var metadata = selection.ReadMetadata(await metadataClient.ReadAsync(
                GitHubReadTarget.Artifact(selection.ArtifactId), deadline.Token), earliest, latest);
            using var client = new HttpClient(GitHubWirePolicy.CreateHandler()) { Timeout = Timeout.InfiniteTimeSpan };
            using var request = GitHubWirePolicy.Request(GitHubReadTarget.ArtifactZip(selection.ArtifactId), token);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            GitHubEvidenceChecks.Require(response.StatusCode == HttpStatusCode.Redirect, "artifact-http-status");
            using var blobRequest = WorkflowArtifactWire.BlobRequest(response.Headers.Location);
            using var blobResponse = await client.SendAsync(blobRequest, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            var bytes = await WorkflowArtifactWire.ReadZipAsync(blobResponse, deadline.Token);
            var files = WorkflowArtifactArchive.Read(bytes, metadata);
            return new(metadata, files, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw GitHubWirePolicy.Error("artifact-timeout");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw GitHubWirePolicy.Error("artifact-transfer");
        }
    }
}

internal sealed class DownloadedWorkflowArtifact
{
    private readonly IReadOnlyDictionary<string, ReadOnlyMemory<byte>> _files;

    internal DownloadedWorkflowArtifact(WorkflowArtifactMetadata metadata,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files, DateTimeOffset observedAtUtc)
    {
        Metadata = metadata;
        _files = files.ToDictionary(p => p.Key, p => (ReadOnlyMemory<byte>)p.Value.ToArray(), StringComparer.Ordinal);
        ObservedAtUtc = observedAtUtc;
    }

    internal WorkflowArtifactMetadata Metadata { get; }
    internal DateTimeOffset ObservedAtUtc { get; }
    internal IReadOnlyList<string> Names => Array.AsReadOnly(_files.Keys.Order(StringComparer.Ordinal).ToArray());
    internal byte[] CopyFile(string name) => _files.TryGetValue(name, out var bytes) ?
        bytes.ToArray() : throw GitHubWirePolicy.Error("artifact-entry-set");
}
