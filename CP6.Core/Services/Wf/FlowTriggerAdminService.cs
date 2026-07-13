// CP6.Core/Services/Wf/FlowTriggerAdminService.cs
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public record FlowTriggerSaveReq(
    string FlowKey, int TriggerType, string ConfigJson, bool Enabled,
    string? EventKey, Guid StarterUserId);

public record FlowTriggerListItem(
    Guid Id, string FlowKey, int TriggerType, bool Enabled, string? EventKey,
    Guid StarterUserId, DateTime? NextDueUtc, DateTime? LastFiredUtc, bool HasApiKey, string ConfigJson);

public record TriggerFireListItem(
    Guid Id, string IdempotencyKey, DateTime FiredUtc, Guid? InstanceId, int Source, string? Error);

public interface IFlowTriggerAdminService
{
    Task<List<FlowTriggerListItem>> ListAsync(CancellationToken ct);
    Task<FlowTriggerListItem?> GetAsync(Guid id, CancellationToken ct);
    /// <summary>返回 (id, apiKeyPlain)。apiKeyPlain 仅 message 型创建时非空——明文只此一次（spec §3.4）。</summary>
    Task<(Guid Id, string? ApiKeyPlain)> CreateAsync(FlowTriggerSaveReq req, CancellationToken ct);
    Task UpdateAsync(Guid id, FlowTriggerSaveReq req, CancellationToken ct);
    Task SetEnabledAsync(Guid id, bool enabled, CancellationToken ct);
    /// <summary>重置 key（仅 message）：返回新明文，旧 key 即刻失效。</summary>
    Task<string> ResetKeyAsync(Guid id, CancellationToken ct);
    /// <summary>手动试发（权限同 Edit）：幂等键 = "manual:{GUID}"（spec §4）。</summary>
    Task<TriggerFireResult> ManualFireAsync(Guid id, CancellationToken ct);
    Task<List<TriggerFireListItem>> ListFiresAsync(Guid id, int take, CancellationToken ct);
}

public class FlowTriggerAdminService : IFlowTriggerAdminService
{
    private readonly CP6Context _db;
    private readonly IFlowTriggerService _fire;

    public FlowTriggerAdminService(CP6Context db, IFlowTriggerService fire)
    {
        _db = db;
        _fire = fire;
    }

    public async Task<List<FlowTriggerListItem>> ListAsync(CancellationToken ct)
        => await _db.Wf_FlowTriggers.OrderBy(t => t.FlowKey).ThenBy(t => t.TriggerType)
            .Select(t => ToItem(t)).ToListAsync(ct);

    public async Task<FlowTriggerListItem?> GetAsync(Guid id, CancellationToken ct)
    {
        var t = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(x => x.Id == id, ct);
        return t == null ? null : ToItem(t);
    }

    public async Task<(Guid Id, string? ApiKeyPlain)> CreateAsync(FlowTriggerSaveReq req, CancellationToken ct)
    {
        await FlowTriggerValidator.ValidateAsync(_db, req, ct);   // F-T1 落地；E-T1 阶段先建含基本必填检查的桩
        var t = new Wf_FlowTrigger
        {
            FlowKey = req.FlowKey, TriggerType = req.TriggerType,
            ConfigJson = string.IsNullOrWhiteSpace(req.ConfigJson) ? "{}" : req.ConfigJson,
            Enabled = req.Enabled,
            EventKey = req.TriggerType == WfTriggerType.Event ? req.EventKey : null,
            StarterUserId = req.StarterUserId,
        };
        string? plain = null;
        if (req.TriggerType == WfTriggerType.Message)
        {
            plain = WfApiKeyHelper.NewRawKey();
            t.ApiKeyHash = WfApiKeyHelper.HashOf(plain);
        }
        if (req.TriggerType == WfTriggerType.Timer)
            t.NextDueUtc = WfCronHelper.NextUtc(WfTriggerConfig.ParseTimer(t.ConfigJson).Cron, DateTime.UtcNow);
        _db.Wf_FlowTriggers.Add(t);
        await _db.SaveChangesAsync(ct);
        return (t.Id, plain);
    }

    public async Task UpdateAsync(Guid id, FlowTriggerSaveReq req, CancellationToken ct)
    {
        var t = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("E-WF-022: 触发器不存在");
        if (t.TriggerType != req.TriggerType)
            throw new InvalidOperationException("E-WF-022: 触发器类型不可变更（删除重建）");
        await FlowTriggerValidator.ValidateAsync(_db, req, ct);
        t.FlowKey = req.FlowKey;
        t.ConfigJson = string.IsNullOrWhiteSpace(req.ConfigJson) ? "{}" : req.ConfigJson;
        t.Enabled = req.Enabled;
        t.EventKey = req.TriggerType == WfTriggerType.Event ? req.EventKey : null;
        t.StarterUserId = req.StarterUserId;
        if (t.TriggerType == WfTriggerType.Timer)
            t.NextDueUtc = WfCronHelper.NextUtc(WfTriggerConfig.ParseTimer(t.ConfigJson).Cron, DateTime.UtcNow);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken ct)
    {
        var t = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("E-WF-022: 触发器不存在");
        t.Enabled = enabled;
        if (enabled && t.TriggerType == WfTriggerType.Timer && t.NextDueUtc == null)
            t.NextDueUtc = WfCronHelper.NextUtc(WfTriggerConfig.ParseTimer(t.ConfigJson).Cron, DateTime.UtcNow);  // cron 修复后重新上膛
        await _db.SaveChangesAsync(ct);
    }

    public async Task<string> ResetKeyAsync(Guid id, CancellationToken ct)
    {
        var t = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("E-WF-022: 触发器不存在");
        if (t.TriggerType != WfTriggerType.Message)
            throw new InvalidOperationException("E-WF-022: 仅 message 触发器有 API key");
        var plain = WfApiKeyHelper.NewRawKey();
        t.ApiKeyHash = WfApiKeyHelper.HashOf(plain);
        await _db.SaveChangesAsync(ct);
        return plain;
    }

    public async Task<TriggerFireResult> ManualFireAsync(Guid id, CancellationToken ct)
    {
        var t = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("E-WF-022: 触发器不存在");
        var varsJson = t.TriggerType == WfTriggerType.Timer
            ? WfTriggerConfig.ParseTimer(t.ConfigJson).VarsJson
            : "{}";
        // 同上下文重复试发时脱钩：令 FireAsync 步④重查到带当前 RowVersion 的鲜活实例，
        // 避免上次 LastFiredUtc 写触达后本追踪实例持旧令牌撞乐观并发（同 ScanTimersOnceAsync 第二段脱钩口径）。
        _db.Entry(t).State = EntityState.Detached;
        return await _fire.FireAsync(t, varsJson, t.TriggerType, $"manual:{Guid.NewGuid():N}", ct);
    }

    public async Task<List<TriggerFireListItem>> ListFiresAsync(Guid id, int take, CancellationToken ct)
        => await _db.Wf_TriggerFires.Where(f => f.TriggerId == id)
            .OrderByDescending(f => f.FiredUtc).Take(Math.Clamp(take, 1, 200))
            .Select(f => new TriggerFireListItem(f.Id, f.IdempotencyKey, f.FiredUtc, f.InstanceId, f.Source, f.Error))
            .ToListAsync(ct);

    private static FlowTriggerListItem ToItem(Wf_FlowTrigger t)
        => new(t.Id, t.FlowKey, t.TriggerType, t.Enabled, t.EventKey, t.StarterUserId,
               t.NextDueUtc, t.LastFiredUtc, t.ApiKeyHash != null, t.ConfigJson);
}
