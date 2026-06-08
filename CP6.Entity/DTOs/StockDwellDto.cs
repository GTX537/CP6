namespace CP6.Entity.DTOs;

public static class StockDwellGroupBy
{
    public const string Product = "product";
    public const string Customer = "customer";
}

public class StockDwellQuery
{
    public string GroupBy { get; set; } = StockDwellGroupBy.Product;
    public string? WarehouseCd { get; set; }
    public string? ProductCd { get; set; }
    public string? OwnerCd { get; set; }
    public DateTime? AsOfDate { get; set; }
}

public class StockDwellSummaryDto
{
    public DateTime AsOfDate { get; set; }
    public string GroupBy { get; set; } = StockDwellGroupBy.Product;
    public decimal TotalQty { get; set; }
    public decimal TotalValue { get; set; }
    public decimal Over90Qty { get; set; }
    public decimal Over90Value { get; set; }
    public List<StockDwellRowDto> Rows { get; set; } = new();
}

public class StockDwellRowDto
{
    public string GroupKey { get; set; } = string.Empty;
    public string GroupLabel { get; set; } = string.Empty;
    public decimal TotalQty { get; set; }
    public decimal TotalValue { get; set; }
    public decimal Bucket0To30Qty { get; set; }
    public decimal Bucket31To60Qty { get; set; }
    public decimal Bucket61To90Qty { get; set; }
    public decimal BucketOver90Qty { get; set; }
    public DateTime? OldestReceiveDate { get; set; }
    public int OldestAgeDays { get; set; }
}
