using CP6.Entity.DTOs.Client;

namespace CP6.Core.Services.Wms;

public sealed class CreateDeviceActivationRequest
{
    public string Platform { get; set; } = string.Empty;
    public string DeviceMode { get; set; } = "Shared";
    public string? WarehouseCd { get; set; }
    public string? AreaCd { get; set; }
    public int ValidMinutes { get; set; } = 15;
}

public sealed class DeviceActivationTicket
{
    public string ActivationToken { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; init; }
    public string Platform { get; init; } = string.Empty;
    public string DeviceMode { get; init; } = string.Empty;
    public string? WarehouseCd { get; init; }
    public string? AreaCd { get; init; }
}

public sealed class ActivateClientDeviceRequest
{
    public string TenantCode { get; set; } = string.Empty;
    public string ActivationToken { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string? PlatformVersion { get; set; }
}

public sealed class ActivatedClientDeviceDto
{
    public string DeviceId { get; init; } = string.Empty;
    public string TenantCode { get; init; } = string.Empty;
    public string DeviceMode { get; init; } = string.Empty;
    public string? WarehouseCd { get; init; }
    public string? AreaCd { get; init; }
    public DateTimeOffset ActivatedAt { get; init; }
}

public sealed class ClientDeviceHeartbeatRequest
{
    public string DeviceId { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string? PlatformVersion { get; set; }
    public int? BatteryPercent { get; set; }
    public string? NetworkType { get; set; }
    public string? CurrentTaskNo { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Nonce { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}

public sealed class ClientDeviceDto
{
    public string DeviceId { get; init; } = string.Empty;
    public string DeviceMode { get; init; } = string.Empty;
    public string Platform { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? WarehouseCd { get; init; }
    public string? AreaCd { get; init; }
    public string? AppVersion { get; init; }
    public string? PlatformVersion { get; init; }
    public DateTime? ActivatedAt { get; init; }
    public DateTime? LastSeenAt { get; init; }
    public int? BatteryPercent { get; init; }
    public string? NetworkType { get; init; }
    public string? CurrentUser { get; init; }
    public string? CurrentTaskNo { get; init; }
    public DateTime? FullAuthExpiresAt { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class UpdateClientDeviceRequest
{
    public string RowVersion { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? DeviceMode { get; set; }
    public string? WarehouseCd { get; set; }
    public string? AreaCd { get; set; }
}

public sealed class QuickSwitchRequest
{
    public string TenantCode { get; set; } = string.Empty;
    public string BadgeNo { get; set; } = string.Empty;
    public string Pin { get; set; } = string.Empty;
    public ClientContextDto Client { get; set; } = new();
}

public sealed class SetWarehouseQuickPinRequest
{
    public string UserName { get; set; } = string.Empty;
    public string BadgeNo { get; set; } = string.Empty;
    public string Pin { get; set; } = string.Empty;
}

public sealed class WmsFeatureFlagDto
{
    public string WarehouseCd { get; init; } = string.Empty;
    public bool ProductionMoveEnabled { get; init; }
    public bool SerialLpnEnabled { get; init; }
    public int ScanRetentionDays { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class UpdateWmsFeatureFlagRequest
{
    public bool ProductionMoveEnabled { get; set; }
    public bool SerialLpnEnabled { get; set; }
    public int ScanRetentionDays { get; set; } = 180;
}
