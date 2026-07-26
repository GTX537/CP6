# OA P0-5：PUR_PR 业务审批样板

日期：2026-07-23  
状态：Frozen  
优先级：P0  
前置：P0-1、P0-4  

## 1. 目标

以采购申请 `PUR_PR` 证明代码客制化业务可以安全接入 WFS：

```text
PR 草稿
→ 业务后端送审并构造可信快照
→ Wf_ApprovalBinding 选 Published 流程
→ OA 待办
→ 深链回 PR 详情
→ ApprovalPanel 办理
→ WFS 终态回调
→ PR Approved 或退回 Draft
```

这个样板是后续 PO、预算、凭证等模块的复制模板，不在 P0 内同时改造所有业务。

## 2. 当前基础与缺口

已经存在：

- `PurchaseRequestService.SubmitForApprovalAsync`。
- `ApprovalServiceAdapter`。
- `Wf_ApprovalBinding` 的 `PUR_PR` 种子。
- `PrApprovalCallback`，通过后 Approved、驳回后 Draft。
- 后端集成测试基础。

缺口：

- Adapter 在绑定缺失时自动批准，配置删除可绕过审批。
- 当前 snapshot 只有 `amount=0` 和 submitter，不能支撑真实选流。
- 送审与 PR 状态更新没有显式事务，启动后第二次 SaveChanges 失败会产生状态裂缝。
- 浏览器通用 Approval submit 仍可伪造快照。
- 绑定没有 DetailRoute 和条件选流运行语义。
- PR 详情没有 ApprovalPanel，收件箱不能深链到具体单据。

## 3. 业务提交契约

业务浏览器只调用：

```http
POST /api/pur/pr/{prNo}/submit
```

控制器从登录上下文取用户；业务服务从数据库重新读取 PR 和 Lines，浏览器不能传 approval snapshot。

采购侧端口收敛为：

```csharp
public sealed class ApprovalSubmitRequest
{
    public string BizType { get; init; } = "";
    public string BizKey { get; init; } = "";
    public Guid ActorId { get; init; }
    public object Snapshot { get; init; } = new();
}

Task<PurchaseRequest> SubmitForApprovalAsync(
    string prNo,
    Guid actorId,
    string? userName);
```

`ApprovalServiceAdapter` 直接使用 ActorId 和 Snapshot，不再按用户名二次查用户，也不再生成 `Guid.Empty`。

### 3.1 snapshot v1

```json
{
  "snapshotVersion": 1,
  "prNo": "PR202607230001",
  "requesterId": "alice",
  "deptId": "guid-or-null",
  "requestDate": "2026-07-23",
  "source": "Manual",
  "lineCount": 3,
  "totalEstimatedAmount": 12500.00,
  "hasUnpricedLines": false,
  "suggestedSupplierCount": 2
}
```

计算口径：

- `totalEstimatedAmount = Σ(qty * estPrice)`；任一 `estPrice=null` 时对应行按 0 累加且 `hasUnpricedLines=true`。
- `suggestedSupplierCount` 为非空建议供应商去重数量。
- 字段名是流程条件契约。改名必须先扫描 Published FlowDefVersion 的表达式引用。

### 3.2 原子顺序

`PurchaseRequestService.SubmitForApprovalAsync` 必须在显式数据库事务中：

1. 加载并校验 PR 为 Draft，加载全部有效 Lines。
2. 校验当前用户可提交该 PR。
3. 把 PR 暂置 `Submitted`，使立即终态流程的 callback 能看到正确前置状态。
4. 构造 snapshot v1。
5. 调用 WFS `IApprovalService.SubmitAsync("PUR_PR", prNo, actorId, snapshot)`。
6. 写 `ApprovalRef=instanceId`。
7. 若流程立即通过/驳回，callback 可在同一 DbContext 把状态进一步改为 Approved/Draft。
8. 一次提交并 commit。

任何异常整体回滚，不允许“流程已启动但 PR 仍为 Draft”。

ActorId 必须来自 `ICurrentPermissionContext` 且在当前租户有效；否则拒绝提交，不再使用 `Guid.Empty`。

## 4. 绑定和条件选流

`Wf_ApprovalBinding` 新增：

```csharp
public string? DetailRoute { get; set; } // "/pur/pr?prNo={bizId}"
```

`ConditionJson` 语义沿用已确认设计：

```json
[
  {"when": "totalEstimatedAmount > 100000", "flowKey": "pur-pr-high"},
  {"when": "totalEstimatedAmount > 10000",  "flowKey": "pur-pr-mid"}
]
```

解析规则：

1. 顺序求值，首中即选。
2. 全不中使用主 FlowKey。
3. JSON 解析、表达式求值、选中流程不存在/停用/无 Published 版本全部 fail-closed。
4. 主 FlowKey 无效时即使条件命中其他流程也拒绝，因为兜底契约已损坏。
5. 选中的 FlowDefVersion 在提交时 pin 到实例。

绑定缺失或停用必须拒绝送审。业务若无需审批，业务代码不应调用 `IApprovalService`；不得以“没有配置”表达自动批准。

### 4.1 防重复

除服务层查询外，数据库增加活跃审批唯一闸：

```sql
CREATE UNIQUE INDEX UX_Wf_FlowInstance_ActiveBusiness
ON Wf_FlowInstance(TenantId, BizType, BizId)
WHERE BizType IS NOT NULL AND BizId IS NOT NULL AND Status IN (0, 4);
```

Running 和 Suspended 都视为活跃。并发双击只能创建一个实例；失败请求返回现有 active instanceId 或稳定冲突错误。

## 5. ApprovalPanel

新增公共组件：

```vue
<ApprovalPanel
  biz-type="PUR_PR"
  :biz-id="detail.prNo"
  :submit-handler="submitForApproval"
  @decided="reloadDetail"
/>
```

组件消费：

```http
GET /api/oa/approval/detail?bizType=PUR_PR&bizId={prNo}
```

返回状态、instanceId、myTask、timeline、canSubmit、detailRoute。授权按 P0-4。

状态矩阵：

| PR/OA 状态 | 呈现 |
|---|---|
| Draft + 无实例/已驳回/已撤回 | 送审按钮 |
| Submitted + 我有 Pending task | 意见 + 同意/驳回/退回/转办 |
| Submitted + 我是参与者但无 task | 只读状态与时间线 |
| Approved | 已通过与时间线 |
| Suspended | 挂起提示，无办理按钮 |

引擎动作仍走 WFS task 端点；PR 页面不自行实现审批状态机。

## 6. 收件箱深链

Inbox 行 DTO 新增服务端渲染后的：

```json
{
  "detailRoute": "/pur/pr?prNo=PR202607230001"
}
```

规则：

- 仅替换 `{bizId}`，并做 URL 编码。
- `DetailRoute` 为空时走 SFS FormDetail。
- 绑定停用只阻止新送审，不移除在途实例的路由。
- 已被实例引用的绑定不得物理删除，只能停用。

`PrView.vue` 在 mounted 和 route query 变化时读取 `prNo`，加载并打开对应详情。刷新深链后仍能恢复详情。

## 7. 采购权限

- PR 列表与详情 GET 增加 `pur-pr:query`。
- 送审保持 `pur-pr:submit`。
- ApprovalPanel task 决策需要 `oa-inbox:approve` 且必须拥有 task。
- 首批试点审批角色必须同时获得 `pur-pr:query`，否则深链 403；这是配置错误，应在上线前权限矩阵测试发现。
- P0 不让 OA 参与者自动绕过采购业务权限。

## 8. 回调

`PrApprovalCallback` 保持业务拥有状态机：

- Approved：仅 `Submitted -> Approved`，其他状态 no-op，幂等。
- Rejected：仅 `Submitted -> Draft`，审批意见留在 WFS history。
- callback 不调用 SaveChanges，由引擎统一提交。
- callback 抛异常时，流程终态与 PR 状态一起回滚。

重复 task 决定不会再次 dispatch；即使 callback 被故障注入重复调用，状态守卫也不产生第二副作用。

## 9. 通用入口收口

- 删除前端或外部对 `POST /api/wf/approval/submit` 的依赖。
- `ApprovalServiceAdapter` 不再检查“无 binding 自动通过”，直接委托 WFS；WFS 对缺失/停用 binding 返回 E-WF-031。
- `POST /api/wf/flow/submit` 不能用于 PUR_PR。
- 状态查询改用受 P0-4 保护的 Approval detail，不使用开放 status 端点。

## 10. 错误码

| 码 | 场景 |
|---|---|
| E-WF-031 | PUR_PR binding 缺失或停用 |
| E-WF-032 | ConditionJson 解析失败 |
| E-WF-033 | 条件表达式求值失败 |
| E-WF-034 | 主/命中流程无可用 Published 版本 |
| E-WF-035 | 尝试删除已被实例引用的 binding |
| E-PUR-052 | PR 不是 Draft，不能送审 |
| E-PUR-056 | PR 不存在 |
| E-PUR-057 | 当前登录用户无法映射为有效 Sys_User |
| E-PUR-058 | PR 已有 Running/Suspended 审批实例 |

## 11. 验收标准

1. PR 页面送审只传 prNo，不传 BizType、FlowKey 或 snapshot。
2. 服务端 snapshot 数值和行数来自数据库，客户端无法篡改。
3. 缺失/停用 binding 时送审失败，PR 保持 Draft。
4. 条件选流按 totalEstimatedAmount 命中正确 Published 版本。
5. 送审成功后 PR=Submitted、ApprovalRef 指向唯一实例。
6. 并发双击只生成一个 active instance。
7. 收件箱点击 PUR_PR 待办深链到对应 PR 详情，刷新 URL 可恢复。
8. 当前审批人在 PR 页面看见 ApprovalPanel 并完成办理。
9. 通过后 PR=Approved；驳回后 PR=Draft；流程轨迹完整。
10. callback 失败时流程终态和 PR 状态同时回滚。
11. 无采购查看权限的用户即使知道 prNo 也不能读取 PR。
12. 无实例参与关系的用户不能通过 Approval detail 读取轨迹。

## 12. 测试

| 层 | 覆盖 |
|---|---|
| 单元 | snapshot 计算、条件顺序、binding fail-closed、callback 幂等 |
| 集成 | 送审事务、立即终态、callback 回滚、active unique race |
| 控制器 | 采购权限、登录用户映射、通用入口禁用 |
| 前端 | query 深链恢复、ApprovalPanel 状态矩阵、办理后刷新 |
| E2E | 提交人→审批人→收件箱→PR详情→通过/驳回→状态回写 |

E2E 至少使用三个账号：提交人、审批人、无关用户。

## 13. 接入模板产物

样板完成后同步一份 `docs/approval/11-business-integration-checklist.md`：

1. 业务后端端点和权限。
2. 服务端 snapshot 契约。
3. binding、DetailRoute 和条件配置。
4. callback 状态机与幂等。
5. ApprovalPanel 接入。
6. 收件箱深链。
7. 权限矩阵与 E2E。

PO、预算、凭证必须按该 checklist 接入，不复制 PR 专用逻辑到 WFS。

## 14. 回滚

- `PurPrApprovalPanel` feature flag 可恢复旧 PR 详情 UI，但后端 fail-closed 和通用入口禁用不回退。
- binding 停用会阻止新送审；在途实例继续办理。
- PR callback 与原状态字段兼容，前端回滚不影响已运行实例。

## 15. 文件影响

| 文件/区域 | 变化 |
|---|---|
| `CP6.Entity/DomainModels/Wf/Wf_ApprovalBinding.cs` | DetailRoute |
| `CP6.Core/Services/Wf/ApprovalService.cs` | 条件选流、Published 解析、fail-closed |
| `CP6.Core/Services/Pur/Contracts/ApprovalServiceAdapter.cs` | 移除无 binding 自动批准 |
| `CP6.Core/Services/Pur/Contracts/IApprovalService.cs` | 请求增加 ActorId 和可信 Snapshot，废弃 AutoApproved 语义 |
| `CP6.Core/Services/Pur/PurchaseRequestService.cs` | 可信 snapshot 和显式事务 |
| `CP6.Core/Services/Pur/PurApprovalCallback.cs` | 幂等回归锁定 |
| `CP6.WebApi/Controllers/Pur/PurchaseRequestController.cs` | query 权限和错误码 |
| `CP6.Core/Services/Oa/InboxService.cs` | DetailRoute 投影 |
| `cp6.web/src/components/approval/` | useApproval、ApprovalPanel、对话框 |
| `cp6.web/src/views/pur/PrView.vue` | 深链和统一审批面 |
| `CP6.Tests/Pur`、前端 tests、E2E | 样板闭环 |

## 16. 范围外

- PO、预算、凭证前端改造。
- 绑定 ActionsJson。
- 多实例并存和历史轮次合并时间线。
- OA 自动授予业务页面权限。

## 17. 相对工作量

- Approval binding/aggregate：中到大。
- PR 服务事务和 snapshot：中。
- ApprovalPanel 与深链：大。
- 权限与 E2E：中到大。
