# E13-S11 Generation Run 取消、重试、降级与 Stale 恢复开发报告

- 状态：Integrated
- 日期：2026-08-06
- 起始集成基线：`c1efea2b`
- 功能分支：`codex/space-e13-s11-recovery`
- 功能提交：`dcbbfca8`
- 证据提交：`c695850f`
- no-ff 集成提交：`d3c2da75`
- 目标分支：`integration/space-v1-20260730`

## 1. 交付结论

E13-S11 已把 Generation Run 的取消、同输入重试、结果对账、Provider 降级和 Stale 重建接入
Design V1 产品链。所有 mutation 都要求内部主体、租户与 Site 写权限、精确 Run rowversion 和
`Idempotency-Key`；SQL Server 使用 Serializable 事务及 Run 行更新锁串行化状态变化。

失败恢复不修改 Published。排队取消和安全点取消不会产生 Draft 写入；Apply 仍沿用 E13-S10 的单
事务提交，取消不能拆分该事务。Stale/Failed 恢复创建新的 Run 与 BuildScene Job，保留
`basedOnRunId`、源文件哈希、映射/货架方案、规则版本和审计历史，不允许原地 rebase 旧 Run。

## 2. 状态机与恢复语义

- `Queued/Preparing/Inferring/Validating/Applying` 可请求取消。排队 Job 立即进入 Cancelled；运行中
  Job 记录 cancellation pending，由 Worker 在安全点确认。
- Worker 在最后一个步骤后再次续租并检查取消。若 Apply 原子事务已经提交，权威 Run/CommandBatch
  仍表示成功；若尚未提交，则取消确认会终止 Run 并把未应用 Proposal 标为 Obsolete。
- 同输入重试只允许终态 `Failed/DeadLetter` 且 failure kind 为
  `Transient/Resource/Bug`，最多 20 次。重试复用同一个 Job、Run、输入哈希、检查点和 ApplyPlan，
  不复制 Job，也不改变已冻结输入。
- `Input/Security` 等不可安全重试失败必须创建新 Run；Apply 阶段的 Provider 风格错误不会被误导为
  RuleOnly。只有 BuildScene 的 `SPACE_AI_PROVIDER_UNAVAILABLE` 才建议规则降级。
- `AwaitingReview/Failed/Stale` 可废弃；Decision 与审计保留，未应用 Proposal 进入 Obsolete。

## 3. 不确定 Apply 结果对账

`POST .../reconcile` 不根据 Job 文本或客户端超时猜测成功。服务只接受已提交且与当前 Draft 匹配的
`Space_ElementCommandBatch`：

- CommandBatch 必须绑定同一 ModelVersion、目标 Floor 与当前 ContentRevision；
- response 必须包含同一 RunId、冻结 ApplyPlanHash 和对象计数；
- 校验通过后可把 Applying/Failed Run 修复为 Succeeded，并清除旧失败与取消残留；
- Job 显示成功但没有权威 CommandBatch 时记录稳定
  `SPACE_AI_APPLY_RESULT_UNKNOWN`，要求人工升级处理，不补写 Draft。

真库运维演练模拟“Draft 已提交但 Run 被外部故障误记 Failed”，对账后恢复同一成功 revision，未新增
第二个 CommandBatch，也没有再次推进 ContentRevision。

## 4. Stale/Failed replacement Run

恢复 API 以最新精确 Draft revision 创建 replacement Run：

- `SamePolicy` 复用源 Run 的策略快照和 ProviderConfigVersion；
- `RuleOnly` 固定 `PolicySnapshot=Disabled` 且清空 ProviderConfigVersion，不调用 Provider；
- replacement 使用新 RunId 与新 BuildScene Job，旧 Run 不原地修改为新基线；
- 同 SourceHash 的既有人工 Modify 锁定事实继续通过 S09 的 lineage 服务物化；旧 Proposal 随后
  Obsolete，Decision/审计不删除；
- Failed 源 Run 先在同一事务中退役并落库，再插入 current replacement，避免把源 Run 自己误判为
  并发恢复或撞 current business-key 唯一索引；
- 相同幂等键并发到达时，锁内先读取已提交重放记录，再校验旧 rowversion，保证只执行一次并返回一
  个原始响应和一个 replay。

## 5. API、SDK 与前端

新增 Design V1 操作：

- `POST /api/space/design/v1/generation-runs/{runId}/cancel`
- `POST /api/space/design/v1/generation-runs/{runId}/retry`
- `POST /api/space/design/v1/generation-runs/{runId}/discard`
- `POST /api/space/design/v1/generation-runs/{runId}/reconcile`
- `POST /api/space/design/v1/versions/{versionId}/generation-runs`

Run 查询新增 `basedOnRunId`、`degradedReason`、`cancellationPending`、`retryable`、
`recoveryAction` 和 `applyCommitState`。所有新 POST 暴露 Problem Details、幂等重放响应头和稳定
operationId；C# 与 TypeScript SDK 已由统一脚本重新生成。

前端审核面板按服务端分类显示安全取消、同输入重试、权威对账、最新 Draft 同策略重建、Provider
故障 RuleOnly 降级和废弃。每个动作持有独立幂等键；409/422 会丢弃旧键并刷新权威 Run。创建
replacement 后 URL 切换到新 RunId，旧 Decision 历史不被覆盖。

## 6. 数据与兼容性

本切片复用 E13-S02/S09/S10 已有的 Run、Job、Idempotency、Proposal、Decision、LockedFact 与
CommandBatch 表，没有新增 Migration。`SpaceGenerationRunDefinition` 增加可选 RunId，仅用于先生成
RunId 后把它冻结进 BuildScene Job payload 的循环身份绑定；既有调用保持兼容。

Job 终态失败、最终租约耗尽和取消确认会同步 Run 终态，避免 Job 已结束而 Run 永久停留在
Applying/Inferring。成功终态统一清理失败码、失败摘要和取消 pending 残留。

## 7. 验证证据

| 门禁 | 结果 |
|---|---|
| WebApi Debug build | 0 warning / 0 error |
| Run/Job 状态机与分类聚焦 | 42 passed / 0 failed |
| OpenAPI/权限聚焦 | 52 passed / 0 failed |
| AI Apply/Recovery 真实 SQL 整组 | 14 passed / 0 failed / 0 skipped |
| 前端 API/组件聚焦 | 2 files / 6 tests passed |
| 前端全量 | 129 files / 695 tests passed |
| 前端 type-check | passed |
| 前端 production build | passed；仅既有大 chunk 提示 |
| OpenAPI/C#/TypeScript SDK 生成 | passed |
| `git diff --check` | passed |

真实 SQL 14 项包含 E13-S10 的原子新增、更新、故障回滚、Stale、并发幂等与 external-principal 基线，
并新增排队取消、运行中安全点取消、Resource 同 Job 重试、Failed replacement、Stale RuleOnly
replacement、CommandBatch 对账和并发相同取消键重放。测试使用本机命名 SQL Server、Windows 身份
与 `Encrypt=False`；连接凭据未写入仓库或报告。

## 8. 清理、回滚与剩余边界

前端依赖、production `dist` 与顶层 .NET `bin/obj` 已在集成后删除，共清理 34 个可重建目录并
回收 1,008,090,267 bytes（约 0.939 GiB）；依赖和产物可由 lockfile/源码重建。运行回滚可
关闭新 UI/API 权限并停止领取恢复后 BuildScene Job；已有 Run、Proposal、Decision、CommandBatch
和幂等记录保留，数据库不需要破坏性回滚。

这是 E13-S11 开发切片完成证据，不是完整 AI/CAD 生产签收。生产默认
`UnavailableSpaceBuildSceneJobStepExecutor` 仍失败关闭；真实 BuildScene 全阶段
`LoadLockedFacts` 自动接线、不同 SourceHash 的确定性几何建议继承与人工确认、首个外部 Provider、
授权真实 DWG/DXF、正式 20 文件黄金集、性能/影子试点和发布晋级证据仍是独立缺口。本报告不把
合成数据或 RuleOnly 排队描述成外部 Provider/CAD 端到端通过。
