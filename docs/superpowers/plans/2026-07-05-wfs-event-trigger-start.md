# WFS 事件触发 Start Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. **每个 Task 执行前必读对应 spec 章节**（`docs/superpowers/specs/2026-07-05-wfs-event-trigger-start-design.md`，唯一权威，不许改设计），本计划对体量大的产品代码给全代码或引用 spec 逐字代码块，测试代码在本计划内逐条给全。

**Goal:** 给 WFS 加「事件触发 start」——流程不再只能信箱手工发起，三类自动发起一期全做（D1）：**timer**（cron 定时，NCrontab）/ **event**（IntegrationEvent 业务事件联动）/ **message**（外部 REST + API key）。三入口收敛单一出口 `IFlowTriggerService.FireAsync`（D2：Enabled 检查→幂等闸→SubmitAsync→写流水→更新水位），`Wf_TriggerFire` 流水既是审计台账也是幂等闸（D7）。

**Architecture:** 两新表一次迁移（`Wf_FlowTrigger` 配置 + `Wf_TriggerFire` 流水，复合唯一幂等索引）。timer = `WfTriggerWorker`（照抄 `WfServiceJobScanWorker` 骨架 + `TenantScopeRunner` 逐租户）+ **占坑两段式**（第一段单事务「NextDueUtc 前移 + INSERT 占坑流水」＝抢占，第二段 FireAsync 补跑回填——不双发不丢发；misfire 只补最近）。event = `IWfTriggerBridgeHook`（`BridgeHookBase` 家族新成员，D4，eventId 必填入口定键 `{eventId}:{TriggerId}`）+ `IntegrationEventDispatcher` **目标泛化 fallback 路由**（target=WF 不看 source，唯一 Integration 触点）。message = REST 端点 `[AllowAnonymous]` + 自定义过滤器（API key SHA-256 常量时间比较 + Idempotency-Key 头必填 + 64KB 上限 + varsSchema 白名单）。管理 = 流程管理页（FlowAdmin.vue）加「触发器」tab。**引擎零改动**（只消费 `IFlowEngine.SubmitAsync`，FlowEngine 系文件零 diff）。

**Tech Stack:** .NET 8 / EF Core（SQL Server 生产，SQLite 测试）/ NCrontab 3.3.3（新依赖，MIT 单包无传递依赖）/ xUnit（`CP6.Tests/Wf`）/ Vue3 + Element Plus + Cp 组件库（`cp6.web/src/views/oa/admin`）/ vitest。

---

## 落码纪律（Discipline — 每个 Task 都遵守）

- **隔离 worktree**：照 superpowers:using-git-worktrees 建（off main `fb90d75`），分支 `feat/wfs-event-trigger`。
- **基线闸（每 Task 收口必跑）**：后端 `dotnet test CP6.Tests/CP6.Tests.csproj` **1509（5 skip）→ +N 全绿**；前端 `npm run test`（vitest）**320 → +N 全绿** + `npm run type-check`；EF `dotnet ef migrations has-pending-model-changes` clean（**本波恰一次迁移 `WfsFlowTrigger`**，A-T1 之后任何实体/索引改动都算破戒）。
- **零跨模块污染**：dispatcher fallback（C-T2 一个分支 + 一个 ctor 注入）是**唯一** Integration 触点；不碰既有静态路由表任何条目；不碰 Space/WMS/MES/FIN 业务文件。每 Task `git show --stat` 复核。
- **引擎零改动**：不碰 `FlowEngine*.cs` / `NodeHandlers` / `FlowSchemaValidator.cs`；既有 Wf 测试字节等价全绿。
- **subagent-driven TDD**：每 Task 全新子代理（编码用 Opus 4.8 档）→ 主代理 `git show` diff 复核 → 本地 commit **不 push**。先写失败测试→FAIL→最小实现→PASS→commit。提交信息 `feat(wfs-trigger): ...` 中文。
- **五语 i18n**：ZhCN/ZhTW/En/Ja/Ko 五列（`Sys_Lang`），视图全 `t()`，**零硬编码色**（CpTag tone、Design System v1.0 token）。
- **测试脚手架**：SQLite in-memory 共享连接，照 `CP6.Tests/Wf/FlowConcurrencyTests.cs` 的 `GenerateCreateScript()` + `Regex.Replace("n?varchar\(max\)","TEXT")` 建库；rowversion 靠 AFTER UPDATE 触发器模拟（本波需给 `Wf_FlowTrigger` 也建同款触发器）；时间全部**注入 `nowUtc`**（实现类测试重载）。
- **不重新设计**：spec 决策 D1~D7 + §3 三执行架构 + §5 错误码全锁。

---

## 现状锚点速查（侦察结论 2026-07-05，executor 免重查）

| 锚点 | 现状（已实读核实） |
|---|---|
| 发起入口 | **`IFlowEngine.SubmitAsync(string flowKey, Guid starterId, string varsJson, string? bizType = null, string? bizId = null)` → `Task<Guid>`**（`IFlowEngine.cs:13`；引擎内 `inst.StarterId = starterId` 贯穿 starter.* 审批人解析）。**不存在 `StartAsync`**（见下冲突表①）。草稿版 `StartDraftAsync(Guid instanceId, Guid actorId)` 与本波无关。 |
| 租户切换现状写法（spec §6 照抄对象） | `CP6.WebApi/BackgroundServices/TenantScopeRunner.cs`：`ForEachTenantAsync(scopeFactory, body, logger, ct)` —— 先开 scope 用 `ITenantEnumerator.ListActiveAsync()` 取启用租户；**逐租户 `CreateScope()` → `scope.ServiceProvider.GetRequiredService<ITenantContext>().CurrentTenantId = tenantId`（setter 赋值即切换）→ 跑 body**；单租户异常记日志跳过继续。service 层**零租户感知**（查询不带 TenantId 条件，全靠 `CP6Context` 全局过滤读 scoped `ITenantContext`）。 |
| worker 骨架 | `WfServiceJobScanWorker.cs`（52 行）：`Interval=20s`；进程级 `_workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}"`；`ExecuteAsync` while 循环内 `TenantScopeRunner.ForEachTenantAsync(...)`，`catch (OperationCanceledException) when (...) { throw; } catch (Exception ex) { _logger.LogError(...) }`，`await Task.Delay(Interval, stoppingToken)`。 |
| lease/乐观并发写法 | `WfServiceJobService.cs:80-86`：置字段后 `try { await _db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { await _db.Entry(job).ReloadAsync(ct); continue; }`。常量 `BatchSize=50`、`Trunc` 截 1000。 |
| BridgeHook 家族 | `BridgeHookBase(CP6Context db, ILogger logger)`，protected `Db`/`Logger`；`PersistEventAsync(sourceModule, targetModule, hookName, sourceNo, targetNo, status, error, correlationId, payload)` 写 `IntegrationEvents` outbox 行（`:59-79`，Failed 时 `NextRetryAt=UtcNow+60s`），整体 try/catch 吞错只记日志。范本子类 `MesBridgeHook : BridgeHookBase, IMesBridgeHook`——`corrId = Guid.NewGuid()`、payload=方法参数匿名对象、hookName=`nameof(方法)`、三分支（Success/Skipped/Failed）各 persist 一次。接口范本 `IMesBridgeHook`：接口 + Result 类（Ok/Skipped/Failed 工厂）+ NoOp 实现同文件。 |
| dispatcher | `IntegrationEventDispatcher.cs`：静态字典 `Routes`（键 `RouteKey(source,target,hook)` = `$"{source}\|{target}\|{hook}"`，`:102-103`）；`DispatchAsync(IntegrationEvent evt, CancellationToken ct)`（`:106-120`）——`:110` 算 key，`:111` `TryGetValue` 失败抛 `InvalidOperationException("DISPATCH-404: ...")`。**fallback 插在 `:110` 与 `:111` 之间**。ctor 注入六个 hook 接口。`IntegrationEventStatus` 是字符串常量（`"SUCCESS"/"SKIPPED"/"FAILED"/"DEAD"`）。 |
| retry worker | `IntegrationEventRetryWorker.cs:81-110`：`TenantScopeRunner` 逐租户，取 `Status==Failed && NextRetryAt<=now` Take(50)，`dispatcher.DispatchAsync(evt, ct)` 返回 bool 定 Success/Failed，异常 catch 记 `LastError` 退避，`Attempts>=MaxAttempts` 转 DeadLetter。 |
| 实体/迁移范本 | `Wf_ServiceJob.cs`：`[Table("Wf_ServiceJob")] : BaseTenantEntity`，字符串用 `[MaxLength(n)]`（无长度=nvarchar(max)），`[Timestamp] byte[]? RowVersion`。`BaseTenantEntity` 提供 `Id/Creator/CreateDate/Modifier/ModifyDate/TenantId`。索引在 `CP6Context.OnModelCreating`（`:733-744` 范本）。迁移命令：`dotnet ef migrations add <名> --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context`；**不手写迁移文件**。 |
| 包管理 | **非 CPM**（无 `Directory.Packages.props`）；csproj 内联 `<PackageReference Include="X" Version="Y" />`。**全仓无任何 cron 包**（NCrontab/Quartz/Cronos/Hangfire 均无）→ NCrontab 为绿地引入（B-T1）。 |
| API key 先例 | **无现成 API key 基建**，但三处可复刻：`TwoFactorService.cs:137-149` `Sha256Hex` + `FixedTimeEquals`（`CryptographicOperations.FixedTimeEquals`，先比长度）；`RefreshTokenService.cs:31-33` `NewRaw()`=32 字节随机 Base64Url + `HashOf()`=SHA-256 hex 入库（库内只存哈希）+ 查库 `IgnoreQueryFilters()`（令牌即凭证跨租户定位）；`RequirePlatformAdminAttribute.cs`＝自定义 `IAsyncAuthorizationFilter` 先例（特性经 `RequestServices` 服务定位取依赖，失败设 `context.Result` 短路）。 |
| 权限/菜单 | 权限模型=MenuAction：`[RequirePermission(menuKey, action)]`（`CP6.Core/Auth/RequirePermissionAttribute.cs`，→`IPermissionService.HasActionAsync`）；menuKey = `RoutePath.Trim('/').Replace('/','-')` 派生（sso/field-audit 先例）。菜单种子内联 `Program.cs`（734=流程管理 `/oa/flow-admin`，**当前 MenuKey=null、控制器仅 `[Authorize]`**）；动作点 seed=`Sys_MenuAction`（定义）+`Sys_RoleAction`（RoleId=1 授予）幂等块（Program.cs:850-856 范本）。 |
| 前端 | 流程管理页=`cp6.web/src/views/oa/admin/FlowAdmin.vue`（97 行，CpPageShell+CpListPage，**当前无 tab**）。API 范式 `cp6.web/src/api/oa/*.ts`（`import http from '../http'`，导出 `xxxApi` 字面量，剥壳 `res.data ?? res`）。CpTag 用 `:tone="'ok'\|'muted'"`；对话框直接 el-dialog（`SendBackDialog.vue` 范本）。 |
| i18n seed | `CP6.WebApi/Seed/I18nOa*ScreenSeed.cs` 静态 `Sys_Lang[] Items`（五列 ZhCN/ZhTW/En/Ja/Ko + LangKey；错误码直接以 `E-WF-0xx` 作 LangKey）；注册在 `Program.cs:1812-1814` `.Concat(...Items)` 链追加。 |
| 控制器范式 | `FlowAdminController : LocalizedControllerBase`，`[ApiController][Route("api/oa/flow-admin")][Authorize]`；私有 `Ok2(data)` = `Ok(new { code = 0, message = "OK", data })`、`Err(e)` = 400 壳。 |
| DI | `Program.cs:107-108`：`AddScoped<FlowEngine>()` + `AddScoped<IFlowEngine>(sp => sp.GetRequiredService<FlowEngine>())`（同 scoped 实例）。hook 注册范本 `:441-448`（配置开关选真/NoOp）。 |
| 字段核实 | `Sys_User.Enable`（bool，`Sys_User.cs:40`）、`Wf_FlowDef.Enable`（bool，`Wf_FlowDef.cs:33`）、`Sys_Menu.MenuKey`（可空，filtered 唯一索引）。 |

## spec ↔ 现状映射（不改 spec，落地口径）

| # | spec 表述 | 现状/落地口径 |
|---|---|---|
| ① | `FlowEngine.StartAsync`（§1/§3.1「StartAsync」） | 仓库实际发起入口是 **`IFlowEngine.SubmitAsync`**（签名见锚点表）。spec 的「StartAsync」语义即它，本计划一律落 `SubmitAsync(trigger.FlowKey, trigger.StarterUserId, varsJson)`。 |
| ② | 权限点 `OA.FlowTrigger.View/Edit`（§6） | 权限模型无字符串权限点，落地=菜单 734 回填 `MenuKey="oa-flow-admin"`（RoutePath 派生口径）+ `Sys_MenuAction` ActionCode **`FlowTrigger.View` / `FlowTrigger.Edit`** + RoleId=1 授予；控制器 `[RequirePermission("oa-flow-admin","FlowTrigger.View/Edit")]`。spec 权限点名原样保留在 ActionCode。 |
| ③ | UI 预设「每月末」（§4） | NCrontab 标准 5 段**无 `L` 语义**。预设「每月末」落 `0 0 28 * *` 并在 UI 文案注明「按每月 28 日近似」；真月末与工作日口径同列 spec §9 留后条目。cron 边界测试用「每月 31 日只在大月发」「2/29 只闰年发」验证 NCrontab 行为。 |
| ④ | `EventKey` 提列可索引（§2.1）、幂等复合唯一索引（§2.2） | SQL Server 索引键列不能是 nvarchar(max) → `FlowKey/EventKey [MaxLength(200)]`、`IdempotencyKey [MaxLength(200)]`、`ApiKeyHash/PayloadHash [MaxLength(64)]`、`Error [MaxLength(1000)]`。这是 spec「提列正是为可索引」的必然实现细节，非设计变更。message 端点与 event hook 相应校验键长 ≤200。 |
| ⑤ | `ScanTimersOnceAsync(CancellationToken ct)`（§3.1 接口） | 接口签名照 spec 不动；实现类 `FlowTriggerService` 另给 **`ScanTimersOnceAsync(DateTime nowUtc, CancellationToken ct)` 测试重载**（对齐 ServiceJob「注入 nowUtc」测试铁律），接口方法委托 `DateTime.UtcNow`。 |
| ⑥ | 「StartAsync 与流水在同一 SaveChanges 事务」（§3.1） | `SubmitAsync` 内部自带 `SaveChangesAsync` → 用 **显式事务** `BeginTransactionAsync` 包「SubmitAsync + 流水回填 + LastFiredUtc」达成同一原子提交（第二段整体原子）；占坑第一段在事务**之外**先行落库（两段式本义）。 |
| ⑦ | dispatcher 重放（§3.3） | hook 家族被 dispatcher 重放时若原样调 `OnEventAsync` 会**每次重放再写一行新 outbox**（Failed 行自增殖）。故接口拆双入口：`OnEventAsync`（业务调用，写台账）+ `ReplayEventAsync`（dispatcher 重放专用，同一执行逻辑**不再写新 outbox 行**，去重仍靠 TriggerFire 幂等闸）。spec「失败自动进 outbox / 重放原样复用 eventId」语义不变。 |

---

## File Structure（创建/修改清单，每文件一职责）

**后端 `CP6.Entity`**
- Create `CP6.Entity/DomainModels/Wf/Wf_FlowTrigger.cs` — 触发器配置实体（BaseTenantEntity）。
- Create `CP6.Entity/DomainModels/Wf/Wf_TriggerFire.cs` — 触发流水实体（审计+幂等闸）。

**后端 `CP6.Core`**
- Modify `CP6.Core/Services/Wf/WfStatus.cs` — 加 `WfTriggerType` 常量类。
- Create `CP6.Core/Services/Wf/WfTriggerConfig.cs` — ConfigJson 三分型 DTO + 解析（纯逻辑）。
- Create `CP6.Core/Services/Wf/FlowTriggerService.cs` — `IFlowTriggerService` + `TriggerFireResult` + FireAsync（幂等闸/占坑复用/E-WF-022~024 运行时检）+ ScanTimersOnceAsync（占坑两段式+补跑+misfire）。
- Create `CP6.Core/Services/Wf/WfCronHelper.cs` — NCrontab 包装（IsValid/NextUtc/PreviewUtc，app 默认时区解释、存 UTC）。
- Create `CP6.Core/Services/Wf/WfTriggerVarsMapper.cs` — event varsMap 点路径映射（复用 ServiceVarsHelper 口径）+ message varsSchema 白名单过滤（纯逻辑）。
- Create `CP6.Core/Services/Wf/WfApiKeyHelper.cs` — key 生成/哈希/常量时间校验（复刻 RefreshToken/TwoFactor 先例）。
- Create `CP6.Core/Services/Integration/IWfTriggerBridgeHook.cs` — 接口 + `WfTriggerBridgeResult` + `WfTriggerEventPayload` + NoOp（同文件，仿 IMesBridgeHook）。
- Create `CP6.Core/Services/Wf/WfTriggerBridgeHook.cs` — BridgeHookBase 家族新成员（OnEventAsync/ReplayEventAsync）。
- Modify `CP6.Core/Services/Integration/IntegrationEventDispatcher.cs` — **唯一 Integration 触点**：ctor 注入 `IWfTriggerBridgeHook` + `DispatchAsync` 加 target=WF fallback 分支。
- Create `CP6.Core/Auth/WfTriggerApiKeyAttribute.cs` — message 端点自定义过滤器（key/幂等头/404 不区分/租户切换）。
- Create `CP6.Core/Services/Wf/FlowTriggerAdminService.cs` — 管理 CRUD + 手动试发 + 流水查询 + key 重置（T-E）。
- Create `CP6.Core/Services/Wf/FlowTriggerValidator.cs` — 保存时校验 E-WF-022/023（T-F）。
- Modify `CP6.Core/CP6.Core.csproj` — `<PackageReference Include="NCrontab" Version="3.3.3" />`。
- Create 迁移 `CP6.Core/Migrations/<ts>_WfsFlowTrigger.cs`（由 `dotnet ef` 生成，恰一次）。
- Modify `CP6.Core/EFDbContext/CP6Context.cs` — 两 DbSet + 四索引。

**后端 `CP6.WebApi`**
- Create `CP6.WebApi/BackgroundServices/WfTriggerWorker.cs` — timer 扫描 worker（克隆 WfServiceJobScanWorker + TenantScopeRunner）。
- Create `CP6.WebApi/Controllers/Oa/FlowTriggerFireController.cs` — message 外呼端点（AllowAnonymous+过滤器）。
- Create `CP6.WebApi/Controllers/Oa/FlowTriggerAdminController.cs` — 管理 CRUD/试发/流水/重置 key/cron 预览 + Echo 样例事件源端点。
- Modify `CP6.WebApi/Program.cs` — DI（IFlowTriggerService/hook/worker/admin service）+ 菜单 734 MenuKey 回填 + MenuAction/RoleAction seed + i18n concat。
- Create `CP6.WebApi/Seed/I18nOaFlowTriggerScreenSeed.cs` — 五语键。

**前端 `cp6.web`**
- Create `cp6.web/src/api/oa/flowTrigger.ts` — API 封装 + 类型。
- Create `cp6.web/src/views/oa/admin/flowTriggerModel.ts` — 纯逻辑（类型标签/预设/表单校验，vitest 可测）。
- Modify `cp6.web/src/views/oa/admin/FlowAdmin.vue` — el-tabs 包裹（流程 tab=既有内容原样 + 触发器 tab）。
- Create `cp6.web/src/views/oa/admin/FlowTriggerPanel.vue` — 触发器列表 + 流水抽屉 + 试发/重置 key。
- Create `cp6.web/src/views/oa/admin/FlowTriggerDialog.vue` — 新建/编辑分型表单（cron 预设+预览）。

**测试 / QA**
- Create `CP6.Tests/Wf/FlowTrigger*.cs` — 各 Task 测试（见各任务）。
- Create `cp6.web/src/views/oa/admin/__tests__/flowTriggerModel.spec.ts` — vitest。
- Create `docs/superpowers/qa/wfs-flow-trigger/{README.md,seed.sql,qa_flow_trigger.ps1}` — gstack harness（照 ServiceTask E-T3 结构）。

---

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

---

## Wave T-A — 数据模型 + 单一出口服务

### Task A-T1: 常量 + 两实体 + DbSet/索引 + 一次迁移 `WfsFlowTrigger`

**Files:**
- Modify: `CP6.Core/Services/Wf/WfStatus.cs`（追加 WfTriggerType）
- Create: `CP6.Entity/DomainModels/Wf/Wf_FlowTrigger.cs`
- Create: `CP6.Entity/DomainModels/Wf/Wf_TriggerFire.cs`
- Modify: `CP6.Core/EFDbContext/CP6Context.cs`（DbSet + 索引）
- Create: 迁移 `CP6.Core/Migrations/<ts>_WfsFlowTrigger.cs`（`dotnet ef` 生成）
- Test: `CP6.Tests/Wf/FlowTriggerModelTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/FlowTriggerModelTests.cs
using System;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Xunit;

public class FlowTriggerModelTests
{
    [Fact]
    public void WfTriggerType_Constants()
    {
        Assert.Equal(0, WfTriggerType.Timer);
        Assert.Equal(1, WfTriggerType.Event);
        Assert.Equal(2, WfTriggerType.Message);
    }

    [Fact]
    public void Wf_FlowTrigger_Defaults()
    {
        var t = new Wf_FlowTrigger { FlowKey = "fk-demo", TriggerType = WfTriggerType.Timer, StarterUserId = Guid.NewGuid() };
        Assert.Equal("{}", t.ConfigJson);
        Assert.False(t.Enabled);
        Assert.Null(t.EventKey);
        Assert.Null(t.NextDueUtc);
        Assert.Null(t.LastFiredUtc);
        Assert.Null(t.ApiKeyHash);
    }

    [Fact]
    public void Wf_TriggerFire_Defaults()
    {
        var f = new Wf_TriggerFire { TriggerId = Guid.NewGuid(), IdempotencyKey = "k1", FiredUtc = DateTime.UtcNow, Source = WfTriggerType.Event };
        Assert.Null(f.InstanceId);
        Assert.Null(f.Error);
        Assert.Null(f.PayloadHash);
    }
}
```

- [ ] **Step 2: 跑测试验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter FlowTriggerModelTests`，预期编译失败（类型不存在）。

- [ ] **Step 3: 实现**

`WfStatus.cs` 追加：

```csharp
/// <summary>流程触发器类型（事件触发 start 增量，spec §2.1）。</summary>
public static class WfTriggerType
{
    public const int Timer = 0;
    public const int Event = 1;
    public const int Message = 2;
}
```

`Wf_FlowTrigger.cs`（spec §2.1 逐字 + 映射表④的 MaxLength——索引键列不能 nvarchar(max)）：

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>流程触发器（timer/event/message 三型，spec §2.1）。配置挂流程级（D5），不进设计器 schema。</summary>
[Table("Wf_FlowTrigger")]
public class Wf_FlowTrigger : BaseTenantEntity
{
    /// <summary>目标流程（对齐 SubmitAsync 口径）</summary>
    [MaxLength(200)] public string FlowKey { get; set; } = "";

    /// <summary>WfTriggerType: Timer=0 / Event=1 / Message=2</summary>
    public int TriggerType { get; set; }

    /// <summary>分型配置（spec §2.3）：timer={cron,varsJson} / event={varsMap} / message={varsSchema}</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string ConfigJson { get; set; } = "{}";

    public bool Enabled { get; set; }

    /// <summary>event 专用（提列可索引；格式 "{SourceModule}|{HookName}"；ConfigJson 不再重复存）</summary>
    [MaxLength(200)] public string? EventKey { get; set; }

    /// <summary>名义发起人（D6，必填）——审计与 starter.* 审批人解析都依赖它</summary>
    public Guid StarterUserId { get; set; }

    /// <summary>timer 专用：下次到期（扫描键，UTC）</summary>
    public DateTime? NextDueUtc { get; set; }

    public DateTime? LastFiredUtc { get; set; }

    /// <summary>message 专用：SHA-256 hex（明文只在创建/重置响应显示一次）</summary>
    [MaxLength(64)] public string? ApiKeyHash { get; set; }

    /// <summary>乐观并发（多实例 worker 抢占）</summary>
    [Timestamp] public byte[]? RowVersion { get; set; }
}
```

`Wf_TriggerFire.cs`（spec §2.2 逐字 + MaxLength）：

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>触发流水 = 审计台账 + 幂等闸（D7）。占坑行：InstanceId==null && Error==null。</summary>
[Table("Wf_TriggerFire")]
public class Wf_TriggerFire : BaseTenantEntity
{
    public Guid TriggerId { get; set; }

    /// <summary>复合唯一索引（TenantId,TriggerId,IdempotencyKey）＝幂等闸权威判据；键非空必填，无需 filtered（D7）</summary>
    [MaxLength(200)] public string IdempotencyKey { get; set; } = "";

    public DateTime FiredUtc { get; set; }

    /// <summary>成功发起的流程实例；null=占坑未完成或失败</summary>
    public Guid? InstanceId { get; set; }

    /// <summary>同 WfTriggerType（冗余便查）</summary>
    public int Source { get; set; }

    /// <summary>发起失败原因（结构化码+detail）</summary>
    [MaxLength(1000)] public string? Error { get; set; }

    /// <summary>message/event 负载 SHA-256（审计，不存原文）</summary>
    [MaxLength(64)] public string? PayloadHash { get; set; }
}
```

`CP6Context.cs`：DbSet 两行（放 `Wf_ServiceJobs` 声明同块）：

```csharp
/// <summary>流程触发器配置（事件触发 start 增量，spec §2.1）</summary>
public DbSet<Wf_FlowTrigger> Wf_FlowTriggers { get; set; }
/// <summary>触发流水（审计+幂等闸，spec §2.2）</summary>
public DbSet<Wf_TriggerFire> Wf_TriggerFires { get; set; }
```

`OnModelCreating`（放 `Wf_ServiceJob` 索引块之后，索引照 spec §2.1/§2.2 原文三＋一）：

```csharp
modelBuilder.Entity<Wf_FlowTrigger>(b =>
{
    b.HasIndex(x => new { x.TenantId, x.FlowKey }).HasDatabaseName("IX_Wf_FlowTrigger_Flow");
    // 扫描索引（spec §2.1 原文列序，不含 TenantId——worker 逐租户 scope 下全局过滤补 TenantId 条件）
    b.HasIndex(x => new { x.Enabled, x.TriggerType, x.NextDueUtc }).HasDatabaseName("IX_Wf_FlowTrigger_Scan");
    b.HasIndex(x => new { x.TenantId, x.EventKey }).HasDatabaseName("IX_Wf_FlowTrigger_Event");
});
modelBuilder.Entity<Wf_TriggerFire>(b =>
{
    // 键非空必填 → 无需 filtered（ServiceJob 先例 filtered 是因其键可空，此处不是——D7 原文）
    b.HasIndex(x => new { x.TenantId, x.TriggerId, x.IdempotencyKey })
        .IsUnique().HasDatabaseName("UX_Wf_TriggerFire_Idem");
});
```

- [ ] **Step 4: 跑测试验证 PASS** — `dotnet test ... --filter FlowTriggerModelTests`。

- [ ] **Step 5: 生成迁移**（本波**唯一**一次）：

```bash
dotnet ef migrations add WfsFlowTrigger --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context
dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context   # 应 clean
```

检查 Up() 仅建 `Wf_FlowTrigger` + `Wf_TriggerFire` 两表 + 4 索引（`UX_Wf_TriggerFire_Idem` 带 `unique: true` 无 filter），零其他表改动、零回填。**不手写迁移文件**（快照会失同步）。

- [ ] **Step 6: Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf   # 既有照绿
git add -A && git commit -m "feat(wfs-trigger): A-T1 数据模型 Wf_FlowTrigger/Wf_TriggerFire 两表+WfTriggerType+一次迁移 WfsFlowTrigger"
```

---

### Task A-T2: WfTriggerConfig 分型解析 + `IFlowTriggerService.FireAsync`（幂等闸+占坑复用+运行时双检+SubmitAsync 接缝）+ DI

> **D2 落点，本波最关键的正确性任务。** FireAsync 是三入口唯一出口；撞键语义见「共享契约」末条。

**Files:**
- Create: `CP6.Core/Services/Wf/WfTriggerConfig.cs`
- Create: `CP6.Core/Services/Wf/FlowTriggerService.cs`（`IFlowTriggerService` + `TriggerFireResult` + 实现；ScanTimersOnceAsync 本任务先抛 `NotImplementedException`——B-T2 实现，接口先立全）
- Modify: `CP6.WebApi/Program.cs`（DI：`AddScoped<IFlowTriggerService, FlowTriggerService>()`，放 `:107-108` FlowEngine 注册同块）
- Test: `CP6.Tests/Wf/FlowTriggerTestHarness.cs`（共享基座，本波所有 SQLite 测试复用）、`CP6.Tests/Wf/FlowTriggerConfigTests.cs`、`CP6.Tests/Wf/FlowTriggerFireTests.cs`

- [ ] **Step 1: 写失败测试（config 解析，纯逻辑）**

```csharp
// CP6.Tests/Wf/FlowTriggerConfigTests.cs
using CP6.Core.Services.Wf;
using Xunit;

public class FlowTriggerConfigTests
{
    [Fact]
    public void ParseTimer_ReadsCronAndVars()
    {
        var c = WfTriggerConfig.ParseTimer("{\"cron\":\"0 0 25 * *\",\"varsJson\":\"{\\\"a\\\":1}\"}");
        Assert.Equal("0 0 25 * *", c.Cron);
        Assert.Equal("{\"a\":1}", c.VarsJson);
    }

    [Fact]
    public void ParseEvent_ReadsVarsMap()
    {
        var c = WfTriggerConfig.ParseEvent("{\"varsMap\":{\"orderNo\":\"$.OutboundNo\"}}");
        Assert.Equal("$.OutboundNo", c.VarsMap!["orderNo"]);
    }

    [Fact]
    public void ParseMessage_ReadsVarsSchema()
    {
        var c = WfTriggerConfig.ParseMessage("{\"varsSchema\":[\"orderNo\",\"amount\"]}");
        Assert.Equal(new[] { "orderNo", "amount" }, c.VarsSchema);
    }

    [Fact]
    public void Parse_EmptyOrBadJson_YieldsEmptyConfig()
    {
        Assert.Null(WfTriggerConfig.ParseTimer("{}").Cron == "" ? null : "x"); // Cron 默认空串
        Assert.Null(WfTriggerConfig.ParseEvent("not-json").VarsMap);
        Assert.Null(WfTriggerConfig.ParseMessage("").VarsSchema);
    }
}
```

- [ ] **Step 2: 建共享测试基座 + 写失败测试（FireAsync 行为，SQLite + 真 FlowEngine）**

先建共享基座（照 `FlowConcurrencyTests.cs` 逐字模式，本波 A-T2/B-T2/C-T1/D-T2/E-T1/F-T1 全部测试复用）：

```csharp
// CP6.Tests/Wf/FlowTriggerTestHarness.cs —— 共享基座：GenerateCreateScript + TEXT 替换建库 +
// AFTER UPDATE 触发器模拟 rowversion（本波额外给 Wf_FlowTrigger 建同款触发器，B-T2 双 worker 抢占用）
using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

internal static class FlowTriggerTestHarness
{
    /// <summary>测试专用子类：声明两表带 rowversion 触发器（EF Core 8 SQLite 关 RETURNING 改 SELECT 读回，
    /// 令 [Timestamp] 并发令牌在 SQLite 基座真正生效——照 FlowConcurrencyTests 口径）。</summary>
    internal sealed class SqliteCP6Context : CP6Context
    {
        public SqliteCP6Context(DbContextOptions<CP6Context> o) : base(o) { }
        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);
            mb.Entity<Wf_FlowInstance>().ToTable(t => t.HasTrigger("trg_Wf_FlowInstance_RowVersion"));
            mb.Entity<Wf_FlowTrigger>().ToTable(t => t.HasTrigger("trg_Wf_FlowTrigger_RowVersion"));
        }
    }

    public static SqliteCP6Context Ctx(SqliteConnection c)
        => new(new DbContextOptionsBuilder<CP6Context>().UseSqlite(c).Options);

    public static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    public static FlowTriggerService Service(CP6Context db) => new(db, Engine(db));

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
            "CREATE TRIGGER trg_Wf_FlowInstance_RowVersion AFTER UPDATE ON \"Wf_FlowInstance\" " +
            "BEGIN UPDATE \"Wf_FlowInstance\" SET \"RowVersion\" = randomblob(8) WHERE \"Id\" = NEW.\"Id\"; END;");
        Exec(conn,
            "CREATE TRIGGER trg_Wf_FlowTrigger_RowVersion AFTER UPDATE ON \"Wf_FlowTrigger\" " +
            "BEGIN UPDATE \"Wf_FlowTrigger\" SET \"RowVersion\" = randomblob(8) WHERE \"Id\" = NEW.\"Id\"; END;");
        return conn;
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>最小 schema：start → approval(指定人) → end（形状照 FlowConcurrencyTests.ForkSchema）。</summary>
    public static string MinimalSchemaJson(Guid approver) => JsonSerializer.Serialize(new FlowSchema
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges = { new FlowEdge { From = "s", To = "a" }, new FlowEdge { From = "a", To = "end" } },
    });

    /// <summary>seed：一个流程定义（默认 enabled，flowKey 默认 "fk-trig"）+ 发起人 + 审批人。</summary>
    public static async Task<(Guid StarterId, Guid ApproverId)> SeedFlowAndUsersAsync(
        SqliteConnection conn, string flowKey = "fk-trig", bool flowEnabled = true, bool starterEnabled = true)
    {
        var starter = Guid.NewGuid();
        var approver = Guid.NewGuid();
        using var db = Ctx(conn);
        db.Sys_Users.AddRange(
            new Sys_User { Id = starter, UserName = $"st{starter:N}", Password = "x", RoleId = 1, Enable = starterEnabled },
            new Sys_User { Id = approver, UserName = $"ap{approver:N}", Password = "x", RoleId = 1, Enable = true });
        db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = flowKey, FormKey = "f",
            SchemaJson = MinimalSchemaJson(approver), Version = 1, Enable = flowEnabled,
        });
        await db.SaveChangesAsync();
        return (starter, approver);
    }

    public static Wf_FlowTrigger NewTrigger(string flowKey, int type, Guid starterId,
        bool enabled = true, string configJson = "{}", string? eventKey = null)
        => new()
        {
            FlowKey = flowKey, TriggerType = type, StarterUserId = starterId,
            Enabled = enabled, ConfigJson = configJson, EventKey = eventKey,
        };
}
```

再写 FireAsync 行为测试（全代码）：

```csharp
// CP6.Tests/Wf/FlowTriggerFireTests.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static CP6.Tests.FlowTriggerTestHarness;

namespace CP6.Tests;

public class FlowTriggerFireTests
{
    [Fact]
    public async Task Fire_Success_CreatesInstance_WritesFire_UpdatesLastFired()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var trig = NewTrigger("fk-trig", WfTriggerType.Message, starter);
        db.Wf_FlowTriggers.Add(trig);
        await db.SaveChangesAsync();

        var r = await Service(db).FireAsync(trig, "{}", WfTriggerType.Message, "k1", CancellationToken.None);

        Assert.True(r.Success);
        Assert.False(r.Replayed);
        Assert.NotNull(r.InstanceId);
        Assert.Equal(1, await db.Wf_FlowInstances.CountAsync());
        var fire = await db.Wf_TriggerFires.AsNoTracking().SingleAsync();
        Assert.Equal(r.InstanceId, fire.InstanceId);
        Assert.Null(fire.Error);
        Assert.Equal(WfTriggerType.Message, fire.Source);
        Assert.Equal("k1", fire.IdempotencyKey);
        Assert.NotNull((await db.Wf_FlowTriggers.AsNoTracking().SingleAsync()).LastFiredUtc);
    }

    [Fact]
    public async Task Fire_SameKey_Replays_ExistingInstance_NoSecondInstance()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var trig = NewTrigger("fk-trig", WfTriggerType.Message, starter);
        db.Wf_FlowTriggers.Add(trig);
        await db.SaveChangesAsync();
        var svc = Service(db);

        var r1 = await svc.FireAsync(trig, "{}", WfTriggerType.Message, "k1", CancellationToken.None);
        var r2 = await svc.FireAsync(trig, "{}", WfTriggerType.Message, "k1", CancellationToken.None);

        Assert.True(r2.Success);
        Assert.True(r2.Replayed);                        // 幂等成功不是错误（spec §3.1/§8）
        Assert.Equal(r1.InstanceId, r2.InstanceId);
        Assert.Equal(1, await db.Wf_FlowInstances.CountAsync());
        Assert.Equal(1, await db.Wf_TriggerFires.CountAsync());
    }

    [Fact]
    public async Task Fire_Disabled_Rejected_NoFireRow()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var trig = NewTrigger("fk-trig", WfTriggerType.Message, starter, enabled: false);
        db.Wf_FlowTriggers.Add(trig);
        await db.SaveChangesAsync();

        var r = await Service(db).FireAsync(trig, "{}", WfTriggerType.Message, "k1", CancellationToken.None);

        Assert.False(r.Success);
        Assert.Equal(0, await db.Wf_TriggerFires.CountAsync());   // Enabled 检查先于幂等闸（spec §3.1 顺序）
        Assert.Equal(0, await db.Wf_FlowInstances.CountAsync());
    }

    [Fact]
    public async Task Fire_StarterDisabled_EWF022_ErrorBackfilled()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn, starterEnabled: false);
        using var db = Ctx(conn);
        var trig = NewTrigger("fk-trig", WfTriggerType.Message, starter);
        db.Wf_FlowTriggers.Add(trig);
        await db.SaveChangesAsync();

        var r = await Service(db).FireAsync(trig, "{}", WfTriggerType.Message, "k1", CancellationToken.None);

        Assert.False(r.Success);
        Assert.Contains("E-WF-022", r.Error);
        var fire = await db.Wf_TriggerFires.AsNoTracking().SingleAsync();
        Assert.Contains("E-WF-022", fire.Error);          // 流水行保留供排障
        Assert.Null(fire.InstanceId);
        Assert.Equal(0, await db.Wf_FlowInstances.CountAsync());
    }

    [Fact]
    public async Task Fire_FlowDisabled_EWF023_ErrorBackfilled()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn, flowEnabled: false);
        using var db = Ctx(conn);
        var trig = NewTrigger("fk-trig", WfTriggerType.Message, starter);
        db.Wf_FlowTriggers.Add(trig);
        await db.SaveChangesAsync();

        var r = await Service(db).FireAsync(trig, "{}", WfTriggerType.Message, "k1", CancellationToken.None);

        Assert.False(r.Success);
        Assert.Contains("E-WF-023", r.Error);
        Assert.Contains("E-WF-023", (await db.Wf_TriggerFires.AsNoTracking().SingleAsync()).Error);
    }

    [Fact]
    public async Task Fire_SubmitThrows_EWF024_ErrorBackfilled_RowKept()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);   // 先 seed 合法用户
        using (var seed = Ctx(conn))
        {
            // 空 schema（无节点）的 enabled 流程 → SubmitAsync 抛"无节点" → E-WF-024 包装
            seed.Wf_FlowDefs.Add(new Wf_FlowDef
            {
                Id = Guid.NewGuid(), FlowKey = "fk-bad", FlowName = "fk-bad", FormKey = "f",
                SchemaJson = "{\"Start\":null,\"Nodes\":[],\"Edges\":[]}", Version = 1, Enable = true,
            });
            await seed.SaveChangesAsync();
        }
        using var db = Ctx(conn);
        var trig = NewTrigger("fk-bad", WfTriggerType.Message, starter);
        db.Wf_FlowTriggers.Add(trig);
        await db.SaveChangesAsync();

        var r = await Service(db).FireAsync(trig, "{}", WfTriggerType.Message, "k1", CancellationToken.None);

        Assert.False(r.Success);
        Assert.Contains("E-WF-024", r.Error);
        var fire = await db.Wf_TriggerFires.AsNoTracking().SingleAsync();
        Assert.Contains("E-WF-024", fire.Error);          // 流水行保留 Error 回填（spec §3.1）
        Assert.Null(fire.InstanceId);
        Assert.Equal(0, await db.Wf_FlowInstances.CountAsync());
    }

    [Fact]
    public async Task Fire_ResumesUnfinishedSlot_BackfillsInstance()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var trig = NewTrigger("fk-trig", WfTriggerType.Timer, starter);
        db.Wf_FlowTriggers.Add(trig);
        await db.SaveChangesAsync();
        // 预插占坑行（模拟第一段已提交、第二段未跑）
        db.Wf_TriggerFires.Add(new Wf_TriggerFire
        {
            TriggerId = trig.Id, IdempotencyKey = "slot-1",
            FiredUtc = DateTime.UtcNow.AddMinutes(-5), Source = WfTriggerType.Timer,
        });
        await db.SaveChangesAsync();

        var r = await Service(db).FireAsync(trig, "{}", WfTriggerType.Timer, "slot-1", CancellationToken.None);

        Assert.True(r.Success);
        var fire = await db.Wf_TriggerFires.AsNoTracking().SingleAsync();   // 复用该行，不新增
        Assert.Equal(r.InstanceId, fire.InstanceId);
        Assert.Equal(1, await db.Wf_FlowInstances.CountAsync());
    }

    [Fact]
    public async Task Fire_RetriesFailedSlot_ClearsError()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn, flowEnabled: false);
        using var db = Ctx(conn);
        var trig = NewTrigger("fk-trig", WfTriggerType.Event, starter);
        db.Wf_FlowTriggers.Add(trig);
        await db.SaveChangesAsync();
        var svc = Service(db);

        // 第一发：流程停用 → E-WF-023 失败流水
        var r1 = await svc.FireAsync(trig, "{}", WfTriggerType.Event, "ev-1:k", CancellationToken.None);
        Assert.False(r1.Success);

        // 启用流程 → 同 key 重发（event outbox 重放 / message 客户端重试语义，映射表⑦）
        using (var fix = Ctx(conn))
        {
            (await fix.Wf_FlowDefs.SingleAsync(d => d.FlowKey == "fk-trig")).Enable = true;
            await fix.SaveChangesAsync();
        }
        var r2 = await svc.FireAsync(trig, "{}", WfTriggerType.Event, "ev-1:k", CancellationToken.None);

        Assert.True(r2.Success);
        var fire = await db.Wf_TriggerFires.AsNoTracking().SingleAsync();   // 同一行：Error 清空、InstanceId 回填
        Assert.Null(fire.Error);
        Assert.Equal(r2.InstanceId, fire.InstanceId);
        Assert.Equal(1, await db.Wf_FlowInstances.CountAsync());
    }

    [Fact]
    public async Task Fire_PayloadHash_SetForNonTimer()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var msgTrig = NewTrigger("fk-trig", WfTriggerType.Message, starter);
        var timerTrig = NewTrigger("fk-trig", WfTriggerType.Timer, starter);
        db.Wf_FlowTriggers.AddRange(msgTrig, timerTrig);
        await db.SaveChangesAsync();
        var svc = Service(db);

        await svc.FireAsync(msgTrig, "{\"a\":1}", WfTriggerType.Message, "km", CancellationToken.None);
        await svc.FireAsync(timerTrig, "{}", WfTriggerType.Timer, "kt", CancellationToken.None);

        var msgFire = await db.Wf_TriggerFires.AsNoTracking().SingleAsync(f => f.TriggerId == msgTrig.Id);
        var timerFire = await db.Wf_TriggerFires.AsNoTracking().SingleAsync(f => f.TriggerId == timerTrig.Id);
        Assert.NotNull(msgFire.PayloadHash);
        Assert.Equal(64, msgFire.PayloadHash!.Length);     // SHA-256 hex
        Assert.Null(timerFire.PayloadHash);                // timer 无负载哈希（spec §2.2）
    }
}
```

- [ ] **Step 3: 跑验证 FAIL** — `--filter "FlowTriggerConfigTests|FlowTriggerFireTests"`（编译失败）。

- [ ] **Step 4: 实现 WfTriggerConfig**

```csharp
// CP6.Core/Services/Wf/WfTriggerConfig.cs
using System.Text.Json;

namespace CP6.Core.Services.Wf;

public class WfTimerTriggerConfig { public string Cron { get; set; } = ""; public string? VarsJson { get; set; } }
public class WfEventTriggerConfig { public Dictionary<string, string>? VarsMap { get; set; } }
public class WfMessageTriggerConfig { public List<string>? VarsSchema { get; set; } }

/// <summary>ConfigJson 分型解析（spec §2.3）。坏 JSON → 空配置（校验在 FlowTriggerValidator，解析不抛）。</summary>
public static class WfTriggerConfig
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public static WfTimerTriggerConfig ParseTimer(string? json) => Parse<WfTimerTriggerConfig>(json) ?? new();
    public static WfEventTriggerConfig ParseEvent(string? json) => Parse<WfEventTriggerConfig>(json) ?? new();
    public static WfMessageTriggerConfig ParseMessage(string? json) => Parse<WfMessageTriggerConfig>(json) ?? new();

    private static T? Parse<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Opts); }
        catch (JsonException) { return null; }
    }
}
```

- [ ] **Step 5: 实现 FlowTriggerService（FireAsync 部分）**

```csharp
// CP6.Core/Services/Wf/FlowTriggerService.cs
using System.Security.Cryptography;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public class TriggerFireResult
{
    public bool Success { get; init; }
    /// <summary>幂等撞键命中既有成功流水（HTTP 层据此回 200 而非 201）</summary>
    public bool Replayed { get; init; }
    public Guid? InstanceId { get; init; }
    public string? Error { get; init; }
    public static TriggerFireResult Ok(Guid instanceId, bool replayed = false)
        => new() { Success = true, InstanceId = instanceId, Replayed = replayed };
    public static TriggerFireResult Fail(string error) => new() { Success = false, Error = error };
}

public interface IFlowTriggerService
{
    /// <summary>统一发起（D2，spec §3.1）：Enabled 检查 → 幂等闸（撞键幂等返回既有 InstanceId 不报错）
    /// → 运行时双检 E-WF-022/023 → 变量构造由调用方完成 → SubmitAsync(trigger.StarterUserId) → 写流水 → 更新水位。</summary>
    Task<TriggerFireResult> FireAsync(Wf_FlowTrigger trigger, string? varsJson,
                                      int source, string idempotencyKey, CancellationToken ct);

    /// <summary>timer 扫描一轮（worker 复用；lease 语义 = RowVersion 乐观并发 + NextDueUtc 前移即抢占）。</summary>
    Task<int> ScanTimersOnceAsync(CancellationToken ct);
}

public class FlowTriggerService : IFlowTriggerService
{
    /// <summary>占坑补跑宽限：FiredUtc 早于此宽限仍未回填的占坑行才补跑（避免与正在进行的第二段抢跑）</summary>
    public static readonly TimeSpan RecoveryGrace = TimeSpan.FromMinutes(2);
    private const int BatchSize = 50;

    private readonly CP6Context _db;
    private readonly IFlowEngine _engine;

    public FlowTriggerService(CP6Context db, IFlowEngine engine)
    {
        _db = db;
        _engine = engine;
    }

    public async Task<TriggerFireResult> FireAsync(Wf_FlowTrigger trigger, string? varsJson,
                                                   int source, string idempotencyKey, CancellationToken ct)
    {
        // ① Enabled 检查（spec §3.1 顺序：先于幂等闸）
        if (!trigger.Enabled) return TriggerFireResult.Fail("触发器已停用");

        // ② 幂等闸：先查既有流水（Local + 库，防同 context 二次调用漏变更追踪器）
        var fire = _db.Wf_TriggerFires.Local
                       .FirstOrDefault(f => f.TriggerId == trigger.Id && f.IdempotencyKey == idempotencyKey)
                   ?? await _db.Wf_TriggerFires
                       .FirstOrDefaultAsync(f => f.TriggerId == trigger.Id && f.IdempotencyKey == idempotencyKey, ct);
        if (fire == null)
        {
            fire = new Wf_TriggerFire
            {
                TriggerId = trigger.Id,
                IdempotencyKey = idempotencyKey,
                FiredUtc = DateTime.UtcNow,
                Source = source,
                PayloadHash = source == WfTriggerType.Timer ? null : HashOrNull(varsJson),
            };
            _db.Wf_TriggerFires.Add(fire);
            try { await _db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                // 并发撞唯一索引：让位既有行（另一实例先占坑），转入撞键分支
                _db.Entry(fire).State = EntityState.Detached;
                fire = await _db.Wf_TriggerFires
                    .FirstAsync(f => f.TriggerId == trigger.Id && f.IdempotencyKey == idempotencyKey, ct);
            }
        }
        if (fire.InstanceId != null)
            return TriggerFireResult.Ok(fire.InstanceId.Value, replayed: true);   // 幂等成功不是错误（spec §3.1）
        // InstanceId==null（占坑未完成或上次失败）→ 补跑第二段（共享契约末条语义）

        // ③ 运行时双检（spec §5：发起人/流程可能在保存后被停用）
        var starterOk = await _db.Sys_Users.AnyAsync(u => u.Id == trigger.StarterUserId && u.Enable, ct);
        if (!starterOk) return await FailFireAsync(fire, "E-WF-022: 发起人不存在或已停用", ct);
        var flowOk = await _db.Wf_FlowDefs.AnyAsync(d => d.FlowKey == trigger.FlowKey && d.Enable, ct);
        if (!flowOk) return await FailFireAsync(fire, "E-WF-023: 目标流程不存在或未启用", ct);

        // ④ 第二段：SubmitAsync + 流水回填 + 水位 同一显式事务（映射表⑥，引擎原子接缝）
        //    trigger 可能被上游 ChangeTracker.Clear 失联 → 用库内跟踪实例回写水位
        var trackedTrigger = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(t => t.Id == trigger.Id, ct) ?? trigger;
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            var instanceId = await _engine.SubmitAsync(trackedTrigger.FlowKey, trackedTrigger.StarterUserId, varsJson ?? "{}");
            fire.InstanceId = instanceId;
            fire.Error = null;                              // 失败重试成功 → 清错
            trackedTrigger.LastFiredUtc = DateTime.UtcNow;  // 水位
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return TriggerFireResult.Ok(instanceId);
        }
        catch (Exception ex)
        {
            // SubmitAsync 半途实体已随事务回滚，但仍挂在变更追踪器上 → 清追踪器后重查流水行回填 Error
            _db.ChangeTracker.Clear();
            var fresh = await _db.Wf_TriggerFires.FirstAsync(f => f.Id == fire.Id, ct);
            fresh.Error = Trunc($"E-WF-024: {ex.Message}");
            await _db.SaveChangesAsync(ct);
            return TriggerFireResult.Fail(fresh.Error);
        }
    }

    public Task<int> ScanTimersOnceAsync(CancellationToken ct)
        => ScanTimersOnceAsync(DateTime.UtcNow, ct);

    /// <summary>测试重载（注入 nowUtc，映射表⑤）——B-T2 实现。</summary>
    public Task<int> ScanTimersOnceAsync(DateTime nowUtc, CancellationToken ct)
        => throw new NotImplementedException("B-T2");

    private async Task<TriggerFireResult> FailFireAsync(Wf_TriggerFire fire, string error, CancellationToken ct)
    {
        fire.Error = Trunc(error);
        await _db.SaveChangesAsync(ct);
        return TriggerFireResult.Fail(error);
    }

    private static string? HashOrNull(string? s)
        => s == null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    private static string Trunc(string s) => s.Length <= 1000 ? s : s[..1000];
}
```

> **调用方契约（注释进代码）**：FireAsync 失败路径会 `ChangeTracker.Clear()`——调用方在一次 FireAsync 失败后**不得复用先前批量加载的跟踪实体**（B-T2 扫描循环、C-T1 hook 循环均按 Id 逐条重查，见各任务）。

- [ ] **Step 6: DI** — `Program.cs` FlowEngine 注册块（`:107-108`）之后追加：

```csharp
builder.Services.AddScoped<CP6.Core.Services.Wf.IFlowTriggerService, CP6.Core.Services.Wf.FlowTriggerService>(); // 事件触发 start：三入口单一出口（D2）
```

- [ ] **Step 7: 跑验证 PASS + Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FlowTriggerConfigTests|FlowTriggerFireTests"
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-trigger): A-T2 IFlowTriggerService.FireAsync 幂等闸+占坑复用+E-WF-022/023/024 运行时双检+SubmitAsync 原子接缝+DI"
```

---

## Wave T-B — timer（与 T-C、T-D 可并行，均只依赖 T-A）

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

### Task B-T3: WfTriggerWorker（BackgroundService）+ DI

**Files:**
- Create: `CP6.WebApi/BackgroundServices/WfTriggerWorker.cs`
- Modify: `CP6.WebApi/Program.cs`（`AddHostedService`，放 WfServiceJobScanWorker 注册同块）

- [ ] **Step 1: 实现** — 照 `WfServiceJobScanWorker.cs` 逐字克隆（骨架 + TenantScopeRunner 租户切换现状写法，spec §6），差异仅：无 workerId（抢占靠 RowVersion+NextDueUtc 前移，无 lease）、间隔 30s、日志文案：

```csharp
// CP6.WebApi/BackgroundServices/WfTriggerWorker.cs
using CP6.Core.Services.Wf;

namespace CP6.WebApi.BackgroundServices;

/// <summary>流程触发器 timer 扫描（spec §3.2）。逐租户 scope 切换照 TenantScopeRunner 现状口径（spec §6）；
/// 多实例安全：抢占 = Wf_FlowTrigger.RowVersion 乐观并发 + NextDueUtc 前移 + 占坑唯一键，无需 lease。</summary>
public class WfTriggerWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);   // cron 最小粒度 1min，30s 扫描足够
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WfTriggerWorker> _logger;

    public WfTriggerWorker(IServiceScopeFactory scopeFactory, ILogger<WfTriggerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Wf 触发器扫描 Worker 启动");
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TenantScopeRunner.ForEachTenantAsync(_scopeFactory, async (sp, tenantId, ct) =>
                    {
                        var svc = sp.GetRequiredService<IFlowTriggerService>();
                        var n = await svc.ScanTimersOnceAsync(ct);
                        if (n > 0) _logger.LogInformation("Wf 触发器扫描处理租户 {Tenant} {Count} 条", tenantId, n);
                    }, _logger, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { _logger.LogError(ex, "Wf 触发器扫描异常"); }

                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { _logger.LogInformation("Wf 触发器扫描 Worker 停止"); }
    }
}
```

- [ ] **Step 2: DI** — `Program.cs` WfServiceJobScanWorker 注册同块追加：

```csharp
builder.Services.AddHostedService<CP6.WebApi.BackgroundServices.WfTriggerWorker>();   // 事件触发 start：timer 扫描
```

- [ ] **Step 3: 编译 + Wf 闸 + commit**

```bash
dotnet build CP6.WebApi/CP6.WebApi.csproj
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-trigger): B-T3 WfTriggerWorker 逐租户扫描(照 TenantScopeRunner 现状口径)+DI"
```

---

## Wave T-C — event（BridgeHook 家族新成员，与 T-B、T-D 并行）

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

## Wave T-D — message（REST + API key，与 T-B、T-C 并行）

### Task D-T1: WfApiKeyHelper（生成/哈希/常量时间校验）

**Files:**
- Create: `CP6.Core/Services/Wf/WfApiKeyHelper.cs`
- Test: `CP6.Tests/Wf/WfApiKeyHelperTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/WfApiKeyHelperTests.cs
using CP6.Core.Services.Wf;
using Xunit;

public class WfApiKeyHelperTests
{
    [Fact]
    public void NewRawKey_Is32ByteHighEntropy_Base64Url()
    {
        var a = WfApiKeyHelper.NewRawKey();
        var b = WfApiKeyHelper.NewRawKey();
        Assert.NotEqual(a, b);
        Assert.True(a.Length >= 43);                       // 32 字节 base64url ≈ 43 字符
        Assert.DoesNotContain("+", a); Assert.DoesNotContain("/", a); Assert.DoesNotContain("=", a);
    }

    [Fact]
    public void HashOf_Sha256Hex_64Chars_Deterministic()
    {
        var h1 = WfApiKeyHelper.HashOf("k");
        var h2 = WfApiKeyHelper.HashOf("k");
        Assert.Equal(h1, h2);
        Assert.Equal(64, h1.Length);
        Assert.NotEqual("k", h1);
    }

    [Fact]
    public void Verify_RoundTrip_True_WrongKey_False()
    {
        var raw = WfApiKeyHelper.NewRawKey();
        var hash = WfApiKeyHelper.HashOf(raw);
        Assert.True(WfApiKeyHelper.Verify(raw, hash));
        Assert.False(WfApiKeyHelper.Verify(raw + "x", hash));
        Assert.False(WfApiKeyHelper.Verify("", hash));
    }

    [Fact]
    public void Verify_NullOrEmptyStoredHash_False()
    {
        Assert.False(WfApiKeyHelper.Verify("any", null));
        Assert.False(WfApiKeyHelper.Verify("any", ""));
    }
}
```

- [ ] **Step 2: 跑验证 FAIL**（`--filter WfApiKeyHelperTests`）。

- [ ] **Step 3: 实现**（复刻 `RefreshTokenService.cs:31-33` 生成/哈希 + `TwoFactorService.cs:137-149` 常量时间比较先例）：

```csharp
// CP6.Core/Services/Wf/WfApiKeyHelper.cs
using System.Security.Cryptography;
using System.Text;

namespace CP6.Core.Services.Wf;

/// <summary>message 触发器 API key 基建（spec §3.4）：32 字节高熵随机，明文只在创建/重置响应显示一次，
/// 库内仅存 SHA-256 hex（泄库不可还原）；校验常量时间比较。复刻 RefreshTokenService/TwoFactorService 先例。</summary>
public static class WfApiKeyHelper
{
    public static string NewRawKey() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string HashOf(string raw)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    public static bool Verify(string raw, string? storedHash)
    {
        if (string.IsNullOrEmpty(raw) || string.IsNullOrEmpty(storedHash)) return false;
        var candidate = HashOf(raw);
        var stored = storedHash.ToUpperInvariant();            // ToHexString 恒大写，防御性归一
        if (candidate.Length != stored.Length) return false;   // FixedTimeEquals 要求等长
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(candidate), Encoding.ASCII.GetBytes(stored));
    }

    private static string Base64Url(byte[] b)
        => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
```

- [ ] **Step 4: 跑验证 PASS + Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter WfApiKeyHelperTests
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-trigger): D-T1 WfApiKeyHelper 32字节随机+SHA-256入库+常量时间校验"
```

---

### Task D-T2: message 外呼端点（`[AllowAnonymous]` + 自定义过滤器 + 白名单 + 64KB + 幂等头）

> **spec §3.4 全文落点。** 过滤器仿 `RequirePlatformAdminAttribute`（`IAsyncAuthorizationFilter` + `RequestServices` 服务定位 + `context.Result` 短路）；跨租户定位仿 `RefreshTokenService` 的 `IgnoreQueryFilters`（key 绑定单触发器单租户）；验过 key 后**切租户上下文**（对齐 TenantScopeRunner 的 `ITenantContext.CurrentTenantId` setter 口径）。

**Files:**
- Create: `CP6.Core/Auth/WfTriggerApiKeyAttribute.cs`
- Create: `CP6.WebApi/Controllers/Oa/FlowTriggerFireController.cs`
- Test: `CP6.Tests/Wf/WfTriggerMessageEndpointTests.cs`

- [ ] **Step 1: 写失败测试**（过滤器与控制器直接构造调用，不起 Host：`DefaultHttpContext` + `RequestServices` = 手搭 `ServiceCollection`{CP6Context(SQLite harness)、ITenantContext=TenantContext}；`AuthorizationFilterContext` 带 `RouteData{ id }`。若 `CP6.Tests.csproj` 尚未引用 `CP6.WebApi`，加 ProjectReference——控制器类型需要）

```csharp
// CP6.Tests/Wf/WfTriggerMessageEndpointTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.WebApi.Controllers.Oa;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static CP6.Tests.FlowTriggerTestHarness;

namespace CP6.Tests;

public class WfTriggerMessageEndpointTests
{
    // ── 脚手架 ──

    private static ServiceProvider NewSp(SqliteConnection conn)
    {
        var services = new ServiceCollection();
        services.AddScoped<CP6Context>(_ => Ctx(conn));
        services.AddSingleton<ITenantContext, TenantContext>();
        return services.BuildServiceProvider();
    }

    private static async Task<(Guid TriggerId, string RawKey, Guid TenantId)> SeedMessageTriggerAsync(
        SqliteConnection conn, bool enabled = true, int type = WfTriggerType.Message,
        string configJson = "{\"varsSchema\":[\"orderNo\"]}")
    {
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var raw = WfApiKeyHelper.NewRawKey();
        var trig = NewTrigger("fk-trig", type, starter, enabled, configJson);
        trig.ApiKeyHash = WfApiKeyHelper.HashOf(raw);
        db.Wf_FlowTriggers.Add(trig);
        await db.SaveChangesAsync();
        return (trig.Id, raw, trig.TenantId);
    }

    private static async Task<(AuthorizationFilterContext Ctx, DefaultHttpContext Http)> RunFilterAsync(
        IServiceProvider sp, Guid id, string? apiKey, string? idemKey)
    {
        var http = new DefaultHttpContext { RequestServices = sp };
        if (apiKey != null) http.Request.Headers["X-Api-Key"] = apiKey;
        if (idemKey != null) http.Request.Headers["Idempotency-Key"] = idemKey;
        var routeData = new RouteData();
        routeData.Values["id"] = id.ToString();
        var actx = new AuthorizationFilterContext(
            new ActionContext(http, routeData, new ActionDescriptor()),
            new List<IFilterMetadata>());
        await new WfTriggerApiKeyAttribute().OnAuthorizationAsync(actx);
        return (actx, http);
    }

    /// <summary>控制器直接构造：过滤器已放行的前提（trigger 塞 Items、幂等头就位、body 就位）。</summary>
    private static FlowTriggerFireController NewController(
        SqliteConnection conn, CP6Context db, Wf_FlowTrigger trigger, string idemKey, string body)
    {
        var http = new DefaultHttpContext { RequestServices = NewSp(conn) };
        http.Items[WfTriggerApiKeyAttribute.ItemKey] = trigger;
        http.Request.Headers["Idempotency-Key"] = idemKey;
        var bytes = Encoding.UTF8.GetBytes(body);
        http.Request.Body = new MemoryStream(bytes);
        http.Request.ContentLength = bytes.Length;
        return new FlowTriggerFireController(Service(db))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private static string ResultJson(IActionResult r) => JsonSerializer.Serialize(((ObjectResult)r).Value);

    // ── 过滤器 ──

    [Fact]
    public async Task Filter_UnknownId_404()
    {
        using var conn = NewSqliteWithSchema();
        var (actx, _) = await RunFilterAsync(NewSp(conn), Guid.NewGuid(), "any", "ik-1");
        var nf = Assert.IsType<NotFoundObjectResult>(actx.Result);
        Assert.Contains("404", ResultJson(nf));
    }

    [Fact]
    public async Task Filter_DisabledTrigger_404_SameShapeAsUnknown()
    {
        using var conn = NewSqliteWithSchema();
        var (id, raw, _) = await SeedMessageTriggerAsync(conn, enabled: false);
        var sp = NewSp(conn);

        var (disabledCase, _) = await RunFilterAsync(sp, id, raw, "ik-1");
        var (unknownCase, _) = await RunFilterAsync(sp, Guid.NewGuid(), raw, "ik-1");

        var a = Assert.IsType<NotFoundObjectResult>(disabledCase.Result);
        var b = Assert.IsType<NotFoundObjectResult>(unknownCase.Result);
        Assert.Equal(ResultJson(b), ResultJson(a));        // 停用与不存在响应体逐字段相同（spec §3.4）
    }

    [Fact]
    public async Task Filter_WrongKey_401()
    {
        using var conn = NewSqliteWithSchema();
        var (id, _, _) = await SeedMessageTriggerAsync(conn);
        var (actx, _) = await RunFilterAsync(NewSp(conn), id, "wrong-key", "ik-1");
        var obj = Assert.IsType<ObjectResult>(actx.Result);
        Assert.Equal(401, obj.StatusCode);
    }

    [Fact]
    public async Task Filter_MissingIdempotencyKey_400()
    {
        using var conn = NewSqliteWithSchema();
        var (id, raw, _) = await SeedMessageTriggerAsync(conn);
        var (actx, _) = await RunFilterAsync(NewSp(conn), id, raw, idemKey: null);
        Assert.IsType<BadRequestObjectResult>(actx.Result);
    }

    [Fact]
    public async Task Filter_Valid_SetsTenant_StashesTrigger_NoResult()
    {
        using var conn = NewSqliteWithSchema();
        var (id, raw, tenantId) = await SeedMessageTriggerAsync(conn);
        var sp = NewSp(conn);

        var (actx, http) = await RunFilterAsync(sp, id, raw, "ik-1");

        Assert.Null(actx.Result);                          // 放行
        var stashed = Assert.IsType<Wf_FlowTrigger>(http.Items[WfTriggerApiKeyAttribute.ItemKey]);
        Assert.Equal(id, stashed.Id);
        Assert.Equal(tenantId, sp.GetRequiredService<ITenantContext>().CurrentTenantId);   // 租户已切
    }

    [Fact]
    public async Task Filter_NonMessageType_404()
    {
        using var conn = NewSqliteWithSchema();
        var (id, raw, _) = await SeedMessageTriggerAsync(conn, type: WfTriggerType.Timer);
        var (actx, _) = await RunFilterAsync(NewSp(conn), id, raw, "ik-1");
        Assert.IsType<NotFoundObjectResult>(actx.Result);  // 端点只服务 message 型
    }

    // ── 控制器 ──

    [Fact]
    public async Task Fire_FirstCall_201_WithInstanceId_SchemaFiltered()
    {
        using var conn = NewSqliteWithSchema();
        var (id, _, _) = await SeedMessageTriggerAsync(conn);
        using var db = Ctx(conn);
        var trig = await db.Wf_FlowTriggers.SingleAsync(t => t.Id == id);

        var c = NewController(conn, db, trig, "ik-1", "{\"orderNo\":\"PO-1\",\"evil\":\"x\"}");
        var r = await c.Fire(id, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(r);
        Assert.Equal(201, obj.StatusCode);
        Assert.Contains("instanceId", ResultJson(obj));
        var inst = await db.Wf_FlowInstances.AsNoTracking().SingleAsync();
        Assert.Contains("\"orderNo\":\"PO-1\"", inst.VarsJson);   // 白名单保留
        Assert.DoesNotContain("evil", inst.VarsJson);             // 白名单外丢弃（防变量注入）
    }

    [Fact]
    public async Task Fire_SameIdempotencyKey_200_SameInstance()
    {
        using var conn = NewSqliteWithSchema();
        var (id, _, _) = await SeedMessageTriggerAsync(conn);
        using var db = Ctx(conn);
        var trig = await db.Wf_FlowTriggers.SingleAsync(t => t.Id == id);

        var r1 = await NewController(conn, db, trig, "ik-1", "{\"orderNo\":\"PO-1\"}").Fire(id, CancellationToken.None);
        var r2 = await NewController(conn, db, trig, "ik-1", "{\"orderNo\":\"PO-1\"}").Fire(id, CancellationToken.None);

        Assert.Equal(201, ((ObjectResult)r1).StatusCode);
        var ok = Assert.IsType<OkObjectResult>(r2);                // 200 幂等重放
        Assert.Equal(ResultJson((ObjectResult)r1), ResultJson(ok));   // 同 instanceId
        Assert.Equal(1, await db.Wf_FlowInstances.CountAsync());
    }

    [Fact]
    public async Task Fire_OversizeBody_400()
    {
        using var conn = NewSqliteWithSchema();
        var (id, _, _) = await SeedMessageTriggerAsync(conn);
        using var db = Ctx(conn);
        var trig = await db.Wf_FlowTriggers.SingleAsync(t => t.Id == id);
        var big = "{\"orderNo\":\"" + new string('x', 65 * 1024) + "\"}";   // >64KB

        var r = await NewController(conn, db, trig, "ik-1", big).Fire(id, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(r);
        Assert.Equal(0, await db.Wf_FlowInstances.CountAsync());
    }

    [Fact]
    public async Task Fire_NonObjectBody_400()
    {
        using var conn = NewSqliteWithSchema();
        var (id, _, _) = await SeedMessageTriggerAsync(conn);
        using var db = Ctx(conn);
        var trig = await db.Wf_FlowTriggers.SingleAsync(t => t.Id == id);

        var r = await NewController(conn, db, trig, "ik-1", "[1,2]").Fire(id, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(r);
        Assert.Equal(0, await db.Wf_FlowInstances.CountAsync());
    }
}
```

- [ ] **Step 2: 跑验证 FAIL**（`--filter WfTriggerMessageEndpointTests`）。

- [ ] **Step 3: 实现过滤器**

```csharp
// CP6.Core/Auth/WfTriggerApiKeyAttribute.cs
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Core.Auth;

/// <summary>message 触发器外呼闸（spec §3.4）：X-Api-Key SHA-256 常量时间校验 + Idempotency-Key 必填
/// + 404 不区分「不存在/停用」。验过 key 后按触发器租户切 ITenantContext（AllowAnonymous 无 JWT 租户）。
/// 特性不能构造注入，用 RequestServices 服务定位（仿 RequirePlatformAdminAttribute）。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class WfTriggerApiKeyAttribute : Attribute, IAsyncAuthorizationFilter
{
    public const string ItemKey = "WfTrigger.Fire.Trigger";
    public const int MaxIdempotencyKeyLength = 200;   // 进唯一索引键列（映射表④）

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var db = http.RequestServices.GetService<CP6Context>();
        if (db == null)
        {
            context.Result = new ObjectResult(new { code = 500, message = "服务未注册" }) { StatusCode = 500 };
            return;
        }

        static IActionResult NotFound404() => new NotFoundObjectResult(new { code = 404, message = "trigger not found" });

        if (!Guid.TryParse(context.RouteData.Values["id"]?.ToString(), out var id))
        {
            context.Result = NotFound404();
            return;
        }

        // 跨租户按 Id 定位（key 绑定单触发器单租户，IgnoreQueryFilters 仿 RefreshTokenService 先例）
        var trigger = await db.Wf_FlowTriggers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id && t.TriggerType == WfTriggerType.Message);
        if (trigger == null || !trigger.Enabled)
        {
            context.Result = NotFound404();   // 停用与不存在不区分（spec §3.4）
            return;
        }

        var rawKey = http.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(rawKey) || !WfApiKeyHelper.Verify(rawKey, trigger.ApiKeyHash))
        {
            context.Result = new ObjectResult(new { code = 401, message = "invalid api key" }) { StatusCode = 401 };
            return;
        }

        var idemKey = http.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(idemKey) || idemKey.Length > MaxIdempotencyKeyLength)
        {
            context.Result = new BadRequestObjectResult(
                new { code = 400, message = $"Idempotency-Key header required (<= {MaxIdempotencyKeyLength} chars)" });
            return;
        }

        // 租户切换：同 scope 的 ITenantContext setter（对齐 TenantScopeRunner 现状口径，spec §6）
        http.RequestServices.GetRequiredService<ITenantContext>().CurrentTenantId = trigger.TenantId;
        http.Items[ItemKey] = trigger;
    }
}
```

- [ ] **Step 4: 实现控制器**

```csharp
// CP6.WebApi/Controllers/Oa/FlowTriggerFireController.cs
using System.Text;
using System.Text.Json;
using CP6.Core.Auth;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>message 触发器外呼端点（spec §3.4）。
/// 响应：201 新发起 {instanceId} / 200 幂等重放 {instanceId} / 400 缺幂等头·负载超限·非对象 /
/// 401 key 无效 / 404 不存在或未启用（不区分）/ 500 运行时发起失败（E-WF-022/023/024 detail）。</summary>
[ApiController]
[Route("api/oa/flow-triggers")]
public class FlowTriggerFireController : ControllerBase
{
    public const int MaxPayloadBytes = 64 * 1024;   // 64KB 上限防滥用（spec §6）

    private readonly IFlowTriggerService _triggers;

    public FlowTriggerFireController(IFlowTriggerService triggers) { _triggers = triggers; }

    [HttpPost("{id:guid}/fire")]
    [AllowAnonymous]
    [WfTriggerApiKey]
    public async Task<IActionResult> Fire(Guid id, CancellationToken ct)
    {
        var trigger = (Wf_FlowTrigger)HttpContext.Items[WfTriggerApiKeyAttribute.ItemKey]!;
        var idemKey = Request.Headers["Idempotency-Key"].First()!;

        // 64KB：Content-Length 先验 + 实读字节数兜底（chunked 无 Content-Length 时）
        if (Request.ContentLength is > MaxPayloadBytes)
            return BadRequest(new { code = 400, message = "payload too large (64KB max)" });
        string body;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            body = await reader.ReadToEndAsync(ct);
        if (Encoding.UTF8.GetByteCount(body) > MaxPayloadBytes)
            return BadRequest(new { code = 400, message = "payload too large (64KB max)" });

        // varsSchema 白名单过滤（防变量注入，spec §2.3/§6）
        string varsJson;
        try
        {
            var cfg = WfTriggerConfig.ParseMessage(trigger.ConfigJson);
            varsJson = WfTriggerVarsMapper.FilterBySchema(body, cfg.VarsSchema);
        }
        catch (JsonException)
        {
            return BadRequest(new { code = 400, message = "body must be a JSON object" });
        }

        var r = await _triggers.FireAsync(trigger, varsJson, WfTriggerType.Message, idemKey, ct);
        if (!r.Success)
            return StatusCode(500, new { code = 500, message = r.Error });
        return r.Replayed
            ? Ok(new { instanceId = r.InstanceId })                          // 200 幂等重放
            : StatusCode(201, new { instanceId = r.InstanceId });            // 201 新发起
    }
}
```

- [ ] **Step 5: 跑验证 PASS + Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter WfTriggerMessageEndpointTests
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-trigger): D-T2 message 外呼端点 AllowAnonymous+key 常量时间闸+幂等头+64KB+白名单"
```

---

## Wave T-E — 管理 UI（依赖 T-B/T-C/T-D 全部完成）

### Task E-T1: 管理后端（CRUD + 启停 + 手动试发 + 流水 + key 重置 + cron 预览）

**Files:**
- Create: `CP6.Core/Services/Wf/FlowTriggerAdminService.cs`
- Create: `CP6.WebApi/Controllers/Oa/FlowTriggerAdminController.cs`
- Modify: `CP6.WebApi/Program.cs`（DI）
- Test: `CP6.Tests/Wf/FlowTriggerAdminTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/FlowTriggerAdminTests.cs —— 服务层，基座同 A-T2
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static CP6.Tests.FlowTriggerTestHarness;

namespace CP6.Tests;

public class FlowTriggerAdminTests
{
    private static FlowTriggerAdminService Admin(CP6Context db) => new(db, Service(db));

    private static FlowTriggerSaveReq Req(int type, Guid starter, string configJson,
        string flowKey = "fk-trig", bool enabled = true, string? eventKey = null)
        => new(flowKey, type, configJson, enabled, eventKey, starter);

    [Fact]
    public async Task Create_Timer_ComputesInitialNextDue()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);

        var (id, plain) = await Admin(db).CreateAsync(
            Req(WfTriggerType.Timer, starter, "{\"cron\":\"0 9 * * *\"}"), CancellationToken.None);

        Assert.Null(plain);                                // timer 无 key
        var row = await db.Wf_FlowTriggers.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.NotNull(row.NextDueUtc);
        Assert.True(row.NextDueUtc > DateTime.UtcNow.AddMinutes(-1));   // 初始 NextDue 已上膛且非过去
    }

    [Fact]
    public async Task Create_Message_ReturnsPlainKeyOnce_StoresOnlyHash()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);

        var (id, plain) = await Admin(db).CreateAsync(
            Req(WfTriggerType.Message, starter, "{\"varsSchema\":[\"orderNo\"]}"), CancellationToken.None);

        Assert.False(string.IsNullOrEmpty(plain));
        var row = await db.Wf_FlowTriggers.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.Equal(WfApiKeyHelper.HashOf(plain!), row.ApiKeyHash);   // 库中只有哈希
        Assert.NotEqual(plain, row.ApiKeyHash);                        // 明文不落库（spec §3.4）
        Assert.DoesNotContain(plain!, row.ConfigJson);
    }

    [Fact]
    public async Task Update_Timer_CronChanged_RecomputesNextDue()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var admin = Admin(db);
        var (id, _) = await admin.CreateAsync(
            Req(WfTriggerType.Timer, starter, "{\"cron\":\"0 9 * * *\"}"), CancellationToken.None);

        // 改成「每年 1 月 1 日」→ NextDue 必落在 1 月 1 日（与每日 cron 不可能撞同一到期语义）
        await admin.UpdateAsync(id,
            Req(WfTriggerType.Timer, starter, "{\"cron\":\"0 0 1 1 *\"}"), CancellationToken.None);

        var row = await db.Wf_FlowTriggers.AsNoTracking().SingleAsync(t => t.Id == id);
        var local = TimeZoneInfo.ConvertTimeFromUtc(row.NextDueUtc!.Value, TimeZoneInfo.Local);
        Assert.Equal(1, local.Month);
        Assert.Equal(1, local.Day);
    }

    [Fact]
    public async Task Update_NeverReturnsKey_KeepsHash()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var admin = Admin(db);
        var (id, plain) = await admin.CreateAsync(
            Req(WfTriggerType.Message, starter, "{\"varsSchema\":[\"orderNo\"]}"), CancellationToken.None);
        var hashBefore = (await db.Wf_FlowTriggers.AsNoTracking().SingleAsync(t => t.Id == id)).ApiKeyHash;

        // UpdateAsync 返回 Task（编译期即保证不回明文）；改 varsSchema 不动 key
        await admin.UpdateAsync(id,
            Req(WfTriggerType.Message, starter, "{\"varsSchema\":[\"orderNo\",\"amount\"]}"), CancellationToken.None);

        var row = await db.Wf_FlowTriggers.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.Equal(hashBefore, row.ApiKeyHash);          // hash 不变，旧明文仍有效
        Assert.True(WfApiKeyHelper.Verify(plain!, row.ApiKeyHash));
    }

    [Fact]
    public async Task ResetKey_NewPlain_OldKeyInvalid()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var admin = Admin(db);
        var (id, oldPlain) = await admin.CreateAsync(
            Req(WfTriggerType.Message, starter, "{\"varsSchema\":[]}"), CancellationToken.None);

        var newPlain = await admin.ResetKeyAsync(id, CancellationToken.None);

        var row = await db.Wf_FlowTriggers.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.False(WfApiKeyHelper.Verify(oldPlain!, row.ApiKeyHash));   // 旧 key 即刻失效
        Assert.True(WfApiKeyHelper.Verify(newPlain, row.ApiKeyHash));
        Assert.NotEqual(oldPlain, newPlain);
    }

    [Fact]
    public async Task ResetKey_OnNonMessage_Throws()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var admin = Admin(db);
        var (id, _) = await admin.CreateAsync(
            Req(WfTriggerType.Timer, starter, "{\"cron\":\"0 9 * * *\"}"), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => admin.ResetKeyAsync(id, CancellationToken.None));
        Assert.Contains("E-WF-022", ex.Message);
    }

    [Fact]
    public async Task ManualFire_UsesManualKey_CreatesInstance()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var admin = Admin(db);
        var (id, _) = await admin.CreateAsync(
            Req(WfTriggerType.Timer, starter, "{\"cron\":\"0 9 * * *\"}"), CancellationToken.None);

        var r1 = await admin.ManualFireAsync(id, CancellationToken.None);
        var r2 = await admin.ManualFireAsync(id, CancellationToken.None);   // 手动键每次新 GUID → 再发一单

        Assert.True(r1.Success);
        Assert.True(r2.Success);
        Assert.NotEqual(r1.InstanceId, r2.InstanceId);
        var fires = await db.Wf_TriggerFires.AsNoTracking().ToListAsync();
        Assert.Equal(2, fires.Count);
        Assert.All(fires, f => Assert.StartsWith("manual:", f.IdempotencyKey));   // spec §4 手动试发键
        Assert.Equal(2, await db.Wf_FlowInstances.CountAsync());
    }

    [Fact]
    public async Task ListFires_ReturnsRecent_DescByFiredUtc()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var admin = Admin(db);
        var (id, _) = await admin.CreateAsync(
            Req(WfTriggerType.Timer, starter, "{\"cron\":\"0 9 * * *\"}"), CancellationToken.None);
        var baseUtc = DateTime.UtcNow;
        for (var i = 0; i < 3; i++)
            db.Wf_TriggerFires.Add(new Wf_TriggerFire
            {
                TriggerId = id, IdempotencyKey = $"k{i}",
                FiredUtc = baseUtc.AddMinutes(-i), Source = WfTriggerType.Timer,
            });
        await db.SaveChangesAsync();

        var list = await admin.ListFiresAsync(id, take: 2, CancellationToken.None);

        Assert.Equal(2, list.Count);
        Assert.Equal("k0", list[0].IdempotencyKey);        // 最新在前
        Assert.Equal("k1", list[1].IdempotencyKey);
        Assert.True(list[0].FiredUtc > list[1].FiredUtc);
    }

    [Fact]
    public async Task SetEnabled_Toggles()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var admin = Admin(db);
        var (id, _) = await admin.CreateAsync(
            Req(WfTriggerType.Timer, starter, "{\"cron\":\"0 9 * * *\"}"), CancellationToken.None);

        await admin.SetEnabledAsync(id, false, CancellationToken.None);

        Assert.False((await db.Wf_FlowTriggers.AsNoTracking().SingleAsync(t => t.Id == id)).Enabled);
    }
}
```

- [ ] **Step 2: 跑验证 FAIL**（`--filter FlowTriggerAdminTests`）。

- [ ] **Step 3: 实现服务**

```csharp
// CP6.Core/Services/Wf/FlowTriggerAdminService.cs
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public record FlowTriggerSaveReq(
    string FlowKey, int TriggerType, string ConfigJson, bool Enabled,
    string? EventKey, Guid StarterUserId);

public record FlowTriggerListItem(
    Guid Id, string FlowKey, int TriggerType, bool Enabled, string? EventKey,
    Guid StarterUserId, DateTime? NextDueUtc, DateTime? LastFiredUtc, bool HasApiKey, string ConfigJson);

public record TriggerFireListItem(
    Guid Id, string IdempotencyKey, DateTime FiredUtc, Guid? InstanceId, int Source, string? Error);

public interface IFlowTriggerAdminService
{
    Task<List<FlowTriggerListItem>> ListAsync(CancellationToken ct);
    Task<FlowTriggerListItem?> GetAsync(Guid id, CancellationToken ct);
    /// <summary>返回 (id, apiKeyPlain)。apiKeyPlain 仅 message 型创建时非空——明文只此一次（spec §3.4）。</summary>
    Task<(Guid Id, string? ApiKeyPlain)> CreateAsync(FlowTriggerSaveReq req, CancellationToken ct);
    Task UpdateAsync(Guid id, FlowTriggerSaveReq req, CancellationToken ct);
    Task SetEnabledAsync(Guid id, bool enabled, CancellationToken ct);
    /// <summary>重置 key（仅 message）：返回新明文，旧 key 即刻失效。</summary>
    Task<string> ResetKeyAsync(Guid id, CancellationToken ct);
    /// <summary>手动试发（权限同 Edit）：幂等键 = "manual:{GUID}"（spec §4）。</summary>
    Task<TriggerFireResult> ManualFireAsync(Guid id, CancellationToken ct);
    Task<List<TriggerFireListItem>> ListFiresAsync(Guid id, int take, CancellationToken ct);
}

public class FlowTriggerAdminService : IFlowTriggerAdminService
{
    private readonly CP6Context _db;
    private readonly IFlowTriggerService _fire;

    public FlowTriggerAdminService(CP6Context db, IFlowTriggerService fire)
    {
        _db = db;
        _fire = fire;
    }

    public async Task<List<FlowTriggerListItem>> ListAsync(CancellationToken ct)
        => await _db.Wf_FlowTriggers.OrderBy(t => t.FlowKey).ThenBy(t => t.TriggerType)
            .Select(t => ToItem(t)).ToListAsync(ct);

    public async Task<FlowTriggerListItem?> GetAsync(Guid id, CancellationToken ct)
    {
        var t = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(x => x.Id == id, ct);
        return t == null ? null : ToItem(t);
    }

    public async Task<(Guid Id, string? ApiKeyPlain)> CreateAsync(FlowTriggerSaveReq req, CancellationToken ct)
    {
        await FlowTriggerValidator.ValidateAsync(_db, req, ct);   // F-T1 落地；E-T1 阶段先建含基本必填检查的桩（见 Step 3 末注）
        var t = new Wf_FlowTrigger
        {
            FlowKey = req.FlowKey, TriggerType = req.TriggerType,
            ConfigJson = string.IsNullOrWhiteSpace(req.ConfigJson) ? "{}" : req.ConfigJson,
            Enabled = req.Enabled,
            EventKey = req.TriggerType == WfTriggerType.Event ? req.EventKey : null,
            StarterUserId = req.StarterUserId,
        };
        string? plain = null;
        if (req.TriggerType == WfTriggerType.Message)
        {
            plain = WfApiKeyHelper.NewRawKey();
            t.ApiKeyHash = WfApiKeyHelper.HashOf(plain);
        }
        if (req.TriggerType == WfTriggerType.Timer)
            t.NextDueUtc = WfCronHelper.NextUtc(WfTriggerConfig.ParseTimer(t.ConfigJson).Cron, DateTime.UtcNow);
        _db.Wf_FlowTriggers.Add(t);
        await _db.SaveChangesAsync(ct);
        return (t.Id, plain);
    }

    public async Task UpdateAsync(Guid id, FlowTriggerSaveReq req, CancellationToken ct)
    {
        var t = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("E-WF-022: 触发器不存在");
        if (t.TriggerType != req.TriggerType)
            throw new InvalidOperationException("E-WF-022: 触发器类型不可变更（删除重建）");
        await FlowTriggerValidator.ValidateAsync(_db, req, ct);
        t.FlowKey = req.FlowKey;
        t.ConfigJson = string.IsNullOrWhiteSpace(req.ConfigJson) ? "{}" : req.ConfigJson;
        t.Enabled = req.Enabled;
        t.EventKey = req.TriggerType == WfTriggerType.Event ? req.EventKey : null;
        t.StarterUserId = req.StarterUserId;
        if (t.TriggerType == WfTriggerType.Timer)
            t.NextDueUtc = WfCronHelper.NextUtc(WfTriggerConfig.ParseTimer(t.ConfigJson).Cron, DateTime.UtcNow);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken ct)
    {
        var t = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("E-WF-022: 触发器不存在");
        t.Enabled = enabled;
        if (enabled && t.TriggerType == WfTriggerType.Timer && t.NextDueUtc == null)
            t.NextDueUtc = WfCronHelper.NextUtc(WfTriggerConfig.ParseTimer(t.ConfigJson).Cron, DateTime.UtcNow);  // cron 修复后重新上膛
        await _db.SaveChangesAsync(ct);
    }

    public async Task<string> ResetKeyAsync(Guid id, CancellationToken ct)
    {
        var t = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("E-WF-022: 触发器不存在");
        if (t.TriggerType != WfTriggerType.Message)
            throw new InvalidOperationException("E-WF-022: 仅 message 触发器有 API key");
        var plain = WfApiKeyHelper.NewRawKey();
        t.ApiKeyHash = WfApiKeyHelper.HashOf(plain);
        await _db.SaveChangesAsync(ct);
        return plain;
    }

    public async Task<TriggerFireResult> ManualFireAsync(Guid id, CancellationToken ct)
    {
        var t = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("E-WF-022: 触发器不存在");
        var varsJson = t.TriggerType == WfTriggerType.Timer
            ? WfTriggerConfig.ParseTimer(t.ConfigJson).VarsJson
            : "{}";
        return await _fire.FireAsync(t, varsJson, t.TriggerType, $"manual:{Guid.NewGuid():N}", ct);
    }

    public async Task<List<TriggerFireListItem>> ListFiresAsync(Guid id, int take, CancellationToken ct)
        => await _db.Wf_TriggerFires.Where(f => f.TriggerId == id)
            .OrderByDescending(f => f.FiredUtc).Take(Math.Clamp(take, 1, 200))
            .Select(f => new TriggerFireListItem(f.Id, f.IdempotencyKey, f.FiredUtc, f.InstanceId, f.Source, f.Error))
            .ToListAsync(ct);

    private static FlowTriggerListItem ToItem(Wf_FlowTrigger t)
        => new(t.Id, t.FlowKey, t.TriggerType, t.Enabled, t.EventKey, t.StarterUserId,
               t.NextDueUtc, t.LastFiredUtc, t.ApiKeyHash != null, t.ConfigJson);
}
```

> **E-T1 阶段的 `FlowTriggerValidator` 桩**：本任务同文件夹先建最小版（仅必填检查，全代码）：
>
> ```csharp
> // CP6.Core/Services/Wf/FlowTriggerValidator.cs（E-T1 最小版；F-T1 以 TDD 扩成 spec §5 全量校验）
> public static class FlowTriggerValidator
> {
>     public static Task ValidateAsync(CP6Context db, FlowTriggerSaveReq req, CancellationToken ct)
>     {
>         if (string.IsNullOrWhiteSpace(req.FlowKey)) throw new InvalidOperationException("E-WF-023: FlowKey 必填");
>         if (req.TriggerType is < WfTriggerType.Timer or > WfTriggerType.Message)
>             throw new InvalidOperationException("E-WF-022: 触发器类型非法");
>         if (req.StarterUserId == Guid.Empty) throw new InvalidOperationException("E-WF-022: StarterUserId 必填");
>         return Task.CompletedTask;
>     }
> }
> ```

- [ ] **Step 4: 实现控制器**（仿 `FlowAdminController` 范式：`LocalizedControllerBase` + `Ok2`/`Err` 壳；权限点映射表②——`[RequirePermission]` 在 F-T2 seed 落地前会 403，本任务先贴特性、F-T2 seed 后 QA 验通）

```csharp
// CP6.WebApi/Controllers/Oa/FlowTriggerAdminController.cs
using CP6.Core.Auth;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>流程触发器管理（spec §4，流程管理页「触发器」tab 后端）。
/// 权限点（spec §6 OA.FlowTrigger.* → 映射表②）：View=查，Edit=增改/启停/试发/重置 key。</summary>
[ApiController]
[Route("api/oa/flow-triggers")]
[Authorize]
public class FlowTriggerAdminController : LocalizedControllerBase
{
    private readonly IFlowTriggerAdminService _admin;

    public FlowTriggerAdminController(IFlowTriggerAdminService admin) { _admin = admin; }

    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpGet("list")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.View")]
    public async Task<IActionResult> List(CancellationToken ct) => Ok2(await _admin.ListAsync(ct));

    [HttpGet("{id:guid}")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.View")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var item = await _admin.GetAsync(id, ct);
        return item is null ? NotFound(new { code = 404, message = "E-WF-022" }) : Ok2(item);
    }

    [HttpPost]
    [RequirePermission("oa-flow-admin", "FlowTrigger.Edit")]
    public async Task<IActionResult> Create([FromBody] FlowTriggerSaveReq req, CancellationToken ct)
    {
        try
        {
            var (id, apiKeyPlain) = await _admin.CreateAsync(req, ct);
            return Ok2(new { id, apiKeyPlain });   // 明文只此一次（spec §3.4）
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.Edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] FlowTriggerSaveReq req, CancellationToken ct)
    {
        try { await _admin.UpdateAsync(id, req, ct); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPost("{id:guid}/enable")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.Edit")]
    public async Task<IActionResult> Enable(Guid id, [FromBody] EnableReq r, CancellationToken ct)
    {
        try { await _admin.SetEnabledAsync(id, r.Enabled, ct); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPost("{id:guid}/reset-key")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.Edit")]
    public async Task<IActionResult> ResetKey(Guid id, CancellationToken ct)
    {
        try { return Ok2(new { apiKeyPlain = await _admin.ResetKeyAsync(id, ct) }); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPost("{id:guid}/manual-fire")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.Edit")]   // 手动试发归 Edit（spec §6）
    public async Task<IActionResult> ManualFire(Guid id, CancellationToken ct)
    {
        try
        {
            var r = await _admin.ManualFireAsync(id, ct);
            return r.Success
                ? Ok2(new { r.InstanceId })
                : BadRequest(new { code = 400, message = r.Error });
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpGet("{id:guid}/fires")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.View")]
    public async Task<IActionResult> Fires(Guid id, [FromQuery] int take, CancellationToken ct)
        => Ok2(await _admin.ListFiresAsync(id, take <= 0 ? 20 : take, ct));

    [HttpPost("cron-preview")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.View")]
    public IActionResult CronPreview([FromBody] CronPreviewReq r)
        => WfCronHelper.IsValid(r.Cron)
            ? Ok2(new { next = WfCronHelper.PreviewUtc(r.Cron, DateTime.UtcNow, 5) })
            : BadRequest(new { code = 400, message = "E-WF-022" });

    public record EnableReq(bool Enabled);
    public record CronPreviewReq(string Cron);
}
```

- [ ] **Step 5: DI** — `Program.cs`（IFlowTriggerService 注册同块）：

```csharp
builder.Services.AddScoped<CP6.Core.Services.Wf.IFlowTriggerAdminService, CP6.Core.Services.Wf.FlowTriggerAdminService>();
```

- [ ] **Step 6: 跑验证 PASS + Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter FlowTriggerAdminTests
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-trigger): E-T1 管理后端 CRUD/启停/手动试发/流水/key 重置/cron 预览+权限特性"
```

---

### Task E-T2: 前端「触发器」tab（API + 分型表单 + 流水抽屉 + key 一次性显示）+ vitest

> 纪律：视图全 `t()`（键在 F-T2 seed）；零硬编码色（CpTag tone / Design System token）；每步 `npm run test` + `npm run type-check`。

**Files:**
- Create: `cp6.web/src/api/oa/flowTrigger.ts`
- Create: `cp6.web/src/views/oa/admin/flowTriggerModel.ts`（纯逻辑，vitest 可测）
- Create: `cp6.web/src/views/oa/admin/__tests__/flowTriggerModel.spec.ts`
- Modify: `cp6.web/src/views/oa/admin/FlowAdmin.vue`（el-tabs 包裹）
- Create: `cp6.web/src/views/oa/admin/FlowTriggerPanel.vue`
- Create: `cp6.web/src/views/oa/admin/FlowTriggerDialog.vue`

- [ ] **Step 1: 写失败 vitest（纯逻辑先行）**

```ts
// cp6.web/src/views/oa/admin/__tests__/flowTriggerModel.spec.ts
import { describe, it, expect } from 'vitest'
import { TRIGGER_TYPES, CRON_PRESETS, typeTone, validateTriggerForm, buildConfigJson } from '../flowTriggerModel'

describe('flowTriggerModel', () => {
  it('three trigger types with stable codes', () => {
    expect(TRIGGER_TYPES.map(t => t.value)).toEqual([0, 1, 2])
  })
  it('cron presets include daily/monday/day25/monthEnd(≈28th)', () => {
    const crons = CRON_PRESETS.map(p => p.cron)
    expect(crons).toContain('0 9 * * *')
    expect(crons).toContain('0 9 * * 1')
    expect(crons).toContain('0 9 25 * *')
    expect(crons).toContain('0 9 28 * *')   // 每月末近似（NCrontab 无 L，映射表③）
  })
  it('typeTone maps to Cp tones (no hardcoded colors)', () => {
    expect(['ok', 'info', 'warn', 'muted']).toContain(typeTone(0))
  })
  it('validateTriggerForm flags missing per-type fields', () => {
    expect(validateTriggerForm({ triggerType: 0, flowKey: 'fk', starterUserId: 'u', cron: '' }).length).toBeGreaterThan(0)
    expect(validateTriggerForm({ triggerType: 1, flowKey: 'fk', starterUserId: 'u', eventKey: '' }).length).toBeGreaterThan(0)
    expect(validateTriggerForm({ triggerType: 0, flowKey: '', starterUserId: 'u', cron: '0 9 * * *' }).length).toBeGreaterThan(0)
    expect(validateTriggerForm({ triggerType: 0, flowKey: 'fk', starterUserId: 'u', cron: '0 9 * * *' })).toEqual([])
  })
  it('buildConfigJson per type', () => {
    expect(JSON.parse(buildConfigJson({ triggerType: 0, cron: '0 9 * * *', varsJson: '{"a":1}' }))).toEqual({ cron: '0 9 * * *', varsJson: '{"a":1}' })
    expect(JSON.parse(buildConfigJson({ triggerType: 1, varsMap: { orderNo: '$.No' } }))).toEqual({ varsMap: { orderNo: '$.No' } })
    expect(JSON.parse(buildConfigJson({ triggerType: 2, varsSchema: ['orderNo'] }))).toEqual({ varsSchema: ['orderNo'] })
  })
})
```

- [ ] **Step 2: 跑验证 FAIL** — `cd cp6.web && npm run test -- flowTriggerModel`。

- [ ] **Step 3: 实现纯逻辑 + API**

```ts
// cp6.web/src/views/oa/admin/flowTriggerModel.ts
export interface TriggerFormState {
  triggerType: number
  flowKey?: string
  starterUserId?: string
  cron?: string
  varsJson?: string
  eventKey?: string
  varsMap?: Record<string, string>
  varsSchema?: string[]
}

export const TRIGGER_TYPES = [
  { value: 0, labelKey: 'oa.flowtrigger.type.timer' },
  { value: 1, labelKey: 'oa.flowtrigger.type.event' },
  { value: 2, labelKey: 'oa.flowtrigger.type.message' },
] as const

/** cron 常用预设（spec §4；「每月末」按 28 日近似——NCrontab 无 L 语义，映射表③，文案已注明） */
export const CRON_PRESETS = [
  { labelKey: 'oa.flowtrigger.preset.daily', cron: '0 9 * * *' },
  { labelKey: 'oa.flowtrigger.preset.monday', cron: '0 9 * * 1' },
  { labelKey: 'oa.flowtrigger.preset.day25', cron: '0 9 25 * *' },
  { labelKey: 'oa.flowtrigger.preset.monthEnd', cron: '0 9 28 * *' },
] as const

/** CpTag tone（零硬编码色）：timer=info / event=ok / message=warn */
export function typeTone(triggerType: number): 'ok' | 'info' | 'warn' | 'muted' {
  return triggerType === 0 ? 'info' : triggerType === 1 ? 'ok' : triggerType === 2 ? 'warn' : 'muted'
}

/** 客户端镜像校验（后端权威 E-WF-022/023）；返回 i18n 键数组，空=通过 */
export function validateTriggerForm(f: TriggerFormState): string[] {
  const errs: string[] = []
  if (!f.flowKey) errs.push('oa.flowtrigger.err.flowKey')
  if (!f.starterUserId) errs.push('oa.flowtrigger.err.starter')
  if (f.triggerType === 0 && !f.cron) errs.push('oa.flowtrigger.err.cron')
  if (f.triggerType === 1 && !f.eventKey) errs.push('oa.flowtrigger.err.eventKey')
  return errs
}

export function buildConfigJson(f: Partial<TriggerFormState> & { triggerType: number }): string {
  if (f.triggerType === 0) return JSON.stringify({ cron: f.cron ?? '', ...(f.varsJson ? { varsJson: f.varsJson } : {}) })
  if (f.triggerType === 1) return JSON.stringify({ varsMap: f.varsMap ?? {} })
  return JSON.stringify({ varsSchema: f.varsSchema ?? [] })
}
```

```ts
// cp6.web/src/api/oa/flowTrigger.ts —— 范式照 designer.ts/flowAdmin.ts（http + 剥壳 res.data ?? res）
import http from '../http'

export interface FlowTriggerItem {
  id: string
  flowKey: string
  triggerType: number
  enabled: boolean
  eventKey?: string | null
  starterUserId: string
  nextDueUtc?: string | null
  lastFiredUtc?: string | null
  hasApiKey: boolean
  configJson: string
}

export interface TriggerFireItem {
  id: string
  idempotencyKey: string
  firedUtc: string
  instanceId?: string | null
  source: number
  error?: string | null
}

export interface FlowTriggerSaveBody {
  flowKey: string
  triggerType: number
  configJson: string
  enabled: boolean
  eventKey?: string | null
  starterUserId: string
}

const unwrap = (res: any) => res?.data ?? res

export const flowTriggerApi = {
  list: async (): Promise<FlowTriggerItem[]> => unwrap(await http.get('/oa/flow-triggers/list')) ?? [],
  get: async (id: string): Promise<FlowTriggerItem> => unwrap(await http.get(`/oa/flow-triggers/${id}`)),
  create: async (body: FlowTriggerSaveBody): Promise<{ id: string; apiKeyPlain?: string | null }> =>
    unwrap(await http.post('/oa/flow-triggers', body)),
  update: (id: string, body: FlowTriggerSaveBody) => http.put(`/oa/flow-triggers/${id}`, body),
  enable: (id: string, enabled: boolean) => http.post(`/oa/flow-triggers/${id}/enable`, { enabled }),
  resetKey: async (id: string): Promise<{ apiKeyPlain: string }> =>
    unwrap(await http.post(`/oa/flow-triggers/${id}/reset-key`)),
  manualFire: async (id: string): Promise<{ instanceId?: string }> =>
    unwrap(await http.post(`/oa/flow-triggers/${id}/manual-fire`)),
  fires: async (id: string, take = 20): Promise<TriggerFireItem[]> =>
    unwrap(await http.get(`/oa/flow-triggers/${id}/fires`, { params: { take } })) ?? [],
  cronPreview: async (cron: string): Promise<{ next: string[] }> =>
    unwrap(await http.post('/oa/flow-triggers/cron-preview', { cron })),
}
```

- [ ] **Step 4: FlowAdmin.vue 加 tab**（既有流程列表内容**原样整体移入**第一个 tab-pane，行为零变；`:count` 只在流程 tab 生效）：

```vue
<!-- template 改造骨架（script 追加 activeTab + FlowTriggerPanel import，其余既有代码不动） -->
<CpPageShell :title="t('oa.flowadmin.title')" :count="activeTab === 'flows' ? total : undefined">
  <template #actions>
    <el-button v-if="activeTab === 'flows'" :icon="Refresh" circle :loading="refreshing" @click="refresh" />
  </template>
  <el-tabs v-model="activeTab">
    <el-tab-pane :label="t('oa.flowadmin.tab.flows')" name="flows">
      <!-- 既有 el-alert + CpListPage 原样移入 -->
    </el-tab-pane>
    <el-tab-pane :label="t('oa.flowtrigger.tab')" name="triggers" lazy>
      <FlowTriggerPanel />
    </el-tab-pane>
  </el-tabs>
</CpPageShell>
```

```ts
// script setup 追加
import FlowTriggerPanel from './FlowTriggerPanel.vue'
const activeTab = ref<'flows' | 'triggers'>('flows')
```

- [ ] **Step 5: FlowTriggerPanel.vue**（列表 + 操作 + 流水抽屉 + key 一次性弹窗；骨架级完整）：

```vue
<template>
  <div class="flow-trigger-panel">
    <div class="panel-actions">
      <el-button type="primary" @click="openCreate">{{ t('oa.flowtrigger.new') }}</el-button>
      <el-button :icon="Refresh" circle @click="reload" />
    </div>

    <el-table :data="rows" v-loading="loading" :empty-text="t('oa.flowtrigger.empty')">
      <el-table-column prop="triggerType" :label="t('oa.flowtrigger.col.type')" width="110">
        <template #default="{ row }">
          <CpTag :tone="typeTone(row.triggerType)">{{ t(typeLabelKey(row.triggerType)) }}</CpTag>
        </template>
      </el-table-column>
      <el-table-column prop="flowKey" :label="t('oa.flowtrigger.col.flowKey')" min-width="160" />
      <el-table-column prop="eventKey" :label="t('oa.flowtrigger.col.eventKey')" min-width="180">
        <template #default="{ row }">{{ row.eventKey ?? '—' }}</template>
      </el-table-column>
      <el-table-column :label="t('oa.flowtrigger.col.enabled')" width="90">
        <template #default="{ row }">
          <el-switch :model-value="row.enabled" :loading="toggling.has(row.id)"
                     @change="(v: boolean | string | number) => toggleEnable(row, v as boolean)" />
        </template>
      </el-table-column>
      <el-table-column prop="nextDueUtc" :label="t('oa.flowtrigger.col.nextDue')" width="170">
        <template #default="{ row }">{{ fmtUtc(row.nextDueUtc) }}</template>
      </el-table-column>
      <el-table-column prop="lastFiredUtc" :label="t('oa.flowtrigger.col.lastFired')" width="170">
        <template #default="{ row }">{{ fmtUtc(row.lastFiredUtc) }}</template>
      </el-table-column>
      <el-table-column :label="t('oa.flowtrigger.col.actions')" width="280" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="openEdit(row)">{{ t('common.edit') }}</el-button>
          <el-button link type="primary" @click="manualFire(row)">{{ t('oa.flowtrigger.manualFire') }}</el-button>
          <el-button link @click="openFires(row)">{{ t('oa.flowtrigger.fires') }}</el-button>
          <el-button v-if="row.triggerType === 2" link type="danger" @click="resetKey(row)">
            {{ t('oa.flowtrigger.resetKey') }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <FlowTriggerDialog v-model="dialogVisible" :editing="editing" @saved="onSaved" />

    <!-- 流水抽屉（spec §4：最近 N 条 时间/结果/实例链接/错误） -->
    <el-drawer v-model="firesVisible" :title="t('oa.flowtrigger.fires')" size="480px">
      <el-table :data="fires" v-loading="firesLoading">
        <el-table-column prop="firedUtc" :label="t('oa.flowtrigger.fire.time')" width="170">
          <template #default="{ row }">{{ fmtUtc(row.firedUtc) }}</template>
        </el-table-column>
        <el-table-column :label="t('oa.flowtrigger.fire.result')" width="90">
          <template #default="{ row }">
            <CpTag :tone="row.instanceId ? 'ok' : row.error ? 'warn' : 'muted'">
              {{ row.instanceId ? t('oa.flowtrigger.fire.ok') : row.error ? t('oa.flowtrigger.fire.fail') : t('oa.flowtrigger.fire.pending') }}
            </CpTag>
          </template>
        </el-table-column>
        <el-table-column :label="t('oa.flowtrigger.fire.instance')" min-width="140">
          <template #default="{ row }">
            <router-link v-if="row.instanceId" :to="`/oa/inbox?instanceId=${row.instanceId}`">{{ row.instanceId.slice(0, 8) }}…</router-link>
            <span v-else>—</span>
          </template>
        </el-table-column>
        <el-table-column prop="error" :label="t('oa.flowtrigger.fire.error')" min-width="160" show-overflow-tooltip />
      </el-table>
    </el-drawer>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Refresh } from '@element-plus/icons-vue'
import CpTag from '@/components/base/CpTag.vue'
import FlowTriggerDialog from './FlowTriggerDialog.vue'
import { flowTriggerApi, type FlowTriggerItem, type TriggerFireItem } from '@/api/oa/flowTrigger'
import { typeTone, TRIGGER_TYPES } from './flowTriggerModel'

const { t } = useI18n()
const rows = ref<FlowTriggerItem[]>([])
const loading = ref(false)
const toggling = reactive(new Set<string>())
const dialogVisible = ref(false)
const editing = ref<FlowTriggerItem | null>(null)
const firesVisible = ref(false)
const fires = ref<TriggerFireItem[]>([])
const firesLoading = ref(false)

const typeLabelKey = (v: number) => TRIGGER_TYPES.find(x => x.value === v)?.labelKey ?? 'oa.flowtrigger.type.timer'
const fmtUtc = (s?: string | null) => (s ? new Date(s).toLocaleString() : '—')

async function reload() {
  loading.value = true
  try { rows.value = await flowTriggerApi.list() } finally { loading.value = false }
}
onMounted(reload)

function openCreate() { editing.value = null; dialogVisible.value = true }
function openEdit(row: FlowTriggerItem) { editing.value = row; dialogVisible.value = true }

async function onSaved(apiKeyPlain?: string | null) {
  if (apiKeyPlain) showKeyOnce(apiKeyPlain)
  await reload()
}

/** key 一次性显示（spec §3.4：明文只此一次） */
function showKeyOnce(plain: string) {
  ElMessageBox.alert(plain, t('oa.flowtrigger.keyTitle'), {
    confirmButtonText: t('common.ok'),
    message: `${t('oa.flowtrigger.keyOnce')}\n\n${plain}`,
  })
}

async function toggleEnable(row: FlowTriggerItem, v: boolean) {
  if (toggling.has(row.id)) return
  toggling.add(row.id)
  try { await flowTriggerApi.enable(row.id, v); row.enabled = v } 
  catch {
    // http 拦截器已 toast，无需重复提示
  }
  finally { toggling.delete(row.id); await reload() }
}

async function manualFire(row: FlowTriggerItem) {
  try {
    const r = await flowTriggerApi.manualFire(row.id)
    ElMessage.success(`${t('oa.flowtrigger.fired')}: ${r.instanceId ?? ''}`)
    await reload()
  } catch {
    // http 拦截器已 toast，无需重复提示
  }
}

async function resetKey(row: FlowTriggerItem) {
  await ElMessageBox.confirm(t('oa.flowtrigger.resetKeyConfirm'), t('oa.flowtrigger.resetKey'))
  const r = await flowTriggerApi.resetKey(row.id)
  showKeyOnce(r.apiKeyPlain)
}

async function openFires(row: FlowTriggerItem) {
  firesVisible.value = true
  firesLoading.value = true
  try { fires.value = await flowTriggerApi.fires(row.id, 20) } finally { firesLoading.value = false }
}
</script>

<style scoped>
.panel-actions { display: flex; justify-content: flex-end; gap: 8px; margin-bottom: 12px; }
</style>
```

- [ ] **Step 6: FlowTriggerDialog.vue**（分型表单：timer=cron+预设+预览 / event=eventKey+varsMap 键值编辑 / message=varsSchema 白名单；el-dialog 范式照 `SendBackDialog.vue`）：

```vue
<template>
  <el-dialog :model-value="modelValue" :title="editing ? t('common.edit') : t('oa.flowtrigger.new')"
             width="560px" @close="onClose">
    <el-form label-width="120px">
      <el-form-item :label="t('oa.flowtrigger.form.type')">
        <el-radio-group v-model="form.triggerType" :disabled="!!editing">
          <el-radio v-for="ty in TRIGGER_TYPES" :key="ty.value" :value="ty.value">{{ t(ty.labelKey) }}</el-radio>
        </el-radio-group>
      </el-form-item>
      <el-form-item :label="t('oa.flowtrigger.form.flowKey')">
        <el-input v-model="form.flowKey" />
      </el-form-item>
      <el-form-item :label="t('oa.flowtrigger.form.starter')">
        <el-input v-model="form.starterUserId" :placeholder="t('oa.flowtrigger.form.starterHint')" />
      </el-form-item>

      <!-- timer -->
      <template v-if="form.triggerType === 0">
        <el-form-item :label="t('oa.flowtrigger.form.cronPreset')">
          <el-select v-model="preset" clearable @change="applyPreset">
            <el-option v-for="p in CRON_PRESETS" :key="p.cron" :value="p.cron" :label="t(p.labelKey)" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('oa.flowtrigger.form.cron')">
          <el-input v-model="form.cron" placeholder="0 9 * * *" @change="loadPreview" />
          <div class="cron-preview">
            <div>{{ t('oa.flowtrigger.form.previewTz') }}</div>
            <div v-for="d in preview" :key="d">{{ new Date(d).toLocaleString() }}</div>
          </div>
        </el-form-item>
        <el-form-item :label="t('oa.flowtrigger.form.varsJson')">
          <el-input v-model="form.varsJson" type="textarea" :rows="2" placeholder='{"a":1}' />
        </el-form-item>
      </template>

      <!-- event -->
      <template v-if="form.triggerType === 1">
        <el-form-item :label="t('oa.flowtrigger.form.eventKey')">
          <el-input v-model="form.eventKey" placeholder="WMS|OnShipmentConfirmedAsync" />
        </el-form-item>
        <el-form-item :label="t('oa.flowtrigger.form.varsMap')">
          <div v-for="(pair, i) in varsMapPairs" :key="i" class="kv-row">
            <el-input v-model="pair.k" :placeholder="t('oa.flowtrigger.form.varName')" />
            <el-input v-model="pair.v" placeholder="$.OutboundNo" />
            <el-button link type="danger" @click="varsMapPairs.splice(i, 1)">✕</el-button>
          </div>
          <el-button link type="primary" @click="varsMapPairs.push({ k: '', v: '' })">+ {{ t('common.add') }}</el-button>
        </el-form-item>
      </template>

      <!-- message -->
      <template v-if="form.triggerType === 2">
        <el-form-item :label="t('oa.flowtrigger.form.varsSchema')">
          <el-input v-model="varsSchemaText" :placeholder="t('oa.flowtrigger.form.varsSchemaHint')" />
        </el-form-item>
        <el-alert v-if="!editing" type="info" :closable="false" show-icon>{{ t('oa.flowtrigger.keyCreateHint') }}</el-alert>
      </template>

      <el-form-item :label="t('oa.flowtrigger.col.enabled')">
        <el-switch v-model="form.enabled" />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="onClose">{{ t('common.cancel') }}</el-button>
      <el-button type="primary" :loading="saving" @click="onSave">{{ t('common.save') }}</el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { flowTriggerApi, type FlowTriggerItem } from '@/api/oa/flowTrigger'
import { TRIGGER_TYPES, CRON_PRESETS, validateTriggerForm, buildConfigJson } from './flowTriggerModel'

const props = defineProps<{ modelValue: boolean; editing: FlowTriggerItem | null }>()
const emit = defineEmits<{ 'update:modelValue': [boolean]; saved: [string | null | undefined] }>()
const { t } = useI18n()

const form = reactive({ triggerType: 0, flowKey: '', starterUserId: '', cron: '', varsJson: '', eventKey: '', enabled: true })
const varsMapPairs = ref<{ k: string; v: string }[]>([])
const varsSchemaText = ref('')
const preset = ref('')
const preview = ref<string[]>([])
const saving = ref(false)

watch(() => props.modelValue, open => { if (open) hydrate() })

function hydrate() {
  preview.value = []; preset.value = ''
  const e = props.editing
  if (!e) {
    Object.assign(form, { triggerType: 0, flowKey: '', starterUserId: '', cron: '', varsJson: '', eventKey: '', enabled: true })
    varsMapPairs.value = []; varsSchemaText.value = ''
    return
  }
  const cfg = JSON.parse(e.configJson || '{}')
  Object.assign(form, {
    triggerType: e.triggerType, flowKey: e.flowKey, starterUserId: e.starterUserId,
    cron: cfg.cron ?? '', varsJson: cfg.varsJson ?? '', eventKey: e.eventKey ?? '', enabled: e.enabled,
  })
  varsMapPairs.value = Object.entries(cfg.varsMap ?? {}).map(([k, v]) => ({ k, v: String(v) }))
  varsSchemaText.value = (cfg.varsSchema ?? []).join(',')
  if (form.cron) loadPreview()
}

function applyPreset(cron: string) { if (cron) { form.cron = cron; loadPreview() } }

async function loadPreview() {
  if (!form.cron) { preview.value = []; return }
  try { preview.value = (await flowTriggerApi.cronPreview(form.cron)).next } catch { preview.value = [] }
}

function onClose() { emit('update:modelValue', false) }

async function onSave() {
  const errs = validateTriggerForm({ ...form, triggerType: form.triggerType })
  if (errs.length) { ElMessage.warning(t(errs[0])); return }
  const body = {
    flowKey: form.flowKey, triggerType: form.triggerType, enabled: form.enabled,
    eventKey: form.triggerType === 1 ? form.eventKey : null,
    starterUserId: form.starterUserId,
    configJson: buildConfigJson({
      triggerType: form.triggerType, cron: form.cron, varsJson: form.varsJson || undefined,
      varsMap: Object.fromEntries(varsMapPairs.value.filter(p => p.k).map(p => [p.k, p.v])),
      varsSchema: varsSchemaText.value.split(',').map(s => s.trim()).filter(Boolean),
    }),
  }
  saving.value = true
  try {
    if (props.editing) { await flowTriggerApi.update(props.editing.id, body); emit('saved', null) }
    else { const r = await flowTriggerApi.create(body); emit('saved', r.apiKeyPlain) }
    onClose()
  } catch {
    // http 拦截器已 toast，无需重复提示
  }
  finally { saving.value = false }
}
</script>

<style scoped>
.kv-row { display: flex; gap: 8px; margin-bottom: 6px; }
.cron-preview { font-size: 12px; opacity: 0.75; margin-top: 4px; }
</style>
```

- [ ] **Step 7: 验证 + commit**

```bash
cd cp6.web && npm run test -- flowTriggerModel && npm run test && npm run type-check && npm run build
git add -A && git commit -m "feat(wfs-trigger): E-T2 流程管理页触发器 tab+分型表单+流水抽屉+key 一次性显示+vitest"
```

---

## Wave T-F — 校验 + 权限/i18n 种子 + QA + DoD

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

### Task F-T2: 权限点/菜单种子 + 五语 i18n seed

**Files:**
- Create: `CP6.WebApi/Seed/I18nOaFlowTriggerScreenSeed.cs`
- Modify: `CP6.WebApi/Program.cs`（菜单 734 MenuKey 回填 + MenuAction/RoleAction 幂等 seed + i18n concat）

- [ ] **Step 1: 权限/菜单种子** — `Program.cs` OA 菜单种子区（734 块之后）追加幂等块（映射表②；范本 Program.cs:850-856）：

```csharp
// wfs-trigger：菜单 734 MenuKey 回填（RoutePath /oa/flow-admin 派生口径）+ FlowTrigger 权限点（spec §6）
var flowAdminMenu = db.Sys_Menus.FirstOrDefault(m => m.MenuId == 734);
if (flowAdminMenu != null && string.IsNullOrEmpty(flowAdminMenu.MenuKey))
{
    flowAdminMenu.MenuKey = "oa-flow-admin";
    db.SaveChanges();
}
foreach (var (code, name) in new[] { ("FlowTrigger.View", "触发器查看"), ("FlowTrigger.Edit", "触发器编辑") })
{
    if (!db.Sys_MenuActions.Any(x => x.MenuId == 734 && x.ActionCode == code))
        db.Sys_MenuActions.Add(new Sys_MenuAction { MenuId = 734, ActionCode = code, ActionName = name, Sort = 0 });
    if (!db.Sys_RoleActions.Any(x => x.RoleId == 1 && x.MenuId == 734 && x.ActionCode == code))
        db.Sys_RoleActions.Add(new Sys_RoleAction { RoleId = 1, MenuId = 734, ActionCode = code });
}
db.SaveChanges();
```

- [ ] **Step 2: i18n seed**（五语 ZhCN/ZhTW/En/Ja/Ko；仿 `I18nOaServiceTaskScreenSeed`；**先 grep 既有 I18nOa* seed 去重 LangKey**）：

```csharp
// CP6.WebApi/Seed/I18nOaFlowTriggerScreenSeed.cs
using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>流程触发器画面五语（wfs-trigger；E-WF-022~024 错误码同表）。</summary>
public static class I18nOaFlowTriggerScreenSeed
{
    public static readonly Sys_Lang[] Items =
    {
        new() { LangKey = "oa.flowadmin.tab.flows", ZhCN = "流程", ZhTW = "流程", En = "Flows", Ja = "フロー", Ko = "플로우" },
        new() { LangKey = "oa.flowtrigger.tab", ZhCN = "触发器", ZhTW = "觸發器", En = "Triggers", Ja = "トリガー", Ko = "트리거" },
        new() { LangKey = "oa.flowtrigger.new", ZhCN = "新建触发器", ZhTW = "新建觸發器", En = "New Trigger", Ja = "トリガー作成", Ko = "트리거 생성" },
        new() { LangKey = "oa.flowtrigger.empty", ZhCN = "暂无触发器", ZhTW = "暫無觸發器", En = "No triggers", Ja = "トリガーなし", Ko = "트리거 없음" },
        new() { LangKey = "oa.flowtrigger.col.type", ZhCN = "类型", ZhTW = "類型", En = "Type", Ja = "種別", Ko = "유형" },
        new() { LangKey = "oa.flowtrigger.col.flowKey", ZhCN = "目标流程", ZhTW = "目標流程", En = "Target Flow", Ja = "対象フロー", Ko = "대상 플로우" },
        new() { LangKey = "oa.flowtrigger.col.eventKey", ZhCN = "事件键", ZhTW = "事件鍵", En = "Event Key", Ja = "イベントキー", Ko = "이벤트 키" },
        new() { LangKey = "oa.flowtrigger.col.enabled", ZhCN = "启用", ZhTW = "啟用", En = "Enabled", Ja = "有効", Ko = "사용" },
        new() { LangKey = "oa.flowtrigger.col.nextDue", ZhCN = "下次触发", ZhTW = "下次觸發", En = "Next Due", Ja = "次回実行", Ko = "다음 실행" },
        new() { LangKey = "oa.flowtrigger.col.lastFired", ZhCN = "上次触发", ZhTW = "上次觸發", En = "Last Fired", Ja = "前回実行", Ko = "마지막 실행" },
        new() { LangKey = "oa.flowtrigger.col.actions", ZhCN = "操作", ZhTW = "操作", En = "Actions", Ja = "操作", Ko = "작업" },
        new() { LangKey = "oa.flowtrigger.type.timer", ZhCN = "定时", ZhTW = "定時", En = "Timer", Ja = "タイマー", Ko = "타이머" },
        new() { LangKey = "oa.flowtrigger.type.event", ZhCN = "事件", ZhTW = "事件", En = "Event", Ja = "イベント", Ko = "이벤트" },
        new() { LangKey = "oa.flowtrigger.type.message", ZhCN = "外呼", ZhTW = "外呼", En = "Message", Ja = "メッセージ", Ko = "메시지" },
        new() { LangKey = "oa.flowtrigger.manualFire", ZhCN = "试发", ZhTW = "試發", En = "Test Fire", Ja = "テスト実行", Ko = "테스트 실행" },
        new() { LangKey = "oa.flowtrigger.fires", ZhCN = "流水", ZhTW = "流水", En = "Fire Log", Ja = "実行履歴", Ko = "실행 이력" },
        new() { LangKey = "oa.flowtrigger.resetKey", ZhCN = "重置密钥", ZhTW = "重置密鑰", En = "Reset Key", Ja = "キー再発行", Ko = "키 재설정" },
        new() { LangKey = "oa.flowtrigger.resetKeyConfirm", ZhCN = "重置后旧密钥立即失效，确认？", ZhTW = "重置後舊密鑰立即失效，確認？", En = "The old key becomes invalid immediately. Continue?", Ja = "再発行すると旧キーは即時無効になります。続行しますか？", Ko = "재설정하면 기존 키가 즉시 무효화됩니다. 계속하시겠습니까?" },
        new() { LangKey = "oa.flowtrigger.keyTitle", ZhCN = "API 密钥", ZhTW = "API 密鑰", En = "API Key", Ja = "API キー", Ko = "API 키" },
        new() { LangKey = "oa.flowtrigger.keyOnce", ZhCN = "密钥仅此一次显示，请立即妥善保存", ZhTW = "密鑰僅此一次顯示，請立即妥善保存", En = "This key is shown only once. Store it securely now.", Ja = "このキーは一度しか表示されません。今すぐ安全に保管してください。", Ko = "이 키는 한 번만 표시됩니다. 지금 안전하게 보관하세요." },
        new() { LangKey = "oa.flowtrigger.keyCreateHint", ZhCN = "保存后将生成并显示一次 API 密钥", ZhTW = "保存後將生成並顯示一次 API 密鑰", En = "An API key will be generated and shown once after saving", Ja = "保存後に API キーが生成され一度だけ表示されます", Ko = "저장 후 API 키가 생성되어 한 번만 표시됩니다" },
        new() { LangKey = "oa.flowtrigger.fired", ZhCN = "已发起", ZhTW = "已發起", En = "Fired", Ja = "起動済み", Ko = "실행됨" },
        new() { LangKey = "oa.flowtrigger.fire.time", ZhCN = "时间", ZhTW = "時間", En = "Time", Ja = "時刻", Ko = "시각" },
        new() { LangKey = "oa.flowtrigger.fire.result", ZhCN = "结果", ZhTW = "結果", En = "Result", Ja = "結果", Ko = "결과" },
        new() { LangKey = "oa.flowtrigger.fire.instance", ZhCN = "实例", ZhTW = "實例", En = "Instance", Ja = "インスタンス", Ko = "인스턴스" },
        new() { LangKey = "oa.flowtrigger.fire.error", ZhCN = "错误", ZhTW = "錯誤", En = "Error", Ja = "エラー", Ko = "오류" },
        new() { LangKey = "oa.flowtrigger.fire.ok", ZhCN = "成功", ZhTW = "成功", En = "OK", Ja = "成功", Ko = "성공" },
        new() { LangKey = "oa.flowtrigger.fire.fail", ZhCN = "失败", ZhTW = "失敗", En = "Failed", Ja = "失敗", Ko = "실패" },
        new() { LangKey = "oa.flowtrigger.fire.pending", ZhCN = "进行中", ZhTW = "進行中", En = "Pending", Ja = "処理中", Ko = "진행 중" },
        new() { LangKey = "oa.flowtrigger.form.type", ZhCN = "触发器类型", ZhTW = "觸發器類型", En = "Trigger Type", Ja = "トリガー種別", Ko = "트리거 유형" },
        new() { LangKey = "oa.flowtrigger.form.flowKey", ZhCN = "目标流程", ZhTW = "目標流程", En = "Target Flow", Ja = "対象フロー", Ko = "대상 플로우" },
        new() { LangKey = "oa.flowtrigger.form.starter", ZhCN = "名义发起人", ZhTW = "名義發起人", En = "Nominal Starter", Ja = "名義起動者", Ko = "명의 시작자" },
        new() { LangKey = "oa.flowtrigger.form.starterHint", ZhCN = "用户 Id（审批人 starter.* 解析依赖它）", ZhTW = "用戶 Id（審批人 starter.* 解析依賴它）", En = "User Id (starter.* approver resolution depends on it)", Ja = "ユーザー Id（starter.* 承認者解決が依存）", Ko = "사용자 Id (starter.* 결재자 해석에 사용)" },
        new() { LangKey = "oa.flowtrigger.form.cron", ZhCN = "cron 表达式", ZhTW = "cron 表達式", En = "Cron Expression", Ja = "cron 式", Ko = "cron 식" },
        new() { LangKey = "oa.flowtrigger.form.cronPreset", ZhCN = "常用预设", ZhTW = "常用預設", En = "Presets", Ja = "プリセット", Ko = "프리셋" },
        new() { LangKey = "oa.flowtrigger.form.previewTz", ZhCN = "下次触发预览（服务器默认时区）", ZhTW = "下次觸發預覽（伺服器默認時區）", En = "Next occurrences (server default timezone)", Ja = "次回実行プレビュー（サーバー既定タイムゾーン）", Ko = "다음 실행 미리보기 (서버 기본 시간대)" },
        new() { LangKey = "oa.flowtrigger.form.varsJson", ZhCN = "初始流程变量", ZhTW = "初始流程變量", En = "Initial Variables", Ja = "初期変数", Ko = "초기 변수" },
        new() { LangKey = "oa.flowtrigger.form.eventKey", ZhCN = "事件键", ZhTW = "事件鍵", En = "Event Key", Ja = "イベントキー", Ko = "이벤트 키" },
        new() { LangKey = "oa.flowtrigger.form.varsMap", ZhCN = "变量映射", ZhTW = "變量映射", En = "Variable Mapping", Ja = "変数マッピング", Ko = "변수 매핑" },
        new() { LangKey = "oa.flowtrigger.form.varName", ZhCN = "变量名", ZhTW = "變量名", En = "Variable", Ja = "変数名", Ko = "변수명" },
        new() { LangKey = "oa.flowtrigger.form.varsSchema", ZhCN = "白名单字段", ZhTW = "白名單欄位", En = "Allowed Fields", Ja = "許可フィールド", Ko = "허용 필드" },
        new() { LangKey = "oa.flowtrigger.form.varsSchemaHint", ZhCN = "逗号分隔；不在名单的负载键将被丢弃", ZhTW = "逗號分隔；不在名單的負載鍵將被丟棄", En = "Comma separated; payload keys not listed are dropped", Ja = "カンマ区切り；リスト外のキーは破棄", Ko = "쉼표 구분; 목록에 없는 키는 삭제됨" },
        new() { LangKey = "oa.flowtrigger.preset.daily", ZhCN = "每日 9 点", ZhTW = "每日 9 點", En = "Daily 09:00", Ja = "毎日 9 時", Ko = "매일 9시" },
        new() { LangKey = "oa.flowtrigger.preset.monday", ZhCN = "每周一 9 点", ZhTW = "每週一 9 點", En = "Monday 09:00", Ja = "毎週月曜 9 時", Ko = "매주 월요일 9시" },
        new() { LangKey = "oa.flowtrigger.preset.day25", ZhCN = "每月 25 日 9 点", ZhTW = "每月 25 日 9 點", En = "25th 09:00", Ja = "毎月 25 日 9 時", Ko = "매월 25일 9시" },
        new() { LangKey = "oa.flowtrigger.preset.monthEnd", ZhCN = "每月末（按 28 日近似）", ZhTW = "每月末（按 28 日近似）", En = "Month end (approx. 28th)", Ja = "月末（28 日で近似）", Ko = "월말 (28일로 근사)" },
        new() { LangKey = "oa.flowtrigger.err.flowKey", ZhCN = "请填写目标流程", ZhTW = "請填寫目標流程", En = "Target flow is required", Ja = "対象フローを入力してください", Ko = "대상 플로우를 입력하세요" },
        new() { LangKey = "oa.flowtrigger.err.starter", ZhCN = "请填写名义发起人", ZhTW = "請填寫名義發起人", En = "Nominal starter is required", Ja = "名義起動者を入力してください", Ko = "명의 시작자를 입력하세요" },
        new() { LangKey = "oa.flowtrigger.err.cron", ZhCN = "请填写 cron 表达式", ZhTW = "請填寫 cron 表達式", En = "Cron expression is required", Ja = "cron 式を入力してください", Ko = "cron 식을 입력하세요" },
        new() { LangKey = "oa.flowtrigger.err.eventKey", ZhCN = "请填写事件键", ZhTW = "請填寫事件鍵", En = "Event key is required", Ja = "イベントキーを入力してください", Ko = "이벤트 키를 입력하세요" },
        new() { LangKey = "E-WF-022", ZhCN = "触发器配置无效", ZhTW = "觸發器配置無效", En = "Invalid trigger configuration", Ja = "トリガー設定が無効です", Ko = "트리거 구성이 잘못되었습니다" },
        new() { LangKey = "E-WF-023", ZhCN = "目标流程不可发起", ZhTW = "目標流程不可發起", En = "Target flow cannot be started", Ja = "対象フローを起動できません", Ko = "대상 플로우를 시작할 수 없습니다" },
        new() { LangKey = "E-WF-024", ZhCN = "触发发起失败", ZhTW = "觸發發起失敗", En = "Trigger fire failed", Ja = "トリガー起動に失敗しました", Ko = "트리거 실행에 실패했습니다" },
    };
}
```

`Program.cs` i18n concat 链（`:1812-1814` 同块）追加：

```csharp
.Concat(CP6.WebApi.Seed.I18nOaFlowTriggerScreenSeed.Items)   // oa.flowtrigger.*/E-WF-022~024
```

> 去重检查：`grep -rn "oa.flowadmin.tab.flows\|oa.flowtrigger\." CP6.WebApi/Seed/` 确认无 LangKey 与既有 seed 重复（`common.add`/`common.save`/`common.ok`/`common.edit`/`common.cancel` 属既有通用键，**不在本 seed 重复放**；若 grep 发现缺失则补进本 seed）。

- [ ] **Step 3: 验证 + commit**

```bash
dotnet build CP6.WebApi/CP6.WebApi.csproj
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-trigger): F-T2 菜单734 MenuKey 回填+FlowTrigger.View/Edit 权限点+五语 i18n seed"
```

---

### Task F-T3: gstack QA harness（只写不跑）+ DoD 自查

**Files:**
- Create: `docs/superpowers/qa/wfs-flow-trigger/README.md`（剧本）
- Create: `docs/superpowers/qa/wfs-flow-trigger/seed.sql`
- Create: `docs/superpowers/qa/wfs-flow-trigger/qa_flow_trigger.ps1`（HTTP e2e，ASCII 数据）

- [ ] **Step 1: 写 harness**（结构照 `docs/superpowers/qa/wfs-service-task/` E-T3 先例：README 剧本 + seed.sql + ps1；隔离库 `CP6DB_OA` 真 SQL Server，harness 只写不跑服务器）。剧本 8 条：
  1. **管理页建三型触发器**（真浏览器）：触发器 tab → 分别建 timer（预设「每日 9 点」+ cron 预览显示 5 个未来时刻）/ event（eventKey=`QA|OnEchoAsync` + varsMap）/ message（varsSchema=`orderNo,amount`，**创建后弹出明文 key 且仅此一次**——刷新后不可再见）。
  2. **手动试发**：timer 触发器「试发」→ toast 带 instanceId → 流水抽屉出现 1 行成功（实例链接可点）。
  3. **timer 短周期发起**：seed 一个 `*/1 * * * *` 每分钟触发器 → 等 ≤90s → 流水自动 +1、NextDue 前移、实例落信箱。
  4. **event 联动**：POST `/api/oa/wf-trigger-echo/fire`（Echo 样例源）body `{eventKey:"QA|OnEchoAsync",eventId:"QA-EV-1",payloadJson:"{\"OutboundNo\":\"OB-1\"}"}` → 实例发起且 VarsJson 含 varsMap 映射值；**同 eventId 重发** → FiredCount 含幂等跳过，实例不增。
  5. **message e2e**（ps1）：`POST /api/oa/flow-triggers/{id}/fire` 三头齐 → 201 {instanceId}；同 Idempotency-Key 重放 → 200 同 instanceId；错 key → 401；停用后 → 404（与不存在 GUID 的 404 响应体逐字段一致）；缺 Idempotency-Key → 400；body 65KB → 400；白名单外字段不入 VarsJson。
  6. **key 重置**：重置 → 新明文一次性显示 → 旧 key 打端点 401、新 key 201。
  7. **保存校验**：cron 填 `not a cron` 保存 → 400 E-WF-022 文案（五语抽 2 语验 i18n）；FlowKey 填停用流程 → E-WF-023。
  8. **流水抽屉**：查看 message 触发器流水 → 时间/结果/实例链接/错误列齐（含一条人为失败：发起人停用后试发 → Error 显示 E-WF-022）。
  - seed.sql：OA 单数表名、`SET QUOTED_IDENTIFIER ON`；seed 一个 enabled 流程（复用 ServiceTask harness 的 FlowDef 模式）+ QA 发起人。
- [ ] **Step 2: commit** — `git add -A && git commit -m "test(wfs-trigger): F-T3 gstack QA harness(8 剧本+seed+e2e 脚本)"`
- [ ] **Step 3: 末期 live QA（用户在场）** — 隔离库 `CP6DB_OA` 起后端 + 前端 → 跑 ps1 e2e + gstack 真浏览器过 8 剧本。**抓 bug 当场 TDD 修**。

---

## DoD / 验收

- [ ] 后端 `dotnet test CP6.Tests/CP6.Tests.csproj` 全绿（1509[5 skip] → +N）；**既有 Wf/Integration 测试字节等价**（引擎零改动、dispatcher 既有路由零改动）。
- [ ] 前端 `npm run test`（320 → +N）/ `npm run type-check` / `npm run build` 全绿。
- [ ] EF `dotnet ef migrations has-pending-model-changes` clean；**本波恰一次迁移 `WfsFlowTrigger`**（两表四索引，零其他改动）。
- [ ] **零跨模块污染**：`git diff --stat fb90d75..HEAD` 中 Integration 目录仅 `IntegrationEventDispatcher.cs`（1 注入+1 fallback 分支）与新增 `IWfTriggerBridgeHook.cs`；无 Space/WMS/MES/FIN 业务文件。
- [ ] spec §8 测试矩阵全覆盖（见下表）；E-WF-022/023/024 保存+运行时双检各有专测。
- [ ] 五语 seed 齐（ZhCN/ZhTW/En/Ja/Ko）、LangKey 无重复；权限点 FlowTrigger.View/Edit seed + RoleId=1 授权。
- [ ] 零硬编码色（CpTag tone / Design System token）。
- [ ] gstack QA harness 齐（8 剧本）+ live QA 全过（用户在场，隔离库 CP6DB_OA）。

### 覆盖核对（spec §8 → 测试 → 任务）

| spec §8 条目 | 测试 | 任务 |
|---|---|---|
| FireAsync 幂等撞键返回既有实例 | `Fire_SameKey_Replays_ExistingInstance_NoSecondInstance` | A-T2 |
| Enabled=false 拒绝 | `Fire_Disabled_Rejected_NoFireRow` | A-T2 |
| StarterUserId 停用 E-WF-022 | `Fire_StarterDisabled_EWF022_ErrorBackfilled` + `Starter_MissingOrDisabled_EWF022` | A-T2 / F-T1 |
| StartAsync 失败流水回填 | `Fire_SubmitThrows_EWF024_ErrorBackfilled_RowKept` | A-T2 |
| timer 到期扫描发起 | `DueTimer_Fires_AdvancesNextDue_WritesFire` | B-T2 |
| NextDueUtc 前移抢占（并发两 worker 只发一次） | `TwoWorkers_SameDue_FiresExactlyOnce` | B-T2 |
| **占坑两段式崩溃恢复（不丢发不双发）** | `CrashBetweenPhases_RecoveryBackfills_NoLoss_NoDouble` + `RecoveryGrace_NotYetElapsed_SlotUntouched` | B-T2 |
| misfire 只补最近一次 | `Misfire_MultipleMissedDue_OnlyLatestFired` | B-T2 |
| cron 边界（月末/闰年） | `NextUtc_Day31_SkipsShortMonths` / `NextUtc_Feb29_OnlyLeapYear` | B-T1 |
| event eventKey 匹配多触发器逐发 | `OnEvent_MatchesMany_FiresEach_WithPerTriggerKey` | C-T1 |
| varsMap 映射 | `MapVars_*` + `OnEvent_VarsMap_Applied` | C-T1 |
| outbox 失败重试路径（dispatcher fallback 路由） | `Dispatch_TargetWF_OnEventAsync_RoutesToReplay_AnySource` | C-T2 |
| **部分成功重放去重（3 发 1 失败→重放仅补 2）** | `OnEvent_PartialFail_OutboxFailed_ReplayTopsUpOnlyMissing` | C-T1 |
| 未匹配零动作 | `OnEvent_NoMatch_ZeroAction_SkippedRow` | C-T1 |
| message key 常量时间校验 | `Verify_*`（等长闸+FixedTimeEquals）+ `Filter_WrongKey_401` | D-T1 / D-T2 |
| 幂等头缺失 400 | `Filter_MissingIdempotencyKey_400` | D-T2 |
| 白名单过滤 | `FilterBySchema_*` + `Fire_FirstCall_201_WithInstanceId_SchemaFiltered` | C-T1 / D-T2 |
| **404 不泄露存在性（停用=不存在）** | `Filter_DisabledTrigger_404_SameShapeAsUnknown` | D-T2 |
| 64KB 上限 | `Fire_OversizeBody_400` | D-T2 |
| 幂等重放 200 既有实例 | `Fire_SameIdempotencyKey_200_SameInstance` | D-T2 |
| QA harness（三型/预览/试发/流水/key 一次性） | 剧本 1~8 | F-T3 |

### 执行顺序与依赖（spec §10）

**T-A（A-T1 → A-T2）→ { T-B（B-T1 → B-T2 → B-T3）‖ T-C（C-T1 → C-T2）‖ T-D（D-T1 → D-T2）} → T-E（E-T1 → E-T2）→ T-F（F-T1 → F-T2 → F-T3）**

- T-B/T-C/T-D 三波仅依赖 T-A 契约，可三线并行（并行时各自独立 worktree 或串行执行皆可，合并后跑全量闸）。
- E-T1 依赖 B-T1（WfCronHelper 初始 NextDueUtc/预览）+ D-T1（key 生成）；E-T2 依赖 E-T1。
- F-T1 依赖 E-T1（Validator 挂点已在）；F-T2 依赖 E-T2（键面定稿）；F-T3 依赖全部。
- 共 **14 个任务**。每任务收口：`--filter Wf` 既有全绿（C-T2 跑全量）+ commit 不 push。

---

*生成于 2026-07-05，由 spec `2026-07-05-wfs-event-trigger-start-design.md`（唯一权威）细化。执行铁律：引擎零改动；E-WF-022~024 双检；零跨模块污染（dispatcher fallback 唯一 Integration 触点）；占坑两段式与幂等闸的语义以本计划「共享契约」末条为准，与 spec §3.1/§3.2 一致。*






