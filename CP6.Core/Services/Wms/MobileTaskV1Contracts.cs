using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

public sealed class MobileTaskV1Query
{
    public string? AssignedTo { get; set; }
    public bool IncludeUnassigned { get; set; }
    public int? Status { get; set; }
    public bool OpenOnly { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class MobileTaskV1Dto
{
    public string TaskNo { get; init; } = string.Empty;
    public string TaskType { get; init; } = MobileTaskType.Move;
    public int Status { get; init; }
    public string? AssignedTo { get; init; }
    public int Priority { get; init; }
    public string? WarehouseCd { get; init; }
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
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public Guid? CompletionOperationId { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class CreateMoveTaskRequest
{
    public string? AssignedTo { get; set; }
    public int Priority { get; set; } = 2;
    public string WarehouseCd { get; set; } = string.Empty;
    public string FromLocationCd { get; set; } = string.Empty;
    public string ToLocationCd { get; set; } = string.Empty;
    public string ProductCd { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public string? LotNo { get; set; }
    public decimal Qty { get; set; }
    public string? UnitCd { get; set; }
    public string? Instruction { get; set; }
    public string? Remarks { get; set; }
}

public sealed class AssignTaskRequest
{
    public string AssignedTo { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class ClaimTaskRequest
{
    public string DeviceId { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class StartTaskRequest
{
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class CompleteMoveRequest
{
    public Guid OperationId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public decimal ScannedQty { get; set; }
    public string ToLocationCd { get; set; } = string.Empty;
    public string? Remarks { get; set; }
}

public sealed class CancelTaskRequest
{
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class MobileTaskNotFoundException : InvalidOperationException
{
    public MobileTaskNotFoundException() : base("WM-MSG-070") { }
}

public sealed class MobileTaskConflictException : InvalidOperationException
{
    public MobileTaskConflictException(string code) : base(code) => Code = code;
    public string Code { get; }
}
