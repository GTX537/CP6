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

    public Task<FinResult> ConfirmAsync(Guid id, string userId) => throw new NotImplementedException();
    public Task<FinResult> ReverseAsync(Guid id, string userId, string reason) => throw new NotImplementedException();

    public Task<AssetDisposal?> GetAsync(Guid id) => _db.AssetDisposals.FirstOrDefaultAsync(x => x.Id == id);

    public Task<List<AssetDisposal>> ListAsync(AssetDisposalStatus? status, Guid? assetCardId)
        => _db.AssetDisposals
            .Where(x => (status == null || x.Status == status) && (assetCardId == null || x.AssetCardId == assetCardId))
            .OrderByDescending(x => x.DisposalDate).ToListAsync();
}
