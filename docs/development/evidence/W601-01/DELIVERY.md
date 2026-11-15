# W601-01 CRM 恢复库受信重绑与目标写入凭据

2026-11-15 UTC。CRM 产品源 `a62176c9eb11255c9f6e1f7f81775482b52d63e8`，CRM 远端 `main` `2b6092a6e63f405067da05387e223bd5d1b5ea1c`。 [CRM 交付报告](https://github.com/GTX537/CP6.CRM/blob/2b6092a6e63f405067da05387e223bd5d1b5ea1c/docs/development/evidence/W601-01/DELIVERY.md) · [CRM PR](https://github.com/GTX537/CP6.CRM/pull/233)

- CRM 增加显式登记和重绑的基础设施操作。签名批准覆盖目标数据库、源与目标时间线、登记租户集合及 Main、Kafka、恢复写集、源隔离和目标身份的证据摘要。原生事务锁定登记集和绑定，时间线与原操作回执一起提交。
- 隔离 PostgreSQL 18.6 的最终 **8/8** 场景零跳过。分别截断实际登记和重绑的提交应答，原操作读回同一结果。缺少一个租户的候选就绪 503，完整目标按批准后恢复 200。已重绑库再次物理恢复至 `00000003` 时拒绝旧批准，保留原回执，继续保持 503。
- 源库实际重启，原 API 和两个租户发布者的三份凭据在旧端点均拒绝。恢复库沿用群集内的角色名，轮换为目标专用密码，旧凭据也被目标拒绝。实际新写者通过各自运行验证，权限不能改重绑控制表。Kafka 原记录与结束位点 2 / 2 / 30 一致。
- 隔离验收由模拟批准方签发。生产接入依赖可信批准发行方、完整旧写者与控制面持续隔离、Main 封存和 C02、Kafka/Dapr 直接角色及生产目标凭据。CRM 任务分支已清理，DEV-2 已释放。

主线同步 CRM 已采用证据。可信实际部署控制面、正式拓扑和全部写者仍按 PGT-017 继续核对。正式 PostgreSQL **26 PASS / 1 NOT_RUN**，Actions 与生产部署为零。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)
