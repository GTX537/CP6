namespace CP6.Entity.DTOs.Mes;

/// <summary>MES-MASTER-OWNER/1: decimal(21,8) values are exact invariant strings; versions are opaque base64.</summary>
public sealed class WorkCenterOwnerV1
{
    public Guid? Id { get; set; }
    public byte[]? RowVersion { get; set; }
    public string WgCd { get; set; } = "";
    public string? WgName { get; set; }
    public string? DailyCapacityHours { get; set; }
    public bool Enable { get; set; } = true;
}

public sealed class ProcessCostRateOwnerV1
{
    public Guid? Id { get; set; }
    public byte[]? RowVersion { get; set; }
    public string WgCd { get; set; } = "";
    // Read: null means hidden. Write: null means preserve (both are required on creation).
    public string? LaborRate { get; set; }
    public string? OverheadRate { get; set; }
    public bool LaborRateWritable { get; set; }
    public bool OverheadRateWritable { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
}

public sealed record MesMasterDeleteV1(Guid Id, byte[]? RowVersion);
