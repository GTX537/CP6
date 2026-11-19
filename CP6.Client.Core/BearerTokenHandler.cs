using System.Net;
using System.Net.Http.Headers;
using CP6.Client.Api;

namespace CP6.Client.Core;

/// <summary>
/// Adds the in-memory access token. Concurrent safe-request 401s share one
/// refresh. Unsafe writes are never replayed automatically.
/// </summary>
public sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly IClientSessionService _sessions;
    private readonly ClientOptions? _options;

    public BearerTokenHandler(IClientSessionService sessions) : this(sessions, null) { }
    public BearerTokenHandler(IClientSessionService sessions, ClientOptions? options) { _sessions = sessions; _options = options; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var interaction = _sessions.InteractionVersion;
        var token = _sessions.AccessToken;
        RequirePinnedScope(request, token);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var safeToReplay = request.Method is { } method
            && (method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options);
        using var retry = safeToReplay ? await CloneAsync(request, cancellationToken) : null;
        var response = await base.SendAsync(request, cancellationToken);
        if (interaction != _sessions.InteractionVersion)
        {
            response.Dispose();
            throw new ClientInteractionChangedException();
        }
        if (response.StatusCode != HttpStatusCode.Unauthorized
            || request.RequestUri?.AbsolutePath.Contains("/client-auth/", StringComparison.OrdinalIgnoreCase) == true)
            return response;

        try
        {
            await _sessions.RefreshMergedAsync(token, cancellationToken);
            if (interaction != _sessions.InteractionVersion) throw new ClientInteractionChangedException();
            if (retry == null) return response;
            response.Dispose();
            var refreshed = _sessions.AccessToken;
            RequirePinnedScope(retry, refreshed);
            retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed);
            var replay = await base.SendAsync(retry, cancellationToken);
            if (interaction == _sessions.InteractionVersion) return replay;
            replay.Dispose();
            throw new ClientInteractionChangedException();
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private void RequirePinnedScope(HttpRequestMessage request, string? token)
    {
        if (!request.Options.TryGetValue(ClientRequestScope.Key, out var pinned)) return;
        if (pinned.InteractionVersion is { } version && version != _sessions.InteractionVersion)
            throw new ClientInteractionChangedException();
        if (_options == null || !_options.ApiBaseAddress.Equals(pinned.Source) || _options.Context.DeviceId != pinned.DeviceId)
            throw new LabelGatewayRecoveryException("RecoveryScopeChanged");
        var actor = ClientLabelGatewayContext.ReadIdentity(token, pinned.Source, pinned.DeviceId);
        if (actor.TenantId != pinned.TenantId || actor.ActorId != pinned.ActorId)
            throw new LabelGatewayRecoveryException("RecoveryScopeChanged");
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
        foreach (var option in request.Options)
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
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
