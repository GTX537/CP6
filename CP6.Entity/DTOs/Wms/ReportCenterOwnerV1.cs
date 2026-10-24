namespace CP6.Entity.DTOs.Wms;

public sealed class ReportQueryV1
{
    public string? WarehouseCd { get; set; }
    public string? ProductCd { get; set; }
    public string? LotNo { get; set; }
    public string? TxnType { get; set; }
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
/// <summary>Current authorized raw records; no frozen report, derived rank, quantity sum or valuation.</summary>
public sealed class ReportPageV1<T>
{
    public string Version => "REPORT-CENTER-OWNER/1";
    public string Coverage => "CURRENT_AUTHORIZED_RECORDS";
    public string Source { get; init; } = "";
    public string SnapshotState => "NOT_CAPTURED";
    public string BusinessTimeState => "NOT_RECONSTRUCTED";
    public string ValuationState => "UNKNOWN";
    public string FreeState => "NOT_EVALUATED";
    public bool ExportAvailable => false;
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public List<T> Items { get; init; } = [];
}
public sealed class ReportStockRowV1
{
    public Guid Id { get; init; }
    public string WarehouseCd { get; init; } = "";
    public string LocationCd { get; init; } = "";
    public string ProductCd { get; init; } = "";
    public string LotNo { get; init; } = "";
    public string PhysicalQty { get; init; } = "0";
    public string LegacyAllocatedQty { get; init; } = "0";
    public string LegacyAvailableQty { get; init; } = "0";
    public string? UnitCd { get; init; }
    public string UnitState => string.IsNullOrWhiteSpace(UnitCd) ? "NOT_RECORDED" : "RECORDED";
    public string? OwnerType { get; init; }
    public string OwnerState => OwnerType is "SELF" or "CUSTOMER" ? "RECORDED" : "UNKNOWN";
    public string? OwnerCd { get; init; }
    public bool OwnerCdReadable { get; init; }
    public DateTime? ReceiveDate { get; init; }
    public DateTime? ExpiryDate { get; init; }
    public string? FreeQty => null;
    public string? Valuation => null;
}
public sealed class ReportTransactionRowV1
{
    public Guid Id { get; init; }
    public string TxnNo { get; init; } = "";
    public string TxnType { get; init; } = "";
    public DateTime TxnDateTime { get; init; }
    public string WarehouseCd { get; init; } = "";
    public string LocationCd { get; init; } = "";
    public string ProductCd { get; init; } = "";
    public string LotNo { get; init; } = "";
    public string Qty { get; init; } = "0";
    public string? UnitCd { get; init; }
    public string UnitState => string.IsNullOrWhiteSpace(UnitCd) ? "NOT_RECORDED" : "RECORDED";
    public string? OwnerType { get; init; }
    public string OwnerState => OwnerType is "SELF" or "CUSTOMER" ? "RECORDED" : "UNKNOWN";
    public string? OwnerCd { get; init; }
    public bool OwnerCdReadable { get; init; }
    public string? OperatorCd { get; init; }
    public bool OperatorCdReadable { get; init; }
    public string? Remark { get; init; }
    public bool RemarkReadable { get; init; }
    public string QuantityMeaning => "ORIGINAL_UNSIGNED_OR_SIGNED_LEGACY_VALUE";
    public string OccurrenceState => "NOT_RECONSTRUCTED";
    public string TimeBasis => "RECORDED_TXN_DATETIME_WITHOUT_SITE_TIMEZONE";
    public string RelatedSourceState => "SOURCE_DISCLOSURE_UNAVAILABLE";
    public string? RelatedNo => null;
    public string? RelatedType => null;
}
