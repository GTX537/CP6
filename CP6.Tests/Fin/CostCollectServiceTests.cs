using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// 财务章06 A：成本归集（★卖点）。料 = MES 实际消耗(WorkOrderMaterial.ActualQty) × BOM 供给单价(ProductMaterial.SupplyPrice)；
/// 标准料 = 计划用量×同单价 → 差额即料用量差异；工费标准估算；FG 单位成本 = 实际总成本/完工数。
/// 场景：M1 计划100实际110单价5；M2 计划50实际50单价2；工300费200；完工10。
/// </summary>
public class CostCollectServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static CostCollectService Svc(CP6Context db) => new(db, new FinSequenceService(db));

    private static async Task SeedAsync(CP6Context db, decimal m1Actual = 110m)
    {
        db.Set<WorkOrder>().Add(new WorkOrder
        {
            Id = Guid.NewGuid(), WorkOrderNo = "WO1", ProductCd = "P1",
            ProductionQty = 10m, CompletedQty = 10m, Status = WorkOrderStatus.Completed,
        });
        db.Set<ProductMaterial>().AddRange(
            new ProductMaterial { Id = Guid.NewGuid(), ProductCd = "P1", ProcessCd = "OP1", MaterialCd = "M1", SupplyPrice = 5m },
            new ProductMaterial { Id = Guid.NewGuid(), ProductCd = "P1", ProcessCd = "OP1", MaterialCd = "M2", SupplyPrice = 2m });
        db.Set<WorkOrderMaterial>().AddRange(
            new WorkOrderMaterial { Id = Guid.NewGuid(), WorkOrderNo = "WO1", ProcessCd = "OP1", MaterialCd = "M1", MaterialName = "原纸A", PlanQty = 100m, ActualQty = m1Actual },
            new WorkOrderMaterial { Id = Guid.NewGuid(), WorkOrderNo = "WO1", ProcessCd = "OP1", MaterialCd = "M2", MaterialName = "油墨B", PlanQty = 50m, ActualQty = 50m });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Collect_Material_ActualAndStandardFromConsumptionTimesBomPrice()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var r = await Svc(db).CollectAsync("WO1", laborStd: 300m, overheadStd: 200m, "u");
        Assert.True(r.Ok, r.Code);

        var cs = (await Svc(db).GetByWorkOrderAsync("WO1"))!;
        Assert.Equal(650m, cs.MaterialActual);     // 110×5 + 50×2
        Assert.Equal(600m, cs.MaterialStandard);   // 100×5 + 50×2
        Assert.Equal(300m, cs.LaborStd);
        Assert.Equal(200m, cs.OverheadStd);
        Assert.Equal(CostSheetStatus.Collected, cs.Status);
    }

    [Fact]
    public async Task Collect_TotalActual_Variance_FgUnitCost()
    {
        using var db = NewDb();
        await SeedAsync(db);
        await Svc(db).CollectAsync("WO1", 300m, 200m, "u");

        var cs = (await Svc(db).GetByWorkOrderAsync("WO1"))!;
        Assert.Equal(1150m, cs.TotalActual);       // 650 + 300 + 200
        Assert.Equal(1100m, cs.StandardCost);      // 600 + 300 + 200
        Assert.Equal(50m, cs.Variance);            // 料用量差异：M1 多耗 10×5
        Assert.Equal(115m, cs.FgUnitCost);         // 1150 / 10
    }

    [Fact]
    public async Task Collect_Lines_RecordMaterialLaborOverhead()
    {
        using var db = NewDb();
        await SeedAsync(db);
        await Svc(db).CollectAsync("WO1", 300m, 200m, "u");

        var cs = (await Svc(db).GetByWorkOrderAsync("WO1"))!;
        Assert.Equal(2, cs.Lines.Count(l => l.Element == CostElement.Material));
        Assert.Single(cs.Lines, l => l.Element == CostElement.Labor);
        Assert.Single(cs.Lines, l => l.Element == CostElement.Overhead);
        var m1 = cs.Lines.Single(l => l.MaterialCd == "M1");
        Assert.Equal(550m, m1.ActualAmount);       // 110×5
        Assert.Equal(500m, m1.StandardAmount);     // 100×5
    }

    [Fact]
    public async Task Collect_WorkOrderNotFound_Fails()
    {
        using var db = NewDb();
        var r = await Svc(db).CollectAsync("NOPE", 0m, 0m, "u");
        Assert.False(r.Ok);
        Assert.Equal("E-FIN-401", r.Code);
    }

    [Fact]
    public async Task Collect_MissingBomPrice_TreatedAsZero()
    {
        using var db = NewDb();
        db.Set<WorkOrder>().Add(new WorkOrder { Id = Guid.NewGuid(), WorkOrderNo = "WO2", ProductCd = "PX", CompletedQty = 5m });
        db.Set<WorkOrderMaterial>().Add(new WorkOrderMaterial { Id = Guid.NewGuid(), WorkOrderNo = "WO2", ProcessCd = "OP1", MaterialCd = "MZ", PlanQty = 10m, ActualQty = 10m });
        await db.SaveChangesAsync();

        var r = await Svc(db).CollectAsync("WO2", 0m, 0m, "u");
        Assert.True(r.Ok, r.Code);
        var cs = (await Svc(db).GetByWorkOrderAsync("WO2"))!;
        Assert.Equal(0m, cs.MaterialActual);       // 无 BOM 单价 → 0，不崩
    }

    [Fact]
    public async Task Collect_Idempotent_Recollect_OverwritesNotDuplicate()
    {
        using var db = NewDb();
        await SeedAsync(db);
        await Svc(db).CollectAsync("WO1", 300m, 200m, "u");
        // 第二次归集（如实际用量更新）：覆盖，不重复建单/堆明细
        var cs0 = (await Svc(db).GetByWorkOrderAsync("WO1"))!;
        var no0 = cs0.No;
        await Svc(db).CollectAsync("WO1", 300m, 200m, "u");

        Assert.Equal(1, await db.CostSheets.CountAsync(s => s.WorkOrderNo == "WO1"));
        var cs = (await Svc(db).GetByWorkOrderAsync("WO1"))!;
        Assert.Equal(no0, cs.No);                  // 同单号
        Assert.Equal(4, cs.Lines.Count);           // 2 料 + 工 + 费，未翻倍
        Assert.Equal(650m, cs.MaterialActual);
    }
}
