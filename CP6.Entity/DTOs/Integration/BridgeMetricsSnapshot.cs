namespace CP6.Entity.DTOs.Integration;

/// <summary>
/// Prometheus /metrics 用のブリッジ指標スナップショット（T15 / Gap 2.3）。
/// </summary>
/// <remarks>
/// IntegrationEvent テーブルを単一の真実とし、スクレイプ毎に集計する。
/// プロセス内カウンタと異なり再起動で値が失われない（DB が持久化担当）。
/// </remarks>
public class BridgeMetricsSnapshot
{
    /// <summary>(HookName, Status) ごとの件数 → cp6_bridge_hook_total{hook,status}</summary>
    public List<BridgeHookStatusCount> HookStatusCounts { get; set; } = new();

    /// <summary>Status=FAILED（リトライ待ち）件数 → cp6_bridge_retry_queue_depth</summary>
    public int RetryQueueDepth { get; set; }

    /// <summary>Status=DEAD（死信）件数 → cp6_integration_event_dead_letter_total</summary>
    public int DeadLetterCount { get; set; }
}

/// <summary>
/// (HookName, Status) 単位の集計件数。
/// </summary>
public class BridgeHookStatusCount
{
    public string Hook { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
}
