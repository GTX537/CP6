# E13-S02 Run、Proposal、Decision、Usage 数据模型完成报告

- 状态：**Integrated**
- 日期：2026-07-30
- 功能分支：`codex/space-e13-s02-generation-model`
- 功能提交：`cff25a25`
- no-ff 集成提交：`94822669`
- 集成分支：`integration/space-v1-20260730`

## 1. 交付结论

E13-S02 已按冻结边界交付并进入唯一 Space 集成基线。新增 `SpaceGenerationRun`、`SpaceGenerationProposal`、`SpaceProposalDecision` 和 `SpaceAiUsageRecord` 四个租户化审计模型，以及独立 EF Migration 和幂等 SQL 脚本。

Run 固定 Site、ModelVersion、SourceHash、BaseContentRevision、映射/货架方案版本、规则版本、AI 策略、Provider 配置版本、Schema 版本和 Job。Proposal 保存 AI 原值、来源、证据、字段来源、置信度和人工 Patch；Decision 仅可追加；Usage 以租户和 Provider 请求哈希去重，防止重复计费。

## 2. 冻结边界

### 已实现

- Run 完整状态机：Queued、Preparing、Inferring、Validating、AwaitingReview、Applying、Succeeded、Failed、Cancelled、Stale。
- 状态转换、取消等待、废弃、Stale、失败重试和只增不减进度的领域门禁。
- 启用 AI 的 Run 必须固定 Provider 配置版本；Disabled Run 不得固定 Provider 配置。
- Proposal 的 Proposed、Accepted、Rejected、Modified、Applied、Obsolete 转换。
- Blocking Proposal 不可接受或修改；Applied Proposal 不可重复应用或转为 Obsolete。
- Decision 的 Before/After、锁定字段、原因、评论和批次审计；Context 拒绝更新和删除历史 Decision。
- Usage 的输入/输出单位、预估/实际最小货币单位、币种、延迟、结果和记录时间。
- 四表全量 Tenant Query Filter、Tenant 复合外键、RowVersion、审计字段和软删除过滤。
- Current Run、Run 内 SourceKey+ProposalType、Provider 请求哈希的唯一约束。
- 进度、置信度、用量、费用和延迟的数据库 Check Constraint。
- JSON、SHA-256、UTC、ISO 4217 和枚举值在领域入口失败关闭。

### 明确未实现

- `SpaceAiProviderConfig`、MappingProfile、RackGenerationProfile 的实体和管理 API。
- E13-S03 Import/BuildScene Worker 处理器。
- E13-S04 CAD IR 最小化与脱敏。
- E13-S05 Provider 生产适配器和供应商证据。
- E13-S06 Provider 输出不可信输入校验。
- E13-S07 规则/AI 融合和确定性几何生成。
- Issue/Artifact 的 Run/Proposal 扩展、内部 Staging、Apply、并发槽和预算预留。
- AI HTTP API、Provider 凭据、外部网络调用、Prompt 或响应正文持久化。

## 3. 数据库变更

Migration：

`20260730174231_SpaceE13S02GenerationDataModel`

新增表：

- `Space_GenerationRun`
- `Space_GenerationProposal`
- `Space_ProposalDecision`
- `Space_AiUsageRecord`

关键物理约束：

- `UX_GenerationRun_Tenant_Business_Current`
- `UX_Proposal_Tenant_Run_Source_Type`
- `UX_AiUsage_Tenant_ProviderRequest`
- `FK_Space_GenerationRun_Source_Tenant_Version`
- `FK_Space_ProposalDecision_Proposal_Tenant_Run`
- `CK_Space_GenerationRun_Progress`
- `CK_Space_GenerationProposal_Confidence`
- Usage 单位、费用和延迟三个 Check Constraint

本卡不修改 Legacy 表、不增加权限和错误码，也不把未来 E13 表提前并入本 Migration。

## 4. 验证证据

| 检查 | 结果 |
|---|---|
| `dotnet build CP6.slnx -c Release --no-restore` | 0 errors；首次完整构建 10 existing warnings，最终增量构建 0 warnings |
| Space UnitTests | 113 passed；其中 E13-S02 新增 16 |
| Space IntegrationTests（默认门禁） | 44 passed，31 SQL-gated skipped |
| E13-S02 SQL Server Migration/唯一约束测试 | 1 passed，0 skipped |
| Space SQL 全量启用 | 75 项中 73 首轮通过；2 项既有并发测试在并行建库压力下超时，分别串行复跑均通过 |
| EF Migration 漂移 | `has-pending-model-changes` 通过 |
| CP6.Tests | 2680 passed，17 environment-gated skipped |
| CP6.Client.Tests | 71 passed |
| 精确 C# whitespace/style 与 staged diff | 通过 |

SQL 测试使用本机 `KOUSQLSERVER`、Windows 集成认证和每测试唯一临时数据库；测试结束自动删除临时库。新 E13-S02 SQL 测试真实执行 Migration，并由 SQL Server 证明重复 Provider 请求不能重复计费、同租户同业务键不能存在两个 Current Run。

全量 SQL 首轮的两个超时分别来自既有文件保留并发测试和既有 Job 业务键测试，均在隔离串行复跑 2 秒内通过；失败栈未触及本卡代码。该现象记录为测试宿主并行建库压力，不记作功能失败，也不声称首轮 75 项一次性全绿。

## 5. 回滚与故障演练

- E13-S01 的租户 AI 开关仍默认 Disabled；本卡没有新增可创建 Run 的 HTTP 入口或 Worker。
- Migration 只新增表、索引、外键和约束，不改变 Legacy 或 Published 数据。
- 生产回滚采用关闭入口和向前修复 Migration，不破坏性删除 Decision/Usage 审计。
- Decision 更新和删除在 EF SaveChanges 前失败关闭。
- 跨租户查询由四个 Query Filter 隐藏，所有关系外键均含 `TenantId`。
- 重复 Provider 请求哈希由 SQL 唯一索引拒绝，不产生第二笔 Usage。
- Stale、Succeeded、Cancelled 和已 Applied Proposal 不允许继续执行不合法转换。

## 6. 偏差、估算与后续

与详细设计卷六和 E13-S02 冻结验收无产品行为偏差。字段采用项目既有 `CreatedAtUtc` / `ModifiedAtUtc` 命名；数据库列语义与设计中的 CreatedAt/UpdatedAt 一致。

E13-S02 的 3 工程师日规划基线不调整；没有产生足以重估 196 工程师日总基线的新证据。下一张可独立启动卡为 E13-S03。E13-S04 继续等待 E02-S03，E13-S05 继续等待 E13-S04 和正式供应商证据。
