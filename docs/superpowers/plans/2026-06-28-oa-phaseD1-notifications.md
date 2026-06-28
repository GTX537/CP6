# OA Phase D-1（通知中心 + 邮件投递）实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development（每 Task 全新 subagent TDD → 控制器级复核 → 下一个）。续接 worktree `D:\CP6-oa-core` 分支 `feat/oa-inbox-core`（绝不碰 `D:\CP6` / `D:\CP6-space-backend`）。

**Goal:** 在 OA 信箱之上加**通知中心 + 邮件投递**（umbrella §9）。把已有 `IWfNotifier`（仅 SignalR 实时）扩成**持久化站内通知 + SignalR 实时 + 邮件**三渠道复合通知器；新增 4 类事件通知：**新待办**（→处理人）、**签核完成**（→发起人）、**被驳回**（→发起人）、**超时提醒**（→处理人/上级）；前端加**头部铃铛**（未读角标+下拉列表+标记已读）+ **设定开关**（per-user，邮件/按事件）。

**范围决策（用户已锁，2026-06-28）：** 方案 = 邮件 + 站内通知中心（持久化 `Wf_Notification` + 铃铛未读 + 邮件）；事件 = 新待办 / 签核完成 / 被驳回 / 超时。

**落码锚点（已实读核验）：**
- `CP6.Core/Services/Wf/IWfNotifier.cs`：现仅 `TodoCreatedAsync(assigneeId,instanceId,taskId,flowKey)`；`NullWfNotifier`（Core，测试/无 SignalR 用）。
- `CP6.WebApi/Services/SignalRWfNotifier.cs`：广播 `WfTodoCreated` 到 `Clients.All`（客户端按 assigneeId 过滤），注入 `IHubContext<NotifyHub>`。DI 在 Program.cs。
- 引擎调用点：`ApprovalNodeHandler.cs:50`（新待办）、`AdvancedFlow.cs:49/96`（加签/转交）、`WfTimeoutService.cs:63/87`（超时 re-notify）——均已调 `TodoCreatedAsync`。
- **签核完成/驳回钩子点 = `FlowEngine.cs`**：L193 `inst.Status=FlowInstanceStatus.Rejected`；Approved 决策在 `EndNodeHandler.FinishIfDrained`/L207-209。通知对象 = `inst.StarterId`。
- `CP6.Entity/DomainModels/Wf/Wf_InboxPref.cs`：`PrefsJson`(nvarchar max 自由 JSON)，唯一 (TenantId,UserId) → **通知开关进 PrefsJson，无需改表**。
- `IEmailSender`（`LogEmailSender` dev / `SmtpEmailSender` prod，2FA 已用），`SendAsync(to,subject,body)`。
- 前端：`cp6.web/src/utils/signalr.ts`（SignalR 客户端）；`cp6.web/src/views/LayoutView.vue`（布局头部，`layout.logout` 所在 → 铃铛位）；`cp6.web/src/views/oa/settings/InboxSettings.vue`（设定）。

**Tech Stack:** .NET 8 / EF Core(SqlServer+InMemory 测试) / xUnit；Vue3 + Element Plus + @microsoft/signalr + Pinia + vue-i18n(5 语) / Vitest。

**测试基线：** 后端 1262 passed/1 skip；前端 vitest 39。每 Task 末跑相关测试；**零改引擎执行态**（既有 Wf 测试照绿是硬闸）。

---

## 关键设计（先锁）

- **复合通知器（核心）**：新建 `PersistentWfNotifier : IWfNotifier`（WebApi 层，替换 `SignalRWfNotifier` 注册）。每事件：①**持久化** `Wf_Notification`（写入引擎同一 `CP6Context` 工作单元，correct-by-construction，随引擎 SaveChanges 落库，仿 Phase A §3 读模型钩子）；②**SignalR 推送**（best-effort，try/catch 吞错，不破流程）；③**邮件**（按 per-user 偏好 + 用户有 Email 才发，best-effort try/catch）。**铁律：持久化失败可冒泡（属同一事务），但 SignalR/邮件失败绝不破坏审批流。**
- **IWfNotifier 扩接口**：保留 `TodoCreatedAsync`；加 `FlowApprovedAsync(Guid starterId, Guid instanceId, string flowKey)` + `FlowRejectedAsync(Guid starterId, Guid instanceId, string flowKey, string? comment)`。`NullWfNotifier` 全部 no-op（测试/Core 默认）。
- **通知偏好（PrefsJson 子键）**：`notify` = `{ todo:bool, approved:bool, rejected:bool, timeout:bool, email:bool }`，缺省全 true。复合通知器读 `Wf_InboxPref` 解析；无行=默认全开。
- **错误码**：沿用 `E-WF-0xx`（通知读写一般不抛业务码；非法 markRead 他人通知→静默忽略或 E-WF-007 not found）。
- **commit**：每 Task 末本地 commit（不 push），尾 `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`。

---

# Part A — 后端

## N-T1：数据模型 — Wf_Notification 表 + IWfNotifier 扩接口 + 迁移

**Files:** Create `CP6.Entity/DomainModels/Wf/Wf_Notification.cs`、`CP6.Entity/DomainModels/Wf/WfNotificationType.cs`；Modify `CP6.Core/Services/Wf/IWfNotifier.cs`、`CP6.Core/EFDbContext/CP6Context.cs`；Test `CP6.Tests/Oa/NotificationModelTests.cs`

- 实体 `Wf_Notification : BaseTenantEntity`：`Guid UserId`(收件人) / `int Type`(WfNotificationType) / `string Title`(MaxLength 200) / `string Body`(nvarchar max) / `Guid? InstanceId` / `Guid? TaskId` / `string? FlowKey`(MaxLength 100) / `bool IsRead`(默认 false) / `DateTime? ReadAt`。
- `WfNotificationType` 常量：`TodoCreated=1 / FlowApproved=2 / FlowRejected=3 / Timeout=4`。
- `IWfNotifier` 加 `FlowApprovedAsync` / `FlowRejectedAsync`；`NullWfNotifier` 补 no-op 实现。
- `CP6Context`：`DbSet<Wf_Notification> Wf_Notifications` + 索引 `HasIndex(x => new { x.TenantId, x.UserId, x.IsRead })`。
- 迁移 `OaPhaseD1Notification`（仅建 Wf_Notification 表 + 索引，**核对 Up() 零 Space_***）。
- **测试**：实体 CRUD（InMemory，NewDb 工厂同既有）；类型常量；兼容回归 `--filter Wf` 全绿（IWfNotifier 加方法须确保所有实现/调用点编译——NullWfNotifier 已补，PersistentWfNotifier 在 N-T4 才建：本 Task 先让 NullWfNotifier + SignalRWfNotifier 临时也实现新方法 no-op，避免编译断；N-T4 再替换 SignalRWfNotifier）。

## N-T2：NotificationService（list/unread/read/read-all/create）

**Files:** Create `CP6.Core/Services/Oa/INotificationService.cs`、`NotificationService.cs`、`NotificationModels.cs`；Test `CP6.Tests/Oa/NotificationServiceTests.cs`

- DTO `NotificationItem(Guid Id, int Type, string Title, string Body, Guid? InstanceId, Guid? TaskId, string? FlowKey, bool IsRead, DateTime CreateDate)`。
- 接口：`ListAsync(Guid userId, bool unreadOnly, int page, int pageSize)` / `UnreadCountAsync(Guid userId)` / `MarkReadAsync(Guid userId, Guid id)`（仅本人，他人静默跳过）/ `MarkAllReadAsync(Guid userId)` / `CreateAsync(Guid userId, int type, string title, string body, Guid? instanceId, Guid? taskId, string? flowKey)`（**不 SaveChanges**，仅 Add——交由调用方/引擎同事务 save，复合通知器与引擎共用单元；列表/标记类自行 save）。
- **测试**：create+list 正序倒序 / unreadOnly 过滤 / unreadCount / markRead 本人成功他人无效 / markAll 清零。

## N-T3：通知偏好（PrefsJson 子键 notify）

**Files:** Modify `CP6.Core/Services/Oa/PrefService.cs`（或新建 helper）；Test 扩 `CP6.Tests/Oa/PrefServiceTests.cs`（若有）或 `NotificationServiceTests`

- 解析 `Wf_InboxPref.PrefsJson` 的 `notify` 子对象 → `NotificationPrefs(bool Todo, bool Approved, bool Rejected, bool Timeout, bool Email)`，缺省全 true（无行/无键即默认开）。
- 暴露 `GetNotifyPrefsAsync(Guid userId)`（复合通知器用）+ 写入路径（设定 UI 经现有 PrefService.SaveAsync 存整个 PrefsJson，本 Task 仅保证读取健壮）。
- **测试**：无偏好=全 true / 关 email / 关单个事件 各正确解析。

## N-T4：PersistentWfNotifier 复合通知器（持久化+SignalR+邮件）

**Files:** Create `CP6.WebApi/Services/PersistentWfNotifier.cs`；Modify `CP6.WebApi/Program.cs`（替换 `SignalRWfNotifier` 注册为 `PersistentWfNotifier`；删 `SignalRWfNotifier` 或保留备用）

- 注入 `CP6Context` + `INotificationService` + `IEmailSender` + `IHubContext<NotifyHub>` + 用户查询（Sys_Users 取 Email/姓名）+ 偏好读取（N-T3）。
- 每方法（TodoCreated/FlowApproved/FlowRejected + 复用于 Timeout，超时仍走 TodoCreatedAsync 但 Type 由调用上下文区分——简化：超时也记 TodoCreated 类型，或加 TimeoutAsync；**本计划：WfTimeoutService 的 re-notify 继续调 TodoCreatedAsync，Type=TodoCreated**，§9 的「超时邮件」由偏好 timeout 控制留待 N-T5 决定是否细分；MVP 先让超时复用 todo 通知）：
  1. 读收件人偏好；事件被关→跳过该渠道。
  2. 持久化：`INotificationService.CreateAsync(...)`（Add 到共享 context）。
  3. SignalR：`_hub.Clients.All.SendAsync("WfNotification", new {...})`（含 type/userId/instanceId，客户端按 userId 过滤）——try/catch 吞错。
  4. 邮件：偏好 email 开 + 用户有 Email → `_email.SendAsync(...)`——try/catch 吞错。
- Title/Body 文案：后端用简洁中文模板（如「您有新的待办：{flowName}」/「您的申请已通过」/「您的申请被驳回：{comment}」）；前端展示用 type 映射 i18n（N-T10）。
- **验收**：编译 + 全量回归绿；DI 装配（dotnet build WebApi）。控制器/通知器无单测（集成性），靠 N-T5 引擎钩子测 + live QA 实证。

## N-T5：引擎钩子（签核完成/驳回 → 通知发起人）

**Files:** Modify `CP6.Core/Services/Wf/FlowEngine.cs`（Approved/Rejected 决策点）、可能 `EndNodeHandler.cs`；Test `CP6.Tests/Oa/NotificationEngineHookTests.cs`

- 在 `inst.Status` 落 `Approved`（FinishIfDrained 成功整体通过）→ `await Notifier.FlowApprovedAsync(inst.StarterId, inst.Id, inst.FlowKey)`。
- 在 `inst.Status` 落 `Rejected`（FlowEngine L193 区）→ `await Notifier.FlowRejectedAsync(inst.StarterId, inst.Id, inst.FlowKey, comment)`。
- **铁律**：钩子置 SaveChanges/状态确定后；用 `NullWfNotifier` 时 no-op，既有引擎测试零改照绿（硬闸 `--filter Wf`）。
- **测试**（用记数 FakeNotifier 注入引擎）：线性流程批准到 end → FlowApprovedAsync 收到 1 次且 userId=starter；驳回 → FlowRejectedAsync 1 次带 comment；并行全通过 → Approved 仅 1 次（FinishIfDrained 去重）。

## N-T6：NotificationController + DI（Part A 收尾）

**Files:** Create `CP6.WebApi/Controllers/Oa/NotificationController.cs`；Modify `CP6.WebApi/Program.cs`（注册 INotificationService）

- 路由 `api/oa/notification`，`[Authorize]`，照 InboxController 模式（LocalizedControllerBase / 自定义 Ok2 / `(await _ctx.GetAsync()).UserId`）：
  - `GET list?unreadOnly=&page=&pageSize=` / `GET unread-count` / `POST read {id}` / `POST read-all`。
- DI：`AddScoped<INotificationService, NotificationService>()`；确认 N-T4 的 PersistentWfNotifier 已注册替换。
- **验收**：dotnet build + 全量 `dotnet test` 全绿（1262 + 新增）。

---

# Part B — 前端

## N-T7：API + TS 类型

**Files:** Create `cp6.web/src/api/oa/notification.ts`、`cp6.web/src/types/oa/notification.ts`

- 类型 `NotificationItem`（对齐后端 DTO，camelCase）+ `NotificationType` 枚举。
- api：`list(unreadOnly?,page?,pageSize?)` / `unreadCount()` / `read(id)` / `readAll()`（`import http from '../http'`，照 inbox.ts）。
- type-check 绿。

## N-T8：NotificationBell.vue 头部铃铛 + SignalR 接线

**Files:** Create `cp6.web/src/views/oa/notification/NotificationBell.vue`；Modify `cp6.web/src/views/LayoutView.vue`（头部插铃铛）、`cp6.web/src/utils/signalr.ts`（订阅 `WfNotification`/`WfTodoCreated`）

- 铃铛：el-badge 未读角标（轮询/SignalR 刷新 unreadCount）+ el-popover/dropdown 下拉最近通知列表 + 点项跳 `/oa/inbox` detail（按 instanceId）+「全部已读」。
- SignalR：监听 `WfNotification`（按当前 userId 过滤）→ unreadCount+1 + 列表 prepend + 可选 ElNotification 弹窗。复用 signalr.ts 既有连接。
- 文案 `t('oa.notify.*')`（运行时键，免重生 keys.generated）。type-check + vitest(39) 绿。

## N-T9：设定 UI（通知偏好开关）

**Files:** Modify `cp6.web/src/views/oa/settings/InboxSettings.vue`

- 加「通知设定」区：邮件总开关 + 按事件开关（新待办/签核完成/被驳回/超时）→ 存入 PrefsJson.notify（经现有 pref save）。
- type-check 绿。

## N-T10：i18n 五语 + gstack QA + build 闸

**Files:** Create `CP6.WebApi/Seed/I18nOaNotifyScreenSeed.cs`；Modify `CP6.WebApi/Program.cs`（concat seed）；Create `docs/superpowers/qa/wfs-form-inbox/phaseD1/`

- i18n seed：grep `t('oa.notify.*')` 全键 + 通知类型文案，5 语全覆盖，**避开 Phase B/C/C′ seed LangKey 重复**（读 I18nOaInbox/Advanced/DesignerScreenSeed），接 Program.cs concat 链（带去重）。
- build 闸：dotnet build + type-check + vitest + npm build 全绿。
- gstack QA harness（隔离库 CP6DB_OA，6 剧本草稿）：填單发起→处理人收到站内通知+铃铛角标→批准→发起人收「签核完成」通知→驳回另一单→发起人收「被驳回」→关掉某事件偏好后不再收→邮件 LogEmailSender 日志可见。**live QA 待用户在场跑**（与 Space 共用实例，隔离库执行）。

---

## DoD（完成定义）

- 后端 `dotnet test` 全绿（1262 + N-T1/T2/T3/T5 新增）；零改引擎执行态（`--filter Wf` 照绿）。
- 前端 type-check / vitest / build 全绿；铃铛在真浏览器渲染、SignalR 实时刷新、设定开关生效（live QA）。
- 站内通知持久化 + 邮件投递（dev=LogEmailSender 日志）+ 4 事件齐全 + per-user 偏好生效。
- 整支零 Space 污染；本地 commit 不 push。
