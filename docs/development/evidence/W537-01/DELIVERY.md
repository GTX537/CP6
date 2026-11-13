# W537-01 · Main OIDC 会话族撤销

2026-11-13 UTC。Main 基线 `a0582b0d7e230ea1282c56f531b21cd75d2b69bf`，实测源码 `8c6086976116b432423d207a64818802b5230e1f`。正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。 [原验收](../../acceptance/postgresql-tenancy.json#L646) · [Issue #1013](https://github.com/GTX537/CP6/issues/1013)

Main OIDC 会话族撤销在原 Serializable 事务先获取共同认证根，再锁原浏览器会话族、撤销刷新记录和对应 CRM grant，事务内保存身份快照和优先 Outbox。刷新与撤销均按根、会话族、刷新行取锁。原 OIDC 验证、会话族边界、可重试事务处理、注销继续凭证沿用现有合同。 [OIDC 会话存储](../../../../CP6.WebApi/Services/CrmOidcGrantStore.cs) · [刷新事务](../../../../CP6.Core/Services/Sys/RefreshTokenService.PostgreSql.cs)

## 实际验证

隔离 PostgreSQL 18.6、Main Kestrel 和真实 OIDC 授权码交换。模拟租户有两个同账号会话族。独立连接锁住订单计数行或原浏览器会话族行，以 `pg_blocking_pids` 观察两条实际等待链。初次基线用例的 OIDC state 长度不满足原合同，调整模拟输入后重跑。产品旧基线两例失败。撤销等待会话族行时，业务可先取号；业务已持根时，旧撤销没有先等在共同根。 [两种实际顺序](../../../../CP6.Tests/Erp/Pgt017OidcFamilyPostgreSqlTests.cs)

| 先提交 | 后续行为 |
| --- | --- |
| 业务 | 订单取号 200，计数 0 → 1。撤销等待期间原会话族行仍可单独获取。 |
| OIDC 会话族撤销 | 等待中的旧会话取号返回 401，计数 0 → 0。 |

两例均核对原会话族、刷新行和 CRM grant 已撤销，同版本身份快照与优先 Outbox 各一条，重复注销无新增撤销事实，另一浏览器会话仍有效。旧会话后续新写返回 401。最终 **38 通过、0 失败、0 跳过**，含 9 项实际 PostgreSQL 及 29 项 OIDC 组件回归。构建零错误，原警告保留。实际 WebApi 与本地发布 SHA-256 均为 `0ac29a49d9f3185a5e6be06b8e1e5e89b3865f64a6bfa9a9e2118120af399ab8`。隔离发布存活 200、匿名取号 401。数据库、宿主已停止。原始 TRX、源码及发布清单在 `D:/CP6-test-artifacts/W537-01`。

Main 的 OIDC 撤销与 CRM 应用其身份消息各有本地提交。跨库复查确认，当前两仓没有共享事务或已采用的等价协议。后续需在 Main 同根冻结旧准入、CRM 原业务事务内封存旧代次并持久确认，再完成 Main 有效撤权，或落地满足同等提交语义的方案。保持 CRM 已提交原结果的读取、可信事实接收及技术重投。跨库协议及其他写者仍待核销，PGT-017 保持 NOT_RUN。Actions 启动与取消为零。最终采用、分支及席位见 PR 和 `D:/CP6-test-artifacts/W537-01/main-finish.json`。 [跨库缺口 #1012](https://github.com/GTX537/CP6/issues/1012) · [原跨库要求](../../../design-handbook/architecture/02-postgresql-tenancy.md#7-owner消息和撤权的共同提交边界)
