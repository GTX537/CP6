### Task F-T1: FlowTriggerValidator 全量保存时校验（E-WF-022/023 双检之「保存侧」）

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowTriggerValidator.cs`（E-T1 最小版扩成 spec §5 全量）
- Test: `CP6.Tests/Wf/FlowTriggerValidatorTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/FlowTriggerValidatorTests.cs —— 基座同 A-T2（需 Sys_User/Wf_FlowDef seed）
using System;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Services.Wf;
using Xunit;
using static CP6.Tests.FlowTriggerTestHarness;

namespace CP6.Tests;

public class FlowTriggerValidatorTests
{
    private static FlowTriggerSaveReq Req(int type, Guid starter, string configJson,
        string flowKey = "fk-trig", string? eventKey = null)
        => new(flowKey, type, configJson, Enabled: true, eventKey, starter);

    private static async Task AssertThrowsCodeAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn, FlowTriggerSaveReq req, string code)
    {
        using var db = Ctx(conn);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => FlowTriggerValidator.ValidateAsync(db, req, CancellationToken.None));
        Assert.Contains(code, ex.Message);
    }

    [Fact]
    public async Task Timer_BadCron_EWF022()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        await AssertThrowsCodeAsync(conn,
            Req(WfTriggerType.Timer, starter, "{\"cron\":\"not a cron\"}"), "E-WF-022");
    }

    [Fact]
    public async Task Event_BadEventKeyFormat_EWF022()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        foreach (var badKey in new[] { "noSeparator", "|x", "x|", "", null })
            await AssertThrowsCodeAsync(conn,
                Req(WfTriggerType.Event, starter, "{}", eventKey: badKey), "E-WF-022");
    }

    [Fact]
    public async Task Event_BadVarsMapPath_EWF022()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        // 空模板值
        await AssertThrowsCodeAsync(conn,
            Req(WfTriggerType.Event, starter, "{\"varsMap\":{\"a\":\"\"}}", eventKey: "WMS|OnXAsync"), "E-WF-022");
        // 空变量名
        await AssertThrowsCodeAsync(conn,
            Req(WfTriggerType.Event, starter, "{\"varsMap\":{\"\":\"$.x\"}}", eventKey: "WMS|OnXAsync"), "E-WF-022");
    }

    [Fact]
    public async Task Starter_MissingOrDisabled_EWF022()
    {
        using var conn = NewSqliteWithSchema();
        await SeedFlowAndUsersAsync(conn);                              // 流程 enabled
        // 不存在的发起人
        await AssertThrowsCodeAsync(conn,
            Req(WfTriggerType.Timer, Guid.NewGuid(), "{\"cron\":\"0 9 * * *\"}"), "E-WF-022");
        // 停用的发起人（独立库避免 flowKey 撞车）
        using var conn2 = NewSqliteWithSchema();
        var (disabledStarter, _) = await SeedFlowAndUsersAsync(conn2, starterEnabled: false);
        await AssertThrowsCodeAsync(conn2,
            Req(WfTriggerType.Timer, disabledStarter, "{\"cron\":\"0 9 * * *\"}"), "E-WF-022");
    }

    [Fact]
    public async Task Flow_MissingOrDisabled_EWF023()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        // FlowKey 不存在
        await AssertThrowsCodeAsync(conn,
            Req(WfTriggerType.Timer, starter, "{\"cron\":\"0 9 * * *\"}", flowKey: "nope"), "E-WF-023");
        // FlowKey 存在但停用
        using var conn2 = NewSqliteWithSchema();
        var (starter2, _) = await SeedFlowAndUsersAsync(conn2, flowEnabled: false);
        await AssertThrowsCodeAsync(conn2,
            Req(WfTriggerType.Timer, starter2, "{\"cron\":\"0 9 * * *\"}"), "E-WF-023");
    }

    [Fact]
    public async Task Timer_BadVarsJson_EWF022()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        await AssertThrowsCodeAsync(conn,
            Req(WfTriggerType.Timer, starter, "{\"cron\":\"0 9 * * *\",\"varsJson\":\"not-json\"}"), "E-WF-022");
    }

    [Fact]
    public async Task ValidThreeTypes_Pass()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        // 三型合法配置全过（不抛）
        await FlowTriggerValidator.ValidateAsync(db,
            Req(WfTriggerType.Timer, starter, "{\"cron\":\"0 9 * * *\",\"varsJson\":\"{\\\"a\\\":1}\"}"),
            CancellationToken.None);
        await FlowTriggerValidator.ValidateAsync(db,
            Req(WfTriggerType.Event, starter, "{\"varsMap\":{\"orderNo\":\"$.OutboundNo\"}}",
                eventKey: "WMS|OnShipmentConfirmedAsync"),
            CancellationToken.None);
        await FlowTriggerValidator.ValidateAsync(db,
            Req(WfTriggerType.Message, starter, "{\"varsSchema\":[\"orderNo\",\"amount\"]}"),
            CancellationToken.None);
    }
}
```

- [ ] **Step 2: 跑验证 FAIL**（`--filter FlowTriggerValidatorTests`）。

- [ ] **Step 3: 实现**（替换 E-T1 最小版方法体；签名不变，E-T1 调用点零改动）

```csharp
// CP6.Core/Services/Wf/FlowTriggerValidator.cs（全量版）
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>触发器保存时校验（spec §5 E-WF-022/023 的保存侧；运行时侧在 FireAsync，双检——
/// 发起人/流程可能在保存后被停用）。失败抛 InvalidOperationException("E-WF-0xx: ...")（对齐引擎错误码风格）。</summary>
public static class FlowTriggerValidator
{
    private static readonly Regex EventKeyPattern = new(@"^[A-Za-z0-9_.-]+\|[A-Za-z0-9_.-]+$", RegexOptions.Compiled);

    public static async Task ValidateAsync(CP6Context db, FlowTriggerSaveReq req, CancellationToken ct)
    {
        // ── 通用 ──
        if (string.IsNullOrWhiteSpace(req.FlowKey))
            throw new InvalidOperationException("E-WF-023: FlowKey 必填");
        if (req.TriggerType is < WfTriggerType.Timer or > WfTriggerType.Message)
            throw new InvalidOperationException("E-WF-022: 触发器类型非法");
        if (req.StarterUserId == Guid.Empty)
            throw new InvalidOperationException("E-WF-022: StarterUserId 必填");

        // ── 分型（spec §2.3）──
        switch (req.TriggerType)
        {
            case WfTriggerType.Timer:
            {
                var cfg = WfTriggerConfig.ParseTimer(req.ConfigJson);
                if (!WfCronHelper.IsValid(cfg.Cron))
                    throw new InvalidOperationException("E-WF-022: cron 解析失败（NCrontab 标准 5 段）");
                if (!string.IsNullOrWhiteSpace(cfg.VarsJson) && !IsJsonObject(cfg.VarsJson))
                    throw new InvalidOperationException("E-WF-022: varsJson 须为 JSON 对象");
                break;
            }
            case WfTriggerType.Event:
            {
                if (string.IsNullOrWhiteSpace(req.EventKey) || !EventKeyPattern.IsMatch(req.EventKey))
                    throw new InvalidOperationException("E-WF-022: eventKey 格式错（应为 \"{SourceModule}|{HookName}\"）");
                var cfg = WfTriggerConfig.ParseEvent(req.ConfigJson);
                foreach (var (k, v) in cfg.VarsMap ?? new())
                {
                    if (string.IsNullOrWhiteSpace(k))
                        throw new InvalidOperationException("E-WF-022: varsMap 变量名不能为空");
                    if (string.IsNullOrWhiteSpace(v))
                        throw new InvalidOperationException($"E-WF-022: varsMap[{k}] 点路径/模板不能为空");
                }
                break;
            }
            case WfTriggerType.Message:
            {
                var cfg = WfTriggerConfig.ParseMessage(req.ConfigJson);
                if (cfg.VarsSchema != null && cfg.VarsSchema.Any(string.IsNullOrWhiteSpace))
                    throw new InvalidOperationException("E-WF-022: varsSchema 含空字段名");
                break;
            }
        }

        // ── 引用存在性（保存侧）──
        var starterOk = await db.Sys_Users.AnyAsync(u => u.Id == req.StarterUserId && u.Enable, ct);
        if (!starterOk) throw new InvalidOperationException("E-WF-022: StarterUserId 不存在或已停用");
        var flowOk = await db.Wf_FlowDefs.AnyAsync(d => d.FlowKey == req.FlowKey && d.Enable, ct);
        if (!flowOk) throw new InvalidOperationException("E-WF-023: 目标流程不存在或未启用");
    }

    private static bool IsJsonObject(string s)
    {
        try { using var d = JsonDocument.Parse(s); return d.RootElement.ValueKind == JsonValueKind.Object; }
        catch (JsonException) { return false; }
    }
}
```

- [ ] **Step 4: 跑验证 PASS + Admin/Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FlowTriggerValidatorTests|FlowTriggerAdminTests"
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-trigger): F-T1 FlowTriggerValidator 保存时全量校验 E-WF-022/023(cron/eventKey/varsMap/starter/flow)"
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

