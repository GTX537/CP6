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

    /// <summary>矩阵偏好查询（wfs-inbox-ux §2.2）。逐收件人×逐通道；Scoped 实例内字典缓存（= per-request）。</summary>
    Task<bool> IsEnabledAsync(Guid userId, string type, string channel);

    /// <summary>顶层键合并写（wfs-inbox-ux §6）：读-改-写单次 SaveChanges；patch 键值为 null → 删除该键。
    /// patch 非法 JSON → InvalidOperationException("oa.pref.errBadJson")。</summary>
    Task SaveMergeAsync(Guid userId, string partialJson);

    /// <summary>rowMode 显示偏好（wfs-inbox-ux §5）："merged"（默认）| "expanded"。</summary>
    Task<string> GetRowModeAsync(Guid userId);
}
