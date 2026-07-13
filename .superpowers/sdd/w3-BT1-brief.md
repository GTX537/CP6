### Task B-T1: NCrontab 引包 + WfCronHelper（时区口径 + 预览）

**Files:**
- Modify: `CP6.Core/CP6.Core.csproj`（引包）
- Create: `CP6.Core/Services/Wf/WfCronHelper.cs`
- Test: `CP6.Tests/Wf/WfCronHelperTests.cs`

- [ ] **Step 1: 引包**（非 CPM，内联版本；NCrontab 3.3.3 = 最新稳定，MIT，单包无传递依赖——过依赖审查记录于 commit body）：

```xml
<!-- CP6.Core/CP6.Core.csproj 既有 ItemGroup 内追加（按字母序插在 Microsoft.* 前后合适位置） -->
<PackageReference Include="NCrontab" Version="3.3.3" />
```

`dotnet restore CP6.Core/CP6.Core.csproj` 确认拉包成功；若私有源无此版本，用 `dotnet package search NCrontab` 核实可用最新 3.x 并在 commit message 记录实际版本。

- [ ] **Step 2: 写失败测试**

```csharp
// CP6.Tests/Wf/WfCronHelperTests.cs
using System;
using CP6.Core.Services.Wf;
using Xunit;

public class WfCronHelperTests
{
    [Fact]
    public void IsValid_AcceptsStandard5Field_RejectsGarbage()
    {
        Assert.True(WfCronHelper.IsValid("0 0 25 * *"));
        Assert.True(WfCronHelper.IsValid("*/5 * * * *"));
        Assert.False(WfCronHelper.IsValid("not a cron"));
        Assert.False(WfCronHelper.IsValid(""));
        Assert.False(WfCronHelper.IsValid(null));
        Assert.False(WfCronHelper.IsValid("0 0 25 * * ?"));   // 6 段 Quartz 风格拒绝
    }

    [Fact]
    public void NextUtc_IsStrictlyFuture()
    {
        var after = DateTime.UtcNow;
        var next = WfCronHelper.NextUtc("*/5 * * * *", after);
        Assert.NotNull(next);
        Assert.True(next > after);
        Assert.Equal(DateTimeKind.Utc, next!.Value.Kind);
    }

    [Fact]
    public void NextUtc_BadCron_ReturnsNull()
    {
        Assert.Null(WfCronHelper.NextUtc("garbage", DateTime.UtcNow));
    }

    [Fact]
    public void NextUtc_Day31_SkipsShortMonths()
    {
        // 2026-04-01（4 月无 31 日）→ 下一次 "0 0 31 * *" 应落在 5 月 31 日（NCrontab 跳过无效日期）
        var april = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var next = WfCronHelper.NextUtc("0 0 31 * *", april)!.Value;
        var local = TimeZoneInfo.ConvertTimeFromUtc(next, TimeZoneInfo.Local);
        Assert.Equal(5, local.Month);
        Assert.Equal(31, local.Day);
    }

    [Fact]
    public void NextUtc_Feb29_OnlyLeapYear()
    {
        // 2026 非闰年 → "0 0 29 2 *" 下一次落 2028-02-29
        var start = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var next = WfCronHelper.NextUtc("0 0 29 2 *", start)!.Value;
        var local = TimeZoneInfo.ConvertTimeFromUtc(next, TimeZoneInfo.Local);
        Assert.Equal(2028, local.Year);
    }

    [Fact]
    public void PreviewUtc_ReturnsAscending_NCount()
    {
        var list = WfCronHelper.PreviewUtc("0 9 * * *", DateTime.UtcNow, 5);
        Assert.Equal(5, list.Count);
        for (var i = 1; i < list.Count; i++) Assert.True(list[i] > list[i - 1]);
    }
}
```

- [ ] **Step 3: 跑验证 FAIL**（`--filter WfCronHelperTests`）。

- [ ] **Step 4: 实现**

```csharp
// CP6.Core/Services/Wf/WfCronHelper.cs
using NCrontab;

namespace CP6.Core.Services.Wf;

/// <summary>NCrontab 包装（D3）。cron 5 段标准，按 app 默认时区解释（spec §9 一期口径，UI 文案标注时区），
/// 存储/比较一律 UTC。无 L 语义（映射表③：「每月末」预设按 28 日近似）。</summary>
public static class WfCronHelper
{
    public static bool IsValid(string? cron)
        => !string.IsNullOrWhiteSpace(cron) && CrontabSchedule.TryParse(cron) != null;

    /// <summary>afterUtc 之后（严格未来）的下一次到期（UTC）；cron 非法返回 null。
    /// 从「当前时刻」起算即天然实现 misfire 口径：宕机跨过的历史到期点直接跳过（spec §3.2）。</summary>
    public static DateTime? NextUtc(string cron, DateTime afterUtc)
    {
        var sched = CrontabSchedule.TryParse(cron);
        if (sched == null) return null;
        var afterLocal = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc), TimeZoneInfo.Local);
        var nextLocal = sched.GetNextOccurrence(afterLocal);
        return TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(nextLocal, DateTimeKind.Unspecified), TimeZoneInfo.Local);
    }

    /// <summary>fromUtc 起未来 count 次到期（UTC 升序）——管理页「下次触发时间预览」用。</summary>
    public static IReadOnlyList<DateTime> PreviewUtc(string cron, DateTime fromUtc, int count)
    {
        var list = new List<DateTime>(count);
        var cursor = fromUtc;
        for (var i = 0; i < count; i++)
        {
            var next = NextUtc(cron, cursor);
            if (next == null) break;
            list.Add(next.Value);
            cursor = next.Value;
        }
        return list;
    }
}
```

- [ ] **Step 5: 跑验证 PASS + Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter WfCronHelperTests
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-trigger): B-T1 NCrontab 3.3.3 引包+WfCronHelper 时区口径/严格未来/预览"
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


## 附: 映射③cron月末口径
| ③ | UI 预设「每月末」（§4） | NCrontab 标准 5 段**无 `L` 语义**。预设「每月末」落 `0 0 28 * *` 并在 UI 文案注明「按每月 28 日近似」；真月末与工作日口径同列 spec §9 留后条目。cron 边界测试用「每月 31 日只在大月发」「2/29 只闰年发」验证 NCrontab 行为。 |
