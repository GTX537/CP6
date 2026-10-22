using CP6.Core.EFDbContext;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

/// <summary>
/// MSBBME090 — MES ダッシュボード 業務サービス実装
/// </summary>
public partial class MesDashboardService : IMesDashboardService
{
    private readonly CP6Context _db;
    private readonly CP6.Core.Services.Integration.OperationsOwnerV1? _owner;
    public MesDashboardService(CP6Context db, CP6.Core.Services.Integration.OperationsOwnerV1? owner=null) { _db=db; _owner=owner; }

    private async Task<MesDashboardSummaryDto> GetSummaryCoreAsync(MesDashboardData? data=null,DateTime? calendar=null)
    {
        var today = (calendar ?? DateTime.Today);
        var tomorrow = today.AddDays(1);

        var inProgress = await (data?.WorkOrders ?? _db.WorkOrders.AsNoTracking())
            .CountAsync(w => !w.IsDeleted && w.Status == 3);

        var completedToday = await (data?.WorkOrders ?? _db.WorkOrders.AsNoTracking())
            .CountAsync(w => !w.IsDeleted
                && (w.Status == 4 || w.Status == 6)
                && w.ActualEndDate >= today && w.ActualEndDate < tomorrow);

        // 本日の良品/不良数（ProductionResult 集計）
        var qty = await (data?.ProductionResults ?? _db.ProductionResults.AsNoTracking())
            .Where(r => !r.IsDeleted && r.CreateDate >= today && r.CreateDate < tomorrow)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Good = g.Sum(x => x.GoodQty),
                Defect = g.Sum(x => x.DefectQty),
            })
            .FirstOrDefaultAsync();

        var totalGood = qty?.Good ?? 0m;
        var totalDefect = qty?.Defect ?? 0m;
        var sum = totalGood + totalDefect;
        var defectRate = sum > 0 ? Math.Round(totalDefect / sum * 100m, 2) : 0m;

        var delayed = await (data?.WorkOrders ?? _db.WorkOrders.AsNoTracking())
            .CountAsync(w => !w.IsDeleted
                && w.PlanEndDate < today
                && w.Status != 4 && w.Status != 6 && w.Status != 9);

        return new MesDashboardSummaryDto
        {
            InProgressCount = inProgress,
            CompletedCount = completedToday,
            TotalGoodQty = totalGood,
            TotalDefectQty = totalDefect,
            DefectRate = defectRate,
            DelayedCount = delayed,
        };
    }

    private async Task<List<ProcessProgressDto>> GetProcessProgressCoreAsync(MesDashboardData? data=null,DateTime? calendar=null)
    {
        var rows = await (data?.WorkOrderProcesses ?? _db.WorkOrderProcesses.AsNoTracking())
            .Where(p => !p.IsDeleted)
            .GroupBy(p => new { p.ProcessCd, p.ProcessName })
            .Select(g => new ProcessProgressDto
            {
                ProcessCd = g.Key.ProcessCd,
                ProcessName = g.Key.ProcessName,
                NotStarted = g.Count(x => x.ProcessStatus == 0),
                InProgress = g.Count(x => x.ProcessStatus == 1 || x.ProcessStatus == 3),
                Completed = g.Count(x => x.ProcessStatus == 2),
            })
            .OrderBy(x => x.ProcessCd)
            .ToListAsync();
        return rows;
    }

    private async Task<List<DelayAlertDto>> GetDelayAlertsCoreAsync(int top = 50,MesDashboardData? data=null,DateTime? calendar=null)
    {
        var today = (calendar ?? DateTime.Today);
        var wos = await (data?.WorkOrders ?? _db.WorkOrders.AsNoTracking())
            .Where(w => !w.IsDeleted
                && w.PlanEndDate < today
                && w.Status != 4 && w.Status != 6 && w.Status != 9)
            .OrderBy(w => w.PlanEndDate)
            .Take(top)
            .ToListAsync();

        return wos.Select(w => new DelayAlertDto
        {
            WorkOrderNo = w.WorkOrderNo,
            ProductCd = w.ProductCd,
            ProductName = w.ProductName,
            CustomerCd = w.CustomerCd,
            PlanEndDate = w.PlanEndDate,
            DeliveryDate = w.DeliveryDate,
            DelayDays = w.PlanEndDate.HasValue ? (today - w.PlanEndDate.Value.Date).Days : 0,
            Status = w.Status,
            ProgressRate = w.ProductionQty > 0 ? Math.Round(w.CompletedQty / w.ProductionQty * 100m, 2) : 0m,
        }).ToList();
    }

    private async Task<List<DailyTrendDto>> GetDailyTrendCoreAsync(int days = 30,MesDashboardData? data=null,DateTime? calendar=null)
    {
        var from = (calendar ?? DateTime.Today).AddDays(-days + 1);
        var to = (calendar ?? DateTime.Today).AddDays(1);

        var raw = await (data?.ProductionResults ?? _db.ProductionResults.AsNoTracking())
            .Where(r => !r.IsDeleted && r.CreateDate >= from && r.CreateDate < to)
            .GroupBy(r => r.CreateDate.Date)
            .Select(g => new
            {
                Date = g.Key,
                Good = g.Sum(x => x.GoodQty),
                Defect = g.Sum(x => x.DefectQty),
            })
            .ToListAsync();

        var map = raw.ToDictionary(x => x.Date, x => (x.Good, x.Defect));
        var list = new List<DailyTrendDto>();
        for (var d = from; d < to; d = d.AddDays(1))
        {
            map.TryGetValue(d.Date, out var v);
            list.Add(new DailyTrendDto
            {
                Date = d.ToString("yyyy-MM-dd"),
                GoodQty = v.Good,
                DefectQty = v.Defect,
            });
        }
        return list;
    }

    private async Task<List<DefectTop5Dto>> GetDefectTop5CoreAsync(MesDashboardData? data=null,DateTime? calendar=null)
    {
        var since = (calendar ?? DateTime.Today).AddDays(-30);

        var rows = await (data?.DefectRecords ?? _db.DefectRecords.AsNoTracking())
            .Where(d => !d.IsDeleted && d.OccurDate >= since)
            .GroupBy(d => d.CategoryCd)
            .Select(g => new
            {
                CategoryCd = g.Key,
                Count = g.Count(),
                Qty = g.Sum(x => x.DefectQty),
            })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToListAsync();

        var cats = await _db.DefectCategories.AsNoTracking()
            .Where(c => !c.IsDeleted)
            .Select(c => new { c.CategoryCd, c.CategoryName })
            .Distinct()
            .ToListAsync();
        var catMap = cats.GroupBy(c => c.CategoryCd).ToDictionary(g => g.Key, g => g.First().CategoryName);

        return rows.Select(x => new DefectTop5Dto
        {
            CategoryCd = x.CategoryCd,
            CategoryName = catMap.GetValueOrDefault(x.CategoryCd),
            Count = x.Count,
            Qty = x.Qty,
        }).ToList();
    }

    private async Task<List<RecentCompletedDto>> GetRecentCompletedCoreAsync(int top = 10,MesDashboardData? data=null,DateTime? calendar=null)
    {
        var rows = await (data?.WorkOrders ?? _db.WorkOrders.AsNoTracking())
            .Where(w => !w.IsDeleted && (w.Status == 4 || w.Status == 6))
            .OrderByDescending(w => w.ActualEndDate ?? w.ModifyDate ?? w.CreateDate)
            .Take(top)
            .ToListAsync();

        return rows.Select(w => new RecentCompletedDto
        {
            WorkOrderNo = w.WorkOrderNo,
            ProductCd = w.ProductCd,
            ProductName = w.ProductName,
            ProductionQty = w.ProductionQty,
            CompletedQty = w.CompletedQty,
            ActualEndDate = w.ActualEndDate,
        }).ToList();
    }

    private async Task<List<MachineHeatmapDto>> GetMachineHeatmapCoreAsync(int days = 7,MesDashboardData? data=null,DateTime? calendar=null)
    {
        var from = (calendar ?? DateTime.Today).AddDays(-days);

        // 過去 7 日の ProductionResult を 号機×時間帯（hour）で集計
        var rows = await (data?.ProductionResults ?? _db.ProductionResults.AsNoTracking())
            .Where(r => !r.IsDeleted && r.MachineCd != null && r.CreateDate >= from)
            .Select(r => new { r.MachineCd, r.CreateDate })
            .ToListAsync();

        return rows
            .GroupBy(r => new { Machine = r.MachineCd!, Hour = r.CreateDate.Hour })
            .Select(g => new MachineHeatmapDto
            {
                MachineCd = g.Key.Machine,
                Hour = g.Key.Hour,
                Count = g.Count(),
            })
            .OrderBy(x => x.MachineCd).ThenBy(x => x.Hour)
            .ToList();
    }
}
