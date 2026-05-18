using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

/// <summary>
/// OEE 計算・分析サービス
/// </summary>
/// <remarks>
/// OEE = 可用率 × 性能 × 品質
///   可用率(Availability) = ActualRunMinutes / PlannedRunMinutes
///   性能(Performance)    = (Good+Defect) / (Capacity × ActualRunHours)
///   品質(Quality)        = Good / (Good+Defect)
/// </remarks>
public class OeeService : IOeeService
{
    private readonly CP6Context _db;
    public OeeService(CP6Context db) => _db = db;

    public async Task<List<OeeDailyDto>> SearchAsync(OeeSearchQuery q)
    {
        var query = _db.OeeDailies.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(q.MachineCd)) query = query.Where(x => x.MachineCd == q.MachineCd);
        if (q.DateFrom.HasValue) query = query.Where(x => x.OeeDate >= q.DateFrom.Value.Date);
        if (q.DateTo.HasValue) query = query.Where(x => x.OeeDate <= q.DateTo.Value.Date);

        var rows = await query.OrderByDescending(x => x.OeeDate).ThenBy(x => x.MachineCd).ToListAsync();

        var mcds = rows.Select(x => x.MachineCd).Distinct().ToList();
        var nameMap = await _db.Machines.AsNoTracking()
            .Where(m => mcds.Contains(m.MachineCd))
            .ToDictionaryAsync(m => m.MachineCd, m => m.MachineName);

        return rows.Select(x =>
        {
            var dto = ToDto(x);
            dto.MachineName = nameMap.GetValueOrDefault(x.MachineCd);
            return dto;
        }).ToList();
    }

    /// <summary>
    /// 本日（途中）OEE 計算：保存せず即時返却（リアルタイム表示用）
    /// </summary>
    public async Task<List<OeeDailyDto>> CalculateTodayAsync(string? machineCd = null)
    {
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);

        var machQ = _db.Machines.AsNoTracking().Where(m => !m.IsDeleted && m.ActiveFlg);
        if (!string.IsNullOrWhiteSpace(machineCd)) machQ = machQ.Where(m => m.MachineCd == machineCd);
        var machines = await machQ.ToListAsync();

        var result = new List<OeeDailyDto>();
        foreach (var m in machines)
        {
            var dto = await CalculateForAsync(m, today, tomorrow);
            result.Add(dto);
        }
        return result;
    }

    /// <summary>指定日 OEE 再計算 + 保存（バッチ・手動）</summary>
    public async Task<int> RecalculateAsync(OeeRecalcRequest req, string? userName)
    {
        var date = req.TargetDate.Date;
        var nextDay = date.AddDays(1);

        var machQ = _db.Machines.Where(m => !m.IsDeleted && m.ActiveFlg);
        if (!string.IsNullOrWhiteSpace(req.MachineCd)) machQ = machQ.Where(m => m.MachineCd == req.MachineCd);
        var machines = await machQ.ToListAsync();

        var count = 0;
        foreach (var m in machines)
        {
            var dto = await CalculateForAsync(m, date, nextDay);

            var existing = await _db.OeeDailies
                .FirstOrDefaultAsync(o => o.OeeDate == date && o.MachineCd == m.MachineCd);

            if (existing == null)
            {
                _db.OeeDailies.Add(new OeeDaily
                {
                    OeeDate = date,
                    MachineCd = m.MachineCd,
                    PlannedRunMinutes = dto.PlannedRunMinutes,
                    ActualRunMinutes = dto.ActualRunMinutes,
                    DowntimeMinutes = dto.DowntimeMinutes,
                    GoodQty = dto.GoodQty,
                    DefectQty = dto.DefectQty,
                    Availability = dto.Availability,
                    Performance = dto.Performance,
                    Quality = dto.Quality,
                    Oee = dto.Oee,
                    Creator = userName ?? "system",
                    CreateDate = DateTime.Now,
                });
            }
            else
            {
                existing.PlannedRunMinutes = dto.PlannedRunMinutes;
                existing.ActualRunMinutes = dto.ActualRunMinutes;
                existing.DowntimeMinutes = dto.DowntimeMinutes;
                existing.GoodQty = dto.GoodQty;
                existing.DefectQty = dto.DefectQty;
                existing.Availability = dto.Availability;
                existing.Performance = dto.Performance;
                existing.Quality = dto.Quality;
                existing.Oee = dto.Oee;
                existing.Modifier = userName ?? "system";
                existing.ModifyDate = DateTime.Now;
            }
            count++;
        }

        await _db.SaveChangesAsync();
        return count;
    }

    public async Task<Dictionary<string, List<OeeDailyDto>>> GetTrendAsync(int days, string? machineCd)
    {
        var from = DateTime.Today.AddDays(-days + 1);
        var to = DateTime.Today.AddDays(1);

        var query = _db.OeeDailies.AsNoTracking()
            .Where(x => !x.IsDeleted && x.OeeDate >= from && x.OeeDate < to);
        if (!string.IsNullOrWhiteSpace(machineCd)) query = query.Where(x => x.MachineCd == machineCd);

        var rows = await query.OrderBy(x => x.MachineCd).ThenBy(x => x.OeeDate).ToListAsync();
        return rows.GroupBy(x => x.MachineCd)
            .ToDictionary(g => g.Key, g => g.Select(ToDto).ToList());
    }

    // ═══════════════════════════════════════════════════════════
    //  計算ロジック（OEE 3 要素）
    // ═══════════════════════════════════════════════════════════

    private async Task<OeeDailyDto> CalculateForAsync(Machine m, DateTime dateFrom, DateTime dateTo)
    {
        // 1. 計画稼働時間
        var planned = m.PlannedRunMinutesPerDay > 0 ? m.PlannedRunMinutesPerDay : 480;

        // 2. 停止時間（指定日に重なる停止記録の合計）
        var downtimes = await _db.MachineDowntimes.AsNoTracking()
            .Where(d => !d.IsDeleted && d.MachineCd == m.MachineCd
                && d.StartTime < dateTo
                && (d.EndTime == null || d.EndTime >= dateFrom))
            .ToListAsync();

        var downtimeMinutes = 0;
        foreach (var d in downtimes)
        {
            var start = d.StartTime < dateFrom ? dateFrom : d.StartTime;
            var end = d.EndTime ?? DateTime.Now;
            if (end > dateTo) end = dateTo;
            if (end > start) downtimeMinutes += (int)(end - start).TotalMinutes;
        }
        if (downtimeMinutes > planned) downtimeMinutes = planned;
        var actualRun = planned - downtimeMinutes;

        // 3. 良品 / 不良数（ProductionResult から）
        var qty = await _db.ProductionResults.AsNoTracking()
            .Where(r => !r.IsDeleted && r.MachineCd == m.MachineCd
                && r.CreateDate >= dateFrom && r.CreateDate < dateTo)
            .GroupBy(_ => 1)
            .Select(g => new { Good = g.Sum(x => x.GoodQty), Defect = g.Sum(x => x.DefectQty) })
            .FirstOrDefaultAsync();

        var good = qty?.Good ?? 0m;
        var defect = qty?.Defect ?? 0m;
        var total = good + defect;

        // 4. 3 要素 + OEE
        var availability = planned > 0 ? Math.Round((decimal)actualRun / planned * 100m, 4) : 0m;
        var quality = total > 0 ? Math.Round(good / total * 100m, 4) : 0m;

        // 性能：理論能力（個/時間）× 実稼働時間 が分母
        decimal performance = 0m;
        if (m.CapacityPerHour.HasValue && m.CapacityPerHour > 0 && actualRun > 0)
        {
            var capacityForRun = m.CapacityPerHour.Value * (decimal)actualRun / 60m;
            if (capacityForRun > 0) performance = Math.Round(total / capacityForRun * 100m, 4);
            if (performance > 100m) performance = 100m;
        }
        else
        {
            performance = total > 0 ? 100m : 0m;
        }

        var oee = Math.Round(availability * performance * quality / 10000m, 4);

        return new OeeDailyDto
        {
            OeeDate = dateFrom,
            MachineCd = m.MachineCd,
            MachineName = m.MachineName,
            PlannedRunMinutes = planned,
            ActualRunMinutes = actualRun,
            DowntimeMinutes = downtimeMinutes,
            GoodQty = good,
            DefectQty = defect,
            Availability = availability,
            Performance = performance,
            Quality = quality,
            Oee = oee,
        };
    }

    private static OeeDailyDto ToDto(OeeDaily x) => new()
    {
        Id = x.Id,
        OeeDate = x.OeeDate,
        MachineCd = x.MachineCd,
        PlannedRunMinutes = x.PlannedRunMinutes,
        ActualRunMinutes = x.ActualRunMinutes,
        DowntimeMinutes = x.DowntimeMinutes,
        GoodQty = x.GoodQty,
        DefectQty = x.DefectQty,
        Availability = x.Availability,
        Performance = x.Performance,
        Quality = x.Quality,
        Oee = x.Oee,
        Remarks = x.Remarks,
    };
}
