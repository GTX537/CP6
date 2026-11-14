# W573-01 · Main C02 撤权与重授的真实 broker 投递

2026-11-14。Main 基线 `45ce8fd5f07ca62717751a7d853ca3dfc06b1ff1`，受测 `42528e5438607c1ec9304d1bea0ed81be3803406`。CRM 受测 `47fa9176e14a6092d64c49ec3260ceb6f4966f94`，远端采用 `ec7158d3fbb547f60bc2bcd0b747fb254e54417e`。隔离 PostgreSQL 18.6、Dapr 1.18.2 和 Kafka 4.3.1，使用模拟租户、询盘和隐私导出。 [CRM #224](https://github.com/GTX537/CP6.CRM/pull/224) · [CRM 联验证据](https://github.com/GTX537/CP6.CRM/blob/ec7158d3fbb547f60bc2bcd0b747fb254e54417e/docs/development/evidence/W573-01/DELIVERY.md)

- Main 实际 IAM 撤销和重授站点/表单访问范围，并回读原回执、身份快照、Outbox、审计和权限代次。撤权先提交时，CRM 已取得旧服务证明的导出在封存下被拒，原状态和消息写集保留。
- Main 普通 Outbox dispatcher 和既有 C02 发布器通过 Dapr 送进 Kafka。实际侧车订阅调用 CRM 原生 C02 HTTP 路由。两条事件在原生 Inbox 提交后读回为原 Main payload 字节。Kafka 原分区键与原字节匹配，版本 6 和 7 分别位于分区 0 offsets 13、14，终点 15。文件只提供对比原件及阶段同步。
- 撤权事件进入 Inbox 并以本地夹具发布投影后，旧导出仍被拒。重授经相同真实传输路径送达并以现有测试基线重建投影后，原导出完成一次，随后的轮询无新效果。Main 和 CRM 各 1 项通过，零跳过。

保留的前两次运行分别暴露本机 Kafka 消费组默认索引写满磁盘，以及事件提前到达触发 CRM 投影失效，打乱了封存后、投递前的独立观测。最终缩小隔离 broker 索引和内部 offsets 分区数，通过夹具明确等待已验证的封存拒写，再开启 broker 投递。产品代码无修改，复用 W572 和 W569 的产品冒烟，不重新构建发布件。原始执行与停止日志、测试程序集哈希、两条 broker 读回及脚本见 `D:/CP6-test-artifacts/W573-01`。所有本轮运行进程均已退出。 [Main 测试入口](../../../../CP6.Tests/Platform/Pgt017CrmExportScopeRevocationPostgreSqlTests.cs) · [本地 Kafka 配置参考](https://kafka.apache.org/43/configuration/broker-configs/)

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。此轮验证 Main C02 真实传输和 Inbox 落库，采用测试基线完成投影重建。已知合法业务先提交的跨库顺序沿用 [W555 原证据](../W555-01/DELIVERY.md)。生产切换编排、源库不可达或尾段 WAL 缺失时的恢复、Main 旧 OA 其余入口、财务、独立 Space Design 和其他普通写者继续验收。Actions 启动、重跑及取消为零。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)
