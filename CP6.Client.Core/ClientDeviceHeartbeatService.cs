using System.Text;
using CP6.Client.Api;

namespace CP6.Client.Core;

public interface IDeviceRequestSigner
{
    Task<string> GetOrCreatePublicKeyAsync(CancellationToken ct = default);
    Task<string> SignAsync(byte[] payload, CancellationToken ct = default);
}

public sealed class ClientDeviceHeartbeatService : IClientDeviceHeartbeatSender
{
    private readonly HttpClient _transport;
    private readonly IClientSessionService _sessions;
    private readonly ClientOptions _options;
    private readonly IDeviceRequestSigner _signer;

    public ClientDeviceHeartbeatService(
        IHttpClientFactory clients,
        ClientOptions options,
        IDeviceRequestSigner signer,
        IClientSessionService sessions)
    {
        _transport = clients.CreateClient(ClientServiceCollectionExtensions.AuthenticatedClient);
        _sessions = sessions;
        _options = options;
        _signer = signer;
    }

    public Task<ClientDevice> SendAsync(
        string? currentTaskNo = null,
        int? batteryPercent = null,
        string? networkType = null,
        CancellationToken ct = default)
        => SendForSessionAsync(_sessions.Current ?? throw new SessionExpiredException(), currentTaskNo, batteryPercent, networkType, ct);

    public async Task<ClientDevice> SendForSessionAsync(TokenSession session, string? currentTaskNo, int? batteryPercent,
        string? networkType, CancellationToken ct = default)
    {
        var interaction = _sessions.InteractionVersion;
        if (!ReferenceEquals(session, _sessions.Current)) throw new ClientInteractionChangedException();
        var device = _options.Context;
        var scope = ClientLabelGatewayContext.ReadIdentity(session.AccessToken, _options.ApiBaseAddress, device.DeviceId);
        var api = new Cp6ApiClient(_transport, new ClientRequestScope(scope.Source, scope.TenantId, scope.ActorId, scope.DeviceId, interaction));
        var timestamp = DateTimeOffset.UtcNow;
        var nonce = Guid.NewGuid().ToString("N");
        var payload = Encoding.UTF8.GetBytes(
            $"{device.DeviceId}|{timestamp.ToUnixTimeSeconds()}|{nonce}|{device.AppVersion}");
        return await api.HeartbeatAsync(new ClientDeviceHeartbeatRequest
        {
            DeviceId = device.DeviceId,
            AppVersion = device.AppVersion,
            PlatformVersion = device.PlatformVersion,
            BatteryPercent = batteryPercent,
            NetworkType = networkType,
            CurrentTaskNo = currentTaskNo,
            Timestamp = timestamp,
            Nonce = nonce,
            Signature = await _signer.SignAsync(payload, ct),
        }, ct);
    }
}
