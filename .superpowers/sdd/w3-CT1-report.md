# Task C-T1 报告：IWfTriggerBridgeHook + WfTriggerBridgeHook + varsMap 映射 + DI

**状态：DONE**
**Commit：** `4970046`（分支 `feat/wfs-event-trigger`，已 push）

## 交付文件（6，与简报一致，surgical add）
- `CP6.Core/Services/Integration/IWfTriggerBridgeHook.cs`（接口 + `WfTriggerEventPayload` record + `WfTriggerBridgeResult` + `NoOpWfTriggerBridgeHook`，单文件仿 `IMesBridgeHook`）
- `CP6.Core/Services/Wf/WfTriggerVarsMapper.cs`（`MapVars` 点路径映射 + `FilterBySchema` 白名单过滤，纯静态逻辑）
- `CP6.Core/Services/Wf/WfTriggerBridgeHook.cs`（`BridgeHookBase` 家族成员；`OnEventAsync`/`ReplayEventAsync` → 私有 `FireMatchingAsync(persistOutbox)` 双入口）
- `CP6.WebApi/Program.cs`（DI 追加一行，位于 MES hook 家族注册区后）
- `CP6.Tests/Wf/WfTriggerVarsMapperTests.cs`（6 用例，逐字转录）
- `CP6.Tests/Wf/WfTriggerBridgeHookTests.cs`（6 用例，逐字转录，SQLite + 真 FireAsync）

## TDD 证据
- **RED：** 首跑 `--filter "WfTriggerVarsMapperTests|WfTriggerBridgeHookTests"` 编译失败 `CS0246: WfTriggerBridgeHook could not be found`（实现类未存在），确认测试真正约束实现。
- **GREEN：** 实现三源文件 + DI 后，同 filter `Passed! Failed: 0, Passed: 12, Skipped: 0`（6 varsMapper + 6 hook）。

## 闸门
1. **全量测试：** `dotnet test CP6.slnx` → `Passed: 1935, Skipped: 5, Failed: 0`。基线 1923 + 12 = 1935，零回归。
2. **迁移：** `dotnet ef migrations has-pending-model-changes` → `No changes have been made to the model since the last migration.`（零迁移，引擎/实体零 diff）。
3. **git show --stat HEAD：** 恰 6 文件 375 insertions，无越界（`.superpowers/sdd/*.md` 既有改动未纳入）。

## 自审
- **映射⑦双入口：** `OnEventAsync`（`persistOutbox: true`，写 IntegrationEvents 台账）与 `ReplayEventAsync`（`persistOutbox: false`，同执行逻辑不再写新 outbox 行）共用 `FireMatchingAsync`。`Replay_DoesNotWriteNewOutboxRow` 与 `OnEvent_PartialFail_...ReplayTopsUpOnlyMissing`（`outboxBefore == after`）双证。
- **触发器粒度幂等键：** 逐条 `FireAsync(..., $"{eventId}:{trig.Id}", ...)`，与共享契约 event 键口径一致；部分失败重放靠 A-T2 内建的 TriggerFire 撞键幂等闸（`InstanceId!=null → Ok(replayed:true)`）补齐第 3 个、跳过前 2 个。
- **eventId 必填：** 空/空白/`>150` 直接 `Failed` 且不写 outbox（重试同样缺 → 不进 outbox），`OnEvent_MissingEventId_Failed_NoOutbox` 验证。
- **未匹配零动作：** `matchedIds.Count==0` 返回 `Ok(0,0)` 但写 `Skipped` 审计行（spec §8），非错误。
- **PersistEventAsync 契约：** `source`（自 `eventKey` `|` 前段解析，格式不符归 "WF"）→ SourceModule，"WF" → TargetModule，`payload`=`WfTriggerEventPayload` record（含 eventId，供 `failedEvt.PayloadJson` 含 "EV-4" 断言）。`operatorUser` 用默认（→ "system"），与简报代码一致。
- **逐条重查：** matched 只取 Id 列表，循环内 `FirstOrDefaultAsync(Id)` 重查，遵守 A-T2「FireAsync 失败路径 ChangeTracker.Clear」契约，避免跟踪实体在失败后被清导致的陈旧引用。
- **varsMap：** 复用 `ServiceVarsHelper.ResolveValue`（值统一字符串的已记档限制原样继承）；无 varsMap → `"{}"` 不透传原负载（防注入）。`FilterBySchema` 非对象 body 抛 `JsonException`（端点回 400）。

## 偏差
无。三源文件与简报代码逐字实现；测试逐字转录；DI 用简报给定的「始终注册具体实现」形态（非 MesBridge 式 Enabled 开关——`NoOpWfTriggerBridgeHook` 已备好供后续配置切换，但本任务按简报只注册具体类）。插入点选在 MES hook 家族注册区之后（Program.cs `IMesNotifier` 行前），风格与既有 `:396-448` hook 家族一致。

## Watch-item 说明（A-T2 复审关注点）
A-T2 复审指出「真并发同键 FireAsync 有窄双提交窗口」。本任务代码路径关系：
- **本 hook 内串行：** `FireMatchingAsync` 对 matched 触发器逐条 `await`（无并发 fan-out），单次调用内部不触发该窗口。
- **窗口暴露面：** 仅当**同一 eventId 的重复投递**被并发调用（例如业务并发发同一事件，或 dispatcher 重放与业务原调用同时在跑）时，两路各自对同 `{eventId}:{trigger.Id}` 键 FireAsync 才会撞到该窄窗。生产侧由 dispatcher 逐行串行处理 outbox（retry worker `Take(50)` 顺序 DispatchAsync）自然收敛，业务并发发同一 eventId 属调用方职责。
- **未改 FireAsync**（本任务范围外，遵指示）。留作 A-T2 既有关注项，无需在 C-T1 内处置。

## 后续挂钩提示（供 C 波后续任务）
- dispatcher 路由（映射⑦）应把 `WfTriggerEventPayload` 反序列化后调 `ReplayEventAsync`（非 `OnEventAsync`），避免 Failed 行自增殖。
- `NoOpWfTriggerBridgeHook` 已就位，如后续要配置化停用可仿 MesBridge 加 `WfTriggerBridge:Enabled` 分支。
