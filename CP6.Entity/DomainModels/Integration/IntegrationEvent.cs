using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Integration;

/// <summary>
/// 跨模块集成事件 — Bridge Hook 调用的持久化记录（Phase 6）
/// </summary>
/// <remarks>
/// 设计目的：
///   - 替代既有 Bridge Hook 的「失败仅 ILogger.LogError」模式
///   - 支持自动重试 / DeadLetter / 端到端 trace
///
/// 三段式生命周期（BridgeHookBase 强制流程）：
///   1. Pending 写入（在调用 hook 业务方法前）
///   2. 调用 hook 业务方法
///   3. 根据结果更新 Status：Success / Skipped（业务规则跳过，不重试）/ Failed（待重试）
///
/// Worker（IntegrationEventRetryWorker）每 60s 扫 Failed AND NextRetryAt &lt;= now，
/// 按 IntegrationEventOptions 配置的退避策略重试；达 MaxAttempts 后置 DeadLetter
/// 并触发 IDeadLetterNotifier（SignalR + Sys_OperLog 双通道告警）。
/// </remarks>
[Table("T_IntegrationEvent")]
public class IntegrationEvent : BaseBizEntity
{
    /// <summary>来源模块：ERP / MES / WMS</summary>
    [Required, MaxLength(10)]
    public string SourceModule { get; set; } = string.Empty;

    /// <summary>目标模块：ERP / MES / WMS</summary>
    [Required, MaxLength(10)]
    public string TargetModule { get; set; } = string.Empty;

    /// <summary>Hook 全限定名，如 "MesBridgeHook.OnOrderCreatedAsync"。Dispatcher 反射路由用</summary>
    [Required, MaxLength(100)]
    public string HookName { get; set; } = string.Empty;

    /// <summary>源业务单号（受注号 / 指図号 / 出庫号）</summary>
    [Required, MaxLength(30)]
    public string SourceNo { get; set; } = string.Empty;

    /// <summary>目标业务单号（成功时填）</summary>
    [MaxLength(30)]
    public string? TargetNo { get; set; }

    /// <summary>状态：见 <see cref="IntegrationEventStatus"/> 常量</summary>
    [Required, MaxLength(15)]
    public string Status { get; set; } = IntegrationEventStatus.Pending;

    /// <summary>已尝试次数（首次写入 = 1，每次 Worker 重试 +1）</summary>
    public int Attempts { get; set; } = 0;

    /// <summary>最后一次异常 ToString()</summary>
    public string? LastError { get; set; }

    /// <summary>下次重试时间（UTC）。Status=Failed 时由退避策略计算填入</summary>
    public DateTime? NextRetryAt { get; set; }

    /// <summary>串联同一业务链（受注→指図→出库→回写 共享同一 GUID，端到端 trace 用）</summary>
    public Guid CorrelationId { get; set; }

    /// <summary>后台工作项标识。非 Space 事件保持为空</summary>
    public Guid? JobId { get; set; }

    /// <summary>Space 发布意图标识。非 Space 事件保持为空</summary>
    public Guid? PublishAttemptId { get; set; }

    /// <summary>
    /// Canonical occurrence time for Space observability queries.
    /// New Space rows always persist this as UTC using the same clock sample
    /// as <see cref="BaseEntity.CreateDate"/>. Non-Space rows may remain null.
    /// </summary>
    public DateTime? OccurredAtUtc { get; set; }

    /// <summary>
    /// Space retry lease owner/fencing token. Null when no worker owns the
    /// row. NextRetryAt is the matching lease expiry while this is non-null.
    /// Non-Space events keep this field null.
    /// </summary>
    public Guid? RetryLeaseId { get; set; }

    /// <summary>
    /// Lease that durably completed the WMS transaction. The outcome remains
    /// recoverable until the retry finalizer atomically clears both fields.
    /// </summary>
    public Guid? RetryCompletionLeaseId { get; set; }

    /// <summary>
    /// Durable WMS completion outcome for <see cref="RetryCompletionLeaseId"/>.
    /// </summary>
    public bool? RetryCompletionSucceeded { get; set; }

    /// <summary>UTC acknowledgement time for the durable dead-letter alert.</summary>
    public DateTime? DeadLetterNotifiedAtUtc { get; set; }

    /// <summary>Owner token for a pending Space dead-letter notification.</summary>
    public Guid? DeadLetterNotificationLeaseId { get; set; }

    /// <summary>UTC expiry for <see cref="DeadLetterNotificationLeaseId"/>.</summary>
    public DateTime? DeadLetterNotificationLeaseUntilUtc { get; set; }

    /// <summary>触发时的入参 JSON（重试用，Dispatcher 反序列化后调用）</summary>
    public string PayloadJson { get; set; } = "{}";
}

/// <summary>
/// <see cref="IntegrationEvent.Status"/> 取值常量
/// </summary>
public static class IntegrationEventStatus
{
    /// <summary>已写入但 hook 业务方法尚未执行（极短窗口）</summary>
    public const string Pending = "PENDING";

    /// <summary>成功 — 终态</summary>
    public const string Success = "SUCCESS";

    /// <summary>业务规则跳过（如冪等命中、目标已存在）— 终态，不重试</summary>
    public const string Skipped = "SKIPPED";

    /// <summary>失败，待 Worker 重试</summary>
    public const string Failed = "FAILED";

    /// <summary>重试用尽，转入死信 — 终态，需人工介入</summary>
    public const string DeadLetter = "DEAD";

    /// <summary>人工补偿处理完毕 — 终态，由运维标记</summary>
    public const string Compensated = "COMPENSATED";
}
