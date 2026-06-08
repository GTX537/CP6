namespace CP6.Entity.DTOs;

public class BackorderQueueQuery
{
    public string? CustomerCd { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
}

public class BackorderQueueItemDto
{
    public string WebOrderNo { get; set; } = string.Empty;
    public string CustomerCd { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public int DetailNo { get; set; }
    public string ProductCd { get; set; } = string.Empty;
    public decimal OrderedQty { get; set; }
    public decimal ShippedQty { get; set; }
    public decimal BackorderQty { get; set; }
    public decimal RemainingQty { get; set; }
    public DateTime? LastShipDate { get; set; }
}

public class BackorderActionRequest
{
    public string? Reason { get; set; }
}

public class BackorderActionResultDto
{
    public string WebOrderNo { get; set; } = string.Empty;
    public int DetailNo { get; set; }
    public decimal BackorderQty { get; set; }
    public decimal RemainingQty { get; set; }
    public string? NewWebOrderNo { get; set; }
}
