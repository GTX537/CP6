# W564-01 · Stock 普通数据库写入与同一授权事务

2026-11-14 UTC。Main 基线 `c272150887cd00e9a6e1578fb8d876c7e1f7b2a4`，受测源码 `ee9378d4c5385c4fe843f5401b7e60e636230c61`。使用隔离 PostgreSQL 18.6、模拟两租户、普通库存写登录与真实 Main Kestrel。 [Issue #1053](https://github.com/GTX537/CP6/issues/1053)

现有普通库存角色可写 `T_Stock`、`T_StockTransaction`、`T_WmsSequence` 与 `movement_sequences`。新增 Core 前向迁移，在这些写语句前确认原认证根行版本由本次事务取得，并读取已绑定租户的启用状态。仅有原库存栅栏执行权限的指定普通登录走此检查。触发器读取根行，获取顺序继续由现有 `lock_graph` 保证，数据库应用角色和现有 RLS 不新增授权。普通登录无法直接修改授权根。 [前向迁移](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261114030500_StockOrdinaryWriteAuthorityV1.cs) · [系统列与事务标识](https://www.postgresql.org/docs/18/ddl-system-columns.html) · [PostgreSQL 事务标识](https://www.postgresql.org/docs/18/functions-info.html)

- 原产品的普通登录更新在未持根时完成，基线用例失败。修复后指定普通登录的四个库存及编号写入均按权限拒绝。持根后仍拒绝已停用租户。恢复启用后既有 Stock Owner 语义保留，事务回滚无库存、编号、审计、原件或 Outbox 新增，合法提交保留原精度及完整写集。其他租户保持原值。
- 分别以独占实例实测五项通过，零失败零跳过。包括普通写集、普通根权限、两种实际暂停与库存顺序、V1 调整/移库/原件及 V2 创建。中间一次组合运行因两个集合试图复用同一隔离夹具目录被保护拒绝，后改每次独占目录和端口。另一次扩展负测的夹具 SQL 语法失败修正后通过，原始失败留存。 [普通写集回归](../../../../CP6.Tests/Persistence/Tenancy/StockTenantMovementPostgreSqlTests.cs) · [暂停顺序回归](../../../../CP6.Tests/Erp/StockTenantPausePostgreSqlTests.cs)
- 编译零错误，既有告警保留。本地发布 310 文件，WebApi SHA-256 `38206c589e91cf38d1529851ad8b97a117932b5c984511cac5611a0e79ffc117`，PostgreSQL 迁移程序集 `57707772c3e69a7adfa6f170549f5dbdbb44c5efa05221b14d0a1aa7869e8258`。隔离发布建库退出 0、存活 200、匿名管理及询价 401。原始 TRX、发布摘要和停止记录在 `D:/CP6-test-artifacts/W564-01`。Actions 启动、重跑和取消为零。

数据库门覆盖指定库存角色的上述四张表和租户启停。当前人类动作与到期由既有应用 Owner 核对，正式直接数据库身份的全部业务权限、独立采购收货、旧 OA、财务、Space Design 及跨库恢复待验。正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。分支、工作区及席位处置见 PR 与 `main-finish.json`。 [矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)
