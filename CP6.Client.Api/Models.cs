using System.Text.Json.Serialization;

namespace CP6.Client.Api;

public sealed class ClientContext
{
    public string ClientKind { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string? PlatformVersion { get; set; }
}

public sealed class NativeLoginRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? TenantCode { get; set; }
    public ClientContext Client { get; set; } = new();
}

public class NativeChallengeRequest
{
    public string ChallengeToken { get; set; } = string.Empty;
    public ClientContext Client { get; set; } = new();
}

public sealed class NativeTwoFactorRequest : NativeChallengeRequest
{
    public string Code { get; set; } = string.Empty;
    public string? Method { get; set; }
}

public sealed class TwoFactorSetup
{
    public string OtpAuthUri { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
}

public sealed class NativeRefreshRequest
{
    public string RefreshToken { get; set; } = string.Empty;
    public ClientContext Client { get; set; } = new();
}

public sealed class NativeLogoutRequest
{
    public string? RefreshToken { get; set; }
    public ClientContext Client { get; set; } = new();
}

public sealed class QuickSwitchRequest
{
    public string TenantCode { get; set; } = string.Empty;
    public string BadgeNo { get; set; } = string.Empty;
    public string Pin { get; set; } = string.Empty;
    public ClientContext Client { get; set; } = new();
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

public sealed class ActivatedClientDevice
{
    public string DeviceId { get; set; } = string.Empty;
    public string TenantCode { get; set; } = string.Empty;
    public string DeviceMode { get; set; } = string.Empty;
    public string? WarehouseCd { get; set; }
    public string? AreaCd { get; set; }
    public DateTimeOffset ActivatedAt { get; set; }
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

public sealed class ClientDevice
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceMode { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? WarehouseCd { get; set; }
    public string? AreaCd { get; set; }
    public string? AppVersion { get; set; }
    public string? PlatformVersion { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public int? BatteryPercent { get; set; }
    public string? NetworkType { get; set; }
    public string? CurrentUser { get; set; }
    public string? CurrentTaskNo { get; set; }
    public DateTime? FullAuthExpiresAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class UpdateClientDeviceRequest
{
    public string RowVersion { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? DeviceMode { get; set; }
    public string? WarehouseCd { get; set; }
    public string? AreaCd { get; set; }
}

public sealed class TaskAnalytics
{
    public int Created { get; set; }
    public int Completed { get; set; }
    public int PartiallyCompleted { get; set; }
    public int Exceptions { get; set; }
    public int Overdue { get; set; }
    public double AverageMinutes { get; set; }
}

public sealed class BarcodeAlias
{
    public Guid Id { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string BarcodeType { get; set; } = string.Empty;
    public string TargetKey { get; set; } = string.Empty;
    public string? ProductCd { get; set; }
    public string? LotNo { get; set; }
    public string? LocationCd { get; set; }
    public string? PackageUnitCd { get; set; }
    public decimal ConversionRate { get; set; } = 1m;
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class UpsertBarcodeAliasRequest
{
    public Guid? Id { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string BarcodeType { get; set; } = "PRODUCT";
    public string TargetKey { get; set; } = string.Empty;
    public string? ProductCd { get; set; }
    public string? LotNo { get; set; }
    public string? LocationCd { get; set; }
    public string? PackageUnitCd { get; set; }
    public decimal ConversionRate { get; set; } = 1m;
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? RowVersion { get; set; }
}

public sealed class LabelJob
{
    public string JobNo { get; set; } = string.Empty;
    public Guid OperationId { get; set; }
    public string WarehouseCd { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string TemplateBody { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string? PrinterName { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? RequestedDeviceId { get; set; }
    public string? RequestedBy { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int AttemptCount { get; set; }
    public string? ResultMessage { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class LabelJobCommand
{
    public Guid OperationId { get; set; } = Guid.NewGuid();
    public string RowVersion { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string? ResultMessage { get; set; }
}

public sealed class ClientMenu
{
    public int Id { get; set; }
    public string MenuName { get; set; } = string.Empty;
    public string? RoutePath { get; set; }
    public string? Icon { get; set; }
    public int? ParentId { get; set; }
    public int OrderNo { get; set; }
}

public sealed class ClientProfile
{
    public string UserName { get; set; } = string.Empty;
    public string? NickName { get; set; }
    public int? RoleId { get; set; }
    public List<ClientMenu> Menus { get; set; } = new();
    public bool MustChangePassword { get; set; }
    public bool IsPlatformAdmin { get; set; }
}

public sealed class TokenSession
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTimeOffset AccessExpiresAt { get; set; }
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset RefreshExpiresAt { get; set; }
    public ClientProfile Profile { get; set; } = new();
}

public sealed class NativeAuthResult
{
    public string State { get; set; } = string.Empty;
    public string? ChallengeToken { get; set; }
    public TokenSession? Session { get; set; }
}

public sealed class NativeSsoStartRequest
{
    public string TenantCode { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string CodeChallenge { get; set; } = string.Empty;
    public ClientContext Client { get; set; } = new();
}

public sealed class NativeSsoStartResponse
{
    public string AuthorizeUrl { get; set; } = string.Empty;
}

public sealed class NativeSsoExchangeRequest
{
    public string GrantCode { get; set; } = string.Empty;
    public string CodeVerifier { get; set; } = string.Empty;
    public ClientContext Client { get; set; } = new();
}

public sealed class ClientBootstrap
{
    public string ApiVersion { get; set; } = "1";
    public DateTimeOffset ServerUtc { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string CurrentVersion { get; set; } = string.Empty;
    public string LatestVersion { get; set; } = string.Empty;
    public string MinimumVersion { get; set; } = string.Empty;
    public bool UpgradeRequired { get; set; }
    public string? DownloadUrl { get; set; }
    public string? Sha256 { get; set; }
    public string LanguageManifestVersion { get; set; } = string.Empty;
}

public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public sealed class MobileTask
{
    public string TaskNo { get; set; } = string.Empty;
    public string TaskType { get; set; } = "MOVE";
    public int Status { get; set; }
    public string? AssignedTo { get; set; }
    public int Priority { get; set; }
    public string? WarehouseCd { get; set; }
    public string? AreaCd { get; set; }
    public string? FromLocationCd { get; set; }
    public string? ToLocationCd { get; set; }
    public string? ProductCd { get; set; }
    public string? ProductName { get; set; }
    public string? LotNo { get; set; }
    public decimal Qty { get; set; }
    public decimal ScannedQty { get; set; }
    public string? UnitCd { get; set; }
    public string? Instruction { get; set; }
    public string? Remarks { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid? CompletionOperationId { get; set; }
    public string? SourceType { get; set; }
    public string? SourceNo { get; set; }
    public DateTime? PlannedStartAt { get; set; }
    public DateTime? DueAt { get; set; }
    public string? ParentTaskNo { get; set; }
    public string? RemainderTaskNo { get; set; }
    public string? ExceptionReasonCd { get; set; }
    public string? ExceptionDescription { get; set; }
    public int ExecutionVersion { get; set; }
    public Guid? ExecutionId { get; set; }
    public decimal ReservedSourceQty { get; set; }
    public decimal ReservedTargetCapacityQty { get; set; }
    public string RowVersion { get; set; } = string.Empty;

    public string SourceReference
        => string.IsNullOrWhiteSpace(SourceType)
           && string.IsNullOrWhiteSpace(SourceNo)
            ? "Manual"
            : $"{SourceType ?? "SOURCE"} / {SourceNo ?? "-"}";

    public string SourceLinkState
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ParentTaskNo))
                return $"Remainder of {ParentTaskNo}";
            if (!string.IsNullOrWhiteSpace(RemainderTaskNo))
                return $"Remainder task {RemainderTaskNo}";
            return string.IsNullOrWhiteSpace(SourceType)
                   && string.IsNullOrWhiteSpace(SourceNo)
                ? "Unlinked"
                : "Linked";
        }
    }
}

public sealed class CreateMoveTaskRequest
{
    public Guid OperationId { get; set; } = Guid.NewGuid();
    public string? AssignedTo { get; set; }
    public int Priority { get; set; } = 2;
    public string WarehouseCd { get; set; } = string.Empty;
    public string? AreaCd { get; set; }
    public string FromLocationCd { get; set; } = string.Empty;
    public string ToLocationCd { get; set; } = string.Empty;
    public string ProductCd { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public string? LotNo { get; set; }
    public decimal Qty { get; set; }
    public string? UnitCd { get; set; }
    public string? Instruction { get; set; }
    public string? Remarks { get; set; }
    public string? SourceType { get; set; }
    public string? SourceNo { get; set; }
    public DateTime? PlannedStartAt { get; set; }
    public DateTime? DueAt { get; set; }
}

public sealed class AssignTaskRequest
{
    public Guid OperationId { get; set; } = Guid.NewGuid();
    public string AssignedTo { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
    public int? ExecutionVersion { get; set; }
}

public sealed class ClaimTaskRequest
{
    public Guid OperationId { get; set; } = Guid.NewGuid();
    public string DeviceId { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
    public int? ExecutionVersion { get; set; }
}

public sealed class StartTaskRequest
{
    public Guid OperationId { get; set; } = Guid.NewGuid();
    public string RowVersion { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
    public int? ExecutionVersion { get; set; }
}

public sealed class ScanRequest
{
    public Guid OperationId { get; set; } = Guid.NewGuid();
    public string RowVersion { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public int ExecutionVersion { get; set; }
    public string Step { get; set; } = string.Empty;
    public string RawBarcode { get; set; } = string.Empty;
    public string ClientScanNo { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset ScannedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ScanResult
{
    public string Kind { get; set; } = "UNKNOWN";
    public string Barcode { get; set; } = string.Empty;
    public string? LocationCd { get; set; }
    public string? LocationName { get; set; }
    public bool? IsBlocked { get; set; }
    public string? ProductCd { get; set; }
    public bool? Matched { get; set; }
    public string? Message { get; set; }
    public string TaskNo { get; set; } = string.Empty;
    public string Step { get; set; } = string.Empty;
    public string RawBarcode { get; set; } = string.Empty;
    public ParsedBarcode? Parsed { get; set; }
    public string? ErrorCode { get; set; }
    public string RecoveryAction { get; set; } = string.Empty;
    public int ExecutionVersion { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class CompleteMoveRequest
{
    public Guid OperationId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public decimal ScannedQty { get; set; }
    public string ToLocationCd { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
    public int? ExecutionVersion { get; set; }
    public string? PartialReason { get; set; }
    public string? Remarks { get; set; }
}

public sealed class CancelTaskRequest
{
    public Guid OperationId { get; set; } = Guid.NewGuid();
    public string RowVersion { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
    public int? ExecutionVersion { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class ParsedBarcode
{
    public string Kind { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? ProductCd { get; set; }
    public string? LotNo { get; set; }
    public string? LocationCd { get; set; }
    public string? PackageUnitCd { get; set; }
    public decimal ConversionRate { get; set; } = 1m;
}

public sealed class TaskScanProfile
{
    public string TaskNo { get; set; } = string.Empty;
    public int ExecutionVersion { get; set; }
    public IReadOnlyList<string> Steps { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> SupportedSymbologies { get; set; } = Array.Empty<string>();
}

public sealed class PauseTaskRequest
{
    public Guid OperationId { get; set; } = Guid.NewGuid();
    public string RowVersion { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
    public int? ExecutionVersion { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class TakeoverTaskRequest
{
    public Guid OperationId { get; set; } = Guid.NewGuid();
    public string RowVersion { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
    public int? ExecutionVersion { get; set; }
    public string AssignedTo { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class RaiseTaskExceptionRequest
{
    public Guid OperationId { get; set; } = Guid.NewGuid();
    public string RowVersion { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
    public int? ExecutionVersion { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class LangManifest
{
    public string Version { get; set; } = string.Empty;
}

public sealed class ApiError
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? Status { get; set; }
    public string? Code { get; set; }
    public string? Message { get; set; }
    public string? Title { get; set; }
}
