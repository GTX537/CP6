namespace CP6.Entity.DTOs;

public class BridgeHealthMetricsDto
{
    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }
    public List<BridgeHookStatsDto> Hooks { get; set; } = new();
    public int QueueDepth { get; set; }
    public int DeadLetterCount { get; set; }
    public List<DeadLetterItemDto> DeadLetters { get; set; } = new();
}

public class BridgeHookStatsDto
{
    public string HookName { get; set; } = string.Empty;
    public string SourceModule { get; set; } = string.Empty;
    public string TargetModule { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public int DeadLetterCount { get; set; }
    public decimal SuccessRate { get; set; }
}

public class DeadLetterItemDto
{
    public Guid EventId { get; set; }
    public string HookName { get; set; } = string.Empty;
    public string SourceModule { get; set; } = string.Empty;
    public string TargetModule { get; set; } = string.Empty;
    public string SourceNo { get; set; } = string.Empty;
    public string? TargetNo { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime CreateDate { get; set; }
}
