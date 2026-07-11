using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
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

        // ① 料工费 → WIP：借 WIP 550 / 贷 原材料(INVENTORY) 550
        var collect = await db.JournalEntries.Include(e => e.Lines).SingleAsync(e => e.SourceDocNo!.EndsWith("#WIP"));
        Assert.Equal(VoucherSource.Cost, collect.Source);
        Assert.Equal(550m, collect.Lines.Single(l => l.AccountId == wip).Debit);
        Assert.Equal(550m, collect.Lines.Single(l => l.AccountId == inv).Credit);

        // ② WIP → FG：借 库存商品(FG) 550 / 贷 在制品(WIP) 550
        var settle = await db.JournalEntries.Include(e => e.Lines).SingleAsync(e => e.SourceDocNo!.EndsWith("#FG"));
        Assert.Equal(550m, settle.Lines.Single(l => l.AccountId == fg).Debit);
        Assert.Equal(550m, settle.Lines.Single(l => l.AccountId == wip).Credit);
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

        // 仍只有 1 张 #WIP + 1 张 #FG（共 2 张 Cost 凭证）
        Assert.Equal(2, await db.JournalEntries.CountAsync(j => j.Source == VoucherSource.Cost));
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
}
