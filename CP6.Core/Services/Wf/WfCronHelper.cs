using NCrontab;

namespace CP6.Core.Services.Wf;

/// <summary>NCrontab 包装（D3）。cron 5 段标准，按 app 默认时区解释（spec §9 一期口径，UI 文案标注时区），
/// 存储/比较一律 UTC。无 L 语义（映射表③：「每月末」预设按 28 日近似）。</summary>
public static class WfCronHelper
{
    public static bool IsValid(string? cron)
        => !string.IsNullOrWhiteSpace(cron) && CrontabSchedule.TryParse(cron) != null;

    /// <summary>afterUtc 之后（严格未来）的下一次到期（UTC）；cron 非法返回 null。
    /// 从「当前时刻」起算即天然实现 misfire 口径：宕机跨过的历史到期点直接跳过（spec §3.2）。</summary>
    public static DateTime? NextUtc(string cron, DateTime afterUtc)
    {
        var sched = CrontabSchedule.TryParse(cron);
        if (sched == null) return null;
        var afterLocal = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc), TimeZoneInfo.Local);
        var nextLocal = sched.GetNextOccurrence(afterLocal);
        // 审查修复 Finding 1：语法合法但永不匹配的 cron（如 "0 0 30 2 *"）——NCrontab 回吐内部哨兵
        // 9999-12-31T23:59:59.9999999 而非报"无"。契约意图 null=无下一次，故 Year>=9999 视为"永不"。
        if (nextLocal.Year >= 9999) return null;
        return ToUtcSafe(nextLocal);
    }

    /// <summary>本地时刻→UTC。审查修复 Finding 2：DST 春季跳变缺口内的本地时刻不存在，
    /// ConvertTimeToUtc 会抛 ArgumentException；命中无效时刻则按 30 分钟步进推进至有效时刻
    /// （DST 缺口 30–60 分钟，最多 6 步做安全网上界）。</summary>
    private static DateTime ToUtcSafe(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        for (var guard = 0; guard < 6 && TimeZoneInfo.Local.IsInvalidTime(unspecified); guard++)
            unspecified = unspecified.AddMinutes(30);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZoneInfo.Local);
    }

    /// <summary>fromUtc 起未来 count 次到期（UTC 升序）——管理页「下次触发时间预览」用。</summary>
    public static IReadOnlyList<DateTime> PreviewUtc(string cron, DateTime fromUtc, int count)
    {
        var list = new List<DateTime>(count);
        var cursor = fromUtc;
        for (var i = 0; i < count; i++)
        {
            var next = NextUtc(cron, cursor);
            if (next == null) break;
            list.Add(next.Value);
            cursor = next.Value;
        }
        return list;
    }
}
