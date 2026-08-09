using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Client;

namespace CP6.Core.Services.Wms;

public interface IClientDeviceService
{
    Task<DeviceActivationTicket> CreateActivationAsync(
        CreateDeviceActivationRequest request,
        string? userName,
        CancellationToken ct = default);
    Task<ActivatedClientDeviceDto> ActivateAsync(
        ActivateClientDeviceRequest request,
        CancellationToken ct = default);
    Task<ClientDeviceDto> HeartbeatAsync(
        ClientDeviceHeartbeatRequest request,
        string? userName,
        CancellationToken ct = default);
    Task<PagedResult<ClientDeviceDto>> GetDevicesAsync(
        string? warehouseCd,
        string? areaCd,
        string? status,
        int page,
        int pageSize,
        CancellationToken ct = default);
    Task<ClientDeviceDto> UpdateAsync(
        string deviceId,
        UpdateClientDeviceRequest request,
        string? userName,
        CancellationToken ct = default);
    Task EnsureLoginAllowedAsync(
        ClientContextDto client,
        Guid tenantId,
        CancellationToken ct = default);
    Task MarkFullAuthenticationAsync(
        ClientContextDto client,
        Guid tenantId,
        string userName,
        CancellationToken ct = default);
    Task<ClientDevice> GetQuickSwitchDeviceAsync(
        ClientContextDto client,
        Guid tenantId,
        CancellationToken ct = default);
}
