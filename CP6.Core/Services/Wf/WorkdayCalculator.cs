using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>工作日历纯查询服务（WFS infra ①，spec §2.2）。IsWorkday=例外表命中?行值:(周一~五)；
/// AddWorkdays 当天不算、跳非工作日、连续 366 天无工作日快速失败防死循环。date 按租户时区解释（消费点负责换算）。</summary>
public interface IWorkdayCalculator
{
    Task<DateTime> AddWorkdaysAsync(DateTime dateLocal, int n, CancellationToken ct);
    Task<bool> IsWorkdayAsync(DateTime dateLocal, CancellationToken ct);
}

public sealed class WorkdayCalculator : IWorkdayCalculator
{
    private const int MaxScanDays = 366;
    private readonly CP6Context _db;
    public WorkdayCalculator(CP6Context db) => _db = db;

    public async Task<bool> IsWorkdayAsync(DateTime dateLocal, CancellationToken ct)
    {
        var d = dateLocal.Date;
        var ex = await _db.Sys_WorkCalendars.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Date == d, ct);
        if (ex != null) return ex.IsWorkday;
        return dateLocal.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    public async Task<DateTime> AddWorkdaysAsync(DateTime dateLocal, int n, CancellationToken ct)
    {
        if (n < 1) throw new ArgumentOutOfRangeException(nameof(n), "工作日步数须 ≥1");
        var cursor = dateLocal.Date;
        int added = 0, scanned = 0;
        while (added < n)
        {
            cursor = cursor.AddDays(1);
            if (++scanned > MaxScanDays)
                throw new InvalidOperationException("E-WF-016 连续 366 天无工作日，疑似假日表异常，拒绝无限顺延");
            if (await IsWorkdayAsync(cursor, ct)) added++;
        }
        return cursor;
    }
}
