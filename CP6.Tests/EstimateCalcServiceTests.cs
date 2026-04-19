using CP6.Core.Services;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

/// <summary>
/// EstimateCalcService 単元测试（MSBBPA010）
///
/// 面试要点：
/// 1. 用 InMemory DB 做集成测试，覆盖采番/乐观锁/软删/Copy/diff
/// 2. 通过 SaveChanges 触发 EF 行为，与真实 SQL Server 一致
/// 3. 每个测试独立数据库，避免相互污染
/// 4. 采番测试要 Create 两次验证主番连号
/// 5. Calculate 是纯函数，不依赖 DB，仅断言公式
/// </summary>
public class EstimateCalcServiceTests
{
    // ===== 工厂 =====

    private static EstimateCalcService CreateService(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        return new EstimateCalcService(db);
    }

    private static EstimateCalcDto NewDto(int orderQty = 10000)
    {
        return new EstimateCalcDto
        {
            QtnBaseCd = "01",
            OrderBaseCd = "01",
            StaffCd = "S001",
            CustomerCd = "C0001",
            CustomerProductName1 = "テスト商品",
            OrderQty = orderQty,
            ParentChildDiv = "01",
            OrderType = "01",
            ProductCategoryBig = "A",
            ProductCategoryMid = "A01",
            SheetFlute = "A",
            PaperCdF = "P01",
            PrintCdF = "PR01",
            FinalMachineProc = "F01",
            ProductShape1 = "SH01",
            DistDiv = "D01",
            Unit = "U01",
            SheetDimW = 500,
            SheetDimF = 400,
            EstimateQtys = new decimal?[] { 1000, 5000, 10000, 0, 0, 0, 0, 0 },
            PalletCnts = new decimal?[8],
            StrategicDivs = new bool[10],
        };
    }

    // ===== Create =====

    [Fact]
    public async Task CreateAsync_ShouldAssignSequentialQtnCalcNo()
    {
        // Arrange
        var svc = CreateService(out var db);

        // Act - 连续创建两个
        var no1 = await svc.CreateAsync(NewDto(), "admin");
        var no2 = await svc.CreateAsync(NewDto(), "admin");

        // Assert - 采番是 00000001-01 / 00000002-01
        Assert.Equal("00000001-01", no1);
        Assert.Equal("00000002-01", no2);
        Assert.Equal(2, await db.EstimateCalcs.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_ShouldSaveProcesses()
    {
        var svc = CreateService(out var db);
        var dto = NewDto();
        dto.Processes.Add(new EstimateCalcProcessDto { SeqNo = 1, ProcessCd = "P01", ProcessName = "印刷" });
        dto.Processes.Add(new EstimateCalcProcessDto { SeqNo = 2, ProcessCd = "P02", ProcessName = "打抜" });

        var no = await svc.CreateAsync(dto, "admin");

        var fresh = await svc.GetByNoAsync(no);
        Assert.NotNull(fresh);
        Assert.Equal(2, fresh!.Processes.Count);
        Assert.Equal("印刷", fresh.Processes[0].ProcessName);
    }

    // ===== GetByNo =====

    [Fact]
    public async Task GetByNoAsync_NotFound_ShouldReturnNull()
    {
        var svc = CreateService(out _);
        var dto = await svc.GetByNoAsync("99999999-99");
        Assert.Null(dto);
    }

    [Fact]
    public async Task GetByNoAsync_SoftDeleted_ShouldReturnNullByDefault()
    {
        var svc = CreateService(out var db);
        var no = await svc.CreateAsync(NewDto(), "admin");
        await svc.DeleteAsync(no, null, "admin");

        // 默认 includeDeleted=false
        Assert.Null(await svc.GetByNoAsync(no));
        // includeDeleted=true 可以查到
        Assert.NotNull(await svc.GetByNoAsync(no, includeDeleted: true));
    }

    // ===== Update =====

    [Fact]
    public async Task UpdateAsync_ShouldPersistChangesAndDiffProcesses()
    {
        var svc = CreateService(out var db);
        var dto = NewDto();
        dto.Processes.Add(new EstimateCalcProcessDto { SeqNo = 1, ProcessCd = "P01", ProcessName = "印刷" });
        dto.Processes.Add(new EstimateCalcProcessDto { SeqNo = 2, ProcessCd = "P02", ProcessName = "打抜" });
        var no = await svc.CreateAsync(dto, "admin");

        // 读回来 → 修改 CustomerProductName1 + 工程 diff
        var fresh = await svc.GetByNoAsync(no);
        Assert.NotNull(fresh);
        fresh!.CustomerProductName1 = "更新後商品";

        // 删除 SeqNo=2，保留 SeqNo=1，新增 SeqNo=3
        fresh.Processes.RemoveAt(1);
        fresh.Processes.Add(new EstimateCalcProcessDto { SeqNo = 3, ProcessCd = "P03", ProcessName = "貼合" });

        await svc.UpdateAsync(no, fresh, "admin2");

        // 再读回来验证
        var after = await svc.GetByNoAsync(no);
        Assert.Equal("更新後商品", after!.CustomerProductName1);
        // Diff: SeqNo=1 保留, SeqNo=2 软删（视图过滤掉）, SeqNo=3 新增
        Assert.Equal(2, after.Processes.Count);
        Assert.Contains(after.Processes, p => p.SeqNo == 1);
        Assert.Contains(after.Processes, p => p.SeqNo == 3);
        Assert.DoesNotContain(after.Processes, p => p.SeqNo == 2);
    }

    [Fact]
    public async Task UpdateAsync_NonExistent_ShouldThrowKeyNotFound()
    {
        var svc = CreateService(out _);
        var dto = NewDto();
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.UpdateAsync("00000099-99", dto, "admin"));
    }

    // ===== Delete (soft) =====

    [Fact]
    public async Task DeleteAsync_ShouldSoftDelete()
    {
        var svc = CreateService(out var db);
        var no = await svc.CreateAsync(NewDto(), "admin");

        await svc.DeleteAsync(no, null, "admin");

        // 物理仍在
        var raw = await db.EstimateCalcs.FirstAsync(x => x.QtnCalcNo == no);
        Assert.True(raw.IsDeleted);

        // 但接口过滤后查不到
        Assert.Null(await svc.GetByNoAsync(no));
    }

    [Fact]
    public async Task DeleteAsync_AlreadyDeleted_ShouldThrowKeyNotFound()
    {
        var svc = CreateService(out _);
        var no = await svc.CreateAsync(NewDto(), "admin");
        await svc.DeleteAsync(no, null, "admin");

        // 第二次删除：已过滤掉，找不到
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.DeleteAsync(no, null, "admin"));
    }

    // ===== Copy =====

    [Fact]
    public async Task CopyAsync_ShouldGenerateNewNoAndKeepRef()
    {
        var svc = CreateService(out var db);
        var dto = NewDto();
        dto.Processes.Add(new EstimateCalcProcessDto { SeqNo = 1, ProcessCd = "P01" });
        var sourceNo = await svc.CreateAsync(dto, "admin");

        var newNo = await svc.CopyAsync(sourceNo, "admin");

        Assert.NotEqual(sourceNo, newNo);
        Assert.Equal("00000002-01", newNo);

        var copied = await svc.GetByNoAsync(newNo);
        Assert.NotNull(copied);
        Assert.Equal(sourceNo, copied!.RefQtnCalcNo);
        Assert.Single(copied.Processes);
        Assert.Equal("P01", copied.Processes[0].ProcessCd);
    }

    [Fact]
    public async Task CopyAsync_NonExistent_ShouldThrowKeyNotFound()
    {
        var svc = CreateService(out _);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.CopyAsync("99999999-99", "admin"));
    }

    // ===== Calculate（查 M_GenericCode Paper/M067） =====

    /// <summary>
    /// 无主数据时：Paper 単価 fallback = 30，段成率 fallback = 1.00
    /// </summary>
    [Fact]
    public async Task CalculateAsync_NoMasterData_ShouldUseFallback()
    {
        var svc = CreateService(out _);
        var dto = NewDto(orderQty: 10000);
        dto.SheetDimW = 500;
        dto.SheetDimF = 400;
        dto.PaperCdF = null;  // 不触发主数据查询
        dto.SheetFlute = null;

        var r = await svc.CalculateAsync(dto);

        // SheetArea = 0.2m² / 枚
        Assert.Equal(2000m, r.EstimateSqm);
        // 30 × 0.2 × 1.00 + 0 = 6
        Assert.Equal(6m, r.StandardUnitPrice);
        Assert.Equal(7.200m, r.EstimateUnitPrice);
        Assert.Equal(r.EstimateUnitPrice, r.ConfirmedUnitPrice);
    }

    /// <summary>
    /// 从 M_GenericCode 取到单价和段成率：Paper.P001.Num1=32.5 / M067.E.Num1=1.35
    /// </summary>
    [Fact]
    public async Task CalculateAsync_WithMasterData_ShouldUseDbValues()
    {
        var svc = CreateService(out var db);
        db.MasterGenericCodes.Add(new MasterGenericCode
        { GroupCode = "Paper", Code = "P001", Name = "K220", Num1 = 32.5m });
        db.MasterGenericCodes.Add(new MasterGenericCode
        { GroupCode = "M067", Code = "E", Name = "E 段成率", Num1 = 1.35m });
        await db.SaveChangesAsync();

        var dto = NewDto();
        dto.SheetDimW = 500;
        dto.SheetDimF = 400;
        dto.PaperCdF = "P001";
        dto.SheetFlute = "E";

        var r = await svc.CalculateAsync(dto);

        // SheetArea = 0.2 m²
        // PaperCost = 32.5 × 0.2 × 1.35 = 8.775
        // Std = 8.775 + 工程0 = 8.775
        Assert.Equal(8.775m, r.StandardUnitPrice);
        // Est = 8.775 × 1.20 = 10.53
        Assert.Equal(10.530m, r.EstimateUnitPrice);
    }

    [Fact]
    public async Task CalculateAsync_WithProcesses_ShouldAddProcessCost()
    {
        var svc = CreateService(out _);
        var dto = NewDto();
        dto.SheetDimW = 500;
        dto.SheetDimF = 400;
        dto.PaperCdF = null;
        dto.SheetFlute = null;
        dto.Processes.Add(new EstimateCalcProcessDto { SeqNo = 1, ProcessCd = "P01" });
        dto.Processes.Add(new EstimateCalcProcessDto { SeqNo = 2, ProcessCd = "P02" });

        var r = await svc.CalculateAsync(dto);

        // 6 + 2×5 = 16
        Assert.Equal(16m, r.StandardUnitPrice);
        Assert.Equal(19.200m, r.EstimateUnitPrice);
    }

    [Fact]
    public async Task CalculateAsync_ShouldFillAmountRowsForQtyOnly()
    {
        var svc = CreateService(out _);
        var dto = NewDto();
        dto.SheetDimW = 500;
        dto.SheetDimF = 400;
        dto.EstimateQtys = new decimal?[] { 1000, 0, 3000, 0, 5000, 0, 0, 0 };

        var r = await svc.CalculateAsync(dto);

        Assert.Equal(3, r.AmountRows.Count);
        Assert.Equal(1000m, r.AmountRows[0].Qty);
        Assert.Equal(3000m, r.AmountRows[1].Qty);
        Assert.Equal(5000m, r.AmountRows[2].Qty);
        Assert.All(r.AmountRows, row => Assert.Equal(r.EstimateUnitPrice, row.UnitPrice));
    }

    [Fact]
    public async Task CalculateAsync_ZeroSheet_ShouldReturnZeroPrice()
    {
        var svc = CreateService(out _);
        var dto = NewDto();
        dto.SheetDimW = 0;
        dto.SheetDimF = 0;

        var r = await svc.CalculateAsync(dto);

        Assert.Equal(0m, r.EstimateSqm);
        Assert.Equal(0m, r.StandardUnitPrice);
        Assert.Equal(0m, r.EstimateUnitPrice);
        Assert.All(r.AmountRows, row => Assert.Equal(0m, row.UnitPrice));
        Assert.All(r.AmountRows, row => Assert.Equal(0m, row.Amount));
    }
}
