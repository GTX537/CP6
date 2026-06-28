namespace CP6.Core.Services.Oa;

public interface IPrefService
{
    Task<string> GetAsync(Guid userId);           // 无则 "{}"
    Task SaveAsync(Guid userId, string prefsJson); // upsert

    /// <summary>
    /// 读取用户通知偏好（N-T3）。
    /// 解析 Wf_InboxPref.PrefsJson 的 <c>notify</c> 子对象；
    /// 无行 / 无键 / 缺字段 / 解析失败 → 该项默认 true（全开）。
    /// </summary>
    Task<NotificationPrefs> GetNotifyPrefsAsync(Guid userId);
}
