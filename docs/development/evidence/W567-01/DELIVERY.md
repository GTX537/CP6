# W567-01 · 采购确认收货与撤权

2026-11-14 UTC。Main 基线 `718aa9035e2f51147f88962e71c142d0d456a67b`，受测源码 `64ea3f2eea76b17652a2051a61212aa798e52b74`。使用隔离 PostgreSQL 18.6、真实 Kestrel、当前 WMS 入库和库存实现、模拟采购资料及普通业务角色。 [Issue #1059](https://github.com/GTX537/CP6/issues/1059)

旧收货 HTTP 在入口检查采购权限，WMS 预定、确认和入库分别提交。修复前 3 项原生回归均失败。采购 add 撤权先提交，旧请求等待后仍返回 200。业务执行到采购写入时，其他连接已看到入库和库存。采购最后提交失败仍留下库存 3.12345678、流水和两类编号。

- 现行 `POST /api/pur/gr` 由 WMS 现有 Owner 开启单个 Core 本地事务，持 Main 共同认证根，核对当前采购动作和原 WMS 的建预定、确认、入库、来源、仓位、字段与 Stock 继承。采购单、PO 状态与累计、WMS 计划/实绩、库存流水和两类编号同生效。撤权先获锁时拒绝旧授权；收货先获锁时，IAM 等待，收货和复核过的原历史保留。直接回读权威代次、版本、快照、优先 Outbox、审计和回执。针对同一 PO 行的多次收货明细按合计检查剩余量并保留合法组合。 [采购入口](../../../../CP6.WebApi/Controllers/Pur/GoodsReceiptController.cs) · [仓储 Owner](../../../../CP6.Core/Services/Wms/InboundService.Owner.cs)
- 数据库最后拒绝提交时全部回滚。真实丢弃 PostgreSQL COMMIT 回执时返回 `OPERATIONS_OUTCOME_UNKNOWN`，采用 `Idempotency-Key` 可读回原操作或按同一内容重试。回读绑定原租户、操作者和当前 WMS 入库读取范围。不同内容返回 409，采购 add 已撤后仍可使用保有的 WMS 读取权限查询已提交原件。兼容现有无该头的调用，其提交仍原子，原操作查询需要调用方提供键。网页保存当次键和请求用于恢复。WMS 通知和财务桥继续使用既有提交后尽力投递，故障回读不会重新触发它们。 [PostgreSQL 回归](../../../../CP6.Tests/Erp/Pgt017GoodsReceiptConfirmationPostgreSqlTests.cs) · [网页回归](../../../../cp6.web/src/views/pur/GoodsReceiptView.spec.ts)
- 最终定向 40 项全部通过、零跳过，包含本窗口 8 项 PostgreSQL 行为、已修检收、WMS 入库和采购组件。网页 3 项通过，类型与前端构建通过。本地 WebApi 发布 310 文件，SHA-256 `3af708f8902d5e5bf4dda232d73dd9c9704586bfbcd28040a025a608069ec597`，同测试制品；Web 构建 568 文件，首页 SHA-256 `67597b27459213236cf7192b805332711f82a1a8c737c3e71917a15319c85e5d`。隔离发布建库退出 0，存活 200，匿名租户管理与采购操作查询 401。保留原运行、失败、摘要与日志于 `D:/CP6-test-artifacts/W567-01`。Actions 未触发，未部署。

正式 PostgreSQL 26 PASS / 1 NOT_RUN，原询盘 60/60。 本轮验证现行入口的并发与事务，规划中的正式 PUR-GR-01 版本采用仍沿用其验收记录。PGT-017 剩余范围包括Main 旧 OA 其余写者、财务、独立 Space Design、原生数据库角色，以及两库未知结果恢复、重开对账、已登记宿主传输和旧实例排空。 最终分支/工作区处置记录位于 PR 和本任务 `main-finish.json`。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)
