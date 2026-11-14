# W592-01 CRM 恢复库就绪验证

CRM [#230](https://github.com/GTX537/CP6.CRM/pull/230) 已合入 `14062739ffa66e1664744595e59427508d7d982b`，与受测源码 `5b5333cb751ba0b7abf950a5b3c3ab28cadf181b` 的内容一致。[CRM 交付记录](https://github.com/GTX537/CP6.CRM/blob/14062739ffa66e1664744595e59427508d7d982b/docs/development/evidence/W592-01/DELIVERY.md) · [缺陷 #229](https://github.com/GTX537/CP6.CRM/issues/229)

- 隔离 PostgreSQL 18.6 中真实恢复原备份并提升时间线。基线误报就绪 200，修复后目标就绪 503、存活 200。受保护运维探针也报告依赖不可用。原库租户处于业务暂停且物理存储匹配时，就绪为 200。
- 使用两个模拟租户验证任一已登记绑定缺失时探针关闭。补齐后返回 200，撤销和恢复只读检查执行权分别返回 503 和 200。CRM 复用 Outbox 的物理存储绑定判断。采用时 API 运行角色须获原函数执行权，封存表权限无需更改。
- 本地两项新增场景及四项相关回归 **6/6**，零跳过。保留基线误报和一次夹具构建失败。受测源码另行本地 Release，确认 255 个文件 SHA-256，实际宿主 HTTP 存活 200、匿名配置 403、未初始化依赖的就绪 503。原始 TRX、发布清单和停机记录在 `D:/CP6-test-artifacts/W592-01`。

前序 Main 与 CRM 两种提交顺序及真实 C02/Kafka 仍引用 [W582](../W582-01/DELIVERY.md)，原写集增量恢复仍引用 [W585](../W585-01/DELIVERY.md)。本轮验证的是 CRM 恢复目标的就绪判定。正式切换还需把所有登记租户的 Main 权威、CRM 封存、恢复写集及 Kafka 水位对账，核实原写者退出，再受控重绑和切换。Main 还需核对其余财务写者、独立 Space Design 和全量写者登记。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。Actions 启动、重跑及取消为零，未部署。[正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)
