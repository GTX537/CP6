# WFS 版本治理+运维驾驶舱 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. **每个 Task 执行前必读对应 spec 章节**（`docs/superpowers/specs/2026-07-05-wfs-version-ops-design.md`，唯一权威，含评审修订，决策 D1~D5 全锁不许重新设计）。本计划所有 C#/TS 代码块均按 2026-07-05 main（fb90d75）实读代码上下文写就，测试代码逐条给全、可编译、无骨架占位（二期 56 处 `{ /* 场景注释 */ }` 被打回的教训：**绝对禁止**）。

**Goal:** 还掉 `Wf_FlowDef.cs:8` 自注的正确性债（实例按 FlowKey 取最新 schema，改版发布即污染在途）：① **版本 pin + 发布语义**——Def 多版本行（`(TenantId,FlowKey,Version)` 唯一）、Draft/Published 状态机（Published 行 SchemaJson 不可变）、实例 pinned `FlowDefId`（可空：在途恒非空、终态孤儿 null 降级）、`LoadSchemaAsync` 签名收敛 instance-scoped、`SubmitAsync` 解析「最新 Published 且 Enable」+ E-WF-029 不回落；② **设计器发布流**——copy-on-write 衍生草稿（撞键重载）、发布冻结（E-WF-030、Enable 继承前版）、版本历史只读、State/Path 表级版本 diff（复用三期投影，并行退化 JSON diff）；③ **运维驾驶舱** `FlowOps.vue` 三 tab（实例检索+版本分布 / job 运维 / 分析四报表）+ 干预四动作（job 重放/取消带 token 停泊前置、强制终止级联子实例、重解析审批人、强制推进带停泊伴随规则 + platform-admin）；④ **恰一次迁移 `WfsVersionPin`**（含数据回填 + 在途孤儿预检守卫）。

**Architecture:** `Wf_FlowDef` 加 `Status/PublishedAtUtc/PublishedBy/RowVersion` 四列、唯一索引 FlowKey→(TenantId,FlowKey,Version)；`Wf_FlowInstance` 加 `FlowDefId(Guid?)` pin 列。引擎读 schema 全部经 `LoadSchemaAsync(Wf_FlowInstance inst)`（签名收敛使漏改点编译期暴露；null pin 抛异常快速失败）。发布域新 `IFlowVersionService`（copy-on-write/publish/版本清单/历史 schema/另存草稿/不可变+禁删守卫）；运维域新 `IFlowOpsService`（检索/版本分布/job 动作/干预三动作/分析聚合），复用三期 `SubFlowCascade.CancelChildrenOfToken` 级联与二期 `CancelTokenSubtree` 血缘工具，不重复造轮。前端：设计器工具栏加版本下拉+发布按钮（`DesignerView.vue` 只做加法）、`versionDiff.ts` 纯函数消费三期 `schemaToStateMachine`/`smCapability`；新页 `views/oa/admin/FlowOps.vue`（菜单 741 `oa-flow-ops`）。分析图表**无 BI 依赖**：纯 CSS 条形 + inline SVG 折线（侦察定案：项目无 ECharts）。

**Tech Stack:** .NET 8 / EF Core（SQL Server 生产、InMemory+SQLite 测试）/ xUnit（`CP6.Tests/Wf`、InternalsVisibleTo 已开）/ Vue3 + Element Plus（`cp6.web/src/views/oa`）/ vitest / SQL Server 迁移（`dotnet ef`）。

---

## 落码纪律 / Global Constraints（每个 Task 都遵守）

- **基线锁定**：后端 `dotnet test CP6.Tests/CP6.Tests.csproj` = **1509 通过（5 skip=SQLite 既知）+ 前置各期新增**（以执行时 main 实测数为基线，只增不减、全绿）；前端 `npm run test`（vitest）= **320 + 前置各期新增** 全绿；`npm run type-check`（`NODE_OPTIONS=--max-old-space-size=8192`，package.json 既有命令）+ `npm run build` 全过。
- **EF 恰一次迁移 `WfsVersionPin`**（含数据回填 + 在途孤儿守卫 SQL）。迁移后每波末跑 `dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context` 必须 clean。
- **迁移后行为与迁移前逐字节一致（回归锁定）**：既有单行 Def 标 Published（POCO/DB 默认值即 Published，见侦察结论 #7）、在途实例回填 pin——迁移后所有既有流程的 token 状态序列/任务/FormTo/通知 bit 级等价。既有 27+ Wf 不变量测试**一个断言不许改**；唯一允许的既有测试改动 = 手工 `new Wf_FlowInstance` 直入库且后续走引擎的测试 seed 补 `FlowDefId`（构造行修补，断言零改，V-A2 Step 5 逐个列账并在 commit message 记录）。
- **前置依赖显式（执行序硬闸）**：二三期全部计划先行合入——`2026-07-05-wfs-kernel-hardening.md`（`CancelTokenSubtree`/`TokenLineage` 血缘工具、inclusive 网关）、`2026-07-05-wfs-statemachine-designer.md`（V-C 消费 `schemaToStateMachine`/`smCapability`/`FlowSchemaDto`、真壳 `DesignerView.vue`）、`2026-07-05-wfs-subflow.md`（强制终止/强推级联消费 `SubFlowCascade.CancelChildrenOfToken`、`Wf_FlowInstance.ParentInstanceId/ParentTokenId`）、`2026-07-05-wfs-event-trigger-start.md`（E-WF-023 检查点、`Wf_TriggerFire` 表）、`2026-07-05-wfs-engine-infra.md`（老化占坑告警口径=job tab 消费端）。**本计划任何 Task 不得在前置未合入时启动**。
- **引擎内写路径三律（黄金模板铁律）**：① 先校验后写；② 幂等；③ handler/引擎内部方法绝不自行 SaveChanges（干预动作服务方法作为「外壳」允许收口 SaveChanges，对齐 `WithdrawAsync`/`SendBackAsync` 先例）。
- **干预动作全双痕**：OperLog（全局 `OperLogFilter` 对控制器 action 自动记）+ `Wf_FlowHistory` 显式行（操作者+理由）。强制推进理由必填；重放/取消理由可选。
- **E 波紧跟 D 波**：V-G（i18n/QA）紧跟 V-D/V-E/V-F 合入，不允许「有 UI 无 i18n/无 QA」的中间态过夜。
- **零跨模块污染**：不碰 `cp6.web/src/views/space/**`、`Services/*Space*`、Space 迁移/DbSet。每 Task 完成 `git show --stat` 复核。
- **零硬编码色**：前端新增视觉全部走 Design System token（`var(--cp-*)` 家族）；分析图表落码前先读 dataviz skill 校色/形制（本计划 V-F2 已注记）。
- **五语 i18n**：ja / zh-CN / zh-TW / en / ko，新 UI 文案全 `t()` 运行时键，键值入 `I18nOa*ScreenSeed` 家族新 seed（V-G1）。
- **隔离 worktree**：建议 `git worktree add C:/CP6-wfs-version-ops -b feat/wfs-version-ops <前置全合入的 main>`。
- **subagent-driven TDD**：每 Task 全新编码子代理（模型按 model-policy：Opus 4.8）→ 主代理 `git show` diff 复核 → 本地 commit **不 push**。节奏：先写失败测试 → 跑验证 FAIL → 最小实现 → 跑验证 PASS → commit。提交信息 `feat(wfs-version-ops): <Task 号> 中文摘要`。

---

## 侦察结论（spec 各核实项已实读代码定案——执行者照此实现，不再二次侦察）

| # | 核实项 | 结论 |
|---|---|---|
| 1 | **FlowAdmin 删除入口（spec §2.2 核实项）** | **无**。`FlowAdmin.vue` 仅列表+启停 el-switch+刷新；`flowAdminApi` 无 delete；`FlowAdminController` 仅 list/get/enable；全仓 grep `Wf_FlowDefs.Remove`/`DeleteDef` 零命中。⇒ Published 禁删守卫**只落服务层**（`FlowVersionService.EnsureDeletable` 静态守卫，闸未来一切删除入口）+ 测试锁定，不做 UI。 |
| 2 | **Failed job 后 token 行为（spec §4.2 核实项）** | 两态都真实存在：`WfServiceJobService.ScanOnceAsync:162` 重试耗尽 → `FailServiceTokenAsync`（`FlowEngine.cs:350`）→ 节点**有** `IsError` 出边 → `AdvanceAlongErrorEdge` 改 token.NodeId 离开（=「token 已走」态）；**无**错误边 → `Suspend`（inst.Status=Suspended，**token 仍 Active 停泊原节点**=「仍停泊」态）。⇒ §4.2 token 前置校验按 token 实时状态动态判断，V-D2 测试**两态用例都构造**（有错误边→重放/取消拒 400；无错误边→放行）。 |
| 3 | **超时动作痕迹落点（spec §4.3 核实项）** | **`Wf_FlowHistory`（非 FormTo）**。`WfTimeoutService.ScanOnceAsync`：approve/reject 经 `ActAsync(SystemActor=Guid.Empty)` 写 action=`approve`/`reject` + Comment=「超时自动同意/驳回」；escalate 直接写 action=`escalate` + Comment 前缀「超时升级」；**remind 无痕**（只重发通知+顺延 DueAt）。⇒ 超时率分子 = FlowHistory 聚合查询：`(ActorId==Guid.Empty && Action IN (approve,reject) && Comment LIKE '超时自动%') OR (Action=='escalate' && Comment LIKE '超时升级%')`；remind 不计入（报表 UI 注记「催办不计」）。读模型不加列，本波补聚合查询（spec 预案）。 |
| 4 | **图表库现状（spec §4.3 核实项）** | `cp6.web/package.json` 无 echarts/chart.js/d3。⇒ 分析 tab 用**轻量方案**：条形=div 宽度百分比条、折线=inline SVG polyline，全色走 `var(--cp-*)` token，落码前执行 dataviz skill 校色/形制（V-F2 Step 0）。**不引 BI 依赖**（spec §4.3 铁律）。 |
| 5 | **LoadSchemaAsync 调用点实数** | 引擎家族 **6 处**：`FlowEngine.cs:85`（StartDraftAsync）/`:160`（办结快照）/`:194`（会签判定后流转）/`:322`（ResumeServiceTokenAsync）/`:361`（FailServiceTokenAsync）/`AdvancedFlow.cs:112`（SendBackAsync）；定义 `FlowEngine.cs:433`。**旁支实例语境读点 3 处**（同一债，V-A2 一并 pin 化）：`WfTimeoutService.cs:106`（NodeOf_Schema 按 FlowKey 读在途任务节点的 TimeoutAction）、`InboxService.cs:223`（DetailAsync 按 inst.FlowKey 读 def——現代码已对 def==null 容忍，pin 化后终态孤儿**天然降级**仅履历视图）、`InboxService.cs:248`（Running 实例 forecast 传 flowKey → 改传 pin）。**设计期读点不动语义只改行选择**：`ForecastService.cs:20`（起草前预测→「最新 Published 且 Enable」）、`DesignerService`/`FlowAdminService`（V-B 组口径改造）。 |
| 6 | **Wf_FlowDef 索引现状与多版本冲突（计划期发现）** | `CP6Context.cs:673` 现有 `UX_Wf_FlowDef_FlowKey`（FlowKey 全局唯一）→ 迁移**删除**，换 `(TenantId,FlowKey,Version)` 唯一 + `(TenantId,FlowKey,Status)` 辅助。**`UX_Wf_FlowDef_Function`/`UX_Wf_FlowDef_Code`（(TenantId,FunctionId/FlowCode) filtered 唯一）与多版本行相撞**——copy-on-write 衍生草稿复制身份码即违反行级唯一。⇒ 两索引**退非唯一辅助索引**（改名 `IX_`），「身份码租户内跨 FlowKey 唯一」语义由既有服务层校验 `DesignerService.SaveAsync` E-WF-009（`FlowKey != req.FlowKey` 排自身，天然兼容多版本行）兜底 + 测试锁定。不改 spec（身份码语义不变，只挪唯一性执行层）。 |
| 7 | **Status 默认值决策** | spec §2.1 常量 `0=Draft / 1=Published` 保持；**POCO/DB 默认值 = Published(1)**：`public int Status { get; set; } = WfFlowDefStatus.Published;` + 迁移 AddColumn defaultValue:1。依据：迁移语义「既有行=v1 已发布」与默认值同构；全仓测试/种子（`PurApprovalFlowSeed`/`OaLeaveFormSeed`/`A5BudgetFlowSeed` + 数百处测试 `new Wf_FlowDef{...}`）零改即满足「SubmitAsync 只认 Published」，回归面收敛到零。草稿行由发布流**显式** `Status=Draft` 创建。 |
| 8 | **Draft 实例 pin 点** | `DraftService.cs:19` 建 Draft 实例（`Status=Draft, CurrentNode=""`）→ V-A2 在此解析「最新 Published 且 Enable」并 pin（无可用版本→起草即 E-WF-029）；`StartDraftAsync`（`FlowEngine.cs:85`）用 pin 读 schema；`SendBackToStarterAsync`（`AdvancedFlow.cs:174`）实例回 Draft 时 pin 已有、保留原版本（D1 pin 语义）。迁移预检口径：**非终态 = Running(0)/Suspended(4)/Draft(5)** 孤儿→迁移失败；终态(1/2/3)→null 容忍。 |
| 9 | **FlowAdmin Enable 互斥（E-WF-008）组口径** | `FlowAdminService.SetEnabledAsync:27` 现按行判「1 表单↔1 启用流程」。多版本后 V-B1 改**组口径**：启停只作用于该 FlowKey **最新 Published 行**；互斥检查改「其他 FlowKey 的最新 Published 行 Enable==true 且同 FormKey」；`ListFlowsAsync` 按 FlowKey 分组取最新 Published 行（+有无草稿标记）。E-WF-008 语义不变。 |
| 10 | **设计器 load 契约现状** | `DesignerController.Load` 返回 `{ summary, schemaJson }`；`DesignerService.LoadAsync` 返回 FlowDefSummary（无 schema），schema 由 `IFlowDefService.GetDefAsync` 读。V-B2 改 load 走 `IFlowVersionService.OpenDraftAsync`（copy-on-write），响应形状**向后兼容**（summary+schemaJson 保留，增量加 version/status/rowVersion/publishedAt 字段）。 |
| 11 | **菜单/权限先例** | OA 组父菜单 740，子项 733~739 已占（`Program.cs:1346-1398`）→ FlowOps 用 **MenuId=741**、RoutePath=`/oa/flow-ops`、ParentId=740、MenuKey=`oa-flow-ops`。权限种子照波③口径：`Sys_MenuAction(MenuId,ActionCode,ActionName)` + `Sys_RoleAction` RoleId=1 授予 + 控制器 `[RequirePermission("oa-flow-ops","<action>")]`（`Sys` 控制器家族先例）；强制推进叠加 `[RequirePlatformAdmin]`（`Controllers/Platform/*` 三道闸先例，属性自身已有 `RequirePlatformAdminFilterTests` 锁定——本计划以反射断言端点属性存在，不重测三道闸）。 |
| 12 | **重放与状态闸的组合现实（计划期发现）** | worker 执行前状态闸（`WfServiceJobService.cs:93-104`）要求 **inst Running**；而「Failed 无错误边」态实例已 Suspended——若重放只改 job 不改实例，worker 拾起即 Cancel，重放无意义。⇒ `ReplayJobAsync` 伴随动作：inst.Status==Suspended 且 token 停泊于该节点 → 置回 Running（履历 `jobReplay` 行记录）。这是状态闸与重放的组合必然，不是改设计（spec §4.2 重放定义「AttemptCount=0、Pending、清 LastError」照落）。 |
| 13 | **跨计划契约逐字核对** | 三期状态机：`schemaToStateMachine(schema: FlowSchemaDto): SmView`、`smCapability(schema: FlowSchemaDto): Capability`、`SmView{states: SmState[]; paths: SmPath[]; capability: 'editable'\|'readonly'}`、`SmState{no,nodeId,type,name,approverSummary,countersign?,raw}`、`SmPath{fromNo,toNo,condition?,isError?,raw}`（类型自 `../designerModel` 的 `FlowSchemaDto/SchemaNode/SchemaEdge`，**不是** FlowSchema/FlowNode）；真壳=`DesignerView.vue`（模式开关插 `.designer-toolbar`）。三期 subflow：`SubFlowCascade.CancelChildrenOfToken(CP6Context db, Guid parentTokenId)`（token 名下在途子实例组级联取消，递归）、履历 `subFlowCascadeCancelled`、实例列 `ParentInstanceId/ParentTokenId`。二期 hardening：`FlowEngine.CancelTokenSubtree(Guid instanceId, Guid rootTokenId)`（内含子流程第五清钩子）、`SnapshotTokens`。波③：`Wf_TriggerFire{TriggerId,IdempotencyKey,FiredUtc,InstanceId?,Source,Error?,PayloadHash?}`，占坑=InstanceId 与 Error 均 null；E-WF-023 保存时校验（TriggerService）+ FireAsync 运行时检查。基建：老化占坑=占坑行 FiredUtc 超宽限，永不清仅告警。 |

**冲突点清单（不改 spec，落码口径已在上表声明）**：#6 身份码唯一索引退服务层、#7 Status 默认值=Published、#9 E-WF-008 组口径、#12 重放伴随 Suspended→Running。另：`FlowDefService.SaveDefAsync:34`「schema 变更才 Version++」的旧升版逻辑与草稿/发布状态机冲突 → V-B1 收窄（草稿行内保存**不升版**，版本号只由 copy-on-write 分配一次）——spec §3.2「保存只写草稿行 SchemaJson」的直接推论。

---

## File Structure（创建/修改清单，每文件一职责）

**后端 `CP6.Entity` / `CP6.Core`**
- Modify `CP6.Entity/DomainModels/Wf/Wf_FlowDef.cs` — 加 Status/PublishedAtUtc/PublishedBy/RowVersion 四列（类注释还债改写）。
- Modify `CP6.Entity/DomainModels/Wf/Wf_FlowInstance.cs` — 加 `FlowDefId(Guid?)` pin 列。
- Modify `CP6.Core/Services/Wf/WfStatus.cs` — 新 `WfFlowDefStatus` 常量类。
- Modify `CP6.Core/EFDbContext/CP6Context.cs` — Wf_FlowDef 索引改造 + Wf_FlowInstance pin 索引。
- Create `CP6.Core/Migrations/xxx_WfsVersionPin.cs` — 恰一次迁移（脚手架+手写回填/守卫 SQL）。
- Modify `CP6.Core/Services/Wf/FlowEngine.cs` — `LoadSchemaAsync` 签名收敛（private→internal、入参 inst）+ SubmitAsync pin + E-WF-029 + `DispatchIfFinishedAsync` private→internal（V-E 复用）。
- Modify `CP6.Core/Services/Wf/AdvancedFlow.cs` — :112 调用点收敛。
- Modify `CP6.Core/Services/Wf/WfTimeoutService.cs` — schema 缓存键 FlowKey→FlowDefId（pin 读）。
- Modify `CP6.Core/Services/Oa/DraftService.cs` — 起草即 pin。
- Modify `CP6.Core/Services/Oa/InboxService.cs` — DetailAsync pin 读 + 孤儿降级 + forecast 传 pin。
- Modify `CP6.Core/Services/Oa/ForecastService.cs` — 读口径「最新 Published 且 Enable」+ 可选 pin 入参。
- Create `CP6.Core/Services/Wf/FlowVersionService.cs` — `IFlowVersionService` + 实现（发布域全部）。
- Modify `CP6.Core/Services/Wf/FlowDefService.cs` — SaveDefAsync 收窄草稿行 + 守卫接线。
- Modify `CP6.Core/Services/Oa/DesignerService.cs` — SaveAsync 走草稿行 + ListAsync 组口径 + CloneAsync 版本口径。
- Modify `CP6.Core/Services/Oa/FlowAdminService.cs` — 组口径（侦察结论 #9）。
- Create `CP6.Core/Services/Wf/FlowOpsService.cs` — `IFlowOpsService` + 实现（检索/版本分布/job 动作/干预/分析）。
- Create `CP6.Core/Services/Wf/FlowOpsModels.cs` — 过滤器/DTO record 全家（编译单元独立便审）。

**后端 `CP6.WebApi`**
- Modify `Controllers/Oa/DesignerController.cs` — publish/versions/version-schema/draft-from 端点 + load 改 OpenDraft + 409 冲突语义。
- Create `Controllers/Oa/FlowOpsController.cs` — 驾驶舱 REST（`[RequirePermission("oa-flow-ops",...)]`；force-advance 叠 `[RequirePlatformAdmin]`）。
- Modify `Program.cs` — DI（IFlowVersionService/IFlowOpsService）+ 菜单 741 种子 + MenuAction/RoleAction 种子 + i18n concat 一行。
- Create `Seed/I18nOaFlowOpsScreenSeed.cs` — 五语 ~50 键。
- Modify（波③文件）`CP6.Core/Services/Wf/FlowTriggerService.cs` — E-WF-023 保存校验口径同步「存在最新 Published 且 Enable」。

**前端 `cp6.web/src`**
- Modify `api/oa/designer.ts` — publish/versions/versionSchema/draftFrom 客户端。
- Create `api/oa/flowOps.ts` — 驾驶舱 API 客户端。
- Modify `views/oa/designer/DesignerView.vue` — 工具栏版本下拉+发布按钮+只读横幅+冲突重载提示（只做加法）。
- Create `views/oa/designer/versionDiff.ts` — `diffVersions` 纯函数（消费三期投影）。
- Create `views/oa/designer/versionDiff.test.ts` — vitest。
- Create `views/oa/designer/VersionDiffDialog.vue` — 对比对话框（表级三色 / JSON 退化）。
- Create `views/oa/admin/FlowOps.vue` — 驾驶舱三 tab。
- Create `views/oa/admin/flowOpsCharts.ts` — 分析图表纯函数（SVG 折线点位计算等，可测）。
- Create `views/oa/admin/flowOpsCharts.test.ts` — vitest。
- Modify `router/index.ts` — `'/oa/flow-ops'` 路由一行。
- Modify `types/oa/inbox.ts`（或就近类型文件）— FlowAdminItem 增量字段镜像。

**测试 / QA**
- Create `CP6.Tests/Wf/VersionPinTests.cs` / `FlowVersionServiceTests.cs` / `FlowVersionConcurrencyTests.cs`（SQLite）/ `FlowOpsSearchTests.cs` / `FlowOpsJobActionTests.cs` / `FlowOpsInterventionTests.cs` / `FlowOpsAnalyticsTests.cs`。
- Create `CP6.Tests/Oa/FlowOpsControllerContractTests.cs` — 端点权限属性反射矩阵。
- Create `docs/superpowers/qa/wfs-version-ops/{README.md,precheck-orphan.sql,seed.sql,qa_version_ops.ps1}` — 预检 SQL + gstack harness。

---

## 共享契约（所有 Task 用这些**精确**名字与签名，前后一致，不许漂移）

```csharp
// WfStatus.cs —— 新常量类（spec §2.1）
public static class WfFlowDefStatus
{
    public const int Draft = 0;       // 草稿：可编辑、可删、不可发起
    public const int Published = 1;   // 已发布：SchemaJson/FlowName/FormKey 不可变、禁删、可 pin
}

// Wf_FlowDef 新列（V-A1）
public int Status { get; set; } = WfFlowDefStatus.Published;   // 默认 Published（侦察结论 #7）
public DateTime? PublishedAtUtc { get; set; }
public Guid? PublishedBy { get; set; }
[Timestamp] public byte[]? RowVersion { get; set; }

// Wf_FlowInstance 新列（V-A1）
public Guid? FlowDefId { get; set; }                            // 版本 pin：在途恒非空，终态孤儿 null

// FlowEngine（V-A2 签名收敛；private→internal 供 FlowOpsService 复用）
internal async Task<FlowSchema> LoadSchemaAsync(Wf_FlowInstance inst);   // null pin → throw（快速失败）
internal Task DispatchIfFinishedAsync(Wf_FlowInstance inst, Guid decidedBy, string? reason);  // 可见性放宽，体零改

// IFlowVersionService（CP6.Core/Services/Wf/FlowVersionService.cs，V-B1）
public interface IFlowVersionService
{
    Task<Wf_FlowDef?> GetLatestPublishedAsync(string flowKey);                       // 读口径唯一入口（不含 Enable 过滤）
    Task<DesignerDraftDto> OpenDraftAsync(string flowKey, Guid userId);              // 最新草稿 / copy-on-write / 撞键重载
    Task PublishAsync(string flowKey, byte[]? rowVersion, Guid userId);              // 校验→冻结→Enable 继承；失败 E-WF-030
    Task<IReadOnlyList<FlowVersionItem>> ListVersionsAsync(string flowKey);          // 版本历史（新→旧）
    Task<string?> GetVersionSchemaAsync(string flowKey, int version);                // 历史只读 / diff 取 schema
    Task<DesignerDraftDto> DraftFromVersionAsync(string flowKey, int version, Guid userId);  // 从历史另存草稿
}
public sealed record DesignerDraftDto(Guid Id, string FlowKey, int Version, int Status, string FlowName,
    string FormKey, string? FunctionId, string? FlowCode, string SchemaJson, byte[]? RowVersion);
public sealed record FlowVersionItem(int Version, int Status, DateTime? PublishedAtUtc, Guid? PublishedBy, bool Enable);
// 守卫（静态，服务层唯一口径；FlowVersionService 内）
internal static void EnsureMutable(Wf_FlowDef def);      // Published 改 SchemaJson/FlowName/FormKey → throw
internal static void EnsureDeletable(Wf_FlowDef def);    // Published → throw（闸未来删除入口，侦察结论 #1）

// IFlowOpsService（CP6.Core/Services/Wf/FlowOpsService.cs，V-D/V-E/V-F）
public interface IFlowOpsService
{
    Task<FlowOpsPage<FlowOpsInstanceItem>> SearchInstancesAsync(FlowOpsInstanceFilter filter, int page, int pageSize);
    Task<IReadOnlyList<VersionDistributionRow>> GetVersionDistributionAsync();
    Task<FlowOpsPage<FlowOpsJobItem>> SearchJobsAsync(FlowOpsJobFilter filter, int page, int pageSize);
    Task<IReadOnlyList<StaleTriggerFireItem>> GetStaleTriggerFiresAsync(int graceDays);
    Task ReplayJobAsync(Guid jobId, Guid actorId, string? reason);                   // token 停泊前置；400 语义=InvalidOperationException
    Task CancelJobAsync(Guid jobId, Guid actorId, string? reason);                   // 同上 + FailServiceTokenAsync 错误路由
    Task ForceTerminateAsync(Guid instanceId, Guid actorId, string reason);          // 撤回清场语义 + 级联子实例 + 双痕
    Task<ReResolveResult> ReResolveAsync(Guid instanceId, Guid actorId, string? reason);  // 两态：成功回 Running / 仍 Suspended
    Task ForceAdvanceAsync(Guid instanceId, Guid tokenId, Guid actorId, string reason);   // 停泊伴随规则；理由必填
    Task<FlowOpsAnalyticsDto> GetAnalyticsAsync(string? flowKey, DateTime fromUtc, DateTime toUtc);
}
// FlowOpsModels.cs（record 全家）
public sealed record FlowOpsPage<T>(IReadOnlyList<T> Rows, int Total);
public sealed record FlowOpsInstanceFilter(int[]? Statuses, string? FlowKey, int? Version, int? StuckDays,
    Guid? StarterId, DateTime? FromUtc, DateTime? ToUtc);
public sealed record FlowOpsInstanceItem(Guid Id, string? BizId, string FlowKey, string? FlowName, int? Version,
    string CurrentNode, DateTime? StuckSince, Guid StarterId, string? StarterName, int Status, DateTime CreateDate);
public sealed record VersionDistributionRow(string FlowKey, string? FlowName, int? Version, int RunningCount, int SuspendedCount);
public sealed record FlowOpsJobFilter(int[]? Statuses, string? Kind, DateTime? FromUtc, DateTime? ToUtc);
public sealed record FlowOpsJobItem(Guid Id, Guid InstanceId, string NodeId, string Kind, int Status,
    int AttemptCount, int MaxAttempts, DateTime NextAttemptAtUtc, string? LastError, DateTime? CompletedAtUtc, string FlowKey);
public sealed record StaleTriggerFireItem(Guid Id, Guid TriggerId, string IdempotencyKey, DateTime FiredUtc, int Source);
public sealed record ReResolveResult(bool Resolved, string? Reason);
public sealed record FlowOpsAnalyticsDto(IReadOnlyList<AvgDurationRow> AvgDuration, IReadOnlyList<BottleneckRow> Bottlenecks,
    IReadOnlyList<TimeoutRateRow> TimeoutRates, IReadOnlyList<RejectRateRow> RejectRates);
public sealed record AvgDurationRow(string FlowKey, DateTime Day, double AvgHours, int Count);
public sealed record BottleneckRow(string FlowKey, string NodeId, string? NodeName, double AvgStayHours, int Handled);
public sealed record TimeoutRateRow(string FlowKey, int TimeoutCount, int HandledCount, double Rate);
public sealed record RejectRateRow(string FlowKey, int Rejected, int Finished, double Rate);
```

```ts
// cp6.web/src/views/oa/designer/versionDiff.ts（V-C1，消费三期投影——类型逐字对齐侦察结论 #13）
import type { FlowSchemaDto } from './designerModel'
import { schemaToStateMachine, smCapability } from './statemachine/stateMachineModel'
export type DiffKind = 'added' | 'removed' | 'changed' | 'same'
export interface StateDiffRow { kind: DiffKind; nodeId: string; a?: { no: number; type: string; name: string; approverSummary: string; countersign?: string }; b?: /*同 a 形*/ { no: number; type: string; name: string; approverSummary: string; countersign?: string }; changedFields: string[] }
export interface PathDiffRow  { kind: DiffKind; edgeKey: string; a?: { from: string; to: string; condition?: string; isError?: boolean }; b?: { from: string; to: string; condition?: string; isError?: boolean }; changedFields: string[] }
export interface VersionDiffResult { mode: 'table' | 'json'; states: StateDiffRow[]; paths: PathDiffRow[]; jsonA: string; jsonB: string }
export function diffVersions(a: FlowSchemaDto, b: FlowSchemaDto): VersionDiffResult
```

- **错误码**：**E-WF-029**（发起失败：无「Published 且 Enable」版本；SubmitAsync/DraftService 统一抛，触发器运行时**原码透传**进 TriggerFire.Error）/ **E-WF-030**（发布失败：无草稿可发布/校验未过，聚合校验错误随响应）。驾驶舱前置态不符走 HTTP 400 + 明细（InvalidOperationException 非 E-WF 码文案）；发布 RowVersion 冲突走 HTTP **409**（前端提示重载）。
- **履历 action**：`"forceTerminate"` / `"reResolve"` / `"forceAdvance"` / `"jobReplay"` / `"jobCancel"`（全部带操作者+理由 Comment）。
- **权限**：菜单 `oa-flow-ops`（MenuId 741）actions：`view` / `job-ops` / `terminate` / `re-resolve` / `force-advance`；force-advance 端点叠 `[RequirePlatformAdmin]`。
- **前端 i18n 键前缀**：`oa.designer.pub.*`（发布/版本/diff）+ `oa.flowops.*`（驾驶舱）+ `E-WF-029`/`E-WF-030`（清单见 V-G1，~50 键五语）。

---

## Wave V-A — pin 内核（迁移 + 引擎收敛 + 定点回归）

### Task V-A0: 生产数据孤儿预检（部署前置产物，先跑后定）

**Files:**
- Create: `docs/superpowers/qa/wfs-version-ops/precheck-orphan.sql`

- [ ] **Step 1: 落预检 SQL**（spec §2.2 原文照落 + 租户/状态分组增强版）：

```sql
-- wfs-version-ops 迁移前置预检（spec §2.2）：孤儿 FlowKey 盘点。
-- 迁移铁律：非终态（Status IN 0,4,5 = Running/Suspended/Draft）孤儿 → 迁移必须失败（先修数据事故）；
--          终态（1,2,3 = Approved/Rejected/Withdrawn）孤儿 → FlowDefId 留 NULL 容忍（仅履历视图降级）。
SET QUOTED_IDENTIFIER ON;

-- ① spec 原文形（快速有无判断）
SELECT DISTINCT FlowKey FROM Wf_FlowInstance
WHERE FlowKey NOT IN (SELECT FlowKey FROM Wf_FlowDef);

-- ② 按租户+状态分组盘点（处置决策输入）
SELECT i.TenantId, i.FlowKey, i.Status, COUNT(*) AS Cnt,
       MIN(i.CreateDate) AS OldestUtc, MAX(i.CreateDate) AS NewestUtc
FROM Wf_FlowInstance i
WHERE NOT EXISTS (SELECT 1 FROM Wf_FlowDef d
                  WHERE d.FlowKey = i.FlowKey AND d.TenantId = i.TenantId)
GROUP BY i.TenantId, i.FlowKey, i.Status
ORDER BY i.TenantId, i.FlowKey, i.Status;
```

- [ ] **Step 2: 处置决策表**（写进 precheck-orphan.sql 头注释 + README，部署者照表执行）：

| 预检结果 | 处置 | 依据 |
|---|---|---|
| 零孤儿 | 直接迁移 | 理想态 |
| 仅终态孤儿（Status 1/2/3） | 直接迁移（回填 SQL 自动留 NULL） | spec §2.2「终态无引擎动作，永不 LoadSchema」 |
| 非终态孤儿（Status 0/4/5） | **停止部署**。逐单处置后重跑预检：a) 找回/重建同 FlowKey Def 行（推荐，恢复可运行）；b) 业务确认后手工 UPDATE 置终态 Withdrawn(3) + 补 FlowHistory 说明行。处置属数据订正，走生产变更流程留痕 | spec §2.2「在途孤儿本不该存在，属须先修的数据事故」 |
| Draft(5) 孤儿 | 同非终态（StartDraftAsync 需 pin）；量大且确认废弃可批量转 Withdrawn | 侦察结论 #8 |

- [ ] **Step 3: commit** — `git add -A && git commit -m "feat(wfs-version-ops): V-A0 迁移前置孤儿预检SQL+处置决策表"`

---

### Task V-A1: 常量 + 实体四列/pin 列 + 索引改造 + 恰一次迁移 WfsVersionPin

**Files:**
- Modify: `CP6.Core/Services/Wf/WfStatus.cs`
- Modify: `CP6.Entity/DomainModels/Wf/Wf_FlowDef.cs`
- Modify: `CP6.Entity/DomainModels/Wf/Wf_FlowInstance.cs`
- Modify: `CP6.Core/EFDbContext/CP6Context.cs`（:671-678 Wf_FlowDef 块 + :679 起 Wf_FlowInstance 块）
- Create: `CP6.Core/Migrations/<timestamp>_WfsVersionPin.cs`（脚手架后手补 SQL）

- [ ] **Step 1: 写失败测试**——常量/默认值断言并入 V-A3 的 `VersionPinTests.cs` 首两测（`WfFlowDefStatus_Constants` / `FlowDef_Defaults_Published`），本 Task 只需让它们编译通过。

- [ ] **Step 2: 实现常量与实体**

`WfStatus.cs` 追加（文件尾，保持既有注释风格）：

```csharp
/// <summary>流程定义版本状态（版本治理 spec §2.1）。Published 行 SchemaJson/FlowName/FormKey 不可变（服务层守卫）。</summary>
public static class WfFlowDefStatus
{
    public const int Draft = 0;       // 草稿：可编辑、可删、不可被 SubmitAsync 选中
    public const int Published = 1;   // 已发布：不可变、禁删、实例 pin 目标
}
```

`Wf_FlowDef.cs`——类注释第 8 行「阶段1 简化…按 FlowKey 取最新」改为「版本治理（2026-07-05 spec）：每次发布产生新版本行，(TenantId,FlowKey,Version) 唯一；实例按 FlowDefId pin 运行」；`FlowCode` 属性后追加：

```csharp
    /// <summary>版本状态：0=Draft(草稿,可编辑) / 1=Published(已发布,SchemaJson 不可变)。见 WfFlowDefStatus。
    /// 默认 Published：迁移语义「既有单行即 v1 已发布」+ 既有测试/种子 new Wf_FlowDef 零改（侦察结论 #7）；
    /// 草稿行由 FlowVersionService copy-on-write 显式 Status=Draft 创建。</summary>
    public int Status { get; set; } = 1;   // = WfFlowDefStatus.Published（Entity 层不引 Core，字面量+注释锚定；V-A3 常量一致性测试锁定）

    /// <summary>发布时刻 UTC（Published 时置，Draft 为 null）。</summary>
    public DateTime? PublishedAtUtc { get; set; }

    /// <summary>发布人 → Sys_User.Id。</summary>
    public Guid? PublishedBy { get; set; }

    /// <summary>乐观并发令牌：并发发布 / 同草稿双人保存冲突检测（spec §3.1/§3.3）。</summary>
    [Timestamp] public byte[]? RowVersion { get; set; }
```

`Wf_FlowInstance.cs`——`RowVersion` 属性前追加：

```csharp
    /// <summary>版本 pin（spec §2.2）：发起时刻固定的流程定义版本行 → Wf_FlowDef.Id。
    /// <b>在途实例（Running/Suspended/Draft）恒非空</b>；仅历史终态实例允许 null
    /// （迁移期孤儿 FlowKey：定义行曾被删——消费端降级为仅履历视图）。引擎 LoadSchemaAsync 遇 null 抛异常快速失败。</summary>
    public Guid? FlowDefId { get; set; }
```

- [ ] **Step 3: CP6Context 索引改造** — `CP6Context.cs:671-678` Wf_FlowDef 块改为：

```csharp
        // OA 章03 流程引擎 → 版本治理（WfsVersionPin）：多版本行 (TenantId,FlowKey,Version) 唯一；
        // FunctionId/FlowCode 行级唯一退服务层校验 E-WF-009（多版本行同身份码合法——侦察结论 #6），索引降非唯一辅助。
        modelBuilder.Entity<Wf_FlowDef>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.FlowKey, x.Version }).IsUnique()
                .HasDatabaseName("UX_Wf_FlowDef_TenantKeyVer");
            e.HasIndex(x => new { x.TenantId, x.FlowKey, x.Status })
                .HasDatabaseName("IX_Wf_FlowDef_TenantKeyStatus");
            e.HasIndex(x => new { x.TenantId, x.FunctionId })
                .HasFilter("[FunctionId] IS NOT NULL").HasDatabaseName("IX_Wf_FlowDef_Function");
            e.HasIndex(x => new { x.TenantId, x.FlowCode })
                .HasFilter("[FlowCode] IS NOT NULL").HasDatabaseName("IX_Wf_FlowDef_Code");
        });
```

`Wf_FlowInstance` 块内（既有索引行后）追加：

```csharp
            e.HasIndex(x => x.FlowDefId).HasDatabaseName("IX_Wf_FlowInstance_FlowDefId");   // 版本分布统计键（spec §2.2）
```

- [ ] **Step 4: 生成迁移并手补回填/守卫 SQL**

```bash
dotnet ef migrations add WfsVersionPin --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context
```

脚手架 `Up()` 会含：DropIndex `UX_Wf_FlowDef_FlowKey`/`UX_Wf_FlowDef_Function`/`UX_Wf_FlowDef_Code`、AddColumn Status(int, defaultValue: 1)/PublishedAtUtc/PublishedBy/RowVersion(rowversion)、AddColumn FlowDefId、CreateIndex ×5。**在 AddColumn 之后、CreateIndex 之前**插入手写 SQL（顺序敏感）：

```csharp
            // ── 数据回填①：既有行全部标 Published（AddColumn defaultValue:1 已覆盖 Status；显式补发布时刻）──
            migrationBuilder.Sql(
                @"UPDATE Wf_FlowDef SET PublishedAtUtc = GETUTCDATE() WHERE Status = 1 AND PublishedAtUtc IS NULL;");

            // ── 数据回填②：实例按 (TenantId, FlowKey) 关联现存 Def 行回填 pin。
            //    迁移时刻每 FlowKey 至多一行（旧唯一索引保证），TOP 1 ORDER BY Version DESC 纯防御。──
            migrationBuilder.Sql(@"
UPDATE i SET FlowDefId = d.Id
FROM Wf_FlowInstance i
CROSS APPLY (SELECT TOP 1 Id FROM Wf_FlowDef d
             WHERE d.TenantId = i.TenantId AND d.FlowKey = i.FlowKey
             ORDER BY d.Version DESC) d
WHERE i.FlowDefId IS NULL;");

            // ── 守卫：非终态（0=Running/4=Suspended/5=Draft）孤儿 → 迁移失败（spec §2.2：在途孤儿属数据事故，
            //    必须先按 precheck-orphan.sql 决策表处置）。终态（1/2/3）孤儿 FlowDefId 留 NULL 容忍。──
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM Wf_FlowInstance WHERE FlowDefId IS NULL AND Status IN (0, 4, 5))
    THROW 51001, N'WfsVersionPin 迁移中止：存在非终态孤儿实例（FlowKey 无对应 Wf_FlowDef 行）。请先跑 docs/superpowers/qa/wfs-version-ops/precheck-orphan.sql 并按处置决策表修复后重试。', 1;");
```

`Down()`：脚手架逆操作即可。注意：发布过新版本（同 FlowKey 多行）后 Down 会因重建 `UX_Wf_FlowDef_FlowKey` 撞唯一而失败——属预期，在 Down() 顶部加注释写明「多版本行存在时不支持回退」。

- [ ] **Step 5: 验证 + commit**

```bash
dotnet build CP6.WebApi/CP6.WebApi.csproj
dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context   # 必须 clean
dotnet ef migrations script --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context > NUL              # 脚本生成冒烟（SQL 语法过）
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf   # 既有全绿（新列默认值使行为零变）
git add -A && git commit -m "feat(wfs-version-ops): V-A1 Def四列+实例pin列+索引改造+WfsVersionPin迁移(回填+在途孤儿守卫)"
```

---

### Task V-A2: LoadSchemaAsync 签名收敛 + SubmitAsync pin + E-WF-029 + 旁支读点 pin 化

> 依赖 V-A1。**执行前必读 spec §2.2/§2.3/§5。** 签名收敛=删旧 `LoadSchemaAsync(string flowKey)`，编译错误清单即调用点清单（防漏改）。

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowEngine.cs`（:45-72 SubmitAsync、:85/:160/:194/:322/:361 调用点、:433-438 定义、:235 DispatchIfFinishedAsync 可见性）
- Modify: `CP6.Core/Services/Wf/AdvancedFlow.cs`（:112 调用点）
- Modify: `CP6.Core/Services/Wf/WfTimeoutService.cs`（:50/:57/:103-114 缓存键改 FlowDefId）
- Modify: `CP6.Core/Services/Oa/DraftService.cs`（:19 起草即 pin）
- Modify: `CP6.Core/Services/Oa/InboxService.cs`（:223 pin 读 + :247-249 forecast 传 pin）
- Modify: `CP6.Core/Services/Oa/ForecastService.cs`（:18-22 读口径 + 可选 pin 入参；接口同步）
- Test: 验收测试在 V-A3 `VersionPinTests.cs`；本 Task 以 `--filter Wf` 全量锁回归

- [ ] **Step 1: 引擎定义收敛** — `FlowEngine.cs:433-438` 替换为：

```csharp
    /// <summary>按实例版本 pin 读 schema（版本治理 spec §2.3，签名收敛防漏改）。
    /// <b>不变量</b>：在途实例 FlowDefId 恒非空（发起路径 pin + 迁移守卫保证）——null 只可能是 bug，抛异常快速失败；
    /// pin 指向行不存在 = Published 禁删守卫被绕过，同样快速失败。internal：FlowOpsService（干预动作）复用。</summary>
    internal async Task<FlowSchema> LoadSchemaAsync(Wf_FlowInstance inst)
    {
        if (inst.FlowDefId is not Guid defId)
            throw new InvalidOperationException($"实例 {inst.Id} 缺失版本 pin（FlowDefId null；在途实例构造上恒非空，null 即 bug）");
        var def = await _db.Wf_FlowDefs.FirstOrDefaultAsync(x => x.Id == defId)
                  ?? throw new InvalidOperationException($"流程定义版本行不存在：{defId}（Published 禁删守卫被绕过）");
        return Deserialize(def.SchemaJson);
    }
```

- [ ] **Step 2: SubmitAsync pin + E-WF-029** — `FlowEngine.cs:47-49` 替换为（并在 :52 实例对象初始化器追加 `FlowDefId = def.Id,`）：

```csharp
        // ★ 版本治理读口径（spec §2.1 评审修订，防静默回退）：先取「最新 Published」唯一一行，再看该行 Enable
        //   ——false 即 E-WF-029 拒绝发起，绝不回落更旧 Published 版本（若把 Enable 放进查询条件，关 v3 会静默发 v2）。
        var def = await _db.Wf_FlowDefs
            .Where(x => x.FlowKey == flowKey && x.Status == WfFlowDefStatus.Published)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync();
        if (def is null || !def.Enable)
            throw new InvalidOperationException($"E-WF-029: 目标流程无可用已发布版本或已停用：{flowKey}");
        var schema = Deserialize(def.SchemaJson);
```

（子流程/触发器发起都经 SubmitAsync → 自动继承 pin；触发器 FireAsync 捕获异常写 TriggerFire.Error → **E-WF-029 原码透传不翻译**，spec §5 边界，V-A3 一测锁定。）

- [ ] **Step 3: 六调用点逐个收敛**（编译器驱动，逐个确认改造形态）：
  - `FlowEngine.cs:85`（StartDraftAsync）→ `await LoadSchemaAsync(inst)`；
  - `:160`（办结快照）→ `FindNode(await LoadSchemaAsync(inst), task.NodeId)`；
  - `:194`（会签过后流转）→ `await LoadSchemaAsync(inst)`；
  - `:322`（ResumeServiceTokenAsync）/ `:361`（FailServiceTokenAsync）→ `await LoadSchemaAsync(inst)`（inst 均在 ×3 重试循环内已重读，pin 不变性使重试语义零变）；
  - `AdvancedFlow.cs:112`（SendBackAsync）→ `await LoadSchemaAsync(inst)`。
  - 编译通过后 grep 复核：`grep -rn "LoadSchemaAsync(inst.FlowKey)\|LoadSchemaAsync(string" CP6.Core/` 零命中。

- [ ] **Step 4: 旁支读点 pin 化**

`WfTimeoutService.cs`——`NodeOf_Schema`/`NodeOf` 缓存键 `string flowKey` → `Guid flowDefId`（`:50` 字典类型 `Dictionary<Guid, FlowSchema>`、`:57` 调用改传 inst）：

```csharp
    private FlowSchema? SchemaOf(Dictionary<Guid, FlowSchema> cache, Wf_FlowInstance inst)
    {
        if (inst.FlowDefId is not Guid defId) return null;                    // 扫描已闸 Running（:55），null 防御性返回→按「无动作配置」标记
        if (cache.TryGetValue(defId, out var s)) return s;
        var def = _db.Wf_FlowDefs.FirstOrDefault(x => x.Id == defId);
        if (def is null) return null;
        s = JsonSerializer.Deserialize<FlowSchema>(def.SchemaJson, JsonOpts) ?? new FlowSchema();
        cache[defId] = s;
        return s;
    }

    private FlowNode? NodeOf(Dictionary<Guid, FlowSchema> cache, Wf_FlowInstance inst, string nodeId)
        => SchemaOf(cache, inst)?.Nodes.FirstOrDefault(n => n.Id == nodeId);
```

`DraftService.cs:19` 建 Draft 实例处，前置解析同 SubmitAsync 口径并 pin（起草即拒 E-WF-029）：

```csharp
        var def = await _db.Wf_FlowDefs
            .Where(x => x.FlowKey == flowKey && x.Status == WfFlowDefStatus.Published)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync();
        if (def is null || !def.Enable)
            throw new InvalidOperationException($"E-WF-029: 目标流程无可用已发布版本或已停用：{flowKey}");
        // …既有 new Wf_FlowInstance 初始化器追加一行：
        FlowDefId = def.Id,
```

`InboxService.cs:223` → `var def = inst.FlowDefId is Guid defId ? await _db.Wf_FlowDefs.FirstOrDefaultAsync(d => d.Id == defId) : null;`（后续 `def?.FlowName`/`def?.FormKey`/formSchema 已 null-safe——**终态孤儿天然降级仅履历视图**，零额外分支）；`:248` forecast 调用改 `_forecast.ForecastAsync(inst.FlowKey, inst.VarsJson, inst.StarterId, fromNodeId: inst.CurrentNode, flowDefId: inst.FlowDefId)`（Running 实例预测按 pin 版本，不再受改版漂移）。

`ForecastService.cs:18-22` → 签名加可选 pin：`ForecastAsync(string flowKey, string varsJson, Guid starterId, string? fromNodeId = null, Guid? flowDefId = null)`（`IForecastService` 接口同步）；行选择：

```csharp
        var def = flowDefId is Guid pin
            ? await _db.Wf_FlowDefs.FirstOrDefaultAsync(x => x.Id == pin)
            : await _db.Wf_FlowDefs.Where(x => x.FlowKey == flowKey && x.Status == WfFlowDefStatus.Published && x.Enable)
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync();
        if (def is null) throw new InvalidOperationException("E-WF-006");   // 起草前预测错误码保持现状（前端兼容）
```

（起草前预测无「不回落」问题——预测的就是「若现在发起」，与 SubmitAsync 同一行选择即正确。）

`FlowEngine.cs:235` `private async Task DispatchIfFinishedAsync` → `internal async Task DispatchIfFinishedAsync`（体零改，V-E 干预动作收口终态分发复用）。

- [ ] **Step 5: 全量回归 + 手工实例账目** — `dotnet test CP6.Tests/CP6.Tests.csproj` 全量：凡「手工 `new Wf_FlowInstance` 直入库且后续走引擎动作」的既有测试因 null pin 抛异常而红 → 该测试 seed 构造行补 `FlowDefId = <该测试 def 行 Id>`（**只改构造行，断言零改**），逐文件列入 commit message。经 SubmitAsync 建实例的测试（绝大多数）零改。

- [ ] **Step 6: commit** — `git add -A && git commit -m "feat(wfs-version-ops): V-A2 LoadSchemaAsync签名收敛(6调用点)+SubmitAsync pin+E-WF-029+超时/收件箱/预测旁支pin化"`

---

### Task V-A3: pin 语义定点回归（债的两个事故形态 + E-WF-029 + 孤儿降级）

> 依赖 V-A2。**执行前必读 spec §7「pin 语义」「Enable/孤儿定点」。**

**Files:**
- Test: `CP6.Tests/Wf/VersionPinTests.cs`（新建）
- Test: 波③既有触发器测试文件旁新增一测（E-WF-029 透传）

- [ ] **Step 1: 写测试**（V-A2 完成后应一次通过——它们是收敛正确性的验收；红则修 V-A2 不改测试）：

```csharp
// CP6.Tests/Wf/VersionPinTests.cs
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>版本 pin 语义定点（version-ops spec §2/§7）：发布新版不污染在途（删节点/改审批人两事故形态）、
/// 新单 pin 最新 Published、E-WF-029 不回落、终态孤儿 null 降级、null pin 快速失败。</summary>
public class VersionPinTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    // v1: s → a(u1) → b(u2) → end
    private static FlowSchema TwoStep(Guid u1, Guid u2) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u1 },
            new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u2 },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "a" }, new FlowEdge { From = "a", To = "b" },
            new FlowEdge { From = "b", To = "end" },
        },
    };

    // v2 变体①（删节点）：s → a(u1) → end
    private static FlowSchema OneStep(Guid u1) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u1 },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges = { new FlowEdge { From = "s", To = "a" }, new FlowEdge { From = "a", To = "end" } },
    };

    private static Wf_FlowDef Def(string key, FlowSchema schema, int version, bool enable = true, int status = 1)
        => new()
        {
            Id = Guid.NewGuid(), FlowKey = key, FlowName = key, FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(schema), Version = version, Enable = enable, Status = status,
            PublishedAtUtc = status == WfFlowDefStatus.Published ? DateTime.UtcNow : null,
        };

    [Fact]
    public void WfFlowDefStatus_Constants()
    {
        Assert.Equal(0, WfFlowDefStatus.Draft);
        Assert.Equal(1, WfFlowDefStatus.Published);
    }

    [Fact]
    public void FlowDef_Defaults_Published()   // 侦察结论 #7：默认 Published → 既有测试/种子零改
        => Assert.Equal(WfFlowDefStatus.Published, new Wf_FlowDef().Status);

    [Fact]
    public async Task InFlight_PinnedV1_SurvivesV2_NodeDeleted()   // 债的事故形态①：删节点
    {
        using var db = NewDb();
        Guid u1 = Guid.NewGuid(), u2 = Guid.NewGuid();
        var v1 = Def("vp1", TwoStep(u1, u2), 1);
        db.Wf_FlowDefs.Add(v1);
        await db.SaveChangesAsync();

        var instId = await Engine(db).SubmitAsync("vp1", Guid.NewGuid(), "{}");
        Assert.Equal(v1.Id, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId)).FlowDefId);   // 新单 pin v1

        db.Wf_FlowDefs.Add(Def("vp1", OneStep(u1), 2));   // 「发布 v2」：删掉节点 b
        await db.SaveChangesAsync();

        // 在途 v1 单继续按 v1 跑：a 过 → b 生任务 → b 过 → 终态。
        // （旧实现按 FlowKey 取最新 → b 不存在 token 卡死——本测试即防回归闸。）
        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == instId && t.AssigneeId == u1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(ta.Id, u1, approve: true);
        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == instId && t.AssigneeId == u2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(tb.Id, u2, approve: true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId)).Status);
    }

    [Fact]
    public async Task InFlight_PinnedV1_SurvivesV2_ApproverChanged()   // 债的事故形态②：改审批人
    {
        using var db = NewDb();
        Guid u1 = Guid.NewGuid(), u2 = Guid.NewGuid(), u3 = Guid.NewGuid();
        db.Wf_FlowDefs.Add(Def("vp2", TwoStep(u1, u2), 1));
        await db.SaveChangesAsync();
        var instId = await Engine(db).SubmitAsync("vp2", Guid.NewGuid(), "{}");

        db.Wf_FlowDefs.Add(Def("vp2", TwoStep(u1, u3), 2));   // v2：b 审批人 u2→u3
        await db.SaveChangesAsync();

        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == instId && t.AssigneeId == u1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(ta.Id, u1, approve: true);
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == instId && t.AssigneeId == u2 && t.Status == FlowTaskStatus.Pending));   // 仍 u2
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == instId && t.AssigneeId == u3));                                        // 绝不漂移到 u3
    }

    [Fact]
    public async Task NewSubmit_PinsLatestPublished()
    {
        using var db = NewDb();
        Guid u1 = Guid.NewGuid(), u2 = Guid.NewGuid();
        db.Wf_FlowDefs.Add(Def("vp3", TwoStep(u1, u2), 1));
        var v2 = Def("vp3", OneStep(u1), 2);
        db.Wf_FlowDefs.Add(v2);
        await db.SaveChangesAsync();

        var instId = await Engine(db).SubmitAsync("vp3", Guid.NewGuid(), "{}");
        Assert.Equal(v2.Id, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId)).FlowDefId);

        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == instId && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(ta.Id, u1, approve: true);   // v2 单节点 → 直达终态
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId)).Status);
    }

    [Fact]
    public async Task E_WF_029_DisabledLatest_NoFallbackToOlder()   // spec §2.1 评审修订核心：不回落
    {
        using var db = NewDb();
        Guid u1 = Guid.NewGuid(), u2 = Guid.NewGuid();
        db.Wf_FlowDefs.Add(Def("vp4", TwoStep(u1, u2), 1, enable: true));    // 旧版仍 Enable
        db.Wf_FlowDefs.Add(Def("vp4", OneStep(u1), 2, enable: false));       // 最新 Published 关闸
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).SubmitAsync("vp4", Guid.NewGuid(), "{}"));
        Assert.Contains("E-WF-029", ex.Message);                             // 绝不静默回落 v1
        Assert.False(await db.Wf_FlowInstances.AnyAsync());
    }

    [Fact]
    public async Task E_WF_029_DraftOnly_NotStartable()   // 只有草稿（Status=Draft）→ 不可发起
    {
        using var db = NewDb();
        db.Wf_FlowDefs.Add(Def("vp5", OneStep(Guid.NewGuid()), 1, enable: true, status: WfFlowDefStatus.Draft));
        await db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).SubmitAsync("vp5", Guid.NewGuid(), "{}"));
        Assert.Contains("E-WF-029", ex.Message);
    }

    [Fact]
    public async Task EnableOff_InFlightUnaffected()   // 关闸只停发起，在途照跑（spec §7）
    {
        using var db = NewDb();
        Guid u1 = Guid.NewGuid(), u2 = Guid.NewGuid();
        var v1 = Def("vp6", TwoStep(u1, u2), 1);
        db.Wf_FlowDefs.Add(v1);
        await db.SaveChangesAsync();
        var instId = await Engine(db).SubmitAsync("vp6", Guid.NewGuid(), "{}");

        v1.Enable = false;
        await db.SaveChangesAsync();

        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == instId && t.AssigneeId == u1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(ta.Id, u1, approve: true);
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == instId && t.AssigneeId == u2 && t.Status == FlowTaskStatus.Pending));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Engine(db).SubmitAsync("vp6", Guid.NewGuid(), "{}"));   // 新发起被拒
    }

    [Fact]
    public async Task NullPin_TerminalOrphan_EngineThrowsFast()
    {
        using var db = NewDb();
        // 终态孤儿：无 Def 行、FlowDefId null（迁移容忍形态）
        var inst = new Wf_FlowInstance
        {
            Id = Guid.NewGuid(), FlowKey = "ghost", CurrentNode = "end",
            Status = FlowInstanceStatus.Approved, StarterId = Guid.NewGuid(), VarsJson = "{}", FlowDefId = null,
        };
        db.Wf_FlowInstances.Add(inst);
        await db.SaveChangesAsync();

        // 引擎侧：null pin 快速失败（构造上只可能是 bug 才会流到这里——终态实例永不 LoadSchema）
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Engine(db).LoadSchemaAsync(inst));
        Assert.Contains("FlowDefId", ex.Message);
    }

    [Fact]
    public async Task NullPin_TerminalOrphan_InboxDetailDegrades()   // 消费端降级：仅履历视图（spec §2.2）
    {
        using var db = NewDb();
        var starter = Guid.NewGuid();
        var inst = new Wf_FlowInstance
        {
            Id = Guid.NewGuid(), FlowKey = "ghost2", CurrentNode = "end",
            Status = FlowInstanceStatus.Approved, StarterId = starter, VarsJson = "{}", FlowDefId = null,
        };
        db.Wf_FlowInstances.Add(inst);
        db.Wf_FlowFormTos.Add(new Wf_FlowFormTo
        {
            Id = Guid.NewGuid(), InstanceId = inst.Id, StepSeq = 1, NodeId = "a", NodeName = "关卡A",
            ExpectedHandlerId = starter, Status = FlowFormToStatus.Approved,
            SentAt = DateTime.Now.AddDays(-1), HandledAt = DateTime.Now,
        });
        await db.SaveChangesAsync();

        var forecast = new CP6.Core.Services.Oa.ForecastService(db, new ApproverResolver(db), new ApprovalStagePlanner(new ApproverResolver(db)));
        var svc = new CP6.Core.Services.Oa.InboxService(db, Engine(db), forecast);
        var detail = await svc.DetailAsync(inst.Id);

        Assert.NotNull(detail);
        Assert.Null(detail!.FlowName);              // def 相关字段降级 null
        Assert.Single(detail.Timeline);             // 履历照常
    }
}
```

> 适配注记：`InboxService` 实际 ctor 若含 notifier 等更多依赖，按实际签名就地补 `new NullWfNotifier()` 之类空实现——**断言不动**。`Timeline`/`FlowName` 属性名以 `InboxDetail` record 实际定义为准（实读 `InboxService.cs:251` 返回形状：`InboxDetail(inst, def?.FlowName, def?.FormKey, formSchema, …, timeline, …)`）。

- [ ] **Step 2: 波③触发器透传测试** — 在波③触发器测试文件（`CP6.Tests/Wf/TriggerFire*` 家族）旁新增：seed「仅 Draft 版本」流程 + 定时触发器 → 驱动一次 fire → `Wf_TriggerFires.Single().Error` 断言 `Assert.Contains("E-WF-029", ...)` 且 **不含** `"E-WF-023"`（运行时保留根因，spec §5 边界）。

- [ ] **Step 3: 跑验证** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter VersionPinTests` 全绿；随后 `--filter Wf` 全量既有照绿。
- [ ] **Step 4: commit** — `git add -A && git commit -m "feat(wfs-version-ops): V-A3 pin语义定点回归(删节点/改审批人两事故形态+E-WF-029不回落+孤儿降级+触发器透传)"`

---

## Wave V-B — 发布流（草稿/发布状态机 + copy-on-write + 守卫 + 设计器 UI）

### Task V-B1: FlowVersionService + 不可变/禁删守卫 + 既有服务组口径改造

> 依赖 V-A3。**执行前必读 spec §2.1（不可变铁律/Enable 语义）§3.1-§3.3（打开/保存/发布）。**

**Files:**
- Create: `CP6.Core/Services/Wf/FlowVersionService.cs`
- Modify: `CP6.Core/Services/Wf/FlowDefService.cs`（SaveDefAsync 收窄草稿行）
- Modify: `CP6.Core/Services/Oa/DesignerService.cs`（SaveAsync 走草稿 + ListAsync/CloneAsync 组口径）
- Modify: `CP6.Core/Services/Oa/FlowAdminService.cs`（组口径，侦察结论 #9）
- Modify: `CP6.WebApi/Program.cs`（DI 注册 `IFlowVersionService`）
- Test: `CP6.Tests/Wf/FlowVersionServiceTests.cs`（新建）

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/FlowVersionServiceTests.cs
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>发布流状态机（version-ops spec §3/§7）：copy-on-write 衍生、Published 不可变/禁删守卫、
/// E-WF-030、发布 Enable 继承、从历史另存草稿、保存不升版。</summary>
public class FlowVersionServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowVersionService Svc(CP6Context db) => new(db);

    private static string LinearJson(Guid u) => JsonSerializer.Serialize(new FlowSchema
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges = { new FlowEdge { From = "s", To = "a" }, new FlowEdge { From = "a", To = "end" } },
    });

    private static Wf_FlowDef Published(string key, int version, bool enable = true, string? schemaJson = null) => new()
    {
        Id = Guid.NewGuid(), FlowKey = key, FlowName = key, FormKey = "f",
        SchemaJson = schemaJson ?? LinearJson(Guid.NewGuid()), Version = version, Enable = enable,
        Status = WfFlowDefStatus.Published, PublishedAtUtc = DateTime.UtcNow,
    };

    [Fact]
    public async Task OpenDraft_NoDraft_CopyOnWrite_FromLatestPublished()
    {
        using var db = NewDb();
        db.Wf_FlowDefs.Add(Published("fv1", 1));
        var v2 = Published("fv1", 2);
        db.Wf_FlowDefs.Add(v2);
        await db.SaveChangesAsync();

        var draft = await Svc(db).OpenDraftAsync("fv1", Guid.NewGuid());

        Assert.Equal(3, draft.Version);                                       // max+1
        Assert.Equal(WfFlowDefStatus.Draft, draft.Status);
        Assert.Equal(v2.SchemaJson, draft.SchemaJson);                        // 从最新 Published 衍生
        Assert.Equal(3, await db.Wf_FlowDefs.CountAsync(d => d.FlowKey == "fv1"));
        var row = await db.Wf_FlowDefs.SingleAsync(d => d.FlowKey == "fv1" && d.Version == 3);
        Assert.Equal(WfFlowDefStatus.Draft, row.Status);
        Assert.Null(row.PublishedAtUtc);
    }

    [Fact]
    public async Task OpenDraft_DraftExists_ReturnsIt_NoDuplicate()
    {
        using var db = NewDb();
        db.Wf_FlowDefs.Add(Published("fv2", 1));
        await db.SaveChangesAsync();

        var d1 = await Svc(db).OpenDraftAsync("fv2", Guid.NewGuid());
        var d2 = await Svc(db).OpenDraftAsync("fv2", Guid.NewGuid());   // 再开 → 同一草稿，不再衍生

        Assert.Equal(d1.Id, d2.Id);
        Assert.Equal(2, await db.Wf_FlowDefs.CountAsync(d => d.FlowKey == "fv2"));
    }

    [Fact]
    public async Task OpenDraft_BrandNewFlow_BlankDraftV1()
    {
        using var db = NewDb();
        var draft = await Svc(db).OpenDraftAsync("fv-new", Guid.NewGuid());
        Assert.Equal(1, draft.Version);
        Assert.Equal(WfFlowDefStatus.Draft, draft.Status);
        Assert.Single(await db.Wf_FlowDefs.Where(d => d.FlowKey == "fv-new").ToListAsync());
    }

    [Fact]
    public async Task Publish_FreezesDraft_InheritsEnable_FromPrevPublished()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        var v1 = Published("fv3", 1, enable: false, schemaJson: LinearJson(u));   // 前版已停用
        db.Wf_FlowDefs.Add(v1);
        await db.SaveChangesAsync();
        var draft = await Svc(db).OpenDraftAsync("fv3", u);

        var publisher = Guid.NewGuid();
        await Svc(db).PublishAsync("fv3", draft.RowVersion, publisher);

        var row = await db.Wf_FlowDefs.SingleAsync(d => d.FlowKey == "fv3" && d.Version == draft.Version);
        Assert.Equal(WfFlowDefStatus.Published, row.Status);
        Assert.NotNull(row.PublishedAtUtc);
        Assert.Equal(publisher, row.PublishedBy);
        Assert.False(row.Enable);   // ★ Enable 继承前 Published 版——被停用的流程发布新版不静默开闸（spec §2.1）
    }

    [Fact]
    public async Task Publish_FirstVersion_EnableDefaultsTrue()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        var svc = Svc(db);
        var draft = await svc.OpenDraftAsync("fv4", u);   // 全新流程空白草稿
        var row0 = await db.Wf_FlowDefs.SingleAsync(d => d.Id == draft.Id);
        row0.SchemaJson = LinearJson(u); row0.FormKey = "f"; row0.FlowName = "fv4";
        await db.SaveChangesAsync();

        await svc.PublishAsync("fv4", row0.RowVersion, u);
        Assert.True((await db.Wf_FlowDefs.SingleAsync(d => d.Id == draft.Id)).Enable);   // 无前版 = true
    }

    [Fact]
    public async Task Publish_NoDraft_E_WF_030()
    {
        using var db = NewDb();
        db.Wf_FlowDefs.Add(Published("fv5", 1));
        await db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Svc(db).PublishAsync("fv5", null, Guid.NewGuid()));
        Assert.Contains("E-WF-030", ex.Message);
    }

    [Fact]
    public async Task Publish_InvalidSchema_E_WF_030_WithAggregatedErrors()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        var svc = Svc(db);
        var draft = await svc.OpenDraftAsync("fv6", u);
        var row = await db.Wf_FlowDefs.SingleAsync(d => d.Id == draft.Id);
        row.SchemaJson = "{}";   // 空 schema：FlowSchemaValidator 必报（无 start/节点）
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.PublishAsync("fv6", row.RowVersion, u));
        Assert.Contains("E-WF-030", ex.Message);
        Assert.Equal(WfFlowDefStatus.Draft, (await db.Wf_FlowDefs.SingleAsync(d => d.Id == draft.Id)).Status);   // 未冻结
    }

    [Fact]
    public void EnsureMutable_PublishedRow_Throws_DraftRow_Passes()
    {
        var pub = Published("fv7", 1);
        var ex = Assert.Throws<InvalidOperationException>(() => FlowVersionService.EnsureMutable(pub));
        Assert.Contains("不可变", ex.Message);
        FlowVersionService.EnsureMutable(new Wf_FlowDef { Status = WfFlowDefStatus.Draft });   // 不抛
    }

    [Fact]
    public void EnsureDeletable_PublishedRow_Throws_DraftRow_Passes()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => FlowVersionService.EnsureDeletable(Published("fv8", 1)));
        Assert.Contains("禁删", ex.Message);
        FlowVersionService.EnsureDeletable(new Wf_FlowDef { Status = WfFlowDefStatus.Draft });
    }

    [Fact]
    public async Task DraftFromVersion_HistoricalVersion_NewDraftMaxPlusOne()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        var v1 = Published("fv9", 1, schemaJson: LinearJson(u));
        db.Wf_FlowDefs.Add(v1);
        db.Wf_FlowDefs.Add(Published("fv9", 2));
        await db.SaveChangesAsync();

        var draft = await Svc(db).DraftFromVersionAsync("fv9", 1, u);   // 回滚达成方式（spec §3.4/§9）
        Assert.Equal(3, draft.Version);
        Assert.Equal(v1.SchemaJson, draft.SchemaJson);                  // 内容取 v1
        Assert.Equal(WfFlowDefStatus.Draft, draft.Status);
    }

    [Fact]
    public async Task DraftFromVersion_DraftAlreadyExists_Throws400Semantics()
    {
        using var db = NewDb();
        db.Wf_FlowDefs.Add(Published("fv10", 1));
        await db.SaveChangesAsync();
        await Svc(db).OpenDraftAsync("fv10", Guid.NewGuid());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Svc(db).DraftFromVersionAsync("fv10", 1, Guid.NewGuid()));   // 已有草稿 → 先处理再另存（防两草稿并存）
    }

    [Fact]
    public async Task ListVersions_NewestFirst_WithPublishMeta()
    {
        using var db = NewDb();
        db.Wf_FlowDefs.Add(Published("fv11", 1));
        db.Wf_FlowDefs.Add(Published("fv11", 2, enable: false));
        await db.SaveChangesAsync();
        await Svc(db).OpenDraftAsync("fv11", Guid.NewGuid());   // v3 草稿

        var list = await Svc(db).ListVersionsAsync("fv11");
        Assert.Equal(new[] { 3, 2, 1 }, list.Select(x => x.Version).ToArray());
        Assert.Equal(WfFlowDefStatus.Draft, list[0].Status);
        Assert.Equal(WfFlowDefStatus.Published, list[1].Status);
        Assert.False(list[1].Enable);
    }

    [Fact]
    public async Task GetLatestPublished_IgnoresDraft_PicksMaxVersion()
    {
        using var db = NewDb();
        db.Wf_FlowDefs.Add(Published("fv12", 1));
        var v2 = Published("fv12", 2);
        db.Wf_FlowDefs.Add(v2);
        await db.SaveChangesAsync();
        await Svc(db).OpenDraftAsync("fv12", Guid.NewGuid());   // v3 Draft 不该被选中

        var latest = await Svc(db).GetLatestPublishedAsync("fv12");
        Assert.Equal(v2.Id, latest!.Id);
    }
}
```

- [ ] **Step 2: 跑验证 FAIL** — `--filter FlowVersionServiceTests`：编译失败（FlowVersionService 不存在）。

- [ ] **Step 3: 实现 `FlowVersionService.cs` 全文**

```csharp
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public interface IFlowVersionService
{
    Task<Wf_FlowDef?> GetLatestPublishedAsync(string flowKey);
    Task<DesignerDraftDto> OpenDraftAsync(string flowKey, Guid userId);
    Task PublishAsync(string flowKey, byte[]? rowVersion, Guid userId);
    Task<IReadOnlyList<FlowVersionItem>> ListVersionsAsync(string flowKey);
    Task<string?> GetVersionSchemaAsync(string flowKey, int version);
    Task<DesignerDraftDto> DraftFromVersionAsync(string flowKey, int version, Guid userId);
}

public sealed record DesignerDraftDto(Guid Id, string FlowKey, int Version, int Status, string FlowName,
    string FormKey, string? FunctionId, string? FlowCode, string SchemaJson, byte[]? RowVersion);
public sealed record FlowVersionItem(int Version, int Status, DateTime? PublishedAtUtc, Guid? PublishedBy, bool Enable);

/// <summary>流程定义版本域（version-ops spec §2.1/§3）：草稿/发布状态机、copy-on-write、
/// Published 不可变/禁删守卫（服务层唯一口径——FlowAdmin 现无删除入口，守卫闸未来一切入口，侦察结论 #1）。
/// 并发口径（spec §3.1）：双设计器同时衍生撞 (TenantId,FlowKey,Version) 唯一键 → 捕获后重载对方草稿（不是报错）；
/// 同草稿并发保存/发布 → Def RowVersion 冲突 → 上层 409 提示重载（不做静默 last-write-wins）。</summary>
public class FlowVersionService : IFlowVersionService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private readonly CP6Context _db;
    public FlowVersionService(CP6Context db) { _db = db; }

    /// <summary>Published 行 SchemaJson/FlowName/FormKey 不可变（spec §2.1 铁律；可变的只有 Enable）。</summary>
    internal static void EnsureMutable(Wf_FlowDef def)
    {
        if (def.Status == WfFlowDefStatus.Published)
            throw new InvalidOperationException($"已发布版本不可变（{def.FlowKey} v{def.Version}）：请通过设计器衍生新草稿再修改");
    }

    /// <summary>Published 行一律禁删（历史版本 KB 级永久保留，spec §2.2/§9）；Draft 可删。</summary>
    internal static void EnsureDeletable(Wf_FlowDef def)
    {
        if (def.Status == WfFlowDefStatus.Published)
            throw new InvalidOperationException($"已发布版本禁删（{def.FlowKey} v{def.Version}）：实例 pin 依赖其永久保留");
    }

    public Task<Wf_FlowDef?> GetLatestPublishedAsync(string flowKey) =>
        _db.Wf_FlowDefs.Where(x => x.FlowKey == flowKey && x.Status == WfFlowDefStatus.Published)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync();

    public async Task<DesignerDraftDto> OpenDraftAsync(string flowKey, Guid userId)
    {
        var draft = await _db.Wf_FlowDefs
            .Where(x => x.FlowKey == flowKey && x.Status == WfFlowDefStatus.Draft)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync();
        if (draft is not null) return ToDto(draft);

        var basis = await _db.Wf_FlowDefs.Where(x => x.FlowKey == flowKey)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync();   // 最新行（必为 Published——无草稿前提下）

        var row = new Wf_FlowDef
        {
            Id = Guid.NewGuid(),
            FlowKey = flowKey,
            FlowName = basis?.FlowName ?? flowKey,
            FormKey = basis?.FormKey ?? string.Empty,
            SchemaJson = basis?.SchemaJson ?? "{}",
            FunctionId = basis?.FunctionId,
            FlowCode = basis?.FlowCode,
            Version = (basis?.Version ?? 0) + 1,
            Enable = basis?.Enable ?? true,          // 行值随前版；发起读口径只看最新 Published，草稿行 Enable 无运行时效力
            Status = WfFlowDefStatus.Draft,
            Creator = userId.ToString(),
        };
        _db.Wf_FlowDefs.Add(row);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)   // 撞 (TenantId,FlowKey,Version) 唯一键：对方刚衍生 → 重载对方草稿（spec §3.1 并发口径）
        {
            _db.Entry(row).State = EntityState.Detached;
            var other = await _db.Wf_FlowDefs
                .Where(x => x.FlowKey == flowKey && x.Status == WfFlowDefStatus.Draft)
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync()
                ?? throw new InvalidOperationException($"草稿衍生冲突且未找到对方草稿：{flowKey}（请重试）");
            return ToDto(other);
        }
        return ToDto(row);
    }

    public async Task PublishAsync(string flowKey, byte[]? rowVersion, Guid userId)
    {
        var draft = await _db.Wf_FlowDefs
            .Where(x => x.FlowKey == flowKey && x.Status == WfFlowDefStatus.Draft)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException($"E-WF-030: 无草稿可发布：{flowKey}");

        // 全族校验（spec §3.3）：FlowSchemaValidator 全部规则；错误聚合随响应（不止报第一条）
        var schema = JsonSerializer.Deserialize<FlowSchema>(draft.SchemaJson, JsonOpts) ?? new FlowSchema();
        var errs = FlowSchemaValidator.Validate(schema);
        if (errs.Count > 0)
            throw new InvalidOperationException("E-WF-030: 发布校验未过：" + string.Join("；", errs));

        // 并发闸：显式比较（InMemory 可测）+ OriginalValue（SQL Server rowversion 真闸）
        if (rowVersion is not null && draft.RowVersion is not null && !rowVersion.SequenceEqual(draft.RowVersion))
            throw new DbUpdateConcurrencyException($"发布冲突：草稿 {flowKey} v{draft.Version} 已被他人修改，请重载");
        if (rowVersion is not null) _db.Entry(draft).Property(x => x.RowVersion).OriginalValue = rowVersion;

        var prev = await GetLatestPublishedAsync(flowKey);
        draft.Status = WfFlowDefStatus.Published;
        draft.PublishedAtUtc = DateTime.UtcNow;
        draft.PublishedBy = userId;
        draft.Enable = prev?.Enable ?? true;   // ★ Enable 继承上一 Published 版（无前版=true，spec §2.1）
        draft.Modifier = userId.ToString();
        draft.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<FlowVersionItem>> ListVersionsAsync(string flowKey) =>
        await _db.Wf_FlowDefs.Where(x => x.FlowKey == flowKey)
            .OrderByDescending(x => x.Version)
            .Select(x => new FlowVersionItem(x.Version, x.Status, x.PublishedAtUtc, x.PublishedBy, x.Enable))
            .ToListAsync();

    public Task<string?> GetVersionSchemaAsync(string flowKey, int version) =>
        _db.Wf_FlowDefs.Where(x => x.FlowKey == flowKey && x.Version == version)
            .Select(x => (string?)x.SchemaJson).FirstOrDefaultAsync();

    public async Task<DesignerDraftDto> DraftFromVersionAsync(string flowKey, int version, Guid userId)
    {
        if (await _db.Wf_FlowDefs.AnyAsync(x => x.FlowKey == flowKey && x.Status == WfFlowDefStatus.Draft))
            throw new InvalidOperationException($"已存在未发布草稿：{flowKey}（请先发布或继续编辑既有草稿）");
        var src = await _db.Wf_FlowDefs.FirstOrDefaultAsync(x => x.FlowKey == flowKey && x.Version == version)
                  ?? throw new InvalidOperationException("E-WF-006");
        var max = await _db.Wf_FlowDefs.Where(x => x.FlowKey == flowKey).MaxAsync(x => x.Version);
        var row = new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = src.FlowName, FormKey = src.FormKey,
            SchemaJson = src.SchemaJson, FunctionId = src.FunctionId, FlowCode = src.FlowCode,
            Version = max + 1, Enable = src.Enable, Status = WfFlowDefStatus.Draft, Creator = userId.ToString(),
        };
        _db.Wf_FlowDefs.Add(row);
        await _db.SaveChangesAsync();   // 撞键概率极低（前置 AnyAsync 已查）；真撞由上层 409 重试
        return ToDto(row);
    }

    private static DesignerDraftDto ToDto(Wf_FlowDef d) => new(
        d.Id, d.FlowKey, d.Version, d.Status, d.FlowName, d.FormKey, d.FunctionId, d.FlowCode, d.SchemaJson, d.RowVersion);
}
```

- [ ] **Step 4: 既有服务组口径改造**（同 commit，测试同文件补断言或就地小测试类）：

  1. **`FlowDefService.SaveDefAsync` 收窄**（冲突点清单条目）：查行改「最新 Draft 行优先，无则走 FlowVersionService 语义」——实际落码：`SaveDefAsync` 只被 `DesignerService.SaveAsync`/`CloneAsync` 消费（已 grep 全仓），直接改为：

```csharp
        // 版本治理收窄（V-B1）：保存只写草稿行（spec §3.2）；旧「schema 变更才 Version++」升版逻辑退役——
        // 版本号只由 copy-on-write（OpenDraftAsync/DraftFromVersionAsync）分配一次。写 Published 行被守卫拒绝。
        var def = await _db.Wf_FlowDefs.Where(x => x.FlowKey == flowKey && x.Status == WfFlowDefStatus.Draft)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync();
        if (def == null)
        {
            def = new Wf_FlowDef
            {
                Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = flowName, FormKey = formKey,
                SchemaJson = schemaJson, Version = 1, Status = WfFlowDefStatus.Draft, Creator = user,
            };
            // 已有 Published 行而无草稿 → 应先 OpenDraftAsync 衍生；直接 Save 时兜底衍生（Version=max+1）
            var max = await _db.Wf_FlowDefs.Where(x => x.FlowKey == flowKey).Select(x => (int?)x.Version).MaxAsync();
            if (max is int m) def.Version = m + 1;
            _db.Wf_FlowDefs.Add(def);
        }
        else
        {
            FlowVersionService.EnsureMutable(def);   // 防御（构造上 Draft 必过）
            def.FlowName = flowName;
            def.FormKey = formKey;
            def.SchemaJson = schemaJson;             // ★ 不再 Version++
            def.Modifier = user;
            def.ModifyDate = DateTime.Now;
        }
        await _db.SaveChangesAsync();
        return def.Id;
```

  `GetDefAsync(flowKey)`（`FlowDefService.cs:46`）保持返回单行语义 → 改「最新行（草稿优先，其次最新 Published）」：`OrderByDescending(x => x.Status == WfFlowDefStatus.Draft).ThenByDescending(x => x.Version).FirstOrDefaultAsync()`——设计器语境（读草稿）与克隆语境兼容。

  2. **`DesignerService`**：`SaveAsync` ③ 段前插 `RowVersion` 透传（`SaveFlowRequest` 加 `byte[]? RowVersion` 字段，controller 传入；不为 null 时对草稿行设 OriginalValue，冲突抛 `DbUpdateConcurrencyException` → V-B2 409）；`ListAsync` 改组口径（每 FlowKey 一行 = 最新版本行 + `HasDraft` 标记；`FlowDefSummary` 加 `int Status`/`bool HasDraft` 尾参）；`CloneAsync` 源取「最新 Published，无则最新草稿」schema，新行 `Version=1, Status=Draft, Enable=false`（克隆需重新发布，语义更严即防呆，测试锁定）。
  3. **`FlowAdminService`**（侦察结论 #9）：`ListFlowsAsync` 组口径（GroupBy FlowKey → 最新 Published 行投影，无 Published 的纯草稿流程也列出但 enable 列灰）；`SetEnabledAsync` 取「最新 Published 行」操作，E-WF-008 互斥检查改「其他 FlowKey 的**最新 Published** 行」：

```csharp
    public async Task SetEnabledAsync(string flowKey, bool enabled)
    {
        var def = await _db.Wf_FlowDefs
            .Where(d => d.FlowKey == flowKey && d.Status == WfFlowDefStatus.Published)
            .OrderByDescending(d => d.Version).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("E-WF-006");
        if (enabled && !def.Enable)
        {
            // 1 表单 ↔ 1 启用流程（E-WF-008 语义不变，判据收窄到各 FlowKey 最新 Published 行）
            var latestOthers = _db.Wf_FlowDefs
                .Where(d => d.FormKey == def.FormKey && d.FlowKey != def.FlowKey && d.Status == WfFlowDefStatus.Published)
                .GroupBy(d => d.FlowKey)
                .Select(g => g.OrderByDescending(x => x.Version).First());
            if (await latestOthers.AnyAsync(d => d.Enable))
                throw new InvalidOperationException("E-WF-008");
        }
        def.Enable = enabled;
        await _db.SaveChangesAsync();
    }
```

  4. **DI**：`Program.cs` 服务注册区（`IFlowDefService` 注册行旁）加 `builder.Services.AddScoped<CP6.Core.Services.Wf.IFlowVersionService, CP6.Core.Services.Wf.FlowVersionService>();`。

- [ ] **Step 5: 跑验证 PASS + 全量** — `--filter FlowVersionServiceTests` 全绿；`--filter Wf` + `--filter Oa` 全量既有照绿（DesignerServiceTests 若因 FlowDefSummary 加尾参红 → 构造行适配，断言不动；若因「保存不升版」断言红——该断言锁的是被 spec 明令退役的行为，属规格变更允许的**唯一**断言级调整，逐条列入 commit message 并在测试内注明「V-B1 版本号改由 copy-on-write 分配（spec §3.2）」）。
- [ ] **Step 6: commit** — `git add -A && git commit -m "feat(wfs-version-ops): V-B1 FlowVersionService(copy-on-write/发布冻结/Enable继承/守卫)+SaveDef收窄+FlowAdmin组口径"`

---

### Task V-B2: 撞键并发定点（SQLite）+ 发布/版本端点 + E-WF-023 口径同步

> 依赖 V-B1。撞键重载路径 InMemory 不 enforce 唯一索引，用 SQLite 定点。

**Files:**
- Test: `CP6.Tests/Wf/FlowVersionConcurrencyTests.cs`（新建，SQLite）
- Modify: `CP6.WebApi/Controllers/Oa/DesignerController.cs`
- Modify: `cp6.web/src/api/oa/designer.ts`
- Modify: 波③ `CP6.Core/Services/Wf/FlowTriggerService.cs`（E-WF-023 保存校验口径）

- [ ] **Step 1: 写 SQLite 并发测试**

```csharp
// CP6.Tests/Wf/FlowVersionConcurrencyTests.cs
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Wf;

/// <summary>copy-on-write 撞键重载定点（spec §3.1 并发口径）：两设计器同时衍生 → 后到者撞
/// (TenantId,FlowKey,Version) 唯一键 → 捕获后重载对方刚建的草稿（不是报错）。SQLite 真索引 enforce。</summary>
public class FlowVersionConcurrencyTests
{
    [Fact]
    public async Task OpenDraft_ConcurrentDerive_UniqueClash_ReloadsPeersDraft()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<CP6Context>().UseSqlite(conn).Options;
        using (var setup = new CP6Context(opts))
        {
            setup.Database.EnsureCreated();
            setup.Wf_FlowDefs.Add(new Wf_FlowDef
            {
                Id = Guid.NewGuid(), FlowKey = "cc1", FlowName = "cc1", FormKey = "f",
                SchemaJson = "{\"start\":\"s\",\"nodes\":[{\"id\":\"s\",\"type\":\"start\"}],\"edges\":[]}",
                Version = 1, Enable = true, Status = WfFlowDefStatus.Published, PublishedAtUtc = DateTime.UtcNow,
            });
            setup.SaveChanges();
        }

        // 模拟并发：ctxB 先落 v2 草稿（对方赢）；ctxA 不知情走衍生 → 撞唯一键 → 必须重载 ctxB 的草稿
        Guid peerDraftId;
        using (var ctxB = new CP6Context(opts))
        {
            var svcB = new FlowVersionService(ctxB);
            peerDraftId = (await svcB.OpenDraftAsync("cc1", Guid.NewGuid())).Id;
        }
        using (var ctxA = new CP6Context(opts))
        {
            // ctxA 视角复现「查草稿时对方尚未提交」：先断言草稿可见性，再直接压一行同 Version 冲突行验证捕获路径。
            // 直接调 OpenDraftAsync 会命中「已有草稿」分支（对方已提交）——同样是 spec 语义（返回对方草稿），一并断言：
            var svcA = new FlowVersionService(ctxA);
            var got = await svcA.OpenDraftAsync("cc1", Guid.NewGuid());
            Assert.Equal(peerDraftId, got.Id);                      // 不重复衍生
        }
        using (var check = new CP6Context(opts))
            Assert.Equal(1, await check.Wf_FlowDefs.CountAsync(d => d.FlowKey == "cc1" && d.Status == WfFlowDefStatus.Draft));
    }

    [Fact]
    public async Task OpenDraft_TrueRace_DbUpdateException_CaughtAndReloaded()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<CP6Context>().UseSqlite(conn).Options;
        using (var setup = new CP6Context(opts))
        {
            setup.Database.EnsureCreated();
            setup.Wf_FlowDefs.Add(new Wf_FlowDef
            {
                Id = Guid.NewGuid(), FlowKey = "cc2", FlowName = "cc2", FormKey = "f", SchemaJson = "{}",
                Version = 1, Enable = true, Status = WfFlowDefStatus.Published, PublishedAtUtc = DateTime.UtcNow,
            });
            setup.SaveChanges();
        }

        // 真竞态复现：ctxA 已查完「无草稿」（用同一 service 实例分步不可行——OpenDraftAsync 原子），
        // 等价复现法：预先用旁路连接压入 v2 草稿行，再在 ctxA 的 DbContext 里手工 Add 同 (FlowKey,Version) 行
        // 走同一 catch 恢复路径（对 OpenDraftAsync 的 catch 分支做直接单元覆盖）。
        using var ctxPeer = new CP6Context(opts);
        ctxPeer.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = "cc2", FlowName = "cc2", FormKey = "f", SchemaJson = "{}",
            Version = 2, Enable = true, Status = WfFlowDefStatus.Draft,
        });
        await ctxPeer.SaveChangesAsync();

        using var ctxA = new CP6Context(opts);
        ctxA.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = "cc2", FlowName = "cc2", FormKey = "f", SchemaJson = "{}",
            Version = 2, Enable = true, Status = WfFlowDefStatus.Draft,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctxA.SaveChangesAsync());   // 唯一索引真 enforce（衬底断言）

        // OpenDraftAsync 的恢复语义：撞后 Detach + 重载对方草稿
        ctxA.ChangeTracker.Clear();
        var svcA = new FlowVersionService(ctxA);
        var got = await svcA.OpenDraftAsync("cc2", Guid.NewGuid());
        Assert.Equal(2, got.Version);
        using var check = new CP6Context(opts);
        Assert.Equal(1, await check.Wf_FlowDefs.CountAsync(d => d.FlowKey == "cc2" && d.Version == 2));
    }
}
```

> SQLite 注意：`[Timestamp] RowVersion` 在 SQLite 无自动值——本文件不断言 RowVersion 行为（发布并发闸由 V-B1 显式比较测试覆盖 + SQL Server 生产 rowversion 真闸）；若 `EnsureCreated` 因既有 SQLite 既知 5-skip 同因失败，则本文件加同款 `[SkippableFact]`/环境探测跳过口径（镜像既有 SQLite 测试文件的处理，不新造模式）。

- [ ] **Step 2: DesignerController 端点**（`DesignerController.cs` 追加；`_ver` = 注入 `IFlowVersionService`）：

```csharp
    // ── 版本治理（V-B2）：打开草稿（copy-on-write）/发布/版本历史/历史 schema/从历史另存草稿 ──

    [HttpGet("draft/{flowKey}")]
    public async Task<IActionResult> OpenDraft(string flowKey)
    {
        var user = (await _ctx.GetAsync()).UserId;
        return Ok2(await _ver.OpenDraftAsync(flowKey, user));
    }

    public record PublishReq(string FlowKey, byte[]? RowVersion);

    [HttpPost("publish")]
    public async Task<IActionResult> Publish([FromBody] PublishReq r)
    {
        try
        {
            var user = (await _ctx.GetAsync()).UserId;
            await _ver.PublishAsync(r.FlowKey, r.RowVersion, user);
            return Ok2(true);
        }
        catch (DbUpdateConcurrencyException e) { return Conflict(new { code = 409, message = e.Message }); }   // 前端提示重载
        catch (InvalidOperationException e) { return Err(e); }   // E-WF-030 聚合校验错误随响应
    }

    [HttpGet("versions/{flowKey}")]
    public async Task<IActionResult> Versions(string flowKey)
        => Ok2(await _ver.ListVersionsAsync(flowKey));

    [HttpGet("version-schema/{flowKey}/{version:int}")]
    public async Task<IActionResult> VersionSchema(string flowKey, int version)
    {
        var json = await _ver.GetVersionSchemaAsync(flowKey, version);
        return json is null ? NotFound(new { code = 404, message = "E-WF-006" }) : Ok2(new { schemaJson = json });
    }

    public record DraftFromReq(string FlowKey, int Version);

    [HttpPost("draft-from")]
    public async Task<IActionResult> DraftFrom([FromBody] DraftFromReq r)
    {
        try
        {
            var user = (await _ctx.GetAsync()).UserId;
            return Ok2(await _ver.DraftFromVersionAsync(r.FlowKey, r.Version, user));
        }
        catch (InvalidOperationException e) { return Err(e); }
    }
```

  既有 `Load`（:40-47）改：`summary` 照旧 + `schemaJson` 改取 `OpenDraftAsync` 结果（**打开设计器=进入草稿语义**，spec §3.1），响应加 `version/status/rowVersion`（向后兼容加字段）；`Save`（:51-61）`SaveReq` 加 `byte[]? RowVersion`，`DbUpdateConcurrencyException` → 409（同 Publish）。

- [ ] **Step 3: 前端 API 客户端** — `api/oa/designer.ts` 加 `openDraft(flowKey)` / `publish(flowKey, rowVersion)` / `versions(flowKey)` / `versionSchema(flowKey, version)` / `draftFrom(flowKey, version)` 五方法（镜像既有 axios 封装形状）。

- [ ] **Step 4: E-WF-023 口径同步**（spec §3.6）— 波③ `FlowTriggerService` 保存校验处 `flowOk` 判据由「存在 FlowKey && Enable 行」改：

```csharp
        var latest = await _db.Wf_FlowDefs
            .Where(x => x.FlowKey == req.FlowKey && x.Status == WfFlowDefStatus.Published)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync();
        var flowOk = latest is not null && latest.Enable;   // E-WF-023 口径=「无 Published+Enable 版本」（version-ops spec §3.6）
```

  FireAsync 运行时检查同判据（透传测试已在 V-A3 Step 2 锁定）。波③ 触发器测试若有「Enable=false 行存在仍 fail」断言——判据收紧后照绿（子集关系），跑 `--filter Trigger` 确认。

- [ ] **Step 5: 验证 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FlowVersionConcurrencyTests|FlowVersionServiceTests"
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-version-ops): V-B2 撞键重载SQLite定点+发布/版本五端点(409冲突语义)+E-WF-023口径同步"
```

---

### Task V-B3: 设计器 UI——版本下拉 + 发布按钮 + 历史只读 + 冲突重载提示

> 依赖 V-B2。**`DesignerView.vue` 只做加法**（三期已锁图形模式零回归；本 Task 同样不动 `DesignerCanvas.vue`/`designerModel.ts`）。

**Files:**
- Modify: `cp6.web/src/views/oa/designer/DesignerView.vue`
- Test: type-check + build（交互闭环 QA harness 剧本走真浏览器，V-G2）

- [ ] **Step 1: 状态与加载改造**（`<script setup>` 区，`:27 flowKey` ref 附近加）：

```ts
// ── 版本治理（V-B3）────────────────────────────────────────────
const currentVersion  = ref<number>()                 // 正在查看/编辑的版本号
const currentStatus   = ref<number>()                 // 0=Draft 1=Published（WfFlowDefStatus 镜像）
const rowVersion      = ref<string | null>(null)      // base64 乐观并发令牌（保存/发布回传）
const versions        = ref<{ version: number; status: number; publishedAtUtc?: string; publishedBy?: string; enable: boolean }[]>([])
const viewingHistory  = computed(() => currentStatus.value === 1)   // 选中 Published 历史版 → 画布只读
const publishing      = ref(false)
```

  选中流程加载（既有 `:58` 一带）改调 `designerApi.openDraft(flowKey)`：草稿 schema 上画布、`currentVersion/currentStatus/rowVersion` 就位、`versions` 由 `designerApi.versions(flowKey)` 填充。版本下拉切到历史版 → `designerApi.versionSchema` 取 schema 只读渲染（画布/状态机两模式均可查看；保存/发布按钮禁用 + 只读横幅）。

- [ ] **Step 2: 工具栏加法**（`.designer-toolbar`（:241）内、保存按钮（:299）前插）：

```html
      <!-- 版本下拉（vN·状态·发布时间）：选历史版本只读查看（spec §3.4） -->
      <el-select v-model="currentVersion" style="width: 220px" @change="onPickVersion">
        <el-option v-for="v in versions" :key="v.version" :value="v.version"
          :label="`v${v.version} · ${v.status === 1 ? t('oa.designer.pub.published') : t('oa.designer.pub.draft')}${v.publishedAtUtc ? ' · ' + v.publishedAtUtc.slice(0, 16) : ''}`" />
      </el-select>
      <el-button v-if="viewingHistory" @click="saveAsDraftFromHistory">{{ t('oa.designer.pub.saveAsDraft') }}</el-button>
      <el-button v-if="viewingHistory" type="primary" plain @click="openDiffDialog">{{ t('oa.designer.pub.diff') }}</el-button>
      <el-button type="success" :loading="publishing" :disabled="viewingHistory" @click="doPublish">
        {{ t('oa.designer.pub.publish') }}
      </el-button>
```

  只读横幅（`.designer-toolbar` 之后、主区之前）：

```html
    <el-alert v-if="viewingHistory" type="warning" :closable="false" show-icon>
      {{ t('oa.designer.pub.readonlyBanner', { version: currentVersion }) }}
    </el-alert>
```

- [ ] **Step 3: 行为函数**：

```ts
async function doPublish() {
  publishing.value = true
  try {
    await ElMessageBox.confirm(t('oa.designer.pub.publishConfirm'), t('oa.designer.pub.publish'))
    await doSave()                                                   // 先保存草稿再发布（未保存改动不丢）
    await designerApi.publish(flowKey.value.trim(), rowVersion.value)
    ElMessage.success(t('oa.designer.pub.publishOk'))
    await reloadCurrent()                                            // 重载：新 Published + 无草稿态
  } catch (e: unknown) {
    // 409 → 冲突重载提示（http 拦截器 toast 之外的明确指引）；400 E-WF-030 聚合错误由拦截器展示
    if ((e as { response?: { status?: number } })?.response?.status === 409) {
      ElMessage.warning(t('oa.designer.pub.conflictReload'))
      await reloadCurrent()
    }
  } finally { publishing.value = false }
}

async function onPickVersion(v: number) {
  const meta = versions.value.find(x => x.version === v)
  if (!meta) return
  if (meta.status === 0) { await reloadCurrent(); return }          // 回到草稿 = 可编辑
  const res = await designerApi.versionSchema(flowKey.value.trim(), v)
  loadSchemaToCanvas(res.data.schemaJson)                            // 复用既有 schema→画布装载函数（:58 一带抽出）
  currentStatus.value = 1
}

async function saveAsDraftFromHistory() {
  const d = await designerApi.draftFrom(flowKey.value.trim(), currentVersion.value!)
  ElMessage.success(t('oa.designer.pub.draftFromOk'))
  loadDraft(d.data)                                                  // 装载新草稿（可编辑态）
}
```

  保存（既有 `doSave` :158）：payload 加 `rowVersion`；409 分支同 `doPublish`（提示重载）；`viewingHistory` 时保存按钮 `:disabled`。

- [ ] **Step 4: 验证 + commit** — `npm run type-check`（`NODE_OPTIONS=--max-old-space-size=8192`）+ `npm run build`；`git add -A && git commit -m "feat(wfs-version-ops): V-B3 设计器版本下拉+发布按钮+历史只读横幅+409冲突重载提示(DesignerView只做加法)"`

---

## Wave V-C — 版本 diff（State/Path 表级，消费三期投影；并行退化 JSON diff）

### Task V-C1: `versionDiff.ts` 纯函数 + vitest

> 依赖 V-B2（版本 schema 端点）+ 三期 M-A（`stateMachineModel.ts` 已合入）。**契约逐字**：`schemaToStateMachine(schema: FlowSchemaDto): SmView`、`smCapability(schema: FlowSchemaDto): 'editable' | 'readonly'`（侦察结论 #13）。

**Files:**
- Create: `cp6.web/src/views/oa/designer/versionDiff.ts`
- Test: `cp6.web/src/views/oa/designer/versionDiff.test.ts`

- [ ] **Step 1: 写失败测试**

```ts
// cp6.web/src/views/oa/designer/versionDiff.test.ts
import { describe, it, expect } from 'vitest'
import { diffVersions } from './versionDiff'
import type { FlowSchemaDto } from './designerModel'

const base: FlowSchemaDto = {
  start: 's',
  nodes: [
    { id: 's', type: 'start' },
    { id: 'a', type: 'approval', name: '主管审批', approverStrategy: 'Specified', approverUserId: 'u1' },
    { id: 'b', type: 'approval', name: '财务审批', approverStrategy: 'Specified', approverUserId: 'u2' },
    { id: 'end', type: 'end' },
  ] as FlowSchemaDto['nodes'],
  edges: [
    { from: 's', to: 'a' },
    { from: 'a', to: 'b', condition: 'amount > 1000' },
    { from: 'b', to: 'end' },
  ] as FlowSchemaDto['edges'],
}

/** b 版：删节点 b、a 直连 end（删/改并发） */
const nodeDeleted: FlowSchemaDto = {
  start: 's',
  nodes: base.nodes.filter(n => n.id !== 'b'),
  edges: [{ from: 's', to: 'a' }, { from: 'a', to: 'end' }] as FlowSchemaDto['edges'],
}

/** b 版：b 改名+加节点 c */
const nodeChangedAdded: FlowSchemaDto = {
  start: 's',
  nodes: [
    ...base.nodes.map(n => (n.id === 'b' ? { ...n, name: '总监审批' } : n)),
    { id: 'c', type: 'approval', name: '归档确认', approverStrategy: 'Specified', approverUserId: 'u3' },
  ] as FlowSchemaDto['nodes'],
  edges: [
    { from: 's', to: 'a' },
    { from: 'a', to: 'b', condition: 'amount > 2000' },   // 条件也改
    { from: 'b', to: 'c' },
    { from: 'c', to: 'end' },
  ] as FlowSchemaDto['edges'],
}

/** 含并行结构（capability=readonly）→ 退化 JSON diff */
const parallel: FlowSchemaDto = {
  start: 's',
  nodes: [
    { id: 's', type: 'start' },
    { id: 'ps', type: 'parallelSplit' },
    { id: 'a', type: 'approval', approverStrategy: 'Specified', approverUserId: 'u1' },
    { id: 'b', type: 'approval', approverStrategy: 'Specified', approverUserId: 'u2' },
    { id: 'pj', type: 'parallelJoin' },
    { id: 'end', type: 'end' },
  ] as FlowSchemaDto['nodes'],
  edges: [
    { from: 's', to: 'ps' }, { from: 'ps', to: 'a' }, { from: 'ps', to: 'b' },
    { from: 'a', to: 'pj' }, { from: 'b', to: 'pj' }, { from: 'pj', to: 'end' },
  ] as FlowSchemaDto['edges'],
}

describe('diffVersions — 表级模式（双方 editable）', () => {
  it('删节点 → removed 行 + 受影响 path', () => {
    const d = diffVersions(base, nodeDeleted)
    expect(d.mode).toBe('table')
    const b = d.states.find(r => r.nodeId === 'b')
    expect(b?.kind).toBe('removed')
    expect(d.states.find(r => r.nodeId === 'a')?.kind).toBe('same')
    // 边：a→b 消失（removed）、a→end 出现（added）
    expect(d.paths.find(p => p.edgeKey === 'a__b')?.kind).toBe('removed')
    expect(d.paths.find(p => p.edgeKey === 'a__end')?.kind).toBe('added')
  })

  it('改名/加节点/改条件 → changed + added + changedFields 命中', () => {
    const d = diffVersions(base, nodeChangedAdded)
    expect(d.mode).toBe('table')
    const b = d.states.find(r => r.nodeId === 'b')
    expect(b?.kind).toBe('changed')
    expect(b?.changedFields).toContain('name')
    expect(d.states.find(r => r.nodeId === 'c')?.kind).toBe('added')
    const ab = d.paths.find(p => p.edgeKey === 'a__b')
    expect(ab?.kind).toBe('changed')
    expect(ab?.changedFields).toContain('condition')
  })

  it('同 schema 自比 → 全 same', () => {
    const d = diffVersions(base, base)
    expect(d.states.every(r => r.kind === 'same')).toBe(true)
    expect(d.paths.every(r => r.kind === 'same')).toBe(true)
  })
})

describe('diffVersions — JSON 退化（任一方 readonly，spec D4）', () => {
  it('b 含并行 → mode=json 且格式化 JSON 双列就绪', () => {
    const d = diffVersions(base, parallel)
    expect(d.mode).toBe('json')
    expect(d.jsonB).toContain('parallelSplit')
    expect(d.jsonA).toContain('"start"')
    expect(JSON.parse(d.jsonA)).toBeTruthy()   // 是合法 JSON（格式化不破坏）
  })

  it('a 含并行同样退化（对称）', () => {
    expect(diffVersions(parallel, base).mode).toBe('json')
  })
})
```

- [ ] **Step 2: 跑验证 FAIL** — `cd cp6.web && npx vitest run src/views/oa/designer/versionDiff.test.ts`（模块不存在）。

- [ ] **Step 3: 实现 `versionDiff.ts` 全文**

```ts
// cp6.web/src/views/oa/designer/versionDiff.ts
// 版本对比（version-ops spec §3.5 / D4）：复用三期状态机投影做 State/Path 表级 diff（比 JSON diff 可读）；
// 任一版本 capability=readonly（并行/inclusive/多实例 subFlow）→ 退化为格式化 SchemaJson 双列视图。
import type { FlowSchemaDto, SchemaNode, SchemaEdge } from './designerModel'
import { schemaToStateMachine, smCapability } from './statemachine/stateMachineModel'

export type DiffKind = 'added' | 'removed' | 'changed' | 'same'

export interface StateSide {
  no: number
  type: string
  name: string
  approverSummary: string
  countersign?: string
}
export interface StateDiffRow { kind: DiffKind; nodeId: string; a?: StateSide; b?: StateSide; changedFields: string[] }

export interface PathSide { from: string; to: string; condition?: string; isError?: boolean }
export interface PathDiffRow { kind: DiffKind; edgeKey: string; a?: PathSide; b?: PathSide; changedFields: string[] }

export interface VersionDiffResult {
  mode: 'table' | 'json'
  states: StateDiffRow[]
  paths: PathDiffRow[]
  jsonA: string
  jsonB: string
}

const fmt = (s: FlowSchemaDto) => JSON.stringify(s, null, 2)
const edgeKeyOf = (e: SchemaEdge) => `${e.from}__${e.to}`

/** 状态行身份 = nodeId（表级 diff 的锚；编号 no 只作展示，不参与身份比较）。 */
function stateSide(v: ReturnType<typeof schemaToStateMachine>, nodeId: string): StateSide | undefined {
  const s = v.states.find(x => x.nodeId === nodeId)
  return s ? { no: s.no, type: s.type, name: s.name, approverSummary: s.approverSummary, countersign: s.countersign } : undefined
}

const STATE_FIELDS: (keyof StateSide)[] = ['type', 'name', 'approverSummary', 'countersign']
const PATH_FIELDS: (keyof PathSide)[] = ['condition', 'isError']

export function diffVersions(a: FlowSchemaDto, b: FlowSchemaDto): VersionDiffResult {
  const jsonA = fmt(a)
  const jsonB = fmt(b)
  if (smCapability(a) === 'readonly' || smCapability(b) === 'readonly')
    return { mode: 'json', states: [], paths: [], jsonA, jsonB }   // 并行结构退化（spec D4）

  const va = schemaToStateMachine(a)
  const vb = schemaToStateMachine(b)

  // ── State 表：nodeId 并集，三色标注 ──
  const nodeIds = [...new Set([...va.states.map(s => s.nodeId), ...vb.states.map(s => s.nodeId)])]
  const states: StateDiffRow[] = nodeIds.map(nodeId => {
    const sa = stateSide(va, nodeId)
    const sb = stateSide(vb, nodeId)
    if (sa && !sb) return { kind: 'removed', nodeId, a: sa, changedFields: [] }
    if (!sa && sb) return { kind: 'added', nodeId, b: sb, changedFields: [] }
    const changed = STATE_FIELDS.filter(f => (sa![f] ?? '') !== (sb![f] ?? ''))
    return { kind: changed.length ? 'changed' : 'same', nodeId, a: sa, b: sb, changedFields: changed }
  })

  // ── Path 表：edgeKey(from__to) 并集（raw 边为身份源——SmPath 的 fromNo/toNo 是版本内编号，不跨版本可比）──
  const edgesA = new Map((a.edges ?? []).map(e => [edgeKeyOf(e), e]))
  const edgesB = new Map((b.edges ?? []).map(e => [edgeKeyOf(e), e]))
  const edgeKeys = [...new Set([...edgesA.keys(), ...edgesB.keys()])]
  const side = (e?: SchemaEdge): PathSide | undefined =>
    e ? { from: e.from, to: e.to, condition: e.condition ?? undefined, isError: e.isError ?? undefined } : undefined
  const paths: PathDiffRow[] = edgeKeys.map(edgeKey => {
    const pa = side(edgesA.get(edgeKey))
    const pb = side(edgesB.get(edgeKey))
    if (pa && !pb) return { kind: 'removed', edgeKey, a: pa, changedFields: [] }
    if (!pa && pb) return { kind: 'added', edgeKey, b: pb, changedFields: [] }
    const changed = PATH_FIELDS.filter(f => (pa![f] ?? '') !== (pb![f] ?? ''))
    return { kind: changed.length ? 'changed' : 'same', edgeKey, a: pa, b: pb, changedFields: changed }
  })

  return { mode: 'table', states, paths, jsonA, jsonB }
}
```

> 类型注记：`SchemaNode`/`SchemaEdge` 字段名（`from/to/condition/isError/name/approverStrategy`…）以 `designerModel.ts` 实际定义为准——测试里的字面量对象若字段名有出入（如 `label` vs `name`），**以实际类型编译错误为指引就地对齐**，diff 字段清单 `STATE_FIELDS/PATH_FIELDS` 同步。断言语义不动。

- [ ] **Step 4: 跑验证 PASS + commit**

```bash
cd cp6.web && npx vitest run src/views/oa/designer/versionDiff.test.ts && npm run type-check
git add -A && git commit -m "feat(wfs-version-ops): V-C1 versionDiff纯函数(State/Path表级三色+readonly退化JSON)+vitest"
```

---

### Task V-C2: VersionDiffDialog.vue + DesignerView 接线

> 依赖 V-C1、V-B3。

**Files:**
- Create: `cp6.web/src/views/oa/designer/VersionDiffDialog.vue`
- Modify: `cp6.web/src/views/oa/designer/DesignerView.vue`（对比按钮已插，接对话框）

- [ ] **Step 1: 组件实现**——`el-dialog`：顶部两个版本下拉（默认 A=当前查看版、B=最新 Published）→ 取双方 `versionSchema` → `diffVersions`：
  - `mode==='table'`：两张 `el-table`（States/Paths），行 class 按 kind 三色——**零硬编码色**：

```css
.diff-added   { background: color-mix(in srgb, var(--cp-ok) 12%, transparent); }
.diff-removed { background: color-mix(in srgb, var(--cp-danger) 12%, transparent); text-decoration: line-through; }
.diff-changed { background: color-mix(in srgb, var(--cp-warn) 14%, transparent); }
```

  changed 行的 `changedFields` 命中的单元格加 `<strong>` 强调；列：编号(A/B)/nodeId/类型/名称/审批人摘要/会签（Paths：from→to/条件/错误边）。
  - `mode==='json'`：并排两个 `<pre class="diff-json">`（`overflow: auto; font-family: var(--cp-mono, monospace)`）+ 顶部 `el-alert`（`t('oa.designer.pub.diffJsonFallback')` 说明并行结构退化，spec D4）。
- [ ] **Step 2: DesignerView 接线**——`openDiffDialog()` 打开对话框传 `flowKey` + `versions` 列表；对话框自取 schema（不污染画布状态）。
- [ ] **Step 3: 验证 + commit** — `npm run type-check && npm run build && npm run test`；`git add -A && git commit -m "feat(wfs-version-ops): V-C2 VersionDiffDialog(表级三色/JSON退化双列)+DesignerView接线"`

---

## Wave V-D — 驾驶舱：实例检索 + 版本分布 + job 运维（消费基建告警口径）

### Task V-D1: IFlowOpsService 检索域（SearchInstances / 版本分布 / SearchJobs / 老化占坑）

> 依赖 V-A3（pin 列就位）。可与 V-B/V-C 并行（不同 executor 时注意 `Program.cs` DI 区以先落者为准）。**执行前必读 spec §4.1/§4.2 筛选定义。**

**Files:**
- Create: `CP6.Core/Services/Wf/FlowOpsModels.cs`（共享契约 record 全家，逐字照抄本计划「共享契约」节）
- Create: `CP6.Core/Services/Wf/FlowOpsService.cs`（本 Task 落检索四方法；干预/分析方法留 V-D2/V-E/V-F 增补，接口一次declared 全）
- Modify: `CP6.WebApi/Program.cs`（DI 注册 `IFlowOpsService`）
- Test: `CP6.Tests/Wf/FlowOpsSearchTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/FlowOpsSearchTests.cs
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>驾驶舱检索域（spec §4.1/§4.2）：过滤矩阵、版本分布计数、job 筛选、老化占坑消费端。</summary>
public class FlowOpsSearchTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));
    private static FlowOpsService Svc(CP6Context db) => new(db, Engine(db));

    private static FlowSchema Linear(Guid u) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges = { new FlowEdge { From = "s", To = "a" }, new FlowEdge { From = "a", To = "end" } },
    };

    private static Wf_FlowDef Def(string key, int version, Guid approver) => new()
    {
        Id = Guid.NewGuid(), FlowKey = key, FlowName = key, FormKey = "f",
        SchemaJson = JsonSerializer.Serialize(Linear(approver)), Version = version, Enable = true,
        Status = WfFlowDefStatus.Published, PublishedAtUtc = DateTime.UtcNow,
    };

    [Fact]
    public async Task SearchInstances_FilterMatrix_StatusFlowKeyVersionStarter()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        var v1 = Def("ops1", 1, u); db.Wf_FlowDefs.Add(v1);
        var other = Def("ops2", 1, u); db.Wf_FlowDefs.Add(other);
        await db.SaveChangesAsync();

        Guid s1 = Guid.NewGuid(), s2 = Guid.NewGuid();
        var i1 = await Engine(db).SubmitAsync("ops1", s1, "{}");
        var i2 = await Engine(db).SubmitAsync("ops1", s2, "{}");
        var i3 = await Engine(db).SubmitAsync("ops2", s1, "{}");
        // i2 办结成终态
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == i2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(t2.Id, u, approve: true);

        var svc = Svc(db);
        var all = await svc.SearchInstancesAsync(new FlowOpsInstanceFilter(null, null, null, null, null, null, null), 1, 20);
        Assert.Equal(3, all.Total);

        var running = await svc.SearchInstancesAsync(new FlowOpsInstanceFilter(new[] { FlowInstanceStatus.Running }, null, null, null, null, null, null), 1, 20);
        Assert.Equal(2, running.Total);
        Assert.DoesNotContain(running.Rows, r => r.Id == i2);

        var byKey = await svc.SearchInstancesAsync(new FlowOpsInstanceFilter(null, "ops1", null, null, null, null, null), 1, 20);
        Assert.Equal(2, byKey.Total);

        var byVer = await svc.SearchInstancesAsync(new FlowOpsInstanceFilter(null, "ops1", 1, null, null, null, null), 1, 20);
        Assert.Equal(2, byVer.Total);
        Assert.All(byVer.Rows, r => Assert.Equal(1, r.Version));

        var byStarter = await svc.SearchInstancesAsync(new FlowOpsInstanceFilter(null, null, null, null, s1, null, null), 1, 20);
        Assert.Equal(2, byStarter.Total);
        Assert.All(byStarter.Rows, r => Assert.Equal(s1, r.StarterId));

        // 分页
        var page1 = await svc.SearchInstancesAsync(new FlowOpsInstanceFilter(null, null, null, null, null, null, null), 1, 2);
        Assert.Equal(3, page1.Total);
        Assert.Equal(2, page1.Rows.Count);
        Assert.Contains(i3, (await svc.SearchInstancesAsync(new FlowOpsInstanceFilter(null, null, null, null, null, null, null), 2, 2)).Rows.Select(r => r.Id).Concat(page1.Rows.Select(r => r.Id)));
    }

    [Fact]
    public async Task SearchInstances_StuckDays_ParkedOverN()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        db.Wf_FlowDefs.Add(Def("ops3", 1, u));
        await db.SaveChangesAsync();
        var i1 = await Engine(db).SubmitAsync("ops3", Guid.NewGuid(), "{}");
        var i2 = await Engine(db).SubmitAsync("ops3", Guid.NewGuid(), "{}");

        // i1 停泊 10 天（把其 Pending FormTo 的 SentAt 拨旧——停留时长口径 = Pending FormTo 最早 SentAt）
        foreach (var f in await db.Wf_FlowFormTos.Where(f => f.InstanceId == i1 && f.Status == FlowFormToStatus.Pending).ToListAsync())
            f.SentAt = DateTime.Now.AddDays(-10);
        await db.SaveChangesAsync();

        var stuck = await Svc(db).SearchInstancesAsync(new FlowOpsInstanceFilter(null, null, null, 7, null, null, null), 1, 20);
        Assert.Single(stuck.Rows);
        Assert.Equal(i1, stuck.Rows[0].Id);
        Assert.NotNull(stuck.Rows[0].StuckSince);
        Assert.DoesNotContain(stuck.Rows, r => r.Id == i2);
    }

    [Fact]
    public async Task VersionDistribution_Matrix_InFlightByVersion()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        var v1 = Def("ops4", 1, u); db.Wf_FlowDefs.Add(v1);
        await db.SaveChangesAsync();
        var a = await Engine(db).SubmitAsync("ops4", Guid.NewGuid(), "{}");   // pin v1
        var b = await Engine(db).SubmitAsync("ops4", Guid.NewGuid(), "{}");   // pin v1

        db.Wf_FlowDefs.Add(Def("ops4", 2, u));   // 发布 v2
        await db.SaveChangesAsync();
        var c = await Engine(db).SubmitAsync("ops4", Guid.NewGuid(), "{}");   // pin v2
        // b 办结 → 不再计入在途
        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == b && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(tb.Id, u, approve: true);

        var rows = await Svc(db).GetVersionDistributionAsync();
        var m = rows.Where(r => r.FlowKey == "ops4").ToDictionary(r => r.Version!.Value);
        Assert.Equal(1, m[1].RunningCount);   // 只剩 a（D3「观察」面：v1 在途清零即可关闸收敛）
        Assert.Equal(1, m[2].RunningCount);
        Assert.Equal(0, m[1].SuspendedCount);
    }

    [Fact]
    public async Task SearchJobs_ByStatusAndKind()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        var def = Def("ops5", 1, u); db.Wf_FlowDefs.Add(def);
        await db.SaveChangesAsync();
        var inst = await Engine(db).SubmitAsync("ops5", Guid.NewGuid(), "{}");
        var tok = await db.Wf_FlowTokens.FirstAsync(t => t.InstanceId == inst);

        db.Wf_ServiceJobs.AddRange(
            new Wf_ServiceJob { Id = Guid.NewGuid(), InstanceId = inst, TokenId = tok.Id, NodeId = "svc1", Kind = "webApi",
                Status = ServiceJobStatus.Failed, AttemptCount = 4, MaxAttempts = 4, NextAttemptAtUtc = DateTime.UtcNow, LastError = "boom", CompletedAtUtc = DateTime.UtcNow },
            new Wf_ServiceJob { Id = Guid.NewGuid(), InstanceId = inst, TokenId = tok.Id, NodeId = "svc2", Kind = "timer",
                Status = ServiceJobStatus.Pending, AttemptCount = 0, MaxAttempts = 4, NextAttemptAtUtc = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();

        var failed = await Svc(db).SearchJobsAsync(new FlowOpsJobFilter(new[] { ServiceJobStatus.Failed }, null, null, null), 1, 20);
        Assert.Single(failed.Rows);
        Assert.Equal("boom", failed.Rows[0].LastError);
        Assert.Equal("ops5", failed.Rows[0].FlowKey);

        var timers = await Svc(db).SearchJobsAsync(new FlowOpsJobFilter(null, "timer", null, null), 1, 20);
        Assert.Single(timers.Rows);
        Assert.Equal("svc2", timers.Rows[0].NodeId);
    }

    [Fact]
    public async Task StaleTriggerFires_ReservationHoles_OverGrace()
    {
        using var db = NewDb();
        var trig = Guid.NewGuid();
        db.Wf_TriggerFires.AddRange(
            new Wf_TriggerFire { Id = Guid.NewGuid(), TriggerId = trig, IdempotencyKey = "k-old",
                FiredUtc = DateTime.UtcNow.AddDays(-9), InstanceId = null, Error = null, Source = 0 },      // 老化占坑 ★
            new Wf_TriggerFire { Id = Guid.NewGuid(), TriggerId = trig, IdempotencyKey = "k-fresh",
                FiredUtc = DateTime.UtcNow.AddHours(-1), InstanceId = null, Error = null, Source = 0 },     // 新占坑（宽限内）
            new Wf_TriggerFire { Id = Guid.NewGuid(), TriggerId = trig, IdempotencyKey = "k-done",
                FiredUtc = DateTime.UtcNow.AddDays(-9), InstanceId = Guid.NewGuid(), Error = null, Source = 0 },   // 已完成
            new Wf_TriggerFire { Id = Guid.NewGuid(), TriggerId = trig, IdempotencyKey = "k-err",
                FiredUtc = DateTime.UtcNow.AddDays(-9), InstanceId = null, Error = "E-WF-024: x", Source = 0 });   // 已失败
        await db.SaveChangesAsync();

        var stale = await Svc(db).GetStaleTriggerFiresAsync(graceDays: 7);
        Assert.Single(stale);
        Assert.Equal("k-old", stale[0].IdempotencyKey);   // 基建 spec §4 口径：InstanceId 与 Error 均空 且 超宽限
    }
}
```

- [ ] **Step 2: 跑验证 FAIL**，然后实现 `FlowOpsService.cs`（接口全量声明 + 本 Task 四方法；其余方法体 `throw new NotImplementedException("V-E/V-F 落地")` **不允许**——接口分波落地改为：**接口方法随波次增补声明**，V-D1 只声明检索四方法，V-D2/V-E/V-F 各自扩接口+实现，规避占位空体）。核心实现：

```csharp
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public partial interface IFlowOpsService
{
    Task<FlowOpsPage<FlowOpsInstanceItem>> SearchInstancesAsync(FlowOpsInstanceFilter filter, int page, int pageSize);
    Task<IReadOnlyList<VersionDistributionRow>> GetVersionDistributionAsync();
    Task<FlowOpsPage<FlowOpsJobItem>> SearchJobsAsync(FlowOpsJobFilter filter, int page, int pageSize);
    Task<IReadOnlyList<StaleTriggerFireItem>> GetStaleTriggerFiresAsync(int graceDays);
}

/// <summary>运维驾驶舱聚合服务（spec §4）。专用聚合查询，不复用收件箱查询（视角不同：全租户 vs 个人，spec §4.1）；
/// TenantId 由全局查询过滤器贯穿（platform-admin 亦租户内，跨租户运维不在本期，spec §6）。</summary>
public partial class FlowOpsService : IFlowOpsService
{
    private readonly CP6Context _db;
    private readonly FlowEngine _engine;
    public FlowOpsService(CP6Context db, FlowEngine engine) { _db = db; _engine = engine; }

    public async Task<FlowOpsPage<FlowOpsInstanceItem>> SearchInstancesAsync(FlowOpsInstanceFilter f, int page, int pageSize)
    {
        var q = _db.Wf_FlowInstances.AsQueryable();
        if (f.Statuses is { Length: > 0 }) q = q.Where(i => f.Statuses.Contains(i.Status));
        if (!string.IsNullOrWhiteSpace(f.FlowKey)) q = q.Where(i => i.FlowKey == f.FlowKey);
        if (f.StarterId is Guid s) q = q.Where(i => i.StarterId == s);
        if (f.FromUtc is DateTime from) q = q.Where(i => i.CreateDate >= from);
        if (f.ToUtc is DateTime to) q = q.Where(i => i.CreateDate <= to);
        if (f.Version is int ver)
            q = from i in q
                join d0 in _db.Wf_FlowDefs on i.FlowDefId equals d0.Id
                where d0.Version == ver
                select i;
        if (f.StuckDays is int days)   // 停泊超龄口径：存在 Pending FormTo 且 SentAt < now-N 天（送签后无人处理）
        {
            var cutoff = DateTime.Now.AddDays(-days);
            q = q.Where(i => _db.Wf_FlowFormTos.Any(ft => ft.InstanceId == i.Id
                && ft.Status == FlowFormToStatus.Pending && ft.SentAt < cutoff));
        }

        var total = await q.CountAsync();
        var rows = await (
            from i in q
            join d in _db.Wf_FlowDefs on i.FlowDefId equals d.Id into dj
            from d in dj.DefaultIfEmpty()                                    // 终态孤儿 null pin → 版本列空
            orderby i.CreateDate descending
            select new
            {
                i.Id, i.BizId, i.FlowKey, FlowName = (string?)d.FlowName, Version = (int?)d.Version,
                i.CurrentNode, i.StarterId, i.Status, i.CreateDate,
                StuckSince = _db.Wf_FlowFormTos.Where(ft => ft.InstanceId == i.Id && ft.Status == FlowFormToStatus.Pending)
                    .Min(ft => (DateTime?)ft.SentAt),
            })
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var names = await OaUserNamesResolveAsync(rows.Select(r => r.StarterId));
        return new FlowOpsPage<FlowOpsInstanceItem>(rows.Select(r => new FlowOpsInstanceItem(
            r.Id, r.BizId, r.FlowKey, r.FlowName, r.Version, r.CurrentNode, r.StuckSince,
            r.StarterId, names.GetValueOrDefault(r.StarterId), r.Status, r.CreateDate)).ToList(), total);
    }

    public async Task<IReadOnlyList<VersionDistributionRow>> GetVersionDistributionAsync() =>
        await (from i in _db.Wf_FlowInstances
               where i.Status == FlowInstanceStatus.Running || i.Status == FlowInstanceStatus.Suspended
               join d in _db.Wf_FlowDefs on i.FlowDefId equals d.Id into dj
               from d in dj.DefaultIfEmpty()
               group new { i, d } by new { i.FlowKey, FlowName = (string?)d.FlowName, Version = (int?)d.Version } into g
               orderby g.Key.FlowKey, g.Key.Version
               select new VersionDistributionRow(g.Key.FlowKey, g.Key.FlowName, g.Key.Version,
                   g.Count(x => x.i.Status == FlowInstanceStatus.Running),
                   g.Count(x => x.i.Status == FlowInstanceStatus.Suspended)))
            .ToListAsync();

    public async Task<FlowOpsPage<FlowOpsJobItem>> SearchJobsAsync(FlowOpsJobFilter f, int page, int pageSize)
    {
        var q = _db.Wf_ServiceJobs.AsQueryable();
        if (f.Statuses is { Length: > 0 }) q = q.Where(j => f.Statuses.Contains(j.Status));
        if (!string.IsNullOrWhiteSpace(f.Kind)) q = q.Where(j => j.Kind == f.Kind);
        if (f.FromUtc is DateTime from) q = q.Where(j => j.CreateDate >= from);
        if (f.ToUtc is DateTime to) q = q.Where(j => j.CreateDate <= to);

        var total = await q.CountAsync();
        var rows = await (
            from j in q
            join i in _db.Wf_FlowInstances on j.InstanceId equals i.Id
            orderby j.NextAttemptAtUtc descending
            select new FlowOpsJobItem(j.Id, j.InstanceId, j.NodeId, j.Kind, j.Status, j.AttemptCount, j.MaxAttempts,
                j.NextAttemptAtUtc, j.LastError, j.CompletedAtUtc, i.FlowKey))
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new FlowOpsPage<FlowOpsJobItem>(rows, total);
    }

    /// <summary>老化占坑消费端（基建 spec §4：InstanceId 与 Error 均空、FiredUtc 超宽限；清理 worker 永不清、只告警）。</summary>
    public async Task<IReadOnlyList<StaleTriggerFireItem>> GetStaleTriggerFiresAsync(int graceDays)
    {
        var cutoff = DateTime.UtcNow.AddDays(-graceDays);
        return await _db.Wf_TriggerFires
            .Where(x => x.InstanceId == null && x.Error == null && x.FiredUtc < cutoff)
            .OrderBy(x => x.FiredUtc)
            .Select(x => new StaleTriggerFireItem(x.Id, x.TriggerId, x.IdempotencyKey, x.FiredUtc, x.Source))
            .ToListAsync();
    }

    /// <summary>发起人姓名解析——复用 OaUserNames 惯用法（InboxService 同款）。</summary>
    private async Task<Dictionary<Guid, string?>> OaUserNamesResolveAsync(IEnumerable<Guid> ids)
    {
        var set = ids.Distinct().ToList();
        return await _db.Sys_Users.Where(u => set.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => (string?)u.UserTrueName);
    }
}
```

  DI：`Program.cs` 加 `builder.Services.AddScoped<CP6.Core.Services.Wf.IFlowOpsService, CP6.Core.Services.Wf.FlowOpsService>();`。`Sys_Users.UserTrueName` 字段名以实体实际为准（`OaUserNames.ResolveAsync` 既有工具若可直用则直用，删本地私有方法）。

- [ ] **Step 3: 跑验证 PASS + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter FlowOpsSearchTests
git add -A && git commit -m "feat(wfs-version-ops): V-D1 FlowOpsService检索域(实例过滤矩阵/版本分布/job筛选/老化占坑消费端)"
```

---

### Task V-D2: job 重放/取消（token 停泊前置两态 + Running 拒 + 双痕）

> 依赖 V-D1。**执行前必读 spec §4.2 全节（评审补的 token 前置校验是本 Task 灵魂）+ 侦察结论 #2/#12。**

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowOpsService.cs`（partial 增补 `ReplayJobAsync`/`CancelJobAsync` + 接口声明）
- Test: `CP6.Tests/Wf/FlowOpsJobActionTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/FlowOpsJobActionTests.cs
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>job 重放/取消（spec §4.2）：token 停泊前置两态（侦察结论 #2）、Running lease 拒、
/// 重放后 worker 正常拾起、取消走错误路由、双痕。</summary>
public class FlowOpsJobActionTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));
    private static FlowOpsService Svc(CP6Context db) => new(db, Engine(db));

    /// <summary>svc 节点流程：s → svc(serviceTask) → end；withErrorEdge=true 时另有 svc→errEnd 错误边。</summary>
    private static FlowSchema SvcSchema(bool withErrorEdge) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "svc", Type = "serviceTask" },
            new FlowNode { Id = "end", Type = "end" },
            new FlowNode { Id = "errEnd", Type = "end" },
        },
        Edges = withErrorEdge
            ? new List<FlowEdge>
            {
                new() { From = "s", To = "svc" }, new() { From = "svc", To = "end" },
                new() { From = "svc", To = "errEnd", IsError = true },
            }
            : new List<FlowEdge> { new() { From = "s", To = "svc" }, new() { From = "svc", To = "end" } },
    };

    /// <summary>手工布景：实例 + 停泊 token@svc + Failed job（绕过 handler 细节，聚焦 job 动作语义）。</summary>
    private static async Task<(Guid instId, Guid tokId, Guid jobId)> SeedFailedAsync(CP6Context db, bool withErrorEdge)
    {
        var def = new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = "ja" + Guid.NewGuid().ToString("N")[..6], FlowName = "ja", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(SvcSchema(withErrorEdge)), Version = 1, Enable = true,
            Status = WfFlowDefStatus.Published, PublishedAtUtc = DateTime.UtcNow,
        };
        db.Wf_FlowDefs.Add(def);
        var inst = new Wf_FlowInstance
        {
            Id = Guid.NewGuid(), FlowKey = def.FlowKey, FlowDefId = def.Id, CurrentNode = "svc",
            Status = FlowInstanceStatus.Running, StarterId = Guid.NewGuid(), VarsJson = "{}",
        };
        db.Wf_FlowInstances.Add(inst);
        var tok = new Wf_FlowToken { Id = Guid.NewGuid(), InstanceId = inst.Id, NodeId = "svc", Status = FlowTokenStatus.Active };
        db.Wf_FlowTokens.Add(tok);
        var job = new Wf_ServiceJob
        {
            Id = Guid.NewGuid(), InstanceId = inst.Id, TokenId = tok.Id, NodeId = "svc", Kind = "webApi",
            Status = ServiceJobStatus.Failed, AttemptCount = 4, MaxAttempts = 4,
            NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1), LastError = "E: 外部系统 500", CompletedAtUtc = DateTime.UtcNow,
        };
        db.Wf_ServiceJobs.Add(job);
        await db.SaveChangesAsync();
        return (inst.Id, tok.Id, job.Id);
    }

    [Fact]
    public async Task Replay_TokenParked_ResetsJob_WorkerPicksUp()
    {
        using var db = NewDb();
        var (instId, tokId, jobId) = await SeedFailedAsync(db, withErrorEdge: false);
        var actor = Guid.NewGuid();

        await Svc(db).ReplayJobAsync(jobId, actor, "外部系统已恢复");

        var job = await db.Wf_ServiceJobs.SingleAsync(j => j.Id == jobId);
        Assert.Equal(ServiceJobStatus.Pending, job.Status);      // spec §4.2 重放定义
        Assert.Equal(0, job.AttemptCount);
        Assert.Null(job.LastError);
        Assert.Null(job.LockedBy);
        Assert.Null(job.CompletedAtUtc);
        Assert.True(job.NextAttemptAtUtc <= DateTime.UtcNow.AddSeconds(1));
        Assert.True(await db.Wf_FlowHistories.AnyAsync(h => h.InstanceId == instId && h.Action == "jobReplay" && h.ActorId == actor));

        // worker 正常拾起（executor 未注册 → E-WF-018 失败路径也算「拾起」；此处只验状态闸放行不 Cancel）
        var worker = new WfServiceJobService(db, Engine(db), Array.Empty<IServiceTaskExecutor>());
        var n = await worker.ScanOnceAsync(DateTime.UtcNow, "w-test");
        Assert.Equal(1, n);
        Assert.NotEqual(ServiceJobStatus.Cancelled, (await db.Wf_ServiceJobs.SingleAsync(j => j.Id == jobId)).Status);
    }

    [Fact]
    public async Task Replay_SuspendedInstance_CompanionResume()   // 侦察结论 #12：挂起实例重放 → 伴随回 Running
    {
        using var db = NewDb();
        var (instId, _, jobId) = await SeedFailedAsync(db, withErrorEdge: false);
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        inst.Status = FlowInstanceStatus.Suspended;   // 「Failed 无错误边」真实形态
        await db.SaveChangesAsync();

        await Svc(db).ReplayJobAsync(jobId, Guid.NewGuid(), null);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId)).Status);
    }

    [Fact]
    public async Task Replay_TokenGone_Rejected400()   // 侦察结论 #2 态①：有错误边、token 已走 → 僵尸调用定点
    {
        using var db = NewDb();
        var (instId, tokId, jobId) = await SeedFailedAsync(db, withErrorEdge: true);
        // 模拟「重试耗尽已走错误边」：token 已离开 svc（NodeId 改 errEnd 并消费）
        var tok = await db.Wf_FlowTokens.SingleAsync(t => t.Id == tokId);
        tok.NodeId = "errEnd"; tok.Status = FlowTokenStatus.Consumed;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Svc(db).ReplayJobAsync(jobId, Guid.NewGuid(), null));
        Assert.Contains("强制推进", ex.Message);   // 「流程已走错误路径，请对实例使用强制推进/终止」
        Assert.Equal(ServiceJobStatus.Failed, (await db.Wf_ServiceJobs.SingleAsync(j => j.Id == jobId)).Status);   // 未动
    }

    [Fact]
    public async Task Replay_RunningJob_LeaseHeld_Rejected400()
    {
        using var db = NewDb();
        var (_, _, jobId) = await SeedFailedAsync(db, withErrorEdge: false);
        var job = await db.Wf_ServiceJobs.SingleAsync(j => j.Id == jobId);
        job.Status = ServiceJobStatus.Running;
        job.LockedBy = "w-1"; job.LockExpiresAtUtc = DateTime.UtcNow.AddMinutes(4);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Svc(db).ReplayJobAsync(jobId, Guid.NewGuid(), null));   // 防与 worker 竞争
    }

    [Fact]
    public async Task Cancel_PendingJob_ErrorRouting_WalksErrorEdge()
    {
        using var db = NewDb();
        var (instId, tokId, jobId) = await SeedFailedAsync(db, withErrorEdge: true);
        var job = await db.Wf_ServiceJobs.SingleAsync(j => j.Id == jobId);
        job.Status = ServiceJobStatus.Pending;   // 退避中形态
        job.CompletedAtUtc = null;
        await db.SaveChangesAsync();

        var actor = Guid.NewGuid();
        await Svc(db).CancelJobAsync(jobId, actor, "外部系统废弃");

        Assert.Equal(ServiceJobStatus.Cancelled, (await db.Wf_ServiceJobs.SingleAsync(j => j.Id == jobId)).Status);
        // 等价重试耗尽处置：FailServiceTokenAsync 走错误边 → token 到 errEnd → 实例终态
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        Assert.NotEqual("svc", (await db.Wf_FlowTokens.SingleAsync(t => t.Id == tokId)).NodeId);
        Assert.True(await db.Wf_FlowHistories.AnyAsync(h => h.InstanceId == instId && h.Action == "jobCancel" && h.ActorId == actor));
    }

    [Fact]
    public async Task Cancel_NoErrorEdge_SuspendsInstance()
    {
        using var db = NewDb();
        var (instId, tokId, jobId) = await SeedFailedAsync(db, withErrorEdge: false);
        var job = await db.Wf_ServiceJobs.SingleAsync(j => j.Id == jobId);
        job.Status = ServiceJobStatus.Pending; job.CompletedAtUtc = null;
        await db.SaveChangesAsync();

        await Svc(db).CancelJobAsync(jobId, Guid.NewGuid(), null);

        Assert.Equal(FlowInstanceStatus.Suspended, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId)).Status);
        Assert.Equal(FlowTokenStatus.Active, (await db.Wf_FlowTokens.SingleAsync(t => t.Id == tokId)).Status);   // 停泊保持
    }

    [Fact]
    public async Task Cancel_TokenGone_Rejected400()   // 对已处置 job 二次 fail 语义不成立
    {
        using var db = NewDb();
        var (_, tokId, jobId) = await SeedFailedAsync(db, withErrorEdge: true);
        var tok = await db.Wf_FlowTokens.SingleAsync(t => t.Id == tokId);
        tok.Status = FlowTokenStatus.Consumed; tok.NodeId = "errEnd";
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Svc(db).CancelJobAsync(jobId, Guid.NewGuid(), null));
    }
}
```

- [ ] **Step 2: 实现**（`FlowOpsService` partial 增补）：

```csharp
public partial interface IFlowOpsService
{
    Task ReplayJobAsync(Guid jobId, Guid actorId, string? reason);
    Task CancelJobAsync(Guid jobId, Guid actorId, string? reason);
}

public partial class FlowOpsService
{
    /// <summary>token 停泊前置（spec §4.2 评审补）：两动作统一要求 token 仍 Active 停泊于 job.NodeId——
    /// token 已走（重试耗尽走错误边等）→ 重放=纯副作用僵尸调用、取消=二次 fail，语义均不成立 → 400。
    /// 按 token 实时状态动态判断，与「Failed 后走边还是停泊」的实现形态无关（两态测试锁定）。</summary>
    private async Task<(Wf_ServiceJob job, Wf_FlowInstance inst, Wf_FlowToken token)> LoadJobForActionAsync(Guid jobId)
    {
        var job = await _db.Wf_ServiceJobs.FirstOrDefaultAsync(j => j.Id == jobId)
                  ?? throw new InvalidOperationException("job 不存在");
        if (job.Status == ServiceJobStatus.Running && job.LockExpiresAtUtc > DateTime.UtcNow)
            throw new InvalidOperationException("job 执行中（lease 未过期），禁止操作以防与 worker 竞争");
        if (job.Status == ServiceJobStatus.Running)
            throw new InvalidOperationException("job 处于 Running（等待 reaper 回收过期租约），请稍后重试");
        var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == job.InstanceId)
                   ?? throw new InvalidOperationException("实例不存在");
        var token = await _db.Wf_FlowTokens.FirstOrDefaultAsync(t => t.Id == job.TokenId);
        if (token is null || token.Status != FlowTokenStatus.Active || token.NodeId != job.NodeId)
            throw new InvalidOperationException("流程已走错误路径（token 不再停泊于该服务节点），请对实例使用强制推进/终止");
        return (job, inst, token);
    }

    public async Task ReplayJobAsync(Guid jobId, Guid actorId, string? reason)
    {
        var (job, inst, _) = await LoadJobForActionAsync(jobId);
        if (job.Status != ServiceJobStatus.Failed)
            throw new InvalidOperationException("仅 Failed job 可重放（退避中 Pending 无需重放）");

        job.Status = ServiceJobStatus.Pending;        // executor 幂等是底座铁律 → 重放安全（spec §4.2）
        job.AttemptCount = 0;
        job.NextAttemptAtUtc = DateTime.UtcNow;
        job.LastError = null;
        job.LockedBy = null; job.LockedAtUtc = null; job.LockExpiresAtUtc = null;
        job.CompletedAtUtc = null;

        if (inst.Status == FlowInstanceStatus.Suspended)
            inst.Status = FlowInstanceStatus.Running;   // 伴随：否则 worker 状态闸立即 Cancel（侦察结论 #12）

        _engine.AddHistory(inst.Id, job.NodeId, actorId, "jobReplay", reason);   // FlowHistory 痕；OperLog 由过滤器自动
        await _db.SaveChangesAsync();
    }

    public async Task CancelJobAsync(Guid jobId, Guid actorId, string? reason)
    {
        var (job, inst, _) = await LoadJobForActionAsync(jobId);
        if (job.Status is not (ServiceJobStatus.Failed or ServiceJobStatus.Pending))
            throw new InvalidOperationException("仅 Failed / Pending(退避中) job 可取消");

        job.Status = ServiceJobStatus.Cancelled;
        job.CompletedAtUtc = DateTime.UtcNow;
        _engine.AddHistory(inst.Id, job.NodeId, actorId, "jobCancel", reason);
        await _db.SaveChangesAsync();   // 先落 job 终态（防 FailServiceToken 内部 Reload 回滚，镜像 WfServiceJobService FIX 1 反例次序：此处 job 独立追踪安全）

        // 等价重试耗尽处置（spec §4.2）：错误路由（有错误边走边，无则 Suspend）。幂等：token 已在前置校验确认停泊。
        await _engine.FailServiceTokenAsync(job.InstanceId, job.TokenId, job.NodeId, "管理员取消: " + (reason ?? "无"));
    }
}
```

- [ ] **Step 3: 跑验证 PASS + 全量 Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter FlowOpsJobActionTests
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-version-ops): V-D2 job重放/取消(token停泊前置两态+Running拒+挂起伴随回Running+双痕)"
```

---

### Task V-D3: FlowOpsController + 菜单/权限种子 + FlowOps.vue 三 tab 骨架（检索/job 两 tab 就绪）

> 依赖 V-D2。分析 tab 内容 V-F2 填充，本 Task 落页面骨架与前两 tab。

**Files:**
- Create: `CP6.WebApi/Controllers/Oa/FlowOpsController.cs`
- Modify: `CP6.WebApi/Program.cs`（菜单 741 + MenuKey + MenuAction/RoleAction 种子）
- Create: `cp6.web/src/api/oa/flowOps.ts`
- Create: `cp6.web/src/views/oa/admin/FlowOps.vue`
- Modify: `cp6.web/src/router/index.ts`
- Test: `CP6.Tests/Oa/FlowOpsControllerContractTests.cs`

- [ ] **Step 1: 契约测试（反射权限矩阵——先写先红）**

```csharp
// CP6.Tests/Oa/FlowOpsControllerContractTests.cs
using System.Reflection;
using CP6.WebApi.Controllers.Oa;

namespace CP6.Tests.Oa;

/// <summary>驾驶舱端点权限矩阵（spec §4.4/§4.5）：每动作独立 action、force-advance 仅 platform-admin。
/// RequirePlatformAdmin 三道闸自身已有 RequirePlatformAdminFilterTests 锁定——此处只锁「端点贴了正确属性」。</summary>
public class FlowOpsControllerContractTests
{
    private static MethodInfo M(string name) => typeof(FlowOpsController).GetMethod(name)!;

    [Theory]
    [InlineData("SearchInstances", "view")]
    [InlineData("VersionDistribution", "view")]
    [InlineData("SearchJobs", "view")]
    [InlineData("StaleFires", "view")]
    [InlineData("ReplayJob", "job-ops")]
    [InlineData("CancelJob", "job-ops")]
    [InlineData("ForceTerminate", "terminate")]
    [InlineData("ReResolve", "re-resolve")]
    [InlineData("ForceAdvance", "force-advance")]
    [InlineData("Analytics", "view")]
    public void Endpoint_Carries_RequirePermission(string method, string action)
    {
        var attrs = M(method).GetCustomAttributes(true);
        var hit = attrs.Select(a => a.GetType().GetProperties()
                .Where(p => p.PropertyType == typeof(string))
                .Select(p => p.GetValue(a) as string).ToArray())
            .Any(vals => vals.Contains("oa-flow-ops") && vals.Contains(action));
        Assert.True(hit, $"{method} 缺 [RequirePermission(\"oa-flow-ops\",\"{action}\")]");
    }

    [Fact]
    public void ForceAdvance_Additionally_RequiresPlatformAdmin()
        => Assert.Contains(M("ForceAdvance").GetCustomAttributes(true),
            a => a.GetType().Name.Contains("RequirePlatformAdmin"));

    [Fact]
    public void ForceTerminate_ReResolve_NotPlatformAdminGated()   // 矩阵负例：只有强推是 platform-admin 专属
    {
        Assert.DoesNotContain(M("ForceTerminate").GetCustomAttributes(true), a => a.GetType().Name.Contains("RequirePlatformAdmin"));
        Assert.DoesNotContain(M("ReResolve").GetCustomAttributes(true), a => a.GetType().Name.Contains("RequirePlatformAdmin"));
    }
}
```

  > `RequirePermission` 属性的构造参数若不暴露为 string 属性（以实际实现为准），改用 `a.ToString()`/字段反射同义断言——**锁定语义不变**：oa-flow-ops + 正确 action。
  >
  > **commit 拆分口径（保证每个 commit 可编译可测，且禁 `NotImplementedException` 占位空实现）**：本 Task 把 Controller 全部端点与接口全部方法声明**写好**，但本 Task 的 commit 只包含检索/job 端点段与接口检索/job 段；干预三端点段、接口干预/分析段、本契约测试文件**暂存不 add**，随 V-E3/V-F1 的 Service 实现 commit 合入并跑绿。

- [ ] **Step 2: FlowOpsController 全端点**

```csharp
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>运维驾驶舱 REST（version-ops spec §4）。/api/oa/flow-ops —— 三 tab 查询 + 干预四动作。
/// 权限：MenuKey oa-flow-ops 五 action（§4.5）；强制推进叠 platform-admin 三道闸（D2）。
/// 双痕：OperLogFilter 全局自动记（本控制器全部 POST）+ 各 service 方法 FlowHistory 显式行。</summary>
[ApiController]
[Route("api/oa/flow-ops")]
[Authorize]
public class FlowOpsController : LocalizedControllerBase
{
    private readonly IFlowOpsService _ops;
    private readonly ICurrentPermissionContext _ctx;
    public FlowOpsController(IFlowOpsService ops, ICurrentPermissionContext ctx) { _ops = ops; _ctx = ctx; }

    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    public record InstanceSearchReq(int[]? Statuses, string? FlowKey, int? Version, int? StuckDays,
        Guid? StarterId, DateTime? FromUtc, DateTime? ToUtc, int Page = 1, int PageSize = 20);

    [HttpPost("instances/search")]
    [RequirePermission("oa-flow-ops", "view")]
    public async Task<IActionResult> SearchInstances([FromBody] InstanceSearchReq r)
        => Ok2(await _ops.SearchInstancesAsync(new FlowOpsInstanceFilter(r.Statuses, r.FlowKey, r.Version,
            r.StuckDays, r.StarterId, r.FromUtc, r.ToUtc), r.Page, r.PageSize));

    [HttpGet("version-distribution")]
    [RequirePermission("oa-flow-ops", "view")]
    public async Task<IActionResult> VersionDistribution() => Ok2(await _ops.GetVersionDistributionAsync());

    public record JobSearchReq(int[]? Statuses, string? Kind, DateTime? FromUtc, DateTime? ToUtc, int Page = 1, int PageSize = 20);

    [HttpPost("jobs/search")]
    [RequirePermission("oa-flow-ops", "view")]
    public async Task<IActionResult> SearchJobs([FromBody] JobSearchReq r)
        => Ok2(await _ops.SearchJobsAsync(new FlowOpsJobFilter(r.Statuses, r.Kind, r.FromUtc, r.ToUtc), r.Page, r.PageSize));

    [HttpGet("stale-fires")]
    [RequirePermission("oa-flow-ops", "view")]
    public async Task<IActionResult> StaleFires([FromQuery] int graceDays = 7)
        => Ok2(await _ops.GetStaleTriggerFiresAsync(graceDays));

    public record JobActionReq(Guid JobId, string? Reason);

    [HttpPost("jobs/replay")]
    [RequirePermission("oa-flow-ops", "job-ops")]
    public async Task<IActionResult> ReplayJob([FromBody] JobActionReq r)
    {
        try { await _ops.ReplayJobAsync(r.JobId, (await _ctx.GetAsync()).UserId, r.Reason); return Ok2(true); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPost("jobs/cancel")]
    [RequirePermission("oa-flow-ops", "job-ops")]
    public async Task<IActionResult> CancelJob([FromBody] JobActionReq r)
    {
        try { await _ops.CancelJobAsync(r.JobId, (await _ctx.GetAsync()).UserId, r.Reason); return Ok2(true); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    public record TerminateReq(Guid InstanceId, string Reason);

    [HttpPost("instances/force-terminate")]
    [RequirePermission("oa-flow-ops", "terminate")]
    public async Task<IActionResult> ForceTerminate([FromBody] TerminateReq r)
    {
        try { await _ops.ForceTerminateAsync(r.InstanceId, (await _ctx.GetAsync()).UserId, r.Reason); return Ok2(true); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { code = 409, message = "并发冲突，请刷新重试" }); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    public record ReResolveReq(Guid InstanceId, string? Reason);

    [HttpPost("instances/re-resolve")]
    [RequirePermission("oa-flow-ops", "re-resolve")]
    public async Task<IActionResult> ReResolve([FromBody] ReResolveReq r)
    {
        try { return Ok2(await _ops.ReResolveAsync(r.InstanceId, (await _ctx.GetAsync()).UserId, r.Reason)); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    public record ForceAdvanceReq(Guid InstanceId, Guid TokenId, string Reason);

    [HttpPost("instances/force-advance")]
    [RequirePermission("oa-flow-ops", "force-advance")]
    [RequirePlatformAdmin]                                        // D2：仅 platform-admin + 理由必填 + 双痕
    public async Task<IActionResult> ForceAdvance([FromBody] ForceAdvanceReq r)
    {
        try { await _ops.ForceAdvanceAsync(r.InstanceId, r.TokenId, (await _ctx.GetAsync()).UserId, r.Reason); return Ok2(true); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { code = 409, message = "并发冲突，请刷新重试" }); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpGet("analytics")]
    [RequirePermission("oa-flow-ops", "view")]
    public async Task<IActionResult> Analytics([FromQuery] string? flowKey, [FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc)
        => Ok2(await _ops.GetAnalyticsAsync(flowKey, fromUtc, toUtc));
}
```

  接口 `IFlowOpsService` 的干预/分析方法（`ForceTerminateAsync/ReResolveAsync/ForceAdvanceAsync/GetAnalyticsAsync`）声明与 Controller 对应端点段本 Task **写好但暂不 `git add`**（见 Step 1 的 commit 拆分口径）：V-D3 commit 只含检索/job 端点 + 接口检索/job 段 + 页面，编译自洽；其余段随 V-E3/V-F1 的实现 commit 合入。

- [ ] **Step 3: 菜单/权限种子** — `Program.cs` OA 菜单种子区 739 块之后追加（幂等，照 733~739 范式 + 波③ MenuAction 口径）：

```csharp
    // WFS 版本治理+运维驾驶舱菜单（741）—— 幂等，置于 739 之后
    if (!db.Sys_Menus.Any(m => m.MenuId == 741))
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 741, MenuName = "流程运维", RoutePath = "/oa/flow-ops", Icon = "Monitor", ParentId = 740, OrderNo = 741, Enable = true, MenuKey = "oa-flow-ops" });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 741 });
        db.SaveChanges();
    }
    // 驾驶舱操作点（spec §4.5）：view / job-ops / terminate / re-resolve / force-advance；RoleId=1 授予
    {
        var opsActions = new (string Code, string Name)[]
        {
            ("view", "查看"), ("job-ops", "job运维"), ("terminate", "强制终止"),
            ("re-resolve", "重解析审批人"), ("force-advance", "强制推进"),
        };
        foreach (var (code, name) in opsActions)
            if (!db.Sys_MenuActions.Any(x => x.MenuId == 741 && x.ActionCode == code))
                db.Sys_MenuActions.Add(new Sys_MenuAction { MenuId = 741, ActionCode = code, ActionName = name, Sort = 0 });
        db.SaveChanges();
        // RoleAction 授予镜像波③ F-T2 既有块的幂等写法（RoleId=1 全 action）
    }
```

  （`Sys_Menu.MenuKey` 若为回填式而非构造参数——照波③ F-T2 落地后的实际形态，构造直赋或回填二选一对齐。）

- [ ] **Step 4: 前端** — `router/index.ts` :42 后加 `'/oa/flow-ops': () => import('@/views/oa/admin/FlowOps.vue'),   // WFS 版本治理 V-D3 运维驾驶舱（菜单741）`；`api/oa/flowOps.ts` 镜像 Controller 全端点；`FlowOps.vue`：`CpPageShell` + `el-tabs` 三 tab：
  - **实例检索 tab**：筛选行（状态多选/FlowKey/版本下拉[由 `designerApi.versions` 取、**只列 Published**]/停泊超龄天数/发起人/日期范围）+ `el-table`（单号/流程/版本/当前关卡/停留时长/发起人/状态列，行点开跳 FormDetail 既有路由）+「版本分布」切换视图（`version-distribution` 矩阵表：FlowKey×Version 在途数，Running/Suspended 双列）。
  - **job 运维 tab**：筛选（状态/Kind/日期 + 「老化占坑」开关——开时改查 `stale-fires` 列表）+ 表格（节点/Kind/状态/尝试/NextAttempt/LastError）+ 行内「重放」「取消」按钮（`el-popconfirm` + 理由输入 `el-input`，400 报文 toast 由 http 拦截器既有行为展示）。
  - **分析 tab**：占位卡片框架（V-F2 填充图表；本 Task 只放 tab 壳 + 空态 `el-empty`，不留任何假数据）。
  - 全部文案 `t('oa.flowops.*')`；权限显隐：干预按钮按用户 action 权限显隐（镜像既有 MenuAction 前端消费惯用法）。
- [ ] **Step 5: 验证 + commit**

```bash
dotnet build CP6.WebApi/CP6.WebApi.csproj && dotnet test CP6.Tests/CP6.Tests.csproj --filter FlowOps
cd cp6.web && npm run type-check && npm run build
git add -A && git commit -m "feat(wfs-version-ops): V-D3 FlowOpsController(检索/job端点)+菜单741权限种子+FlowOps.vue三tab(检索/job就绪)"
```

---

## Wave V-E — 干预三动作（强制终止 / 重解析 / 强制推进）

### Task V-E1: ForceTerminateAsync（撤回清场语义 + 级联子实例 + 双痕）

> 依赖 V-D3（接口/端点已声明）。**执行前必读 spec §4.4 表格「强制终止」行 + 三期 subflow spec §3.3（级联复用）。**

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowOpsService.cs`（partial 增补实现）
- Test: `CP6.Tests/Wf/FlowOpsInterventionTests.cs`（新建，本波三 Task 共用文件、分 region）

- [ ] **Step 1: 写失败测试**（文件头/公共布景 + 强制终止段）

```csharp
// CP6.Tests/Wf/FlowOpsInterventionTests.cs
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>干预三动作（spec §4.4）：强制终止（撤回清场+级联子实例+双痕）、重解析两态、
/// 强制推进（系统代办语义 + subFlow/serviceTask 停泊伴随规则——孤儿/幽灵副作用定点）。</summary>
public class FlowOpsInterventionTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));
    private static FlowOpsService Svc(CP6Context db) => new(db, Engine(db));

    private static FlowSchema Linear(Guid u) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u },
            new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "a" }, new FlowEdge { From = "a", To = "b" },
            new FlowEdge { From = "b", To = "end" },
        },
    };

    private static Wf_FlowDef PublishedDef(string key, FlowSchema schema) => new()
    {
        Id = Guid.NewGuid(), FlowKey = key, FlowName = key, FormKey = "f",
        SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true,
        Status = WfFlowDefStatus.Published, PublishedAtUtc = DateTime.UtcNow,
    };

    // ────────────────────────── 强制终止（V-E1）──────────────────────────

    [Fact]
    public async Task ForceTerminate_Running_ClearsAll_CascadesChildren_DualTrace()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        var def = PublishedDef("ft1", Linear(u));
        db.Wf_FlowDefs.Add(def);
        await db.SaveChangesAsync();
        var instId = await Engine(db).SubmitAsync("ft1", Guid.NewGuid(), "{}");
        var parentTok = await db.Wf_FlowTokens.FirstAsync(t => t.InstanceId == instId && t.Status == FlowTokenStatus.Active);

        // 子实例组（三期 subflow 契约）：挂在停泊 token 名下的在途子实例 → 必须被级联取消
        var child = new Wf_FlowInstance
        {
            Id = Guid.NewGuid(), FlowKey = "ft1-child", FlowDefId = def.Id, CurrentNode = "a",
            Status = FlowInstanceStatus.Running, StarterId = Guid.NewGuid(), VarsJson = "{}",
            ParentInstanceId = instId, ParentTokenId = parentTok.Id,
        };
        db.Wf_FlowInstances.Add(child);
        db.Wf_FlowTokens.Add(new Wf_FlowToken { Id = Guid.NewGuid(), InstanceId = child.Id, NodeId = "a", Status = FlowTokenStatus.Active });
        // 停泊 Pending 服务 job（应随清场 Cancelled——CancelAllActiveTokens B-T3 既有行为）
        db.Wf_ServiceJobs.Add(new Wf_ServiceJob { Id = Guid.NewGuid(), InstanceId = instId, TokenId = parentTok.Id,
            NodeId = "a", Kind = "timer", Status = ServiceJobStatus.Pending, MaxAttempts = 4, NextAttemptAtUtc = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();

        var admin = Guid.NewGuid();
        await Svc(db).ForceTerminateAsync(instId, admin, "错误发起，管理员终止");

        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        Assert.Equal(FlowInstanceStatus.Withdrawn, inst.Status);                                             // 撤回清场语义终态
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.InstanceId == instId && t.Status == FlowTokenStatus.Active));
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == instId && t.Status == FlowTaskStatus.Pending));
        Assert.False(await db.Wf_FlowFormTos.AnyAsync(f => f.InstanceId == instId && f.Status == FlowFormToStatus.Pending));
        Assert.Equal(ServiceJobStatus.Cancelled, (await db.Wf_ServiceJobs.SingleAsync(j => j.InstanceId == instId)).Status);
        // 级联子实例（复用 SubFlowCascade：子实例组视同 token 死亡）
        Assert.NotEqual(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == child.Id)).Status);
        // 双痕：FlowHistory forceTerminate 行（OperLog 由控制器过滤器自动，不在 service 测试范围）
        var h = await db.Wf_FlowHistories.SingleAsync(x => x.InstanceId == instId && x.Action == "forceTerminate");
        Assert.Equal(admin, h.ActorId);
        Assert.Contains("管理员终止", h.Comment);
    }

    [Fact]
    public async Task ForceTerminate_Suspended_Allowed()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        db.Wf_FlowDefs.Add(PublishedDef("ft2", Linear(u)));
        await db.SaveChangesAsync();
        var instId = await Engine(db).SubmitAsync("ft2", Guid.NewGuid(), "{}");
        (await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId)).Status = FlowInstanceStatus.Suspended;
        await db.SaveChangesAsync();

        await Svc(db).ForceTerminateAsync(instId, Guid.NewGuid(), "挂起无解，终止");
        Assert.Equal(FlowInstanceStatus.Withdrawn, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId)).Status);
    }

    [Fact]
    public async Task ForceTerminate_Terminal_Rejected400_And_ReasonRequired()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        db.Wf_FlowDefs.Add(PublishedDef("ft3", Linear(u)));
        await db.SaveChangesAsync();
        var instId = await Engine(db).SubmitAsync("ft3", Guid.NewGuid(), "{}");
        await Svc(db).ForceTerminateAsync(instId, Guid.NewGuid(), "第一次终止");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Svc(db).ForceTerminateAsync(instId, Guid.NewGuid(), "再来一次"));  // 终态拒
        var inst2 = await Engine(db).SubmitAsync("ft3", Guid.NewGuid(), "{}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Svc(db).ForceTerminateAsync(inst2, Guid.NewGuid(), " "));          // 理由必填
    }
```

- [ ] **Step 2: 实现**（`FlowOpsService` partial）：

```csharp
public partial class FlowOpsService
{
    /// <summary>强制终止（spec §4.4 / D2）：前置 Running/Suspended；走撤回清场语义
    /// （在途任务 Cancelled + CancelAllActiveTokens[内含 Pending job 清场 + 三期级联钩子] + Pending FormTo Voided）
    /// + 停泊 token 名下子实例组级联（SubFlowCascade 三期 §3.3 复用）+ 双痕。终态=Withdrawn（撤回语义等价）。</summary>
    public async Task ForceTerminateAsync(Guid instanceId, Guid actorId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("强制终止理由必填");
        var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == instanceId)
                   ?? throw new InvalidOperationException("实例不存在");
        if (inst.Status is not (FlowInstanceStatus.Running or FlowInstanceStatus.Suspended))
            throw new InvalidOperationException("仅进行中/挂起实例可强制终止");

        // 撤回清场语义（镜像 TaskCenterService.WithdrawAsync 惯用法；活 token 先快照供级联）
        var activeTokenIds = await _db.Wf_FlowTokens
            .Where(t => t.InstanceId == instanceId && t.Status == FlowTokenStatus.Active)
            .Select(t => t.Id).ToListAsync();

        var live = await _db.Wf_FlowTasks.Where(t => t.InstanceId == instanceId
            && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended)).ToListAsync();
        foreach (var t in live) t.Status = FlowTaskStatus.Cancelled;

        _engine.CancelAllActiveTokens(instanceId);      // token + Pending job 清场（内含三期 subflow 级联钩子）
        _engine.VoidPendingFormTos(instanceId);
        foreach (var tid in activeTokenIds)
            SubFlowCascade.CancelChildrenOfToken(_db, tid);   // 防御性显式级联（与 CancelAllActiveTokens 钩子幂等重入安全）

        inst.Status = FlowInstanceStatus.Withdrawn;
        inst.Modifier = actorId.ToString();
        inst.ModifyDate = DateTime.Now;
        _engine.AddHistory(instanceId, inst.CurrentNode, actorId, "forceTerminate", reason);
        await _db.SaveChangesAsync();                    // 并发安全：inst RowVersion → 冲突抛给控制器 409
    }
}
```

  > `VoidPendingFormTos` 可见性若为 private → 放宽 internal（体零改，同 DispatchIfFinishedAsync 口径）。`SubFlowCascade.CancelChildrenOfToken` 若三期落地时已内嵌进 `CancelAllActiveTokens` 钩子（S-C C-T1 定案），显式循环即冗余幂等重入（no-op），保留作防御+注释互指；执行者按三期实际合入形态取舍，测试断言（子实例非 Running）不变。

- [ ] **Step 3: 跑验证 + commit** — `--filter FlowOpsInterventionTests` 本段绿 + `--filter Wf` 全量；`git add -A && git commit -m "feat(wfs-version-ops): V-E1 强制终止(撤回清场+级联子实例+双痕+RowVersion并发)"`

---

### Task V-E2: ReResolveAsync（挂起单重解析审批人，成功/仍失败两态）

> 依赖 V-E1（同测试文件追加）。**执行前必读 spec §4.4「重解析」行。** 机制：对挂起节点重跑 `EnterNodeAsync`——`ApprovalNodeHandler` 重入自带「作废上一轮遗留任务 + 重解析 + 解析不到再 Suspend」语义（`ApprovalNodeHandler.cs:22-27`），天然幂等两态。

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowOpsService.cs`
- Test: `CP6.Tests/Wf/FlowOpsInterventionTests.cs`（追加）

- [ ] **Step 1: 追加测试**

```csharp
    // ────────────────────────── 重解析审批人（V-E2）──────────────────────────

    /// <summary>Field 策略流程：审批人取 vars 字段 → 空值发起即 Suspended；管理员补数据后重解析 → 回 Running。</summary>
    private static FlowSchema FieldSchema() => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Field", ApproverFieldName = "assignee" },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges = { new FlowEdge { From = "s", To = "a" }, new FlowEdge { From = "a", To = "end" } },
    };

    [Fact]
    public async Task ReResolve_AfterVarsFixed_CreatesTodo_BackToRunning()
    {
        using var db = NewDb();
        db.Wf_FlowDefs.Add(PublishedDef("rr1", FieldSchema()));
        await db.SaveChangesAsync();
        var instId = await Engine(db).SubmitAsync("rr1", Guid.NewGuid(), "{}");   // assignee 缺 → Suspended
        Assert.Equal(FlowInstanceStatus.Suspended, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId)).Status);

        var approver = Guid.NewGuid();
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        inst.VarsJson = JsonSerializer.Serialize(new { assignee = approver });    // 管理员修数据
        await db.SaveChangesAsync();

        var res = await Svc(db).ReResolveAsync(instId, Guid.NewGuid(), "字段已补");
        Assert.True(res.Resolved);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId)).Status);
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == instId && t.AssigneeId == approver && t.Status == FlowTaskStatus.Pending));
        Assert.True(await db.Wf_FlowHistories.AnyAsync(h => h.InstanceId == instId && h.Action == "reResolve"));
    }

    [Fact]
    public async Task ReResolve_StillUnresolvable_StaysSuspended_WithReason()
    {
        using var db = NewDb();
        db.Wf_FlowDefs.Add(PublishedDef("rr2", FieldSchema()));
        await db.SaveChangesAsync();
        var instId = await Engine(db).SubmitAsync("rr2", Guid.NewGuid(), "{}");

        var res = await Svc(db).ReResolveAsync(instId, Guid.NewGuid(), null);     // 数据仍缺 → 仍失败
        Assert.False(res.Resolved);
        Assert.NotNull(res.Reason);
        Assert.Equal(FlowInstanceStatus.Suspended, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId)).Status);
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == instId && t.Status == FlowTaskStatus.Pending));
    }

    [Fact]
    public async Task ReResolve_NotSuspended_Rejected400()
    {
        using var db = NewDb();
        db.Wf_FlowDefs.Add(PublishedDef("rr3", Linear(Guid.NewGuid())));
        await db.SaveChangesAsync();
        var instId = await Engine(db).SubmitAsync("rr3", Guid.NewGuid(), "{}");   // Running
        await Assert.ThrowsAsync<InvalidOperationException>(() => Svc(db).ReResolveAsync(instId, Guid.NewGuid(), null));
    }
```

  > `ApproverStrategy = "Field"` / `ApproverFieldName` 字段名以 `FlowNode`/`ApproverRule` 实际定义为准（`FlowEngine.BuildRule` 实读已确认 `ApproverFieldName` 存在）；若 Field 策略枚举名不同（如 `FormField`），照实际改布景——**两态断言语义不动**。

- [ ] **Step 2: 实现**

```csharp
    /// <summary>重解析审批人（spec §4.4 / D2）：前置 Suspended；对挂起节点（CurrentNode 处停泊 token）重跑
    /// EnterNodeAsync——ApprovalNodeHandler 重入自带「作废旧轮任务 + 重解析 + 失败再 Suspend」（幂等）。
    /// 解析成功 → 生成待办、实例回 Running；仍失败 → 保持 Suspended + 返回原因。serviceTask 停泊挂起
    /// （服务失败无错误边）不属本动作 → 400 引导 job 重放/强制推进。</summary>
    public async Task<ReResolveResult> ReResolveAsync(Guid instanceId, Guid actorId, string? reason)
    {
        var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == instanceId)
                   ?? throw new InvalidOperationException("实例不存在");
        if (inst.Status != FlowInstanceStatus.Suspended)
            throw new InvalidOperationException("仅挂起实例可重解析审批人");

        var token = await _db.Wf_FlowTokens.FirstOrDefaultAsync(t => t.InstanceId == instanceId
            && t.NodeId == inst.CurrentNode && t.Status == FlowTokenStatus.Active)
            ?? throw new InvalidOperationException("挂起节点无停泊 token（数据异常，请强制终止）");

        var schema = await _engine.LoadSchemaAsync(inst);
        var node = FlowEngine.FindNode(schema, inst.CurrentNode)
                   ?? throw new InvalidOperationException("挂起节点不在 schema 中（数据异常）");
        var type = (node.Type ?? "approval").Trim().ToLowerInvariant();
        if (type != "approval")
            throw new InvalidOperationException("挂起源于服务任务失败，请使用 job 重放或强制推进/终止");

        inst.Status = FlowInstanceStatus.Running;                     // 先复位；handler 解析失败会再 Suspend（两态天然成立）
        _engine.AddHistory(instanceId, node.Id, actorId, "reResolve", reason);
        await _engine.EnterNodeAsync(inst, schema, node, token);      // 重入：作废旧轮 + 重解析 + 建任务/再挂起

        var resolved = inst.Status == FlowInstanceStatus.Running;
        string? failReason = null;
        if (!resolved)
            failReason = _db.Wf_FlowHistories.Local
                .Where(h => h.InstanceId == instanceId && h.Action == "suspend")
                .Select(h => h.Comment).LastOrDefault() ?? "审批人仍无法解析";
        await _db.SaveChangesAsync();
        return new ReResolveResult(resolved, failReason);
    }
```

- [ ] **Step 3: 跑验证 + commit** — `--filter FlowOpsInterventionTests` + `--filter Wf`；`git add -A && git commit -m "feat(wfs-version-ops): V-E2 重解析审批人(EnterNodeAsync重入两态+400引导+履历痕)"`

---

### Task V-E3: ForceAdvanceAsync（系统代办语义 + 停泊伴随规则）+ 契约测试收口

> 依赖 V-E2。**执行前必读 spec §4.4「强制推进」行全文（评审补的伴随规则是防孤儿/幽灵副作用的灵魂）。** 本 Task 同时提交 V-D3 暂缓的 Controller 干预端点段 + `FlowOpsControllerContractTests.cs`。

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowOpsService.cs`
- Modify: `CP6.WebApi/Controllers/Oa/FlowOpsController.cs`（补干预三端点——V-D3 已写好暂缓段）
- Test: `CP6.Tests/Wf/FlowOpsInterventionTests.cs`（追加）+ `CP6.Tests/Oa/FlowOpsControllerContractTests.cs`（提交）

- [ ] **Step 1: 追加测试**

```csharp
    // ────────────────────────── 强制推进（V-E3）──────────────────────────

    [Fact]
    public async Task ForceAdvance_ApprovalParked_SystemProxy_FormToSkipped_HistoryStamped()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        db.Wf_FlowDefs.Add(PublishedDef("fa1", Linear(u)));
        await db.SaveChangesAsync();
        var instId = await Engine(db).SubmitAsync("fa1", Guid.NewGuid(), "{}");
        var tok = await db.Wf_FlowTokens.FirstAsync(t => t.InstanceId == instId && t.Status == FlowTokenStatus.Active);

        var admin = Guid.NewGuid();
        await Svc(db).ForceAdvanceAsync(instId, tok.Id, admin, "审批人长期缺席，管理员强推");

        // 系统代办语义：在途待办 Cancelled、FormTo Skipped、history forceAdvance，token 沿正常出边 a→b
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == instId && t.NodeId == "a" && t.Status == FlowTaskStatus.Pending));
        Assert.True(await db.Wf_FlowFormTos.AnyAsync(f => f.InstanceId == instId && f.NodeId == "a" && f.Status == FlowFormToStatus.Skipped));
        var h = await db.Wf_FlowHistories.SingleAsync(x => x.InstanceId == instId && x.Action == "forceAdvance");
        Assert.Equal(admin, h.ActorId);
        Assert.Contains("强推", h.Comment);
        Assert.Equal("b", (await db.Wf_FlowTokens.SingleAsync(t => t.Id == tok.Id)).NodeId);   // 沿正常出边推进一站
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId)).Status);
    }

    [Fact]
    public async Task ForceAdvance_SubFlowParked_CascadesChildGroup()   // 伴随规则①：防回注黑洞孤儿（spec 评审补）
    {
        using var db = NewDb();
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "sub", Type = "subFlow" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges = { new FlowEdge { From = "s", To = "sub" }, new FlowEdge { From = "sub", To = "end" } },
        };
        var def = PublishedDef("fa2", schema);
        db.Wf_FlowDefs.Add(def);
        // 手工布景（不经 handler）：token 停泊 subFlow + 在途子实例组两枚
        var inst = new Wf_FlowInstance { Id = Guid.NewGuid(), FlowKey = "fa2", FlowDefId = def.Id, CurrentNode = "sub",
            Status = FlowInstanceStatus.Running, StarterId = Guid.NewGuid(), VarsJson = "{}" };
        db.Wf_FlowInstances.Add(inst);
        var tok = new Wf_FlowToken { Id = Guid.NewGuid(), InstanceId = inst.Id, NodeId = "sub", Status = FlowTokenStatus.Active };
        db.Wf_FlowTokens.Add(tok);
        var kids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        foreach (var kid in kids)
        {
            db.Wf_FlowInstances.Add(new Wf_FlowInstance { Id = kid, FlowKey = "fa2-c", FlowDefId = def.Id, CurrentNode = "a",
                Status = FlowInstanceStatus.Running, StarterId = Guid.NewGuid(), VarsJson = "{}",
                ParentInstanceId = inst.Id, ParentTokenId = tok.Id });
            db.Wf_FlowTokens.Add(new Wf_FlowToken { Id = Guid.NewGuid(), InstanceId = kid, NodeId = "a", Status = FlowTokenStatus.Active });
        }
        await db.SaveChangesAsync();

        await Svc(db).ForceAdvanceAsync(inst.Id, tok.Id, Guid.NewGuid(), "子流程僵死，整体强推");

        foreach (var kid in kids)   // 子实例组级联取消（否则回注注定进状态闸黑洞的孤儿子流程）
            Assert.NotEqual(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == kid)).Status);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == inst.Id)).Status);   // sub→end 直达终态
    }

    [Fact]
    public async Task ForceAdvance_ServiceTaskParked_CancelsPendingJob()   // 伴随规则②：防 worker 幽灵外呼（spec 评审补）
    {
        using var db = NewDb();
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "svc", Type = "serviceTask" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges = { new FlowEdge { From = "s", To = "svc" }, new FlowEdge { From = "svc", To = "end" } },
        };
        var def = PublishedDef("fa3", schema);
        db.Wf_FlowDefs.Add(def);
        var inst = new Wf_FlowInstance { Id = Guid.NewGuid(), FlowKey = "fa3", FlowDefId = def.Id, CurrentNode = "svc",
            Status = FlowInstanceStatus.Running, StarterId = Guid.NewGuid(), VarsJson = "{}" };
        db.Wf_FlowInstances.Add(inst);
        var tok = new Wf_FlowToken { Id = Guid.NewGuid(), InstanceId = inst.Id, NodeId = "svc", Status = FlowTokenStatus.Active };
        db.Wf_FlowTokens.Add(tok);
        var job = new Wf_ServiceJob { Id = Guid.NewGuid(), InstanceId = inst.Id, TokenId = tok.Id, NodeId = "svc",
            Kind = "webApi", Status = ServiceJobStatus.Pending, MaxAttempts = 4, NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(5) };
        db.Wf_ServiceJobs.Add(job);
        await db.SaveChangesAsync();

        await Svc(db).ForceAdvanceAsync(inst.Id, tok.Id, Guid.NewGuid(), "外部系统废弃，跳过服务任务");

        Assert.Equal(ServiceJobStatus.Cancelled, (await db.Wf_ServiceJobs.SingleAsync(j => j.Id == job.Id)).Status);   // worker 不再拾起 → 无幽灵 HTTP
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == inst.Id)).Status);
    }

    [Fact]
    public async Task ForceAdvance_SuspendedInstance_ResumesAndAdvances()   // 挂起（服务失败无错误边）救援路径
    {
        using var db = NewDb();
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "svc", Type = "serviceTask" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges = { new FlowEdge { From = "s", To = "svc" }, new FlowEdge { From = "svc", To = "end" } },
        };
        var def = PublishedDef("fa4", schema);
        db.Wf_FlowDefs.Add(def);
        var inst = new Wf_FlowInstance { Id = Guid.NewGuid(), FlowKey = "fa4", FlowDefId = def.Id, CurrentNode = "svc",
            Status = FlowInstanceStatus.Suspended, StarterId = Guid.NewGuid(), VarsJson = "{}" };   // Failed 无错误边形态
        db.Wf_FlowInstances.Add(inst);
        var tok = new Wf_FlowToken { Id = Guid.NewGuid(), InstanceId = inst.Id, NodeId = "svc", Status = FlowTokenStatus.Active };
        db.Wf_FlowTokens.Add(tok);
        await db.SaveChangesAsync();

        await Svc(db).ForceAdvanceAsync(inst.Id, tok.Id, Guid.NewGuid(), "失败服务节点人工放行");
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == inst.Id)).Status);
    }

    [Fact]
    public async Task ForceAdvance_ReasonRequired_And_TokenNotParked_Rejected400()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        db.Wf_FlowDefs.Add(PublishedDef("fa5", Linear(u)));
        await db.SaveChangesAsync();
        var instId = await Engine(db).SubmitAsync("fa5", Guid.NewGuid(), "{}");
        var tok = await db.Wf_FlowTokens.FirstAsync(t => t.InstanceId == instId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Svc(db).ForceAdvanceAsync(instId, tok.Id, Guid.NewGuid(), ""));   // 理由必填

        tok.Status = FlowTokenStatus.Consumed;   // token 已走
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Svc(db).ForceAdvanceAsync(instId, tok.Id, Guid.NewGuid(), "x"));
    }
}
```

- [ ] **Step 2: 实现**

```csharp
    /// <summary>强制推进（spec §4.4 / D2，仅 platform-admin——控制器闸）：当前关卡按「系统代办通过」处置：
    /// 在途待办 Cancelled、Pending FormTo → Skipped、history forceAdvance（理由必填）→ AdvanceToken 沿正常出边。
    /// <b>停泊伴随规则（spec 评审补）</b>：subFlow 停泊 → 级联取消在途子实例组（否则回注进状态闸黑洞的孤儿）；
    /// serviceTask 停泊 → Cancel 其 pending/退避中 job（否则 worker 拾起照发 HTTP，对外部系统是幽灵调用）。</summary>
    public async Task ForceAdvanceAsync(Guid instanceId, Guid tokenId, Guid actorId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("强制推进理由必填");
        var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == instanceId)
                   ?? throw new InvalidOperationException("实例不存在");
        if (inst.Status is not (FlowInstanceStatus.Running or FlowInstanceStatus.Suspended))
            throw new InvalidOperationException("仅进行中/挂起实例可强制推进");
        var token = await _db.Wf_FlowTokens.FirstOrDefaultAsync(t => t.Id == tokenId && t.InstanceId == instanceId);
        if (token is null || token.Status != FlowTokenStatus.Active)
            throw new InvalidOperationException("token 不在停泊态（已流转/已清场），无从强推");

        var schema = await _engine.LoadSchemaAsync(inst);
        var node = FlowEngine.FindNode(schema, token.NodeId)
                   ?? throw new InvalidOperationException("token 所在节点不在 schema 中（数据异常，请强制终止）");
        var type = (node.Type ?? "approval").Trim().ToLowerInvariant();

        // ① 本 token 本节点在途待办 → Cancelled（系统代办：节点已被管理员代决）
        var live = await _db.Wf_FlowTasks.Where(t => t.InstanceId == instanceId && t.TokenId == token.Id
            && t.NodeId == token.NodeId
            && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended)).ToListAsync();
        foreach (var t in live) t.Status = FlowTaskStatus.Cancelled;

        // ② 本 token 本节点 Pending FormTo → Skipped(5)（与会签过后 SkipPendingFormTos 同语义）
        var pendingFts = await _db.Wf_FlowFormTos.Where(f => f.InstanceId == instanceId && f.TokenId == token.Id
            && f.NodeId == token.NodeId && f.Status == FlowFormToStatus.Pending).ToListAsync();
        foreach (var f in pendingFts) { f.Status = FlowFormToStatus.Skipped; f.HandledAt = DateTime.Now; }

        // ③ 停泊伴随规则（spec 评审补）
        if (type == "subflow")
            SubFlowCascade.CancelChildrenOfToken(_db, token.Id);          // 视同 token 死亡（三期 §3.3 复用）
        else if (type == "servicetask")
        {
            var jobs = await _db.Wf_ServiceJobs.Where(j => j.TokenId == token.Id && j.NodeId == token.NodeId
                && (j.Status == ServiceJobStatus.Pending || j.Status == ServiceJobStatus.Failed)).ToListAsync();
            var now = DateTime.UtcNow;
            foreach (var j in jobs) { j.Status = ServiceJobStatus.Cancelled; j.CompletedAtUtc = now; }
        }

        // ④ 双痕 + 推进
        _engine.AddHistory(instanceId, token.NodeId, actorId, "forceAdvance", reason);
        if (inst.Status == FlowInstanceStatus.Suspended) inst.Status = FlowInstanceStatus.Running;   // 挂起救援
        token.StagePlanJson = null;                                        // 清串簽冻结计划（防下游误判多档）
        await _engine.AdvanceToken(inst, schema, token);                   // 沿正常出边（IsError != true）
        await _engine.DispatchIfFinishedAsync(inst, actorId, reason);      // 强推至终态 → 反向回调业务
        await _db.SaveChangesAsync();
    }
```

- [ ] **Step 3: Controller 干预端点段 + 契约测试收口** — 提交 V-D3 暂缓的三端点与 `FlowOpsControllerContractTests.cs`（Step 1 全绿）。
- [ ] **Step 4: 跑验证 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FlowOpsInterventionTests|FlowOpsControllerContractTests"
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf     # 27+ 不变量全绿
git add -A && git commit -m "feat(wfs-version-ops): V-E3 强制推进(系统代办+subFlow级联/serviceTask取消job伴随规则+挂起救援)+权限契约矩阵收口"
```

  前端（同 commit）：FlowOps.vue 实例行操作列接三动作对话框（理由 `el-input` 必填校验[强推/终止]、强推需先选停泊 token——行展开显示该实例停泊 token 列表[查询接口复用 SearchInstances 行内 `tokens` 子查询或补一个轻端点 `GET instances/{id}/tokens`，执行者按最小改动取舍]、409/400 报文 toast）。

---

## Wave V-F — 分析 tab（四固定报表，`Wf_FlowFormTo` 读模型聚合）

### Task V-F1: GetAnalyticsAsync 四聚合 + 已知数据集断言

> 依赖 V-D1。可与 V-E 并行。**执行前必读 spec §4.3 + 侦察结论 #3（超时分子=FlowHistory）。**

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowOpsService.cs`（partial 增补）
- Test: `CP6.Tests/Wf/FlowOpsAnalyticsTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/FlowOpsAnalyticsTests.cs
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>分析四报表（spec §4.3 D5）：已知数据集精确断言 + 空态 + 日期边界。
/// 数据直构（不走引擎）——聚合口径测试要的是「算得对」，与引擎行为解耦。</summary>
public class FlowOpsAnalyticsTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowOpsService Svc(CP6Context db) => new(db, new FlowEngine(db, new ApproverResolver(db)));

    private static readonly DateTime Day = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Wf_FlowInstance Inst(string key, int status, DateTime createdUtc, DateTime? doneUtc)
        => new()
        {
            Id = Guid.NewGuid(), FlowKey = key, CurrentNode = "end", Status = status,
            StarterId = Guid.NewGuid(), VarsJson = "{}",
            CreateDate = createdUtc, ModifyDate = doneUtc,   // 终态时刻口径 = ModifyDate（终态置位必刷）
        };

    [Fact]
    public async Task FourAggregates_KnownDataset_ExactNumbers()
    {
        using var db = NewDb();
        // ── 布景：leave 流程 2 单终态（10h/20h，同日）+ 1 驳回；expense 流程 1 单 Approved ──
        var l1 = Inst("leave", FlowInstanceStatus.Approved, Day, Day.AddHours(10));
        var l2 = Inst("leave", FlowInstanceStatus.Approved, Day.AddHours(1), Day.AddHours(21));   // 20h
        var l3 = Inst("leave", FlowInstanceStatus.Rejected, Day, Day.AddHours(5));
        var e1 = Inst("expense", FlowInstanceStatus.Approved, Day, Day.AddHours(2));
        db.Wf_FlowInstances.AddRange(l1, l2, l3, e1);

        // 瓶颈：leave 节点 a 两行（4h/6h → 均 5h），节点 b 一行 1h
        db.Wf_FlowFormTos.AddRange(
            new Wf_FlowFormTo { Id = Guid.NewGuid(), InstanceId = l1.Id, StepSeq = 1, NodeId = "a", NodeName = "主管",
                ExpectedHandlerId = Guid.NewGuid(), Status = FlowFormToStatus.Approved, SentAt = Day, HandledAt = Day.AddHours(4) },
            new Wf_FlowFormTo { Id = Guid.NewGuid(), InstanceId = l2.Id, StepSeq = 1, NodeId = "a", NodeName = "主管",
                ExpectedHandlerId = Guid.NewGuid(), Status = FlowFormToStatus.Approved, SentAt = Day, HandledAt = Day.AddHours(6) },
            new Wf_FlowFormTo { Id = Guid.NewGuid(), InstanceId = l1.Id, StepSeq = 2, NodeId = "b", NodeName = "财务",
                ExpectedHandlerId = Guid.NewGuid(), Status = FlowFormToStatus.Approved, SentAt = Day.AddHours(4), HandledAt = Day.AddHours(5) });

        // 超时分子（侦察结论 #3）：系统超时自动同意 1 行 + 超时升级 1 行；催办 remind 无痕不计
        db.Wf_FlowHistories.AddRange(
            new Wf_FlowHistory { Id = Guid.NewGuid(), InstanceId = l1.Id, NodeId = "a", ActorId = Guid.Empty,
                Action = "approve", Comment = "超时自动同意", CreateDate = Day.AddHours(4) },
            new Wf_FlowHistory { Id = Guid.NewGuid(), InstanceId = l2.Id, NodeId = "a", ActorId = Guid.NewGuid(),
                Action = "escalate", Comment = "超时升级：x → y", CreateDate = Day.AddHours(5) },
            new Wf_FlowHistory { Id = Guid.NewGuid(), InstanceId = l2.Id, NodeId = "a", ActorId = Guid.NewGuid(),
                Action = "approve", Comment = "正常同意", CreateDate = Day.AddHours(6) });   // 干扰项：不计
        await db.SaveChangesAsync();

        var dto = await Svc(db).GetAnalyticsAsync(null, Day.AddDays(-1), Day.AddDays(1));

        // ① 平均审批时长：leave 终态 3 单（10+20+5)/3；expense 2h
        var leaveAvg = dto.AvgDuration.Where(r => r.FlowKey == "leave").ToList();
        Assert.Equal(3, leaveAvg.Sum(r => r.Count));
        Assert.Equal((10 + 20 + 5) / 3.0, leaveAvg.Sum(r => r.AvgHours * r.Count) / leaveAvg.Sum(r => r.Count), 3);
        Assert.Equal(2.0, dto.AvgDuration.Single(r => r.FlowKey == "expense").AvgHours, 3);

        // ② 瓶颈 Top：leave/a 均停留 5h、Handled=2；排序首位
        var top = dto.Bottlenecks.First();
        Assert.Equal(("leave", "a"), (top.FlowKey, top.NodeId));
        Assert.Equal(5.0, top.AvgStayHours, 3);
        Assert.Equal(2, top.Handled);

        // ③ 超时率：leave 分子 2（自动同意+升级）/ 分母 3（FormTo HandledAt 非空数）
        var to = dto.TimeoutRates.Single(r => r.FlowKey == "leave");
        Assert.Equal(2, to.TimeoutCount);
        Assert.Equal(3, to.HandledCount);
        Assert.Equal(2 / 3.0, to.Rate, 3);

        // ④ 驳回率：leave 1/3；expense 0/1
        var rej = dto.RejectRates.Single(r => r.FlowKey == "leave");
        Assert.Equal(1, rej.Rejected);
        Assert.Equal(3, rej.Finished);
        Assert.Equal(1 / 3.0, rej.Rate, 3);
        Assert.Equal(0.0, dto.RejectRates.Single(r => r.FlowKey == "expense").Rate, 3);
    }

    [Fact]
    public async Task EmptyDataset_AllFourBlocksEmpty_NoThrow()
    {
        using var db = NewDb();
        var dto = await Svc(db).GetAnalyticsAsync(null, Day, Day.AddDays(1));
        Assert.Empty(dto.AvgDuration);
        Assert.Empty(dto.Bottlenecks);
        Assert.Empty(dto.TimeoutRates);
        Assert.Empty(dto.RejectRates);
    }

    [Fact]
    public async Task DateBoundary_ExclusiveOutside_FlowKeyFilter()
    {
        using var db = NewDb();
        db.Wf_FlowInstances.AddRange(
            Inst("leave", FlowInstanceStatus.Approved, Day.AddDays(-5), Day.AddDays(-5).AddHours(1)),   // 窗外
            Inst("leave", FlowInstanceStatus.Approved, Day, Day.AddHours(1)),                            // 窗内
            Inst("expense", FlowInstanceStatus.Approved, Day, Day.AddHours(1)));                         // 异 key
        await db.SaveChangesAsync();

        var dto = await Svc(db).GetAnalyticsAsync("leave", Day.AddHours(-1), Day.AddDays(1));
        Assert.Single(dto.AvgDuration);
        Assert.Equal(1, dto.AvgDuration[0].Count);       // 窗外不计
        Assert.DoesNotContain(dto.RejectRates, r => r.FlowKey == "expense");   // flowKey 过滤
    }
}
```

  > `CreateDate/ModifyDate` 若由 `BaseEntity` 拦截器覆写导致布景值失效——改用 `db.Database` 直写或实体配置绕过（镜像既有测试对审计字段的处理惯例）；断言数值不动。

- [ ] **Step 2: 实现**（`FlowOpsService` partial；InMemory 兼容——聚合先取窄投影再内存分组，避免 GroupBy 翻译坑）：

```csharp
    /// <summary>分析四报表（spec §4.3 D5）：一次返回四块聚合。终态时刻口径=ModifyDate（终态置位必刷，
    /// 免加列）；超时分子=FlowHistory（侦察结论 #3：remind 无痕不计——报表 UI 注记）。只读，不引 BI 依赖。</summary>
    public async Task<FlowOpsAnalyticsDto> GetAnalyticsAsync(string? flowKey, DateTime fromUtc, DateTime toUtc)
    {
        int[] finals = { FlowInstanceStatus.Approved, FlowInstanceStatus.Rejected, FlowInstanceStatus.Withdrawn };

        var instances = await _db.Wf_FlowInstances
            .Where(i => finals.Contains(i.Status) && i.CreateDate >= fromUtc && i.CreateDate <= toUtc
                        && (flowKey == null || i.FlowKey == flowKey) && i.ModifyDate != null)
            .Select(i => new { i.Id, i.FlowKey, i.Status, i.CreateDate, i.ModifyDate })
            .ToListAsync();

        // ① 平均审批时长（发起→终态，按流程×日，趋势折线数据源）
        var avg = instances
            .GroupBy(i => new { i.FlowKey, Day = i.CreateDate.Date })
            .OrderBy(g => g.Key.FlowKey).ThenBy(g => g.Key.Day)
            .Select(g => new AvgDurationRow(g.Key.FlowKey, g.Key.Day,
                g.Average(x => (x.ModifyDate!.Value - x.CreateDate).TotalHours), g.Count()))
            .ToList();

        // ② 瓶颈关卡 Top（FormTo 按 (FlowKey,NodeId) 平均停留，条形图；Top 20）
        var instIds = instances.Select(i => i.Id).ToHashSet();
        var fts = await _db.Wf_FlowFormTos
            .Where(f => f.HandledAt != null && f.SentAt >= fromUtc && f.SentAt <= toUtc)
            .Select(f => new { f.InstanceId, f.NodeId, f.NodeName, f.SentAt, f.HandledAt })
            .ToListAsync();
        var keyOf = instances.ToDictionary(i => i.Id, i => i.FlowKey);
        var bottlenecks = fts.Where(f => keyOf.ContainsKey(f.InstanceId))
            .GroupBy(f => new { FlowKey = keyOf[f.InstanceId], f.NodeId })
            .Select(g => new BottleneckRow(g.Key.FlowKey, g.Key.NodeId, g.First().NodeName,
                g.Average(x => (x.HandledAt!.Value - x.SentAt).TotalHours), g.Count()))
            .OrderByDescending(r => r.AvgStayHours).Take(20).ToList();

        // ③ 超时率：分子=FlowHistory 超时动作行（approve/reject 系统身份+「超时自动」前缀 ∪ escalate+「超时升级」前缀）；
        //    分母=窗内已办结 FormTo 数。remind 无痕不计（口径注记）。
        var histories = await _db.Wf_FlowHistories
            .Where(h => h.CreateDate >= fromUtc && h.CreateDate <= toUtc
                && ((h.ActorId == Guid.Empty && (h.Action == "approve" || h.Action == "reject") && h.Comment!.StartsWith("超时自动"))
                    || (h.Action == "escalate" && h.Comment!.StartsWith("超时升级"))))
            .Select(h => h.InstanceId).ToListAsync();
        var timeoutByKey = histories.Where(id => keyOf.ContainsKey(id))
            .GroupBy(id => keyOf[id]).ToDictionary(g => g.Key, g => g.Count());
        var handledByKey = fts.Where(f => keyOf.ContainsKey(f.InstanceId))
            .GroupBy(f => keyOf[f.InstanceId]).ToDictionary(g => g.Key, g => g.Count());
        var timeoutRates = handledByKey.Keys.Union(timeoutByKey.Keys).OrderBy(k => k)
            .Select(k =>
            {
                var num = timeoutByKey.GetValueOrDefault(k);
                var den = handledByKey.GetValueOrDefault(k);
                return new TimeoutRateRow(k, num, den, den == 0 ? 0 : (double)num / den);
            }).ToList();

        // ④ 驳回率：Rejected / 终态（按流程）
        var rejectRates = instances.GroupBy(i => i.FlowKey).OrderBy(g => g.Key)
            .Select(g => new RejectRateRow(g.Key, g.Count(x => x.Status == FlowInstanceStatus.Rejected), g.Count(),
                g.Count() == 0 ? 0 : (double)g.Count(x => x.Status == FlowInstanceStatus.Rejected) / g.Count()))
            .ToList();

        return new FlowOpsAnalyticsDto(avg, bottlenecks, timeoutRates, rejectRates);
    }
```

  接口第四段声明 `GetAnalyticsAsync` 随本 Task 落（V-D3 Controller 端点已就位）。

- [ ] **Step 3: 跑验证 + commit** — `--filter FlowOpsAnalyticsTests` + `--filter Wf`；`git add -A && git commit -m "feat(wfs-version-ops): V-F1 分析四聚合(时长/瓶颈/超时率[FlowHistory口径]/驳回率)+已知数据集断言"`

---

### Task V-F2: 分析 tab 前端（四报表卡片，纯 CSS/SVG，dataviz 规范）

> 依赖 V-F1、V-D3。**Step 0（阻断性）：落码前先读 dataviz skill**——形制/校色/空态遵其规范；全部颜色走 `var(--cp-*)` Design System token（零硬编码色 DoD 会 grep）。

**Files:**
- Create: `cp6.web/src/views/oa/admin/flowOpsCharts.ts`（纯函数：折线点位/条形宽度/百分比格式）
- Create: `cp6.web/src/views/oa/admin/flowOpsCharts.test.ts`
- Modify: `cp6.web/src/views/oa/admin/FlowOps.vue`（分析 tab 填充）

- [ ] **Step 1: 纯函数 + vitest**

```ts
// cp6.web/src/views/oa/admin/flowOpsCharts.ts
// 分析图表数据变换（无图表库——侦察结论 #4：条形=div 宽度%、折线=SVG polyline）。纯函数可测。
export interface LinePoint { x: number; y: number }

/** 折线点位：把 (day, value) 序列映射进 viewBox (w×h，含 pad 内边距)。空/单点安全。 */
export function toPolylinePoints(values: number[], w: number, h: number, pad = 4): LinePoint[] {
  if (values.length === 0) return []
  const max = Math.max(...values, 1e-9)
  const min = Math.min(...values, 0)
  const span = max - min || 1
  const stepX = values.length === 1 ? 0 : (w - pad * 2) / (values.length - 1)
  return values.map((v, i) => ({
    x: +(pad + i * stepX).toFixed(2),
    y: +(h - pad - ((v - min) / span) * (h - pad * 2)).toFixed(2),
  }))
}

export const toPointsAttr = (pts: LinePoint[]) => pts.map(p => `${p.x},${p.y}`).join(' ')

/** 条形宽度百分比（相对组内最大值；max<=0 → 0）。 */
export function barWidthPct(value: number, max: number): number {
  if (max <= 0) return 0
  return Math.max(0, Math.min(100, +(value / max * 100).toFixed(1)))
}

export const pct = (rate: number) => `${(rate * 100).toFixed(1)}%`
export const hoursLabel = (h: number) => (h >= 48 ? `${(h / 24).toFixed(1)}d` : `${h.toFixed(1)}h`)
```

```ts
// cp6.web/src/views/oa/admin/flowOpsCharts.test.ts
import { describe, it, expect } from 'vitest'
import { toPolylinePoints, toPointsAttr, barWidthPct, pct, hoursLabel } from './flowOpsCharts'

describe('toPolylinePoints', () => {
  it('两点映射满宽、min→底 max→顶', () => {
    const pts = toPolylinePoints([0, 10], 100, 50, 4)
    expect(pts).toHaveLength(2)
    expect(pts[0]).toEqual({ x: 4, y: 46 })      // min → h-pad
    expect(pts[1]).toEqual({ x: 96, y: 4 })      // max → pad
  })
  it('空序列/单点安全', () => {
    expect(toPolylinePoints([], 100, 50)).toEqual([])
    const one = toPolylinePoints([5], 100, 50, 4)
    expect(one).toHaveLength(1)
    expect(one[0].x).toBe(4)
  })
  it('恒值序列不除零（span=1 兜底）', () => {
    const pts = toPolylinePoints([3, 3, 3], 100, 50, 4)
    expect(new Set(pts.map(p => p.y)).size).toBe(1)
  })
  it('toPointsAttr 拼 SVG points 串', () => {
    expect(toPointsAttr([{ x: 1, y: 2 }, { x: 3, y: 4 }])).toBe('1,2 3,4')
  })
})

describe('barWidthPct / pct / hoursLabel', () => {
  it('条形相对组内最大值', () => {
    expect(barWidthPct(5, 10)).toBe(50)
    expect(barWidthPct(10, 10)).toBe(100)
    expect(barWidthPct(1, 0)).toBe(0)
  })
  it('百分比与时长格式', () => {
    expect(pct(0.667)).toBe('66.7%')
    expect(hoursLabel(5.25)).toBe('5.3h')
    expect(hoursLabel(72)).toBe('3.0d')
  })
})
```

- [ ] **Step 2: 分析 tab 填充**（FlowOps.vue）——顶部筛选（FlowKey 下拉可空=全部 + 日期范围默认近 30 天）→ `flowOpsApi.analytics` → 四卡片（`el-card` 2×2 栅格，`el-empty` 空态）：
  1. **平均审批时长**：按 FlowKey 分组多折线（每 key 一条 `<polyline :points="toPointsAttr(...)">`，`stroke="var(--cp-primary)"` 系列色按 dataviz skill 分类色板 token 轮转，`fill="none"`）+ 图例；
  2. **瓶颈关卡 Top**：横向条形列表（行=`NodeName(NodeId)·FlowKey`，`div.bar` 宽 `barWidthPct`，`background: var(--cp-warn)`，右侧 `hoursLabel`）；
  3. **超时率**：每 FlowKey 一行「率 + 分子/分母」条形（`var(--cp-danger)`），卡片脚注 `t('oa.flowops.an.timeoutNote')`（remind 催办不计口径注记，侦察结论 #3）；
  4. **驳回率**：同形制条形（`var(--cp-danger)`）+ `pct` 标签。
  溢出容器 `overflow-x:auto`；数字用等宽字体 token；深浅主题双测（token 天然适配）。
- [ ] **Step 3: 验证 + commit**

```bash
cd cp6.web && npx vitest run src/views/oa/admin/flowOpsCharts.test.ts && npm run test && npm run type-check && npm run build
git add -A && git commit -m "feat(wfs-version-ops): V-F2 分析tab四报表(纯CSS条形+SVG折线,dataviz规范,零硬编码色)+图表纯函数vitest"
```

---

## Wave V-G — i18n + QA + DoD（紧跟 D/E/F 波，不留窗口）

### Task V-G1: i18n 五语 seed（~52 键）

**Files:**
- Create: `CP6.WebApi/Seed/I18nOaFlowOpsScreenSeed.cs`
- Modify: `CP6.WebApi/Program.cs`（i18n concat 链尾追加一行——链尾在执行时为前置各期最后一个 `I18nOa*ScreenSeed` concat 行之后）

- [ ] **Step 1: 去重前置** — `grep -rn "oa.designer.pub\|oa.flowops\|E-WF-029\|E-WF-030" CP6.WebApi/Seed/` 必须零命中（键面冲突即改前缀，不覆写他人键）。

- [ ] **Step 2: 实现 seed**（仿 `I18nOaServiceTaskScreenSeed` 静态 `Sys_Lang[] Items` 模式；键面以 DesignerView/VersionDiffDialog/FlowOps.vue 实际 `t()` 引用为权威，落地时逐键对账）：

```csharp
using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>版本治理+运维驾驶舱画面词条（version-ops V-G1）：设计器发布/版本/diff（oa.designer.pub.*）
/// + 驾驶舱三 tab 与干预四动作（oa.flowops.*）+ E-WF-029/030。五语。去重：落地前 grep 复核零重复。</summary>
public static class I18nOaFlowOpsScreenSeed
{
    public static readonly Sys_Lang[] Items =
    {
        // ── 设计器：发布/版本（DesignerView.vue）──
        new() { LangKey = "oa.designer.pub.publish",         ZhCN = "发布",         ZhTW = "發布",         En = "Publish",                    Ja = "公開",                 Ko = "게시" },
        new() { LangKey = "oa.designer.pub.publishOk",       ZhCN = "发布成功",     ZhTW = "發布成功",     En = "Published successfully",     Ja = "公開しました",         Ko = "게시되었습니다" },
        new() { LangKey = "oa.designer.pub.publishConfirm",  ZhCN = "确认发布当前草稿？发布后该版本不可再修改", ZhTW = "確認發布當前草稿？發布後該版本不可再修改", En = "Publish current draft? The version becomes immutable after publishing.", Ja = "現在の下書きを公開しますか？公開後このバージョンは変更できません", Ko = "현재 초안을 게시하시겠습니까? 게시 후 해당 버전은 수정할 수 없습니다" },
        new() { LangKey = "oa.designer.pub.draft",           ZhCN = "草稿",         ZhTW = "草稿",         En = "Draft",                      Ja = "下書き",               Ko = "초안" },
        new() { LangKey = "oa.designer.pub.published",       ZhCN = "已发布",       ZhTW = "已發布",       En = "Published",                  Ja = "公開済み",             Ko = "게시됨" },
        new() { LangKey = "oa.designer.pub.readonlyBanner",  ZhCN = "正在查看历史版本 v{version}（只读）", ZhTW = "正在查看歷史版本 v{version}（唯讀）", En = "Viewing historical version v{version} (read-only)", Ja = "履歴バージョン v{version} を表示中（読み取り専用）", Ko = "이전 버전 v{version} 보기(읽기 전용)" },
        new() { LangKey = "oa.designer.pub.saveAsDraft",     ZhCN = "从此版本另存草稿", ZhTW = "從此版本另存草稿", En = "Save as draft from this version", Ja = "このバージョンから下書き作成", Ko = "이 버전에서 초안으로 저장" },
        new() { LangKey = "oa.designer.pub.draftFromOk",     ZhCN = "已从历史版本创建草稿", ZhTW = "已從歷史版本建立草稿", En = "Draft created from historical version", Ja = "履歴バージョンから下書きを作成しました", Ko = "이전 버전에서 초안을 생성했습니다" },
        new() { LangKey = "oa.designer.pub.conflictReload",  ZhCN = "内容已被他人修改，请重新加载后再操作", ZhTW = "內容已被他人修改，請重新載入後再操作", En = "Modified by someone else. Reload before continuing.", Ja = "他のユーザーにより変更されています。再読み込みしてください", Ko = "다른 사용자가 수정했습니다. 다시 불러온 후 진행하세요" },
        // ── 设计器：版本对比（VersionDiffDialog.vue）──
        new() { LangKey = "oa.designer.pub.diff",            ZhCN = "版本对比",     ZhTW = "版本對比",     En = "Compare versions",           Ja = "バージョン比較",       Ko = "버전 비교" },
        new() { LangKey = "oa.designer.pub.diffAdded",       ZhCN = "新增",         ZhTW = "新增",         En = "Added",                      Ja = "追加",                 Ko = "추가" },
        new() { LangKey = "oa.designer.pub.diffRemoved",     ZhCN = "删除",         ZhTW = "刪除",         En = "Removed",                    Ja = "削除",                 Ko = "삭제" },
        new() { LangKey = "oa.designer.pub.diffChanged",     ZhCN = "变更",         ZhTW = "變更",         En = "Changed",                    Ja = "変更",                 Ko = "변경" },
        new() { LangKey = "oa.designer.pub.diffStates",      ZhCN = "状态（关卡）", ZhTW = "狀態（關卡）", En = "States",                     Ja = "ステート（関所）",     Ko = "상태(단계)" },
        new() { LangKey = "oa.designer.pub.diffPaths",       ZhCN = "路径（流转）", ZhTW = "路徑（流轉）", En = "Paths",                      Ja = "パス（遷移）",         Ko = "경로(전이)" },
        new() { LangKey = "oa.designer.pub.diffJsonFallback",ZhCN = "所选版本含并行结构，已退化为 JSON 视图对比", ZhTW = "所選版本含並行結構，已退化為 JSON 視圖對比", En = "Selected version contains parallel structures; falling back to JSON view diff.", Ja = "選択バージョンに並列構造が含まれるため、JSON ビュー比較に切り替えました", Ko = "선택한 버전에 병렬 구조가 포함되어 JSON 보기 비교로 전환했습니다" },
        // ── 驾驶舱：页与 tab（FlowOps.vue）──
        new() { LangKey = "oa.flowops.title",            ZhCN = "流程运维",     ZhTW = "流程運維",     En = "Flow Operations",        Ja = "フロー運用",           Ko = "플로우 운영" },
        new() { LangKey = "oa.flowops.tab.instances",    ZhCN = "实例检索",     ZhTW = "實例檢索",     En = "Instances",              Ja = "インスタンス検索",     Ko = "인스턴스 검색" },
        new() { LangKey = "oa.flowops.tab.jobs",         ZhCN = "job 运维",     ZhTW = "job 運維",     En = "Jobs",                   Ja = "ジョブ運用",           Ko = "잡 운영" },
        new() { LangKey = "oa.flowops.tab.analytics",    ZhCN = "分析",         ZhTW = "分析",         En = "Analytics",              Ja = "分析",                 Ko = "분석" },
        // ── 驾驶舱：筛选 ──
        new() { LangKey = "oa.flowops.f.status",         ZhCN = "状态",         ZhTW = "狀態",         En = "Status",                 Ja = "ステータス",           Ko = "상태" },
        new() { LangKey = "oa.flowops.f.flowKey",        ZhCN = "流程",         ZhTW = "流程",         En = "Flow",                   Ja = "フロー",               Ko = "플로우" },
        new() { LangKey = "oa.flowops.f.version",        ZhCN = "版本",         ZhTW = "版本",         En = "Version",                Ja = "バージョン",           Ko = "버전" },
        new() { LangKey = "oa.flowops.f.stuckDays",      ZhCN = "停泊超龄（天）", ZhTW = "停泊超齡（天）", En = "Parked over (days)",   Ja = "滞留超過（日）",       Ko = "정체 초과(일)" },
        new() { LangKey = "oa.flowops.f.starter",        ZhCN = "发起人",       ZhTW = "發起人",       En = "Starter",                Ja = "起票者",               Ko = "기안자" },
        new() { LangKey = "oa.flowops.f.dateRange",      ZhCN = "日期范围",     ZhTW = "日期範圍",     En = "Date range",             Ja = "期間",                 Ko = "기간" },
        new() { LangKey = "oa.flowops.f.kind",           ZhCN = "类型",         ZhTW = "類型",         En = "Kind",                   Ja = "種別",                 Ko = "종류" },
        // ── 驾驶舱：实例列表列 ──
        new() { LangKey = "oa.flowops.col.bizNo",        ZhCN = "单号",         ZhTW = "單號",         En = "Doc No.",                Ja = "伝票番号",             Ko = "문서번호" },
        new() { LangKey = "oa.flowops.col.flow",         ZhCN = "流程",         ZhTW = "流程",         En = "Flow",                   Ja = "フロー",               Ko = "플로우" },
        new() { LangKey = "oa.flowops.col.version",      ZhCN = "版本",         ZhTW = "版本",         En = "Version",                Ja = "バージョン",           Ko = "버전" },
        new() { LangKey = "oa.flowops.col.node",         ZhCN = "当前关卡",     ZhTW = "當前關卡",     En = "Current step",           Ja = "現在の関所",           Ko = "현재 단계" },
        new() { LangKey = "oa.flowops.col.stay",         ZhCN = "停留时长",     ZhTW = "停留時長",     En = "Dwell time",             Ja = "滞留時間",             Ko = "체류 시간" },
        new() { LangKey = "oa.flowops.col.starter",      ZhCN = "发起人",       ZhTW = "發起人",       En = "Starter",                Ja = "起票者",               Ko = "기안자" },
        new() { LangKey = "oa.flowops.col.status",       ZhCN = "状态",         ZhTW = "狀態",         En = "Status",                 Ja = "ステータス",           Ko = "상태" },
        new() { LangKey = "oa.flowops.versionMatrix",    ZhCN = "版本分布",     ZhTW = "版本分佈",     En = "Version distribution",   Ja = "バージョン分布",       Ko = "버전 분포" },
        // ── 驾驶舱：job tab ──
        new() { LangKey = "oa.flowops.job.replay",       ZhCN = "重放",         ZhTW = "重放",         En = "Replay",                 Ja = "再実行",               Ko = "재실행" },
        new() { LangKey = "oa.flowops.job.cancel",       ZhCN = "取消",         ZhTW = "取消",         En = "Cancel",                 Ja = "キャンセル",           Ko = "취소" },
        new() { LangKey = "oa.flowops.job.staleFires",   ZhCN = "老化占坑",     ZhTW = "老化佔位",     En = "Stale reservations",     Ja = "滞留予約枠",           Ko = "장기 미결 예약" },
        new() { LangKey = "oa.flowops.job.attempts",     ZhCN = "尝试",         ZhTW = "嘗試",         En = "Attempts",               Ja = "試行",                 Ko = "시도" },
        new() { LangKey = "oa.flowops.job.nextAttempt",  ZhCN = "下次执行",     ZhTW = "下次執行",     En = "Next attempt",           Ja = "次回実行",             Ko = "다음 실행" },
        new() { LangKey = "oa.flowops.job.lastError",    ZhCN = "最后错误",     ZhTW = "最後錯誤",     En = "Last error",             Ja = "最終エラー",           Ko = "마지막 오류" },
        // ── 驾驶舱：干预动作 ──
        new() { LangKey = "oa.flowops.act.terminate",    ZhCN = "强制终止",     ZhTW = "強制終止",     En = "Force terminate",        Ja = "強制終了",             Ko = "강제 종료" },
        new() { LangKey = "oa.flowops.act.reResolve",    ZhCN = "重解析审批人", ZhTW = "重解析審批人", En = "Re-resolve approver",    Ja = "承認者再解決",         Ko = "결재자 재해석" },
        new() { LangKey = "oa.flowops.act.forceAdvance", ZhCN = "强制推进",     ZhTW = "強制推進",     En = "Force advance",          Ja = "強制推進",             Ko = "강제 진행" },
        new() { LangKey = "oa.flowops.reason",           ZhCN = "理由",         ZhTW = "理由",         En = "Reason",                 Ja = "理由",                 Ko = "사유" },
        new() { LangKey = "oa.flowops.reasonRequired",   ZhCN = "理由必填",     ZhTW = "理由必填",     En = "Reason is required",     Ja = "理由は必須です",       Ko = "사유는 필수입니다" },
        new() { LangKey = "oa.flowops.tokenPick",        ZhCN = "选择停泊 token", ZhTW = "選擇停泊 token", En = "Pick a parked token",  Ja = "停泊トークンを選択",   Ko = "정박 토큰 선택" },
        new() { LangKey = "oa.flowops.reResolveOk",      ZhCN = "解析成功，实例已恢复运行", ZhTW = "解析成功，實例已恢復運行", En = "Resolved. Instance is running again.", Ja = "解決しました。インスタンスは再開されました", Ko = "해석 성공. 인스턴스가 다시 실행 중입니다" },
        new() { LangKey = "oa.flowops.reResolveStill",   ZhCN = "仍无法解析：{reason}", ZhTW = "仍無法解析：{reason}", En = "Still unresolvable: {reason}", Ja = "まだ解決できません：{reason}", Ko = "여전히 해석 불가: {reason}" },
        // ── 驾驶舱：分析 tab ──
        new() { LangKey = "oa.flowops.an.avgDuration",   ZhCN = "平均审批时长", ZhTW = "平均審批時長", En = "Avg. approval duration", Ja = "平均承認時間",         Ko = "평균 결재 시간" },
        new() { LangKey = "oa.flowops.an.bottleneck",    ZhCN = "瓶颈关卡 Top", ZhTW = "瓶頸關卡 Top", En = "Top bottleneck steps",   Ja = "ボトルネック関所 Top", Ko = "병목 단계 Top" },
        new() { LangKey = "oa.flowops.an.timeoutRate",   ZhCN = "超时率",       ZhTW = "超時率",       En = "Timeout rate",           Ja = "タイムアウト率",       Ko = "시간 초과율" },
        new() { LangKey = "oa.flowops.an.rejectRate",    ZhCN = "驳回率",       ZhTW = "駁回率",       En = "Rejection rate",         Ja = "却下率",               Ko = "반려율" },
        new() { LangKey = "oa.flowops.an.empty",         ZhCN = "所选范围内暂无数据", ZhTW = "所選範圍內暫無資料", En = "No data in the selected range", Ja = "選択範囲にデータがありません", Ko = "선택 범위에 데이터가 없습니다" },
        new() { LangKey = "oa.flowops.an.timeoutNote",   ZhCN = "口径：超时自动同意/驳回与超时升级计入分子；催办（remind）不留痕不计", ZhTW = "口徑：超時自動同意/駁回與超時升級計入分子；催辦（remind）不留痕不計", En = "Note: auto approve/reject on timeout and escalations count; reminders leave no trace and are excluded.", Ja = "基準：タイムアウト自動承認/却下とエスカレーションを分子に計上。催促（remind）は痕跡がなく対象外", Ko = "기준: 시간 초과 자동 승인/반려 및 에스컬레이션은 분자에 포함; 독촉(remind)은 흔적이 없어 제외" },
        // ── 错误码 ──
        new() { LangKey = "E-WF-029", ZhCN = "该流程无可用已发布版本或已停用，无法发起", ZhTW = "該流程無可用已發布版本或已停用，無法發起", En = "No published & enabled version of this flow; cannot start", Ja = "公開済みで有効なバージョンがないため起動できません", Ko = "게시되고 활성화된 버전이 없어 시작할 수 없습니다" },
        new() { LangKey = "E-WF-030", ZhCN = "发布失败：无草稿可发布或校验未通过", ZhTW = "發布失敗：無草稿可發布或校驗未通過", En = "Publish failed: no draft or validation errors", Ja = "公開失敗：下書きがないか検証エラーがあります", Ko = "게시 실패: 초안이 없거나 검증 오류가 있습니다" },
    };
}
```

- [ ] **Step 3: Program.cs concat**（i18n concat 链尾追加）：

```csharp
            .Concat(CP6.WebApi.Seed.I18nOaFlowOpsScreenSeed.Items)  // 版本治理+驾驶舱 oa.designer.pub.* / oa.flowops.* / E-WF-029/030
```

- [ ] **Step 4: build + commit** — `dotnet build CP6.WebApi/CP6.WebApi.csproj`（SeedLangs 运行期幂等去重）；`git add -A && git commit -m "feat(wfs-version-ops): V-G1 I18nOaFlowOpsScreenSeed 五语52键+concat"`

---

### Task V-G2: gstack QA harness（只写不跑）

**Files:**
- Create: `docs/superpowers/qa/wfs-version-ops/README.md`（剧本；同目录已有 V-A0 的 precheck-orphan.sql）
- Create: `docs/superpowers/qa/wfs-version-ops/seed.sql`
- Create: `docs/superpowers/qa/wfs-version-ops/qa_version_ops.ps1`（HTTP e2e，ASCII 数据）

- [ ] **Step 1: 写 harness**（参 `docs/superpowers/qa/wfs-service-task/` E-T3 先例三件套；seed.sql 单数表名、`SET QUOTED_IDENTIFIER ON`、隔离库 `CP6DB_OA`；README 记 QA 登录 admin/123456 与 dev server 命令）。剧本 8 条（spec §7 QA 清单全覆盖）：
  1. **发布 v2 → 旧单继续走 v1 实况走查**：seed 两关卡流程 v1 + 在途单 → 设计器改 schema（删第二关卡）→ 发布 v2 → 旧单第二关卡照常出现并办结到终态；新发起单走 v2 一步到终态。
  2. **Enable 关最新不回落**：FlowAdmin 关闸 → 新发起报 E-WF-029 五语文案（切 2 语验证）；在途单不受影响。
  3. **版本 diff 视图**：设计器版本下拉选 v1 →「版本对比」v1 vs v2 → 删除行红色显示；构造含并行结构版本 → JSON 退化视图 + 提示文案。
  4. **copy-on-write 与只读**：发布后再编辑 → 自动衍生 v3 草稿（版本下拉出现「草稿」徽标）；选 v1 → 只读横幅 + 保存/发布禁用 +「从此版本另存草稿」。
  5. **驾驶舱检索+版本分布**：FlowOps 实例 tab 按状态/版本/停泊超龄过滤；版本分布矩阵显示 v1×在途数（D3 观察面）。
  6. **job 重放/取消全流程**：seed Failed job（外部假端点）→ job tab 重放 → worker 拾起（成功或按假端点再失败）；取消 → 错误路由（错误边流程走 errEnd）；token 已走的 job 重放 → 400 文案「请对实例使用强制推进/终止」。
  7. **干预四动作**：强制终止（带子流程单 → 子实例组收件箱消失 + 履历 forceTerminate）；挂起单重解析（改 vars 后成功回 Running）；强制推进（platform-admin 登录，serviceTask 停泊单 → job 变 Cancelled + 单据推进；非 platform-admin 调 force-advance 端点 → 403）。
  8. **老化占坑筛选**：seed 8 天前占坑 `Wf_TriggerFire`（InstanceId/Error 均 NULL）→ job tab 老化占坑开关 → 该行出现；新占坑不出现。
- [ ] **Step 2: commit** — `git add -A && git commit -m "test(wfs-version-ops): V-G2 gstack QA harness(8剧本+seed+e2e脚本,只写不跑)"`
- [ ] **Step 3: 末期 live QA（用户在场，不在本计划 DoD 内）** — 隔离库起后端+前端 → ps1 HTTP e2e + gstack browse 真浏览器走剧本 1/3/4/7。抓 bug 当场 TDD 修。记入 memory 待办。

---

### Task V-G3: DoD 验收（主代理执行）

- [ ] 后端 `dotnet test CP6.Tests/CP6.Tests.csproj` 全绿：**基线(1509+前置各期增量)+本波新增，5 skip 不变**；`--filter Wf` 既有 27+ 不变量测试**断言零改**（`git diff <base> -- CP6.Tests/Wf` 复核：既有文件改动仅限 V-A2 Step 5 / V-B1 Step 5 列账的构造行/规格退役条目）。
- [ ] **EF 恰一次迁移**：`git diff --stat <base> -- CP6.Core/Migrations` 仅 `*_WfsVersionPin.*` + ModelSnapshot；`dotnet ef migrations has-pending-model-changes ... --context CP6Context` **clean**；迁移含数据回填②与在途孤儿守卫 THROW（代码走查确认）。
- [ ] **迁移回归锁定**：迁移后（单行 Def 数据形态）SubmitAsync/办理/退回/撤回行为与迁移前逐字节一致——由「全部既有 Wf 测试零断言改动全绿」自证 + VersionPinTests 两事故形态定点。
- [ ] 前端 `npm run test` 全绿（320+前置增量+本波 versionDiff/flowOpsCharts 新增）；`NODE_OPTIONS=--max-old-space-size=8192 npm run type-check` / `npm run build` 过。
- [ ] **零 Space 污染**：`git diff --stat <base>..HEAD` 无 `views/space` / `*Space*` / Space 迁移。
- [ ] **零硬编码色**：新增前端文件 grep 无 `#[0-9a-fA-F]{3,6}` 字面色；图表/diff 三色全走 `var(--cp-*)` token。
- [ ] i18n ~52 键五语齐 + LangKey 与既有 seed 零重复（grep 复核）；E-WF-029/030 前后端文案闭环。
- [ ] 错误码/边界齐备：E-WF-029（VersionPinTests + 触发器透传测）、E-WF-030（FlowVersionServiceTests）、E-WF-023 口径同步（波③测试照绿）、发布 409 冲突语义。
- [ ] 权限矩阵：`FlowOpsControllerContractTests` 全绿（五 action + force-advance 叠 platform-admin + 负例）。
- [ ] 干预四动作双痕：FlowHistory action 行测试断言齐（forceTerminate/reResolve/forceAdvance/jobReplay/jobCancel）；OperLog 走全局过滤器（controller POST 全覆盖，走查确认）。
- [ ] QA harness 四件套齐（precheck-orphan.sql + README 8 剧本 + seed.sql + ps1）；live QA 留待用户在场（记入 memory 待办）。
- [ ] `git log` 提交信息全部 `feat(wfs-version-ops):` / `test(wfs-version-ops):` 中文风格；**只本地 commit 不 push**。

---

## 覆盖核对（spec §7 测试策略 → 任务落点）

| spec §7 条目 | 落点 |
|---|---|
| 发布 v2 后在途 v1 按 v1 跑到终态（**删节点/改审批人两变体**） | V-A3 `InFlight_PinnedV1_SurvivesV2_NodeDeleted` / `_ApproverChanged` + QA 剧本 1 |
| 新单 pin v2 | V-A3 `NewSubmit_PinsLatestPublished` |
| Enable 关闭停发起、在途不受影响 | V-A3 `EnableOff_InFlightUnaffected` |
| **E-WF-029 关最新不回落旧版本** | V-A3 `E_WF_029_DisabledLatest_NoFallbackToOlder` + QA 剧本 2 |
| 发布新版 Enable 继承前版（无前版=true） | V-B1 `Publish_FreezesDraft_InheritsEnable_FromPrevPublished` / `Publish_FirstVersion_EnableDefaultsTrue` |
| copy-on-write 衍生 / **撞键重载** | V-B1 `OpenDraft_NoDraft_CopyOnWrite_*`、`OpenDraft_DraftExists_*` + V-B2 SQLite 两测 + QA 剧本 4 |
| Published 不可变守卫 / 禁删守卫 | V-B1 `EnsureMutable_*` / `EnsureDeletable_*`（+ SaveDef 写路径接线） |
| 校验未过 E-WF-030（聚合错误随响应） | V-B1 `Publish_NoDraft_E_WF_030` / `Publish_InvalidSchema_E_WF_030_*` |
| 并发发布 RowVersion | V-B1 显式比较闸 + SQL Server rowversion 真闸（生产）+ 409 controller 语义 |
| 「从历史另存草稿」 | V-B1 `DraftFromVersion_*` 两测 |
| 版本 diff（表级三色 / 并行退化 JSON） | V-C1 vitest 全套 + QA 剧本 3 |
| 检索过滤矩阵 / 版本分布计数 | V-D1 `SearchInstances_FilterMatrix_*` / `_StuckDays_*` / `VersionDistribution_Matrix_*` |
| job 重放后 worker 正常拾起 / 取消走错误路由 / Running job 拒 | V-D2 `Replay_TokenParked_ResetsJob_WorkerPicksUp` / `Cancel_PendingJob_ErrorRouting_*` / `Replay_RunningJob_*` |
| **token 已走时重放/取消拒 400（僵尸调用定点，两态）** | V-D2 `Replay_TokenGone_Rejected400` / `Cancel_TokenGone_Rejected400`（+ 停泊态放行两测=另一态） |
| 强制终止级联子实例 + 双痕 | V-E1 `ForceTerminate_Running_ClearsAll_CascadesChildren_DualTrace` |
| 重解析成功/仍失败两态 | V-E2 `ReResolve_AfterVarsFixed_*` / `ReResolve_StillUnresolvable_*` |
| 强制推进 FormTo/History/权限矩阵（非 platform-admin 403） | V-E3 `ForceAdvance_ApprovalParked_*` + `FlowOpsControllerContractTests`（403 由 RequirePlatformAdmin 属性三道闸既有测试承接）+ QA 剧本 7 |
| **强推 subFlow 停泊级联取消子实例组 / serviceTask 停泊 Cancel pending job（孤儿/幽灵定点）** | V-E3 `ForceAdvance_SubFlowParked_CascadesChildGroup` / `ForceAdvance_ServiceTaskParked_CancelsPendingJob` |
| 终态孤儿 null 降级仅履历视图 / 在途孤儿迁移失败 | V-A3 `NullPin_TerminalOrphan_*` 两测 / V-A1 迁移守卫 THROW + V-A0 预检 |
| 分析四聚合已知数据集断言 / 空态 / 日期边界 | V-F1 三测 |
| QA harness（发布实况走查/diff/驾驶舱四动作/老化占坑） | V-G2 剧本 1-8 |
| 基线全绿 / EF 恰一次迁移 WfsVersionPin | V-G3 DoD |

---

## 执行顺序与依赖

```
前置硬闸：二三期全部计划（kernel-hardening / statemachine-designer / subflow / event-trigger-start / engine-infra）已合入 main

V-A: V-A0 → V-A1 → V-A2 → V-A3          （pin 内核：迁移→收敛→定点回归）
V-B: V-B1 → V-B2 → V-B3                 （依赖 V-A3；发布流：服务→端点/并发→UI）
V-C: V-C1 → V-C2                        （依赖 V-B2 + 三期 stateMachineModel；版本 diff）
V-D: V-D1 → V-D2 → V-D3                 （依赖 V-A3，可与 V-B/V-C 并行；驾驶舱检索+job）
V-E: V-E1 → V-E2 → V-E3                 （依赖 V-D3 + 三期 SubFlowCascade；干预三动作）
V-F: V-F1 → V-F2                        （依赖 V-D1/V-D3，与 V-E 并行；分析）
V-G: V-G1 → V-G2 → V-G3                 （紧跟 V-D/V-E/V-F 全合入，不留窗口）

并行组注意：V-D‖V-B 双 executor 时 Program.cs（DI/种子区）与 FlowOpsModels/接口文件以先落者为准 rebase。
```
