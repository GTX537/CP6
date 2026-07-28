using System.Net;
using System.Net.Http.Headers;

namespace CP6.Client.Core;

/// <summary>
/// Adds the in-memory access token. Concurrent safe-request 401s share one
/// refresh. Unsafe writes are never replayed automatically.
/// </summary>
public sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly IClientSessionService _sessions;

    public BearerTokenHandler(IClientSessionService sessions) => _sessions = sessions;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = _sessions.AccessToken;
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var safeToReplay = request.Method is { } method
                           && (method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options);
        var retry = safeToReplay ? await CloneAsync(request, cancellationToken) : null;
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized
            || request.RequestUri?.AbsolutePath.Contains("/client-auth/", StringComparison.OrdinalIgnoreCase) == true)
            return response;

        await _sessions.RefreshMergedAsync(token, cancellationToken);
        if (retry == null) return response;

        response.Dispose();
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _sessions.AccessToken);
        return await base.SendAsync(retry, cancellationToken);
    }

    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage request,
        CancellationToken ct)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };
        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (request.Content != null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(ct);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return clone;
    }
}
