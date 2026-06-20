using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>资产处置服务（出售/报废/转让/盘亏，经清理科目结转，spec §4）。仿 FxRevaluationService 直建凭证。</summary>
public sealed class AssetDisposalService : IAssetDisposalService
{
    private readonly CP6Context _db;
    private readonly IJournalEntryService _journal;
    private readonly IFiscalPeriodService _periods;
    private readonly IFinSequenceService _seq;
    private readonly IAssetDepreciationService _deprec;

    public AssetDisposalService(CP6Context db, IJournalEntryService journal, IFiscalPeriodService periods,
        IFinSequenceService seq, IAssetDepreciationService deprec)
    {
        _db = db; _journal = journal; _periods = periods; _seq = seq; _deprec = deprec;
    }

    private async Task<Guid?> RoleIdAsync(string role)
        => (await _db.GlAccounts.FirstOrDefaultAsync(a => a.Role == role && a.IsActive))?.Id;

    private async Task<(Guid? clearing, Guid? gainLoss)> ResolveAccountsAsync(AssetDisposalType type, decimal netGainLoss)
    {
        Guid? clearing = type == AssetDisposalType.InventoryLoss
            ? await RoleIdAsync("PENDING_PROPERTY_LOSS")
            : await RoleIdAsync("ASSET_CLEARING");
        Guid? gainLoss = type switch
        {
            AssetDisposalType.Sale or AssetDisposalType.Transfer => await RoleIdAsync("ASSET_DISPOSAL_PL"),
            AssetDisposalType.InventoryLoss => await RoleIdAsync("NON_OP_EXPENSE"),
            AssetDisposalType.Scrap => netGainLoss >= 0
                ? await RoleIdAsync("NON_OP_INCOME")
                : await RoleIdAsync("NON_OP_EXPENSE"),
            _ => null,
        };
        return (clearing, gainLoss);
    }

    public async Task<FinResult> CreateAsync(AssetDisposal d, string userId)
    {
        var card = await _db.AssetCards.FindAsync(d.AssetCardId);
        if (card == null) return FinResult.Fail("FA006");
        if (card.Status is not (AssetStatus.InUse or AssetStatus.FullyDepreciated)) return FinResult.Fail("FA002");
        if (await _db.AssetDisposals.AnyAsync(x => x.AssetCardId == d.AssetCardId && x.Status != AssetDisposalStatus.Reversed))
            return FinResult.Fail("FA002");
        var period = await _db.FiscalPeriods.FindAsync(d.FiscalPeriodId);
        if (period == null || period.Status != PeriodStatus.Open) return FinResult.Fail("FA007");
        if ((d.Proceeds > 0m || d.DisposalExpense > 0m) && d.ReceiptBankAccountId == null) return FinResult.Fail("FA010");

        d.OriginalValue = card.OriginalValue;
        d.AccumulatedDepreciation = card.AccumulatedDepreciation;
        d.NetBookValue = card.OriginalValue - card.AccumulatedDepreciation;
        d.NetGainLoss = d.Proceeds - d.DisposalExpense - d.NetBookValue;

        var (clearing, gainLoss) = await ResolveAccountsAsync(d.DisposalType, d.NetGainLoss);
        if (clearing == null || gainLoss == null) return FinResult.Fail("FA001");
        d.ClearingAccountId = clearing.Value;
        d.GainLossAccountId = gainLoss.Value;

        d.Id = d.Id == Guid.Empty ? Guid.NewGuid() : d.Id;
        d.No = await _seq.NextAsync("FAD", d.DisposalDate);
        d.Status = AssetDisposalStatus.Draft;
        _db.AssetDisposals.Add(d);
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    // 处置结转凭证行（spec §5.2）
    private async Task<List<JournalLine>> BuildDisposalLinesAsync(AssetDisposal d, Guid assetAcc, Guid accumAcc)
    {
        var lines = new List<JournalLine>();
        int n = 1;
        void Add(Guid acc, decimal dr, decimal cr)
        {
            if (dr > 0m || cr > 0m) lines.Add(new JournalLine { LineNo = n++, AccountId = acc, Debit = dr, Credit = cr });
        }

        if (d.DisposalType == AssetDisposalType.InventoryLoss)
        {
            Add(accumAcc, d.AccumulatedDepreciation, 0m);
            Add(d.ClearingAccountId, d.NetBookValue, 0m);
            Add(assetAcc, 0m, d.OriginalValue);
            Add(d.GainLossAccountId, d.NetBookValue, 0m);
            Add(d.ClearingAccountId, 0m, d.NetBookValue);
            return lines;
        }

        Add(accumAcc, d.AccumulatedDepreciation, 0m);
        Add(d.ClearingAccountId, d.NetBookValue, 0m);
        Add(assetAcc, 0m, d.OriginalValue);
        if (d.Proceeds > 0m)
        {
            Add(d.ReceiptBankAccountId!.Value, d.Proceeds + d.TaxAmount, 0m);
            Add(d.ClearingAccountId, 0m, d.Proceeds);
            if (d.TaxAmount > 0m)
            {
                var vat = await RoleIdAsync("TAX_OUTPUT");
                if (vat == null) throw new InvalidOperationException("FA001");
                Add(vat.Value, 0m, d.TaxAmount);
            }
        }
        if (d.DisposalExpense > 0m)
        {
            Add(d.ClearingAccountId, d.DisposalExpense, 0m);
            Add(d.ReceiptBankAccountId!.Value, 0m, d.DisposalExpense);
        }
        if (d.NetGainLoss > 0m) { Add(d.ClearingAccountId, d.NetGainLoss, 0m); Add(d.GainLossAccountId, 0m, d.NetGainLoss); }
        else if (d.NetGainLoss < 0m) { Add(d.GainLossAccountId, -d.NetGainLoss, 0m); Add(d.ClearingAccountId, 0m, -d.NetGainLoss); }
        return lines;
    }

    public async Task<FinResult> ConfirmAsync(Guid id, string userId)
    {
        var d = await _db.AssetDisposals.FindAsync(id);
        if (d == null) return FinResult.Fail("FA006");
        if (d.Status != AssetDisposalStatus.Draft) return FinResult.Fail("FA009");
        var card = await _db.AssetCards.FindAsync(d.AssetCardId);
        if (card == null) return FinResult.Fail("FA006");
        var cat = await _db.AssetCategories.FindAsync(card.CategoryId);
        if (cat == null) return FinResult.Fail("FA001");
        var priorStatus = card.Status;

        var fin = await _deprec.AccrueDisposalFinalAsync(card.Id, d.FiscalPeriodId, userId);
        if (!fin.Ok) return FinResult.Fail(fin.Code ?? "FA006");
        if (!fin.Skipped) d.FinalDeprecEntryId = fin.DeprecEntryId;

        card = await _db.AssetCards.FindAsync(d.AssetCardId);
        d.AccumulatedDepreciation = card!.AccumulatedDepreciation;
        d.NetBookValue = card.OriginalValue - card.AccumulatedDepreciation;
        d.NetGainLoss = d.Proceeds - d.DisposalExpense - d.NetBookValue;
        var (clearing, gainLoss) = await ResolveAccountsAsync(d.DisposalType, d.NetGainLoss);
        if (clearing == null || gainLoss == null) return FinResult.Fail("FA001");
        d.ClearingAccountId = clearing.Value;
        d.GainLossAccountId = gainLoss.Value;

        var lines = await BuildDisposalLinesAsync(d, cat.AssetAccountId, cat.AccumDeprecAccountId);
        var je = new JournalEntry
        {
            Id = Guid.NewGuid(), VoucherDate = d.DisposalDate, Source = VoucherSource.AssetDisposal,
            SourceDocNo = d.No, Description = $"资产处置 {d.No}（{d.DisposalType}）", Lines = lines,
        };
        var post = await _journal.AutoPostAsync(je);
        if (!post.Ok) return post;

        d.JournalEntryId = je.Id;
        d.Status = AssetDisposalStatus.Confirmed;
        d.PriorStatus = priorStatus;
        d.ConfirmedAt = DateTime.Now;
        d.ConfirmedBy = userId;
        card.Status = AssetStatus.Disposed;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public Task<FinResult> ReverseAsync(Guid id, string userId, string reason) => throw new NotImplementedException();

    public Task<AssetDisposal?> GetAsync(Guid id) => _db.AssetDisposals.FirstOrDefaultAsync(x => x.Id == id);

    public Task<List<AssetDisposal>> ListAsync(AssetDisposalStatus? status, Guid? assetCardId)
        => _db.AssetDisposals
            .Where(x => (status == null || x.Status == status) && (assetCardId == null || x.AssetCardId == assetCardId))
            .OrderByDescending(x => x.DisposalDate).ToListAsync();
}
