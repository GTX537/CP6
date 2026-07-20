using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public class SlottingService : ISlottingService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private const string Prefix = "SLP";

    public SlottingService(CP6Context db, IWmsSequenceService seq)
    {
        _db = db; _seq = seq;
    }

    public async Task<List<SlottingPlan>> SearchAsync(string? warehouseCd, int? status)
    {
        var query = _db.SlottingPlans.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(warehouseCd)) query = query.Where(x => x.WarehouseCd == warehouseCd);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        return await query.OrderByDescending(x => x.AnalyzedAt).Take(200).ToListAsync();
    }

    public async Task<SlottingPlanResult?> GetAsync(string planNo)
    {
        var p = await _db.SlottingPlans.AsNoTracking().FirstOrDefaultAsync(x => x.SlottingPlanNo == planNo && !x.IsDeleted);
        if (p == null) return null;
        var recs = string.IsNullOrWhiteSpace(p.RecommendationsJson)
            ? new List<SlottingRecommendation>()
            : JsonSerializer.Deserialize<List<SlottingRecommendation>>(p.RecommendationsJson!) ?? new();
        return new SlottingPlanResult { Plan = p, Recommendations = recs };
    }

    public async Task<string> AnalyzeAsync(string warehouseCd, int analysisDays, string? userName)
    {
        if (string.IsNullOrWhiteSpace(warehouseCd))
            throw new InvalidOperationException("warehouseCd required");
        if (analysisDays <= 0) analysisDays = 90;

        var since = DateTime.Today.AddDays(-analysisDays);

        // 1. 期間内 OUT トランザクションを製品別に集計
        var stats = await _db.StockTransactions.AsNoTracking()
            .Where(t => t.WarehouseCd == warehouseCd
                        && t.TxnType == WmsTxnType.OUT
                        && t.TxnDateTime >= since
                        && !t.IsDeleted)
            .GroupBy(t => t.ProductCd)
            .Select(g => new { ProductCd = g.Key, Count = g.Count(), Qty = g.Sum(t => t.Qty) })
            .ToListAsync();

        // 2. 累計構成比で ABC 分類（パレート 80/15/5）
        var classified = AbcClassifier.Classify(
            stats.Select(s => new AbcInputRow(s.ProductCd, s.Count, s.Qty)),
            AbcMetric.Frequency);
        var totalCount = stats.Sum(s => s.Count);
        var productCodes = classified.Select(s => s.ProductCd).ToList();
        var stockRows = await _db.Stocks.AsNoTracking()
            .Where(x => x.WarehouseCd == warehouseCd
                        && productCodes.Contains(x.ProductCd)
                        && !x.IsDeleted)
            .Select(x => new { x.ProductCd, x.LocationCd, x.PhysicalQty })
            .ToListAsync();
        var currentByProduct = stockRows
            .GroupBy(x => x.ProductCd)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.PhysicalQty).Select(x => x.LocationCd).FirstOrDefault());

        var recs = new List<SlottingRecommendation>();
        foreach (var s in classified)
        {
            var rank = s.AbcRank;
            string pattern = rank switch
            {
                AbcRank.A => "PIK-A-*",
                AbcRank.B => "PIK-B-*",
                _ => "RES-C-*",
            };

            // 現在ロケ：在庫数最大のロケ
            currentByProduct.TryGetValue(s.ProductCd, out var currentLoc);

            // 推奨と現状の prefix が違えば移動候補
            var prefix = pattern.Split('-')[0] + "-" + pattern.Split('-')[1] + "-"; // PIK-A- / PIK-B- / RES-C-
            bool needs = !string.IsNullOrWhiteSpace(currentLoc) && !currentLoc.StartsWith(prefix);

            recs.Add(new SlottingRecommendation
            {
                ProductCd = s.ProductCd,
                OutCount = s.OutCount,
                OutQty = s.OutQty,
                AbcRank = rank,
                CurrentLocationCd = currentLoc,
                RecommendedLocationPattern = pattern,
                NeedsRelocation = needs,
            });
        }

        var no = await _seq.NextAsync(Prefix);
        _db.SlottingPlans.Add(new SlottingPlan
        {
            SlottingPlanNo = no,
            WarehouseCd = warehouseCd,
            AnalysisDays = analysisDays,
            TxnSampleCount = totalCount,
            AnalyzedAt = DateTime.Now,
            Status = SlottingStatus.Recommended,
            RecommendationCount = recs.Count,
            RecommendationsJson = JsonSerializer.Serialize(recs),
            Creator = userName,
        });
        await _db.SaveChangesAsync();
        return no;
    }

    public async Task ApproveAsync(string planNo, string? userName)
    {
        var p = await _db.SlottingPlans.FirstOrDefaultAsync(x => x.SlottingPlanNo == planNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        if (p.Status != SlottingStatus.Recommended)
            throw new InvalidOperationException("WM-MSG-043: 推奨完了以外は承認不可");
        p.Status = SlottingStatus.Approved;
        p.ApproverCd = userName;
        p.Modifier = userName; p.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task CancelAsync(string planNo, string? userName)
    {
        var p = await _db.SlottingPlans.FirstOrDefaultAsync(x => x.SlottingPlanNo == planNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        p.Status = SlottingStatus.Cancelled;
        p.Modifier = userName; p.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }
}
