# W636-01 普通 Core 库存的财务意图和原事件恢复

Main 开发基线 `919b1868041fbeb2034022b5072d38ca4bdc9d14`。产品及 W636 测试提交 `2ce0ac27062292cd0da40fde118ac03e51729ec5`，本地分支最终提交 `e2529f9fbd6b591f9eacca2f4c0d0c2bc2a639af`。关联 #1156 和 PGT-017。复用 W635 已实测的旧库缺口，原入库初次因锁期失败，在同一期间复开后实际 Worker 将同一事件从 Attempts 1 推进到 2，仍返回 DISPATCH-404。W632 已证实开放期的原采购入库数量 2、单价 12.3456、原借贷各 24.69。[Main #1156](https://github.com/GTX537/CP6/issues/1156) · [W635 原失败](D:/CP6-test-artifacts/W635-01/DELIVERY.md) · [W632 原正向对照](D:/CP6-test-artifacts/W632-01/DELIVERY.md)

## 实现

普通 Core PostgreSQL 的 `StockMovementService.ApplyInternalAsync` 在原库存交易、原库存及来源业务行的物理事务内保存财务意图。意图包含版本、不可变 StockTransaction GUID、原业务号码和原始量价。原交易先提交，随后既有即时入口尝试过账。首次处理中断时，真实 retry worker 仍可消费同一 Pending 意图。采购入库、盘盈、盘亏和原 SCRAP 映射继续使用已有财务规则。保存意图只做原分类、身份及量价捕获，实际估价在后段执行。[原库存保存及投递](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.Core/Services/Wms/StockMovementService.cs:165) · [财务意图](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.Core/Services/Fin/StockFinBridge.StockMoveOwner.cs:21)

原尝试在 Main 根锁和 Core PostgreSQL 事务上重读同租户事件、原流水及实际已过账借贷。旧事件按原租户和唯一库存交易号定位，并核对原 JSON 的类别及量价。原数量和单价与 PG 存储投影一致后，估价使用事件中的完整原始数值。效果失败通过原生保存点撤销，之后更新同一条事件的尝试、结果及退避。提交前再次检查当前租户有效性。已经完成的原凭证沿用，已红冲原凭证的历史记录不会复记。[原事件处理](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.Core/Services/Fin/StockFinBridge.StockMoveOwner.cs:55) · [后台所有者分流](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.WebApi/BackgroundServices/IntegrationEventRetryWorker.cs:114)

## 实际验收

隔离 PostgreSQL 18.6、正式登录后的签名 Main Kestrel 和独立连接回读。模拟入库计划、库存、会计期、科目和权限。W636 的 11 项定向原生用例全部通过，失败及跳过为 0。复用 14 项已有库存财务组件回归，另在任务端口验证 7 项 W620 原生出货恢复回归。[W636 原始 TRX](D:/CP6-test-artifacts/W636-01/candidate/results.trx) · [命令与测试源码对象](D:/CP6-test-artifacts/W636-01/candidate/invocation.json) · [W620 相关回归](D:/CP6-test-artifacts/W636-01/candidate-shipment-regression/results.trx)

| 范围 | 实际结果 |
| --- | --- |
| 真实采购入库事务与估价 | 阻塞意图 INSERT 时，独立连接仍见原库存、原收货单和原采番的请求前快照。随后在 GL INSERT 阻塞时已见 1 笔实物收货、同一原 StockTransaction 与 1 条 Pending 意图，尝试 0。库存量 2，PG 单价按原列保存为 1.0025，意图保留请求单价 1.00249，最终借 INVENTORY 2.00、贷 GRNI 2.00。若使用舍入后的单价估价，金额会变为 2.01。 |
| 原有业务失败和成功 | 锁期的合法实收入库返回 200，原财务事件 Failed、Attempts 1、E-FIN-112，无 GL 或 GL 采番。复开后，新意图和模拟的原格式旧事件均通过真实 Worker 恢复原事件、同一库存身份和 24.69 借贷，Attempts 2。已存在正确原凭证且重新锁期时仍收敛到原凭证，原总账和采番逐行一致。用实际签名人工红冲及异人过账后，旧失败事件重试不产生新凭证，原转回和采番逐行一致。 |
| 两种实际提交顺序 | Worker 先抵达真实 Fin_JournalEntry INSERT 时，签名租户暂停在 Main 根锁等待。Worker 提交后，独立连接在暂停 UPDATE 前见 Attempts 2 和唯一凭证。暂停先抵达真实 Sys_Tenants UPDATE 时，已选中事件的 Worker 在同一根锁等待。暂停提交后事件保持 Attempts 1，原库存和财务快照一致。重新启用后原事件只处理一次。每种顺序均观察到关系锁及事务 ID 等待。 |
| 两次 COMMIT 回执中断 | 隔离 PostgreSQL 回执夹具先使真实收货返回 503 OPERATIONS_OUTCOME_UNKNOWN，独立连接已见一笔实物库存和 Pending 原事件。实际 Worker 的财务 COMMIT 回执再次中断后，原事件已 Success、Attempts 1，凭证及采番各一份。新 Worker 再扫描，完整快照一致。 |
| 盘点审批和通用库存映射 | 实际签名审批同一张库存盘点，原差异 +1.23456789 / -1.25，两条原 ADJ 分别关联原明细及独立凭证。盘盈借 INVENTORY、贷 NON_OP_INCOME 各 3.70，盘亏借 PENDING_PROPERTY_LOSS、贷 INVENTORY 各 6.25。普通 Core 通用库存入口的原 SCRAP 借 NON_OP_EXPENSE、贷 INVENTORY 各 7.50。无价 INBOUND 保留实物及 Skipped，无额外凭证。 |
| 配置停用 | `StockFinBridge:Enabled=false` 的实际签名收货保存实物，未新建财务事件。停用时既有待处理事件和 Attempts 保持原值，重新启用后同一记录正常恢复。 |

首轮夹具对入库及盘点沿用了财务 `OK` 响应断言，真实 WMS 合同返回 `WM-MSG-071`。10 项因此在前置步骤结束，原 TRX 保留。首轮五个 W620 测试宿主沿用了旧夹具的 56936。测试夹具增加隔离端口覆盖后，仅重跑受影响的 7 项，使用 PostgreSQL 57113 和 Kestrel 57117。11 项 W636 新场景使用 PostgreSQL 57112、Kestrel 57117、回执夹具 57114 和 57115。被复用的组件回归三个产品程序集 SHA-256 与正式 W636 验证相同。W620 重跑的程序集内嵌版本使用随后生成的 Git 提交元数据，原 W636 发布证明仍对应通过 11 项的实际二进制。[首轮未改产品的原 TRX](D:/CP6-test-artifacts/W636-01/candidate-first/results.trx) · [首轮程序集哈希](D:/CP6-test-artifacts/W636-01/candidate-first/tested-assemblies.json) · [原生验证程序集哈希](D:/CP6-test-artifacts/W636-01/candidate/tested-assemblies.json)

## 受限库存写者交接

启用 `StockReadTenancy` 对应写入配置时，通用 `POST /api/wms/stock/apply`、`/commands/v1/apply` 和 `/commands/v2/apply` 可以经 `StockMovementWriteContextV1` 写入实际原库存。通用请求可进入现有 INBOUND 和 SCRAP 分类，盘点审批的 STOCKTAKE 由其独立 Core owner 持有。受限写者继续在提交后调用既有兼容财务桥，其已持久化且可校验的 Failed 事件可由 W636 的 Worker 恢复。受限写者本次未接入原生意图和首次财务提交边界。W642 需用实际受限连接实现并验证，同一库存事务内的写入只接受该事务及租户拥有的原库存行，保留原始量价和 PG 投影。现有 `record_department` 函数的同租户和当前事务 xmin 约束提供参考。Issue #1156 保持待 W642。[受限授权与写事务](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.Core/Services/Integration/OperationsOwnerV1.StockOperation.cs:10) · [受限配置](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.WebApi/Configuration/StockReadTenancyConfiguration.cs:13) · [同事务原库存身份约束](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.Core/Persistence/Tenancy/StockMovementWriterStorageV1.cs:139)

## 本地发布及工作区

本地发布与验证的 WebApi、Core 和 PostgreSQL 程序集逐项 SHA-256 一致。清单覆盖 310 个文件，清单 SHA-256 为 `8aafa6d47a811920195e5080af9f8f6df5310f2ee21aca22eda07f1db0759f8a`。发布二进制在 57118 实际启动，`GET /health/live` 返回 200，匿名 `POST /api/wms/inbound-receipt` 返回 401。测试夹具后续提交仅决定相关回归使用哪个本地端口，发布运行时哈希沿用。[发布冒烟](D:/CP6-test-artifacts/W636-01/publish-smoke.json) · [发布清单](D:/CP6-test-artifacts/W636-01/publish-sha256.json)

任务分支 `codex/w636-01` 本地保留，DEV-1 工作区干净。范围 57110–57119 无监听，任务测试、Kestrel 和 PostgreSQL 已停止。协调窗口持有远端采用、Issue 和席位释放，正式生产验收状态由相应窗口记录。
