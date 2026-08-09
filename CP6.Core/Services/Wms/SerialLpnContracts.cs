using CP6.Entity.DomainModels.Erp;

namespace CP6.Core.Services.Wms;

public sealed class StockSerialDto
{
    public string ProductCd { get; init; } = string.Empty;
    public string SerialNo { get; init; } = string.Empty;
    public string WarehouseCd { get; init; } = string.Empty;
    public string LocationCd { get; init; } = string.Empty;
    public string LotNo { get; init; } = string.Empty;
    public string? LpnNo { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? LastTxnNo { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class ExistingSerialInput
{
    public string SerialNo { get; set; } = string.Empty;
    public string WarehouseCd { get; set; } = string.Empty;
    public string LocationCd { get; set; } = string.Empty;
    public string LotNo { get; set; } = string.Empty;
}

public sealed class EnableSerialTrackingRequest
{
    public Guid OperationId { get; set; }
    public string ProductCd { get; set; } = string.Empty;
    public int TrackingMode { get; set; } = ProductTrackingMode.Serial;
    public List<ExistingSerialInput> ExistingSerials { get; set; } = new();
}

public sealed class SerialLifecycleRequest
{
    public Guid OperationId { get; set; }
    public string TxnType { get; set; } = string.Empty;
    public string ProductCd { get; set; } = string.Empty;
    public List<string> SerialNos { get; set; } = new();
    public string WarehouseCd { get; set; } = string.Empty;
    public string LotNo { get; set; } = string.Empty;
    public string? FromLocationCd { get; set; }
    public string? ToLocationCd { get; set; }
    public string? LpnNo { get; set; }
    public string? DeviceId { get; set; }
}

public sealed class SerialOperationResult
{
    public Guid OperationId { get; init; }
    public string TxnType { get; init; } = string.Empty;
    public string ProductCd { get; init; } = string.Empty;
    public int SerialCount { get; init; }
    public IReadOnlyList<string> StockTxnNos { get; init; } = Array.Empty<string>();
    public IReadOnlyList<StockSerialDto> Serials { get; init; } = Array.Empty<StockSerialDto>();
}

public sealed class LogisticsUnitDto
{
    public string LpnNo { get; init; } = string.Empty;
    public string ContainerType { get; init; } = string.Empty;
    public string WarehouseCd { get; init; } = string.Empty;
    public string LocationCd { get; init; } = string.Empty;
    public string? ParentLpnNo { get; init; }
    public string Status { get; init; } = string.Empty;
    public IReadOnlyList<LpnContentDto> Contents { get; init; } = Array.Empty<LpnContentDto>();
    public IReadOnlyList<string> ChildLpns { get; init; } = Array.Empty<string>();
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class LpnContentDto
{
    public string ProductCd { get; init; } = string.Empty;
    public string LotNo { get; init; } = string.Empty;
    public string? SerialNo { get; init; }
    public decimal Qty { get; init; }
}

public sealed class CreateLpnRequest
{
    public Guid OperationId { get; set; }
    public string LpnNo { get; set; } = string.Empty;
    public string ContainerType { get; set; } = string.Empty;
    public string WarehouseCd { get; set; } = string.Empty;
    public string LocationCd { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
}

public sealed class LpnContentInput
{
    public string ProductCd { get; set; } = string.Empty;
    public string LotNo { get; set; } = string.Empty;
    public string? SerialNo { get; set; }
    public decimal Qty { get; set; }
}

public abstract class LpnCommand
{
    public Guid OperationId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
}

public sealed class PackLpnRequest : LpnCommand
{
    public List<string> ChildLpns { get; set; } = new();
    public List<LpnContentInput> Contents { get; set; } = new();
}

public sealed class UnpackLpnRequest : LpnCommand
{
    public List<string> ChildLpns { get; set; } = new();
    public List<string> SerialNos { get; set; } = new();
}

public sealed class MoveLpnRequest : LpnCommand
{
    public string ToLocationCd { get; set; } = string.Empty;
}

public sealed class SplitLpnRequest : LpnCommand
{
    public string TargetLpnNo { get; set; } = string.Empty;
    public string TargetContainerType { get; set; } = string.Empty;
    public List<string> SerialNos { get; set; } = new();
    public List<string> ChildLpns { get; set; } = new();
}

public sealed class MergeLpnRequest : LpnCommand
{
    public string SourceLpnNo { get; set; } = string.Empty;
}

public sealed class LpnPolicyRequest
{
    public string WarehouseCd { get; set; } = string.Empty;
    public string ContainerType { get; set; } = string.Empty;
    public bool AllowMixedProducts { get; set; }
    public bool AllowMixedLots { get; set; }
}
