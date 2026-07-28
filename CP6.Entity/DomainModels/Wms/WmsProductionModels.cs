using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>Tenant/warehouse feature gates used for gradual rollout.</summary>
[Table("T_WmsFeatureFlag")]
public sealed class WmsFeatureFlag : BaseBizEntity
{
    [Required, MaxLength(10)] public string WarehouseCd { get; set; } = string.Empty;
    public bool ProductionMoveEnabled { get; set; }
    public bool SerialLpnEnabled { get; set; }
    public int ScanRetentionDays { get; set; } = 180;
}

[Table("T_WmsRoleScope")]
public sealed class WmsRoleScope : BaseTenantEntity
{
    public int RoleId { get; set; }
    [Required, MaxLength(10)] public string WarehouseCd { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string AreaCd { get; set; } = "*";
}

[Table("T_ClientDevice")]
public sealed class ClientDevice : BaseBizEntity
{
    [Required, MaxLength(128)] public string DeviceId { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string DeviceMode { get; set; } = ClientDeviceMode.Shared;
    [Required, MaxLength(20)] public string Platform { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string Status { get; set; } = ClientDeviceStatus.Active;
    [Required, MaxLength(2048)] public string PublicKey { get; set; } = string.Empty;
    [MaxLength(10)] public string? WarehouseCd { get; set; }
    [MaxLength(20)] public string? AreaCd { get; set; }
    [MaxLength(32)] public string? AppVersion { get; set; }
    [MaxLength(64)] public string? PlatformVersion { get; set; }
    public DateTime ActivatedAt { get; set; }
    [MaxLength(100)] public string? ActivatedBy { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public int? BatteryPercent { get; set; }
    [MaxLength(32)] public string? NetworkType { get; set; }
    [MaxLength(100)] public string? CurrentUser { get; set; }
    [MaxLength(25)] public string? CurrentTaskNo { get; set; }
    public DateTime? FullAuthExpiresAt { get; set; }
    public int QuickSwitchFailureCount { get; set; }
    public DateTime? DisabledAt { get; set; }
    [MaxLength(100)] public string? DisabledBy { get; set; }
}

public static class ClientDeviceMode
{
    public const string Shared = "Shared";
    public const string Personal = "Personal";
}

public static class ClientDeviceStatus
{
    public const string Active = "Active";
    public const string Disabled = "Disabled";
}

[Table("T_DeviceActivation")]
public sealed class DeviceActivation : BaseBizEntity
{
    [Required, MaxLength(64)] public string TokenHash { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string Platform { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string DeviceMode { get; set; } = ClientDeviceMode.Shared;
    [MaxLength(10)] public string? WarehouseCd { get; set; }
    [MaxLength(20)] public string? AreaCd { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    [MaxLength(128)] public string? ConsumedByDeviceId { get; set; }
}

[Table("T_BarcodeAlias")]
public sealed class BarcodeAlias : BaseBizEntity
{
    [Required, MaxLength(256)] public string Barcode { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string BarcodeType { get; set; } = BarcodeTargetType.Product;
    [Required, MaxLength(128)] public string TargetKey { get; set; } = string.Empty;
    [MaxLength(20)] public string? ProductCd { get; set; }
    [MaxLength(30)] public string? LotNo { get; set; }
    [MaxLength(30)] public string? LocationCd { get; set; }
    [MaxLength(10)] public string? PackageUnitCd { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal ConversionRate { get; set; } = 1m;
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public static class BarcodeTargetType
{
    public const string Product = "PRODUCT";
    public const string Lot = "LOT";
    public const string Location = "LOCATION";
    public const string Package = "PACKAGE";
    public const string Serial = "SERIAL";
    public const string Lpn = "LPN";
}

[Table("T_MobileTaskReservation")]
public sealed class MobileTaskReservation : BaseBizEntity
{
    [Required, MaxLength(25)] public string TaskNo { get; set; } = string.Empty;
    [Required, MaxLength(10)] public string WarehouseCd { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string FromLocationCd { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string ToLocationCd { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string ProductCd { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string LotNo { get; set; } = string.Empty;
    [Column(TypeName = "decimal(21,8)")] public decimal ReservedQty { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal ConsumedQty { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal ReleasedQty { get; set; }
    public bool IsActive { get; set; } = true;
}

[Table("T_MobileTaskEvent")]
public sealed class MobileTaskEvent : BaseTenantEntity
{
    [Required, MaxLength(25)] public string TaskNo { get; set; } = string.Empty;
    [Required, MaxLength(40)] public string EventType { get; set; } = string.Empty;
    public Guid? OperationId { get; set; }
    public int ExecutionVersion { get; set; }
    [MaxLength(100)] public string? UserName { get; set; }
    [MaxLength(128)] public string? DeviceId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string? DataJson { get; set; }
}

[Table("T_MobileTaskScanLog")]
public sealed class MobileTaskScanLog : BaseTenantEntity
{
    [Required, MaxLength(25)] public string TaskNo { get; set; } = string.Empty;
    [Required, MaxLength(64)] public string ClientScanNo { get; set; } = string.Empty;
    public int ExecutionVersion { get; set; }
    [Required, MaxLength(30)] public string Step { get; set; } = string.Empty;
    [Required, MaxLength(512)] public string RawBarcode { get; set; } = string.Empty;
    [Required, MaxLength(128)] public string DeviceId { get; set; } = string.Empty;
    [MaxLength(100)] public string? UserName { get; set; }
    public DateTime ScannedAt { get; set; }
    [MaxLength(20)] public string? ParsedKind { get; set; }
    [MaxLength(256)] public string? ParsedValue { get; set; }
    public bool Matched { get; set; }
    [MaxLength(64)] public string? FailureCode { get; set; }
    public DateTime RetainUntil { get; set; }
}

[Table("T_TaskCommandReceipt")]
public sealed class TaskCommandReceipt : BaseTenantEntity
{
    public Guid OperationId { get; set; }
    [Required, MaxLength(128)] public string TaskNo { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string CommandName { get; set; } = string.Empty;
    [Required] public string ResultJson { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}

[Table("T_StockSerial")]
public sealed class StockSerial : BaseBizEntity
{
    [Required, MaxLength(20)] public string ProductCd { get; set; } = string.Empty;
    [Required, MaxLength(128)] public string SerialNo { get; set; } = string.Empty;
    [Required, MaxLength(10)] public string WarehouseCd { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string LocationCd { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string LotNo { get; set; } = string.Empty;
    [MaxLength(64)] public string? LpnNo { get; set; }
    [Required, MaxLength(20)] public string Status { get; set; } = StockSerialStatus.InStock;
    [MaxLength(25)] public string? LastTxnNo { get; set; }
}

public static class StockSerialStatus
{
    public const string InStock = "IN_STOCK";
    public const string Picked = "PICKED";
    public const string Shipped = "SHIPPED";
    public const string Returned = "RETURNED";
    public const string CountException = "COUNT_EXCEPTION";
}

[Table("T_LogisticsUnit")]
public sealed class LogisticsUnit : BaseBizEntity
{
    [Required, MaxLength(64)] public string LpnNo { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string ContainerType { get; set; } = string.Empty;
    [Required, MaxLength(10)] public string WarehouseCd { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string LocationCd { get; set; } = string.Empty;
    [MaxLength(64)] public string? ParentLpnNo { get; set; }
    [Required, MaxLength(20)] public string Status { get; set; } = "ACTIVE";
}

[Table("T_StockSerialTransaction")]
public sealed class StockSerialTransaction : BaseTenantEntity
{
    [Required, MaxLength(25)] public string TxnNo { get; set; } = string.Empty;
    public Guid OperationId { get; set; }
    [Required, MaxLength(20)] public string TxnType { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string ProductCd { get; set; } = string.Empty;
    [Required, MaxLength(128)] public string SerialNo { get; set; } = string.Empty;
    [Required, MaxLength(10)] public string WarehouseCd { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string LotNo { get; set; } = string.Empty;
    [MaxLength(30)] public string? FromLocationCd { get; set; }
    [MaxLength(30)] public string? ToLocationCd { get; set; }
    [MaxLength(64)] public string? LpnNo { get; set; }
    [MaxLength(100)] public string? OperatorCd { get; set; }
    [MaxLength(128)] public string? DeviceId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

[Table("T_LpnEvent")]
public sealed class LpnEvent : BaseTenantEntity
{
    [Required, MaxLength(64)] public string LpnNo { get; set; } = string.Empty;
    public Guid OperationId { get; set; }
    [Required, MaxLength(30)] public string EventType { get; set; } = string.Empty;
    [MaxLength(100)] public string? UserName { get; set; }
    [MaxLength(128)] public string? DeviceId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string? DataJson { get; set; }
}

[Table("T_LpnContent")]
public sealed class LpnContent : BaseBizEntity
{
    [Required, MaxLength(64)] public string LpnNo { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string ProductCd { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string LotNo { get; set; } = string.Empty;
    [MaxLength(128)] public string? SerialNo { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal Qty { get; set; }
}

[Table("T_LpnClosure")]
public sealed class LpnClosure : BaseTenantEntity
{
    [Required, MaxLength(64)] public string AncestorLpnNo { get; set; } = string.Empty;
    [Required, MaxLength(64)] public string DescendantLpnNo { get; set; } = string.Empty;
    public int Depth { get; set; }
}

[Table("T_LpnPolicy")]
public sealed class LpnPolicy : BaseBizEntity
{
    [Required, MaxLength(10)] public string WarehouseCd { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string ContainerType { get; set; } = string.Empty;
    public bool AllowMixedProducts { get; set; }
    public bool AllowMixedLots { get; set; }
}

[Table("T_BarcodeProfile")]
public sealed class BarcodeProfile : BaseBizEntity
{
    [Required, MaxLength(100)] public string ProfileName { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string Format { get; set; } = "CUSTOM";
    [Required, MaxLength(1000)] public string Pattern { get; set; } = string.Empty;
    [Required] public string MappingJson { get; set; } = "{}";
    public int Priority { get; set; } = 100;
    public bool IsEnabled { get; set; } = true;
}

[Table("T_LabelTemplate")]
public sealed class LabelTemplate : BaseBizEntity
{
    [Required, MaxLength(100)] public string TemplateName { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string Format { get; set; } = "ZPL";
    [Required] public string TemplateBody { get; set; } = string.Empty;
    [MaxLength(10)] public string? Language { get; set; }
    public bool IsEnabled { get; set; } = true;
}

[Table("T_LabelJob")]
public sealed class LabelJob : BaseBizEntity
{
    [Required, MaxLength(25)] public string JobNo { get; set; } = string.Empty;
    public Guid OperationId { get; set; }
    [Required, MaxLength(100)] public string TemplateName { get; set; } = string.Empty;
    [Required, MaxLength(10)] public string WarehouseCd { get; set; } = string.Empty;
    [MaxLength(128)] public string? PrinterName { get; set; }
    [Required] public string PayloadJson { get; set; } = "{}";
    [Required, MaxLength(20)] public string Status { get; set; } = LabelJobStatus.Pending;
    [MaxLength(128)] public string? RequestedDeviceId { get; set; }
    [MaxLength(100)] public string? RequestedBy { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public int AttemptCount { get; set; }
    [MaxLength(1000)] public string? ResultMessage { get; set; }
}

public static class LabelJobStatus
{
    public const string Pending = "PENDING";
    public const string Printing = "PRINTING";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";
}
