using Microsoft.EntityFrameworkCore;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;

namespace CP6.Tests.Fin;

public class AssetCloseHookTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static async Task<(CP6Context db, PeriodCloseService close, AssetDepreciationService dep, Guid june, Guid cardId)>
        SetupAsync(DepreciationMethod method = DepreciationMethod.StraightLine)
    {
        var db = NewDb();
        await new GlAccountService(db).ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");
        var periods = new FiscalPeriodService(db, 1);
        var june = (await periods.EnsureOpenAsync(new DateTime(2026, 6, 1), "seed")).Id;
        // 上一期 5 月置 Closed，避开 E-FIN-145
        var may = await periods.EnsureOpenAsync(new DateTime(2026, 5, 1), "seed");
        may.Status = PeriodStatus.Closed;
        await db.SaveChangesAsync();

        var seq = new FinSequenceService(db);
        var jes = new JournalEntryService(db, periods, seq);
        var dep = new AssetDepreciationService(db, new DepreciationCalculator(), jes, periods, seq);

        var expAcc = (await db.GlAccounts.FirstAsync(a => a.Code == "5101.01")).Id;
        var cat = new AssetCategory
        {
            Id = Guid.NewGuid(), Code = "MC", Name = "机器设备", DefaultMethod = method,
            DefaultUsefulLifeMonths = 12,
            AssetAccountId = (await db.GlAccounts.FirstAsync(a => a.Code == "1601")).Id,
            AccumDeprecAccountId = (await db.GlAccounts.FirstAsync(a => a.Code == "1602")).Id,
            DeprecExpenseAccountId = expAcc, IsActive = true,
        };
        db.AssetCategories.Add(cat);
        var card = new AssetCard
        {
            Id = Guid.NewGuid(), AssetNo = "FA-1", Name = "冲床", CategoryId = cat.Id,
            OriginalValue = 12000m, SalvageValue = 0m, Method = method, UsefulLifeMonths = 12,
            TotalWorkload = method == DepreciationMethod.UnitsOfProduction ? 10000m : null,
            AcquisitionDate = new DateTime(2026, 4, 15),
            DepreciationStartPeriod = "2026-05", Status = AssetStatus.InUse,
        };
        db.AssetCards.Add(card);
        await db.SaveChangesAsync();

        var trial = new TrialBalanceService(db);
        var close = new PeriodCloseService(db, periods, trial, reval: null, deprec: dep);
        return (db, close, dep, june, card.Id);
    }

    [Fact] // §6.1 结账钩子：直线法资产本期未计提 → CloseAsync 自动 Accrue（Run+Post）
    public async Task CloseAsync_AutoAccruesDepreciation()
    {
        var (db, close, _, june, cardId) = await SetupAsync();
        var r = await close.CloseAsync(june, "admin");
        Assert.True(r.Ok, r.Code);
        Assert.Equal(DepreciationRunStatus.Posted, (await db.DepreciationRuns.SingleAsync()).Status);
        Assert.Equal(1000m, (await db.AssetCards.FindAsync(cardId))!.AccumulatedDepreciation);
    }

    [Fact] // §6.1 硬校验：工作量法在用资产本期未录量 → PreCloseCheck 硬阻断结账（FA008）
    public async Task CloseAsync_UoPMissingWorkload_HardBlocked()
    {
        var (db, close, _, june, _) = await SetupAsync(method: DepreciationMethod.UnitsOfProduction);
        var r = await close.CloseAsync(june, "admin");
        Assert.False(r.Ok);
        Assert.Equal("FA008", r.Code);
        Assert.Empty(await db.DepreciationRuns.ToListAsync());
    }
}
