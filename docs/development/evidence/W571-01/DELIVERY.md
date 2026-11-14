# W571-01 · 排空后恢复 CRM 与真实 Kafka 水位

2026-11-14。Main 基线 `aa5d8ca7a166013a7e00c015be3c3bb278aed88a`。CRM 受测提交 `19d82d5aaa5fedde84aff33ece57354efb7ec55d` 已合入远端 `b0a4181076a4c7cf77b521ba0ca1932b4ca38d6f`。采用隔离 PostgreSQL 18.6、Dapr 1.18.2、Kafka 4.3.1 和模拟业务数据。真实 Main IAM 撤销及重授，C02 事件经现有联验桥进入 CRM 原 Inbox。 [CRM #223](https://github.com/GTX537/CP6.CRM/pull/223) · [夹具与证据](https://github.com/GTX537/CP6.CRM/blob/b0a4181076a4c7cf77b521ba0ca1932b4ca38d6f/docs/development/evidence/W571-01/DELIVERY.md)

- 原 CRM 发布器通过真实 Dapr 写入 Kafka。两条提交消息在各自主题从分区 0、offset 0 读回，原分区键与 payload 字节摘要相符。
- 两仓旧联验宿主退出，原 CRM 连接归零后写入最终恢复点，再停止原库。四节连续 WAL 补齐了此前旧恢复点缺少的封存和已提交效果。最终恢复库读回封存与 C02 版本 7、原导出回执、已生成制品、审计及两条消息的原派发和发送尝试记录。
- 目标提升后仍关闭。核对原库写集与实际 broker 水位后，由本地夹具登记目标时间线。数据库授权门拒绝旧角色版本并接纳当前版本，发布器运行一次返回 IDLE，两个分区终点仍为 1，原写集不变。最终 Main、CRM 跨库和独立恢复阶段共 3 项通过，零跳过。首次最终恢复发现旧 WAL 会在停库时被回收，改为在停库前保留连续段后复测。原件在 `D:/CP6-test-artifacts/W571-01`。 [CRM 实测条目](https://github.com/GTX537/CP6.CRM/blob/b0a4181076a4c7cf77b521ba0ca1932b4ca38d6f/docs/development/evidence/W571-01/DELIVERY.md)

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。此次验证为可取得源库尾段 WAL 的计划切换。仍需验收源库不可达或 WAL 缺失时的差量恢复、生产切换编排、真正的 Main C02 broker 投递、Main 旧 OA、财务、独立 Space Design 及其余普通写者。Actions 启动、重跑和取消为零，未部署。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)
