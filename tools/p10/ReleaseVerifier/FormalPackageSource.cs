using System.Net;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

public static class FormalPackageSource
{
    // No endpoint, cache, handler, trust policy or version override is accepted.
    public static async Task<DownloadedNuGetPackage> DownloadAndVerifyAsync(string packageId, string readToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FormalFeedPolicy.RequirePackageId(packageId);
        using var indexRequest = FormalFeedPolicy.IndexRequest(readToken);
        using var client = new HttpClient(FormalFeedPolicy.CreateHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            using var indexResponse = await client.SendAsync(indexRequest, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            FormalFeedPolicy.RequireIndex(await StrictHttpBody.ReadAsync(indexResponse, "application/json", 65536, deadline.Token));
            using var packageRequest = FormalFeedPolicy.PackageRequest(packageId, readToken);
            using var packageResponse = await client.SendAsync(packageRequest, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            byte[] bytes;
            if (packageResponse.StatusCode == HttpStatusCode.Redirect)
            {
                // A single explicitly allowed storage hop; never forward GitHub credentials.
                using var blobRequest = FormalFeedPolicy.BlobRequest(packageResponse.Headers.Location);
                using var blobResponse = await client.SendAsync(blobRequest, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                bytes = await StrictHttpBody.ReadAsync(blobResponse, "application/octet-stream",
                    FormalNuGetVerifier.MaximumPackageBytes, deadline.Token);
            }
            else
            {
                bytes = await StrictHttpBody.ReadAsync(packageResponse, "application/octet-stream",
                    FormalNuGetVerifier.MaximumPackageBytes, deadline.Token);
            }
            var retrievedAt = DateTimeOffset.UtcNow;
            var proof = await FormalNuGetVerifier.VerifyAsync(packageId, bytes, deadline.Token);
            return new(bytes, proof, retrievedAt);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw FormalFeedPolicy.Error("feed-timeout");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Neither signed redirect URLs nor response/error bodies enter diagnostics.
            throw FormalFeedPolicy.Error("feed-transfer");
        }
    }
}

public sealed class DownloadedNuGetPackage
{
    private readonly byte[] _bytes;

    internal DownloadedNuGetPackage(byte[] bytes, VerifiedNuGetPackage proof, DateTimeOffset retrievedAtUtc)
    {
        _bytes = bytes.ToArray();
        Proof = proof;
        RetrievedAtUtc = retrievedAtUtc;
    }

    public VerifiedNuGetPackage Proof { get; }
    public DateTimeOffset RetrievedAtUtc { get; }
    public string FeedServiceIndex => FormalFeedPolicy.Index;
    public byte[] CopyPackageBytes() => _bytes.ToArray();
}
