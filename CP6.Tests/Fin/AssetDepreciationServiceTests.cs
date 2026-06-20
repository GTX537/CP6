using Microsoft.EntityFrameworkCore;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;

namespace CP6.Tests.Fin;

public class AssetDepreciationServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static async Task<(CP6Context db, AssetDepreciationService svc, Guid june, Guid cardId)> SetupAsync(
        DepreciationMethod method = DepreciationMethod.StraightLine, int life = 12,
        DateTime? acq = null, string startPeriod = "2026-05")
    {
        var db = NewDb();
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");
        var periods = new FiscalPeriodService(db, 1);
        var june = (await periods.EnsureOpenAsync(new DateTime(2026, 6, 1), "seed")).Id;

        var expAcc = (await db.GlAccounts.FirstAsync(a => a.Code == "5101.01")).Id;
        var assetAcc = (await db.GlAccounts.FirstAsync(a => a.Code == "1601")).Id;
        var accumAcc = (await db.GlAccounts.FirstAsync(a => a.Code == "1602")).Id;
        var cat = new AssetCategory
        {
            Id = Guid.NewGuid(), Code = "MC", Name = "机器设备",
            DefaultMethod = method, DefaultUsefulLifeMonths = life, DefaultSalvageRate = 0m,
            AssetAccountId = assetAcc, AccumDeprecAccountId = accumAcc, DeprecExpenseAccountId = expAcc,
            IsActive = true,
        };
        db.AssetCategories.Add(cat);
        var card = new AssetCard
        {
            Id = Guid.NewGuid(), AssetNo = "FA-1", Name = "冲床", CategoryId = cat.Id,
            OriginalValue = 12000m, SalvageRate = 0m, SalvageValue = 0m,
            Method = method, UsefulLifeMonths = life,
            AcquisitionDate = acq ?? new DateTime(2026, 4, 15),
            DepreciationStartPeriod = startPeriod,
            AccumulatedDepreciation = 0m, DepreciatedPeriods = 0, Status = AssetStatus.InUse,
        };
        db.AssetCards.Add(card);
        await db.SaveChangesAsync();

        var seq = new FinSequenceService(db);
        var svc = new AssetDepreciationService(db, new DepreciationCalculator(),
            new JournalEntryService(db, periods, seq), periods, seq);
        return (db, svc, june, card.Id);
    }

    [Fact] // §13.6 次月起折：起折期≤本期则计提
    public async Task RunAsync_EligibleInUse_CreatesDraftRunAndEntry()
    {
        var (db, svc, june, cardId) = await SetupAsync(startPeriod: "2026-05");
        var r = await svc.RunAsync(june, "admin", DepreciationRunMode.Manual);
        Assert.True(r.Ok, r.Code);
        var run = await db.DepreciationRuns.SingleAsync();
        Assert.Equal(DepreciationRunStatus.Draft, run.Status);
        Assert.Equal(DepreciationRunMode.Manual, run.RunMode);
        Assert.Equal(1, run.AssetCount);
        Assert.Equal(1000m, run.TotalAmount);
        var entry = await db.DepreciationEntries.SingleAsync();
        Assert.Equal(cardId, entry.AssetCardId);
        Assert.Equal(1000m, entry.DepreciationAmount);
    }

    [Fact] // §13.6 当期增加不提：起折期=本期次月（晚于本期）→ 不纳入
    public async Task RunAsync_AcquiredThisMonth_NotDepreciated()
    {
        var (db, svc, june, _) = await SetupAsync(startPeriod: "2026-07");
        var r = await svc.RunAsync(june, "admin", DepreciationRunMode.Manual);
        Assert.True(r.Ok, r.Code);
        Assert.Equal(0, (await db.DepreciationRuns.SingleAsync()).AssetCount);
    }

    [Fact] // §13.8 RunAsync 幂等：已有非 Reversed 批量批次 → FA003
    public async Task RunAsync_SecondBatch_RejectedFA003()
    {
        var (_, svc, june, _) = await SetupAsync();
        Assert.True((await svc.RunAsync(june, "admin", DepreciationRunMode.Manual)).Ok);
        var r2 = await svc.RunAsync(june, "admin", DepreciationRunMode.Manual);
        Assert.False(r2.Ok);
        Assert.Equal("FA003", r2.Code);
    }

    [Fact] // §13.13 成本中心派生序：卡片 MachineId → CostCenter.LinkMachineId 命中
    public async Task RunAsync_CostCenter_DerivedFromMachine()
    {
        var (db, svc, june, cardId) = await SetupAsync();
        var mid = Guid.NewGuid();
        db.CostCenters.Add(new CostCenter
        {
            Id = Guid.NewGuid(), Code = "CC-M1", Name = "冲床中心",
            Type = CostCenterType.Machine, LinkMachineId = mid.ToString(), IsActive = true,
        });
        var card = await db.AssetCards.FindAsync(cardId);
        card!.MachineId = mid; card.CostCenterId = null;
        await db.SaveChangesAsync();

        await svc.RunAsync(june, "admin", DepreciationRunMode.Manual);
        var entry = await db.DepreciationEntries.SingleAsync();
        var cc = await db.CostCenters.SingleAsync();
        Assert.Equal(cc.Id, entry.CostCenterId);
    }
}
