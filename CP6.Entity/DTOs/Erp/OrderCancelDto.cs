namespace CP6.Entity.DTOs.Erp;

/// <summary>
/// 受注取消の結果区分（Phase 6）
/// </summary>
public enum CancelOutcome
{
    /// <summary>取消成功（全関連 WO/Outbound も取消済）</summary>
    Cancelled,
    /// <summary>一部の WO/Outbound が着手済のため要決定 — DB 未更新</summary>
    NeedsDecision,
    /// <summary>取消不可（Shipped 済 / Cancelled 済 等）— DB 未更新</summary>
    Rejected,
    /// <summary>一部取消完了（force=true で半路状態のまま部分実施）</summary>
    PartiallyCancelled,
}

/// <summary>受注取消の最終結果（OrderService.CancelAsync 戻り値）</summary>
public class OrderCancelResult
{
    public CancelOutcome Outcome { get; init; }
    public string? Message { get; init; }
    public List<WorkOrderProbe> RelatedWorkOrders { get; init; } = new();
    public List<OutboundProbe> RelatedOutbounds { get; init; } = new();
    /// <summary>端到端 trace 用（同一業務鎖を貫く）</summary>
    public Guid CorrelationId { get; init; }

    public static OrderCancelResult Cancelled(
        Guid correlationId,
        List<WorkOrderProbe> wos,
        List<OutboundProbe> obs,
        string? message = null) => new()
        {
            Outcome = CancelOutcome.Cancelled,
            Message = message,
            RelatedWorkOrders = wos,
            RelatedOutbounds = obs,
            CorrelationId = correlationId,
        };

    public static OrderCancelResult NeedsDecision(
        Guid correlationId,
        List<WorkOrderProbe> wos,
        List<OutboundProbe> obs) => new()
        {
            Outcome = CancelOutcome.NeedsDecision,
            Message = "半路状態の関連 WO/Outbound あり。force=true で続行可。",
            RelatedWorkOrders = wos,
            RelatedOutbounds = obs,
            CorrelationId = correlationId,
        };

    public static OrderCancelResult Rejected(Guid correlationId, string reason) => new()
    {
        Outcome = CancelOutcome.Rejected,
        Message = reason,
        CorrelationId = correlationId,
    };

    public static OrderCancelResult PartiallyCancelled(
        Guid correlationId,
        List<WorkOrderProbe> wos,
        List<OutboundProbe> obs,
        string message) => new()
        {
            Outcome = CancelOutcome.PartiallyCancelled,
            Message = message,
            RelatedWorkOrders = wos,
            RelatedOutbounds = obs,
            CorrelationId = correlationId,
        };
}

/// <summary>関連 WorkOrder の取消可否プローブ</summary>
public class WorkOrderProbe
{
    public string WorkOrderNo { get; init; } = string.Empty;
    public int Status { get; init; }
    /// <summary>Status &lt;= Issued(2) なら true</summary>
    public bool AutoCancellable { get; init; }
    /// <summary>取消実施結果（force=true 時のみセット）：true=取消成功、false=スキップ／既に Cancelled</summary>
    public bool? Cancelled { get; set; }
    public string? Message { get; set; }
}

/// <summary>関連 OutboundOrder の取消可否プローブ</summary>
public class OutboundProbe
{
    public string OutboundNo { get; init; } = string.Empty;
    public int Status { get; init; }
    public int OutboundType { get; init; }
    /// <summary>Status &lt;= Allocated(2) なら true（Picking 以降は不可）</summary>
    public bool AutoCancellable { get; init; }
    public bool? Cancelled { get; set; }
    public string? Message { get; set; }
}

/// <summary>IOrderCancelBridgeHook 戻り値</summary>
public class OrderCancelHookResult
{
    public bool Success { get; init; }
    /// <summary>全関連項目が AutoCancellable=true で、force=true 時は全部 cancel 済</summary>
    public bool FullyCascaded { get; init; }
    public List<WorkOrderProbe> WorkOrders { get; init; } = new();
    public List<OutboundProbe> Outbounds { get; init; } = new();
    public string? Message { get; init; }
}
