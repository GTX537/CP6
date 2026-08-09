# CP6 Space 详细设计卷五：外部组织、授权、审计与质量门禁

版本：v1.0  
日期：2026-07-23  
状态：详细设计已锁定，可进入安全模型和验收基线实现  
覆盖决策：D7、D10、D11、D12、D14、D15

关联入口：

- [低成本 3D 建模 Spec](../requirements/04-low-cost-3d-modeling-spec.md)
- [卷四：发布、WMS 与恢复](./04-validation-publish-wms-recovery.md)
- [当前 TenantMiddleware](../../../CP6.WebApi/Middleware/TenantMiddleware.cs)
- [当前 SpaceHub](../../../CP6.WebApi/Hubs/SpaceHub.cs)

## 1. 本卷结论

CP6 Space 面向多租户、多用户和多类外部协作方：

- 内部用户通过功能 RBAC 获得“能做什么”。
- `ISpaceAccessEvaluator` 统一判断“能访问哪个租户、Site、Floor、Zone、货主和业务对象”。
- 客户、供应商、3PL 通过 External Organization、Membership 和 Grant 登录查看。
- 外部用户默认只读且只能看 Published 数据；不能访问 Draft、来源文件、映射、解析问题、发布和重试。
- 字段输出采用显式 allowlist；缺少策略不是“全部可见”。
- API、导出、缓存、分页游标、Scene Chunk、Overlay 和 SignalR 使用同一访问裁剪。
- D15 的标准数据仓、真实 SQL、契约测试、故障注入、安全和性能门禁共同决定能否发布。

## 2. 当前实现风险

| 当前事实 | 风险 | 目标 |
|---|---|---|
| TenantMiddleware 对无效/缺失 claim 保留默认租户 | 已认证请求可能落到默认租户 | Design API 对无有效 tenant fail closed |
| 通用 DataScope 主要按本人/部门 | 不能表达 Site/Zone/货主/外部组织 | Space 专用 Evaluator |
| FieldPerm 未配置时直接返回 | 容易形成 allow-by-absence | Portal DTO 显式 allowlist |
| SpaceHub 无 `[Authorize]` 和组 | 连接/广播范围不清 | 鉴权 + Tenant/Site/Org 组 |
| Notifier 使用 `Clients.All` | 跨租户元数据泄漏 | 只向授权组发“状态已变化” |
| 新旧 API 错误风格不同 | 客户端恢复困难 | Design v1 统一 Problem Details |
| 真 SQL 测试受环境变量门控 | CI 可能全部 Skip | GA 门禁环境必须运行且不得 Skip |

## 3. 身份与主体

### 3.1 主体类型

| 主体 | 说明 |
|---|---|
| InternalUser | 当前租户员工/管理员 |
| ExternalMember | 某 External Organization 的成员 |
| ServicePrincipal | 后台 Worker、集成或受控客户端 |
| SupportOperator | 平台支持；必须显式临时授权 |

JWT/会话至少携带：

- `sub`
- `tenant_id`
- `session_id` 或 `jti`
- 身份类型
- 授权版本/安全戳

External Organization 不通过前端参数选择来建立信任。服务端根据当前用户有效 Membership 得到组织集合。

### 3.2 客户端认证

| 客户端 | 认证 |
|---|---|
| 浏览器 | 当前安全会话/JWT cookie，CSRF 与 Origin 策略 |
| 桌面/移动 | OIDC Authorization Code + PKCE |
| Worker | 工作负载身份或短期服务令牌 |
| 第三方集成 | 独立 ServicePrincipal、最小 scope、可轮换密钥 |

原生客户端不得嵌入长期共享密钥。SignalR 使用与 HTTP 相同的用户认证和数据范围，不接受客户端自报 TenantId。

## 4. 外部组织数据模型

### 4.1 `Space_ExternalOrganization`

| 字段 | 说明 |
|---|---|
| `Id/TenantId` | 租户内组织 |
| `Type` | Customer/Supplier/ThirdPartyLogistics |
| `BusinessPartnerType/Id` | 可选 ERP 客商关联 |
| `Code/Name` | 租户内唯一编码、名称 |
| `Status` | Active/Suspended/Closed |
| `SecurityStamp` | 授权变化时递增 |

### 4.2 `Space_ExternalMembership`

| 字段 | 说明 |
|---|---|
| `OrganizationId/UserId` | 成员 |
| `Role` | Viewer/OperationsViewer/OrgAdmin |
| `ValidFromUtc/ValidToUtc` | 有效期 |
| `Status` | Invited/Active/Suspended/Revoked |
| `InvitedBy/AcceptedAtUtc` | 审计 |

唯一约束：同一组织、用户只能有一个活动 Membership。用户可以属于多个组织，但一次 Portal 请求必须有明确 `organizationContextId`，且服务端验证 Membership。

### 4.3 `Space_ExternalGrant`

| 字段 | 说明 |
|---|---|
| `OrganizationId` | 所属组织 |
| `SiteId` | 必填 Site |
| `FloorIds/ZoneIds` | 可选范围，规范化子表存储 |
| `OwnerIds` | 可选货主范围 |
| `BusinessObjectType/Ids` | 可选订单、任务等范围 |
| `FieldPolicyId` | 字段策略 |
| `CanExport` | 是否允许导出 |
| `ValidFromUtc/ValidToUtc` | 有效期 |
| `Status` | Active/Suspended/Revoked |
| `GrantVersion` | 缓存失效 |

多值范围使用关联表，不在主表保存任意 JSON 列表：

- `Space_ExternalGrantFloor`
- `Space_ExternalGrantZone`
- `Space_ExternalGrantOwner`
- `Space_ExternalGrantObject`

Zone 必须属于 Grant 的 Site；写入时和查询时都验证。

### 4.4 `Space_FieldPolicy`

| 字段 | 说明 |
|---|---|
| `Name/Version` | 策略版本 |
| `AudienceType` | Customer/Supplier/3PL/Internal |
| `ResourceType` | Scene/Stock/Task/Export |
| `AllowedField` | 关联表中的显式字段 |
| `MaskingRule` | None/Partial/Hash/Redact |
| `CanExport` | 导出规则 |

禁止“只保存隐藏字段，其余默认展示”。新 DTO 字段在未加入 Allowlist 前，对外不可见。

## 5. 授权模型

### 5.1 两层判断

```text
功能 RBAC
∩ Tenant
∩ Principal/Membership
∩ Organization Context
∩ Grant 有效期
∩ Site/Floor/Zone
∩ Owner/Business Object
∩ Published-only
∩ Field Policy
```

任一维度缺失、无效或无法解释都拒绝。

### 5.2 `ISpaceAccessEvaluator`

```csharp
public interface ISpaceAccessEvaluator
{
    Task<SpaceAccessDecision> EvaluateAsync(
        SpacePrincipal principal,
        SpaceAction action,
        SpaceResource resource,
        CancellationToken ct);

    Task<SpaceQueryScope> BuildQueryScopeAsync(
        SpacePrincipal principal,
        SpaceResourceType resourceType,
        OrganizationContext? organization,
        CancellationToken ct);
}
```

Decision 包含：

- Allowed/Denied。
- 稳定 ReasonCode。
- Tenant/Site/Floor/Zone/Owner/Object 范围。
- FieldPolicyVersion。
- Grant/Membership/SecurityStamp。
- 审计所需授权证据 ID。

Controller 不自行拼接 Grant 查询。Application Query 先获取 Scope，再把它编译成 EF 条件；返回 DTO 后再执行字段 allowlist。

### 5.3 Grant 组合

- 同一 Organization 内多个 Grant 可以做集合并集。
- 不同 Organization 的 Grant 不得自动拼接。
- 一次请求只有一个 Organization Context。
- 范围维度之间做交集，不因某维为空而放宽。
- Deny/Suspended/Expired 优先。
- 平台支持人员走独立临时授权，不伪装成 External Membership。

### 5.4 资源行为

| 资源 | Internal | External |
|---|---|---|
| Published Scene | 按 Site 权限 | 按 Grant 裁剪 |
| Stock Overlay | 按 Site/Owner | 按 Grant + FieldPolicy |
| Task Overlay | 按业务对象 | 按 Grant + FieldPolicy |
| Draft/Version | 建模权限 | 永不允许 |
| Source/File/Issue | 建模权限 | 永不允许 |
| Publish/Reconcile | 发布权限 | 永不允许 |
| Audit | 审计权限 | 仅自身访问摘要（如产品需要） |

## 6. Portal API

基础路径：`/api/space/portal/v1`

| Method | Route | 作用 |
|---|---|---|
| GET | `/organizations` | 当前用户可进入的组织上下文 |
| GET | `/sites` | 当前组织可见 Site |
| GET | `/sites/{siteId}/manifest` | 裁剪后的 Published Manifest |
| GET | `/chunks/{chunkId}` | 授权 Scene Chunk |
| GET | `/sites/{siteId}/stock` | 裁剪和脱敏库存 |
| GET | `/sites/{siteId}/tasks` | 授权任务 |
| POST | `/sites/{siteId}/exports` | 创建受控导出任务 |

请求通过受保护 header 或路由上下文携带 Organization Context；服务端仍从 Membership 验证。不得接受 `tenantId` 查询参数切租户。

### 6.1 枚举防护

- 内部用户缺功能权限返回 403。
- 外部用户请求不存在或范围外的资源统一返回 404，减少资源枚举。
- Problem Details 仍包含 traceId，但不泄漏另一个租户、组织或对象是否存在。
- 批量 ID 查询对每个 ID 应用 Scope，不允许“其中一个有权就返回全部”。

## 7. 查询、缓存、导出与实时

### 7.1 查询

所有查询必须：

- 在 SQL 层先应用 Tenant 和数据 Scope。
- 禁止先 `.ToList()` 再内存过滤。
- 子查询、Count、聚合和搜索使用同一 Scope。
- Scene Manifest 不列出无权 Chunk。
- Overlay 只返回已授权 LogicalId。

### 7.2 缓存

缓存键至少包含：

```text
TenantId
PrincipalId
IdentityType
OrganizationContextId
MembershipSecurityStamp
GrantVersion
FieldPolicyVersion
Resource/Filter
PublishedVersionId
```

授权变化后提升 SecurityStamp/GrantVersion，并主动失效相关缓存。禁止只用 SiteId 或 URL 作为 Portal 缓存键。

### 7.3 Cursor

分页 Cursor：

- 服务端签名或加密。
- 绑定 Tenant、Principal、Organization、GrantVersion 和过滤条件。
- 有过期时间。
- 授权变化后旧 Cursor 失效。

### 7.4 导出

导出是后台 Job：

- 创建时和执行时各做一次授权。
- 输出只包含 FieldPolicy allowlist 字段。
- 文件使用短期授权下载。
- 记录过滤范围、字段策略、数量、下载者和下载时间。
- Grant 到期或撤销后，未下载文件立即失效。

### 7.5 SignalR

`SpaceHub` 必须 `[Authorize]`。连接后服务端按 AccessDecision 加入：

- `tenant:{tenantId}`
- `site:{tenantId}:{siteId}`
- `org:{tenantId}:{organizationId}:site:{siteId}`

事件只携带：

- EventType
- SiteId
- PublishedVersionId/OverlaySequence
- CorrelationId

不广播库存、任务或来源明细。客户端收到后通过 HTTP 拉取并重新授权。

## 8. 审计

### 8.1 审计事件

`Space_AuditEvent` 至少包含：

| 字段 | 说明 |
|---|---|
| `TenantId/EventId/OccurredAtUtc` | 身份 |
| `ActorType/ActorId/UserName` | 操作者 |
| `OrganizationContextId` | 外部上下文 |
| `Action` | 稳定动作码 |
| `ResourceType/ResourceId` | 对象 |
| `SiteId/VersionId/FloorId` | 空间上下文 |
| `Outcome/ReasonCode` | 成功/拒绝/失败 |
| `AuthorizationEvidenceJson` | Role、Membership、Grant、Policy 版本 |
| `BeforeHash/AfterHash` | 变更证据 |
| `CommandBatchId/JobId/PublishAttemptId` | 链路 |
| `TraceId/CorrelationId` | 可观测性 |
| `ClientType/Ip/UserAgent` | 受控客户端信息 |

### 8.2 必审动作

- 登录、组织上下文切换和访问拒绝。
- Membership、Grant、FieldPolicy 创建/修改/撤销。
- 文件上传、安全拒绝、下载和删除。
- 导入确认、手工校正、命令保存、强制接管租约。
- 校验 Warning 确认。
- 发布、审批、重试、对账、补偿和历史版重新发布。
- Portal 查看敏感 Overlay 和所有导出。
- 支持人员临时授权。

审计写入失败时：

- 高风险写操作失败关闭，不继续发布/授权/导出。
- 普通只读访问可按配置降级，但必须产生运维告警。

### 8.3 保留

保留期由租户合同、法规和系统策略配置。主 Spec 的默认值可作为产品默认，但不能硬编码为法律结论。Published 版本、发布计划、授权变更和高风险审计不得被普通用户删除；清理任务需要保留策略和 Legal Hold 检查。

## 9. Problem Details 与客户端恢复

新 Design/Portal API 使用：

- HTTP 标准状态。
- `application/problem+json`。
- 稳定 `code`。
- `traceId/correlationId`。
- 可选结构化 `recovery`。

安全错误码：

- `SPACE_TENANT_CONTEXT_REQUIRED`
- `SPACE_TENANT_SCOPE_DENIED`
- `SPACE_ORGANIZATION_CONTEXT_REQUIRED`
- `SPACE_MEMBERSHIP_INACTIVE`
- `SPACE_GRANT_EXPIRED`
- `SPACE_RESOURCE_SCOPE_DENIED`
- `SPACE_FIELD_POLICY_DENIED`
- `SPACE_EXPORT_DENIED`
- `SPACE_CURSOR_SCOPE_MISMATCH`
- `SPACE_REALTIME_SUBSCRIPTION_DENIED`

客户端不得根据 message 文本决定重试、跳转或重新登录。

## 10. 标准验收数据包

D15 使用确定性通用货架仓：

```text
CP6.Tests/TestData/Space/Acceptance/{semanticVersion}/
├─ manifest.json
├─ warehouse-standard.dxf
├─ warehouse-standard.dwg
├─ floor-1.png
├─ floor-2.png
├─ space-master.xlsx
├─ expected-elements.jsonl
├─ expected-locations.csv
├─ wms-seed.json
├─ users-and-grants.json
└─ fault-cases/
```

固定规模：

- 2 个 Floor。
- 500 个 Rack。
- 10,000 个 Location。
- 100 个 SKU、5,000 条库存。
- 20 个拣货任务。
- Customer、Supplier、3PL 各至少一个组织。
- 至少两个租户复用相同 SiteCode、LocationCode 和 PartnerCode。

`manifest.json` 保存每个文件 SHA-256、预期数量、兼容 Spec/Schema/Generator 版本。已用于发布的版本不可覆盖。

## 11. 测试体系

### 11.1 单元测试

- AccessEvaluator 的每个维度、Deny 优先和组织隔离。
- FieldPolicy allowlist 和掩码。
- Audit event 构建。
- Cursor 签名、过期和 Scope 绑定。
- Scene/Overlay 裁剪。
- 版本、命令、校验、Plan 和 Saga 领域规则。

### 11.2 SQL Server 集成测试

必须使用真实 SQL Server 覆盖：

- Tenant 全局过滤和写入盖章。
- 组合唯一索引、过滤索引和 FK。
- RowVersion、Job Lease、Edit Lease、Publish Lease。
- Membership/Grant 有效期查询。
- Site/Zone/Owner Scope 在 SQL 层过滤。
- Runtime 物化、CurrentPublishedVersionId 和 Outbox 事务。

现有 `CP6_TEST_SQLSERVER` 测试可复用，但 GA CI Job 必须提供数据库且把 Skip 视为失败。

### 11.3 API 和安全测试

矩阵至少包含：

| 维度 | 值 |
|---|---|
| 主体 | Internal/Customer/Supplier/3PL/Service |
| 租户 | 正确/另一个/缺失/伪造 |
| 组织 | 正确/另一个/过期/撤销 |
| 范围 | Site/Floor/Zone/Owner/Object |
| 入口 | API/Chunk/Overlay/Export/SignalR |
| 标识 | URL ID/批量 ID/Cursor/缓存命中 |

重点攻击：

- IDOR、跨租户和跨组织猜测。
- 两个 Grant 跨组织拼接。
- 缓存投毒和缓存键缺维度。
- 旧 Cursor 在授权撤销后复用。
- SignalR 未授权订阅。
- 导出字段超出 UI 当前显示。
- 文件名、Problem Details、日志和审计中的注入/敏感泄漏。

### 11.4 契约测试

- OpenAPI 破坏性变更检查。
- TypeScript/C# SDK 生成并编译。
- WMS Adapter 全套契约。
- CAD Converter IR 契约。
- Scene Manifest/Chunk schema。
- Problem Details 错误码快照。

### 11.5 故障注入

沿用卷四并增加：

- 授权在 Job 排队后撤销。
- Grant 在大文件导出中途到期。
- SignalR 重连时 Site 权限变化。
- 缓存服务不可用。
- 审计写入失败。
- 杀毒服务不可用。
- 数据库死锁、Worker 租约丢失和客户端重复提交。

### 11.6 端到端

至少自动运行：

1. 内部用户从底图建立标准仓、校验、发布到模拟 WMS。
2. CAD+Excel 生成同一规范快照。
3. CP6 WMS 真实适配发布。
4. Customer 只见授权货主库存。
5. Supplier 只见授权任务字段。
6. 3PL 见多个授权 Zone，但不能访问其他组织 Grant。
7. 发布失败、自动重试、对账和历史版重新发布。
8. 10,000 库位 Viewer、搜索、拾取、Overlay 和导出。

## 12. 性能与容量

### 12.1 测量规则

每次结果记录：

- 应用 SHA、数据库迁移、数据包版本。
- 浏览器、终端、CPU/GPU/内存。
- 网络和缓存状态。
- 租户/用户/Grant 数量。
- P50/P95/P99、错误率和样本数。

不允许只报平均值。

### 12.2 门槛

| 指标 | MVP 门槛 |
|---|---:|
| Manifest API P95 | ≤1s |
| 首个可见场景 | ≤15s |
| 可拾取和搜索 | ≤20s |
| 平移/缩放中位 FPS | ≥30 |
| 拾取 P95 | ≤150ms |
| Overlay 更新 P95 | ≤3s |
| Portal Scope 查询 P95 | ≤1s |
| 10,000 Location 发布 | ≤15min，含 WMS 验证 |
| 授权撤销生效 | 下一请求立即；实时连接≤30s 重验证/断开 |

测量环境和证据格式见 [ADR-0004](../adr/0004-performance-acceptance-environment.md)。门槛已经冻结；实现团队不得自行调整。确需放宽必须提交 Scope Change RFC，由产品、架构、QA 和安全负责人批准，不能因失败静默放宽。

### 12.3 容量保护

- 所有集合 API 分页或 Chunk 化。
- WMS、数据库和对象存储批量调用。
- 导出、解析、发布走 Job Ledger。
- 每租户并发上传、解析、发布和导出有限额。
- Scene/Overlay 缓存按 PublishedVersionId 和授权版本分区。
- 高基数指标不把 UserId/LogicalId 作为 Prometheus label。

## 13. 发布门禁

本节测试细节服从 [MVP Scope Baseline v1.0 §11](../requirements/06-mvp-scope-freeze-baseline-v1.0.md#11-统一发布关卡)。外部 Portal 是 GA 硬门槛，不阻塞仅内部租户用户参加的 Beta。

### Alpha

- 底图/模板垂直切片通过。
- 模拟 WMS 发布闭环。
- DesignV1 租户开关不影响 Legacy。
- 文件隔离和 Job 恢复可用。

### Beta

- DXF、受控 DWG 和 Excel 路径通过。
- 2D/3D 机器清单一致。
- SQL Server 集成、WMS 契约和故障注入通过。
- 内部建模、AI 审查和发布角色权限矩阵通过；外部 Portal 可以试点。

### GA

- CP6 WMS Certified。
- 标准 10,000 库位全流程性能通过。
- 所有跨租户/跨组织安全测试通过。
- Customer/Supplier/3PL Portal 权限矩阵通过。
- 发布部分成功和本地激活失败均可恢复。
- 审计、备份、恢复演练和运维手册完成。
- 产品、QA、WMS、架构和安全负责人签署验收证据。

任一跨租户测试失败、适配器契约失败、真 SQL 测试 Skip、ReconciliationRequired 无处理路径或性能门槛失败，均阻断 GA。

## 14. 当前到目标的完成矩阵

| 领域 | 当前 | 目标完成标志 |
|---|---|---|
| 多租户 | 基础 TenantId 过滤 | 无效上下文 fail closed，全部入口同 Scope |
| 外部登录 | 未形成 Space 组织模型 | Customer/Supplier/3PL Membership + Grant |
| 字段权限 | 通用反射掩码 | Portal DTO 显式 allowlist |
| SignalR | 全局广播 | 鉴权、分组、无明细广播 |
| SQL 测试 | 有基础、可 Skip | GA CI 强制运行完整矩阵 |
| 性能 | Viewer 有优化组件 | 标准数据包和机器证据 |
| 安全 | 分散控制 | 文件、API、导出、缓存、实时统一门禁 |
| 审计 | 有通用基础 | 设计、发布、授权、外部访问全链路证据 |

## 15. 完成定义

- 多人、多组织、多 Site 使用时，授权结果可解释、可审计、可立即撤销。
- 客户、供应商和 3PL 只能读取 Published 且只见被授权字段和对象。
- API、Scene Chunk、Overlay、导出、缓存、Cursor 和 SignalR 不存在旁路。
- 标准仓在真实 SQL、真实 CP6 WMS/模拟器、故障注入和目标终端上通过。
- 发布结论由机器可读证据和门禁决定，不由演示截图或“手工看起来正常”决定。
