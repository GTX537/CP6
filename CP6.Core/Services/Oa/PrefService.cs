using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public class PrefService : IPrefService
{
    private readonly CP6Context _db;
    public PrefService(CP6Context db) { _db = db; }

    public async Task<string> GetAsync(Guid userId) =>
        (await _db.Wf_InboxPrefs.FirstOrDefaultAsync(p => p.UserId == userId))?.PrefsJson ?? "{}";

    public async Task SaveAsync(Guid userId, string prefsJson)
    {
        var p = await _db.Wf_InboxPrefs.FirstOrDefaultAsync(x => x.UserId == userId);
        if (p is null)
            _db.Wf_InboxPrefs.Add(new Wf_InboxPref { Id = Guid.NewGuid(), UserId = userId, PrefsJson = prefsJson ?? "{}" });
        else { p.PrefsJson = prefsJson ?? "{}"; p.ModifyDate = DateTime.Now; }
        await _db.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task<NotificationPrefs> GetNotifyPrefsAsync(Guid userId)
    {
        var json = await GetAsync(userId);
        return ParseNotifyPrefs(json);
    }

    // ── wfs-inbox-ux：矩阵偏好 + 合并写 ────────────────────────────────────

    /// <summary>per-request 缓存：本服务 Scoped 注册（Program.cs），实例生命周期=单请求。</summary>
    private readonly Dictionary<Guid, string> _prefsCache = new();

    private async Task<string> GetCachedAsync(Guid userId)
    {
        if (_prefsCache.TryGetValue(userId, out var cached)) return cached;
        var json = await GetAsync(userId);
        _prefsCache[userId] = json;
        return json;
    }

    /// <inheritdoc/>
    public async Task<bool> IsEnabledAsync(Guid userId, string type, string channel) =>
        NotifyMatrix.IsEnabled(await GetCachedAsync(userId), type, channel);

    /// <inheritdoc/>
    public async Task SaveMergeAsync(Guid userId, string partialJson)
    {
        System.Text.Json.Nodes.JsonObject patch;
        try
        {
            patch = System.Text.Json.Nodes.JsonNode.Parse(
                string.IsNullOrWhiteSpace(partialJson) ? "{}" : partialJson) as System.Text.Json.Nodes.JsonObject
                ?? throw new InvalidOperationException("oa.pref.errBadJson");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("oa.pref.errBadJson");
        }

        System.Text.Json.Nodes.JsonObject baseObj;
        try
        {
            baseObj = System.Text.Json.Nodes.JsonNode.Parse(await GetAsync(userId)) as System.Text.Json.Nodes.JsonObject
                      ?? new System.Text.Json.Nodes.JsonObject();
        }
        catch (JsonException)
        {
            baseObj = new System.Text.Json.Nodes.JsonObject();   // 库内畸形 → 以 patch 重建（与解析回落口径一致）
        }

        foreach (var kv in patch.ToList())
        {
            if (kv.Value is null) baseObj.Remove(kv.Key);                       // null → 删键（恢复默认）
            else baseObj[kv.Key] = kv.Value.DeepClone();                        // 顶层键整体替换
        }

        await SaveAsync(userId, baseObj.ToJsonString());
        _prefsCache.Remove(userId);                                             // 同请求内后续读取到新值
    }

    /// <inheritdoc/>
    public async Task<string> GetRowModeAsync(Guid userId)
    {
        try
        {
            using var doc = JsonDocument.Parse(await GetCachedAsync(userId));
            if (doc.RootElement.TryGetProperty("rowMode", out var el)
                && el.ValueKind == JsonValueKind.String && el.GetString() == "expanded")
                return "expanded";
        }
        catch (JsonException) { }
        return "merged";
    }

    // ── internal helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// 从 PrefsJson 字符串解析 notify 子对象 → NotificationPrefs。
    /// 解析失败 / 缺键 / 缺字段 → 回落 true（默认开启）。
    /// </summary>
    internal static NotificationPrefs ParseNotifyPrefs(string prefsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(prefsJson);
            if (!doc.RootElement.TryGetProperty("notify", out var notifyEl))
                return NotificationPrefs.Default;

            bool Get(string key) =>
                notifyEl.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.False
                    ? false
                    : true; // 缺字段 / true / 非布尔 → 默认 true

            return new NotificationPrefs(
                Todo:     Get("todo"),
                Approved: Get("approved"),
                Rejected: Get("rejected"),
                Timeout:  Get("timeout"),
                Email:    Get("email"));
        }
        catch (JsonException)
        {
            return NotificationPrefs.Default;
        }
    }
}
