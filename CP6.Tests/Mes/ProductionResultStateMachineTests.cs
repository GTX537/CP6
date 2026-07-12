using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Mes;

/// <summary>
/// M-MES T6 — 報工状態機（ProductionResultService 開始/中断/再開/完了）核心用例。
///
/// 全局審計 #7：報工状態機 零テスト → 補網。真値錨定（期望値手算，服務算錯会红）。
///
/// 境界（F1 波との棲み分け）：
///  ・反冲（OUT/ISSUE）＋成本归集+结转链は F1 波 BackflushTests / WorkOrderCompleteCostFlowTests が既に錨定済 → 本ファイルでは再現しない。
///  ・完成品入庫の在庫側真値（InboundService.CreateFinishedGoodsFromWorkOrderAsync → W01 完成品在庫）は
///    WmsErpClosedLoopTests が既に錨定済 → 本ファイルは「状態機が末工程完了で入庫フックを正しい良品累計で一度だけ発火するか」
///    という状態機側の缺口のみを spy で検証（F1/端到端テストは NoOpWmsBridgeHook を使い、この発火契約は未検証だった）。
/// </summary>
public class ProductionResultStateMachineTests
{
    // WriteAsync は明示トランザクションを使うため InMemory の TransactionIgnoredWarning を抑止。
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private static ProductionResultService NewService(CP6Context db, IWmsBridgeHook? wms = null)
    {
        var seq = new MesSequenceService(db);
        var wo = new WorkOrderService(db, seq, new NoOpWmsBridgeHook());
        // backflush/finBridge は null（反冲・成本链は F1 波で錨定済のため状態機テストでは注入しない）
        return new ProductionResultService(db, seq, wo, new NoOpMesNotifier(), wms ?? new NoOpWmsBridgeHook());
    }

    private static ProductionResultRequest Req(string wo, string proc, decimal good = 0m,
        decimal defect = 0m, string? suspendReason = null)
        => new() { WorkOrderNo = wo, ProcessCd = proc, OperatorCd = "OP1", GoodQty = good, DefectQty = defect, SuspendReasonCd = suspendReason };

    // ── ① 非法流転：未開始（ProcessStatus=0）で直接完了 → ME-MSG-042 で拒否、何も落库しない ──
    // 手算：case4 の先頭 `proc.ProcessStatus != 1` で throw（mutation 前）。tx 未 commit → PR 0 件、proc/wo 不変。
    [Fact]
    public async Task Complete_WhenNotStarted_Rejected_NothingPersisted()
    {
        using var db = NewDb();
        db.Set<WorkOrder>().Add(new WorkOrder { WorkOrderNo = "WO_A", ProductCd = "P1", Status = WorkOrderStatus.Issued });
        db.Set<WorkOrderProcess>().Add(new WorkOrderProcess { WorkOrderNo = "WO_A", ProcessCd = "OP1", TaskCd = "T1", SortOrder = 1, ProcessStatus = 0 });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => NewService(db).CompleteAsync(Req("WO_A", "OP1", good: 5m), "tester"));
        Assert.Equal("ME-MSG-042", ex.Message);

        Assert.Equal(0, await db.Set<ProductionResult>().CountAsync());
        var proc = await db.Set<WorkOrderProcess>().AsNoTracking().SingleAsync(p => p.WorkOrderNo == "WO_A");
        Assert.Equal(0, proc.ProcessStatus);                                  // 未着手のまま
        var wo = await db.Set<WorkOrder>().AsNoTracking().SingleAsync(w => w.WorkOrderNo == "WO_A");
        Assert.Equal(WorkOrderStatus.Issued, wo.Status);                      // 指図も不変
    }

    // ── ② 非法流転：着手中（ProcessStatus=1）で再度開始 → ME-MSG-042 で拒否 ──
    // 手算：case1 の先頭 `proc.ProcessStatus != 0` で throw。PR 0 件、proc 不変。
    [Fact]
    public async Task Start_WhenAlreadyStarted_Rejected()
    {
        using var db = NewDb();
        db.Set<WorkOrder>().Add(new WorkOrder { WorkOrderNo = "WO_B", ProductCd = "P1", Status = WorkOrderStatus.InProgress });
        db.Set<WorkOrderProcess>().Add(new WorkOrderProcess { WorkOrderNo = "WO_B", ProcessCd = "OP1", TaskCd = "T1", SortOrder = 1, ProcessStatus = 1 });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => NewService(db).StartAsync(Req("WO_B", "OP1"), "tester"));
        Assert.Equal("ME-MSG-042", ex.Message);

        Assert.Equal(0, await db.Set<ProductionResult>().CountAsync());
        var proc = await db.Set<WorkOrderProcess>().AsNoTracking().SingleAsync(p => p.WorkOrderNo == "WO_B");
        Assert.Equal(1, proc.ProcessStatus);                                  // 着手中のまま
    }

    // ── ③ 合法全流転：開始→中断→再開→完了（唯一工程 → 完了で指図=完了）──
    // 手算（時系列）：
    //   Start :  proc 0→1、proc.ActualStartTime=NOW、wo.Status→3、wo.ActualStartDate=NOW、PR type=1
    //   Suspend: proc 1→3、wo.Status→5、PR type=2
    //   Resume:  proc 3→1、wo.Status→3、PR type=3
    //   Complete(good=8): proc 1→2、proc.ActualEndTime=NOW、proc.GoodQty 0→8、
    //                     全工程完了 → wo.Status→4、wo.ActualEndDate=NOW、wo.CompletedQty 0→8、PR type=4
    //   PR 合計 4 件（type 1/2/3/4 各 1）
    [Fact]
    public async Task FullLifecycle_Start_Suspend_Resume_Complete_TransitionsAndTimestamps()
    {
        using var db = NewDb();
        db.Set<WorkOrder>().Add(new WorkOrder { WorkOrderNo = "WO_C", ProductCd = "P1", Status = WorkOrderStatus.Issued, ProductionQty = 8m });
        db.Set<WorkOrderProcess>().Add(new WorkOrderProcess { WorkOrderNo = "WO_C", ProcessCd = "OP1", TaskCd = "T1", SortOrder = 1, ProcessStatus = 0 });
        await db.SaveChangesAsync();
        var svc = NewService(db);
        var t0 = DateTime.Now.AddSeconds(-1);

        await svc.StartAsync(Req("WO_C", "OP1"), "tester");
        var p1 = await db.Set<WorkOrderProcess>().AsNoTracking().SingleAsync(p => p.WorkOrderNo == "WO_C");
        var w1 = await db.Set<WorkOrder>().AsNoTracking().SingleAsync(w => w.WorkOrderNo == "WO_C");
        Assert.Equal(1, p1.ProcessStatus);
        Assert.NotNull(p1.ActualStartTime);
        Assert.True(p1.ActualStartTime >= t0);
        Assert.Equal(WorkOrderStatus.InProgress, w1.Status);
        Assert.NotNull(w1.ActualStartDate);

        await svc.SuspendAsync(Req("WO_C", "OP1", suspendReason: "R01"), "tester");
        var p2 = await db.Set<WorkOrderProcess>().AsNoTracking().SingleAsync(p => p.WorkOrderNo == "WO_C");
        var w2 = await db.Set<WorkOrder>().AsNoTracking().SingleAsync(w => w.WorkOrderNo == "WO_C");
        Assert.Equal(3, p2.ProcessStatus);                                   // 中断
        Assert.Equal(WorkOrderStatus.Interrupted, w2.Status);                // 5

        await svc.ResumeAsync(Req("WO_C", "OP1"), "tester");
        var p3 = await db.Set<WorkOrderProcess>().AsNoTracking().SingleAsync(p => p.WorkOrderNo == "WO_C");
        var w3 = await db.Set<WorkOrder>().AsNoTracking().SingleAsync(w => w.WorkOrderNo == "WO_C");
        Assert.Equal(1, p3.ProcessStatus);                                   // 着手中に復帰
        Assert.Equal(WorkOrderStatus.InProgress, w3.Status);                 // 3

        await svc.CompleteAsync(Req("WO_C", "OP1", good: 8m), "tester");
        var p4 = await db.Set<WorkOrderProcess>().AsNoTracking().SingleAsync(p => p.WorkOrderNo == "WO_C");
        var w4 = await db.Set<WorkOrder>().AsNoTracking().SingleAsync(w => w.WorkOrderNo == "WO_C");
        Assert.Equal(2, p4.ProcessStatus);                                   // 完了
        Assert.NotNull(p4.ActualEndTime);
        Assert.Equal(8m, p4.GoodQty);
        Assert.Equal(WorkOrderStatus.Completed, w4.Status);                  // 4（全工程完了）
        Assert.NotNull(w4.ActualEndDate);
        Assert.Equal(8m, w4.CompletedQty);

        // PR 4 件（type 1/2/3/4）
        var prTypes = await db.Set<ProductionResult>().AsNoTracking().Where(r => r.WorkOrderNo == "WO_C")
            .Select(r => r.ResultType).OrderBy(t => t).ToListAsync();
        Assert.Equal(new[] { 1, 2, 3, 4 }, prTypes);
    }

    // ── ④ 全工程完了トリガ入庫：中間工程完了では発火せず、末工程完了で良品累計(=17)で一度だけ発火 ──
    // 手算：2 工程 OP1/OP2 いずれも着手中(1)。
    //   Complete(OP1,10): OP1 1→2。allDone? OP2 が status1 → false。wo.Status=3 のまま、フック 0 回。CompletedQty 0→10。
    //   Complete(OP2, 7): OP2 1→2。allDone? OP1=2 & OP2=当該 → true。wo.Status→4、CompletedQty 10→17、
    //                     justCompleted → OnProductionCompletedAsync(WO_D, 17, tester) 一度だけ発火。
    [Fact]
    public async Task Complete_LastProcess_FiresInboundHookOnce_WithAccumulatedGoodQty()
    {
        using var db = NewDb();
        db.Set<WorkOrder>().Add(new WorkOrder { WorkOrderNo = "WO_D", ProductCd = "P1", Status = WorkOrderStatus.InProgress, ProductionQty = 17m });
        db.Set<WorkOrderProcess>().Add(new WorkOrderProcess { WorkOrderNo = "WO_D", ProcessCd = "OP1", TaskCd = "T1", SortOrder = 1, ProcessStatus = 1 });
        db.Set<WorkOrderProcess>().Add(new WorkOrderProcess { WorkOrderNo = "WO_D", ProcessCd = "OP2", TaskCd = "T1", SortOrder = 2, ProcessStatus = 1 });
        await db.SaveChangesAsync();

        var spy = new RecordingWmsBridge();
        var svc = NewService(db, spy);

        // 中間工程 OP1 完了 → まだ全工程完了ではない
        await svc.CompleteAsync(Req("WO_D", "OP1", good: 10m), "tester");
        var wMid = await db.Set<WorkOrder>().AsNoTracking().SingleAsync(w => w.WorkOrderNo == "WO_D");
        Assert.Equal(WorkOrderStatus.InProgress, wMid.Status);               // 3 のまま
        Assert.Equal(10m, wMid.CompletedQty);
        Assert.Equal(0, spy.Calls);                                          // フック未発火

        // 末工程 OP2 完了 → 全工程完了 → 入庫フック発火
        await svc.CompleteAsync(Req("WO_D", "OP2", good: 7m), "tester");
        var wEnd = await db.Set<WorkOrder>().AsNoTracking().SingleAsync(w => w.WorkOrderNo == "WO_D");
        Assert.Equal(WorkOrderStatus.Completed, wEnd.Status);               // 4
        Assert.NotNull(wEnd.ActualEndDate);
        Assert.Equal(17m, wEnd.CompletedQty);

        Assert.Equal(1, spy.Calls);                                         // 一度だけ発火
        Assert.Equal("WO_D", spy.LastWorkOrderNo);
        Assert.Equal(17m, spy.LastGoodQty);                                // 良品累計（10+7）で入庫
    }

    // ── ⑤ 境界：中断は理由必須（ME-MSG-024）／完了は良品数>0 必須（ME-MSG-012）。拒否時は状態不変・PR 0 件 ──
    // 手算：case2 は proc 変更前に理由空チェックで throw。case4 は proc 変更前に GoodQty<=0 チェックで throw。
    [Fact]
    public async Task Guards_SuspendNeedsReason_CompleteNeedsPositiveGood()
    {
        using var db = NewDb();
        // 中断：理由なし → ME-MSG-024
        db.Set<WorkOrder>().Add(new WorkOrder { WorkOrderNo = "WO_E", ProductCd = "P1", Status = WorkOrderStatus.InProgress });
        db.Set<WorkOrderProcess>().Add(new WorkOrderProcess { WorkOrderNo = "WO_E", ProcessCd = "OP1", TaskCd = "T1", SortOrder = 1, ProcessStatus = 1 });
        // 完了：良品数 0 → ME-MSG-012
        db.Set<WorkOrder>().Add(new WorkOrder { WorkOrderNo = "WO_F", ProductCd = "P1", Status = WorkOrderStatus.InProgress });
        db.Set<WorkOrderProcess>().Add(new WorkOrderProcess { WorkOrderNo = "WO_F", ProcessCd = "OP1", TaskCd = "T1", SortOrder = 1, ProcessStatus = 1 });
        await db.SaveChangesAsync();
        var svc = NewService(db);

        var exS = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SuspendAsync(Req("WO_E", "OP1"), "tester"));
        Assert.Equal("ME-MSG-024", exS.Message);
        Assert.Equal(1, (await db.Set<WorkOrderProcess>().AsNoTracking().SingleAsync(p => p.WorkOrderNo == "WO_E")).ProcessStatus);

        var exC = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CompleteAsync(Req("WO_F", "OP1", good: 0m), "tester"));
        Assert.Equal("ME-MSG-012", exC.Message);
        Assert.Equal(1, (await db.Set<WorkOrderProcess>().AsNoTracking().SingleAsync(p => p.WorkOrderNo == "WO_F")).ProcessStatus);

        Assert.Equal(0, await db.Set<ProductionResult>().CountAsync());       // 両 WO とも PR 0 件
    }

    /// <summary>入庫フック発火の記録用 spy（実 InboundService/在庫側は WmsErpClosedLoopTests が別途錨定）。</summary>
    private sealed class RecordingWmsBridge : IWmsBridgeHook
    {
        public int Calls;
        public string? LastWorkOrderNo;
        public decimal LastGoodQty;

        public Task<WmsBridgeResult> OnWorkOrderIssuedAsync(string workOrderNo, string? userName)
            => Task.FromResult(WmsBridgeResult.Skipped("n/a"));
        public Task<WmsBridgeResult> OnOrderCreatedAsync(string webOrderNo, string? userName)
            => Task.FromResult(WmsBridgeResult.Skipped("n/a"));
        public Task<WmsBridgeResult> OnProductionCompletedAsync(string workOrderNo, decimal goodQty, string? userName)
        {
            Calls++;
            LastWorkOrderNo = workOrderNo;
            LastGoodQty = goodQty;
            return Task.FromResult(WmsBridgeResult.Ok("RCPT-TEST"));
        }
    }
}
