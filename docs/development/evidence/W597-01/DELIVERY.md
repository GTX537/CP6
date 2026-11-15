# W597-01 会计期间写入的共同事务栅栏

2026-11-15 UTC。开发基线 `7cd756c715d5a67ecf5170e1697b2781f79ad6cd`，受测源码 `2425e6556cd387c81c68042e79f72867cfe4c1de`。使用实际 Main Kestrel 和隔离 PostgreSQL 18.6。模拟业务数据包括租户、月期和财年、固定资产、外币应付、汇兑与结转凭证。 [缺陷 #1107](https://github.com/GTX537/CP6/issues/1107) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)

- 会计期间结账、反结账、年结、反年结在各自 HTTP 入口使用请求 Core 原生事务。持有 Main 当前权限栅栏后按原动作和会话执行。期间、折旧、汇兑、凭证和采番同事务提交。业务失败先回滚，再返回原 400 和财务业务代码。
- 四条路由各验证暂停先提交、业务先提交。额外对年结执行保持会话的角色撤权两种顺序。业务先提交的原写集保留，撤权先提交时业务拒绝。按实际用例核对权限代次、快照、安全审计、原命令回执与 Outbox。通用 HTTP 尝试日志继续记录失败请求。
- 月结在已完成早期写入后触发 `E-FIN-112`，年结第二张凭证触发 `E-FIN-104`，两个场景都确认失败写集回滚。已提交的历史 v1 支持只补第二张凭证。反年结保留已过账历史，重复年结和反年结使用不冲突的来源号，由实际 PostgreSQL 索引的比较语义判断占用。
- 基线 13 项复现、1 项原历史恢复通过。最终 **14/14 实际 PostgreSQL 场景和 30/30 相关财务回归，零跳过**。本地发布 310 文件 SHA-256，Main、Core 和 PostgreSQL 迁移程序集与受测文件一致，实际存活 200、匿名年结 401。集成合并核对三份受测文件逐字节一致，期间执行相关输入沿用受测源码的结果。 最终 TRX、脱敏基线失败与本地制品证据在 `D:/CP6-test-artifacts/W597-01`。Actions 启动、重跑及取消为零，未部署。

## 财务写入口清单

从当前 `CP6.WebApi/Controllers/Fin` 核对 23 个控制器，其中 19 个共有 69 条 POST/PUT/DELETE 声明。26 条采用共同栅栏，40 条可写路径待采用，2 条预览只读，1 条在 PostgreSQL 下先拒绝写入。

| 路径前缀 | HTTP 写声明及后缀 | 栅栏采用 |
| --- | --- | --- |
| `/api/fin/ap/invoice` | `POST /`、`POST /{id}/post` | 待接入，2。[入口](../../../../CP6.WebApi/Controllers/Fin/ApInvoiceController.cs) |
| `/api/fin/ap/master` | `POST /bank-account`、`POST /tax-code` | 待接入，2。[入口](../../../../CP6.WebApi/Controllers/Fin/ApMasterController.cs) |
| `/api/fin/ar/invoice` | `POST /`、`POST /{id}/post`、`POST /credit-memo`、`POST /{id}/reverse` | 待接入，4。[入口](../../../../CP6.WebApi/Controllers/Fin/ArInvoiceController.cs) |
| `/api/fin/asset-card` | `POST /`、`PUT /{id}`、`POST /{id}/activate` | 待接入，3。[入口](../../../../CP6.WebApi/Controllers/Fin/AssetCardController.cs) |
| `/api/fin/asset-category` | `POST /`、`PUT /{id}`、`DELETE /{id}` | [W534](../W534-01/DELIVERY.md)，3。[入口](../../../../CP6.WebApi/Controllers/Fin/AssetCategoryController.cs) |
| `/api/fin/asset-deprec` | `POST /run`、`PUT /entry/{entryId}/workload`、`POST /{runId}/post`、`POST /{runId}/reverse` | 待接入，4。[入口](../../../../CP6.WebApi/Controllers/Fin/AssetDepreciationController.cs) |
| `/api/fin/asset-disposal` | `POST /`、`POST /{id}/confirm`、`POST /{id}/reverse` | 待接入，3。[入口](../../../../CP6.WebApi/Controllers/Fin/AssetDisposalController.cs) |
| `/api/fin/bank-import-profile` | `POST /upsert`、`DELETE /{id}` | [W589](../W589-01/DELIVERY.md)，2。[入口](../../../../CP6.WebApi/Controllers/Fin/BankImportProfileController.cs) |
| `/api/fin/bank-recon` | `POST /{statementId}/auto-match`、`POST /{statementId}/manual-match`、`POST /unmatch/{groupId}`、`POST /{statementId}/generate-voucher`、`POST /{statementId}/mark-pending`、`POST /{statementId}/lock`、`POST /{statementId}/unlock` | [W589](../W589-01/DELIVERY.md) / [W590](../W590-01/DELIVERY.md)，7。[入口](../../../../CP6.WebApi/Controllers/Fin/BankReconciliationController.cs) |
| `/api/fin/bank-statement` | `POST /`、`POST /{id}/import`、`POST /{id}/line`、`PUT /{id}/line/{lineId}`、`DELETE /{id}/line/{lineId}` | [W589](../W589-01/DELIVERY.md) / [W591](../W591-01/DELIVERY.md)，5。[入口](../../../../CP6.WebApi/Controllers/Fin/BankStatementController.cs) |
| `/api/fin/budget` | `POST /`、`PUT /{id:guid}`、`POST /{id:guid}/deactivate`、`POST /{id:guid}/versions`、`PUT /versions/{vid:guid}`、`POST /versions/{vid:guid}/submit`、`POST /versions/{vid:guid}/activate`、`DELETE /versions/{vid:guid}` | 待接入，8。[入口](../../../../CP6.WebApi/Controllers/Fin/BudgetController.cs) |
| `/api/fin/budget/lines` | `POST /`、`DELETE /{lineId:guid}`、`POST /import/preview`、`POST /import/confirm` | 待接入，3。`import/preview` 只读。[入口](../../../../CP6.WebApi/Controllers/Fin/BudgetLineController.cs) |
| `/api/fin/budget/report` | `POST /pre-check` | `pre-check` 只读。[入口](../../../../CP6.WebApi/Controllers/Fin/BudgetReportController.cs) |
| `/api/fin/cost` | `POST /{workOrderNo}/collect`、`POST /{workOrderNo}/settle` | 工单 `settle` 待接入，1。PostgreSQL `collect` 在写入前返回 `FINANCE_OWNER_REQUIRED`。[入口](../../../../CP6.WebApi/Controllers/Fin/CostController.cs) |
| `/api/fin/account` | `POST /`、`PUT /{id}`、`POST /{id}/deactivate`、`POST /import` | 待接入，4。[入口](../../../../CP6.WebApi/Controllers/Fin/GlAccountController.cs) |
| `/api/fin/journal` | `POST /`、`POST /{id}/submit`、`POST /{id}/post`、`POST /{id}/reject`、`POST /{id}/reverse` | [W538](../W538-01/DELIVERY.md)，5。[入口](../../../../CP6.WebApi/Controllers/Fin/JournalEntryController.cs) |
| `/api/fin/ap/payment` | `POST /`、`POST /{id}/reverse`、`POST /{id}/settle` | 待接入，3。[入口](../../../../CP6.WebApi/Controllers/Fin/PaymentController.cs) |
| `/api/fin/period` | `POST /{id}/close`、`POST /{id}/reopen`、`POST /year-close/{fiscalYear:int}`、`POST /reopen-year/{fiscalYear:int}` | 本轮四项。[入口](../../../../CP6.WebApi/Controllers/Fin/PeriodController.cs) |
| `/api/fin/ar/receipt` | `POST /`、`POST /{id}/reverse`、`POST /{id}/settle` | 待接入，3。[入口](../../../../CP6.WebApi/Controllers/Fin/ReceiptController.cs) |

另登记当前运行配置可触达的背景或跨域写者。月末折旧 Worker 创建批次草稿及采番。WMS 财务桥有原业务提交后的凭证写入与重试。采购三单匹配及外注成品成本适配器分别创建应付和成本凭证。OA `FinJournalPost` 回调可过账或驳回。需分别核对其实际投递事务、授权和安全继续合同。财务对账 Worker 只读。 [月末折旧](../../../../CP6.WebApi/BackgroundServices/AssetDepreciationWorker.cs) · [WMS 财务桥](../../../../CP6.Core/Services/Fin/StockFinBridge.cs) · [采购匹配](../../../../CP6.Core/Services/Pur/ThreeWayMatchService.cs) · [外注成本](../../../../CP6.Core/Services/Pur/Contracts/FinCostServiceAdapter.cs) · [OA 凭证回调](../../../../CP6.Core/Services/Fin/JournalApprovalCallback.cs)

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。余项包括剩余财务、Space 文件和其他设计写入口以及 CRM 产品级受控切换。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646)
