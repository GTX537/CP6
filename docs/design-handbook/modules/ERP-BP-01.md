# ERP-BP-01 交易伙伴主数据开发设计

> 整理状态：`required_materials_consolidated`。本页已整理全部有效规范、143 个原字段、128 个可写字段和 146 个 JSON 样例的对象语义；重复子树按精确值复用，摘要字面未逐项重算。设计已接受，124 项业务 AC 均 `NOT_RUN`，10 项生产 Owner Gate 均 `UNPROVEN_PRODUCTION_GATE`。本轮没有执行源代码、SQL、迁移或测试。[接受范围][U31] [验证边界][U54]

## 1. 业务目的、操作者与职责

Main ERP 的主数据人员在一个经营主体下登记客户、供应商及结算关系，维护交易资料，办理注册、冻结、停用和恢复；映射管理员核实 CRM Account 与 Main BP 的真实身份关系。Sales/Pur 服务在真实新业务事务中使用 BP，取得绑定该次业务版本的凭证。三个目的不能合并为一个“客户可用”标签。[身份与角色][S34] [交易使用][S872]

四个原 SPEC 分别是客户/供应商维护、交易资料、商务状态、CRM 账户映射。BP 不创建工程需求、报价、订单、Won、采购/库存/财务事实，不负责信用批准或银行账户主账。登记 BP 不要求 BOM；下游在自己的用途边界另核工程、来源、审批和信用条件。[原范围][S23]

CRM Account 是关系经营身份，BP 是 Main 某经营主体的交易身份。一个 BP 可以同时 Customer 和 Supplier；同一外部企业跨 Main 法人需要不同 BP。映射与当前交易资格独立：预登记或冻结 BP 可以保留身份关联，已知 INACTIVE Account 可记录真实关系，但不能由此放行新 CRM 业务。历史单据继续引用当时 BP/Profile/Mapping 版本。[映射事实][S58]

## 2. 当前版本、接受与组合边界

当前正文为 `ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md`，356627 字节、8628 行，SHA-256 `92c20d1ee1dc090fdff5425bba68bf44f67a3f513400bd875c7c1d605d159280`。`UA-20260930-A-ERP-BP-MD01` 接受原字节、全部四 SPEC 和 D-BP01–10。候选内“未接受/待 O1”与 INDEX `Acceptance:false` 是冻结时历史标签，已被准确 UA 覆盖。[原件身份][U12] [覆盖规则][U45]

最终独审明确关闭九组问题：真实候选 criteria 持久化、乱序查询代次、同关系纠错后继、遮罩可空读型、零候选创建关联原子性、真实 ETag 前驱、Unicode 展开、复合创建授权、先历史终态再新写授权。它证明文书设计审核，未证明真实交易参与者或数据库并发。[独审闭合][R30]

正文依赖 OPP `bdc91e…` 与 HO `affc1c…` 的准确接受原件，不新增 CRM 商业效果写者。它引用 ACC 83331 字节 `11aeb11f…` 的七字段映射协议；本次归档选择的 ACC 当前设计是 R01/R02/R03 组合，不能仅凭 BP 中的外部输入称谓替换 ACC 选择。该七字段是独立 DRAFT 的建议新适配协议；当前 ACC R03 还要求 Owner Scope/唯一决定或完整覆盖证明、绑定身份资料版与本地资格。七字段快照本身不能自动满足这些启用证据，两种合同不能混成同一 wire。[历史七字段][ACC925] [当前效力合同][ACCCT32] 整合时按 [CRM-ACC-01](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/CRM-ACC-01.md>) 的有效组合逐项对齐。BP/UA 中 INQ、LEAD Stage10 是 2026-09-30 历史，不覆盖它们各自后续 CURRENT。[保留输入][U47]

## 3. 实体、字段、身份与版本轴

### 身份与状态

|对象/轴|准确身份与规则|
|---|---|
|BP|服务端 UUID `BpId`；身份 `(TenantId,BpId)`；`LegalEntityKey` 创建后不可变，不能以 BaseCd/Tenant/外部法人号替代。FK 同时含 Tenant、BP、LegalEntity。|
|业务号|`BpCd` 为 1–20 ASCII 大写字母/数字/连字符，`UQ(Tenant,BpCd)`，跨法人仍不重复，停用/旧软删后终生不重用。SYS-NUM 只给候选号，最终唯一约束决定。|
|组织|`BaseCd` 1–10，由组织 Owner 证明属于本 LegalEntity。同法人迁移需原、新 Base 管理权及原因；跨法人迁移和 merge 没有入口。|
|13 个角色|Customer、AccountsReceivable、Billing、Receipt、Delivery、Supplier、AccountsPayable、PaymentSchedule、Payment、CreditMgmt、Maker、PaidSupply、RebuyObligation 全部 bool。前九至少一个 true；PaidSupply 要 Supplier，RebuyObligation 要 PaidSupply。首次保存后全部不可改。|
|3 个状态轴|Registration=`PRE_REGISTERED/REGISTERED/UNKNOWN_LEGACY`；Lifecycle=`ACTIVE/RETIRED`；TradingHold=`CLEAR/FROZEN`。资格按目的现算 `KNOWN_ALLOW/KNOWN_DENY/UNKNOWN`，不存全局 `isEligible`。|
|BP Revision|Core/Profile/Relation/Finance/State 的同一聚合版本。每次状态变化也复制完整 Profile 快照，不能 Root 指向不存在的版本。业务 Rev 与物理 rowversion 分开。|
|MappingScope|`(Tenant,AccountId,LegalEntity)` 一个当前 LINKED/UNLINKED 完整头；无权威记录是 UNSEEN、revision=null，不造 rev0。MappingRevision 从 1 单 Owner 递增。|
|反向 Claim|`(Tenant,LegalEntity,BpId)` 至多一个当前 Account；Account 可在不同法人有不同 BP。Mapping 与 Claim 改动原子。|
|投影知识|`ProjectionGeneration`、Job Claim/租约、Main MappingRevision 是独立轴；同一个 Main 版本的不同查询也有新 generation，迟到旧成功不能覆盖新失败。|

[身份来源][S34] [角色/状态][S42] [Mapping][S58] [投影代次][S860]

名称 1–100 UTF-16 代码单元，简称可空最多 10，LegalEntityKey 1–64。`commercialCurrency` 是当前 ISO4217 CurrencyOwner 可用值；旧 null 保留 `LegacyCurrencyUnproven`，不能默补 JPY 回写历史。decimal 使用规范十进制字符串，Rev 使用 1..Int64max 十进制字符串，UTC 毫秒 Z；JSON 拒未知/重复键。Core 可 patch 仅 `bpName/bpAbbrev/baseCd/commercialCurrency`，其余按字段表 W；patch 最大 190 项、同字段不能重复、空 patch 拒绝。[输入与 patch][S108]

### 143 个原字段的准确登记

下表保留原字段到 wire 名、长度/精度与写入 Owner。W 为 128 个 ProfileValues 可写成员，R 是通过独立关系槽表达的原关系字段，F 是 Finance 只读来源，D 为派生显示。代码类必须有当前字段字典，不能把旧默认 P010/CAL01 当已证当前选项。完整 `ProfileValues` 和独立 `ProfileRead` 声明分别从原文 273、410 行开始。[登记规则][S118] [写型][S273] [读型][S405]

|FIELD|原列 → API|类型与上限|来源/写入|显示与校验|
|---|---|---|---|---|
|F001|StdCoCd → stdCoCd|string\|null；nvarchar(20)|W / Main BP|代码选择/文本输入|
|F002|Ein → ein|string\|null；nvarchar(13)|W / Main BP|代码选择/文本输入；敏感权限|
|F003|EinType → einType|string\|null；nvarchar(2)|W / Main BP|代码选择/文本输入；敏感权限|
|F004|LocalPublicCd → localPublicCd|string\|null；nvarchar(5)|W / Main BP|代码选择/文本输入|
|F005|DenzaiNo → denzaiNo|string\|null；nvarchar(9)|W / Main BP|代码选择/文本输入|
|F006|ZipCd → zipCd|string\|null；nvarchar(10)|W / Main BP|代码选择/文本输入|
|F007|Addr1 → addr1|string\|null；nvarchar(40)|W / Main BP|代码选择/文本输入；敏感权限|
|F008|Addr2 → addr2|string\|null；nvarchar(40)|W / Main BP|代码选择/文本输入；敏感权限|
|F009|Addr3 → addr3|string\|null；nvarchar(40)|W / Main BP|代码选择/文本输入；敏感权限|
|F010|Addr4 → addr4|string\|null；nvarchar(40)|W / Main BP|代码选择/文本输入；敏感权限|
|F011|Tel → tel|string\|null；nvarchar(30)|W / Main BP|代码选择/文本输入；敏感权限|
|F012|Fax → fax|string\|null；nvarchar(30)|W / Main BP|代码选择/文本输入；敏感权限|
|F013|AreaCd → areaCd|string\|null；nvarchar(3)|W / Main BP|代码选择/文本输入|
|F014|SalesStaffCd → salesStaffCd|string\|null；nvarchar(20)|W / Main BP|代码选择/文本输入|
|F015|BusinessStaffCd → businessStaffCd|string\|null；nvarchar(20)|W / Main BP|代码选择/文本输入|
|F016|BpClass01 → bpClass01|string\|null；nvarchar(10)|W / Main BP|代码选择/文本输入|
|F017|BpClass02 → bpClass02|string\|null；nvarchar(10)|W / Main BP|代码选择/文本输入|
|F018|BpClass03 → bpClass03|string\|null；nvarchar(10)|W / Main BP|代码选择/文本输入|
|F019|BpClass04 → bpClass04|string\|null；nvarchar(10)|W / Main BP|代码选择/文本输入|
|F020|BpClass05 → bpClass05|string\|null；nvarchar(10)|W / Main BP|代码选择/文本输入|
|F021|BpClass06 → bpClass06|string\|null；nvarchar(10)|W / Main BP|代码选择/文本输入|
|F022|BpClass07 → bpClass07|string\|null；nvarchar(10)|W / Main BP|代码选择/文本输入|
|F023|BpClass08 → bpClass08|string\|null；nvarchar(10)|W / Main BP|代码选择/文本输入|
|F024|BpClass09 → bpClass09|string\|null；nvarchar(10)|W / Main BP|代码选择/文本输入|
|F025|BpClass10 → bpClass10|string\|null；nvarchar(10)|W / Main BP|代码选择/文本输入|
|F026|SalesAnalysis1 → salesAnalysis1|string\|null；nvarchar(10)|W / Main BP|代码选择/文本输入|
|F027|SalesAnalysis2 → salesAnalysis2|string\|null；nvarchar(10)|W / Main BP|代码选择/文本输入|
|F028|SalesAnalysis3 → salesAnalysis3|string\|null；nvarchar(10)|W / Main BP|代码选择/文本输入|
|F029|ParentCustomerCd → parentCustomerCd|string\|null；nvarchar(20)|R / RelationSet派生或旧档|只读文本；无写入口|
|F030|UserConverterDiv → userConverterDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F031|AccountsReceivableCd → accountsReceivableCd|string\|null；nvarchar(20)|R / RelationSet派生或旧档|只读文本；无写入口|
|F032|CreditMgmtCd → creditMgmtCd|string\|null；nvarchar(20)|R / RelationSet派生或旧档|只读文本；无写入口|
|F033|CustomerDept → customerDept|string\|null；nvarchar(50)|W / Main BP|代码选择/文本输入|
|F034|CustomerContact → customerContact|string\|null；nvarchar(50)|W / Main BP|代码选择/文本输入；敏感权限|
|F035|CustomerContactTitle → customerContactTitle|string\|null；nvarchar(20)|W / Main BP|代码选择/文本输入；敏感权限|
|F036|RecyclingTarget → recyclingTarget|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F037|SalesPostingDiv → salesPostingDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F038|SheetSalesCalcMethod → sheetSalesCalcMethod|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F039|SalesCalcDivM2 → salesCalcDivM2|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F040|SalesCalcDivPiece → salesCalcDivPiece|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F041|FractionCalcDiv → fractionCalcDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F042|FullSheetSalesDiv → fullSheetSalesDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F043|SlitterBillingDiv → slitterBillingDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F044|SlitterBillingUnitPrice → slitterBillingUnitPrice|Dec\|null；decimal(18,4)，非负，至多4小数|W / Main BP|数字输入|
|F045|SlitterMaxFlow → slitterMaxFlow|Dec\|null；decimal(18,4)，非负，至多4小数|W / Main BP|数字输入|
|F046|PrintMinBilling → printMinBilling|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F047|PrintMinBillingBelow → printMinBillingBelow|Dec\|null；decimal(18,4)，非负，至多4小数|W / Main BP|数字输入|
|F048|PrintMinBillingUnit → printMinBillingUnit|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F049|LaminateMinBilling → laminateMinBilling|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F050|LaminateMinBillingBelow → laminateMinBillingBelow|Dec\|null；decimal(18,4)，非负，至多4小数|W / Main BP|数字输入|
|F051|LaminateMinBillingUnit → laminateMinBillingUnit|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F052|LaminateAddRate → laminateAddRate|Dec\|null；decimal(18,4)，非负，至多4小数|W / Main BP|数字输入；敏感权限|
|F053|LaminateAddDisplay → laminateAddDisplay|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F054|ProcessingMinEstimate → processingMinEstimate|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F055|NewSheetUnitPriceBase → newSheetUnitPriceBase|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F056|DeliverySlipOutDiv → deliverySlipOutDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F057|DeliverySlipIssueDiv → deliverySlipIssueDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F058|SpecialSlipDiv → specialSlipDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F059|NightLoadDiv → nightLoadDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F060|SizePrintDiv → sizePrintDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F061|DeliveryCalcOutDiv → deliveryCalcOutDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F062|DeliveryCalcIssueDiv → deliveryCalcIssueDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F063|DeliveryCalcOutDiv2 → deliveryCalcOutDiv2|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F064|DeliveryCalcAddressee → deliveryCalcAddressee|string\|null；nvarchar(100)|W / Main BP|代码选择/文本输入；敏感权限|
|F065|DeliveryCalcSender → deliveryCalcSender|string\|null；nvarchar(50)|W / Main BP|代码选择/文本输入|
|F066|DeliveryCalcZipCd → deliveryCalcZipCd|string\|null；nvarchar(10)|W / Main BP|代码选择/文本输入|
|F067|DeliveryCalcAddr1 → deliveryCalcAddr1|string\|null；nvarchar(40)|W / Main BP|代码选择/文本输入；敏感权限|
|F068|DeliveryCalcAddr2 → deliveryCalcAddr2|string\|null；nvarchar(40)|W / Main BP|代码选择/文本输入；敏感权限|
|F069|DeliveryCalcAddr3 → deliveryCalcAddr3|string\|null；nvarchar(40)|W / Main BP|代码选择/文本输入；敏感权限|
|F070|DeliveryCalcAddr4 → deliveryCalcAddr4|string\|null；nvarchar(40)|W / Main BP|代码选择/文本输入；敏感权限|
|F071|BillingCd → billingCd|string\|null；nvarchar(20)|R / RelationSet派生或旧档|只读文本；无写入口|
|F072|BillingName → billingName|string\|null；nvarchar(100)|D / 关系/目录名称|只读文本；无写入口|
|F073|ReceiptCd → receiptCd|string\|null；nvarchar(20)|R / RelationSet派生或旧档|只读文本；无写入口|
|F074|ReceiptName → receiptName|string\|null；nvarchar(100)|D / 关系/目录名称|只读文本；无写入口|
|F075|CreditMgmtArCd → creditMgmtArCd|string\|null；nvarchar(20)|R / RelationSet派生或旧档|只读文本；无写入口|
|F076|BillingClosingDay1 → billingClosingDay1|number\|null；Int32非负|W / Main BP|数字输入；仅1..31或99，99=末日|
|F077|BillingPrintDiv → billingPrintDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F078|BillingSealDiv → billingSealDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F079|ElectronicBilling → electronicBilling|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F080|BillingAddressee → billingAddressee|string\|null；nvarchar(100)|W / Main BP|代码选择/文本输入；敏感权限|
|F081|BillingSender → billingSender|string\|null；nvarchar(50)|W / Main BP|代码选择/文本输入|
|F082|BillingZipCd → billingZipCd|string\|null；nvarchar(10)|W / Main BP|代码选择/文本输入|
|F083|BillingAddr1 → billingAddr1|string\|null；nvarchar(40)|W / Main BP|代码选择/文本输入；敏感权限|
|F084|BillingAddr2 → billingAddr2|string\|null；nvarchar(40)|W / Main BP|代码选择/文本输入；敏感权限|
|F085|BillingAddr3 → billingAddr3|string\|null；nvarchar(40)|W / Main BP|代码选择/文本输入；敏感权限|
|F086|BillingAddr4 → billingAddr4|string\|null；nvarchar(40)|W / Main BP|代码选择/文本输入；敏感权限|
|F087|RemittanceName → remittanceName|string\|null；nvarchar(100)|W / Main BP|代码选择/文本输入；敏感权限|
|F088|BankCd → bankCd|string\|null；nvarchar(20)|F / FinanceReference|只读文本；无写入口；敏感权限|
|F089|BankBranchCd → bankBranchCd|string\|null；nvarchar(20)|F / FinanceReference|只读文本；无写入口；敏感权限|
|F090|RemittanceAccount → remittanceAccount|string\|null；nvarchar(50)|F / FinanceReference|只读文本；无写入口；敏感权限|
|F091|TempAccountDiv → tempAccountDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F092|MainAccountRegDate → mainAccountRegDate|DateOnly\|null；YYYY-MM-DD|F / FinanceReference|只读文本；无写入口|
|F093|Drawer → drawer|string\|null；nvarchar(50)|W / Main BP|代码选择/文本输入；敏感权限|
|F094|CollectionDiv → collectionDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F095|CollectionPlannedDay → collectionPlannedDay|number\|null；Int32非负|W / Main BP|数字输入；仅1..31或99，99=末日|
|F096|BillRotationDiv → billRotationDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F097|ReceiptAddressee → receiptAddressee|string\|null；nvarchar(100)|W / Main BP|代码选择/文本输入；敏感权限|
|F098|ReceiptSender → receiptSender|string\|null；nvarchar(50)|W / Main BP|代码选择/文本输入|
|F099|ReceiptZipCd → receiptZipCd|string\|null；nvarchar(10)|W / Main BP|代码选择/文本输入|
|F100|ReceiptAddr1 → receiptAddr1|string\|null；nvarchar(40)|W / Main BP|代码选择/文本输入；敏感权限|
|F101|ReceiptAddr2 → receiptAddr2|string\|null；nvarchar(40)|W / Main BP|代码选择/文本输入；敏感权限|
|F102|ReceiptAddr3 → receiptAddr3|string\|null；nvarchar(40)|W / Main BP|代码选择/文本输入；敏感权限|
|F103|ReceiptAddr4 → receiptAddr4|string\|null；nvarchar(40)|W / Main BP|代码选择/文本输入；敏感权限|
|F104|CollectionNote → collectionNote|string\|null；nvarchar(200)|W / Main BP|代码选择/文本输入；敏感权限|
|F105|LogisticsGroupCd → logisticsGroupCd|string\|null；nvarchar(10)|W / Main BP|代码选择/文本输入|
|F106|LogisticsGroupName → logisticsGroupName|string\|null；nvarchar(100)|D / 关系/目录名称|只读文本；无写入口|
|F107|DeliveryDept → deliveryDept|string\|null；nvarchar(50)|W / Main BP|代码选择/文本输入|
|F108|DeliveryContact → deliveryContact|string\|null；nvarchar(50)|W / Main BP|代码选择/文本输入；敏感权限|
|F109|DeliveryContactTitle → deliveryContactTitle|string\|null；nvarchar(20)|W / Main BP|代码选择/文本输入；敏感权限|
|F110|TruckLengthLimit → truckLengthLimit|Dec\|null；decimal(10,2)，0..99999999.99|W / Main BP|数字输入|
|F111|DeliveryTimeFrom → deliveryTimeFrom|string\|null；nvarchar(8)|W / Main BP|代码选择/文本输入；HH:mm:ss，跨日须显式来源规则|
|F112|DeliveryTimeTo → deliveryTimeTo|string\|null；nvarchar(8)|W / Main BP|代码选择/文本输入；HH:mm:ss，跨日须显式来源规则|
|F113|PlannedShipTime → plannedShipTime|string\|null；nvarchar(8)|W / Main BP|代码选择/文本输入；HH:mm:ss，跨日须显式来源规则|
|F114|SupplierPattern → supplierPattern|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入；1/2/3/4/5/9|
|F115|SubcontractTargetFlg → subcontractTargetFlg|boolean；bool|W / Main BP|开关|
|F116|SubcontractPriceDiv → subcontractPriceDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F117|SupplyPostingDiv → supplyPostingDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F118|SupplyPriceChangeAllowFlg → supplyPriceChangeAllowFlg|boolean；bool|W / Main BP|开关|
|F119|DeliveryConfirmFlg → deliveryConfirmFlg|boolean；bool|W / Main BP|开关|
|F120|PurchaseFractionDiv → purchaseFractionDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F121|PurchaseTaxFractionDiv → purchaseTaxFractionDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F122|PurchaseTaxCd → purchaseTaxCd|string\|null；nvarchar(10)|W / Main BP|代码选择/文本输入|
|F123|PurchaseTaxPriorityFlg → purchaseTaxPriorityFlg|boolean；bool|W / Main BP|开关|
|F124|SupplierCalendarCd → supplierCalendarCd|string\|null；nvarchar(10)|W / Main BP|代码选择/文本输入|
|F125|SupplyConsignDiv → supplyConsignDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F126|PurchaseLotSplitDiv → purchaseLotSplitDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F127|PurchasePostingDiv → purchasePostingDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F128|PaidSupplyFractionDiv → paidSupplyFractionDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F129|PaidSupplyTaxCd → paidSupplyTaxCd|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F130|PaidSupplyTaxPriorityFlg → paidSupplyTaxPriorityFlg|boolean；bool|W / Main BP|开关|
|F131|PaidSupplyAmountFractionDiv → paidSupplyAmountFractionDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F132|PaidSupplyTaxFractionDiv → paidSupplyTaxFractionDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F133|PaidSupplyTaxCalcDiv → paidSupplyTaxCalcDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F134|FscCertificationDiv → fscCertificationDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F135|PaymentScheduleCd → paymentScheduleCd|string\|null；nvarchar(20)|R / RelationSet派生或旧档|只读文本；无写入口|
|F136|PaymentCd → paymentCd|string\|null；nvarchar(20)|R / RelationSet派生或旧档|只读文本；无写入口|
|F137|PaymentScheduleDeptCd → paymentScheduleDeptCd|string\|null；nvarchar(20)|W / Main BP|代码选择/文本输入|
|F138|PaidEachTimeFlg → paidEachTimeFlg|boolean；bool|W / Main BP|开关|
|F139|PaymentClosingDay1 → paymentClosingDay1|number\|null；Int32非负|W / Main BP|数字输入；仅1..31或99，99=末日|
|F140|PaymentTaxCalcFlg → paymentTaxCalcFlg|boolean；bool|W / Main BP|开关|
|F141|PaymentTaxFractionDiv → paymentTaxFractionDiv|string\|null；nvarchar(4)|W / Main BP|代码选择/文本输入|
|F142|BatchPaymentScheduleFlg → batchPaymentScheduleFlg|boolean；bool|W / Main BP|开关|
|F143|PaymentScheduleDelayDays → paymentScheduleDelayDays|number\|null；Int32非负|W / Main BP|数字输入|

所有字符串去首尾空白，空串变 null，必填因此报错；拒 NUL/control。名称/注释内部空白保留，候选规范键另存。自由文本限名称/部门/联系人/职衔/抬头/发送者/地址、CollectionNote、电话/传真/邮编等列明字段；Code/Cd/Div/Class/Analysis/Pattern/Target/Method/Unit 使用封闭字典。Dec 不按币种自行四舍五入；日为 1..31 或 99（月末规则），DelayDays 0..366；时间 HH:mm:ss，from/to 同填或同空，from>to 拒绝，不能猜跨日。邮编首版仍日本 7 位或 3-4 校验，其他国家格式未扩展；电话允许数字、空格、+、-、括号。[规范与字典][S266] [精确范围][S562]

### 九个关系槽与完整性

|槽|发起角色 → 目标所需角色|是否必填|
|---|---|---|
|CUSTOMER_AR|Customer → AccountsReceivable|是|
|AR_BILLING|AccountsReceivable → Billing|是|
|BILLING_RECEIPT|Billing → Receipt|是|
|CUSTOMER_CREDIT|Customer → CreditMgmt|否，填则核|
|BILLING_CREDIT_AR|Billing → AccountsReceivable+CreditMgmt|否|
|PARENT_CUSTOMER|Customer → Customer|否，禁止 SELF/环|
|SUPPLIER_AP|Supplier → AccountsPayable|是|
|AP_SCHEDULE|AccountsPayable → PaymentSchedule|是|
|SCHEDULE_PAYEE|PaymentSchedule → Payment|是|

每次完整 RelationInput 九槽都在，target=null 表示未填；目标是 SELF 或 BpId，SELF 只在本 BP 已有全部目标角色时成立，创建时解析为真实新 BpId。所有外部目标同 Tenant/LegalEntity；至多 64 个不同 BP，父链最多 32，闭包按 `(BpId,RequiredRole)` 去重。旧 `PaymentScheduleCd` 被 Supplier/AP 两种用途重载，必须分别证明 SUPPLIER_AP 与 AP_SCHEDULE，不能复制同一个旧列猜两个关系。[关系规则][S542]

注册完整性：Customer 需 CUSTOMER_AR 与 salesStaffCd/businessStaffCd；AR 需 AR_BILLING；Billing 需 BILLING_RECEIPT；Delivery 需 logisticsGroupCd；Supplier 需 supplierPattern、SUPPLIER_AP、purchaseFractionDiv/purchaseTaxFractionDiv/supplyConsignDiv/purchaseLotSplitDiv；AP 需 AP_SCHEDULE；PaymentSchedule 需 SCHEDULE_PAYEE/paymentScheduleDeptCd，非逐次付款还需 closing day，计算税还需 tax fraction，批量计划还需 delay days。PaidSupply 需三项 amount/tax fraction/tax calc，RebuyObligation 再需 tax code。SupplierPattern 只允许 1/2/3/4/5/9；非 5 时外注标志 false 且外注专用字段 null；Supplier 的 deliveryConfirmFlg 固定 true，历史 false 必须明确修正后才能新采购使用。[角色完整性][S560]

### 持久化聚合

`main_bp` 是建议 schema，已有等价实体必须一对一采用，保留旧 `T_WebBusinessPartner` Id/BpCd/历史外键，不能建平行主账。核心为 BpRoot、BpProfileCurrent/Revision、BpRelationCurrent、BpFinanceLink、BpNormalizedIdentity、BpLegacySnapshot、BpStateHistory；控制为 ScopeGuard、Preview、Command、AccountObservation、MappingContext/CorrectionContext/DecisionBinding；映射为 Slot/Claim/Revision/Observation/Outbox/DeliveryAttempt/ProjectionObservation/ReconcileJob；下游证据为 UseReceipt/Audit/Conflict。[完整物理责任][S933]

所有 PK/FK 带 Tenant，无历史级联删除；可变控制用真实 rowversion，JSON 带封闭 schema 校验，hash 保留 schemaVersion 和可恢复原体。Root/Profile/Relation Revision 一致；所有历史版本只追加。Profile 可用类型化列或一份完整受约束 JSON，须选择唯一权威，不能两份各自可写。Preview/Account/Conflict/Trust 等完整敏感原体加密，不能只存不可逆摘要。[聚合一致性][S940] [表约束][S966]

## 4. 正常流程、状态与事务内写集

### 建档与资料维护

B01 读取受权 Base/mode/字段字典 → B05 传完整输入和候选决定 → B06 以准确 PreviewRef、ETag、CommandId 执行。预览不占 BpCd/BpId，不写主数据或 Outbox。执行在当前 IAM/组织/字段共同门、Scope X 和 Command 保护中核复合授权、原预览及当前完整候选；任一变化要求重读。`USE_EXISTING` 只写 Command NO_CHANGE 和审计，不修改既有 BP、不建立映射；`NEW_DISTINCT` 一次分配 ID，Root/Profile1/初始状态/关系/Finance 引用/归一索引/Audit/Command 同提交，无默认映射/订单/采购事件。[创建算法][S747]

预登记允许角色必填资料不全，已填值仍须类型/租户/字段合法；REGISTERED 必须当前全角色完整性和 Owner 引用可证。B08 只对当前明确 Revision 的服务器快照应用用户明确 patch，未列遮罩 null 不清原值；B09 同事务写新 ProfileRevision/根指针/关系/归一索引/Audit/Command/ScopeRevision。冻结和停用时可受权修资料，但不恢复状态；无语义差分 NO_CHANGE，不生成版本。更币种不改旧订单币种或汇率。[维护算法][S753]

### 状态动作

|动作|前置与变化|保留的独立事实|
|---|---|---|
|REGISTER|ACTIVE、注册权、完整性/字典/依赖已证，PRE_REGISTERED→REGISTERED；UNKNOWN_LEGACY 不走普通动作|不解除冻结，不创建映射；已正式同版 NO_CHANGE|
|FREEZE/UNFREEZE|状态权与原因，CLEAR↔FROZEN|不注册、不恢复 RETIRED、不替 Finance 解除信用限制|
|RETIRE|准确版本与原因，ACTIVE→RETIRED|不删历史、不释放号码、不发 UNLINKED|
|RESTORE|RETIRED；UNKNOWN_LEGACY 必须 confirmRegistration=true 和 register 权，形成当次新注册事实|Hold 保留；预登记可保留；不补造旧注册历史|

状态变化、完整新 Profile 快照、Root/Audit/Command/ScopeRevision 同事务。冻结/停用不依赖 CRM/Finance 在线，但仍要求 Main 当前权限和 Scope 采用可证。旧 Status0/1 分别映射预登记/正式 ACTIVE；Status9/IsDeleted 为 RETIRED，注册史无证保留 UNKNOWN_LEGACY。缺法人的旧行 LegacyReadOnly，不能猜法人开启新写。[状态表][S765] [旧状态][S56]

### 映射、零候选建档关联与更正

B15 用实际 AccountId 取得不可变身份原件与 Trust → B16 保存完整 criteria/候选/Head/Actor/Tenant/法人/期限 → B17 保存不可变预览 → B18 同事务执行。B16 的 Context 必须可从数据库恢复完整 criteria，不能从 hash 或所选 BP 反猜；两浏览器上下文不能混用。[前置证据][S783]

|动作|准确允许范围|写集与后继|
|---|---|---|
|LINK|UNSEEN/UNLINKED → 同法人 ACTIVE Customer BP；可预登记/冻结；真实 Account 身份与完整候选|首次 1 或旧+1；取得 Claim，Revision/Slot/原事件/Outbox/审计/Command 同事务|
|CREATE_AND_LINK|真实完整空候选，NEW_DISTINCT/acknowledged=[]，完整 newPartner 与 criteria 一致，targetBpId=null|真实事务内首次分配 BP；BP/Profile/九关系/索引/Mapping/Claim/Outbox/Command 同提交，Scope 仅增一次；任何失败无孤立 BP|
|REPLACE|当前 LINKED，目标不同；精确旧头和有权 DecisionEvidence|旧 Claim 释放、新 Claim 取得同事务；占用冲突整批回滚，不半解绑|
|UNLINK|当前 LINKED，target=null，准确决定凭证|旧+1 完整 UNLINKED，释放本 Scope Claim；不删 BP/历史单据|
|CONFIRM|目标与当前 LINKED/UNLINKED 完全相同且当前真实关系可证|关系修订不增；新 Observation/Event/sourceAsOf，Command NO_CHANGE；旧 body 不改|
|CORRECT_CURRENT|Main 已解释当前同版本异义，全部原体/Trust/发行审计/适用冲突覆盖齐备|保持真实当前关系，唯一后继 r+1，推进 Claim 修订，精确关闭已覆盖冲突；不假 UNLINK 再 LINK|

[动作条件][S793] [同事务算法][S801] [有据更正][S814]

REPLACE/UNLINK 必须有 B25 真正读取并核用途的 Binding，Subject 绑定 Account/法人/旧 MappingRevision/新 BP/原因，不能把文件名或客户端 hash 当决定批准。CORRECT_CURRENT 再要求 B27 当前全部适用 OPEN 冲突原体/Trust/coverage、Main canonical event 和发行纠错审计；缺任何项或 CRM 头高于 Main 时 UNPROVEN。最终 Scope X 内重比 Main 头、ProjectionGeneration、每个冲突 recordVersion/hash/完整覆盖；新增适用冲突使上下文失效，不能部分清。更正后投影仍 UNKNOWN，需新代次读回准确 r+1 才显示匹配。[纠错细则][S826] [证据绑定][S972]

## 5. API、事件与跨 Owner 契约

公共根 `/api/main/v1/bp`，全部 no-store，人类会话按平台 CSRF/Origin 保护。body 不接收 Tenant；当前 Tenant/Actor/组织来自可信服务。以下是设计路径，不能视为已部署。[HTTP 前置][S658]

|ID|准确入口（相对公共根，另标内部）|输入 → 输出/用途|
|---|---|---|
|B01–04|GET `/options?legalEntityKey`；POST `/query`；POST `/export`；GET `/{bpId}`|OptionsResult；QueryInput→QueryResult/CSV；DetailResult+BP-CAS ETag|
|B05–09|POST `/create-previews`；POST `/create`；GET `/{bpId}/edit-context`；POST `/profile-previews`；POST `/profile-apply`|Create/ProfilePreviewInput→PreviewResult+ETag；ApplyInput→CommandResponse；EditContext|
|B10–14|GET `/{bpId}/status-context`；POST `/register`、`/hold`、`/retire`、`/restore`|StatusContext+ETag；对应状态 Input→CommandResponse|
|B15–20|POST `/account-probes`、`/mapping-contexts`、`/mapping-previews`、`/mapping-apply`；GET `/mapping?legalEntityKey&accountId`；POST `/mapping-reconcile`|身份观察、持久上下文、预览、命令、MappingReadResult、对账任务命令|
|B21–24|POST `/history`；GET `/commands/{commandId}`；POST `/related-candidates`；GET `/mapping-history?legalEntityKey&accountId&beforeRevision&limit`|HistoryResult、CommandRead、RelatedResult、精确历史分页|
|B25–27|POST `/mapping-decision-bindings`；GET `/reconcile-jobs/{jobId}`；POST `/mapping-correction-contexts`|真实用途绑定、任务当前状态、同版冲突纠错证据上下文|
|I01|POST `/internal/main/v1/bp/use-preview`|UsePreviewInput→Eligibility，isExecutionPermit=false|
|I02|`BpUseParticipant.Enlist(tx, verifiedRequest)`|UseApplyInput→UseReceipt，仅同实际 DB/连接事务函数；无远程 HTTP 写口|
|I03|POST `/internal/main/v1/bp/mapping-snapshots`|MappingSnapshotQuery→已存当前完整快照与原 Event 字节；无记录 UNSEEN|

[完整路由/能力表][S664] [封闭 DTO][S568]

写命令只有 B06/09/11–14/18/20，Header `Idempotency-Key=UUID`；其他 POST 是查询/准备，不占业务命令域。B06/09/18 使用真实 Preview ETag；B11–14 需 body expectedRevision 与真实 BP-CAS 强 ETag 一致；B20 明确只用 body expectedMappingRevision，If-Match 必须省略。准备读 B05/08/17/25 不接业务 If-Match。ETag 格式 `"BP-CAS/1:<BpId>:<Revision>:<8byte-rowversion-base64>"` 和 `"BP-PREVIEW/1:<PreviewId>:<IntentHash>"`，拒弱 tag/*；最终仍需真实 rowversion CAS UPDATE。[并发协议][S660] [ETag 表示][S1024]

映射出站唯一 body 是 `{eventId,accountId,legalEntityKey,mappingRevision,state,businessPartnerKey,sourceAsOf}` 七字段，POST `/internal/crm/v1/account-erp-mappings`。LINKED 的 businessPartnerKey 是实际 BpCd 字符串，UNLINKED=null；Tenant 在受信通道，不能加 body Tenant/BpId/Producer/Hash。C03 ResultVersion 不得改名为 MappingRevision。BodyHash 是一次序列化原 UTF-8 hash；业务比较为 Account/法人/MappingRevision/state/BPkey 五元组，排除 Event/time；Main 重算并逐字段比较 CRM 原观察，hash 不是签名。[准确七字段][S844]

Required Owner 适配器：Directory.Read 供预览；CurrentReferenceParticipant.Enlist 在实际事务中持真实 Owner 共同门直至提交；Finance 只供银行/信用来源引用，不能替信用批准；CRM AccountIdentityAdapter 存完整原体/ReceivedTrust，只显示最小 Observation；ProjectionReader 精确读 ACC `(T,A,L)` 当前投影；纠错桥 POST `/internal/crm/v1/account-erp-mapping-conflicts/read` 给完整两份冲突原文与 coverage。返回漏项、重复项或来源不一致均 UNPROVEN；不能用 TTL、缓存水位、客户端“valid”代真实参与者。[Owner 读取/参与][S699] [冲突桥][S826]

## 6. 事务、幂等、并发与恢复

### 当前资格与冻结竞争

本域所有 BP/状态/映射/归一索引写者持 `BpScopeGuard(T,L)` X 锁；Sales/Pur 新交易持 S 锁直至同实际数据库事务提交。锁序为当前 IAM/组织/字段门 → Scope → Command → 按 AccountId 排 MappingScope → 按 BpId 排 ReverseClaim → BpRoot → 从表 → 下游业务根。一个命令一个法人，短事务不做未知网络调用；锁等待超 2 秒 TECHNICAL_RETRY，同原键回查。[共同门][S76]

I02 核 purpose、BP 当前 ACTIVE/REGISTERED/CLEAR、当前完整角色闭包/代码/币种/真实 Owner 依赖。SALES 需 Customer；传 accountId 时还需当前 LINKED 精确 BP 和 MappingRevision、无 Owner 流冲突。真实无 CRM 来源的 Sales 可 accountId=null；PURCHASE 必须 accountId=null 且 Supplier，不要求 CRM 客户映射。信用/工程/来源准入仍由 Sales/Pur 各自核。UseReceipt 与真实下游版本/审计/Command/Outbox 共同 commit，任何回滚都无凭证。跨库没有共享锁证明时 `OWNER_PARTICIPANT_UNPROVEN`，不能先 HTTP 发许可再尽力保存。[新交易采用][S872]

`(T,consumerOwner,useKey)` 幂等完整输入；另有 `(T,consumerOwner,id,version,purpose)` 唯一意图约束，换 useKey 也不能再消耗一次准入。原同指纹重放只证明当时动作，即使今天冻结也保留原凭证；下一 Confirm/Release 必须真实新 ConsumerRevision/目的并重验。[UseReceipt 身份][S878]

### 先识别已完成命令，再授权新写

先验证当前身份/Tenant/Actor 并解析完整 fingerprint，可安全快读原终态。快读未见不能先以撤写权拒绝：进入 IAM → Scope → Command 有序事务，再查终态；已完成 APPLIED/NO_CHANGE/REJECTED 按当前 command-result.read 与披露政策回原 receipt，不再要求 create/register/mapping 或未过期 Preview。只有仍无终态的新意图才执行完整当前写授权与业务前置。这个顺序必须在路由中间件层保留，避免统一 RequirePermission(write) 提前拦掉恢复。[R3 顺序][S886]

POST 重放限原 Actor、同 operation/path/body/IfMatch；其他 Actor 的独立审计读权不授权代 POST。存在性不可见先统一 404；有权准确同键异指纹 409，不重建。当前结果读权或敏感披露不足 403，零新效果，不改原 ReceiptJson/Hash 造“脱敏历史”。当前身份失效仍先拒绝。原文标准 Receipt 的私有内容不能安全返回时采用更严格的 §11.0 拒披露规则，不能凭 §11.1 的概要措辞改写已存回执。[查询与披露][S890]

Command 键 `(Tenant,CommandId)`，fingerprint 覆盖 Actor/operation/path/L/IfMatch/body（含原因、预览及预期版本），不含 transport time/correlation。占位与业务同事务，无单独永久 PROCESSING；可证明业务拒绝可以无副作用事务记 REJECTED，鉴权/JSON 错误不占键。技术错误全回滚。B22 NOT_OBSERVED 固定 noEffectProven=false，不能视为取消；继续原 key/body/IfMatch。最小幂等墓碑保留至原业务历史可重放期限，清 PII 不释放键。[命令恢复][S898]

### 投递与当前投影知识

Outbox 30 秒租约与 Claim CAS；原 Event/body/hash 重试，退避 1/5/30/120/600 秒，之后 600，20 次 DEAD。有权 B20 重启原 Event，不能改 EventId/sourceAsOf。2xx 仅 ACKED_PROCESSING，可能旧版本被忽略；401/403 待身份修复，404 Account 待 Owner 核实不得补建，409 保存原体与响应证据隔离，不改 body 硬重试。迟到旧 Worker 不能覆盖新租约状态。[投递][S852]

当前投影必须由独立真实只读桥证实：五元组相同且无冲突才 MATCHES_CURRENT，较低 BEHIND，同版异义/冲突 CONFLICT，较高 AHEAD 并暂停自动修复，无证 UNKNOWN/NOT_CONFIGURED。每次查询入队先在同槽 generation+1 并置 UNKNOWN；Main 头变化、当前适用可信冲突/失证也推进代次。安装结果同时核 MainRevision/SemanticHash、Slot generation、当前 Job Claim/租约；Q1 迟到成功不能覆盖 Q2 失败，即使 Main 始终 r12。旧响应可留历史/Job COMPLETE，但 currentApplicable=false。[代次安装][S858]

OwnerConflict 不能由普通成功查询或 CONFIRM 清除；只能准确有据更正覆盖当前冲突，再新代次读后继。I03 返回当前修订最高已提交 ObservationSequence 对应的一份原 Event 字节；无槽不发 UNLINKED，查询不制造新 Event。B20 Job 完成、投递 ACK、当前投影匹配分开展示，不重跑 Main 决定。[当前冲突/补偿][S864]

## 7. 权限、租户与敏感资料

当前授权至少有 allowedLegalEntities、readBaseCds/manageBaseCds、capabilities、fieldRead/Write、permissionRevision/currentValidity。可见对象无动作权 403，不可见对象 404；不能因 Admin 跳过检查。普通新建和 CREATE_AND_LINK 使用同一个 AuthorizeCreateIntent：read+create、所选 Base 管理权、Core/关系/Finance 引用及全部 128 Profile 成员写权（null/false 也不豁免），敏感 write 还需 read；REGISTERED 再需 register；创建关联再交 mapping 权。普通 LINK 既有 BP 不额外要求未发生的 create。[权限基础][S68] [复合授权][S722]

当前撤权与执行共用真实门：撤权先取得门则新写拒绝、零 Root/Mapping/Claim/Outbox；执行先持门提交则只留这一次真实历史，不追删、不授权下一动作。旧按钮、canCreate、Preview permissionRevision 不能替最终门。[撤权竞争][S733]

敏感信息含注册号、电话/传真/地址、联系人、银行/汇款、Drawer/CollectionNote 及相关审计，字段表还明确 laminateAddRate 等字段敏感条件。读型 ProfileRead 保留全部成员，遮罩值 null 并列 redactedFields，隐藏 bool 不是 false；写型非 nullable bool 仍拒 null。组成字段被遮罩时公开 BpRef.contentHash=null，防低熵 bool 哈希推断；内部规范体和 UseReceipt 不因此放宽。Core 最小身份/角色/状态不能合法显示时整对象 404。[遮罩/摘要][S74]

候选、错误、日志不泄露隐藏 BP 名称/数量/银行号。Detail、历史、导出、命令结果每次按当前字段政策。原完整 Preview/Account/Trust/冲突证据加密；fixture 与生产身份、存储、队列和唯一域隔离，DEMO proof 不能入生产 Verifier。[日志与留存][S900] [隔离][S1032]

## 8. 页面与服务端校验

保留 Main `MSBBPA110` 详情、`MSBBPA120` 列表及 `/business-partner`、`/business-partner-list`；子页签受相同 Function 资源授权，导航用已登记 MainNavigation，不接任意返回 URL。[页面入口][S88]

|页面|用户操作与必须看见的状态|
|---|---|
|P01 列表|必选经营主体，角色 OR、多状态/编号/名称/员工/分类/日期筛选，稳定排序分页；角色/状态空集表示不限定，仍受权限。|
|P02 详情/建档|创建时选 13 角色，保存后不可改；按角色页签显示完整字段、缺项、非适用历史值和敏感遮罩；保存先预览候选/差分，再明确确认。|
|P03 状态|三轴分别显示，按 purpose 展示准入理由；Unknown 不显示可用，恢复不代解除冻结。旧 Delete 以 RETIRE 呈现历史保留。|
|P04 CRM 映射|真实 Account 查找、法人槽/历史、候选、版本/asOf、投递/当前投影分栏；Main 已提交与 CRM 待证明确分开；无 MAX+1 或自动 merge 按钮。|
|P05 历史|不可变 Profile/状态/映射版本、UseReceipt、当时凭证与当前状态并排；当前字段裁剪，不以当前状态改原 receipt。|

[页面表][S92]

超时锁重复点击，保原 CommandId/Preview/body/ETag 回查 B22；UNKNOWN 不能换键新建。409/412 保留仍有权草稿并显式比较最新，不自动覆盖；撤权清客户端敏感草稿。页大小 1–100，history limit 1–100，classFilters 仅 bpClass01..10 无重复，Base 最多 50 个且都有读权。预览 10 分钟，字段改变必须重预览。[交互恢复][S100] [查询限制][S656]

导出当前受权全部筛选命中最多 5000 行，超限 422、不能截断；单次 SNAPSHOT 后结束事务。UTF-8 BOM CSV、CRLF、RFC4180，公式首字符 =+-@ 和制表符作文本转义，记录策略版本/asOf；不只导出当前页，不改变 BP。[导出][S104]

## 9. 实施顺序与固定源码差异

原规范引用固定 Main `157630594e3371fe181955d2f6227ff3b6962c84` 的 `ErpCommerceAuthority.cs`（blob `2164d8017cf18df8de897f334ce295db75499096`）与 `ErpCommerceController.cs`（blob `3fd6c28bc14d31453314e2a96d006372b1baa21b`）。旧 `PUT /api/business-partners/{key}/commerce-profile` 已真实维护 CrmAccountId/Currency/IsFrozen，强制 8 字节 RowVersion；旧关系不可换/解除，另一 BP 同 Account 报 C03_ACCOUNT_ALREADY_BOUND。不能说旧系统没有商务资料写者。本轮未重新审计今日代码，这些是固定原文源码观察。[固定旧写者][S986]

建议按已确定的依赖次序实施：

1. 证明旧行 Tenant/法人/不可重用号码、13 角色、旧币种/Status9、重载关系槽的真实采用；保存原快照，重建完整 Unicode15 归一索引和覆盖证明。
2. 明确旧 CRUD/commerce-profile/C03/导入/批作业唯一写路径；共同 IAM/Scope/字段门可证后再开新写。旧缺 RowVersion 请求拒绝，旧 ResultVersion 不升级映射版本。
3. 实现唯一 BP 聚合、128 字段/9 槽约束、不可变版本、Preview/Command 和先终态后新写顺序；接通真实 Directory/Finance 引用参与者。
4. 实现 Account 原身份读取、Mapping/Claim 原子写集、精确七字段 Outbox、冲突原件桥和 generation 对账；覆盖 CREATE_AND_LINK 全回滚与 12→13 更正→14 真实解除。
5. Sales/Pur 在各自真实 DB 事务接入 I02；验证冻结竞争与幂等意图约束；最后接页面/导出/历史，按下节实际验收。

[旧入口采用表][S990] [聚合设计][S933] [最终交易参与][S84]

以上是开发文档的实施依赖分析，不宣称 Gate 已闭合。未配置 CRM 不阻合法本地预登记/历史读；未闭合依赖只阻相应真实动作，Freeze/Retire 不应被无关 CRM/Finance 离线阻塞。[失败边界][S1001]

## 10. 验收与当前证据状态

124 项均 NOT_RUN。146 JSON/263 独立文书检查是历史静态证据，不能回填数据库、权限、恢复或 UI PASS。[原验收说明][S8490] [独审验证范围][R46]

|验收组|准确范围与必须观察的结果|
|---|---|
|身份/创建|AC001–015：跨法人同号拒绝；停用后不重用；角色不可改；隐形/超 50 候选不能当空；并发同号只一 Root；重复 Command 返原版本。|
|字段/关系|AC016–028：float/string bool 拒绝；Finance/状态/映射不能普通 patch；遮罩不清原值；跨法人、父环、重载关系、供应商模式、99 月末、时间窗口准确。|
|状态/使用|AC029–045：Root/Profile 同版本；冻结先赢零新效果、交易先赢一凭证；Purchase 无 Account 合法；依赖 BP 冻结也阻交易；跨库 HTTP 不能伪原子。|
|映射/恢复|AC046–070：INACTIVE Account 身份可维护但不授新 CRM 使用；反向 Claim 唯一/换关系整回滚；七字段、迟到低版本、ACK≠匹配；旧订单保旧 BP。|
|并发/隐私|AC071–082：强 ETag/FK/Tenant/撤权共同门、fixture 隔离、未知 commit 同键、完整原体缺失拒恢复、旧写者必须统一门。|
|R1|AC083–100：criteria 重启恢复，跨窗口不混；Q1 迟到不覆盖 Q2；OwnerConflict 普通读不清；有据同关系更正；遮罩 bool/hash；原子创建关联；Unicode 展开/hash 碰撞。|
|R2|AC101–112：创建+Base+字段+register+mapping 授权交集；null/false 不绕字段权；真实撤权次序；普通 LINK 不误要求 create。|
|R3|AC113–124：已提交撤写权仍按当前读/披露权回历史；快读未见后有序重查；查权失效不再执行业务；异 Actor/异指纹不代重放；NO_CHANGE/REJECTED 仍终态。|

[全体准确 AC][S8496]

10 Gate：IAM、SCOPE、SINGLE-WRITER、REFERENCE、ACCOUNT、MAPPING、ACC、TRADE、SECURITY、RUNTIME 全部 UNPROVEN_PRODUCTION_GATE。它们分别需要真实共同权限门、旧行主体映射、所有写者采用、引用参与者、不可变 Account 原件桥、权威 Mapping/Claim、精确 ACC 合同/只读桥、下游同事务、加密/保留和并发恢复实证。[生产门][S1001]

### 连续样例的可实施约束

|原链/入口|开发时必须保持的语义|
|---|---|
|E01–E06 创建|Options 仅允许所列字典值；空 options 不是任意代码。128 个 profile 值与九关系槽完整传入；SELF 在提交后解析为实际 BP。预览只冻结意图，Apply 只带 PreviewRef，不再次接受可改正文。|
|E07–E17 首次关联|Account observation、真实查询 criteria、候选集和 scopeRevision 一起绑定 context。LINK 后产生 mappingRevision=1 的七字段原事件；ACKED_PROCESSING 与受信投影 MATCHES_CURRENT 分开记录。|
|E18–E25 实际使用/冻结|Eligibility 的 isExecutionPermit=false；UseReceipt 的 isReusablePermit=false，效果绑定真实 consumer 版本。冻结使新 Sales 用途拒绝，不修改已有使用凭证。|
|E26–E39 换关系|DecisionBinding 绑定 action/前驱/目标/原因；REPLACE 由 BP-C001 换 BP-C002，mappingRevision 从1到2，历史订单保留原BP；快照携带原Event完整字节。|
|E40–E57 恢复/供应商|原创建重放仍返回原Receipt；UNPROVEN 查询不构造空映射。Supplier 可在 accountId=null 下作 PURCHASE_NEW_SOURCE，不能凭 Supplier 角色用作销售客户；NOT_OBSERVED 同时 noEffectProven=false。|
|R01–R06 遮罩/上下文|paidEachTimeFlg 读遮罩为 null 并列 redactedFields，完整私有 contentHash 也为 null；这不是允许将 null 写回非空 bool。第二组 criteria 不可混用第一组候选摘要。|
|R07–R38 冲突/后继|Main rev12 与 Query generation31/32/33 分轴；Q2 UNKNOWN 已成为当前后，迟到 Q1 成功仅历史 currentApplicable=false。已证同版错误发布用 CORRECT_CURRENT 建 rev13（仍同一 BP），真实新解绑再建 rev14；迟到旧消息不能回到12。|
|R39–R51 原子创建/索引|零候选 CREATE_AND_LINK 一次创建 BP、Claim、映射、Outbox、命令回执；插 BP 后中断必须所有计数及 scopeRevision 不变。U+FDFA ×100 的规范化值可展开到1800 UTF-16 units，原输入合法不等于索引可截断；存完整值并用 hash+精确比较。|
|P01–P17 创建授权|REGISTERED 必须另有 register；CREATE_AND_LINK 取 create+mapping+Base管理+字段权交集。先撤权零效果，先合法提交一组原子效果；普通 LINK 不误要求 create/Base新建字段权，PRE_REGISTERED 不要求 register。|
|Q01–Q19 历史与新写|APPLIED/NO_CHANGE/REJECTED 都是历史终态；撤新写权或预览过期不改原结果。快读未见要有序重查；结果读权/披露权仍当前判定。异指纹409、异Actor404、当前身份无效401，不重执行业务。|

上述都是隔离文书夹具，P/Q 的权限布尔与 expected 数量不进入公共请求，也不授予生产权限。[E链][J1073] [映射/使用][J3546] [恢复/供应商][J4801] [R链][J5443] [创建/规范化][J7030] [P链][J7537] [Q链][J8019]

## 11. 实际未确认事项与整合约束

- **两种 ACC 合同的实际采用仍须明确**：已核 BP 指定历史草案七字段与当前 R03 效力要求的差异。保留 BP 严格七字段及当前 ACC 所需独立 Owner 证据；真实双边合同缺失时不能自填字段或将 ACK 当可交易证明。[历史边界][ACC925] [当前要求][ACCCT32]
- **143 字段/128 可写的制造业适用性**：当前 BP 正文保留纸业字段、Supplier 固定条件和日本式邮编首版范围。此处忠实整理已接受规范，不自行将其扩成通用全行业规则或删字段；后继通用制造边界材料的适用与 Owner 决定由全局来源谱系对齐。
- **历史单列采用需要真实资料**：PaymentScheduleCd 双用途、缺法人、旧 null 币种/Status9/软删不能算法猜全。只能依据实际 Owner 证据建立当前新事实，历史未知保留。
- **物理共同门与旧写者尚未证明**：同事务 S/X 与 CurrentReferenceParticipant 是强要求，不能用 RPC/TTL 签名替换；固定旧 commerce-profile/C03 必须统一采用或准确封闭。
- **样例阅读不等于执行审计**：146 JSON 所有有效请求、结果、事务和失败语义已覆盖；106 相同子树按精确对象复用，三处嵌入原事件解码匹配。未逐项重算所有 canonical hash、运行 API 或证明真实 rollback，保留原 NOT_RUN。
- **实际运行尚未执行**：这里的重放/撤权/Query generation、创建关联回滚、跨 Actor 与敏感 hash 防泄漏均是待实现验收，124 NOT_RUN 不变。

[源内采用缺口][S986] [实际范围][U54]

## 12. 来源与实际阅读覆盖

主文 1–1067、8490–8628 和中段所有非 JSON 流程/请求头说明已全文语义读；146 JSON 全部对象已按结构投影审读，106 相同子树精确复用，128 fieldRules 分为 24 个完全相同规则类并保留全部字段，三处 Base64 原体准确对应已读 E38/R08/R10。SHA 字面与重复合成实例值不声称逐字符人工审计，未逐项重算所有规范摘要。准确块行段、复用指针与读法见 [commercial-reading.json](<D:/CP6/docs/CP6_开发设计文档_20261010/evidence/commercial-reading.json>)。历史 ACC DRAFT 仅补读 1–20、319–329、925–952，其余章节未全文；当前 ACC 合同按同 SHA 已全文记录复用。UA 81 行、最终独审 70 行、INDEX 23 行、CURRENT 274 行全文已读；早期批量输出截断的 110–112、后段说明、AC001–014、INDEX/独审部分已重新分段补齐。全文抽取/索引定位不等语义全读。

本页事实引用均指本地原件的一基行号。ACC/OPP/HO 的完整模块在交付根供联读；旧 report 只作为继承来源，不把其抽取窗口升级为本轮全文。版本、Owner Gate 和业务测试状态分别记录，不以候选名、最大 Stage 或静态 PASS 自动替换。

[U31]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5b/5bfd91bbad5913dd__UA-20260930-A-ERP-BP-MD01.md:31>
[U54]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5b/5bfd91bbad5913dd__UA-20260930-A-ERP-BP-MD01.md:54>
[S34]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:34>
[S872]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:872>
[S23]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:23>
[S58]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:58>
[U12]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5b/5bfd91bbad5913dd__UA-20260930-A-ERP-BP-MD01.md:12>
[U45]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5b/5bfd91bbad5913dd__UA-20260930-A-ERP-BP-MD01.md:45>
[R30]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c3e8c3945a2351f9__SR-20260930-A-ERP-BP-MD01-FINAL.md:30>
[U47]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5b/5bfd91bbad5913dd__UA-20260930-A-ERP-BP-MD01.md:47>
[S42]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:42>
[S860]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:860>
[S108]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:108>
[S118]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:118>
[S273]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:273>
[S405]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:405>
[S266]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:266>
[S562]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:562>
[S542]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:542>
[S560]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:560>
[S933]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:933>
[S940]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:940>
[S966]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:966>
[S747]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:747>
[S753]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:753>
[S765]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:765>
[S56]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:56>
[S783]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:783>
[S793]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:793>
[S801]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:801>
[S814]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:814>
[S826]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:826>
[S972]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:972>
[S658]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:658>
[S664]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:664>
[S568]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:568>
[S660]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:660>
[S1024]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:1024>
[S844]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:844>
[S699]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:699>
[S76]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:76>
[S878]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:878>
[S886]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:886>
[S890]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:890>
[S898]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:898>
[S852]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:852>
[S858]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:858>
[S864]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:864>
[S68]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:68>
[S722]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:722>
[S733]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:733>
[S74]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:74>
[S900]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:900>
[S1032]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:1032>
[S88]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:88>
[S92]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:92>
[S100]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:100>
[S656]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:656>
[S104]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:104>
[S986]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:986>
[S990]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:990>
[S84]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:84>
[S1001]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:1001>
[S8490]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:8490>
[R46]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c3e8c3945a2351f9__SR-20260930-A-ERP-BP-MD01-FINAL.md:46>
[S8496]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:8496>

[J1073]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:1073>

[J3546]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:3546>

[J4801]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:4801>

[J5443]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:5443>

[J7030]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:7030>

[J7537]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:7537>

[J8019]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/92c20d1ee1dc090f__ERP-BP-01_交易伙伴主数据_前后端开发Spec_v1.0.3_REVIEW_R3_CANDIDATE.md:8019>

[ACC925]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/11/11aeb11f6a3b717b__CRM-ACC-01_客户关系账户_前后端开发Spec_v1.0_DRAFT.md:925>
[ACCCT32]: <D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bd97dd191196cc6a__CONTRACT-CARDS.md:32>
