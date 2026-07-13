// CP6.Core/Services/Integration/IWfTriggerBridgeHook.cs
namespace CP6.Core.Services.Integration;

/// <summary>WF 触发器桥接 hook（BridgeHook 家族成员，D4）。业务模块发事件＝一行调用 OnEventAsync。</summary>
public interface IWfTriggerBridgeHook
{
    /// <summary>业务调用入口：匹配 eventKey 的启用触发器逐条发起 + 写 IntegrationEvents 台账（失败行由 RetryWorker 重放）。</summary>
    /// <param name="eventKey">"{SourceModule}|{HookName}"，如 "WMS|OnShipmentConfirmedAsync"</param>
    /// <param name="eventId">业务事件唯一标识（必填，幂等键素材 "{eventId}:{TriggerId}"，spec §2.2）</param>
    Task<WfTriggerBridgeResult> OnEventAsync(string eventKey, string eventId, string payloadJson, string? userName);

    /// <summary>dispatcher 重放入口：同一执行逻辑但不再写新 outbox 行（防重放行自增殖，映射表⑦）；去重靠 TriggerFire 幂等闸。</summary>
    Task<WfTriggerBridgeResult> ReplayEventAsync(string eventKey, string eventId, string payloadJson, string? userName);
}

/// <summary>outbox 负载契约（PersistEventAsync 序列化 / dispatcher 反序列化，重放原样复用 eventId）。</summary>
public sealed record WfTriggerEventPayload(string EventKey, string EventId, string PayloadJson, string? UserName);

public class WfTriggerBridgeResult
{
    public bool Success { get; init; }
    public int MatchedCount { get; init; }
    public int FiredCount { get; init; }
    public string? Message { get; init; }

    public static WfTriggerBridgeResult Ok(int matched, int fired)
        => new() { Success = true, MatchedCount = matched, FiredCount = fired };
    public static WfTriggerBridgeResult Skipped(string reason)
        => new() { Success = false, Message = $"SKIPPED: {reason}" };
    public static WfTriggerBridgeResult Failed(string reason)
        => new() { Success = false, Message = reason };
}

public class NoOpWfTriggerBridgeHook : IWfTriggerBridgeHook
{
    public Task<WfTriggerBridgeResult> OnEventAsync(string eventKey, string eventId, string payloadJson, string? userName)
        => Task.FromResult(WfTriggerBridgeResult.Skipped("WfTriggerBridge:Enabled=false"));
    public Task<WfTriggerBridgeResult> ReplayEventAsync(string eventKey, string eventId, string payloadJson, string? userName)
        => Task.FromResult(WfTriggerBridgeResult.Skipped("WfTriggerBridge:Enabled=false"));
}
