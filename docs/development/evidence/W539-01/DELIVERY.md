# W539-01 · WMS 油墨提交权限

2026-11-13 UTC，Main 基线 `a0582b0d7e230ea1282c56f531b21cd75d2b69bf`，实测源码 `61b7ec49b80e49a12e64c0fcaa0de82f2b925d51`。正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。 [验收矩阵](../../acceptance/postgresql-tenancy.json#L646) · [Issue #1017](https://github.com/GTX537/CP6/issues/1017)

创建、开封、混合、配色共四个现行 HTTP 写入口沿用 `wms-ink` 动作键。请求的 Core Context、油墨服务和采番器是同一作用域。先获取 IAM 的 `cp6_authentication_v1.acquire()`，在该事务中读取当前会话和权限，持锁至业务提交。两次保存共同提交，数据库拒绝子批次时，父批次扣减与采番一起回滚。 [HTTP 入口](../../../../CP6.WebApi/Controllers/Wms/InkController.cs) · [油墨服务](../../../../CP6.Core/Services/Wms/InkService.cs) · [采番器](../../../../CP6.Core/Services/Wms/WmsSequenceService.cs)

## 实际验证

隔离 PostgreSQL 18.6、Main Kestrel 和模拟租户、批次、客户、IAM 用户。使用真实登录及 IAM 动作撤销，在独立观察连接通过 `pg_blocking_pids` 记录业务表等待和共同根上的等待。撤权等待时四个新写均等共同根并在提交后返回 403，批次、配色和采番写集相等。混合先取得根时撤权等混合，混合返回 200，父批各扣减精确 8 位小数，子批数量 3.35802467、父批关联、较早到期日与采番同时保留。旧会话后续新写 403，已有写集一致。另在重新授予权限后，以 PostgreSQL 现有颜色字段约束验证失败回滚。两种顺序中 IAM 动作版本推进、租户身份代次和快照各加 1，同版本 Outbox、审计及回执各一条。 [并发回归](../../../../CP6.Tests/Erp/Pgt017InkPostgreSqlTests.cs)

`baseline.trx` 5 项均在修复前复现缺少等待。首轮构建修正测试的 C# 元组推断后通过。`final.trx` 为 29 通过、1 失败、0 跳过，失败的检查把普通入口的本地化 403 误当成版本化动作错误码。仅纠正测试断言，`confirmation.trx` 对该用例复测通过。产品程序集一致，合计 **27 项 PostgreSQL、3 项组件通过**。其中包括已有 IAM 撤权和库位隔离行为。构建零错误，保留既有警告。

实际测试宿主与本地发布 WebApi 的 SHA-256 均为 `306e1eed2251fa6b70fd352d2d2ab22b1b9d58fbf5aec555bd99bafb306be5e5`。310 个发布文件摘要、TRX 和本地启动证据在 `D:/CP6-test-artifacts/W539-01`。冒烟初次跳过建库导致本地化查表失败，使用发布制品的 DatabaseInit 在隔离实例完成初始化后，通过存活 200、无 CSRF 的混合请求 403、匿名查询 401。测试及发布宿主和 PostgreSQL 均已停止。Actions 启动和取消为零。

正式 PGT-017 仍需核销其他 Main 业务写者、普通数据库角色及 [Main/CRM 跨库提交](https://github.com/GTX537/CP6/issues/1012)。[OIDC #1014](https://github.com/GTX537/CP6/pull/1014) 与 [财务 #1016](https://github.com/GTX537/CP6/pull/1016) 已通过原验收并进入本次集成基线。本窗只覆盖四个油墨 HTTP 入口。分支和席位见本任务 PR 与 `main-finish.json`。

本次集成基线 `4004fe606cae47c2bbdb0ded3882b63d9e0e52f9`。原任务产品与测试文件均逐文件核对，复用前述验证。新的集成冒烟结果随 PR 记录。
