# E13 首次 Generation Run 创建入口生产链接线报告

- 状态：已进入受控集成分支；外部 Provider 与未经验证 RackProfile 继续失败关闭
- 日期：2026-08-08
- 起始集成基线：`54f1cda7`
- 功能分支：`codex/space-generation-run-create`
- 功能提交：`770bdc96`
- 验证报告提交：`bbcaf6fe`
- no-ff 集成提交：`9d0971f4`
- 目标分支：`integration/space-v1-20260730`

## 1. 交付结论

`POST /api/space/design/v1/versions/{versionId}/generation-runs` 现在是统一的 Generation Run 创建入口：

- 不传 `basedOnRunId` 时，从已确认的 DWG/DXF CAD Preview 首次创建 `Queued` Run 和 `BuildScene` Job；
- 传 `basedOnRunId` 与其 RowVersion 时，保留 Failed/Stale replacement Run 恢复语义，不原地 rebase；
- 请求必须同时提交 `If-Match` Draft RowVersion、`expectedContentRevision` 和 `Idempotency-Key`；
- 权限继续使用 `space:model:generate-ai`，审计操作统一为 `space.ai-generation-run.create`；
- OpenAPI、C# SDK 与 TypeScript SDK 已从 `RecoverGenerationRun` 收敛为 `CreateGenerationRun`。

当前生产实现只放行 `RuleOnly` 首次创建。`AiAssisted` 在租户 Disabled 时返回 `SPACE_AI_DISABLED`；即使策略已启用，真实 Provider-backed BuildScene 尚未配置时仍返回 `SPACE_AI_PROVIDER_UNAVAILABLE`。不会为了通过开发流程注册 Mock/Local Provider、密钥、URL 或网络访问。

## 2. 冻结输入与失败关闭

首建服务在 Serializable 事务中重新校验：

- 内部主体、Tenant、Actor 和 Site 写数据范围；外部主体在数据读取前稳定 403；
- Version 属于当前租户、仍为 Draft，RowVersion 与 `If-Match` 一致，ContentRevision 与请求一致；
- Source 属于该 Version、类型为 DWG/DXF、Source 文件仍 Clean/Source-retained、SHA-256 一致；
- Source 已达到 PreviewReady/Imported，坐标元数据与 SourceHash、`LOCAL_MM_Z_UP` 和活动目标 Floor 一致；
- MappingProfile 必须与已确认 CAD Preview 的权威 Profile 一致；
- 非空 RackGenerationProfileVersionId 目前拒绝，直到存在可校验的权威版本存储，禁止把任意 GUID 当成有效配置；
- 必须存在由成功 CadParse Job 生成、文件仍 Clean 的 PreviewSet Artifact。

创建时不再只让 Worker 选择“当时最新”的 PreviewSet，而是把 `previewArtifactId + artifact file SHA-256` 固定进 Job 输入和业务键。BuildScene Worker 若收到固定点，只接受该精确 Artifact 与哈希；replacement Run 继承旧 Run 的固定点。旧 Run 没有固定字段时仍按原有血缘校验兼容执行。

## 3. 幂等、业务去重与恢复

- `IdempotencyKeyHash = SHA256(TenantId + operation + normalized key)`，公开 operation 固定为 `space.ai-generation-run.create`；
- 同键同请求 24 小时内返回原 Run，并设置 `Idempotent-Replay: true`；记录保留 90 天；
- 同键不同首次/恢复请求稳定返回 `SPACE_IDEMPOTENCY_KEY_REUSED`；
- 不同键但相同固定业务输入复用当前 Run，不创建第二个 Job；
- BusinessKey 包含 Version、Source/SourceHash、Revision、Floor、Mapping、Rack profile、Preview Artifact/SHA、Mode、Rule 与输入 Schema；
- 恢复仍由既有 E13-S11 服务执行状态、RowVersion、LockedFacts、旧 Proposal Obsolete 和 replacement Run 原子合同；统一入口额外写公开 create 幂等记录。

RuleOnly 首建和恢复都不调用 Provider、不创建 AI Usage、不写 Draft；只有 BuildScene 生成只读 Proposal/Issue，后续仍必须人工 Decision 并经 E13-S10 原子 Apply 才能修改 Draft。

## 4. API 与 SDK 影响

公开路由未增加，Design V1 operation 总数仍为 115。该路由的请求变为 `CreateSpaceAiGenerationRunRequest`，新增/冻结：

- `sourceId`
- `mappingProfileVersionId`（可空类型但作为显式合同字段）
- `rackGenerationProfileVersionId`（可空类型但作为显式合同字段）
- `mode`
- `expectedContentRevision`
- 可选 `basedOnRunId`
- 可选 `expectedBasedOnRunRowVersion`

响应为 `SpaceAiGenerationRunAcceptedDto`，包含 Run/Job、SourceHash、Mode、Policy、BasedOn、链接、业务复用和幂等重放标记。调用方必须重新生成或更新 SDK，使用 `CreateGenerationRun` 并传 `If-Match`；旧生成客户端的 `RecoverGenerationRun` 方法不再是公开 Design V1 操作。

## 5. 验证证据

| 门禁 | 结果 |
|---|---|
| 首建/统一恢复/失败关闭 + BuildScene + 默认注册聚焦 | 9/9 passed |
| OpenAPI 与 AI 审计安全合同 | 31/31 passed |
| Space Unit 全量 | 484/484 passed |
| 默认 Space Integration | 283 passed / 94 SQL-gated skipped / 0 failed |
| CP6.Tests | 2812 passed / 17 environment-gated skipped / 0 failed |
| C# SDK Release | 0 warning / 0 error |
| TypeScript SDK strict | passed |
| OpenAPI/C#/TypeScript SDK drift | passed |
| 完整 solution Release（含 Desktop/Android AOT） | 0 error / 7 条未改动测试文件既有 warning |
| C# whitespace / `git diff --check` | passed |

本切片没有新增数据库表、列、Migration、外部 Provider、Secret、网络访问、Draft 自动写入或 High Accept。
最终完整构建的 7 条 warning 位于未改动的 `SpaceRetryLeaseMigrationTests`、`PendingCookieTests`、
`InboxServiceTests` 和 `BudgetVsActualTests`，不来自本切片文件；C# SDK 单独构建为 0 warning / 0 error。

## 6. 剩余边界

- 真实 Provider-backed BuildScene、Provider Config 版本存储、预算/配额端到端和外发合规证据仍未完成；
- 权威 RackGenerationProfile 版本持久化/读取尚不存在，首建请求不能固定未经验证的 RackProfile；
- 无人工锁定时的确定性 Zone/Aisle/Rack 父关系推导仍会产生 Blocking；
- 不同 SourceHash locked facts 需要确定性几何匹配与人工确认；
- 建模 Web UI 的“从 CAD Preview 创建 Run”交互、正式 CAD/黄金集、S14～S15/S18/S19 证据仍独立存在。

因此，本报告证明内部建模主体可通过权威 API 从已确认 CAD Preview 首次排队 RuleOnly Generation Run，并可沿既有 Worker 到达 AwaitingReview；不证明外部 AI、未经验证 RackProfile、正式 CAD 或 GA 签收。

## 7. 受控集成与清理

功能提交 `770bdc96` 和验证报告提交 `bbcaf6fe` 已通过 no-ff 提交 `9d0971f4` 进入
`integration/space-v1-20260730`；合并后首建/恢复/BuildScene/注册 9/9 与 OpenAPI/审计 31/31 复验通过。
远端集成推送前清理当前隔离工作区内 36 个可重建 `bin/obj` 目录、8,622 个文件，释放
1,666,117,627 bytes（1,588.93 MiB，约 1.55 GiB）。`main` 未修改。
