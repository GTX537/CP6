namespace CP6.Client.Core;

public sealed class DynamicApiEndpointHandler(ClientOptions options) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
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
