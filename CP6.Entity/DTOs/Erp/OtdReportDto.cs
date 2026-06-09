namespace CP6.Entity.DTOs.Erp;

public static class OtdReportGroupBy
{
    public const string Customer = "customer";
    public const string Month = "month";
}

public class OtdReportQuery
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string GroupBy { get; set; } = OtdReportGroupBy.Customer;
    public string? CustomerCd { get; set; }
}

public class OtdReportSummaryDto
{
    public int TotalShippedOrders { get; set; }
    public int OnTimeCount { get; set; }
    public int LateCount { get; set; }
    public decimal OnTimeRate { get; set; }
    public List<OtdReportRowDto> Rows { get; set; } = new();
}

public class OtdReportRowDto
{
    public string GroupKey { get; set; } = string.Empty;
    public string GroupLabel { get; set; } = string.Empty;
    public int TotalShippedOrders { get; set; }
    public int OnTimeCount { get; set; }
    public int LateCount { get; set; }
    public decimal OnTimeRate { get; set; }
    public decimal AvgLateDays { get; set; }
}
