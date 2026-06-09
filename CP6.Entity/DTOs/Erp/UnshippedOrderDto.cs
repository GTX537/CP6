namespace CP6.Entity.DTOs.Erp;

/// <summary>Dashboard unshipped order row item.</summary>
public class UnshippedOrderItemDto
{
    public string WebOrderNo { get; set; } = string.Empty;
    public string CustomerCd { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public DateTime? OrderDate { get; set; }
    public DateTime? CustomerDeliveryDate { get; set; }
    /// <summary>Phase 6 order lifecycle (CONFIRMED / IN_PRODUCTION / PARTIALLY_CANCELLED)</summary>
    public string OrderStatus { get; set; } = string.Empty;
    /// <summary>0=Not shipped / 5=Partially shipped / 9=Shipped</summary>
    public int ShipStatus { get; set; }
    public decimal OrderedQty { get; set; }
    public decimal ShippedQty { get; set; }
    /// <summary>Remaining = OrderedQty - ShippedQty</summary>
    public decimal RemainingQty { get; set; }
    /// <summary>Overdue when CustomerDeliveryDate is before today.</summary>
    public bool IsOverdue { get; set; }
    public int? DaysUntilDue { get; set; }
    /// <summary>Aggregated MES status across related work orders.</summary>
    public string? MesStatusSummary { get; set; }
    /// <summary>Aggregated WMS status across related outbounds.</summary>
    public string? WmsStatusSummary { get; set; }
}

public class UnshippedOrderQuery
{
    public string? CustomerCd { get; set; }
    public bool? OnlyOverdue { get; set; }
    /// <summary>Pagination page is 1-indexed.</summary>
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? SortField { get; set; }
    public string? SortOrder { get; set; }
}
