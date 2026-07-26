# OA P0-3：SFS 草稿生命周期

日期：2026-07-23  
状态：Frozen  
优先级：P0  
前置：P0-1、P0-2  

## 1. 目标

草稿是“尚未正式提交的 SFS 表单数据”，不是流程实例。用户必须能够：

- 保存完整草稿。
- 再次打开时按保存时版本正确渲染。
- 在并发编辑时得到冲突提示。
- 表单已发布新版时安全升级草稿。
- 提交时复用 P0-2 的服务端权威提交链。

## 2. 当前问题

当前草稿使用 `Wf_FlowInstance.Status=Draft`，因此：

- 创建草稿前就要求存在 FlowKey，SFS 无法独立。
- 列表 DTO `InboxRunningItem` 不含 VarsJson，而页面假设有 `row.varsJson`。
- 编辑器展示原始 JSON 文本。
- `StartDraftAsync` 直接推进流程，不调用表单服务端复算和校验。
- 草稿没有 FormVersion pin，也没有并发控制。

## 3. 数据模型

```sql
CREATE TABLE Wf_FormDraft (
    Id                    uniqueidentifier NOT NULL PRIMARY KEY,
    TenantId              uniqueidentifier NOT NULL,
    OwnerUserId           uniqueidentifier NOT NULL,
    FormDefId             uniqueidentifier NOT NULL,
    FormDefVersionId      uniqueidentifier NOT NULL,
    DataJson              nvarchar(max)    NOT NULL,
    Title                 nvarchar(200)    NULL,
    Status                int              NOT NULL, -- 0 Active, 1 Submitted
    SubmittedFormDataId   uniqueidentifier NULL,
    SubmittedAtUtc        datetime2        NULL,
    LegacyFlowInstanceId  uniqueidentifier NULL,
    RebasedFromVersionId  uniqueidentifier NULL,
    RowVersion            rowversion       NOT NULL,
    Creator               nvarchar(100)    NULL,
    CreateDate            datetime2        NOT NULL,
    Modifier              nvarchar(100)    NULL,
    ModifyDate            datetime2        NULL,
    CONSTRAINT FK_Wf_FormDraft_Form FOREIGN KEY (FormDefId) REFERENCES Wf_FormDef(Id),
    CONSTRAINT FK_Wf_FormDraft_Version FOREIGN KEY (FormDefVersionId) REFERENCES Wf_FormDefVersion(Id)
);

CREATE INDEX IX_Wf_FormDraft_Owner
ON Wf_FormDraft(TenantId, OwnerUserId, Status, ModifyDate DESC);

CREATE UNIQUE INDEX UX_Wf_FormDraft_Legacy
ON Wf_FormDraft(TenantId, LegacyFlowInstanceId)
WHERE LegacyFlowInstanceId IS NOT NULL;
```

草稿不创建 FlowInstance、token、task、history、notification。P0 只支持个人草稿，Owner 不可转让。列表只显示 `Status=Active`。

## 4. 保存语义

### 4.1 新建

```http
POST /api/oa/forms/{formKey}/drafts
```

请求：

```json
{
  "data": {"leaveType": "annual"},
  "title": "7月休假"
}
```

服务端解析该表单 latest Published，创建 Draft 并 pin `FormDefVersionId`。

草稿保存允许缺少 required 字段，但必须满足：

- JSON object。
- 字段名存在于 pinned schema。
- 值类型可解析。
- 请求大小和深度不超 P0-2 限额。

compute 可以在保存时预计算以保证再次打开一致，但正式提交仍必须全部重算。

### 4.2 更新

```http
PUT /api/oa/drafts/{draftId}
```

```json
{
  "data": {"leaveType": "annual", "days": 2},
  "title": "7月休假",
  "rowVersion": "base64"
}
```

Owner 不匹配返回 403；RowVersion 冲突返回 409。不得用 last-write-wins。

### 4.3 列表和详情

```http
GET /api/oa/drafts?page=1&pageSize=20
GET /api/oa/drafts/{draftId}
```

列表必须返回：

```json
{
  "id": "guid",
  "formKey": "leave",
  "formName": "请假申请",
  "formVersion": 2,
  "title": "7月休假",
  "updatedAtUtc": "...",
  "stale": false,
  "rowVersion": "base64"
}
```

详情返回 pinned schema 和完整 DataJson。页面使用 DynamicForm，不显示原始 JSON 编辑器。

## 5. 版本过期与 rebase

Draft pin 的版本不是 latest Published 时，`stale=true`。

过期草稿：

- 可以继续查看和编辑 pinned 版本。
- 不允许直接正式提交。
- 页面必须显示“表单已有新版本，请升级后提交”。

升级接口：

```http
POST /api/oa/drafts/{draftId}/rebase
```

请求：

```json
{
  "targetVersion": 3,
  "confirmRemovedValues": false,
  "rowVersion": "base64"
}
```

处理规则：

1. 目标必须是该 FormDef 的 latest Published。
2. 同名且兼容类型字段保留值。
3. 新字段使用 schema 默认值；没有默认值则为空。
4. 已删除或类型不兼容字段从新 DataJson 移除。
5. 被移除字段存在非空值且 `confirmRemovedValues=false` 时返回 409，并列出字段和值摘要；不修改草稿。
6. 确认后更新 FormDefVersionId、DataJson、RebasedFromVersionId 和 RowVersion。
7. rebase 不要求所有 required 已填写；正式提交时再完整校验。

响应：

```json
{
  "draftId": "guid",
  "formVersion": 3,
  "data": {"leaveType": "annual", "days": 2, "handover": null},
  "removedFields": ["legacyReason"],
  "validationErrors": [
    {"field": "handover", "code": "required", "messageKey": "oa.form.required"}
  ],
  "rowVersion": "base64"
}
```

## 6. 提交和删除

### 6.1 提交

```http
POST /api/oa/drafts/{draftId}/submit
Idempotency-Key: uuid
```

DraftService 只做 Owner、stale 和 RowVersion 检查，然后调用 P0-2 `IFormSubmissionService`。同一事务内：

1. 创建 FormData。
2. 可选创建流程实例。
3. 把 Draft 标为 Submitted，并记录 SubmittedFormDataId/SubmittedAtUtc。

任何步骤失败，Draft 保持 Active。API 超时后用相同 Idempotency-Key 重试时，Submitted Draft 返回原 FormData/实例结果，不产生第二次提交。Submitted Draft 不再出现在草稿列表，并由后续保留策略清理。

### 6.2 删除

```http
DELETE /api/oa/drafts/{draftId}
```

P0 为用户明确操作后的硬删除，不做回收站。只允许 Owner 删除 Active Draft；Submitted Draft 不能通过本端点删除，且不影响已提交数据。

## 7. 遗留草稿迁移

对 `Wf_FlowInstance.Status=Draft`：

1. 必须没有 token、task、history；存在则记录异常并阻止自动迁移。
2. 通过 FlowDef 旧 `FormKey` 解析 FormDef 和回填后的 Published FormVersion。
3. `VarsJson` 原样复制到 `Wf_FormDraft.DataJson`。
4. `LegacyFlowInstanceId` 记录来源，保证迁移幂等。
5. 原实例本次不删除，旧草稿列表停止读取；待一个发布周期核数后另立清理。
6. 无法解析 FormDef 的遗留草稿进入迁移报告，不允许静默丢失。

## 8. 错误码

| 码 | HTTP | 场景 |
|---|---:|---|
| E-WF-003 | 403/409 | 非 Owner 或不是有效草稿 |
| E-WF-040 | 409 | 草稿版本已过期，必须 rebase |
| E-WF-041 | 409 | 草稿 RowVersion 冲突 |
| E-WF-047 | 400 | 草稿 JSON 形态或字段类型非法 |
| E-WF-048 | 409 | rebase 将移除非空值，等待用户确认 |

## 9. 验收标准

1. 草稿表中不存在 FlowKey、FlowInstanceId 或流程状态。
2. 新建草稿不会生成任何流程运行数据。
3. 列表和详情返回完整数据，不再依赖 `InboxRunningItem`。
4. 编辑页面使用 DynamicForm，原始 JSON 编辑器从正常路径移除。
5. 重新打开草稿按 pinned FormVersion 渲染，字段值完整。
6. 两个窗口并发保存时，后写者收到 409。
7. 表单发布新版后旧草稿显示 stale 且不能直接提交。
8. rebase 对同名兼容字段保值，对删除字段有明确确认，不静默丢值。
9. 提交失败时 Draft 保持 Active；提交成功时 Draft=Submitted，FormData、FlowInstance 状态一致，重试返回原结果。
10. 遗留草稿迁移数量与原 Draft 实例数量核对一致，异常逐条输出。

## 10. 测试

| 层 | 覆盖 |
|---|---|
| 单元 | Owner、轻校验、stale 判定、rebase 字段合并和移除确认 |
| 集成 | RowVersion、提交事务、失败保留草稿、遗留迁移幂等 |
| 前端 | 列表、DynamicForm 编辑、冲突、stale banner、rebase 确认 |
| E2E | 保存→退出→恢复→发布新版→rebase→补字段→提交→草稿消失 |

## 11. 回滚

- 旧 Draft FlowInstance 暂不删除，可在 feature flag 回滚时重新展示。
- 新草稿表独立，不影响运行实例。
- 回滚前关闭新草稿写入口；已创建新草稿保持只读，待恢复后继续。

## 12. 文件影响

| 文件/区域 | 变化 |
|---|---|
| `CP6.Entity/DomainModels/Wf/` | 新 `Wf_FormDraft` |
| `CP6.Core/Services/Oa/DraftService.cs` | 改用 FormDraft，新增 rebase |
| `CP6.Core/Services/Oa/IDraftService.cs` | 新命令/响应模型 |
| `CP6.WebApi/Controllers/Oa/DraftController.cs` | REST 资源化和并发响应 |
| `cp6.web/src/views/oa/inbox/InboxDraft.vue` | DynamicForm 编辑、版本提示 |
| `cp6.web/src/views/oa/catalog/FormInitiate.vue` | 保存草稿使用 FormKey，不依赖 FlowKey |
| `cp6.web/src/api/oa/draft.ts` | 新 API |
| `CP6.Tests/Oa/DraftServiceTests.cs` | 全面改写 |

## 13. 范围外

- 协作草稿、分享、转让。
- 自动保存和离线草稿。
- 回收站与恢复。
- 草稿评论和版本历史。

## 14. 相对工作量

- 模型和迁移：中。
- rebase：中。
- UI 重做：中。
- 遗留数据验证：中。
