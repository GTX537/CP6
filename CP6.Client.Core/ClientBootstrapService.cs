using CP6.Client.Api;

namespace CP6.Client.Core;

public sealed class ClientBootstrapService
{
    private readonly Cp6ApiClient _api;
    private readonly ClientOptions _options;

    public ClientBootstrapService(IHttpClientFactory clients, ClientOptions options)
    {
        _api = new Cp6ApiClient(clients.CreateClient(ClientServiceCollectionExtensions.RawClient));
        _options = options;
    }

    public Task<ClientBootstrap> CheckAsync(CancellationToken ct = default)
        => _api.BootstrapAsync(_options.Platform, _options.Context.AppVersion, ct);

    public static int CompareVersions(string left, string right)
    {
        if (!ClientSemanticVersion.TryParse(
                left,
                out var parsedLeft))
        {
            return -1;
        }
        if (!ClientSemanticVersion.TryParse(
                right,
                out var parsedRight))
        {
            return 1;
        }
        return parsedLeft.CompareTo(parsedRight);
    }
}
