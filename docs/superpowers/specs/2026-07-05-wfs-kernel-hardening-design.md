# WFS 引擎内核 hardening · inclusive 网关 / 驳回剪枝 / 退回三规则 设计

> 生成于 2026-07-05（brainstorming 已确认）。上游：umbrella 总设计 §9「YAGNI/后续」内核 hardening 三项。
> 配套：L0 内核 spec `2026-06-26-wfs-runtime-kernel-design.md`；ServiceTask spec `2026-06-29-wfs-service-task-design.md`（handler 扩展点先例）。
> 落码位：`CP6.Core/Services/Wf`、`cp6.web/src/views/oa/designer`。

---

## §0 背景、范围与决策

### §0.1 背景

内核 P1 的并行语义有三处已知短板（umbrella §9 记档）：

1. **驳回连坐**：任一分支驳回 → 整实例终止（`FlowEngine.cs:224` 注释原文「驳回 = terminate，兄弟分支连坐」）。多部门并行会签场景下，一个部门否了，其他部门的意见直接作废，业务上常常不是想要的。
2. **退回全清场**：`SendBackAsync` 无论退到哪，都作废本实例**全部**在途 token/待办（`IFlowEngine.cs:26` 注释「作废本实例所有在途待办」）。同分支内小退一步，也会把兄弟分支整体抽掉。
3. **无 inclusive 包容网关**：只有 parallelSplit（全边必走）和条件互斥边，没有「按条件走一到多条」的语义。

### §0.2 范围（In / Out）

**In**：inclusiveSplit/inclusiveJoin 两个新节点类型；parallelSplit/inclusiveSplit 的 `onBranchReject` 剪枝配置；SendBack 退回作用域三规则；join 计票由静态入边数改为动态活支数；设计器镜像（palette/面板/校验/i18n/QA）。

**Out（→ §10 YAGNI）**：错误边来源放宽到 approval 节点；剪枝触发补偿动作；跨块退回到兄弟分支内部（永久禁止，非缓做）；inclusive 多 default 边。

### §0.3 锁定决策（用户已拍板 2026-07-05）

| # | 决策 | 依据 |
|---|------|------|
| D1 | 驳回 = **剪枝语义，split 节点级可配置** `onBranchReject: cascade(默认)\|prune`；默认 cascade 与现状全等，既有流程零影响 | 用户选项确认 |
| D2 | 退回 = **三规则分治**：同分支内只清本支 / 退到 split 前整块重来 / 退到兄弟支内部拒绝（E-WF-019） | 用户选项确认 |
| D3 | inclusive 网关 = **独立节点类型对** `inclusiveSplit`/`inclusiveJoin`（handler 字典第 7/8 个），不在 parallel handler 上加 mode 旗标 | 两种语义混一个 handler 违背单一职责，校验规则纠缠；ServiceTask 已实证字典分发扩展点 |
| D4 | join 计票统一改为**动态计票**：同 ForkId 曾生成 token 总数 − Pruned 数，parallelJoin 与 inclusiveJoin 共用 | 剪枝对两种 join 统一生效；无剪枝时与静态入边数等价（parallelSplit 全边生 token） |
| D5 | 零 EF 迁移：FlowNode/FlowEdge 是 SchemaJson 内 POCO；token 剪枝走新状态常量 `FlowTokenStatus.Pruned = 3` | 向后兼容铁律（ServiceTask spec §10 先例） |

---

## §1 现状锚点（逆向真实，不编造）

- **驳回连坐**：`FlowEngine.cs:223-225` —— `inst.Status = Rejected; CancelAllActiveTokens(inst.Id); VoidPendingFormTos(inst.Id)`。
- **退回**：`AdvancedFlow.cs:101-171` —— `SendBackAsync` 三目标（node/prevStage/starter），入口即全清场。
- **join 计票**：`ParallelJoinNodeHandler.cs:15-19` —— `inEdges = schema.Edges.Count(e => e.To == node.Id)` 静态数 vs 同 ForkId Active 到场数；放行 = 消费同批 + 上弹一层血缘续 token（`:25-29`）。
- **token 血缘**：`Wf_FlowToken.ForkId/ParentTokenId`；`FlowTokenStatus`：Active=0 / Consumed=1 / Cancelled=2（`WfStatus.cs:26-31`）。
- **会签计票**：`FlowEngine.cs:168-176` 节点级 all/any/veto，按本 token·本节点在途/已决任务计票。
- **履历清场**：`FlowEngine.ReadModel.cs:158-159` `VoidPendingFormTos` 已支持 nodeId/tokenId/stageIndex/stageRound 过滤——剪枝按 tokenId 过滤可直接复用。
- **校验双层**：`FlowSchemaValidator`（纯静态）+ `DesignerService.save`（有 DI）+ 前端 `designerModel.ts` `validateClient` 镜像。
- **错误码水位**：E-WF-018 已用（ServiceTask E 波），本增量从 **E-WF-019** 起。
- **不变量测试**：27 个 Wf 测试锁定既有语义，全绿保持是 DoD 一部分。

---

## §2 数据模型（零迁移）

### §2.1 FlowNode（SchemaJson POCO，可空向后兼容）

```csharp
/// <summary>分支驳回策略（仅 parallelSplit/inclusiveSplit 有意义）：null/"cascade"=连坐（现状）；"prune"=剪枝。</summary>
public string? OnBranchReject { get; set; }
```

### §2.2 FlowEdge

无新字段。inclusiveSplit 出边复用既有 `Condition`（条件表达式，`ExpressionEvaluator` 同口径）；**无条件出边 = 恒真 = default 兜底**。

### §2.3 常量（`WfStatus.cs`）

```csharp
public static class FlowTokenStatus
{
    // ... 既有 Active=0 / Consumed=1 / Cancelled=2
    public const int Pruned = 3;   // 剪枝（分支驳回不连坐；join 动态计票时计入"已死分支"）
}
```

`Cancelled` 与 `Pruned` 语义区分：Cancelled=清场类作废（撤回/退回/连坐），不参与 join 计票逻辑的"预期扣减"；Pruned=剪枝死亡，join 计票时从预期数中扣减。

---

## §3 inclusive 网关

### §3.1 `InclusiveSplitNodeHandler`（Type=`"inclusiveSplit"`，第 7 个 handler）

1. 对全部出边求值 `Condition`（无条件边恒真），得真边集 T。
2. 校验层保证 |T| ≥ 1（§6 E-WF-020 强制至少一条无条件出边兜底），运行时零真不可能；防御式兜底：|T|=0 → 抛引擎异常（校验漏网属 bug，不静默）。
3. 对每条真边各生一枚 token：新 ForkId（同批共享）、ParentTokenId=当前 token——与 parallelSplit 完全相同的血缘机制。
4. AddHistory("inclusiveSplit", 记真边集)。

### §3.2 `InclusiveJoinNodeHandler`（Type=`"inclusiveJoin"`，第 8 个 handler）

与 `ParallelJoinNodeHandler` 同构，唯一差异是计票（§3.3）。放行 = 消费同批到场 token + 上弹一层血缘续 token 沿单出边继续（复制 `ParallelJoinNodeHandler.cs:21-29` 机制）。实现上抽共享私有基类或静态辅助，**不合并两个 handler**（D3）。

### §3.3 动态计票（parallelJoin 一并改造，D4）

```
放行条件：本 join 节点到场数（同 ForkId Active）≥ 1
          且 同 ForkId 的 Active token 全部位于本 join 节点（别处无在途活支）
```

即「本 fork 所有还活着的分支都到齐了」——不数静态入边，也不数历史生成总数：

- 无剪枝、parallelSplit 场景：活支数 == 出边数，与旧静态入边计票行为全等（回归测试锁定）。
- inclusive 场景：活支 == |T|（只生成了真边 token），join 只等实际激活的分支——inclusive join 语义的标准解。
- 剪枝场景：Pruned 不是 Active，天然从等待集消失。
- **同分支退回重生 token（§5.2 保留原 ForkId）场景**：重生 token 是 Active 且不在 join → join 继续等，语义正确。（若按"曾生成总数"计票，退回重生会撑大预期数导致永久等不齐——此判据是为此专门规避的。）
- token 查询沿用 `ParallelJoinNodeHandler.AllTokens`（Local ∪ DB 身份映射去重）口径。

---

## §4 驳回剪枝（D1）

### §4.1 触发点

`FlowEngine.cs:220-225` 现驳回分支改造：节点判驳（会签 all 任一驳/veto 反对/单人驳）时——

1. 沿当前 token 的 ForkId 找到**本层 split 节点**（schema 里 ForkId 对应批次的生成者；token 血缘可定位）。
2. split 的 `OnBranchReject`：
   - `null`/`"cascade"` → 走现有连坐路径（实例 Rejected + 全清场），**一行不改**。
   - `"prune"` → 进 §4.2 剪枝路径。

### §4.2 剪枝路径

1. 本分支 token `Status = Pruned`；本分支（tokenId 过滤）Pending 待办 → Cancelled、Pending FormTo 行 → Voided（复用 `VoidPendingFormTos` tokenId 过滤）。
2. AddHistory("branchPruned")，通知发起人分支被剪（`IWfNotifier` 复用驳回通知类型，文案区分）。
3. **join 补放行探测**：剪枝可能使停泊中的 join 凑齐——扫同 ForkId 停在 join 节点的 Active token，重入其 `OnEnterAsync`（计数本身幂等，重入安全——`ParallelJoinNodeHandler.cs:6` 注释既有保证）。
4. **全剪光递归上弹**：若同 ForkId 已无任何 Active token（§3.3 判据下 join 放行至少需一枚到场 Active，故无 Active 即不可能再放行）—— 视同「该 fork 的续 token 被驳回」，递归应用**上一层** fork 的 `OnBranchReject`（外层 prune → 剪外层该支；外层 cascade/无外层 → 实例 Rejected 走既有终态分发）。递归定义天然覆盖嵌套网关。

### §4.3 不变量

- 剪枝绝不改 `inst.Status`（除全剪光递归到顶）。
- 终态分发（`DispatchIfFinished` 在 SaveChanges 前）原子接缝保持不动。
- cascade 默认路径 diff 为零（回归锁定）。

---

## §5 退回三规则（D2）

### §5.1 作用域分析

`SendBackAsync`（`AdvancedFlow.cs:104`）目标解析后、动手清场前，插入纯函数作用域判定：

```
SendBackScope Analyze(schema, token血缘链, currentNodeId, targetNodeId)
  → SameBranch | BeforeSplit | SiblingBranch
```

判定口径：沿当前 token 的 ParentTokenId 链上溯得 fork 栈；

- 目标节点在**当前分支可达域**（从本 token 所属 split 出边出发、不经过配对 join 可达的节点集，含本 token 已走过的轨迹）→ `SameBranch`；
- 目标节点在 **fork 栈某层 split 之前**（start→split 路径域）→ `BeforeSplit`（多层嵌套取包含目标的最外剥离层）;
- 其余（同 split 兄弟出边可达域）→ `SiblingBranch`。

> 实现细节留 plan 阶段核实：优先用 `Wf_FlowHistory` 轨迹（若带 token 维度）辅助判定，否则纯 schema 图可达性分析；环路（条件边回跳）以「首个公共 join 为配对边界」+ 校验兜底。

### §5.2 三规则行为

| 作用域 | 行为 |
|---|---|
| `SameBranch` | 只清**本分支**：Cancel 本 token 血缘下在途 token、Cancel 本分支 Pending 待办、Void 本分支 Pending FormTo（均 tokenId/forkId 过滤）；在目标节点重生 token **保留原 ForkId/ParentTokenId 血缘**（★join 认亲不破坏，兄弟分支照常走） |
| `BeforeSplit` | 现行为：全清场 + 目标节点单链重启（`CancelAllActiveTokens` + `VoidPendingFormTos` 全量）——被剥离的 fork 批次 token 全 Cancelled，join 无残留 |
| `SiblingBranch` | 拒绝，抛 **E-WF-019**（结构化码，非自由文本） |

`prevStage`/`starter` 目标：prevStage 天然同节点同分支 → `SameBranch` 口径收窄清场；starter 天然 `BeforeSplit`（start 在一切 split 之前）→ 现行为不变。

### §5.3 需核实项（plan 阶段）

- `SendBackToNodeAsync` 重生 token 时血缘保留现状（`AdvancedFlow.cs:124-143`）；
- `CancelAllActiveTokens` 加 forkId/tokenId 过滤重载（`FlowEngine.Tokens.cs:34`）；
- 退回后 `StageRound` 递增语义与分支局部退回的相容性（`FlowEngine.ReadModel.cs:51`）。

---

## §6 校验（双层 + 前端镜像）

`FlowSchemaValidator`（纯静态）新规则：

| 码 | 规则 |
|---|---|
| **E-WF-020** | inclusiveSplit 出边须 ≥ 2，且**至少一条无条件出边**（default 兜底，运行时零真不可能） |
| **E-WF-021** | inclusiveSplit/inclusiveJoin 须成对可达（split 各出边的首个公共汇聚 join 类型须为 inclusiveJoin；孤立 join 报错）；`onBranchReject` 值域 ∈ {cascade, prune}（写在非 split 节点上报错） |

E-WF-019（退回兄弟支拒绝）是**运行时**错误码，不在静态校验层。

前端 `designerModel.ts` `validateClient` 镜像 E-WF-020/021（对齐 ServiceTask validateClient 先例，含 timer 双字段镜像的教训——**镜像规则与后端逐条对齐，不多不少**）。

---

## §7 设计器（`cp6.web/src/views/oa/designer/`）

1. **palette**：「网关」分组加 inclusiveSplit/inclusiveJoin 两入口（图标区分 parallel：实心菱形 vs 空心圆菱形，遵循 BPMN 惯例 + Design System token，无硬编码色）。
2. **NodePropertyPanel**：parallelSplit/inclusiveSplit 节点显示「分支驳回策略」开关（cascade/prune，默认 cascade，帮助文案说明剪枝语义）。
3. **EdgePropertyPanel**：不动（inclusive 出边复用既有条件边编辑）。
4. **designerModel round-trip**：新节点类型/OnBranchReject 字段序列化往返测试（camelCase 契约对齐后端 POCO）。
5. **i18n**：五语（ja/zh-CN/zh-TW/en/ko），估 ~15 键（节点名×2、面板项、校验文案 E-WF-019/020/021、帮助文案），续 `I18nOa*ScreenSeed` 家族新 seed。

---

## §8 安全 / 多租户 / 向后兼容

- 无新表无新端点，多租户天然贯穿（引擎层已有）。
- SchemaJson 新字段全可空：旧 schema 反序列化零影响；旧引擎读新 schema（含 inclusive 节点）——不存在此场景（引擎与 schema 同库同发）。
- **铁律**：默认路径（无 inclusive 节点、无 onBranchReject 配置）行为与现状 bit 级等价，27 个既有 Wf 不变量测试一个不许改断言。

---

## §9 测试策略

- **inclusive**：2/3 真边、全真、仅 default 兜底、嵌套 parallel⊂inclusive 与 inclusive⊂parallel、动态计票与静态等价回归（parallelJoin 改造后旧场景全等）。
- **剪枝**：单支剪、多支剪、剪后 join 补放行、全剪光→实例 Rejected、嵌套递归上弹（内层全剪光×外层 prune/cascade 两态）、cascade 默认零 diff 回归、FormTo 履历状态矩阵（Pruned 分支 Voided、兄弟分支不受扰）。
- **退回**：三规则 ×（parallel/inclusive）×（node/prevStage/starter 三目标）矩阵；SameBranch 退回后血缘保持、join 仍能认亲齐批；E-WF-019 拒绝路径。
- **QA harness**：gstack 剧本（设计器拖 inclusive 对、配 prune、校验报错走查、错误码 i18n 显示），对齐 E-T3 harness 先例。
- 基线：后端 1509（5 skip=SQLite 既知）→ +N 全绿；前端 320 → +N 全绿。

---

## §10 YAGNI / 留后

- 错误边（IsError）来源放宽到 approval 节点（超时/异常统一错误路由）——仍留后（ServiceTask spec §13 原条目）。
- 剪枝触发补偿动作（剪枝时执行 serviceTask 回滚钩子）。
- inclusive 网关多 default 边优先级。
- 退回到兄弟分支内部：**永久禁止**（语义不成立），非缓做项。
- 剪枝原因链可视化（时间线上剪枝标记只做基本 history 行，富展示随 UI 需求拉动）。

---

## §11 分期 / 任务波次（供 writing-plans 细化）

- **H-A 动态计票 + inclusive 网关**：FlowTokenStatus.Pruned 常量 + parallelJoin 动态计票改造（回归锁定）→ InclusiveSplit/Join 两 handler + 注册 → 校验 E-WF-020/021。
- **H-B 驳回剪枝**：OnBranchReject POCO + 驳回分支改造（cascade 零 diff）+ 剪枝路径 + join 补放行 + 递归上弹 + 通知。
- **H-C 退回三规则**：作用域分析纯函数 + 三规则行为 + E-WF-019 + CancelAllActiveTokens 过滤重载。
- **H-D 设计器**：palette/面板/round-trip/validateClient 镜像。
- **H-E i18n + QA**：五语 seed + gstack harness + DoD 验收。

依赖：H-A → H-B（剪枝依赖动态计票）→ H-C 可与 H-B 并行（退回不依赖剪枝）→ H-D → H-E。

---

*生成于 2026-07-05。执行遵守 ServiceTask 留档铁律：E 波紧跟 D 波不留窗口；黄金模板三律适用于一切引擎内写路径；零 Space/其他模块污染。*
