# W515-01 · Main 启用 RMA 租户 schema 时保留原撤销

当前 Main 基线 `2d4c23d36c117a71e001eb5ed0da5726ca7ea225`，实际测试源 `5c4c58f53ec8973508fd60e8cd355749241cd116`。环境为本地 .NET 8、实际 Kestrel 和 PostgreSQL 18.6，使用合成租户、业务与登录资料。复用同源 W508 产品 DLL 和已有 DatabaseInit、RMA CLI 的真实安装与 commissioning，只编译受影响的测试项目。 [实际用例](../../../../CP6.Tests/Persistence/Tenancy/RmaTenantReadHttpPostgresTests.cs#L121) · [真实安装与查询基础](../W356-01/DELIVERY.md)

- 同一个 `w356_live` 数据库 OID 16386、同一个 A 租户和相同 JWT issuer、签名键。旧宿主在正式 commissioning 要求的共享布局为空时，真实列表返回 200 和空数据，原生 SQL 读取 `cp6_rma_application_v1.heads`（OID 33015）。
- 使用真实 access、refresh 和 CSRF Cookie 调用原登出，核对 PostgreSQL 原 access session 被撤销，第二份当前登录仍有效。重启同一库的宿主并启用绑定 revision `rma-schema-activation-v2`，独立复核同一原令牌的签名和有效期，真实 HTTP 按原撤销状态返回 401。其 SQL 窗口只有授权源的会话读取，普通租户连接和 RMA SQL 均为零，业务表写集一致。
- 切换前签发的另一份当前令牌在新宿主返回 200。实际普通登录 `w350_a`、后端 29924 启用只读事务，查询当前 `t_<A>` 的 `heads`（OID 43512）及 `versions` 并得到本租户原业务。

`rma-schema-activation.trx` 实际 1 通过、0 跳过。原生 SQL、构建来源、测试结果和所有自有进程停机证据位于 `D:/CP6-test-artifacts/W515-01/rma-http-20261112T144127-6ab42497/fixture`。PG 端口 55591 与两本地宿主均已停止。一次集中审查核对同库、旧令牌撤销、真实存储选择和普通角色。

本窗覆盖当前正式 commissioning 的共享空布局与已启用租户 schema 之间的启动切换。PGT-026 另组合 Main 同名目录、普通角色和 CRM 共享公共路由证据，真实已有数据迁移按独立任务。正式矩阵仍 19 PASS / 8 NOT_RUN，原询盘仍 60/60。Actions 触发和取消为零，部署为零。 [Main 目录](../W511-01/DELIVERY.md) · [普通角色](../W427-01/DELIVERY.md)
