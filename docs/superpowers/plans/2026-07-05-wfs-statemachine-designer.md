# WFS 状态机模式设计器 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐 Task 落地。步骤用 checkbox（`- [ ]`）跟踪。**每个 Task 执行前必读对应 spec 章节**（`docs/superpowers/specs/2026-07-05-wfs-statemachine-designer-design.md`）。本计划所有产品代码 / 测试代码在 Task 内逐条给全，禁 `{ /* 场景注释 */ }` 骨架（二期 56 处被打回教训）。**不许改设计**——遇 spec 与现状冲突照「侦察冲突登记」的实现取向执行，不回改 spec。

**Goal:** 给 OA 流程设计器（`views/oa/designer/`）新增**状态机模式**——State 表（状态=节点）+ Path 表（转移=边）双表编辑范式，服务 Delta 老用户零学习成本心智；与既有 VueFlow 图形模式**同 schema 双向投影**、顶部分段开关切换（每浏览器 localStorage 记忆）；线性+条件分支流程完整可编辑，含并行/inclusive/多实例 subFlow 的**只读降级横幅**；表格模式对 validateClient 错误做**行级定位高亮**。**后端零改动（仅 i18n seed）**、图形模式零回归。

**Architecture:** 单一事实源永远是内存 `schema: FlowSchemaDto`（DesignerView 的既有 ref）。图形模式 `DesignerCanvas` 与状态机模式 `StateMachinePanel` **共用同一 `v-model="schema"`**，顶部开关只切换渲染哪个组件，schema ref 不换。状态机模式的 SmView 是 `schemaToStateMachine(schema)` 派生视图（只读投影）；**编辑落点=write-through**：每次表格编辑（改名/条件/失败边/审批配置/插删状态）即时经 `stateMachineModel.ts` 纯函数合成回新 `schema` 并 `emit('update:modelValue')`，模式切换永远无损（切图形→切回→未保存编辑仍在）。保存按钮两模式共用（内存 schema 恒最新，走同一 `designerApi.save` + `validateClient`）。审批配置编辑复用 M-B 从 `NodePropertyPanel.vue` 抽出的 `ApprovalConfigSection.vue`（`part` prop 三态渲染，两处消费杜绝复制粘贴，行为零变化）。capability 检测：schema 含 `parallelSplit/parallelJoin/inclusiveSplit/inclusiveJoin` 节点或 subFlow 节点 `subCollectionVar` 非空 → `readonly`（横幅提示切图形模式，表格禁编辑但可查看/跳转）；否则 `editable`。

**Tech Stack:** Vue3.5 + `<script setup lang="ts">` + Element Plus（`el-table`/`el-drawer`/`el-segmented`/`el-alert`）+ Design System v1.0 tokens（`--cp-*`，`cp6.web/src/styles/tokens.css`，零硬编码色）/ vitest 4 + @vue/test-utils 2 + jsdom（`environment: jsdom`，`vitest.config.ts` 既有）/ 五语 i18n seed（`CP6.WebApi/Seed/I18nOa*ScreenSeed.cs`，`Sys_Lang[] Items` 五列 ZhCN/ZhTW/En/Ja/Ko）。

---

## Global Constraints（每个 Task 隐含遵守）

- **测试基线**：前端 `npm run test` 320 通过 → **+N 全绿**，既有测试零回归；`npm run type-check`（`NODE_OPTIONS=--max-old-space-size=8192 vue-tsc --noEmit -p tsconfig.app.json`）通过；`npm run build` 通过。
- **后端零改动**：唯一后端触点 = M-E 新增 `I18nOaStateMachineScreenSeed.cs` + Program.cs concat 一行。无实体/DbSet/迁移/控制器/服务改动。DoD 前 `git show --stat` 复核后端仅此两处。
- **图形模式零回归**：`DesignerCanvas.vue` / `designerModel.ts`（`schemaToGraph`/`graphToSchema`/`validateClient`/`NODE_PALETTE`）**一字不改**；`DesignerView.vue` 只做**加法**（顶部 mode 开关 + 条件渲染 StateMachinePanel），既有图形路径 DOM 与行为字节等价；`NodePropertyPanel.vue` 的 M-B 重构以「emit 形状/次数不变」的组件测试锁定。默认模式 = 图形（`oa.designer.mode` 缺省 `graph`），未切换用户无感知。
- **零跨模块污染**：只碰 `cp6.web/src/views/oa/designer/**`（新增 `statemachine/` 子目录 + `ApprovalConfigSection.vue` + 改 `DesignerView.vue`/`NodePropertyPanel.vue`）、`CP6.WebApi/Seed/I18nOaStateMachineScreenSeed.cs`(新建) + `Program.cs`(concat 一行)。不碰 Space/WMS/ERP/FIN/其他 OA 视图。
- **五语 i18n**：全部新 UI 文案走 `t('...')` 运行时键；键在 M-E 一次性 seed（五列）。行级错误定位复用 `oa.designer.err*` 既有键（不新增错误码）。
- **零硬编码色**：新增 CSS 一律 `--cp-*` token；node-type 徽标复用 `.dot-<type>` / `CpTag`。
- **提交纪律**：TDD（先失败测试→最小实现→绿→commit）；提交信息 `feat(wfs-smdesigner): <任务号> <中文描述>`；**只本地 commit 不 push**（本计划最终交付只保存文件，git 由用户决定）。

---

## 侦察结论（2026-07-05 实读，各 Task 代码以此为准）

### R1 现状文件与 schema 形态（逆向真实）

- **设计器壳 = `views/oa/designer/DesignerView.vue`**（**不是 spec/§1 所称 `FlowDesigner.vue`**——`FlowDesigner.vue` 是 `views/wf/designer/` 下的章09 遗留设计器，与本增量无关，见 `router` :46-47）。DesignerView 结构：顶部 `.designer-toolbar`（流程选择/身份字段/校验/保存/克隆按钮）+ `.designer-main`（`<DesignerCanvas v-model="schema" @select>` + 右侧 `NodePropertyPanel`/`EdgePropertyPanel`）。`schema = ref<FlowSchemaDto>`（:34），`doSave`（:158）走 `validateClient(schema.value)` → `designerApi.save({ schemaJson: JSON.stringify(schema.value), ... })`。
- **schema TS 形态（`designerModel.ts` 实读，权威）**：类型名 = **`FlowSchemaDto` / `SchemaNode` / `SchemaEdge`**（**不是 spec §2.2 伪代码里的 `FlowSchema`/`FlowNode`/`FlowEdge`**）。
  - `FlowSchemaDto { start?: string; nodes: SchemaNode[]; edges: SchemaEdge[] }`
  - `SchemaNode { id: string; type: string; name?: string; code?: string; approver* / stages? / countersign? / timeoutHours? / allowReject? / ccUsers? / serviceKind? / service* ...; x?: number; y?: number }`（camelCase 镜像后端 PascalCase）
  - `SchemaEdge { from: string; to: string; condition?: string; ccUsers?: string[]; isError?: boolean }`
  - 投影函数签名、SmState/SmPath 的 `raw` 字段一律绑 `SchemaNode`/`SchemaEdge`；SmView **字段名**照 spec §2.2 逐字（`no/nodeId/type/name/approverSummary/countersign?/raw`、`fromNo/toNo/condition?/isError?/raw`、`states/paths/capability`）。见冲突 C1。
- **node 类型集（`NODE_PALETTE` + handler 字典实读）**：现 = start/approval/serviceTask(三 kind)/parallelSplit/parallelJoin/end；二期 hardening 后 +inclusiveSplit/inclusiveJoin（handler 第 7/8）；三期 subFlow 后 +subFlow（handler 第 9）。`CP6.Core/Services/Wf/NodeHandlers/` 实读现 6 个 handler（Start/End/Approval/ServiceTask/ParallelJoin/ParallelSplit）。
- **测试基建**：`vitest.config.ts` `environment: jsdom` + `@vue/test-utils` 2.4 + jsdom 29 均在 `package.json`（组件 mount 测试可用，`CpTag.spec.ts` 为 mount 范本）。model 纯函数测试范本 = `designerModel.test.ts` / `designerModel.serviceTask.spec.ts`（`describe/it/expect` + 深比较 round-trip）。

### R2 condition 节点侦察结论（spec §1 遗留侦察项，**已敲定**）

**condition 不是独立节点类型——它是边属性。** 证据三重：① `designerModel.ts` 无 `condition` 型节点，`condition?: string` 是 `SchemaEdge` 的字段（:57）；② `NODE_PALETTE` 无 condition 条目；③ `CP6.Core/Services/Wf/NodeHandlers/` 无 `ConditionNodeHandler`，全库无 condition 型 handler 分发。条件分支 = **一个节点（approval/gateway）挂多条带 `condition` 表达式的出边**（`EdgePropertyPanel.vue` 的 `conditionType: 'none'|'condition'` 即编辑此字段）。

**对本计划的口径**：
- **State 表无 condition 行类型**——State 表「类型」列仅显示 `start/approval/serviceTask/subFlow/end`（+并行/inclusive 触发只读）；条件**全部落在 Path 表** `SmPath.condition`（印证 spec §2.2 SmPath.condition 字段）。
- capability 检测**不含 condition**（本就非节点）；条件边（含回跳成环）属 editable 范围，Path 表照常显示（回跳边 = `fromNo > toNo` 行）。
- 无「condition 节点是否可编辑」问题——不存在该行。

### R3 subFlow / inclusive 字段契约（二/三期计划声明，尚未编码——投影按全集写、capability 按全集检）

本计划排三期最后（M 波依赖二期 D 波 inclusive UI 与三期 S-E 波 subFlow 面板已落）。届时 `SchemaNode` 已长出下列字段（camelCase TS 镜像），投影/capability 一次写全：

- **inclusive/剪枝**（`kernel-hardening-design.md` §2.1 `FlowNode.OnBranchReject`）：节点类型 `inclusiveSplit`/`inclusiveJoin`；`onBranchReject?: string`（`cascade`|`prune`，仅 parallelSplit/inclusiveSplit 有意义）。**FlowEdge 无新字段**（inclusive 出边复用既有 `condition`，无条件出边=default 兜底边）。
- **subFlow**（`subflow-design.md` §2.1 `FlowNode` POCO）：节点类型 `subFlow`；`subFlowKey?: string`、`subVarsInJson?: string`、`subVarsOutJson?: string`、`subCollectionVar?: string`（**多实例判据**：非空/非 undefined → 多实例 → readonly）、`subCompletionPolicy?: string`（`all`|`any`）。

**capability 检测口径**（spec §2.2/D2 逐字落实）：schema 含 `parallelSplit|parallelJoin|inclusiveSplit|inclusiveJoin` 任一节点，**或** 任一 `type==='subFlow'` 节点 `subCollectionVar` 非空串 → `readonly`；否则 `editable`。serviceTask/单实例 subFlow(`subCollectionVar` 空)/条件边/错误边均 editable。

### R4 NodePropertyPanel 抽取边界结论（M-B 定向重构，**已敲定**）

`NodePropertyPanel.vue`（815 行）的 approval 配置**不是单块连续区**，散布三处 el-collapse-item：
- **basic 段**（`name="basic"`）内 `v-if="isApproval && !stageEnabled"` 单档审批人块：`approverStrategy` 下拉 + 策略条件选择器（DirectManager 级数 / Role 下拉 / Specified 远搜 / FormField / DataMap / Group 成员增删 / countersign）——:231-353。
- **stages 段**（`name="stages"`, `v-if="isApproval"`）串簽档位全编辑器 + 加档/上下移/删档 helper——:492-681。
- **advanced 段**内 `v-if="isApproval"` 的 `approverWhen`/`approverFilter`——:711-716。
- 共享脚本：`searchUsers`/`userOptions`/`userSearchLoading`、`loadRoles`/`roleOptions`/`roleLoading`、`stageEnabled`/`toggleStages`/`addStage`/`removeStage`/`moveStageUp`/`moveStageDown`/`addMember`/`removeMember`。

**抽取方案（byte-identical + 复用 + 杜绝复制粘贴）**：建 `ApprovalConfigSection.vue`，`defineProps<{ node: SchemaNode; part: 'approver' | 'stages' | 'advanced' }>()`，按 `part` 渲染对应片段（片段 3 取 1），并**内聚全部共享脚本**（上列 helper/远搜数据源移入本组件）。组件对传入的 `node` 对象**深-mutate**（沿用现 NodePropertyPanel 对 `local` 的直改范式：`node.approverStrategy = x` / `node.stages!.push(...)`）——因 `node` 与父 `local` 是同一引用，父既有 `watch(local, deep)` 单一 emit 照常触发，**emit 形状/次数字节等价**。NodePropertyPanel 把三处 approval 片段**原位**替换为 `<ApprovalConfigSection :node="local" part="approver" />` / `part="stages"` / `part="advanced"`（DOM 输出与原一致——同一 collapse-item 同一位置同一标记）；StateEditDrawer（M-C）复用同组件三片段。行为零变化由 M-B 组件测试（emit 计数/形状 + 关键片段渲染）锁定。

> **边界不含**：basic 段的 name/code/type（全节点共享，留 NodePropertyPanel）、serviceTask 段（serviceKind 三分支，留 NodePropertyPanel；spec §3 「serviceTask 段同法评估」结论=**本计划不抽 serviceTask**——State 表 editable 域含 serviceTask 但其配置编辑走图形模式或后续增量，StateEditDrawer 仅提供 approval 编辑 + 通用 name；见 C5）、advanced 段的 timeout/allowReject/timeoutAction（全节点通用）、CC 段。

### R5 i18n seed 家族与 concat 点

- 家族 = `CP6.WebApi/Seed/I18nOa{Inbox|Advanced|Designer|Notify|SerialSign|Approver|ServiceTask}ScreenSeed.cs`，`public static readonly Sys_Lang[] Items = { new() { LangKey=..., ZhCN=..., ZhTW=..., En=..., Ja=..., Ko=... }, ... }`。范本 = `I18nOaServiceTaskScreenSeed.cs`（含去重声明注释）。
- Program.cs concat 链 :1793-1819，尾部（既有 `.Where(!existingKeys)` + `GroupBy(LangKey)` 双层去重护栏）；新 seed 插 :1819 之后一行：`.Concat(CP6.WebApi.Seed.I18nOaStateMachineScreenSeed.Items)`。

### 侦察冲突登记（**不改 spec**，实现取向如下）

| # | 冲突 | 实现取向 |
|---|------|---------|
| C1 | spec §2.2 伪代码 `raw: FlowNode`/`FlowEdge` + `schemaToStateMachine(schema: FlowSchema)` vs 实际 TS 类型 `FlowSchemaDto`/`SchemaNode`/`SchemaEdge` | SmView **字段名逐字照 spec**；`raw`/函数入参绑**实际类型** `SchemaNode`/`SchemaEdge`/`FlowSchemaDto`。全计划签名统一用 `FlowSchemaDto`。 |
| C2 | spec §1/§2.3 称壳为 `FlowDesigner.vue` | 实际壳 = `DesignerView.vue`（`views/oa/designer/`）；mode 开关插 `.designer-toolbar`，条件渲染插 `.designer-main`。 |
| C3 | spec §2.2「approverSummary: string」需入模型但纯函数不宜含 i18n | `approverSummary(node)` 返回**语言中性稳定 token**（如 `'Specified'`/`'stages:2'`/`'managerChain'`/`''`）；StateTable 以 `t()` 映射本地化。round-trip 不消费此派生字段（写回走 raw + 抽屉编辑）。 |
| C4 | spec §3「表格模式对 validateClient 错误做行级定位」但 `validateClient` 现仅返回 `string[]` 码、无节点归属 | **不改 `validateClient`**（后端镜像稳定）；M-D 新增纯函数 `locateValidation(schema): SmErrorLoc[]`（同规则、附 nodeId/edgeKey 归属）供表格高亮；保存仍走既有 `validateClient`。 |
| C5 | spec §3「serviceTask/subFlow 段同法按需抽取」 | 本计划仅抽 approval 段（R4）。serviceTask/subFlow 配置编辑在 editable 状态机模式下**走图形模式**（StateEditDrawer 只做 name + approval 编辑）；State 表可显示/改名/插删 serviceTask/单实例 subFlow 行，深配置引导切图形。避免过度抽取扩面。 |
| C6 | spec §7 建议排三期最后、投影一次写全 vs subFlow/inclusive 字段**尚未编码** | 投影/capability 按 R3 字段契约**前瞻写全**；M-A 测试用**内联 `as any` 造 inclusive/subFlow 节点**（不依赖 SchemaNode 届时已加字段，测试自洽可编译）；执行 M 波前置校验 `git log` 确认二期 H 波 + 三期 S-E 波已并 main。 |

---

## File Structure（创建/修改清单）

**前端 `cp6.web/src/views/oa/designer`**
- Create `statemachine/stateMachineModel.ts` — 纯函数投影：`schemaToStateMachine` / `stateMachineToSchema` / `smCapability` / `approverSummary` / `insertStateAfter` / `deleteState` / `locateValidation` + 接口 `SmState`/`SmPath`/`SmView`/`SmErrorLoc`/`DeleteStateResult`。
- Create `statemachine/stateMachineModel.test.ts` — round-trip / capability 矩阵 / BFS 编号确定性 / 插删缝合 / 多入出拦 / write-through 无损 / 行级定位。
- Create `statemachine/StateMachinePanel.vue` — 状态机模式壳（只读横幅 + State 表 + Path 表；`v-model="schema"` write-through）。
- Create `statemachine/StateTable.vue` — State 表（编号/名称/类型/审批摘要/会签/操作 + 行级错误高亮）。
- Create `statemachine/PathTable.vue` — Path 表（从/到/条件/失败边/操作 + 行级错误高亮）。
- Create `statemachine/StateEditDrawer.vue` — 行编辑抽屉（复用 `ApprovalConfigSection`）。
- Create `statemachine/StateMachinePanel.spec.ts` / `StateTable.spec.ts` — 只读横幅渲染 / 行级错误定位映射 / 编辑 emit（mount 组件测试）。
- Create `ApprovalConfigSection.vue`（M-B）— approval 配置段抽取（`part` 三态）。
- Create `ApprovalConfigSection.spec.ts`（M-B）— 片段渲染 + 深-mutate 生效。
- Modify `NodePropertyPanel.vue`（M-B）— 三处 approval 片段替换为 `<ApprovalConfigSection>`；共享脚本移入子组件。
- Modify `NodePropertyPanel.spec.ts` **若不存在则 Create**（M-B）— emit 形状/次数行为锁定。
- Modify `DesignerView.vue`（M-C/M-D）— 顶部 mode 分段开关（localStorage `oa.designer.mode`）+ 条件渲染 `StateMachinePanel`（write-through 接线）。

**后端 `CP6.WebApi`**（M-E）
- Create `Seed/I18nOaStateMachineScreenSeed.cs` — 五语 ~24 键。
- Modify `Program.cs` — concat 一行（:1819 之后）。

**QA**（M-E）
- Create `docs/superpowers/qa/wfs-smdesigner/{README.md, qa_smdesigner.ps1}` — gstack harness 剧本（只写不跑）。

---

## 共享契约（所有 Task 用这些**精确**名字）

```ts
// cp6.web/src/views/oa/designer/statemachine/stateMachineModel.ts
import type { FlowSchemaDto, SchemaNode, SchemaEdge } from '../designerModel'

export interface SmState {
  no: number; nodeId: string; type: string; name: string;
  approverSummary: string; countersign?: string; raw: SchemaNode
}
export interface SmPath  { fromNo: number; toNo: number; condition?: string; isError?: boolean; raw: SchemaEdge }
export interface SmView  { states: SmState[]; paths: SmPath[]; capability: 'editable' | 'readonly' }

export type Capability = 'editable' | 'readonly'
export function smCapability(schema: FlowSchemaDto): Capability
export function schemaToStateMachine(schema: FlowSchemaDto): SmView
export function stateMachineToSchema(smView: SmView, baseSchema: FlowSchemaDto): FlowSchemaDto
export function approverSummary(node: SchemaNode): string            // 语言中性 token（C3）

export function insertStateAfter(baseSchema: FlowSchemaDto, afterNodeId: string, node: SchemaNode): FlowSchemaDto
export type DeleteStateResult =
  | { ok: true; schema: FlowSchemaDto }
  | { ok: false; reason: 'multiIn' | 'multiOut' | 'protected' }
export function deleteState(baseSchema: FlowSchemaDto, nodeId: string): DeleteStateResult

export interface SmErrorLoc { code: string; nodeId?: string; edgeKey?: string }   // edgeKey = `${from}__${to}`
export function locateValidation(schema: FlowSchemaDto): SmErrorLoc[]
```

- **BFS 编号规则**（`schemaToStateMachine` / 内部 `numberStates`）：`start` 节点 no=0；从 start 沿出边**按 `schema.edges` 声明序** BFS、visited 截断（环下拓扑序不存在，仍确定性）；非 end 已访问节点按首访序赋号；未访问非 end 节点按 nodeId 字典序追加；**所有 end 型节点恒排最末**（按 nodeId 字典序）。编号仅视图内行号，不持久化、不进 schema（nodeId 恒权威）。回跳边表现为 `fromNo > toNo` 的 Path 行。
- **write-through 合成**：`stateMachineToSchema(smView, baseSchema)` 以 baseSchema 为底，按 nodeId/边身份将 `SmState.name`、`SmPath.condition/isError` 覆盖回对应 raw 节点/边，**其余 raw 字段与坐标原样保留**（round-trip 除坐标外语义等价）。结构增删走 `insertStateAfter`/`deleteState`（新节点坐标=前驱与后继中点，既有坐标不动）。
- **端点**：无新端点（保存复用 `designerApi.save`）。
- **localStorage 键**：`oa.designer.mode` ∈ `{'graph','sm'}`，缺省 `'graph'`（每浏览器，D4）。
- **i18n 键前缀**：`oa.designer.sm.*`（模式开关/两表列头/只读横幅/插删操作/摘要 token/抽屉）；行级错误复用既有 `oa.designer.err*`。

### 任务波次（spec §7）：**M-A → M-B → M-C → M-D → M-E**（严格线性依赖）

M-A 纯逻辑先行（无 UI）；M-B 重构解耦（M-C 抽屉依赖）；M-C 表格 UI + 模式切换；M-D 行级校验 + 保存接线；M-E i18n + QA + DoD。

---

## Wave M-A — 投影内核（`stateMachineModel.ts` 纯函数 + 全量 vitest）

### Task M-A-T1: 投影三函数 + 编号/摘要/插删/定位 + round-trip/capability/确定性/缝合 vitest

**Files:**
- Create: `cp6.web/src/views/oa/designer/statemachine/stateMachineModel.ts`
- Test: `cp6.web/src/views/oa/designer/statemachine/stateMachineModel.test.ts`

**Interfaces:**
- Consumes: `FlowSchemaDto`/`SchemaNode`/`SchemaEdge`（`../designerModel`）。
- Produces: 「共享契约」全部签名——M-C/M-D 全依赖。

- [ ] **Step 1: 写失败测试**（全代码，禁骨架）

```ts
// cp6.web/src/views/oa/designer/statemachine/stateMachineModel.test.ts
import { describe, it, expect } from 'vitest'
import {
  schemaToStateMachine, stateMachineToSchema, smCapability, approverSummary,
  insertStateAfter, deleteState, locateValidation,
  type SmView,
} from './stateMachineModel'
import type { FlowSchemaDto, SchemaNode } from '../designerModel'

// ── helpers ───────────────────────────────────────────────────────────────
/** 去坐标 + 排序，做语义深比较（round-trip 不变量：除 x/y 外全等）。 */
function normalize(s: FlowSchemaDto) {
  const nodes = [...s.nodes].map(({ x, y, ...rest }) => rest)
    .sort((a, b) => a.id.localeCompare(b.id))
  const edges = [...s.edges].map(e => ({ ...e }))
    .sort((a, b) => (a.from + a.to).localeCompare(b.from + b.to))
  return { start: s.start, nodes, edges }
}

const linear: FlowSchemaDto = {
  start: 's',
  nodes: [
    { id: 's', type: 'start', name: '填單', x: 0, y: 0 },
    { id: 'a', type: 'approval', name: '課長審', approverStrategy: 'DirectManager', approverLevels: 1, countersign: 'all', x: 0, y: 120 },
    { id: 'b', type: 'approval', name: '經理審', approverStrategy: 'Specified', approverUserId: 'u9', x: 0, y: 240 },
    { id: 'e', type: 'end', name: '結束', x: 0, y: 360 },
  ],
  edges: [
    { from: 's', to: 'a' },
    { from: 'a', to: 'b', condition: 'days>3' },
    { from: 'b', to: 'e' },
  ],
}

// 条件回跳成环（editable：条件边可回跳，二期 hardening §5.1 承认环存在）
const cyclic: FlowSchemaDto = {
  start: 's',
  nodes: [
    { id: 's', type: 'start', x: 0, y: 0 },
    { id: 'a', type: 'approval', name: 'A', approverStrategy: 'Specified', approverUserId: 'u1', x: 0, y: 120 },
    { id: 'b', type: 'approval', name: 'B', approverStrategy: 'Specified', approverUserId: 'u2', x: 0, y: 240 },
    { id: 'e', type: 'end', x: 0, y: 360 },
  ],
  edges: [
    { from: 's', to: 'a' },
    { from: 'a', to: 'b', condition: 'ok' },
    { from: 'b', to: 'a', condition: 'redo' },   // 回跳
    { from: 'b', to: 'e', condition: 'done' },
  ],
}

describe('smCapability', () => {
  it('线性+条件+serviceTask+单实例subFlow → editable', () => {
    expect(smCapability(linear)).toBe('editable')
    const withSvc: FlowSchemaDto = { start: 's', nodes: [
      { id: 's', type: 'start' },
      { id: 'x', type: 'serviceTask', serviceKind: 'webApi', serviceConnectorName: 'c', servicePath: '/p' } as any,
      { id: 'e', type: 'end' },
    ], edges: [{ from: 's', to: 'x' }, { from: 'x', to: 'e' }] }
    expect(smCapability(withSvc)).toBe('editable')
    const singleSub: FlowSchemaDto = { start: 's', nodes: [
      { id: 's', type: 'start' },
      { id: 'sf', type: 'subFlow', subFlowKey: 'PUR', subCollectionVar: '' } as any,   // 空串=单实例
      { id: 'e', type: 'end' },
    ], edges: [{ from: 's', to: 'sf' }, { from: 'sf', to: 'e' }] }
    expect(smCapability(singleSub)).toBe('editable')
  })

  it('并行/inclusive/多实例subFlow → readonly', () => {
    for (const t of ['parallelSplit', 'parallelJoin', 'inclusiveSplit', 'inclusiveJoin']) {
      const s: FlowSchemaDto = { start: 's', nodes: [
        { id: 's', type: 'start' }, { id: 'g', type: t } as any, { id: 'e', type: 'end' },
      ], edges: [{ from: 's', to: 'g' }, { from: 'g', to: 'e' }] }
      expect(smCapability(s)).toBe('readonly')
    }
    const multiSub: FlowSchemaDto = { start: 's', nodes: [
      { id: 's', type: 'start' },
      { id: 'sf', type: 'subFlow', subFlowKey: 'PUR', subCollectionVar: 'items' } as any,   // 非空=多实例
      { id: 'e', type: 'end' },
    ], edges: [{ from: 's', to: 'sf' }, { from: 'sf', to: 'e' }] }
    expect(smCapability(multiSub)).toBe('readonly')
  })
})

describe('schemaToStateMachine — 编号 & 投影', () => {
  it('BFS 编号：start=0，链序递增，end 恒最末', () => {
    const v = schemaToStateMachine(linear)
    const byId = Object.fromEntries(v.states.map(s => [s.nodeId, s.no]))
    expect(byId['s']).toBe(0)
    expect(byId['a']).toBe(1)
    expect(byId['b']).toBe(2)
    expect(byId['e']).toBe(3)                       // end 最末
    expect(v.capability).toBe('editable')
  })

  it('Path 投影：条件/失败边映射，fromNo/toNo 正确', () => {
    const v = schemaToStateMachine(linear)
    const ab = v.paths.find(p => p.raw.from === 'a' && p.raw.to === 'b')!
    expect(ab.fromNo).toBe(1)
    expect(ab.toNo).toBe(2)
    expect(ab.condition).toBe('days>3')
  })

  it('回跳边显示为 fromNo>toNo，且编号确定（两次投影一致）', () => {
    const v1 = schemaToStateMachine(cyclic)
    const v2 = schemaToStateMachine(cyclic)
    expect(v1.states.map(s => `${s.nodeId}:${s.no}`)).toEqual(v2.states.map(s => `${s.nodeId}:${s.no}`))
    const back = v1.paths.find(p => p.raw.from === 'b' && p.raw.to === 'a')!
    expect(back.fromNo).toBeGreaterThan(back.toNo)   // 回跳 = fromNo>toNo
  })

  it('approverSummary 语言中性 token', () => {
    expect(approverSummary(linear.nodes[1]!)).toBe('DirectManager')
    expect(approverSummary(linear.nodes[2]!)).toBe('Specified')
    const staged: SchemaNode = { id: 'x', type: 'approval', stages: [
      { kind: 'fixed', approverStrategy: 'Specified' }, { kind: 'managerChain', maxLevels: 2 },
    ] }
    expect(approverSummary(staged)).toBe('stages:2')
    expect(approverSummary({ id: 'e', type: 'end' })).toBe('')   // 非审批节点无摘要
  })
})

describe('round-trip 语义等价（除坐标）', () => {
  it('线性 approval 全配置', () => {
    const v = schemaToStateMachine(linear)
    const back = stateMachineToSchema(v, linear)
    expect(normalize(back)).toEqual(normalize(linear))
  })

  it('回跳边环 schema 投影稳定（round-trip 不丢环）', () => {
    const v = schemaToStateMachine(cyclic)
    const back = stateMachineToSchema(v, cyclic)
    expect(normalize(back)).toEqual(normalize(cyclic))
  })

  it('serviceTask 三 kind 全字段保真', () => {
    const svc: FlowSchemaDto = { start: 's', nodes: [
      { id: 's', type: 'start' },
      { id: 'w', type: 'serviceTask', serviceKind: 'webApi', serviceMode: 'async', serviceConnectorName: 'erp', servicePath: '/o', serviceParamsJson: '{}', serviceMaxRetries: 3 } as any,
      { id: 'd', type: 'serviceTask', serviceKind: 'dataWriteback', serviceActionName: 'writeBack', serviceMode: 'sync' } as any,
      { id: 't', type: 'serviceTask', serviceKind: 'timer', serviceDelayMode: 'duration', serviceDelayValue: '3d' } as any,
      { id: 'e', type: 'end' },
    ], edges: [
      { from: 's', to: 'w' }, { from: 'w', to: 'd', isError: true }, { from: 'd', to: 't' }, { from: 't', to: 'e' },
    ] }
    const back = stateMachineToSchema(schemaToStateMachine(svc), svc)
    expect(normalize(back)).toEqual(normalize(svc))
  })

  it('单实例 subFlow + 错误边保真', () => {
    const sf: FlowSchemaDto = { start: 's', nodes: [
      { id: 's', type: 'start' },
      { id: 'sf', type: 'subFlow', subFlowKey: 'PUR', subVarsInJson: '{"a":"$.b"}', subCompletionPolicy: 'all' } as any,
      { id: 'e', type: 'end' }, { id: 'x', type: 'end' },
    ], edges: [{ from: 's', to: 'sf' }, { from: 'sf', to: 'e' }, { from: 'sf', to: 'x', isError: true }] }
    const back = stateMachineToSchema(schemaToStateMachine(sf), sf)
    expect(normalize(back)).toEqual(normalize(sf))
  })

  it('write-through 改名+改条件后 round-trip 落回 baseSchema，坐标不动', () => {
    const v: SmView = schemaToStateMachine(linear)
    v.states.find(s => s.nodeId === 'a')!.name = '課長複審'          // 表格改名
    v.paths.find(p => p.raw.from === 'a' && p.raw.to === 'b')!.condition = 'days>5'   // Path 改条件
    const back = stateMachineToSchema(v, linear)
    expect(back.nodes.find(n => n.id === 'a')!.name).toBe('課長複審')
    expect(back.nodes.find(n => n.id === 'a')!.x).toBe(0)            // 坐标保留
    expect(back.edges.find(e => e.from === 'a' && e.to === 'b')!.condition).toBe('days>5')
    // 其余字段不动
    expect(back.nodes.find(n => n.id === 'a')!.approverStrategy).toBe('DirectManager')
  })
})

describe('insertStateAfter — 自动接链', () => {
  it('在 a 后插入新状态：a→新→b（原 a→b 断，接缝正确）', () => {
    const nw: SchemaNode = { id: 'n1', type: 'approval', name: '加簽', approverStrategy: 'Specified', approverUserId: 'u3' }
    const out = insertStateAfter(linear, 'a', nw)
    expect(out.nodes.some(n => n.id === 'n1')).toBe(true)
    expect(out.edges.some(e => e.from === 'a' && e.to === 'n1')).toBe(true)
    expect(out.edges.some(e => e.from === 'n1' && e.to === 'b')).toBe(true)
    expect(out.edges.some(e => e.from === 'a' && e.to === 'b')).toBe(false)   // 原直连断开
    // 新节点坐标 = a 与 b 中点
    const na = out.nodes.find(n => n.id === 'a')!, nb = out.nodes.find(n => n.id === 'b')!, n1 = out.nodes.find(n => n.id === 'n1')!
    expect(n1.y).toBe(((na.y ?? 0) + (nb.y ?? 0)) / 2)
  })
})

describe('deleteState — 缝合 & 多入出拦截', () => {
  it('单入单出删除：前后自动缝合', () => {
    const r = deleteState(linear, 'a')
    expect(r.ok).toBe(true)
    if (r.ok) {
      expect(r.schema.nodes.some(n => n.id === 'a')).toBe(false)
      expect(r.schema.edges.some(e => e.from === 's' && e.to === 'b')).toBe(true)   // s→b 缝合
      expect(r.schema.edges.some(e => e.from === 's' && e.to === 'a')).toBe(false)
      expect(r.schema.edges.some(e => e.from === 'a')).toBe(false)
    }
  })

  it('多入删除被拦（reason=multiIn）', () => {
    const s: FlowSchemaDto = { start: 'st', nodes: [
      { id: 'st', type: 'start' }, { id: 'p', type: 'approval', approverStrategy: 'Specified' },
      { id: 'q', type: 'approval', approverStrategy: 'Specified' }, { id: 'e', type: 'end' },
    ], edges: [{ from: 'st', to: 'p' }, { from: 'st', to: 'q' }, { from: 'p', to: 'e' }, { from: 'q', to: 'e' }] }
    const r = deleteState(s, 'e')
    expect(r).toEqual({ ok: false, reason: 'multiIn' })
  })

  it('多出删除被拦（reason=multiOut）', () => {
    const r = deleteState({ ...cyclic }, 'b')   // b 有 b→a / b→e 两出边
    expect(r).toEqual({ ok: false, reason: 'multiOut' })
  })

  it('删 start/end 保护（reason=protected）', () => {
    expect(deleteState(linear, 's')).toEqual({ ok: false, reason: 'protected' })
  })
})

describe('locateValidation — 错误码→行归属（C4）', () => {
  it('无策略 approval 归到 nodeId', () => {
    const s: FlowSchemaDto = { start: 's', nodes: [
      { id: 's', type: 'start' }, { id: 'a', type: 'approval' /* 缺策略 */ }, { id: 'e', type: 'end' },
    ], edges: [{ from: 's', to: 'a' }, { from: 'a', to: 'e' }] }
    const locs = locateValidation(s)
    expect(locs.some(l => l.code === 'oa.designer.errNoStrategy' && l.nodeId === 'a')).toBe(true)
  })

  it('悬挂边归到 edgeKey', () => {
    const s: FlowSchemaDto = { start: 's', nodes: [
      { id: 's', type: 'start' }, { id: 'a', type: 'approval', approverStrategy: 'Specified' }, { id: 'e', type: 'end' },
    ], edges: [{ from: 's', to: 'a' }, { from: 'a', to: 'e' }, { from: 'a', to: 'zzz' }] }
    const locs = locateValidation(s)
    expect(locs.some(l => l.code === 'oa.designer.errDanglingEdge' && l.edgeKey === 'a__zzz')).toBe(true)
  })

  it('serviceTask 配置不全归到 nodeId', () => {
    const s: FlowSchemaDto = { start: 's', nodes: [
      { id: 's', type: 'start' }, { id: 'w', type: 'serviceTask', serviceKind: 'webApi' } as any, { id: 'e', type: 'end' },
    ], edges: [{ from: 's', to: 'w' }, { from: 'w', to: 'e' }] }
    const locs = locateValidation(s)
    expect(locs.some(l => l.code === 'oa.designer.errServiceConfig' && l.nodeId === 'w')).toBe(true)
  })

  it('无错误 schema → 空定位', () => {
    expect(locateValidation(linear)).toEqual([])
  })
})
```

- [ ] **Step 2: 跑测试验证 FAIL** — `cd cp6.web && npx vitest run src/views/oa/designer/statemachine/stateMachineModel.test.ts`。预期：模块不存在，编译失败。

- [ ] **Step 3: 最小实现**

```ts
// cp6.web/src/views/oa/designer/statemachine/stateMachineModel.ts
import type { FlowSchemaDto, SchemaNode, SchemaEdge } from '../designerModel'

export interface SmState {
  no: number; nodeId: string; type: string; name: string;
  approverSummary: string; countersign?: string; raw: SchemaNode
}
export interface SmPath { fromNo: number; toNo: number; condition?: string; isError?: boolean; raw: SchemaEdge }
export interface SmView { states: SmState[]; paths: SmPath[]; capability: Capability }
export type Capability = 'editable' | 'readonly'

const READONLY_NODE_TYPES = new Set(['parallelSplit', 'parallelJoin', 'inclusiveSplit', 'inclusiveJoin'])
const END_TYPE = 'end'
const PROTECTED_TYPES = new Set(['start', 'end'])

/** D2/§2.2：含并行/inclusive 网关，或多实例 subFlow（subCollectionVar 非空）→ readonly。 */
export function smCapability(schema: FlowSchemaDto): Capability {
  const nodes = schema.nodes ?? []
  if (nodes.some(n => READONLY_NODE_TYPES.has(n.type))) return 'readonly'
  if (nodes.some(n => n.type === 'subFlow' && !!((n as any).subCollectionVar ?? '').trim())) return 'readonly'
  return 'editable'
}

/** 语言中性审批摘要 token（C3）：串簽优先 'stages:N'；单档=策略名；非审批/未配=''。 */
export function approverSummary(node: SchemaNode): string {
  if (node.type !== 'approval') return ''
  if (node.stages && node.stages.length) return `stages:${node.stages.length}`
  return node.approverStrategy ?? ''
}

/** start 起 BFS + visited 截断、出边声明序、end 恒最末——确定性行号（不持久化）。 */
function numberStates(schema: FlowSchemaDto): Map<string, number> {
  const nodes = schema.nodes ?? [], edges = schema.edges ?? []
  const startId = schema.start ?? nodes.find(n => n.type === 'start')?.id
  const nonEnd = nodes.filter(n => n.type !== END_TYPE)
  const order: string[] = []
  const visited = new Set<string>()
  if (startId && nonEnd.some(n => n.id === startId)) {
    const queue = [startId]
    visited.add(startId)
    while (queue.length) {
      const cur = queue.shift()!
      order.push(cur)
      for (const e of edges) {                       // schema.edges 声明序
        if (e.from !== cur) continue
        const tgt = nodes.find(n => n.id === e.to)
        if (!tgt || tgt.type === END_TYPE) continue  // end 不参与 BFS 主体
        if (!visited.has(e.to)) { visited.add(e.to); queue.push(e.to) }
      }
    }
  }
  // 未访问非 end 节点：nodeId 字典序追加（确定性）
  for (const n of nonEnd.map(n => n.id).sort()) if (!visited.has(n)) { visited.add(n); order.push(n) }
  // end 恒最末（nodeId 字典序）
  for (const n of nodes.filter(n => n.type === END_TYPE).map(n => n.id).sort()) order.push(n)
  return new Map(order.map((id, i) => [id, i]))
}

export function schemaToStateMachine(schema: FlowSchemaDto): SmView {
  const noMap = numberStates(schema)
  const states: SmState[] = (schema.nodes ?? [])
    .map(n => ({
      no: noMap.get(n.id) ?? -1, nodeId: n.id, type: n.type, name: n.name ?? n.id,
      approverSummary: approverSummary(n), countersign: n.countersign, raw: n,
    }))
    .sort((a, b) => a.no - b.no)
  const paths: SmPath[] = (schema.edges ?? []).map(e => ({
    fromNo: noMap.get(e.from) ?? -1, toNo: noMap.get(e.to) ?? -1,
    condition: e.condition, isError: e.isError, raw: e,
  }))
  return { states, paths, capability: smCapability(schema) }
}

/** SmView → schema：以 baseSchema 为底，name/condition/isError 覆盖回 raw；余字段+坐标保留（round-trip 除坐标等价）。 */
export function stateMachineToSchema(smView: SmView, baseSchema: FlowSchemaDto): FlowSchemaDto {
  const nameByNode = new Map(smView.states.map(s => [s.nodeId, s.name]))
  const pathByKey = new Map(smView.paths.map(p => [`${p.raw.from}__${p.raw.to}`, p]))
  const nodes: SchemaNode[] = (baseSchema.nodes ?? []).map(n => {
    const nm = nameByNode.get(n.id)
    return nm !== undefined && nm !== n.name ? { ...n, name: nm } : n
  })
  const edges: SchemaEdge[] = (baseSchema.edges ?? []).map(e => {
    const p = pathByKey.get(`${e.from}__${e.to}`)
    if (!p) return e
    return { ...e, condition: p.condition || undefined, isError: p.isError || undefined }
  })
  return { start: baseSchema.start, nodes, edges }
}

/** 选中行后插入：afterNode 单出边时接链 after→new→原后继；否则接 after→new（无后继时）。坐标=中点。 */
export function insertStateAfter(baseSchema: FlowSchemaDto, afterNodeId: string, node: SchemaNode): FlowSchemaDto {
  const nodes = [...(baseSchema.nodes ?? [])]
  const edges = [...(baseSchema.edges ?? [])]
  const after = nodes.find(n => n.id === afterNodeId)
  const outIdx = edges.findIndex(e => e.from === afterNodeId)
  let x = after?.x, y = after?.y
  if (outIdx >= 0) {
    const succ = nodes.find(n => n.id === edges[outIdx]!.to)
    if (after && succ) { x = ((after.x ?? 0) + (succ.x ?? 0)) / 2; y = ((after.y ?? 0) + (succ.y ?? 0)) / 2 }
    const succTo = edges[outIdx]!.to
    edges.splice(outIdx, 1, { from: afterNodeId, to: node.id }, { from: node.id, to: succTo })
  } else {
    edges.push({ from: afterNodeId, to: node.id })
  }
  nodes.push({ ...node, x, y })
  return { start: baseSchema.start, nodes, edges }
}

export type DeleteStateResult =
  | { ok: true; schema: FlowSchemaDto }
  | { ok: false; reason: 'multiIn' | 'multiOut' | 'protected' }

/** 单入单出删除自动缝合前后边；多入/多出/删 start·end 拦截。 */
export function deleteState(baseSchema: FlowSchemaDto, nodeId: string): DeleteStateResult {
  const node = (baseSchema.nodes ?? []).find(n => n.id === nodeId)
  if (!node || PROTECTED_TYPES.has(node.type)) return { ok: false, reason: 'protected' }
  const ins = (baseSchema.edges ?? []).filter(e => e.to === nodeId)
  const outs = (baseSchema.edges ?? []).filter(e => e.from === nodeId)
  if (ins.length > 1) return { ok: false, reason: 'multiIn' }
  if (outs.length > 1) return { ok: false, reason: 'multiOut' }
  const nodes = (baseSchema.nodes ?? []).filter(n => n.id !== nodeId)
  let edges = (baseSchema.edges ?? []).filter(e => e.from !== nodeId && e.to !== nodeId)
  if (ins.length === 1 && outs.length === 1) {          // 缝合 pred→succ（保留入边条件/失败标记）
    edges = [...edges, { ...ins[0]!, from: ins[0]!.from, to: outs[0]!.to }]
  }
  return { ok: true, schema: { start: baseSchema.start, nodes, edges } }
}

export interface SmErrorLoc { code: string; nodeId?: string; edgeKey?: string }

/** validateClient 同规则 + 行归属（C4；不改 validateClient 本体，保存仍走原函数）。 */
export function locateValidation(schema: FlowSchemaDto): SmErrorLoc[] {
  const out: SmErrorLoc[] = []
  const nodes = schema.nodes ?? [], edges = schema.edges ?? []
  const ids = new Set(nodes.map(n => n.id))
  for (const e of edges) {
    if (!ids.has(e.from) || !ids.has(e.to)) out.push({ code: 'oa.designer.errDanglingEdge', edgeKey: `${e.from}__${e.to}` })
  }
  for (const n of nodes) {
    if (n.type === 'approval' && !n.approverStrategy && !(n.stages?.length))
      out.push({ code: 'oa.designer.errNoStrategy', nodeId: n.id })
    if (n.type === 'approval' && n.stages?.length) {
      for (const s of n.stages) {
        const ruleOk = s.kind === 'managerChain' ? (s.maxLevels ?? 0) >= 1 : !!s.approverStrategy
        const csOk = !s.countersign || ['all', 'any', 'veto'].includes(s.countersign)
        if (!ruleOk || !csOk) { out.push({ code: 'oa.designer.errStageInvalid', nodeId: n.id }); break }
      }
    }
    if (n.type === 'approval') {
      if (n.approverStrategy === 'FormField' && !n.approverFieldName) out.push({ code: 'oa.designer.errApproverConfig', nodeId: n.id })
      if (n.approverStrategy === 'DataMap' && (!n.approverMapKey || !n.approverFieldName)) out.push({ code: 'oa.designer.errApproverConfig', nodeId: n.id })
      if (n.approverStrategy === 'Group' && !(n.approverMembers?.length)) out.push({ code: 'oa.designer.errApproverConfig', nodeId: n.id })
    }
    if (n.type === 'serviceTask') {
      const anyN = n as any
      const ok = anyN.serviceKind === 'webApi'
        ? !!anyN.serviceConnectorName && !!anyN.servicePath
        : anyN.serviceKind === 'dataWriteback' ? !!anyN.serviceActionName
        : anyN.serviceKind === 'timer' ? anyN.serviceDelayValue != null && anyN.serviceDelayMode != null
        : false
      if (!ok) out.push({ code: 'oa.designer.errServiceConfig', nodeId: n.id })
    }
  }
  return out
}
```

- [ ] **Step 4: 跑测试验证 PASS** — `npx vitest run src/views/oa/designer/statemachine/stateMachineModel.test.ts`。

- [ ] **Step 5: 全量回归 + type-check + commit**

```bash
cd cp6.web && npm run test && NODE_OPTIONS=--max-old-space-size=8192 npm run type-check
git add -A && git commit -m "feat(wfs-smdesigner): M-A 投影内核 schemaToStateMachine/stateMachineToSchema/smCapability + 编号/插删/行级定位 + 全量 vitest"
```

---

## Wave M-B — 配置段抽取重构（`ApprovalConfigSection.vue`，NodePropertyPanel 行为零变化）

### Task M-B-T1: 抽 approval 配置段为 `ApprovalConfigSection.vue`（part 三态）+ NodePropertyPanel 接线 + 行为锁定 vitest

**Files:**
- Create: `cp6.web/src/views/oa/designer/ApprovalConfigSection.vue`
- Create: `cp6.web/src/views/oa/designer/ApprovalConfigSection.spec.ts`
- Modify: `cp6.web/src/views/oa/designer/NodePropertyPanel.vue`
- Create: `cp6.web/src/views/oa/designer/NodePropertyPanel.spec.ts`（行为锁定，若无则建）

**Interfaces:**
- Produces: `ApprovalConfigSection`（`defineProps<{ node: SchemaNode; part: 'approver' | 'stages' | 'advanced' }>()`，深-mutate `node`）——M-C StateEditDrawer 复用。
- NodePropertyPanel emit 契约（`update: [patch: Partial<SchemaNode>]`）与 `local` 深-watch 单一 emit **不变**（byte-identical）。

- [ ] **Step 1: 写行为锁定测试**（先锁现状，重构后仍绿）

```ts
// cp6.web/src/views/oa/designer/NodePropertyPanel.spec.ts
// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import NodePropertyPanel from './NodePropertyPanel.vue'
import type { SchemaNode } from './designerModel'

// 桩掉远程 API（组件 onFocus/remote 才触发，挂载期不发；防御式 mock）
vi.mock('@/api/sys/user', () => ({ userApi: { getList: vi.fn().mockResolvedValue({ rows: [] }) } }))
vi.mock('@/api/sys/role', () => ({ roleApi: { getAll: vi.fn().mockResolvedValue([]) } }))
vi.mock('@/api/oa/designer', () => ({ designerApi: { getServiceCatalog: vi.fn().mockResolvedValue({ actions: [], connectors: [] }) } }))

const i18n = createI18n({ legacy: false, locale: 'zh-CN', missingWarn: false, fallbackWarn: false, messages: { 'zh-CN': {} } })

function mountPanel(node: SchemaNode) {
  return mount(NodePropertyPanel, {
    props: { node },
    global: { plugins: [i18n], stubs: { CpTag: true } },
  })
}

describe('NodePropertyPanel 行为锁定（M-B 重构前后一致）', () => {
  it('改 name emit 单次 update，patch 含新 name + id 保留', async () => {
    const node: SchemaNode = { id: 'a', type: 'approval', name: 'X', approverStrategy: 'Specified' }
    const w = mountPanel(node)
    await flushPromises()
    const input = w.find('input')
    await input.setValue('Y')
    await flushPromises()
    const updates = w.emitted('update') as any[][] | undefined
    expect(updates).toBeTruthy()
    const last = updates![updates!.length - 1]![0]
    expect(last.name).toBe('Y')
    expect(last.id).toBe('a')                    // id 恒保留
    expect(Array.isArray(last.ccUsers)).toBe(true)   // emit 形状：ccUsers 展开为数组
  })

  it('approval 节点渲染审批人策略下拉（抽取后仍在）', async () => {
    const w = mountPanel({ id: 'a', type: 'approval', approverStrategy: 'Specified' })
    await flushPromises()
    expect(w.html()).toContain('el-select')      // 审批人配置段存在
  })

  it('非 approval 节点不渲染审批段（end）', async () => {
    const w = mountPanel({ id: 'e', type: 'end', name: '结束' })
    await flushPromises()
    // end 无 approverStrategy 相关；串簽段仅 isApproval 显示——html 不含档位启用文案键
    expect(w.vm).toBeTruthy()
  })
})
```

```ts
// cp6.web/src/views/oa/designer/ApprovalConfigSection.spec.ts
// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ApprovalConfigSection from './ApprovalConfigSection.vue'
import type { SchemaNode } from './designerModel'

vi.mock('@/api/sys/user', () => ({ userApi: { getList: vi.fn().mockResolvedValue({ rows: [] }) } }))
vi.mock('@/api/sys/role', () => ({ roleApi: { getAll: vi.fn().mockResolvedValue([]) } }))

const i18n = createI18n({ legacy: false, locale: 'zh-CN', missingWarn: false, fallbackWarn: false, messages: { 'zh-CN': {} } })
const mountS = (node: SchemaNode, part: 'approver' | 'stages' | 'advanced') =>
  mount(ApprovalConfigSection, { props: { node, part }, global: { plugins: [i18n] } })

describe('ApprovalConfigSection', () => {
  it('part=approver 渲染策略下拉', async () => {
    const w = mountS({ id: 'a', type: 'approval', approverStrategy: 'Specified' }, 'approver')
    await flushPromises()
    expect(w.html()).toContain('el-select')
  })

  it('part=stages 启用串簽后可加档（深-mutate node.stages 生效）', async () => {
    const node: SchemaNode = { id: 'a', type: 'approval' }
    const w = mountS(node, 'stages')
    await flushPromises()
    // 触发启用开关
    const sw = w.find('.el-switch')
    await sw.trigger('click')
    await flushPromises()
    expect(Array.isArray(node.stages)).toBe(true)   // 共享引用被 mutate
    expect(node.stages!.length).toBeGreaterThanOrEqual(1)
  })

  it('part=advanced 渲染 approverWhen/Filter 文本域', async () => {
    const w = mountS({ id: 'a', type: 'approval', approverWhen: 'x>1' }, 'advanced')
    await flushPromises()
    expect(w.findAll('textarea').length).toBeGreaterThanOrEqual(1)
  })
})
```

- [ ] **Step 2: 跑测试验证** — `npx vitest run src/views/oa/designer/NodePropertyPanel.spec.ts`：现状（未重构）应绿（锁定基线）。`ApprovalConfigSection.spec.ts`：FAIL（组件不存在）。

- [ ] **Step 3: 建 `ApprovalConfigSection.vue`** — 从 NodePropertyPanel 迁移三片段 + 共享脚本。`defineProps<{ node: SchemaNode; part: 'approver' | 'stages' | 'advanced' }>()`；组件内 `const node = props.node`（深-mutate，因与父 `local` 同引用，父 deep watch 触发单一 emit）。迁入脚本：`searchUsers`/`userOptions`/`userSearchLoading`、`loadRoles`/`roleOptions`/`roleLoading`、`stageEnabled`/`toggleStages`/`addStage`/`removeStage`/`moveStageUp`/`moveStageDown`/`addMember`/`removeMember`。模板按 `part` 用 `<template v-if="part==='approver'">`（迁 NodePropertyPanel :231-353 的 `isApproval && !stageEnabled` 单档块，含 Group 成员/countersign）/ `part==='stages'`（迁 :509-679 档位列表 + 加档，本片段自带内容，**不含 el-collapse-item 外壳**——外壳留 NodePropertyPanel）/ `part==='advanced'`（迁 :711-716 approverWhen/Filter）。**所有 `t()` 键、`el-*` 属性、样式类逐字保留**。

> 注：`stages` 片段的 `<el-collapse-item name="stages">` 外壳留在 NodePropertyPanel（保 collapse 分组 byte-identical），`<ApprovalConfigSection part="stages">` 只渲染 item **内部** body。同理 `approver` 片段嵌 `basic` item 内、`advanced` 片段嵌 `advanced` item 内。

- [ ] **Step 4: 改 `NodePropertyPanel.vue`** — ① 删迁走的脚本（searchUsers/loadRoles/stage/member helpers + userOptions/roleOptions 等），保留 `local`/`cloneNode`/`watch(local)` 单一 emit + serviceKind 清理 watch + timeoutDays + isApproval/isServiceTask/catalog + searchCcUsers（CC 段留用）。② import `ApprovalConfigSection`。③ 模板三处原位替换：basic item 内 `v-if="isApproval && !stageEnabled"` 块 → `<ApprovalConfigSection v-if="isApproval && !stageEnabled" :node="local" part="approver" />`；stages item body → `<ApprovalConfigSection v-if="isApproval" :node="local" part="stages" />`（item 外壳留）；advanced item 内 approverWhen/Filter → `<ApprovalConfigSection v-if="isApproval" :node="local" part="advanced" />`。`stageEnabled` NodePropertyPanel 仍需（控制 approver 片段 v-if）——保留该 computed（或从子组件同源计算，二者一致）。

> `stageEnabled` 双持谨慎：NodePropertyPanel 用它决定单档 approver 片段是否显示（`!stageEnabled`），子组件 stages 片段用它决定档列表——两者读同一 `local.stages`，值恒一致，无冲突。

- [ ] **Step 5: 跑测试验证 PASS** — `npx vitest run src/views/oa/designer/NodePropertyPanel.spec.ts src/views/oa/designer/ApprovalConfigSection.spec.ts` 全绿（行为锁定 + 新组件）。

- [ ] **Step 6: 全量回归 + type-check + commit**

```bash
cd cp6.web && npm run test && NODE_OPTIONS=--max-old-space-size=8192 npm run type-check
git add -A && git commit -m "feat(wfs-smdesigner): M-B 抽 ApprovalConfigSection(part三态) NodePropertyPanel 行为零变化锁定"
```

---

## Wave M-C — 表格 UI（StateTable/PathTable/StateEditDrawer/StateMachinePanel + 模式切换 + 只读降级）

### Task M-C-T1: StateMachinePanel + 两表 + 抽屉 + write-through（组件 vitest）

**Files:**
- Create: `cp6.web/src/views/oa/designer/statemachine/StateTable.vue`
- Create: `cp6.web/src/views/oa/designer/statemachine/PathTable.vue`
- Create: `cp6.web/src/views/oa/designer/statemachine/StateEditDrawer.vue`
- Create: `cp6.web/src/views/oa/designer/statemachine/StateMachinePanel.vue`
- Create: `cp6.web/src/views/oa/designer/statemachine/StateMachinePanel.spec.ts`
- Create: `cp6.web/src/views/oa/designer/statemachine/StateTable.spec.ts`

**Interfaces:**
- Consumes: M-A 全函数、M-B `ApprovalConfigSection`。
- `StateMachinePanel`：`const schema = defineModel<FlowSchemaDto>({ required: true })`（write-through：每次编辑 → 合成新 schema → `schema.value = ...`）。emit 无（v-model 双向）。
- `StateTable`：`defineProps<{ states: SmState[]; capability: Capability; errorNodeIds: Set<string> }>()` + `defineEmits<{ rename: [nodeId, name]; edit: [nodeId]; insertAfter: [nodeId]; remove: [nodeId] }>()`。
- `PathTable`：`defineProps<{ paths: SmPath[]; states: SmState[]; capability: Capability; errorEdgeKeys: Set<string> }>()` + `defineEmits<{ setCondition: [edgeKey, cond]; toggleError: [edgeKey, val]; addPath: [fromNo, toNo]; removePath: [edgeKey] }>()`。
- `StateEditDrawer`：`defineProps<{ node: SchemaNode | null }>()` + `defineEmits<{ apply: [patch]; close: [] }>()`。

- [ ] **Step 1: 写失败测试**

```ts
// cp6.web/src/views/oa/designer/statemachine/StateMachinePanel.spec.ts
// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import StateMachinePanel from './StateMachinePanel.vue'
import type { FlowSchemaDto } from '../designerModel'

vi.mock('@/api/sys/user', () => ({ userApi: { getList: vi.fn().mockResolvedValue({ rows: [] }) } }))
vi.mock('@/api/sys/role', () => ({ roleApi: { getAll: vi.fn().mockResolvedValue([]) } }))
vi.mock('@/api/oa/designer', () => ({ designerApi: { getServiceCatalog: vi.fn().mockResolvedValue({ actions: [], connectors: [] }) } }))

const i18n = createI18n({ legacy: false, locale: 'zh-CN', missingWarn: false, fallbackWarn: false, messages: { 'zh-CN': {} } })

const linear: FlowSchemaDto = {
  start: 's', nodes: [
    { id: 's', type: 'start', name: '填單', x: 0, y: 0 },
    { id: 'a', type: 'approval', name: '審', approverStrategy: 'Specified', x: 0, y: 120 },
    { id: 'e', type: 'end', name: '結束', x: 0, y: 240 },
  ], edges: [{ from: 's', to: 'a' }, { from: 'a', to: 'e', condition: 'ok' }],
}
const parallel: FlowSchemaDto = {
  start: 's', nodes: [
    { id: 's', type: 'start' }, { id: 'g', type: 'parallelSplit' }, { id: 'e', type: 'end' },
  ], edges: [{ from: 's', to: 'g' }, { from: 'g', to: 'e' }],
}

function mountPanel(schema: FlowSchemaDto) {
  return mount(StateMachinePanel, {
    props: { modelValue: schema, 'onUpdate:modelValue': (v: FlowSchemaDto) => (schema = v) },
    global: { plugins: [i18n] },
  })
}

describe('StateMachinePanel', () => {
  it('editable：渲染 State 表 + Path 表，无只读横幅', async () => {
    const w = mountPanel(linear)
    await flushPromises()
    expect(w.findComponent({ name: 'StateTable' }).exists()).toBe(true)
    expect(w.findComponent({ name: 'PathTable' }).exists()).toBe(true)
    expect(w.find('.el-alert').exists()).toBe(false)   // editable 无横幅
  })

  it('readonly（并行 schema）：渲染只读横幅 + 表格禁编辑', async () => {
    const w = mountPanel(parallel)
    await flushPromises()
    expect(w.find('.el-alert').exists()).toBe(true)    // 降级横幅
    expect(w.findComponent({ name: 'StateTable' }).props('capability')).toBe('readonly')
  })

  it('write-through：改名 emit update:modelValue 落回新 schema，未改坐标', async () => {
    let captured: FlowSchemaDto | null = null
    const w = mount(StateMachinePanel, {
      props: { modelValue: linear, 'onUpdate:modelValue': (v: FlowSchemaDto) => (captured = v) },
      global: { plugins: [i18n] },
    })
    await flushPromises()
    w.findComponent({ name: 'StateTable' }).vm.$emit('rename', 'a', '複審')
    await flushPromises()
    expect(captured).toBeTruthy()
    expect(captured!.nodes.find(n => n.id === 'a')!.name).toBe('複審')
    expect(captured!.nodes.find(n => n.id === 'a')!.x).toBe(0)     // 坐标保留
  })

  it('write-through：Path 改条件落回 schema 边', async () => {
    let captured: FlowSchemaDto | null = null
    const w = mount(StateMachinePanel, {
      props: { modelValue: linear, 'onUpdate:modelValue': (v: FlowSchemaDto) => (captured = v) },
      global: { plugins: [i18n] },
    })
    await flushPromises()
    w.findComponent({ name: 'PathTable' }).vm.$emit('setCondition', 'a__e', 'days>5')
    await flushPromises()
    expect(captured!.edges.find(e => e.from === 'a' && e.to === 'e')!.condition).toBe('days>5')
  })

  it('write-through：插入状态后 s→新→a 接链', async () => {
    let captured: FlowSchemaDto | null = null
    const w = mount(StateMachinePanel, {
      props: { modelValue: linear, 'onUpdate:modelValue': (v: FlowSchemaDto) => (captured = v) },
      global: { plugins: [i18n] },
    })
    await flushPromises()
    w.findComponent({ name: 'StateTable' }).vm.$emit('insertAfter', 's')
    await flushPromises()
    expect(captured!.nodes.length).toBe(4)                      // 多一个状态
    expect(captured!.edges.some(e => e.from === 's' && e.to === 'a')).toBe(false)  // 原直连断
  })

  it('write-through：删多入出被拦（不改 schema，示警）', async () => {
    let captured: FlowSchemaDto | null = null
    const multi: FlowSchemaDto = { start: 's', nodes: [
      { id: 's', type: 'start' }, { id: 'p', type: 'approval', approverStrategy: 'Specified' },
      { id: 'q', type: 'approval', approverStrategy: 'Specified' }, { id: 'e', type: 'end' },
    ], edges: [{ from: 's', to: 'p' }, { from: 's', to: 'q' }, { from: 'p', to: 'e' }, { from: 'q', to: 'e' }] }
    const w = mount(StateMachinePanel, {
      props: { modelValue: multi, 'onUpdate:modelValue': (v: FlowSchemaDto) => (captured = v) },
      global: { plugins: [i18n] },
    })
    await flushPromises()
    w.findComponent({ name: 'StateTable' }).vm.$emit('remove', 'e')   // e 多入
    await flushPromises()
    expect(captured).toBeNull()   // 拦截：未 emit 新 schema
  })
})
```

```ts
// cp6.web/src/views/oa/designer/statemachine/StateTable.spec.ts
// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import StateTable from './StateTable.vue'
import type { SmState } from './stateMachineModel'

const i18n = createI18n({ legacy: false, locale: 'zh-CN', missingWarn: false, fallbackWarn: false, messages: { 'zh-CN': {} } })
const states: SmState[] = [
  { no: 0, nodeId: 's', type: 'start', name: '填單', approverSummary: '', raw: { id: 's', type: 'start' } },
  { no: 1, nodeId: 'a', type: 'approval', name: '審', approverSummary: 'Specified', countersign: 'all', raw: { id: 'a', type: 'approval' } },
  { no: 2, nodeId: 'e', type: 'end', name: '結束', approverSummary: '', raw: { id: 'e', type: 'end' } },
]

describe('StateTable', () => {
  it('渲染每状态一行（编号列）', () => {
    const w = mount(StateTable, { props: { states, capability: 'editable', errorNodeIds: new Set() }, global: { plugins: [i18n] } })
    expect(w.findAll('.el-table__row').length).toBe(3)
  })

  it('错误行加高亮 class', () => {
    const w = mount(StateTable, { props: { states, capability: 'editable', errorNodeIds: new Set(['a']) }, global: { plugins: [i18n] } })
    // el-table row-class-name 注入 sm-row-error（详见实现）
    expect(w.html()).toContain('sm-row-error')
  })

  it('readonly 隐藏操作列按钮', () => {
    const w = mount(StateTable, { props: { states, capability: 'readonly', errorNodeIds: new Set() }, global: { plugins: [i18n] } })
    expect(w.html()).not.toContain('sm-op-btn')   // 操作按钮仅 editable 渲染
  })
})
```

- [ ] **Step 2: 跑测试验证 FAIL** — `npx vitest run src/views/oa/designer/statemachine/`。预期组件不存在。

- [ ] **Step 3: 实现四组件**（要点，全模板走 `t()` + `--cp-*` token）

`StateTable.vue`：`el-table :data="states"` + `:row-class-name` 返回 `errorNodeIds.has(row.nodeId) ? 'sm-row-error' : ''`；列=编号(`no`)/名称(可行内 `el-input`，`@change` emit `rename`)/类型(`CpTag tone=muted`)/审批摘要(`t('oa.designer.sm.summary.'+approverSummary前缀)` 映射，token 为空则空)/会签(`countersign` → `t('oa.designer.countersign.'+countersign)`)/操作(`v-if="capability==='editable'"` 三 `el-button.sm-op-btn`：编辑→emit `edit`、插入→emit `insertAfter`、删除→emit `remove`)。

`PathTable.vue`：`el-table :data="paths"`；`:row-class-name` 用 `errorEdgeKeys`；列=从(`fromNo` + 状态名)/到(`toNo` + 状态名)/条件(行内 `el-input`，`@change` emit `setCondition(edgeKey,val)`)/失败边(`el-checkbox` `@change` emit `toggleError`)/操作(删边 emit `removePath`)；顶部「新增转移」按钮（from/to 选择器→emit `addPath`）。edgeKey=`${raw.from}__${raw.to}`。

`StateEditDrawer.vue`：`el-drawer :model-value="!!node"`；内嵌 `<el-collapse>` + `<ApprovalConfigSection :node="draft" part="approver"/>`/`part="stages"`/`part="advanced"`（`draft` = `node` 深拷贝；`@apply` 时 emit `apply(draft)`）+ 通用 name 输入。仅 approval 节点显示 approval 片段（C5：serviceTask/subFlow 深配置引导切图形，抽屉给提示文案 `oa.designer.sm.editInGraphHint`）。

`StateMachinePanel.vue`（write-through 核心）：
```ts
const schema = defineModel<FlowSchemaDto>({ required: true })
const view = computed(() => schemaToStateMachine(schema.value))
const editing = ref<string | null>(null)    // 抽屉编辑中的 nodeId
const errLocs = computed(() => locateValidation(schema.value))
const errorNodeIds = computed(() => new Set(errLocs.value.filter(l => l.nodeId).map(l => l.nodeId!)))
const errorEdgeKeys = computed(() => new Set(errLocs.value.filter(l => l.edgeKey).map(l => l.edgeKey!)))

function onRename(nodeId: string, name: string) {
  const v = schemaToStateMachine(schema.value)
  const st = v.states.find(s => s.nodeId === nodeId); if (st) st.name = name
  schema.value = stateMachineToSchema(v, schema.value)
}
function onSetCondition(edgeKey: string, cond: string) {
  const v = schemaToStateMachine(schema.value)
  const p = v.paths.find(p => `${p.raw.from}__${p.raw.to}` === edgeKey)
  if (p) p.condition = cond || undefined
  schema.value = stateMachineToSchema(v, schema.value)
}
function onToggleError(edgeKey: string, val: boolean) {
  const v = schemaToStateMachine(schema.value)
  const p = v.paths.find(p => `${p.raw.from}__${p.raw.to}` === edgeKey)
  if (p) p.isError = val || undefined
  schema.value = stateMachineToSchema(v, schema.value)
}
function onInsertAfter(nodeId: string) {
  const id = `n${Date.now().toString(36)}`
  schema.value = insertStateAfter(schema.value, nodeId, { id, type: 'approval', name: t('oa.designer.sm.newState') })
}
function onRemove(nodeId: string) {
  const r = deleteState(schema.value, nodeId)
  if (r.ok) schema.value = r.schema
  else ElMessage.warning(t('oa.designer.sm.del.' + r.reason))   // multiIn/multiOut/protected
}
function onApplyEdit(patch: Partial<SchemaNode>) {
  schema.value = { ...schema.value, nodes: schema.value.nodes.map(n => n.id === editing.value ? { ...n, ...patch, id: n.id } : n) }
  editing.value = null
}
```
模板：`v-if="view.capability==='readonly'"` → `<el-alert type="warning" :title="t('oa.designer.sm.readonlyBanner')" :closable="false"/>`；`<StateTable :states="view.states" :capability="view.capability" :error-node-ids="errorNodeIds" @rename @edit="editing=$event" @insert-after="onInsertAfter" @remove="onRemove"/>`；`<PathTable :paths="view.paths" :states="view.states" :capability="view.capability" :error-edge-keys="errorEdgeKeys" @set-condition="onSetCondition" @toggle-error="onToggleError" @add-path="..." @remove-path="..."/>`；`<StateEditDrawer :node="editingNode" @apply="onApplyEdit" @close="editing=null"/>`。`readonly` 时 addPath/insert/remove/rename 走 `capability` 门（表格已隐操作列，双保险）。

- [ ] **Step 4: 跑测试验证 PASS** — `npx vitest run src/views/oa/designer/statemachine/`。

- [ ] **Step 5: 全量回归 + type-check + commit**

```bash
cd cp6.web && npm run test && NODE_OPTIONS=--max-old-space-size=8192 npm run type-check
git add -A && git commit -m "feat(wfs-smdesigner): M-C StateMachinePanel+两表+抽屉 write-through 编辑与只读降级"
```

---

## Wave M-D — 模式切换接线 + 行级校验高亮 + 保存链路

### Task M-D-T1: DesignerView 模式开关（localStorage）+ StateMachinePanel 接线 + 保存复用

**Files:**
- Modify: `cp6.web/src/views/oa/designer/DesignerView.vue`
- Create: `cp6.web/src/views/oa/designer/statemachine/modePref.ts` + `modePref.test.ts`（localStorage 纯函数，可测）

**Interfaces:**
- `modePref.ts`：`export function readMode(): 'graph' | 'sm'`（缺省 graph）、`export function writeMode(m: 'graph' | 'sm'): void`（localStorage `oa.designer.mode`，每浏览器）。
- DesignerView：顶部 `el-segmented` 开关 `mode`；`.designer-main` 内 `v-if="mode==='graph'"` 渲 `DesignerCanvas`+右面板（现状）、`v-else` 渲 `<StateMachinePanel v-model="schema"/>`。`schema` ref 不换（同源 write-through）。`doSave`/`validateClient` 两模式共用（不改）。

- [ ] **Step 1: 写失败测试**

```ts
// cp6.web/src/views/oa/designer/statemachine/modePref.test.ts
import { describe, it, expect, beforeEach } from 'vitest'
import { readMode, writeMode } from './modePref'

describe('modePref (localStorage oa.designer.mode)', () => {
  beforeEach(() => localStorage.clear())
  it('缺省 graph', () => { expect(readMode()).toBe('graph') })
  it('写后可读回 sm', () => { writeMode('sm'); expect(readMode()).toBe('sm') })
  it('非法值回落 graph', () => { localStorage.setItem('oa.designer.mode', 'xxx'); expect(readMode()).toBe('graph') })
})
```

- [ ] **Step 2: 跑测试验证 FAIL** — `npx vitest run src/views/oa/designer/statemachine/modePref.test.ts`。

- [ ] **Step 3: 实现 `modePref.ts`**

```ts
// cp6.web/src/views/oa/designer/statemachine/modePref.ts
const KEY = 'oa.designer.mode'
export type DesignerMode = 'graph' | 'sm'
export function readMode(): DesignerMode {
  return localStorage.getItem(KEY) === 'sm' ? 'sm' : 'graph'   // 非 sm 一律 graph（含非法/缺省）
}
export function writeMode(m: DesignerMode): void {
  try { localStorage.setItem(KEY, m) } catch { /* 隐私模式忽略 */ }
}
```

- [ ] **Step 4: 改 `DesignerView.vue`**（纯加法）— ① import `StateMachinePanel` + `readMode`/`writeMode`。② `const mode = ref<DesignerMode>(readMode())`；`watch(mode, writeMode)`。③ toolbar 加 `<el-segmented v-model="mode" :options="[{label:t('oa.designer.sm.modeGraph'),value:'graph'},{label:t('oa.designer.sm.modeSm'),value:'sm'}]"/>`（放校验/保存按钮左侧）。④ `.designer-main` 包裹：`<template v-if="mode==='graph'">`（现有 canvas-wrap + right-panel 原样）`</template>` + `<StateMachinePanel v-else v-model="schema"/>`。**图形分支 DOM 与行为字节等价**（现状代码零改，仅套 `v-if` 外壳）。保存/校验按钮/身份字段不动，两模式共用同一 `schema`。

- [ ] **Step 5: 跑全量 + type-check + build 验证** — 组件测试（StateMachinePanel 行级高亮已在 M-C 覆盖，此处补 DesignerView 冒烟可选）。行级校验高亮的行为闭环由 M-C StateTable.spec + M-A locateValidation.test 已锁；M-D 只接线。

```bash
cd cp6.web && npm run test && NODE_OPTIONS=--max-old-space-size=8192 npm run type-check && npm run build
```

- [ ] **Step 6: commit**

```bash
git add -A && git commit -m "feat(wfs-smdesigner): M-D DesignerView 模式开关(localStorage)+StateMachinePanel 接线+保存链路复用"
```

---

## Wave M-E — i18n 五语 seed + QA harness + DoD

### Task M-E-T1: `I18nOaStateMachineScreenSeed.cs` 五语 + Program.cs concat

**Files:**
- Create: `CP6.WebApi/Seed/I18nOaStateMachineScreenSeed.cs`
- Modify: `CP6.WebApi/Program.cs`（:1819 后一行）

**Interfaces:** 键面 = `cp6.web/src/views/oa/designer/statemachine/**` 实际 `t()` 引用为权威（M-C/M-D 落定后 grep 汇总）。~24 键，前缀 `oa.designer.sm.*`。

- [ ] **Step 1: grep 汇总键面** — `grep -rho "oa\.designer\.sm\.[a-zA-Z.]*" cp6.web/src/views/oa/designer/statemachine | sort -u`，逐键入 seed；去重核对既有 `I18nOaDesigner/ServiceTask/SerialSign/Approver` seed 无碰撞。

- [ ] **Step 2: 建 seed**（范本 = `I18nOaServiceTaskScreenSeed.cs`，含去重声明注释）

```csharp
// CP6.WebApi/Seed/I18nOaStateMachineScreenSeed.cs
using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>状态机模式设计器画面词条：oa.designer.sm.*（模式开关/两表列头/只读横幅/插删操作/审批摘要/抽屉/删除拦截）。
/// 键面以 cp6.web/src/views/oa/designer/statemachine 实际 t() 引用为权威。
/// 去重：本文件全部 oa.designer.sm.* 在既有 I18nOaInbox/Advanced/Designer/SerialSign/Approver/ServiceTask seed 中均无重复（已 grep 核实）。
/// 行级错误定位复用既有 oa.designer.err*（不新增错误码）。</summary>
public static class I18nOaStateMachineScreenSeed
{
    public static readonly Sys_Lang[] Items =
    {
        // ── 模式开关 ──
        new() { LangKey = "oa.designer.sm.modeGraph", ZhCN = "图形模式", ZhTW = "圖形模式", En = "Graph", Ja = "図モード", Ko = "그래프 모드" },
        new() { LangKey = "oa.designer.sm.modeSm",    ZhCN = "状态机模式", ZhTW = "狀態機模式", En = "State Machine", Ja = "状態機モード", Ko = "상태 머신 모드" },
        // ── 只读横幅 ──
        new() { LangKey = "oa.designer.sm.readonlyBanner", ZhCN = "此流程含并行/包容网关或多实例子流程，状态机模式仅供查看，请切图形模式编辑。", ZhTW = "此流程含並行/包容閘道或多實例子流程，狀態機模式僅供檢視，請切圖形模式編輯。", En = "This flow has parallel/inclusive gateways or multi-instance sub-flows; state-machine mode is read-only — switch to graph mode to edit.", Ja = "このフローは並列/包含ゲートウェイまたは多重サブフローを含むため、状態機モードは閲覧のみです。編集は図モードへ。", Ko = "이 흐름은 병렬/포함 게이트웨이 또는 다중 인스턴스 하위 흐름을 포함하여 상태 머신 모드는 보기 전용입니다. 편집은 그래프 모드에서 하세요." },
        // ── State 表列头 ──
        new() { LangKey = "oa.designer.sm.colNo",       ZhCN = "编号", ZhTW = "編號", En = "No.", Ja = "番号", Ko = "번호" },
        new() { LangKey = "oa.designer.sm.colName",     ZhCN = "名称", ZhTW = "名稱", En = "Name", Ja = "名称", Ko = "이름" },
        new() { LangKey = "oa.designer.sm.colType",     ZhCN = "类型", ZhTW = "類型", En = "Type", Ja = "種別", Ko = "유형" },
        new() { LangKey = "oa.designer.sm.colApprover", ZhCN = "审批人", ZhTW = "審批人", En = "Approver", Ja = "承認者", Ko = "승인자" },
        new() { LangKey = "oa.designer.sm.colCountersign", ZhCN = "会签", ZhTW = "會簽", En = "Countersign", Ja = "合議", Ko = "합의" },
        new() { LangKey = "oa.designer.sm.colOps",      ZhCN = "操作", ZhTW = "操作", En = "Actions", Ja = "操作", Ko = "작업" },
        // ── Path 表列头 ──
        new() { LangKey = "oa.designer.sm.colFrom",     ZhCN = "从", ZhTW = "從", En = "From", Ja = "元", Ko = "출발" },
        new() { LangKey = "oa.designer.sm.colTo",       ZhCN = "到", ZhTW = "到", En = "To", Ja = "先", Ko = "도착" },
        new() { LangKey = "oa.designer.sm.colCondition",ZhCN = "条件", ZhTW = "條件", En = "Condition", Ja = "条件", Ko = "조건" },
        new() { LangKey = "oa.designer.sm.colIsError",  ZhCN = "失败边", ZhTW = "失敗邊", En = "Error Path", Ja = "失敗辺", Ko = "실패 경로" },
        // ── 操作按钮 ──
        new() { LangKey = "oa.designer.sm.opEdit",      ZhCN = "编辑", ZhTW = "編輯", En = "Edit", Ja = "編集", Ko = "편집" },
        new() { LangKey = "oa.designer.sm.opInsert",    ZhCN = "插入状态", ZhTW = "插入狀態", En = "Insert State", Ja = "状態を挿入", Ko = "상태 삽입" },
        new() { LangKey = "oa.designer.sm.opRemove",    ZhCN = "删除", ZhTW = "刪除", En = "Delete", Ja = "削除", Ko = "삭제" },
        new() { LangKey = "oa.designer.sm.addPath",     ZhCN = "新增转移", ZhTW = "新增轉移", En = "Add Transition", Ja = "遷移を追加", Ko = "전이 추가" },
        new() { LangKey = "oa.designer.sm.newState",    ZhCN = "新状态", ZhTW = "新狀態", En = "New State", Ja = "新規状態", Ko = "새 상태" },
        // ── 删除拦截 ──
        new() { LangKey = "oa.designer.sm.del.multiIn", ZhCN = "该状态有多条入边，请先在转移表清理入边再删。", ZhTW = "該狀態有多條入邊，請先在轉移表清理入邊再刪。", En = "This state has multiple incoming transitions — clear them in the Path table first.", Ja = "この状態には複数の入辺があります。まず遷移表で入辺を整理してください。", Ko = "이 상태에 여러 진입 전이가 있습니다. 전이 표에서 먼저 정리하세요." },
        new() { LangKey = "oa.designer.sm.del.multiOut",ZhCN = "该状态有多条出边，请先在转移表清理出边再删。", ZhTW = "該狀態有多條出邊，請先在轉移表清理出邊再刪。", En = "This state has multiple outgoing transitions — clear them in the Path table first.", Ja = "この状態には複数の出辺があります。まず遷移表で出辺を整理してください。", Ko = "이 상태에 여러 진출 전이가 있습니다. 전이 표에서 먼저 정리하세요." },
        new() { LangKey = "oa.designer.sm.del.protected", ZhCN = "起点/终点状态不可删除。", ZhTW = "起點/終點狀態不可刪除。", En = "Start/End states cannot be deleted.", Ja = "開始/終了状態は削除できません。", Ko = "시작/종료 상태는 삭제할 수 없습니다." },
        // ── 抽屉 ──
        new() { LangKey = "oa.designer.sm.editTitle",   ZhCN = "编辑状态", ZhTW = "編輯狀態", En = "Edit State", Ja = "状態を編集", Ko = "상태 편집" },
        new() { LangKey = "oa.designer.sm.editInGraphHint", ZhCN = "此类型状态的详细配置请切图形模式编辑。", ZhTW = "此類型狀態的詳細配置請切圖形模式編輯。", En = "Detailed config for this state type is edited in graph mode.", Ja = "この種別の詳細設定は図モードで編集します。", Ko = "이 유형 상태의 상세 설정은 그래프 모드에서 편집합니다." },
    };
}
```

> 审批摘要 token（`approverSummary` 返回 `'DirectManager'`/`'Specified'`/`'stages:N'` 等）在 StateTable 内**复用既有** `oa.designer.strategy.*`（`I18nOaApproverScreenSeed`）+ 自渲 `stages:N`（数字直显），不新增摘要键。会签复用既有 `oa.designer.countersign.*`。类型列复用 node type 原文 / 既有键。执行时按实际引用微调键数（±3）。

- [ ] **Step 3: Program.cs concat**（:1819 后）

```csharp
            .Concat(CP6.WebApi.Seed.I18nOaStateMachineScreenSeed.Items)  // WFS 状态机模式设计器 oa.designer.sm.*
```

- [ ] **Step 4: 后端编译验证** — `dotnet build CP6.WebApi/CP6.WebApi.csproj`（seed 数组语法/concat 通过；无迁移无实体改动）。

- [ ] **Step 5: 前端 i18n 快照 + 全绿闸**

```bash
cd cp6.web && npm run test && NODE_OPTIONS=--max-old-space-size=8192 npm run type-check && npm run build
git add -A && git commit -m "feat(wfs-smdesigner): M-E I18nOaStateMachine 五语 seed + Program.cs concat"
```

### Task M-E-T2: QA harness（gstack 剧本，只写不跑）+ DoD

**Files:**
- Create: `docs/superpowers/qa/wfs-smdesigner/README.md`
- Create: `docs/superpowers/qa/wfs-smdesigner/qa_smdesigner.ps1`

- [ ] **Step 1: 写 README + 剧本脚本**（对齐 E-T3 harness 先例），三剧本**只写不跑**（live QA 需用户在场）：
  - **剧本 1 图形建→状态机改→图形验→保存 round-trip**：图形模式拖线性流程（start→approval→end）→切状态机模式→改审批人（抽屉，复用 ApprovalConfigSection）+ 改条件 + 插入一状态 → 切回图形模式验证节点/边一致 → 保存 → 再入状态机模式确认 round-trip 无损（改动全在）。
  - **剧本 2 并行 schema 只读横幅**：加载含 parallelSplit 的流程 → 切状态机模式 → 断言只读横幅出现 + 表格操作列隐藏 + 切图形模式仍可编辑。
  - **剧本 3 行级校验错误高亮**：造无策略 approval + 悬挂边 → 状态机模式 → 断言对应 State 行/Path 行加 `sm-row-error` 高亮；补齐后高亮消失。
  - 登录/dev server 命令、每浏览器 localStorage 验证口径（`oa.designer.mode`）写入 README。

- [ ] **Step 2: DoD 终检清单**（逐条勾选）
  - [ ] `cd cp6.web && npm run test` = 320 + N 全绿（N = M-A 投影/M-B 抽取/M-C 组件/M-D modePref 用例数）。
  - [ ] `NODE_OPTIONS=--max-old-space-size=8192 npm run type-check` 通过。
  - [ ] `npm run build` 通过。
  - [ ] `dotnet build CP6.WebApi/CP6.WebApi.csproj` 通过；`git show --stat` 后端仅 `I18nOaStateMachineScreenSeed.cs`(新) + `Program.cs`(concat 一行) 两处。
  - [ ] 图形模式零回归：`designerModel.ts`/`DesignerCanvas.vue`/`schemaToGraph`/`graphToSchema`/`validateClient`/`NODE_PALETTE` git diff 为空；`DesignerView.vue` 图形分支 DOM 仅套 `v-if` 外壳。
  - [ ] 零硬编码色：`git grep -nE '#[0-9a-fA-F]{3,6}' cp6.web/src/views/oa/designer/statemachine cp6.web/src/views/oa/designer/ApprovalConfigSection.vue` 无业务色（仅注释 `/* cp-* */` 允许）。
  - [ ] 五语齐全：seed 每条 ZhCN/ZhTW/En/Ja/Ko 五列非空；键面与 `statemachine/**` 实际 `t()` 引用一致（grep 复核，无缺键无孤键）。
  - [ ] round-trip 矩阵全覆盖：approval 全配置 / serviceTask 三 kind / 单实例 subFlow / 条件边 / 错误边 / **回跳边环稳定性** / BFS 编号两次一致 / 插删缝合 / 多入出拦 / write-through 切模式无损（M-A + M-C 测试）。
  - [ ] QA harness 三剧本文档就绪（未跑，live QA 待用户在场）。

- [ ] **Step 3: commit**

```bash
git add -A && git commit -m "feat(wfs-smdesigner): M-E QA harness 三剧本(只写不跑)+DoD 终检清单"
```

---

## 前置依赖（执行 M 波前置校验）

- **二期 hardening H-A~H-C 已并 main**（inclusiveSplit/inclusiveJoin 节点 + `onBranchReject` + inclusive NodePropertyPanel 面板落定 `SchemaNode.onBranchReject`）。
- **三期 subflow S-A~S-E 已并 main**（`subFlow` 节点 + `SchemaNode.subFlowKey/subVarsInJson/subVarsOutJson/subCollectionVar/subCompletionPolicy` + subFlow NodePropertyPanel 面板）。
- 校验命令：`git log --oneline --all | grep -E 'wfs-(kernel-hardening|subflow)'` 确认 H/S 波合并提交存在；`grep -E 'subFlowKey|onBranchReject' cp6.web/src/views/oa/designer/designerModel.ts` 确认字段已入 `SchemaNode`。若字段未落，M-A 测试的 `as any` 造节点仍可编译通过（capability/投影按 R3 契约前瞻写全，C6），但 QA 剧本 2 依赖 inclusive 面板真实存在——执行 M 波须待前置全绿。

---

*生成于 2026-07-05。执行遵守铁律：纯前端增量、后端零改动（i18n seed 除外）、图形模式零回归；write-through 单一事实源=内存 schema；M-E 紧跟 M-D；测试体全代码禁骨架；只本地 commit 不 push。*
