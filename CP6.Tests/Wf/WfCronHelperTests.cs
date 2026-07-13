using System;
using CP6.Core.Services.Wf;
using Xunit;

public class WfCronHelperTests
{
    [Fact]
    public void IsValid_AcceptsStandard5Field_RejectsGarbage()
    {
        Assert.True(WfCronHelper.IsValid("0 0 25 * *"));
        Assert.True(WfCronHelper.IsValid("*/5 * * * *"));
        Assert.False(WfCronHelper.IsValid("not a cron"));
        Assert.False(WfCronHelper.IsValid(""));
        Assert.False(WfCronHelper.IsValid(null));
        Assert.False(WfCronHelper.IsValid("0 0 25 * * ?"));   // 6 段 Quartz 风格拒绝
    }

    [Fact]
    public void NextUtc_IsStrictlyFuture()
    {
        var after = DateTime.UtcNow;
        var next = WfCronHelper.NextUtc("*/5 * * * *", after);
        Assert.NotNull(next);
        Assert.True(next > after);
        Assert.Equal(DateTimeKind.Utc, next!.Value.Kind);
    }

    [Fact]
    public void NextUtc_BadCron_ReturnsNull()
    {
        Assert.Null(WfCronHelper.NextUtc("garbage", DateTime.UtcNow));
    }

    [Fact]
    public void NextUtc_Day31_SkipsShortMonths()
    {
        // 2026-04-01（4 月无 31 日）→ 下一次 "0 0 31 * *" 应落在 5 月 31 日（NCrontab 跳过无效日期）
        var april = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var next = WfCronHelper.NextUtc("0 0 31 * *", april)!.Value;
        var local = TimeZoneInfo.ConvertTimeFromUtc(next, TimeZoneInfo.Local);
        Assert.Equal(5, local.Month);
        Assert.Equal(31, local.Day);
    }

    [Fact]
    public void NextUtc_Feb29_OnlyLeapYear()
    {
        // 2026 非闰年 → "0 0 29 2 *" 下一次落 2028-02-29
        var start = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var next = WfCronHelper.NextUtc("0 0 29 2 *", start)!.Value;
        var local = TimeZoneInfo.ConvertTimeFromUtc(next, TimeZoneInfo.Local);
        Assert.Equal(2028, local.Year);
    }

    [Fact]
    public void PreviewUtc_ReturnsAscending_NCount()
    {
        var list = WfCronHelper.PreviewUtc("0 9 * * *", DateTime.UtcNow, 5);
        Assert.Equal(5, list.Count);
        for (var i = 1; i < list.Count; i++) Assert.True(list[i] > list[i - 1]);
    }

    // ── 审查修复 Finding 1：不可达 cron（语法合法但永不匹配）返回 null，不得回吐 NCrontab 哨兵日期 9999-12-31 ──
    [Fact]
    public void NextUtc_UnreachableCron_ReturnsNull()
    {
        // "0 0 30 2 *" = 2 月 30 日，永不存在 → NCrontab 返回内部哨兵 9999-12-31T23:59:59.9999999
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Null(WfCronHelper.NextUtc("0 0 30 2 *", start));
    }

    [Fact]
    public void PreviewUtc_UnreachableCron_ReturnsEmpty()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var list = WfCronHelper.PreviewUtc("0 0 30 2 *", start, 3);
        Assert.Empty(list);
    }

    // ── 审查修复 Finding 2：DST 春季跳变守卫——2:30 AM 每日 cron 扫过 PST 春季 2:00–3:00 缺口不得抛 ArgumentException ──
    [Fact]
    public void NextUtc_AcrossDstTransitions_NeverThrows_StrictlyIncreasing()
    {
        // 2:30 每日；从 2026-01-01 起算 400 次跨越 PST 春季（2026-03-08 2:00→3:00 跳变）与秋季两个转换点。
        // 非 DST 主机上此测试平凡通过（不存在无效本地时刻）。
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var list = WfCronHelper.PreviewUtc("30 2 * * *", start, 400);
        Assert.Equal(400, list.Count);
        for (var i = 1; i < list.Count; i++) Assert.True(list[i] > list[i - 1]);
    }
}
