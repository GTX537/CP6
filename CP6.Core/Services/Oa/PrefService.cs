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
