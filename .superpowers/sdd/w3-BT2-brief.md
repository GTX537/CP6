### Task B-T2: `ScanTimersOnceAsync` 占坑两段式（不双发不丢发 + 补跑 + misfire 只补最近）

> **spec §3.2 全文落点，timer 正确性核心。** 第一段单事务（SaveChanges 原子）＝「NextDueUtc 前移 + INSERT 占坑行」，写回成功者获得发火权；第二段 FireAsync 补跑回填。两段之间崩溃 → 占坑行留存 → 每轮补跑扫描兜底。

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowTriggerService.cs`（实现 `ScanTimersOnceAsync(DateTime, CancellationToken)`）
- Test: `CP6.Tests/Wf/FlowTriggerTimerScanTests.cs`

- [ ] **Step 1: 写失败测试**（harness 同 A-T2；`Wf_FlowTrigger` rowversion 触发器必须已建）

```csharp
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
```

- [ ] **Step 2: 跑验证 FAIL**（`--filter FlowTriggerTimerScanTests`）。

- [ ] **Step 3: 实现** — 替换 A-T2 的 `NotImplementedException` 占位实现：

```csharp
public async Task<int> ScanTimersOnceAsync(DateTime nowUtc, CancellationToken ct)
{
    var processed = 0;

    // ── ① 补跑扫描（spec §3.2 崩溃恢复）：宽限期外仍未完成的占坑行 → 补第二段 ──
    var staleIds = await _db.Wf_TriggerFires
        .Where(f => f.Source == WfTriggerType.Timer && f.InstanceId == null && f.Error == null
                    && f.FiredUtc < nowUtc - RecoveryGrace)
        .OrderBy(f => f.FiredUtc)
        .Take(BatchSize)
        .Select(f => f.Id)
        .ToListAsync(ct);
    foreach (var fireId in staleIds)
    {
        ct.ThrowIfCancellationRequested();
        // 每条重查（FireAsync 失败路径 ChangeTracker.Clear 会使批量实体失联——调用方契约）
        var fire = await _db.Wf_TriggerFires.FirstOrDefaultAsync(f => f.Id == fireId, ct);
        if (fire == null || fire.InstanceId != null || fire.Error != null) continue;   // 已被别人完成
        var trig = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(t => t.Id == fire.TriggerId, ct);
        if (trig == null) continue;
        var cfg = WfTriggerConfig.ParseTimer(trig.ConfigJson);
        await FireAsync(trig, cfg.VarsJson, WfTriggerType.Timer, fire.IdempotencyKey, ct);
        processed++;
    }

    // ── ② 到期扫描 + 占坑两段式 ──
    var dueIds = await _db.Wf_FlowTriggers
        .Where(t => t.Enabled && t.TriggerType == WfTriggerType.Timer
                    && t.NextDueUtc != null && t.NextDueUtc <= nowUtc)
        .OrderBy(t => t.NextDueUtc)
        .Take(BatchSize)
        .Select(t => t.Id)
        .ToListAsync(ct);
    foreach (var id in dueIds)
    {
        ct.ThrowIfCancellationRequested();
        var trig = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(t => t.Id == id, ct);   // 逐条重查（同上契约）
        if (trig == null || !trig.Enabled || trig.NextDueUtc == null || trig.NextDueUtc > nowUtc) continue;

        var dueUtc = trig.NextDueUtc.Value;
        var key = $"{trig.Id}:{dueUtc:O}";
        var cfg = WfTriggerConfig.ParseTimer(trig.ConfigJson);

        // 第一段：抢占 + 占坑，单 SaveChanges（隐式单事务）＝「NextDueUtc 前移 + INSERT 占坑行」原子提交。
        // misfire：NextUtc 从 nowUtc 起算严格未来下一个 → 跨过的历史到期点只补最近（本次），不追积压（spec §3.2）。
        var next = WfCronHelper.NextUtc(cfg.Cron, nowUtc);
        var fire = new Wf_TriggerFire
        {
            TriggerId = trig.Id, IdempotencyKey = key,
            FiredUtc = nowUtc, Source = WfTriggerType.Timer,
        };
        if (next == null)
        {
            // 保存后被改坏的 cron：停摆 + 记错（不占坑发起，不无限重扫）
            trig.NextDueUtc = null;
            fire.Error = "E-WF-022: cron 解析失败";
            _db.Wf_TriggerFires.Add(fire);
            await _db.SaveChangesAsync(ct);
            processed++;
            continue;
        }
        trig.NextDueUtc = next;
        _db.Wf_TriggerFires.Add(fire);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)   // 含 DbUpdateConcurrencyException：RowVersion 被抢 / 占坑撞唯一键 → 让位
        {
            _db.Entry(fire).State = EntityState.Detached;
            _db.Entry(trig).State = EntityState.Detached;
            continue;
        }

        // 第二段：完成（FireAsync 复用占坑行回填 InstanceId/Error；两半各自幂等）
        await FireAsync(trig, cfg.VarsJson, WfTriggerType.Timer, key, ct);
        processed++;
    }

    return processed;
}
```

- [ ] **Step 4: 跑验证 PASS**（8 测全绿）。
- [ ] **Step 5: Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-trigger): B-T2 ScanTimersOnceAsync 占坑两段式(不双发不丢发)+补跑扫描+misfire 只补最近"
```

---


---
## 附: 共享契约(plan全局)
## 共享契约（所有 Task 用这些**精确**名字，前后一致）

- `WfTriggerType`：`Timer=0 / Event=1 / Message=2`（int 常量，`WfStatus.cs`）。
- 实体字段：`Wf_FlowTrigger { FlowKey, TriggerType, ConfigJson, Enabled, EventKey, StarterUserId, NextDueUtc, LastFiredUtc, ApiKeyHash, RowVersion }`；`Wf_TriggerFire { TriggerId, IdempotencyKey, FiredUtc, InstanceId, Source, Error, PayloadHash }`（均继承 BaseTenantEntity）。
- `TriggerFireResult { bool Success; bool Replayed; Guid? InstanceId; string? Error; static Ok(Guid, bool replayed=false); static Fail(string); }`
- `IFlowTriggerService`（spec §3.1 逐字）：
  - `Task<TriggerFireResult> FireAsync(Wf_FlowTrigger trigger, string? varsJson, int source, string idempotencyKey, CancellationToken ct);`
  - `Task<int> ScanTimersOnceAsync(CancellationToken ct);`（实现类测试重载 `ScanTimersOnceAsync(DateTime nowUtc, CancellationToken ct)`）
- 幂等键口径（spec §2.2）：timer=`$"{trigger.Id}:{dueUtc:O}"`；event=`$"{eventId}:{trigger.Id}"`；message=`Idempotency-Key` 头；手动试发=`$"manual:{Guid.NewGuid():N}"`。
- `WfCronHelper { static bool IsValid(string?); static DateTime? NextUtc(string cron, DateTime afterUtc); static IReadOnlyList<DateTime> PreviewUtc(string cron, DateTime fromUtc, int count); }`
- `IWfTriggerBridgeHook`：
  - `Task<WfTriggerBridgeResult> OnEventAsync(string eventKey, string eventId, string payloadJson, string? userName);`（业务入口，写 outbox 台账）
  - `Task<WfTriggerBridgeResult> ReplayEventAsync(string eventKey, string eventId, string payloadJson, string? userName);`（dispatcher 重放入口，不再写新 outbox 行）
- `WfTriggerBridgeResult { bool Success; int MatchedCount; int FiredCount; string? Message; static Ok(int matched, int fired); static Skipped(string); static Failed(string); }`
- `WfTriggerEventPayload(string EventKey, string EventId, string PayloadJson, string? UserName)`（record，outbox 负载契约）。
- `WfTriggerVarsMapper { static string MapVars(Dictionary<string,string>? varsMap, string payloadJson); static string FilterBySchema(string bodyJson, IReadOnlyList<string>? schema); }`
- `WfApiKeyHelper { static string NewRawKey(); static string HashOf(string raw); static bool Verify(string raw, string? storedHash); }`
- `WfTriggerConfig`：`ParseTimer(string)→WfTimerTriggerConfig{Cron,VarsJson}` / `ParseEvent(string)→WfEventTriggerConfig{VarsMap}` / `ParseMessage(string)→WfMessageTriggerConfig{VarsSchema}`。
- 常量（`FlowTriggerService`）：`RecoveryGrace = TimeSpan.FromMinutes(2)`（补跑宽限）、`BatchSize = 50`、`Trunc` 截 1000。
- 错误码：`E-WF-022`（配置无效：cron/eventKey/varsMap/StarterUserId）/ `E-WF-023`（目标流程不可发起）/ `E-WF-024`（运行时发起失败，写 TriggerFire.Error）。message 端点 401/404/400 走 HTTP 语义不占 E-WF 码。
- FireAsync 撞键语义（spec §3.1 引申，全计划统一）：既有行 `InstanceId != null` → `Ok(instanceId, replayed:true)`（幂等成功非错误）；既有行 `InstanceId == null`（占坑未完成**或**上次失败）→ 补跑第二段（成功回填并清 Error / 失败覆写 Error）。timer 补跑扫描只捡 `Error==null` 的占坑行（spec §3.2 原文）；Error 行的重试机会来自 event outbox 重放与 message 客户端重试。

