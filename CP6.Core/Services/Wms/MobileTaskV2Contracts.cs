using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

public sealed class MobileTaskV2Query
{
    public string? AssignedTo { get; set; }
    public bool IncludeUnassigned { get; set; }
    public string? WarehouseCd { get; set; }
    public string? AreaCd { get; set; }
    public int? Status { get; set; }
    public bool OpenOnly { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class MobileTaskV2Dto
{
    public string TaskNo { get; init; } = string.Empty;
    public string TaskType { get; init; } = MobileTaskType.Move;
    public int Status { get; init; }
    public string? AssignedTo { get; init; }
    public int Priority { get; init; }
    public string? WarehouseCd { get; init; }
    public string? AreaCd { get; init; }
    public string? FromLocationCd { get; init; }
    public string? ToLocationCd { get; init; }
    public string? ProductCd { get; init; }
    public string? ProductName { get; init; }
    public string? LotNo { get; init; }
    public decimal Qty { get; init; }
    public decimal ScannedQty { get; init; }
    public string? UnitCd { get; init; }
    public string? Instruction { get; init; }
    public string? Remarks { get; init; }
    public string? SourceType { get; init; }
    public string? SourceNo { get; init; }
    public DateTime? PlannedStartAt { get; init; }
    public DateTime? DueAt { get; init; }
    public string? ParentTaskNo { get; init; }
    public string? RemainderTaskNo { get; init; }
    public string? ExceptionReasonCd { get; init; }
    public string? ExceptionDescription { get; init; }
    public int ExecutionVersion { get; init; }
    public Guid? ExecutionId { get; init; }
    public decimal ReservedSourceQty { get; init; }
    public decimal ReservedTargetCapacityQty { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public Guid? CompletionOperationId { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public abstract class TaskCommand
{
    public Guid OperationId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
    public int? ExecutionVersion { get; set; }
}

public sealed class CreateMoveTaskV2Request
{
    public Guid OperationId { get; set; }
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

public sealed class AssignTaskV2Request : TaskCommand
{
    public string AssignedTo { get; set; } = string.Empty;
}

public sealed class ClaimTaskV2Request : TaskCommand;
public sealed class StartTaskV2Request : TaskCommand;

public sealed class PauseTaskRequest : TaskCommand
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class ReleaseTaskRequest : TaskCommand
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class TakeoverTaskRequest : TaskCommand
{
    public string AssignedTo { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class RaiseTaskExceptionRequest : TaskCommand
{
    public string ReasonCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class ResolveTaskExceptionRequest : TaskCommand
{
    /// <summary>RESUME / REASSIGN / ADJUST / CANCEL.</summary>
    public string Action { get; set; } = string.Empty;
    public string? AssignedTo { get; set; }
    public decimal? Qty { get; set; }
    public string? ToLocationCd { get; set; }
    public string? Remarks { get; set; }
}

public sealed class ScanCommand : TaskCommand
{
    public string Step { get; set; } = string.Empty;
    public string RawBarcode { get; set; } = string.Empty;
    public string ClientScanNo { get; set; } = string.Empty;
    public DateTimeOffset ScannedAt { get; set; }
}

public sealed class ParsedBarcode
{
    public string Kind { get; init; } = BarcodeTargetType.Product;
    public string Value { get; init; } = string.Empty;
    public string? ProductCd { get; init; }
    public string? LotNo { get; init; }
    public string? LocationCd { get; init; }
    public string? PackageUnitCd { get; init; }
    public decimal ConversionRate { get; init; } = 1m;
}

public sealed class ScanResult
{
    public string TaskNo { get; init; } = string.Empty;
    public string Step { get; init; } = string.Empty;
    public string RawBarcode { get; init; } = string.Empty;
    public ParsedBarcode? Parsed { get; init; }
    public bool Matched { get; init; }
    public string? ErrorCode { get; init; }
    public string RecoveryAction { get; init; } = "CONTINUE";
    public int ExecutionVersion { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class CompleteMoveV2Request : TaskCommand
{
    public decimal ScannedQty { get; set; }
    public string ToLocationCd { get; set; } = string.Empty;
    public string? PartialReason { get; set; }
    public string? Remarks { get; set; }
}

public sealed class CancelTaskV2Request : TaskCommand
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class MobileTaskEventDto
{
    public string TaskNo { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public Guid? OperationId { get; init; }
    public int ExecutionVersion { get; init; }
    public string? UserName { get; init; }
    public string? DeviceId { get; init; }
    public DateTime OccurredAt { get; init; }
    public string? DataJson { get; init; }
}

public sealed class TaskScanProfileDto
{
    public string TaskNo { get; init; } = string.Empty;
    public int ExecutionVersion { get; init; }
    public IReadOnlyList<string> Steps { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SupportedSymbologies { get; init; } =
        new[] { "CODE128", "CODE39", "EAN", "UPC", "DATAMATRIX", "QR" };
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class BarcodeAliasDto
{
    public Guid Id { get; init; }
    public string Barcode { get; init; } = string.Empty;
    public string BarcodeType { get; init; } = string.Empty;
    public string TargetKey { get; init; } = string.Empty;
    public string? ProductCd { get; init; }
    public string? LotNo { get; init; }
    public string? LocationCd { get; init; }
    public string? PackageUnitCd { get; init; }
    public decimal ConversionRate { get; init; }
    public DateTime? ValidFrom { get; init; }
    public DateTime? ValidUntil { get; init; }
    public bool IsEnabled { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class UpsertBarcodeAliasRequest
{
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
    public string? RowVersion { get; set; }
}

public sealed class BarcodeImportRow
{
    public int RowNumber { get; init; }
    public bool Valid { get; init; }
    public string? ErrorCode { get; init; }
    public BarcodeAliasDto? Item { get; init; }
}

public sealed class BarcodeImportResult
{
    public bool Committed { get; init; }
    public int ValidCount { get; init; }
    public int InvalidCount { get; init; }
    public IReadOnlyList<BarcodeImportRow> Rows { get; init; } = Array.Empty<BarcodeImportRow>();
}

public sealed class TaskAnalyticsQuery
{
    public string? WarehouseCd { get; set; }
    public string? AreaCd { get; set; }
    public string? AssignedTo { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public sealed class TaskAnalyticsDto
{
    public int Created { get; init; }
    public int Completed { get; init; }
    public int PartiallyCompleted { get; init; }
    public int Exceptions { get; init; }
    public int Overdue { get; init; }
    public double AverageMinutes { get; init; }
}
