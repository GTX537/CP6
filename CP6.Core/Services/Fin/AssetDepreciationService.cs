using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>资产折旧服务（三路：手动 Run/Post、Worker 备草稿、结账钩子 Accrue，spec §3.2）。仿 FxRevaluationService 直建凭证。</summary>
public sealed class AssetDepreciationService : IAssetDepreciationService
{
    private readonly CP6Context _db;
    private readonly IDepreciationCalculator _calc;
    private readonly IJournalEntryService _journal;
    private readonly IFiscalPeriodService _periods;
    private readonly IFinSequenceService _seq;

    public AssetDepreciationService(CP6Context db, IDepreciationCalculator calc, IJournalEntryService journal,
        IFiscalPeriodService periods, IFinSequenceService seq)
    {
        _db = db; _calc = calc; _journal = journal; _periods = periods; _seq = seq;
    }

    // ── 资格集（spec §3.2 计提资格，期 P）──
    private async Task<List<AssetCard>> EligibleAsync(string periodYm)
    {
        var doneCardIds = await (from de in _db.DepreciationEntries
                                 join run in _db.DepreciationRuns on de.RunId equals run.Id
                                 where run.Status != DepreciationRunStatus.Reversed
                                       && run.PeriodYearMonth == periodYm
                                 select de.AssetCardId).Distinct().ToListAsync();

        var cards = await _db.AssetCards
            .Where(c => c.Status == AssetStatus.InUse
                        && c.DepreciationStartPeriod != null
                        && string.Compare(c.DepreciationStartPeriod, periodYm) <= 0
                        && c.AccumulatedDepreciation < c.OriginalValue - c.SalvageValue
                        && !doneCardIds.Contains(c.Id))
            .ToListAsync();
        return cards;
    }

    private async Task<Guid?> DeriveCostCenterAsync(AssetCard card)
    {
        if (card.CostCenterId.HasValue) return card.CostCenterId;
        if (card.MachineId.HasValue)
        {
            var mid = card.MachineId.Value.ToString();
            var cc = await _db.CostCenters.FirstOrDefaultAsync(c => c.LinkMachineId == mid && c.IsActive);
            if (cc != null) return cc.Id;
        }
        return null;
    }

    private async Task<DepreciationEntry> BuildEntryAsync(AssetCard card, AssetCategory cat, Guid periodId, string periodYm)
    {
        int Y = (int)Math.Ceiling(card.UsefulLifeMonths / 12.0);
        int y = card.DepreciatedPeriods / 12 + 1;
        decimal nbvYearStart = Y <= 0 ? card.NetBookValue
            : card.OriginalValue * (decimal)Math.Pow((double)(1m - 2m / Math.Max(Y, 1)), Math.Max(y - 1, 0));

        var input = new DepreciationCalcInput
        {
            Method = card.Method, OriginalValue = card.OriginalValue, SalvageValue = card.SalvageValue,
            UsefulLifeMonths = card.UsefulLifeMonths, DepreciatedPeriods = card.DepreciatedPeriods,
            AccumulatedBefore = card.AccumulatedDepreciation, NetBookValueAtYearStart = nbvYearStart,
            TotalWorkload = card.TotalWorkload, WorkloadThisPeriod = null,
        };
        decimal amount = card.Method == DepreciationMethod.UnitsOfProduction ? 0m : _calc.PeriodAmount(input);

        var expAcc = card.DeprecExpenseAccountId ?? cat.DeprecExpenseAccountId;
        return new DepreciationEntry
        {
            Id = Guid.NewGuid(), AssetCardId = card.Id, FiscalPeriodId = periodId, Method = card.Method,
            DepreciationAmount = amount,
            OpeningAccumulated = card.AccumulatedDepreciation, ClosingAccumulated = card.AccumulatedDepreciation + amount,
            OpeningNetValue = card.NetBookValue, ClosingNetValue = card.NetBookValue - amount,
            DeprecExpenseAccountId = expAcc, AccumDeprecAccountId = cat.AccumDeprecAccountId,
            CostCenterId = await DeriveCostCenterAsync(card),
            WorkloadThisPeriod = null,
        };
    }

    public async Task<FinResult> RunAsync(Guid periodId, string userId, DepreciationRunMode mode)
    {
        var period = await _db.FiscalPeriods.FindAsync(periodId);
        if (period == null) return FinResult.Fail("FA007");
        if (period.Status != PeriodStatus.Open) return FinResult.Fail("FA007");
        var ym = $"{period.Year:D4}-{period.Month:D2}";

        bool batchExists = await _db.DepreciationRuns.AnyAsync(r => r.FiscalPeriodId == periodId
            && r.RunMode != DepreciationRunMode.DisposalFinal && r.Status != DepreciationRunStatus.Reversed);
        if (batchExists) return FinResult.Fail("FA003");

        var cards = await EligibleAsync(ym);
        var catIds = cards.Select(c => c.CategoryId).Distinct().ToList();
        var cats = await _db.AssetCategories.Where(c => catIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);

        var run = new DepreciationRun
        {
            Id = Guid.NewGuid(), No = await _seq.NextAsync("DEP", new DateTime(period.Year, period.Month, 1)),
            FiscalPeriodId = periodId, PeriodYearMonth = ym, Status = DepreciationRunStatus.Draft, RunMode = mode,
            RunAt = DateTime.Now, RunBy = userId,
        };
        decimal total = 0m;
        foreach (var card in cards)
        {
            if (!cats.TryGetValue(card.CategoryId, out var cat)) return FinResult.Fail("FA001");
            var entry = await BuildEntryAsync(card, cat, periodId, ym);
            entry.RunId = run.Id;
            _db.DepreciationEntries.Add(entry);
            total += entry.DepreciationAmount;
        }
        run.TotalAmount = total;
        run.AssetCount = cards.Count;
        _db.DepreciationRuns.Add(run);
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task<List<DepreciationEntryDto>> PreviewAsync(Guid periodId)
    {
        var period = await _db.FiscalPeriods.FindAsync(periodId);
        if (period == null) return new();
        var ym = $"{period.Year:D4}-{period.Month:D2}";
        var cards = await EligibleAsync(ym);
        var catIds = cards.Select(c => c.CategoryId).Distinct().ToList();
        var cats = await _db.AssetCategories.Where(c => catIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
        var list = new List<DepreciationEntryDto>();
        foreach (var card in cards)
        {
            if (!cats.TryGetValue(card.CategoryId, out var cat)) continue;
            var e = await BuildEntryAsync(card, cat, periodId, ym);
            list.Add(new DepreciationEntryDto
            {
                AssetCardId = card.Id, AssetNo = card.AssetNo, AssetName = card.Name, Method = card.Method,
                DepreciationAmount = e.DepreciationAmount, OpeningAccumulated = e.OpeningAccumulated,
                ClosingAccumulated = e.ClosingAccumulated, DeprecExpenseAccountId = e.DeprecExpenseAccountId,
                AccumDeprecAccountId = e.AccumDeprecAccountId, CostCenterId = e.CostCenterId,
                WorkloadThisPeriod = null,
            });
        }
        return list;
    }

    public Task<FinResult> SetWorkloadAsync(Guid entryId, decimal workload) => throw new NotImplementedException();
    public Task<FinResult> PostAsync(Guid runId, string userId) => throw new NotImplementedException();
    public Task<FinResult> ReverseAsync(Guid runId, string userId, string reason) => throw new NotImplementedException();
    public Task<FinResult> AccrueAsync(Guid periodId, string userId) => throw new NotImplementedException();
    public Task<FinResult> PreCloseWorkloadCheckAsync(Guid periodId) => throw new NotImplementedException();
    public Task<DisposalFinalResult> AccrueDisposalFinalAsync(Guid a, Guid p, string u) => throw new NotImplementedException();
    public Task<List<DepreciationScheduleRow>> GetScheduleAsync(Guid assetCardId) => throw new NotImplementedException();
}
