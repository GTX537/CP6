# W574-01 · 未取得完整 WAL 时拒绝重新开放

2026-11-14。Main 基线 `4799bc6ba8c574b34afc05b1702236e9ce18e9c5`。CRM 受测 `4963af73b084f2849fa5c6456711594c39a9945a`，已入远端 `103a44260e8a39dba0a1aa36a91988bcc4150c3a`。使用 PostgreSQL 18.6 和 W571 的模拟询盘、原物理恢复及独立 broker 水位。 [CRM #225](https://github.com/GTX537/CP6.CRM/pull/225) · [CRM 联验证据](https://github.com/GTX537/CP6.CRM/blob/103a44260e8a39dba0a1aa36a91988bcc4150c3a/docs/development/evidence/W574-01/DELIVERY.md)

- 确认 CRM 原库已停且原端口空闲。把已提升的早期恢复点复制到新的隔离目录，比对相同实例标识、数据库 OID 和早期时间线，并验证目标缺少原库最终 WAL 段。
- 在目标库读回身份投影版本 7、原导出回执及两条消息的原 payload 摘要。对照 W571 的已提交写集，确认缺少角色封存、1 份原导出制品和发送记录。两条原已发送消息在目标仍为待发。
- CRM 原有业务权限门实测返回 `503 AUTHORITY_FENCE_UNAVAILABLE`。以实际原受限发布者连接检查，存储绑定为 false。目标完整写集摘要在核对前后相同。最终 PostgreSQL 1 项通过、零跳过。复用 W571 的独立 broker 水位及发布 worker 验收，未在本次重复启动 broker 或执行发布 worker。

首次启动等待受 Windows 管道继承影响，改用独立启动。另一次数据库按原合同拒绝替换发布者的不可变传输登记，最终保持原登记并实测绑定函数。失败及通过 TRX、最终读回、摘要及已停止记录均位于 `D:/CP6-test-artifacts/W574-01`。范围为测试夹具与证据，生产代码未修改。 [完整 WAL 的计划恢复](../W571-01/DELIVERY.md) · [真实 C02 投递](../W573-01/DELIVERY.md)

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。缺失写集取得权威证据后的差量对账和安全重开、正式恢复切换编排、旧 OA 其他入口、财务、独立 Space Design 和其他普通数据库写者待验。Actions 启动、重跑及取消为零。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)
