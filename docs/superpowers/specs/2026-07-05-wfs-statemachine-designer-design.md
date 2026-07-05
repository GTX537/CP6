# WFS 状态机模式设计器（Delta State+Path 双模式并存）设计

> 生成于 2026-07-05（brainstorming 已确认，WFS 深化三期 Spec C）。上游：内核 spec §11 P5「Delta 状态机式 BPM 设计器」；umbrella §1.5「Delta = State（状态）+ Path（路径）状态机」实证。视觉参照：`docs/oa/WFS/流程编辑器-离线版.html`。
> 落码位：`cp6.web/src/views/oa/designer/`（纯前端 + 少量 i18n seed；后端零改动）。

---

## §0 背景、范围与决策

### §0.1 背景

现有设计器是 VueFlow 图形画布（C′ 波落地，二期三期节点逐波补齐）。目标用户群含大量 Delta 老用户——Delta 的流程编辑心智是 **State 表（2887 起票/2888 课长审…编号态）+ Path 表（态→态转移）**，不是拖拽画布。P5 的目标是给这批用户一个零学习成本的编辑范式，同时不动引擎 schema、不动图形画布。

### §0.2 范围（In / Out）

**In**：状态机模式视图（State 表 + Path 表）；与图形模式共 schema 双向投影；顶部模式切换（每用户记忆）；线性+条件分支流程的完整编辑能力；并行/inclusive/多实例 schema 的**只读降级**；round-trip 测试；五语 i18n；QA harness。

**Out（→ §8 YAGNI）**：状态机模式下编辑并行结构；替换图形设计器；离线版 html 的像素级复刻（对齐范式不对齐皮肤——皮肤走 CP6 Design System）；移动端设计器。

### §0.3 锁定决策（用户已拍板 2026-07-05）

| # | 决策 | 依据 |
|---|------|------|
| D1 | **双模式并存**：保留 VueFlow 图形画布，新增状态机模式，同一 schema 双向投影，顶部切换 | 用户选项确认；否决替换（丢弃已 QA 的画布与二期 inclusive/剪枝 UI，回归面大） |
| D2 | 能力边界：状态机模式覆盖**线性+条件分支**；schema 含 parallel/inclusive 网关或多实例 subFlow → 状态机模式**降级只读**（横幅提示切图形模式） | Delta State+Path 范式无并行语义，硬造表格表达两头不像；只读投影保留"看得懂"价值 |
| D3 | 引擎 schema/后端**零改动**：状态机是纯视图投影，保存走同一 `DesignerService.save` + 同一 E-WF 校验族 | 范式之差不是模型之差 |
| D4 | 模式偏好每用户记忆（localStorage，键 `oa.designer.mode`；不入 InboxPref——设计器偏好非信箱偏好，且无跨端同步需求） | YAGNI |

---

## §1 现状锚点（逆向真实，不编造）

- **设计器结构**：`cp6.web/src/views/oa/designer/`——`FlowDesigner.vue`（画布壳）/ `designerModel.ts`（schema↔VueFlow 图投影 `schemaToGraph`/`graphToSchema` + `validateClient`）/ `NodePropertyPanel.vue`（约 900 行，按节点类型分段）/ `EdgePropertyPanel.vue`。
- **schema 形态**：`FlowSchema { nodes: FlowNode[], edges: FlowEdge[] }`（camelCase 序列化契约，C-T3 已核）；节点类型（三期后全集）：start / approval / condition? / parallelSplit / parallelJoin / inclusiveSplit / inclusiveJoin / serviceTask / subFlow / end（执行 plan 时以 handler 字典与 `designerModel.ts` 实际为准）。
- **审批人配置面**：approval 节点 8 策略 + When/Filter + 串签 stages + 会签 countersign——State 表行内编辑复用 `NodePropertyPanel` 的既有分段控件（抽子组件复用，不复制粘贴）。
- **参照文件**：`docs/oa/WFS/流程编辑器-离线版.html`（State/Path 表格范式来源）。
- **umbrella §1.5.2**：Sign Records 弹窗 = `Wf_FlowFormTo` 时间线——State 编号（FunctionId/FlowCode 家族，`Wf_FlowDef` 相关）与 Delta 2887 式人面编号的对应关系在 umbrella §2.7。
- **既有前端测试**：`designerModel.test.ts` / `designerModel.serviceTask.spec.ts`——投影 round-trip 测试风格范本。

---

## §2 投影架构

### §2.1 文件结构（新增，均在 `views/oa/designer/statemachine/`）

| 文件 | 职责 |
|---|---|
| `stateMachineModel.ts` | **纯函数投影**：`schemaToStateMachine(schema) → SmView`、`stateMachineToSchema(smView, baseSchema) → FlowSchema`、`smCapability(schema) → 'editable' \| 'readonly'`（能力检测） |
| `StateMachinePanel.vue` | 状态机模式壳（State 表 + Path 表 + 只读横幅） |
| `StateTable.vue` | State 表：行=状态（节点），列=编号/名称/类型/审批人摘要/会签/操作 |
| `PathTable.vue` | Path 表：行=转移（边），列=从/到/条件/失败边标记/操作 |
| `StateEditDrawer.vue` | 行编辑抽屉：复用 NodePropertyPanel 抽出的分段子组件 |

### §2.2 `SmView` 模型与投影规则

```typescript
interface SmState { no: number; nodeId: string; type: string; name: string;
                    approverSummary: string; countersign?: string; raw: FlowNode }
interface SmPath  { fromNo: number; toNo: number; condition?: string; isError?: boolean; raw: FlowEdge }
interface SmView  { states: SmState[]; paths: SmPath[]; capability: 'editable' | 'readonly' }
```

- **状态编号 no**：从 start 拓扑序生成（start=0，end=最大），只是**视图内行号**（Delta 心智锚点），不持久化、不进 schema——nodeId 仍是权威身份。
- **capability 检测**（D2）：schema 含 `parallelSplit/parallelJoin/inclusiveSplit/inclusiveJoin` 节点，或 subFlow 节点 `SubCollectionVar` 非空（多实例）→ `readonly`；否则 `editable`。serviceTask/单实例 subFlow/条件边/错误边都可编辑（线性链上的自动化步骤是 Delta 心智内的）。
- **双向投影**：`editable` 时 `stateMachineToSchema` 以 baseSchema 为底，按表格增删改合成新 schema（节点坐标：新增状态自动布局在链尾/插入点中点，图形模式切回后可再手排；既有节点坐标原样保留）。`readonly` 时不提供反向。
- **round-trip 不变量**：`editable` schema → SmView → schema′，除坐标外语义等价（节点/边/全部配置字段深比较）——vitest 锁定。

### §2.3 模式切换

`FlowDesigner.vue` 顶部加分段开关「图形 / 状态机」：切换即投影（同一内存 schema，无需保存）；localStorage 记忆（D4）；`readonly` schema 切入状态机时横幅提示 + 表格禁编辑（查看/跳转仍可用）。保存按钮两模式共用（状态机模式保存前先 `stateMachineToSchema` 合成，走同一 save + validateClient）。

---

## §3 编辑能力（editable 模式）

- **State 表**：行内改名称；抽屉编辑审批人策略/When/Filter/会签/串签 stages/超时（复用抽出的分段子组件——**本 spec 含一次定向重构：把 `NodePropertyPanel.vue` 的 approval 配置段抽成 `ApprovalConfigSection.vue`**，两处消费，杜绝复制粘贴；serviceTask/subFlow 段同法按需抽取）；「插入状态」在选中行后插入（自动接链：原 from→新→原 to）；删除状态（自动缝合前后边；有多入/多出时要求先在 Path 表清边）。
- **Path 表**：增删转移、行内编辑条件表达式（同 EdgePropertyPanel 的表达式输入）、失败边勾选（IsError，来源类型受基建 spec E-WF-027 集合约束，validateClient 同镜像）。
- **校验**：保存时走既有全家族；表格视图对 validateClient 错误做**行级定位**（错误码→nodeId/edge→表格行高亮），这是表格模式对图形模式的体验增益点。

---

## §4 视觉与 i18n

- 视觉对齐 `流程编辑器-离线版.html` 的 State/Path 范式（两表上下布局、编号列、紧凑行高），**皮肤走 CP6 Design System v1.0**（CpTag/tokens，零硬编码色）；执行 D 波时用 frontend-design skill 定稿。
- i18n 五语：估 ~22 键（模式切换/两表列头/只读横幅/插入删除操作/行级错误提示），续 `I18nOa*ScreenSeed` 家族新 seed。

---

## §5 安全 / 向后兼容

- 纯前端增量：后端/引擎/schema/权限零改动；图形模式零回归（模式开关默认=图形，未切换用户无感知）。
- `capability=readonly` 是**视图级**限制，非权限限制——同一用户切图形模式即可编辑。

---

## §6 测试策略

- **投影 vitest**：round-trip 语义等价（含 approval 全配置/serviceTask 三 kind/单实例 subFlow/条件边/错误边）；capability 矩阵（parallel/inclusive/多实例→readonly，其余→editable）；编号拓扑序稳定性；插入/删除状态的缝合正确性；删除多入出节点被拦。
- **组件 vitest**：只读横幅渲染、行级错误定位映射。
- **QA harness**（gstack）：图形建线性流程→切状态机改审批人+插状态→切回图形验证→保存→再入状态机 round-trip；并行 schema 切入只读降级横幅；行级校验错误高亮。
- 基线：前端 vitest/type-check/build 全绿；后端零改动（i18n seed 除外）。

---

## §7 分期 / 任务波次（供 writing-plans 细化）

- **M-A 投影内核**：`stateMachineModel.ts` 三函数 + round-trip/capability 全量 vitest（纯逻辑先行，无 UI）。
- **M-B 配置段抽取重构**：`ApprovalConfigSection.vue` 等从 NodePropertyPanel 抽出（NodePropertyPanel 行为零变化，vitest+type-check 锁定）。
- **M-C 表格 UI**：StateTable/PathTable/StateEditDrawer/StateMachinePanel + 模式切换 + 只读降级。
- **M-D 行级校验定位** + 保存链路接线。
- **M-E i18n + QA**：五语 seed + harness + DoD。

依赖：M-A → M-B → M-C → M-D → M-E。**建议排三期最后**（S/I 两 spec 的新节点类型敲定后投影一次写全）。

---

## §8 YAGNI / 留后

- 状态机模式编辑并行结构（范式不匹配，永久走图形模式）。
- 离线版 html 像素复刻/皮肤主题化。
- 状态编号持久化为 FlowCode（umbrella §2.7 的 flowcode 是流程级编号，与行号无关；若未来要 Delta 式关卡号持久化，另立增量）。
- 移动端设计器、模式偏好跨端同步。

---

*生成于 2026-07-05。执行遵守铁律：纯前端增量、图形模式零回归；E 波紧跟 D 波；frontend-design skill 定稿视觉。*
