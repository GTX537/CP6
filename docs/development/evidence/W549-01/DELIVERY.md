# W549-01 · CRM 公开表单签发与租户自然到期

2026-11-13 UTC。Main 产品复用 `0fded5f38fec19eaf3bc6f458d373e334e8c559f`。CRM 已合入 `ef66e49f34ee75615813178aa4e911ae8b992803`，受测源码 `cf69633ae693f1d65e0a16961d584f41a0a9ac33`。 [CRM 交付](https://github.com/GTX537/CP6.CRM/blob/ef66e49f34ee75615813178aa4e911ae8b992803/docs/development/evidence/W549-01/DELIVERY.md) · [正式 PGT-017](../../acceptance/postgresql-tenancy.json#L646)

在 CRM 原生公开表单 HTTP 路由和独立 PostgreSQL 18.6 使用模拟租户、期限及表单。先核对有效期内签发成功，再用 PostgreSQL `pg_blocking_pids` 观察配置行、签发表及 COMMIT 三种等待。用同一数据库时钟确认越过登记租户期限才释放阻塞。三种顺序均返回原 404，签发行、提交业务、Outbox 及 Cookie 无新增，过期前签发保留。基线在配置行和签发表等待后返回 200。

修复保留原 C02 锁，写入租户与 C02 发布有效期的较早值，延迟约束在签发事务内核对 `clock_timestamp()`。原凭证期限、响应和历史回读沿用现行合同。需要在 CRM 既有库前向安装，原历史行无需回填。定向 PostgreSQL 共 15 项通过。真实本地发布 CRM 200/403，Main 复用 W548 制品 200/401，原始结果和发布摘要在 `D:/CP6-test-artifacts/W549-01`。Actions 启动、重跑、取消为零。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。尚需核对部门授权、直接 SQL、Main 其余独立写者、其他当前权限的期限边界，以及两库中断和恢复。 [跨库余项 #1012](https://github.com/GTX537/CP6/issues/1012)

两仓任务分支和席位处置以远端 PR 和本地 `main-finish.json`、`crm-finish.json` 为准。
