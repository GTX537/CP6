using Microsoft.EntityFrameworkCore;
using CP6.Entity.DomainModels.Mes;
using CP6.Core.Services.Mes;

namespace CP6.Tests;

public class ProductionResultHourTests
{
    private static ProductionResultService Create(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        return new ProductionResultService(db);
    }

    private static ProductionResult R(string wo, string proc, string op, string? mc,
        DateTime s, DateTime e, int type = 4)
        => new() { ResultNo = Guid.NewGuid().ToString("N")[..8], WorkOrderNo = wo, ProcessCd = proc,
                   OperatorCd = op, MachineCd = mc, ActualStartTime = s, ActualEndTime = e, ResultType = type };

    private static async Task SeedWop(CP6.Core.EFDbContext.CP6Context db, string wo, string proc)
    {
        db.Set<WorkOrderProcess>().Add(new WorkOrderProcess { WorkOrderNo = wo, ProcessCd = proc, TaskCd = "T1" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task MachineHour_MergesIntervals_LaborHour_AccumulatesByOperator()
    {
        var svc = Create(out var db);
        await SeedWop(db, "WO1", "P1");
        var d = new DateTime(2026, 7, 1, 8, 0, 0);
        // 张三、李四 同机 M1 同区间 08:00-10:00
        db.Set<ProductionResult>().AddRange(
            R("WO1", "P1", "z3", "M1", d, d.AddHours(2)),
            R("WO1", "P1", "l4", "M1", d, d.AddHours(2)));
        await db.SaveChangesAsync();

        await svc.RecalculateProcessHoursAsync("WO1", "P1", "T1", "admin");

        var wop = await db.Set<WorkOrderProcess>().FirstAsync(x => x.WorkOrderNo == "WO1" && x.ProcessCd == "P1");
        Assert.Equal(2m, wop.ActualMachineHour);   // 机器区间合并：2h，非 4h
        Assert.Equal(4m, wop.ActualLaborHour);     // 人工按作业者累加：4h
        Assert.Equal("Derived", wop.ActualHourSource);
    }

    [Fact]
    public async Task ExplicitHours_OverrideTimestamps()
    {
        var svc = Create(out var db);
        await SeedWop(db, "WO2", "P1");
        var d = new DateTime(2026, 7, 1, 8, 0, 0);
        var r = R("WO2", "P1", "z3", "M1", d, d.AddHours(2));
        r.LaborHour = 1.5m; r.MachineHour = 1.0m;   // 显式工时优先
        db.Set<ProductionResult>().Add(r);
        await db.SaveChangesAsync();

        await svc.RecalculateProcessHoursAsync("WO2", "P1", "T1", "admin");

        var wop = await db.Set<WorkOrderProcess>().FirstAsync(x => x.WorkOrderNo == "WO2");
        Assert.Equal(1.0m, wop.ActualMachineHour);
        Assert.Equal(1.5m, wop.ActualLaborHour);
    }

    [Fact]
    public async Task Overridden_NotRecalculated()
    {
        var svc = Create(out var db);
        db.Set<WorkOrderProcess>().Add(new WorkOrderProcess
        { WorkOrderNo = "WO3", ProcessCd = "P1", TaskCd = "T1",
          ActualMachineHour = 9, ActualLaborHour = 9, IsHourOverridden = true, ActualHourSource = "Manual" });
        var d = new DateTime(2026, 7, 1, 8, 0, 0);
        db.Set<ProductionResult>().Add(R("WO3", "P1", "z3", "M1", d, d.AddHours(2)));
        await db.SaveChangesAsync();

        await svc.RecalculateProcessHoursAsync("WO3", "P1", "T1", "admin");

        var wop = await db.Set<WorkOrderProcess>().FirstAsync(x => x.WorkOrderNo == "WO3");
        Assert.Equal(9m, wop.ActualMachineHour);   // 覆盖态不动
        Assert.Equal("Manual", wop.ActualHourSource);
    }

    [Fact]
    public async Task Interrupt_ClosedPair_Deducted()
    {
        var svc = Create(out var db);
        await SeedWop(db, "WO4", "P1");
        var d = new DateTime(2026, 7, 1, 8, 0, 0);
        // 运行 08:00-12:00（4h），中断 09:00-10:00（1h）→ 净 3h
        db.Set<ProductionResult>().AddRange(
            R("WO4", "P1", "z3", "M1", d, d.AddHours(4)),
            R("WO4", "P1", "z3", "M1", d.AddHours(1), d.AddHours(2), type: 2)); // 2=中断
        await db.SaveChangesAsync();

        await svc.RecalculateProcessHoursAsync("WO4", "P1", "T1", "admin");

        var wop = await db.Set<WorkOrderProcess>().FirstAsync(x => x.WorkOrderNo == "WO4");
        Assert.Equal(3m, wop.ActualLaborHour);
    }
}
