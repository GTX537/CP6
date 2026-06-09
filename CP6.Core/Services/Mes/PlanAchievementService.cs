using System.Globalization;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

/// <inheritdoc />
public class PlanAchievementService : IPlanAchievementService
{
    private static readonly string[] CsvHeader =
    [
        "GroupKey",
        "GroupLabel",
        "WorkOrderCount",
        "PlannedQty",
        "GoodQty",
        "DefectQty",
        "AchievementRate",
        "DefectRate",
        "OnTargetCount",
    ];

    private readonly CP6Context _db;

    public PlanAchievementService(CP6Context db)
    {
        _db = db;
    }

    public async Task<PlanAchievementSummaryDto> GetSummaryAsync(PlanAchievementQuery query, CancellationToken ct = default)
    {
        query ??= new PlanAchievementQuery();
        var groupBy = NormalizeGroupBy(query.GroupBy);

        var woQuery = _db.WorkOrders.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Status != WorkOrderStatus.Cancelled && x.ProductionQty > 0);

        if (query.OnlyCompleted)
        {
            woQuery = woQuery.Where(x => x.Status == WorkOrderStatus.Completed || x.Status == WorkOrderStatus.Inspected);
        }
        else
        {
            // 発行済以降（着手前の計画段階は達成率に意味がないため除外）
            woQuery = woQuery.Where(x => x.Status >= WorkOrderStatus.Issued);
        }

        if (!string.IsNullOrWhiteSpace(query.ProductCd))
        {
            var p = query.ProductCd.Trim();
            woQuery = woQuery.Where(x => x.ProductCd == p);
        }

        if (!string.IsNullOrWhiteSpace(query.CustomerCd))
        {
            var c = query.CustomerCd.Trim();
            woQuery = woQuery.Where(x => x.CustomerCd == c);
        }

        var workOrders = await woQuery.ToListAsync(ct);

        // 基準日フィルタ（ActualEndDate ?? PlanEndDate ?? PlanStartDate）はメモリ側で評価
        var from = query.DateFrom?.Date;
        var toExclusive = query.DateTo?.Date.AddDays(1);
        var facts = workOrders
            .Select(wo => ToFact(wo, groupBy))
            .Where(f => f != null).Select(f => f!)
            .Where(f => (from == null || f.RefDate >= from) && (toExclusive == null || f.RefDate < toExclusive))
            .ToList();

        var rows = facts
            .GroupBy(f => new { f.GroupKey, f.GroupLabel })
            .Select(g => BuildRow(g.Key.GroupKey, g.Key.GroupLabel, g))
            .OrderBy(r => r.AchievementRate)   // 達成率の低い順 = 要改善を上に
            .ThenBy(r => r.GroupKey)
            .ToList();

        var planned = facts.Sum(f => f.Planned);
        var good = facts.Sum(f => f.Good);
        var defect = facts.Sum(f => f.Defect);

        return new PlanAchievementSummaryDto
        {
            TotalWorkOrders = facts.Count,
            TotalPlannedQty = planned,
            TotalGoodQty = good,
            TotalDefectQty = defect,
            AchievementRate = Rate(good, planned),
            DefectRate = Rate(defect, good + defect),
            OnTargetCount = facts.Count(f => f.Planned > 0 && f.Good >= f.Planned),
            Rows = rows,
        };
    }

    public async Task<byte[]> ExportCsvAsync(PlanAchievementQuery query, CancellationToken ct = default)
    {
        var summary = await GetSummaryAsync(query, ct);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", CsvHeader));
        foreach (var row in summary.Rows)
        {
            sb.AppendLine(string.Join(",", new[]
            {
                Csv(row.GroupKey),
                Csv(row.GroupLabel),
                row.WorkOrderCount.ToString(CultureInfo.InvariantCulture),
                FormatDecimal(row.PlannedQty),
                FormatDecimal(row.GoodQty),
                FormatDecimal(row.DefectQty),
                FormatDecimal(row.AchievementRate),
                FormatDecimal(row.DefectRate),
                row.OnTargetCount.ToString(CultureInfo.InvariantCulture),
            }));
        }
        var content = Encoding.UTF8.GetBytes(sb.ToString());
        return Encoding.UTF8.GetPreamble().Concat(content).ToArray();
    }

    private static PlanFact? ToFact(WorkOrder wo, string groupBy)
    {
        var refDate = (wo.ActualEndDate ?? wo.PlanEndDate ?? wo.PlanStartDate)?.Date;
        if (refDate == null)
        {
            return null;
        }

        string key, label;
        switch (groupBy)
        {
            case PlanAchievementGroupBy.Month:
                key = refDate.Value.ToString("yyyyMM", CultureInfo.InvariantCulture);
                label = refDate.Value.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                break;
            case PlanAchievementGroupBy.Customer:
                key = string.IsNullOrWhiteSpace(wo.CustomerCd) ? "(none)" : wo.CustomerCd!;
                label = key;
                break;
            default: // product
                key = wo.ProductCd;
                label = string.IsNullOrWhiteSpace(wo.ProductName) ? wo.ProductCd : wo.ProductName!;
                break;
        }

        return new PlanFact(key, label, refDate.Value, wo.ProductionQty, wo.CompletedQty, wo.DefectQty);
    }

    private static PlanAchievementRowDto BuildRow(string key, string label, IEnumerable<PlanFact> facts)
    {
        var list = facts.ToList();
        var planned = list.Sum(f => f.Planned);
        var good = list.Sum(f => f.Good);
        var defect = list.Sum(f => f.Defect);
        return new PlanAchievementRowDto
        {
            GroupKey = key,
            GroupLabel = label,
            WorkOrderCount = list.Count,
            PlannedQty = planned,
            GoodQty = good,
            DefectQty = defect,
            AchievementRate = Rate(good, planned),
            DefectRate = Rate(defect, good + defect),
            OnTargetCount = list.Count(f => f.Planned > 0 && f.Good >= f.Planned),
        };
    }

    private static string NormalizeGroupBy(string? groupBy)
    {
        if (string.Equals(groupBy, PlanAchievementGroupBy.Month, StringComparison.OrdinalIgnoreCase))
            return PlanAchievementGroupBy.Month;
        if (string.Equals(groupBy, PlanAchievementGroupBy.Customer, StringComparison.OrdinalIgnoreCase))
            return PlanAchievementGroupBy.Customer;
        return PlanAchievementGroupBy.Product;
    }

    private static decimal Rate(decimal numerator, decimal denominator)
        => denominator == 0 ? 0m : decimal.Round(numerator / denominator, 4, MidpointRounding.AwayFromZero);

    private static string Csv(string? value)
    {
        var text = value ?? string.Empty;
        return text.Contains(',') || text.Contains('"') || text.Contains('\r') || text.Contains('\n')
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }

    private static string FormatDecimal(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private sealed record PlanFact(string GroupKey, string GroupLabel, DateTime RefDate, decimal Planned, decimal Good, decimal Defect);
}
