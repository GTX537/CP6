using CP6.Core.Services.Mes;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Mes;

/// <summary>
/// M-MES T6 — 生産計画ボード（PlanningBoardService）排産/改期 核心用例。
///
/// 全局審計 #7：PlanningBoard 零テスト → 補網。真値錨定（期望値手算）。
/// RescheduleAsync/AutoArrangeAsync は明示トランザクション不使用のため素の InMemory で十分。
/// </summary>
public class PlanningBoardServiceTests
{
    private static WorkOrderProcess Proc(string wo, string proc, int status, string? machine,
        int sort = 1, decimal? leadTime = null, DateTime? planStart = null, DateTime? planEnd = null)
        => new()
        {
            WorkOrderNo = wo, ProcessCd = proc, TaskCd = "T1", ProcessStatus = status,
            MachineCd = machine, SortOrder = sort, LeadTime = leadTime,
            PlanStartTime = planStart, PlanEndTime = planEnd,
        };

    // ══════════════════════════════════════════════════════════════════
    //  Reschedule（ドラッグ改期）
    // ══════════════════════════════════════════════════════════════════

    // ── ① 改期落库真値：未着手(0)工程の計画日時＋号機が上書きされる ──
    // 手算：Id 一致 → status0 通過 → start<end 通過 → PlanStart/PlanEnd/MachineCd を req 値で上書き。
    [Fact]
    public async Task Reschedule_UnstartedProcess_PersistsNewTimesAndMachine()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var p = Proc("WO1", "OP1", status: 0, machine: "M0",
            planStart: new DateTime(2026, 7, 15, 8, 0, 0), planEnd: new DateTime(2026, 7, 15, 10, 0, 0));
        db.Set<WorkOrderProcess>().Add(p);
        await db.SaveChangesAsync();

        var newStart = new DateTime(2026, 8, 1, 9, 0, 0);
        var newEnd = new DateTime(2026, 8, 1, 17, 0, 0);
        await new PlanningBoardService(db).RescheduleAsync(
            new RescheduleRequest { Id = p.Id, PlanStartTime = newStart, PlanEndTime = newEnd, MachineCd = "M9" }, "planner");

        var got = await db.Set<WorkOrderProcess>().AsNoTracking().SingleAsync(x => x.Id == p.Id);
        Assert.Equal(newStart, got.PlanStartTime);
        Assert.Equal(newEnd, got.PlanEndTime);
        Assert.Equal("M9", got.MachineCd);
        Assert.Equal("planner", got.Modifier);
    }

    // ── ② 改期の3ガード：start>end(ME-MSG-003) / 着手済(ME-MSG-042) / 不存在(ME-MSG-043) ──
    // 手算：
    //   start>end → ME-MSG-003
    //   ProcessStatus!=0（例：1 着手中）→ ME-MSG-042（発行後はドラッグ不可）
    //   Id 不一致 → ME-MSG-043
    [Fact]
    public async Task Reschedule_Guards_StartAfterEnd_Started_NotFound()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var pUnstarted = Proc("WO2", "OP1", status: 0, machine: "M1");
        var pStarted = Proc("WO2", "OP2", status: 1, machine: "M1", sort: 2);
        db.Set<WorkOrderProcess>().AddRange(pUnstarted, pStarted);
        await db.SaveChangesAsync();
        var svc = new PlanningBoardService(db);

        // start > end（未着手でも日時逆転で拒否）
        var ex3 = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RescheduleAsync(
            new RescheduleRequest { Id = pUnstarted.Id, PlanStartTime = new DateTime(2026, 8, 2), PlanEndTime = new DateTime(2026, 8, 1) }, "planner"));
        Assert.Equal("ME-MSG-003", ex3.Message);

        // 着手中 → 改期不可
        var ex42 = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RescheduleAsync(
            new RescheduleRequest { Id = pStarted.Id, PlanStartTime = new DateTime(2026, 8, 1), PlanEndTime = new DateTime(2026, 8, 2) }, "planner"));
        Assert.Equal("ME-MSG-042", ex42.Message);

        // 不存在 Id
        var ex43 = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RescheduleAsync(
            new RescheduleRequest { Id = Guid.NewGuid(), PlanStartTime = new DateTime(2026, 8, 1), PlanEndTime = new DateTime(2026, 8, 2) }, "planner"));
        Assert.Equal("ME-MSG-043", ex43.Message);
    }

    // ── ③ 境界（服務の実語義を pin）：過去日付／同一号機の時間重複 いずれもガードなし＝受理 ──
    // 手算：RescheduleAsync は start<end のみ検証。過去日付ガード無し・号機衝突検知無し → どちらも落库成功。
    // ※これは「サーバは単にドラッグ位置を永続化するだけ、衝突/過去は UI/計画側の責務」という現状仕様の記録。
    //   自動衝突回避が要件なら缺陷（concerns 参照）。
    [Fact]
    public async Task Reschedule_PastDate_And_MachineOverlap_AreAccepted_NoConflictGuard()
    {
        using var db = TestHelper.CreateInMemoryContext();
        // 既存：M1 上 2026-07-15 08:00-10:00 を占有する未着手工程 A
        var a = Proc("WO3", "OP1", status: 0, machine: "M1",
            planStart: new DateTime(2026, 7, 15, 8, 0, 0), planEnd: new DateTime(2026, 7, 15, 10, 0, 0));
        var b = Proc("WO3", "OP2", status: 0, machine: "M1", sort: 2);
        db.Set<WorkOrderProcess>().AddRange(a, b);
        await db.SaveChangesAsync();
        var svc = new PlanningBoardService(db);

        // (a) 過去日付（2020 年）→ ガード無し、受理
        await svc.RescheduleAsync(new RescheduleRequest
        { Id = b.Id, PlanStartTime = new DateTime(2020, 1, 1, 8, 0, 0), PlanEndTime = new DateTime(2020, 1, 1, 10, 0, 0), MachineCd = "M1" }, "planner");
        var afterPast = await db.Set<WorkOrderProcess>().AsNoTracking().SingleAsync(x => x.Id == b.Id);
        Assert.Equal(new DateTime(2020, 1, 1, 8, 0, 0), afterPast.PlanStartTime);

        // (b) 号機 M1 上で A(08:00-10:00) と完全重複する 08:00-10:00 → 衝突検知無し、受理
        await svc.RescheduleAsync(new RescheduleRequest
        { Id = b.Id, PlanStartTime = new DateTime(2026, 7, 15, 8, 0, 0), PlanEndTime = new DateTime(2026, 7, 15, 10, 0, 0), MachineCd = "M1" }, "planner");
        var afterOverlap = await db.Set<WorkOrderProcess>().AsNoTracking().SingleAsync(x => x.Id == b.Id);
        Assert.Equal(new DateTime(2026, 7, 15, 8, 0, 0), afterOverlap.PlanStartTime);
        Assert.Equal(new DateTime(2026, 7, 15, 10, 0, 0), afterOverlap.PlanEndTime);
        // A は依然 08:00-10:00 を占有（重複を許容）
        var stillA = await db.Set<WorkOrderProcess>().AsNoTracking().SingleAsync(x => x.Id == a.Id);
        Assert.Equal(new DateTime(2026, 7, 15, 8, 0, 0), stillA.PlanStartTime);
    }

    // ══════════════════════════════════════════════════════════════════
    //  AutoArrange（自動配置）
    // ══════════════════════════════════════════════════════════════════

    // ── ④ 自動配置：優先度降順→納期昇順→SortOrder で並べ、号機別に連続配置。着手済は対象外 ──
    // 手算（BaseDate=2026-07-15、baseTime=07-15 08:00、DefaultHoursPerJob=2）：
    //   対象（status0）を優先度DESC/納期ASC/SortOrder で並べる：
    //     P_A: WO_HI(prio3), M1, sort1, LeadTime=null → hours=default2
    //     P_C: WO_HI(prio3), M2, sort2, LeadTime=1日 → hours=(int)max(1,1*8)=8
    //     P_B: WO_LO(prio1), M1, sort1, LeadTime=null → hours=default2
    //   並び = [P_A, P_C, P_B]（WO_HI 群が先、群内は sort、最後に WO_LO）
    //   配置（号機別カーソル、初期=08:00）：
    //     P_A(M1): 08:00→10:00、M1 ptr=10:00
    //     P_C(M2): 08:00→16:00（8h）、M2 ptr=16:00
    //     P_B(M1): 10:00→12:00（M1 の続き）、M1 ptr=12:00
    //   changed=3。着手済 P_D(status1,M1) は対象外＝計画日時 null のまま。
    [Fact]
    public async Task AutoArrange_OrdersByPriorityDeliverySort_PacksPerMachine_SkipsStarted()
    {
        using var db = TestHelper.CreateInMemoryContext();
        db.Set<WorkOrder>().Add(new WorkOrder { WorkOrderNo = "WO_HI", ProductCd = "P1", Priority = 3, DeliveryDate = new DateTime(2026, 7, 20) });
        db.Set<WorkOrder>().Add(new WorkOrder { WorkOrderNo = "WO_LO", ProductCd = "P1", Priority = 1, DeliveryDate = new DateTime(2026, 7, 18) });

        var pA = Proc("WO_HI", "OP1", status: 0, machine: "M1", sort: 1);
        var pC = Proc("WO_HI", "OP2", status: 0, machine: "M2", sort: 2, leadTime: 1m);
        var pB = Proc("WO_LO", "OP1", status: 0, machine: "M1", sort: 1);
        var pD = Proc("WO_HI", "OP3", status: 1, machine: "M1", sort: 3); // 着手済 → 対象外
        db.Set<WorkOrderProcess>().AddRange(pA, pC, pB, pD);
        await db.SaveChangesAsync();

        var changed = await new PlanningBoardService(db).AutoArrangeAsync(
            new AutoArrangeRequest { BaseDate = new DateTime(2026, 7, 15), DefaultHoursPerJob = 2 }, "planner");

        Assert.Equal(3, changed);

        var gA = await db.Set<WorkOrderProcess>().AsNoTracking().SingleAsync(x => x.Id == pA.Id);
        var gB = await db.Set<WorkOrderProcess>().AsNoTracking().SingleAsync(x => x.Id == pB.Id);
        var gC = await db.Set<WorkOrderProcess>().AsNoTracking().SingleAsync(x => x.Id == pC.Id);
        var gD = await db.Set<WorkOrderProcess>().AsNoTracking().SingleAsync(x => x.Id == pD.Id);

        // P_A(M1): 08:00-10:00
        Assert.Equal(new DateTime(2026, 7, 15, 8, 0, 0), gA.PlanStartTime);
        Assert.Equal(new DateTime(2026, 7, 15, 10, 0, 0), gA.PlanEndTime);
        // P_C(M2): 08:00-16:00（LeadTime 1日=8h）
        Assert.Equal(new DateTime(2026, 7, 15, 8, 0, 0), gC.PlanStartTime);
        Assert.Equal(new DateTime(2026, 7, 15, 16, 0, 0), gC.PlanEndTime);
        // P_B(M1): 10:00-12:00（M1 で P_A の後に連続）
        Assert.Equal(new DateTime(2026, 7, 15, 10, 0, 0), gB.PlanStartTime);
        Assert.Equal(new DateTime(2026, 7, 15, 12, 0, 0), gB.PlanEndTime);
        // 着手済 P_D は不変（配置対象外）
        Assert.Null(gD.PlanStartTime);
        Assert.Null(gD.PlanEndTime);
    }
}
