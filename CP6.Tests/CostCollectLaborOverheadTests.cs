using Microsoft.EntityFrameworkCore;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Fin;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Fin;

namespace CP6.Tests;

/// <summary>
/// A2 §4.4 D-2：CostCollect 工/费做真（双模式）。逐工序 工时×费率：人工=ActualLaborHour×LaborRate（缺回退标准），
/// 制费=ActualMachineHour×OverheadRate；标准列=标准工时×费率。缺实绩→标准回退(W-A2-COST-001)；
/// 缺费率/工序→严格失败(E-A2-RATE-002/E-A2-COST-001)或迁移整单回退传入估算(W-A2-COST-002)。
/// </summary>
public class CostCollectLaborOverheadTests
{
    private static CostCollectService Create(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        return new CostCollectService(db, new FakeSeq(), new ProcessCostRateService(db));
    }

    private sealed class FakeSeq : IFinSequenceService
    {
        public Task<string> NextAsync(string key, DateTime? date = null) => Task.FromResult($"{key}-1");
    }

    private static async Task Seed(CP6.Core.EFDbContext.CP6Context db)
    {
        db.WorkCenters.Add(new WorkCenter { WgCd = "PRINT", Enable = true });
        db.ProcessCostRates.Add(new ProcessCostRate { WgCd = "PRINT", LaborRate = 80, OverheadRate = 120, ValidFrom = new(2026, 1, 1) });
        db.Set<WorkOrder>().Add(new WorkOrder { WorkOrderNo = "WO1", ProductCd = "FG", CompletedQty = 1000, PlanStartDate = new(2026, 7, 1) });
        db.Set<ProductProcess>().Add(new ProductProcess { ProductCd = "FG", TaskCd = "T1", ProcessCd = "P1", WgCd = "PRINT", SetupHour = 0.5m, CycleTime = 0.002m, StandardCrewSize = 2 });
        db.Set<WorkOrderProcess>().Add(new WorkOrderProcess { WorkOrderNo = "WO1", ProcessCd = "P1", TaskCd = "T1", WgCd = "PRINT", ActualMachineHour = 2, ActualLaborHour = 4 });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task LaborOverhead_FromHoursTimesRate_WithStandard()
    {
        var svc = Create(out var db);
        await Seed(db);

        var r = await svc.CollectAsync("WO1", 0, 0, "admin");
        Assert.True(r.Ok);

        var sheet = await db.CostSheets.Include(s => s.Lines).FirstAsync(s => s.WorkOrderNo == "WO1");
        // 标准机时 = 0.5 + 1000×0.002 = 2.5；标准人工工时 = 2.5×2 = 5
        // LaborActual = 4×80 = 320；LaborStandard = 5×80 = 400
        // OverheadActual = 2×120 = 240；OverheadStandard = 2.5×120 = 300
        Assert.Equal(320m, sheet.LaborActual);
        Assert.Equal(400m, sheet.LaborStandard);
        Assert.Equal(240m, sheet.OverheadActual);
        Assert.Equal(300m, sheet.OverheadStandard);
        Assert.Contains(sheet.Lines, l => l.Element == CostElement.Labor && l.WgCd == "PRINT" && l.RateValidFrom == new DateTime(2026, 1, 1));
    }

    [Fact]
    public async Task MissingActualHour_UsesStandardFallback_Warning()
    {
        var svc = Create(out var db);
        await Seed(db);
        var wop = await db.Set<WorkOrderProcess>().FirstAsync();
        wop.ActualMachineHour = null; wop.ActualLaborHour = null;   // 无实绩工时
        await db.SaveChangesAsync();

        await svc.CollectAsync("WO1", 0, 0, "admin");

        var sheet = await db.CostSheets.Include(s => s.Lines).FirstAsync(s => s.WorkOrderNo == "WO1");
        Assert.Equal(400m, sheet.LaborActual);     // 回退用标准工时 5×80
        Assert.Contains(sheet.Lines, l => l.WarningCode == "W-A2-COST-001");
    }

    [Fact]
    public async Task MissingRate_StrictMode_Fails()
    {
        var svc = Create(out var db);
        await Seed(db);
        var rate = await db.ProcessCostRates.FirstAsync();
        rate.IsDeleted = true;   // 删费率
        await db.SaveChangesAsync();
        svc.StrictCostRate = true;

        var r = await svc.CollectAsync("WO1", 0, 0, "admin");
        Assert.False(r.Ok);
        Assert.Equal("E-A2-RATE-002", r.Code);
    }

    [Fact]
    public async Task MissingRate_MigrationMode_LegacyFallback()
    {
        var svc = Create(out var db);
        await Seed(db);
        var rate = await db.ProcessCostRates.FirstAsync();
        rate.IsDeleted = true;
        await db.SaveChangesAsync();
        svc.StrictCostRate = false;

        await svc.CollectAsync("WO1", 111, 222, "admin");

        var sheet = await db.CostSheets.Include(s => s.Lines).FirstAsync(s => s.WorkOrderNo == "WO1");
        Assert.Equal(111m, sheet.LaborActual);
        Assert.Equal(111m, sheet.LaborStandard);
        Assert.Equal(222m, sheet.OverheadActual);
        Assert.Contains(sheet.Lines, l => l.WarningCode == "W-A2-COST-002");
    }
}
