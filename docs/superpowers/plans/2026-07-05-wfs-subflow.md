# WFS 子流程 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. **每个 Task 执行前必读对应 spec 章节**（`docs/superpowers/specs/2026-07-05-wfs-subflow-design.md`，唯一权威，决策 D1~D5 全锁不许重新设计）。本计划所有 C#/TS 代码块均按 2026-07-05 main（fb90d75）+ 二期 hardening 计划契约实读写就，测试代码逐条给全，**禁止骨架/占位符**。

**Goal:** WFS 子流程（call-activity 引用式）：`subFlow` 节点引用另一个已发布 FlowKey 启动独立子实例（独立收件箱/独立传签履历），父 token 停泊等子终态；动态多实例（`SubCollectionVar` 集合展开 N 并行子实例 + 完成策略 all/any，N 上限 `Wfs:SubFlowMaxInstances` 默认 100）；**入队-复核两段式回注**（子终态窗口内只原子入队 `Wf_ServiceJob` 内部 Kind=`subFlowResume`，提交后 fast path 复核 + worker 兜底，父实例 RowVersion 乐观并发 + 停泊状态闸保恰一次）；错误边优先无边传播（组合二期 `onBranchReject` 剪枝语义）；级联取消三路径（父终止递归 / `CancelTokenSubtree` 第五清 / 退回重生防双批）；防环 DFS + 深度 8（E-WF-025/026）；设计器面板 + 收件箱父子互链 + 五语 i18n + QA harness。

**Architecture:** 恰一次 EF 迁移 `WfsSubFlow`（`Wf_FlowInstance` 加回指三列 `ParentInstanceId/ParentTokenId/SubIndex` + 两索引；FlowNode 五字段是 SchemaJson 内 POCO 零迁移）。`SubFlowNodeHandler` = handler 字典**第 9 个**（二期 inclusive 后为 8）。子实例 = 普通 `Wf_FlowInstance`（引擎零特判，收件箱/权限/租户与顶层实例全等）；父 token 停泊沿用 serviceTask async「Active 停节点不动」形态，无新 token 状态。回注两段式：第一段挂 `DispatchIfFinishedAsync`（Approved/Rejected）与 `TaskCenterService.WithdrawAsync`（Withdrawn）只入队；第二段 `FlowEngine.CheckSubFlowGroupAsync` 由 fast path（各写路径外壳 SaveChanges 后）与 `WfServiceJobService.ScanOnceAsync` 内部 Kind 短路共用。级联取消统一走 `SubFlowCascade` 静态工具，三处挂钩（`CancelAllActiveTokens` / `CancelTokenSubtree` 第五清 / `WithdrawAsync`）。

**Tech Stack:** .NET 8 / EF Core（SQL Server 生产、InMemory+SQLite 测试）/ xUnit（`CP6.Tests/Wf`，InternalsVisibleTo 已开）/ Vue3 + Vue Flow（`cp6.web/src/views/oa/designer`）/ vitest。

---

## 前置依赖（硬闸，未满足不许开工）

1. **二期 hardening 计划 H-A~H-C 已并 main**（`docs/superpowers/plans/2026-07-05-wfs-kernel-hardening.md`）。本计划直接消费其共享契约（签名逐字，不许漂移）：
   - `FlowTokenStatus.Pruned = 3`（`CP6.Core/Services/Wf/WfStatus.cs`）
   - `internal void CancelTokenSubtree(Guid instanceId, Guid rootTokenId)`（`CP6.Core/Services/Wf/FlowEngine.Tokens.cs`）——**S-C 第五清接缝在此文件此方法上改**
   - `internal Task<bool> TryPruneBranchAsync(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowToken token, Guid actorId, string? comment)`（`CP6.Core/Services/Wf/FlowEngine.Prune.cs`）——错误处置「传播父驳回」的剪枝分流复用
   - `TokenLineage` / `GatewayJoinHelper` 动态计票（组合语义测试依赖：Pruned token 从 join 等待集消失）
   - H-C `SendBackToNodeAsync` 三规则（S-C 退回重生防双批测试依赖 BeforeSplit 放开跨网关退回）
2. **软前置**：cleanup-tickets 计划票4 的 `ServiceVarsHelper.ContainsUnsupportedSubscript(string? text)`（`docs/superpowers/plans/2026-07-05-wfs-cleanup-tickets.md` 票4）。若尚未落地，D-T1 按该计划的**逐字签名与实现**先行落到 `ServiceVarsHelper.cs`（以先落者为准，签名一致取一份）。
3. 三期 event-trigger 计划（`2026-07-05-wfs-event-trigger-start.md`）**非前置**——仅借其 `FlowTriggerTestHarness` 风格（SQLite 共享连接 + GenerateCreateScript + rowversion 触发器）与错误码水位（022~024 已占，本计划从 **E-WF-025** 起）。

---

## 落码纪律 / Global Constraints（每个 Task 都遵守）

- **基线锁定**：后端 `dotnet test CP6.Tests/CP6.Tests.csproj` = 开工时 main 全绿计数（fb90d75 时点 **1509 通过（5 skip=SQLite 既知）**；二期/三期并入后以当时计数为准）→ 本波只增不减、全绿。前端 `npm run test`（vitest）= **320（+已并波次增量）全绿** → +N 全绿；`npm run type-check`（package.json 既有命令，内含 `NODE_OPTIONS=--max-old-space-size=8192`）+ `npm run build` 全过。
- **EF 恰一次迁移 `WfsSubFlow`**：只加 `Wf_FlowInstance` 三可空列 + 两索引（范本 `CP6.Core/Migrations/20260629142700_WfsServiceTask.cs`）。A-T1 生成后，每波末跑 `dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context` 必须 clean。**全计划不许出现第二个迁移**。
- **既有 Wf 不变量测试断言零改**（`CP6.Tests/Wf/**` 既有文件只增不改）。唯一例外沿二期 D-T1 先例：前端 `designerModel.test.ts` 的 palette 类型清单断言随 palette 扩展同步 +1（该断言就是「palette 全集清单」本身）。`TaskCenterService` 构造函数只许**追加可选参数**（既有 9 处 `new TaskCenterService(db)` 零改动）。
- **默认路径行为与现状全等**：无 subFlow 节点的既有流程 bit 级等价——`DispatchIfFinishedAsync` 钩子对 `ParentInstanceId==null` 实例是纯谓词短路；`CancelAllActiveTokens`/`CancelTokenSubtree`/`WithdrawAsync` 的级联钩子对无子实例 token 是零行为查询；fast path 对空 Local 队列是 O(1) no-op。
- **引擎内写路径三律（黄金模板铁律）**：① 先校验后写；② 幂等（复核重入安全、停泊重入不重复起子）；③ handler/引擎内部方法**绝不自行 SaveChanges**——唯二例外与既有先例同构：`CheckSubFlowGroupAsync`/`FastPathSubFlowResumeAsync` 是**提交后**复核阶段自带事务（仿 `ResumeServiceTokenAsync` ×3 乐观并发重试收口），不在任何原子接缝窗口内。
- **DispatchIfFinished 原子接缝内零外呼**：第一段（子终态 SaveChanges 窗口内）只做 `SubFlowResume.EnqueueIfChild` 纯内存 Add——零计票、零推进、零通知、零 HTTP（spec D5 铁律边界）。
- **E 波紧跟 D 波不留窗口**：S-E 合入后立即执行 S-F（i18n/QA），不允许「有 UI 无 i18n」中间态过夜。
- **零跨模块污染**：不碰 `cp6.web/src/views/space/**`、`Services/*Space*`、Space 迁移/DbSet。每 Task 完成 `git show --stat` 复核。
- **零硬编码色**：前端新增视觉全部走 Design System token（`.dot-*` / `var(--cp-*)` 家族），沿 `DesignerCanvas.vue`/`ServiceTaskNode.vue` 既有 token 用法。
- **五语 i18n**：ja / zh-CN / zh-TW / en / ko，新 UI 文案全 `t()` 运行时键，键值入 `I18nOa*ScreenSeed` 家族新 seed（F-T1）。
- **隔离 worktree**：建议 `git worktree add C:/CP6-wfs-subflow -b feat/wfs-subflow main`（须在二期已并之后的 main 上），不污染 `C:\CP6` 工作区。
- **subagent-driven TDD**：每 Task 全新编码子代理（模型按 model-policy：Opus 4.8）→ 主代理 `git show` diff 复核 → 本地 commit **不 push**。节奏：先写失败测试 → 跑验证 FAIL → 最小实现 → 跑验证 PASS → commit。提交信息 `feat(wfs-subflow): <Task 号> 中文摘要`。

---

## 侦察结论（spec §8 各核实项 + 计划期新发现，已实读代码定案——执行者照此实现，不再二次侦察）

| # | 核实项 | 结论 |
|---|---|---|
| 1 | **SubmitAsync 精确签名** | `IFlowEngine.cs:13`：`Task<Guid> SubmitAsync(string flowKey, Guid starterId, string varsJson, string? bizType = null, string? bizId = null)`（实现 `FlowEngine.cs:45`，尾部自带 `SaveChangesAsync`）。子实例**不复用**公有入口（handler 内不许 SaveChanges，且回指三列必须在构造期写入——起即终态子实例的第一段入队钩子读 `ParentInstanceId` 才看得见）→ 新增 `internal SubmitChildAsync(...)`（B-T1），复制 SubmitAsync 机制但不 SaveChanges、构造期带三列。 |
| 2 | **DispatchIfFinished 挂点** | `FlowEngine.cs:235-247` `private DispatchIfFinishedAsync(inst, decidedBy, reason)`，只处理 Approved/Rejected；调用点：SubmitAsync:69 / StartDraftAsync:96 / ActOnceAsync:227 / ResumeServiceTokenAsync:333 / FailServiceTokenAsync:373——全部在各自 SaveChanges 之前（原子）。第一段入队钩子加在**方法体首行**（终态谓词短路）。**Withdrawn 不经此方法**：撤回在 `TaskCenterService.WithdrawAsync`（`TaskCenterService.cs:31-78`，独立服务、无 FlowEngine 依赖、自带 SaveChanges）→ 撤回入队钩子单独挂在该方法内。 |
| 3 | **`Wf_FlowInstance` RowVersion 现状（spec §8 核实项）** | **已存在，无需任何迁移动作**：实体 `CP6.Entity/DomainModels/Wf/Wf_FlowInstance.cs:40-41` `[Timestamp] public byte[]? RowVersion`（WFS P1 Task 6 引入）；迁移 `20260626201249_WfsPhaseAKernel.cs:21-23` 已加 `rowversion` 列；`ActOnceAsync:183` 的 `inst.ModifyDate = DateTime.Now` 写触达即为既有乐观并发闸先例。SQLite 测试无原生 rowversion → `AFTER UPDATE` 触发器模拟（`FlowConcurrencyTests.cs:61-75` + 三期 harness 同款）。`WfsSubFlow` 迁移只加三列+两索引。 |
| 4 | **Kind 分发点（spec §8 核实项）** | `WfServiceJobService.ScanOnceAsync`（`WfServiceJobService.cs:53-179`）**不按 `job.Kind` 分发**——按 `ServiceTaskActionRef.Parse(job.ActionRefJson)` → `ResolveExecutorKey` 查 `_executors` 字典（key null=纯 timer 直接推进；未命中=E-WF-018 Fail）。执行前状态闸④按 `job.TokenId` 查真实 token。**内部 Kind 插入点 = lease 抢占成功后、状态闸④之前，按 `job.Kind == WfJobKind.SubFlowResume` 短路分派**到 `CheckSubFlowGroupAsync`：零 `IServiceTaskExecutor` 注册、`GetServiceCatalog`（按 executor Kind/VisibleInDesigner 过滤，`DesignerService.cs:25-28`）与 `FlowSchemaValidator.KnownServiceKinds`（只校验节点 `ServiceKind`，`subFlowResume` 永不出现在节点上）均零污染。 |
| 5 | **`UX_Wf_ServiceJob_LiveToken` 防撞定案（计划期新发现）** | 该 filtered unique index =`(TenantId, TokenId, NodeId) WHERE Status IN (0,1)`（`CP6Context.cs:741-743`）。若 subFlowResume job 以 ParentTokenId 占 TokenId 槽，同组两个并发子终态会撞唯一键令**子终态事务整体失败**。定案：**`TokenId = 子实例 Id`（每子实例至多一条活跃唤醒凭据，天然防重不撞组）、`NodeId = "$subFlowResume"` 哨兵、ParentTokenId 入 `ActionRefJson` 载荷**（spec §3.2「载荷=ParentTokenId」的落码细化，语义不变）。 |
| 6 | **复核「重读已提交数据」的落码含义（计划期新发现）** | scoped DbContext 身份映射会让同上下文早先追踪的子实例呈**陈旧态**（EF 查询命中已追踪实体返回旧值）→ all 计票误判「未齐」后把凭据标 Succeeded = 丢唤醒。定案：`CheckSubFlowGroupAsync` 计票前对子实例组逐行 `Entry(c).ReloadAsync()`（N≤100 可控），保证「重读已提交数据」字面成立。worker 路径每 scan 新 scope 天然新鲜，此闸兜同请求 fast path。 |
| 7 | **`ContainsUnsupportedSubscript` 出处** | 代码中尚不存在；spec 所指「波①」= cleanup-tickets 计划票4 的 `public static bool ContainsUnsupportedSubscript(string? text)`（挂 `ServiceVarsHelper`）。前置依赖第 2 条处置。 |
| 8 | **面板下拉数据源（已发布清单端点）** | `GET /api/oa/designer/list`（`DesignerController.cs:32-34` → `DesignerService.ListAsync` → `FlowDefSummary(FlowKey, FlowName, FormKey, FunctionId, FlowCode, Version, Enable)`）。前端已有 `designerApi.list()` 封装（NodePropertyPanel 服务目录同款懒加载模式 `NodePropertyPanel.vue:82-95`）。面板过滤 `enable && flowKey !== 当前流程`。 |
| 9 | **TaskCenterService 构造变更策略** | 既有测试 9 处 `new TaskCenterService(db)`（TaskCenterServiceTests / WithdrawCleanupTests / ServiceJobCleanupTests）——ctor 追加**可选参数** `FlowEngine? engine = null`（同程序集 internal 可见；DI `Program.cs:117` 自动解析已注册的 scoped FlowEngine `Program.cs:107`），既有调用零改动。engine==null 时跳过 fast path（worker 20s 兜底），不影响正确性。 |
| 10 | **撤回路径 token 清场形态** | `WithdrawAsync` **不走** `CancelAllActiveTokens`（就地循环 `TaskCenterService.cs:48-51`）→ 级联钩子必须**三处齐挂**：`CancelAllActiveTokens`（驳回连坐/退回全清场/剪枝坍缩）+ `CancelTokenSubtree`（二期 SameBranch 剥离，第五清）+ `WithdrawAsync`（就地循环后逐 token）。 |
| 11 | **spec 与二期计划出入（不改 spec，按此口径落码）** | (a) spec §3.3 称「剪枝路径（H-B）经由同一工具（CancelTokenSubtree）天然覆盖」——二期计划实况：`PruneTokenAsync` **自带单 token 清场不经 CancelTokenSubtree**（`FlowEngine.Prune.cs` B-T2）。**语义无缺口**：prune 只把「被驳任务的 token」置 Pruned，停泊 subFlow token 无任务永不被直接剪；其死亡只经 `CancelTokenSubtree`（SameBranch）或 `CancelAllActiveTokens`（终止/坍缩/全清场）两路 + `WithdrawAsync`，三钩子即闭合。按 spec 预案在 `SubFlowCascade` 与 `CancelTokenSubtree`/`FlowEngine.Prune.cs` 写互指注释。 (b) spec §1 锚点「不走 Wf_ServiceJob 队列」与 D5 修订（入队 subFlowResume）表述冲突——**以 D5/§3.2 为准**（§1 语义=停泊本身不建扫描 job，唤醒凭据仍入队）。 |

---

## File Structure（创建/修改清单，每文件一职责）

**后端 `CP6.Entity` / `CP6.Core`**
- Modify `CP6.Entity/DomainModels/Wf/Wf_FlowInstance.cs` — 回指三列（§2.2）。
- Modify `CP6.Core/EFDbContext/CP6Context.cs` — `Wf_FlowInstance` 两索引（:679-683 块内追加）。
- Create 迁移 `CP6.Core/Migrations/<ts>_WfsSubFlow.cs`（`dotnet ef` 生成，唯一一次）。
- Modify `CP6.Core/Services/Wf/WfStatus.cs` — `WfJobKind` / `SubFlowCompletionPolicy` / `SubFlowLimits` 常量。
- Modify `CP6.Core/Services/Wf/FlowSchema.cs` — `FlowNode` 五个 `Sub*` 可空字段（零迁移）。
- Create `CP6.Core/Services/Wf/SubFlowVarsMapper.cs` — 变量映射纯函数（in/out/dot-path 保类型）。
- Create `CP6.Core/Services/Wf/SubFlowResume.cs` — `SubFlowResumePayload` + `EnqueueIfChild`（第一段）。
- Create `CP6.Core/Services/Wf/SubFlowCascade.cs` — 级联取消工具（撤回语义+递归）。
- Create `CP6.Core/Services/Wf/NodeHandlers/SubFlowNodeHandler.cs` — 第 9 个 handler。
- Create `CP6.Core/Services/Wf/FlowEngine.SubFlow.cs` — partial：`SubmitChildAsync` / `CheckSubFlowGroupAsync` / `FastPathSubFlowResumeAsync` / `SubFlowErrorDisposeAsync`。
- Modify `CP6.Core/Services/Wf/FlowEngine.cs` — `DefaultHandlers()` 第 9 项；`DispatchIfFinishedAsync` 首行入队钩子；`SubmitAsync`/`StartDraftAsync`/`ActAsync`/`ActAsAsync`/`ResumeServiceTokenAsync`/`FailServiceTokenAsync` 尾部 fast path。
- Modify `CP6.Core/Services/Wf/FlowEngine.Tokens.cs` — `CancelAllActiveTokens` 级联钩子 + `CancelTokenSubtree` 第五清。
- Modify `CP6.Core/Services/Wf/TaskCenterService.cs` — ctor 可选 engine + 撤回入队/级联/fast path。
- Modify `CP6.Core/Services/Wf/WfServiceJobService.cs` — `ScanOnceAsync` 内部 Kind 短路（状态闸④之前）。
- Modify `CP6.Core/Services/Wf/FlowSchemaValidator.cs` — subFlow 静态规则（E-WF-025）。
- Modify（条件）`CP6.Core/Services/Wf/ServiceVarsHelper.cs` — `ContainsUnsupportedSubscript`（票4 未落地时按其逐字实现先落）。
- Create `CP6.Core/Services/Wf/SubFlowRefValidator.cs` — 保存时 FlowKey 存在性 + 防环 DFS（E-WF-025/026，DI 层）。
- Modify `CP6.Core/Services/Oa/DesignerService.cs` — `SaveAsync` ①c 调 `SubFlowRefValidator`。
- Modify `CP6.Core/Services/Oa/InboxModels.cs` — `SubFlowParentRow`/`SubFlowChildRow` + `InboxDetail` 追加两可选参数。
- Modify `CP6.Core/Services/Oa/InboxService.cs` — `DetailAsync` 父子互链聚合。

**后端 `CP6.WebApi`**
- Modify `CP6.WebApi/Program.cs` — DI 注册 SubFlowNodeHandler（:114 ServiceTask 与二期 inclusive 两行之后）；i18n concat（:1819 链尾，二期 hardening seed 行之后）。
- Create `CP6.WebApi/Seed/I18nOaSubFlowScreenSeed.cs` — 五语 18 键。

**前端 `cp6.web/src`**
- Modify `views/oa/designer/designerModel.ts` — `SchemaNode` 五字段 + palette `subFlow` 入口 + validateClient 镜像。
- Modify `views/oa/designer/designerModel.test.ts` — palette 类型清单断言 +1（唯一既有前端测试改动）。
- Create `views/oa/designer/designerModel.subflow.spec.ts` — 新 vitest。
- Create `views/oa/designer/nodes/SubFlowNode.vue` — 子流程节点（token 化配色）。
- Modify `views/oa/designer/DesignerCanvas.vue` — 注册节点模板 + palette dot 样式。
- Modify `views/oa/designer/NodePropertyPanel.vue` — subFlow 配置段（目标流程下拉/映射编辑/多实例开关）。
- Modify `views/oa/inbox/FormDetail.vue` — 父流程链接 + 子实例列表段。
- Modify `types/oa/inbox.ts` — `InboxDetail` 镜像新字段。

**测试 / QA**
- Create `CP6.Tests/Wf/SubFlowModelTests.cs` / `SubFlowVarsMapperTests.cs` / `SubFlowTestHarness.cs` / `SubFlowHandlerTests.cs` / `SubFlowResumeCheckTests.cs` / `SubFlowTwoPhaseTests.cs` / `SubFlowConcurrencyTests.cs` / `SubFlowCascadeTests.cs` / `SubFlowSendBackComboTests.cs` / `SubFlowValidatorTests.cs`。
- Create `docs/superpowers/qa/wfs-subflow/{README.md,seed.sql,qa_subflow.ps1}` — gstack harness（只写不跑）。

---

## 共享契约（所有 Task 用这些**精确**名字与签名，前后一致，不许漂移）

```csharp
// WfStatus.cs
public static class WfJobKind          // Wf_ServiceJob.Kind 的内部值域扩展（不进 ServiceKind：设计器/校验永不见）
{
    public const string SubFlowResume = "subFlowResume";
}
public static class SubFlowCompletionPolicy { public const string All = "all"; public const string Any = "any"; }
public static class SubFlowLimits
{
    public const int MaxDepth = 8;                    // E-WF-026 深度守卫 = 保存时 DFS 上限（spec §5）
    public const int DefaultMaxInstances = 100;       // Wfs:SubFlowMaxInstances 缺省（spec §3.1）
}

// FlowSchema.cs / FlowNode（SchemaJson POCO，全可空向后兼容，spec §2.1 注释逐字）
public string? SubFlowKey { get; set; }
public string? SubVarsInJson { get; set; }
public string? SubVarsOutJson { get; set; }
public string? SubCollectionVar { get; set; }
public string? SubCompletionPolicy { get; set; }

// Wf_FlowInstance（迁移 WfsSubFlow，spec §2.2 注释逐字）
public Guid? ParentInstanceId { get; set; }
public Guid? ParentTokenId { get; set; }
public int? SubIndex { get; set; }
// 索引：IX_Wf_FlowInstance_Parent (TenantId, ParentInstanceId)
//       UX_Wf_FlowInstance_SubSlot (TenantId, ParentTokenId, SubIndex) WHERE ParentTokenId IS NOT NULL

// SubFlowVarsMapper.cs（internal static class，纯函数）
public static bool TryParseMap(string? mapJson, out Dictionary<string, string> map);          // 非法 JSON/非字符串值 → false
public static JsonNode? ResolveNode(string path, string? varsJson);                            // "$.a.b" 点路径保类型取值
public static string BuildChildVars(string? subVarsInJson, string parentVarsJson, JsonNode? item, int? itemIndex);
public static Dictionary<string, object?> BuildOutMerge(string? subVarsOutJson,
    IReadOnlyList<(int SubIndex, string VarsJson)> approvedChildren, bool aggregateAsArray);   // all 多实例=按 SubIndex 数组；any/单实例=标量

// SubFlowResume.cs
internal sealed record SubFlowResumePayload(Guid ParentTokenId, Guid ParentInstanceId, Guid ChildInstanceId, int SubIndex)
{ public string ToJson(); public static SubFlowResumePayload? Parse(string? json); }
internal static class SubFlowResume
{
    public const string JobNodeId = "$subFlowResume";                                          // 哨兵，永不匹配真实节点
    public static void EnqueueIfChild(CP6Context db, Wf_FlowInstance inst);                    // 第一段：纯内存 Add，零外呼
}

// SubFlowCascade.cs
internal static class SubFlowCascade
{
    internal static void CancelChildrenOfToken(CP6Context db, Guid parentTokenId);             // 该 token 名下在途子实例组级联取消（递归）
    internal static void CancelInstanceTree(CP6Context db, Wf_FlowInstance inst);              // 单实例撤回语义清场 + 孙代递归；不 SaveChanges
}

// FlowEngine.SubFlow.cs（partial FlowEngine）
internal Task<Guid> SubmitChildAsync(string flowKey, Guid starterId, string varsJson,
    Guid parentInstanceId, Guid parentTokenId, int subIndex);                                  // 不 SaveChanges（随父外壳落库）
internal Task CheckSubFlowGroupAsync(Guid parentTokenId, CancellationToken ct = default);      // 第二段复核（幂等，两入口共用）
internal Task FastPathSubFlowResumeAsync(CancellationToken ct = default);                      // 提交后 fast path（Local 队列扫描）
internal Task SubFlowErrorDisposeAsync(Wf_FlowInstance inst, FlowSchema schema, FlowNode node,
    Wf_FlowToken token, string? code, int subIndex, Guid? childInstanceId, int? childStatus);  // D2 错误处置（错边优先→剪枝分流→驳回）

// SubFlowRefValidator.cs（保存时 DI 层校验）
internal static class SubFlowRefValidator
{
    internal static void Validate(CP6Context db, string flowKey, FlowSchema schema);           // 违规 throw InvalidOperationException("E-WF-025"/"E-WF-026")
}

// InboxModels.cs（收件箱互链读模型）
public record SubFlowParentRow(Guid InstanceId, string FlowKey, string? FlowName);
public record SubFlowChildRow(Guid InstanceId, int SubIndex, string FlowKey, string? FlowName, int Status, string NodeId);
// InboxDetail 末尾追加：SubFlowParentRow? SubFlowParent = null, IReadOnlyList<SubFlowChildRow>? SubFlows = null
```

- 节点类型串：`"subFlow"`（比较一律 OrdinalIgnoreCase，对齐 `EnterNodeAsync` 的 ToLowerInvariant 分发）。
- 履历 action：`"subFlowStarted"` / `"subFlowEmptyCollection"` / `"subFlowResumed"` / `"subFlowError"` / `"subFlowCascadeCancelled"`。
- 错误码：**E-WF-025**（配置无效：SubFlowKey 空/不存在/未启用；映射 JSON 非法或含下标；policy 值域；集合变量空串；运行时集合非数组/N 超上限）/ **E-WF-026**（引用环/深度 ≥8：保存时 DFS + 运行时守卫双检）。
- app 配置键：`Wfs:SubFlowMaxInstances`（int，缺省 100；DI 注册处 `IConfiguration.GetValue` 读取）。
- 错误变量：父 vars 顶层 `subFlowError = { nodeId, code, subIndex, childInstanceId, childStatus, atUtc }`（非保留前缀，直写 JsonNode）。
- 前端 i18n 键（18 键五语，F-T1）：`oa.designer.subflow.title|target|targetHint|varsIn|varsOut|varsHint|multi|collectionVar|policy|policy.all|policy.any|policyHint`、`oa.designer.errSubFlowConfig`、`oa.detail.parentFlow|subFlows|subIndex`、`E-WF-025`、`E-WF-026`。

---

## Wave S-A — 数据模型（回指三列 + 常量 + 映射纯函数 + 测试基座）

### Task A-T1: FlowNode POCO + 回指三列 + 索引 + 常量 + 一次迁移 `WfsSubFlow`

**Files:**
- Modify: `CP6.Entity/DomainModels/Wf/Wf_FlowInstance.cs`
- Modify: `CP6.Core/Services/Wf/FlowSchema.cs`
- Modify: `CP6.Core/Services/Wf/WfStatus.cs`
- Modify: `CP6.Core/EFDbContext/CP6Context.cs`（:679-683 `Wf_FlowInstance` 配置块）
- Create: 迁移 `CP6.Core/Migrations/<ts>_WfsSubFlow.cs`（`dotnet ef` 生成）
- Test: `CP6.Tests/Wf/SubFlowModelTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/SubFlowModelTests.cs
using System.Text.Json;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;

namespace CP6.Tests.Wf;

public class SubFlowModelTests
{
    [Fact]
    public void WfJobKind_SubFlowResume_Constant()
        => Assert.Equal("subFlowResume", WfJobKind.SubFlowResume);

    [Fact]
    public void SubFlowCompletionPolicy_Constants()
    {
        Assert.Equal("all", SubFlowCompletionPolicy.All);
        Assert.Equal("any", SubFlowCompletionPolicy.Any);
    }

    [Fact]
    public void SubFlowLimits_Constants()
    {
        Assert.Equal(8, SubFlowLimits.MaxDepth);
        Assert.Equal(100, SubFlowLimits.DefaultMaxInstances);
    }

    [Fact]
    public void FlowNode_SubFlowFields_DefaultNull()
    {
        var n = new FlowNode();
        Assert.Null(n.SubFlowKey);
        Assert.Null(n.SubVarsInJson);
        Assert.Null(n.SubVarsOutJson);
        Assert.Null(n.SubCollectionVar);
        Assert.Null(n.SubCompletionPolicy);
    }

    [Fact]
    public void Wf_FlowInstance_ParentColumns_DefaultNull()
    {
        var i = new Wf_FlowInstance();
        Assert.Null(i.ParentInstanceId);
        Assert.Null(i.ParentTokenId);
        Assert.Null(i.SubIndex);
    }

    [Fact]
    public void FlowSchema_SubFlowNode_JsonRoundTrip()
    {
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode
                {
                    Id = "sub", Type = "subFlow", SubFlowKey = "fk-child",
                    SubVarsInJson = "{\"childVar\":\"$.parentVar\"}",
                    SubVarsOutJson = "{\"parentOut\":\"$.childOut\"}",
                    SubCollectionVar = "items", SubCompletionPolicy = "any",
                },
                new FlowNode { Id = "e", Type = "end" },
            },
            Edges = { new FlowEdge { From = "s", To = "sub" }, new FlowEdge { From = "sub", To = "e" } },
        };
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var back = JsonSerializer.Deserialize<FlowSchema>(JsonSerializer.Serialize(schema), opts)!;
        var sub = back.Nodes.Single(n => n.Id == "sub");
        Assert.Equal("fk-child", sub.SubFlowKey);
        Assert.Equal("{\"childVar\":\"$.parentVar\"}", sub.SubVarsInJson);
        Assert.Equal("{\"parentOut\":\"$.childOut\"}", sub.SubVarsOutJson);
        Assert.Equal("items", sub.SubCollectionVar);
        Assert.Equal("any", sub.SubCompletionPolicy);
    }
}
```

- [ ] **Step 2: 跑测试验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter SubFlowModelTests`。预期编译失败（常量/字段不存在）。

- [ ] **Step 3: 最小实现**

`WfStatus.cs` 文件尾追加：

```csharp
/// <summary>Wf_ServiceJob.Kind 的内部值域扩展（子流程 spec §3.2）。不进 <see cref="ServiceKind"/>：
/// 设计器目录（GetServiceCatalog）、节点校验（FlowSchemaValidator.KnownServiceKinds）永不见此值；
/// 扫描 worker 在 lease 后按 Kind 短路分派，不进 executor 注册表。</summary>
public static class WfJobKind
{
    public const string SubFlowResume = "subFlowResume";
}

/// <summary>子流程完成策略（spec §2.1 SubCompletionPolicy 值域）。</summary>
public static class SubFlowCompletionPolicy
{
    public const string All = "all";   // 默认:全部 Approved 才过;任一 Rejected/Withdrawn → 错误处置
    public const string Any = "any";   // 首个 Approved 即过(级联撤回其余);全驳/撤才错误处置
}

/// <summary>子流程守卫常量（spec §3.1/§5）。</summary>
public static class SubFlowLimits
{
    /// <summary>嵌套深度上限（E-WF-026）：保存时 DFS 与运行时 ParentInstanceId 链上溯同口径。</summary>
    public const int MaxDepth = 8;
    /// <summary>多实例 N 上限缺省（app 配置键 Wfs:SubFlowMaxInstances 可覆盖）。</summary>
    public const int DefaultMaxInstances = 100;
}
```

`FlowSchema.cs` 的 `FlowNode`（服务任务字段块之后）追加，注释照 spec §2.1 逐字：

```csharp
    // ── 子流程节点(子流程 spec §2.1,全可空向后兼容,null=非 subFlow 节点) ──
    /// <summary>子流程引用（Type="subFlow" 专用）：目标已发布流程的 FlowKey。</summary>
    public string? SubFlowKey { get; set; }
    /// <summary>父→子变量映射 JSON：{"子var":"$.父var路径"}（ServiceVarsHelper 点路径口径，含其已记档限制）。null=不传。</summary>
    public string? SubVarsInJson { get; set; }
    /// <summary>子终态→父回注映射 JSON：{"父var":"$.子var路径"}。null=不回注。多实例时回注值为数组（按子实例序号）。</summary>
    public string? SubVarsOutJson { get; set; }
    /// <summary>多实例集合变量名（父 vars 中的 JSON 数组）。null=单实例。每个元素注入对应子实例 vars 的 "item" 键（含 "itemIndex"）。</summary>
    public string? SubCollectionVar { get; set; }
    /// <summary>完成策略："all"(默认,全部 Approved 才过) | "any"(首个 Approved 即过,级联撤回其余)。</summary>
    public string? SubCompletionPolicy { get; set; }
```

`Wf_FlowInstance.cs`（RowVersion 之后）追加，注释照 spec §2.2 逐字：

```csharp
    /// <summary>父流程实例（子流程 call-activity 回指；null=顶层实例）。</summary>
    public Guid? ParentInstanceId { get; set; }

    /// <summary>父流程停泊 token（子终态回注/恢复的定位键）。</summary>
    public Guid? ParentTokenId { get; set; }

    /// <summary>多实例序号（0 起；单实例=0）。与 ParentTokenId 组成防重唯一键。</summary>
    public int? SubIndex { get; set; }
```

`CP6Context.cs` `Wf_FlowInstance` 配置块（:679-683）内追加两行：

```csharp
            e.HasIndex(x => new { x.TenantId, x.ParentInstanceId }).HasDatabaseName("IX_Wf_FlowInstance_Parent");   // 子流程:父详情列子实例组
            // filtered unique: 停泊重入防重复起子的幂等闸(子流程 spec §2.2)。SQLite 经 GenerateCreateScript 生成 WHERE;
            // 代码级 SubFlowNodeHandler 先查兜底(InMemory 无索引语义,B-T1)
            e.HasIndex(x => new { x.TenantId, x.ParentTokenId, x.SubIndex }).IsUnique()
                .HasFilter("[ParentTokenId] IS NOT NULL").HasDatabaseName("UX_Wf_FlowInstance_SubSlot");
```

- [ ] **Step 4: 生成唯一迁移**

```bash
dotnet ef migrations add WfsSubFlow --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context
```

生成物应形如（范本 `20260629142700_WfsServiceTask.cs`；**只许**三 AddColumn + 两 CreateIndex，出现任何别的表/列 = 模型被污染，立刻排查回滚）：

```csharp
migrationBuilder.AddColumn<Guid>(name: "ParentInstanceId", table: "Wf_FlowInstance", type: "uniqueidentifier", nullable: true);
migrationBuilder.AddColumn<Guid>(name: "ParentTokenId",    table: "Wf_FlowInstance", type: "uniqueidentifier", nullable: true);
migrationBuilder.AddColumn<int> (name: "SubIndex",         table: "Wf_FlowInstance", type: "int",              nullable: true);
migrationBuilder.CreateIndex(name: "IX_Wf_FlowInstance_Parent",  table: "Wf_FlowInstance", columns: new[] { "TenantId", "ParentInstanceId" });
migrationBuilder.CreateIndex(name: "UX_Wf_FlowInstance_SubSlot", table: "Wf_FlowInstance", columns: new[] { "TenantId", "ParentTokenId", "SubIndex" },
    unique: true, filter: "[ParentTokenId] IS NOT NULL");
```

- [ ] **Step 5: 跑验证 PASS + EF clean + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter SubFlowModelTests
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf        # 既有照绿(纯增列/常量,零执行路径改动)
dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context
git add -A && git commit -m "feat(wfs-subflow): A-T1 FlowNode子流程POCO+Wf_FlowInstance回指三列+防重唯一键+一次迁移WfsSubFlow"
```

---

### Task A-T2: SubFlowVarsMapper 变量映射纯函数

> 依赖 A-T1（FlowNode 字段）。双向映射的**唯一**口径：handler 传入（B-T1）与复核回注（B-T2）都消费本文件，禁止各写一份。

**Files:**
- Create: `CP6.Core/Services/Wf/SubFlowVarsMapper.cs`
- Test: `CP6.Tests/Wf/SubFlowVarsMapperTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/SubFlowVarsMapperTests.cs
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.Wf;

namespace CP6.Tests.Wf;

public class SubFlowVarsMapperTests
{
    [Fact]
    public void TryParseMap_ValidStringMap_True()
    {
        Assert.True(SubFlowVarsMapper.TryParseMap("{\"a\":\"$.x\",\"b\":\"$.y.z\"}", out var map));
        Assert.Equal(2, map.Count);
        Assert.Equal("$.x", map["a"]);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[1,2]")]
    [InlineData("{\"a\":1}")]          // 值必须是字符串路径
    public void TryParseMap_Invalid_False(string bad)
        => Assert.False(SubFlowVarsMapper.TryParseMap(bad, out _));

    [Fact]
    public void TryParseMap_NullOrBlank_TrueEmpty()
    {
        Assert.True(SubFlowVarsMapper.TryParseMap(null, out var m1));
        Assert.Empty(m1);
        Assert.True(SubFlowVarsMapper.TryParseMap("  ", out var m2));
        Assert.Empty(m2);
    }

    [Fact]
    public void ResolveNode_DotPath_PreservesType()
    {
        const string vars = "{\"amount\":42,\"o\":{\"name\":\"zed\",\"ok\":true},\"list\":[1,2]}";
        Assert.Equal(42, SubFlowVarsMapper.ResolveNode("$.amount", vars)!.GetValue<int>());
        Assert.Equal("zed", SubFlowVarsMapper.ResolveNode("$.o.name", vars)!.GetValue<string>());
        Assert.True(SubFlowVarsMapper.ResolveNode("$.o.ok", vars)!.GetValue<bool>());
        Assert.IsType<JsonArray>(SubFlowVarsMapper.ResolveNode("$.list", vars));
        Assert.Null(SubFlowVarsMapper.ResolveNode("$.missing", vars));
        Assert.Null(SubFlowVarsMapper.ResolveNode("$.o.missing.deep", vars));
    }

    [Fact]
    public void BuildChildVars_MapsAndInjectsItem()
    {
        const string parent = "{\"seed\":\"OK\",\"n\":7}";
        var item = JsonNode.Parse("{\"sku\":\"A1\"}");
        var json = SubFlowVarsMapper.BuildChildVars("{\"result\":\"$.seed\",\"num\":\"$.n\"}", parent, item, 2);
        var o = JsonNode.Parse(json)!.AsObject();
        Assert.Equal("OK", o["result"]!.GetValue<string>());
        Assert.Equal(7, o["num"]!.GetValue<int>());
        Assert.Equal("A1", o["item"]!["sku"]!.GetValue<string>());
        Assert.Equal(2, o["itemIndex"]!.GetValue<int>());
    }

    [Fact]
    public void BuildChildVars_SingleInstance_NoItemKeys()
    {
        var json = SubFlowVarsMapper.BuildChildVars(null, "{\"seed\":1}", item: null, itemIndex: null);
        var o = JsonNode.Parse(json)!.AsObject();
        Assert.False(o.ContainsKey("item"));
        Assert.False(o.ContainsKey("itemIndex"));
        Assert.Empty(o);   // null 映射=不传(spec §2.1),子 vars 从空对象起
    }

    [Fact]
    public void BuildOutMerge_Aggregate_ArrayBySubIndex_MissingAsNull()
    {
        var children = new List<(int, string)> { (1, "{\"v\":20}"), (0, "{\"v\":10}"), (2, "{}") };
        var outVars = SubFlowVarsMapper.BuildOutMerge("{\"results\":\"$.v\"}", children, aggregateAsArray: true);
        var arr = Assert.IsType<JsonArray>(outVars["results"]);
        Assert.Equal(10, arr[0]!.GetValue<int>());
        Assert.Equal(20, arr[1]!.GetValue<int>());
        Assert.Null(arr[2]);
    }

    [Fact]
    public void BuildOutMerge_Scalar_SingleChild()
    {
        var outVars = SubFlowVarsMapper.BuildOutMerge("{\"r\":\"$.v\"}", new List<(int, string)> { (0, "{\"v\":\"win\"}") }, aggregateAsArray: false);
        Assert.Equal("win", ((JsonNode)outVars["r"]!).GetValue<string>());
    }

    [Fact]
    public void BuildOutMerge_NullMap_Empty()
        => Assert.Empty(SubFlowVarsMapper.BuildOutMerge(null, new List<(int, string)> { (0, "{}") }, false));
}
```

- [ ] **Step 2: 跑验证 FAIL** — `--filter SubFlowVarsMapperTests`（编译失败）。

- [ ] **Step 3: 实现** — `SubFlowVarsMapper.cs` 全文：

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CP6.Core.Services.Wf;

/// <summary>子流程双向变量映射（spec §2.1/§3.2，纯函数零 I/O）。点路径与 <see cref="ServiceVarsHelper.ResolveValue"/>
/// 同口径（"$.a.b" 顶层/嵌套对象键，不支持数组下标——含下标由校验层 E-WF-025 拦），但**保 JSON 类型**
/// （ResolveValue 返回字符串，回注数组/数字会失真，故独立实现 ResolveNode）。</summary>
internal static class SubFlowVarsMapper
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>解析映射 JSON（{"目标var":"$.源路径"}）。null/空白 → true+空表；非对象/值非字符串/非法 JSON → false。</summary>
    public static bool TryParseMap(string? mapJson, out Dictionary<string, string> map)
    {
        map = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(mapJson)) return true;
        try
        {
            if (JsonNode.Parse(mapJson) is not JsonObject o) return false;
            foreach (var (k, v) in o)
            {
                if (v is not JsonValue jv || !jv.TryGetValue<string>(out var path)) return false;
                map[k] = path;
            }
            return true;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>"$.a.b" 点路径取值（保类型）。缺失/非法 → null。无 "$." 前缀视为顶层键。</summary>
    public static JsonNode? ResolveNode(string path, string? varsJson)
    {
        if (string.IsNullOrWhiteSpace(varsJson) || string.IsNullOrWhiteSpace(path)) return null;
        var p = path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : path;
        try
        {
            JsonNode? cur = JsonNode.Parse(varsJson);
            foreach (var part in p.Split('.'))
            {
                if (cur is not JsonObject o) return null;
                cur = o[part];
                if (cur is null) return null;
            }
            return cur;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>构造子实例 varsJson（spec §3.1 第 3 步）：SubVarsInJson 映射自父 vars ∪ {"item","itemIndex"}
    /// （单实例 item==null 时不注入两键）。映射源缺失 → 该键写 null（子流程可空感知）。</summary>
    public static string BuildChildVars(string? subVarsInJson, string parentVarsJson, JsonNode? item, int? itemIndex)
    {
        var child = new JsonObject();
        if (TryParseMap(subVarsInJson, out var map))
            foreach (var (childVar, path) in map)
                child[childVar] = ResolveNode(path, parentVarsJson)?.DeepClone();
        if (item is not null)
        {
            child["item"] = item.DeepClone();
            child["itemIndex"] = itemIndex;
        }
        return child.ToJsonString();
    }

    /// <summary>子终态→父回注（spec §3.2 恢复路径）：{"父var":"$.子var路径"}。
    /// aggregateAsArray=true（多实例 all）→ 按 SubIndex 升序聚合数组（缺失=null）；false（单实例/any 首个）→ 标量。
    /// 返回 dict 供 <see cref="ServiceVarsHelper.MergeOutputVars"/> 合并（保留前缀 wf./sys./_internal. 同款拦截）。</summary>
    public static Dictionary<string, object?> BuildOutMerge(string? subVarsOutJson,
        IReadOnlyList<(int SubIndex, string VarsJson)> approvedChildren, bool aggregateAsArray)
    {
        var result = new Dictionary<string, object?>();
        if (!TryParseMap(subVarsOutJson, out var map) || map.Count == 0) return result;
        var ordered = approvedChildren.OrderBy(c => c.SubIndex).ToList();
        foreach (var (parentVar, path) in map)
        {
            if (aggregateAsArray)
            {
                var arr = new JsonArray();
                foreach (var (_, vars) in ordered) arr.Add(ResolveNode(path, vars)?.DeepClone());
                result[parentVar] = arr;
            }
            else
            {
                result[parentVar] = ordered.Count == 0 ? null : ResolveNode(path, ordered[0].VarsJson)?.DeepClone();
            }
        }
        return result;
    }
}
```

- [ ] **Step 4: 跑验证 PASS** — `--filter SubFlowVarsMapperTests` 全绿。
- [ ] **Step 5: Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-subflow): A-T2 SubFlowVarsMapper双向映射纯函数(保类型点路径+SubIndex聚合)"
```

---

### Task A-T3: SubFlowTestHarness 测试基座（SQLite 共享连接 + rowversion 触发器）

> 依赖 A-T1。照 `FlowConcurrencyTests.cs:61-75` / 三期 `FlowTriggerTestHarness` 逐字模式。S-B/S-C 全部 SQLite 测试复用；InMemory 测试也复用其 schema 工厂。**本 Task 纯测试基建，无产品代码。**

**Files:**
- Create: `CP6.Tests/Wf/SubFlowTestHarness.cs`

- [ ] **Step 1: 实现基座**（无独立断言，编译即达标；后续任务的测试是它的验证）：

```csharp
// CP6.Tests/Wf/SubFlowTestHarness.cs —— 共享基座：GenerateCreateScript + TEXT 替换建库 +
// AFTER UPDATE 触发器模拟 rowversion（Wf_FlowInstance=恰一次恢复闸；Wf_ServiceJob=fast path/worker 抢 job）
using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Wf;

internal static class SubFlowTestHarness
{
    /// <summary>测试专用子类：两表声明 rowversion 触发器（EF Core 8 SQLite 关 RETURNING 改 SELECT 读回，
    /// 令 [Timestamp] 并发令牌在 SQLite 基座真正生效——照 FlowConcurrencyTests 口径）。</summary>
    internal sealed class SqliteCP6Context : CP6Context
    {
        public SqliteCP6Context(DbContextOptions<CP6Context> o) : base(o) { }
        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);
            mb.Entity<Wf_FlowInstance>().ToTable(t => t.HasTrigger("trg_Wf_FlowInstance_RowVersion"));
            mb.Entity<Wf_ServiceJob>().ToTable(t => t.HasTrigger("trg_Wf_ServiceJob_RowVersion"));
        }
    }

    public static SqliteCP6Context Ctx(SqliteConnection c)
        => new(new DbContextOptionsBuilder<CP6Context>().UseSqlite(c).Options);

    public static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

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
            "CREATE TRIGGER trg_Wf_ServiceJob_RowVersion AFTER UPDATE ON \"Wf_ServiceJob\" " +
            "BEGIN UPDATE \"Wf_ServiceJob\" SET \"RowVersion\" = randomblob(8) WHERE \"Id\" = NEW.\"Id\"; END;");
        return conn;
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>子流程：cs → ca(指定审批人) → ce。</summary>
    public static FlowSchema ChildSchema(Guid approver) => new()
    {
        Start = "cs",
        Nodes =
        {
            new FlowNode { Id = "cs", Type = "start" },
            new FlowNode { Id = "ca", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
            new FlowNode { Id = "ce", Type = "end" },
        },
        Edges = { new FlowEdge { From = "cs", To = "ca" }, new FlowEdge { From = "ca", To = "ce" } },
    };

    /// <summary>秒批子流程：cs → ce（起即终态，测 fast path 即时收敛）。</summary>
    public static FlowSchema InstantChildSchema() => new()
    {
        Start = "cs",
        Nodes = { new FlowNode { Id = "cs", Type = "start" }, new FlowNode { Id = "ce", Type = "end" } },
        Edges = { new FlowEdge { From = "cs", To = "ce" } },
    };

    /// <summary>父流程：ps → sub(subFlow) → pa(父审批,证明恢复推进) → pe；errorEdge=true 时另挂 sub→err(IsError)→ee。</summary>
    public static FlowSchema ParentSchema(Guid parentApprover, string subFlowKey,
        string? collectionVar = null, string? policy = null, string? varsIn = null, string? varsOut = null,
        bool errorEdge = false, Guid? errApprover = null)
    {
        var s = new FlowSchema
        {
            Start = "ps",
            Nodes =
            {
                new FlowNode { Id = "ps", Type = "start" },
                new FlowNode { Id = "sub", Type = "subFlow", SubFlowKey = subFlowKey, SubCollectionVar = collectionVar,
                               SubCompletionPolicy = policy, SubVarsInJson = varsIn, SubVarsOutJson = varsOut },
                new FlowNode { Id = "pa", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = parentApprover },
                new FlowNode { Id = "pe", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "ps", To = "sub" },
                new FlowEdge { From = "sub", To = "pa" },
                new FlowEdge { From = "pa", To = "pe" },
            },
        };
        if (errorEdge)
        {
            s.Nodes.Add(new FlowNode { Id = "err", Type = "approval", ApproverStrategy = "Specified",
                                       ApproverUserId = errApprover ?? parentApprover });
            s.Nodes.Add(new FlowNode { Id = "ee", Type = "end" });
            s.Edges.Add(new FlowEdge { From = "sub", To = "err", IsError = true });
            s.Edges.Add(new FlowEdge { From = "err", To = "ee" });
        }
        return s;
    }

    public static void SeedDef(CP6Context db, string flowKey, FlowSchema schema, bool enable = true)
        => db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = flowKey, FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = enable,
        });
}
```

- [ ] **Step 2: 编译 + Wf 闸 + commit**

```bash
dotnet build CP6.Tests/CP6.Tests.csproj
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "test(wfs-subflow): A-T3 SubFlowTestHarness基座(SQLite共享连接+双表rowversion触发器+父子schema工厂)"
```

---

## Wave S-B — handler + 入队-复核两段式回注（本计划正确性核心）

### Task B-T1: SubFlowNodeHandler + SubmitChildAsync + 错误处置 + 深度守卫（第 9 个 handler）

> 依赖 A-T1/A-T2/A-T3。**执行前必读 spec §3.1/§3.3 与二期 `FlowEngine.Prune.cs` 的 `TryPruneBranchAsync` 契约。**

**Files:**
- Create: `CP6.Core/Services/Wf/NodeHandlers/SubFlowNodeHandler.cs`
- Create: `CP6.Core/Services/Wf/FlowEngine.SubFlow.cs`（本 Task 先落 `SubmitChildAsync` + `SubFlowErrorDisposeAsync` + `WriteSubFlowError`）
- Modify: `CP6.Core/Services/Wf/FlowEngine.cs`（`DefaultHandlers()` 加第 9 项，注释「八 handler」改「九 handler」）
- Modify: `CP6.WebApi/Program.cs`（二期 inclusive 两行注册之后加 1 行，`IConfiguration` 读 `Wfs:SubFlowMaxInstances`）
- Test: `CP6.Tests/Wf/SubFlowHandlerTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/SubFlowHandlerTests.cs
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static CP6.Tests.Wf.SubFlowTestHarness;

namespace CP6.Tests.Wf;

/// <summary>SubFlowNodeHandler OnEnter 行为（spec §3.1）：起子/停泊/回指回填/多实例/空集直通/N 上限/深度守卫。
/// InMemory 基座（单线程行为面）；并发面在 SubFlowConcurrencyTests(SQLite)。</summary>
public class SubFlowHandlerTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    [Fact]
    public async Task SingleInstance_ParksParent_SpawnsChild_BackfillsPointers()
    {
        using var db = NewDb();
        Guid pa = Guid.NewGuid(), ca = Guid.NewGuid(), starter = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        SeedDef(db, "parent", ParentSchema(pa, "child", varsIn: "{\"result\":\"$.seed\"}"));
        await db.SaveChangesAsync();

        var pid = await Engine(db).SubmitAsync("parent", starter, "{\"seed\":\"OK\"}");

        // 父 token 停泊在 sub（Active 不动）
        var parked = await db.Wf_FlowTokens.SingleAsync(t => t.InstanceId == pid && t.NodeId == "sub");
        Assert.Equal(FlowTokenStatus.Active, parked.Status);
        // 子实例：回指三列 + 变量映射 + 独立收件箱待办
        var child = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == pid);
        Assert.Equal(parked.Id, child.ParentTokenId);
        Assert.Equal(0, child.SubIndex);
        Assert.Equal(starter, child.StarterId);
        Assert.Equal("OK", JsonNode.Parse(child.VarsJson)!["result"]!.GetValue<string>());
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == child.Id && t.AssigneeId == ca && t.Status == FlowTaskStatus.Pending));
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.InstanceId == pid && h.Action == "subFlowStarted"));
        // 父审批人此刻不应有待办（父没推进）
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == pa));
    }

    [Fact]
    public async Task Multi_N3_SpawnsThree_WithItemVars()
    {
        using var db = NewDb();
        Guid pa = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        SeedDef(db, "parent", ParentSchema(pa, "child", collectionVar: "items"));
        await db.SaveChangesAsync();

        var pid = await Engine(db).SubmitAsync("parent", Guid.NewGuid(), "{\"items\":[\"a\",\"b\",\"c\"]}");

        var children = await db.Wf_FlowInstances.Where(i => i.ParentInstanceId == pid).OrderBy(i => i.SubIndex).ToListAsync();
        Assert.Equal(3, children.Count);
        Assert.Equal(new[] { 0, 1, 2 }, children.Select(c => c.SubIndex!.Value).ToArray());
        for (int i = 0; i < 3; i++)
        {
            var o = JsonNode.Parse(children[i].VarsJson)!.AsObject();
            Assert.Equal(new[] { "a", "b", "c" }[i], o["item"]!.GetValue<string>());
            Assert.Equal(i, o["itemIndex"]!.GetValue<int>());
        }
    }

    [Fact]
    public async Task EmptyCollection_PassThrough_NoChildren_NoWriteback()
    {
        using var db = NewDb();
        Guid pa = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        SeedDef(db, "parent", ParentSchema(pa, "child", collectionVar: "items", varsOut: "{\"r\":\"$.v\"}"));
        await db.SaveChangesAsync();

        var pid = await Engine(db).SubmitAsync("parent", Guid.NewGuid(), "{\"items\":[]}");

        Assert.False(await db.Wf_FlowInstances.AnyAsync(i => i.ParentInstanceId == pid));
        // 直接沿非错误出边前进：父审批人有待办
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == pa && t.Status == FlowTaskStatus.Pending));
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.InstanceId == pid && h.Action == "subFlowEmptyCollection"));
        Assert.False(JsonNode.Parse((await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).VarsJson)!.AsObject().ContainsKey("r"));   // 不回注
    }

    [Fact]
    public async Task OverCap_ErrorEdge_E_WF_025_NoChildren()
    {
        using var db = NewDb();
        Guid pa = Guid.NewGuid(), ca = Guid.NewGuid(), errU = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        SeedDef(db, "parent", ParentSchema(pa, "child", collectionVar: "items", errorEdge: true, errApprover: errU));
        await db.SaveChangesAsync();

        // 上限 2 的 handler（DI 读 Wfs:SubFlowMaxInstances 的等价注入面），集合给 3 → E-WF-025 错误处置走错边
        var handlers = new INodeHandler[]
        {
            new StartNodeHandler(), new ApprovalNodeHandler(), new EndNodeHandler(),
            new ParallelSplitNodeHandler(), new ParallelJoinNodeHandler(),
            new ServiceTaskNodeHandler(Array.Empty<IServiceTaskExecutor>()),
            new InclusiveSplitNodeHandler(), new InclusiveJoinNodeHandler(),
            new SubFlowNodeHandler(2),
        };
        var eng = new FlowEngine(db, new ApproverResolver(db), handlers: handlers);
        var pid = await eng.SubmitAsync("parent", Guid.NewGuid(), "{\"items\":[1,2,3]}");

        Assert.False(await db.Wf_FlowInstances.AnyAsync(i => i.ParentInstanceId == pid));
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == errU && t.Status == FlowTaskStatus.Pending));
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid);
        Assert.Equal("E-WF-025", JsonNode.Parse(inst.VarsJson)!["subFlowError"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task CollectionNotArray_ErrorDisposition_E_WF_025()
    {
        using var db = NewDb();
        Guid pa = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        SeedDef(db, "parent", ParentSchema(pa, "child", collectionVar: "items"));   // 无错边 → 传播父驳回
        await db.SaveChangesAsync();

        var pid = await Engine(db).SubmitAsync("parent", Guid.NewGuid(), "{\"items\":\"not-an-array\"}");

        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid);
        Assert.Equal(FlowInstanceStatus.Rejected, inst.Status);
        Assert.Equal("E-WF-025", JsonNode.Parse(inst.VarsJson)!["subFlowError"]!["code"]!.GetValue<string>());
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.InstanceId == pid && t.Status == FlowTokenStatus.Active));
    }

    [Fact]
    public async Task DepthGuard_ChainOf10_Throws_E_WF_026()
    {
        using var db = NewDb();
        Guid u = Guid.NewGuid();
        // d9 是叶子审批流；d0..d8 逐层引用下一层。提交 d0 递归起子至 d8 实例（祖先数=8）
        // → 其 subFlow handler 深度守卫 ++depth 达 8 → 抛（spec §3.1「≥8 层」；绕过保存时校验直插 def 模拟发布后新环/深链）
        SeedDef(db, "d9", ChildSchema(u));
        for (int i = 8; i >= 0; i--)
            SeedDef(db, $"d{i}", ParentSchema(u, $"d{i + 1}"));
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).SubmitAsync("d0", Guid.NewGuid(), "{}"));
        Assert.Contains("E-WF-026", ex.Message);
    }
}
```

- [ ] **Step 2: 跑验证 FAIL** — `--filter SubFlowHandlerTests`。预期「未知节点类型：subFlow」（`EnterNodeAsync` 抛）。

- [ ] **Step 3: 实现**

`FlowEngine.SubFlow.cs` 新建（本 Task 部分；B-T2/B-T3 在同文件追加）：

```csharp
using System.Text.Json.Nodes;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>子流程引擎面（spec §3）。partial：与 FlowEngine 共享 scoped DbContext 与内部方法。
/// 铁律：SubmitChildAsync/SubFlowErrorDisposeAsync 不自行 SaveChanges（随调用方外壳收口）；
/// CheckSubFlowGroupAsync/FastPathSubFlowResumeAsync 是提交后复核阶段，自带事务（B-T2/B-T3）。</summary>
public partial class FlowEngine
{
    /// <summary>发起子实例（spec §3.1 第 3 步）。与 <see cref="SubmitAsync"/> 机制同构，差异恰三点：
    /// ① 构造期写回指三列（起即终态子实例的第一段入队钩子依赖 ParentInstanceId 已就位）；
    /// ② 不 SaveChanges（handler 三律——随父动作外壳统一落库，子实例与父停泊同事务原子）；
    /// ③ 目标不存在/停用抛 E-WF-025（保存时校验已拦，运行时兜底防发布后停用）。
    /// 版本口径=发起时刻该 FlowKey 最新已发布版（SubmitAsync 既有口径，spec §3.1）。</summary>
    internal async Task<Guid> SubmitChildAsync(string flowKey, Guid starterId, string varsJson,
        Guid parentInstanceId, Guid parentTokenId, int subIndex)
    {
        var def = await _db.Wf_FlowDefs.FirstOrDefaultAsync(x => x.FlowKey == flowKey && x.Enable)
                  ?? throw new InvalidOperationException($"E-WF-025: 子流程引用不存在或已停用:{flowKey}");
        var schema = Deserialize(def.SchemaJson);
        var first = FirstNode(schema) ?? throw new InvalidOperationException($"E-WF-025: 子流程 {flowKey} 无节点");

        var inst = new Wf_FlowInstance
        {
            Id = Guid.NewGuid(),
            FlowKey = flowKey,
            VarsJson = string.IsNullOrWhiteSpace(varsJson) ? "{}" : varsJson,
            StarterId = starterId,
            Status = FlowInstanceStatus.Running,
            CurrentNode = first.Id,
            Creator = starterId.ToString(),
            ParentInstanceId = parentInstanceId,
            ParentTokenId = parentTokenId,
            SubIndex = subIndex,
        };
        _db.Wf_FlowInstances.Add(inst);
        AddHistory(inst.Id, first.Id, starterId, "submit", null);

        var root = SpawnToken(inst, first, parent: null, fork: null);
        await EnterNodeAsync(inst, schema, first, root);
        await DispatchIfFinishedAsync(inst, starterId, null);   // 起即终态(cs→ce)子实例：第一段入队钩子在此看见回指列
        return inst.Id;
    }

    /// <summary>子流程错误处置（spec §3.2 第 3 步 + D2）：subFlowError 注入父 vars →
    /// 有 IsError 出边走错误边；无则传播父驳回——父 token 在并行支且本层 split 配 prune 时剪枝
    /// （二期 <see cref="TryPruneBranchAsync"/> 分流，本方法零新增剪枝逻辑，语义自动组合），否则整单驳回。
    /// handler 的运行时 E-WF-025（集合非数组/N 超上限）与复核错误路径共用本方法。不 SaveChanges。</summary>
    internal async Task SubFlowErrorDisposeAsync(Wf_FlowInstance inst, FlowSchema schema, FlowNode node,
        Wf_FlowToken token, string? code, int subIndex, Guid? childInstanceId, int? childStatus)
    {
        inst.VarsJson = WriteSubFlowError(inst.VarsJson, node.Id, code, subIndex, childInstanceId, childStatus);
        AddHistory(inst.Id, node.Id, inst.StarterId, "subFlowError",
            code ?? $"child={childInstanceId};status={childStatus};subIndex={subIndex}");

        if (schema.Edges.Any(e => e.From == node.Id && e.IsError == true))
        {
            await AdvanceAlongErrorEdge(inst, schema, token);   // D2：错误边优先
            return;
        }
        // 无错边 → 传播驳回；剪枝分流与 ActOnceAsync 驳回分支同构（二期 B-T2 契约）
        if (token.ForkId is not null && await TryPruneBranchAsync(inst, schema, token, inst.StarterId, "subFlowError"))
            return;
        inst.Status = FlowInstanceStatus.Rejected;
        CancelAllActiveTokens(inst.Id);   // 驳回 = terminate（S-C 级联钩子在其内递归取消子实例）
        VoidPendingFormTos(inst.Id);
    }

    /// <summary>父 vars 顶层直写 subFlowError（非保留前缀，形态仿 <see cref="WriteServiceError"/>）。</summary>
    private static string WriteSubFlowError(string? varsJson, string nodeId, string? code,
        int subIndex, Guid? childInstanceId, int? childStatus)
    {
        JsonObject root;
        if (!string.IsNullOrWhiteSpace(varsJson))
        {
            try   { root = JsonNode.Parse(varsJson)?.AsObject() ?? new JsonObject(); }
            catch { root = new JsonObject(); }
        }
        else root = new JsonObject();

        root["subFlowError"] = new JsonObject
        {
            ["nodeId"]          = nodeId,
            ["code"]            = code,
            ["subIndex"]        = subIndex,
            ["childInstanceId"] = childInstanceId?.ToString(),
            ["childStatus"]     = childStatus,
            ["atUtc"]           = DateTime.UtcNow.ToString("O"),
        };
        return root.ToJsonString();
    }
}
```

`NodeHandlers/SubFlowNodeHandler.cs` 全文：

```csharp
using System.Text.Json.Nodes;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>子流程 call-activity 节点（spec §3.1，第 9 个 handler）：解析集合展开 N 子实例
/// （<see cref="FlowEngine.SubmitChildAsync"/>，回指三列构造期写入），父 token 停泊（不 Advance 不 Consume，
/// 与 serviceTask async 停泊同形态）。N=0 空集直通；N 超上限/集合非数组 → 运行时 E-WF-025 错误处置；
/// 深度守卫 ≥8 → E-WF-026（保存时 DFS 的运行时兜底——环检测是保存时快照，后续发布可能引入新环）。
/// 停泊重入幂等：(ParentTokenId,SubIndex) 槽已存在 → 跳过（Local ∪ DB 先查 + UX_Wf_FlowInstance_SubSlot 双保险）。
/// handler 不 SaveChanges（引擎外壳收口）。</summary>
internal sealed class SubFlowNodeHandler : INodeHandler
{
    private readonly int _maxInstances;

    /// <param name="maxInstances">多实例 N 上限；DI 注册处读 app 配置 Wfs:SubFlowMaxInstances，缺省 100。</param>
    public SubFlowNodeHandler(int? maxInstances = null)
        => _maxInstances = maxInstances ?? SubFlowLimits.DefaultMaxInstances;

    public string Type => "subFlow";

    public async Task OnEnterAsync(NodeContext ctx)
    {
        var eng = ctx.Engine; var inst = ctx.Inst; var schema = ctx.Schema; var node = ctx.Node; var token = ctx.Token;

        // ① 防御式配置复检（E-WF-025；保存时校验已拦，坏 schema 直发兜底）
        if (string.IsNullOrWhiteSpace(node.SubFlowKey))
            throw new InvalidOperationException("E-WF-025: subFlow 节点缺 SubFlowKey");
        var policy = (node.SubCompletionPolicy ?? SubFlowCompletionPolicy.All).Trim().ToLowerInvariant();
        if (policy != SubFlowCompletionPolicy.All && policy != SubFlowCompletionPolicy.Any)
            throw new InvalidOperationException("E-WF-025: SubCompletionPolicy 非法");
        if (!SubFlowVarsMapper.TryParseMap(node.SubVarsInJson, out _) || !SubFlowVarsMapper.TryParseMap(node.SubVarsOutJson, out _))
            throw new InvalidOperationException("E-WF-025: 变量映射 JSON 非法");

        // ② 深度守卫（E-WF-026）：沿 ParentInstanceId 链上溯计数（spec §3.1）
        int depth = 0;
        var pid = inst.ParentInstanceId;
        while (pid is Guid p)
        {
            if (++depth >= SubFlowLimits.MaxDepth)
                throw new InvalidOperationException("E-WF-026: 子流程嵌套深度超限");
            pid = await eng.Db.Wf_FlowInstances.Where(i => i.Id == p)
                .Select(i => i.ParentInstanceId).FirstOrDefaultAsync();
        }

        // ③ 集合解析（spec §3.1 第 2 步）
        JsonArray? coll = null;
        if (!string.IsNullOrWhiteSpace(node.SubCollectionVar))
        {
            var raw = SubFlowVarsMapper.ResolveNode("$." + node.SubCollectionVar, inst.VarsJson);
            if (raw is not JsonArray ja)
            {
                await eng.SubFlowErrorDisposeAsync(inst, schema, node, token, "E-WF-025", -1, null, null);   // 集合非数组
                return;
            }
            coll = ja;
            if (coll.Count == 0)
            {
                // N=0 空集完成：与完成策略无关，直接沿非错误出边前进、不回注（spec §3.1）
                eng.AddHistory(inst.Id, node.Id, inst.StarterId, "subFlowEmptyCollection", null);
                await eng.AdvanceToken(inst, schema, token);
                return;
            }
            if (coll.Count > _maxInstances)
            {
                await eng.SubFlowErrorDisposeAsync(inst, schema, node, token, "E-WF-025", -1, null, null);   // N 超上限
                return;
            }
        }
        int n = coll?.Count ?? 1;

        // ④ 逐 i 起子实例（停泊重入幂等：槽已存在跳过——Local ∪ DB 惯用法 + filtered unique 双保险）
        var childIds = new List<Guid>();
        for (int i = 0; i < n; i++)
        {
            int idx = i;
            bool exists = eng.Db.Wf_FlowInstances.Local.Any(x => x.ParentTokenId == token.Id && x.SubIndex == idx)
                || await eng.Db.Wf_FlowInstances.AnyAsync(x => x.ParentTokenId == token.Id && x.SubIndex == idx);
            if (exists) continue;
            var childVars = SubFlowVarsMapper.BuildChildVars(node.SubVarsInJson, inst.VarsJson,
                coll?[i], coll is null ? null : i);
            childIds.Add(await eng.SubmitChildAsync(node.SubFlowKey!, inst.StarterId, childVars, inst.Id, token.Id, i));
        }
        eng.AddHistory(inst.Id, node.Id, inst.StarterId, "subFlowStarted",
            $"n={n}; children=[{string.Join(",", childIds)}]");
        // ⑤ 父 token 停泊：不 Advance、不 Consume（子实例本身就是停泊凭据；唤醒走两段式回注 §3.2）
    }
}
```

`FlowEngine.cs` `DefaultHandlers()`（二期后八项）改九项（注释同步「第 9 个 subFlow」）：

```csharp
    private static IEnumerable<INodeHandler> DefaultHandlers() => new INodeHandler[]
    {
        new StartNodeHandler(), new ApprovalNodeHandler(), new EndNodeHandler(),
        new ParallelSplitNodeHandler(), new ParallelJoinNodeHandler(),
        new ServiceTaskNodeHandler(Array.Empty<IServiceTaskExecutor>()),
        new InclusiveSplitNodeHandler(), new InclusiveJoinNodeHandler(),
        new SubFlowNodeHandler(),   // ★ 子流程 B-T1：第 9 个（缺省上限 100；DI 实例携 app 配置）
    };
```

`Program.cs` 二期 inclusive 两行注册之后加：

```csharp
builder.Services.AddScoped<CP6.Core.Services.Wf.INodeHandler>(sp => new CP6.Core.Services.Wf.SubFlowNodeHandler(
    sp.GetRequiredService<IConfiguration>().GetValue<int?>("Wfs:SubFlowMaxInstances")));   // 子流程 B-T1 节点处理器（N 上限可配，缺省 100）
```

- [ ] **Step 4: 跑验证 PASS** — `--filter SubFlowHandlerTests` 全绿；`dotnet build CP6.WebApi/CP6.WebApi.csproj` 过。
- [ ] **Step 5: Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-subflow): B-T1 SubFlowNodeHandler第9个handler+SubmitChildAsync+错误处置+深度守卫E-WF-026"
```

---

### Task B-T2: CheckSubFlowGroupAsync 复核核心（计票 + 回注 + 错误处置 + 恰一次闸）

> 依赖 B-T1。**执行前必读 spec §3.2 全节（D5 修订版）。** 本 Task 只落复核方法本体（测试直调 internal），队列接线在 B-T3。

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowEngine.SubFlow.cs`（追加 `CheckSubFlowGroupAsync` + 私有 `ResumeSubFlowAsync`）
- Create: `CP6.Core/Services/Wf/SubFlowCascade.cs`（最小体：`CancelInstanceTree` + `CancelChildrenOfToken`，代码**逐字取 C-T1 Step 3 的同一份**——两 Task 以先落者为准，签名一致）
- Test: `CP6.Tests/Wf/SubFlowResumeCheckTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/SubFlowResumeCheckTests.cs
using System.Text.Json.Nodes;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static CP6.Tests.Wf.SubFlowTestHarness;

namespace CP6.Tests.Wf;

/// <summary>第二段复核 CheckSubFlowGroupAsync 计票语义（spec §3.2 表格逐行）。InternalsVisibleTo 直调；
/// 队列/fast path 接线面在 SubFlowTwoPhaseTests。</summary>
public class SubFlowResumeCheckTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static async Task<(CP6Context db, FlowEngine eng, Guid pid, Guid parkedTokenId, Guid pa, Guid ca)> SetupAsync(
        string? collectionVar = null, string? policy = null, string? varsIn = null, string? varsOut = null,
        bool errorEdge = false, string parentVars = "{}")
    {
        var db = NewDb();
        Guid pa = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        SeedDef(db, "parent", ParentSchema(pa, "child", collectionVar, policy, varsIn, varsOut, errorEdge));
        await db.SaveChangesAsync();
        var eng = Engine(db);
        var pid = await eng.SubmitAsync("parent", Guid.NewGuid(), parentVars);
        var parked = await db.Wf_FlowTokens.SingleAsync(t => t.InstanceId == pid && t.NodeId == "sub" && t.Status == FlowTokenStatus.Active);
        return (db, eng, pid, parked.Id, pa, ca);
    }

    private static async Task ActChildAsync(CP6Context db, FlowEngine eng, Guid childId, Guid approver, bool approve)
    {
        var t = await db.Wf_FlowTasks.SingleAsync(x => x.InstanceId == childId && x.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(t.Id, approver, approve);
    }

    [Fact]
    public async Task Single_ChildApproved_ResumesParent_MergesOutVar()
    {
        var (db, eng, pid, tok, pa, ca) = await SetupAsync(
            varsIn: "{\"result\":\"$.seed\"}", varsOut: "{\"subResult\":\"$.result\"}", parentVars: "{\"seed\":\"OK\"}");
        using var _ = db;
        var child = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == pid);
        await ActChildAsync(db, eng, child.Id, ca, approve: true);

        await eng.CheckSubFlowGroupAsync(tok);

        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid);
        Assert.Equal("OK", JsonNode.Parse(inst.VarsJson)!["subResult"]!.GetValue<string>());   // 单实例=标量回注
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == pa && t.Status == FlowTaskStatus.Pending));
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.InstanceId == pid && h.Action == "subFlowResumed"));
    }

    [Fact]
    public async Task Single_ChildRejected_NoErrorEdge_ParentRejected()
    {
        var (db, eng, pid, tok, _, ca) = await SetupAsync();
        using var _1 = db;
        var child = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == pid);
        await ActChildAsync(db, eng, child.Id, ca, approve: false);

        await eng.CheckSubFlowGroupAsync(tok);

        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid);
        Assert.Equal(FlowInstanceStatus.Rejected, inst.Status);
        var err = JsonNode.Parse(inst.VarsJson)!["subFlowError"]!;
        Assert.Equal(child.Id.ToString(), err["childInstanceId"]!.GetValue<string>());
        Assert.Equal(FlowInstanceStatus.Rejected, err["childStatus"]!.GetValue<int>());
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.InstanceId == pid && t.Status == FlowTokenStatus.Active));
    }

    [Fact]
    public async Task Single_ChildRejected_ErrorEdge_RoutesErrBranch_ParentStillRunning()
    {
        var (db, eng, pid, tok, pa, ca) = await SetupAsync(errorEdge: true);
        using var _ = db;
        var child = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == pid);
        await ActChildAsync(db, eng, child.Id, ca, approve: false);

        await eng.CheckSubFlowGroupAsync(tok);

        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid);
        Assert.Equal(FlowInstanceStatus.Running, inst.Status);
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.NodeId == "err" && t.Status == FlowTaskStatus.Pending));
    }

    [Fact]
    public async Task All_N3_AllApproved_ArrayWritebackBySubIndex()
    {
        var (db, eng, pid, tok, pa, ca) = await SetupAsync(collectionVar: "items",
            varsIn: "{\"v\":\"$.item\"}", varsOut: "{\"results\":\"$.v\"}", parentVars: "{\"items\":[10,20,30]}");
        using var _ = db;
        var children = await db.Wf_FlowInstances.Where(i => i.ParentInstanceId == pid).OrderBy(i => i.SubIndex).ToListAsync();
        // 乱序办结（2→0→1），回注仍按 SubIndex 排
        await ActChildAsync(db, eng, children[2].Id, ca, true);
        await eng.CheckSubFlowGroupAsync(tok);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == pa));   // 未齐不恢复

        await ActChildAsync(db, eng, children[0].Id, ca, true);
        await ActChildAsync(db, eng, children[1].Id, ca, true);
        await eng.CheckSubFlowGroupAsync(tok);

        var arr = (JsonArray)JsonNode.Parse((await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).VarsJson)!["results"]!;
        Assert.Equal(new[] { 10, 20, 30 }, arr.Select(x => x!.GetValue<int>()).ToArray());
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == pa && t.Status == FlowTaskStatus.Pending));
    }

    [Fact]
    public async Task All_OneRejected_CascadesSiblings_ErrorPath()
    {
        var (db, eng, pid, tok, _, ca) = await SetupAsync(collectionVar: "items", parentVars: "{\"items\":[1,2,3]}");
        using var _1 = db;
        var children = await db.Wf_FlowInstances.Where(i => i.ParentInstanceId == pid).OrderBy(i => i.SubIndex).ToListAsync();
        await ActChildAsync(db, eng, children[1].Id, ca, approve: false);

        await eng.CheckSubFlowGroupAsync(tok);

        Assert.Equal(FlowInstanceStatus.Rejected, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);
        // 附带动作：其余在途兄弟被级联撤回，其待办作废
        foreach (var sib in new[] { children[0], children[2] })
        {
            Assert.Equal(FlowInstanceStatus.Withdrawn, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == sib.Id)).Status);
            Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == sib.Id && t.Status == FlowTaskStatus.Pending));
        }
    }

    [Fact]
    public async Task Any_FirstApproved_Resumes_WithdrawsRest_ScalarWriteback()
    {
        var (db, eng, pid, tok, pa, ca) = await SetupAsync(collectionVar: "items", policy: "any",
            varsIn: "{\"v\":\"$.item\"}", varsOut: "{\"winner\":\"$.v\"}", parentVars: "{\"items\":[\"x\",\"y\"]}");
        using var _ = db;
        var children = await db.Wf_FlowInstances.Where(i => i.ParentInstanceId == pid).OrderBy(i => i.SubIndex).ToListAsync();
        await ActChildAsync(db, eng, children[1].Id, ca, approve: true);   // SubIndex=1 先过

        await eng.CheckSubFlowGroupAsync(tok);

        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid);
        Assert.Equal("y", JsonNode.Parse(inst.VarsJson)!["winner"]!.GetValue<string>());   // any=仅首个 Approved 的值(标量)
        Assert.Equal(FlowInstanceStatus.Withdrawn, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == children[0].Id)).Status);
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == pa && t.Status == FlowTaskStatus.Pending));
    }

    [Fact]
    public async Task Any_AllRejected_ErrorPath()
    {
        var (db, eng, pid, tok, _, ca) = await SetupAsync(collectionVar: "items", policy: "any", parentVars: "{\"items\":[1,2]}");
        using var _1 = db;
        var children = await db.Wf_FlowInstances.Where(i => i.ParentInstanceId == pid).ToListAsync();
        await ActChildAsync(db, eng, children[0].Id, ca, false);
        await eng.CheckSubFlowGroupAsync(tok);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);   // 任一驳不判死

        await ActChildAsync(db, eng, children[1].Id, ca, false);
        await eng.CheckSubFlowGroupAsync(tok);
        Assert.Equal(FlowInstanceStatus.Rejected, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);   // 全驳才错误处置
    }

    [Fact]
    public async Task ParentAlreadyTerminal_StateGate_ZeroAction()
    {
        var (db, eng, pid, tok, _, ca) = await SetupAsync();
        using var _1 = db;
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid);
        inst.Status = FlowInstanceStatus.Withdrawn;   // 父已终态（模拟撤回竞态窗口）
        await db.SaveChangesAsync();
        var child = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == pid);
        await ActChildAsync(db, eng, child.Id, ca, approve: true);

        await eng.CheckSubFlowGroupAsync(tok);   // 父实例状态闸 → 零动作

        Assert.Equal(0, await db.Wf_FlowHistories.CountAsync(h => h.InstanceId == pid && h.Action == "subFlowResumed"));
        Assert.Equal(FlowInstanceStatus.Withdrawn, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);
    }

    [Fact]
    public async Task TokenAlreadyResumed_LateCheck_ZeroAction()
    {
        var (db, eng, pid, tok, pa, ca) = await SetupAsync();
        using var _ = db;
        var child = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == pid);
        await ActChildAsync(db, eng, child.Id, ca, approve: true);
        await eng.CheckSubFlowGroupAsync(tok);
        await eng.CheckSubFlowGroupAsync(tok);   // 迟到复核（重入）

        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.InstanceId == pid && h.Action == "subFlowResumed"));
        Assert.Equal(1, await db.Wf_FlowTasks.CountAsync(t => t.InstanceId == pid && t.NodeId == "pa"));   // 不双推进
    }
}
```

- [ ] **Step 2: 跑验证 FAIL** — `--filter SubFlowResumeCheckTests`（方法不存在，编译失败）。

- [ ] **Step 3: 实现** — `FlowEngine.SubFlow.cs` 追加：

```csharp
    /// <summary>第二段复核（spec §3.2，幂等，fast path 与 worker 兜底共用）。恰一次保证：
    /// ① 三重状态闸（token Active + 停在 subFlow 节点 / 父实例 Running）；② 恢复/错误处置动作
    /// 触达父实例行（VarsJson 或 ModifyDate）→ SaveChanges 走 RowVersion 乐观并发，撞版 → 重读 → 闸零动作；
    /// ③ 计票前对子实例组逐行 Reload（「重读已提交数据」——身份映射会让同上下文旧读呈陈旧态，侦察结论 #6）。
    /// 丢唤醒由第一段原子入队闭合：每个子终态各自持凭据各自复核，后提交者必见完整组。</summary>
    internal async Task CheckSubFlowGroupAsync(Guid parentTokenId, CancellationToken ct = default)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var token = await _db.Wf_FlowTokens.FirstOrDefaultAsync(t => t.Id == parentTokenId, ct);
                if (token is null || token.Status != FlowTokenStatus.Active) return;          // 停泊状态闸：已恢复/已剪/已取消
                var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == token.InstanceId, ct);
                if (inst is null || inst.Status != FlowInstanceStatus.Running) return;        // 父实例状态闸：级联 Withdrawn 不回注
                var schema = await LoadSchemaAsync(inst.FlowKey);
                var node = FindNode(schema, token.NodeId);
                if (node is null || string.IsNullOrWhiteSpace(node.SubFlowKey)) return;       // token 已离开 subFlow 节点

                var children = await _db.Wf_FlowInstances
                    .Where(i => i.ParentTokenId == parentTokenId)
                    .OrderBy(i => i.SubIndex).ToListAsync(ct);
                if (children.Count == 0) return;                                              // 空集在 handler 已直通，此处组不存在
                foreach (var c in children.Where(c => _db.Entry(c).State == EntityState.Unchanged))
                    await _db.Entry(c).ReloadAsync(ct);                                       // ★ 重读已提交数据（防身份映射陈旧态）

                bool any = string.Equals((node.SubCompletionPolicy ?? SubFlowCompletionPolicy.All).Trim(),
                    SubFlowCompletionPolicy.Any, StringComparison.OrdinalIgnoreCase);
                var approved = children.Where(c => c.Status == FlowInstanceStatus.Approved).ToList();
                var dead     = children.Where(c => c.Status is FlowInstanceStatus.Rejected or FlowInstanceStatus.Withdrawn).ToList();
                var inFlight = children.Where(c => c.Status is FlowInstanceStatus.Running or FlowInstanceStatus.Suspended
                                                             or FlowInstanceStatus.Draft).ToList();

                if (!any)
                {
                    if (dead.Count > 0)
                    {
                        foreach (var c in inFlight) SubFlowCascade.CancelInstanceTree(_db, c);   // all：任一死→级联取消其余在途
                        await SubFlowErrorDisposeAsync(inst, schema, node, token,
                            null, dead[0].SubIndex ?? 0, dead[0].Id, dead[0].Status);
                    }
                    else if (inFlight.Count == 0)
                        await ResumeSubFlowAsync(inst, schema, node, token, approved, aggregate: node.SubCollectionVar != null);
                    else return;   // all 未齐——等下一个子终态的凭据
                }
                else
                {
                    if (approved.Count > 0)
                    {
                        foreach (var c in inFlight) SubFlowCascade.CancelInstanceTree(_db, c);   // any：恢复时级联撤回其余在途
                        await ResumeSubFlowAsync(inst, schema, node, token,
                            new List<Wf_FlowInstance> { approved[0] }, aggregate: false);        // 首个= SubIndex 最小的 Approved（确定性）
                    }
                    else if (inFlight.Count == 0 && dead.Count == children.Count)
                        await SubFlowErrorDisposeAsync(inst, schema, node, token,
                            null, dead[0].SubIndex ?? 0, dead[0].Id, dead[0].Status);
                    else return;   // any 未决
                }

                inst.ModifyDate = DateTime.Now;   // ★ 写触达父行 → RowVersion 乐观并发（恰一次闸，仿 ActOnceAsync Task6/Fix4）
                await DispatchIfFinishedAsync(inst, inst.StarterId, null);   // 错误处置可打出终态；父自身是子实例时递归入队（孙 subFlow）
                await _db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 2)
            {
                foreach (var e in _db.ChangeTracker.Entries().ToList()) await e.ReloadAsync(ct);   // 撞版 → 重读 → 状态闸零动作
            }
        }
    }

    /// <summary>恢复路径（spec §3.2 第 2 步）：SubVarsOutJson 回注父 vars（MergeOutputVars 保留前缀同款拦截）
    /// → 恢复父 token 沿非错误出边推进。</summary>
    private async Task ResumeSubFlowAsync(Wf_FlowInstance inst, FlowSchema schema, FlowNode node, Wf_FlowToken token,
        IReadOnlyList<Wf_FlowInstance> approved, bool aggregate)
    {
        var outVars = SubFlowVarsMapper.BuildOutMerge(node.SubVarsOutJson,
            approved.Select(c => (c.SubIndex ?? 0, c.VarsJson)).ToList(), aggregate);
        if (outVars.Count > 0)
        {
            var merged = ServiceVarsHelper.MergeOutputVars(inst.VarsJson, outVars);
            inst.VarsJson = merged.VarsJson;
        }
        AddHistory(inst.Id, node.Id, inst.StarterId, "subFlowResumed", $"approved={approved.Count}");
        await AdvanceToken(inst, schema, token);   // 沿非错误出边（IsError != true）
    }
```

> **注**：本 Task 需要 `SubFlowCascade.CancelInstanceTree` 已存在——为保依赖顺序，本 Task 同时落 `SubFlowCascade.cs` 的**最小体**（`CancelInstanceTree` 单实例撤回语义 + 孙代递归，代码见 C-T1 Step 3，逐字同一份），C-T1 只补三处挂钩与 `CancelChildrenOfToken` 的钩子消费。两 Task 以先落者为准、签名一致。

- [ ] **Step 4: 跑验证 PASS** — `--filter SubFlowResumeCheckTests` 全绿。
- [ ] **Step 5: Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-subflow): B-T2 CheckSubFlowGroupAsync复核核心(all/any计票+SubIndex回注+错误处置+RowVersion恰一次闸)"
```

---

### Task B-T3: 两段式接线——第一段原子入队 + worker 内部 Kind 短路 + fast path

> 依赖 B-T2。**执行前必读 spec §3.2 第一段/两入口 + 侦察结论 #2/#4/#5。**

**Files:**
- Create: `CP6.Core/Services/Wf/SubFlowResume.cs`
- Modify: `CP6.Core/Services/Wf/FlowEngine.cs`（`DispatchIfFinishedAsync` 首行钩子；`SubmitAsync`/`StartDraftAsync`/`ActAsync`/`ActAsAsync`/`ResumeServiceTokenAsync`/`FailServiceTokenAsync` 尾部 fast path）
- Modify: `CP6.Core/Services/Wf/FlowEngine.SubFlow.cs`（追加 `FastPathSubFlowResumeAsync`）
- Modify: `CP6.Core/Services/Wf/WfServiceJobService.cs`（`ScanOnceAsync` 内部 Kind 短路）
- Modify: `CP6.Core/Services/Wf/TaskCenterService.cs`（ctor 可选 engine + 撤回入队 + fast path；级联钩子留 C-T1）
- Test: `CP6.Tests/Wf/SubFlowTwoPhaseTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/SubFlowTwoPhaseTests.cs
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static CP6.Tests.Wf.SubFlowTestHarness;

namespace CP6.Tests.Wf;

/// <summary>入队-复核两段式接线（spec §3.2 D5）：第一段凭据落库形态 / fast path 同请求收敛 /
/// worker 内部 Kind 短路兜底 / 手工撤回入计票。InMemory 单线程面；竞态面在 SubFlowConcurrencyTests。</summary>
public class SubFlowTwoPhaseTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    [Fact]
    public async Task ChildTerminal_JobPersisted_KindTokenNodePayload()
    {
        using var db = NewDb();
        Guid pa = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        SeedDef(db, "parent", ParentSchema(pa, "child", collectionVar: "items"));
        await db.SaveChangesAsync();
        var eng = Engine(db);
        var pid = await eng.SubmitAsync("parent", Guid.NewGuid(), "{\"items\":[1,2]}");
        var parked = await db.Wf_FlowTokens.SingleAsync(t => t.InstanceId == pid && t.NodeId == "sub");
        var c0 = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == pid && i.SubIndex == 0);

        var t0 = await db.Wf_FlowTasks.SingleAsync(x => x.InstanceId == c0.Id && x.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(t0.Id, ca, approve: true);   // c0 终态（组未齐,父不动）

        var job = await db.Wf_ServiceJobs.SingleAsync(j => j.Kind == WfJobKind.SubFlowResume);
        Assert.Equal(c0.Id, job.TokenId);                          // ★ 防撞定案：TokenId=子实例 Id
        Assert.Equal(SubFlowResume.JobNodeId, job.NodeId);         // 哨兵
        Assert.Equal(pid, job.InstanceId);                         // 归父实例
        var payload = SubFlowResumePayload.Parse(job.ActionRefJson);
        Assert.NotNull(payload);
        Assert.Equal(parked.Id, payload!.ParentTokenId);
        Assert.Equal(ServiceJobStatus.Succeeded, job.Status);      // fast path 已消费凭据（组未齐也算消费）
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);
    }

    [Fact]
    public async Task ApproveLastChild_FastPath_ResumesWithinRequest()
    {
        using var db = NewDb();
        Guid pa = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        SeedDef(db, "parent", ParentSchema(pa, "child", collectionVar: "items"));
        await db.SaveChangesAsync();
        var eng = Engine(db);
        var pid = await eng.SubmitAsync("parent", Guid.NewGuid(), "{\"items\":[1,2]}");

        foreach (var c in await db.Wf_FlowInstances.Where(i => i.ParentInstanceId == pid).OrderBy(i => i.SubIndex).ToListAsync())
        {
            var t = await db.Wf_FlowTasks.SingleAsync(x => x.InstanceId == c.Id && x.Status == FlowTaskStatus.Pending);
            await eng.ActAsync(t.Id, ca, approve: true);   // 无任何手动 Check——fast path 自动收敛
        }

        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == pa && t.Status == FlowTaskStatus.Pending));
        Assert.False(await db.Wf_ServiceJobs.AnyAsync(j => j.Kind == WfJobKind.SubFlowResume && j.Status == ServiceJobStatus.Pending));
    }

    [Fact]
    public async Task InstantTerminalChild_SubmitFastPath_ParentAdvancesImmediately()
    {
        using var db = NewDb();
        Guid pa = Guid.NewGuid();
        SeedDef(db, "instant", InstantChildSchema());
        SeedDef(db, "parent", ParentSchema(pa, "instant"));
        await db.SaveChangesAsync();

        var pid = await Engine(db).SubmitAsync("parent", Guid.NewGuid(), "{}");   // 子起即 Approved → SubmitAsync 尾 fast path

        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == pa && t.Status == FlowTaskStatus.Pending));
        Assert.Equal(FlowInstanceStatus.Approved,
            (await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == pid)).Status);
    }

    [Fact]
    public async Task ManualChildWithdraw_NullEngine_JobPending_WorkerInterceptDisposes()
    {
        using var db = NewDb();
        Guid pa = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        SeedDef(db, "parent", ParentSchema(pa, "child"));
        await db.SaveChangesAsync();
        var eng = Engine(db);
        var pid = await eng.SubmitAsync("parent", Guid.NewGuid(), "{}");
        var child = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == pid);

        // engine=null 的既有构造 → 无 fast path（=「fast path 前崩溃」行为等价面）：凭据必须已落库
        await new TaskCenterService(db).WithdrawAsync(child.Id, child.StarterId);
        var job = await db.Wf_ServiceJobs.SingleAsync(j => j.Kind == WfJobKind.SubFlowResume);
        Assert.Equal(ServiceJobStatus.Pending, job.Status);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);   // 尚未处置

        // worker 兜底：内部 Kind 短路 → 复核 → all 策略 Withdrawn=死 → 错误处置（手工撤回入计票,spec §3.3 末条）
        var svc = new WfServiceJobService(db, eng, Array.Empty<IServiceTaskExecutor>());
        var n = await svc.ScanOnceAsync(DateTime.UtcNow, "w1");
        Assert.Equal(1, n);

        Assert.Equal(ServiceJobStatus.Succeeded, (await db.Wf_ServiceJobs.SingleAsync(j => j.Id == job.Id)).Status);
        Assert.Equal(FlowInstanceStatus.Rejected, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);
    }

    [Fact]
    public async Task GrandChild_NestedResume_PropagatesTwoLevels()
    {
        using var db = NewDb();
        Guid ca = Guid.NewGuid(), pa = Guid.NewGuid();
        SeedDef(db, "leaf", ChildSchema(ca));
        SeedDef(db, "mid", ParentSchema(pa, "leaf"));      // mid 的 pa 审批在 leaf 恢复后出现
        SeedDef(db, "top", ParentSchema(pa, "mid"));
        await db.SaveChangesAsync();
        var eng = Engine(db);
        var topId = await eng.SubmitAsync("top", Guid.NewGuid(), "{}");
        var midInst = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == topId);
        var leafInst = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == midInst.Id);

        // leaf 审批过 → mid 恢复到 pa；mid 的 pa 过 → mid Approved → top 恢复（孙 subFlow 递归,全靠 fast path 链）
        var tLeaf = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == leafInst.Id && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(tLeaf.Id, ca, true);
        var tMid = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == midInst.Id && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(tMid.Id, pa, true);

        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == midInst.Id)).Status);
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == topId && t.AssigneeId == pa && t.Status == FlowTaskStatus.Pending));
    }
}
```

- [ ] **Step 2: 跑验证 FAIL** — `--filter SubFlowTwoPhaseTests`（`SubFlowResume` 不存在，编译失败）。

- [ ] **Step 3: 实现**

`SubFlowResume.cs` 全文：

```csharp
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>subFlowResume 内部 job 载荷（spec §3.2 第一段）。ParentTokenId 是复核定位键；
/// ChildInstanceId/SubIndex 供排查与哨兵防重。</summary>
internal sealed record SubFlowResumePayload(Guid ParentTokenId, Guid ParentInstanceId, Guid ChildInstanceId, int SubIndex)
{
    private static readonly JsonSerializerOptions Opts = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    public string ToJson() => JsonSerializer.Serialize(this, Opts);

    public static SubFlowResumePayload? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var p = JsonSerializer.Deserialize<SubFlowResumePayload>(json, Opts);
            return p is { ParentTokenId: var t } && t != Guid.Empty ? p : null;
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>子终态第一段原子入队（spec D5）：只做纯内存 Add——与子终态同一 SaveChanges 持久化即 crash-safe，
/// 窗口内零计票/零推进/零外呼（DispatchIfFinished 原子接缝铁律相容）。
/// <para>防撞定案（计划侦察结论 #5）：<c>TokenId=子实例 Id</c> + <c>NodeId="$subFlowResume"</c> 哨兵——
/// 若用 ParentTokenId 占 TokenId 槽，同组两个并发子终态会撞 <c>UX_Wf_ServiceJob_LiveToken</c> filtered unique
/// 令子终态事务整体失败；子实例 Id 全局唯一且一次终态一条凭据，天然防重不撞组。ParentTokenId 走载荷。</para></summary>
internal static class SubFlowResume
{
    public const string JobNodeId = "$subFlowResume";

    public static void EnqueueIfChild(CP6Context db, Wf_FlowInstance inst)
    {
        if (inst.ParentInstanceId is not Guid pi || inst.ParentTokenId is not Guid pt) return;   // 顶层实例：纯谓词短路,零开销

        // 防重（Local ∪ DB 惯用法,镜像 ServiceTaskNodeHandler.EnqueueServiceJob）：每子实例至多一条活跃凭据
        if (db.Wf_ServiceJobs.Local.Any(j => j.TokenId == inst.Id && j.NodeId == JobNodeId
                && (j.Status == ServiceJobStatus.Pending || j.Status == ServiceJobStatus.Running)))
            return;
        var localIds = db.Wf_ServiceJobs.Local
            .Where(j => j.TokenId == inst.Id && j.NodeId == JobNodeId).Select(j => j.Id).ToHashSet();
        if (db.Wf_ServiceJobs.Any(j => j.TokenId == inst.Id && j.NodeId == JobNodeId
                && (j.Status == ServiceJobStatus.Pending || j.Status == ServiceJobStatus.Running)
                && !localIds.Contains(j.Id)))
            return;

        var now = DateTime.UtcNow;
        db.Wf_ServiceJobs.Add(new Wf_ServiceJob
        {
            Id = Guid.NewGuid(),
            InstanceId = pi,               // 归父实例：父终止清 Pending 是良性动作（复核状态闸兜底）
            TokenId = inst.Id,             // ★ 子实例 Id 占防撞键
            NodeId = JobNodeId,
            Kind = WfJobKind.SubFlowResume,
            ActionRefJson = new SubFlowResumePayload(pt, pi, inst.Id, inst.SubIndex ?? 0).ToJson(),
            DueAtUtc = now,
            Status = ServiceJobStatus.Pending,
            AttemptCount = 0,
            MaxAttempts = 4,               // 复核幂等,重投无害；对齐 job 缺省口径
            NextAttemptAtUtc = now,
            CreateDate = now,
        });   // TenantId 由 StampTenant 自动盖
    }
}
```

`FlowEngine.cs` `DispatchIfFinishedAsync` 方法体**首行**加（Approved/Rejected 两分支之前）：

```csharp
        // ★ 子流程第一段（spec D5）：子终态窗口内只原子入队唤醒凭据（Withdrawn 走 TaskCenterService.WithdrawAsync 的对称钩子）
        if (inst.Status is FlowInstanceStatus.Approved or FlowInstanceStatus.Rejected)
            SubFlowResume.EnqueueIfChild(_db, inst);
```

`FlowEngine.SubFlow.cs` 追加 fast path：

```csharp
    /// <summary>提交后 fast path（spec §3.2 两入口之一）：扫本上下文 Local 中 Pending 的 subFlowResume 凭据
    /// 逐条复核并标 Succeeded（worker 迟到看见已完成 → 状态闸零动作）。外层 for 让「复核推进父 → 父又终态 →
    /// 再入队祖父凭据」的嵌套链同请求收敛，上限与深度守卫同口径。撞 job RowVersion（worker 已抢走）→ 让给 worker。
    /// 对无 subFlow 的请求 = Local 空集 O(1) no-op。</summary>
    internal async Task FastPathSubFlowResumeAsync(CancellationToken ct = default)
    {
        for (int round = 0; round < SubFlowLimits.MaxDepth; round++)
        {
            var jobs = _db.Wf_ServiceJobs.Local
                .Where(j => j.Kind == WfJobKind.SubFlowResume && j.Status == ServiceJobStatus.Pending)
                .ToList();
            if (jobs.Count == 0) return;
            foreach (var job in jobs)
            {
                var payload = SubFlowResumePayload.Parse(job.ActionRefJson);
                if (payload is null) continue;                       // 载荷坏 → 留给 worker 标 Failed（唯一记账处）
                await CheckSubFlowGroupAsync(payload.ParentTokenId, ct);
                job.Status = ServiceJobStatus.Succeeded;             // 凭据已消费（组未齐也算——组齐由各子终态各自凭据保证）
                job.CompletedAtUtc = DateTime.UtcNow;
                try { await _db.SaveChangesAsync(ct); }
                catch (DbUpdateConcurrencyException) { await _db.Entry(job).ReloadAsync(ct); }
            }
        }
    }
```

`FlowEngine.cs` fast path 接线（六处，逐处一行）：
1. `SubmitAsync`：`await _db.SaveChangesAsync();` 后、`return inst.Id;` 前加 `await FastPathSubFlowResumeAsync();`
2. `StartDraftAsync`：尾部 `SaveChangesAsync` 后加同一行。
3. `ActAsync`：重构为「循环内 `return` 改 `break`，循环后 fast path」——

```csharp
    public async Task ActAsync(Guid taskId, Guid actorId, bool approve, string? comment = null)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { await ActOnceAsync(taskId, actorId, approve, comment); break; }
            catch (DbUpdateConcurrencyException) when (attempt < 2)
            {
                foreach (var e in _db.ChangeTracker.Entries().ToList()) await e.ReloadAsync();
            }
        }
        await FastPathSubFlowResumeAsync();   // ★ 子流程 fast path：非子终态办理时 Local 空集 O(1) no-op
    }
```

4. `ActAsAsync`：同款重构。
5. `ResumeServiceTokenAsync`：`await _db.SaveChangesAsync();` 后、`return;` 前加 `await FastPathSubFlowResumeAsync();`（fast path 内部吞并发冲突，不会触发外层重试重入）。
6. `FailServiceTokenAsync`：同第 5 处。

`WfServiceJobService.cs` `ScanOnceAsync`——单 job try 块内、**状态闸④之前**加短路（侦察结论 #4：TokenId=子实例 Id 哨兵不匹配真实 token，绝不能走闸④否则被误 Cancelled）：

```csharp
                // ── 子流程内部 Kind 短路（子流程 spec §3.2 worker 兜底入口）：不进 executor 注册表、不走状态闸④
                //    （job.TokenId=子实例 Id 哨兵）。复核幂等：组未齐/已处理 → 引擎内状态闸零动作，凭据照常消费。
                if (job.Kind == WfJobKind.SubFlowResume)
                {
                    var subPayload = SubFlowResumePayload.Parse(job.ActionRefJson);
                    if (subPayload is null)
                    {
                        job.Status = ServiceJobStatus.Failed;
                        job.LastError = Trunc("E-WF-025 subFlowResume 载荷非法");
                        job.CompletedAtUtc = nowUtc;
                    }
                    else
                    {
                        await _engine.CheckSubFlowGroupAsync(subPayload.ParentTokenId, ct);
                        job.Status = ServiceJobStatus.Succeeded;
                        job.CompletedAtUtc = nowUtc;
                    }
                    await _db.SaveChangesAsync(ct);
                    processed++;
                    continue;
                }
```

`TaskCenterService.cs`：ctor 追加可选参数（侦察结论 #9，既有 9 处 `new TaskCenterService(db)` 零改动）：

```csharp
    private readonly CP6Context _db;
    private readonly FlowEngine? _engine;   // 子流程 fast path 用；null（既有测试构造）= 交 worker 20s 兜底
    public TaskCenterService(CP6Context db, FlowEngine? engine = null) { _db = db; _engine = engine; }
```

`WithdrawAsync`：pendingJobs 清理块之后、`_db.Wf_FlowHistories.Add(...)` 之前加入队钩子；`SaveChangesAsync` 之后加 fast path：

```csharp
        // ★ 子流程第一段（Withdrawn 与 Approved/Rejected 对称,spec §3.3 末条「手工撤回入计票」）。
        //    注意置于本方法 pendingJobs 清理之后：本凭据 InstanceId=父实例,不会被上面按本实例的清理误杀。
        SubFlowResume.EnqueueIfChild(_db, inst);
```

```csharp
        await _db.SaveChangesAsync();
        if (_engine is not null) await _engine.FastPathSubFlowResumeAsync();   // ★ 子流程 fast path（null=worker 兜底）
```

- [ ] **Step 4: 跑验证 PASS** — `--filter SubFlowTwoPhaseTests` + `--filter SubFlowResumeCheckTests`（B-T2 测试在 fast path 下必须照绿——手动复核变为幂等重入）+ `--filter SubFlowHandlerTests` 全绿。
- [ ] **Step 5: 全量 Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf    # 既有服务任务/并发测试必须全绿（ScanOnceAsync/ActAsync 是热路径改动）
dotnet build CP6.WebApi/CP6.WebApi.csproj
git add -A && git commit -m "feat(wfs-subflow): B-T3 两段式接线(DispatchIfFinished/Withdraw原子入队+worker内部Kind短路+六处fast path)"
```

---

### Task B-T4: 跨事务并发/幂等定点矩阵（SQLite 双 context）

> 依赖 B-T3。**这是 spec §7「幂等/竞态」定点矩阵的落点**——用双 context 模拟两个 web 请求事务，SQLite 触发器让父实例/job RowVersion 真实生效。

**Files:**
- Test: `CP6.Tests/Wf/SubFlowConcurrencyTests.cs`

- [ ] **Step 1: 写测试（在 B-T3 实现上应直接绿——这是并发语义锁定测试，先跑基线再锁）**

```csharp
// CP6.Tests/Wf/SubFlowConcurrencyTests.cs
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using static CP6.Tests.Wf.SubFlowTestHarness;

namespace CP6.Tests.Wf;

/// <summary>两段式回注竞态矩阵（spec §3.2/§7）：SQLite 共享连接 + rowversion 触发器 + 双 context 模拟两事务。
/// all 不丢唤醒（陈旧身份映射被 Reload 击穿）/ any 不双恢复（RowVersion 撞 → 状态闸零动作）/
/// fast path 崩溃 worker 兜底 / 停泊重入唯一槽。</summary>
public class SubFlowConcurrencyTests
{
    private static async Task<(Guid pid, Guid parkedTokenId, Guid pa, Guid ca, List<Guid> childIds)> SeedAndSubmitAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn, string? policy, string parentVars,
        string? varsOut = null, string? varsIn = null)
    {
        Guid pa = Guid.NewGuid(), ca = Guid.NewGuid();
        using (var db = Ctx(conn))
        {
            SeedDef(db, "child", ChildSchema(ca));
            SeedDef(db, "parent", ParentSchema(pa, "child", collectionVar: "items", policy: policy,
                varsIn: varsIn, varsOut: varsOut));
            await db.SaveChangesAsync();
        }
        using (var db = Ctx(conn))
        {
            var pid = await Engine(db).SubmitAsync("parent", Guid.NewGuid(), parentVars);
            var parked = await db.Wf_FlowTokens.SingleAsync(t => t.InstanceId == pid && t.NodeId == "sub");
            var kids = await db.Wf_FlowInstances.Where(i => i.ParentInstanceId == pid)
                .OrderBy(i => i.SubIndex).Select(i => i.Id).ToListAsync();
            return (pid, parked.Id, pa, ca, kids);
        }
    }

    [Fact]
    public async Task All_TwoRequests_StaleIdentityMap_ReloadDefeatsIt_NoLostWakeup()
    {
        using var conn = NewSqliteWithSchema();
        var (pid, tok, pa, ca, kids) = await SeedAndSubmitAsync(conn, null, "{\"items\":[1,2]}");

        // 请求2 的 context 先把 child0 拉进身份映射（陈旧态 Running）——复核若不 Reload 会误判「未齐」丢唤醒
        using var db2 = Ctx(conn);
        _ = await db2.Wf_FlowInstances.SingleAsync(i => i.Id == kids[0]);

        // 请求1：审结 child0（独立事务提交）
        using (var db1 = Ctx(conn))
        {
            var t0 = await db1.Wf_FlowTasks.SingleAsync(t => t.InstanceId == kids[0] && t.Status == FlowTaskStatus.Pending);
            await Engine(db1).ActAsync(t0.Id, ca, approve: true);
        }

        // 请求2：审结 child1 → fast path 复核（其身份映射中 child0 是陈旧 Running）→ Reload 击穿 → 恢复父
        var t1 = await db2.Wf_FlowTasks.SingleAsync(t => t.InstanceId == kids[1] && t.Status == FlowTaskStatus.Pending);
        await Engine(db2).ActAsync(t1.Id, ca, approve: true);

        using var check = Ctx(conn);
        Assert.True(await check.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == pa && t.Status == FlowTaskStatus.Pending));
        Assert.Equal(1, await check.Wf_FlowHistories.CountAsync(h => h.InstanceId == pid && h.Action == "subFlowResumed"));
        Assert.False(await check.Wf_ServiceJobs.AnyAsync(j => j.Kind == WfJobKind.SubFlowResume && j.Status == ServiceJobStatus.Pending));
    }

    [Fact]
    public async Task Any_LateStaleChecker_RowVersionClash_StateGate_NoDoubleResume()
    {
        using var conn = NewSqliteWithSchema();
        var (pid, tok, pa, ca, kids) = await SeedAndSubmitAsync(conn, "any", "{\"items\":[\"x\",\"y\"]}",
            varsOut: "{\"winner\":\"$.v\"}", varsIn: "{\"v\":\"$.item\"}");

        // 迟到复核方：先把父 token/实例/子组拉进身份映射（陈旧态：token 仍停泊）
        using var db2 = Ctx(conn);
        _ = await db2.Wf_FlowTokens.SingleAsync(t => t.Id == tok);
        _ = await db2.Wf_FlowInstances.SingleAsync(i => i.Id == pid);
        _ = await db2.Wf_FlowInstances.Where(i => i.ParentInstanceId == pid).ToListAsync();

        // 胜方：child0 审过 → any 立即恢复父 + 级联撤回 child1（独立事务已提交）
        using (var db1 = Ctx(conn))
        {
            var t0 = await db1.Wf_FlowTasks.SingleAsync(t => t.InstanceId == kids[0] && t.Status == FlowTaskStatus.Pending);
            await Engine(db1).ActAsync(t0.Id, ca, approve: true);
        }

        // 败方：拿陈旧停泊 token 直闯复核 → 双恢复动作在 SaveChanges 撞父行 RowVersion → 重读 → 状态闸零动作
        await Engine(db2).CheckSubFlowGroupAsync(tok);

        using var check = Ctx(conn);
        Assert.Equal(1, await check.Wf_FlowHistories.CountAsync(h => h.InstanceId == pid && h.Action == "subFlowResumed"));
        Assert.Equal(1, await check.Wf_FlowTasks.CountAsync(t => t.InstanceId == pid && t.NodeId == "pa"));   // 不双推进
        Assert.Equal(FlowInstanceStatus.Withdrawn, (await check.Wf_FlowInstances.SingleAsync(i => i.Id == kids[1])).Status);
    }

    [Fact]
    public async Task FastPathCrash_WorkerScan_RescuesWakeup()
    {
        using var conn = NewSqliteWithSchema();
        var (pid, tok, pa, ca, kids) = await SeedAndSubmitAsync(conn, "any", "{\"items\":[1,2]}");

        // 「崩溃窗口」等价面：engine=null 撤回 child0 → 凭据落库但第二段没跑
        using (var db1 = Ctx(conn))
        {
            var child = await db1.Wf_FlowInstances.SingleAsync(i => i.Id == kids[0]);
            await new TaskCenterService(db1).WithdrawAsync(child.Id, child.StarterId);
            Assert.True(await db1.Wf_ServiceJobs.AnyAsync(j => j.Kind == WfJobKind.SubFlowResume && j.Status == ServiceJobStatus.Pending));
        }

        // worker 兜底（新 scope=新 context）：any 策略一死一活 → 未决,凭据消费但父照常停泊
        using (var db2 = Ctx(conn))
        {
            var svc = new WfServiceJobService(db2, Engine(db2), Array.Empty<IServiceTaskExecutor>());
            Assert.Equal(1, await svc.ScanOnceAsync(DateTime.UtcNow, "w1"));
        }
        using (var check1 = Ctx(conn))
            Assert.Equal(FlowInstanceStatus.Running, (await check1.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);

        // child1 审过 → fast path 恢复（证明前一凭据消费不吞后续唤醒）
        using (var db3 = Ctx(conn))
        {
            var t1 = await db3.Wf_FlowTasks.SingleAsync(t => t.InstanceId == kids[1] && t.Status == FlowTaskStatus.Pending);
            await Engine(db3).ActAsync(t1.Id, ca, approve: true);
        }
        using var check2 = Ctx(conn);
        Assert.True(await check2.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == pa && t.Status == FlowTaskStatus.Pending));
    }

    [Fact]
    public async Task ParkedReentry_UniqueSlot_NoDuplicateChildren()
    {
        using var conn = NewSqliteWithSchema();
        var (pid, tok, _, _, kids) = await SeedAndSubmitAsync(conn, null, "{\"items\":[1,2]}");

        using var db = Ctx(conn);
        var eng = Engine(db);
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid);
        var token = await db.Wf_FlowTokens.SingleAsync(t => t.Id == tok);
        var def = await db.Wf_FlowDefs.SingleAsync(d => d.FlowKey == "parent");
        var schema = System.Text.Json.JsonSerializer.Deserialize<FlowSchema>(def.SchemaJson,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var node = schema.Nodes.Single(n => n.Id == "sub");

        await eng.EnterNodeAsync(inst, schema, node, token);   // 停泊重入（InternalsVisibleTo 直调）
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Wf_FlowInstances.CountAsync(i => i.ParentTokenId == tok));   // 槽幂等,不重复起子
    }
}
```

- [ ] **Step 2: 跑验证** — `--filter SubFlowConcurrencyTests` 全绿。若「Reload 击穿」测试红 → B-T2 的子组 Reload 缺失/写错，回去修实现而不是改测试。
- [ ] **Step 3: Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "test(wfs-subflow): B-T4 跨事务并发矩阵(all不丢唤醒+any不双恢复+worker兜底+停泊重入唯一槽,SQLite双context)"
```

---

## Wave S-C — 级联取消三路径（缺一即孤儿；与 S-D 互不依赖可并行）

### Task C-T1: SubFlowCascade 工具 + 三处挂钩（父终止递归 / 第五清 / 撤回）

> 依赖 B-T3。**执行前必读 spec §3.3 全节 + 侦察结论 #10/#11。** `CancelTokenSubtree` 第五清改在二期声明的文件 `CP6.Core/Services/Wf/FlowEngine.Tokens.cs` 的同名方法上；并按 spec 预案在 `SubFlowCascade.cs` 与 `FlowEngine.Prune.cs`（二期文件）各写一条互指注释。

**Files:**
- Create/Modify: `CP6.Core/Services/Wf/SubFlowCascade.cs`（B-T2 已落最小体则在其上补 `CancelChildrenOfToken` 与注释）
- Modify: `CP6.Core/Services/Wf/FlowEngine.Tokens.cs`（`CancelAllActiveTokens` 钩子 + `CancelTokenSubtree` 第五清）
- Modify: `CP6.Core/Services/Wf/TaskCenterService.cs`（`WithdrawAsync` token 循环后级联钩子）
- Modify: `CP6.Core/Services/Wf/FlowEngine.Prune.cs`（只加一条互指注释，零逻辑改动）
- Test: `CP6.Tests/Wf/SubFlowCascadeTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/SubFlowCascadeTests.cs
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static CP6.Tests.Wf.SubFlowTestHarness;

namespace CP6.Tests.Wf;

/// <summary>级联取消三路径（spec §3.3）：父终止递归 / CancelTokenSubtree 第五清 / 撤回路径。
/// 断言口径：子实例 Withdrawn + 在途待办清 + 不回注（父无 subFlowResumed 履历）。</summary>
public class SubFlowCascadeTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    /// <summary>并行父：ps → split(可配 onBranchReject) → ( sub ⊂ A支 , b ⊂ B支 ) → join → pe。</summary>
    private static FlowSchema ParallelParent(Guid ub, string subFlowKey, string? onBranchReject = null) => new()
    {
        Start = "ps",
        Nodes =
        {
            new FlowNode { Id = "ps", Type = "start" },
            new FlowNode { Id = "split", Type = "parallelSplit", OnBranchReject = onBranchReject },
            new FlowNode { Id = "sub", Type = "subFlow", SubFlowKey = subFlowKey },
            new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
            new FlowNode { Id = "join", Type = "parallelJoin" },
            new FlowNode { Id = "pe", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "ps", To = "split" },
            new FlowEdge { From = "split", To = "sub" }, new FlowEdge { From = "split", To = "b" },
            new FlowEdge { From = "sub", To = "join" }, new FlowEdge { From = "b", To = "join" },
            new FlowEdge { From = "join", To = "pe" },
        },
    };

    [Fact]
    public async Task ParentWithdraw_CascadesThreeLevels_NoWriteback()
    {
        using var db = NewDb();
        Guid ca = Guid.NewGuid(), pa = Guid.NewGuid();
        SeedDef(db, "leaf", ChildSchema(ca));
        SeedDef(db, "mid", ParentSchema(pa, "leaf"));
        SeedDef(db, "top", ParentSchema(pa, "mid"));
        await db.SaveChangesAsync();
        var eng = Engine(db);
        var topId = await eng.SubmitAsync("top", Guid.NewGuid(), "{}");
        var mid = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == topId);
        var leaf = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == mid.Id);
        var starter = (await db.Wf_FlowInstances.SingleAsync(i => i.Id == topId)).StarterId;

        await new TaskCenterService(db, eng).WithdrawAsync(topId, starter);

        Assert.Equal(FlowInstanceStatus.Withdrawn, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == mid.Id)).Status);
        Assert.Equal(FlowInstanceStatus.Withdrawn, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == leaf.Id)).Status);
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == leaf.Id && t.Status == FlowTaskStatus.Pending));
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => (t.InstanceId == mid.Id || t.InstanceId == leaf.Id)
            && t.Status == FlowTokenStatus.Active));
        // 级联 Withdrawn 不回注：父/祖父零 subFlowResumed；级联路径不投递唤醒凭据
        Assert.Equal(0, await db.Wf_FlowHistories.CountAsync(h => h.Action == "subFlowResumed"));
        Assert.False(await db.Wf_ServiceJobs.AnyAsync(j => j.Kind == WfJobKind.SubFlowResume && j.Status == ServiceJobStatus.Pending));
        Assert.True(await db.Wf_FlowHistories.AnyAsync(h => h.InstanceId == leaf.Id && h.Action == "subFlowCascadeCancelled"));
    }

    [Fact]
    public async Task SiblingReject_DefaultCascade_ParentTerminates_ChildrenCancelled()
    {
        using var db = NewDb();
        Guid ub = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "pp", FlowName = "pp", FormKey = "f",
            SchemaJson = System.Text.Json.JsonSerializer.Serialize(ParallelParent(ub, "child")), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        var eng = Engine(db);
        var pid = await eng.SubmitAsync("pp", Guid.NewGuid(), "{}");
        var child = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == pid);

        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == pid && t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(tb.Id, ub, approve: false);   // B 支驳回 → 默认连坐 terminate → CancelAllActiveTokens 钩子级联

        Assert.Equal(FlowInstanceStatus.Rejected, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);
        Assert.Equal(FlowInstanceStatus.Withdrawn, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == child.Id)).Status);
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == child.Id && t.Status == FlowTaskStatus.Pending));
    }

    [Fact]
    public async Task CancelTokenSubtree_FifthClean_ParkedSubFlowToken_ChildrenCancelled_SiblingUntouched()
    {
        using var db = NewDb();
        Guid ub = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "pp", FlowName = "pp", FormKey = "f",
            SchemaJson = System.Text.Json.JsonSerializer.Serialize(ParallelParent(ub, "child")), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        var eng = Engine(db);
        var pid = await eng.SubmitAsync("pp", Guid.NewGuid(), "{}");
        var child = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == pid);
        var parked = await db.Wf_FlowTokens.SingleAsync(t => t.InstanceId == pid && t.NodeId == "sub" && t.Status == FlowTokenStatus.Active);

        eng.CancelTokenSubtree(pid, parked.Id);   // 剥离层=停泊 subFlow token（二期 SameBranch 剥离形态,直调 internal）
        await db.SaveChangesAsync();

        Assert.Equal(FlowInstanceStatus.Withdrawn, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == child.Id)).Status);   // ★ 第五清
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == child.Id && t.Status == FlowTaskStatus.Pending));
        // 兄弟支 b 零扰动（二期 C-T2 不变量在第五清下保持）
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending));
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);
    }
}
```

- [ ] **Step 2: 跑验证 FAIL** — `--filter SubFlowCascadeTests`（钩子未挂：撤回/连坐后子实例仍 Running）。

- [ ] **Step 3: 实现**

`SubFlowCascade.cs` 全文（B-T2 已落 `CancelInstanceTree` 则核对逐字一致后补齐其余）：

```csharp
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>子流程级联取消（spec §3.3）：子实例走既有撤回语义（Withdrawn + 任务/token/履历/Pending job 清场）
/// 并沿其 token 递归孙代。级联产生的 Withdrawn **不投递唤醒凭据**（不调 SubFlowResume.EnqueueIfChild）——
/// 父已终态/已死 token 的回注入口由 CheckSubFlowGroupAsync 状态闸双保险。不 SaveChanges（随调用方外壳落库）。
/// <para>★ 接缝互指（spec §3.3）：消费方=① FlowEngine.CancelAllActiveTokens（实例终止/全清场）、
/// ② FlowEngine.CancelTokenSubtree 第五清（二期 SameBranch 剥离）、③ TaskCenterService.WithdrawAsync（就地循环）。
/// 二期 FlowEngine.Prune.cs 的 PruneTokenAsync 只剪「被驳任务的 token」（停泊 subFlow token 无任务不会被直接剪），
/// 其坍缩路径落在 CancelAllActiveTokens——若未来出现绕过上述三处的新 token 清场路径，必须同步审视本接缝。</para></summary>
internal static class SubFlowCascade
{
    /// <summary>取消 parentTokenId 名下全部在途子实例（组级联，递归）。无子实例=零行为查询。</summary>
    internal static void CancelChildrenOfToken(CP6Context db, Guid parentTokenId)
    {
        // Local ∪ DB 惯用法（本回合刚起的子实例在 Local 未落盘）
        var local = db.Wf_FlowInstances.Local.Where(i => i.ParentTokenId == parentTokenId).ToList();
        var localIds = local.Select(i => i.Id).ToHashSet();
        var fromDb = db.Wf_FlowInstances
            .Where(i => i.ParentTokenId == parentTokenId && !localIds.Contains(i.Id)).ToList();
        foreach (var c in local.Concat(fromDb))
            if (c.Status is FlowInstanceStatus.Running or FlowInstanceStatus.Suspended or FlowInstanceStatus.Draft)
                CancelInstanceTree(db, c);
    }

    /// <summary>单实例撤回语义清场（镜像 TaskCenterService.WithdrawAsync 的清场块）+ 孙代递归。</summary>
    internal static void CancelInstanceTree(CP6Context db, Wf_FlowInstance inst)
    {
        inst.Status = FlowInstanceStatus.Withdrawn;
        inst.ModifyDate = DateTime.Now;

        // 在途任务 → Cancelled（Local ∪ DB）
        foreach (var t in db.Wf_FlowTasks.Local.Where(t => t.InstanceId == inst.Id
            && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended)).ToList())
            t.Status = FlowTaskStatus.Cancelled;
        var localTaskIds = db.Wf_FlowTasks.Local.Where(t => t.InstanceId == inst.Id).Select(t => t.Id).ToHashSet();
        foreach (var t in db.Wf_FlowTasks.Where(t => t.InstanceId == inst.Id
            && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended)
            && !localTaskIds.Contains(t.Id)).ToList())
            t.Status = FlowTaskStatus.Cancelled;

        // Active token → Cancelled（收集 id 供孙代递归）
        var cancelledTokens = new List<Guid>();
        foreach (var tk in db.Wf_FlowTokens.Local.Where(t => t.InstanceId == inst.Id && t.Status == FlowTokenStatus.Active).ToList())
        { tk.Status = FlowTokenStatus.Cancelled; cancelledTokens.Add(tk.Id); }
        var localTokIds = db.Wf_FlowTokens.Local.Where(t => t.InstanceId == inst.Id).Select(t => t.Id).ToHashSet();
        foreach (var tk in db.Wf_FlowTokens.Where(t => t.InstanceId == inst.Id && t.Status == FlowTokenStatus.Active
            && !localTokIds.Contains(t.Id)).ToList())
        { tk.Status = FlowTokenStatus.Cancelled; cancelledTokens.Add(tk.Id); }

        // Pending 传签履历 → Voided
        foreach (var f in db.Wf_FlowFormTos.Local.Where(f => f.InstanceId == inst.Id && f.Status == FlowFormToStatus.Pending).ToList())
            f.Status = FlowFormToStatus.Voided;
        var localFtIds = db.Wf_FlowFormTos.Local.Where(f => f.InstanceId == inst.Id).Select(f => f.Id).ToHashSet();
        foreach (var f in db.Wf_FlowFormTos.Where(f => f.InstanceId == inst.Id && f.Status == FlowFormToStatus.Pending
            && !localFtIds.Contains(f.Id)).ToList())
            f.Status = FlowFormToStatus.Voided;

        // Pending 服务作业 → Cancelled
        var now = DateTime.UtcNow;
        foreach (var j in db.Wf_ServiceJobs.Local.Where(j => j.InstanceId == inst.Id && j.Status == ServiceJobStatus.Pending).ToList())
        { j.Status = ServiceJobStatus.Cancelled; j.CompletedAtUtc = now; }
        var localJobIds = db.Wf_ServiceJobs.Local.Where(j => j.InstanceId == inst.Id).Select(j => j.Id).ToHashSet();
        foreach (var j in db.Wf_ServiceJobs.Where(j => j.InstanceId == inst.Id && j.Status == ServiceJobStatus.Pending
            && !localJobIds.Contains(j.Id)).ToList())
        { j.Status = ServiceJobStatus.Cancelled; j.CompletedAtUtc = now; }

        db.Wf_FlowHistories.Add(new Wf_FlowHistory
        {
            Id = Guid.NewGuid(), InstanceId = inst.Id, NodeId = inst.CurrentNode,
            ActorId = inst.StarterId, Action = "subFlowCascadeCancelled", Comment = null,
        });

        foreach (var tid in cancelledTokens) CancelChildrenOfToken(db, tid);   // 孙代递归（spec §3.3 第一路径）
    }
}
```

`FlowEngine.Tokens.cs` 两处挂钩：

① `CancelAllActiveTokens`——两段 token 置 Cancelled 的循环里**收集被取消的 token id**，方法末尾（B-T3 job 清场块之后）加：

```csharp
        // ── 子流程 C-T1（spec §3.3 路径①/坍缩路径）：本次被取消的 token 若停泊着 subFlow 组 → 级联取消子实例（递归）──
        foreach (var id in cancelledTokenIds) SubFlowCascade.CancelChildrenOfToken(_db, id);
```

（实现方式：两个 foreach 循环体从 `t.Status = FlowTokenStatus.Cancelled;` 改为 `{ t.Status = FlowTokenStatus.Cancelled; cancelledTokenIds.Add(t.Id); }`，循环前声明 `var cancelledTokenIds = new List<Guid>();`——既有行为 bit 级不变，只多收集。）

② `CancelTokenSubtree`——第五清（spec §3.3 路径②，四清扩五清），既有 per-token 清理循环体追加一行：

```csharp
        foreach (var id in subtree)
        {
            CancelPendingTasksOfToken(instanceId, id);
            VoidPendingFormTos(instanceId, tokenId: id);
            CancelPendingServiceJobsOfToken(instanceId, id);
            SubFlowCascade.CancelChildrenOfToken(_db, id);   // ★ 第五清（子流程 spec §3.3）：停泊 subFlow token → 子实例组级联
        }
```

并在 `CancelTokenSubtree` 的 XML 注释「四清」措辞处同步为「五清」，加互指：`// 接缝注释：第五清的语义与消费清单见 SubFlowCascade 类注释`。

③ `FlowEngine.Prune.cs`（二期文件）`PruneTokenAsync` 的注释块尾追加一行注释（零逻辑改动）：

```csharp
    // 子流程接缝（spec §3.3）：本方法只剪「被驳任务的 token」，停泊 subFlow token 无任务不会流入此处；
    // 其级联取消由 CancelAllActiveTokens / CancelTokenSubtree / WithdrawAsync 三钩子负责——见 SubFlowCascade 类注释。
```

`TaskCenterService.WithdrawAsync`——activeTokens 置 Cancelled 循环之后加：

```csharp
        // ── 子流程 C-T1（spec §3.3 路径①）：撤回 = terminate,就地循环不经 CancelAllActiveTokens → 此处补级联 ──
        foreach (var t in activeTokens) SubFlowCascade.CancelChildrenOfToken(_db, t.Id);
```

- [ ] **Step 4: 跑验证 PASS** — `--filter SubFlowCascadeTests` + `--filter "SubFlow"` 全部子流程测试全绿。
- [ ] **Step 5: 全量 Wf 闸（重点盯二期 `TokenSubtreeCancelTests`/`BranchPruneTests` 与既有 `WithdrawCleanupTests` 照绿）+ commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-subflow): C-T1 SubFlowCascade级联工具+三处挂钩(父终止递归/CancelTokenSubtree第五清/撤回路径)"
```

---

### Task C-T2: 退回重生防双批 + 二期组合语义定点

> 依赖 C-T1 + 二期 H-B/H-C 语义。**执行前必读 spec §3.3 后两条 + 二期计划 C-T3（BeforeSplit=放开跨网关退回后套用全清场）。**

**Files:**
- Test: `CP6.Tests/Wf/SubFlowSendBackComboTests.cs`（纯定点测试，产品代码在 C-T1 已闭合；测试红=实现有缺口，修实现）

- [ ] **Step 1: 写测试**

```csharp
// CP6.Tests/Wf/SubFlowSendBackComboTests.cs
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static CP6.Tests.Wf.SubFlowTestHarness;

namespace CP6.Tests.Wf;

/// <summary>spec §3.3 末两条定点：SameBranch/BeforeSplit 退回重生防双批（旧批取消+新批起+不并跑）；
/// spec §7 组合语义：父 subFlow 在并行支 + onBranchReject=prune + 子驳无错边 → 剪父支不连坐。</summary>
public class SubFlowSendBackComboTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    /// <summary>ps → a1(审批) → split(onBranchReject 可配) → ( sub , b ) → join → pe。</summary>
    private static FlowSchema SendBackParent(Guid ua, Guid ub, string subFlowKey, string? onBranchReject = null) => new()
    {
        Start = "ps",
        Nodes =
        {
            new FlowNode { Id = "ps", Type = "start" },
            new FlowNode { Id = "a1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua },
            new FlowNode { Id = "split", Type = "parallelSplit", OnBranchReject = onBranchReject },
            new FlowNode { Id = "sub", Type = "subFlow", SubFlowKey = subFlowKey },
            new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
            new FlowNode { Id = "join", Type = "parallelJoin" },
            new FlowNode { Id = "pe", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "ps", To = "a1" }, new FlowEdge { From = "a1", To = "split" },
            new FlowEdge { From = "split", To = "sub" }, new FlowEdge { From = "split", To = "b" },
            new FlowEdge { From = "sub", To = "join" }, new FlowEdge { From = "b", To = "join" },
            new FlowEdge { From = "join", To = "pe" },
        },
    };

    [Fact]
    public async Task BeforeSplitSendBack_OldBatchCancelled_ReapproveStartsNewBatch_NoParallelRun()
    {
        using var db = NewDb();
        Guid ua = Guid.NewGuid(), ub = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "sb", FlowName = "sb", FormKey = "f",
            SchemaJson = System.Text.Json.JsonSerializer.Serialize(SendBackParent(ua, ub, "child")), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        var eng = Engine(db);
        var pid = await eng.SubmitAsync("sb", Guid.NewGuid(), "{}");

        var ta1 = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == pid && t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(ta1.Id, ua, approve: true);   // 进并行块,sub 停泊 + 旧批子实例起

        var oldToken = await db.Wf_FlowTokens.SingleAsync(t => t.InstanceId == pid && t.NodeId == "sub" && t.Status == FlowTokenStatus.Active);
        var oldChild = await db.Wf_FlowInstances.SingleAsync(i => i.ParentTokenId == oldToken.Id);

        // B 支从 b 退回 a1（跨 split 边界=二期 BeforeSplit 整块重来,全清场路径）
        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == pid && t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await eng.SendBackAsync(tb.Id, ub, "a1");

        Assert.Equal(FlowInstanceStatus.Withdrawn, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == oldChild.Id)).Status);   // 旧批死
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == oldChild.Id && t.Status == FlowTaskStatus.Pending));

        // 重批：a1 再过 → 重入 sub 是新 tokenId → (ParentTokenId,SubIndex) 按设计不撞 → 新批照起
        var ta1b = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == pid && t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(ta1b.Id, ua, approve: true);

        var newToken = await db.Wf_FlowTokens.SingleAsync(t => t.InstanceId == pid && t.NodeId == "sub" && t.Status == FlowTokenStatus.Active);
        Assert.NotEqual(oldToken.Id, newToken.Id);
        var newChild = await db.Wf_FlowInstances.SingleAsync(i => i.ParentTokenId == newToken.Id);
        Assert.Equal(FlowInstanceStatus.Running, newChild.Status);
        // ★ 不并跑：全库在途子实例恰一个（旧批 Withdrawn 不复活）
        Assert.Equal(1, await db.Wf_FlowInstances.CountAsync(i => i.ParentInstanceId == pid && i.Status == FlowInstanceStatus.Running));

        // 新批走完 → 父可正常通过（新批凭据链路无残留污染）
        var tNew = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == newChild.Id && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(tNew.Id, ca, approve: true);
        var tb2 = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == pid && t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(tb2.Id, ub, approve: true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);
    }

    [Fact]
    public async Task ComboSemantics_SubFlowInParallelBranch_Prune_ChildReject_PrunesBranchOnly()
    {
        using var db = NewDb();
        Guid ua = Guid.NewGuid(), ub = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "combo", FlowName = "combo", FormKey = "f",
            SchemaJson = System.Text.Json.JsonSerializer.Serialize(SendBackParent(ua, ub, "child", onBranchReject: "prune")),
            Version = 1, Enable = true });
        await db.SaveChangesAsync();
        var eng = Engine(db);
        var pid = await eng.SubmitAsync("combo", Guid.NewGuid(), "{}");
        var ta1 = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == pid && t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(ta1.Id, ua, approve: true);

        var subToken = await db.Wf_FlowTokens.SingleAsync(t => t.InstanceId == pid && t.NodeId == "sub" && t.Status == FlowTokenStatus.Active);
        var child = await db.Wf_FlowInstances.SingleAsync(i => i.ParentTokenId == subToken.Id);

        // 子驳回 → 复核错误处置 → 无错边 → TryPruneBranch（split 配 prune）→ 只剪 sub 支
        var tc = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == child.Id && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(tc.Id, ca, approve: false);

        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid);
        Assert.Equal(FlowInstanceStatus.Running, inst.Status);                                    // ★ 不连坐
        Assert.Equal(FlowTokenStatus.Pruned, (await db.Wf_FlowTokens.SingleAsync(t => t.Id == subToken.Id)).Status);
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending));

        // B 支办结 → 动态计票放行（Pruned 从等待集消失,二期 D4）→ 实例通过
        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == pid && t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(tb.Id, ub, approve: true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);
    }
}
```

- [ ] **Step 2: 跑验证** — `--filter SubFlowSendBackComboTests`。红=C-T1 钩子/B-T2 剪枝分流有缺口，**修实现不改测试**。
- [ ] **Step 3: Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "test(wfs-subflow): C-T2 退回重生防双批+并行支prune组合语义定点(spec §3.3/§7)"
```

---

## Wave S-D — 校验双层（E-WF-025/026；与 S-C 并行）

### Task D-T1: FlowSchemaValidator 静态规则 + SubFlowRefValidator 防环 DFS + DesignerService 接线

> 依赖 A-T1（POCO）。**执行前必读 spec §5 全节。** `ContainsUnsupportedSubscript` 依赖处置见「前置依赖」第 2 条。

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowSchemaValidator.cs`
- Create: `CP6.Core/Services/Wf/SubFlowRefValidator.cs`
- Modify: `CP6.Core/Services/Oa/DesignerService.cs`（`SaveAsync` ①c）
- Modify（条件）: `CP6.Core/Services/Wf/ServiceVarsHelper.cs`（票4 未落地时补 `ContainsUnsupportedSubscript`，实现照 cleanup 计划逐字）
- Test: `CP6.Tests/Wf/SubFlowValidatorTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/SubFlowValidatorTests.cs
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>E-WF-025/026 双层校验（spec §5）：FlowSchemaValidator 纯静态规则 + SubFlowRefValidator DI 层
/// （FlowKey 存在性/启用 + 引用环 DFS 深度 8）+ DesignerService 保存接线。</summary>
public class SubFlowValidatorTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static FlowSchema SubSchema(Action<FlowNode>? mutate = null)
    {
        var sub = new FlowNode { Id = "sub", Type = "subFlow", SubFlowKey = "target" };
        mutate?.Invoke(sub);
        return new FlowSchema
        {
            Start = "s",
            Nodes = { new FlowNode { Id = "s", Type = "start" }, sub, new FlowNode { Id = "e", Type = "end" } },
            Edges = { new FlowEdge { From = "s", To = "sub" }, new FlowEdge { From = "sub", To = "e" } },
        };
    }

    // ── 静态层（FlowSchemaValidator,无 DI）──

    [Fact]
    public void Static_ValidSubFlow_NoErrors()
        => Assert.Empty(FlowSchemaValidator.Validate(SubSchema()));

    [Fact]
    public void Static_MissingSubFlowKey_E_WF_025()
        => Assert.Contains("E-WF-025", FlowSchemaValidator.Validate(SubSchema(n => n.SubFlowKey = " ")));

    [Fact]
    public void Static_NoNonErrorOutEdge_E_WF_025()
    {
        var schema = SubSchema();
        schema.Edges.Single(e => e.From == "sub").IsError = true;   // 仅错误出边=成功路径无后继(对齐 E-WF-016 同款规则)
        Assert.Contains("E-WF-025", FlowSchemaValidator.Validate(schema));
    }

    [Theory]
    [InlineData("quorum")]
    [InlineData("first")]
    public void Static_BadPolicy_E_WF_025(string bad)
        => Assert.Contains("E-WF-025", FlowSchemaValidator.Validate(SubSchema(n => n.SubCompletionPolicy = bad)));

    [Fact]
    public void Static_BlankCollectionVar_E_WF_025()
        => Assert.Contains("E-WF-025", FlowSchemaValidator.Validate(SubSchema(n => n.SubCollectionVar = "  ")));

    [Theory]
    [InlineData("{bad json")]
    [InlineData("{\"a\":1}")]                       // 值非字符串路径
    [InlineData("{\"a\":\"$.items[0]\"}")]          // 不支持下标(ContainsUnsupportedSubscript)
    public void Static_BadVarsMap_E_WF_025(string bad)
    {
        Assert.Contains("E-WF-025", FlowSchemaValidator.Validate(SubSchema(n => n.SubVarsInJson = bad)));
        Assert.Contains("E-WF-025", FlowSchemaValidator.Validate(SubSchema(n => n.SubVarsOutJson = bad)));
    }

    // ── DI 层（SubFlowRefValidator）──

    private static void SeedDef(CP6Context db, string key, FlowSchema schema, bool enable = true)
        => db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = key, FlowName = key, FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = enable });

    private static FlowSchema RefSchema(string targetKey) => SubSchema(n => n.SubFlowKey = targetKey);

    private static FlowSchema PlainApproval() => new()
    {
        Start = "s",
        Nodes = { new FlowNode { Id = "s", Type = "start" },
                  new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                  new FlowNode { Id = "e", Type = "end" } },
        Edges = { new FlowEdge { From = "s", To = "a" }, new FlowEdge { From = "a", To = "e" } },
    };

    [Fact]
    public async Task Ref_TargetMissing_E_WF_025()
    {
        using var db = NewDb();
        await db.SaveChangesAsync();
        var ex = Assert.Throws<InvalidOperationException>(
            () => SubFlowRefValidator.Validate(db, "me", RefSchema("ghost")));
        Assert.Contains("E-WF-025", ex.Message);
    }

    [Fact]
    public async Task Ref_TargetDisabled_E_WF_025()
    {
        using var db = NewDb();
        SeedDef(db, "target", PlainApproval(), enable: false);
        await db.SaveChangesAsync();
        var ex = Assert.Throws<InvalidOperationException>(
            () => SubFlowRefValidator.Validate(db, "me", RefSchema("target")));
        Assert.Contains("E-WF-025", ex.Message);
    }

    [Fact]
    public async Task Ref_SelfReference_E_WF_026()
    {
        using var db = NewDb();
        SeedDef(db, "me", RefSchema("me"));
        await db.SaveChangesAsync();
        var ex = Assert.Throws<InvalidOperationException>(
            () => SubFlowRefValidator.Validate(db, "me", RefSchema("me")));
        Assert.Contains("E-WF-026", ex.Message);
    }

    [Fact]
    public async Task Ref_TwoNodeCycle_E_WF_026()
    {
        using var db = NewDb();
        SeedDef(db, "a", RefSchema("b"));
        SeedDef(db, "b", PlainApproval());   // b 现存版本不引用 a
        await db.SaveChangesAsync();
        // 保存 b 的新 schema 引用 a → a→b→a 成环（校验时刻的当前已发布版口径,spec §3.1）
        var ex = Assert.Throws<InvalidOperationException>(
            () => SubFlowRefValidator.Validate(db, "b", RefSchema("a")));
        Assert.Contains("E-WF-026", ex.Message);
    }

    [Fact]
    public async Task Ref_ChainDepth8_E_WF_026_Depth7_Ok()
    {
        using var db = NewDb();
        SeedDef(db, "d7", PlainApproval());
        for (int i = 6; i >= 1; i--) SeedDef(db, $"d{i}", RefSchema($"d{i + 1}"));
        await db.SaveChangesAsync();
        SubFlowRefValidator.Validate(db, "d0", RefSchema("d1"));   // 链长 8 节点(d0..d7)=深度 7 引用,放行

        using var db2 = NewDb();
        SeedDef(db2, "d8", PlainApproval());
        for (int i = 7; i >= 1; i--) SeedDef(db2, $"d{i}", RefSchema($"d{i + 1}"));
        await db2.SaveChangesAsync();
        var ex = Assert.Throws<InvalidOperationException>(
            () => SubFlowRefValidator.Validate(db2, "d0", RefSchema("d1")));   // 深度 8 → 拦
        Assert.Contains("E-WF-026", ex.Message);
    }

    // ── DesignerService 接线 ──

    [Fact]
    public async Task DesignerSave_SubFlowGhostTarget_Throws_E_WF_025()
    {
        using var db = NewDb();
        await db.SaveChangesAsync();
        var svc = new DesignerService(db, new FlowDefService(db),
            Array.Empty<IServiceTaskExecutor>(), Array.Empty<IWfConnector>());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveAsync(
            new SaveFlowRequest("me", "me", "f", null, null, JsonSerializer.Serialize(RefSchema("ghost"))), "u"));
        Assert.Contains("E-WF-025", ex.Message);
    }
}
```

- [ ] **Step 2: 跑验证 FAIL** — `--filter SubFlowValidatorTests`。

- [ ] **Step 3: 实现**

（若票4 未落地）`ServiceVarsHelper.cs` 追加 cleanup 计划逐字实现：

```csharp
    /// <summary>模板/映射文本是否含不支持的数组下标（"[n]"）。点路径 lite 只支持对象键（票4/子流程 E-WF-025 共用）。</summary>
    public static bool ContainsUnsupportedSubscript(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        // 命中 "$.xxx[" 或 "{xxx[" 形态的下标引用；纯 JSON 数组字面量（值位置）不误伤
        return System.Text.RegularExpressions.Regex.IsMatch(text, @"[$.{][A-Za-z0-9_.]*\[");
    }
```

`FlowSchemaValidator.cs` 规则 ⑨ 之后追加（静态无 DI，只查结构；FlowKey 存在性交 DI 层）：

```csharp
        // ⑩ 子流程节点(E-WF-025,子流程 spec §5)：SubFlowKey 非空;非错误出边必有(对齐 serviceTask E-WF-016 同款);
        //    SubCompletionPolicy ∈ {null,all,any};SubCollectionVar 非空串;SubVarsIn/OutJson 合法映射且无下标。
        foreach (var n in schema.Nodes.Where(n => T(n) == "subflow"))
        {
            var pol = (n.SubCompletionPolicy ?? SubFlowCompletionPolicy.All).Trim().ToLowerInvariant();
            bool bad =
                string.IsNullOrWhiteSpace(n.SubFlowKey)
                || !schema.Edges.Any(e => e.From == n.Id && e.IsError != true)
                || (pol != SubFlowCompletionPolicy.All && pol != SubFlowCompletionPolicy.Any)
                || (n.SubCollectionVar is not null && string.IsNullOrWhiteSpace(n.SubCollectionVar))
                || !SubFlowVarsMapper.TryParseMap(n.SubVarsInJson, out _)
                || !SubFlowVarsMapper.TryParseMap(n.SubVarsOutJson, out _)
                || ServiceVarsHelper.ContainsUnsupportedSubscript(n.SubVarsInJson)
                || ServiceVarsHelper.ContainsUnsupportedSubscript(n.SubVarsOutJson);
            if (bad) { errs.Add("E-WF-025"); break; }
        }
```

`SubFlowRefValidator.cs` 全文：

```csharp
using System.Text.Json;
using CP6.Core.EFDbContext;

namespace CP6.Core.Services.Wf;

/// <summary>子流程引用校验（spec §5 E-WF-025/026，保存时 DI 层——静态 FlowSchemaValidator 无 DI 查不了 FlowKey）。
/// 环检测口径（spec §3.1）：DFS 遍历**校验时刻的当前已发布版**；保存后其他流程再发布可能引入新环，
/// 由运行时深度守卫（SubFlowNodeHandler E-WF-026）兜底。链上任何 FlowKey 重复即环；深度 ≥ MaxDepth 拦。</summary>
internal static class SubFlowRefValidator
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>违规抛 InvalidOperationException("E-WF-025 ..."/"E-WF-026 ...")。flowKey=正在保存的流程（用保存中的 schema，不读库中旧版）。</summary>
    internal static void Validate(CP6Context db, string flowKey, FlowSchema schema)
    {
        var chain = new List<string> { flowKey };
        Walk(db, schema, chain);
    }

    private static void Walk(CP6Context db, FlowSchema schema, List<string> chain)
    {
        foreach (var n in schema.Nodes.Where(n =>
            string.Equals((n.Type ?? "").Trim(), "subFlow", StringComparison.OrdinalIgnoreCase)))
        {
            var key = n.SubFlowKey?.Trim();
            if (string.IsNullOrEmpty(key))
                throw new InvalidOperationException("E-WF-025 subFlow 节点缺 SubFlowKey");
            if (chain.Contains(key, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"E-WF-026 子流程引用环: {string.Join("→", chain)}→{key}");
            if (chain.Count >= SubFlowLimits.MaxDepth)
                throw new InvalidOperationException($"E-WF-026 子流程引用深度超限({SubFlowLimits.MaxDepth})");

            var def = db.Wf_FlowDefs.FirstOrDefault(d => d.FlowKey == key);
            if (def is null || !def.Enable)
                throw new InvalidOperationException($"E-WF-025 子流程引用不存在或未启用: {key}");

            var target = JsonSerializer.Deserialize<FlowSchema>(def.SchemaJson, JsonOpts) ?? new FlowSchema();
            chain.Add(key);
            Walk(db, target, chain);
            chain.RemoveAt(chain.Count - 1);
        }
    }
}
```

`DesignerService.SaveAsync` ①b 之后加 ①c：

```csharp
        // ①c 子流程引用校验(E-WF-025/026,子流程 spec §5)：FlowKey 存在且启用 + 引用环 DFS(深度 8,当前已发布版快照)
        SubFlowRefValidator.Validate(_db, req.FlowKey, schema);
```

- [ ] **Step 4: 跑验证 PASS** — `--filter SubFlowValidatorTests` 全绿。
- [ ] **Step 5: Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-subflow): D-T1 校验双层E-WF-025/026(静态规则+SubFlowRefValidator防环DFS+DesignerService接线)"
```

---

## Wave S-E — 设计器 + 收件箱互链（依赖 S-B/S-D 的 schema 契约）

### Task E-T1: designerModel — SchemaNode 五字段 + palette 入口 + validateClient 镜像

**Files:**
- Modify: `cp6.web/src/views/oa/designer/designerModel.ts`
- Modify: `cp6.web/src/views/oa/designer/designerModel.test.ts`（palette 类型清单断言 +1，唯一既有改动）
- Create: `cp6.web/src/views/oa/designer/designerModel.subflow.spec.ts`

- [ ] **Step 1: 写失败测试**

```typescript
// cp6.web/src/views/oa/designer/designerModel.subflow.spec.ts
import { describe, it, expect } from 'vitest'
import {
  NODE_PALETTE, schemaToGraph, graphToSchema, validateClient,
  type FlowSchemaDto, type SchemaNode,
} from './designerModel'

const subNode = (over: Partial<SchemaNode> = {}): SchemaNode => ({
  id: 'sub', type: 'subFlow', subFlowKey: 'fk-child', ...over,
})

const schemaWith = (n: SchemaNode, edges?: FlowSchemaDto['edges']): FlowSchemaDto => ({
  start: 's',
  nodes: [{ id: 's', type: 'start' }, n, { id: 'e', type: 'end' }],
  edges: edges ?? [{ from: 's', to: 'sub' }, { from: 'sub', to: 'e' }],
})

describe('designerModel subFlow', () => {
  it('palette 含 subFlow 入口', () => {
    expect(NODE_PALETTE.some(p => p.type === 'subFlow')).toBe(true)
  })

  it('round-trip 保全五字段', () => {
    const schema = schemaWith(subNode({
      subVarsInJson: '{"a":"$.x"}', subVarsOutJson: '{"y":"$.b"}',
      subCollectionVar: 'items', subCompletionPolicy: 'any',
    }))
    const back = graphToSchema(schemaToGraph(schema))
    const sub = back.nodes.find(n => n.id === 'sub')!
    expect(sub.subFlowKey).toBe('fk-child')
    expect(sub.subVarsInJson).toBe('{"a":"$.x"}')
    expect(sub.subVarsOutJson).toBe('{"y":"$.b"}')
    expect(sub.subCollectionVar).toBe('items')
    expect(sub.subCompletionPolicy).toBe('any')
  })

  it('validateClient: 合法配置零错误', () => {
    expect(validateClient(schemaWith(subNode()))).toEqual([])
  })

  it('validateClient: 缺 subFlowKey → errSubFlowConfig', () => {
    expect(validateClient(schemaWith(subNode({ subFlowKey: '' }))))
      .toContain('oa.designer.errSubFlowConfig')
  })

  it('validateClient: 非法完成策略 → errSubFlowConfig', () => {
    expect(validateClient(schemaWith(subNode({ subCompletionPolicy: 'quorum' }))))
      .toContain('oa.designer.errSubFlowConfig')
  })

  it('validateClient: 集合变量空串 → errSubFlowConfig', () => {
    expect(validateClient(schemaWith(subNode({ subCollectionVar: '  ' }))))
      .toContain('oa.designer.errSubFlowConfig')
  })

  it('validateClient: 映射 JSON 非法/含下标 → errSubFlowConfig', () => {
    expect(validateClient(schemaWith(subNode({ subVarsInJson: '{bad' }))))
      .toContain('oa.designer.errSubFlowConfig')
    expect(validateClient(schemaWith(subNode({ subVarsOutJson: '{"a":"$.items[0]"}' }))))
      .toContain('oa.designer.errSubFlowConfig')
  })

  it('validateClient: 无非错误出边 → errSubFlowConfig（镜像后端 E-WF-025 静态部分）', () => {
    const bad = schemaWith(subNode(), [
      { from: 's', to: 'sub' },
      { from: 'sub', to: 'e', isError: true },
    ])
    expect(validateClient(bad)).toContain('oa.designer.errSubFlowConfig')
  })
})
```

- [ ] **Step 2: 跑验证 FAIL** — `npm run test -- designerModel.subflow`。

- [ ] **Step 3: 实现** — `designerModel.ts` 三处：

`SchemaNode` 服务任务字段块之后追加：

```typescript
  // 子流程（subFlow）配置——镜像后端 FlowNode 的 Sub*（子流程 spec §2.1;交换 JSON 用 camelCase）
  subFlowKey?: string                                  // 目标已发布流程 FlowKey
  subVarsInJson?: string                               // 父→子映射 {"子var":"$.父var路径"}
  subVarsOutJson?: string                              // 子→父回注映射 {"父var":"$.子var路径"}
  subCollectionVar?: string                            // 多实例集合变量名(空=单实例)
  subCompletionPolicy?: string                         // all | any
```

`NODE_PALETTE` 服务任务三入口之后追加（不带 color 字段，OA 批次4 裁定同款）：

```typescript
  { type: 'subFlow',       label: '子流程' },
```

`validateClient` 末尾（serviceTask 校验块之后、`return errs` 之前）追加：

```typescript
  // subFlow 静态镜像（后端 E-WF-025 静态部分,子流程 spec §5）：key 必填/策略值域/集合变量非空串/映射合法无下标/非错误出边必有
  const validMap = (s?: string): boolean => {
    if (s == null || s.trim() === '') return true
    if (/[$.{][A-Za-z0-9_.]*\[/.test(s)) return false          // 不支持数组下标
    try {
      const o = JSON.parse(s)
      return !!o && typeof o === 'object' && !Array.isArray(o)
        && Object.values(o).every(v => typeof v === 'string')
    } catch { return false }
  }
  for (const n of nodes) {
    if (n.type !== 'subFlow') continue
    const ok = !!n.subFlowKey?.trim()
      && (!n.subCompletionPolicy || ['all', 'any'].includes(n.subCompletionPolicy))
      && (n.subCollectionVar == null || n.subCollectionVar.trim() !== '')
      && validMap(n.subVarsInJson) && validMap(n.subVarsOutJson)
      && edges.some(e => e.from === n.id && e.isError !== true)
    if (!ok) errs.push('oa.designer.errSubFlowConfig')
  }
```

`designerModel.test.ts` palette 类型清单断言（既有 :45 一带，二期后含 inclusive 两项）在数组尾追加 `'subFlow'`。

- [ ] **Step 4: 跑验证 PASS** — `npm run test -- designerModel`（全部 designerModel 测试）。
- [ ] **Step 5: commit**

```bash
git add -A && git commit -m "feat(wfs-subflow): E-T1 designerModel子流程五字段+palette入口+validateClient镜像"
```

---

### Task E-T2: SubFlowNode 节点组件 + 画布接线 + NodePropertyPanel 子流程段

> 依赖 E-T1。样式全走 Design System token（`.dot-*` 家族），沿 `ServiceTaskNode.vue`/`DesignerCanvas.vue` 既有模式逐字仿写。

**Files:**
- Create: `cp6.web/src/views/oa/designer/nodes/SubFlowNode.vue`
- Modify: `cp6.web/src/views/oa/designer/DesignerCanvas.vue`（注册 `node-subFlow` 模板 + palette `.dot-subFlow` token 样式）
- Modify: `cp6.web/src/views/oa/designer/NodePropertyPanel.vue`（subFlow 配置段）

- [ ] **Step 1: SubFlowNode.vue**（结构仿 `ServiceTaskNode.vue`：标题 `t('oa.designer.subflow.title')` + 副行显示 `subFlowKey`；多实例时角标显示 `collectionVar`；配色用既有 token 变量，**零硬编码色**）：

```vue
<template>
  <div class="subflow-node" :class="{ selected }">
    <div class="sf-title">
      <span class="sf-dot" />
      {{ data.name || t('oa.designer.subflow.title') }}
    </div>
    <div class="sf-key">{{ data.subFlowKey || '—' }}</div>
    <div v-if="data.subCollectionVar" class="sf-multi">
      ×N {{ data.subCollectionVar }} · {{ t(`oa.designer.subflow.policy.${data.subCompletionPolicy || 'all'}`) }}
    </div>
    <Handle type="target" :position="Position.Top" />
    <Handle type="source" :position="Position.Bottom" />
  </div>
</template>

<script setup lang="ts">
import { Handle, Position } from '@vue-flow/core'
import { useI18n } from 'vue-i18n'
import type { SchemaNode } from '../designerModel'

defineProps<{ data: SchemaNode; selected?: boolean }>()
const { t } = useI18n()
</script>

<style scoped>
/* 全 token,零硬编码色;形态区分 serviceTask(双边框=容器语义,BPMN call-activity 惯例) */
.subflow-node {
  border: 2px double var(--cp-border-strong, var(--cp-border));
  border-radius: 8px;
  background: var(--cp-bg-elevated);
  color: var(--cp-text);
  padding: 8px 12px;
  min-width: 140px;
  font-size: 12px;
}
.subflow-node.selected { border-color: var(--cp-primary); }
.sf-title { display: flex; align-items: center; gap: 6px; font-weight: 600; }
.sf-dot { width: 8px; height: 8px; border-radius: 2px; background: var(--cp-info); }
.sf-key { color: var(--cp-text-secondary); margin-top: 2px; }
.sf-multi { color: var(--cp-warn); margin-top: 2px; }
</style>
```

（执行者落地前 `grep -n "cp-border-strong\|cp-bg-elevated\|cp-info\|cp-warn\|cp-text-secondary" cp6.web/src` 核对 token 名与本仓库 Design System 实际变量一致，不一致者以仓库现名替换——**禁止回退到字面色**。）

- [ ] **Step 2: DesignerCanvas.vue** — 仿二期 `InclusiveGatewayNode` 接线：`import SubFlowNode from './nodes/SubFlowNode.vue'`，`<template #node-subFlow="p"><SubFlowNode v-bind="p" /></template>`；palette 图例样式加 `.dot-subFlow { background: var(--cp-info); border-radius: 2px; }`（与 serviceTask dot 区分形状）。

- [ ] **Step 3: NodePropertyPanel.vue** — 仿 serviceTask 段（:357 起）结构，`v-else-if="local.type === 'subFlow'"` 段：

```vue
        <!-- ── 子流程配置（subFlow 专属,子流程 spec §4）────────────────── -->
        <template v-else-if="local.type === 'subFlow'">
          <el-form-item :label="t('oa.designer.subflow.target')">
            <el-select v-model="local.subFlowKey" filterable style="width: 100%">
              <el-option v-for="d in publishedFlows" :key="d.flowKey" :value="d.flowKey"
                         :label="`${d.flowName} (${d.flowKey})`" />
            </el-select>
            <div class="hint">{{ t('oa.designer.subflow.targetHint') }}</div>
          </el-form-item>
          <el-form-item :label="t('oa.designer.subflow.varsIn')">
            <el-input v-model="local.subVarsInJson" type="textarea" :rows="2"
                      placeholder='{"childVar":"$.parentVar"}' />
          </el-form-item>
          <el-form-item :label="t('oa.designer.subflow.varsOut')">
            <el-input v-model="local.subVarsOutJson" type="textarea" :rows="2"
                      placeholder='{"parentVar":"$.childVar"}' />
            <div class="hint">{{ t('oa.designer.subflow.varsHint') }}</div>
          </el-form-item>
          <el-form-item :label="t('oa.designer.subflow.multi')">
            <el-switch v-model="subMulti" />
          </el-form-item>
          <template v-if="subMulti">
            <el-form-item :label="t('oa.designer.subflow.collectionVar')">
              <el-input v-model="local.subCollectionVar" placeholder="items" />
            </el-form-item>
            <el-form-item :label="t('oa.designer.subflow.policy')">
              <el-radio-group v-model="local.subCompletionPolicy">
                <el-radio value="all">{{ t('oa.designer.subflow.policy.all') }}</el-radio>
                <el-radio value="any">{{ t('oa.designer.subflow.policy.any') }}</el-radio>
              </el-radio-group>
              <div class="hint">{{ t('oa.designer.subflow.policyHint') }}</div>
            </el-form-item>
          </template>
        </template>
```

script 区（懒加载模式仿 catalog :82-95）：

```typescript
// ── subFlow：已发布流程目录（复用 /api/oa/designer/list,排除当前流程自身,spec §4.2）──
const publishedFlows = ref<Array<{ flowKey: string; flowName: string }>>([])
const flowsLoaded = ref(false)
watch(() => local.value.type === 'subFlow', async (v) => {
  if (!v || flowsLoaded.value) return
  flowsLoaded.value = true
  try {
    const list = await designerApi.list()
    publishedFlows.value = list.filter(d => d.enable && d.flowKey !== props.currentFlowKey)
  } catch { flowsLoaded.value = false }
}, { immediate: true })

// 多实例开关：关闭时清空两字段（对齐「kind 切换清残留」终审教训,631f0e2——不留静默残留配置）
const subMulti = computed({
  get: () => local.value.subCollectionVar != null && local.value.subCollectionVar !== '',
  set: (v: boolean) => {
    if (v) { local.value.subCollectionVar = local.value.subCollectionVar || ''; local.value.subCompletionPolicy = local.value.subCompletionPolicy || 'all' }
    else { local.value.subCollectionVar = undefined; local.value.subCompletionPolicy = undefined }
  },
})
```

（`props.currentFlowKey`：若面板尚无该 prop，从 `DesignerView.vue` 既有已加载 flowKey 下传一份，只读。`designerApi.list()` 返回形状以 `FlowDefSummary` 为准——`enable`/`flowKey`/`flowName` camelCase。）

- [ ] **Step 4: 验证 + commit**

```bash
npm run test && npm run type-check && npm run build
git add -A && git commit -m "feat(wfs-subflow): E-T2 SubFlowNode组件+画布接线+属性面板(目标下拉/双向映射/多实例开关,零硬编码色)"
```

---

### Task E-T3: 收件箱父子互链（InboxDetail 扩展 + FormDetail 展示）

> 依赖 A-T1（回指列）。spec §4.5：子实例详情时间线头部「父流程」链接；父详情 subFlow 停泊节点行展开子实例列表。

**Files:**
- Modify: `CP6.Core/Services/Oa/InboxModels.cs`
- Modify: `CP6.Core/Services/Oa/InboxService.cs`（`DetailAsync`）
- Modify: `cp6.web/src/types/oa/inbox.ts`
- Modify: `cp6.web/src/views/oa/inbox/FormDetail.vue`
- Test: `CP6.Tests/Wf/SubFlowInboxDetailTests.cs`（后端聚合面）

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/SubFlowInboxDetailTests.cs
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static CP6.Tests.Wf.SubFlowTestHarness;

namespace CP6.Tests.Wf;

public class SubFlowInboxDetailTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    [Fact]
    public async Task Detail_ParentAndChildren_BothDirectionsLinked()
    {
        using var db = NewDb();
        Guid pa = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        SeedDef(db, "parent", ParentSchema(pa, "child", collectionVar: "items"));
        await db.SaveChangesAsync();
        var pid = await Engine(db).SubmitAsync("parent", Guid.NewGuid(), "{\"items\":[1,2]}");
        var kids = await db.Wf_FlowInstances.Where(i => i.ParentInstanceId == pid).OrderBy(i => i.SubIndex).ToListAsync();

        var svc = new InboxService(db, new ForecastService(db));   // 若 InboxService ctor 形参不同,以现签名为准补齐

        var parentDetail = await svc.DetailAsync(pid);
        Assert.NotNull(parentDetail);
        Assert.Null(parentDetail!.SubFlowParent);                      // 顶层实例无父链
        Assert.NotNull(parentDetail.SubFlows);
        Assert.Equal(2, parentDetail.SubFlows!.Count);
        Assert.Equal(new[] { 0, 1 }, parentDetail.SubFlows.Select(s => s.SubIndex).ToArray());
        Assert.All(parentDetail.SubFlows, s => Assert.Equal("sub", s.NodeId));
        Assert.All(parentDetail.SubFlows, s => Assert.Equal("child", s.FlowKey));

        var childDetail = await svc.DetailAsync(kids[0].Id);
        Assert.NotNull(childDetail!.SubFlowParent);
        Assert.Equal(pid, childDetail.SubFlowParent!.InstanceId);
        Assert.Equal("parent", childDetail.SubFlowParent.FlowKey);
    }

    [Fact]
    public async Task Detail_PlainInstance_NullBothWays()
    {
        using var db = NewDb();
        Guid ca = Guid.NewGuid();
        SeedDef(db, "plain", ChildSchema(ca));
        await db.SaveChangesAsync();
        var id = await Engine(db).SubmitAsync("plain", Guid.NewGuid(), "{}");
        var svc = new InboxService(db, new ForecastService(db));
        var d = await svc.DetailAsync(id);
        Assert.Null(d!.SubFlowParent);
        Assert.True(d.SubFlows is null || d.SubFlows.Count == 0);
    }
}
```

（`InboxService` 构造签名以现代码为准——执行者先 `grep -n "public InboxService(" CP6.Core/Services/Oa/InboxService.cs` 补齐参数；测试意图不变。）

- [ ] **Step 2: 实现**

`InboxModels.cs` 追加两 record，`InboxDetail` 末尾追加两个**带默认值**的可选参数（既有 `new InboxDetail(9 参)` 调用零改动）：

```csharp
// ── 子流程互链（spec §4.5）──
public record SubFlowParentRow(Guid InstanceId, string FlowKey, string? FlowName);
public record SubFlowChildRow(Guid InstanceId, int SubIndex, string FlowKey, string? FlowName, int Status, string NodeId);

public record InboxDetail(Wf_FlowInstance Instance, string? FlowName, string? FormKey, string? FormSchemaJson,
    string CurrentDataJson, IReadOnlyList<TimelineRow> Timeline, IReadOnlyList<SnapshotRow> Snapshots,
    IReadOnlyList<ForecastStep> Forecast, IReadOnlyList<CcRow> Cc,
    SubFlowParentRow? SubFlowParent = null, IReadOnlyList<SubFlowChildRow>? SubFlows = null);
```

`InboxService.DetailAsync` 返回语句前聚合、返回语句补两参：

```csharp
        // ── 子流程互链（spec §4.5）：向上=父实例链接;向下=本实例名下子实例组（按停泊 token 的 NodeId 归组）──
        SubFlowParentRow? subFlowParent = null;
        if (inst.ParentInstanceId is Guid parentId)
        {
            var p = await _db.Wf_FlowInstances.FirstOrDefaultAsync(x => x.Id == parentId);
            var pDef = p is null ? null : await _db.Wf_FlowDefs.FirstOrDefaultAsync(d => d.FlowKey == p.FlowKey);
            if (p is not null) subFlowParent = new SubFlowParentRow(p.Id, p.FlowKey, pDef?.FlowName);
        }
        var childRows = await (
            from c in _db.Wf_FlowInstances
            where c.ParentInstanceId == instanceId
            join tk in _db.Wf_FlowTokens on c.ParentTokenId equals tk.Id
            join cd in _db.Wf_FlowDefs on c.FlowKey equals cd.FlowKey into cds
            from cd in cds.DefaultIfEmpty()
            orderby tk.NodeId, c.SubIndex
            select new SubFlowChildRow(c.Id, c.SubIndex ?? 0, c.FlowKey, cd != null ? cd.FlowName : null, c.Status, tk.NodeId)
        ).ToListAsync();

        return new InboxDetail(inst, def?.FlowName, def?.FormKey, formSchema,
            inst.VarsJson, timeline, snapshots, forecast, ccRows,
            subFlowParent, childRows);
```

`types/oa/inbox.ts` `InboxDetail` 接口镜像追加：

```typescript
  subFlowParent?: { instanceId: string; flowKey: string; flowName?: string } | null
  subFlows?: Array<{ instanceId: string; subIndex: number; flowKey: string; flowName?: string; status: number; nodeId: string }>
```

`FormDetail.vue`：
1. 右栏时间线标题上方（`panel-title` 之前）加父链接行：`v-if="detail?.subFlowParent"` → `<el-link @click="openInstance(detail.subFlowParent.instanceId)">{{ t('oa.detail.parentFlow') }}: {{ detail.subFlowParent.flowName || detail.subFlowParent.flowKey }}</el-link>`。
2. 时间线之后、CC 之前加子实例段：`v-if="detail?.subFlows?.length"` → 标题 `t('oa.detail.subFlows')` + 列表行（`#{{ s.subIndex }} {{ s.flowName || s.flowKey }}` + 状态 tag（复用既有实例状态映射）+ `el-link` 跳转）。
3. `openInstance(id)`：`FormDetail` 以 `instanceId` prop 驱动——沿用其宿主（InboxView 系）既有的实例打开机制：若宿主经路由 query 打开则 `router.push` 同款；否则 emit `open-instance` 事件由宿主处理（执行者以 `grep -n "FormDetail" cp6.web/src/views/oa/inbox/*.vue` 实读宿主用法后择既有机制，**不发明新导航**）。
4. 样式零硬编码色（`var(--cp-*)` token）。

- [ ] **Step 3: 验证 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter SubFlowInboxDetailTests
dotnet test CP6.Tests/CP6.Tests.csproj --filter Oa      # InboxService 面回归
npm run test && npm run type-check && npm run build
git add -A && git commit -m "feat(wfs-subflow): E-T3 收件箱父子互链(InboxDetail扩展+FormDetail父链接/子实例列表)"
```

---

## Wave S-F — i18n + QA + 验收（紧跟 S-E，不留窗口）

### Task F-T1: i18n 五语 seed（18 键）

**Files:**
- Create: `CP6.WebApi/Seed/I18nOaSubFlowScreenSeed.cs`
- Modify: `CP6.WebApi/Program.cs`（i18n concat 链尾——二期 `I18nOaKernelHardeningScreenSeed` 行之后）

- [ ] **Step 1: 实现 seed**（仿 `I18nOaServiceTaskScreenSeed` 静态 `Sys_Lang[] Items` 模式；**先 grep 确认 18 键零重复**：`grep -rn "subflow\|subFlow\|parentFlow\|E-WF-025\|E-WF-026" CP6.WebApi/Seed/`）：

```csharp
using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>子流程(subFlow)画面词条：oa.designer.subflow.* + 前端校验 errSubFlowConfig + 收件箱互链 oa.detail.* +
/// 后端错误码 E-WF-025/026。键面以 cp6.web/src/views/oa 实际引用为权威
/// （SubFlowNode.vue / NodePropertyPanel.vue / designerModel.ts / FormDetail.vue）。
/// 去重：18 键在既有 I18nOa* seed 中均无重复（落地前 grep 复核）。</summary>
public static class I18nOaSubFlowScreenSeed
{
    public static readonly Sys_Lang[] Items =
    {
        // ── 节点/面板（SubFlowNode.vue + NodePropertyPanel.vue）──
        new() { LangKey = "oa.designer.subflow.title",         ZhCN = "子流程",         ZhTW = "子流程",         En = "Sub-flow",              Ja = "サブフロー",           Ko = "하위 플로우" },
        new() { LangKey = "oa.designer.subflow.target",        ZhCN = "目标流程",       ZhTW = "目標流程",       En = "Target Flow",           Ja = "対象フロー",           Ko = "대상 플로우" },
        new() { LangKey = "oa.designer.subflow.targetHint",    ZhCN = "引用一个已发布流程作为子步骤（不能引用当前流程自身）", ZhTW = "引用一個已發布流程作為子步驟（不能引用當前流程自身）", En = "Reference a published flow as a sub-step (cannot reference this flow itself)", Ja = "公開済みフローをサブステップとして参照します（自分自身は参照できません）", Ko = "게시된 플로우를 하위 단계로 참조합니다(자기 자신은 참조 불가)" },
        new() { LangKey = "oa.designer.subflow.varsIn",        ZhCN = "传入变量映射",   ZhTW = "傳入變數對應",   En = "Input Variable Map",    Ja = "入力変数マッピング",   Ko = "입력 변수 매핑" },
        new() { LangKey = "oa.designer.subflow.varsOut",       ZhCN = "回注变量映射",   ZhTW = "回注變數對應",   En = "Output Variable Map",   Ja = "出力変数マッピング",   Ko = "출력 변수 매핑" },
        new() { LangKey = "oa.designer.subflow.varsHint",      ZhCN = "JSON 映射：{\"变量\":\"$.路径\"}，多实例回注为数组", ZhTW = "JSON 對應：{\"變數\":\"$.路徑\"}，多實例回注為陣列", En = "JSON map: {\"var\":\"$.path\"}; multi-instance writes back an array", Ja = "JSONマッピング：{\"変数\":\"$.パス\"}。複数インスタンスでは配列で書き戻します", Ko = "JSON 매핑: {\"변수\":\"$.경로\"}, 다중 인스턴스는 배열로 기록" },
        new() { LangKey = "oa.designer.subflow.multi",         ZhCN = "多实例",         ZhTW = "多實例",         En = "Multi-instance",        Ja = "マルチインスタンス",   Ko = "다중 인스턴스" },
        new() { LangKey = "oa.designer.subflow.collectionVar", ZhCN = "集合变量",       ZhTW = "集合變數",       En = "Collection Variable",   Ja = "コレクション変数",     Ko = "컬렉션 변수" },
        new() { LangKey = "oa.designer.subflow.policy",        ZhCN = "完成策略",       ZhTW = "完成策略",       En = "Completion Policy",     Ja = "完了ポリシー",         Ko = "완료 정책" },
        new() { LangKey = "oa.designer.subflow.policy.all",    ZhCN = "全部通过",       ZhTW = "全部通過",       En = "All approved",          Ja = "全件承認",             Ko = "전체 승인" },
        new() { LangKey = "oa.designer.subflow.policy.any",    ZhCN = "任一通过",       ZhTW = "任一通過",       En = "Any approved",          Ja = "いずれか承認",         Ko = "하나라도 승인" },
        new() { LangKey = "oa.designer.subflow.policyHint",    ZhCN = "全部通过：任一驳回/撤回即走错误处置；任一通过：首个通过即恢复并撤回其余", ZhTW = "全部通過：任一駁回/撤回即走錯誤處置；任一通過：首個通過即恢復並撤回其餘", En = "All: any rejection/withdrawal triggers error handling. Any: first approval resumes and withdraws the rest.", Ja = "全件承認：いずれかが却下/取下げされるとエラー処理へ。いずれか承認：最初の承認で再開し残りを取り下げます。", Ko = "전체 승인: 하나라도 반려/철회되면 오류 처리. 하나라도 승인: 첫 승인 시 재개하고 나머지는 철회합니다." },

        // ── 前端校验消息（designerModel.ts validateClient 镜像）──
        new() { LangKey = "oa.designer.errSubFlowConfig",      ZhCN = "子流程配置不完整或非法", ZhTW = "子流程配置不完整或非法", En = "Sub-flow config incomplete or invalid", Ja = "サブフローの設定が不完全または不正です", Ko = "하위 플로우 구성이 불완전하거나 잘못되었습니다" },

        // ── 收件箱互链（FormDetail.vue）──
        new() { LangKey = "oa.detail.parentFlow",              ZhCN = "父流程",         ZhTW = "父流程",         En = "Parent Flow",           Ja = "親フロー",             Ko = "상위 플로우" },
        new() { LangKey = "oa.detail.subFlows",                ZhCN = "子流程实例",     ZhTW = "子流程實例",     En = "Sub-flow Instances",    Ja = "サブフローインスタンス", Ko = "하위 플로우 인스턴스" },
        new() { LangKey = "oa.detail.subIndex",                ZhCN = "序号",           ZhTW = "序號",           En = "Index",                 Ja = "序数",                 Ko = "순번" },

        // ── 后端错误码（FlowSchemaValidator / SubFlowRefValidator / SubFlowNodeHandler）──
        new() { LangKey = "E-WF-025", ZhCN = "子流程配置无效（引用/映射/策略/集合）", ZhTW = "子流程配置無效（引用/對應/策略/集合）", En = "Invalid sub-flow configuration (reference/map/policy/collection)", Ja = "サブフロー設定が無効です（参照/マッピング/ポリシー/コレクション）", Ko = "하위 플로우 구성이 잘못되었습니다(참조/매핑/정책/컬렉션)" },
        new() { LangKey = "E-WF-026", ZhCN = "子流程引用成环或嵌套深度超限",         ZhTW = "子流程引用成環或嵌套深度超限",         En = "Sub-flow reference cycle or nesting depth exceeded",              Ja = "サブフロー参照が循環しているか、ネスト深度が上限を超えています",   Ko = "하위 플로우 참조가 순환하거나 중첩 깊이가 초과되었습니다" },
    };
}
```

- [ ] **Step 2: Program.cs concat** — i18n 链尾（二期 `I18nOaKernelHardeningScreenSeed` 行之后）加：

```csharp
            .Concat(CP6.WebApi.Seed.I18nOaSubFlowScreenSeed.Items)  // WFS 子流程 oa.designer.subflow.* + oa.detail.parentFlow/subFlows + E-WF-025/026
```

- [ ] **Step 3: build 验证 + commit** — `dotnet build CP6.WebApi/CP6.WebApi.csproj`（SeedLangs 运行期幂等去重）。

```bash
git add -A && git commit -m "feat(wfs-subflow): F-T1 I18nOaSubFlowScreenSeed 五语18键+concat"
```

---

### Task F-T2: gstack QA harness（只写不跑）

**Files:**
- Create: `docs/superpowers/qa/wfs-subflow/README.md`（剧本）
- Create: `docs/superpowers/qa/wfs-subflow/seed.sql`
- Create: `docs/superpowers/qa/wfs-subflow/qa_subflow.ps1`（HTTP e2e，ASCII 数据）

- [ ] **Step 1: 写 harness**（参 `docs/superpowers/qa/wfs-service-task/` 与 `wfs-kernel-hardening/` 先例：README 剧本 + seed.sql + ps1 三件套；seed.sql 对 OA 表用单数表名 `Wf_FlowDef`/`Wf_FormDef`、`SET QUOTED_IDENTIFIER ON`；隔离库 `CP6DB_OA`）。剧本 7 条：
  1. **单实例全链**：seed 父流程（sub→审批）+ 子流程（审批→end）；提交父 → 父详情见「子流程实例」列表；办结子审批 → 父自动恢复到下一关（20s 内含 worker 兜底路径验证：直调 fast path 场景 + 停 worker 场景各一遍）。
  2. **父子互链走查**（gstack browse）：打开子实例 FormDetail → 点「父流程」链接回父详情 → 父详情子实例列表 → 点回子详情。
  3. **多实例 all + 数组回注**：集合 3 元素 → 3 子实例；乱序办结 → 父 vars 回注数组按序号排列（`CurrentDataJson` 校验）。
  4. **all 任一驳 + 级联**：驳一个子 → 其余子实例被撤回（收件箱消失）、父走错误边/驳回；发起人时间线可见 `subFlowError`。
  5. **any 首过**：首个子通过 → 父恢复、其余子撤回。
  6. **组合语义**：并行支 `onBranchReject=prune` 上挂 subFlow，子驳 → 只剪该支、兄弟支继续办至通过。
  7. **设计器真浏览器**（gstack browse）：palette 拖 subFlow（双边框节点渲染）→ 面板选目标流程（下拉只见已发布且非自身）→ 配自引用/两流程互引 → 保存报错含 `E-WF-026` 五语文案；删非错误出边 → `oa.designer.errSubFlowConfig`。
- [ ] **Step 2: commit**

```bash
git add -A && git commit -m "test(wfs-subflow): F-T2 gstack QA harness(7剧本+seed+e2e脚本,只写不跑)"
```

- [ ] **Step 3: 末期 live QA（用户在场）** — 隔离库 `CP6DB_OA` 起后端 + 前端 → 跑 ps1 HTTP e2e + gstack 真浏览器走剧本 2/7。**抓 bug 当场 TDD 修**（回归测试补进 `CP6.Tests/Wf`）。

---

### Task F-T3: DoD 验收（主代理执行）

- [ ] 后端 `dotnet test CP6.Tests/CP6.Tests.csproj` 全绿：**基线+N 通过（5 skip 不变）**；`git diff main -- CP6.Tests/Wf` 复核既有文件只增不改。
- [ ] `dotnet ef migrations has-pending-model-changes ... --context CP6Context` **clean**，且 `git log --stat` 全分支恰一个迁移文件对 `WfsSubFlow`。
- [ ] 前端 `npm run test` 基线+N 全绿（唯一既有改动=designerModel.test.ts palette 清单断言）；`npm run type-check`（`NODE_OPTIONS=--max-old-space-size=8192` 内建于命令）/ `npm run build` 过。
- [ ] **零 Space 污染**：`git diff --stat main..HEAD` 无 `views/space` / `*Space*` / Space 迁移。
- [ ] **零硬编码色**：新增前端文件 grep 无 `#[0-9a-fA-F]{3,6}` 字面色。
- [ ] **零占位符**：`grep -rn "TODO\|FIXME\|placeholder\|场景注释" <新增文件>` 零命中；所有测试体为可编译完整代码。
- [ ] i18n 18 键五语齐 + LangKey 与既有 seed 零重复（grep 复核）。
- [ ] 错误码齐备：E-WF-025（SubFlowValidatorTests 静态+DI 双层 + SubFlowHandlerTests 运行时）/ E-WF-026（保存 DFS + 运行时深度守卫双检测试）。
- [ ] spec §7 测试矩阵覆盖核对表（下节）逐行有落点。
- [ ] 铁律复核：第一段窗口内零外呼（`SubFlowResume.EnqueueIfChild` 纯 Add，`git grep` 其调用点仅 `DispatchIfFinishedAsync`/`WithdrawAsync`）；handler 零 `SaveChanges`。
- [ ] QA harness 三件套齐（7 剧本）；live QA 留待用户在场（记入 memory 待办）。
- [ ] `git log` 提交信息全部 `feat(wfs-subflow):` / `test(wfs-subflow):` 中文风格；**只本地 commit 不 push**。

---

## 覆盖核对（spec §7 测试矩阵 → 任务落点）

| spec §7 条目 | 落点 |
|---|---|
| 单实例：起子→停泊→Approved 回注恢复 | B-T1 `SingleInstance_*` + B-T2 `Single_ChildApproved_*` |
| 子 Rejected 有错边走错边 / 无错边传播父驳回 | B-T2 `Single_ChildRejected_ErrorEdge_*` / `Single_ChildRejected_NoErrorEdge_*` |
| 子 Withdrawn 同口径（手工撤回入计票） | B-T3 `ManualChildWithdraw_NullEngine_*` + B-T4 `FastPathCrash_*` |
| 多实例 N=3 all 全过回注数组 | B-T2 `All_N3_AllApproved_ArrayWritebackBySubIndex` |
| all 任一驳→错误处置+级联取消兄弟 | B-T2 `All_OneRejected_CascadesSiblings_ErrorPath` |
| any 首过恢复+级联撤回其余 / any 全驳才错误处置 | B-T2 `Any_FirstApproved_*` / `Any_AllRejected_ErrorPath` |
| N=0 空集直通 / 集合非数组 E-WF-025 / N 超上限 E-WF-025 | B-T1 `EmptyCollection_*` / `CollectionNotArray_*` / `OverCap_*` |
| 组合语义：并行支 + 无错边 + 子驳 + onBranchReject=prune 剪父支不连坐 | C-T2 `ComboSemantics_SubFlowInParallelBranch_Prune_*` |
| 级联：父撤回→子递归取消且不回注 / 三层嵌套级联 | C-T1 `ParentWithdraw_CascadesThreeLevels_NoWriteback` |
| 深度 8 守卫 E-WF-026 / 保存时环检测 | B-T1 `DepthGuard_ChainOf10_*` / D-T1 `Ref_SelfReference/TwoNodeCycle/ChainDepth8_*` |
| 停泊重入不重复起子（唯一键） | B-T1 幂等跳过逻辑 + B-T4 `ParkedReentry_UniqueSlot_*`（SQLite 真索引面） |
| **跨事务并发双子终态：all 不丢唤醒** | B-T4 `All_TwoRequests_StaleIdentityMap_ReloadDefeatsIt_NoLostWakeup` |
| **跨事务并发双子终态：any 不双恢复（RowVersion 撞→状态闸零动作）** | B-T4 `Any_LateStaleChecker_RowVersionClash_StateGate_NoDoubleResume` |
| fast path 崩溃后 worker 兜底补唤醒 | B-T4 `FastPathCrash_WorkerScan_RescuesWakeup`（+ B-T3 InMemory 版） |
| fast path 与 worker 兜底各自幂等 / 父 token 已恢复后迟到复核零动作 | B-T2 `TokenAlreadyResumed_LateCheck_ZeroAction` + B-T3 job Succeeded 断言 + B-T4 撞版测试 |
| 剪枝停泊 subFlow token → 子实例组级联取消（第五清） | C-T1 `CancelTokenSubtree_FifthClean_*` |
| SameBranch/BeforeSplit 退回 → 旧批取消+新批起且不并跑 | C-T2 `BeforeSplitSendBack_OldBatchCancelled_*` |
| 孙 subFlow 递归恢复 | B-T3 `GrandChild_NestedResume_PropagatesTwoLevels` |
| 收件箱父子互链 | E-T3 `SubFlowInboxDetailTests` + QA 剧本 2 |
| 设计器面板/round-trip/validateClient 镜像 | E-T1 `designerModel.subflow.spec.ts` + QA 剧本 7 |
| QA harness（设计器配 subFlow+映射+多实例、互链走查、驳回传播实况） | F-T2 剧本 1~7 |
| 基线：后端全绿+N / 前端 vitest/type-check/build / EF 恰一次 `WfsSubFlow` | F-T3 DoD |

---

## 执行顺序与依赖

```
S-A: A-T1 → A-T2 → A-T3                （数据模型/纯函数/测试基座）
S-B: B-T1 → B-T2 → B-T3 → B-T4          （handler → 复核核心 → 两段接线 → 并发矩阵；严格串行）
S-C: C-T1 → C-T2                        （依赖 B-T3；级联三路径 + 退回/组合定点）
S-D: D-T1                               （依赖 A-T1/A-T2；与 S-C 并行,注意与 C 波无共享文件）
S-E: E-T1 → E-T2 → E-T3                 （依赖 S-B/S-D 的 schema 契约；E-T3 只依赖 A-T1+B-T1）
S-F: F-T1 → F-T2 → F-T3                 （紧跟 S-E，不留窗口）
```

（S-C‖S-D 并行时唯一注意：两波都不改对方文件——C 波在 `FlowEngine.Tokens.cs`/`TaskCenterService.cs`/`SubFlowCascade.cs`，D 波在 `FlowSchemaValidator.cs`/`SubFlowRefValidator.cs`/`DesignerService.cs`，零交集。）

---

*生成于 2026-07-05，由 spec `2026-07-05-wfs-subflow-design.md`（唯一权威，D1~D5 锁定）细化。执行铁律：DispatchIfFinished 原子接缝内零外呼（第一段只入队）；引擎内写路径三律；恰一次迁移 WfsSubFlow；E 波紧跟 D 波；零跨模块污染。与二期 hardening 计划的契约名（CancelTokenSubtree/TryPruneBranchAsync/FlowTokenStatus.Pruned/SnapshotTokens）逐字对齐，第五清接缝落在其声明文件 FlowEngine.Tokens.cs 上。*






