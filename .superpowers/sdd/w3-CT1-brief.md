### Task C-T1: IWfTriggerBridgeHook + WfTriggerBridgeHook + varsMap 映射 + DI

> **D4 落点。** eventId 必填入口定键（spec §2.2：不能用 outbox 行 Id——成功路径无键可用，且部分成功须按触发器粒度去重）。

**Files:**
- Create: `CP6.Core/Services/Integration/IWfTriggerBridgeHook.cs`（接口 + Result + Payload record + NoOp，同文件仿 `IMesBridgeHook.cs`）
- Create: `CP6.Core/Services/Wf/WfTriggerVarsMapper.cs`
- Create: `CP6.Core/Services/Wf/WfTriggerBridgeHook.cs`
- Modify: `CP6.WebApi/Program.cs`（DI）
- Test: `CP6.Tests/Wf/WfTriggerVarsMapperTests.cs`、`CP6.Tests/Wf/WfTriggerBridgeHookTests.cs`

- [ ] **Step 1: 写失败测试（varsMap 纯逻辑）**

```csharp
// CP6.Tests/Wf/WfTriggerVarsMapperTests.cs
using CP6.Core.Services.Wf;
using Xunit;

public class WfTriggerVarsMapperTests
{
    [Fact]
    public void MapVars_DotPath_And_Literal()
    {
        var payload = "{\"OutboundNo\":\"OB-9\",\"detail\":{\"qty\":3}}";
        var map = new Dictionary<string, string> { ["orderNo"] = "$.OutboundNo", ["qty"] = "$.detail.qty", ["src"] = "wms" };
        var vars = WfTriggerVarsMapper.MapVars(map, payload);
        Assert.Contains("\"orderNo\":\"OB-9\"", vars);
        Assert.Contains("\"qty\":\"3\"", vars);      // ServiceVarsHelper 口径：值统一字符串（已记档限制）
        Assert.Contains("\"src\":\"wms\"", vars);
    }

    [Fact]
    public void MapVars_MissingPath_EmptyString()
    {
        var vars = WfTriggerVarsMapper.MapVars(new() { ["x"] = "$.nope" }, "{}");
        Assert.Contains("\"x\":\"\"", vars);
    }

    [Fact]
    public void MapVars_NullOrEmptyMap_EmptyVars_NoPassthrough()
    {
        // 无 varsMap 不透传原负载（防变量注入，与 message 白名单同哲学）
        Assert.Equal("{}", WfTriggerVarsMapper.MapVars(null, "{\"a\":1}"));
        Assert.Equal("{}", WfTriggerVarsMapper.MapVars(new(), "{\"a\":1}"));
    }

    [Fact]
    public void FilterBySchema_KeepsWhitelisted_DropsRest()
    {
        var vars = WfTriggerVarsMapper.FilterBySchema("{\"orderNo\":\"PO-1\",\"amount\":5,\"evil\":\"x\"}",
                                                      new[] { "orderNo", "amount" });
        Assert.Contains("\"orderNo\":\"PO-1\"", vars);
        Assert.Contains("\"amount\":5", vars);
        Assert.DoesNotContain("evil", vars);
    }

    [Fact]
    public void FilterBySchema_NullSchema_DropsAll()
    {
        Assert.Equal("{}", WfTriggerVarsMapper.FilterBySchema("{\"a\":1}", null));
    }

    [Fact]
    public void FilterBySchema_NonObjectBody_Throws()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(
            () => WfTriggerVarsMapper.FilterBySchema("[1,2]", new[] { "a" }));
    }
}
```

- [ ] **Step 2: 写失败测试（hook 行为，SQLite + 真 FireAsync）**

```csharp
// CP6.Tests/Wf/WfTriggerBridgeHookTests.cs —— 基座同 A-T2；hook 用真 FlowTriggerService 构造
using System;
using System.Threading.Tasks;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static CP6.Tests.FlowTriggerTestHarness;

namespace CP6.Tests;

public class WfTriggerBridgeHookTests
{
    private const string EventKey = "WMS|OnShipmentConfirmedAsync";

    private static WfTriggerBridgeHook Hook(CP6Context db)
        => new(db, Service(db), NullLogger<WfTriggerBridgeHook>.Instance);

    [Fact]
    public async Task OnEvent_MatchesMany_FiresEach_WithPerTriggerKey()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        for (var i = 0; i < 3; i++)
            db.Wf_FlowTriggers.Add(NewTrigger("fk-trig", WfTriggerType.Event, starter, eventKey: EventKey));
        await db.SaveChangesAsync();

        var r = await Hook(db).OnEventAsync(EventKey, "EV-1", "{}", "u");

        Assert.True(r.Success);
        Assert.Equal(3, r.MatchedCount);
        Assert.Equal(3, r.FiredCount);
        Assert.Equal(3, await db.Wf_FlowInstances.CountAsync());
        var fires = await db.Wf_TriggerFires.AsNoTracking().ToListAsync();
        Assert.Equal(3, fires.Count);
        foreach (var f in fires)
            Assert.Equal($"EV-1:{f.TriggerId}", f.IdempotencyKey);   // 触发器粒度幂等键（spec §2.2）
        var evt = await db.IntegrationEvents.AsNoTracking().SingleAsync();   // outbox 台账恰 1 行
        Assert.Equal(IntegrationEventStatus.Success, evt.Status);
        Assert.Equal("WF", evt.TargetModule);
        Assert.Equal("WMS", evt.SourceModule);
    }

    [Fact]
    public async Task OnEvent_NoMatch_ZeroAction_SkippedRow()
    {
        using var conn = NewSqliteWithSchema();
        await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);

        var r = await Hook(db).OnEventAsync("MES|OnNobodyListensAsync", "EV-2", "{}", null);

        Assert.True(r.Success);                            // 未匹配零动作不是错误（spec §8）
        Assert.Equal(0, r.MatchedCount);
        Assert.Equal(0, r.FiredCount);
        Assert.Equal(0, await db.Wf_FlowInstances.CountAsync());
        var evt = await db.IntegrationEvents.AsNoTracking().SingleAsync();   // 审计 Skipped 行
        Assert.Equal(IntegrationEventStatus.Skipped, evt.Status);
    }

    [Fact]
    public async Task OnEvent_MissingEventId_Failed_NoOutbox()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        db.Wf_FlowTriggers.Add(NewTrigger("fk-trig", WfTriggerType.Event, starter, eventKey: EventKey));
        await db.SaveChangesAsync();

        var r = await Hook(db).OnEventAsync(EventKey, "", "{}", null);

        Assert.False(r.Success);                           // eventId 必填（幂等键素材，spec §3.3）
        Assert.Equal(0, await db.Wf_FlowInstances.CountAsync());
        Assert.Equal(0, await db.IntegrationEvents.CountAsync());   // 重试同样缺 → 不进 outbox
    }

    [Fact]
    public async Task OnEvent_VarsMap_Applied()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        db.Wf_FlowTriggers.Add(NewTrigger("fk-trig", WfTriggerType.Event, starter,
            configJson: "{\"varsMap\":{\"orderNo\":\"$.OutboundNo\"}}", eventKey: EventKey));
        await db.SaveChangesAsync();

        var r = await Hook(db).OnEventAsync(EventKey, "EV-3", "{\"OutboundNo\":\"OB-9\"}", "u");

        Assert.True(r.Success);
        var inst = await db.Wf_FlowInstances.AsNoTracking().SingleAsync();
        Assert.Contains("\"orderNo\":\"OB-9\"", inst.VarsJson);   // varsMap 点路径映射进流程变量
    }

    [Fact]
    public async Task OnEvent_PartialFail_OutboxFailed_ReplayTopsUpOnlyMissing()
    {
        // spec §8 关键测试：3 触发器发 2 成 1 败 → 重放仅补 1，已发的撞键幂等跳过
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);                       // fk-trig enabled
        await SeedFlowAndUsersAsync(conn, flowKey: "fk-off", flowEnabled: false);   // fk-off 停用
        using var db = Ctx(conn);
        db.Wf_FlowTriggers.Add(NewTrigger("fk-trig", WfTriggerType.Event, starter, eventKey: EventKey));
        db.Wf_FlowTriggers.Add(NewTrigger("fk-trig", WfTriggerType.Event, starter, eventKey: EventKey));
        db.Wf_FlowTriggers.Add(NewTrigger("fk-off", WfTriggerType.Event, starter, eventKey: EventKey));
        await db.SaveChangesAsync();
        var hook = Hook(db);

        var r1 = await hook.OnEventAsync(EventKey, "EV-4", "{}", "u");

        Assert.False(r1.Success);                          // 部分失败
        Assert.Equal(2, await db.Wf_FlowInstances.CountAsync());
        var failedEvt = await db.IntegrationEvents.AsNoTracking()
            .SingleAsync(e => e.Status == IntegrationEventStatus.Failed);
        Assert.Contains("EV-4", failedEvt.PayloadJson);    // eventId 随负载持久化供重放复用（spec §2.2）
        var outboxBefore = await db.IntegrationEvents.CountAsync();

        // 修复：启用 fk-off → dispatcher 重放路径（ReplayEventAsync，同 eventKey/eventId/payload）
        using (var fix = Ctx(conn))
        {
            (await fix.Wf_FlowDefs.SingleAsync(d => d.FlowKey == "fk-off")).Enable = true;
            await fix.SaveChangesAsync();
        }
        var r2 = await hook.ReplayEventAsync(EventKey, "EV-4", "{}", "u");

        Assert.True(r2.Success);
        Assert.Equal(3, await db.Wf_FlowInstances.CountAsync());   // 只补第 3 个，前 2 个幂等跳过
        Assert.Equal(3, await db.Wf_TriggerFires.CountAsync());
        Assert.Equal(outboxBefore, await db.IntegrationEvents.CountAsync());   // 重放不再新写 outbox 行
    }

    [Fact]
    public async Task Replay_DoesNotWriteNewOutboxRow()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        db.Wf_FlowTriggers.Add(NewTrigger("fk-trig", WfTriggerType.Event, starter, eventKey: EventKey));
        await db.SaveChangesAsync();

        var r = await Hook(db).ReplayEventAsync(EventKey, "EV-5", "{}", null);

        Assert.True(r.Success);
        Assert.Equal(1, await db.Wf_FlowInstances.CountAsync());
        Assert.Equal(0, await db.IntegrationEvents.CountAsync());   // 重放入口零 outbox 写入（映射表⑦）
    }
}
```

- [ ] **Step 3: 跑验证 FAIL**（`--filter "WfTriggerVarsMapperTests|WfTriggerBridgeHookTests"`）。

- [ ] **Step 4: 实现 WfTriggerVarsMapper**

```csharp
// CP6.Core/Services/Wf/WfTriggerVarsMapper.cs
using System.Text.Json;

namespace CP6.Core.Services.Wf;

/// <summary>event varsMap 映射（复用 ServiceVarsHelper 点路径口径，含其已记档限制：值统一为字符串）
/// + message varsSchema 白名单过滤（spec §2.3）。两者共同哲学：不透传原负载，防变量注入。</summary>
public static class WfTriggerVarsMapper
{
    public static string MapVars(Dictionary<string, string>? varsMap, string payloadJson)
    {
        if (varsMap == null || varsMap.Count == 0) return "{}";
        var ctx = new ServiceTemplateCtx(payloadJson, actorId: "", jobId: "", instanceId: "",
                                         nowUtcIso: DateTime.UtcNow.ToString("O"));
        var vars = new Dictionary<string, string>(varsMap.Count);
        foreach (var (key, template) in varsMap)
            vars[key] = ServiceVarsHelper.ResolveValue(template, ctx);
        return JsonSerializer.Serialize(vars);
    }

    /// <summary>白名单过滤：不在名单的负载键丢弃。body 非 JSON 对象抛 JsonException（端点回 400）。</summary>
    public static string FilterBySchema(string bodyJson, IReadOnlyList<string>? schema)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(bodyJson) ? "{}" : bodyJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("body must be a JSON object");
        var allow = new HashSet<string>(schema ?? Array.Empty<string>(), StringComparer.Ordinal);
        var kept = new Dictionary<string, JsonElement>();
        foreach (var p in doc.RootElement.EnumerateObject())
            if (allow.Contains(p.Name)) kept[p.Name] = p.Value.Clone();
        return JsonSerializer.Serialize(kept);
    }
}
```

- [ ] **Step 5: 实现接口文件**（仿 `IMesBridgeHook.cs` 单文件三件套 + payload record）

```csharp
// CP6.Core/Services/Integration/IWfTriggerBridgeHook.cs
namespace CP6.Core.Services.Integration;

/// <summary>WF 触发器桥接 hook（BridgeHook 家族成员，D4）。业务模块发事件＝一行调用 OnEventAsync。</summary>
public interface IWfTriggerBridgeHook
{
    /// <summary>业务调用入口：匹配 eventKey 的启用触发器逐条发起 + 写 IntegrationEvents 台账（失败行由 RetryWorker 重放）。</summary>
    /// <param name="eventKey">"{SourceModule}|{HookName}"，如 "WMS|OnShipmentConfirmedAsync"</param>
    /// <param name="eventId">业务事件唯一标识（必填，幂等键素材 "{eventId}:{TriggerId}"，spec §2.2）</param>
    Task<WfTriggerBridgeResult> OnEventAsync(string eventKey, string eventId, string payloadJson, string? userName);

    /// <summary>dispatcher 重放入口：同一执行逻辑但不再写新 outbox 行（防重放行自增殖，映射表⑦）；去重靠 TriggerFire 幂等闸。</summary>
    Task<WfTriggerBridgeResult> ReplayEventAsync(string eventKey, string eventId, string payloadJson, string? userName);
}

/// <summary>outbox 负载契约（PersistEventAsync 序列化 / dispatcher 反序列化，重放原样复用 eventId）。</summary>
public sealed record WfTriggerEventPayload(string EventKey, string EventId, string PayloadJson, string? UserName);

public class WfTriggerBridgeResult
{
    public bool Success { get; init; }
    public int MatchedCount { get; init; }
    public int FiredCount { get; init; }
    public string? Message { get; init; }

    public static WfTriggerBridgeResult Ok(int matched, int fired)
        => new() { Success = true, MatchedCount = matched, FiredCount = fired };
    public static WfTriggerBridgeResult Skipped(string reason)
        => new() { Success = false, Message = $"SKIPPED: {reason}" };
    public static WfTriggerBridgeResult Failed(string reason)
        => new() { Success = false, Message = reason };
}

public class NoOpWfTriggerBridgeHook : IWfTriggerBridgeHook
{
    public Task<WfTriggerBridgeResult> OnEventAsync(string eventKey, string eventId, string payloadJson, string? userName)
        => Task.FromResult(WfTriggerBridgeResult.Skipped("WfTriggerBridge:Enabled=false"));
    public Task<WfTriggerBridgeResult> ReplayEventAsync(string eventKey, string eventId, string payloadJson, string? userName)
        => Task.FromResult(WfTriggerBridgeResult.Skipped("WfTriggerBridge:Enabled=false"));
}
```

- [ ] **Step 6: 实现 WfTriggerBridgeHook**（仿 `MesBridgeHook` 三分支 persist 模式）

```csharp
// CP6.Core/Services/Wf/WfTriggerBridgeHook.cs
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Wf;

public class WfTriggerBridgeHook : BridgeHookBase, IWfTriggerBridgeHook
{
    private readonly IFlowTriggerService _triggers;

    public WfTriggerBridgeHook(CP6Context db, IFlowTriggerService triggers, ILogger<WfTriggerBridgeHook> logger)
        : base(db, logger)
    {
        _triggers = triggers;
    }

    public Task<WfTriggerBridgeResult> OnEventAsync(string eventKey, string eventId, string payloadJson, string? userName)
        => FireMatchingAsync(eventKey, eventId, payloadJson, userName, persistOutbox: true);

    public Task<WfTriggerBridgeResult> ReplayEventAsync(string eventKey, string eventId, string payloadJson, string? userName)
        => FireMatchingAsync(eventKey, eventId, payloadJson, userName, persistOutbox: false);

    private async Task<WfTriggerBridgeResult> FireMatchingAsync(
        string eventKey, string eventId, string payloadJson, string? userName, bool persistOutbox)
    {
        // eventId 必填（幂等键素材）：缺失重试同样缺 → 直接拒绝，不进 outbox（spec §3.3）
        if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 150)
            return WfTriggerBridgeResult.Failed("eventId 必填且 ≤150 字符（幂等键素材）");

        var corrId = Guid.NewGuid();
        var payload = new WfTriggerEventPayload(eventKey, eventId, payloadJson ?? "{}", userName);
        var source = ParseSource(eventKey);
        try
        {
            var matchedIds = await Db.Wf_FlowTriggers
                .Where(t => t.Enabled && t.TriggerType == WfTriggerType.Event && t.EventKey == eventKey)
                .Select(t => t.Id)
                .ToListAsync();

            if (matchedIds.Count == 0)
            {
                if (persistOutbox)
                    await PersistEventAsync(source, "WF", nameof(OnEventAsync), eventId, null,
                        IntegrationEventStatus.Skipped, "no matching trigger", corrId, payload);
                return WfTriggerBridgeResult.Ok(0, 0);   // 未匹配零动作（spec §8）
            }

            var fired = 0;
            string? firstError = null;
            foreach (var id in matchedIds)
            {
                // 逐条重查（FireAsync 失败路径 ChangeTracker.Clear 契约，见 A-T2）
                var trig = await Db.Wf_FlowTriggers.FirstOrDefaultAsync(t => t.Id == id);
                if (trig == null || !trig.Enabled) continue;
                var cfg = WfTriggerConfig.ParseEvent(trig.ConfigJson);
                var varsJson = WfTriggerVarsMapper.MapVars(cfg.VarsMap, payload.PayloadJson);
                var r = await _triggers.FireAsync(trig, varsJson, WfTriggerType.Event,
                                                  $"{eventId}:{trig.Id}", CancellationToken.None);
                if (r.Success) fired++;
                else firstError ??= r.Error;
            }

            if (firstError == null)
            {
                if (persistOutbox)
                    await PersistEventAsync(source, "WF", nameof(OnEventAsync), eventId, null,
                        IntegrationEventStatus.Success, null, corrId, payload);
                return WfTriggerBridgeResult.Ok(matchedIds.Count, fired);
            }

            // 部分成功 → Failed 进 outbox 重放；已发触发器撞键幂等跳过，未发补发（spec §3.3）
            if (persistOutbox)
                await PersistEventAsync(source, "WF", nameof(OnEventAsync), eventId, null,
                    IntegrationEventStatus.Failed, firstError, corrId, payload);
            return WfTriggerBridgeResult.Failed($"部分失败 {fired}/{matchedIds.Count}: {firstError}");
        }
        catch (Exception ex)
        {
            if (persistOutbox)
                await PersistEventAsync(source, "WF", nameof(OnEventAsync), eventId, null,
                    IntegrationEventStatus.Failed, ex.ToString(), corrId, payload);
            return WfTriggerBridgeResult.Failed(ex.Message);
        }
    }

    /// <summary>"{SourceModule}|{HookName}" → SourceModule（outbox 行 SourceModule 列）；格式不符归 "WF"。</summary>
    private static string ParseSource(string eventKey)
    {
        var i = eventKey?.IndexOf('|') ?? -1;
        return i > 0 ? eventKey![..i] : "WF";
    }
}
```

- [ ] **Step 7: DI** — `Program.cs` hook 家族注册区（`:396-448` 同风格）追加：

```csharp
// 事件触发 start：WF 触发器桥接 hook（BridgeHook 家族，D4；NoOpWfTriggerBridgeHook 备配置停用切换）
builder.Services.AddScoped<CP6.Core.Services.Integration.IWfTriggerBridgeHook, CP6.Core.Services.Wf.WfTriggerBridgeHook>();
```

- [ ] **Step 8: 跑验证 PASS + Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "WfTriggerVarsMapperTests|WfTriggerBridgeHookTests"
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-trigger): C-T1 IWfTriggerBridgeHook 家族新成员+varsMap 映射+部分成功重放去重+DI"
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


## 附: 现状锚点(BridgeHook家族/dispatcher)
| BridgeHook 家族 | `BridgeHookBase(CP6Context db, ILogger logger)`，protected `Db`/`Logger`；`PersistEventAsync(sourceModule, targetModule, hookName, sourceNo, targetNo, status, error, correlationId, payload)` 写 `IntegrationEvents` outbox 行（`:59-79`，Failed 时 `NextRetryAt=UtcNow+60s`），整体 try/catch 吞错只记日志。范本子类 `MesBridgeHook : BridgeHookBase, IMesBridgeHook`——`corrId = Guid.NewGuid()`、payload=方法参数匿名对象、hookName=`nameof(方法)`、三分支（Success/Skipped/Failed）各 persist 一次。接口范本 `IMesBridgeHook`：接口 + Result 类（Ok/Skipped/Failed 工厂）+ NoOp 实现同文件。 |
| dispatcher | `IntegrationEventDispatcher.cs`：静态字典 `Routes`（键 `RouteKey(source,target,hook)` = `$"{source}\|{target}\|{hook}"`，`:102-103`）；`DispatchAsync(IntegrationEvent evt, CancellationToken ct)`（`:106-120`）——`:110` 算 key，`:111` `TryGetValue` 失败抛 `InvalidOperationException("DISPATCH-404: ...")`。**fallback 插在 `:110` 与 `:111` 之间**。ctor 注入六个 hook 接口。`IntegrationEventStatus` 是字符串常量（`"SUCCESS"/"SKIPPED"/"FAILED"/"DEAD"`）。 |
| retry worker | `IntegrationEventRetryWorker.cs:81-110`：`TenantScopeRunner` 逐租户，取 `Status==Failed && NextRetryAt<=now` Take(50)，`dispatcher.DispatchAsync(evt, ct)` 返回 bool 定 Success/Failed，异常 catch 记 `LastError` 退避，`Attempts>=MaxAttempts` 转 DeadLetter。 |

## 附: 映射⑦重放双入口
| ⑦ | dispatcher 重放（§3.3） | hook 家族被 dispatcher 重放时若原样调 `OnEventAsync` 会**每次重放再写一行新 outbox**（Failed 行自增殖）。故接口拆双入口：`OnEventAsync`（业务调用，写台账）+ `ReplayEventAsync`（dispatcher 重放专用，同一执行逻辑**不再写新 outbox 行**，去重仍靠 TriggerFire 幂等闸）。spec「失败自动进 outbox / 重放原样复用 eventId」语义不变。 |
