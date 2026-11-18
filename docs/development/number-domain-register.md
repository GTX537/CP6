# 业务编号域与原分配器登记

本登记对应 WP02-16b 和 SYS-NUM-01 的 NU03。逐项定位 Main 业务服务和控制器中的编号调用、业务显示码的派生处，以及 CRM 保有的外部查询号。Main 依据 `3ea9d2ba2ab4e1a644369432460a5251e2f1bcda`，CRM 依据 `ac8a2706a018317eafce8247aff1e7fe5e416329`。表中的业务类和方法是原调用者 `sourceOwner` 的代码入口。登记或规则编辑不会自动改变任何消费者的写入者。

## 原分配器的域、时间和处置依据

| 原分配器 | 唯一域与法人、仓库归属 | 周期、时区与处置原因 |
|---|---|---|
| Main.Common.DocNumber | `T_DocSequence.FuncCode` 全局。使用订单、产品、模具等原业务范围，租户、法人、仓库不拆计数 | 调用者日期或服务器本地日仅用于显示年月，功能码流水永不重置。保留原事务；PostgreSQL 的 ORD 在原事务内原子递增，无原事务时独立保留。**KEEP_EXISTING**。[实现](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Common/DocNumber.cs#L28) |
| Main.Fin.FinSequenceService | 租户、财务键和会计年月。同租户不同凭证或会计期按其实际键区分，法人、仓库无独立计数键 | 调用者提供业务日历日期，不把已入账日期折算到另一时区。未传日期采用服务器本地日，每月起新流水。财务归属方维持会计口径。**KEEP_EXISTING**。[实现](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Fin/FinSequenceService.cs#L16) |
| Main.Mes.MesSequenceService | `SeqKey, SeqDate="*"` 全局。工厂、产线、租户、仓库和法人不另开同键 | 原调用点默认服务器本地日，展示年月，流水跨期累加；工单和生产事件仍由 MES 负责。**KEEP_EXISTING**。[实现](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Mes/MesSequenceService.cs#L26) |
| Main.Wms.WmsSequenceService | 租户、前缀和 `DateKey="*"`。仓库是业务对象范围，同租户跨仓复用同前缀计数；法人无独立计数键 | 原调用点采用服务器本地日，展示年月，跨期不重置。仓储归属方保留出入库与移动事务。**KEEP_EXISTING**。[实现](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/WmsSequenceService.cs#L22) |
| Main.Pub.SeqService | `Pub_DocSequence(TenantId,BizKey)`。业务、法人、仓库只能描述现有编号域，同租户同键共享唯一规则 | 日期输入见逐项表。ResetCycle 1／2／3 按日／月／年，其余无周期。原提供者保留原写事务，但单独的旧流水表不足以还原外露号码和未决操作。拟切换时 **ADAPTER_REQUIRED**，由原业务归属方提供批准、冻结清单与原键查询；本卡未给下列业务签发切换批准。[实现](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Pub/SeqService.cs#L25) |
| Main.Wms.CarrierService | 货运业务所属租户与承运商。优先使用承运商已给的跟踪号；缺失时原服务以承运商、服务器本地日期和随机段生成 | 无跨仓共享流水或月重置合同，外部可见原值由承运业务保留。**KEEP_EXISTING**。[原方法](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/CarrierService.cs#L120) |
| Main.Wms.StockMovementSequenceV1 | PostgreSQL 表级 identity，为已接受的租户、库存池、移动原件分配单调序号。读取按租户与移动绑定，表级序号可跳 | 无日期、时区或按池重置要求。不能将账务引用重新格式化为 WMS 显示码。**KEEP_EXISTING**。[写入](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/StockMovementSequenceV1.cs#L10) · [表定义](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Persistence.PostgreSql/Migrations/Core/20261030023000_RmaPhysicalReturnV1.cs#L47) |

## Main 源入口逐项登记

下表每一行沿用上表对应原分配器的法人、仓库、周期与处置依据。相同字符串在不同原分配器或租户内具有不同含义。采购 PR 和 MES PR、MES QC 和 WMS QC 同名，各有独立原表。共用同一原分配器和业务键的多个入口继续共享一个编号域。

### Erp

| 原业务归属方与入口 | 业务键 | 原分配器与唯一域 | 具体时间输入 | 处置 |
|---|---|---|---|---|
| [BackorderService.SplitToNewOrderAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Erp/BackorderService.cs#L151) | `ORD` | DocNumber · 全局功能码 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [EstimateCalcService.CreateAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Erp/EstimateCalcService.cs#L97) | `EMC` | DocNumber · 全局功能码 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [EstimateCalcService.CopyAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Erp/EstimateCalcService.cs#L192) | `EMC` | DocNumber · 全局功能码 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [FscChecklistService.NextFscNoAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Erp/FscChecklistService.cs#L230) | 配置前缀，默认 FSC，至多 3 字符 | DocNumber · 全局功能码 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [OrderService.NextSequenceCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Erp/OrderService.cs#L516) | `ORD` | DocNumber · 全局功能码 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [PlateMoldService.CreateAsync（转交原服务）](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Erp/PlateMoldService.cs#L124) | `MLD` | DocNumber · 全局功能码 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [PlateMoldService.NextSequenceAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Erp/PlateMoldService.cs#L343) | `MLD` | DocNumber · 全局功能码 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [PlateMoldService.CreatePlateMoldOrderAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Erp/PlateMoldService.cs#L637) | `ORD` | DocNumber · 全局功能码 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [PlateMoldWriteOwnerV1.ExecuteAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Erp/PlateMoldWriteOwnerV1.cs#L71) | `MLD` | DocNumber · 全局功能码 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [ProductService.CreateAsync（转交原服务）](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Erp/ProductService.cs#L175) | `PRD` | DocNumber · 全局功能码 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [ProductService.NextSequenceAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Erp/ProductService.cs#L337) | `PRD` | DocNumber · 全局功能码 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [QuotationService.CreateAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Erp/QuotationService.cs#L197) | `QTN` | DocNumber · 全局功能码 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [QuotationService.CopyAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Erp/QuotationService.cs#L404) | `QTN` | DocNumber · 全局功能码 | 服务器本地日，显示年月 | `KEEP_EXISTING` |

### Fin

| 原业务归属方与入口 | 业务键 | 原分配器与唯一域 | 具体时间输入 | 处置 |
|---|---|---|---|---|
| [ApInvoiceService.WriteDraftAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Fin/ApInvoiceService.cs#L93) | `AP` | FinSequenceService · 租户／财务业务键 | invoice.InvoiceDate | `KEEP_EXISTING` |
| [ArInvoiceService.WriteDraftAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Fin/ArInvoiceService.cs#L82) | `AR` | FinSequenceService · 租户／财务业务键 | invoice.InvoiceDate | `KEEP_EXISTING` |
| [AssetDepreciationService.RunAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Fin/AssetDepreciationService.cs#L99) | `DEP` | FinSequenceService · 租户／财务业务键 | 会计年月首日 | `KEEP_EXISTING` |
| [AssetDepreciationService.AccrueDisposalFinalAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Fin/AssetDepreciationService.cs#L317) | `DEP` | FinSequenceService · 租户／财务业务键 | 会计年月首日 | `KEEP_EXISTING` |
| [AssetDisposalService.CreateAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Fin/AssetDisposalService.cs#L77) | `FAD` | FinSequenceService · 租户／财务业务键 | d.DisposalDate | `KEEP_EXISTING` |
| [BankStatementService.CreateAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Fin/BankStatementService.cs#L89) | `BKR` | FinSequenceService · 租户／财务业务键 | period.PeriodStart | `KEEP_EXISTING` |
| [BankStatementService.ConfirmImportAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Fin/BankStatementService.cs#L118) | `BKRIMP` | FinSequenceService · 租户／财务业务键 | DateTime.Today | `KEEP_EXISTING` |
| [BudgetService.CreateBudgetAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Fin/BudgetService.cs#L26) | `BUD` | FinSequenceService · 租户／财务业务键 | 服务器 DateTime.Now | `KEEP_EXISTING` |
| [CostCollectService.SaveCollectionAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Fin/CostCollectService.cs#L181) | `CS` | FinSequenceService · 租户／财务业务键 | 服务器 DateTime.Now | `KEEP_EXISTING` |
| [JournalEntryService.CreateDraftAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Fin/JournalEntryService.cs#L53) | `GL` | FinSequenceService · 租户／财务业务键 | entry.VoucherDate | `KEEP_EXISTING` |
| [JournalEntryService.AutoPostAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Fin/JournalEntryService.cs#L129) | `GL` | FinSequenceService · 租户／财务业务键 | entry.VoucherDate | `KEEP_EXISTING` |
| [JournalEntryService.ReverseAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Fin/JournalEntryService.cs#L182) | `GL` | FinSequenceService · 租户／财务业务键 | origin.VoucherDate | `KEEP_EXISTING` |
| [PaymentService.PayAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Fin/PaymentService.cs#L44) | `PAY` | FinSequenceService · 租户／财务业务键 | payment.PayDate | `KEEP_EXISTING` |
| [ReceiptService.ReceiveAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Fin/ReceiptService.cs#L44) | `RCP` | FinSequenceService · 租户／财务业务键 | receipt.ReceiptDate | `KEEP_EXISTING` |

### Mes

| 原业务归属方与入口 | 业务键 | 原分配器与唯一域 | 具体时间输入 | 处置 |
|---|---|---|---|---|
| [DefectRecordService.CreateCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Mes/DefectRecordService.cs#L79) | `DF` | MesSequenceService · 全局 MES 键 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [MachineService.RegisterDowntimeCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Mes/MachineService.cs#L211) | `DT` | MesSequenceService · 全局 MES 键 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [ProductionResultService.WriteCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Mes/ProductionResultService.cs#L230) | `PR` | MesSequenceService · 全局 MES 键 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [QualityInspectionService.CreateCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Mes/QualityInspectionService.cs#L150) | `QC` | MesSequenceService · 全局 MES 键 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [QualityInspectionService.CreateCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Mes/QualityInspectionService.cs#L200) | `DF` | MesSequenceService · 全局 MES 键 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [WorkOrderService.NextSequenceAsync（历史兼容分支）](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Mes/WorkOrderService.Owner.cs#L21) | `WO` | MesSequenceService · 全局 MES 键 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [WorkOrderService.NextSequenceAsync（PostgreSQL 归属方）](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Mes/WorkOrderService.Owner.cs#L22) | `WO` | MesSequenceService · 全局 MES 键 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [WorkOrderService.CreateCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Mes/WorkOrderService.cs#L159) | `WO` | MesSequenceService · 全局 MES 键 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [WorkOrderService.ExpandFromOrderCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Mes/WorkOrderService.cs#L426) | `WO` | MesSequenceService · 全局 MES 键 | 服务器本地日，显示年月 | `KEEP_EXISTING` |

### Plan

| 原业务归属方与入口 | 业务键 | 原分配器与唯一域 | 具体时间输入 | 处置 |
|---|---|---|---|---|
| [MrpEngine.RunAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Plan/MrpEngine.cs#L43) | `MRP` | DocNumber · 全局功能码 | 服务器本地日，显示年月 | `KEEP_EXISTING` |

### Plm

| 原业务归属方与入口 | 业务键 | 原分配器与唯一域 | 具体时间输入 | 处置 |
|---|---|---|---|---|
| [EngineeringIterationService.Change → Number](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Plm/EngineeringIterationService.Commands.cs#L47) | `PLM_PROJECT` | SeqService · 租户／规则 BizKey | DateTime.UtcNow 原日历值 | `ADAPTER_REQUIRED` |
| [EngineeringIterationService.Change → Number](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Plm/EngineeringIterationService.Commands.cs#L64) | `PLM_ITERATION` | SeqService · 租户／规则 BizKey | DateTime.UtcNow 原日历值 | `ADAPTER_REQUIRED` |
| [EngineeringRevisionOwnerV1.Allocate](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Plm/EngineeringRevisionOwnerV1.cs#L134) | `ENG_REV` | SeqService · 租户／规则 BizKey | 原修订操作固定的本地时间 | `ADAPTER_REQUIRED` |

### Pub

| 原业务归属方与入口 | 业务键 | 原分配器与唯一域 | 具体时间输入 | 处置 |
|---|---|---|---|---|
| [BaseCrudService.CreateAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Pub/BaseCrudService.cs#L57) | 派生类同时配置 SeqBizKey、CodeField；当前为模板扩展点 | SeqService · 租户／规则 BizKey | 服务器本地时间，按旧规则 | `ADAPTER_REQUIRED` |

### Pur

| 原业务归属方与入口 | 业务键 | 原分配器与唯一域 | 具体时间输入 | 处置 |
|---|---|---|---|---|
| [GoodsReceiptService.ConfirmReceiveCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Pur/GoodsReceiptService.cs#L154) | `GR` | SeqService · 租户／规则 BizKey | 服务器本地时间，按旧规则 | `ADAPTER_REQUIRED` |
| [PrGenerationService.GenerateFromShortageAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Pur/PrGenerationService.cs#L43) | `PR` | SeqService · 租户／规则 BizKey | 服务器本地时间，按旧规则 | `ADAPTER_REQUIRED` |
| [PrGenerationService.GenerateFromWorkOrderAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Pur/PrGenerationService.cs#L87) | `PR` | SeqService · 租户／规则 BizKey | 服务器本地时间，按旧规则 | `ADAPTER_REQUIRED` |
| [PurchaseOrderService.CreateAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Pur/PurchaseOrderService.cs#L74) | `PO` | SeqService · 租户／规则 BizKey | 服务器本地时间，按旧规则 | `ADAPTER_REQUIRED` |
| [PurchaseRequestService.CreateAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Pur/PurchaseRequestService.cs#L77) | `PR` | SeqService · 租户／规则 BizKey | 服务器本地时间，按旧规则 | `ADAPTER_REQUIRED` |
| [RfqService.CreateFromPrAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Pur/RfqService.cs#L89) | `RFQ` | SeqService · 租户／规则 BizKey | 服务器本地时间，按旧规则 | `ADAPTER_REQUIRED` |
| [ThreeWayMatchService.MatchInvoiceAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Pur/ThreeWayMatchService.cs#L68) | `TWM` | SeqService · 租户／规则 BizKey | 服务器本地时间，按旧规则 | `ADAPTER_REQUIRED` |

### Space

| 原业务归属方与入口 | 业务键 | 原分配器与唯一域 | 具体时间输入 | 处置 |
|---|---|---|---|---|
| [LocationPublishService.PublishFloorCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Space/LocationPublishService.cs#L120) | `LPB` | DocNumber · 全局功能码 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [LocationPublishService.DeactivateCurrentCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Space/LocationPublishService.cs#L241) | `LPB` | DocNumber · 全局功能码 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [LocationPublishService.RepublishAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Space/LocationPublishService.cs#L287) | `LPB` | DocNumber · 全局功能码 | 服务器本地日，显示年月 | `KEEP_EXISTING` |

### Wms

| 原业务归属方与入口 | 业务键 | 原分配器与唯一域 | 具体时间输入 | 处置 |
|---|---|---|---|---|
| [CarrierService.CreateShipmentCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/CarrierService.cs#L39) | `SHIP` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [CarrierService.CreateShipmentCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/CarrierService.cs#L42) | `CarrierCd` | CarrierService · 货运承运商／运单 | 服务器本地日 | `KEEP_EXISTING` |
| [CrossDockService.CreateCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/CrossDockService.cs#L44) | `XD` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [InboundService.CreateOrderCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/InboundService.cs#L98) | `IN` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [InboundService.ConfirmReceiptCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/InboundService.cs#L322) | `RC` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [InkService.CreateLotAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/InkService.cs#L45) | `INK` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [InkService.MixLotsAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/InkService.cs#L98) | `INK` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [InkService.RecordMatchAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/InkService.cs#L131) | `ICM` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [IotService.CreateSensorCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/IotService.cs#L33) | `SEN` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [KittingService.CreateOrderCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/KittingService.cs#L156) | `KIT` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [MobileService.CreateCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/MobileService.cs#L47) | `MTK` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [MobileTaskV1Service.CreateCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/MobileTaskV1Service.cs#L96) | `MTK` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [MobileTaskV2Service.CreateCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/MobileTaskV2Service.cs#L204) | `MTK` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [MobileTaskV2Service.CompleteCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/MobileTaskV2Service.cs#L842) | `MTK` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [OutboundService.CreateOrderCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/OutboundService.cs#L134) | `OUT` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [OutboundService.ShipCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/OutboundService.cs#L601) | `PKG` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [OutboundService.CreateFromWorkOrderCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/OutboundService.cs#L767) | `OUT` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [OutboundService.CreateFromOrderCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/OutboundService.cs#L813) | `OUT` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [PalletService.CreateAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/PalletService.cs#L53) | `PLT` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [PaperRollService.CreateCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/PaperRollService.cs#L39) | `ROLL` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [PaperRollService.SlitCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/PaperRollService.cs#L119) | `ROLL` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [PaperRollService.SlitCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/PaperRollService.cs#L141) | `ROLL` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [PlateMoldService.CreateAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/PlateMoldService.cs#L47) | `PLT2` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [QcInspectionService.CreateFromInboundCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/QcInspectionService.cs#L87) | `QC` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [QcInspectionService.CreateDirectCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/QcInspectionService.cs#L121) | `QC` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [RemnantService.CreateCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/RemnantService.cs#L36) | `REM` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [ReplenishService.CreateCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/ReplenishService.cs#L66) | `RPL` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [ReplenishService.GenerateBatchCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/ReplenishService.cs#L191) | `RPL` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [RmaInboundAdapterV1.Commit](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/RmaInboundAdapterV1.cs#L119) | `RC` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [RmaService.CreateCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/RmaService.cs#L97) | `RMA` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [SampleStockService.CreateAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/SampleStockService.cs#L55) | `SMP` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [SlottingService.AnalyzeCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/SlottingService.cs#L125) | `SLP` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [StockMovementService.ApplyInternalAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/StockMovementService.cs#L142) | `TXN` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [StockMovementService.ApplyInternalAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/StockMovementService.cs#L168) | pool、movement | StockMovementSequenceV1 · 租户／库存池及库存移动原件 | 无周期 | `KEEP_EXISTING` |
| [StockTakeService.CreatePlanCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/StockTakeService.cs#L128) | `ST` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [ToolRegistrationOwnerV1.ExecuteCommandAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/ToolRegistrationOwnerV1.cs#L175) | `PLT2` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [VmiService.CalculateMonthlyBillingCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/VmiService.cs#L99) | `VMI` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [WcsService.CreateCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/WcsService.cs#L35) | `WCS` | WmsSequenceService · 租户／仓储前缀 | 服务器本地日，显示年月 | `KEEP_EXISTING` |

### HTTP · Erp

| 原业务归属方与入口 | 业务键 | 原分配器与唯一域 | 具体时间输入 | 处置 |
|---|---|---|---|---|
| [OrderController.GetNextSeq（转交原服务）](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.WebApi/Controllers/Erp/OrderController.cs#L37) | `ORD` | DocNumber · 全局功能码 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [PlateMoldController.NextSeq（转交原服务）](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.WebApi/Controllers/Erp/PlateMoldController.cs#L28) | `MLD` | DocNumber · 全局功能码 | 服务器本地日，显示年月 | `KEEP_EXISTING` |
| [ProductController.GetNextSeq（转交原服务）](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.WebApi/Controllers/Erp/ProductController.cs#L42) | `PRD` | DocNumber · 全局功能码 | 服务器本地日，显示年月 | `KEEP_EXISTING` |

### HTTP · Fin

| 原业务归属方与入口 | 业务键 | 原分配器与唯一域 | 具体时间输入 | 处置 |
|---|---|---|---|---|
| [AssetCardController.Create](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.WebApi/Controllers/Fin/AssetCardController.cs#L54) | `FA` | FinSequenceService · 租户／财务业务键 | card.AcquisitionDate | `KEEP_EXISTING` |

### HTTP · Mes

| 原业务归属方与入口 | 业务键 | 原分配器与唯一域 | 具体时间输入 | 处置 |
|---|---|---|---|---|
| [WorkOrderController.GetNextSeq（转交原服务）](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.WebApi/Controllers/Mes/WorkOrderController.cs#L65) | `WO` | MesSequenceService · 全局 MES 键 | 服务器本地日，显示年月 | `KEEP_EXISTING` |

## 调用包装、派生号码与外部显示号

下列原业务归属方的显示码或引用保留在原域，处置均为 `KEEP_EXISTING`。CRM 公开回执的访问凭据与其外部显示号分别按原协议保存。

| 范围 | 原调用入口 | 归属、生成方式和时间依据 |
|---|---|---|
| ERP ↔ CRM | [ErpQuotationOrderFactory](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/ErpIntegration/ErpQuotationOrderFactory.cs#L100) | C03 将原业务来源交回 Main 订单 Owner，复用 ERP 全局 ORD 的原分配和时间语义 |
| ERP | [OrderService.CreateCoreAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Erp/OrderService.cs#L160) | 订单创建调用 ORD 原服务；C03 的 CRM 受注亦走 Main 原订单编号合同 |
| ERP | [OrderService.NextSequenceAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Erp/OrderService.cs#L511) | PostgreSQL 领取入口与新建、补单共享同一全局 ORD |
| ERP | [FscChecklistService.IssueAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Erp/FscChecklistService.cs#L139) | 逐行取配置前缀，空白按 FSC，统一大写和前三字符后共用全局功能码 |
| ERP | [EstimateCalcService.CreateAsync / CopyAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Erp/EstimateCalcService.cs#L98) | 在已保留的 EMC 主号上加 -01，不申请另一个序位 |
| ERP | [QuotationService.CreateAsync / CopyAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Erp/QuotationService.cs#L198) | 在已保留的 QTN 主号上加 -01 |
| ERP | [PlateMoldService.CreatePlateMoldOrderAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Erp/PlateMoldService.cs#L657) | 在原订单号上附加明细序号 -001 |
| FIN | [BudgetService.CreateBudgetAsync](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Fin/BudgetService.cs#L27) | 显示 BUD 与会计年度，序位取自租户 BUD 月度流水，原时间依据保留 |
| FIN | [ApSettlementService / ArSettlementService](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Fin/ApSettlementService.cs#L70) | 以付款／收款原号与发票原号形成差额来源引用 |
| FIN | [ArSettlementService](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Fin/ArSettlementService.cs#L70) | 收款结算的对应原引用 |
| FIN | [CostSettleService](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Fin/CostSettleService.cs#L102) | 原单号附 WIP、FG、VAR 作为三个过账来源 |
| FIN | [FxRevaluationService](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Fin/FxRevaluationService.cs#L37) | 汇兑重估按原会计年月形成 FXREVAL，冲销继续引用其 -REV |
| FIN | [CostCollectionBasisStoreV1](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Fin/CostCollectService.cs#L212) | 同一成本归集单的依据版本，在原单内递增，按原归集单读取 |
| PUR | [FinCostServiceAdapter / SubcontractService](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Pur/Contracts/FinCostServiceAdapter.cs#L30) | 原委外 PO 与行号形成业务来源和成本引用，沿用采购单号归属 |
| PUR | [StubWmsServices](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Pur/Contracts/StubWmsServices.cs#L13) | 模拟入库、出库引用保留各自原业务单号和服务器本地时间；实际仓储号见 WMS 原服务 |
| SPACE | [LocationPublishService](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Space/LocationPublishService.cs#L121) | 发布、停用和重发使用全局 LPB 序位，展示 LPUB、本地日期和原流水 |
| WMS | [LabelJobService 权威路径](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/LabelJobService.Authority.cs#L108) | 标签作业接纳原 operation，显示号由 LBL、UTC 日和 GUID 片段构成；不共享单据流水 |
| WMS | [LabelJobService 旧路径](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/LabelJobService.cs#L122) | 旧标签作业以相同格式保存最初外露 JobNo |
| WMS | [LpnService](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/LpnService.cs#L783) | 库存容器的原移动交易显示号由 S、UTC 日和 GUID 片段构成 |
| WMS | [SerialInventoryService](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/SerialInventoryService.cs#L468) | 序列库存原移动交易号沿用 S、UTC 日和 GUID 片段 |
| WMS | [QcInspectionService](https://github.com/GTX537/CP6/blob/3ea9d2ba2ab4e1a644369432460a5251e2f1bcda/CP6.Core/Services/Wms/QcInspectionService.cs#L230) | 质检缺省批号按服务器本地日与该次行序号派生，沿用来料检验批次 |
| CRM | [CrmInquirySubmissionOwnerV1](https://github.com/GTX537/CP6.CRM/blob/ac8a2706a018317eafce8247aff1e7fe5e416329/src/CP6.CRM.Api/Inquiry/CrmInquirySubmissionOwnerV1.Graph.cs#L28) | 原询盘图写入时保留外部显示号，6 个随机字节转大写十六进制；无日期、时区或重置，同一提交读原图结果 |
| CRM | [CrmBusinessService.Receipt](https://github.com/GTX537/CP6.CRM/blob/ac8a2706a018317eafce8247aff1e7fe5e416329/src/CP6.CRM.Infrastructure/Leads/CrmBusinessService.Intake.cs#L328) | 旧资料的公开显示号取原提交 ID 的前 12 个大写十六进制字符，无日期或重置；继续按该原件读取 |

## 已登记域与切换资格

- 全新 Main 租户编号域由 `NumberAllocationOwnerV1` 登记。业务归属方固定 `ruleId`、租户、BizKey、业务范围、可选法人／仓库、原操作引用与业务时点。时区和起始值随登记版本保存。多个归属方不得以同一既有规则行另开计数器。
- 申请迁入的旧 Main Pub 域由原归属方提供 `INumberCutoverSourceV1`。该接口读取已批准的冻结范围、各周期原水位、逐槽外露号／跳号和未决原操作，按原业务键读回原分配。切换在同一 PostgreSQL 事务内锁住旧规则。已登记域拒绝旧分配器写入，清单或原结果不足时保留已知原件并返回对应状态。
- 本卡的采购示例为模拟归属方，验证使用真实 PostgreSQL 和旧、新两个分配器。采购、工程、ERP、MES、仓储、财务及 CRM 线上业务保持各自原入口，具体接线由对应业务卡负责。Platform `f3fbe4be67d40641f7a8a969d74be65a95541819` 的引用和数字解析不登记为上述业务显示号的另一处分配器。

[规则管理与原键恢复](number-allocation-v1.md) · [SYS-NUM-01 原验收](../design-handbook/modules/SYS-NUM-01.md)
