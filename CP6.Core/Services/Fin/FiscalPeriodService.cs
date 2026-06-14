using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 会计期间服务实现（章02 §1）。FiscalYearStartMonth 公司级配置（appsettings "Fin:FiscalYearStartMonth"，默认 1）。
/// </summary>
public class FiscalPeriodService : IFiscalPeriodService
{
    private readonly CP6Context _db;
    private readonly int _fyStart;

    /// <summary>DI 构造：从配置读财年起始月。</summary>
    public FiscalPeriodService(CP6Context db, IConfiguration config)
        : this(db, config.GetValue("Fin:FiscalYearStartMonth", 1)) { }

    /// <summary>测试/显式构造：指定财年起始月（1..12）。</summary>
    public FiscalPeriodService(CP6Context db, int fiscalYearStartMonth)
    {
        _db = db;
        _fyStart = fiscalYearStartMonth is >= 1 and <= 12 ? fiscalYearStartMonth : 1;
    }

    public (int FiscalYear, int PeriodNo) ComputeFiscal(int year, int month)
    {
        var periodNo = ((month - _fyStart + 12) % 12) + 1;
        var fiscalYear = month >= _fyStart ? year : year - 1;
        return (fiscalYear, periodNo);
    }

    public Task<FiscalPeriod?> ResolveAsync(DateTime date) =>
        _db.FiscalPeriods.FirstOrDefaultAsync(p => p.Year == date.Year && p.Month == date.Month);

    public Task<FiscalPeriod> EnsureOpenAsync(DateTime date, string? user = null) =>
        EnsureOpenAsync(date.Year, date.Month, user);

    public async Task<FiscalPeriod> EnsureOpenAsync(int year, int month, string? user = null)
    {
        // 跨年滚动归一（CloseAsync 传 Month+1 可能为 13）
        while (month > 12) { month -= 12; year += 1; }
        while (month < 1) { month += 12; year -= 1; }

        var existing = await _db.FiscalPeriods.FirstOrDefaultAsync(p => p.Year == year && p.Month == month);
        if (existing != null) return existing;

        var (fy, pn) = ComputeFiscal(year, month);
        var start = new DateTime(year, month, 1);
        var period = new FiscalPeriod
        {
            Id = Guid.NewGuid(),
            FiscalYear = fy,
            Year = year,
            Month = month,
            PeriodNo = pn,
            PeriodStart = start,
            PeriodEnd = start.AddMonths(1).AddDays(-1),
            Status = PeriodStatus.Open,
            Creator = user,
            CreateDate = DateTime.Now,
        };
        _db.FiscalPeriods.Add(period);
        await _db.SaveChangesAsync();
        return period;
    }

    public async Task<bool> IsOpenAsync(Guid periodId)
    {
        var p = await _db.FiscalPeriods.FindAsync(periodId);
        return p is { Status: PeriodStatus.Open };
    }

    public async Task<FiscalPeriod?> PreviousAsync(Guid periodId)
    {
        var p = await _db.FiscalPeriods.FindAsync(periodId);
        if (p == null) return null;
        var year = p.Year;
        var month = p.Month - 1;
        if (month < 1) { month = 12; year -= 1; }
        return await _db.FiscalPeriods.FirstOrDefaultAsync(x => x.Year == year && x.Month == month);
    }
}
