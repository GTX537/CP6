# W585-01 CRM 增量恢复与原 Kafka 投递结果

Main 基线 `fe011b9aa96fa623ed6c19472638e9097c1d1ade`，受测 `6ef7789ef5eda9a3c42a4934fe5d14f921fe34f9`。CRM 受测 `c72ce2789614d224280b40caa4fdf988acf28ab0`，已合入 `a8cde86ddd863be00cd9e08b8880e95d16c02234`。真实 Main TLS 权威、CRM PostgreSQL 18.6、Dapr 1.18.2、Kafka 4.3.1 和模拟询盘。 [CRM #228](https://github.com/GTX537/CP6.CRM/pull/228) · [CRM 证据](https://github.com/GTX537/CP6.CRM/blob/a8cde86ddd863be00cd9e08b8880e95d16c02234/docs/development/evidence/W585-01/DELIVERY.md)

- 失联前记录原业务和派送水位后取得增量。原库停机后，从独立全量和增量备份重建，校验三份 manifest 与增量自带 WAL。持久化业务位置 `0/63348A8`，新备份开始位置 `0/7000028`，使用增量自己的 WAL 段。原旧归档尾段不参与。时间线 1 → 2。 [PostgreSQL 增量备份](https://www.postgresql.org/docs/18/continuous-archiving.html#BACKUP-INCREMENTAL-BACKUP)
- 恢复库完整写集摘要匹配源，保留原回执、导出制品、审计、角色封存和投递。C02 撤权和重授原始消息分别在分区 0 偏移 13、14 与 Main 及 CRM Inbox 逐字节匹配。受控夹具先核原写集和 Kafka 再重绑，旧版本拒绝，新版本允许。原发布作业空闲，CRM 两条出站消息各保留偏移 0、结束偏移 1。
- 同轮较早的副本缺最后段及原制品时，原有 503 权限拒绝和发布者关闭均通过，业务写集保持不变。最终 **4/4** 通过，零跳过。真实目录与增量一起运行遇到夹具超时，最终恢复场景用模拟目录和真实 Kafka，真实目录路径使用 [W582-01](../W582-01/DELIVERY.md) 的固定证据。保留此前夹具失败和新测试记录于 `D:/CP6-test-artifacts/W585-01`。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。增量先于失联捕获全部原件是本次通过的前提。正式恢复、重开与切换编排仍待验。Main 旧消息、事件和定时、财务、独立 Space Design 与全写者登记继续开放。Actions 启动、重跑及取消为零，未部署。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)
