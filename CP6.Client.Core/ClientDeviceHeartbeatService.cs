using System.Text;
using CP6.Client.Api;

namespace CP6.Client.Core;

public interface IDeviceRequestSigner
{
    Task<string> GetOrCreatePublicKeyAsync(CancellationToken ct = default);
    Task<string> SignAsync(byte[] payload, CancellationToken ct = default);
}

public sealed class ClientDeviceHeartbeatService
{
    private readonly Cp6ApiClient _api;
    private readonly ClientOptions _options;
    private readonly IDeviceRequestSigner _signer;

    public ClientDeviceHeartbeatService(
        IHttpClientFactory clients,
        ClientOptions options,
        IDeviceRequestSigner signer)
    {
        _api = new Cp6ApiClient(
            clients.CreateClient(ClientServiceCollectionExtensions.AuthenticatedClient));
        _options = options;
        _signer = signer;
    }

    public async Task<ClientDevice> SendAsync(
        string? currentTaskNo = null,
        int? batteryPercent = null,
        string? networkType = null,
        CancellationToken ct = default)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var nonce = Guid.NewGuid().ToString("N");
        var payload = Encoding.UTF8.GetBytes(
            $"{_options.Context.DeviceId}|{timestamp.ToUnixTimeSeconds()}|{nonce}|{_options.Context.AppVersion}");
        return await _api.HeartbeatAsync(new ClientDeviceHeartbeatRequest
        {
            DeviceId = _options.Context.DeviceId,
            AppVersion = _options.Context.AppVersion,
            PlatformVersion = _options.Context.PlatformVersion,
            BatteryPercent = batteryPercent,
            NetworkType = networkType,
            CurrentTaskNo = currentTaskNo,
            Timestamp = timestamp,
            Nonce = nonce,
            Signature = await _signer.SignAsync(payload, ct),
        }, ct);
    }
}
