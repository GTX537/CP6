using CP6.Core.EFDbContext;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

/// <summary>
/// MSBBME010 — 生産計画ボード 業務サービス実装
/// </summary>
public class PlanningBoardService : IPlanningBoardService
{
    private readonly CP6Context _db;
    public PlanningBoardService(CP6Context db) => _db = db;

    public async Task<List<PlanningBarDto>> GetBarsAsync(PlanningBoardQuery q)
    {
        var procQ = _db.WorkOrderProcesses.AsNoTracking().Where(p => !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(q.ProcessCd)) procQ = procQ.Where(p => p.ProcessCd == q.ProcessCd);
        if (!string.IsNullOrWhiteSpace(q.WgCd)) procQ = procQ.Where(p => p.WgCd == q.WgCd);
        if (!string.IsNullOrWhiteSpace(q.MachineCd)) procQ = procQ.Where(p => p.MachineCd == q.MachineCd);
        if (q.Statuses?.Any() == true) procQ = procQ.Where(p => q.Statuses.Contains(p.ProcessStatus));

        // 期間：計画 or 実績が範囲に重なる
        if (q.DateFrom.HasValue || q.DateTo.HasValue)
        {
            var from = q.DateFrom ?? DateTime.MinValue;
            var to = q.DateTo ?? DateTime.MaxValue;
            procQ = procQ.Where(p =>
                (p.PlanStartTime <= to && p.PlanEndTime >= from) ||
                (p.ActualStartTime <= to && (p.ActualEndTime ?? to) >= from));
        }

        var procs = await procQ.OrderBy(p => p.WorkOrderNo).ThenBy(p => p.SortOrder).ToListAsync();
        if (procs.Count == 0) return new();

        var woNos = procs.Select(p => p.WorkOrderNo).Distinct().ToList();
        var wos = await _db.WorkOrders.AsNoTracking()
            .Where(w => woNos.Contains(w.WorkOrderNo) && !w.IsDeleted
                && (string.IsNullOrEmpty(q.BaseCd) || w.BaseCd == q.BaseCd))
            .Select(w => new
            {
                w.WorkOrderNo, w.ProductCd, w.ProductName, w.CustomerCd,
                w.Status, w.Priority, w.DeliveryDate,
            })
            .ToDictionaryAsync(w => w.WorkOrderNo);

        return procs
            .Where(p => wos.ContainsKey(p.WorkOrderNo))
            .Select(p =>
            {
                var w = wos[p.WorkOrderNo];
                return new PlanningBarDto
                {
                    Id = p.Id,
                    WorkOrderNo = p.WorkOrderNo,
                    ProductCd = w.ProductCd,
                    ProductName = w.ProductName,
                    CustomerCd = w.CustomerCd,
                    ProcessCd = p.ProcessCd,
                    TaskCd = p.TaskCd,
                    ProcessName = p.ProcessName,
                    MachineCd = p.MachineCd,
                    WgCd = p.WgCd,
                    SortOrder = p.SortOrder,
                    ProcessStatus = p.ProcessStatus,
                    WorkOrderStatus = w.Status,
                    Priority = w.Priority,
                    PlanStartTime = p.PlanStartTime,
                    PlanEndTime = p.PlanEndTime,
                    ActualStartTime = p.ActualStartTime,
                    ActualEndTime = p.ActualEndTime,
                    DeliveryDate = w.DeliveryDate,
                    PlanQty = p.PlanQty,
                    GoodQty = p.GoodQty,
                    DefectQty = p.DefectQty,
                };
            })
            .ToList();
    }

    public async Task<PlanningKpiDto> GetKpiAsync(PlanningBoardQuery q)
    {
        var today = DateTime.Today;
        var woQ = _db.WorkOrders.AsNoTracking().Where(w => !w.IsDeleted && w.Status != 9);
        if (!string.IsNullOrWhiteSpace(q.BaseCd)) woQ = woQ.Where(w => w.BaseCd == q.BaseCd);

        var stats = await woQ
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                InProgress = g.Count(w => w.Status == 3),
                Completed = g.Count(w => w.Status == 4 || w.Status == 6),
                Delayed = g.Count(w => w.PlanEndDate < today && w.Status != 4 && w.Status != 6),
                AvgDelay = g.Where(w => w.PlanEndDate < today && w.Status != 4 && w.Status != 6 && w.PlanEndDate != null)
                            .Average(w => (double?)EF.Functions.DateDiffDay(w.PlanEndDate!.Value, today)) ?? 0,
            })
            .FirstOrDefaultAsync();

        // 計画消化率：完了工程 / 全工程
        var procStats = await _db.WorkOrderProcesses.AsNoTracking()
            .Where(p => !p.IsDeleted)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Done = g.Count(p => p.ProcessStatus == 2),
            })
            .FirstOrDefaultAsync();

        return new PlanningKpiDto
        {
            TotalOrders = stats?.Total ?? 0,
            InProgressOrders = stats?.InProgress ?? 0,
            CompletedOrders = stats?.Completed ?? 0,
            DelayedOrders = stats?.Delayed ?? 0,
            AvgDelayDays = (decimal)Math.Round(stats?.AvgDelay ?? 0, 1),
            CompletionRate = procStats != null && procStats.Total > 0
                ? Math.Round((decimal)procStats.Done / procStats.Total * 100m, 2)
                : 0m,
        };
    }

    public async Task RescheduleAsync(RescheduleRequest req, string? userName)
    {
        var p = await _db.WorkOrderProcesses.FirstOrDefaultAsync(x => x.Id == req.Id && !x.IsDeleted)
            ?? throw new InvalidOperationException("ME-MSG-043");

        // 発行済み以降は変更不可（仕様書 §2.3 ドラッグはステータス=0/1 のみ）
        // 但しサーバー側は工程ステータスでチェック：未着手のみ移動可
        if (p.ProcessStatus != 0)
            throw new InvalidOperationException("ME-MSG-042");

        if (req.PlanStartTime > req.PlanEndTime)
            throw new InvalidOperationException("ME-MSG-003");

        p.PlanStartTime = req.PlanStartTime;
        p.PlanEndTime = req.PlanEndTime;
        if (!string.IsNullOrWhiteSpace(req.MachineCd)) p.MachineCd = req.MachineCd;
        p.Modifier = userName;
        p.ModifyDate = DateTime.Now;

        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// 自動配置：未着手の工程を、所属指図の優先度＋納期で並べ、号機別に連続配置
    /// </summary>
    public async Task<int> AutoArrangeAsync(AutoArrangeRequest req, string? userName)
    {
        var procQ = _db.WorkOrderProcesses.Where(p => !p.IsDeleted && p.ProcessStatus == 0);
        if (!string.IsNullOrWhiteSpace(req.WgCd)) procQ = procQ.Where(p => p.WgCd == req.WgCd);
        if (!string.IsNullOrWhiteSpace(req.ProcessCd)) procQ = procQ.Where(p => p.ProcessCd == req.ProcessCd);

        var procs = await procQ.ToListAsync();
        if (procs.Count == 0) return 0;

        var woNos = procs.Select(p => p.WorkOrderNo).Distinct().ToList();
        var wos = await _db.WorkOrders.AsNoTracking()
            .Where(w => woNos.Contains(w.WorkOrderNo) && !w.IsDeleted)
            .Select(w => new { w.WorkOrderNo, w.Priority, w.DeliveryDate })
            .ToDictionaryAsync(w => w.WorkOrderNo);

        // 優先度降順（3=特急 → 2=急ぎ → 1=通常）、納期昇順
        var ordered = procs
            .Where(p => wos.ContainsKey(p.WorkOrderNo))
            .OrderByDescending(p => wos[p.WorkOrderNo].Priority)
            .ThenBy(p => wos[p.WorkOrderNo].DeliveryDate ?? DateTime.MaxValue)
            .ThenBy(p => p.SortOrder)
            .ToList();

        // 号機別の現在時刻ポインタ
        var machinePointer = new Dictionary<string, DateTime>();
        var baseTime = req.BaseDate.Date.AddHours(8); // 朝8時開始

        var changed = 0;
        foreach (var p in ordered)
        {
            var machine = p.MachineCd ?? "(unspecified)";
            if (!machinePointer.TryGetValue(machine, out var cursor))
                cursor = baseTime;

            var hours = req.DefaultHoursPerJob > 0 ? req.DefaultHoursPerJob : 2;
            // LeadTime（日）を時間に換算 — null 時 default
            if (p.LeadTime.HasValue && p.LeadTime > 0)
                hours = (int)Math.Max(1m, p.LeadTime.Value * 8m);

            p.PlanStartTime = cursor;
            p.PlanEndTime = cursor.AddHours(hours);
            machinePointer[machine] = p.PlanEndTime.Value;
            p.Modifier = userName;
            p.ModifyDate = DateTime.Now;
            changed++;
        }

        await _db.SaveChangesAsync();
        return changed;
    }
}
