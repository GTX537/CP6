using Microsoft.EntityFrameworkCore;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;

namespace CP6.Tests.Fin;

public class AssetDisposalServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static async Task<(CP6Context db, AssetDisposalService disp, AssetDepreciationService dep, Guid june, AssetCard card)>
        SetupAsync(AssetStatus status = AssetStatus.InUse, decimal accum = 8000m)
    {
        var db = NewDb();
        await new GlAccountService(db).ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");
        var periods = new FiscalPeriodService(db, 1);
        var june = (await periods.EnsureOpenAsync(new DateTime(2026, 6, 1), "seed")).Id;
        var seq = new FinSequenceService(db);
        var jes = new JournalEntryService(db, periods, seq);

        var expAcc = (await db.GlAccounts.FirstAsync(a => a.Code == "5101.01")).Id;
        var cat = new AssetCategory
        {
            Id = Guid.NewGuid(), Code = "MC", Name = "机器设备",
            DefaultMethod = DepreciationMethod.StraightLine, DefaultUsefulLifeMonths = 12, DefaultSalvageRate = 0m,
            AssetAccountId = (await db.GlAccounts.FirstAsync(a => a.Code == "1601")).Id,
            AccumDeprecAccountId = (await db.GlAccounts.FirstAsync(a => a.Code == "1602")).Id,
            DeprecExpenseAccountId = expAcc, IsActive = true,
        };
        db.AssetCategories.Add(cat);
        var card = new AssetCard
        {
            Id = Guid.NewGuid(), AssetNo = "FA-9", Name = "旧冲床", CategoryId = cat.Id,
            OriginalValue = 12000m, SalvageRate = 0m, SalvageValue = 0m, Method = DepreciationMethod.StraightLine,
            UsefulLifeMonths = 12, AcquisitionDate = new DateTime(2025, 6, 1), DepreciationStartPeriod = "2025-07",
            AccumulatedDepreciation = accum, DepreciatedPeriods = 8, Status = status,
        };
        db.AssetCards.Add(card);
        await db.SaveChangesAsync();

        var dep = new AssetDepreciationService(db, new DepreciationCalculator(), jes, periods, seq);
        var disp = new AssetDisposalService(db, jes, periods, seq, dep);
        return (db, disp, dep, june, card);
    }

    [Fact] // §4.2 出售有价款但无收款账户 → FA010
    public async Task CreateAsync_SaleWithProceeds_NoBank_FA010()
    {
        var (db, disp, _, june, card) = await SetupAsync();
        var d = new AssetDisposal
        {
            AssetCardId = card.Id, DisposalType = AssetDisposalType.Sale,
            DisposalDate = new DateTime(2026, 6, 10), FiscalPeriodId = june, Proceeds = 5000m, ReceiptBankAccountId = null,
        };
        var r = await disp.CreateAsync(d, "admin");
        Assert.False(r.Ok);
        Assert.Equal("FA010", r.Code);
    }

    [Fact] // §4.1 科目解析：盘亏 → 清理 1901 / 损益 6711
    public async Task CreateAsync_InventoryLoss_ResolvesClearing1901_Loss6711()
    {
        var (db, disp, _, june, card) = await SetupAsync();
        var d = new AssetDisposal
        {
            AssetCardId = card.Id, DisposalType = AssetDisposalType.InventoryLoss,
            DisposalDate = new DateTime(2026, 6, 10), FiscalPeriodId = june,
        };
        var r = await disp.CreateAsync(d, "admin");
        Assert.True(r.Ok, r.Code);
        var saved = await db.AssetDisposals.SingleAsync();
        var clearing = await db.GlAccounts.FindAsync(saved.ClearingAccountId);
        var loss = await db.GlAccounts.FindAsync(saved.GainLossAccountId);
        Assert.Equal("1901", clearing!.Code);
        Assert.Equal("6711", loss!.Code);
        Assert.Equal(4000m, saved.NetBookValue);
    }

    [Fact] // §13.15 完全折旧资产可处置（CreateAsync 不拒）
    public async Task CreateAsync_FullyDepreciated_Allowed()
    {
        var (db, disp, _, june, card) = await SetupAsync(status: AssetStatus.FullyDepreciated, accum: 12000m);
        var d = new AssetDisposal
        {
            AssetCardId = card.Id, DisposalType = AssetDisposalType.Scrap,
            DisposalDate = new DateTime(2026, 6, 10), FiscalPeriodId = june,
        };
        var r = await disp.CreateAsync(d, "admin");
        Assert.True(r.Ok, r.Code);
    }
}
