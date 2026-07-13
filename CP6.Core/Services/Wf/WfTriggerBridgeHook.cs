// CP6.Core/Services/Wf/WfTriggerBridgeHook.cs
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Wf;

public class WfTriggerBridgeHook : BridgeHookBase, IWfTriggerBridgeHook
{
    private readonly IFlowTriggerService _triggers;

    public WfTriggerBridgeHook(CP6Context db, IFlowTriggerService triggers, ILogger<WfTriggerBridgeHook> logger)
        : base(db, logger)
    {
        _triggers = triggers;
    }

    public Task<WfTriggerBridgeResult> OnEventAsync(string eventKey, string eventId, string payloadJson, string? userName)
        => FireMatchingAsync(eventKey, eventId, payloadJson, userName, persistOutbox: true);

    public Task<WfTriggerBridgeResult> ReplayEventAsync(string eventKey, string eventId, string payloadJson, string? userName)
        => FireMatchingAsync(eventKey, eventId, payloadJson, userName, persistOutbox: false);

    private async Task<WfTriggerBridgeResult> FireMatchingAsync(
        string eventKey, string eventId, string payloadJson, string? userName, bool persistOutbox)
    {
        // eventId 必填（幂等键素材）：缺失重试同样缺 → 直接拒绝，不进 outbox（spec §3.3）
        if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 150)
            return WfTriggerBridgeResult.Failed("eventId 必填且 ≤150 字符（幂等键素材）");

        var corrId = Guid.NewGuid();
        var payload = new WfTriggerEventPayload(eventKey, eventId, payloadJson ?? "{}", userName);
        var source = ParseSource(eventKey);
        try
        {
            var matchedIds = await Db.Wf_FlowTriggers
                .Where(t => t.Enabled && t.TriggerType == WfTriggerType.Event && t.EventKey == eventKey)
                .Select(t => t.Id)
                .ToListAsync();

            if (matchedIds.Count == 0)
            {
                if (persistOutbox)
                    await PersistEventAsync(source, "WF", nameof(OnEventAsync), eventId, null,
                        IntegrationEventStatus.Skipped, "no matching trigger", corrId, payload);
                return WfTriggerBridgeResult.Ok(0, 0);   // 未匹配零动作（spec §8）
            }

            var fired = 0;
            string? firstError = null;
            foreach (var id in matchedIds)
            {
                // 逐条重查（FireAsync 失败路径 ChangeTracker.Clear 契约，见 A-T2）
                var trig = await Db.Wf_FlowTriggers.FirstOrDefaultAsync(t => t.Id == id);
                if (trig == null || !trig.Enabled) continue;
                var cfg = WfTriggerConfig.ParseEvent(trig.ConfigJson);
                var varsJson = WfTriggerVarsMapper.MapVars(cfg.VarsMap, payload.PayloadJson);
                var r = await _triggers.FireAsync(trig, varsJson, WfTriggerType.Event,
                                                  $"{eventId}:{trig.Id}", CancellationToken.None);
                if (r.Success) fired++;
                else firstError ??= r.Error;
            }

            if (firstError == null)
            {
                if (persistOutbox)
                    await PersistEventAsync(source, "WF", nameof(OnEventAsync), eventId, null,
                        IntegrationEventStatus.Success, null, corrId, payload);
                return WfTriggerBridgeResult.Ok(matchedIds.Count, fired);
            }

            // 部分成功 → Failed 进 outbox 重放；已发触发器撞键幂等跳过，未发补发（spec §3.3）
            if (persistOutbox)
                await PersistEventAsync(source, "WF", nameof(OnEventAsync), eventId, null,
                    IntegrationEventStatus.Failed, firstError, corrId, payload);
            return WfTriggerBridgeResult.Failed($"部分失败 {fired}/{matchedIds.Count}: {firstError}");
        }
        catch (Exception ex)
        {
            if (persistOutbox)
                await PersistEventAsync(source, "WF", nameof(OnEventAsync), eventId, null,
                    IntegrationEventStatus.Failed, ex.ToString(), corrId, payload);
            return WfTriggerBridgeResult.Failed(ex.Message);
        }
    }

    /// <summary>"{SourceModule}|{HookName}" → SourceModule（outbox 行 SourceModule 列）；格式不符归 "WF"。</summary>
    private static string ParseSource(string eventKey)
    {
        var i = eventKey?.IndexOf('|') ?? -1;
        return i > 0 ? eventKey![..i] : "WF";
    }
}
