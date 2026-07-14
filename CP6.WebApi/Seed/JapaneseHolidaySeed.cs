using System;
using System.Linq;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>日本法定假日 seed（WFS infra ①，spec §2.1）。启动期默认租户植入 + <c>For(tenantId)</c> 生成器。
/// ★A-T4 起原始 35 日期下沉至 <see cref="JapaneseHolidayData"/>（CP6.Core），本类的 <see cref="Items"/> 委托同源，
///   使 Core 侧 <c>WorkCalendarService.ImportJapaneseHolidaysAsync</c> 与本 WebApi seed 复用单一事实来源、零漂移
///   （Core 不可反向引用 WebApi.Seed，故数据必须在 Core）。日期依据与逐年官报计算说明见 <see cref="JapaneseHolidayData"/>。</summary>
public static class JapaneseHolidaySeed
{
    /// <summary>委托 <see cref="JapaneseHolidayData.Items"/>（单一事实来源，CP6.Core）。</summary>
    public static (int Y, int M, int D, string Note)[] Items => JapaneseHolidayData.Items;

    /// <summary>盖某租户 → Sys_WorkCalendar[]（全 IsWorkday=false）。幂等去重由调用方按 (TenantId,Date) Any 判定。</summary>
    public static Sys_WorkCalendar[] For(Guid tenantId) => Items
        .Select(h => new Sys_WorkCalendar
        {
            TenantId = tenantId,
            Date = new DateTime(h.Y, h.M, h.D),
            IsWorkday = false,
            Note = h.Note,
        })
        .ToArray();
}
