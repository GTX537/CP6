# W551-01 · CRM 封存后的 Main 角色撤权恢复

2026-11-13 UTC。Main 基线 `f252d248866efe706eb3e19292ebe70058a144fe`，实测夹具 `f877edfa61d1b4da9ecb3acc5f25a15cc2bda048`。CRM 复用已采用的 `724570d3d7fe8e3c26615d3097c73546c5eacd63`。两套隔离 PostgreSQL 18.6，模拟租户及询盘，使用原版本化 IAM HTTP、原 Main TLS 服务签发与 CRM 导出。 [W550](../W550-01/DELIVERY.md) · [PGT-017](../../acceptance/postgresql-tenancy.json#L646)

- CRM 持停用前已签发的服务授权。Main 进入 COMMIT 并由临时延迟约束阻塞，`pg_blocking_pids` 证实实际等待。独立连接读回 CRM 角色门槛已持久化，Main 角色、快照、Outbox、审计和原回执尚未变化。
- 释放迟到授权后，CRM 拒绝导出，任务、制品、审计和 Outbox 无新增。随后取消实际 Main COMMIT，接口返回 503 `IAM_OPERATION_UNKNOWN`，两库独立读回确认 Main 未提交、CRM 仍封存。
- 用原操作 ID 查询返回 UNKNOWN。重试同一命令，Main 提交一份角色版本、快照、Outbox、审计与原回执。重复相同命令返回同一回执。角色撤权后实际 TLS 签发返回 403。

实际取消和原命令恢复共 2 项通过，普通撤权先提交兼容共 2 项通过。首轮夹具标记内容与既有 CRM 夹具约定不符，修正后实测通过，原始失败保留。只改测试与记录，复用 W550 两仓发布哈希和 HTTP 证据。所有原件和停机记录在 `D:/CP6-test-artifacts/W551-01`。Actions 启动、重跑和取消为零。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。仍需验证 CRM 提交确认丢失、恢复实例和两库水位，核对直接 SQL 和 Main 其余独立写者。 [跨库余项 #1012](https://github.com/GTX537/CP6/issues/1012)

Main 分支和席位处置以远端 PR 及本地 `main-finish.json` 为准。CRM 复用已合入制品及已释放的固定席位。
