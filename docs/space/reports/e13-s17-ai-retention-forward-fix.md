# E13-S17 AI 数据保留、前向修复与清理开发报告

- 状态：Integrated
- 日期：2026-08-06
- 起始集成基线：`ac9c977c`
- 功能分支：`codex/space-e13-s17-retention-forward-fix`
- 功能提交：`12db5531`
- no-ff 集成提交：`e7720df4`
- 目标分支：`integration/space-v1-20260730`

## 1. 交付结论

E13-S17 已交付 AI 数据保留字段、索引、可恢复清理 Job、幂等部署 SQL 和失败关闭的前向修复边界。
清理只处理非 current、终态且归属 Draft/Failed/Abandoned 版本的 Generation Run；
Published、Superseded、Publishing 和 ReconciliationRequired 均不会进入候选集。Run 级保留锁可以延长，
不能缩短，并同时保护生成载荷和 Usage 归档。

生成身份、状态、SourceHash、Proposal Decision、Locked Fact、CommandBatch、Job/Attempt/Step 和审计历史
不删除。默认 90 天后仅净化 Draft/Failed 生成大载荷；Usage 至少保留 365 天后才进入逻辑归档；暂存
JSON 清零并按平台统一软删除。E01-S06 已有的 30 天未引用文件/Artifact 回收继续独立运行，本卡不重复
或放宽其引用感知边界。

## 2. 数据模型与迁移

Migration `20260806160931_SpaceE13S17AiRetention` 仅做加法：

- `Space_GenerationRun`：`RetentionHoldUntilUtc`、`PayloadPurgedAtUtc`；
- `Space_GenerationProposal`、`Space_ModelIssue`：`PayloadPurgedAtUtc`；
- `Space_AiUsageRecord`：`ArchivedAtUtc`；
- 新增 Run、Proposal、Issue、Usage 四个租户优先保留扫描索引。

幂等 SQL 位于
`CP6.Space.Infrastructure/Migrations/Scripts/20260806160931_SpaceE13S17AiRetention.sql`。
脚本从 E13-S10 迁移点向前部署，事务内只包含 `ALTER TABLE ADD`、`CREATE INDEX` 和 Migration History
写入，没有 `DROP`、`DELETE`、`TRUNCATE` 或业务数据更新。EF 模型快照无待迁移变化。

Migration `Down` 不删除列或索引，而是以 SQL `THROW 51017` 失败关闭，明确要求创建经审查的更新版本
Migration。这样即使应用版本回退，已写入的审计和保留标记仍留在数据库中。

## 3. 清理 Job 与可观测性

新增 `AiRetentionCleanup` JobType 和 Tenant SubjectType。受限内部 service principal 通过
`SpaceAiRetentionCoordinator` 创建每日冻结窗口 Job；外部主体在排队前稳定拒绝，默认授权实现关闭。
Job 使用既有 Ledger、Attempt、Step、租约、重试和 checkpoint：

- processor version 为 `space-ai-retention-v1`；
- 每日 Tenant + window 形成稳定业务键；
- 最多 5 次安全重试，默认 250、最大 1,000 行批次；
- Step checkpoint 记录候选 Run、净化 Run/Proposal/Issue、退役 Staging 和归档 Usage 数量；
- 同租户 SQL Server 使用 transaction-owned `sp_getapplock`，并发执行返回可重试
  `SPACE_AI_RETENTION_BUSY`；
- payload 使用严格 JSON schema、UTC 零点冻结窗口、SHA-256 输入/输出哈希，未知字段和缩短保留期失败
  为 `SPACE_AI_RETENTION_INVALID`。

仓库没有新增公共 HTTP 清理入口，也没有把默认关闭授权改为管理员权限。生产定时器必须在受控 Worker
composition root 中提供专用内部身份后调用 Coordinator；没有该配置时不会自动产生清理 Job。

## 4. 保留与净化语义

- Run：保留 ID、Tenant/Site/Version/Source/Job、SourceHash、冻结策略、状态和失败码；清除失败摘要、
  降级原因、Apply rowversion 副本和大计数 JSON。
- Proposal：保留身份、SourceKey/Hash、类型、置信度、状态、阻断标记和 AppliedLogicalId；几何、属性、
  关系、证据、字段来源、人工 patch 与锁定字段 JSON 净化为空。
- Issue：只允许 generation-scoped Issue 净化；保留 code、severity、status、解决身份和建议动作，清除
  SourceRef、参数 JSON 和人工确认正文。
- Staging：清空临时 normalized payload，保留小型校验身份后显式软删除；普通查询立即不可见。
- Usage：财务事实不物理删除；满 365 天且无有效 Run hold 时写 `ArchivedAtUtc`，普通在线查询不再返回，
  `IgnoreQueryFilters`/审计路径仍可读取。
- Decision、Locked Fact、预算账本、CommandBatch、Job Ledger 和操作审计不被清理 Store 修改。

重复执行在所有实体级操作和 Store 级批次上均为零副作用。若一个 Run 的子载荷超过单批上限，后续
Job 重试/下一轮继续处理；只有 Proposal、Issue 和 Staging 全部完成后才标记 Run 已净化。

## 5. 前向修复操作说明

1. 发布前备份数据库并记录当前应用 SHA、Migration History 和目标脚本 SHA；暂停新的保留 Job。
2. 从已包含 `20260806110504_SpaceE13S10AtomicApply` 的数据库执行本卡幂等 SQL，确认事务提交。
3. 验证 Migration History 只有一条 S17 记录，5 个新列和 4 个索引均存在；再恢复内部清理排队。
4. 若脚本失败，保持 AI/清理排队关闭，保存错误号、数据库版本、Migration History 和失败批次；事务
   回滚后不得执行本 Migration 的 `Down`，不得手工删除 AI 表、列、Decision 或审计数据。
5. 在更新代码基线上创建时间戳更大的 EF forward-fix Migration，只补充缺失/错误对象或兼容转换；
   使用存在性检查和可回滚事务生成新的幂等 SQL，在副本和隔离库连续执行两次。
6. 验证应用可在新增列存在时继续使用确定性 Import/规则/编辑路径；关闭 AI 仅停止新 Provider 调用，
   不删除新表。经审查部署 forward-fix 后，再恢复清理 Worker。

## 6. 验证证据

| 门禁 | 结果 |
|---|---|
| E13-S17 单元 | 6/6 passed |
| E13-S17 内存持久化/迁移契约 | 4/4 passed |
| KOUSQLSERVER 迁移、重复清理、并发租约、脚本双执行 | 3/3 passed / 0 skipped |
| Space Unit 全量 | 430/430 passed |
| 默认 Space Integration 全量 | 255 passed / 81 SQL-gated skipped / 0 failed |
| Release build：Unit | 0 warning / 0 error |
| Release build：Integration 依赖链 | 0 error；仅 3 条既有 Core nullable warning |
| EF pending model changes | none |
| `git diff --check` | passed |

真实 SQL 测试使用本机 `KOUSQLSERVER`、Windows 身份和 `Encrypt=False`，每例创建并删除唯一临时库；
连接串未写入仓库或报告。部署脚本先把数据库迁移到 E13-S10，再在同一连接连续执行 S17 脚本两次，
断言一条 History、5 个列和无重复失败。

全量真实 SQL 共 336 项时得到 333 passed / 3 failed；其中处理器数量契约已按本卡从 4 更新为 5 并
单独通过。剩余两个 Version Clone 用例已在本卡起始提交 `ac9c977c` 的独立临时 worktree 复现，根因是
既有 clone SQL 未写 Zone/Aisle/Rack 的非空 `Name`，与本卡表、迁移和代码无交集，已登记 Known Issue。

## 7. 回滚、安全与剩余边界

应用回滚应关闭 AI 和保留 Job 排队，保留所有新列、索引、Job Ledger、Decision、Usage 和审计；数据库
只允许前向修复。清理不调用 Provider、对象存储、CAD Worker、WMS 或 Draft Apply，也不读取原始 CAD。

本卡完成的是迁移与保留清理开发闭环，不等于生产定时器已启用或 AI 已可发布。E13-S14/S15/S19 仍需
正式黄金集、供应商/区域/DPA、影子运行、试点和跨职能签字；E13-S18 依赖 S15，当前仍不能提前签收。
生产 BuildScene executor 和 External Provider 继续默认失败关闭。
