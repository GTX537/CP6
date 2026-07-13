// CP6.Tests/Wf/FlowTriggerTimerScanTests.cs —— 全部注入 nowUtc 确定性（基座见 FlowTriggerTestHarness.cs）
using System;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static CP6.Tests.FlowTriggerTestHarness;

namespace CP6.Tests;

public class FlowTriggerTimerScanTests
{
    private const string DailyCron = "{\"cron\":\"0 9 * * *\"}";

    [Fact]
    public async Task DueTimer_Fires_AdvancesNextDue_WritesFire()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        var nowUtc = DateTime.UtcNow;
        var t0 = nowUtc.AddMinutes(-1);                    // 已到期
        using var db = Ctx(conn);
        var trig = NewTrigger("fk-trig", WfTriggerType.Timer, starter, configJson: DailyCron);
        trig.NextDueUtc = t0;
        db.Wf_FlowTriggers.Add(trig);
        await db.SaveChangesAsync();

        var n = await Service(db).ScanTimersOnceAsync(nowUtc, CancellationToken.None);

        Assert.Equal(1, n);
        Assert.Equal(1, await db.Wf_FlowInstances.CountAsync());
        var fire = await db.Wf_TriggerFires.AsNoTracking().SingleAsync();
        Assert.Equal($"{trig.Id}:{t0:O}", fire.IdempotencyKey);   // 幂等键 = 旧 NextDueUtc（spec §2.2）
        Assert.NotNull(fire.InstanceId);
        Assert.Equal(WfTriggerType.Timer, fire.Source);
        var after = await db.Wf_FlowTriggers.AsNoTracking().SingleAsync();
        Assert.True(after.NextDueUtc > nowUtc);            // 严格未来
        Assert.NotNull(after.LastFiredUtc);
    }

    [Fact]
    public async Task NotDue_Skipped()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        var nowUtc = DateTime.UtcNow;
        using var db = Ctx(conn);
        var trig = NewTrigger("fk-trig", WfTriggerType.Timer, starter, configJson: DailyCron);
        trig.NextDueUtc = nowUtc.AddHours(1);              // 未到期
        db.Wf_FlowTriggers.Add(trig);
        await db.SaveChangesAsync();

        var n = await Service(db).ScanTimersOnceAsync(nowUtc, CancellationToken.None);

        Assert.Equal(0, n);
        Assert.Equal(0, await db.Wf_TriggerFires.CountAsync());
    }

    [Fact]
    public async Task Disabled_Or_NonTimer_Skipped()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        var nowUtc = DateTime.UtcNow;
        using var db = Ctx(conn);
        var disabledTimer = NewTrigger("fk-trig", WfTriggerType.Timer, starter, enabled: false, configJson: DailyCron);
        disabledTimer.NextDueUtc = nowUtc.AddMinutes(-1);
        var enabledEvent = NewTrigger("fk-trig", WfTriggerType.Event, starter, eventKey: "QA|OnEchoAsync");
        enabledEvent.NextDueUtc = nowUtc.AddMinutes(-1);   // 即使误填 NextDueUtc，类型过滤也须挡住
        db.Wf_FlowTriggers.AddRange(disabledTimer, enabledEvent);
        await db.SaveChangesAsync();

        var n = await Service(db).ScanTimersOnceAsync(nowUtc, CancellationToken.None);

        Assert.Equal(0, n);
        Assert.Equal(0, await db.Wf_TriggerFires.CountAsync());
    }

    [Fact]
    public async Task TwoWorkers_SameDue_FiresExactlyOnce()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        var nowUtc = DateTime.UtcNow;
        using (var seed = Ctx(conn))
        {
            var trig = NewTrigger("fk-trig", WfTriggerType.Timer, starter, configJson: DailyCron);
            trig.NextDueUtc = nowUtc.AddMinutes(-1);
            seed.Wf_FlowTriggers.Add(trig);
            await seed.SaveChangesAsync();
        }

        // 脏读窗口（照 FlowConcurrencyTests 口径）：dbB 先把触发器读进 identity-map 锁旧 RowVersion，
        // 等价于两 worker 近同时扫到同一到期行。
        using var dbA = Ctx(conn);
        using var dbB = Ctx(conn);
        await dbB.Wf_FlowTriggers.FirstAsync();

        var nA = await Service(dbA).ScanTimersOnceAsync(nowUtc, CancellationToken.None);   // A 抢占并完成
        var nB = await Service(dbB).ScanTimersOnceAsync(nowUtc, CancellationToken.None);   // B 第一段撞 RowVersion/占坑唯一键 → 让位

        Assert.Equal(1, nA);
        using var check = Ctx(conn);
        Assert.Equal(1, await check.Wf_FlowInstances.CountAsync());      // 只发一次（spec §8）
        Assert.Equal(1, await check.Wf_TriggerFires.CountAsync());
    }

    [Fact]
    public async Task CrashBetweenPhases_RecoveryBackfills_NoLoss_NoDouble()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        var nowUtc = DateTime.UtcNow;
        var oldDue = nowUtc.AddMinutes(-10);
        using var db = Ctx(conn);
        // 手工模拟第一段已提交、第二段崩溃：NextDueUtc 已前移到未来 + 占坑行留存
        var trig = NewTrigger("fk-trig", WfTriggerType.Timer, starter, configJson: DailyCron);
        trig.NextDueUtc = nowUtc.AddHours(20);
        db.Wf_FlowTriggers.Add(trig);
        db.Wf_TriggerFires.Add(new Wf_TriggerFire
        {
            TriggerId = trig.Id, IdempotencyKey = $"{trig.Id}:{oldDue:O}",
            FiredUtc = nowUtc.AddMinutes(-3),              // 宽限期（2min）之外
            Source = WfTriggerType.Timer,
        });
        await db.SaveChangesAsync();

        var n = await Service(db).ScanTimersOnceAsync(nowUtc, CancellationToken.None);

        Assert.Equal(1, n);                                // 补跑恰一次
        var fire = await db.Wf_TriggerFires.AsNoTracking().SingleAsync();
        Assert.NotNull(fire.InstanceId);                   // 不丢发
        Assert.Equal(1, await db.Wf_FlowInstances.CountAsync());   // 不双发
        Assert.Equal(1, await db.Wf_TriggerFires.CountAsync());
    }

    [Fact]
    public async Task RecoveryGrace_NotYetElapsed_SlotUntouched()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        var nowUtc = DateTime.UtcNow;
        using var db = Ctx(conn);
        var trig = NewTrigger("fk-trig", WfTriggerType.Timer, starter, configJson: DailyCron);
        trig.NextDueUtc = nowUtc.AddHours(20);
        db.Wf_FlowTriggers.Add(trig);
        db.Wf_TriggerFires.Add(new Wf_TriggerFire
        {
            TriggerId = trig.Id, IdempotencyKey = $"{trig.Id}:{nowUtc.AddMinutes(-1):O}",
            FiredUtc = nowUtc.AddSeconds(-30),             // 宽限期内：第二段可能正在进行
            Source = WfTriggerType.Timer,
        });
        await db.SaveChangesAsync();

        var n = await Service(db).ScanTimersOnceAsync(nowUtc, CancellationToken.None);

        Assert.Equal(0, n);
        Assert.Null((await db.Wf_TriggerFires.AsNoTracking().SingleAsync()).InstanceId);   // 不抢跑
        Assert.Equal(0, await db.Wf_FlowInstances.CountAsync());
    }

    [Fact]
    public async Task Misfire_MultipleMissedDue_OnlyLatestFired()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        var nowUtc = DateTime.UtcNow;
        var staleDue = nowUtc.AddDays(-3);                 // 宕机跨过 ≥3 个每日到期点
        using var db = Ctx(conn);
        var trig = NewTrigger("fk-trig", WfTriggerType.Timer, starter, configJson: DailyCron);
        trig.NextDueUtc = staleDue;
        db.Wf_FlowTriggers.Add(trig);
        await db.SaveChangesAsync();
        var svc = Service(db);

        var n1 = await svc.ScanTimersOnceAsync(nowUtc, CancellationToken.None);

        Assert.Equal(1, n1);                               // 只补最近一次
        Assert.Equal(1, await db.Wf_FlowInstances.CountAsync());
        var fire = await db.Wf_TriggerFires.AsNoTracking().SingleAsync();
        Assert.Equal($"{trig.Id}:{staleDue:O}", fire.IdempotencyKey);
        var after = await db.Wf_FlowTriggers.AsNoTracking().SingleAsync();
        Assert.True(after.NextDueUtc > nowUtc);            // 直推未来，不追历史（spec §3.2）

        var n2 = await svc.ScanTimersOnceAsync(nowUtc, CancellationToken.None);
        Assert.Equal(0, n2);                               // 同一 nowUtc 再扫零动作
        Assert.Equal(1, await db.Wf_FlowInstances.CountAsync());
    }

    [Fact]
    public async Task BadCron_MarksError_DoesNotSpin()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        var nowUtc = DateTime.UtcNow;
        using var db = Ctx(conn);
        var trig = NewTrigger("fk-trig", WfTriggerType.Timer, starter,
                              configJson: "{\"cron\":\"not a cron\"}");   // 保存后被改坏的兜底
        trig.NextDueUtc = nowUtc.AddMinutes(-1);
        db.Wf_FlowTriggers.Add(trig);
        await db.SaveChangesAsync();
        var svc = Service(db);

        var n1 = await svc.ScanTimersOnceAsync(nowUtc, CancellationToken.None);

        Assert.Equal(1, n1);                               // 计入处理（记错）
        var fire = await db.Wf_TriggerFires.AsNoTracking().SingleAsync();
        Assert.Contains("E-WF-022", fire.Error);
        Assert.Null(fire.InstanceId);
        Assert.Null((await db.Wf_FlowTriggers.AsNoTracking().SingleAsync()).NextDueUtc);  // 停摆等人工修复
        Assert.Equal(0, await db.Wf_FlowInstances.CountAsync());

        var n2 = await svc.ScanTimersOnceAsync(nowUtc.AddMinutes(5), CancellationToken.None);
        Assert.Equal(0, n2);                               // 不无限重扫
    }
}
