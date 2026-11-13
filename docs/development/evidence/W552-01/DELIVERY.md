# W552-01 · CRM 封存提交回执丢失与 Main 恢复

2026-11-13 UTC。Main 基线 `2a2697ebe199e971d71470de1f826d1c0f39f87a`，实测夹具 `048666daddc08fc5c503d990b50a8135de48eb09`。CRM 采用 `724570d3d7fe8e3c26615d3097c73546c5eacd63`。两套隔离 PostgreSQL 18.6，模拟租户、角色及询盘。使用实际 Main IAM、原 TLS 签发、CRM 隐私导出和独立数据库观察连接。 [W551](../W551-01/DELIVERY.md) · [正式 PGT-017](../../acceptance/postgresql-tenancy.json#L646)

只在校验过实例身份的 CRM 测试集群临时配置缺席同步备用库。观察到专用封存连接在 COMMIT 的 `SyncRep` 等待后终止该连接。CRM 服务器日志记录等待被取消且本地事务已提交，独立读回确认角色封存门槛已推进。随后复原测试集群参数。 [PostgreSQL 同步提交](https://www.postgresql.org/docs/18/runtime-config-wal.html#GUC-SYNCHRONOUS-COMMIT)

Main 返回 503 `AUTHORITY_FENCE_UNAVAILABLE`，原角色、C02 版本、代次、Outbox、审计和回执尚未提交。CRM 在未收到新 C02 事件时继续拒绝旧授权，导出任务、制品、审计和 Outbox 无新增。原 IAM 操作查得 UNKNOWN。用原 ID 及输入重试，封存确认和 Main 提交成功，独立读回确认同版本 Outbox、审计和回执各一份，重复命令返回原回执。

Main 和 CRM 各 1 项通过。本任务只改 Main 夹具与交付记录，复用 W550 两仓本地产品发布哈希及冒烟。原始 TRX、配置复原和停止记录在 `D:/CP6-test-artifacts/W552-01`。Actions 启动、重跑及取消为零。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。两库历史水位恢复、原生数据库写入以及 Main 其他独立写入口仍待核对。 [跨库余项 #1012](https://github.com/GTX537/CP6/issues/1012)

Main 分支和席位处置以远端 PR 与本地 `main-finish.json` 为准。CRM 复用原制品与已释放席位。
