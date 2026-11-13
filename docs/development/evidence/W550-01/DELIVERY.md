# W550-01 · Main 部门停用与 CRM 导出

2026-11-13 UTC。Main 基线 `4174e3b3998bbaa2f824a01ed38157d2b7150834`，受测源码 `b0860945581e0d2cfadad14a914eb016fb967f0e`。CRM 已采用 `724570d3d7fe8e3c26615d3097c73546c5eacd63`，与受测源码内容一致。两套隔离 PostgreSQL 18.6 使用模拟租户、部门和询盘。 [CRM 交付](https://github.com/GTX537/CP6.CRM/blob/724570d3d7fe8e3c26615d3097c73546c5eacd63/docs/development/evidence/W550-01/DELIVERY.md) · [正式 PGT-017](../../acceptance/postgresql-tenancy.json#L646)

Main 原版本化 IAM 部门停用及原 TLS 服务签发与 CRM 隐私导出联验。基线先停用后仍生成制品，另一次在 CRM 已持业务锁时 Main 提前返回 200。修复后两种顺序如下。

| 顺序 | 实际结果 |
| --- | --- |
| CRM 先持门 | PostgreSQL `pg_blocking_pids` 显示 Main 封存连接等待。CRM 导出、制品和审计先提交并保留，随后 Main 提交部门停用。 |
| Main 停用先提交 | CRM 未收到新 C02 部门事件，持停用前签发的权限。实际 CRM 拒绝生成制品，导出任务、制品、审计和 Outbox 未变。 |

独立读回 Main 的部门、身份快照、代次、优先 Outbox、IAM 审计与原回执。角色及租户仍有效。原命令重放返回同一结果，停用后重新签发返回 403。CRM 按事务中实际主体和资源的部门路径核对来源版本。目标上级过旧会拒绝，无关部门更新仍允许相同业务。已提交原件由原读权限恢复。实际两种顺序共 4 项通过，角色撤权与公开提交暂停兼容 4 项通过，Main 定向 14、CRM 定向 10 项通过。

CRM 前向安装和执行授权见交付记录。本地发布 Main 310、CRM 255 个文件，API 与受测程序集摘要一致。隔离宿主实测 Main 200/401、CRM 200/403。原始基线、TRX、发布摘要与停止证据在 `D:/CP6-test-artifacts/W550-01`。Actions 启动、重跑和取消均为零。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。还需核对原生控制及直接 SQL、Main 其他独立写者、跨库确认丢失和恢复。生产采用需相应前向安装及配套代码版本。 [跨库余项 #1012](https://github.com/GTX537/CP6/issues/1012)

两仓分支和席位处置以远端 PR 和本地 `main-finish.json`、`crm-finish.json` 为准。
