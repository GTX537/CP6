# CP6 审批 / OA 引擎 · 完整设计与实现丛书

> **定位**：CP6 有受注→生产→出货的正向链，补了财务（应收/应付/成本），又补了采购（PR→PO→收货→三单匹配）。但所有这些模块的"**谁来批、按什么流程批、批完怎么落地**"是空的——采购总纲里 `IApprovalService` 还只是个桩，PR/PO 审批走"单人/跳过"。本模块补上**完整低代码 OA 平台**：组织模型 + 表单引擎 + 流程引擎 + 规则引擎 + 高级流程 + 自研设计器，并把采购/财务的审批桩用**同步回调**真正接通。
>
> 风格沿用 [`docs/finance`](../finance/README.md)、[`docs/procurement`](../procurement/README.md)：真实代码当教材，每章讲为什么这么设计、不这么写会出什么事、与业界（钉钉/泛微 e-cology / Flowable / Elsa）怎么对比。引擎内部的"从零造一遍"深度讲解见 [`docs/oa`](../oa/README.md) 学习丛书——**本书是可开建的工程总纲，docs/oa 是配套教材**。
>
> 需求基线：完整平台（5 引擎 + 表单/流程双自研设计器）/ 全套审批路由 / 会签三规则 / 退回·加签·超时·委派全要 / 与采购·财务同步回调对接 / 运行时优先、设计器最后。

> **⚠️ 复审定稿调整（2026-06-12）·全文口径以本块为准**：**组织模型 `Sys_Dept`（含部门树/Path、LeaderId 部门长、`Sys_User` 的 DeptId/ManagerId）归属移交 [PUB 公共平台](../pub/README.md)（章 00），作为 PUB 基座先落**——它同时满足 PUB 数据权限(Path 子树)与本 OA 的审批路由(直属上级/部门长)。因此本书下文凡称"阶段0 新建组织模型 / 硬缺口 / 01 章"处，**统一改为"消费 PUB 组织模型，不重复建"**；OA 自身实施**从阶段1（可用 OA·手配 JSON 运行时）起步**。原 [阶段0 组织模型实施计划](../superpowers/plans/2026-06-10-approval-stage0-org-model.md) 移交 PUB 承接（内容已含双方需求）。01 章保留为"组织模型消费方说明"。

---

## 一、先记住这一句话（题眼）

> **拖拽产生的不是代码，而是一段 JSON 配置（schema）；系统里有解释器，运行时读这段 JSON 把表单画出来、把流程转起来。做一个 OA = 做两个解释器（表单解释器 + 流程解释器）+ 一个算"谁来批"的组织引擎。审批与业务模块之间只走同步回调，依赖单向、不双写。**

两层含义：
1. **运行时优先、设计器最后**——先手写 JSON 跑通引擎，最后才做生成 JSON 的拖拽设计器。很多人反着来（先做设计器）所以失败。即便本模块"一次做完整平台"，自研设计器也排在最末阶段。
2. **审批不侵入业务**——ERP 业务单据（PR/PO/付款）有自己的表和状态机，OA 只"挂"一层审批：业务调 `IApprovalService` 起流程，审批终态时 OA 同步调 `IApprovalCallback` 回调业务去落地。**业务状态唯一真相在各模块自己，OA 不双写业务表**。这与采购"同步可调试、依赖单向"原则一致。

---

## 二、一个完整 OA = 5 个引擎 + 2 个设计器

| 引擎 | 干什么 | 设计器产出 | 运行时干什么 | 本书章节 |
|---|---|---|---|---|
| **组织引擎** | 审批人怎么算 | — | 部门树/上下级/角色 → 解析具体审批人 | [01](./01-org-model.md) |
| **表单引擎** | 动态表单 | form schema | 读 schema 动态渲染页面 | [02](./02-form-runtime.md) |
| **流程引擎** | 审批流转 | flow schema | 状态机：当前节点+动作→下一节点 | [03](./03-flow-runtime.md) · [07](./07-advanced-flow.md) |
| **规则引擎** | 字段联动 | rules | 表单运行时执行显隐/计算/联动 | [06](./06-rule-engine.md) |
| **数据引擎** | 表单数据怎么存 | — | 存/查动态表单提交的数据 | [08](./08-data-storage.md) |
| **设计器** | 生成 schema | — | 拖拽产出表单/流程 JSON（最后做） | [09](./09-designers.md) |

---

## 三、模块边界

```
┌──────────────── 审批 / OA 引擎 (Approval / Workflow) ────────────────┐
│ 组织引擎 → 表单引擎 → 流程引擎 → 表单×流程绑定 → 规则引擎 → 高级流程  │
│ 自有数据：Sys_Dept 部门树 / FormDef·FormData / FlowDef·FlowInstance·  │
│           FlowTask·FlowHistory / FlowDelegate / ApprovalBinding       │
│ 自有逻辑：审批人解析、状态机流转、会签编排、退回/加签/超时/委派       │
│ 自研：表单设计器 + 流程设计器（产 schema，最后做）                    │
└───┬───────────────────┬────────────────────┬──────────────────────┘
    │同步起流程/回调       │复用                  │复用
    ▼                    ▼                     ▼
 业务模块(采购PR/PO、     SignalR(待办实时推送)  SQL Server JSON 列
 财务付款)               IntegrationEvent       (FormData/SchemaJson)
 IApprovalService /      (仅引擎内异步驱动,
 IApprovalCallback        非跨模块编排)
```

| 接口 | 方向 | 职责 | 关联 |
|---|---|---|---|
| `IApprovalService.Submit(bizType,bizId,snapshot)` | 业务→OA | 业务发起审批，OA 起 `FlowInstance` | 返回 instanceId |
| `IApprovalService.GetStatus(bizType,bizId)` | 业务→OA | 查审批状态 | 只读 |
| `IApprovalCallback.OnApproved/OnRejected(bizType,bizId)` | OA→业务 | **审批终态同步回调，业务去落地** | 业务实现，依赖单向 |
| `IExternalOaConnector`（可选） | OA→外部 | 对接外部 OA（升级 `IPowerEggWorkflowService` 桩） | 可插拔 |

> 引擎**内部**流转可用 `IntegrationEvent` 异步驱动（幂等/补偿、Phase 6 那套），但**跨模块只走同步回调**——一条直线可追踪，不在事件死信里捞，不双写业务表。

---

## 四、最小数据模型（贯穿全书）

落 `CP6.Entity/DomainModels/Wf/`，与 Erp/Mes/Wms/Fin/Pur 平级，全表带 `TenantId`。

```
■ 组织引擎（阶段0，审批人靠它算）
  Sys_Dept       Id, ParentId, Name, Path(物化路径), LeaderId(部门负责人), Sort, Enable
  Sys_User +新增  DeptId, ManagerId(直属上级), Email          ← 现有表补三字段
  (角色复用现有 Sys_Role / Sys_User.RoleId，不新建)

■ 表单引擎（阶段1）
  FormDef        FormKey, Name, SchemaJson, Version, Status
  FormData       FormKey, BizId, DataJson                     ← SQL Server JSON 列存

■ 流程引擎（阶段1，★最硬核）
  FlowDef        FlowKey, Name, SchemaJson, Version, FormKey
  FlowInstance   FlowKey, BizType, BizId, CurrentNode, Status, VarsJson, StarterId
  FlowTask       InstanceId, NodeId, AssigneeId, Status(待办/已办),
                 CountersignRule(全体/或签/一票否决), AddSignSource(前加/后加)
  FlowHistory    InstanceId, NodeId, ActorId, Action, Comment, At
  FlowDelegate   GrantorId, DelegateId, ValidFrom/To, Scope    ← 委派/代理

■ 集成（阶段2）
  ApprovalBinding  BizType(PR/PO/Payment…), FlowKey, ConditionJson(触发/选流程)
```

> `FormDef.SchemaJson` 和 `FlowDef.SchemaJson` 是整个平台的核心——**设计器的产出全进这两个字段**。`FlowInstance.BizType/BizId` 是审批与业务的唯一关联键，回调靠它定位业务单。

---

## 五、两条核心流程

### 流程 A — OA 原生审批（请假/报销/用章，数据在 OA）
```
发起人填动态表单(FormData) →提交→ 起 FlowInstance
  → 流程引擎按 flow schema 流转：解析审批人(组织引擎)→建 FlowTask→待办(SignalR推送)
        ├ 串行/多层：一节点过再下一节点
        ├ 会签：全体同意/或签/一票否决
        └ 高级：退回/加签/超时/委派
  → 终态(通过/驳回)→记 FlowHistory→（OA 原生场景无需回调，数据已在 FormData）
```

### 流程 B — ERP 业务单据审批（采购 PR/PO、财务付款，数据在各模块）
```
业务建单 → 调 IApprovalService.Submit(bizType,bizId,snapshot)
  → OA 按 ApprovalBinding 选流程→起 FlowInstance→同流程引擎流转
  → 终态：
        ├ 通过→同步调 IApprovalCallback.OnApproved(bizType,bizId)→业务走自己状态机/BridgeHook 落地
        └ 驳回→同步调 IApprovalCallback.OnRejected→业务置回草稿/驳回
```
> 关键：OA **不碰**业务表，只回调；采购总纲承诺的 `IApprovalService` 桩在此升级为本引擎实现，PR/PO 审批从"单人/跳过"升级为"组织路由 + 会签 + 高级动作"。

---

## 六、章节目录

### Part 0 · 总览
- **00. 心智模型 + 模块边界**（本页）

### Part 1 · 运行时优先（手配 JSON 的可用 OA）
- [01. 组织模型](./01-org-model.md) — **阶段0**，`Sys_Dept` 树 + 上级 + 部门长 + 审批人解析器（硬前置）
- [02. 表单引擎运行时](./02-form-runtime.md) — **阶段1**，schema 驱动动态渲染
- [03. 流程引擎运行时](./03-flow-runtime.md) — **阶段1 ★最硬核**，状态机 + 会签三规则
- [04. 表单 × 流程绑定](./04-form-flow-binding.md) — **阶段1**，字段权限 + 待办中心 + 我的申请 + 审批痕迹

### Part 2 · 接通业务硬依赖
- [05. 与 CP6 集成](./05-integration.md) — **阶段2 ★MVP 价值点**，`IApprovalService`/`IApprovalCallback` 同步回调，接采购/财务

### Part 3 · 复杂审批
- [06. 规则引擎](./06-rule-engine.md) — **阶段3**，显隐/计算/联动/条件分支
- [07. 高级流程](./07-advanced-flow.md) — **阶段3**，退回/加签/超时/委派 + 会签编排

### Part 4 · 支撑与产品化
- [08. 数据存储模型](./08-data-storage.md) — JSON 列 vs EAV vs 动态建表
- [09. 自研设计器](./09-designers.md) — **阶段4，最后做**，表单设计器 + 流程设计器
- [10. 多租户与商业化](./10-multi-tenant.md) — schema 隔离、模板市场

---

## 七、分阶段实施路线（范围都在，只是先后；运行时优先）

| 阶段 | 目标 | 含章节 | 完成标志 |
|---|---|---|---|
| **0（归 PUB）** | 组织模型 → **消费 PUB 章00** | 01(消费方说明) | PUB 组织模型就绪，审批人"找直属上级/部门长/角色/指定人"算得出来；**OA 不自建** |
| **1（OA 实施起点）** | 可用 OA（手配 JSON） | 02·03·04·08 | 一张单从提交审到结束，待办/痕迹齐，**难看但能用** |
| **2 ★MVP** | 接通业务硬依赖 | 05 | 采购 PR/PO、财务付款 发起→通过→同步回调落地，**兑现 `IApprovalService`** |
| 3 | 真实复杂审批 | 06·07 | 会签/或签/一票否决 + 退回/加签/超时/委派 全可用 |
| 4 | 可视化 + 商业化 | 09·10 | 自研双设计器产出的 JSON 与手写同构；多租户隔离 |

> 即便"一次做完整平台"，**阶段 2 仍刻意排在高级流程(阶段3)之前**——接采购/财务硬依赖只需要"组织 + 基础流程 + 同步回调"，先兑现价值，复杂审批随后补。

---

## 八、复用 vs 新建

| 能力 | CP6 现成的 | 怎么用 |
|---|---|---|
| 登录/角色/菜单 | `Sys_User` / `Sys_Role` / `Sys_Menu` | 复用；`Sys_User` 的 `DeptId/ManagerId/Email` 三字段由 **PUB 章00 组织模型**补，OA 直接用 |
| 部门/上级/部门长 | **PUB 组织模型（章00）** `Sys_Dept` 树 + LeaderId + `Sys_User.DeptId/ManagerId` | **消费 PUB，不自建**；审批路由直接用 |
| 表单数据存储 | SQL Server 原生 JSON | `DataJson`/`SchemaJson` 直接存 JSON 列，不纠结 EAV |
| 待办实时提醒 | SignalR Hub（已有） | 新审批任务实时推到待办中心 |
| 引擎内异步驱动/补偿 | `IntegrationEvent` + 重试/死信（Phase 6） | 仅引擎内流转复用，幂等可补偿 |
| 审批通过回写各模块 | `IErpBridgeHook`/`IMesBridgeHook`/`IWmsBridgeHook` | 业务在 `OnApproved` 回调里走原有 Hook 落地 |
| 采购/财务审批桩 | 采购 `IApprovalService`（桩） | 升级为本引擎实现 |
| 外部 OA 对接（可选） | `IPowerEggWorkflowService`（桩） | 升级为 `IExternalOaConnector` |

**原唯一硬缺口：组织架构 —— 已移交 PUB 解决。** 组织模型（`Sys_Dept` 树 + `Sys_User` 三字段）由 [PUB 章00](../pub/README.md) 建，OA 消费；它仍是一切审批路由的前置，但 OA 不自建。OA 实施从阶段1 起。

---

## 九、与业界对照

| 想理解 | 去看 | 学什么 |
|---|---|---|
| 表单设计器 + 动态渲染 | **form-create / variant-form** | 拖拽如何产 form schema、运行时如何渲染 |
| 流程引擎（.NET，匹配 CP6） | **Elsa Workflows / Workflow Core** | 节点定义、实例持久化、状态机驱动 |
| 流程图设计器 | **LogicFlow / bpmn-js** | 拖拽如何产 flow schema |
| 完整审批中台 | **钉钉审批 / 泛微 e-cology / Flowable** | 路由解析、会签、退回/加签/超时/委派的工程拼装 |

> 自研设计器不是"造轮子"，是要**控制力与体验统一**——但仍按"运行时优先、设计器最后"，产出的 JSON 必须与手写 schema 同构。

---

## 十、里程碑自检

- [ ] 审批人"找直属上级/部门长/角色/指定人"分别靠组织模型的哪个字段算出来？某种缺位（无上级）怎么兜底？
- [ ] 一张单从提交到结束，流程引擎每一步在算什么？会签的全体同意/或签/一票否决在数据上差在哪？
- [ ] 退回到指定节点要清理哪些 FlowTask / 会签计票？加签（前/后）怎么不破坏原流程？
- [ ] OA 为什么走同步回调而不是事件？业务状态唯一真相在哪？OA 能写业务表吗？
- [ ] 采购总纲的 `IApprovalService` 桩，在阶段 2 怎么升级、怎么不阻塞？
- [ ] 自研设计器为什么排最后？它产出的 JSON 和阶段1手写的是同一种结构吗？（应该是）

全部能答 → CP6 有了自己的审批中台，采购/财务的"谁来批"立住了，并具备完整低代码 OA 平台的商业化底座。

---

*生成于 2026-06-10。需求基线见首部。配套实现将落于 `CP6.Entity/DomainModels/Wf`、`CP6.Core/Services/Wf`、`cp6.web/src/views/wf`（随章节推进）。引擎内部深度讲解见 [`docs/oa`](../oa/README.md)。*
