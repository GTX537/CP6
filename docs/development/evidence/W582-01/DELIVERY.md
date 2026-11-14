# W582-01 真实 Main 目录、C02 Kafka 和 CRM 导出竞争

Main 基线 `7018cc6306f6852d5d0ae1033507a73c4e086af3`，受测 `fe781f325698d98d93ca310d9d93f97b65c582f3`。CRM 受测 `66fa33a30b8f5ac4553ef38196d2a24090245542`，集成到 `256d6ddff81aed86c06620d85f9f8ca95e2f7f3a`。使用隔离 PostgreSQL 18.6 两库、Dapr 1.18.2、Kafka 4.3.1 和模拟询盘。 [CRM #227](https://github.com/GTX537/CP6.CRM/pull/227) · [CRM 验证](https://github.com/GTX537/CP6.CRM/blob/256d6ddff81aed86c06620d85f9f8ca95e2f7f3a/docs/development/evidence/W582-01/DELIVERY.md)

| 强制顺序 | 业务结果与权威结果 |
| --- | --- |
| CRM 先提交原导出 | Main 撤权等待 CRM 持有的事务门。封存、撤权目录发布、重授目录发布后，原导出制品、回执、审计和效果整体 SHA-256 相同。重复业务轮询为空。 |
| Main 先提交撤权 | CRM 旧的已获得权限在持久封存后被拒，且撤权目录发布后仍拒绝。原业务与 Outbox 写集保持。重授目录发布后，原待执行导出完成一次，随后轮询为空。 |

发现时序通过实际 Main 执行、CRM Owner 与数据库锁读取确认。两种顺序都分别在 Kafka 分区 0 的偏移 14 和 15 读回撤权版本 6、重授版本 7。Main Outbox、broker 和 CRM 原生 Inbox 的原始 envelope 按字节核对。一套新迁移 Main 为普通目录登录安装目录。CRM 通过真实 `/connect/token` 后核对版本目录，并在每次确认原 Inbox 已提交后执行原生发布循环。本地三次发布各含 8 个源聚合，CRM 已发布项目的存储原值逐一匹配完整目录 SHA-256 与 Main HTTP 目录，变动角色原值又与 Kafka payload 相等。

本地 **17/17** 通过，零跳过。两种顺序各有 Main 和 CRM 一个通过结果，另有 Main A/B 目录、CRM 人员上下文和隐私导出相关 13 项。完整 TRX、两次真实 Kafka 读回、目录摘要、受测程序集 SHA-256 与停机记录位于 `D:/CP6-test-artifacts/W582-01`。初次编译时修正两个测试类型引用，未改产品代码。会话及配置签发保留模拟；Main 业务服务上下文路由使用实际 TLS，C2 目录为隔离回环 HTTP 的 TLS 终止夹具。实际发布循环由测试在 Inbox 确认后运行，后台调度证据复用 [W579-01](../W579-01/DELIVERY.md)。本次宿主实际启动与 HTTP 验证保留，正式制品和部署沿原流程。Actions 启动、重跑及取消为零。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。差量恢复与安全重开、正式切换编排、旧 OA 触发器、财务、独立 Space Design 和其余 writer 清单待验证。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)
