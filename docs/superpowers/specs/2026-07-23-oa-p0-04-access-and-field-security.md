# OA P0-4：实例与字段级授权

日期：2026-07-23  
状态：Frozen  
优先级：P0  
前置：P0-1；SFS 字段写入部分依赖 P0-2  

## 1. 目标

每一次实例列表、搜索、详情、状态查询和表单字段读取都必须由服务端回答两个问题：

1. 当前用户是否可以看到这条实例？
2. 可以看到哪些字段，又可以修改哪些字段？

前端隐藏按钮和字段不是安全措施。API 在序列化前必须完成裁剪，写入时必须重新授权。

## 2. 当前漏洞面

| 入口 | 当前行为 | 问题 |
|---|---|---|
| `GET /api/oa/inbox/detail/{id}` | 只验证 acting-as 头是否合法 | 无关用户可按 GUID 读取 |
| `GET /api/wf/flow/instance/{id}` | 任意认证用户可取 Instance/History/Tasks | 绕过 Inbox detail |
| `POST /api/oa/query/search` | 从租户全部实例查询 | 横向读取 |
| `GET /api/wf/approval/status` | 任意认证用户按 BizType/BizId 查询 | 业务审批信息泄露 |
| InboxDetail DTO | 直接包含 `Wf_FlowInstance` 和完整 VarsJson | 即使 UI 隐藏字段，响应仍泄露 |
| FormDetail | 所有字段构造 readonly mask | 节点 hidden/edit 未落地 |

## 3. 实例可见性

新增统一 `IOaInstanceAccessService`：

```csharp
public interface IOaInstanceAccessService
{
    Task<InstanceAccessDecision> GetAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        Guid instanceId,
        CancellationToken ct = default);

    IQueryable<Guid> VisibleInstanceIds(
        Guid effectiveUserId);
}
```

普通详情可见集合：

```text
Starter
∪ Wf_FlowTask.AssigneeId
∪ Wf_FlowFormTo.ExpectedHandlerId
∪ Wf_FlowFormTo.ActualHandlerId
∪ Wf_FlowFormTo.OnBehalfOfId
∪ Wf_FlowCc.RecipientId
```

规则：

- 任意一项命中即可读实例元数据和授权后的内容。
- acting-as 时，actualUser 必须先有有效委派，实例参与者判断使用 effectiveUser。
- 管理员不自动进入普通详情集合。管理员排障走后续 `oa-flow-ops:view` 专用入口并记录审计。
- 未授权详情返回 403，不返回字段、流程名、状态或“是否存在”的更多内容。
- 读权限不等于办理权。办理仍要求 pending task 所有权或有效委派。

## 4. 查询授权

`FormQueryFilter` 增加 page/pageSize，但不接受调用者身份：

```csharp
public record FormQueryFilter(
    string? FlowKey,
    string? Keyword,
    int? Status,
    DateTime? FromUtc,
    DateTime? ToUtc,
    int Page = 1,
    int PageSize = 20);
```

查询顺序：

1. 先与 `VisibleInstanceIds(effectiveUserId)` 相交。
2. 再应用 FlowKey、状态、日期和关键词。
3. 最后排序、Count、Skip、Take。

删除“客户端任意 StarterId/HandlerId”的默认查询能力。若保留 UI 筛选，只能在已授权集合内筛选，不能扩大范围。`pageSize` 最大 100，移除 `Take(500)`。

收件箱 pending/running/done/cc 已按用户过滤，但仍应复用统一授权方法生成详情链接和行 DTO，避免之后新增入口再次漏闸。

## 5. 显式详情 DTO

不得再返回 EF 实体：

```json
{
  "instance": {
    "id": "guid",
    "flowKey": "leave",
    "flowName": "请假审批",
    "flowVersion": 2,
    "status": 0,
    "currentNodeId": "manager",
    "currentNodeName": "直属主管",
    "starter": {"id": "guid", "name": "Alice"},
    "createdAtUtc": "..."
  },
  "content": {
    "kind": "sfs",
    "formDataId": "guid",
    "formKey": "leave",
    "formVersion": 3,
    "schema": {"fields": []},
    "data": {},
    "fieldMask": {"reason": "readonly", "salary": "hidden"}
  },
  "myTask": {
    "taskId": "guid",
    "nodeId": "manager",
    "fieldMask": {}
  },
  "timeline": [],
  "cc": [],
  "subflows": []
}
```

业务实例的 `content.kind="business"`，只返回 `bizType/bizId/detailRoute`，不返回完整业务快照 `VarsJson`。业务字段由业务详情 API 自己授权和呈现。

## 6. 字段读取算法

字段权限源为 pinned FlowDefVersion 当前/历史节点中的 `fieldPerms`；表单字段来自 pinned FormDefVersion。

### 6.1 身份到字段视图

| 身份 | 使用的节点权限 | 写权限 |
|---|---|---|
| 当前 Pending Task 办理人 | 当前 task 的 NodeId | `edit` 可写 |
| 发起人 | 所有字段 readonly | 无；若发起人同时有当前 task，则按 task 提升 |
| 历史办理人 | 其曾参与节点的可见字段并集 | 无 |
| 抄送人 | `Wf_FlowCc.AtNodeId` 的可见字段 | 无 |
| 多身份命中 | 可见字段取并集；任一当前 task 标 edit 才可写 | 仅当前 task |

节点未配置字段的默认值：

- 审批节点：`readonly`。
- 显式 `hidden`：该节点身份下不可见。
- `edit`：只有当前 Pending Task 的实际办理上下文可编辑。

### 6.2 服务端裁剪

1. 先执行表单 rules 的 show/hide，得到业务显隐结果。
2. 再应用访问者字段权限。
3. 任一层 hidden 的字段从 schema fields、rules 输出和 DataJson 中移除。
4. 不把 hidden 值以 null、脱敏字符串或其他 key 返回。
5. rules 中引用被裁剪字段时，服务端先在完整数据上求值，再裁剪结果；客户端不需要拿到条件源字段。
6. `Wf_FlowData.DataJson` 节点快照不直接下发；时间线若展示历史表单快照，统一按当前 viewer 的最终可见字段集合重新投影。

历史 FormData 无准确 schema 的 `legacy-fallback`：

- 只有发起人和历史办理人可读。
- 全字段 readonly。
- 如果无法可靠计算 hidden，默认不返回 DataJson，只返回时间线和兼容警告；不得为了兼容而扩大暴露。

## 7. 字段写入与办理

新增单任务决定端点，FormDetail 不再通过 batch 接口办理带编辑字段的任务：

```http
POST /api/oa/tasks/{taskId}/decision
```

请求：

```json
{
  "decision": "approve",
  "comment": "同意",
  "dataPatch": {
    "approvedDays": 2
  },
  "expectedFormDataRowVersion": "base64"
}
```

服务端顺序：

1. 校验 task Pending、实例 Running、actor 所有权或有效委派。
2. 读取 pinned flow/form versions。
3. 计算当前节点 fieldMask。
4. `dataPatch` 每个 key 必须为 `edit`；readonly/hidden/未知字段返回 403。
5. 合并 patch，执行完整服务端规则复算和校验。
6. 更新 `Wf_FormData.DataJson/RowVersion` 与实例 `VarsJson`。
7. 追加 `Wf_FlowData` 节点快照。
8. 调用引擎办理并在同一事务提交。

`dataPatch` 只适用于 `content.kind=sfs`。业务实例传 patch 返回 400，业务字段变更必须走业务服务。

Batch 办理保留，但：

- 不接受 dataPatch。
- 若任务当前节点存在任一 `edit` 字段，默认拒绝 batch，避免跳过必需字段编辑；可在未来节点配置显式允许。

## 8. 业务详情授权

ApprovalPanel 聚合端点与普通实例详情复用同一可见性：

```http
GET /api/oa/approval/detail?instanceId=...
GET /api/oa/approval/detail?bizType=PUR_PR&bizId=...
```

- 双键二选一。
- 有实例时先做参与者授权。
- 无实例时只返回 `{status:None, canSubmit}`，业务页面本身仍需业务权限。
- 聚合端点只返回状态、myTask、timeline 和 detailRoute，不返回业务快照。

P0-5 的 PUR_PR 详情 GET 继续受 `pur-pr:query` 业务权限控制；审批角色必须配置该权限。P0 不在通用 OA 权限中旁路采购数据权限。

## 9. 权限矩阵

| 操作 | 发起人 | 当前办理人 | 历史办理人 | 抄送人 | 无关用户 | 管理员普通入口 |
|---|---:|---:|---:|---:|---:|---:|
| 看实例元数据 | 是 | 是 | 是 | 是 | 否 | 否 |
| 看时间线 | 是 | 是 | 是 | 是 | 否 | 否 |
| 看授权字段 | readonly | 节点 mask | 历史节点可见并集 | 抄送节点 mask | 否 | 否 |
| 改 edit 字段 | 仅当持有 task | 是 | 否 | 否 | 否 | 否 |
| 办理 task | 仅当持有 task | 是 | 否 | 否 | 否 | 否 |
| 查询命中该实例 | 是 | 是 | 是 | 是 | 否 | 否 |

## 10. 错误码

| 码 | HTTP | 场景 |
|---|---:|---|
| E-WF-043 | 403 | 当前用户无实例读取权 |
| E-WF-042 | 403 | dataPatch 写入 readonly/hidden/未知字段 |
| E-WF-049 | 409 | FormData RowVersion 冲突 |
| E-WF-047 | 400 | patch 后规则或字段校验失败 |
| E-WF-004 | 409 | task 已办或实例状态不允许 |

## 11. 验收标准

1. 无关用户访问 Inbox detail、Flow instance、Approval detail 和 status 均被拒绝。
2. 查询只返回参与者集合内实例，分页总数也不泄露全租户数量。
3. API 不再序列化 `Wf_FlowInstance`、`Wf_FlowTask` 等完整实体。
4. hidden 字段不出现在 schema、data、timeline snapshot 展示 DTO。
5. 当前办理人只能写当前节点标 edit 的字段。
6. 直接构造 HTTP patch 修改 readonly/hidden 字段返回 403，数据库不变。
7. 历史办理人和抄送人可以只读其合法可见字段，不能办理。
8. acting-as 必须同时满足有效委派和 effectiveUser 的实例参与者/任务所有权。
9. 并发字段编辑产生 409，不静默覆盖。
10. 业务实例详情不返回 VarsJson 业务快照。
11. 带 edit 字段的任务不能通过 batch 绕过编辑规则。
12. legacy-fallback 不因缺 schema 而默认返回全部数据。

## 12. 测试

| 层 | 覆盖 |
|---|---|
| 单元 | 可见性各身份、acting-as、字段 mask 合并、rules+mask 裁剪 |
| 集成 | 四个读取端点横向越权、查询分页、字段 patch 事务、RowVersion |
| 前端 | hidden 不渲染、edit 可编辑、readonly 置灰、业务 content 不显示空 SFS 表单 |
| E2E | 发起人/审批人/抄送人/无关用户四账号矩阵 |
| 安全回归 | 枚举 GUID、改 query filter、伪造 acting-as、直接 patch hidden 字段 |

## 13. 回滚

- 读取授权不得通过 feature flag 关闭；它是安全修复。
- 新 DTO 可通过 API version 并行一个发布周期，旧 DTO 只在受控开发环境启用。
- 字段编辑功能可单独关闭，关闭后所有字段 readonly，但 hidden 裁剪仍必须保留。

## 14. 文件影响

| 文件/区域 | 变化 |
|---|---|
| `CP6.Core/Services/Oa/InboxService.cs` | 所有读取接统一 access service；显式 DTO |
| `CP6.Core/Services/Oa/IInboxService.cs` | 方法签名必须带 viewer context |
| `CP6.Core/Services/Oa/` | 新 AccessService、FieldProjectionService、DecisionService |
| `CP6.WebApi/Controllers/Oa/{Inbox,Query}Controller.cs` | 传当前用户并处理 403/409 |
| `CP6.WebApi/Controllers/Wf/{Flow,Approval}Controller.cs` | 旧详情/status 收口 |
| `cp6.web/src/views/oa/inbox/FormDetail.vue` | 消费服务端 schema/data/mask；单任务 decision |
| `cp6.web/src/views/wf/DynamicForm.vue` | 正确处理 edit/readonly/hidden |
| `CP6.Tests/Oa` | 身份矩阵、横向越权和字段写入测试 |

## 15. 范围外

- 动态字段接入 PUB `Sys_RoleFieldPerm` 管理 UI。
- 字段脱敏格式。
- 管理员 FlowOps 详情旁路。
- 业务页面通用的“审批参与者即获得业务查看权”策略。

## 16. 相对工作量

- 统一可见性查询和 DTO：大。
- 字段投影：中到大。
- 字段 patch 与事务：大。
- 安全矩阵测试：大。
