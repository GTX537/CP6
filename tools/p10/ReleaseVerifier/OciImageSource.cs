using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Uses the existing intended consumer's GitHub Packages read credential; no new credential authority.
// Only two fixed GHCR GETs are permitted. Returned metadata never confers deployment/candidate acceptance.
internal static class OciImageSource
{
    internal static async Task<DownloadedOciManifest> ReadAsync(string digest, string readToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OciWirePolicy.RequireDigest(digest);
        using var tokenRequest = OciWirePolicy.TokenRequest(readToken);
        using var client = new HttpClient(GitHubWirePolicy.CreateHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            using var tokenResponse = await client.SendAsync(tokenRequest, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            var token = OciWirePolicy.PullToken(await OciWirePolicy.ReadBodyAsync(tokenResponse, false, deadline.Token));
            using var manifestRequest = OciWirePolicy.ManifestRequest(digest, token);
            using var response = await client.SendAsync(manifestRequest, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            var result = await OciManifestChecks.ResponseAsync(response, digest, deadline.Token);
            return new(result.Bytes, digest, result.Description, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw OciWirePolicy.Error("oci-timeout");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw OciWirePolicy.Error("oci-transfer");
        }
    }
}

internal sealed class DownloadedOciManifest
{
    private readonly byte[] _bytes;

    internal DownloadedOciManifest(byte[] bytes, string digest, OciManifestDescription description, DateTimeOffset retrievedAtUtc)
    {
        _bytes = bytes.ToArray();
        Digest = digest;
        Description = description;
        RetrievedAtUtc = retrievedAtUtc;
    }

    internal string Repository => S06ReleaseIdentity.ImageRepository;
    internal string Digest { get; }
    internal OciManifestDescription Description { get; }
    internal DateTimeOffset RetrievedAtUtc { get; }
    internal byte[] CopyBytes() => _bytes.ToArray();
}
