using CP6.Client.Api;
using CP6.Client.Core;

namespace CP6.Mobile;

public sealed class DeviceActivationService
{
    private readonly Cp6ApiClient _api;
    private readonly ClientOptions _options;
    private readonly IDeviceRequestSigner _signer;
    private readonly MobileClientState _state;
    private readonly ClientDeviceHeartbeatLoop _heartbeat;

    public DeviceActivationService(
        IHttpClientFactory clients,
        ClientOptions options,
        IDeviceRequestSigner signer,
        MobileClientState state,
        ClientDeviceHeartbeatLoop heartbeat)
    {
        _api = new Cp6ApiClient(
            clients.CreateClient(ClientServiceCollectionExtensions.RawClient));
        _options = options;
        _signer = signer;
        _state = state;
        _heartbeat = heartbeat;
    }

    public async Task<ActivatedClientDevice> ActivateAsync(
        string payload,
        CancellationToken ct = default)
    {
        var ticket = Parse(payload);
        var previous = _options.ApiBaseAddress;
        _options.ApiBaseAddress = ticket.Server;
        try
        {
            var result = await _api.ActivateDeviceAsync(new ActivateClientDeviceRequest
            {
                TenantCode = ticket.Tenant,
                ActivationToken = ticket.Token,
                DeviceId = _options.Context.DeviceId,
                PublicKey = await _signer.GetOrCreatePublicKeyAsync(ct),
                Platform = "Android",
                AppVersion = _options.Context.AppVersion,
                PlatformVersion = _options.Context.PlatformVersion,
            }, ct);
            Preferences.Default.Set("cp6.api-url", ticket.Server.AbsoluteUri);
            Preferences.Default.Set("cp6.tenant-code", result.TenantCode);
            _state.SetDeviceActivated(true);
            _heartbeat.RequestImmediate();
            return result;
        }
        catch
        {
            _options.ApiBaseAddress = previous;
            throw;
        }
    }

    private static ActivationTicket Parse(string payload)
    {
        if (!Uri.TryCreate(payload.Trim(), UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "cp6-activate", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("WM-DEVICE-ACTIVATION-QR-INVALID");
        var values = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('=', 2))
            .Where(x => x.Length == 2)
            .ToDictionary(
                x => Uri.UnescapeDataString(x[0]),
                x => Uri.UnescapeDataString(x[1]),
                StringComparer.OrdinalIgnoreCase);
        if (!values.TryGetValue("server", out var server)
            || !Uri.TryCreate(server, UriKind.Absolute, out var serverUri)
            || serverUri.Scheme is not ("https" or "http")
            || !values.TryGetValue("tenant", out var tenant)
            || string.IsNullOrWhiteSpace(tenant)
            || !values.TryGetValue("token", out var token)
            || string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("WM-DEVICE-ACTIVATION-QR-INVALID");
        return new ActivationTicket(
            new Uri(serverUri.AbsoluteUri.EndsWith('/') ? serverUri.AbsoluteUri : $"{serverUri}/"),
            tenant.Trim(),
            token.Trim());
    }

    private sealed record ActivationTicket(Uri Server, string Tenant, string Token);
}
