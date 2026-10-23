namespace CP6.Entity.DTOs.Wms;

/// <summary>Read-only observations of stored stock, not a Finance valuation or approved disposal basis.</summary>
public static class InventoryObservationV1
{
    public const string Version = "INVENTORY-OBSERVATION-OWNER/1";
}
public sealed class StockDwellObservationV1
{
    public string Version => InventoryObservationV1.Version;
    public DateTime AsOfDate { get; init; }
    public string GroupBy { get; init; } = "product";
    public string TotalQty { get; init; } = "0";
    public string Over90Qty { get; init; } = "0";
    public string? ReferenceValue { get; init; }
    public string? Over90ReferenceValue { get; init; }
    public bool ReferenceReadable { get; init; }
    public bool CustomerNameReadable { get; init; }
    public int? MissingPriceCount { get; init; }
    public string ValuationStatus => "UNKNOWN";
    public string? Currency => null;
    public List<StockDwellObservationRowV1> Rows { get; init; } = [];
}
public sealed class StockDwellObservationRowV1
{
    public string GroupKey { get; init; } = "";
    public string GroupLabel { get; init; } = "";
    public string TotalQty { get; init; } = "0";
    public string? ReferenceValue { get; init; }
    public string Bucket0To30Qty { get; init; } = "0";
    public string Bucket31To60Qty { get; init; } = "0";
    public string Bucket61To90Qty { get; init; } = "0";
    public string BucketOver90Qty { get; init; } = "0";
    public DateTime OldestReceiveDate { get; init; }
    public int OldestAgeDays { get; init; }
}
public sealed class ExpiryObservationQueryV1
{
    public DateTime? AsOfDate { get; set; }
    public int Days { get; set; } = 30;
    public string? WarehouseCd { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
public sealed class ExpiryObservationV1
{
    public string Version => InventoryObservationV1.Version;
    public DateTime AsOfDate { get; init; }
    public int Days { get; init; }
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public bool UnitPriceReadable { get; init; }
    public bool ReferenceReadable { get; init; }
    public string ValuationStatus => "UNKNOWN";
    public string? Currency => null;
    public List<ExpiryObservationRowV1> Rows { get; init; } = [];
}
public sealed class ExpiryObservationRowV1
{
    public Guid StockId { get; init; }
    public string WarehouseCd { get; init; } = "";
    public string LocationCd { get; init; } = "";
    public string ProductCd { get; init; } = "";
    public string LotNo { get; init; } = "";
    public string PhysicalQty { get; init; } = "0";
    public string AvailableQty { get; init; } = "0";
    public string? UnitPrice { get; init; }
    public string? ReferenceValue { get; init; }
    public DateTime ExpiryDate { get; init; }
    public int DaysUntilExpiry { get; init; }
    public string? UnitCd { get; init; }
}
