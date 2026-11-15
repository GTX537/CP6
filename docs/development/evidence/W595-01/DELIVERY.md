# W595-01 CRM 全登记租户 PostgreSQL 恢复对账

2026-11-15 UTC。CRM 源码 `d1ee854c0a9688ff9b32f96d6152ea4d53695504` 已通过 CRM PR #231 合入，远端 CRM `main` 为 `6a69fb14bc7508b4833ca8a833a8b094fbdb05a0`。Main 进程复用实际受测源码 `be633749701eade262129f80f2dc70f8f5b7108d`。 [CRM 验收与复现](https://github.com/GTX537/CP6.CRM/blob/6a69fb14bc7508b4833ca8a833a8b094fbdb05a0/docs/development/evidence/W595-01/DELIVERY.md) · [CRM PR #231](https://github.com/GTX537/CP6.CRM/pull/231)

- 隔离 PostgreSQL 18.6、Kafka 4.3.1 与 Dapr 1.18.2，同一 CRM 存储登记两个模拟租户。实际 Main 撤权和重授，以及 CRM 原生业务入口、Inbox、Outbox 与 Kafka 分别覆盖两种提交顺序。全部登记租户核对 Main 权限代次、CRM 原权限发布与封存、询盘及导出回执、制品、审计和派送。三个主题独立核对键、哈希与偏移。
- 仅用全量时，两个登记仍在，其中一个租户缺少最终写集。整库保持 503、两个发布者都被拒绝，绑定保留。叠加真实增量及随附 WAL 后逐租户对账 **2/2**，源写者已停止，测试维护连接重绑目标时间线。公开就绪 **503 → 200**，两个发布者均为 `IDLE`，Kafka 结束偏移 **2 / 2 / 30** 保持一致。运维探针的读可用为真，未证明运行身份的状态仍为 503。
- 本轮跨仓及恢复 **7/7**，已有的等价输入回归 **6/6**，零跳过。CRM 任务分支本地及远端已清理，专用进程已退出，DEV-2 已释放。实际失败与最终证据在 CRM 交付报告所列本地目录。Actions 启动、重跑及取消为零，未部署。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。正式切换还需可信的生产授权、能持续拒绝原端复活的写者隔离及可信 Main/Kafka 水位，并与尚未完成的 Space 和财务 writer 核对一同收口。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)
