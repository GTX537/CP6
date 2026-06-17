using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

/// <summary>
/// 見積計算 回归测试（MRP P1 · spec §4 铁律）。
///
/// 用途：A-2 把 CalculateAsync 内联的"面积×段成率"用量子计算改为调共享内核
/// IMaterialUsageCalculator 之前，先用真实输入锁定当前输出；改造后逐项必须一致——
/// 用量算法被提取但見積金额严格不变。
/// </summary>
public class EstimateCalcRegressionTests
{
    private static EstimateCalcService CreateService(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        return new EstimateCalcService(db);
    }

    private static EstimateCalcDto BaseDto() => new()
    {
        QtnBaseCd = "01",
        OrderBaseCd = "01",
        StaffCd = "S001",
        CustomerCd = "C0001",
        CustomerProductName1 = "回归商品",
        OrderQty = 10000,
        ParentChildDiv = "01",
        OrderType = "01",
        ProductCategoryBig = "A",
        ProductCategoryMid = "A01",
        SheetDimW = 500,
        SheetDimF = 400,
        EstimateQtys = new decimal?[] { 1000, 5000, 10000, 0, 0, 0, 0, 0 },
        PalletCnts = new decimal?[8],
        StrategicDivs = new bool[10],
    };

    [Fact]
    public async Task EstimateCalc_DimensionalUsage_WithMasterData_Unchanged()
    {
        var svc = CreateService(out var db);
        db.MasterGenericCodes.Add(new MasterGenericCode { GroupCode = "Paper", Code = "P001", Name = "K220", Num1 = 32.5m });
        db.MasterGenericCodes.Add(new MasterGenericCode { GroupCode = "M067", Code = "E", Name = "E 段成率", Num1 = 1.35m });
        await db.SaveChangesAsync();

        var dto = BaseDto();
        dto.PaperCdF = "P001";
        dto.SheetFlute = "E";

        var r = await svc.CalculateAsync(dto);

        // 用量(每枚) = 500×400/1e6 × 1.35 = 0.27 m²；PaperCost = 32.5 × 0.27 = 8.775
        Assert.Equal(2000m, r.EstimateSqm);          // 面积 0.2 × 10000
        Assert.Equal(8.775m, r.StandardUnitPrice);
        Assert.Equal(10.530m, r.EstimateUnitPrice);  // × 1.20
        Assert.Equal(r.EstimateUnitPrice, r.ConfirmedUnitPrice);
    }

    [Fact]
    public async Task EstimateCalc_DimensionalUsage_Fallback_WithProcesses_Unchanged()
    {
        var svc = CreateService(out _);
        var dto = BaseDto();
        dto.PaperCdF = null;     // fallback 单价 30
        dto.SheetFlute = null;   // fallback 段成率 1.00
        dto.Processes.Add(new EstimateCalcProcessDto { SeqNo = 1, ProcessCd = "P01" });
        dto.Processes.Add(new EstimateCalcProcessDto { SeqNo = 2, ProcessCd = "P02" });

        var r = await svc.CalculateAsync(dto);

        // 用量(每枚) = 0.2 × 1.00 = 0.2；PaperCost = 30 × 0.2 = 6；工程 2×5=10 → Std 16
        Assert.Equal(16m, r.StandardUnitPrice);
        Assert.Equal(19.200m, r.EstimateUnitPrice);
    }
}
