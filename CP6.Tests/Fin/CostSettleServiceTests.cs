using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// 财务章06 A-3：完工结转。①料工费→WIP（借WIP/贷原材料+直接人工+制造费用 applied吸收）②WIP→FG（借FG/贷WIP）。
/// 全按 Role 取科目。场景同归集：料650+工300+费200=1150，完工10 → FG单位成本115。
/// </summary>
public class CostSettleServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private sealed class Kit
    {
        public required CP6Context Db;
        public required GlAccountService Gl;
        public required CostCollectService Collect;
        public required CostSettleService Settle;
    }

    private static async Task<Kit> SetupAsync(CP6Context db)
    {
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");
        var journal = new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db));
        return new Kit
        {
            Db = db, Gl = gl,
            Collect = new CostCollectService(db, new FinSequenceService(db), new CP6.Core.Services.Mes.ProcessCostRateService(db)),
            Settle = new CostSettleService(db, journal),
        };
    }

    private static async Task SeedWoAsync(CP6Context db)
    {
        db.Set<WorkOrder>().Add(new WorkOrder { Id = Guid.NewGuid(), WorkOrderNo = "WO1", ProductCd = "P1", ProductionQty = 10m, CompletedQty = 10m, Status = WorkOrderStatus.Completed });
        db.Set<ProductMaterial>().AddRange(
            new ProductMaterial { Id = Guid.NewGuid(), ProductCd = "P1", ProcessCd = "OP1", MaterialCd = "M1", SupplyPrice = 5m },
            new ProductMaterial { Id = Guid.NewGuid(), ProductCd = "P1", ProcessCd = "OP1", MaterialCd = "M2", SupplyPrice = 2m });
        db.Set<WorkOrderMaterial>().AddRange(
            new WorkOrderMaterial { Id = Guid.NewGuid(), WorkOrderNo = "WO1", ProcessCd = "OP1", MaterialCd = "M1", PlanQty = 100m, ActualQty = 110m },
            new WorkOrderMaterial { Id = Guid.NewGuid(), WorkOrderNo = "WO1", ProcessCd = "OP1", MaterialCd = "M2", PlanQty = 50m, ActualQty = 50m });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Settle_PostsWipCollectAndFgVouchers_MarksSettled()
    {
        using var db = NewDb();
        var k = await SetupAsync(db);
        await SeedWoAsync(db);
        await k.Collect.CollectAsync("WO1", 300m, 200m, "u");

        var r = await k.Settle.SettleAsync("WO1", "u");
        Assert.True(r.Ok, r.Code);

        var wip = (await k.Gl.GetByRoleAsync("WIP"))!.Id;
        var fg = (await k.Gl.GetByRoleAsync("FG"))!.Id;
        var inv = (await k.Gl.GetByRoleAsync("INVENTORY"))!.Id;
        var lab = (await k.Gl.GetByRoleAsync("DIRECT_LABOR"))!.Id;
        var oh = (await k.Gl.GetByRoleAsync("MFG_OVERHEAD"))!.Id;

        var collect = await db.JournalEntries.Include(e => e.Lines).FirstAsync(e => e.SourceDocNo!.EndsWith("#WIP"));
        Assert.Equal(VoucherSource.Cost, collect.Source);
        Assert.Equal(1150m, collect.Lines.Single(l => l.AccountId == wip).Debit);    // 借 WIP 总成本
        Assert.Equal(650m, collect.Lines.Single(l => l.AccountId == inv).Credit);    // 贷 原材料 实际料
        Assert.Equal(300m, collect.Lines.Single(l => l.AccountId == lab).Credit);    // 贷 直接人工 吸收
        Assert.Equal(200m, collect.Lines.Single(l => l.AccountId == oh).Credit);     // 贷 制造费用 吸收

        var settle = await db.JournalEntries.Include(e => e.Lines).FirstAsync(e => e.SourceDocNo!.EndsWith("#FG"));
        Assert.Equal(1150m, settle.Lines.Single(l => l.AccountId == fg).Debit);      // 借 库存商品
        Assert.Equal(1150m, settle.Lines.Single(l => l.AccountId == wip).Credit);    // 贷 在制品

        var cs = await db.CostSheets.FirstAsync(s => s.WorkOrderNo == "WO1");
        Assert.Equal(CostSheetStatus.Settled, cs.Status);
        Assert.NotNull(cs.JournalEntryId);
    }

    [Fact]
    public async Task Settle_WipNetsToZero_AfterCollectAndSettle()
    {
        using var db = NewDb();
        var k = await SetupAsync(db);
        await SeedWoAsync(db);
        await k.Collect.CollectAsync("WO1", 300m, 200m, "u");
        await k.Settle.SettleAsync("WO1", "u");

        var wip = (await k.Gl.GetByRoleAsync("WIP"))!.Id;
        var wipLines = await db.JournalLines.Where(l => l.AccountId == wip).ToListAsync();
        Assert.Equal(wipLines.Sum(l => l.Debit), wipLines.Sum(l => l.Credit));       // WIP 借1150=贷1150 净零
    }

    [Fact]
    public async Task FgUnitCost_ReturnsCostSheetValue()
    {
        using var db = NewDb();
        var k = await SetupAsync(db);
        await SeedWoAsync(db);
        await k.Collect.CollectAsync("WO1", 300m, 200m, "u");
        Assert.Equal(115m, await k.Settle.FgUnitCostAsync("WO1"));     // 1150 / 10
    }

    [Fact]
    public async Task Settle_NotCollected_Fails()
    {
        using var db = NewDb();
        var k = await SetupAsync(db);
        var r = await k.Settle.SettleAsync("NOPE", "u");
        Assert.False(r.Ok);
        Assert.Equal("E-FIN-403", r.Code);
    }

    [Fact]
    public async Task Settle_AlreadySettled_Fails()
    {
        using var db = NewDb();
        var k = await SetupAsync(db);
        await SeedWoAsync(db);
        await k.Collect.CollectAsync("WO1", 300m, 200m, "u");
        await k.Settle.SettleAsync("WO1", "u");

        var r = await k.Settle.SettleAsync("WO1", "u");
        Assert.False(r.Ok);
        Assert.Equal("E-FIN-402", r.Code);
    }
}
