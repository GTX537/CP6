# W607-01 CRM 恢复目标的身份写者

2026-11-15 UTC。CRM 受测源 `5d7e9b9a9222c632824cf5001dbd71a73fb016d4`，产品已合入 `9c28a41c2cc402154a648933fcb5be6c32a3de4e`。Main 本提交登记产品结果，实际演练复用 W601 的隔离双租户 PostgreSQL 18.6、已验证 Main `be633749701eade262129f80f2dc70f8f5b7108d` 宿主及 Kafka/Dapr，目录数据通过模拟 HTTP 按原合同检验。 [CRM PR #235](https://github.com/GTX537/CP6.CRM/pull/235) · [CRM 证据](https://github.com/GTX537/CP6.CRM/blob/9c28a41c2cc402154a648933fcb5be6c32a3de4e/docs/development/evidence/W607-01/DELIVERY.md)

- 原版对两个租户的新投递与重投返回 200，基线任务读取目录并写入快照，当时恢复目标仍未重绑。候选在写事务内核对可信租户配置、Store 和物理时间线。C02 Inbox、IdentityBaseline 登记和租约及最终发布分别采用原写连接。原操作可依既有合同只读恢复。
- 最终 **8/8 物理恢复及 35/35 独立身份 PostgreSQL 回归**，零跳过。两个租户在重绑前投递和重投均 503，基线目录读取为零，写集和原回执一致。重绑后均成功接收事件并发布快照。另实际见证已采集的结果在后继时间线被拒绝、在已绑定的前一库获准提交。
- 实测七份源登录排空、原库重启与目标密码轮换。原库及目标拒绝旧凭据，新的 Main 封存角色在重绑后分别只可对本租户执行原检查。普通身份角色无控制表或全局监控权限。现有环境采用须按 CRM 报告配置已登记 Store 及所需布尔函数的最小权限。
- 测试绑定的源码、程序集哈希、活动时间线、实际写集与对账保存在 `D:/CP6-test-artifacts/W607-01`。相关进程均停止。Actions 与生产部署均为零。

本轮未扩大到实际环境的批准、Kafka/Dapr 认证凭据轮换或生产切换。正式 PostgreSQL **26 PASS / 1 NOT_RUN**。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646)
