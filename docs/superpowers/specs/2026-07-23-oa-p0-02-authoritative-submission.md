# OA P0-2：SFS 权威提交链

日期：2026-07-23  
状态：Frozen  
优先级：P0  
前置：P0-1 定义版本化  

## 1. 目标

SFS 原生表单只有一个正式提交入口。一次成功提交必须原子完成：

```text
身份确认
→ 解析 Published FormVersion
→ 规范化数据
→ 服务端规则复算
→ 服务端校验
→ 保存 Wf_FormData
→ 可选解析 FormFlowBinding
→ 可选创建 pinned FlowInstance
→ 提交事务
```

浏览器不能选择 `FlowKey/BizType/BizId`。表单没有流程绑定时仍可独立提交。

## 2. 当前断点

- `FormService.SubmitDataAsync` 已有服务端复算、校验和 `Wf_FormData` 落库，但主页面没有调用。
- `FormInitiate.doSubmit` 直接调用 `/api/wf/flow/submit`，只传 `flowKey + varsJson`。
- `FlowController.Submit` 接受客户端 `BizType/BizId`。
- `ApprovalController.Submit` 接受客户端 `BizType/BizId/FormSnapshot`。

结果是 SFS 数据可能只存在 `Wf_FlowInstance.VarsJson`，`Wf_FormData` 不是权威来源，客户端还可影响业务绑定。

## 3. 数据模型

### 3.1 `Wf_FormData`

新增并收敛为正式提交记录：

```csharp
public class Wf_FormData : BaseTenantEntity
{
    public Guid FormDefVersionId { get; set; }    // 新数据必填
    public string FormKey { get; set; }           // 检索冗余
    public int FormVersion { get; set; }           // 展示冗余
    public string SubmissionKey { get; set; }      // 客户端生成 UUID，租户内唯一
    public string RequestHash { get; set; }         // FormKey + 规范化请求体 SHA-256
    public Guid SubmittedBy { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public string DataJson { get; set; }            // 服务端规范化结果
    public byte[] RowVersion { get; set; }
}
```

索引：

```sql
UNIQUE (TenantId, SubmissionKey)
INDEX  (TenantId, FormDefVersionId, SubmittedAtUtc)
```

旧 `BizId` 暂留兼容，不作为新 SFS 关联键。新实例通过显式 `FormDataId` 单向关联；不在 FormData 上建立反向 FlowInstanceId，避免循环外键。

### 3.2 实例

SFS 启动的流程实例：

```text
FlowDefVersionId = 绑定流程的最新 Published
FormDefVersionId = 本次提交使用的 Published
FormDataId       = 新 Wf_FormData.Id
BizType          = "SFS"
BizId            = Wf_FormData.Id.ToString()
VarsJson         = Wf_FormData.DataJson 的同字节快照
```

业务流程实例不创建 `Wf_FormData`，其 `FormDefVersionId/FormDataId` 为空。

## 4. 服务契约

新增 `IFormSubmissionService`，控制器不得自行串接 `FormService + FlowEngine`。

```csharp
public sealed record SubmitFormCommand(
    string FormKey,
    Guid ActorId,
    string SubmissionKey,
    JsonElement Data,
    Guid? DraftId);

public sealed record SubmitFormResult(
    Guid FormDataId,
    Guid FormDefVersionId,
    int FormVersion,
    Guid? FlowInstanceId,
    Guid? FlowDefVersionId,
    int? FlowVersion);

public interface IFormSubmissionService
{
    Task<SubmitFormResult> SubmitAsync(
        SubmitFormCommand command,
        CancellationToken ct = default);
}
```

服务必须：

1. 从当前租户和 `FormKey` 解析启用头表及 latest Published。
2. `Data` 必须是 JSON object。
3. 字段名必须来自 schema；未知字段拒绝，不静默保存。
4. 按 schema 类型规范化 number/date/datetime/checkbox 等值。
5. 执行现有 `RecomputeAndValidate`；compute 结果覆盖客户端值。
6. 创建 `Wf_FormData`。
7. 查询启用的 `Wf_FormFlowBinding`：
   - 不存在：直接提交表单数据。
   - 存在：解析启用流程 latest Published，创建 pinned 实例。
8. 在同一个数据库事务中保存 FormData、实例、token、task、history、通知。
9. 只有事务提交后才返回成功。

FlowEngine 新增内部 pinned 启动契约，不暴露给控制器：

```csharp
Task<Guid> StartPinnedAsync(
    Guid flowDefVersionId,
    Guid starterId,
    string varsJson,
    FlowBusinessRef? businessRef,
    FlowFormRef? formRef,
    CancellationToken ct = default);
```

`StartPinnedAsync` 不从客户端字符串解析定义；所有调用者必须先通过受信服务解析版本。

## 5. API

### 5.1 正式提交

```http
POST /api/oa/forms/{formKey}/submissions
Idempotency-Key: 7fe8e303-06c4-47f3-8a35-9a372f47f5da
Content-Type: application/json
```

请求：

```json
{
  "data": {
    "leaveType": "annual",
    "days": 2,
    "reason": "..."
  },
  "draftId": null
}
```

响应：

```json
{
  "code": 0,
  "data": {
    "formDataId": "guid",
    "formVersion": 3,
    "flowInstanceId": "guid-or-null",
    "flowVersion": 5
  }
}
```

同一租户、同一 `Idempotency-Key`、相同 FormKey 与规范化 payload hash 重试时，通过 `Wf_FlowInstance.FormDataId` 找到可选实例并返回第一次结果；同 key 不同 hash 返回 409。Hash 输入为 `FormKey + DraftId + 按 key 排序的原始 JSON object`，在 compute 前计算，确保同一用户输入稳定而不同输入不能复用 key。

### 5.2 读取提交结果

```http
GET /api/oa/form-submissions/{formDataId}
```

读取必须经过 P0-4 授权。无流程的表单数据在 P0 内仅提交人可读；共享、管理员查询和字段报表后续实现。

## 6. 端点收口

P0 前端切换完成后：

| 旧端点 | P0 行为 |
|---|---|
| `POST /api/wf/flow/submit` | 不再授予普通 UI 权限；返回 410 或仅保留内部测试宿主 |
| `POST /api/wf/approval/submit` | 删除浏览器入口；业务模块必须使用自己的后端提交端点 |
| `POST /api/wf/form/data` | 标记 deprecated，并委托新提交服务的“无流程模式”；不允许单独绕过绑定 |

仓库内调用点清零后才能启用 410。内部触发器、子流程和服务端业务集成调用服务接口，不调用 HTTP 端点。

## 7. 事务和失败语义

| 失败点 | 结果 |
|---|---|
| 表单无 Published 版本 | 不写 FormData，不起流程 |
| 服务端校验失败 | 返回字段错误，不写数据 |
| 绑定流程无 Published 版本 | 整体回滚，不保留孤儿 FormData |
| 审批人解析失败并按现有语义挂起 | FormData 与 Suspended 实例一起提交 |
| token/task/history 保存失败 | 整体回滚 |
| 客户端超时后重试 | SubmissionKey 返回原结果，不重复起流 |

通知发送继续遵循现有 notifier 事务边界；若通知是事务外副作用，必须以 outbox/幂等键保证不因 API 重试重复。P0 不允许“数据库失败但通知已发”的实现。

## 8. 权限和信任边界

- ActorId 只取登录上下文。
- FormKey 只用于选择 SFS 表单头；客户端不能传版本 ID。
- FlowKey、BizType、BizId 由绑定和服务端生成。
- DataJson 是不可信输入，必须限制最大请求体、字段数量、字符串长度和嵌套深度。
- `submissionKey` 不作为授权凭证。
- 业务模块禁止调用本端点保存 ERP 固定业务单据。

建议 P0 限额：

```text
请求体 <= 1 MiB
字段数 <= 500
JSON 深度 <= 8
单字符串最终仍受各字段 maxLength；无 maxLength 时平台上限 10000
```

## 9. 错误码

| 码 | HTTP | 场景 |
|---|---:|---|
| E-WF-036 | 409 | 表单无可用 Published 版本或已停用 |
| E-WF-039 | 400 | payload 含 schema 外字段或非法路径 |
| E-WF-044 | 409 | SubmissionKey 已被不同请求使用 |
| E-WF-047 | 400 | 数据规范化或服务端校验失败 |
| E-WF-029 | 409 | 已绑定流程无可用 Published 版本 |

字段校验响应必须包含稳定字段名和本地化 message key，不只返回拼接字符串。

## 10. 验收标准

1. FormInitiate 不再调用 `flowApi.submit`。
2. 任意正式提交都产生一条带准确 FormDefVersionId 的 `Wf_FormData`。
3. 有绑定时 FormData 与实例同时成功，实例的三个 pin 正确且 `FormDataId` 唯一关联。
4. 无绑定时提交成功、`FlowInstanceId=null`，数据可由提交人读取。
5. compute 字段以服务端结果落库，客户端伪造值无效。
6. required/type/maxLength/pattern/未知字段在服务端拒绝。
7. 客户端无法指定 FlowKey、BizType、BizId 或版本 ID。
8. 相同 SubmissionKey 重试不创建第二条 FormData 或第二实例。
9. 绑定流程停用时不留下孤儿 FormData。
10. 通用业务审批提交端点不再接受浏览器快照。

## 11. 测试

| 层 | 覆盖 |
|---|---|
| 单元 | 规范化、未知字段、compute 覆盖、绑定/无绑定解析、idempotency payload hash |
| 集成 | 单事务提交和各失败点回滚、唯一索引竞争、pinned 实例 |
| 控制器 | 不能传可信标识、限额、错误码和字段错误结构 |
| 前端 | FormInitiate 成功/失败/重复点击、无绑定成功态 |
| E2E | 新建表单→发布→绑定流程→填写→提交→待办；解除绑定→再次提交无实例 |

## 12. 回滚

- `SfsAuthoritativeSubmit` feature flag 可把 UI 暂时切回旧入口，但只用于短时回滚。
- 新 `Wf_FormData` 列和实例 pin 不回删。
- 已通过新入口创建的数据可由旧详情按 `DataJson/VarsJson` 降级读取。
- 回滚期间必须禁用表单新版本发布，避免旧应用读到不兼容 schema。

## 13. 文件影响

| 文件/区域 | 变化 |
|---|---|
| `CP6.Entity/DomainModels/Wf/Wf_FormData.cs` | 权威提交字段和并发列 |
| `CP6.Core/Services/Wf/FormService.cs` | 复算/校验能力复用，不再独立代表完整提交 |
| `CP6.Core/Services/Wf/FlowEngine.cs` | 内部 pinned start |
| `CP6.Core/Services/Oa/` | 新 `FormSubmissionService` |
| `CP6.WebApi/Controllers/Oa/` | 新 submissions controller |
| `CP6.WebApi/Controllers/Wf/{Flow,Approval,Form}Controller.cs` | 旧入口收口 |
| `cp6.web/src/views/oa/catalog/FormInitiate.vue` | 切换权威入口 |
| `cp6.web/src/api/oa/` | 新 submission API |
| `CP6.Tests`、前端 spec | 原子性、幂等、信任边界测试 |

## 14. 范围外

- 附件和子表。
- 字段查询/导出。
- 独立表单共享给其他用户。
- 外部 API 客户端直接创建工作流。

## 15. 相对工作量

- 服务端编排和事务：大。
- 数据规范化与错误结构：中。
- 端点收口及前端切换：中。
- 幂等和失败注入测试：中到大。
