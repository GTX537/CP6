# W576-01 · 旧 OA 批量转交逐条复核授权

2026-11-14。Main 基线 `911d85a157d5e3184f68a9d4c8f6275160616b71`，受测修复 `ea85517352a9d2353109d4aea0399a2389fc7e5f`。在隔离 PostgreSQL 18.6 上通过真实 Main Kestrel 和本地登录，使用模拟待办、用户和角色。 [缺陷 #1071](https://github.com/GTX537/CP6/issues/1071)

旧批次只在进入 HTTP 入口时检查 `oa-inbox:batch-transfer`。基线两个提交顺序均转交两条，包括撤权生效后的条目。修复复用 Main IAM 现有事务栅栏，逐条读取动作权限、原待办归属和接收人有效状态。相应改派、传签履历、审批历史和通知入队一起提交。成功计数只包含确认提交的条目，未知的提交结果沿 IAM 原错误码返回。

| 实际顺序 | HTTP 和持久化结果 |
| --- | --- |
| 撤权先提交 | 批次保持 200 汇总，两个条目均为 `IAM_FORBIDDEN`，业务写集保持原样。 |
| 第一条转交先提交 | 首条改派、传签履历、历史和待办通知保留，撤权后的第二条在汇总中被拒。旧会话再次提交返回 403。 |
| 响应候选生成后另一事务改派 | 待办改派持有迁移已安装的共同根，批次等待后读到新处理人。该条以 `E-WF-002` 拒绝，下一条单独完成。 |

独立连接通过 PostgreSQL `pg_blocking_pids` 确认等待链，并读回角色修订、权限代次、身份快照、Outbox、IAM 审计及回执。第三个用例初次等待额外行锁超时，实际竞争发生在既有语句触发器持有的事务根。移除冗余行锁，按该根的实际等待验证。最终 3 项真实 PostgreSQL HTTP 回归和 31 项相关组件测试通过，零失败、零跳过。 [回归入口](../../../../CP6.Tests/Erp/Pgt017LegacyOaBatchTransferPostgreSqlTests.cs) · [已安装的事务根](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261102183000_PersonalFormV2.cs#L287)

本地 Debug PostgreSQL 发布使用受测程序集，实际启动后 `/health/live` 返回 200，匿名批量转交返回 401。WebApi SHA-256 `7452c0f09b093d10125a30903cbaa01bbbf9c422a2d3715885a983e3c2fdb6ee`。310 个文件的清单 SHA-256 `98cc0967410844346a7e2ec7ba536d506d95c7e3905435eed41b0392972ca826`。基线 TRX、失败诊断、最终 TRX 和发布启动日志位于 `D:/CP6-test-artifacts/W576-01`。本地构建延续已有依赖与编译警告。Actions 启动、重跑和取消为零。发布验证在隔离环境完成。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。剩余旧 OA 撤回、退回、批量办理及触发器，财务、独立 Space Design 和其余普通写者。欠缺权威写集后的差量对账、安全重开与切换编排也待核销。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)
