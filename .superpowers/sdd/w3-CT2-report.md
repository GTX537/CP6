# Task C-T2 报告：dispatcher 目标泛化 fallback + Echo 样例事件源

**STATUS: DONE** — 波③ H-C 收口任务（14 之 7）完成并推送。

## 提交
- SHA `f9a319f`（分支 `feat/wfs-event-trigger`，已 push）
- 3 文件 / +144 / -3：
  - `CP6.Core/Services/Integration/IntegrationEventDispatcher.cs`（改）
  - `CP6.WebApi/Controllers/Integration/WfTriggerEchoController.cs`（新，见下「偏离」）
  - `CP6.Tests/Wf/WfTriggerDispatchTests.cs`（新，4 用例）

## 测试
- 新 filter `WfTriggerDispatchTests`：4/4 绿（RED→GREEN 已验，RED 为 7 参构造编译失败）。
- 全量 `dotnet test CP6.slnx`：**1939 passed / 5 skipped / 0 failed**（基线 1935 + 4 新）。
- `dotnet ef migrations has-pending-model-changes`：clean（零迁移）。

## 实现（dispatcher 三处，字节最小）
1. 字段 `_wfTrigger` + ctor 追加 **可选** 参 `IWfTriggerBridgeHook? wfTrigger = null` → `_wfTrigger = wfTrigger ?? new NoOpWfTriggerBridgeHook()`。
2. `DispatchAsync` 转 `async`，在 `RouteKey` 之后、`TryGetValue` 之前插 fallback 分支：`target=="WF" && hook==nameof(OnEventAsync)` → 反序列化 `WfTriggerEventPayload`（空负载抛 DISPATCH-400）→ `await _wfTrigger.ReplayEventAsync(...)`（重放入口，映射⑦）→ `return r.Success`。
3. 方法尾 `return route(context)` → `return await route(context)`。

静态 `Routes` 表、`DispatchContext`、retry worker **零改动**。DISPATCH-404 语义对其余路由不变。

## 两处「brief 无法预知」的偏离（均为必要且更贴 spec）

### 1. ctor 参设为可选（`= null`），而非 brief 的必填
既有 `CP6.Tests/IntegrationEventDispatcherTests.cs` 4 用例以 **6 参** 直接 `new IntegrationEventDispatcher(...)`。若加必填第 7 参，这些用例编译失败——同时违反 gate「既有 dispatcher 测试字节等价」与「只 brief 文件」。设可选参 + `?? NoOp` 兜底：既有 6 参构造点零改动仍编译；DI 已注册 `IWfTriggerBridgeHook`（Program.cs:489）会注入真实 hook；新测试与生产路径均得真实/Fake hook。field 非空、无 NRE 风险。

### 2. Echo 控制器落 `Controllers.Integration`（brief 写 `Controllers.Oa`）
`OawfPermissionAttributeTests`（M-OA/WF 波遗留守卫）**硬锁** `Controllers.Oa ∪ Controllers.Wf` 命名空间控制器数 == 16，且每个 mutating 端点须带 `[RequirePermission]` 或列入「只读 POST 豁免」。Echo 的 `Fire` POST 无权限键、且非只读（会触发 downstream 写），放 Oa 命名空间会双红（计数 16→17 + offender）。

解决：落 `Controllers.Integration`（与既有 `BridgeHealthController` 同族——后者亦有 mutating POST `Compensate` 仅 `[Authorize]` 无权限键，即 Integration/QA-ops 端点先例）。语义正确：Echo 是 Integration 桥接 hook 的 QA harness，非业务 OA 写端点，本不该占 OA 权限键/菜单种子（spec §6 的 OA.FlowTrigger.View/Edit 属管理页 T-E/T-F，非本任务）。路由仍保留 `api/oa/wf-trigger-echo`（QA 脚本可读性）。已核 `Controllers.Integration` 无任何守卫测试扫描。

给管理页/真实业务接入 CI 用的替代路径（若后续要求 Echo 必须在 Oa 命名空间）：须编辑 `OawfPermissionAttributeTests`（16→17）+ 真相源 `docs/seeds/oawf-permission-keys.md`——本任务判定为过度 sprawl 且违 gate #3，故取更小的 Integration 落点。

## 关切 / 交接
- **NoOpSpaceBridgeHook 不存在**：brief 测试 `NewDispatcher()` 用 `new NoOpSpaceBridgeHook()`，但 Space 家族**无 NoOp**（仅 Mes/Wms/Erp/OrderCancel/Fin/WfTrigger 六家族有）。已按既有 dispatcher 测试口径改用 `Mock.Of<ISpaceBridgeHook>()`（+`using Moq;`）。brief 注「六家族各有 NoOp——侦察已核」对 Space 有误。
- 无其它风险。引擎零 diff、零迁移、gate #3（`git show --stat HEAD` 仅 3 文件）已验。
