### Task C-T2: dispatcher 目标泛化 fallback 路由 + Echo 样例事件源

> **零跨模块污染的唯一 Integration 触点**：`IntegrationEventDispatcher` ctor 加 1 注入 + `DispatchAsync` 加 1 个 fallback 分支（`:110` 算 key 之后、`:111` `TryGetValue` 之前）。DISPATCH-404 语义对其余路由不变。

**Files:**
- Modify: `CP6.Core/Services/Integration/IntegrationEventDispatcher.cs`
- Create: `CP6.WebApi/Controllers/Oa/FlowTriggerAdminController.cs` **暂不建**——Echo 样例源先落最小独立控制器 `CP6.WebApi/Controllers/Oa/WfTriggerEchoController.cs`（QA harness 用，对齐 ServiceTask EchoConnector 先例）
- Test: `CP6.Tests/Wf/WfTriggerDispatchTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/WfTriggerDispatchTests.cs
// dispatcher 用 NoOp 六 hook + 可断言的 FakeWfTriggerHook（记录收到的参数、可控返回）构造。
// NoOp 类名照 CP6.Core/Services/Integration 实际（六个家族各有 NoOp——侦察已核）。
using System;
using System.Text.Json;
using System.Threading.Tasks;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Integration;
using Xunit;

namespace CP6.Tests;

public class WfTriggerDispatchTests
{
    private sealed class FakeWfTriggerHook : IWfTriggerBridgeHook
    {
        public string? LastMethod, LastEventKey, LastEventId, LastPayload, LastUser;
        public bool NextSuccess = true;

        public Task<WfTriggerBridgeResult> OnEventAsync(string eventKey, string eventId, string payloadJson, string? userName)
            => Record("OnEventAsync", eventKey, eventId, payloadJson, userName);
        public Task<WfTriggerBridgeResult> ReplayEventAsync(string eventKey, string eventId, string payloadJson, string? userName)
            => Record("ReplayEventAsync", eventKey, eventId, payloadJson, userName);

        private Task<WfTriggerBridgeResult> Record(string method, string k, string id, string p, string? u)
        {
            LastMethod = method; LastEventKey = k; LastEventId = id; LastPayload = p; LastUser = u;
            return Task.FromResult(NextSuccess ? WfTriggerBridgeResult.Ok(1, 1) : WfTriggerBridgeResult.Failed("boom"));
        }
    }

    private static (IntegrationEventDispatcher Dispatcher, FakeWfTriggerHook Fake) NewDispatcher()
    {
        var fake = new FakeWfTriggerHook();
        var d = new IntegrationEventDispatcher(
            new NoOpMesBridgeHook(), new NoOpWmsBridgeHook(), new NoOpErpBridgeHook(),
            new NoOpOrderCancelBridgeHook(), new NoOpFinBridgeHook(), new NoOpSpaceBridgeHook(),
            fake);
        return (d, fake);
    }

    private static IntegrationEvent Evt(string source, string target, string hook, string payloadJson) => new()
    {
        Id = Guid.NewGuid(), SourceModule = source, TargetModule = target,
        HookName = hook, PayloadJson = payloadJson, Status = IntegrationEventStatus.Failed, Attempts = 1,
    };

    [Fact]
    public async Task Dispatch_TargetWF_OnEventAsync_RoutesToReplay_AnySource()
    {
        var (d, fake) = NewDispatcher();
        var payload = JsonSerializer.Serialize(
            new WfTriggerEventPayload("SPACE|OnLocationPublishedAsync", "EV-7", "{\"binNo\":\"B1\"}", "u"));

        var ok = await d.DispatchAsync(Evt("SPACE", "WF", "OnEventAsync", payload));

        Assert.True(ok);
        Assert.Equal("ReplayEventAsync", fake.LastMethod);          // 重放入口，不是 OnEventAsync（映射表⑦）
        Assert.Equal("SPACE|OnLocationPublishedAsync", fake.LastEventKey);
        Assert.Equal("EV-7", fake.LastEventId);                     // eventId 原样复用（spec §2.2）
        Assert.Equal("{\"binNo\":\"B1\"}", fake.LastPayload);
        Assert.Equal("u", fake.LastUser);
    }

    [Fact]
    public async Task Dispatch_TargetWF_OtherHookName_Still404()
    {
        var (d, _) = NewDispatcher();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => d.DispatchAsync(Evt("SPACE", "WF", "OnSomethingElse", "{}")));
        Assert.Contains("DISPATCH-404", ex.Message);                // fallback 只认 OnEventAsync
    }

    [Fact]
    public async Task Dispatch_ExistingRoutes_Unchanged_Unknown404()
    {
        var (d, _) = NewDispatcher();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => d.DispatchAsync(Evt("X", "Y", "Z", "{}")));
        Assert.Contains("DISPATCH-404", ex.Message);                // 既有语义不变
    }

    [Fact]
    public async Task Dispatch_TargetWF_BadPayload_Throws400()
    {
        var (d, _) = NewDispatcher();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => d.DispatchAsync(Evt("SPACE", "WF", "OnEventAsync", "null")));
        Assert.Contains("DISPATCH-400", ex.Message);                // 对齐 GetPayload 空负载语义
    }
}
```

- [ ] **Step 2: 跑验证 FAIL**（`--filter WfTriggerDispatchTests`）。

- [ ] **Step 3: 实现 dispatcher**（全部改动仅此文件三处）：

  1. 私有字段 + ctor 参数追加 `IWfTriggerBridgeHook wfTrigger` → `_wfTrigger = wfTrigger;`（`DispatchContext` **不动**——fallback 不经过它）。
  2. `DispatchAsync` 方法体（签名不变，改 `public async Task<bool>`），在 `var key = RouteKey(...)` 之后、`if (!Routes.TryGetValue(...))` 之前插入：

```csharp
        // WF 触发器目标泛化路由（spec §3.3）：target=WF & hook=OnEventAsync 不看 source 直接路由。
        // 走 ReplayEventAsync（重放不再写新 outbox 行，映射表⑦）；DISPATCH-404 语义对其余路由不变。
        if (evt.TargetModule == "WF" && evt.HookName == nameof(IWfTriggerBridgeHook.OnEventAsync))
        {
            var p = JsonSerializer.Deserialize<WfTriggerEventPayload>(evt.PayloadJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("DISPATCH-400: empty WfTrigger payload");
            var r = await _wfTrigger.ReplayEventAsync(p.EventKey, p.EventId, p.PayloadJson, p.UserName);
            return r.Success;
        }
```

  3. 方法尾 `return route(context);` 改 `return await route(context);`（转 async 后等价）。

- [ ] **Step 4: 实现 Echo 样例事件源**（演示业务模块「一行调用」接入点；真实业务接入按需求单独拉动，spec §3.3）：

```csharp
// CP6.WebApi/Controllers/Oa/WfTriggerEchoController.cs
using CP6.Core.Services.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>Echo 样例事件源（QA harness 用，对齐 ServiceTask EchoConnector 先例，spec §3.3）。
/// 业务模块接入真实事件 = 与本控制器同款「一行调用」IWfTriggerBridgeHook.OnEventAsync。</summary>
[ApiController]
[Route("api/oa/wf-trigger-echo")]
[Authorize]
public class WfTriggerEchoController : ControllerBase
{
    private readonly IWfTriggerBridgeHook _hook;

    public WfTriggerEchoController(IWfTriggerBridgeHook hook) { _hook = hook; }

    [HttpPost("fire")]
    public async Task<IActionResult> Fire([FromBody] EchoEventReq r)
    {
        var result = await _hook.OnEventAsync(
            string.IsNullOrWhiteSpace(r.EventKey) ? "QA|OnEchoAsync" : r.EventKey,
            string.IsNullOrWhiteSpace(r.EventId) ? Guid.NewGuid().ToString("N") : r.EventId,
            r.PayloadJson ?? "{}",
            User.Identity?.Name);
        return Ok(new { code = 0, message = "OK", data = new { result.Success, result.MatchedCount, result.FiredCount, result.Message } });
    }

    public record EchoEventReq(string? EventKey, string? EventId, string? PayloadJson);
}
```

- [ ] **Step 5: 跑验证 PASS + 全量闸（dispatcher 是共享件，跑全量不是只跑 Wf）+ commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter WfTriggerDispatchTests
dotnet test CP6.Tests/CP6.Tests.csproj          # 全量：既有 Integration/dispatcher 测试必须字节等价
git add -A && git commit -m "feat(wfs-trigger): C-T2 dispatcher 目标泛化 fallback(唯一 Integration 触点)+Echo 样例事件源"
```

---


---
## 附: 现状锚点(dispatcher/retry worker)
| dispatcher | `IntegrationEventDispatcher.cs`：静态字典 `Routes`（键 `RouteKey(source,target,hook)` = `$"{source}\|{target}\|{hook}"`，`:102-103`）；`DispatchAsync(IntegrationEvent evt, CancellationToken ct)`（`:106-120`）——`:110` 算 key，`:111` `TryGetValue` 失败抛 `InvalidOperationException("DISPATCH-404: ...")`。**fallback 插在 `:110` 与 `:111` 之间**。ctor 注入六个 hook 接口。`IntegrationEventStatus` 是字符串常量（`"SUCCESS"/"SKIPPED"/"FAILED"/"DEAD"`）。 |
| retry worker | `IntegrationEventRetryWorker.cs:81-110`：`TenantScopeRunner` 逐租户，取 `Status==Failed && NextRetryAt<=now` Take(50)，`dispatcher.DispatchAsync(evt, ct)` 返回 bool 定 Success/Failed，异常 catch 记 `LastError` 退避，`Attempts>=MaxAttempts` 转 DeadLetter。 |

## 附: 映射⑦重放双入口
| ⑦ | dispatcher 重放（§3.3） | hook 家族被 dispatcher 重放时若原样调 `OnEventAsync` 会**每次重放再写一行新 outbox**（Failed 行自增殖）。故接口拆双入口：`OnEventAsync`（业务调用，写台账）+ `ReplayEventAsync`（dispatcher 重放专用，同一执行逻辑**不再写新 outbox 行**，去重仍靠 TriggerFire 幂等闸）。spec「失败自动进 outbox / 重放原样复用 eventId」语义不变。 |
