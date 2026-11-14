# W559-01 · 采购询价五个现行写入口

2026-11-14 UTC。Main 基线 `a37dbab4ed674815634c798e214b90ee605a00f6`，受测源码 `6a79570084a31f78fe220e064c90c37ac3260d7a`。隔离 PostgreSQL 18.6、Main Kestrel、模拟采购申请、询价、供应商及 IAM 用户。 [Issue #1044](https://github.com/GTX537/CP6/issues/1044)

从 PR 发起询价、邀请供应商、录入报价、排名和选定采用既有 Main 共同授权事务。当前动作和会话在该事务核对。发起询价的采番参与原业务提交，报价与原邀请状态同事务。 [HTTP 入口](../../../../CP6.WebApi/Controllers/Pur/RfqController.cs)

- 基线 6 项失败。撤权持有共同根时五个请求能提前返回成功。报价写入等待业务表时撤权也能先提交。
- 修复后，撤权先提交的五个请求均返回 403，头、行、邀请、报价和编号与事前一致。各动作重新授予后原请求成功。报价先持根时通过 `pg_blocking_pids` 验证真实撤权等待，报价 `12.1234` 与邀请状态一起提交保留，旧授权再次写入被拒绝。核对角色版本、身份代次、快照、优先 Outbox、审计和原回执。采购申请转入询价的数量 `3.12345678` 原值保持。 [PostgreSQL 回归](../../../../CP6.Tests/Erp/Pgt017RfqMutationsPostgreSqlTests.cs)
- 共 10 项 PostgreSQL 与 40 项 RFQ 和价表组件通过，零失败、零跳过。10 项包含本轮六个并发及原逐笔回写的四项。构建零错误，现有告警保留。
- 本地发布 310 文件，WebApi SHA-256 `46b11ab755a110082f658e8f69ec6f767312d75f876526c15b5cd7a4b33e81d4` 与受测程序集一致。隔离发布建库退出 0、存活 200、匿名租户管理及询价 401。原始 TRX、日志与文件摘要在 `D:/CP6-test-artifacts/W559-01`，测试实例已停止。Actions 启动、重跑及取消为零。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。现行 RFQ 转 PO 按供应商分组提交、采购其他入口、独立 Space Design、旧 OA、财务、Stock 普通数据库角色及两库恢复重新开放仍待验。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [跨库与剩余写者 #1012](https://github.com/GTX537/CP6/issues/1012)

Main 分支、工作区及席位处置见远端 PR 和 `main-finish.json`。
