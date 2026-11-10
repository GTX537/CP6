using CP6.Client.Api;

namespace CP6.Client.Core;

public sealed class DynamicApiEndpointHandler(ClientOptions options) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Options.TryGetValue(ClientRequestScope.Key, out var pinned))
        {
            if (!pinned.Source.Equals(options.ApiBaseAddress) || request.RequestUri == null
                || !pinned.Source.IsBaseOf(request.RequestUri))
                throw new LabelGatewayRecoveryException("RecoveryScopeChanged");
            return base.SendAsync(request, cancellationToken);
        }
        if (request.RequestUri is { } uri)
        {
            var relative = uri.IsAbsoluteUri
                ? uri.PathAndQuery.TrimStart('/')
                : uri.OriginalString.TrimStart('/');
            request.RequestUri = new Uri(options.ApiBaseAddress, relative);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
