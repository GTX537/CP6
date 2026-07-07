# Task 6 Report — SignalR SpaceHub（发布推送 + events 页自动刷新）

**Status:** DONE
**Commit:** `e2ddf74` on `feat/space-wave4-crosscutting` (single commit)
**Architecture:** 接口注入（照 SignalRWmsNotifier 模式，无反射）

---

## Implemented

### Backend
- **Create** `CP6.Core/Services/Integration/ISpaceNotifier.cs` — `Task NotifyLocationPublishedAsync(string batchNo, int count, string status)` + 同文件内联 `sealed NoOpSpaceNotifier`（空实现，测试/降级）。接口注释写明契约「实现不得抛出」（best-effort，推送失败绝不影响业务）。
- **Create** `CP6.WebApi/Hubs/SpaceHub.cs` — 照 WmsHub：OnConnected/Disconnected 日志、注释含前端 `withUrl('/hubs/space')` 连接样例；**无分组**（低频全播 YAGNI）。
- **Create** `CP6.WebApi/Services/SignalRSpaceNotifier.cs` — 注入 `IHubContext<SpaceHub>`，`Clients.All.SendAsync("LocationPublished", new { batchNo, count, status })`，try/catch 吞错 `LogWarning`（推送失败不传播）。
- **Modify** `CP6.Core/Services/Space/LocationPublishService.cs` — ctor 追加 `ISpaceNotifier notifier`（第 7 参）；
  - `PublishFloorAsync`：**在 `tx.CommitAsync()` 之后、`return locs.Count` 之前** notify（status="SUCCESS", count=locs.Count）。
  - `DeactivateAsync`：兜底事件 hook（`OnLocationPublishedAsync`）调用后 notify（batch.BatchNo, count=1, "SUCCESS"）。
  - 服务层直调（不额外包 try/catch）——契约由实现层吞错保证。
- **Modify** `CP6.WebApi/Program.cs` — `AddScoped<ISpaceNotifier, SignalRSpaceNotifier>()`（ILocationPublishService 注册之后，Space DI 段）；`app.MapHub<SpaceHub>("/hubs/space")`（既有三个 MapHub 之后）。

### Frontend
- **Create** `cp6.web/src/utils/spaceHub.ts` — 照 wmsHub.ts 单例：`withUrl('/hubs/space')` 无 accessTokenFactory（cookie 隐式认证）+ `withAutomaticReconnect([0,2000,5000,10000,30000])` + Disconnected 状态守卫。导出 `getSpaceConnection/startSpaceConnection/stopSpaceConnection/onLocationPublished/offLocationPublished` + `LocationPublishedPayload` 类型。
- **Modify** `cp6.web/src/views/space/lifecycle/SpaceEventsView.vue` — onMounted `startSpaceConnection()` + `onLocationPublished(handler)`；handler `page=1` 后 `listRef.reload()`；onUnmounted `offLocationPublished(handler)`（不 stop 共享连接，仅解绑回调）。无新增 UI 文案 → 无新 i18n key。

---

## 构造点同步清单（grep `new LocationPublishService(` 全覆盖，共 4 处 + 1 帮手默认）

| 文件 | 帮手 | 同步 |
|---|---|---|
| `CP6.Tests/LocationPublishServiceTests.cs:33` | `MakePublishSvc` | 加第 7 参 + 可选 `ISpaceNotifier? notifier` 参（默认 `NoOpSpaceNotifier`） |
| `CP6.Tests/BindCodesTests.cs:23` | `Make` (SceneService) | 加 `new NoOpSpaceNotifier()` |
| `CP6.Tests/SceneServiceTests.cs:24` | `Make` (SceneService) | 加 `new NoOpSpaceNotifier()` |
| `CP6.Tests/SpaceMasterServiceTests.cs:33` | `Make` (SpaceMasterService) | 加 `new NoOpSpaceNotifier()` |

（3 个测试文件均已 `using CP6.Core.Services.Integration;`——`NoOpSpaceNotifier` 直接可见，无需加 using。）

---

## TDD Evidence

**新增测试：**
- `CP6.Tests/SignalRSpaceNotifierTests.cs`（2）——`Mock<IHubContext<SpaceHub>>` 链照 DeadLetterNotifierTests:49 范式：
  - `Notify_PushesLocationPublishedToAllClients`：Verify `SendCoreAsync("LocationPublished", args, ct)` Times.Once。
  - `Notify_SwallowsHubException_DoesNotPropagate`：ClientProxy.SendCoreAsync `ThrowsAsync` → `Record.ExceptionAsync` 断言 `Null`（吞错不传播）。
- `CP6.Tests/LocationPublishServiceTests.cs` +1——`Publish_GatePassed_NotifiesSignalR`：内联 `RecordingSpaceNotifier` 桩，断言 `Calls==1` / `LastBatchNo` 非空且 `StartsWith("LPUB-")` / `LastCount==1` / `LastStatus=="SUCCESS"`。
- `cp6.web/.../SpaceEventsView.spec.ts` +1——`vi.mock('@/utils/spaceHub')` 捕获 `onLocationPublished` 回调；触发回调后断言 `publishApi.events` 二次调用 + `toHaveBeenLastCalledWith(1, 50)`（回第 1 页）。

**验证结果：**
- 后端全量：`dotnet test CP6.Tests` → **Passed 1565, Failed 0, Skipped 5**（1562 → +3：SignalRSpaceNotifier ×2 + LocationPublishService ×1）。
- 前端全量：`npx vitest run` → **Test Files 57 passed, Tests 369 passed**（368 → +1）。
- 前端 type-check：`vue-tsc --build` → 0 error。
- Build：`dotnet build CP6.Tests.csproj` → Build succeeded, 0 Error（6 既有 warning，与本任务无关）。

---

## Files Changed
```
Create  CP6.Core/Services/Integration/ISpaceNotifier.cs
Create  CP6.WebApi/Hubs/SpaceHub.cs
Create  CP6.WebApi/Services/SignalRSpaceNotifier.cs
Create  CP6.Tests/SignalRSpaceNotifierTests.cs
Create  cp6.web/src/utils/spaceHub.ts
Modify  CP6.Core/Services/Space/LocationPublishService.cs
Modify  CP6.WebApi/Program.cs
Modify  CP6.Tests/BindCodesTests.cs
Modify  CP6.Tests/SceneServiceTests.cs
Modify  CP6.Tests/SpaceMasterServiceTests.cs
Modify  CP6.Tests/LocationPublishServiceTests.cs
Modify  cp6.web/src/views/space/lifecycle/SpaceEventsView.vue
Modify  cp6.web/src/views/space/lifecycle/__tests__/SpaceEventsView.spec.ts
```

---

## Self-review
- **推送时序正确**：PublishFloorAsync 的 notify 严格在 `tx.CommitAsync()` 之后——只通知已确定的事件，推送不进事务。DeactivateAsync 在兜底事件 hook（已 SaveChanges）后 notify。
- **吞错契约双保险**：SignalRSpaceNotifier 实现层 try/catch 吞错；服务层因此直调不再包 try/catch（避免冗余）。测试专门覆盖 hub 抛错不传播。
- **无分组决策**：Space 事件低频，全播 `Clients.All` 足够，未引入 warehouse/product 式分组（YAGNI，符合 brief）。
- **前端连接生命周期**：onUnmounted 仅 `off` 回调、不 `stop` 单例连接——避免误伤其他潜在 Space 消费者；当前 SpaceEventsView 是唯一消费者，连接由 startSpaceConnection 惰性启动一次。
- **未提交无关文件**：工作树中预存的 `picture/`、`shots/` 未跟踪文件已排除，仅提交 13 个任务文件。
- **eslint**：项目无 eslint 配置（`eslint.config.*` 不存在），三件套以 type-check + vitest 为编译/测试门禁，均通过。
