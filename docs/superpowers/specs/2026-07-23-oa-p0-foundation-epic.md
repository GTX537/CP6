# OA P0 基础闭环 Epic

日期：2026-07-23  
状态：Frozen（2026-07-23 用户确认 D1–D6）  
范围：OA 工作台、WFS、SFS；组织树作为 PUB/IAM 依赖，不在本 Epic 内重建  

## 1. 结论

本 Epic 不继续扩充流程节点或表单控件，而是先解决五个会阻止生产使用的正确性问题：

1. 流程和表单定义必须不可变发布，运行实例必须锁定版本。
2. SFS 的保存、校验、落库、起流程必须只有一条服务端权威链。
3. 草稿属于 SFS 数据，不再伪装成未启动的流程实例。
4. 所有实例读取和字段读取必须经过服务端授权。
5. 以采购申请 `PUR_PR` 打通第一个真实业务审批闭环。

完成后 OA 的目标等级是“可控试点”，不是“功能完整”。子表、附件、报表、流程运维驾驶舱和组织模型深化继续留在后续 P1/P2。

## 2. 已验证现状

验证日期：2026-07-23。

| 面 | 当前事实 | 直接风险 | 代码证据 |
|---|---|---|---|
| 流程定义 | `SaveDefAsync` 按 `FlowKey` 原地覆盖 `SchemaJson`，只把 `Version++` | 在途实例读取新定义，节点、审批人和分支可漂移 | `CP6.Core/Services/Wf/FlowDefService.cs:13-45` |
| 流程运行 | `SubmitAsync` 与多个推进路径按 `FlowKey` 重新加载当前定义 | 发布修改会污染历史和在途实例 | `CP6.Core/Services/Wf/FlowEngine.cs:48-75`、`FlowEngine.Tokens.cs:214` |
| 表单定义 | `Wf_FormDef` 同样单行覆盖；旧 schema 不可回查 | 旧单据按新表单渲染 | `CP6.Core/Services/Wf/FormService.cs:22-52` |
| SFS 发起 | `FormInitiate` 直接调用 `flowApi.submit` | 绕过服务端表单复算、校验及 `Wf_FormData` 落库 | `cp6.web/src/views/oa/catalog/FormInitiate.vue:166-183` |
| 草稿 | 草稿保存为 `Wf_FlowInstance.Status=Draft`；列表 DTO 不返回 `VarsJson` | 编辑页可能从空 JSON 开始，提交也不走 SFS 校验 | `CP6.Core/Services/Oa/DraftService.cs:14-57`、`cp6.web/src/views/oa/inbox/InboxDraft.vue:89-112` |
| 实例详情 | Detail 只校验 act-as 头，不判断当前人是否属于实例 | 任意登录用户可按实例 ID 读取表单和完整轨迹 | `CP6.WebApi/Controllers/Oa/InboxController.cs:124-136` |
| 表单查询 | 查询从租户全部实例开始，最多 `Take(500)`，没有参与者范围 | 用户可搜索与自己无关的流程 | `CP6.Core/Services/Oa/InboxService.cs:283-305` |
| 字段权限 | Schema 已有 `fieldPerms`，主详情却构造“全部可见、全部只读” | `hidden` 没有在服务端或主 UI 生效 | `CP6.Core/Services/Wf/FlowSchema.cs:35-36`、`cp6.web/src/views/oa/inbox/FormDetail.vue:202-212` |
| 业务起审 | 已有 `IApprovalService`、绑定和回调，但存在接受客户端快照的通用提交端点 | 客户端可伪造 `BizType/BizId/formSnapshot` 影响选流 | `CP6.WebApi/Controllers/Wf/ApprovalController.cs:31-42` |
| 采购申请 | 后端已接 `PUR_PR`，前端详情仍是独立弹窗，无统一审批面 | 待办无法在业务上下文中可靠办理 | `CP6.Core/Services/Pur/PurchaseRequestService.cs:105-125`、`cp6.web/src/views/pur/PrView.vue:92-125` |

已有能力中，下列部分工作正常，本 Epic 不重写：

- WFS token、串签、会签、并行/包容网关、子流程、超时、服务任务和终态回调。
- `IApprovalCallback` 与流程终态共享 scoped DbContext 的原子回调方向。
- 多租户全局过滤、任务所有权校验、代理/转办/退回的现有运行语义。
- SFS 现有规则复算、required/type/maxLength/pattern 校验基础。

## 3. 目标架构

```text
PUB/IAM 组织与权限
       │
       ├──────────────┐
       ▼              ▼
   OA 工作台        业务模块
       │              │ 业务后端构造可信快照
       │              ▼
       │       IApprovalService
       │              │
       ▼              ▼
 SFS FormData ──可选绑定──> WFS Runtime
   │   │                    │
   │   └─ FormDefVersion    └─ FlowDefVersion
   │                              │
   └──────── 实例参与者授权 ──────┘
```

架构不变量：

1. `Wf_FlowDef`、`Wf_FormDef` 是稳定身份头表；不可变内容进入版本表。
2. WFS 不保存 `FormKey` 强绑定。SFS 是否启动流程由独立 `Wf_FormFlowBinding` 决定。
3. SFS 原生表单以 `Wf_FormData` 为数据真相；ERP/采购/财务以各自业务表为真相。
4. 浏览器不能指定可信的 `BizType`、`BizId`、`FlowKey` 或业务快照。
5. 实例详情永不直接序列化 `Wf_FlowInstance`；只返回显式 DTO 和服务端裁剪后的字段。
6. 组织树继续由 PUB/IAM 管理，OA 只消费用户、部门、直属主管、部门负责人和角色。

## 4. 本轮需要确认的设计决策

| ID | 建议决策 | 取舍 | 与旧规格关系 |
|---|---|---|---|
| D1 | 使用“头表 + 版本表”，不在 `Wf_FlowDef/Wf_FormDef` 内堆多版本行 | 多两张表和一次迁移，但稳定标识、启停、唯一约束和不可变版本职责清楚 | 替代 `2026-07-05-wfs-version-ops-design.md` §2 的同表多行方案 |
| D2 | 新增 `Wf_FormFlowBinding`，流程定义不再必填 `FormKey` | 真正解除 WFS 对 SFS 的依赖；原有 `FormKey` 列暂留兼容，不再作为运行真相 | 强化既有“OFS/SFS 解耦”方向 |
| D3 | 草稿迁移到独立 `Wf_FormDraft`；提交时才创建 `Wf_FormData` 和可选流程实例 | 多一张表，但草稿生命周期、版本和权限不再污染流程运行表 | 替代当前 `FlowInstance.Status=Draft` |
| D4 | 普通详情可见人仅为发起人、当前/历史办理人、抄送人；管理员通过专门运维入口，不旁路普通详情 | 最小权限；管理员排障路径需要后续 FlowOps | 沿用审批解耦规格 §3.3 |
| D5 | 禁用两个通用浏览器起审入口，SFS 走 SFS 提交端点，业务走各自业务端点 | 会收紧兼容面，但消除快照伪造和任意起流 | 强化审批解耦规格 §4.1 |
| D6 | 节点 `fieldPerms.edit` 在 P0 内落地为“办理时可提交字段补丁”；无编辑权限的字段服务端拒写 | 增加一条任务决定端点和并发控制，但字段权限不再只是 UI 装饰 | 补齐现有 D-1 未落地主链 |

若以上六项确认，子规格不再保留其他实现分叉。

## 5. 子规格

| 编号 | 子规格 | 结果 | 前置 |
|---|---|---|---|
| P0-1 | [定义版本化与运行锁定](./2026-07-23-oa-p0-01-definition-versioning.md) | Flow/Form 已发布版本不可变；实例 pin | 无 |
| P0-2 | [SFS 权威提交链](./2026-07-23-oa-p0-02-authoritative-submission.md) | 校验、落库、起流原子化；支持无流程表单 | P0-1 |
| P0-3 | [SFS 草稿生命周期](./2026-07-23-oa-p0-03-draft-lifecycle.md) | 草稿完整往返、版本过期处理、提交复用 P0-2 | P0-1、P0-2 |
| P0-4 | [实例与字段级授权](./2026-07-23-oa-p0-04-access-and-field-security.md) | 详情、查询、状态、附件和字段均服务端授权 | P0-1；与 P0-2 可交叠 |
| P0-5 | [PUR_PR 业务审批样板](./2026-07-23-oa-p0-05-pur-pr-approval-pilot.md) | 业务提交、深链、办理、回调、状态回写闭环 | P0-1、P0-4；消费 P0-2 的安全原则 |

依赖图：

```text
P0-1 版本地基
 ├──> P0-2 SFS 权威提交 ───> P0-3 草稿
 ├──> P0-4 授权与字段安全
 └──> P0-5 PUR_PR 样板 <──── P0-4
```

## 6. 跨规格状态与数据约定

### 6.1 定义状态

- `Draft=0`：可编辑，不参与运行。
- `Published=1`：不可变，可被新实例引用。
- 启停属于头表。`Enable=false` 只阻止新提交，不影响在途和历史读取。
- 回滚通过“从历史版本创建新草稿并再次发布”完成，不修改旧版本。

### 6.2 数据真相

| 内容类型 | 真相表 | 流程中保存什么 |
|---|---|---|
| SFS 原生表单 | `Wf_FormData` | `FormDataId` + 规范化 `VarsJson` 快照 |
| PUR_PR 等业务单据 | 业务表 | `BizType/BizId` + 服务端构建的条件快照 |
| 表单草稿 | `Wf_FormDraft` | 不创建流程实例 |

### 6.3 时间与并发

- 新增时间字段统一 UTC，API 使用 ISO-8601 UTC。
- 定义草稿、表单数据和表单草稿使用 `RowVersion` 乐观锁。
- 重复提交使用客户端生成、服务端唯一约束的 `submissionKey`，不得只靠“先查再插”。

## 7. 全局验收门槛

1. 发布流程 v2 后，运行中的 v1 实例继续按 v1 完成；新实例 pin v2。
2. 发布表单 v2 后，v1 历史数据仍按 v1 schema 显示。
3. Published 版本的 schema、名称快照和版本号不能被更新或删除。
4. SFS 提交不能绕过服务端规则复算和校验，且成功响应时 `Wf_FormData` 与流程实例同时存在或同时不存在。
5. 没有流程绑定的 SFS 表单仍可独立提交并查询自己的数据。
6. 草稿重新打开得到相同 schema 版本和完整数据；原始 JSON 编辑器从产品路径移除。
7. 无关用户无法通过详情、查询、状态或直接 URL 获取实例及字段。
8. `hidden` 字段不出现在 API 的 schema 或 data 中；`readonly` 字段的写入在服务端被拒绝。
9. `PUR_PR` 从业务页送审，经 OA 待办深链回业务页办理，终态回写采购状态。
10. 重复点击提交、重复回调和重复办理不产生第二实例或第二次业务副作用。
11. 所有现有 WFS 不变量测试继续通过；新增规格的单元、集成和 E2E 测试通过。
12. 数据迁移前置检查能列出孤儿定义、旧版 FormData 和遗留草稿；存在无法安全迁移的在途数据时部署必须失败。

## 8. 范围外

- 子表、附件控件、布局、可视化规则、字段查询导出。
- 流程实例跨版本迁移。
- FlowOps 驾驶舱、强制推进、强制终止、服务任务重放。
- 动态表单字段与 PUB 静态角色字段注册表的统一配置 UI。
- 组织岗位、主辅部门、虚线汇报、有效期任职。
- PR 之外的 PO、预算、凭证前端换装；它们在样板通过后复制。

## 9. 风险与回滚总则

- 所有数据库变更采用“新增表/新增列 → 双读验证 → 切换读取 → 后续清理”顺序，本 Epic 不直接删除旧定义列。
- 存量流程实例全部 pin 到迁移时实际正在使用的流程 schema，迁移后行为应逐字节等价。
- 无法恢复的历史表单 schema 不伪造：明确标记 `legacy-fallback`，按当前 schema 降级只读。
- 每个子规格独立 feature flag；回滚时关闭新写入口并恢复旧读入口，不回删已生成的版本和数据。

## 10. 规格冻结条件

本 Epic 与五个子规格完成一次用户确认后：

1. 状态改为 `Confirmed`。
2. 锁定 D1-D6，不在执行计划阶段重新选择数据模型。
3. 再生成独立执行计划，拆迁移、后端、前端、测试、真库 QA 和上线闸门。
