# CP6 Space Development Ready 实施交接

- 状态：**Ready for E00/E01 start**
- 范围基线：[Space MVP Scope Baseline v1.0](./06-mvp-scope-freeze-baseline-v1.0.md)

## 1. 使用方式

本文件把固定批次转换为首轮可执行工作卡。E00/E01 可以直接启动；后续批次沿用同一工作卡格式，在进入该批次前补充文件级实现位置，但不得重新打开产品范围。

每张工作卡必须满足：

- 1～3 工程师日。
- 单一可验收输出。
- 明确权限、错误码、测试和回滚。
- 不把候选工作树等同于正式实现。
- 不在当前主工作区混入 OA/ERP/WF 改动。

## 2. 固定依赖

```text
E00-S01
  └─ E00-S02
       ├─ E00-S03
       ├─ E00-S04
       └─ E01-S01
            ├─ E01-S02 ──┬─ E01-S05
            │             ├─ E01-S06
            │             ├─ E02-S01
            │             └─ E13-S02
            ├─ E01-S03 ───┬─ E01-S05
            │              └─ E13-S03
            └─ E01-S04
```

并行启动规则：

- E00-S03 与 E00-S04 可以并行。
- E01-S02、E01-S03、E01-S04 在 E01-S01 后并行。
- E02-S01 只有在 E01-S02 的文件/来源契约可用后启动。
- E13-S05 只有在 Provider 端口和输出校验契约可用后启动。

## 3. 通用实施约束

### 3.1 输入

- 本冻结基线。
- `requirements/01`～`05`。
- `design/01`～`06`。
- AI JSON Schema 和 Proposal Patch Policy。
- 对应 ADR 和验收资产。

### 3.2 权限

统一权限名：

- `space:model:read`
- `space:model:edit`
- `space:model:validate`
- `space:model:publish`
- `space:model:rollback`
- `space:source:upload`
- `space:mapping:manage`
- `space:integration:manage`
- `space:audit:read`
- `space:model:generate-ai`
- `space:model:review-ai`

所有服务端入口先验证 Tenant，再验证 RBAC 和 Space 数据范围。外部主体不能获得 Draft/Source/AI 权限。

### 3.3 错误

- Design API 使用 RFC Problem Details 和稳定 `SPACE_*` 错误码。
- Legacy API 保持旧响应 Envelope。
- 新异常不得把路径、Prompt、原文件内容、Provider 请求或数据库详情返回客户端。

### 3.4 测试

- 领域状态和纯函数：单元测试。
- EF、Migration、事务、索引、并发：SQL Server 集成测试。
- HTTP、权限、Problem Details：API 契约测试。
- 文件和 Worker：恶意输入、超时、取消、租约丢失和重试。
- 端到端：使用 `docs/space/acceptance` 的版本化资产。

### 3.5 回滚

- 所有新入口由租户/Site 功能开关保护。
- 数据库变更使用向前修复 Migration，不删除 Legacy 数据。
- Site 激活前失败可以保持 Frozen 并经审批 ReopenLegacy。
- 已激活 Published 不因 Draft、Import、BuildScene 或 AI 失败改变。

## 4. 批次 A 工作卡

### E00-S01 当前事实清单

| 字段 | 内容 |
|---|---|
| 输入 | 主工作树 Space 实体、服务、Controller、页面、测试；`space-volume1` 候选文件 |
| 输出 | 可重复生成的 API/表/页面/权限/测试/工作树文件清单，并标记 Implemented/Partial/NotStarted |
| 依赖 | 无 |
| 权限 | 不新增运行时权限；清单不得包含凭据和客户数据 |
| 错误 | 扫描失败使任务失败，不允许用不完整清单标记完成 |
| 测试 | 清单生成命令重复运行结果一致；所有 Space 端点有唯一归属 |
| 回滚 | 仅文档和只读扫描；删除生成报告即可 |

### E00-S02 功能开关与兼容

| 字段 | 内容 |
|---|---|
| 输入 | E00-S01；ADR-0003；Legacy/Design V1 状态机 |
| 输出 | 租户+Site 的 `Legacy/DesignV1` 开关、切换前置和 Legacy 写拦截 |
| 依赖 | E00-S01 |
| 权限 | `space:integration:manage`；外部主体拒绝 |
| 错误 | `SPACE_LEGACY_WRITE_DISABLED`、`SPACE_VERSION_STATE_INVALID`、`SPACE_TENANT_SCOPE_DENIED` |
| 测试 | Legacy 默认、Design 开启、跨租户、切换失败、旧读兼容 |
| 回滚 | 关闭 Design 入口；激活前可 ReopenLegacy，激活后走修复发布 |

### E00-S03 数据来源枚举

| 字段 | 内容 |
|---|---|
| 输入 | WMS 真相边界、模拟器需求、现有 Viewer DTO |
| 输出 | `Real/Simulated/Unavailable` 统一枚举、时间戳和前端标识 |
| 依赖 | E00-S02 |
| 权限 | 沿用读取资源权限；来源字段不可被 FieldPolicy 隐藏 |
| 错误 | 未接入返回 `Unavailable`，不得伪装为空库存 |
| 测试 | API、Viewer、导出、任务和库存的三种来源一致 |
| 回滚 | 保留字段并回退 UI 展示，不改变库存数据 |

### E00-S04 可观测性基线

| 字段 | 内容 |
|---|---|
| 输入 | API、Job、Publish、Audit 的执行上下文 |
| 输出 | CorrelationId、TraceId、TenantId、JobId/RunId/PublishAttemptId 的统一传播 |
| 依赖 | E00-S02 |
| 权限 | `space:audit:read` 读取脱敏证据；外部主体拒绝 |
| 错误 | 缺 Tenant/Actor fail closed；日志失败不得暴露敏感正文 |
| 测试 | HTTP→Job→Adapter→Outbox→Audit 可按 CorrelationId 串联 |
| 回滚 | 关闭新增导出/指标，不移除审计事件 |

## 5. 批次 B 的 E01 工作卡

### E01-S01 模型版本和状态机

| 字段 | 内容 |
|---|---|
| 输入 | 详细设计卷一；候选 Domain/Application；ADR-0003 |
| 输出 | `Space_Model`、`Space_ModelVersion`、状态转换、单活动 Draft 和首条正式 Migration |
| 依赖 | E00-S02 |
| 权限 | `space:model:read`、`space:model:edit` |
| 错误 | `SPACE_VERSION_CONFLICT`、`SPACE_VERSION_STATE_INVALID`、`SPACE_TENANT_SCOPE_DENIED` |
| 测试 | 状态机、Published 不可变、单 Draft、租户过滤、SQL 唯一索引和 RowVersion |
| 回滚 | 功能开关关闭；向前修复 Migration；Legacy 表不变 |

### E01-S02 来源、文件和血缘

| 字段 | 内容 |
|---|---|
| 输入 | 详细设计卷二；平台 200MB/租户100MB限制；SHA-256 规则 |
| 输出 | `Space_ModelSource`、`Space_File`、Artifact 引用和 SourceHash 查询 |
| 依赖 | E01-S01 |
| 权限 | `space:source:upload`、`space:model:read` |
| 错误 | `SPACE_FILE_TOO_LARGE`、`SPACE_FILE_TYPE_MISMATCH`、`SPACE_SOURCE_UNSAFE`、`SPACE_TENANT_SCOPE_DENIED` |
| 测试 | 流式哈希、重复上传、跨租户、引用删除、类型/MIME/文件头不一致 |
| 回滚 | 停止新上传；保留元数据和审计；按保留策略清理未引用对象 |

### E01-S03 Job Ledger 和问题模型

| 字段 | 内容 |
|---|---|
| 输入 | 详细设计卷二；候选 `SpaceJobRunner`；Import/BuildScene 缺口 |
| 输出 | Job/Attempt/Step/Issue、租约、Checkpoint、重试分类和进度查询 |
| 依赖 | E01-S01 |
| 权限 | `space:model:read`；创建动作继承来源动作权限 |
| 错误 | `SPACE_JOB_LEASE_LOST`、`SPACE_JOB_NOT_RETRYABLE`、`SPACE_PARSE_FAILED` |
| 测试 | 抢占、续租、Worker 崩溃、重复投递、取消、DeadLetter、跨租户 |
| 回滚 | 停止 Worker；未完成 Job 保留；不得回滚已提交 Published |

### E01-S04 从 Published 克隆 Draft

| 字段 | 内容 |
|---|---|
| 输入 | E01-S01；完整强类型修订；当前 PublishedVersionId |
| 输出 | 异步 Clone Job、完整快照和单活动 Draft 预留 |
| 依赖 | E01-S01、E01-S03 |
| 权限 | `space:model:edit` |
| 错误 | `SPACE_VERSION_CONFLICT`、`SPACE_VERSION_STATE_INVALID`、`SPACE_TENANT_SCOPE_DENIED` |
| 测试 | 空仓、10,000库位、重复请求、Clone失败清除预留、Published 不变 |
| 回滚 | Clone 失败标记 Failed/Abandoned并清除 Draft 预留 |

### E01-S05 版本和来源 API

| 字段 | 内容 |
|---|---|
| 输入 | E01-S01～S03；Design API v1 契约 |
| 输出 | 模型、版本、来源、Job、Issue 的分页查询和创建端点 |
| 依赖 | E01-S01～S03 |
| 权限 | `space:model:read`、`space:model:edit`、`space:source:upload` |
| 错误 | RFC Problem Details；版本/来源/Job不存在；Idempotency冲突；Tenant拒绝 |
| 测试 | OpenAPI、TypeScript/C#生成、分页、并发、幂等、权限和外部角色拒绝 |
| 回滚 | 关闭 Design API 开关；Legacy API 不变 |

### E01-S06 文件安全和保留

| 字段 | 内容 |
|---|---|
| 输入 | E01-S02；Worker 隔离；租户保留配置 |
| 输出 | Quarantine→Scan→Safe/Rejected、引用感知删除和到期清理 |
| 依赖 | E01-S02、E01-S03 |
| 权限 | `space:source:upload`；清理任务使用受限服务主体 |
| 错误 | `SPACE_FILE_MALWARE_DETECTED`、`SPACE_FILE_ARCHIVE_BOMB`、`SPACE_FILE_ENCRYPTED_UNSUPPORTED`、`SPACE_FILE_QUARANTINED`、`SPACE_FILE_ACTIVE_CONTENT` |
| 测试 | 恶意文件、压缩炸弹、加密文件、路径穿越、活动内容、并发引用和跨租户 |
| 回滚 | 关闭解析；文件保持 Quarantined；不删除仍被版本引用的文件 |

## 6. 技术试验启动卡

| ID | 输入 | 输出 | 权限/错误 | 测试 | 回滚 |
|---|---|---|---|---|---|
| E02-S01 | E01-S02、ADR-0001、五类 Seed | CAD 主/备方案、兼容矩阵、授权和性能报告 | Worker 服务主体；转换失败/单位缺失 | 版本/图元、崩溃、50MB、100万图元 | 无方案通过则阻断 DWG Beta |
| E07-S01 | WMS 场景矩阵、卷四 | `ISpaceWmsAdapter` 能力、幂等和健康契约 | `space:integration:manage` | CP6/Mock契约、未知结果、部分失败 | Mock继续；真实发布关闭 |
| E13-S01 | 卷六、AI JSON Schema、ADR-0002 | Provider/确定性端口和契约测试骨架 | AI权限；Disabled/Quota错误 | Mock/本地/外部同一契约 | 外部 AI Disabled |
| E13-S05 | E13-S01/S04、五类 Seed | Mock、本地和首个外部适配器证据 | Provider凭据只在Secret Store | 超时、限流、非法JSON、熔断 | 规则/编辑器继续 |

## 7. 首轮完成报告

批次 A/B 每个工作卡完成时报告：

- 实际文件和 Migration。
- 通过的测试及数量。
- 未通过/跳过测试及原因。
- 权限与错误码变化。
- 与冻结契约的偏差。
- 回滚演练结果。
- 对 196 工程师日基线的估算变化。

任何需要改变冻结行为的偏差必须先提交 [Scope Change RFC](./08-scope-change-rfc-template.md)。
