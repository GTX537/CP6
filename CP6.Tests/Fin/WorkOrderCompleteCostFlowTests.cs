using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Tests.Fin;

/// <summary>
/// F1 財務油路 波C.2 — 完工触发成本归集 + 结转（两断点接线的端到端验证）。
///
/// 断点1：ProductionResultService 完工(justCompleted) → BackflushService 反冲回写 ActualQty → 之后触发
///        IFinBridgeHook.OnWorkOrderCompletedAsync（顺序 load-bearing：CollectAsync 读 ActualQty）。
/// 断点2：FinBridgeHook.OnWorkOrderCompletedAsync → CollectAsync 成功后继续 CostSettleService.SettleAsync
///        （料工费→WIP + WIP→FG 两凭证）。Collect 失败则不 Settle。
///
/// 全走真链（真 ProductionResultService/BackflushService/FinBridgeHook/CostCollect/CostSettle/引擎 + InMemory），
/// 断言凭证内容（WIP/FG/INVENTORY 科目 Role + 金额）非 mock。
/// </summary>
public class WorkOrderCompleteCostFlowTests
{
    // ProductionResultService.WriteAsync は明示トランザクションを使うため、
    // InMemory の TransactionIgnoredWarning を抑止（端到端テストで必須）。
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private sealed class Kit
    {
        public required CP6Context Db;
        public required GlAccountService Gl;
        public required FinBridgeHook Hook;
    }

    /// <summary>真 GL + 真 CostCollect/CostSettle + 真 FinBridgeHook（工费留 0 走 collect 默认）。</summary>
    private static async Task<Kit> SetupHookAsync(CP6Context db)
    {
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");
        PostingRuleSeed.EnsureSeeded(db);
        var journal = new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db));
        var engine = new AutoVoucherEngine(db, journal);
        var ar = new ArInvoiceService(db, engine, journal, new FinSequenceService(db));
        var collect = new CostCollectService(db, new FinSequenceService(db), new ProcessCostRateService(db));
        var settle = new CostSettleService(db, journal);
        var hook = new FinBridgeHook(db, ar, collect, settle, NullLogger<FinBridgeHook>.Instance);
        return new Kit { Db = db, Gl = gl, Hook = hook };
    }

    private static void SeedWoWithActual(CP6Context db, string wo, decimal supplyPrice, decimal actualQty)
    {
        db.Set<WorkOrder>().Add(new WorkOrder { Id = Guid.NewGuid(), WorkOrderNo = wo, ProductCd = "P1", CompletedQty = 10m, Status = WorkOrderStatus.Completed });
        db.Set<ProductMaterial>().Add(new ProductMaterial { Id = Guid.NewGuid(), ProductCd = "P1", ProcessCd = "OP1", MaterialCd = "M1", SupplyPrice = supplyPrice });
        db.Set<WorkOrderMaterial>().Add(new WorkOrderMaterial { Id = Guid.NewGuid(), WorkOrderNo = wo, ProcessCd = "OP1", MaterialCd = "M1", PlanQty = 100m, ActualQty = actualQty });
    }

    // ════════════════════════════════════════════════════════════════════════
    //  断点2：Hook 归集 → 结转两凭证（精确科目 Role + 金额）
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Hook_CollectThenSettle_PostsWipAndFgVouchers_ByRole_MarksSettled()
    {
        using var db = NewDb();
        var k = await SetupHookAsync(db);
        SeedWoWithActual(db, "WO1", supplyPrice: 5m, actualQty: 110m);   // 料真实消耗 110×5 = 550
        await db.SaveChangesAsync();

        var r = await k.Hook.OnWorkOrderCompletedAsync("WO1", "mes");
        Assert.True(r.Success, r.Message);

        var cs = await db.CostSheets.SingleAsync(s => s.WorkOrderNo == "WO1");
        Assert.Equal(550m, cs.MaterialActual);                 // 反冲量×SupplyPrice 真值
        Assert.Equal(CostSheetStatus.Settled, cs.Status);      // 归集后已结转
        Assert.NotNull(cs.JournalEntryId);

        var wip = (await k.Gl.GetByRoleAsync("WIP"))!.Id;
        var fg = (await k.Gl.GetByRoleAsync("FG"))!.Id;
        var inv = (await k.Gl.GetByRoleAsync("INVENTORY"))!.Id;
        var cogs = (await k.Gl.GetByRoleAsync("COGS"))!.Id;

        // ① 料工费 → WIP：借 WIP 实际全额 550 / 贷 原材料(INVENTORY) 550
        var collect = await db.JournalEntries.Include(e => e.Lines).SingleAsync(e => e.SourceDocNo!.EndsWith("#WIP"));
        Assert.Equal(VoucherSource.Cost, collect.Source);
        Assert.Equal(550m, collect.Lines.Single(l => l.AccountId == wip).Debit);
        Assert.Equal(550m, collect.Lines.Single(l => l.AccountId == inv).Credit);

        // ② WIP → FG：拍板②按标准成本 500（计划100×5）/ 贷 在制品(WIP) 500
        var settle = await db.JournalEntries.Include(e => e.Lines).SingleAsync(e => e.SourceDocNo!.EndsWith("#FG"));
        Assert.Equal(500m, settle.Lines.Single(l => l.AccountId == fg).Debit);
        Assert.Equal(500m, settle.Lines.Single(l => l.AccountId == wip).Credit);

        // ③ 差异 = 实际550 − 标准500 = +50 超用（多耗10×5）→ 借 COGS 50 / 贷 WIP 50
        var varv = await db.JournalEntries.Include(e => e.Lines).SingleAsync(e => e.SourceDocNo!.EndsWith("#VAR"));
        Assert.Equal(50m, varv.Lines.Single(l => l.AccountId == cogs).Debit);
        Assert.Equal(50m, varv.Lines.Single(l => l.AccountId == wip).Credit);

        // WIP 借贷净零（三腿合计）
        var wipLines = await db.JournalLines.Where(l => l.AccountId == wip).ToListAsync();
        Assert.Equal(wipLines.Sum(l => l.Debit), wipLines.Sum(l => l.Credit));
    }

    // ── Collect 失败（工单不存在 → E-FIN-401）则不 Settle：零凭证 ──
    [Fact]
    public async Task Hook_CollectFails_NoSettle_NoVouchers()
    {
        using var db = NewDb();
        var k = await SetupHookAsync(db);   // 未 seed 工单

        var r = await k.Hook.OnWorkOrderCompletedAsync("NOPE", "mes");
        Assert.False(r.Success);

        Assert.Equal(0, await db.CostSheets.CountAsync());
        Assert.Equal(0, await db.JournalEntries.CountAsync(j => j.Source == VoucherSource.Cost));
        Assert.True(await db.IntegrationEvents.AnyAsync(e => e.SourceNo == "NOPE" && e.Status == "FAILED"));
    }

    // ── 重复触发：不重复凭证（第二次幂等跳过）──
    [Fact]
    public async Task Hook_RepeatTrigger_NoDuplicateVouchers()
    {
        using var db = NewDb();
        var k = await SetupHookAsync(db);
        SeedWoWithActual(db, "WO1", supplyPrice: 5m, actualQty: 110m);
        await db.SaveChangesAsync();

        var r1 = await k.Hook.OnWorkOrderCompletedAsync("WO1", "mes");
        Assert.True(r1.Success, r1.Message);
        var r2 = await k.Hook.OnWorkOrderCompletedAsync("WO1", "mes");   // 重放同完工事件

        // 仍只有 1 张 #WIP + 1 张 #FG + 1 张 #VAR（料实际550 vs 标准500 有 +50 差异，共 3 张 Cost 凭证）
        Assert.Equal(3, await db.JournalEntries.CountAsync(j => j.Source == VoucherSource.Cost));
        Assert.Equal(1, await db.CostSheets.CountAsync(s => s.WorkOrderNo == "WO1"));
        Assert.False(r2.Success);   // 已结转 → 幂等跳过（非重复过账）
    }

    // ════════════════════════════════════════════════════════════════════════
    //  断点1：真 ProductionResultService 完工 → 反冲回写 → 触发归集+结转（端到端）
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Complete_ThroughRealProductionResultService_BackflushThenCollectThenSettle()
    {
        using var db = NewDb();
        var k = await SetupHookAsync(db);

        // 発行済（Status=2）指図 1 工程（着手中 ProcessStatus=1）
        db.Set<WorkOrder>().Add(new WorkOrder { Id = Guid.NewGuid(), WorkOrderNo = "WOE", ProductCd = "P1", Status = 2, ProductionQty = 10m, CompletedQty = 0m });
        db.Set<WorkOrderProcess>().Add(new WorkOrderProcess { WorkOrderNo = "WOE", ProcessCd = "OP1", TaskCd = "T1", SortOrder = 1, ProcessStatus = 1 });
        // BOM：定额料 UnitUsage=2、SupplyPrice=5 → 反冲量 = 2×完工10 = 20、料成本 = 20×5 = 100
        db.Set<ProductMaterial>().Add(new ProductMaterial { Id = Guid.NewGuid(), ProductCd = "P1", ProcessCd = "OP1", MaterialCd = "M1", MaterialTypeDiv = "3", UsageType = 2, UnitUsage = 2m, SupplyPrice = 5m });
        db.Set<Stock>().Add(new Stock { WarehouseCd = "W01", LocationCd = "L1", ProductCd = "M1", LotNo = "", PhysicalQty = 100m, AllocatedQty = 0m, AvailableQty = 100m, OwnerType = StockOwnerType.Self, UnitPrice = 5m });
        await db.SaveChangesAsync();

        // 真 ProductionResultService（真 BackflushService + 真 FinBridgeHook 注入）
        var stock = new StockMovementService(db, new WmsSequenceService(db));
        var backflush = new BackflushService(db, stock, new MaterialUsageCalculator());
        var mesSeq = new MesSequenceService(db);
        var woService = new WorkOrderService(db, mesSeq, new NoOpWmsBridgeHook());
        var prService = new ProductionResultService(db, mesSeq, woService, new NoOpMesNotifier(), new NoOpWmsBridgeHook(), backflush, k.Hook);

        await prService.CompleteAsync(new ProductionResultRequest
        {
            WorkOrderNo = "WOE", ProcessCd = "OP1", OperatorCd = "OP", GoodQty = 10m,
        }, "mes");

        // ① 反冲回写：ActualQty = 20
        var wom = await db.WorkOrderMaterials.AsNoTracking().SingleAsync(m => m.WorkOrderNo == "WOE" && m.MaterialCd == "M1");
        Assert.Equal(20m, wom.ActualQty);

        // ② 归集：CostSheet.MaterialActual = 反冲量20 × SupplyPrice5 = 100（真值对齐）
        var cs = await db.CostSheets.SingleAsync(s => s.WorkOrderNo == "WOE");
        Assert.Equal(100m, cs.MaterialActual);
        Assert.Equal(CostSheetStatus.Settled, cs.Status);   // ③ 结转完成

        // ④ 两凭证：WIP/FG 各 100，WIP 借贷净零
        var wip = (await k.Gl.GetByRoleAsync("WIP"))!.Id;
        var fg = (await k.Gl.GetByRoleAsync("FG"))!.Id;
        var collect = await db.JournalEntries.Include(e => e.Lines).SingleAsync(e => e.SourceDocNo!.EndsWith("#WIP"));
        Assert.Equal(100m, collect.Lines.Single(l => l.AccountId == wip).Debit);
        var fgVoucher = await db.JournalEntries.Include(e => e.Lines).SingleAsync(e => e.SourceDocNo!.EndsWith("#FG"));
        Assert.Equal(100m, fgVoucher.Lines.Single(l => l.AccountId == fg).Debit);

        var wipLines = await db.JournalLines.Where(l => l.AccountId == wip).ToListAsync();
        Assert.Equal(wipLines.Sum(l => l.Debit), wipLines.Sum(l => l.Credit));   // WIP 净零
    }

    // ════════════════════════════════════════════════════════════════════════
    //  审查修复：两道闸（backflush 失败不触发 fin hook / 零成本不结转防锁死）
    // ════════════════════════════════════════════════════════════════════════

    private sealed class ThrowingBackflush : IBackflushService
    {
        public Task BackflushAsync(string workOrderNo, string? userName)
            => throw new InvalidOperationException("backflush boom");
    }

    private sealed class RecordingFinHook : IFinBridgeHook
    {
        public int WorkOrderCompletedCalls;
        public Task<FinBridgeResult> OnShipmentConfirmedAsync(FinShipmentInvoiceRequest request, string? userName)
            => Task.FromResult(FinBridgeResult.Ok(null));
        public Task<FinBridgeResult> OnShipmentCancelledAsync(string shipmentId, string? userName)
            => Task.FromResult(FinBridgeResult.Ok(null));
        public Task<FinBridgeResult> OnWorkOrderCompletedAsync(string workOrderNo, string? userName)
        {
            WorkOrderCompletedCalls++;
            return Task.FromResult(FinBridgeResult.Ok(workOrderNo));
        }
    }

    // ── 闸1：反冲抛错 → fin hook 本轮不触发（避免零/陈旧 ActualQty 被归集+auto-settle 锁死），报工本体照常成功 ──
    [Fact]
    public async Task Complete_BackflushThrows_FinHookNotTriggered_ReportStillSucceeds()
    {
        using var db = NewDb();
        db.Set<WorkOrder>().Add(new WorkOrder { Id = Guid.NewGuid(), WorkOrderNo = "WOB", ProductCd = "P1", Status = 2, ProductionQty = 10m, CompletedQty = 0m });
        db.Set<WorkOrderProcess>().Add(new WorkOrderProcess { WorkOrderNo = "WOB", ProcessCd = "OP1", TaskCd = "T1", SortOrder = 1, ProcessStatus = 1 });
        await db.SaveChangesAsync();

        var mesSeq = new MesSequenceService(db);
        var woService = new WorkOrderService(db, mesSeq, new NoOpWmsBridgeHook());
        var finHook = new RecordingFinHook();
        var prService = new ProductionResultService(db, mesSeq, woService, new NoOpMesNotifier(), new NoOpWmsBridgeHook(), new ThrowingBackflush(), finHook);

        // 反冲抛错被吞、报工不抛错
        var resultNo = await prService.CompleteAsync(new ProductionResultRequest
        {
            WorkOrderNo = "WOB", ProcessCd = "OP1", OperatorCd = "OP", GoodQty = 10m,
        }, "mes");
        Assert.False(string.IsNullOrEmpty(resultNo));

        // 完工照常落账，但 fin hook 本轮被跳过（0 次调用）
        var wo = await db.Set<WorkOrder>().AsNoTracking().SingleAsync(w => w.WorkOrderNo == "WOB");
        Assert.Equal(WorkOrderStatus.Completed, wo.Status);
        Assert.Equal(0, finHook.WorkOrderCompletedCalls);
    }

    // ── 闸2：归集后 TotalActual<=0 → 不结转（否则 CostSettle 会把零额单直接标 Settled 无凭证 → 恒 E-FIN-402 永久锁死）──
    [Fact]
    public async Task Hook_ZeroTotalActual_SkipsSettle_SheetStaysCollected()
    {
        using var db = NewDb();
        var k = await SetupHookAsync(db);
        SeedWoWithActual(db, "WOZ", supplyPrice: 5m, actualQty: 0m);   // 反冲未回写 → 料消耗 0 → TotalActual = 0
        await db.SaveChangesAsync();

        var r = await k.Hook.OnWorkOrderCompletedAsync("WOZ", "mes");
        Assert.False(r.Success);
        Assert.Contains("SKIPPED", r.Message);

        // 成本单留在 Collected（可重试），未被标 Settled，零凭证
        var cs = await db.CostSheets.SingleAsync(s => s.WorkOrderNo == "WOZ");
        Assert.Equal(CostSheetStatus.Collected, cs.Status);
        Assert.Null(cs.JournalEntryId);
        Assert.Equal(0, await db.JournalEntries.CountAsync(j => j.Source == VoucherSource.Cost));

        // IntegrationEvent 留 Skipped 痕
        Assert.True(await db.IntegrationEvents.AnyAsync(e => e.SourceNo == "WOZ" && e.Status == "SKIPPED"));

        // 反冲补偿回写后重放 → 可恢复：正常归集+结转
        var wom = await db.WorkOrderMaterials.SingleAsync(m => m.WorkOrderNo == "WOZ");
        wom.ActualQty = 10m;
        await db.SaveChangesAsync();
        var r2 = await k.Hook.OnWorkOrderCompletedAsync("WOZ", "mes");
        Assert.True(r2.Success, r2.Message);
        var cs2 = await db.CostSheets.SingleAsync(s => s.WorkOrderNo == "WOZ");
        Assert.Equal(CostSheetStatus.Settled, cs2.Status);
        Assert.Equal(50m, cs2.MaterialActual);   // 10 × 5
    }
}
