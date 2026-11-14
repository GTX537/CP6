# W587-01 旧 OA 定时领取与补跑的事务栅栏

[缺陷 #1089](https://github.com/GTX537/CP6/issues/1089) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)。Main 基线 `ef3175ad20fffce52d3e582561dd101fe8764dc7`，受测 `f969f66b82d054f9b9d3dde8a89628ad8c046731`。隔离的 PostgreSQL 18.6、Main Kestrel 租户管理 HTTP 和定时 Worker 调用的扫描入口，使用模拟租户和流程。

定时领取和旧占位补跑先取得 Main 现有事务栅栏，再读取当前租户、触发器和到期或占位状态。每条单独提交原键流水、下次到期、实例、任务、历史和水位。提交后外部调用交由既有持久任务管线。沿用旧到期键、补跑宽限和按扫描时刻推进的一次补发规则。暂停保留已有流水、实例和未处理的到期。非 PostgreSQL 保留历史两段式。

- 基线 5 项中 3 项失败。暂停先行时扫描未等待管理事务。正常领取先行时，暂停跨过仍在等待的领取并完成。修复后两种顺序分别在领取和补跑中成立。业务先提交时保留一份合法写集，暂停先提交时流水、实例、任务、历史和水位无变化。恢复后原键只完成一次。独立观察连接通过 `pg_blocking_pids` 核对实际锁链，并核对暂停的代次、快照、Outbox、审计和回执。
- 两个 Worker 争同一到期槽只产生一份原键流水和流程实例。数据库在 commit 阶段拒绝时查得无业务写入，下次到期回滚，下一次扫描以同一原键完成。原有 `manual:` 占位受相关回归保护。
- `final/results.trx` 实际 PostgreSQL 24/24，`commit-rollback/results.trx` 6/6，五项重合，共 25 个不同用例，零跳过。构建零错误，现有依赖审计警告保留。局部发布 310 个 SHA-256，实际 Kestrel 存活 200、未知触发器 404，主宿主和隔离 PostgreSQL 已停止。原始验证、摘要和停止日志在 `D:/CP6-test-artifacts/W587-01`。Actions 无启动、重跑或取消，未部署。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。旧事件触发、财务、独立 Space Design 和其他写者待核对，CRM 正式恢复、重开与切换编排待完成。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646)
