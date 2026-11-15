# W599-01 CRM 旧源重启后的本地写者隔离

2026-11-15 UTC。CRM 受测源码 `8d6fec15a276387d4174d34c74d6add3e941175d`，远端 CRM `main` `8f650ced6b186fae4119a4d93d6859d7fa995df8`。 [CRM 交付记录](https://github.com/GTX537/CP6.CRM/blob/8f650ced6b186fae4119a4d93d6859d7fa995df8/docs/development/evidence/W599-01/DELIVERY.md) · [CRM PR](https://github.com/GTX537/CP6.CRM/pull/232)

- 在 W595 两个全部登记租户的隔离 PostgreSQL、Main、CRM、Kafka 与 Dapr 演练中，原库用精确原凭据建立一个 CRM API 会话和两个 submission Outbox 会话。持久提交登录禁用后逐个排空。原数据库在原目录和端点重新启动，校验身份和业务写集，目标重绑前后使用同样三份凭据均得到 PostgreSQL `28000`。
- 只用全量的目标有一租户最终写集不符，就绪维持 503。完整增量恢复两租户一致，独立维护连接重绑后公开就绪 503 → 200，两个发布者均为 `IDLE`，原 Kafka 记录和结束偏移 2 / 2 / 30 保持。源与目标均已停止，具体端点和时间线见原证据。
- 确认轮 **8/8**，零跳过。首轮 OID 文本类型的身份夹具错误在更改登录前失败，已修正并保留。CRM 任务分支已删除，DEV-2 已释放。 主线台账复用 CRM 证据，本提交为文档同步。Actions 运行和生产部署均为零。

本轮证明同一隔离源 PostgreSQL 原端点重启后对已登记的三份旧凭据持续拒绝。正式切换仍需完整实际写者清单、覆盖部署与网络/broker 的控制面隔离、目标专属身份和可信重绑入口。正式 PostgreSQL **26 PASS / 1 NOT_RUN**。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)
