namespace CP6.Entity.DTOs.Wms;

public sealed class MaterialShortagePageV1
{
    public string Version => "MATERIAL-SHORTAGE-OWNER/1";
    public int Total { get; init; }
    public int OpenCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public bool RemarkReadable { get; init; }
    public bool RemarkWritable { get; init; }
    public List<MaterialShortageRowV1> Rows { get; init; } = [];
}

public sealed class MaterialShortageRowV1
{
    public Guid Id { get; init; }
    public byte[]? RowVersion { get; init; }
    public string WorkOrderNo { get; init; } = "";
    public string? RelatedOutboundNo { get; init; }
    public string ProductCd { get; init; } = "";
    public string? LotNo { get; init; }
    public string RequiredQty { get; init; } = "0";
    public string AvailableQty { get; init; } = "0";
    public string ShortQty { get; init; } = "0";
    public DateTime DetectedAt { get; init; }
    public DateTime? ResolvedAt { get; init; }
    public string Status { get; init; } = "";
    public string? Remark { get; init; }
}

public sealed class MaterialShortageActionV1
{
    public byte[]? RowVersion { get; set; }
    public bool SetRemark { get; set; }
    public string? Remark { get; set; }
}
