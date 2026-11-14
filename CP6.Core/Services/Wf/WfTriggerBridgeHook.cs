// CP6.Core/Services/Wf/WfTriggerBridgeHook.cs
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
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

    public Task<WfTriggerBridgeResult> OnEventAsync(string eventKey, string eventId, string payloadJson, string? userName,
        CancellationToken ct = default)
        => FireMatchingAsync(eventKey, eventId, payloadJson, userName, persistOutbox: true, ct);

    public Task<WfTriggerBridgeResult> ReplayEventAsync(string eventKey, string eventId, string payloadJson, string? userName,
        CancellationToken ct = default)
        => FireMatchingAsync(eventKey, eventId, payloadJson, userName, persistOutbox: false, ct);

    private async Task<WfTriggerBridgeResult> FireMatchingAsync(
        string eventKey, string eventId, string payloadJson, string? userName, bool persistOutbox, CancellationToken ct)
    {
        // eventId 必填（幂等键素材）：缺失重试同样缺 → 直接拒绝，不进 outbox（spec §3.3）
        if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 150)
            return WfTriggerBridgeResult.Failed("eventId 必填且 ≤150 字符（幂等键素材）");

        var postgres = Db.Database.IsNpgsql();
        if (postgres && Db.Database.CurrentTransaction == null)
            throw new CurrentPermissionUnavailableException();

        var corrId = Guid.NewGuid();
        var payload = new WfTriggerEventPayload(eventKey, eventId, payloadJson ?? "{}", userName);
        var query = Db.Wf_FlowTriggers.Where(t => t.Enabled && t.TriggerType == WfTriggerType.Event && t.EventKey == eventKey);
        try
        {
            var matchedIds = await query.Select(t => t.Id).ToListAsync(ct);

            if (matchedIds.Count == 0)
            {
                await RecordAsync(IntegrationEventStatus.Skipped, "no matching trigger");
                return WfTriggerBridgeResult.Ok(0, 0);   // 未匹配零动作（spec §8）
            }

            var fired = 0;
            string? firstError = null;
            foreach (var id in matchedIds)
            {
                // 逐条重查（FireAsync 失败路径 ChangeTracker.Clear 契约，见 A-T2）
                var trig = await query.FirstOrDefaultAsync(t => t.Id == id, ct);
                if (trig == null) continue;
                var cfg = WfTriggerConfig.ParseEvent(trig.ConfigJson);
                var varsJson = WfTriggerVarsMapper.MapVars(cfg.VarsMap, payload.PayloadJson);
                var r = await _triggers.FireAsync(trig, varsJson, WfTriggerType.Event,
                                                  $"{eventId}:{trig.Id}", ct);
                if (r.Success) fired++;
                else firstError ??= r.Error;
            }

            if (firstError == null)
            {
                await RecordAsync(IntegrationEventStatus.Success, null);
                return WfTriggerBridgeResult.Ok(matchedIds.Count, fired);
            }

            // 部分成功 → Failed 进 outbox 重放；已发触发器撞键幂等跳过，未发补发（spec §3.3）
            await RecordAsync(IntegrationEventStatus.Failed, firstError);
            return WfTriggerBridgeResult.Failed($"部分失败 {fired}/{matchedIds.Count}: {firstError}");
        }
        catch (Exception ex) when (!postgres && ex is not OperationCanceledException)
        {
            await RecordAsync(IntegrationEventStatus.Failed, ex.ToString());
            return WfTriggerBridgeResult.Failed(ex.Message);
        }

        Task RecordAsync(string status, string? error)
        {
            if (!persistOutbox) return Task.CompletedTask;
            var source = ParseSource(eventKey);
            // The full id remains in the payload and fire key. SourceNo is the display reference.
            var reference = postgres && eventId.Length > 30
                ? eventId[..(char.IsHighSurrogate(eventId[29]) ? 29 : 30)]
                : eventId;
            if (postgres && source.Length > 10) source = "WF";
            return PersistEventAsync(source, "WF", nameof(OnEventAsync), reference, null,
                status, error, corrId, payload, required: postgres, ct: ct);
        }
    }

    /// <summary>"{SourceModule}|{HookName}" → SourceModule（outbox 行 SourceModule 列）；格式不符归 "WF"。</summary>
    private static string ParseSource(string eventKey)
    {
        var i = eventKey?.IndexOf('|') ?? -1;
        return i > 0 ? eventKey![..i] : "WF";
    }
}
