namespace CP6.Core.Services.Wms;

public sealed class CreateWmsFeatureFlagChangeRequest
{
    public Guid OperationId { get; set; }
    public string WarehouseCd { get; set; } = string.Empty;
    public bool ProductionMoveEnabled { get; set; }
    public bool SerialLpnEnabled { get; set; }
    public int ScanRetentionDays { get; set; } = 180;
    public string RowVersion { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string ChangeTicket { get; set; } = string.Empty;
    public string? EvidenceUri { get; set; }
}

public sealed class WmsFeatureFlagChangeQuery
{
    public string? WarehouseCd { get; set; }
    public string? Status { get; set; }
}

public sealed class WmsFeatureFlagChangeDto
{
    public Guid Id { get; init; }
    public Guid OperationId { get; init; }
    public string WarehouseCd { get; init; } = string.Empty;
    public bool BaseProductionMoveEnabled { get; init; }
    public bool BaseSerialLpnEnabled { get; init; }
    public int BaseScanRetentionDays { get; init; }
    public string BaseFeatureRowVersion { get; init; } = string.Empty;
    public bool TargetProductionMoveEnabled { get; init; }
    public bool TargetSerialLpnEnabled { get; init; }
    public int TargetScanRetentionDays { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string ChangeTicket { get; init; } = string.Empty;
    public string? EvidenceUri { get; init; }
    public string Status { get; init; } = string.Empty;
    public Guid RequestedById { get; init; }
    public DateTime RequestedAtUtc { get; init; }
    public Guid FlowInstanceId { get; init; }
    public Guid? DecidedById { get; init; }
    public DateTime? DecidedAtUtc { get; init; }
    public DateTime? AppliedAtUtc { get; init; }
    public string? FailureCode { get; init; }
}

public sealed class WmsFeatureFlagChangeException : Exception
{
    public WmsFeatureFlagChangeException(string code) : base(code) => Code = code;
    public string Code { get; }
}
