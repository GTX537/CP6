using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public class PrefService : IPrefService
{
    private readonly CP6Context _db;
    public PrefService(CP6Context db) { _db = db; }

    public async Task<string> GetAsync(Guid userId) =>
        (await _db.Wf_InboxPrefs.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId))?.PrefsJson ?? "{}";

    public Task SaveAsync(Guid userId, string prefsJson)
        => SaveSerializedAsync(userId, () => Task.FromResult(prefsJson ?? "{}"));

    internal async Task PersistAsync(Guid userId, string prefsJson, CancellationToken ct = default)
    {
        var row = await _db.Wf_InboxPrefs.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (row is null)
            _db.Wf_InboxPrefs.Add(new Wf_InboxPref { Id = Guid.NewGuid(), UserId = userId, PrefsJson = prefsJson });
        else
        {
            row.PrefsJson = prefsJson;
            row.ModifyDate = DateTime.Now;
            _db.Entry(row).Property(x => x.PrefsJson).IsModified = true;
        }
        await _db.SaveChangesAsync(ct);
        _prefsCache.Remove(userId);
    }

    private async Task SaveSerializedAsync(Guid userId, Func<Task<string>> prepare)
    {
        if (!_db.Database.IsNpgsql())
        {
            await PersistAsync(userId, await prepare());
            return;
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();
        await WorkflowNotificationPreferencesV1.LockAsync(_db, userId, default);
        await PersistAsync(userId, await prepare());
        await transaction.CommitAsync();
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
    public Task SaveMergeAsync(Guid userId, string partialJson)
        => SaveSerializedAsync(userId, async () => Merge(await GetAsync(userId), partialJson));

    internal static string Merge(string stored, string partialJson)
    {
        JsonObject patch;
        try
        {
            patch = JsonNode.Parse(string.IsNullOrWhiteSpace(partialJson) ? "{}" : partialJson) as JsonObject
                ?? throw new InvalidOperationException("oa.pref.errBadJson");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("oa.pref.errBadJson");
        }
        JsonObject result;
        try { result = JsonNode.Parse(stored) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { result = new JsonObject(); }
        foreach (var entry in patch)
        {
            if (entry.Value is null) result.Remove(entry.Key);
            else result[entry.Key] = entry.Value.DeepClone();
        }
        return result.ToJsonString();
    }

    /// <inheritdoc/>
    public async Task<string> GetRowModeAsync(Guid userId)
    {
        try
        {
            using var doc = JsonDocument.Parse(await GetCachedAsync(userId));
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("rowMode", out var el)
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
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("notify", out var notifyEl) || notifyEl.ValueKind != JsonValueKind.Object)
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
