# WFS 引擎基建六件套 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: 用 superpowers:subagent-driven-development（首选）或 superpowers:executing-plans 逐 Task 落地。步骤用 `- [ ]` checkbox 追踪。**每个 Task 执行前必读对应 spec 章节**（`docs/superpowers/specs/2026-07-05-wfs-engine-infra-design.md`，唯一权威，**不许改设计**）。测试代码在本计划内逐条给全、可编译，**绝不允许 `{ /* 场景注释 */ }` 骨架**（二期教训：56 处被打回）。

**Goal:** 兑现 WFS 深化三期 Spec B 的六件套引擎基建——①工作日历（新表 `Sys_WorkCalendar` + 年历管理页 + 日本法定假日 seed 2026–2027 + `workdays` 第四延时模式，FireHour 默认 9）②approval 超时错误边（`TimeoutAction="errorEdge"` 第四动作 + 来源放宽 approval + E-WF-027）③终态 job/流水清理 worker（180 天硬删/占坑永不清/老化占坑告警/message 幂等窗口=保留期契约）④per-tenant 连接器（`Wf_Connector` 表 + DataProtection 加密 + 租户优先→app 兜底 + E-WF-028）⑤per-node HTTP method/timeout 覆盖 ⑥租户时区（`Sys_Tenant.TimeZoneId` + `ITenantClock` + 时区自愈/DST 口径）。**三表改动合并一次迁移 `WfsInfra`**（D6）。

**Architecture:** 一次迁移 `WfsInfra` 落三处 schema 变更（`Sys_WorkCalendar` 新表 + `Wf_Connector` 新表 + `Sys_Tenant.TimeZoneId` 新列）——为满足「恰一次迁移」，A-T1 **一次性声明全部三处 schema**，再生成迁移；后续 I-D/I-E 只消费既有 schema，不再产生迁移。`FlowNode` 的 `ServiceHttpMethod?`/`ServiceTimeoutSec?` 是 SchemaJson POCO（零迁移）。纯查询服务 `IWorkdayCalculator`（读 `Sys_WorkCalendar` + 周末缺省 + 366 天防死循环）。`workdays` 延时模式接入 `ServiceTaskNodeHandler` 的 timer 到期计算（I-A 用服务器本地 tz 作 app 默认时区占位，I-E 换 `ITenantClock`——与既有 `ComputeDueUtc` untilDate 的「app 默认=服务器本地、未来接 per-tenant tz 零返工」注释同款演进路径）。超时错误边＝`WfTimeoutService` switch 加 `errorEdge` case + 引擎新入口 `TimeoutAdvanceErrorEdgeAsync`（节点级作废待办 + 注入错误变量 + `AdvanceAlongErrorEdge` 路由）+ `FlowSchemaValidator` 来源集合单一常量放宽 + E-WF-027。清理 worker＝`WfServiceJobCleanupWorker`（BackgroundService，每日 03:00，照抄 `WfServiceJobScanWorker` + `TenantScopeRunner` 逐租户，分批删 + OperLog + 老化告警）。per-tenant 连接器＝`Wf_Connector`（DataProtection 加密 `AuthJsonEncrypted`）+ 动态 `DbWfConnector : IWfConnector`（走 `IHttpClientFactory`）+ `WebApiExecutor` 解析先查租户表后回落 app 字典 + E-WF-028。时区＝`ITenantClock.GetTenantTimeZone()` 统一 timer/cron 本地时刻解释，null 全等回归。

**Tech Stack:** .NET 8 / EF Core（SQL Server 生产，SQLite 测试）/ xUnit（`CP6.Tests/Wf`、`CP6.Tests/Sys`）/ ASP.NET DataProtection（`Microsoft.AspNetCore.DataProtection`，已在仓库用于 SSO ClientSecret，零新包）/ `IHttpClientFactory`（已由 `AddHttpClient("sso")` 注册）/ Vue3 + Element Plus（含 `el-calendar` 年历——**全仓首次使用，绿地引入**）+ Cp 组件库（`cp6.web/src/views/oa`）/ vitest。

---

## Global Constraints（基线闸 — 每 Task 收口必跑）

- **隔离 worktree**：照 superpowers:using-git-worktrees 建（off main `fb90d75`），分支 `feat/wfs-engine-infra`。
- **后端**：`dotnet test CP6.Tests/CP6.Tests.csproj` **1509（5 skip）→ +N 全绿**。
- **前端**：`npm run test`（vitest）**320 → +N 全绿** + `npm run type-check` + `npm run build`。
- **EF**：`dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context` clean；**本波恰一次迁移 `WfsInfra`**（A-T1 之后任何实体/列/索引改动都算破戒）。
- **零跨模块污染**：只碰 `CP6.Entity/DomainModels/{Sys,Wf}`、`CP6.Core/Services/{Wf,Sys}`、`CP6.WebApi`（Program.cs/BackgroundServices/Controllers/Oa+Sys/Seed）、`cp6.web/src/{api,views}/oa`。不碰 Space/WMS/MES/FIN/PUR 业务文件。每 Task `git show --stat` 复核。
- **五语 i18n**：ZhCN/ZhTW/En/Ja/Ko 五列（`Sys_Lang`），视图全 `t()`，**零硬编码色**（CpTag tone / Design System v1.0 token）。
- **subagent-driven TDD**：每 Task 全新子代理（编码用 Opus 4.8 档）→ 主代理 `git show` diff 复核 → 本地 commit **不 push**。先写失败测试→FAIL→最小实现→PASS→commit。提交信息 `feat(wfs-infra): ...` 中文。
- **测试脚手架**：SQLite in-memory 共享连接，照 `CP6.Tests/Wf/FlowConcurrencyTests.cs` 的 `GenerateCreateScript()` + `Regex.Replace("n?varchar\\(max\\)","TEXT")` 建库；rowversion 靠 AFTER UPDATE 触发器模拟（`Wf_Connector` 有 `[Timestamp]` → 测试基座给它也建同款触发器）；时间全部**注入 `nowUtc`/`nowLocal`**（服务/worker 测试重载）。
- **不重新设计**：spec 决策 D1~D6 + §2~§7 校验/错误码全锁。

## 前置依赖（落码前核实，已侦察）

| 前置 | 现状（已实读核实 2026-07-05） | 对本波影响 |
|---|---|---|
| 二期 hardening（`AdvanceAlongErrorEdge` / 血缘 / 剪枝） | **已随 ServiceTask 波落地**：`FlowEngine.Tokens.cs:113` `internal async Task AdvanceAlongErrorEdge(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowToken token)` 已存在（沿 `IsError==true` 首条出边推进，无错误边→`Suspend`）。 | I-B 直接复用此方法；错误边校验现位置 `FlowSchemaValidator.cs:97-100`（E-WF-017）。 |
| 波③计划（`Wf_TriggerFire` 表） | 计划 `docs/superpowers/plans/2026-07-05-wfs-event-trigger-start.md` 定义 `Wf_TriggerFire { TriggerId, IdempotencyKey, FiredUtc, InstanceId, Source, Error, PayloadHash }`（占坑行＝`InstanceId==null && Error==null`）。**该表由波③迁移 `WfsFlowTrigger` 建，非本波**。 | I-C 清理 worker 消费此表；**本波不建 `Wf_TriggerFire`**——I-C 假定其已存在（波③先并 main）。若波③未并，I-C 的 `Wf_TriggerFire` 清理分支写「表存在即清」的守卫（见 C-T1 注）。 |
| DataProtection 密钥环持久化 | **`AddDataProtection()` 已注册（`Program.cs:515`）但无 `PersistKeysTo*`**——密钥落默认位置（容器本地 FS / 每实例独立密钥环），`deploy/runbook.md:112` 已标 🔴 隐患（SSO ClientSecret 换机/重建即解不开）。 | **密钥环持久化＝I-D 硬前置任务 D-T0**（未落地则 per-tenant 连接器密文换机全瘫）。 |

---

## 现状锚点速查（侦察结论 2026-07-05，executor 免重查）

| 锚点 | 现状（已实读核实） |
|---|---|
| 超时 switch 插点 | `WfTimeoutService.cs:60-95` `switch(action)`：`remind`（软：催办+`task.DueAt` 顺延，不置 Handled）/ `approve` / `reject`（硬：`await _engine.ActAsync(task.Id, SystemActor, approve:…)` + `task.TimeoutHandled=true`）/ `escalate`（换 assignee+双痕）/ `default`（置 Handled 防反复扫）。`SystemActor=Guid.Empty`。**第四 case `errorEdge` 插在 `escalate` 与 `default` 之间**。持 `IFlowEngine _engine` + `IWfNotifier _notifier`；`ScanOnceAsync(DateTime now, CancellationToken ct)` 尾 `await _db.SaveChangesAsync(ct)`（handler 不 SaveChanges，服务统一保存）。 |
| 错误路由入口 | `FlowEngine.Tokens.cs:113` `AdvanceAlongErrorEdge(inst, schema, token)`（**internal，不在 `IFlowEngine`**）→ I-B 需在 `IFlowEngine` 加公有入口 `TimeoutAdvanceErrorEdgeAsync` 委托它。`AdvanceToken`（`:93`）沿 `IsError != true` 边推进（成功路径绝不走错误边，D8 不变量）。 |
| 待办作废口径（errorEdge 照抄对象） | **节点级**清理（非实例级）：仿退回 `AdvancedFlow.cs:153-157`——`foreach (var t in cur) t.Status = FlowTaskStatus.Cancelled;`（当前节点在途待办）+ `VoidPendingFormTos(inst.Id, task.NodeId, task.TokenId, task.StageIndex, task.StageRound, FlowFormToStatus.SentBack)`。**不用**实例级 `CancelAllActiveTokens+VoidPendingFormTos(inst.Id)`（那是驳回 `FlowEngine.cs:224-225` 的连坐语义，会误伤兄弟分支）。`VoidPendingFormTos` 签名 `FlowEngine.ReadModel.cs:163`（`instanceId, nodeId?, tokenId?, stageIndex?, stageRound?, status`）。 |
| 错误边来源校验现位置 | `FlowSchemaValidator.cs:97-100`：`serviceIds = 全 serviceTask 节点 Id`；`E-WF-017`＝任一节点 IsError 出边 >1 **或** IsError 边来源不在 serviceIds。**放宽点**：来源集合由「仅 serviceTask」→ 类型集合 `{serviceTask, approval, subFlow}`（跨 spec 单一常量，见共享契约）。`T(n)` 把 Type 转小写序数比较。 |
| ComputeDueUtc 现状三模式 | `ServiceTaskNodeHandler.cs:153` `internal static DateTime ComputeDueUtc(FlowNode node, string? varsJson)`：`duration`（`now+ParseDuration`）/ `untilDate`（`ParseLocalDateToUtc` 按**服务器本地 tz** 解释→UTC，`:179`）/ `untilExpr`（表达式求值出日期串→同 untilDate）/ `default`→`nowUtc`。调用点 `:100`（`kind==ServiceKind.Timer ? ComputeDueUtc(node, inst.VarsJson) : nowUtc`）。`ServiceDelayMode`/`ServiceDelayValue` 是 `FlowNode` POCO 字段（`FlowSchema.cs:81-82`）。**第四值 `workdays` 加此处**。 |
| FlowNode POCO | `FlowSchema.cs:16` `class FlowNode`：`TimeoutHours?`(`:40`)/`TimeoutAction?`(`:43`,"remind/approve/reject/escalate")/`EscalateTo?`(`:46`)/`ServiceKind?`(`:71`)/`ServiceDelayMode?`(`:81`)/`ServiceDelayValue?`(`:82`)。`FlowEdge`(`:89`)：`IsError?`(`:101`)。**新增 `ServiceHttpMethod?`/`ServiceTimeoutSec?` 追加此 POCO（零迁移）**。 |
| 连接器契约 & 解析 | `IWfConnector { string Name; string DisplayName; Task<ServiceTaskResult> CallAsync(string pathTemplate, string? paramsJson, ServiceTaskContext ctx); }`（`IWfConnector.cs`；波①票追加的 `MaxCallDuration` 见 spec §5，若已落则实现同步）。`WebApiExecutor.cs:19-26` ctor 收 `IEnumerable<IWfConnector>` → `Dictionary<Name, IWfConnector>(OrdinalIgnoreCase)`；`ExecuteAsync` `:42-44` 按 `r.ConnectorName` 查字典未命中即 `E-WF-018 连接器未注册`。**解析改造点**：先查租户表（`Wf_Connector` Enabled 行→包 `DbWfConnector`）→ 未命中回落 `_connectors` 字典。 |
| 连接器 app 级注册 | `Program.cs:136-138`：`AddScoped<IServiceTaskExecutor, WebApiExecutor>()` + `AddScoped<IWfConnector, EchoConnector>()`（样例 erpEcho）。**EchoConnector 等 app 级注册零改动**（D5 向后兼容）。`IHttpClientFactory` 已由 `AddHttpClient("sso")` 注册（`Program.cs:520` 一带），真 HTTP 连接器可直接注入。 |
| DataProtection 用法先例 | `TenantSsoConfigService.cs:15-20`：ctor 注入 `IDataProtectionProvider dp` → `_protector = dp.CreateProtector("固定 purpose 串")`；`Protect`/`Unprotect` 加解密 ClientSecret。测试 provider＝`DataProtectionProvider.Create("CP6.Tests")`（`TenantSsoConfigServiceTests.cs:16`）。**本波 purpose 串＝`"Wfs.Connector.Auth"`（spec §5.2）**。`AddDataProtection()` 在 `Program.cs:515`（**无 PersistKeysTo → D-T0 补**）。 |
| worker 骨架 | `WfServiceJobScanWorker.cs`（51 行）：`Interval=20s`；`_workerId=$"{Environment.MachineName}:{Guid.NewGuid():N}"`；`ExecuteAsync` while 内 `TenantScopeRunner.ForEachTenantAsync(_scopeFactory, async (sp, tenantId, ct)=>{ ... }, _logger, stoppingToken)`；`catch(OperationCanceledException) when(...) { throw; } catch(Exception ex){ LogError }`；`await Task.Delay(Interval, stoppingToken)`。清理 worker 照抄，周期改「每日一轮」。 |
| TenantScopeRunner | `TenantScopeRunner.ForEachTenantAsync(scopeFactory, body, logger?, ct)`：先 scope 用 `ITenantEnumerator.ListActiveAsync()` 取启用租户 → 逐租户 `CreateScope()` → `scope.ServiceProvider.GetRequiredService<ITenantContext>().CurrentTenantId = tenantId` → 跑 body；单租户异常记日志跳过继续。service 层零租户感知（全局过滤 + `StampTenant` 盖章）。 |
| 节点处理器 DI | `Program.cs:109-114`：六个 `AddScoped<INodeHandler, XxxNodeHandler>()`，第六＝`ServiceTaskNodeHandler`（ctor 注入 `IEnumerable<IServiceTaskExecutor>?`）。DI 从容器解析 ctor 参数——**新增 ctor 参 `IWorkdayCalculator? workdays=null, WfsInfraOptions? opts=null`（I-A）/`ITenantClock? clock=null`（I-E）** 会被 DI 自动注入；带默认值 → `FlowEngine.DefaultHandlers()` fallback（`FlowEngine.cs:42`，空 executor）与单测 `new ServiceTaskNodeHandler(...)` 零破坏（workdays 缺服务→降级立即）。 |
| Sys_Tenant 实体 | `Sys_Tenant.cs`：`: BaseEntity, IAuditable`（**共享表**，非 BaseTenantEntity，不参与行级过滤）。字段 `TenantCode/TenantName/Enable/ExpireDate/Remark/TwoFactorMode`。**新增 `TimeZoneId?` 列（迁移 `WfsInfra` 之三）**。`Id` 即 TenantId。 |
| 权限/菜单 seed | MenuAction 模型：`Sys_MenuAction{MenuId,ActionCode,ActionName,Sort}` + `Sys_RoleAction{RoleId=1,MenuId,ActionCode}` 幂等块（范本 `Program.cs:850-856`）。MenuKey＝RoutePath 派生（`Trim('/').Replace('/','-')`）。控制器 `[RequirePermission(menuKey, action)]`（`CP6.Core/Auth/RequirePermissionAttribute.cs`）。年历页/连接器 tab 权限沿 `oa-flow-admin` 家族（波③映射②口径）。 |
| i18n seed 注册 | `I18nOa*ScreenSeed.cs` 静态 `Sys_Lang[] Items`（五列 + LangKey；错误码直接以 `E-WF-0xx` 作 LangKey）；`Program.cs:1793-1819` `.Concat(...Items)` 链追加（末行现为 `I18nOaServiceTaskScreenSeed`，E-WF-016/017/018）。**本波追加 `I18nOaEngineInfraScreenSeed`（E-WF-027/028 + 日历/连接器/时区画面词条）**。 |
| 迁移范本 | `20260629142700_WfsServiceTask.cs`：`CreateTable` + `CreateIndex`（普通/`unique:true`/带 `filter:`）；`RowVersion` 列 `type:"rowversion", rowVersion:true`；`BaseTenantEntity` 列 `Creator/CreateDate/Modifier/ModifyDate/TenantId`。迁移命令：`dotnet ef migrations add <名> --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context`；**不手写迁移文件**。 |
| 年历页先例 | **全仓零 `el-calendar`/`ElCalendar`/`WorkCalendar` 使用（grep 确认）→ 年历勾选管理页为绿地**。参照既有 OA 管理页布局（`FlowAdmin.vue` CpPageShell 壳）+ Element Plus `el-calendar` 的 `#date-cell` 插槽自绘工作/休息态 + 勾选反转。 |

## spec ↔ 现状映射（不改 spec，落地口径）

| # | spec 表述 | 现状/落地口径 |
|---|---|---|
| ① | 三表改动「合并一次迁移 `WfsInfra`」（D6/§10 I-A） | 三处 schema 变更横跨 I-A（`Sys_WorkCalendar`）/I-D（`Wf_Connector`）/I-E（`Sys_Tenant.TimeZoneId`）。为「恰一次迁移」，**A-T1 一次性声明全部三处**（两实体+一列+DbSet+索引）再 `dotnet ef` 生成；I-D/I-E 消费既有 schema 零新迁移。`FlowNode` 双字段（⑤）是 POCO 零迁移。 |
| ② | `workdays` 按「租户时区」取当日（§2.3） | `ITenantClock` 在 I-E 落地（spec §10「I-E 时区消费 I-A 的 workdays」）。**I-A 的 workdays 用服务器本地 tz 作 app 默认时区占位**（与既有 `ComputeDueUtc` untilDate `ParseLocalDateToUtc` 同款「app 默认=服务器本地、字段带 Utc 未来接 per-tenant tz 零返工」演进）；I-E 把该 tz 源换成 `ITenantClock.GetTenantTimeZone()`。 |
| ③ | `AdvanceAlongErrorEdge(token)`（§3.1） | 现签名 `AdvanceAlongErrorEdge(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowToken token)`（internal）。approval 超时经 I-B 新增的 `IFlowEngine.TimeoutAdvanceErrorEdgeAsync(taskId, actorId, ct)` 定位 inst/schema/token 后委托它。 |
| ④ | 「作废该节点在途待办（对齐 reject 分支清理口径）」（§3） | reject（`FlowEngine.cs:224`）是**实例级**连坐（`CancelAllActiveTokens+VoidPendingFormTos(inst.Id)`）。errorEdge 是**路由**非终止 → 用**节点级**作废（仿退回 `AdvancedFlow.cs:153-157`：cancel 当前节点待办 + `VoidPendingFormTos` 带 nodeId/tokenId 参），不连坐兄弟。「对齐清理口径」= 复用同款 `VoidPendingFormTos` 作废机制，作用域按 errorEdge 语义收敛到节点。 |
| ⑤ | 密钥环持久化「若现状未配置作为前置」（§5.2） | **现状：`AddDataProtection()` 已注册但无 `PersistKeysTo*`**（`Program.cs:515`）→ 确认未配置 → **D-T0 前置任务**。落地＝`PersistKeysToFileSystem`（共享卷路径，配置 `DataProtection:KeyPath`）+ `SetApplicationName`，**不引 DataProtection EF 存储**（那会新增 `DataProtectionKeys` 表破「恰一次迁移」）。 |
| ⑥ | 错误边来源集合含 subFlow（§10 接缝） | 单一常量 `FlowSchemaValidator.ErrorEdgeSourceTypes = {serviceTask, approval, subFlow}`（本 infra 波**先落地写全集**）；子流程 spec（`2026-07-05-wfs-subflow-design.md` §5）**只加 subFlow 放行测试**，不重复定义常量。两处代码注释互指。 |
| ⑦ | 连接器 `TimeoutSec ≥ 租约 LeaseDuration` 校验（E-WF-028，§5.2） | 租约常量取波①启动护栏同源（`ServiceJob` lease 时长；侦察 `WfServiceJobService` 的 `LeaseDuration`/`Trunc` 常量，保存时前移比对）。**核实项**：lease 时长常量的确切名与值——D-T1 落地前 grep 确认，保存校验用同一常量。 |

---

## 共享契约（所有 Task 用这些**精确**名字，前后一致）

- **实体**（均 `: BaseTenantEntity`，提供 `Id/Creator/CreateDate/Modifier/ModifyDate/TenantId`）：
  - `Sys_WorkCalendar { DateTime Date; bool IsWorkday; string? Note; }`（`CP6.Entity/DomainModels/Sys/`；unique `(TenantId,Date)`）。
  - `Wf_Connector { string Name=""; string DisplayName=""; string BaseUrl=""; string? AuthJsonEncrypted; int TimeoutSec=30; bool Enabled; byte[]? RowVersion; }`（`CP6.Entity/DomainModels/Wf/`；unique `(TenantId,Name)`）。
  - `Sys_Tenant.TimeZoneId`（`string?`，IANA/Windows id）。
- **FlowNode POCO 追加**（`FlowSchema.cs`，零迁移）：`string? ServiceHttpMethod;`（GET/POST/PUT/DELETE）`int? ServiceTimeoutSec;`。
- **配置**（`WfsInfraOptions`，`CP6.Core/Services/Wf/WfsInfraOptions.cs`，绑 `Wfs` 段，`AddSingleton`）：
  ```csharp
  public sealed class WfsInfraOptions
  {
      public int WorkdayFireHour { get; set; } = 9;         // Wfs:WorkdayFireHour
      public int CleanupRetentionDays { get; set; } = 180;   // Wfs:CleanupRetentionDays（<=0 禁用清理）
      public int StaleReservationAlertDays { get; set; } = 7; // Wfs:StaleReservationAlertDays
      public string? DefaultTimeZone { get; set; }            // Wfs:DefaultTimeZone（null→服务器本地）
  }
  ```
- `IWorkdayCalculator`（`CP6.Core/Services/Wf/WorkdayCalculator.cs`）：
  - `Task<DateTime> AddWorkdaysAsync(DateTime dateLocal, int n, CancellationToken ct);`（n≥1；当天不算；跳非工作日；连续 366 天无工作日→抛快速失败）
  - `Task<bool> IsWorkdayAsync(DateTime dateLocal, CancellationToken ct);`（例外表命中→行.IsWorkday；否则 Mon–Fri）
- `ITenantClock`（`CP6.Core/Services/Wf/ITenantClock.cs`，I-E）：
  - `TimeZoneInfo GetTenantTimeZone();`（`Sys_Tenant.TimeZoneId`→解析；缺省 `WfsInfraOptions.DefaultTimeZone`；再缺省 `TimeZoneInfo.Local`）
- `IFlowEngine` 追加（I-B）：`Task TimeoutAdvanceErrorEdgeAsync(Guid taskId, Guid actorId, CancellationToken ct = default);`（幂等：任务非 Pending→零动作）
- `FlowSchemaValidator.ErrorEdgeSourceTypes`（`internal static readonly HashSet<string>`，`OrdinalIgnoreCase`）`= { "serviceTask", "approval", "subFlow" }`。
- **DbWfConnector**（`CP6.Core/Services/Wf/Executors/DbWfConnector.cs`）：`sealed class DbWfConnector : IWfConnector`，从 `Wf_Connector` 行 + 解密 `AuthJsonEncrypted` 构造，`CallAsync` 走 `IHttpClientFactory` 命名客户端（超时=`TimeoutSec`）。
- **JapaneseHolidaySeed**（`CP6.WebApi/Seed/JapaneseHolidaySeed.cs`）：`static (int Y,int M,int D,string Note)[] Items`（2026–2027 全 35 日期，见 A-T2）+ `static Sys_WorkCalendar[] For(Guid tenantId)`（幂等去重靠 seed 侧 `(TenantId,Date)` Any 检查）。
- **错误码**（i18n LangKey）：
  - `E-WF-027`：`TimeoutAction=errorEdge` 的节点必须有 IsError 出边；IsError 边来源 ∈ `{serviceTask, approval, subFlow}`（静态 + validateClient 镜像）。
  - `E-WF-028`：连接器/节点 `TimeoutSec ≥ 租约` → 拒绝保存；`TimeZoneId` 不可解析 → 拒绝保存（保存时服务层）。
  - `workdays` 值非正整数 → 并入既有 `E-WF-016` 家族口径。
- **DataProtection purpose 串**：`"Wfs.Connector.Auth"`。

---

## File Structure（创建/修改清单，每文件一职责）

**后端 `CP6.Entity`**
- Create `CP6.Entity/DomainModels/Sys/Sys_WorkCalendar.cs`
- Create `CP6.Entity/DomainModels/Wf/Wf_Connector.cs`
- Modify `CP6.Entity/DomainModels/Sys/Sys_Tenant.cs`（+`TimeZoneId?`）

**后端 `CP6.Core`**
- Create `CP6.Core/Services/Wf/WfsInfraOptions.cs`
- Create `CP6.Core/Services/Wf/WorkdayCalculator.cs`（`IWorkdayCalculator` + 实现）
- Create `CP6.Core/Services/Wf/ITenantClock.cs`（`ITenantClock` + `TenantClock` 实现，I-E）
- Create `CP6.Core/Services/Wf/WorkCalendarService.cs`（`IWorkCalendarService`：年历 CRUD/反转/导入日本假日到当前租户）
- Create `CP6.Core/Services/Wf/WfConnectorService.cs`（`IWfConnectorService`：连接器 CRUD/加密写/掩码读/E-WF-028）
- Create `CP6.Core/Services/Wf/Executors/DbWfConnector.cs`（动态租户连接器）
- Modify `CP6.Core/Services/Wf/Executors/WebApiExecutor.cs`（解析先租户表后 app 兜底 + 节点覆盖优先，I-D/I-E）
- Modify `CP6.Core/Services/Wf/WfTimeoutService.cs`（`errorEdge` case，I-B）
- Modify `CP6.Core/Services/Wf/FlowEngine.cs` / `FlowEngine.Tokens.cs`（`IFlowEngine.TimeoutAdvanceErrorEdgeAsync` + 实现，I-B）
- Modify `CP6.Core/Services/Wf/FlowSchemaValidator.cs`（`ErrorEdgeSourceTypes` 常量 + E-WF-027，I-B）
- Modify `CP6.Core/Services/Wf/FlowSchema.cs`（FlowNode 双字段，I-E）
- Modify `CP6.Core/Services/Wf/NodeHandlers/ServiceTaskNodeHandler.cs`（`workdays` 模式 + 节点 HTTP 覆盖读取，I-A/I-E）
- Modify `CP6.Core/EFDbContext/CP6Context.cs`（两 DbSet + 两 unique 索引，A-T1）
- Create 迁移 `CP6.Core/Migrations/<ts>_WfsInfra.cs`（`dotnet ef` 生成，**恰一次**）

**后端 `CP6.WebApi`**
- Create `CP6.WebApi/BackgroundServices/WfServiceJobCleanupWorker.cs`（I-C）
- Create `CP6.WebApi/Controllers/Oa/WorkCalendarController.cs`（年历 CRUD/导入，I-A）
- Create `CP6.WebApi/Controllers/Oa/WfConnectorController.cs`（连接器 CRUD，I-D）
- Modify `CP6.WebApi/Controllers/Sys/TenantController.cs`（若存在；时区下拉/保存，I-E；否则最小新增端点）
- Create `CP6.WebApi/Seed/JapaneseHolidaySeed.cs`（I-A）
- Create `CP6.WebApi/Seed/I18nOaEngineInfraScreenSeed.cs`（I-F）
- Modify `CP6.WebApi/Program.cs`（DataProtection 持久化 D-T0 + DI + 日本假日 seed 默认租户 + 菜单/权限 seed + i18n concat + `WfServiceJobCleanupWorker` HostedService）

**前端 `cp6.web`**
- Create `cp6.web/src/api/oa/workCalendar.ts` / `wfConnector.ts`
- Create `cp6.web/src/views/oa/admin/WorkCalendar.vue`（el-calendar 年历勾选 + 空态导入按钮，I-A）
- Create `cp6.web/src/views/oa/admin/WfConnectorPanel.vue` + `WfConnectorDialog.vue`（连接器 tab，I-D）
- Modify designer 面板（timer 延时模式 radio 第四项 + approval 超时动作下拉 + webApi 段两输入 + validateClient E-WF-027，I-A/I-B/I-E）
- Modify 租户管理页（时区下拉 + 自愈口径提示，I-E）
- Create 对应 `__tests__/*.spec.ts`（vitest）

**测试 / QA**
- Create `CP6.Tests/Wf/*.cs`、`CP6.Tests/Sys/*.cs`（各 Task）
- Create `docs/superpowers/qa/wfs-engine-infra/{README.md,seed.sql,qa_infra.ps1}`（gstack harness，只写不跑）

---

# Wave I-A — 迁移 + 工作日历 + `workdays` 延时模式

> **依赖：无（波首）。** A-T1 落全部三处 schema（含 I-D/I-E 的表/列）+ 一次迁移，之后不再有迁移。

## Task A-T1: 三 schema 变更 + DbSet/索引 + 一次迁移 `WfsInfra`

**Files:**
- Create `CP6.Entity/DomainModels/Sys/Sys_WorkCalendar.cs`
- Create `CP6.Entity/DomainModels/Wf/Wf_Connector.cs`
- Modify `CP6.Entity/DomainModels/Sys/Sys_Tenant.cs`
- Modify `CP6.Core/EFDbContext/CP6Context.cs`
- Create 迁移 `CP6.Core/Migrations/<ts>_WfsInfra.cs`
- Test `CP6.Tests/Wf/WfsInfraModelTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/WfsInfraModelTests.cs
using System;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Xunit;

namespace CP6.Tests;

public class WfsInfraModelTests
{
    [Fact]
    public void Sys_WorkCalendar_Defaults()
    {
        var c = new Sys_WorkCalendar { Date = new DateTime(2026, 1, 1), IsWorkday = false, Note = "元日" };
        Assert.False(c.IsWorkday);
        Assert.Equal("元日", c.Note);
        Assert.Equal(new DateTime(2026, 1, 1), c.Date);
    }

    [Fact]
    public void Wf_Connector_Defaults()
    {
        var k = new Wf_Connector { Name = "erpProd", DisplayName = "ERP 生产" };
        Assert.Equal("erpProd", k.Name);
        Assert.Equal("", k.BaseUrl);
        Assert.Equal(30, k.TimeoutSec);
        Assert.False(k.Enabled);
        Assert.Null(k.AuthJsonEncrypted);
        Assert.Null(k.RowVersion);
    }

    [Fact]
    public void Sys_Tenant_TimeZoneId_Nullable()
    {
        var t = new Sys_Tenant { TenantCode = "t1", TenantName = "T1" };
        Assert.Null(t.TimeZoneId);
        t.TimeZoneId = "Asia/Tokyo";
        Assert.Equal("Asia/Tokyo", t.TimeZoneId);
    }
}
```

- [ ] **Step 2: 跑测试验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter WfsInfraModelTests`（编译失败：类型/成员不存在）。

- [ ] **Step 3: 实现三 schema**

`Sys_WorkCalendar.cs`（spec §2.1 逐字）：

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CP6.Entity;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>工作日历例外表（WFS infra ①，spec §2.1）。周末默认非工作日，本表双向反转：
/// IsWorkday=true=补班（周末却上班）；false=假日（工作日却休）。unique(TenantId,Date)。</summary>
[Table("Sys_WorkCalendar")]
public class Sys_WorkCalendar : BaseTenantEntity
{
    /// <summary>例外日（date 粒度，存本地日期午夜）。</summary>
    public DateTime Date { get; set; }

    /// <summary>true=补班；false=假日。</summary>
    public bool IsWorkday { get; set; }

    /// <summary>"元日" / "振替休日" / "臨時休業" 等。</summary>
    [MaxLength(100)]
    public string? Note { get; set; }
}
```

`Wf_Connector.cs`（spec §5.1 逐字；`AuthJsonEncrypted` 用 `nvarchar(max)` 装密文，其余字符串列限长便于索引/审计）：

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CP6.Entity;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>租户级连接器（WFS infra ④，spec §5.1）。解析键 Name（unique(TenantId,Name)）；
/// AuthJsonEncrypted＝DataProtection 密文，读接口永不回显明文。</summary>
[Table("Wf_Connector")]
public class Wf_Connector : BaseTenantEntity
{
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(200)] public string DisplayName { get; set; } = "";
    [MaxLength(500)] public string BaseUrl { get; set; } = "";

    /// <summary>DataProtection 密文：{type:"apiKey|basic|bearer", ...}。null=无认证。</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string? AuthJsonEncrypted { get; set; }

    public int TimeoutSec { get; set; } = 30;
    public bool Enabled { get; set; }

    [Timestamp] public byte[]? RowVersion { get; set; }
}
```

`Sys_Tenant.cs` 追加（spec §6 ⑥）：

```csharp
    /// <summary>租户时区（IANA/Windows id，TimeZoneInfo.FindSystemTimeZoneById 可解析）。
    /// null=沿用 app 默认（Wfs:DefaultTimeZone→服务器本地），存量行为完全不变（WFS infra ⑥）。</summary>
    [MaxLength(64)]
    public string? TimeZoneId { get; set; }
```

`CP6Context.cs` DbSet（放 `Wf_ServiceJobs` 声明同块）：

```csharp
/// <summary>工作日历例外（WFS infra ①，spec §2.1）</summary>
public DbSet<Sys_WorkCalendar> Sys_WorkCalendars { get; set; }
/// <summary>租户级连接器（WFS infra ④，spec §5.1）</summary>
public DbSet<Wf_Connector> Wf_Connectors { get; set; }
```

`OnModelCreating`（放 `Wf_ServiceJob` 索引块之后）：

```csharp
modelBuilder.Entity<Sys_WorkCalendar>(b =>
{
    b.HasIndex(x => new { x.TenantId, x.Date }).IsUnique().HasDatabaseName("UX_Sys_WorkCalendar_Date");
});
modelBuilder.Entity<Wf_Connector>(b =>
{
    b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique().HasDatabaseName("UX_Wf_Connector_Name");
});
```

- [ ] **Step 4: 跑测试验证 PASS** — `--filter WfsInfraModelTests`。

- [ ] **Step 5: 生成迁移**（本波**唯一**一次）：

```bash
dotnet ef migrations add WfsInfra --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context
dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context   # 应 clean
```

检查 `Up()` 恰＝建 `Sys_WorkCalendar` + `Wf_Connector` 两表（各带 unique 索引）+ `Sys_Tenant` 加 `TimeZoneId` 列，**零其他改动、零回填**。**不手写迁移文件**。

- [ ] **Step 6: 全量闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "Wf|Sys"
git add -A && git commit -m "feat(wfs-infra): A-T1 三 schema(Sys_WorkCalendar+Wf_Connector+Sys_Tenant.TimeZoneId)+DbSet/索引+一次迁移 WfsInfra"
```

---

## Task A-T2: `IWorkdayCalculator` + 日本法定假日 seed + 默认租户植入

**Files:**
- Create `CP6.Core/Services/Wf/WfsInfraOptions.cs`
- Create `CP6.Core/Services/Wf/WorkdayCalculator.cs`
- Create `CP6.WebApi/Seed/JapaneseHolidaySeed.cs`
- Modify `CP6.WebApi/Program.cs`（DI `WfsInfraOptions` + `IWorkdayCalculator` + 默认租户假日 seed）
- Test `CP6.Tests/Wf/WorkdayCalculatorTests.cs`、`CP6.Tests/Wf/JapaneseHolidaySeedTests.cs`

- [ ] **Step 1: 建共享测试基座（本波 SQLite 测试复用）**

```csharp
// CP6.Tests/Wf/WfsInfraTestHarness.cs —— GenerateCreateScript + TEXT 替换建库 + Wf_Connector rowversion 触发器
using System;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

internal static class WfsInfraTestHarness
{
    internal sealed class SqliteCP6Context : CP6Context
    {
        public SqliteCP6Context(DbContextOptions<CP6Context> o) : base(o) { }
        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);
            mb.Entity<Wf_Connector>().ToTable(t => t.HasTrigger("trg_Wf_Connector_RowVersion"));
        }
    }

    public static SqliteCP6Context Ctx(SqliteConnection c)
        => new(new DbContextOptionsBuilder<CP6Context>().UseSqlite(c).Options);

    public static SqliteConnection NewSqliteWithSchema()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using (var setup = Ctx(conn))
        {
            var script = Regex.Replace(setup.Database.GenerateCreateScript(),
                                       "n?varchar\\(max\\)", "TEXT", RegexOptions.IgnoreCase);
            Exec(conn, script);
        }
        Exec(conn,
            "CREATE TRIGGER trg_Wf_Connector_RowVersion AFTER UPDATE ON \"Wf_Connector\" " +
            "BEGIN UPDATE \"Wf_Connector\" SET \"RowVersion\" = randomblob(8) WHERE \"Id\" = NEW.\"Id\"; END;");
        return conn;
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
```

- [ ] **Step 2: 写失败测试（日历例外反转矩阵 + AddWorkdays 跨假日/振替 + 366 防死循环）**

```csharp
// CP6.Tests/Wf/WorkdayCalculatorTests.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using Xunit;
using static CP6.Tests.WfsInfraTestHarness;

namespace CP6.Tests;

public class WorkdayCalculatorTests
{
    // 2026-05-04(Mon,みどりの日 假日) / 05-05(Tue,こどもの日 假日) / 05-06(Wed,振替休日) / 05-07(Thu,普通工作日)
    // 2026-05-09(Sat 普通周末) / 05-10(Sun 普通周末) / 2026-05-16(Sat 补班演示)
    private static async Task SeedCalendarAsync(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        using var db = Ctx(conn);
        db.Sys_WorkCalendars.AddRange(
            new Sys_WorkCalendar { Date = new DateTime(2026, 5, 4), IsWorkday = false, Note = "みどりの日" },
            new Sys_WorkCalendar { Date = new DateTime(2026, 5, 5), IsWorkday = false, Note = "こどもの日" },
            new Sys_WorkCalendar { Date = new DateTime(2026, 5, 6), IsWorkday = false, Note = "振替休日" },
            new Sys_WorkCalendar { Date = new DateTime(2026, 5, 16), IsWorkday = true, Note = "臨時出勤" });   // 周六补班
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task IsWorkday_ExceptionReversalMatrix()
    {
        using var conn = NewSqliteWithSchema();
        await SeedCalendarAsync(conn);
        using var db = Ctx(conn);
        var cal = new WorkdayCalculator(db);

        Assert.False(await cal.IsWorkdayAsync(new DateTime(2026, 5, 4), CancellationToken.None));  // 假日（工作日却休）
        Assert.True(await cal.IsWorkdayAsync(new DateTime(2026, 5, 16), CancellationToken.None));   // 补班（周末却上班）
        Assert.False(await cal.IsWorkdayAsync(new DateTime(2026, 5, 9), CancellationToken.None));   // 普通周六
        Assert.True(await cal.IsWorkdayAsync(new DateTime(2026, 5, 7), CancellationToken.None));    // 普通周四
    }

    [Fact]
    public async Task AddWorkdays_SkipsWeekendsHolidaysAndSubstitute()
    {
        using var conn = NewSqliteWithSchema();
        await SeedCalendarAsync(conn);
        using var db = Ctx(conn);
        var cal = new WorkdayCalculator(db);

        // 起点 2026-05-01(Fri 普通工作日)，顺延 1 工作日：跳 05-02(Sat)/03(Sun,系普通周末)/04(假)/05(假)/06(振替) → 05-07(Thu)
        var r1 = await cal.AddWorkdaysAsync(new DateTime(2026, 5, 1), 1, CancellationToken.None);
        Assert.Equal(new DateTime(2026, 5, 7), r1.Date);

        // 起点 2026-05-15(Fri)，顺延 1：05-16 是补班工作日 → 命中
        var r2 = await cal.AddWorkdaysAsync(new DateTime(2026, 5, 15), 1, CancellationToken.None);
        Assert.Equal(new DateTime(2026, 5, 16), r2.Date);
    }

    [Fact]
    public async Task AddWorkdays_TimeComponentStripped_ReturnsDateMidnight()
    {
        using var conn = NewSqliteWithSchema();
        await SeedCalendarAsync(conn);
        using var db = Ctx(conn);
        var cal = new WorkdayCalculator(db);

        var r = await cal.AddWorkdaysAsync(new DateTime(2026, 5, 1, 14, 30, 0), 1, CancellationToken.None);
        Assert.Equal(new DateTime(2026, 5, 7), r);   // 当天不算、返回日期午夜
    }

    [Fact]
    public async Task AddWorkdays_NonPositive_Throws()
    {
        using var conn = NewSqliteWithSchema();
        using var db = Ctx(conn);
        var cal = new WorkdayCalculator(db);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => cal.AddWorkdaysAsync(new DateTime(2026, 1, 1), 0, CancellationToken.None));
    }

    [Fact]
    public async Task AddWorkdays_366ConsecutiveNonWorkdays_FailsFast_NoInfiniteLoop()
    {
        using var conn = NewSqliteWithSchema();
        using (var db = Ctx(conn))
        {
            // 灌满 2026-01-02 起 400 天全设假日 → 无工作日
            var start = new DateTime(2026, 1, 2);
            for (int i = 0; i < 400; i++)
                db.Sys_WorkCalendars.Add(new Sys_WorkCalendar { Date = start.AddDays(i), IsWorkday = false, Note = "灌满" });
            await db.SaveChangesAsync();
        }
        using var db2 = Ctx(conn);
        var cal = new WorkdayCalculator(db2);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => cal.AddWorkdaysAsync(new DateTime(2026, 1, 1), 1, CancellationToken.None));
        Assert.Contains("E-WF-016", ex.Message);
    }
}
```

```csharp
// CP6.Tests/Wf/JapaneseHolidaySeedTests.cs
using System;
using System.Linq;
using CP6.WebApi.Seed;
using Xunit;

namespace CP6.Tests;

public class JapaneseHolidaySeedTests
{
    [Fact]
    public void Items_Cover2026And2027_35Dates_AllDistinct()
    {
        Assert.Equal(35, JapaneseHolidaySeed.Items.Length);
        Assert.Equal(18, JapaneseHolidaySeed.Items.Count(x => x.Y == 2026));
        Assert.Equal(17, JapaneseHolidaySeed.Items.Count(x => x.Y == 2027));
        var dates = JapaneseHolidaySeed.Items.Select(x => new DateTime(x.Y, x.M, x.D)).ToList();
        Assert.Equal(dates.Count, dates.Distinct().Count());
    }

    [Fact]
    public void Items_ContainKeyComputedDates()
    {
        bool Has(int y, int m, int d, string note)
            => JapaneseHolidaySeed.Items.Any(x => x.Y == y && x.M == m && x.D == d && x.Note == note);

        Assert.True(Has(2026, 1, 12, "成人の日"));    // 1 月第 2 月曜
        Assert.True(Has(2026, 3, 20, "春分の日"));    // 2026 春分
        Assert.True(Has(2026, 5, 6, "振替休日"));     // 5/3(日)→振替
        Assert.True(Has(2026, 9, 22, "国民の休日"));  // 9/21(敬老,月) 与 9/23(秋分,水) 之间的挟まれ日
        Assert.True(Has(2027, 3, 21, "春分の日"));    // 2027 春分
        Assert.True(Has(2027, 3, 22, "振替休日"));    // 2027 春分 3/21(日)→振替
        Assert.True(Has(2027, 7, 19, "海の日"));      // 7 月第 3 月曜
    }

    [Fact]
    public void For_StampsTenant_AllHolidaysNonWorkday()
    {
        var tenant = Guid.NewGuid();
        var rows = JapaneseHolidaySeed.For(tenant);
        Assert.Equal(35, rows.Length);
        Assert.All(rows, r => Assert.Equal(tenant, r.TenantId));
        Assert.All(rows, r => Assert.False(r.IsWorkday));
    }
}
```

- [ ] **Step 3: 跑验证 FAIL** — `--filter "WorkdayCalculatorTests|JapaneseHolidaySeedTests"`。

- [ ] **Step 4: 实现 `WfsInfraOptions`（见共享契约代码块）+ `WorkdayCalculator`**

```csharp
// CP6.Core/Services/Wf/WorkdayCalculator.cs
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>工作日历纯查询服务（WFS infra ①，spec §2.2）。IsWorkday=例外表命中?行值:(周一~五)；
/// AddWorkdays 当天不算、跳非工作日、连续 366 天无工作日快速失败防死循环。date 按租户时区解释（消费点负责换算）。</summary>
public interface IWorkdayCalculator
{
    Task<DateTime> AddWorkdaysAsync(DateTime dateLocal, int n, CancellationToken ct);
    Task<bool> IsWorkdayAsync(DateTime dateLocal, CancellationToken ct);
}

public sealed class WorkdayCalculator : IWorkdayCalculator
{
    private const int MaxScanDays = 366;
    private readonly CP6Context _db;
    public WorkdayCalculator(CP6Context db) => _db = db;

    public async Task<bool> IsWorkdayAsync(DateTime dateLocal, CancellationToken ct)
    {
        var d = dateLocal.Date;
        var ex = await _db.Sys_WorkCalendars.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Date == d, ct);
        if (ex != null) return ex.IsWorkday;
        return dateLocal.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    public async Task<DateTime> AddWorkdaysAsync(DateTime dateLocal, int n, CancellationToken ct)
    {
        if (n < 1) throw new ArgumentOutOfRangeException(nameof(n), "工作日步数须 ≥1");
        var cursor = dateLocal.Date;
        int added = 0, scanned = 0;
        while (added < n)
        {
            cursor = cursor.AddDays(1);
            if (++scanned > MaxScanDays)
                throw new InvalidOperationException("E-WF-016 连续 366 天无工作日，疑似假日表异常，拒绝无限顺延");
            if (await IsWorkdayAsync(cursor, ct)) added++;
        }
        return cursor;
    }
}
```

- [ ] **Step 5: 实现 `JapaneseHolidaySeed`（2026–2027 全 35 日期，已算出，executor 不做日历推算）**

```csharp
// CP6.WebApi/Seed/JapaneseHolidaySeed.cs
using System;
using System.Linq;
using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>日本法定假日 seed（WFS infra ①，spec §2.1；2026–2027 两年逐日期，含振替休日与国民の休日）。
/// 振替休日＝法定假日落周日则顺延至次一非假日（2026-05-03(日)→05-06、2027-03-21(日)→03-22）；
/// 国民の休日＝两法定假日之间夹一平日（2026-09-21 敬老の日 与 09-23 秋分の日 之间的 09-22）。
/// 春分/秋分逐年官报确定：2026 春分 3/20·秋分 9/23；2027 春分 3/21·秋分 9/23。</summary>
public static class JapaneseHolidaySeed
{
    public static readonly (int Y, int M, int D, string Note)[] Items =
    {
        // ── 2026（18）──
        (2026, 1, 1, "元日"),
        (2026, 1, 12, "成人の日"),          // 1 月第 2 月曜
        (2026, 2, 11, "建国記念の日"),
        (2026, 2, 23, "天皇誕生日"),
        (2026, 3, 20, "春分の日"),
        (2026, 4, 29, "昭和の日"),
        (2026, 5, 3, "憲法記念日"),          // 日曜
        (2026, 5, 4, "みどりの日"),
        (2026, 5, 5, "こどもの日"),
        (2026, 5, 6, "振替休日"),            // 5/3(日)の振替
        (2026, 7, 20, "海の日"),             // 7 月第 3 月曜
        (2026, 8, 11, "山の日"),
        (2026, 9, 21, "敬老の日"),           // 9 月第 3 月曜
        (2026, 9, 22, "国民の休日"),         // 9/21 と 9/23 に挟まれた平日
        (2026, 9, 23, "秋分の日"),
        (2026, 10, 12, "スポーツの日"),      // 10 月第 2 月曜
        (2026, 11, 3, "文化の日"),
        (2026, 11, 23, "勤労感謝の日"),
        // ── 2027（17）──
        (2027, 1, 1, "元日"),
        (2027, 1, 11, "成人の日"),           // 1 月第 2 月曜
        (2027, 2, 11, "建国記念の日"),
        (2027, 2, 23, "天皇誕生日"),
        (2027, 3, 21, "春分の日"),           // 日曜
        (2027, 3, 22, "振替休日"),           // 3/21(日)の振替
        (2027, 4, 29, "昭和の日"),
        (2027, 5, 3, "憲法記念日"),
        (2027, 5, 4, "みどりの日"),
        (2027, 5, 5, "こどもの日"),
        (2027, 7, 19, "海の日"),             // 7 月第 3 月曜
        (2027, 8, 11, "山の日"),
        (2027, 9, 20, "敬老の日"),           // 9 月第 3 月曜
        (2027, 9, 23, "秋分の日"),
        (2027, 10, 11, "スポーツの日"),      // 10 月第 2 月曜
        (2027, 11, 3, "文化の日"),
        (2027, 11, 23, "勤労感謝の日"),
    };

    /// <summary>盖某租户 → Sys_WorkCalendar[]（全 IsWorkday=false）。幂等去重由调用方按 (TenantId,Date) Any 判定。</summary>
    public static Sys_WorkCalendar[] For(Guid tenantId) => Items
        .Select(h => new Sys_WorkCalendar
        {
            TenantId = tenantId,
            Date = new DateTime(h.Y, h.M, h.D),
            IsWorkday = false,
            Note = h.Note,
        })
        .ToArray();
}
```

- [ ] **Step 6: DI + 默认租户 seed（`Program.cs`）**

```csharp
// DI（放 WFS 服务注册块，Program.cs:132-139 一带）
builder.Services.AddSingleton(builder.Configuration.GetSection("Wfs").Get<CP6.Core.Services.Wf.WfsInfraOptions>()
    ?? new CP6.Core.Services.Wf.WfsInfraOptions());   // WFS infra 配置（FireHour/保留期/时区）
builder.Services.AddScoped<CP6.Core.Services.Wf.IWorkdayCalculator, CP6.Core.Services.Wf.WorkdayCalculator>();
```

```csharp
// 默认租户假日 seed（放 seed 区，幂等；默认租户 Id 取仓库既有默认租户常量——沿用现有 seed 的 DefaultTenantId 口径）
foreach (var row in CP6.WebApi.Seed.JapaneseHolidaySeed.For(defaultTenantId))
{
    if (!db.Sys_WorkCalendars.Any(c => c.TenantId == row.TenantId && c.Date == row.Date))
        db.Sys_WorkCalendars.Add(row);
}
db.SaveChanges();
```

> 落地核实：仓库默认租户 Id 常量名（grep `DefaultTenant`/既有 seed 的 TenantId 赋值）——用同一常量，勿硬编码新 GUID。

- [ ] **Step 7: 跑验证 PASS + 全量闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "WorkdayCalculatorTests|JapaneseHolidaySeedTests"
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-infra): A-T2 IWorkdayCalculator(例外反转+366防死循环)+日本假日seed 2026-27(35日期)+默认租户植入"
```

---

## Task A-T3: `workdays` 第四延时模式（`ComputeDueUtc` 扩展 + 设计器 timer 面板）

> **I-A 用服务器本地 tz 作 app 默认时区占位**（映射②）；I-E 换 `ITenantClock`。

**Files:**
- Modify `CP6.Core/Services/Wf/NodeHandlers/ServiceTaskNodeHandler.cs`（ctor 加 `IWorkdayCalculator? workdays`、`WfsInfraOptions? opts`；timer 到期计算加 `workdays` 分支）
- Modify designer timer 面板（延时模式 radio 第四项 `workdays` + 值输入 + validateClient）
- Test `CP6.Tests/Wf/WorkdaysDelayModeTests.cs`、`cp6.web/.../__tests__/designerModel.spec.ts`（vitest 延时模式）

- [ ] **Step 1: 写失败测试（四模式 ComputeDueUtc + workdays 落点 09:00 + 值非法降级）**

```csharp
// CP6.Tests/Wf/WorkdaysDelayModeTests.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.Data.Sqlite;
using Xunit;
using static CP6.Tests.WfsInfraTestHarness;

namespace CP6.Tests;

public class WorkdaysDelayModeTests
{
    private static async Task<ServiceTaskNodeHandler> HandlerAsync(SqliteConnection conn, int fireHour = 9)
    {
        // 2026-05-04/05/06 假；此测试用服务器本地 tz 作 app 默认（I-A 口径）
        using (var db = Ctx(conn))
        {
            db.Sys_WorkCalendars.AddRange(
                new Sys_WorkCalendar { Date = new DateTime(2026, 5, 4), IsWorkday = false },
                new Sys_WorkCalendar { Date = new DateTime(2026, 5, 5), IsWorkday = false },
                new Sys_WorkCalendar { Date = new DateTime(2026, 5, 6), IsWorkday = false });
            await db.SaveChangesAsync();
        }
        var calDb = Ctx(conn);
        return new ServiceTaskNodeHandler(Array.Empty<IServiceTaskExecutor>(),
            new WorkdayCalculator(calDb), new WfsInfraOptions { WorkdayFireHour = fireHour });
    }

    [Fact]
    public async Task ComputeWorkdaysDue_LandsOnFireHour_ServerLocalToUtc()
    {
        using var conn = NewSqliteWithSchema();
        var handler = await HandlerAsync(conn, fireHour: 9);
        var node = new FlowNode { Id = "t", Type = "serviceTask", ServiceKind = ServiceKind.Timer,
            ServiceDelayMode = "workdays", ServiceDelayValue = "1" };

        // 从固定本地 now=2026-05-01T14:00(Fri) 顺延 1 工作日 → 05-07(Thu) 09:00 本地 → UTC
        var nowLocal = new DateTime(2026, 5, 1, 14, 0, 0, DateTimeKind.Unspecified);
        var due = await handler.ComputeTimerDueUtcForTestAsync(node, "{}", nowLocal, CancellationToken.None);

        var expectedLocal = new DateTime(2026, 5, 7, 9, 0, 0, DateTimeKind.Unspecified);
        var expectedUtc = TimeZoneInfo.ConvertTimeToUtc(expectedLocal, TimeZoneInfo.Local);
        Assert.Equal(expectedUtc, due);
    }

    [Fact]
    public async Task ComputeWorkdaysDue_NonPositiveValue_DegradesToImmediate()
    {
        using var conn = NewSqliteWithSchema();
        var handler = await HandlerAsync(conn);
        var node = new FlowNode { Id = "t", Type = "serviceTask", ServiceKind = ServiceKind.Timer,
            ServiceDelayMode = "workdays", ServiceDelayValue = "0" };
        var nowLocal = new DateTime(2026, 5, 1, 14, 0, 0, DateTimeKind.Unspecified);

        var due = await handler.ComputeTimerDueUtcForTestAsync(node, "{}", nowLocal, CancellationToken.None);

        // 非正整数 → 降级立即（now 的 UTC，容 2s 抖动）
        Assert.True((DateTime.UtcNow - due).Duration() < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ComputeDueUtc_ExistingThreeModes_ByteEquivalent()
    {
        // 既有三模式静态方法零回归
        var dur = new FlowNode { ServiceDelayMode = "duration", ServiceDelayValue = "2h" };
        var due = ServiceTaskNodeHandler.ComputeDueUtc(dur, "{}");
        Assert.True(due > DateTime.UtcNow.AddMinutes(110) && due < DateTime.UtcNow.AddMinutes(130));

        var none = new FlowNode { ServiceDelayMode = "duration", ServiceDelayValue = null };
        Assert.True((ServiceTaskNodeHandler.ComputeDueUtc(none, "{}") - DateTime.UtcNow).Duration() < TimeSpan.FromSeconds(5));
    }
}
```

- [ ] **Step 2: 跑验证 FAIL** — `--filter WorkdaysDelayModeTests`。

- [ ] **Step 3: 实现（`ServiceTaskNodeHandler`）**

ctor 追加参数（DI 自动注入；默认 null/缺省 → fallback 与单测零破坏）：

```csharp
private readonly IWorkdayCalculator? _workdays;
private readonly int _workdayFireHour;

public ServiceTaskNodeHandler(IEnumerable<IServiceTaskExecutor>? executors,
    IWorkdayCalculator? workdays = null, WfsInfraOptions? opts = null)
{
    _executors = (executors ?? Array.Empty<IServiceTaskExecutor>())
        .ToDictionary(e => e.Key, StringComparer.OrdinalIgnoreCase);
    _workdays = workdays;
    _workdayFireHour = opts?.WorkdayFireHour ?? 9;
}
```

timer 到期计算调用点（`:100`）改为 async 分支：

```csharp
var dueAtUtc = (kind == ServiceKind.Timer)
    ? await ComputeTimerDueUtcAsync(node, inst.VarsJson, DateTime.Now, ct: default)
    : nowUtc;
```

新增计算方法（`workdays` 走服务；其余三模式委托既有静态；null 服务/非正值→降级立即）：

```csharp
/// <summary>timer 到期计算（含 workdays 第四模式，spec §2.3）。I-A：workdays 用服务器本地 tz 作 app 默认时区；
/// I-E 把 <paramref name="nowLocal"/> 的 tz 源与回转 tz 换成 ITenantClock。非法/缺服务 → 降级立即（既有铁律）。</summary>
private async Task<DateTime> ComputeTimerDueUtcAsync(FlowNode node, string? varsJson, DateTime nowLocal, CancellationToken ct)
{
    if (node.ServiceDelayMode == "workdays")
    {
        if (_workdays == null || !int.TryParse(node.ServiceDelayValue, out var n) || n <= 0)
            return DateTime.UtcNow;   // 降级立即（值非正并入 E-WF-016 家族，运行期不炸引擎）
        var dueDay = await _workdays.AddWorkdaysAsync(nowLocal.Date, n, ct);
        var fireLocal = DateTime.SpecifyKind(dueDay.Date.AddHours(_workdayFireHour), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(fireLocal, TimeZoneInfo.Local);   // I-E 换 _clock.GetTenantTimeZone()
    }
    return ComputeDueUtc(node, varsJson);   // duration/untilDate/untilExpr（既有静态，字节等价）
}

/// <summary>测试专用重载（注入 nowLocal，A-T3/E-T2 复用）。</summary>
internal Task<DateTime> ComputeTimerDueUtcForTestAsync(FlowNode node, string? varsJson, DateTime nowLocal, CancellationToken ct)
    => ComputeTimerDueUtcAsync(node, varsJson, nowLocal, ct);
```

> `ComputeDueUtc`（既有静态三模式）保持不动——`workdays` 值校验并入既有 E-WF-016 家族（`FlowSchemaValidator` serviceTask 配置无效口径）：设计器保存时校验 `ServiceDelayMode=="workdays"` 则 `ServiceDelayValue` 须为正整数字符串（照 E-WF-016 现规则追加分支）。

- [ ] **Step 4: 设计器 timer 面板**（延时模式 radio 加第四项 `workdays`；值输入用整数校验；validateClient 镜像「workdays 值须正整数」E-WF-016；**触发器 cron 不掺工作日语义**，正交）。vitest：延时模式枚举含 `workdays`、round-trip 保真、值校验。

- [ ] **Step 5: 跑验证 PASS + 全量闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "WorkdaysDelayModeTests|Wf"
cd cp6.web && npm run test && npm run type-check && cd ..
git add -A && git commit -m "feat(wfs-infra): A-T3 workdays 第四延时模式(FireHour 09:00 落点+值校验并入E-WF-016)+设计器timer面板radio"
```

---

## Task A-T4: 年历管理页（`Sys_WorkCalendar` CRUD + 空态导入 + el-calendar 勾选）

**Files:**
- Create `CP6.Core/Services/Wf/WorkCalendarService.cs`（`IWorkCalendarService`：列一年例外 / 反转某日 / 导入日本假日到当前租户 / 空态判定）
- Create `CP6.WebApi/Controllers/Oa/WorkCalendarController.cs`
- Create `cp6.web/src/api/oa/workCalendar.ts` + `cp6.web/src/views/oa/admin/WorkCalendar.vue`
- Modify `Program.cs`（DI + 菜单/权限 seed）
- Test `CP6.Tests/Wf/WorkCalendarServiceTests.cs`、`cp6.web/.../__tests__/workCalendar.spec.ts`

- [ ] **Step 1: 写失败测试（服务：反转幂等 + 导入幂等/写当前租户 + 空态判定）**

```csharp
// CP6.Tests/Wf/WorkCalendarServiceTests.cs
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Services.Wf;
using Xunit;
using static CP6.Tests.WfsInfraTestHarness;

namespace CP6.Tests;

public class WorkCalendarServiceTests
{
    [Fact]
    public async Task ImportJapaneseHolidays_Idempotent_35Rows()
    {
        using var conn = NewSqliteWithSchema();
        using var db = Ctx(conn);
        var svc = new WorkCalendarService(db);

        var n1 = await svc.ImportJapaneseHolidaysAsync(CancellationToken.None);
        var n2 = await svc.ImportJapaneseHolidaysAsync(CancellationToken.None);   // 第二次全命中不重复

        Assert.Equal(35, n1);
        Assert.Equal(0, n2);
        Assert.Equal(35, db.Sys_WorkCalendars.Count());
    }

    [Fact]
    public async Task IsEmpty_TrueBeforeImport_FalseAfter()
    {
        using var conn = NewSqliteWithSchema();
        using var db = Ctx(conn);
        var svc = new WorkCalendarService(db);
        Assert.True(await svc.IsEmptyAsync(CancellationToken.None));
        await svc.ImportJapaneseHolidaysAsync(CancellationToken.None);
        Assert.False(await svc.IsEmptyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ToggleDay_InsertsThenReverses_ThenRemovesOnBackToDefault()
    {
        using var conn = NewSqliteWithSchema();
        using var db = Ctx(conn);
        var svc = new WorkCalendarService(db);
        var sat = new DateTime(2026, 5, 16);   // 周六，默认非工作日

        await svc.SetDayAsync(sat, isWorkday: true, note: "補班", CancellationToken.None);   // 反转为补班
        Assert.Single(db.Sys_WorkCalendars.Where(c => c.Date == sat));
        Assert.True(db.Sys_WorkCalendars.Single(c => c.Date == sat).IsWorkday);

        await svc.ClearDayAsync(sat, CancellationToken.None);   // 回归默认 → 删除例外行
        Assert.Empty(db.Sys_WorkCalendars.Where(c => c.Date == sat));
    }

    [Fact]
    public async Task ListYear_ReturnsOnlyThatYear()
    {
        using var conn = NewSqliteWithSchema();
        using var db = Ctx(conn);
        var svc = new WorkCalendarService(db);
        await svc.ImportJapaneseHolidaysAsync(CancellationToken.None);
        var y2026 = await svc.ListYearAsync(2026, CancellationToken.None);
        Assert.Equal(18, y2026.Count);
        Assert.All(y2026, r => Assert.Equal(2026, r.Date.Year));
    }
}
```

- [ ] **Step 2: FAIL → 实现 `WorkCalendarService`**（`ImportJapaneseHolidaysAsync` 用 `JapaneseHolidaySeed.Items` 逐条 `(TenantId 由 StampTenant 自动盖)` Any 去重 —— 注意 seed helper `For(tenantId)` 需当前租户，服务侧从 `ITenantContext.CurrentTenantId` 取；`SetDayAsync` upsert；`ClearDayAsync` 删；`IsEmptyAsync`=`!Any()`；`ListYearAsync`=按 `Date.Year` 过滤）。全局过滤自动限当前租户，写入 `StampTenant` 自动盖。

- [ ] **Step 3: 控制器 + DI + 菜单/权限 seed**（`WorkCalendarController : LocalizedControllerBase`，`[Route("api/oa/work-calendar")]`，`[RequirePermission("oa-work-calendar","Calendar.View/Edit")]`；端点：`GET ?year=` 列 / `POST toggle` 反转 / `DELETE {date}` 清 / `POST import-jp` 导入。菜单新增一项（RoutePath `/oa/work-calendar`，MenuKey `oa-work-calendar`）+ MenuAction `Calendar.View/Edit` + RoleId=1 授权，幂等块照 `Program.cs:850-856`）。

- [ ] **Step 4: 前端 `WorkCalendar.vue`**（`el-calendar` + `#date-cell` 插槽：工作/休息态用 CpTag tone 区分、点击弹反转对话框填 Note；**空态**（`IsEmptyAsync` 为真）显示提示「本租户未维护假日日历」+「导入日本法定假日」按钮→调 `import-jp`。零硬编码色）。vitest：空态渲染、反转态映射、年切换。

- [ ] **Step 5: 全量闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "WorkCalendarServiceTests|Wf"
cd cp6.web && npm run test && npm run type-check && npm run build && cd ..
git add -A && git commit -m "feat(wfs-infra): A-T4 年历管理页(el-calendar勾选反转+空态导入日本假日按钮)+WorkCalendarService+权限点"
```

---

# Wave I-B — approval 超时错误边（依赖 I-A 契约；与 I-C/I-D 并行）

## Task B-T1: `errorEdge` 第四动作 + 引擎入口 + 来源放宽 + E-WF-027

**Files:**
- Modify `CP6.Core/Services/Wf/FlowEngine.cs`（`IFlowEngine.TimeoutAdvanceErrorEdgeAsync` 声明）
- Modify `CP6.Core/Services/Wf/FlowEngine.Tokens.cs`（实现 `TimeoutAdvanceErrorEdgeAsync`）
- Modify `CP6.Core/Services/Wf/WfTimeoutService.cs`（`case "errorEdge"`）
- Modify `CP6.Core/Services/Wf/FlowSchemaValidator.cs`（`ErrorEdgeSourceTypes` 常量 + E-WF-027）
- Test `CP6.Tests/Wf/TimeoutErrorEdgeTests.cs`、`CP6.Tests/Wf/ErrorEdgeSourceValidatorTests.cs`

- [ ] **Step 1: 写失败测试（errorEdge 路由 + 待办作废节点级 + 无边被 E-WF-027 拦 + 三既有动作零回归 + 来源集合放宽）**

```csharp
// CP6.Tests/Wf/ErrorEdgeSourceValidatorTests.cs
using System.Linq;
using CP6.Core.Services.Wf;
using Xunit;

namespace CP6.Tests;

public class ErrorEdgeSourceValidatorTests
{
    private static FlowSchema Schema(string fromType, bool errorEdgeFromNode, string? timeoutAction = null)
    {
        var s = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "n", Type = fromType, TimeoutAction = timeoutAction },
                new FlowNode { Id = "h", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = System.Guid.NewGuid() },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "n" },
                new FlowEdge { From = "n", To = "end" },   // 非错误出边（满足 E-WF-016）
            },
        };
        if (errorEdgeFromNode) s.Edges.Add(new FlowEdge { From = "n", To = "h", IsError = true });
        return s;
    }

    [Fact]
    public void ApprovalErrorEdge_NowAllowed_NoE017()
    {
        var errs = FlowSchemaValidator.Validate(Schema("approval", errorEdgeFromNode: true, timeoutAction: "errorEdge"));
        Assert.DoesNotContain("E-WF-017", errs);   // approval 现允许 IsError 出边
    }

    [Fact]
    public void SubFlowErrorEdge_NowAllowed_NoE017()
    {
        var errs = FlowSchemaValidator.Validate(Schema("subFlow", errorEdgeFromNode: true));
        Assert.DoesNotContain("E-WF-017", errs);   // 跨 spec 契约：来源集合含 subFlow
    }

    [Fact]
    public void StartErrorEdge_StillRejected_E017()
    {
        var errs = FlowSchemaValidator.Validate(Schema("start", errorEdgeFromNode: true));
        Assert.Contains("E-WF-017", errs);   // 非法来源仍拦
    }

    [Fact]
    public void ApprovalTimeoutErrorEdge_WithoutErrorEdge_E027()
    {
        var errs = FlowSchemaValidator.Validate(Schema("approval", errorEdgeFromNode: false, timeoutAction: "errorEdge"));
        Assert.Contains("E-WF-027", errs);   // 配 errorEdge 但无 IsError 出边
    }

    [Fact]
    public void ApprovalTimeoutErrorEdge_WithErrorEdge_NoE027()
    {
        var errs = FlowSchemaValidator.Validate(Schema("approval", errorEdgeFromNode: true, timeoutAction: "errorEdge"));
        Assert.DoesNotContain("E-WF-027", errs);
    }
}
```

```csharp
// CP6.Tests/Wf/TimeoutErrorEdgeTests.cs
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests;

// 复用 FlowConcurrencyTests 的 SQLite 基座建库口径（GenerateCreateScript+TEXT 替换+FlowInstance rowversion 触发器）。
public class TimeoutErrorEdgeTests
{
    private static string SchemaWithApprovalTimeoutErrorEdge(Guid approver) => JsonSerializer.Serialize(new FlowSchema
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver,
                          TimeoutHours = 1, TimeoutAction = "errorEdge" },
            new FlowNode { Id = "handler", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "a" },
            new FlowEdge { From = "a", To = "end" },
            new FlowEdge { From = "a", To = "handler", IsError = true },   // 失败边
        },
    });

    [Fact]
    public async Task Timeout_ErrorEdge_VoidsPendingTask_RoutesAlongErrorEdge()
    {
        using var conn = WfTestDb.NewSqliteWithSchema();   // 见下 helper 说明
        var approver = Guid.NewGuid();
        Guid instId;
        using (var db = WfTestDb.Ctx(conn))
        {
            db.Sys_Users.Add(new CP6.Entity.DomainModels.Sys.Sys_User { Id = approver, UserName = "ap", Password = "x", RoleId = 1, Enable = true });
            db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "fk", FlowName = "fk", FormKey = "f",
                SchemaJson = SchemaWithApprovalTimeoutErrorEdge(approver), Version = 1, Enable = true });
            await db.SaveChangesAsync();
            var eng = WfTestDb.Engine(db);
            instId = await eng.SubmitAsync("fk", approver, "{}");   // 停在 approval "a"，生成 pending 待办
        }

        using (var db = WfTestDb.Ctx(conn))
        {
            var svc = new WfTimeoutService(db, WfTestDb.Engine(db));
            var handled = await svc.ScanOnceAsync(DateTime.UtcNow.AddHours(2), CancellationToken.None);   // 越过 DueAt
            Assert.Equal(1, handled);
        }

        using (var db = WfTestDb.Ctx(conn))
        {
            // 原 approval "a" 的待办被作废；token 已沿错误边进入 "handler" 节点并生成新 pending
            var tasksA = await db.Wf_FlowTasks.Where(t => t.InstanceId == instId && t.NodeId == "a").ToListAsync();
            Assert.All(tasksA, t => Assert.NotEqual(FlowTaskStatus.Pending, t.Status));
            var pendingHandler = await db.Wf_FlowTasks.CountAsync(t => t.InstanceId == instId && t.NodeId == "handler" && t.Status == FlowTaskStatus.Pending);
            Assert.Equal(1, pendingHandler);
            var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
            Assert.Equal(FlowInstanceStatus.Running, inst.Status);
            Assert.Contains("timeoutError", inst.VarsJson);   // 错误变量已注入
        }
    }

    [Fact]
    public async Task Timeout_Reject_ByteEquivalent_NoRegression()
    {
        // 三既有硬动作零回归的定点：reject 仍走自动驳回（不因 errorEdge case 增改而变）
        using var conn = WfTestDb.NewSqliteWithSchema();
        var approver = Guid.NewGuid();
        Guid instId;
        var schema = JsonSerializer.Serialize(new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver, TimeoutHours = 1, TimeoutAction = "reject" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges = { new FlowEdge { From = "s", To = "a" }, new FlowEdge { From = "a", To = "end" } },
        });
        using (var db = WfTestDb.Ctx(conn))
        {
            db.Sys_Users.Add(new CP6.Entity.DomainModels.Sys.Sys_User { Id = approver, UserName = "ap", Password = "x", RoleId = 1, Enable = true });
            db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "fk", FlowName = "fk", FormKey = "f", SchemaJson = schema, Version = 1, Enable = true });
            await db.SaveChangesAsync();
            instId = await WfTestDb.Engine(db).SubmitAsync("fk", approver, "{}");
        }
        using (var db = WfTestDb.Ctx(conn))
        {
            await new WfTimeoutService(db, WfTestDb.Engine(db)).ScanOnceAsync(DateTime.UtcNow.AddHours(2), CancellationToken.None);
        }
        using (var db = WfTestDb.Ctx(conn))
        {
            var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
            Assert.Equal(FlowInstanceStatus.Rejected, inst.Status);   // reject 语义不变
        }
    }
}
```

> **测试 helper 说明**：`WfTestDb` = 复用/抽出 `FlowConcurrencyTests.cs` 的 SQLite 建库 + `Engine(db)` 工厂（若该文件已有等价静态 helper 则直接引用，勿重复造；否则在 `CP6.Tests/Wf/` 抽一个 `internal static WfTestDb`，与 `WfsInfraTestHarness` 并存，职责=Wf 引擎实例基座）。

- [ ] **Step 2: FAIL → 实现**

`IFlowEngine` 加声明 + `FlowEngine.Tokens.cs` 实现（节点级作废，照映射④）：

```csharp
/// <summary>approval 超时走失败边（infra ②，spec §3）。作废该节点在途待办（节点级，仿退回口径，不连坐兄弟）
/// + 注入 timeoutError 变量 + AdvanceAlongErrorEdge 路由。幂等：任务非 Pending/实例非 Running → 零动作。</summary>
public async Task TimeoutAdvanceErrorEdgeAsync(Guid taskId, Guid actorId, CancellationToken ct = default)
{
    var task = await _db.Wf_FlowTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
    if (task is null || task.Status != FlowTaskStatus.Pending) return;
    var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == task.InstanceId, ct);
    if (inst is null || inst.Status != FlowInstanceStatus.Running) return;
    var schema = Deserialize((await _db.Wf_FlowDefs.FirstAsync(d => d.FlowKey == inst.FlowKey, ct)).SchemaJson);
    var token = await _db.Wf_FlowTokens.FirstOrDefaultAsync(
        t => t.InstanceId == inst.Id && t.NodeId == task.NodeId && t.Status == FlowTokenStatus.Active, ct);
    if (token is null) return;

    // ① 节点级作废在途待办（对齐退回清理口径 AdvancedFlow.cs:153-157，不用实例级驳回连坐）
    var cur = await _db.Wf_FlowTasks
        .Where(t => t.InstanceId == inst.Id && t.NodeId == task.NodeId && t.Status == FlowTaskStatus.Pending).ToListAsync(ct);
    foreach (var t in cur) t.Status = FlowTaskStatus.Cancelled;
    VoidPendingFormTos(inst.Id, task.NodeId, token.Id, task.StageIndex, task.StageRound, FlowFormToStatus.SentBack);

    // ② 注入错误变量（口径同 serviceTask 失败：结构化 key）
    inst.VarsJson = ServiceVarsHelper.MergeOutputVars(inst.VarsJson, new Dictionary<string, object?>
    {
        ["timeoutError"] = new { nodeId = task.NodeId, dueAt = task.DueAt },
    }).VarsJson;
    AddHistory(inst.Id, task.NodeId, actorId, "timeoutErrorEdge", $"审批超时走失败边（node={task.NodeId}）");

    // ③ 沿错误边路由（无边则 Suspend——但 E-WF-027 静态已保证有边）
    await AdvanceAlongErrorEdge(inst, schema, token);
    // 不 SaveChanges——WfTimeoutService.ScanOnceAsync 尾统一保存
}
```

`WfTimeoutService.cs` switch 加 `case "errorEdge"`（在 `escalate` 与 `default` 之间）：

```csharp
case "errorEdge": // 硬：审批超时走失败边（infra ②），委托引擎节点级清场+路由；置 Handled 防反复扫
    await _engine.TimeoutAdvanceErrorEdgeAsync(task.Id, SystemActor, ct);
    task.TimeoutHandled = true;
    break;
```

`FlowSchemaValidator.cs`（来源集合常量 + E-WF-027；改 `:97-100`）：

```csharp
/// <summary>IsError 边合法来源类型（跨 spec 单一常量：本 infra 波写全集含 subFlow；
/// 子流程 spec 2026-07-05-wfs-subflow-design §5 只加放行测试，不重复定义——两处注释互指）。</summary>
internal static readonly HashSet<string> ErrorEdgeSourceTypes =
    new(StringComparer.OrdinalIgnoreCase) { "serviceTask", "approval", "subFlow" };

// …§⑨ 错误出边校验改写：
var errorSourceIds = schema.Nodes.Where(n => ErrorEdgeSourceTypes.Contains(T(n))).Select(n => n.Id).ToHashSet();
if (schema.Edges.Where(e => e.IsError == true).GroupBy(e => e.From).Any(g => g.Count() > 1)) errs.Add("E-WF-017");
if (schema.Edges.Any(e => e.IsError == true && !errorSourceIds.Contains(e.From))) errs.Add("E-WF-017");

// E-WF-027：TimeoutAction=errorEdge 的节点必须有 IsError 出边
foreach (var n in schema.Nodes.Where(n => string.Equals(n.TimeoutAction, "errorEdge", StringComparison.OrdinalIgnoreCase)))
{
    if (!schema.Edges.Any(e => e.From == n.Id && e.IsError == true)) errs.Add("E-WF-027");
}
```

> `T(n)` 输出小写序数值，`ErrorEdgeSourceTypes` 用 `OrdinalIgnoreCase` 比较，`"servicetask"/"approval"/"subflow"` 全命中。

- [ ] **Step 3: PASS + 全量闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "ErrorEdgeSourceValidatorTests|TimeoutErrorEdgeTests|Wf"
git add -A && git commit -m "feat(wfs-infra): B-T1 approval超时errorEdge第四动作+TimeoutAdvanceErrorEdgeAsync节点级清场路由+来源集合放宽{serviceTask,approval,subFlow}+E-WF-027"
```

---

## Task B-T2: 设计器 approval 面板超时动作「超时走失败边」+ validateClient E-WF-027

**Files:** Modify designer approval 面板（超时动作下拉加 `errorEdge` 项）+ EdgePropertyPanel（IsError 复选 approval 放行）+ designerModel validateClient；Test `cp6.web/.../__tests__/designerModel.spec.ts`

- [ ] **Step 1: vitest 失败测试** — 超时动作枚举含 `errorEdge`；配 `errorEdge` 但节点无 IsError 出边 → validateClient 返回含 E-WF-027；approval 节点可挂 IsError 边（前端不再拦）。
- [ ] **Step 2: 实现** — 面板下拉加「超时走失败边」（i18n `oa.designer.timeout.errorEdge`）；validateClient 镜像 B-T1 静态规则（`ErrorEdgeSourceTypes` 前端等价常量 + errorEdge→须有 IsError 出边）。零硬编码色。
- [ ] **Step 3: PASS + commit**

```bash
cd cp6.web && npm run test && npm run type-check && npm run build && cd ..
git add -A && git commit -m "feat(wfs-infra): B-T2 设计器approval超时动作『超时走失败边』+validateClient镜像E-WF-027"
```

---

# Wave I-C — 终态 job/流水清理 worker（依赖 I-A 契约；与 I-B/I-D 并行）

## Task C-T1: `WfServiceJobCleanupWorker`（终态删/在途留/占坑永不清/老化告警/幂等窗口契约）

**Files:**
- Create `CP6.Core/Services/Wf/WfCleanupService.cs`（`IWfCleanupService.CleanupOnceAsync(DateTime nowUtc, CancellationToken ct)` → `CleanupResult`；纯服务，worker 只调它便于注入 now 测试）
- Create `CP6.WebApi/BackgroundServices/WfServiceJobCleanupWorker.cs`
- Modify `Program.cs`（DI `IWfCleanupService` + `AddHostedService<WfServiceJobCleanupWorker>()`）
- Test `CP6.Tests/Wf/WfCleanupServiceTests.cs`

- [ ] **Step 1: 写失败测试（终态删 / 在途留 / 占坑永不清 / 保留期=0 禁用 / 分批 / 老化告警计数）**

```csharp
// CP6.Tests/Wf/WfCleanupServiceTests.cs
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static CP6.Tests.WfsInfraTestHarness;

namespace CP6.Tests;

public class WfCleanupServiceTests
{
    private static Wf_ServiceJob Job(int status, DateTime? completedUtc)
        => new()
        {
            Id = Guid.NewGuid(), InstanceId = Guid.NewGuid(), TokenId = Guid.NewGuid(), NodeId = "n",
            Kind = "webApi", Status = status, DueAtUtc = DateTime.UtcNow.AddDays(-300),
            NextAttemptAtUtc = DateTime.UtcNow.AddDays(-300), CompletedAtUtc = completedUtc,
        };

    private static WfCleanupService Svc(Microsoft.Data.Sqlite.SqliteConnection conn, int retentionDays = 180, int staleDays = 7)
        => new(Ctx(conn), new WfsInfraOptions { CleanupRetentionDays = retentionDays, StaleReservationAlertDays = staleDays });

    [Fact]
    public async Task Cleanup_DeletesTerminalOlderThanRetention_KeepsRunningAndRecent()
    {
        using var conn = NewSqliteWithSchema();
        var now = DateTime.UtcNow;
        using (var db = Ctx(conn))
        {
            db.Wf_ServiceJobs.AddRange(
                Job(ServiceJobStatus.Succeeded, now.AddDays(-200)),   // 删（终态+超龄）
                Job(ServiceJobStatus.Failed, now.AddDays(-181)),      // 删
                Job(ServiceJobStatus.Cancelled, now.AddDays(-181)),   // 删
                Job(ServiceJobStatus.Succeeded, now.AddDays(-10)),    // 留（终态但未超龄）
                Job(ServiceJobStatus.Running, now.AddDays(-300)),     // 留（非终态，在途）
                Job(ServiceJobStatus.Pending, now.AddDays(-300)));    // 留（非终态）
            await db.SaveChangesAsync();
        }
        int deleted;
        using (var db = Ctx(conn)) deleted = (await Svc(conn).CleanupOnceAsync(now, CancellationToken.None)).ServiceJobsDeleted;
        Assert.Equal(3, deleted);
        using var check = Ctx(conn);
        Assert.Equal(3, await check.Wf_ServiceJobs.CountAsync());
    }

    [Fact]
    public async Task Cleanup_RetentionZero_Disabled_NothingDeleted()
    {
        using var conn = NewSqliteWithSchema();
        var now = DateTime.UtcNow;
        using (var db = Ctx(conn)) { db.Wf_ServiceJobs.Add(Job(ServiceJobStatus.Succeeded, now.AddDays(-500))); await db.SaveChangesAsync(); }
        var r = await Svc(conn, retentionDays: 0).CleanupOnceAsync(now, CancellationToken.None);
        Assert.Equal(0, r.ServiceJobsDeleted);
        using var check = Ctx(conn);
        Assert.Equal(1, await check.Wf_ServiceJobs.CountAsync());
    }

    [Fact]
    public async Task Cleanup_Batches_DeletesAllOverMultiplePasses()
    {
        using var conn = NewSqliteWithSchema();
        var now = DateTime.UtcNow;
        using (var db = Ctx(conn))
        {
            for (int i = 0; i < 1200; i++) db.Wf_ServiceJobs.Add(Job(ServiceJobStatus.Succeeded, now.AddDays(-300)));
            await db.SaveChangesAsync();
        }
        var r = await Svc(conn).CleanupOnceAsync(now, CancellationToken.None);   // 内部每批 500，多批删尽
        Assert.Equal(1200, r.ServiceJobsDeleted);
        using var check = Ctx(conn);
        Assert.Equal(0, await check.Wf_ServiceJobs.CountAsync());
    }
}
```

> **`Wf_TriggerFire` 清理与老化告警测试**：`Wf_TriggerFire` 表由波③迁移建（前置依赖表）。若波③已并 main，追加测试：终态删（`FiredUtc` 超龄且 `InstanceId!=null || Error!=null` → 删）/ 占坑永不清（`InstanceId==null && Error==null` → 留）/ 老化占坑告警计数（占坑且 `FiredUtc < now - StaleReservationAlertDays` → 计入 `CleanupResult.StaleReservationCount`，不删）。**若波③未并**：C-T1 的 `Wf_TriggerFire` 分支用「DbSet 存在即清」的守卫（`try { … } catch (Exception) when (表不存在) { }` 或运行时探测 `Wf_TriggerFires` 是否注册），并把该测试标 `[Fact(Skip="待波③ Wf_TriggerFire 表并入")]`——**收口时确认波③状态，二选一落地**。

- [ ] **Step 2: FAIL → 实现 `WfCleanupService`**

```csharp
// CP6.Core/Services/Wf/WfCleanupService.cs（要点）
public sealed class CleanupResult
{
    public int ServiceJobsDeleted { get; set; }
    public int TriggerFiresDeleted { get; set; }
    public int StaleReservationCount { get; set; }   // 老化占坑（永不清，仅告警计数）
}

public interface IWfCleanupService
{
    Task<CleanupResult> CleanupOnceAsync(DateTime nowUtc, CancellationToken ct);
}
```

实现要点（spec §4）：
- 保留期 `opts.CleanupRetentionDays <= 0` → 直接返回空结果（禁用）。
- `Wf_ServiceJob`：`Status ∈ {Succeeded, Failed, Cancelled} && CompletedAtUtc < now - 保留期` → 分批（每批 500，`OrderBy(Id).Take(500)` → `RemoveRange` → `SaveChanges` 循环至 0）。
- `Wf_TriggerFire`（表存在时）：`FiredUtc < now - 保留期 && (InstanceId != null || Error != null)` → 分批删；**占坑行（两者皆 null）永不清**。
- 老化告警：`InstanceId == null && Error == null && FiredUtc < now - opts.StaleReservationAlertDays` → 计数进 `StaleReservationCount`（不删）。
- OperLog：worker 侧每轮记一行（删除计数 + 老化计数）。
- **幂等窗口契约注释**（写进服务 XML doc + message 端点文档呼应波③ §3.4）：`Wf_TriggerFire` 清理即 message 端点幂等保证窗口 = 保留期；180 天前的 Idempotency-Key 重放会重复起单。

- [ ] **Step 3: worker（照抄 `WfServiceJobScanWorker` + `TenantScopeRunner`，周期改每日 03:00）**

```csharp
// CP6.WebApi/BackgroundServices/WfServiceJobCleanupWorker.cs（要点）
// while 循环：算「下一 03:00 UTC」→ await Task.Delay(untilNext, stoppingToken) → TenantScopeRunner.ForEachTenantAsync：
//   var svc = sp.GetRequiredService<IWfCleanupService>();
//   var r = await svc.CleanupOnceAsync(DateTime.UtcNow, ct);
//   if (r.ServiceJobsDeleted + r.TriggerFiresDeleted > 0 || r.StaleReservationCount > 0)
//       写 OperLog 一行（"WFS清理 租户{tenant} job删{n} fire删{m} 老化占坑{k}"）+ LogInformation；
// catch(OperationCanceledException) when(...) throw; catch(Exception ex) LogError。
```

- [ ] **Step 4: DI + commit**

```csharp
builder.Services.AddScoped<CP6.Core.Services.Wf.IWfCleanupService, CP6.Core.Services.Wf.WfCleanupService>();
builder.Services.AddHostedService<CP6.WebApi.BackgroundServices.WfServiceJobCleanupWorker>();   // WFS infra ③ 每日 03:00 清理
```

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "WfCleanupServiceTests|Wf"
git add -A && git commit -m "feat(wfs-infra): C-T1 WfServiceJobCleanupWorker(终态删180天/在途留/占坑永不清/老化告警/分批/幂等窗口契约)"
```

---

# Wave I-D — per-tenant 连接器（依赖 I-A schema；与 I-B/I-C 并行）

> **D-T0 是硬前置**（DataProtection 密钥环持久化）——D-T1 加密落地前必须先做，否则换机/多实例部署凭证密文全瘫。

## Task D-T0: DataProtection 密钥环持久化（前置）

**Files:** Modify `CP6.WebApi/Program.cs`（`AddDataProtection` 加持久化 + ApplicationName）；Modify `docs/superpowers/qa/.../README` 或 `deploy/runbook.md`（运维说明，呼应 runbook:112）

**现状结论**：`Program.cs:515` `builder.Services.AddDataProtection();` **无 `PersistKeysTo*`** → 密钥落容器本地临时位置（重建/多实例即解不开）。`deploy/runbook.md:112` 已标此为 🔴 隐患（SSO ClientSecret 同源）。→ 必须持久化。

- [ ] **Step 1: 实现（文件系统持久化到共享卷，零新表/零迁移）**

```csharp
// Program.cs:515 改写
var dpBuilder = builder.Services.AddDataProtection()
    .SetApplicationName("CP6");   // 多实例共享密钥环需同 ApplicationName
var dpKeyPath = builder.Configuration["DataProtection:KeyPath"];   // 挂载卷路径（生产必配）
if (!string.IsNullOrWhiteSpace(dpKeyPath))
    dpBuilder.PersistKeysToFileSystem(new System.IO.DirectoryInfo(dpKeyPath));
// 未配置（本地 dev）→ 保持默认临时密钥环，行为与现状一致
```

> **不引** `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`——EF 存储会新增 `DataProtectionKeys` 表，破「恰一次迁移 WfsInfra」。文件系统持久化到 Docker 挂载卷即满足「换机/重建/多实例可解」。
> **运维一次性注意（写进 runbook）**：启用持久化前若已有 SSO ClientSecret 密文（旧临时密钥环加密），换新密钥环后旧密文解不开——需在启用后重新录入一次 SSO ClientSecret（临时密钥环本就每次重启丢失，此为既有隐患的正式修复）。连接器为全新功能，无此历史包袱。

- [ ] **Step 2: 验证 + commit** — 全量后端测试绿（DataProtection 测试用 `DataProtectionProvider.Create` 不受影响）；`git add -A && git commit -m "feat(wfs-infra): D-T0 DataProtection 密钥环持久化(PersistKeysToFileSystem+SetApplicationName,修 runbook:112 隐患,连接器加密前置)"`

---

## Task D-T1: `Wf_Connector` 加密服务 + `DbWfConnector` + `WebApiExecutor` 解析合并 + E-WF-028

**Files:**
- Create `CP6.Core/Services/Wf/WfConnectorService.cs`（`IWfConnectorService`：CRUD + 加密写 + 掩码读 + E-WF-028）
- Create `CP6.Core/Services/Wf/Executors/DbWfConnector.cs`
- Modify `CP6.Core/Services/Wf/Executors/WebApiExecutor.cs`（解析先租户表后 app 兜底）
- Modify `Program.cs`（DI）
- Test `CP6.Tests/Wf/WfConnectorServiceTests.cs`、`CP6.Tests/Wf/ConnectorResolutionTests.cs`

- [ ] **Step 1: 写失败测试（密文往返 + 掩码不回显 + E-WF-028 + 租户优先 app 兜底 + 目录合并去重）**

```csharp
// CP6.Tests/Wf/WfConnectorServiceTests.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.DataProtection;
using Xunit;
using static CP6.Tests.WfsInfraTestHarness;

namespace CP6.Tests;

public class WfConnectorServiceTests
{
    private static WfConnectorService Svc(Microsoft.Data.Sqlite.SqliteConnection conn, int leaseSec = 60)
        => new(Ctx(conn), DataProtectionProvider.Create("CP6.Tests"),
               new WfsInfraOptions(), leaseSeconds: leaseSec);

    [Fact]
    public async Task Save_EncryptsAuth_ExecuteDecrypts_ReadMasks()
    {
        using var conn = NewSqliteWithSchema();
        var svc = Svc(conn);
        var id = await svc.CreateAsync(new WfConnectorSaveReq
        {
            Name = "erpProd", DisplayName = "ERP", BaseUrl = "https://erp.example",
            AuthJson = "{\"type\":\"bearer\",\"token\":\"secret-123\"}", TimeoutSec = 30, Enabled = true,
        }, CancellationToken.None);

        // 库内密文 != 明文
        using (var db = Ctx(conn))
        {
            var row = await db.Wf_Connectors.FindAsync(new object[] { id }, CancellationToken.None);
            Assert.NotNull(row!.AuthJsonEncrypted);
            Assert.DoesNotContain("secret-123", row.AuthJsonEncrypted);
        }
        // 读接口掩码（hasAuth=true，无明文）
        var view = await svc.GetAsync(id, CancellationToken.None);
        Assert.True(view!.HasAuth);
        Assert.Null(view.AuthJson);
        // 执行侧解密还原
        var plain = await svc.DecryptAuthAsync(id, CancellationToken.None);
        Assert.Contains("secret-123", plain);
    }

    [Fact]
    public async Task Save_TimeoutBelowLease_E028_Rejected()
    {
        using var conn = NewSqliteWithSchema();
        var svc = Svc(conn, leaseSec: 60);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAsync(new WfConnectorSaveReq
        {
            Name = "slow", DisplayName = "S", BaseUrl = "https://x", TimeoutSec = 30, Enabled = true,   // 30s < 60s 租约
        }, CancellationToken.None));
        Assert.Contains("E-WF-028", ex.Message);
    }
}
```

```csharp
// CP6.Tests/Wf/ConnectorResolutionTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Services.Wf;
using CP6.Core.Services.Wf.Executors;
using CP6.Entity.DomainModels.Wf;
using Xunit;
using static CP6.Tests.WfsInfraTestHarness;

namespace CP6.Tests;

public class ConnectorResolutionTests
{
    private sealed class FakeAppConnector : IWfConnector
    {
        public string Name { get; }
        public string DisplayName => Name;
        public FakeAppConnector(string name) => Name = name;
        public Task<ServiceTaskResult> CallAsync(string p, string? j, ServiceTaskContext c)
            => Task.FromResult(ServiceTaskResult.Ok(new Dictionary<string, object?> { ["src"] = "app" }));
    }

    [Fact]
    public async Task Resolve_TenantRowPreferred_OverAppRegistration()
    {
        using var conn = NewSqliteWithSchema();
        using (var db = Ctx(conn))
        {
            db.Wf_Connectors.Add(new Wf_Connector { Name = "erpEcho", DisplayName = "租户 Echo",
                BaseUrl = "https://tenant", TimeoutSec = 120, Enabled = true });
            await db.SaveChangesAsync();
        }
        using var db2 = Ctx(conn);
        // resolver：先查租户表（命中 erpEcho）→ 不回落 app 的 FakeAppConnector("erpEcho")
        var resolver = new TenantConnectorResolver(db2, new IWfConnector[] { new FakeAppConnector("erpEcho") },
            Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create("CP6.Tests"), TimeoutSecToClientFactoryStub());
        var c = await resolver.ResolveAsync("erpEcho", CancellationToken.None);
        Assert.IsType<DbWfConnector>(c);   // 租户行优先
    }

    [Fact]
    public async Task Resolve_FallsBackToApp_WhenNoTenantRow()
    {
        using var conn = NewSqliteWithSchema();
        using var db = Ctx(conn);
        var resolver = new TenantConnectorResolver(db, new IWfConnector[] { new FakeAppConnector("erpEcho") },
            Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create("CP6.Tests"), TimeoutSecToClientFactoryStub());
        var c = await resolver.ResolveAsync("erpEcho", CancellationToken.None);
        Assert.IsType<FakeAppConnector>(c);   // 无租户行 → app 兜底
    }

    [Fact]
    public async Task Catalog_MergesBothSources_TenantRowDedups()
    {
        using var conn = NewSqliteWithSchema();
        using (var db = Ctx(conn))
        {
            db.Wf_Connectors.Add(new Wf_Connector { Name = "erpEcho", DisplayName = "租户 Echo", BaseUrl = "https://t", TimeoutSec = 120, Enabled = true });
            db.Wf_Connectors.Add(new Wf_Connector { Name = "erpProd", DisplayName = "ERP 生产", BaseUrl = "https://p", TimeoutSec = 120, Enabled = true });
            await db.SaveChangesAsync();
        }
        using var db2 = Ctx(conn);
        var resolver = new TenantConnectorResolver(db2, new IWfConnector[] { new FakeAppConnector("erpEcho") },
            Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create("CP6.Tests"), TimeoutSecToClientFactoryStub());
        var names = (await resolver.ListCatalogAsync(CancellationToken.None)).Select(x => x.Name).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "erpEcho", "erpProd" }, names);   // erpEcho 去重（租户行优先），app-only 项保留
    }

    private static System.Net.Http.IHttpClientFactory TimeoutSecToClientFactoryStub()
        => new StubHttpClientFactory();

    private sealed class StubHttpClientFactory : System.Net.Http.IHttpClientFactory
    {
        public System.Net.Http.HttpClient CreateClient(string name) => new();
    }
}
```

- [ ] **Step 2: FAIL → 实现**

- `WfConnectorService`（ctor 注入 `CP6Context`、`IDataProtectionProvider`（`CreateProtector("Wfs.Connector.Auth")`）、`WfsInfraOptions`、`int leaseSeconds`——**租约时长从波①启动护栏同源常量取**，落地前 grep `WfServiceJobService` 的 lease/`LeaseDuration` 常量确认确切值，用同一常量，勿臆造）：`CreateAsync/UpdateAsync`（加密 `AuthJson`→`AuthJsonEncrypted`；E-WF-028 校验 `TimeoutSec*1 >= leaseSeconds` 否则抛 `InvalidOperationException("E-WF-028 …")`；空 `AuthJson`→`AuthJsonEncrypted=null`）；`GetAsync/ListAsync`（返回掩码 DTO `{Id,Name,DisplayName,BaseUrl,TimeoutSec,Enabled,HasAuth}`，**永不含明文**）；`DecryptAuthAsync`（执行侧解密）；`SetEnabledAsync`。
- `DbWfConnector : IWfConnector`（从 `Wf_Connector` 行 + 解密 auth 构造；`CallAsync` 走注入的 `IHttpClientFactory.CreateClient` HttpClient，超时=`TimeoutSec`，按 auth type 加 header；Idempotency-Key 沿 `ctx.JobId` 口径）。
- `TenantConnectorResolver`（新解析器：`ResolveAsync(name)`＝先查 `Wf_Connectors` Enabled 行→包 `DbWfConnector`；未命中回落 app 字典；`ListCatalogAsync`＝两源合并、租户行按 Name 去重优先）。
- `WebApiExecutor` 改造：ctor 从收 `IEnumerable<IWfConnector>` 改为注入 `TenantConnectorResolver`（内部持 app 字典 + CP6Context）；`ExecuteAsync` 用 `await resolver.ResolveAsync(connectorName, ...)`，未命中仍 `E-WF-018 连接器未注册`。**EchoConnector 等 app 级注册（Program.cs:138）零改动**——它们进 resolver 的 app 兜底字典。

- [ ] **Step 3: DI（`Program.cs`）** — 注册 `TenantConnectorResolver`（scoped，注入 `CP6Context` + `IEnumerable<IWfConnector>`（app 级）+ `IDataProtectionProvider` + `IHttpClientFactory` + `WfsInfraOptions`）；`WebApiExecutor` 注册改注入 resolver；`AddScoped<IWfConnectorService, WfConnectorService>()`。

- [ ] **Step 4: PASS + 全量闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "WfConnectorServiceTests|ConnectorResolutionTests|Wf"
git add -A && git commit -m "feat(wfs-infra): D-T1 Wf_Connector加密服务(DataProtection密文往返+掩码)+DbWfConnector+租户优先app兜底解析+E-WF-028"
```

---

## Task D-T2: 连接器管理 tab（CRUD UI + 掩码 + 启停）

**Files:** Create `CP6.WebApi/Controllers/Oa/WfConnectorController.cs`；Create `cp6.web/src/api/oa/wfConnector.ts` + `WfConnectorPanel.vue` + `WfConnectorDialog.vue`；Modify 管理页挂 tab + 菜单/权限 seed；Test `CP6.Tests/Wf/WfConnectorControllerTests.cs`、vitest。

- [ ] **Step 1: 控制器测试**（直 new 控制器 + InMemory/SQLite + DataProtection 测试 provider）：列表返回掩码（无 `AuthJson`）、创建后 `HasAuth=true`、E-WF-028 保存返 400、启停切换。
- [ ] **Step 2: 实现** — `WfConnectorController : LocalizedControllerBase`，`[Route("api/oa/wf-connector")]`，`[RequirePermission("oa-flow-admin","Connector.View/Edit")]`（沿 `oa-flow-admin` 家族，波③映射②口径）；端点 list/get/create/update/toggle。凭证输入即写不回显。
- [ ] **Step 3: 前端** — 连接器 tab（列表/新建/编辑/启停；`AuthJson` 输入框 placeholder「已配置（不回显）」当 `HasAuth`；保存即加密）。零硬编码色（CpTag tone）。
- [ ] **Step 4: 菜单/权限 seed（`Connector.View/Edit` 挂 `oa-flow-admin` 菜单）+ commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "WfConnectorControllerTests|Wf"
cd cp6.web && npm run test && npm run type-check && npm run build && cd ..
git add -A && git commit -m "feat(wfs-infra): D-T2 连接器管理tab(CRUD/启停/凭证不回显)+Connector.View/Edit权限点"
```

---

# Wave I-E — per-node HTTP 覆盖 + 租户时区（依赖 I-A workdays + I-D 连接器）

## Task E-T1: FlowNode `ServiceHttpMethod?`/`ServiceTimeoutSec?` + 节点覆盖优先 + E-WF-028 节点校验

**Files:**
- Modify `CP6.Core/Services/Wf/FlowSchema.cs`（FlowNode 双字段，POCO 零迁移）
- Modify `CP6.Core/Services/Wf/Executors/WebApiExecutor.cs` / `DbWfConnector.cs`（节点覆盖优先→连接器默认）
- Modify `CP6.Core/Services/Wf/FlowSchemaValidator.cs`（E-WF-028 节点 TimeoutSec 静态值域/上限）
- Modify designer webApi 面板（method/timeout 两可选输入 + validateClient）
- Test `CP6.Tests/Wf/NodeHttpOverrideTests.cs`、vitest

- [ ] **Step 1: 失败测试** — 节点 `ServiceHttpMethod="PUT"`/`ServiceTimeoutSec=5` 时执行按节点值（覆盖连接器默认）；节点缺省时用连接器默认；节点 `ServiceTimeoutSec` 超上限/非正 → 静态 E-WF-028（值域）；节点 TimeoutSec < 租约 → 保存时 E-WF-028（服务层，与连接器同口径）。
- [ ] **Step 2: 实现** — FlowNode 加 `public string? ServiceHttpMethod { get; set; }` / `public int? ServiceTimeoutSec { get; set; }`（POCO，`FlowSchema.cs`）；`WebApiExecutor`/`DbWfConnector.CallAsync` 优先取 `node.ServiceHttpMethod ?? 连接器默认`、`node.ServiceTimeoutSec ?? 连接器 TimeoutSec`（node 覆盖经 `ServiceTaskContext` 传入——若 ctx 未携带 node method/timeout，则 `ServiceTaskActionRef` 或 ctx 扩展一字段承载，落地时择 ctx 现有承载点，勿新增迁移）；`FlowSchemaValidator` 加 E-WF-028 静态：`ServiceTimeoutSec` 有值时须 `>0` 且 `<= 上限`（如 3600）。
- [ ] **Step 3: 设计器 webApi 段两可选输入 + validateClient 镜像 E-WF-028 值域 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "NodeHttpOverrideTests|Wf"
cd cp6.web && npm run test && npm run type-check && npm run build && cd ..
git add -A && git commit -m "feat(wfs-infra): E-T1 FlowNode ServiceHttpMethod/ServiceTimeoutSec节点覆盖(POCO零迁移)+覆盖优先级+E-WF-028节点值域"
```

---

## Task E-T2: `ITenantClock` + 时区消费点接线 + DST 口径 + 租户管理页时区

**Files:**
- Create `CP6.Core/Services/Wf/ITenantClock.cs`（`ITenantClock` + `TenantClock`）
- Modify `CP6.Core/Services/Wf/NodeHandlers/ServiceTaskNodeHandler.cs`（ctor 加 `ITenantClock? clock`；workdays + untilDate 的 tz 源从 `TimeZoneInfo.Local` 换 `_clock.GetTenantTimeZone()`）
- Modify `Program.cs`（DI `ITenantClock`）
- Modify 租户管理页（时区下拉 + 自愈口径提示）
- Test `CP6.Tests/Wf/TenantClockTests.cs`、`CP6.Tests/Wf/WorkdaysTokyoTimeZoneTests.cs`、vitest

- [ ] **Step 1: 失败测试（null 全等回归 + 东京租户换算 + 不可解析拒绝 + DST 口径 + workdays 东京落点）**

```csharp
// CP6.Tests/Wf/TenantClockTests.cs
using System;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using Xunit;
using static CP6.Tests.WfsInfraTestHarness;

namespace CP6.Tests;

public class TenantClockTests
{
    [Fact]
    public async Task NullTimeZoneId_FallsBackToServerLocal_Regression()
    {
        using var conn = NewSqliteWithSchema();
        using (var db = Ctx(conn)) { db.Sys_Tenants.Add(new Sys_Tenant { TenantCode = "t", TenantName = "T", TimeZoneId = null }); await db.SaveChangesAsync(); }
        using var db2 = Ctx(conn);
        var clock = new TenantClock(db2, FakeTenantContext(conn), new WfsInfraOptions());
        Assert.Equal(TimeZoneInfo.Local.Id, clock.GetTenantTimeZone().Id);   // null → 服务器本地（现状全等）
    }

    [Fact]
    public async Task TokyoTimeZoneId_Resolves()
    {
        using var conn = NewSqliteWithSchema();
        Guid tid;
        using (var db = Ctx(conn)) { var t = new Sys_Tenant { TenantCode = "jp", TenantName = "JP", TimeZoneId = "Asia/Tokyo" }; db.Sys_Tenants.Add(t); await db.SaveChangesAsync(); tid = t.Id; }
        using var db2 = Ctx(conn);
        var clock = new TenantClock(db2, FakeTenantContext(conn, tid), new WfsInfraOptions());
        var tz = clock.GetTenantTimeZone();
        Assert.Equal(TimeSpan.FromHours(9), tz.GetUtcOffset(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)));   // JST +9
    }

    // FakeTenantContext：返回设定 CurrentTenantId 的 ITenantContext 桩（照仓库既有测试桩口径）。
    private static CP6.Core.Services.Common.ITenantContext FakeTenantContext(Microsoft.Data.Sqlite.SqliteConnection conn, Guid? tid = null)
        => new StubTenantContext { CurrentTenantId = tid ?? Guid.Empty };
}
```

> DST 定点测试（`WorkdaysTokyoTimeZoneTests` 或 `TenantClockTests` 内）：用一个有 DST 的时区（如 `"America/New_York"`/`"Pacific Standard Time"`）验证 `ComputeTimerDueUtcAsync` 的 workdays/untilDate 落点：跳过区间取下一有效瞬间、重复区间取首次出现（口径注释写死，日本无 DST 但字段不限日本）。`TimeZoneInfo.ConvertTimeToUtc(Unspecified, tz)` 对无效本地时刻抛/规整——实现须捕获并按口径规整（跳过→+DST 偏移取下一有效；重复→取标准时首现）。

- [ ] **Step 2: 实现 `ITenantClock`/`TenantClock`**（ctor 注入 `CP6Context` + `ITenantContext` + `WfsInfraOptions`；`GetTenantTimeZone()`：按 `CurrentTenantId` 查 `Sys_Tenant.TimeZoneId`（共享表，`IgnoreQueryFilters` 或直查，Sys_Tenant 本就不过滤）→ `TimeZoneInfo.FindSystemTimeZoneById`；null/查不到→`opts.DefaultTimeZone`→`TimeZoneInfo.Local`；解析失败→按口径回落 + 记日志。**per-scope 缓存**一份 tz 避免重复查）。

- [ ] **Step 3: 接线消费点** — `ServiceTaskNodeHandler` ctor 加 `ITenantClock? clock = null`；`ComputeTimerDueUtcAsync` 的 tz 源：`var tz = _clock?.GetTenantTimeZone() ?? TimeZoneInfo.Local;`，workdays 的 `nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz)`、回转 `ConvertTimeToUtc(fireLocal, tz)`；`ComputeDueUtc` 的 `ParseLocalDateToUtc`（untilDate/untilExpr）同样从 `TimeZoneInfo.Local` 改注入 tz（把 tz 作参传入，静态方法签名加 `TimeZoneInfo tz` 参，调用点传 `_clock?.GetTenantTimeZone() ?? TimeZoneInfo.Local`）。**存量 null 全等**：无 TimeZoneId → tz=服务器本地 → 与现状字节等价。**E-WF-028 时区不可解析**：租户管理页保存 `TimeZoneId` 时服务层 `FindSystemTimeZoneById` 校验，失败 → `E-WF-028`。

- [ ] **Step 4: 租户管理页时区下拉 + 自愈口径提示** — 下拉候选（常见 IANA/Windows id）；保存提示「改时区不批量重算既有触发器 NextDueUtc，下次发火后按新时区自愈（最多一次旧时区发火）」。**DST 口径**注释写死。vitest：下拉渲染、保存校验。

- [ ] **Step 5: 全量闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "TenantClockTests|WorkdaysTokyoTimeZoneTests|Wf"
cd cp6.web && npm run test && npm run type-check && npm run build && cd ..
git add -A && git commit -m "feat(wfs-infra): E-T2 ITenantClock时区解析(null全等回归+东京+DST口径)+workdays/untilDate接线+租户管理页时区下拉+自愈口径"
```

---

# Wave I-F — i18n + QA（依赖全部）

## Task F-T1: 五语 i18n seed（~30 键）+ 菜单/权限 seed 汇总

**Files:** Create `CP6.WebApi/Seed/I18nOaEngineInfraScreenSeed.cs`；Modify `Program.cs`（i18n concat + 菜单/权限 seed 汇总核对）

- [ ] **Step 1: i18n seed**（五语 ZhCN/ZhTW/En/Ja/Ko，仿 `I18nOaServiceTaskScreenSeed`；**先 grep 既有 I18nOa* 去重 LangKey**，通用键 `common.*` 不重复放）。覆盖键面：
  - 年历：`oa.workcalendar.title/empty/importJp/importJpConfirm/day.workday/day.holiday/note/year` 等
  - workdays 延时：`oa.designer.svc.delay.workdays`（沿 svc 家族）
  - 超时错误边：`oa.designer.timeout.errorEdge`
  - 连接器：`oa.connector.tab/new/name/baseUrl/auth/authMasked/timeoutSec/enabled/empty` 等
  - 时区：`oa.tenant.timezone/timezone.hint（自愈口径）`
  - 错误码：`E-WF-027`/`E-WF-028`（五语）
  ```csharp
  // 关键错误码行（其余键照上列表补全，~30 键）
  new() { LangKey = "E-WF-027", ZhCN = "超时走失败边的节点缺少失败边", ZhTW = "超時走失敗邊的節點缺少失敗邊", En = "Node with errorEdge timeout action lacks an error edge", Ja = "エラー辺タイムアウトのノードに失敗辺がありません", Ko = "실패 경로 타임아웃 노드에 실패 경로가 없습니다" },
  new() { LangKey = "E-WF-028", ZhCN = "超时配置或时区非法", ZhTW = "超時配置或時區非法", En = "Invalid timeout or timezone", Ja = "タイムアウトまたはタイムゾーンが無効です", Ko = "타임아웃 또는 시간대가 잘못되었습니다" },
  ```
- [ ] **Step 2: `Program.cs` i18n concat 追加**（`:1819` 同块）：`.Concat(CP6.WebApi.Seed.I18nOaEngineInfraScreenSeed.Items)   // WFS infra oa.workcalendar.*/oa.connector.*/oa.tenant.timezone/E-WF-027/028`
- [ ] **Step 3: 菜单/权限 seed 汇总核对**（年历 `oa-work-calendar` + `Calendar.View/Edit`、连接器 `Connector.View/Edit` 已在 A-T4/D-T2 落，本任务复核幂等块齐全 + RoleId=1 授权）。
- [ ] **Step 4: 验证 + commit**

```bash
dotnet build CP6.WebApi/CP6.WebApi.csproj
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-infra): F-T1 五语i18n seed(年历/连接器/时区/E-WF-027-028 ~30键)+菜单权限汇总核对"
```

---

## Task F-T2: gstack QA harness（只写不跑）+ DoD 自查

**Files:** Create `docs/superpowers/qa/wfs-engine-infra/{README.md, seed.sql, qa_infra.ps1}`

- [ ] **Step 1: 写 harness**（结构照 ServiceTask E-T3 先例；隔离库 `CP6DB_OA` 真 SQL Server，只写不跑服务器）。剧本 8 条：
  1. **年历勾选→timer 3 工作日实算**（真浏览器）：年历页→勾一天为假日→设计器建 timer 节点 `workdays=3`→试跑→DueAt 落 3 个工作日后 09:00（跨假日/振替验算）。
  2. **年历空态导入**：新租户年历页显示空态提示+「导入日本法定假日」按钮→点击→35 行入库→日历渲染假日态。
  3. **approval 超时走失败边实况**：建带 `TimeoutAction=errorEdge`+IsError 边的 approval→触发超时→原待办作废、token 进失败边节点、`timeoutError` 变量注入；无 IsError 边保存→设计器报 E-WF-027（抽 2 语验 i18n）。
  4. **三既有超时动作零回归**：remind/approve/reject/escalate 各跑一遍确认不变。
  5. **清理 worker**：seed 超龄终态 job + 占坑 `Wf_TriggerFire` + 老化占坑→触发清理→终态删、在途/占坑留、OperLog 记删除+老化计数。
  6. **连接器 tab 全流程**：建连接器（凭证输入）→列表 `HasAuth` 掩码不回显→刷新后仍掩码→执行服务任务解密成功；`TimeoutSec<租约` 保存→E-WF-028；租户连接器与 app EchoConnector 同名→租户优先。
  7. **节点 HTTP 覆盖**：serviceTask webApi 节点填 method=PUT/timeout=5→执行按节点值。
  8. **租户时区**：租户设 `Asia/Tokyo`→timer `untilDate`/workdays 按东京时刻解释；改时区不批量重算提示；`TimeZoneId` 填乱码→E-WF-028。
  - seed.sql：OA 单数表名、`SET QUOTED_IDENTIFIER ON`；seed enabled 流程 + QA 用户 + 一个连接器。ps1：连接器 CRUD + E-WF-028 e2e（ASCII 数据）。
- [ ] **Step 2: commit** — `git add -A && git commit -m "test(wfs-infra): F-T2 gstack QA harness(8剧本+seed+e2e脚本，只写不跑)"`
- [ ] **Step 3: 末期 live QA（用户在场）** — 隔离库 `CP6DB_OA` 起后端+前端→跑 ps1+gstack 真浏览器过 8 剧本。抓 bug 当场 TDD 修。

---

## DoD / 验收

- [ ] 后端 `dotnet test CP6.Tests/CP6.Tests.csproj` 全绿（1509[5 skip] → +N）；**既有 Wf 测试字节等价**（三既有超时动作/ComputeDueUtc 三模式/连接器 app 兜底零回归）。
- [ ] 前端 `npm run test`（320 → +N）/ `npm run type-check` / `npm run build` 全绿。
- [ ] EF `dotnet ef migrations has-pending-model-changes` clean；**本波恰一次迁移 `WfsInfra`**（两新表 + 一新列，零其他改动）。
- [ ] **零跨模块污染**：`git diff --stat fb90d75..HEAD` 仅落在 `{Sys,Wf}` 实体 / `Services/{Wf,Sys}` / WebApi(Program/BackgroundServices/Controllers/Oa+Sys/Seed) / `cp6.web/src/{api,views}/oa`；无 Space/WMS/MES/FIN/PUR 业务文件。
- [ ] spec §8 测试矩阵全覆盖（见下表）；E-WF-027/028 各有静态+服务层专测。
- [ ] 五语 seed 齐（ZhCN/ZhTW/En/Ja/Ko）、LangKey 无重复；权限点 `Calendar.View/Edit` + `Connector.View/Edit` seed + RoleId=1 授权。
- [ ] 零硬编码色（CpTag tone / Design System token）。
- [ ] **日本假日 seed 35 日期**（2026×18 + 2027×17，含振替休日与 2026-09-22 国民の休日），seed 幂等（(TenantId,Date) 去重）。
- [ ] **DataProtection 密钥环持久化已落地**（D-T0；生产配 `DataProtection:KeyPath`），runbook:112 隐患修复说明补齐。
- [ ] gstack QA harness 齐（8 剧本）+ live QA 全过（用户在场，隔离库 CP6DB_OA）。

### 覆盖核对（spec §8 → 测试 → 任务）

| spec §8 条目 | 测试 | 任务 |
|---|---|---|
| 例外反转矩阵（假日/补班/普通周末/普通工作日） | `IsWorkday_ExceptionReversalMatrix` | A-T2 |
| AddWorkdays 跨周末+假日+振替 | `AddWorkdays_SkipsWeekendsHolidaysAndSubstitute` | A-T2 |
| 366 天防死循环 | `AddWorkdays_366ConsecutiveNonWorkdays_FailsFast_NoInfiniteLoop` | A-T2 |
| seed 幂等 | `ImportJapaneseHolidays_Idempotent_35Rows` + `Items_Cover2026And2027_35Dates_AllDistinct` | A-T2/A-T4 |
| ComputeDueUtc 四模式 + 东京 tz + FireHour 落点 | `ComputeWorkdaysDue_LandsOnFireHour_ServerLocalToUtc` / `ComputeDueUtc_ExistingThreeModes_ByteEquivalent` / `WorkdaysTokyoTimeZoneTests` | A-T3/E-T2 |
| errorEdge 路由 + 待办作废 + 三既有动作零回归 | `Timeout_ErrorEdge_VoidsPendingTask_RoutesAlongErrorEdge` / `Timeout_Reject_ByteEquivalent_NoRegression` | B-T1 |
| 无 IsError 边配置被 E-WF-027 拦 + 来源集合放宽 | `ApprovalTimeoutErrorEdge_WithoutErrorEdge_E027` / `SubFlowErrorEdge_NowAllowed_NoE017` / `StartErrorEdge_StillRejected_E017` | B-T1 |
| 清理：终态删/在途留/占坑永不清/保留期=0/分批/老化告警 | `Cleanup_DeletesTerminalOlderThanRetention_KeepsRunningAndRecent` / `Cleanup_RetentionZero_Disabled_NothingDeleted` / `Cleanup_Batches_DeletesAllOverMultiplePasses` / （波③表就绪后）占坑+老化计数 | C-T1 |
| 连接器：租户优先 app 兜底 + 密文往返 + 掩码 + E-WF-028 + 目录合并去重 | `Resolve_TenantRowPreferred_*` / `Resolve_FallsBackToApp_*` / `Save_EncryptsAuth_ExecuteDecrypts_ReadMasks` / `Save_TimeoutBelowLease_E028_Rejected` / `Catalog_MergesBothSources_TenantRowDedups` | D-T1 |
| 节点覆盖优先级 + E-WF-028 值域 | `NodeHttpOverrideTests` | E-T1 |
| 时区 null 全等回归 + 东京 untilDate/workdays + DST 跳变口径 | `NullTimeZoneId_FallsBackToServerLocal_Regression` / `TokyoTimeZoneId_Resolves` / DST 定点 | E-T2 |
| QA harness（年历实算/连接器全流程/超时错边实况） | 剧本 1~8 | F-T2 |

### 执行顺序与依赖（spec §10）

**I-A（A-T1 → A-T2 → A-T3 → A-T4）→ { I-B（B-T1 → B-T2）‖ I-C（C-T1）‖ I-D（D-T0 → D-T1 → D-T2）} → I-E（E-T1 → E-T2）→ I-F（F-T1 → F-T2）**

- A-T1 一次落全部三处 schema + 唯一迁移 `WfsInfra`；A-T2~A-T4 只消费该 schema。
- I-B/I-C/I-D 三波仅依赖 I-A 契约，可三线并行（各自 worktree 或串行皆可，合并后跑全量闸）。**D-T0 是 D-T1 硬前置**。
- I-E 依赖 I-A（workdays 计算接线点）+ I-D（连接器节点覆盖）；E-T2 依赖 E-T1。
- I-F 依赖全部；F-T2 live QA 用户在场。
- 共 **14 个任务**。每任务收口：`--filter Wf`（或 `Wf|Sys`）既有全绿 + commit **不 push**。

---

*生成于 2026-07-05，由 spec `2026-07-05-wfs-engine-infra-design.md`（唯一权威）细化。执行铁律：worker 照抄 `TenantScopeRunner` 口径；errorEdge 节点级清场不连坐；一次迁移 `WfsInfra`；错误边来源集合单一常量（本波写全集含 subFlow，子流程 spec 只加测试）；E 波紧跟 D 波；零跨模块污染；零硬编码色。DataProtection 密钥环持久化（D-T0）是连接器加密硬前置。*
