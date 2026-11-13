# W535-01 · 订单独立取号与租户暂停

2026-11-13 UTC，Main 基线 `c7f6ea0c5cdd25d7354b9bb69d35a25a55d45389`，实测源码 `4f02cd347c02195f09446ba257eb7558711d76f2`。正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。 [原验收](../../acceptance/postgresql-tenancy.json#L646) · [Issue #1008](https://github.com/GTX537/CP6/issues/1008)

订单取号 GET 对全局 ORD 计数执行独立提交。修复后，普通取号在原订单 Owner 的 Read Committed 事务先获取共同根、重验签名会话，再分配并持锁至提交。已启用会话、HTTP 路由和编号格式沿用原合同。普通订单、可信集成和其他内部调用仍在各自已有的写入事务分配编号。产品、版型的同名 GET 只读取或暂存候选序号，没有 SaveChanges。实际计数更新发生于对应的创建流程。MES 工单取号已有自身 Operations 事务。 [订单入口](../../../../CP6.WebApi/Controllers/Erp/OrderController.cs#L33) · [同根取号](../../../../CP6.Core/Services/Erp/OrderWriteOwnerV1.cs) · [实际 UPSERT](../../../../CP6.Core/Services/Common/DocNumber.cs) · [MES Owner](../../../../CP6.Core/Services/Mes/WorkOrderService.Owner.cs)

## 实际验证

隔离 PostgreSQL 18.6、Main Kestrel，模拟租户、真实登录会话及版本化平台暂停。独立数据库连接分别持业务行或租户行，再观察真实 `pg_blocking_pids` 链路。基线两例均失败。取号先等计数时暂停可先返回 200；暂停先持共同根时原取号返回 200。最终结果如下。 [两种顺序](../../../../CP6.Tests/Erp/Pgt017OrderNumberPostgreSqlTests.cs)

| 提交顺序 | 根上等待的请求 | 结果 |
| --- | --- | --- |
| 取号先提交 | 版本化租户暂停 | 取号 200，计数 3 → 4。暂停后此前合法编号保留，旧会话再次取号 401。 |
| 暂停先提交 | 已认证的取号请求 | 等待后 401，计数 4 → 4，旧会话再次请求仍 401。 |

两个顺序均验证租户状态、版本及认证政策变化，身份代次加 1，身份快照、优先 Outbox、审计和暂停回执一致；根 revision 仅作为事务锁序证据。最终 `final.trx` **37 通过、0 失败、0 跳过**，包含新取号两种顺序、旧普通库存与暂停两种顺序及订单相关回归。构建零错误，保留原警告。证据目录 `D:/CP6-test-artifacts/W535-01`。310 个发布文件 SHA-256 已保存，WebApi `623464710227878f0948c9d4a78350e3921d97bb0e39c962ee88ef55f8d8ae8e`、Core `73752635d9bb99914c25d75093ad31d40f65717b009cac8c1e18321835db4957` 均与实际测试程序集匹配。隔离发布存活 200，匿名取号 401。宿主及 PostgreSQL 已停止。

集中审查覆盖当前会话、事务锁序、取消、提交未知、原编号格式、普通创建和集成调用的边界。剩余写者包括旧流程批办、财务过账、采购收货和询价回写、MRP、Space 多 Context、会话撤权及普通数据库角色。Main 事务和 CRM 的本地 C02 消费使用不同数据库；CRM 已通过的本地撤权竞争不能替代 Main 暂停与 CRM 新效果之间的跨库证明。PGT-017 保持 NOT_RUN。Actions 启动和取消为零。采用和席位处置见 PR 与 `D:/CP6-test-artifacts/W535-01/main-finish.json`。 [前置清单](../W531-01/DELIVERY.md#仍待核销的正式前置) · [跨库提交要求](../../../design-handbook/architecture/02-postgresql-tenancy.md#7-owner消息和撤权的共同提交边界)
