using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>年历某日读模型（管理页行）。Date=例外日；IsWorkday=true 补班/false 假日；Note 备注。</summary>
public record WorkCalendarDay(DateTime Date, bool IsWorkday, string? Note);

/// <summary>年历管理页服务（WFS infra ①，A-T4）。<see cref="Sys_WorkCalendar"/> 例外表 CRUD：
/// 列一年例外 / 反转某日（upsert）/ 回归默认（删例外行）/ 导入日本法定假日到当前租户 / 空态判定。
/// 全局查询过滤自动限当前租户（<see cref="CP6Context.CurrentTenantId"/>）；写入经 StampTenant 自动盖章。</summary>
public interface IWorkCalendarService
{
    /// <summary>列出某公历年的全部例外行（升序）。</summary>
    Task<List<WorkCalendarDay>> ListYearAsync(int year, CancellationToken ct);

    /// <summary>反转某日为显式态（upsert）：命中则更新 IsWorkday/Note，否则插入新例外行。</summary>
    Task SetDayAsync(DateTime date, bool isWorkday, string? note, CancellationToken ct);

    /// <summary>回归默认态：删除该日例外行（无例外则周末=休、平日=工）。</summary>
    Task ClearDayAsync(DateTime date, CancellationToken ct);

    /// <summary>导入日本法定假日到当前租户（幂等，仅插缺失日期）。返回本次实际新增行数。</summary>
    Task<int> ImportJapaneseHolidaysAsync(CancellationToken ct);

    /// <summary>当前租户是否尚未维护任何年历例外（空态判定，驱动前端「导入」引导）。</summary>
    Task<bool> IsEmptyAsync(CancellationToken ct);
}

public sealed class WorkCalendarService : IWorkCalendarService
{
    private readonly CP6Context _db;
    public WorkCalendarService(CP6Context db) => _db = db;

    public async Task<List<WorkCalendarDay>> ListYearAsync(int year, CancellationToken ct)
    {
        var from = new DateTime(year, 1, 1);
        var to = new DateTime(year + 1, 1, 1);
        return await _db.Sys_WorkCalendars.AsNoTracking()
            .Where(c => c.Date >= from && c.Date < to)
            .OrderBy(c => c.Date)
            .Select(c => new WorkCalendarDay(c.Date, c.IsWorkday, c.Note))
            .ToListAsync(ct);
    }

    public async Task SetDayAsync(DateTime date, bool isWorkday, string? note, CancellationToken ct)
    {
        var d = date.Date;
        var row = await _db.Sys_WorkCalendars.FirstOrDefaultAsync(c => c.Date == d, ct);
        if (row == null)
            _db.Sys_WorkCalendars.Add(new Sys_WorkCalendar { Date = d, IsWorkday = isWorkday, Note = note });
        else
        {
            row.IsWorkday = isWorkday;
            row.Note = note;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task ClearDayAsync(DateTime date, CancellationToken ct)
    {
        var d = date.Date;
        var row = await _db.Sys_WorkCalendars.FirstOrDefaultAsync(c => c.Date == d, ct);
        if (row != null)
        {
            _db.Sys_WorkCalendars.Remove(row);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<int> ImportJapaneseHolidaysAsync(CancellationToken ct)
    {
        var existing = (await _db.Sys_WorkCalendars.AsNoTracking()
            .Select(c => c.Date).ToListAsync(ct)).ToHashSet();

        var toAdd = JapaneseHolidayData.Items
            .Select(h => new { Date = new DateTime(h.Y, h.M, h.D), h.Note })
            .Where(h => !existing.Contains(h.Date))
            .Select(h => new Sys_WorkCalendar { Date = h.Date, IsWorkday = false, Note = h.Note })
            .ToList();

        if (toAdd.Count == 0) return 0;
        _db.Sys_WorkCalendars.AddRange(toAdd);
        await _db.SaveChangesAsync(ct);
        return toAdd.Count;
    }

    public async Task<bool> IsEmptyAsync(CancellationToken ct)
        => !await _db.Sys_WorkCalendars.AsNoTracking().AnyAsync(ct);
}
