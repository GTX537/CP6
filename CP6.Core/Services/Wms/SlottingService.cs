using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Wms;

public class SlottingService : ISlottingService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private readonly IMobileTaskV2Service _tasks;
    private readonly IWmsAccessScopeProvider _accessScopes;
    private const string Prefix = "SLP";

    public SlottingService(
        CP6Context db,
        IWmsSequenceService seq,
        IMobileTaskV2Service tasks,
        IWmsAccessScopeProvider accessScopes)
    {
        _db = db;
        _seq = seq;
        _tasks = tasks;
        _accessScopes = accessScopes;
    }

    public async Task<List<SlottingPlan>> SearchAsync(string? warehouseCd, int? status)
    {
        var query = (await _accessScopes.GetCurrentAsync())
            .Apply(_db.SlottingPlans.AsNoTracking())
            .Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(warehouseCd)) query = query.Where(x => x.WarehouseCd == warehouseCd);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        return await query.OrderByDescending(x => x.AnalyzedAt).Take(200).ToListAsync();
    }

    public async Task<SlottingPlanResult?> GetAsync(string planNo)
    {
        var p = await (await _accessScopes.GetCurrentAsync())
            .Apply(_db.SlottingPlans.AsNoTracking())
            .FirstOrDefaultAsync(
                x => x.SlottingPlanNo == planNo && !x.IsDeleted);
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
        warehouseCd = warehouseCd.Trim();
        if (!(await _accessScopes.GetCurrentAsync())
            .AllowsWarehouse(warehouseCd))
            throw new WmsAccessDeniedException();
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
        var sorted = stats.OrderByDescending(s => s.Count).ThenByDescending(s => s.Qty).ToList();
        var totalCount = sorted.Sum(s => s.Count);
        decimal cumulative = 0;
        var recs = new List<SlottingRecommendation>();
        foreach (var s in sorted)
        {
            cumulative += s.Count;
            var ratio = totalCount > 0 ? (decimal)cumulative / totalCount : 0m;
            string rank = ratio <= 0.80m ? AbcRank.A : (ratio <= 0.95m ? AbcRank.B : AbcRank.C);
            string pattern = rank switch
            {
                AbcRank.A => "PIK-A-*",
                AbcRank.B => "PIK-B-*",
                _ => "RES-C-*",
            };

            // 現在ロケ：在庫数最大のロケ
            var currentLoc = await _db.Stocks.AsNoTracking()
                .Where(x => x.WarehouseCd == warehouseCd && x.ProductCd == s.ProductCd && !x.IsDeleted)
                .OrderByDescending(x => x.PhysicalQty)
                .Select(x => x.LocationCd)
                .FirstOrDefaultAsync();

            // 推奨と現状の prefix が違えば移動候補
            var prefix = pattern.Split('-')[0] + "-" + pattern.Split('-')[1] + "-"; // PIK-A- / PIK-B- / RES-C-
            bool needs = !string.IsNullOrWhiteSpace(currentLoc) && !currentLoc.StartsWith(prefix);

            recs.Add(new SlottingRecommendation
            {
                ProductCd = s.ProductCd,
                OutCount = s.Count,
                OutQty = s.Qty,
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

    public async Task<int> ApproveAsync(string planNo, string? userName)
    {
        await using var tx = await BeginTransactionAsync();
        try
        {
            var p = await (await _accessScopes.GetCurrentAsync())
                    .Apply(_db.SlottingPlans)
                    .FirstOrDefaultAsync(
                        x => x.SlottingPlanNo == planNo && !x.IsDeleted)
                ?? throw new InvalidOperationException("WM-MSG-070");
            if (p.Status != SlottingStatus.Recommended)
                throw new InvalidOperationException(
                    "WM-MSG-043: 推奨完了以外は承認不可");

            var recommendations = string.IsNullOrWhiteSpace(
                    p.RecommendationsJson)
                ? new List<SlottingRecommendation>()
                : JsonSerializer.Deserialize<List<SlottingRecommendation>>(
                    p.RecommendationsJson) ?? new();
            var candidates = recommendations
                .Where(x => x.NeedsRelocation)
                .ToList();
            var generated = 0;

            foreach (var recommendation in candidates)
            {
                recommendation.GenerationErrorCode = null;
                var source = await _db.Stocks.AsNoTracking()
                    .Where(x => !x.IsDeleted
                                && !x.RecallFlag
                                && x.WarehouseCd == p.WarehouseCd
                                && x.LocationCd
                                    == recommendation.CurrentLocationCd
                                && x.ProductCd
                                    == recommendation.ProductCd
                                && x.AvailableQty > 0m)
                    .OrderByDescending(x => x.AvailableQty)
                    .ThenBy(x => x.ExpiryDate ?? DateTime.MaxValue)
                    .FirstOrDefaultAsync();
                if (source is null)
                {
                    recommendation.GenerationErrorCode =
                        "WM-SLOTTING-SOURCE-STOCK-NOT-FOUND";
                    continue;
                }

                var targetPrefix =
                    recommendation.RecommendedLocationPattern?
                        .Replace("*", string.Empty)
                        .Trim();
                if (string.IsNullOrWhiteSpace(targetPrefix))
                {
                    recommendation.GenerationErrorCode =
                        "WM-SLOTTING-TARGET-PATTERN-INVALID";
                    continue;
                }

                var targets = await _db.Locations.AsNoTracking()
                    .Where(x => !x.IsDeleted
                                && !x.IsBlocked
                                && x.WarehouseCd == p.WarehouseCd
                                && x.LocationCd.StartsWith(targetPrefix)
                                && x.LocationCd
                                    != recommendation.CurrentLocationCd)
                    .OrderBy(x => x.LocationCd)
                    .ToListAsync();
                if (targets.Count == 0)
                {
                    recommendation.GenerationErrorCode =
                        "WM-SLOTTING-TARGET-LOCATION-NOT-FOUND";
                    continue;
                }

                MobileTaskV2Dto? task = null;
                foreach (var target in targets)
                {
                    try
                    {
                        task = await _tasks.CreateAsync(
                            new CreateMoveTaskV2Request
                            {
                                OperationId = Guid.NewGuid(),
                                Priority = recommendation.AbcRank == AbcRank.A
                                    ? 1
                                    : 2,
                                WarehouseCd = p.WarehouseCd,
                                AreaCd = target.AreaCd,
                                FromLocationCd = source.LocationCd,
                                ToLocationCd = target.LocationCd,
                                ProductCd = source.ProductCd,
                                LotNo = source.LotNo,
                                Qty = source.AvailableQty,
                                UnitCd = source.UnitCd,
                                Instruction =
                                    $"Slotting {p.SlottingPlanNo} / {recommendation.AbcRank}",
                                SourceType = "SLOTTING",
                                SourceNo = p.SlottingPlanNo
                            },
                            userName);
                        break;
                    }
                    catch (MobileTaskConflictException ex)
                        when (ex.Code == "WM-V2-TARGET-CAPACITY")
                    {
                        recommendation.GenerationErrorCode = ex.Code;
                    }
                }

                if (task is null) continue;
                recommendation.TargetLocationCd = task.ToLocationCd;
                recommendation.MoveQty = task.Qty;
                recommendation.MobileTaskNo = task.TaskNo;
                recommendation.GenerationErrorCode = null;
                generated++;
            }

            if (candidates.Count > 0 && generated == 0)
                throw new MobileTaskConflictException(
                    "WM-SLOTTING-NO-MOVE-TASK");

            p.Status = SlottingStatus.Approved;
            p.ApproverCd = userName;
            p.RecommendationsJson =
                JsonSerializer.Serialize(recommendations);
            p.Modifier = userName;
            p.ModifyDate = DateTime.Now;
            await _db.SaveChangesAsync();
            if (tx is not null) await tx.CommitAsync();
            return generated;
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync();
            throw;
        }
    }

    public async Task CancelAsync(string planNo, string? userName)
    {
        await using var tx = await BeginTransactionAsync();
        try
        {
            var p = await (await _accessScopes.GetCurrentAsync())
                    .Apply(_db.SlottingPlans)
                    .FirstOrDefaultAsync(
                        x => x.SlottingPlanNo == planNo && !x.IsDeleted)
                ?? throw new InvalidOperationException("WM-MSG-070");
            if (p.Status == SlottingStatus.Cancelled)
            {
                if (tx is not null) await tx.CommitAsync();
                return;
            }
            await _tasks.CancelPendingSourceTasksAsync(
                "SLOTTING", planNo, userName);
            p.Status = SlottingStatus.Cancelled;
            p.Modifier = userName;
            p.ModifyDate = DateTime.Now;
            await _db.SaveChangesAsync();
            if (tx is not null) await tx.CommitAsync();
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync();
            throw;
        }
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync()
        => _db.Database.IsRelational()
           && _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync()
            : null;
}
