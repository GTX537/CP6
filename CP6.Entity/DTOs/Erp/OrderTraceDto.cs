namespace CP6.Entity.DTOs.Erp;

public class OrderTraceDto
{
    public string WebOrderNo { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public DateTime? OrderDate { get; set; }
    public OrderTraceSummaryDto Summary { get; set; } = new();
    public List<OrderTraceTimelineItemDto> Timeline { get; set; } = new();
}

public class OrderTraceSummaryDto
{
    public int TotalEvents { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }
    public int DeadCount { get; set; }
    public int DistinctCorrelationIds { get; set; }
}

public class OrderTraceTimelineItemDto
{
    public DateTime EventTime { get; set; }
    public string EventKind { get; set; } = "BRIDGE_HOOK";
    public string SourceModule { get; set; } = string.Empty;
    public string TargetModule { get; set; } = string.Empty;
    public string HookName { get; set; } = string.Empty;
    public string SourceNo { get; set; } = string.Empty;
    public string? TargetNo { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
    public Guid CorrelationId { get; set; }
}
