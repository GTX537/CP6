# WFS 子流程（Call-Activity + 动态多实例）设计

> 生成于 2026-07-05（brainstorming 已确认，WFS 深化三期 Spec A）。上游：内核 spec §11 P4「子流程」；ServiceTask spec §13 留后条目「子流程(subprocess,P4)：嵌套实例生命周期；本增量异步底座已铺路」。
> 依赖：二期 hardening spec（血缘工具/剪枝/错误边机制）先行落地。
> 落码位：`CP6.Entity/DomainModels/Wf`、`CP6.Core/Services/Wf`、`cp6.web/src/views/oa`。

---

## §0 背景、范围与决策

### §0.1 背景

流程目前无法复用另一个流程：跨部门大流程只能把全部关卡摊平在一个 schema 里，无法引用已发布的「采购审批」「合同审批」作为子步骤。停泊-恢复 plumbing（token 停泊 + 异步回调恢复）已被 serviceTask 异步底座实证，子流程是这套机制的第二个消费者、也是引擎最后一块大能力。

### §0.2 范围（In / Out）

**In**：subFlow 节点类型（call-activity 引用式）；动态多实例（集合变量展开 N 子实例）；完成计票 all/any；子终态回注父（变量映射双向）；错误边优先/无边传播；级联取消；防环/防深；设计器面板；收件箱父子互链；一次迁移。

**Out（→ §10 YAGNI）**：内嵌子图式 subprocess；quorum 完成策略（K/N）；子实例局部变量作用域隔离（子 vars 即独立实例 vars，天然隔离，无需额外机制）；跨租户子流程。

### §0.3 锁定决策（用户已拍板 2026-07-05）

| # | 决策 | 依据 |
|---|------|------|
| D1 | **Call-Activity 引用式**：父节点引用另一个已发布 FlowKey，启动独立子实例（独立收件箱可见/独立传签履历），父 token 停泊等子终态 | 复用发布/版本/设计器机制，BPMN Call Activity 标准解；否决内嵌子图（嵌套域改动面大、不可复用） |
| D2 | 子驳回/撤回传播 = **错误边优先，无边则传播**：父 subFlow 节点挂 IsError 边→沿错误边路由；无→父实例驳回（父节点在并行支内时受二期 `onBranchReject` 剪枝配置管辖，两期语义自动组合） | 与 serviceTask 错误路由同构，设计器心智统一 |
| D3 | **动态多实例一并做**：`SubCollectionVar` 集合变量展开 N 并行子实例 + 完成策略 all/any | 用户选项确认 |
| D4 | 回指用**正式列**：`Wf_FlowInstance.ParentInstanceId/ParentTokenId`（一次迁移 `WfsSubFlow`），不借用 BizType/BizId | 保持业务单据绑定语义纯净 |
| D5 | 终态回注挂 **`DispatchIfFinished` 接缝**（SaveChanges 前原子）——与业务终态分发同一原子窗口 | 引擎原子接缝铁律（内核既有不变量） |

---

## §1 现状锚点（逆向真实，不编造）

- **发起入口**：`IFlowEngine.SubmitAsync(flowKey, starterId, varsJson, bizType?, bizId?)`（三期 plan 侦察已核实，非 spec 早期误记的 StartAsync）。
- **停泊范式**：serviceTask async 停泊 token（token 停节点不动）+ `ResumeServiceTokenAsync`（幂等）/`FailServiceTokenAsync` + 错误路由 `AdvanceAlongErrorEdge`——subFlow 恢复复用同款「停泊-回调恢复」形态，但**不走 Wf_ServiceJob 队列**（子实例本身就是停泊的凭据，无需扫描器）。
- **终态分发**：`ApprovalDispatcher` / `DispatchIfFinished`（`FlowEngine.cs:232-245`，必须在最终 SaveChangesAsync 之前调用）——子实例终态回注父的挂点。
- **撤回/终止清场**：实例撤回/驳回的 `CancelAllActiveTokens` + `VoidPendingFormTos` + 二期 `CancelTokenSubtree`（H-C 落地后可用）。
- **血缘/剪枝**：二期 `TokenLineage`/`onBranchReject`——D2 的「传播驳回」进入父实例后自动受其管辖，本 spec 不重复设计。
- **handler 字典**：二期后共 8 个（含 inclusiveSplit/Join），subFlow 是第 9 个。
- **错误码水位**：二期分配至 E-WF-021、波③ 022~024，本 spec 从 **E-WF-025** 起。
- **发布目录**：设计器已有已发布流程清单端点（C′ 波流程管理），面板下拉数据源复用。

---

## §2 数据模型

### §2.1 `FlowNode` 子流程配置（SchemaJson POCO，零迁移，全可空向后兼容）

```csharp
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

### §2.2 `Wf_FlowInstance` 回指列（唯一迁移 `WfsSubFlow`）

```csharp
/// <summary>父流程实例（子流程 call-activity 回指；null=顶层实例）。</summary>
public Guid? ParentInstanceId { get; set; }
/// <summary>父流程停泊 token（子终态回注/恢复的定位键）。</summary>
public Guid? ParentTokenId { get; set; }
/// <summary>多实例序号（0 起；单实例=0）。与 ParentTokenId 组成防重唯一键。</summary>
public int? SubIndex { get; set; }
```

索引：`(ParentInstanceId)`；**filtered unique** `(ParentTokenId, SubIndex) WHERE ParentTokenId IS NOT NULL`——停泊重入防重复起子实例的幂等闸。

### §2.3 常量

`WfStatus.cs` 无新状态：父 token 停泊沿用 Active-停节点形态（与 serviceTask async 停泊一致）；子实例状态就是普通 `FlowInstanceStatus`。

---

## §3 执行架构

### §3.1 `SubFlowNodeHandler`（Type=`"subFlow"`，第 9 个 handler）

OnEnter：
1. 解析配置（校验层已保证合法，防御式复检 E-WF-025）。
2. 集合解析：`SubCollectionVar` null → 单实例（N=1）；否则从父 `inst.VarsJson` 取 JSON 数组，N=长度（**N=0 → 视为空集完成**：与完成策略无关，直接恢复父 token 沿非错误出边前进、不回注，历史记 "subFlowEmptyCollection"——不发起任何子实例，不算错误）。
3. 逐 i∈[0,N)：构造子 varsJson =（`SubVarsInJson` 映射自父 vars）∪ `{"item": 集合[i], "itemIndex": i}`（单实例无 item 键）→ `SubmitAsync(SubFlowKey, 父inst.StarterId, 子varsJson)` → 新子实例回填 `ParentInstanceId/ParentTokenId/SubIndex`。撞 `(ParentTokenId,SubIndex)` 唯一键 → 幂等跳过（停泊重入）。
4. 父 token 停泊（不 Advance），AddHistory("subFlowStarted", 记 N 与子实例 Id 列表)。

**深度守卫**：Submit 前沿 `ParentInstanceId` 链上溯计数，≥ 8 层 → 抛 E-WF-026（运行时兜底；静态防环见 §5）。

### §3.2 终态回注 `ResumeSubFlowParentAsync`（挂 `DispatchIfFinished`）

子实例进终态且 `ParentInstanceId != null` 时，在**同一 SaveChanges 窗口**内调用：

1. 取同 `ParentTokenId` 的全部子实例组，按 `SubCompletionPolicy` 计票：

| 策略 | 恢复条件 | 错误处置条件 | 附带动作 |
|---|---|---|---|
| **all**（默认） | 全部 Approved | **任一** Rejected/Withdrawn | 错误处置时级联取消其余在途子实例 |
| **any** | **首个** Approved | **全部** Rejected/Withdrawn | 恢复时级联撤回其余在途子实例 |

2. **恢复路径**：按 `SubVarsOutJson` 回注父 vars（多实例=按 SubIndex 聚合为数组；any=仅首个 Approved 的值）→ 恢复父 token `AdvanceToken` 沿非错误出边继续。幂等：父 token 已非停泊态（已恢复/已取消）→ 零动作返回（与 `ResumeServiceTokenAsync` 同款状态闸）。
3. **错误处置路径**（D2）：错误变量注入父 vars（`subFlowError`: 触发子实例 Id/终态/SubIndex）→ 父节点有 IsError 出边 → `AdvanceAlongErrorEdge`；无 → 父实例驳回（走既有驳回语义；父节点在并行支内时由二期 `onBranchReject` 决定剪枝或连坐——本 spec 零新增逻辑，语义自动组合）。
4. 计票口径防竞态：同窗口两子实例同时终态——计票基于 DB+Local 全量子实例组状态（EF 身份映射），后到者看见先到者已把父 token 恢复/取消 → 状态闸零动作。

### §3.3 级联取消

- **父实例终止**（驳回/撤回/被更上层级联）：清场路径追加「递归级联取消在途子实例」——子实例走既有撤回语义（`Withdrawn` + 清场 + 其 pending 待办作废），子实例自己的子实例递归。级联产生的 `Withdrawn` **不再向父回注**（父已终态，回注入口检查父实例状态闸）。
- **子实例被用户手工撤回**：等价子终态 Withdrawn → 走 §3.2 计票（all=触发错误处置；any=计入全驳判定）。

---

## §4 设计器与收件箱

1. **palette**：「自动化家族」加 subFlow 入口（图标区分 serviceTask，Design System token）。
2. **NodePropertyPanel** subFlow 段：目标流程下拉（已发布 FlowKey 目录，复用流程管理清单端点，排除当前流程自身）/ 变量映射双向键值编辑（in/out 两组）/ 多实例开关（开=集合变量名输入 + 完成策略 radio all/any）。
3. **EdgePropertyPanel**：不动（IsError 复选已有，来源校验放宽由基建 spec W-1 承担 approval，subFlow 在本 spec 校验规则里放行）。
4. **designerModel round-trip** + validateClient 镜像（§5 规则）。
5. **收件箱互链**：子实例 FormDetail 时间线头部加「父流程」链接（ParentInstanceId → 父详情）；父 FormDetail 的 subFlow 停泊节点行展开子实例列表（SubIndex/状态/链接）。
6. **i18n 五语**：估 ~18 键（节点名/面板项/完成策略/错误码 025·026/父子链接文案），续 `I18nOa*ScreenSeed` 家族。

---

## §5 校验（双层 + 前端镜像）

| 码 | 规则 | 层 |
|---|---|---|
| **E-WF-025** | subFlow 配置无效：SubFlowKey 空/不存在/未启用；SubVarsIn/OutJson 非法 JSON 或含不支持下标（复用波① `ContainsUnsupportedSubscript`）；SubCompletionPolicy ∉ {all,any}；SubCollectionVar 空串 | 保存时（DesignerService，有 DI 查 FlowKey）+ 运行时防御复检 |
| **E-WF-026** | 引用环/深度：保存时沿 SubFlowKey 引用链探环（A→B→A；DFS 上限 8 层，链上任何 FlowKey 重复即环）；运行时 Submit 前深度守卫 ≥8 | 保存时 + 运行时双检 |

静态规则（FlowSchemaValidator 纯静态，无 DI 不能查 FlowKey 存在性）：subFlow 节点须有非错误出边（对齐 serviceTask E-WF-016 同款规则）；SubFlowKey 非空。前端 validateClient 镜像静态部分。

---

## §6 安全 / 多租户 / 向后兼容

- 子实例继承父实例 TenantId（SubmitAsync 天然同租户上下文）；跨租户引用不存在（FlowKey 查询租户内）。
- 子实例权限：收件箱可见性/办理权限与普通实例完全一致（独立实例，零特判）。
- 纯增量：无 subFlow 节点的既有流程零影响；`ParentInstanceId` 可空列对既有行 null；27+ Wf 不变量测试全绿保持。

---

## §7 测试策略

- **单实例**：起子→父停泊→子 Approved 回注恢复 / 子 Rejected 有错边走错边 / 无错边传播父驳回 / 子 Withdrawn 同口径。
- **多实例**：N=3 all 全过回注数组 / all 任一驳触发错误处置+级联取消兄弟 / any 首过恢复+级联撤回其余 / any 全驳才错误处置 / N=0 空集直通 / 集合非数组 E-WF-025。
- **组合语义**：父 subFlow 节点在并行支内 + 无错边 + 子驳 → 外层 `onBranchReject=prune` 时剪父支不连坐（二期语义组合的定点测试）。
- **级联**：父撤回→子递归取消且不回注 / 三层嵌套级联 / 深度 8 守卫 E-WF-026 / 保存时环检测。
- **幂等/竞态**：停泊重入不重复起子（唯一键）/ 双子同窗终态计票一次 / 父 token 已恢复后迟到回注零动作。
- **QA harness**：gstack 剧本（设计器配 subFlow+映射+多实例、父子收件箱互链走查、驳回传播实况）。
- 基线：后端全绿 +N；前端 vitest/type-check/build 全绿；EF 迁移恰一次 `WfsSubFlow`。

---

## §8 分期 / 任务波次（供 writing-plans 细化）

- **S-A 数据模型**：FlowNode POCO + 迁移（回指三列+索引）+ 常量/防重键。
- **S-B handler + 回注**：SubFlowNodeHandler + ResumeSubFlowParentAsync + 计票 + 错误处置 + 深度守卫。
- **S-C 级联**：父终止递归级联 + 手工撤回入计票。
- **S-D 校验**：E-WF-025/026 双层 + validateClient 镜像。
- **S-E 设计器 + 收件箱**：palette/面板/round-trip + 父子互链。
- **S-F i18n + QA**：五语 seed + harness + DoD。

依赖：S-A → S-B → {S-C ‖ S-D} → S-E → S-F。**前置：二期 hardening H-A~H-C 已并 main**（血缘工具/剪枝/CancelTokenSubtree）。

---

## §9 YAGNI / 留后

- quorum 完成策略（K/N 过）；子实例错误重试（子驳回=业务决定，非技术故障，不重试）。
- 内嵌子图 subprocess；跨租户子流程。
- 多实例动态追加（运行中往集合加元素）。
- 子实例批量操作 UI（父详情一键催办全部子实例等）。

---

*生成于 2026-07-05。执行遵守铁律：DispatchIfFinished 原子接缝内零外呼；引擎内写路径三律；E 波紧跟 D 波；零跨模块污染。*
