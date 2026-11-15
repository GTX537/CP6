# W614-01 CRM 恢复目标的 Outbox 传输采用

2026-11-15 UTC。CRM 受测源码 `1d73c3135544e6c9a9d901a67f9acc14216a4275`，产品远端主线 `cebdcc838563ccb824fd25252a4fc4c80d8dc463`。Main 登记产品结果和所复用验收。本地使用两个已登记模拟租户、模拟审批方以及实际 PostgreSQL 18.6、Dapr 1.18.2 和经过认证的 Kafka 4.3.1。 [CRM PR #236](https://github.com/GTX537/CP6.CRM/pull/236) · [CRM 交付](https://github.com/GTX537/CP6.CRM/blob/cebdcc838563ccb824fd25252a4fc4c80d8dc463/docs/development/evidence/W614-01/DELIVERY.md)

- 目标配置绑定真实数据库、当前 WAL 时间线、原重绑回执、已登记 Root、每租户实际 PostgreSQL 登录与 OID、Dapr 端点和凭据摘要及独立签署的 broker 证据。原有物理库使用原发布者，真独立库保留原合同。物理副本从登记前开始失败关闭，省略 StoreId 也不能越过门禁。独立维护入口核对签名后写持久登记，启动和 HTTP 仍用现有产品入口。
- **17 项定向验证**通过，零跳过。包括 1 项物理门禁、15 项 Outbox 和隐私导出相关 PostgreSQL 回归，以及 1 项实际恢复联验。全部 12 个 Root 核对登记权限，实际消息发送覆盖配置和提交。旧 PostgreSQL 登录在原和目标拒绝，在线旧 Dapr 链路被 Kafka 阻断，旧 token 在目标 Dapr 返回 401。真实 COMMIT 应答丢失后读回同一登记，签名冲突、角色交叉及实际后继时间线均拒绝旧激活。
- 实际配置主题原四条记录、键、内容摘要和结束位点保持。原未知发送保持原 RequestId 和票据，原已完成发送按原回执关闭。目标只发布一条原未发送消息，独立 Kafka 见证人核对原字节与结束位点。复用 W607 的实际 Main 权威交互、旧库重启及身份写者证据，和 W611 的持有连接被撤权及重启拒绝证据。 [W607](../W607-01/DELIVERY.md)
- 源码、程序集、物理时间线、broker 读回和 23 个关联进程停机记录保存在 `D:/CP6-test-artifacts/W614-01`。初轮失败的夹具配置、安装顺序、连接读取及继承恢复点已更正，原日志保留。已保留需原时点工件的独立保留期测试的初次过滤失败。本轮实际目标通过，第二次传输激活与受保护环境采用仍需各自证据。Actions 与部署均为零。

Main 的刊发后台、文件后续写者和财务跨域后台待完成。正式 PostgreSQL **26 PASS / 1 NOT_RUN**。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646)
