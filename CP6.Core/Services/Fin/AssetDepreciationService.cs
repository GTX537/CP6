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

    public async Task<FinResult> PostAsync(Guid runId, string userId)
    {
        var run = await _db.DepreciationRuns.FindAsync(runId);
        if (run == null) return FinResult.Fail("FA006");
        if (run.Status != DepreciationRunStatus.Draft) return FinResult.Fail("FA009");
        if (run.JournalEntryId != null) return FinResult.Pass();

        var entries = await _db.DepreciationEntries.Where(e => e.RunId == runId).ToListAsync();
        if (entries.Any(e => e.Method == DepreciationMethod.UnitsOfProduction && e.WorkloadThisPeriod == null))
            return FinResult.Fail("FA008");

        var period = await _db.FiscalPeriods.FindAsync(run.FiscalPeriodId);
        var voucherDate = new DateTime(period!.Year, period.Month, 1).AddMonths(1).AddDays(-1);

        // 汇总凭证：借方按 (费用科目, 成本中心) 分组分行；贷方按累计折旧科目分组
        var lines = new List<JournalLine>();
        int lineNo = 1;
        foreach (var g in entries.Where(e => e.DepreciationAmount > 0m)
                     .GroupBy(e => new { e.DeprecExpenseAccountId, e.CostCenterId }))
            lines.Add(new JournalLine
            {
                LineNo = lineNo++, AccountId = g.Key.DeprecExpenseAccountId,
                Debit = g.Sum(e => e.DepreciationAmount), Credit = 0m, CostCenterId = g.Key.CostCenterId,
            });
        foreach (var g in entries.Where(e => e.DepreciationAmount > 0m).GroupBy(e => e.AccumDeprecAccountId))
            lines.Add(new JournalLine
            {
                LineNo = lineNo++, AccountId = g.Key, Debit = 0m, Credit = g.Sum(e => e.DepreciationAmount),
            });

        if (lines.Count > 0)
        {
            var je = new JournalEntry
            {
                Id = Guid.NewGuid(), VoucherDate = voucherDate, Source = VoucherSource.Depreciation,
                SourceDocNo = run.No, Description = $"月末折旧 {run.PeriodYearMonth}", Lines = lines,
            };
            var post = await _journal.AutoPostAsync(je);
            if (!post.Ok) return post;
            run.JournalEntryId = je.Id;
        }

        var cardIds = entries.Select(e => e.AssetCardId).ToList();
        var cards = await _db.AssetCards.Where(c => cardIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
        foreach (var e in entries)
        {
            if (!cards.TryGetValue(e.AssetCardId, out var card)) continue;
            card.AccumulatedDepreciation += e.DepreciationAmount;
            card.DepreciatedPeriods += 1;
            if (card.AccumulatedDepreciation >= card.OriginalValue - card.SalvageValue)
                card.Status = AssetStatus.FullyDepreciated;
        }
        run.Status = DepreciationRunStatus.Posted;
        run.PostedAt = DateTime.Now;
        run.PostedBy = userId;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task<FinResult> ReverseAsync(Guid runId, string userId, string reason)
    {
        var run = await _db.DepreciationRuns.FindAsync(runId);
        if (run == null) return FinResult.Fail("FA006");
        if (run.Status != DepreciationRunStatus.Posted) return FinResult.Fail("FA009");
        if (run.RunMode == DepreciationRunMode.DisposalFinal) return FinResult.Fail("FA011");

        var entries = await _db.DepreciationEntries.Where(e => e.RunId == runId).ToListAsync();
        var cardIds = entries.Select(e => e.AssetCardId).ToList();
        bool anyDisposed = await _db.AssetDisposals.AnyAsync(d => cardIds.Contains(d.AssetCardId)
            && d.Status != AssetDisposalStatus.Reversed);
        if (anyDisposed) return FinResult.Fail("FA011");

        if (run.JournalEntryId != null)
        {
            var rev = await _journal.ReverseAsync(run.JournalEntryId.Value, userId, reason, autoPost: true);
            if (!rev.Ok) return rev;
        }
        var cards = await _db.AssetCards.Where(c => cardIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
        foreach (var e in entries)
        {
            if (!cards.TryGetValue(e.AssetCardId, out var card)) continue;
            card.AccumulatedDepreciation -= e.DepreciationAmount;
            card.DepreciatedPeriods -= 1;
            if (card.Status == AssetStatus.FullyDepreciated
                && card.AccumulatedDepreciation < card.OriginalValue - card.SalvageValue)
                card.Status = AssetStatus.InUse;
        }
        run.Status = DepreciationRunStatus.Reversed;
        run.ReversedAt = DateTime.Now;
        run.ReversedBy = userId;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task<FinResult> SetWorkloadAsync(Guid entryId, decimal workload)
    {
        var entry = await _db.DepreciationEntries.FindAsync(entryId);
        if (entry == null) return FinResult.Fail("FA006");
        if (entry.Method != DepreciationMethod.UnitsOfProduction) return FinResult.Fail("FA008");
        var run = await _db.DepreciationRuns.FindAsync(entry.RunId);
        if (run == null || run.Status != DepreciationRunStatus.Draft) return FinResult.Fail("FA009");
        var card = await _db.AssetCards.FindAsync(entry.AssetCardId);
        if (card == null) return FinResult.Fail("FA006");
        if (card.TotalWorkload is null or <= 0m) return FinResult.Fail("FA008");

        var amount = _calc.PeriodAmount(new DepreciationCalcInput
        {
            Method = DepreciationMethod.UnitsOfProduction, OriginalValue = card.OriginalValue,
            SalvageValue = card.SalvageValue, UsefulLifeMonths = card.UsefulLifeMonths,
            DepreciatedPeriods = card.DepreciatedPeriods, AccumulatedBefore = card.AccumulatedDepreciation,
            TotalWorkload = card.TotalWorkload, WorkloadThisPeriod = workload,
        });
        entry.WorkloadThisPeriod = workload;
        entry.DepreciationAmount = amount;
        entry.ClosingAccumulated = entry.OpeningAccumulated + amount;
        entry.ClosingNetValue = entry.OpeningNetValue - amount;
        run.TotalAmount = await _db.DepreciationEntries.Where(e => e.RunId == run.Id && e.Id != entry.Id)
            .SumAsync(e => e.DepreciationAmount) + amount;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task<FinResult> AccrueAsync(Guid periodId, string userId)
    {
        var batch = await _db.DepreciationRuns
            .Where(r => r.FiscalPeriodId == periodId && r.RunMode != DepreciationRunMode.DisposalFinal
                        && r.Status != DepreciationRunStatus.Reversed)
            .OrderByDescending(r => r.RunAt).FirstOrDefaultAsync();

        if (batch is { Status: DepreciationRunStatus.Posted }) return FinResult.Pass();
        if (batch is { Status: DepreciationRunStatus.Draft })
            return await PostAsync(batch.Id, userId);

        var run = await RunAsync(periodId, userId, DepreciationRunMode.CloseHook);
        if (!run.Ok) return run;
        var created = await _db.DepreciationRuns.FirstAsync(r => r.FiscalPeriodId == periodId
            && r.RunMode == DepreciationRunMode.CloseHook && r.Status == DepreciationRunStatus.Draft);
        return await PostAsync(created.Id, userId);
    }

    public async Task<FinResult> PreCloseWorkloadCheckAsync(Guid periodId)
    {
        var period = await _db.FiscalPeriods.FindAsync(periodId);
        if (period == null) return FinResult.Fail("FA007");
        var ym = $"{period.Year:D4}-{period.Month:D2}";
        var eligible = await EligibleAsync(ym);
        bool anyMissing = eligible.Any(c => c.Method == DepreciationMethod.UnitsOfProduction);
        return anyMissing ? FinResult.Fail("FA008") : FinResult.Pass();
    }

    public async Task<DisposalFinalResult> AccrueDisposalFinalAsync(Guid assetCardId, Guid periodId, string userId)
    {
        var period = await _db.FiscalPeriods.FindAsync(periodId);
        if (period == null) return new() { Ok = false, Code = "FA007" };
        var ym = $"{period.Year:D4}-{period.Month:D2}";

        bool already = await (from de in _db.DepreciationEntries
                              join r in _db.DepreciationRuns on de.RunId equals r.Id
                              where de.AssetCardId == assetCardId && r.PeriodYearMonth == ym
                                    && r.Status != DepreciationRunStatus.Reversed
                              select de.Id).AnyAsync();
        if (already) return new() { Ok = true, Skipped = true };

        var card = await _db.AssetCards.FindAsync(assetCardId);
        if (card == null) return new() { Ok = false, Code = "FA006" };
        var cat = await _db.AssetCategories.FindAsync(card.CategoryId);
        if (cat == null) return new() { Ok = false, Code = "FA001" };
        var entry = await BuildEntryAsync(card, cat, periodId, ym);
        if (card.Method == DepreciationMethod.UnitsOfProduction)
            entry.DepreciationAmount = 0m;

        var run = new DepreciationRun
        {
            Id = Guid.NewGuid(), No = await _seq.NextAsync("DEP", new DateTime(period.Year, period.Month, 1)),
            FiscalPeriodId = periodId, PeriodYearMonth = ym, Status = DepreciationRunStatus.Draft,
            RunMode = DepreciationRunMode.DisposalFinal, AssetCount = 1, TotalAmount = entry.DepreciationAmount,
            RunAt = DateTime.Now, RunBy = userId,
        };
        entry.RunId = run.Id;
        _db.DepreciationRuns.Add(run);
        _db.DepreciationEntries.Add(entry);
        await _db.SaveChangesAsync();

        var post = await PostAsync(run.Id, userId);
        if (!post.Ok) return new() { Ok = false, Code = post.Code };
        return new() { Ok = true, RunId = run.Id, DeprecEntryId = entry.Id, Amount = entry.DepreciationAmount };
    }

    public async Task<List<DepreciationScheduleRow>> GetScheduleAsync(Guid assetCardId)
    {
        var card = await _db.AssetCards.FindAsync(assetCardId);
        var rows = new List<DepreciationScheduleRow>();
        if (card == null || string.IsNullOrEmpty(card.DepreciationStartPeriod)) return rows;

        decimal accum = card.AccumulatedDepreciation;
        int done = card.DepreciatedPeriods;
        var ym = DateTime.ParseExact(card.DepreciationStartPeriod + "-01", "yyyy-MM-dd", null).AddMonths(done);
        decimal cap = card.OriginalValue - card.SalvageValue;
        int Y = (int)Math.Ceiling(card.UsefulLifeMonths / 12.0);
        for (int i = 1; i <= 600 && accum < cap; i++)
        {
            int y = done / 12 + 1;
            decimal nbvYearStart = Y <= 0 ? card.OriginalValue - accum
                : card.OriginalValue * (decimal)Math.Pow((double)(1m - 2m / Math.Max(Y, 1)), Math.Max(y - 1, 0));
            decimal amount = card.Method == DepreciationMethod.UnitsOfProduction ? 0m : _calc.PeriodAmount(new DepreciationCalcInput
            {
                Method = card.Method, OriginalValue = card.OriginalValue, SalvageValue = card.SalvageValue,
                UsefulLifeMonths = card.UsefulLifeMonths, DepreciatedPeriods = done, AccumulatedBefore = accum,
                NetBookValueAtYearStart = nbvYearStart, TotalWorkload = card.TotalWorkload, WorkloadThisPeriod = null,
            });
            if (amount <= 0m) break;
            accum += amount; done += 1;
            rows.Add(new DepreciationScheduleRow
            {
                PeriodIndex = i, YearMonth = ym.ToString("yyyy-MM"),
                Amount = amount, Accumulated = accum, NetValue = card.OriginalValue - accum,
            });
            ym = ym.AddMonths(1);
        }
        return rows;
    }
}
