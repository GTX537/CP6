# OA 审批解耦设计：WFS 审批接入套件 + 全站唯一审批面

日期：2026-07-07
状态：已与用户逐节确认（办理位置/范围/条件选流程/动作模型/SFS 按钮归属五项拍板）
范围：WFS 审批接入套件 + 两个正确性修复；SFS 产品面缺口（子表格/附件/草稿发布/JSON 字段查询）**另立项，不进本 spec**

---

## 1. 背景与目标

### 1.1 问题

CP6 OA = WFS（审批流）+ SFS（低代码表单）。盘点（2026-07-07 双路代码实读）确认：

1. **后端解耦骨架已存在且被三家实证**：`IApprovalService.SubmitAsync(bizType, bizId, formSnapshot)` + `Wf_ApprovalBinding`（bizType→FlowKey）+ `IApprovalCallback` 终态回调（原子性铁律：回调与引擎共享 scoped DbContext，终态与业务变更同事务落库）。采购 `PurApprovalCallback`、财务凭证 `JournalApprovalCallback`、预算 `BudgetApprovalCallback` 已接入并有集成测试。
2. **断点全在审批人侧**：收件箱 FormDetail 完全不认 BizType——业务单据审批待办没有结构化呈现，没有去业务页面的通道；三家业务模块前端各自裸调 API，审批 UI 开始出现方言。
3. `Wf_ApprovalBinding.ConditionJson`（按单据字段选流程）预留未实现；绑定关系无管理 UI（只能 SQL 种子）。
4. 两个正确性风险：**旧单渲染错位**（`Wf_FormData.FormVersion` 留痕了，但 `SaveDefAsync` 原地覆盖 SchemaJson，历史 schema 已丢，渲染永远用当前版）；**FormDetail 丢弃 rules**（只取 fields，条件隐藏字段在审批视图全部展示——信息暴露 + 与发起人所见不一致）。

### 1.2 目标（用户原话锚定）

> "WFS 不和低代码强绑定，因为后续有复杂表达，需要客制化使用代码完成，审核就可以直接套用 WFS。"
> "SFS 应该负责低代码页面的开发及逻辑，所有的审核都归 WFS。"

落成三句话：

1. **代码写的客制化页面接审批 = 后端实现一个 Callback + 前端放一个 ApprovalPanel + 配一条绑定**，没有第四步。
2. **审批面是 WFS 的资产**：状态、动作、意见、时间线的 UI 全站只有一份实现；SFS 与任何客制化页面在 WFS 眼里地位平等，都只是内容渲染器。
3. 顺带修掉两个正确性风险。

### 1.3 用户拍板存档

| 决策点 | 拍板 |
|---|---|
| 审批人办理位置 | **业务页面办理**：收件箱纯路由（深链跳业务页），ApprovalPanel 嵌业务详情页 |
| 范围 | 解耦接入套件为主 + 两个正确性风险修复；SFS 产品面缺口另立项 |
| 条件选流程 | **本期做**（复用 ExpressionEvaluator，fail-closed） |
| 审批按钮 | **可客制化动作模型**：引擎动词固定五个，按钮呈现/业务动词由描述符定义 |
| SFS 按钮归属 | FormDetail 写死按钮**删除**，改用 ApprovalPanel——审批 UI 全归 WFS，**FormDetail 改造纳入本期** |

---

## 2. 架构总图

```
SFS 低代码入口                    代码客制化入口
FormInitiate(表单目录发起)         业务页面(采购/财务/未来定制页)
    │                                │
    ▼                                ▼
FormService                    IApprovalService.SubmitAsync
(schema校验+复算落库)           (bizType, bizId, formSnapshot)
    │                                │
    │                     Wf_ApprovalBinding 选流程
    │                     (ConditionJson 条件规则 → FlowKey，fail-closed)
    └────────────┬───────────────────┘
                 ▼
         FlowEngine（唯一引擎：token/会签/串签/并行/超时/通知/收件箱）
                 │
        ┌────────┴─────────┐
        ▼ 终态              ▼ 办理
  ApprovalDispatcher      收件箱(纯路由)
  → IApprovalCallback       ├─ detailRoute 非空 ──深链──▶ 业务页面 = 业务视图 + <ApprovalPanel>
  (业务自己落库,同事务)      └─ detailRoute 为空(SFS) ──▶ FormDetail = DynamicForm只读 + <ApprovalPanel>
```

**不变式（沿用既有约定，本设计只补缺不改向）：**

1. 引擎零业务依赖：WFS 只认 `IApprovalCallback`/`Wf_ApprovalBinding` 抽象；业务实现注册 DI，运行时多态分发。
2. formSnapshot 是唯一数据交换面：序列化进实例 VarsJson，供①条件选流程②流程内条件边③审批人摘要三处消费；OA 终态不回查业务表。
3. 终态回调原子性铁律不动（IApprovalCallback 注释已成文）：回调抛异常 → 流程终态与业务变更一并回滚。
4. SFS 是 WFS 的一个客户，不是宿主。

---

## 3. 后端契约增量

### 3.1 `Wf_ApprovalBinding` 增强（一次 EF 迁移）

**新列 `DetailRoute nvarchar(200) NULL`**：前端路由模板，如 `/pur/orders/{bizId}`。占位符仅支持 `{bizId}`（YAGNI：`{bizType}` 等将来有真需求再加）。为空 = 该绑定走收件箱 FormDetail（SFS 表单绑定留空）。

**实现 `ConditionJson`（列已在，补运行时语义）**：

```json
[
  { "when": "amount > 100000", "flowKey": "po-approval-high" },
  { "when": "amount > 10000",  "flowKey": "po-approval-mid" }
]
```

- `ApprovalService.SubmitAsync` 中：反序列化规则数组 → 用**现成 `ExpressionEvaluator`**（前后端同语义引擎）以 formSnapshot 为 vars **顺序求值，首中即选**；全不中回落主 `FlowKey`。
- **fail-closed 语义**：`ConditionJson` 解析失败 / 表达式求值抛异常 / 选中 FlowKey 不存在或 `Enable=false` → **抛错拒绝提交**（错误码见 §7），绝不静默回落主 FlowKey——审批走错链是合规事故，宁可提交失败暴露配置问题。
- 主 `FlowKey` 自身不存在/停用同样拒绝（现状是起流程时才炸，前移到绑定校验与提交两道闸）。**即使条件规则已命中别的流程，主 FlowKey 无效仍拒绝——有意从严**：主 key 是兜底契约的一部分，允许它烂着等于允许"全不中"路径随时炸，plan 阶段不得当 bug 放松。
- **FlowKey 可发起性判定收敛到单一解析方法**（`ApprovalService` 内一个 private resolver，绑定保存校验与提交两道闸、canSubmit 三处共用）。**四期版本治理联动条款**（照 version-ops spec §3 第 6 条对触发器的写法）：四期 V-A pin 落地后，可发起性口径从「FlowKey 存在且 Enable」切换为「最新 Published 且 Enable」（E-WF-029 语境），**只改该 resolver 一处**，本 spec 三道闸自动继承——不得在三处各写一遍判定。

**绑定生命周期语义（Enable / 删除）：**

- **`Enable=false` 只封发起**：`SubmitAsync` 拒绝（E-WF-031）+ 聚合端点 `canSubmit=false`。**`DetailRoute` 照常下发、在途实例照常办理**——路由是呈现不是发起，停用绑定不得把在途待办变砖（业务单据没有 SFS FormData，回落 FormDetail 是空壳）。条件选流程只发生在提交时，不涉及在途。
- **删除守卫**：绑定已被任何流程实例引用（存在 `Wf_FlowInstance.BizType` 匹配，不限在途）→ **禁止物理删除**（E-WF-035），只能停用——物理删除会使存量待办与历史单据的深链 join 不到路由。从未被引用的绑定可删。管理 UI 的删除按钮按此显隐。

### 3.2 绑定管理 UI

FlowAdmin 增「审批绑定」页签：

- 列表 + CRUD：BizType / 主 FlowKey（下拉，只列启用流程）/ DetailRoute / Enable / 条件规则编辑器（when 表达式 + flowKey 行式编辑，支持排序）。
- 保存校验：表达式语法预检（ExpressionEvaluator 试解析）+ 所有 flowKey 存在且启用 + BizType 唯一（已有 UX 索引，前端友好提示）。
- **模拟求值**框：贴一段 JSON 快照 → 即时显示会命中哪条规则、选中哪个流程。运维排障刚需。
- 权限：挂 FlowAdmin 既有权限点体系，绑定管理属高危操作独立 action。

### 3.3 ApprovalPanel 聚合端点（唯一新增查询端点）

```
GET /api/oa/approval/detail?bizType=PO&bizId=xxx     （业务页模式）
GET /api/oa/approval/detail?instanceId=xxx           （SFS/实例模式）
```

返回：

```
{ instanceId, status, currentNodeName, starterName,
  myTask: { taskId, nodeId, nodeName } | null,      // 当前登录用户的待办任务
  timeline: [...],                                   // 复用 InboxService 既有轨迹投影
  canSubmit: bool }                                  // (None|Rejected|Withdrawn) 且绑定启用（口径见 §4.3）
```

- 双键二选一，都传或都不传 → 400。bizType+bizId 模式取**最新**实例（与 `GetStatusAsync` 现有口径一致）。
- **授权口径（端点级，防止刚堵字段级暴露又开端点级暴露）**：返回体含完整审批轨迹与各关卡意见，不得任意登录用户凭 bizId 可查。可见性判定 = **发起人 ∪ 当前及历史办理人（曾被指派任务，Wf_FlowFormTo 三列）∪ 被抄送人（Wf_FlowCc）**——即复用收件箱既有可见面。管理员不入此集合（plan 阶段收窄）：FlowAdmin 有自己的管理入口，聚合端点不做权限旁路，更安全。未命中 → 403，响应不含任何单据信息。"旁观者态" = 命中可见性但当前无待办者（§4.3 矩阵中的旁观者行即指此集合，不是任意人）。**无实例场景**：仅返回 `{ status: None, canSubmit }` 骨架（信息量近零，无需可见性判定；提交本身由业务页权限守卫）。
- **写操作零新增**：同意/驳回/退回/转办/撤回全部复用现有收件箱 act 端点（按 taskId），审计、幂等、计票路径不分叉。

---

## 4. 前端接入套件（审批面全归 WFS）

### 4.1 `useApproval` composable（逻辑层，无 UI）

```ts
// 双键模式二选一
const a = useApproval({ bizType: 'PO', bizId: orderId })
const b = useApproval({ instanceId })

// 暴露
a.status / a.instanceId / a.myTask / a.timeline / a.loading
a.approve(comment) / a.reject(comment)   // 按 myTask.taskId 调现有 act 端点
a.sendBack(...) / a.transfer(...) / a.revoke()
a.refresh()
a.onDecided(cb)                          // 终态后回调，业务页借此刷新单据状态
```

**提交通道（plan 阶段裁决，防快照信任漏洞）**：**不设通用 submit 端点**。提交永远走业务模块自己的后端端点（黄金模板第 2/3 条：服务端从已持久化单据构建 snapshot 再调 `IApprovalService.SubmitAsync`）——若开放客户端直传 snapshot 的通用端点，恶意客户端可篡改 snapshot 操纵条件选流程绕开高链。ApprovalPanel 经 `submit-handler` prop 触发业务端点，办完自动 refresh；不传 submitHandler 则不渲染提交按钮（SFS 实例模式无需，发起走 FormInitiate）。

### 4.2 动作模型：引擎动词 vs 业务动词

**核心区分**——按钮语义分两层，防止新的强绑定：

- **引擎动词**（语义固定，FlowEngine 已实现）：`approve / reject / sendBack / transfer / revoke`。审计轨迹、计票、幂等全走既有路径。
- **业务动词**（取消、重试、反冲、挂起……）：引擎不认识，也**不应该**认识。面板只渲染和调 handler。

```ts
interface ApprovalAction {
  key: string                    // 'approve' | 'cancel' | 'retry' | 任意
  labelKey: string               // i18n 词条（按钮叫"承認"还是"批准放行"随业务定）
  kind: 'engine' | 'business'
  engineVerb?: 'approve'|'reject'|'sendBack'|'transfer'|'revoke'  // kind=engine 必填
  appearance?: 'primary'|'danger'|'default'
  confirmText?: string           // 二次确认文案
  commentRequired?: boolean      // 强制填意见
  when?: (ctx: ApprovalCtx) => boolean   // 显隐条件（拿 status/myTask/快照）
  handler?: (ctx: ApprovalCtx) => Promise<void>  // kind=business 必填，办完自动 refresh
}
```

- **默认集** = 标准四件套（同意/驳回/退回/转办），不传 `actions` 零配置可用；传了完全接管。
- 极端定制留 `#actions` 插槽（业务自画整个按钮区，仍可用 useApproval 的动作方法）。
- **配置位置本期 = 代码侧 props**（类型安全、可测，贴合"客制化=代码"定位）。演进方向记录不实现：①绑定表 ActionsJson 零代码配置动作 ②流程节点级动作白名单（动引擎+设计器，属 WFS 深化范畴）。

### 4.3 `<ApprovalPanel>` 组件

```vue
<ApprovalPanel biz-type="PO" :biz-id="order.id" :submit-handler="submitForApproval"
               :actions="poActions" @decided="reload" />
<!-- 或 SFS/实例模式 -->
<ApprovalPanel :instance-id="detail.instanceId" />
```

四态渲染矩阵：

| 状态 × 身份 | 面板呈现 |
|---|---|
| 无实例 / 已驳回 / **已撤回(Withdrawn)** | 「提交审批」按钮（调 submit-handler 触发业务端点；canSubmit=false 或未传 handler 则隐藏） |
| 审批中 × 我是办理人 | 意见框 + 动作按钮组（描述符驱动）|
| 审批中 × 旁观者 | 只读状态条 + 当前节点 + 办理人 |
| **挂起(Suspended)** | 旁观条变体 + 挂起原因提示（审批人解析失败等），不出办理区 |
| 已通过 | 结果徽章 |

**canSubmit 口径 = None / Rejected / Withdrawn**（撤回后必须能重新发起，否则单据卡死）。Suspended/Running 一律 false。实现不得让 Withdrawn/Suspended 掉进 default 分支。

时间线：**有实例即恒显示**（含审批中/已通过/已驳回/已撤回/挂起），与状态矩阵正交。`revoke` 动词仅对发起人且流程未完结时可用（沿用 TaskCenterService 既有撤回闸，面板按 ctx 自动显隐）。

- 时间线复用现有 `FlowTimeline.vue`；`TransferDialog`/`SendBackDialog` **收编进 WFS 组件族**（`components/approval/`，本来就是审批语义）。
- Cp* 设计系统 token；五语 i18n 词条随种子入库。

### 4.4 FormDetail 改造（SFS 按钮写死 → 归还 WFS）

- 删除 FormDetail 现有写死的同意/驳回/退回/转办按钮区与相应散装逻辑，替换为 `<ApprovalPanel :instance-id>`。
- FormDetail 职责收敛为：**SFS 表单只读渲染（DynamicForm + fieldMask + rules 显隐，见 §6.2）+ FlowTimeline 由 Panel 承载**。
- 三个已接后端的业务模块（采购/凭证/预算）前端同步换装 ApprovalPanel——它们是套件第一批用户兼验收场景。

---

## 5. 收件箱深链改造

- 收件箱行 DTO 增 `detailRoute`：后端按 BizType join 绑定表，`{bizId}` 代入后下发成品路由。
- 前端 InboxPending/Running/Done 点击：`detailRoute` 非空 → `router.push(detailRoute)`；为空 → 现有 FormDetail。**SFS 单据零感知、存量待办零迁移**（老数据 join 不到路由自然走老路）。
- 待办徽章 / 已读标记 / SignalR 推送全部不动（任务状态在后端，业务页办理后收件箱自然同步）。
- **权限边界（故意设计）**：深链目标页的访问权限归业务模块自己的 `[RequirePermission]`——审批人若无业务页权限则 403。审批人本就该有单据查看权，权限缺失应暴露而非绕过。接入 checklist（§8）含此确认项。

---

## 6. 两个正确性修复

### 6.1 旧单渲染错位（快照方案，被代码事实锁定）

- 事实：`FormService.SaveDefAsync` 原地覆盖 `SchemaJson`（仅 `Version++`），历史 schema 不可回查——"按 FormVersion 取历史版"没有数据基础。
- 修复：`Wf_FormData` 增列 `SchemaSnapshotJson nvarchar(max) NULL`；`SubmitDataAsync` 落库时把当时 `FormDef.SchemaJson` 一并定格。渲染侧（InboxService 详情投影）**优先用快照**；快照为空的存量老单回落当前 FormDef（=现状行为，不更坏）。
- 代价与取舍：每单冗余几 KB schema，换历史单据永远按提交时的样子呈现（docs/oa 01/08 章反复警告的坑就此闭合）。

### 6.2 FormDetail 丢弃 rules

- 修复：详情解析 schema 不再丢 rules；用现成 `applyRules(schema, dataJson)` 求 visible 效果，**条件隐藏字段在只读视图不渲染**。
- 边界：required/disabled 在只读态无意义不应用；compute 不重算（数据提交时已服务端定格）。
- 效果：审批人所见 = 发起人所见，堵住信息暴露面。
- **交叉引用（稟議書打印）**：四期打印 spec 的字段表格「按 FormSchema 字段序」渲染，同样必须走**同一份"快照(§6.1)+applyRules"解析投影**——条件隐藏字段印在纸上归档比屏幕暴露更严重。两边出 plan 时对齐：只读投影抽成共用函数，打印视图是它的第二个消费者（本条同步记入打印 plan 的前置注记）。

---

## 7. 错误处理

沿用 E-WF 错误码族 + 既有 i18n 种子模式（五语词条随迁移种子入库）。**码号本 spec 锁定**（现有水位：四期已用到 E-WF-030，本包从 031 起）：

| 码 | 场景 | 语义 |
|---|---|---|
| **E-WF-031** | 绑定缺失 / 绑定停用（Enable=false） | 提交拒绝（现状 InvalidOperationException 升级为编码错误） |
| **E-WF-032** | ConditionJson 解析失败 | 提交拒绝（fail-closed） |
| **E-WF-033** | 条件表达式求值异常 | 提交拒绝（fail-closed） |
| **E-WF-034** | 解析出的 FlowKey（含条件命中与主 key 兜底）不存在/停用 | 提交拒绝（fail-closed）；四期 pin 落地后此判定切 E-WF-029 口径（§3.1 前向条款） |
| **E-WF-035** | 绑定删除被拒：已被流程实例引用 | 管理 UI 提示改为停用 |
| — | 同单据重复提交 | 已有防重闸，保持 |
| — | 非办理人调 act | 引擎已有闸；聚合端点对旁观者返回 myTask=null，前端不出办理区 |
| — | 聚合端点双键都传/都不传 | 400；未命中可见性 → 403（§3.3） |

---

## 8. 接入黄金模板（文档交付物）

新业务模块接审批的完整 checklist（随 spec 落 `docs/oa/11-approval-integration.md`，体例照 docs/oa 丛书）：

1. 后端：实现 `IApprovalCallback`（铁律照抄 IApprovalCallback 注释：幂等、失败抛异常触发整体回滚、不自行 SaveChanges）→ DI 注册。
2. 后端：业务提交口调 `IApprovalService.SubmitAsync(bizType, bizId, snapshot)`；snapshot 字段即条件选流程与流程条件边的变量面，**字段名一旦被流程引用即为契约，改名要过流程定义排查**。
3. 后端：**调 submit 前确保单据已持久化、快照取自已保存数据**——快照若取自未落库的内存态，与回调落库时按 bizId 查到的数据不一致，条件选流程与审批人所见都会错。这是业务侧最容易踩的坑。
4. 配置：FlowAdmin 建绑定（BizType/FlowKey/DetailRoute/条件规则）+ 确认审批角色有 DetailRoute 目标页 view 权限。
5. 前端：详情页放 `<ApprovalPanel>`（需要自定义按钮则传 actions 描述符）。
6. 验收：提交 → 收件箱深链 → 业务页办理 → 回调落库全链路。

---

## 9. 测试

**后端**：
- 条件选流程：首中即选 / 全不中回落主 FlowKey / ConditionJson 解析失败拒绝 / 表达式异常拒绝 / 选中 FlowKey 停用拒绝 / 主 FlowKey 停用即拒绝（即使条件已命中，有意从严）。
- 聚合端点授权：发起人可见 / 历史办理人可见 / 被抄送人可见 / 无关用户 403 / 旁观者（myTask=null）/ 无实例骨架（canSubmit 口径含 Withdrawn）/ 双键校验 400。
- 绑定生命周期：Enable=false 提交拒绝但 detailRoute 照常下发、在途任务照常可办 / 已被实例引用的绑定删除被拒（E-WF-035）/ 未引用可删。
- 绑定保存校验：表达式语法 / flowKey 启用性 / BizType 唯一。
- SchemaSnapshot：提交落快照 / 改版后旧单回显快照 / 存量空快照回落当前版。
- 集成：照抄 Pur/Fin 既有 harness 模式（`PurApprovalIntegrationTests` 同构）。

**前端**：
- useApproval：双键模式 / 动作分发（engine 走 act、business 走 handler）/ refresh 时序 / onDecided 触发。
- ApprovalPanel：四态渲染矩阵 / 默认动作集 / 自定义描述符（when 显隐、confirmText、commentRequired）/ #actions 插槽。
- FormDetail：rules 显隐生效（条件隐藏字段不渲染）。
- ——顺带开始偿还"前端审批相关零测试"欠账。

**QA（真库）**：采购单业务页提交 → 条件选流程命中 → 收件箱深链 → 业务页 ApprovalPanel 办理 → 回调落库；SFS 表单改版后旧单回显快照；FormDetail 换装 ApprovalPanel 后四动作回归。

**基线**：后端 1565 / 前端 369 全绿不许跌；type-check 0。

---

## 10. 范围外（记录不做）

- SFS 产品面缺口：子表格明细 / 附件控件 / 设计器拖拽与草稿发布 / JSON 字段查询报表 → **另立项**（OA 低代码商业化深化）。
- 绑定表 ActionsJson 零代码动作配置、流程节点级动作白名单 → 演进方向，等真需求。
- `{bizId}` 之外的路由占位符、多实例并存语义 → YAGNI。
- **历史轮次时间线**：驳回重提后"取最新实例"意味着面板只显示本轮轨迹，上一轮驳回意见不可见——与"多实例并存"不是一回事，是同一单据的串行轮次。有真实回溯需求时再做（数据都在 Wf_FlowInstance，按 bizType+bizId 可查全轮次）。
- 收件箱摘要卡（业务字段标注渲染）→ 已被"业务页面办理"决策取代，不做。

## 11. 排期定位

本包不依赖 WFS 深化二三四期任何任务，可独立执行；建议作为「WFS 开工令」前的独立小包，或并入二期首波——plan 阶段用户拍板。执行照既定 SDD 流程（写计划 → 分支 → 逐任务实现+审查 → fable 终审 → 合并即推）。

**串行约束（写死，防并行冲突）：**

- **与三期 inbox-ux 串行**：本包 §4.4 大改 FormDetail，三期 X-C（移动端）同样改 FormDetail。**本包先行**，inbox-ux X 波开工时以换装 ApprovalPanel 后的 FormDetail 为基线（打印 spec 已有"排 X-C 之后"的同类写法）。
- **与四期版本治理的接缝**：FlowKey 可发起性 resolver 单点收敛（§3.1 前向条款），四期 V-A 落地时改一处即可。
- **与四期打印的接缝**：只读投影共用函数（§6.2 交叉引用），打印 plan 含前置注记。
