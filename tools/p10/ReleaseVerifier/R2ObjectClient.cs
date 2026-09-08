using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Internal transport: callers must authenticate the Locator before selecting signed object references.
// Owns credentials; no injected handler, arbitrary endpoint, ambient credentials, retries or delete API.
internal sealed class R2ObjectClient : IDisposable
{
    private readonly R2Credentials _credentials;
    private readonly HttpClient _client;
    private bool _disposed;

    private R2ObjectClient(R2Credentials credentials)
    {
        _credentials = credentials;
        _client = new HttpClient(R2WirePolicy.CreateHandler()) { Timeout = Timeout.InfiniteTimeSpan };
    }

    internal static R2ObjectClient Consumer(string accessKeyId, string secret) => new(R2Credentials.Consumer(accessKeyId, secret));
    internal static R2ObjectClient Publisher(string accessKeyId, string secret) => new(R2Credentials.Publisher(accessKeyId, secret));

    internal Task<byte[]?> ReadAsync(R2ObjectTarget target, CancellationToken cancellationToken = default) =>
        TransferAsync(HttpMethod.Get, target, ReadOnlyMemory<byte>.Empty,
            (response, token) => R2WirePolicy.ReadResponseAsync(response, target, token), cancellationToken);

    internal Task<R2CreateStatus> CreateAsync(R2ObjectTarget target, ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = default) =>
        TransferAsync(HttpMethod.Put, target, bytes,
            (response, _) => Task.FromResult(R2WirePolicy.CreateResponse(response)), cancellationToken);

    private async Task<T> TransferAsync<T>(HttpMethod method, R2ObjectTarget target, ReadOnlyMemory<byte> bytes,
        Func<HttpResponseMessage, CancellationToken, Task<T>> parse, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed) throw R2WirePolicy.Error("r2-client-disposed");
        try
        {
            var now = DateTimeOffset.UtcNow;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(R2WirePolicy.RequestLifetime(_credentials, now));
            using var request = R2WirePolicy.Request(method, target, _credentials, bytes, now);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            return await parse(response, deadline.Token);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw R2WirePolicy.Error("r2-timeout");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Never expose request headers, credential values, server XML, URLs or inner exceptions.
            throw R2WirePolicy.Error("r2-transfer");
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _credentials.Dispose();
        _disposed = true;
    }
}
