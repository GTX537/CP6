using Microsoft.EntityFrameworkCore;
using CP6.Core.Services.Plan.Contracts;

namespace CP6.Tests;

/// <summary>
/// 计划订单人确认转单服务测试（MRP P1 · spec §9，MP-D4）。
/// 确认（建议→已确认进供给）/ 转单（采购→IPlanToPrService、生产→IPlanToWorkOrderService，回填单号+已转单，带 Pegging）/ 忽略。
/// </summary>
public class PlanConvertServiceTests
{
    private sealed class RecordingPrService : IPlanToPrService
    {
        public Plan_PlannedOrder? LastPo;
        public IReadOnlyList<Plan_Pegging>? LastPegs;
        public Task<string> CreatePrFromPlannedOrderAsync(Plan_PlannedOrder po, IReadOnlyList<Plan_Pegging> pegs)
        {
            LastPo = po; LastPegs = pegs;
            return Task.FromResult("PR-001");
        }
    }

    private sealed class RecordingWoService : IPlanToWorkOrderService
    {
        public Plan_PlannedOrder? LastPo;
        public Task<string> CreateWorkOrderFromPlannedOrderAsync(Plan_PlannedOrder po, IReadOnlyList<Plan_Pegging> pegs)
        {
            LastPo = po;
            return Task.FromResult("WO-001");
        }
    }

    private static PlanConvertService Create(out CP6.Core.EFDbContext.CP6Context db, out RecordingPrService pr, out RecordingWoService wo)
    {
        db = TestHelper.CreateInMemoryContext();
        pr = new RecordingPrService();
        wo = new RecordingWoService();
        return new PlanConvertService(db, pr, wo);
    }

    private static async Task<Guid> SeedPoAsync(CP6.Core.EFDbContext.CP6Context db, PlannedOrderType type, int status, bool withPegging = false)
    {
        var po = new Plan_PlannedOrder { ItemCd = "RAW", Qty = 100, Type = (int)type, Status = status, RequiredDate = new DateTime(2026, 7, 1) };
        db.PlannedOrders.Add(po);
        await db.SaveChangesAsync();
        if (withPegging)
        {
            db.Peggings.Add(new Plan_Pegging { PlannedOrderId = po.Id, SourceType = (int)PeggingSourceType.Order, SourceRefNo = "SO-1", Qty = 100 });
            await db.SaveChangesAsync();
        }
        return po.Id;
    }

    [Fact]
    public async Task Confirm_Suggested_BecomesConfirmed()
    {
        var svc = Create(out var db, out _, out _);
        var id = await SeedPoAsync(db, PlannedOrderType.Purchase, (int)PlannedOrderStatus.Suggested);

        await svc.ConfirmAsync(id, "admin");

        var po = await db.PlannedOrders.FirstAsync(o => o.Id == id);
        Assert.Equal((int)PlannedOrderStatus.Confirmed, po.Status);
    }

    [Fact]
    public async Task Convert_Purchase_CallsPrService_WithPegging_SetsConverted()
    {
        var svc = Create(out var db, out var pr, out _);
        var id = await SeedPoAsync(db, PlannedOrderType.Purchase, (int)PlannedOrderStatus.Confirmed, withPegging: true);

        var docNo = await svc.ConvertAsync(id, "admin");

        Assert.Equal("PR-001", docNo);
        Assert.Equal("RAW", pr.LastPo!.ItemCd);
        Assert.Single(pr.LastPegs!);          // Pegging 传给下游
        var po = await db.PlannedOrders.FirstAsync(o => o.Id == id);
        Assert.Equal((int)PlannedOrderStatus.Converted, po.Status);
        Assert.Equal("PR-001", po.ConvertedDocNo);
    }

    [Fact]
    public async Task Convert_Production_CallsWorkOrderService_SetsConverted()
    {
        var svc = Create(out var db, out _, out var wo);
        var id = await SeedPoAsync(db, PlannedOrderType.Production, (int)PlannedOrderStatus.Confirmed);

        var docNo = await svc.ConvertAsync(id, "admin");

        Assert.Equal("WO-001", docNo);
        Assert.Equal("RAW", wo.LastPo!.ItemCd);
        var po = await db.PlannedOrders.FirstAsync(o => o.Id == id);
        Assert.Equal((int)PlannedOrderStatus.Converted, po.Status);
        Assert.Equal("WO-001", po.ConvertedDocNo);
    }

    [Fact]
    public async Task Convert_AlreadyConverted_Throws()
    {
        var svc = Create(out var db, out _, out _);
        var id = await SeedPoAsync(db, PlannedOrderType.Purchase, (int)PlannedOrderStatus.Converted);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConvertAsync(id, "admin"));
    }

    [Fact]
    public async Task Ignore_SetsIgnored()
    {
        var svc = Create(out var db, out _, out _);
        var id = await SeedPoAsync(db, PlannedOrderType.Purchase, (int)PlannedOrderStatus.Suggested);

        await svc.IgnoreAsync(id, "admin");

        var po = await db.PlannedOrders.FirstAsync(o => o.Id == id);
        Assert.Equal((int)PlannedOrderStatus.Ignored, po.Status);
    }
}
