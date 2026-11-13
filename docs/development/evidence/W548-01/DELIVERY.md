# W548-01 · Main 租户有效期与 CRM 导出

2026-11-13 UTC。Main 基线 `fa23eac39b994373a99aa3c0582da27854861c30`，受测源码 `6bd8418a5f5ae8ebff1823d07a013633a6cda9be`。CRM 采用 `af1e66c0e8ee95af5bfd3702d1251693dd6ec6f8`，已核对与受测产品源码一致。两套隔离 PostgreSQL 18.6 使用模拟租户与询盘。 [CRM 交付](https://github.com/GTX537/CP6.CRM/blob/af1e66c0e8ee95af5bfd3702d1251693dd6ec6f8/docs/development/evidence/W548-01/DELIVERY.md) · [PGT-017](../../acceptance/postgresql-tenancy.json#L646)

Main 的旧租户更新与版本化更新共用 `UpdateCoreAsync`。启用租户的有效期变化由同一 C02 快照写入分配新版本，原 Main 事务在 CRM 持久化门槛后提交。CRM 使用既有七个 C02 锁和租户门槛，已登记入口在原业务事务中读取。期限更新不能重新开放已经暂停的租户。新版本发布之前，CRM 拒绝旧版本的新效果。两仓部署需前向安装与专用封存登录授权。 [SQL](https://github.com/GTX537/CP6.CRM/blob/af1e66c0e8ee95af5bfd3702d1251693dd6ec6f8/src/CP6.CRM.Infrastructure/Identity/PostgreSql/InquiryTenantDeadlineSchemaV1.sql)

| 两库实际顺序 | 结果 |
| --- | --- |
| `candidate-authority-first` | 原 Main HTTP 管理命令把有效期改为过去并提交。CRM 仍持旧已签发的服务授权，拒绝生成制品。导出任务、制品、审计、Outbox 未变，期间未接收新 C02。Main 维持 Enable=true，后续实际 TLS 签发返回 403。 |
| `candidate-owner-first` | CRM 持锁后等原询盘行，Main 专用封存连接由 `pg_blocking_pids` 证实等待。CRM 原导出、制品与审计先提交并保留，Main 再提交期限。 |

两种顺序独立读回 Main 有效期、管理版本、认证策略版本、C02 快照与代次、Outbox、审计和原回执，重复请求返回同一回执。基线两个顺序均失败。Main 提前返回成功，另一次 CRM 在 Main 生效后生成了制品。修复后两种顺序共 4 项通过，暂停兼容联验 2 项通过，CRM 定向 9 项通过。Main 定向初次 19 通过，1 项匿名检查在夹具数据库连接中断时返回 500，候选与基线的独立复测均通过。保留原始结果及主机日志。

本地发布目录含 Main 310、CRM 255 个文件，各自 API 与受测程序集 SHA-256 一致。隔离发布启动及 HTTP 实测 Main 存活 200、匿名管理 401，CRM 存活 200、匿名配置 403。原始日志和发布摘要见 `D:/CP6-test-artifacts/W548-01`。Actions 启动、重跑和取消为零。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。自然到期跨越实际业务提交、部门变更、独立数据库控制工具或直接 SQL、Main 其他独立写入口及两库恢复待验收。 [跨库余项 #1012](https://github.com/GTX537/CP6/issues/1012)

两仓任务分支和席位处置以远端 PR 与本地 `main-finish.json`、`crm-finish.json` 为准。
