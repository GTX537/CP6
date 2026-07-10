# 纸器 63 项目分拣表（PA070 实体全量·222 字段）

> 只读盘点 · 2026-07-10 · 对应 07-09 配置基建 spec §7.5
> 范围：Order(27) / OrderDetail(126) / OrderProcess(50) / OrderMaterial(13) / OrderProcessNote(6)，共 **222 字段**（不含 BaseBizEntity 公共列与导航属性）。「63 项目」是历史口径（§5.3 製品マスタ引入项数），实表远多于此，全列。
> 状态：**已评审收口（2026-07-10，用户全采）**——⚠待拍板 13 项全部按推荐拍板，终局归置见下表「拍板结果」列；正文各 ⚠ 行以此为准。
>
> **注**：除三桶外新增第四处置「**淘汰（不迁移）**」——mcframe7 连携键/冗余复制列/被新审批机制替代的字段，既非核心也不该进包或 SFS，迁移映射表中应显式标记为「随老模块退役」。

## 一、三桶统计

| 分桶 | 数量 | 说明 |
|---|---|---|
| **核心** | 65 | 进 SalesOrder / SalesOrderLine / Item / Quotation 血缘 / 履约回写 |
| **纸器包** | 85 | 进 PaperPack_OrderLineExt + PaperPack_OrderLineProcess/Material/ProcessNote（工程/材料/工程备考三张子表整表随包，见分节说明） |
| **SFS** | 31 | 仅录入展示、无逻辑消费的杂项/客户定制槽位 |
| **淘汰（不迁移）** | 28 | mc 连携键 13、冗余复制 3、空槽 1、旧审批字段 2、mc 品目键（工程/材料表）12 内含 |
| **⚠待拍板** | 13 | 见下清单 |

## 二、⚠待拍板清单（13 项，按主题分组）——2026-07-10 已全部拍板

| 主题 | 拍板结果 |
|---|---|
| 套价机制（SalesPriceDiv×2、SetUnitPrice） | **纸器包 IPricingHook**——通用侧走已拍板取价链（07-09 spec §7.2），不污染核心定价模型 |
| セット品四件套 | **纸器包**——与 07-08 拍板 #7 一致，核心销售套件延到 v2 配置器再议 |
| 預り売上三件套 | **纸器包校验钩子**——Rev4 客户定制口径，核心 bill-and-hold 留待真实第二租户需求 |
| HaibaiKbn | **核心 FulfillmentType**——与 DropShipVendorCd 配套的行级供给方式枚举，纸器值域随包种子 |
| DeliveryReserve | **纸器包**——按纸器预备枚数语义保留，v2 做通用交货容差时再上收 |
| ProjectNoMaterial | **纸器包**——随セット品拍板走（_部材 层级与部材结构绑定） |

分桶终局统计：核心 66 / 纸器包 97 / SFS 31 / 淘汰 28 = 222（待拍板 13 项归入：核心+1（HaibaiKbn）、纸器包+12（套价3+セット品4+預り3+DeliveryReserve+ProjectNoMaterial））。

原两可理由存档：

| 主题 | 字段 | 两可理由 |
|---|---|---|
| **套价机制** | Order.SalesPriceDiv、OrderDetail.SalesPriceDiv、OrderDetail.SetUnitPrice | 有硬逻辑消费（OrderService.cs CalcAmountAsync 按 Div 选单价算金额、BatchUpdatePriceAsync 套价一括更新）→不能进 SFS；但「个别/套」二元定价是 CP 特有惯例还是通用 kit 定价，须与定价引擎设计（07-08 spec 推荐值 #2，PriceList 结构）一并拍板：进核心定价模型 vs 纸器包价格钩子（IPricingHook） |
| **セット品四件套** | SetProductCd、SetProductName、ParentChildDiv、SetRatio | 有消费（OrderService.LookupBySetProductCdAsync、ProductService 亲子创建、EstimateCalcService 映射）；「销售套件/组合品」通用 ERP 也有，但 07-08 拍板 #7 说 v1 配置器只留接口、用「Item 变体+手工 BOM」过渡——套件建模进核心还是延到 v2 配置器，需拍板 |
| **預り売上三件套** | ConsignedSalesFlg、SalesReason、ConsignedSalesQty | 有校验消费（OrderService.CheckConsignedSalesQtyAsync：預り数≤受注残）→非 SFS；bill-and-hold（先开票后发货）是通用商业场景可进核心履约子流程，但此处是 Rev4 客户定制口径，也可走纸器包校验钩子 |
| **手配区分** | OrderDetail.HaibaiKbn | 无代码消费；概念上=行级供给方式（自制/采购/直送），通用 ERP 有对应物（FulfillmentType 进核心），但当前值域是纸器手配惯例——进核心枚举 vs SFS |
| **納入予備** | OrderDetail.DeliveryReserve | 无代码消费；印刷/纸器「预备枚数」惯例（进纸器包），但通用 ERP 的对应物是交货超收容差（OverDeliveryTolerance，进核心）——归置取决于 v2 是否做交货容差 |
| **案件NO_部材** | OrderDetail.ProjectNoMaterial | 案件血缘 Parent/Child 建议进核心（Quotation→Order 行级血缘，§7.1 已拍板 Doc_LineRelation）；但 _部材 一级与セット品部材结构绑定，随セット品拍板走 |

## 三、逐字段全表

### 3.1 Order（T_Order 受注ヘッダー，27 字段）

| 实体.字段 | 日文注释/含义 | 分桶 | 归置目标 | 分桶理由 |
|---|---|---|---|---|
| Order.WebOrderNo | Web受注NO（业务PK，自动采番） | 核心 | SalesOrder.OrderNo | 单据号通用；采番迁入②NumberingRule 默认规则（07-09 spec §1②明文） |
| Order.CustomerCd | 得意先コード | 核心 | SalesOrder.CustomerCd (→BusinessPartner) | 客户是通用域，全链路消费（OrderService/WorkOrderService/OutboundService） |
| Order.OrderType | 受注区分（加工製品/シート/原紙/版型/輪切り…） | 核心 | SalesOrder.OrderTypeCd（值域=包种子数据） | 「订单类型」概念通用且有流程消费（OrderService.CheckIsEditableAsync 按 20/40/80 控编辑、PlateMoldService 固定 30）；纸器值域随包种子下发 |
| Order.OrderDepartment | 受注部門 | 核心 | SalesOrder.DepartmentCd | 部门归属通用 |
| Order.OrderDate | 受注日 | 核心 | SalesOrder.OrderDate | 通用；OtdReportService/UnshippedOrderService 消费 |
| Order.CustomerDeliveryDate | 客先納期 | 核心 | SalesOrder.RequestedDeliveryDate | 通用交期；LT 校验（OrderService.CheckDeliveryLeadTimeAsync）、OTD/未出荷报表消费 |
| Order.Quantity | 数量（伝票合計） | 核心 | 派生列（行汇总），建议 v2 不落库 | 通用但属冗余汇总，v2 由 SalesOrderLine 聚合 |
| Order.OrderSheetNo | 注文書NO（客户PO号） | 核心 | SalesOrder.CustomerPoNo | 客户采购单号通用；PA090 检索消费（OrderService） |
| Order.CustomerContact | 先方担当 | 核心 | SalesOrder.CustomerContact | 客户联系人通用 |
| Order.Addressee | 宛名 | 核心 | SalesOrder ShipTo 地址模型 | 收件抬头是通用送货信息 |
| Order.Carrier | 運送会社 | 核心 | SalesOrder.CarrierCd | 承运商通用；一览筛选消费（OrderService.BuildOrderListQuery） |
| Order.ShipDateTime | 出荷日時（字符串 yyyy/MM/dd HH:mm） | 核心 | SalesOrder.PlannedShipAt（改 DateTime 类型） | 计划出货时点通用，且有逻辑消费（OrderService.CalcLeadTimeAsync 由此逆算工程预定日）；字符串存日期是移植债，重建时纠正 |
| Order.ShipCondition | 出荷条件 | 核心 | SalesOrder.ShippingTerms | 出货条件/交货条款通用 |
| Order.SalesPriceDiv | 売価区分：1=個別/2=セット | ⚠待拍板 | 核心定价模型 vs 纸器包 IPricingHook | 见⚠清单「套价机制」；消费点 OrderService.cs（CalcAmountAsync/BatchUpdatePriceAsync） |
| Order.CurrencyCd | 受注通貨CD（凍結） | 核心 | SalesOrder.CurrencyCd | 多币种通用基建（Gap 4.3），FxRateService 消费 |
| Order.FxRate | 凍結為替レート | 核心 | SalesOrder.FxRate | 同上；ProjectListAsync 回填消费 |
| Order.McOrderNo | 受注NO（mcframe7 連携キー） | 淘汰 | 不迁移 | mc 遗留外系统键，随老模块退役 |
| Order.Status | ステータス（0=未転送/9=mc転送済） | 淘汰 | 不迁移 | mc 转送状态，非业务状态机 |
| Order.McTransferFlg | mcframe7 転送FLG | 淘汰 | 不迁移 | 同上 |
| Order.ShipStatus | 出荷ステータス（0/5/9） | 核心 | SalesOrder 主干状态机的 Shipped 支撑列 | WMS 回写履约核心（ErpBridgeHook.cs 回写、UnshippedOrderService/OtdReportService/CancelAsync 闸门消费） |
| Order.ActualShipDate | 実出荷日時（WMS確定） | 核心 | SalesOrder.ActualShipDate | 履约实绩通用；OtdReportService OTD 计算消费 |
| Order.OrderStatus | 受注ライフサイクル状態（Confirmed/…/Cancelled） | 核心 | SalesOrder.Status（v2 主干：Draft→Confirmed→…→Closed） | 即 07-09 spec §1④ 主干状态机前身；CancelAsync/UnshippedOrderService 消费 |
| Order.CancelledAt | 取消時刻 | 核心 | SalesOrder.CancelledAt | 取消审计通用（OrderService.CancelAsync 写入） |
| Order.CancelReason | 取消理由（監査用必須） | 核心 | SalesOrder.CancelReason | 同上 |
| Order.Memo1 | メモ1 | 核心 | SalesOrder.Remarks（三槽归并为一） | 单据备注通用 |
| Order.Memo2 | メモ2 | SFS | SFS | 多余备注槽位，无逻辑消费 |
| Order.Memo3 | メモ3 | SFS | SFS | 同上 |

### 3.2 OrderDetail（T_OrderDetail 受注明細，126 字段）

**键/手配NO/产品标识**

| 实体.字段 | 日文注释/含义 | 分桶 | 归置目标 | 分桶理由 |
|---|---|---|---|---|
| OrderDetail.WebOrderNo | Web受注NO (FK) | 核心 | SalesOrderLine.OrderId | 行外键 |
| OrderDetail.WebOrderDetailNo | Web受注明細NO | 核心 | SalesOrderLine.LineNo | 行号通用 |
| OrderDetail.HaibaiNo1 | 手配NO1（=单号-行号 拼接采番） | 核心 | 由 OrderNo+LineNo 派生的行业务号 | 全系统行级业务键（WorkOrderService.ExpandFromOrderAsync 写 WO.OrderNo1、PlateMoldService 反查、PA080/090 检索排序），v2 收敛为行主键派生 |
| OrderDetail.HaibaiNo2 | 手配NO2（=得意先CD 复制） | 淘汰 | 不迁移（用 header CustomerCd） | 去规范化冗余，但当前被当客户码用（OrderService.CheckCreditAsync 以 HaibaiNo2 汇总授信、一览筛选）——迁移时改指 SalesOrder.CustomerCd |
| OrderDetail.HaibaiNo3 | 手配NO3（=案件NO親 复制） | 淘汰 | 不迁移（以 ProjectNoParent 为准） | 与 ProjectNoParent 重复 |
| OrderDetail.HaibaiNo4 | 手配NO4（NULL固定） | 淘汰 | 不迁移 | 规格书即为 NULL 固定空槽 |
| OrderDetail.ProductCd | 製品コード | 核心 | SalesOrderLine.ItemCd（新 Item） | 物料引用通用；MES/WMS 展开全消费 |
| OrderDetail.ItemCd | 品目コード（mcframe7 連携用） | 淘汰 | 不迁移 | mc 品目键 |
| OrderDetail.Branch1/2/3 | 枝番1〜3（mc） | 淘汰 ×3 | 不迁移 | mc 品目枝番 |
| OrderDetail.ProductCatBig/Mid/Sml | 製品区分 大/中/小 | 核心 ×3 | Item.Category*（行不再快照） | 物料分类通用；一览筛选消费（OrderService.BuildOrderListQuery）、PlateMoldService 固定 61 |
| OrderDetail.CustomerItemName1/2 | 得意先品名1/2 | 核心 ×2 | SalesOrderLine.CustomerItemName（+客户物料对照主数据） | 客户方品名通用；检索消费（OrderService） |
| OrderDetail.CustomerPartNo | 得意先品番 | 核心 | 客户物料对照（CustomerPartNo） | 客户品番通用；一览筛选消费 |
| OrderDetail.CpItemName1/2 | CP品名1/2（自社品名） | 核心 ×2 | Item.Name 快照 → SalesOrderLine.ItemName | 自社品名通用；MES 展开用作 WO.ProductName（WorkOrderService.cs） |
| OrderDetail.JanCode | JANコード | 核心 | Item.Gtin（行不快照） | 条码是通用主数据属性 |

**数量·价格**

| 字段 | 含义 | 分桶 | 目标 | 理由 |
|---|---|---|---|---|
| QtyUnit | 数量単位 | 核心 | SalesOrderLine.QtyUnit | 通用 |
| Quantity | 数量 | 核心 | SalesOrderLine.Qty | 通用；授信/預り校验/MES/WMS 展开全消费 |
| SpecialPriceFlg | 特値FLG | 核心 | 定价引擎人工覆盖留痕（PriceOverrideFlag） | 特价标志是通用定价留痕；价格变更判定消费（OrderService.BatchUpdatePriceAsync） |
| UnitPriceUnit | 単価単位 | 核心 | SalesOrderLine.PriceUnit | 计价单位（每千枚等）通用；WMS 展开 UnitCd 消费（OutboundService.CreateFromOrderAsync） |
| SetUnitPrice | セット単価 | ⚠待拍板 | 定价引擎 vs 纸器包 | 见⚠清单「套价机制」；消费点 OrderService.cs |
| IndividualUnitPrice | 個別単価 | 核心 | SalesOrderLine.UnitPrice | 通用单价；金额计算/WMS 展开消费 |
| Amount | 金額=数量×単価 | 核心 | SalesOrderLine.Amount | 通用；授信汇总消费（OrderService.CheckCreditAsync） |

**纳期·物流**

| 字段 | 含义 | 分桶 | 目标 | 理由 |
|---|---|---|---|---|
| DeliveryCd / DeliveryName | 納入先CD/名 | 核心 ×2 | SalesOrderLine.ShipToCd/Name | 行级送达方通用 |
| CustomerDeliveryDate | 客先納期（行） | 核心 | SalesOrderLine.RequestedDeliveryDate | 通用；MES 展开计划日推算消费（WorkOrderService） |
| LogisticsGroup | 物流G | 核心 | ShipTo/BP 主数据（行不迁移） | 配送路线分组是通用物流概念，主数据侧已有（BusinessPartnerService 校验 LogisticsGroupCd），行快照无消费 |
| HaibaiKbn | 手配区分 | ⚠待拍板 | 核心 FulfillmentType vs SFS | 见⚠清单 |

**預り売上（Rev4）**

| 字段 | 含义 | 分桶 | 目标 | 理由 |
|---|---|---|---|---|
| ConsignedSalesFlg / SalesReason / ConsignedSalesQty | 預り売上FLG/理由/数 | ⚠待拍板 ×3 | 核心 bill-and-hold 子流程 vs 纸器包 | 见⚠清单；消费点 OrderService.cs（CheckConsignedSalesQtyAsync、一览筛选 OnlyConsignedSales） |

**FSC·食品安全**

| 字段 | 含义 | 分桶 | 目标 | 理由 |
|---|---|---|---|---|
| FscOrderType | FSC受注区分 | 纸器包 | PaperPack_OrderLineExt.FscOrderType | FSC 森林认证是纸/包装行业专属，一览筛选消费（OrderService.BuildOrderListQuery） |
| FscProductDiv | FSC製品区分 | 纸器包 | PaperPack_OrderLineExt.FscProductDiv | FSC 发行流程硬消费（FscChecklistService.cs 以 FscProductDiv≠0 为对象抽取） |
| FscMaterialDiv | FSC材料区分 | 纸器包 | PaperPack_OrderLineExt.FscMaterialDiv | 同 FSC 域 |
| FscManagementNo | FSC管理NO | 纸器包 | PaperPack_OrderLineExt.FscManagementNo | 发行采番/已发行判定消费（FscChecklistService.IssueAsync） |
| FoodSafety | 食品安全区分 | 纸器包 | PaperPack_OrderLineExt.FoodSafety | 食品接触包装属性，属包装行业合规域（无核心计算消费但结构化、随 FSC 族群走包） |

**数量関連属性**

| 字段 | 含义 | 分桶 | 目标 | 理由 |
|---|---|---|---|---|
| ShipInspection | 出荷検査 | 核心 | Item/SalesOrderLine.ShipInspectionFlg | 出货检验要求是通用 QC 概念（WMS 有 QcInspectionService 承接面） |
| FixedShipment | 定番出荷? | SFS | SFS | 无任何逻辑消费的区分槽位 |
| DeliveryReserve | 納入予備 | ⚠待拍板 | 核心交货容差 vs 纸器包预备枚数 | 见⚠清单 |
| SalesSample | 営業見本 | SFS | SFS | 仅录入展示 |
| SalesAvailable | 販売可能区分 | SFS | SFS | 无逻辑消费（PlateMoldService 仅赋值 EarningsCd 透传） |

**構成情報スナップショット（13 字段）——纸器包核心地带**

| 字段 | 含义 | 分桶 | 目标 | 理由 |
|---|---|---|---|---|
| SheetFlute | 段（楞型 A/B/W…） | 纸器包 | PaperPack_OrderLineExt.FluteType | 纸器专有且重逻辑消费：价格主键（SheetUnitPriceService.cs 复合键）、歩留率查表（EstimateCalcService.cs M067）、一览筛选/排序（OrderService） |
| PaperCdF/C/B | 原紙CD 表/中/裏 | 纸器包 ×3 | PaperPack_OrderLineExt.PaperCdF/C/B | 原纸构成；原纸单价查表（EstimateCalcService M_GenericCode(Paper).Num1）、价格主键（SheetUnitPriceService）、构成串展示（OrderService.ProjectListAsync） |
| PrintCdF/C/B | 印刷CD 表/中/裏 | 纸器包 ×3 | PaperPack_OrderLineExt.PrintCdF/C/B | 同上（价格主键+构成串+筛选） |
| EmbossCdF/C/B | エンボスCD 表/中/裏 | 纸器包 ×3 | PaperPack_OrderLineExt.EmbossCdF/C/B | 同上 |
| MakerCdF/C/B | メーカーCD 表/中/裏 | 纸器包 ×3 | PaperPack_OrderLineExt.MakerCdF/C/B | 原纸厂商构成；一览筛选消费（OrderService） |

**寸法スナップショット（9 字段）**

| 字段 | 含义 | 分桶 | 目标 | 理由 |
|---|---|---|---|---|
| SheetPrint | シート印刷 | 纸器包 | PaperPack_OrderLineExt.SheetPrint | 纸器工艺属性（EstimateCalc 同名映射） |
| BladeWidth / BladeFlow | 全判 巾/流れ | 纸器包 ×2 | PaperPack_OrderLineExt.FullSheetWidth/Flow | 展开尺寸；PA080 一览「全判巾/流れ」列（OrderService.ProjectListAsync）、见积面积基础 |
| GutterFb / GutterLr | 溝 前後/左右 | 纸器包 ×2 | PaperPack_OrderLineExt.GutterFb/Lr | 罫线/沟槽尺寸，纸器工艺 |
| SheetDimW / SheetDimF | スリ 巾/流れ | 纸器包 ×2 | PaperPack_OrderLineExt.SheetDimW/F | 硬计算消费：シート面積=(W×F)/1e6 进平米单价（EstimateCalcService.cs 行 303-315） |
| SalesWidth | 販売巾 | 纸器包 | PaperPack_OrderLineExt.SalesWidth | 纸器销售尺寸属性 |
| FinalMachineProcess | 最終機械工程 | 纸器包 | PaperPack_OrderLineExt.FinalMachineProcess | 产品区分自动判定的输入（OrderService.CalcProductCategoryAsync 注释口径：最终机械工程→机械主数据→区分） |

**備考（7 字段）**

| 字段 | 含义 | 分桶 | 目标 | 理由 |
|---|---|---|---|---|
| PrintNote | 印刷備考 | SFS | SFS | 仅录入展示 |
| MfgNote | 製造備考 | SFS | SFS | 仅录入展示（MES 展开不读它） |
| RemfgNote | 再製造備考 | SFS | SFS | 仅录入展示 |
| SlipNote | 伝票備考 | 核心 | SalesOrderLine.SlipNote | 票据打印消费（OrderService CSV 出力列「伝票備考」+一览排序白名单） |
| DeliveryNote | 納入備考 | 核心 | SalesOrderLine.DeliveryNote | 纳品书打印消费（同上 CSV「納入備考」列） |
| ShipNote1/2 | 出荷備考1/2 | SFS ×2 | SFS | 无逻辑消费 |

**仕入情報（5 字段）**

| 字段 | 含义 | 分桶 | 目标 | 理由 |
|---|---|---|---|---|
| DefectiveHaibaiNo | 不適合手配NO | 核心 | SalesOrderLine.ReworkRefNo（品质返工关联） | 不良重做关联是通用质量流程；一览筛选/排序/CSV 消费（OrderService） |
| PurchaseVendor | 仕入先 | 核心 | SalesOrderLine.DropShipVendorCd | 购买品受注的直送供应商，通用三角贸易概念 |
| RollMeter | ロールメーター（巻米数） | 纸器包 | PaperPack_OrderLineExt.RollMeter | 原纸受注（OrderType=40）的卷长数量属性，纸器专有结构化字段 |
| PurchaseUnitPrice | 仕入単価 | 核心 | SalesOrderLine.PurchasePrice | 直送采购价通用 |
| PurchaseUnit | 仕入単位 | 核心 | SalesOrderLine.PurchaseUnit | 同上（PA090 用作显示单位，OrderService） |

**案件·見積連携（6 字段）**

| 字段 | 含义 | 分桶 | 目标 | 理由 |
|---|---|---|---|---|
| ProjectNoParent | 案件NO親 | 核心 | Quotation→Order 血缘模型（§7.1 Doc_LineRelation） | 报价/案件血缘是 v1 核心链路；Quotation/Product/FscChecklist 三服务检索消费 |
| ProjectNoChild | 案件NO子 | 核心 | 同上 | 同上 |
| ProjectNoMaterial | 案件NO部材 | ⚠待拍板 | 血缘模型 vs 随セット品拍板 | 见⚠清单 |
| QuotationNo | 見積書NO | 核心 | SalesOrderLine.QuotationRef（行级血缘） | 见积→受注断链修复正是 v2 立项动机；ProductService 检索消费 |
| EstimateCalcNo | 見積計算書NO | 核心 | 血缘模型 | 有状态判定消费（ProductService.cs：EstimateCalcNo 有无决定登录时审批状态） |
| RefEstimateCalcNo | 参考見積計算書NO | SFS | SFS | 参考号仅展示，无逻辑消费 |

**セット品（4 字段）**

| 字段 | 含义 | 分桶 | 目标 | 理由 |
|---|---|---|---|---|
| SetProductCd / SetProductName / ParentChildDiv / SetRatio | セット品CD/名/親子区分/セット比率 | ⚠待拍板 ×4 | 核心销售套件 vs v2 配置器 | 见⚠清单；消费点 OrderService.LookupBySetProductCdAsync、ProductService 亲子登录 |

**受注区分（行）**

| 字段 | 含义 | 分桶 | 目标 | 理由 |
|---|---|---|---|---|
| OrderType | 受注区分（行，承继 header） | 核心 | 随 header（行不再复制） | 同 Order.OrderType；CheckIsEditableAsync 消费 |

**製品属性スナップショット（13 字段）**

| 字段 | 含义 | 分桶 | 目标 | 理由 |
|---|---|---|---|---|
| ProductUsage | 製品用途 | SFS | SFS | 仅展示 |
| DistributionDiv | 流通区分 | SFS | SFS（注：旧一览有筛选 OrderService.BuildOrderListQuery，若 v2 需筛选则升包） | 仅筛选/展示、无计算消费 |
| ConfidentialInfo | 機密情報区分 | SFS | SFS | 仅展示 |
| SeizureDiv | 差押区分 | SFS | SFS | 仅展示 |
| ImportanceDiv | 重要度区分 | SFS | SFS | 仅展示 |
| MChange | M変更（4M変更管理） | SFS | SFS | 概念通用（QMS）但零消费，客户定制口径 |
| QualityDiv | 品質区分 | SFS | SFS | 仅展示 |
| ProductShape | 製品形状（箱式） | 纸器包 | PaperPack_OrderLineExt.BoxStyle | 纸器箱型/形状是结构化行业属性，一览筛选消费（OrderService） |
| UnescoMark | ユネスコマーク | SFS | SFS | 印刷标记类，仅展示 |
| OrigamiMark | 折りマーク | SFS | SFS | 同上 |
| FourMContract | 4M契約 | SFS | SFS | 客户契约定制项 |
| TkpWrinkleStd | TKPシワ基準 | SFS | SFS | 明显特定客户（TKP）定制基准 |
| RecyclingPayment | 容リ法再商品化委託区分 | 纸器包 | PaperPack_OrderLineExt.RecyclingPayment | 容リ法是包装行业法定申报域，与下面使用量 6 字段同族 |

**容リ法使用量（6 字段）**

| 字段 | 含义 | 分桶 | 目标 | 理由 |
|---|---|---|---|---|
| PaperUsageG / PlasticUsageG / GlassUsageG / PetUsageG / PackPaperUsageG / PackPlasticUsageG | 紙/プラ/ガラス/PET/包装紙/包装プラ 使用量(g) | 纸器包 ×6 | PaperPack_OrderLineExt.*UsageG | 容リ法申报的结构化数量输入（当前代码无计算消费，但为法定报表既定输入，属包的申报报表职责，不宜散进 SFS） |

**其他**

| 字段 | 含义 | 分桶 | 目标 | 理由 |
|---|---|---|---|---|
| DesignProposalNo | デザイン提案NO | SFS | SFS | 仅检索展示（ProductService 筛选），无流程消费 |
| SalesPriceDiv | 売価区分（行） | ⚠待拍板 | 同 header | 金额计算消费（OrderService.BatchUpdatePriceAsync 按行 Div 算 Amount） |
| FreightBilling | 運賃請求区分 | 核心 | SalesOrder.FreightTerms | 运费计费方式是通用物流条款 |
| McOrderNo / McOrderDetailNo | mc側受注NO/明細NO | 淘汰 ×2 | 不迁移 | mc 连携键 |
| Status | ステータス（mc転送） | 淘汰 | 不迁移 | mc 转送状态 |
| WfApprovalFlg | WF承認FLG | 淘汰 | 由 ④ApprovalPoints + Wf_ApprovalBinding/IApprovalService 替代 | 07-09 spec §1④ 审批单源拍板；现消费（BatchUpdatePriceAsync 重置）迁到新机制 |
| McTransferFlg | mc転送FLG | 淘汰 | 不迁移 | mc 连携 |
| ProvisionalPriceFlg | 仮単価FLG | 核心 | SalesOrderLine.ProvisionalPriceFlg | 暂定价→正式价是通用定价流程（BatchUpdatePriceAsync 确定为本单价的硬逻辑） |
| PriceChangeReason | 単価変更理由 | 核心 | 价格覆盖留痕（PriceChangeLog.Reason） | 通用留痕（07-08 spec 推荐值 #2「人工覆盖留痕」）；PA090 消费 |
| ApprovalStatus | 承認状況（0/1/9） | 淘汰 | 由审批实例状态查询替代（IApprovalService） | 单据上冗余存审批态与审批单源拍板冲突；现消费（PA090 筛选/置位）迁新机制 |
| ShippedQty | 累計出荷数 | 核心 | SalesOrderLine.ShippedQty | 履约回写核心；ErpBridgeHook.cs 加算、Backorder/Unshipped 计算残数 |
| ShipStatus | 出荷ステータス（行） | 核心 | SalesOrderLine.ShipStatus | 同上（ErpBridgeHook 判定 5/9） |
| LastShipDate | 最終出荷日時 | 核心 | SalesOrderLine.LastShipDate | OTD/Backorder 消费（OtdReportService/BackorderService） |
| LastOutboundNo | 最終出庫指示NO | 核心 | SalesOrderLine.LastOutboundNo | WMS 追溯（ErpBridgeHook/BackorderService） |
| ReturnedQty | 累計返品数（RMA） | 核心 | SalesOrderLine.ReturnedQty | RMA 回写（ErpBridgeHook.cs） |
| BackorderQty | 欠品残数 | 核心 | SalesOrderLine.BackorderQty | Backorder 流程硬消费（BackorderService.cs 残数=Qty−Shipped−Backorder） |

### 3.3 OrderProcess（T_OrderProcess 受注加工工程，50 字段）

**整表归置说明**：受注时可编辑的行级工程快照是纸器「シート受注」业务形态（CheckIsEditableAsync 只对 20/40/80 开放编辑）；v2 核心层的工艺路线归 Item/Routing（07-08 spec 架构骨架），且现 MES 展开（WorkOrderService.ExpandFromOrderAsync）**读的是 ProductProcesses 主数据而非本表**——本表整体作为 **PaperPack_OrderLineProcess** 随包，通用工艺概念（WG/机台/LT/损耗）由核心 Routing 另行承载。

| 实体.字段 | 含义 | 分桶 | 归置目标 | 理由 |
|---|---|---|---|---|
| WebOrderNo / WebOrderDetailNo / ProductCd | 键 | 纸器包 ×3 | PaperPack_OrderLineProcess FK | 随宿主表 |
| OperationCd | 作業コード（mc工程码，PK4） | 纸器包 | .OperationCd | 工程步骤键（mc 语义需在包内重构为 Routing 步骤引用） |
| ProcessCd | 工程コード（mc品目码） | 纸器包 | .ProcessCd | 同上 |
| TopItemCd / TopBranch1/2/3 | TOP品目+枝番（mc） | 淘汰 ×4 | 不迁移 | mc 品目键 |
| ItemCd / Branch1/2/3 | 工程品目+枝番（mc） | 淘汰 ×4 | 不迁移 | mc 品目键 |
| WorkingGroupCd | WG（工作组） | 纸器包 | .WgCd | 订单级 WG 指定随包（通用 WG 概念在核心 Routing/WorkCenter）；CalcProductCategoryAsync 兜底消费 |
| MachineOrVendor | 号機/外注先 | 纸器包 | .MachineOrVendor | 订单级机台/外协指定 |
| MachineFixedFlg | 号機固定FLG | 纸器包 | .MachineFixedFlg | 排产钉机台，纸器排程习惯 |
| CpDeliveryDiv | CP配送区分 | 纸器包 | .CpDeliveryDiv | CP 自有配送区分 |
| Spec01〜10 | 工程仕様01〜10 | 纸器包 ×10 | .Spec01-10 | 结构化工序规格（同名主数据字段被 MES 用作 ProcessName，WorkOrderService.cs 行 459） |
| QtyUnit | 数量単位 | 纸器包 | .QtyUnit | 随工程行 |
| PlateNo1/2/3 | 版NO1〜3 | 纸器包 ×3 | .PlateNo1-3 | 版号是纸器印刷核心资产（PlateMoldService 全域管理版型/木型） |
| Consumable1/2/3 | 消耗品1〜3 | 纸器包 ×3 | .Consumable1-3 | 工序消耗品（木型/刀模等） |
| PurchaseUnitPrice | 仕入単価（工序外协价） | 纸器包 | .PurchasePrice | 订单级工序外协价快照 |
| FixedPrice | 固定単価 | 纸器包 | .FixedPrice | 同上 |
| LossRate | ロス率 | 纸器包 | .LossRate（核心 Routing 有 StdLossRate 对应物） | 订单级快照随包；主数据同名字段进 MES StdLossRate（WorkOrderService） |
| MachineCount | 台数 | 纸器包 | .MachineCount | 工序台数，纸器排产参数 |
| LeadTimeDays | LT日数 | 纸器包 | .LeadTimeDays | 加工予定日逆算输入（OrderService.CalcLeadTimeAsync 营业日逆算） |
| StorageLocation | 置場 | 纸器包 | .StorageLocation | 工序间置场，随包 |
| SortOrder | 並び順 | 纸器包 | .SortOrder | 工序顺序 |
| PriorityItem1〜8 | 製造順優先項目1〜8 | SFS ×8 | SFS | 8 个空槽无任何逻辑消费，典型可裁杂项 |
| ScheduledDate | 加工予定日（出荷日時逆算） | 纸器包 | .ScheduledDate | 有计算消费（CalcLeadTimeAsync 结果落此），属包内排程展示 |

### 3.4 OrderMaterial（T_OrderMaterial 受注加工材料，13 字段）

**整表归置说明**：受注行级材料快照（支给/原纸）整表随包为 **PaperPack_OrderLineMaterial**；v2 通用 BOM 归 Item/BOM 核心，MES 材料展开读 ProductMaterials 主数据（WorkOrderService.ExpandFromOrderAsync），本表无下游计算消费。

| 实体.字段 | 含义 | 分桶 | 归置目标 | 理由 |
|---|---|---|---|---|
| WebOrderNo / WebOrderDetailNo / ProductCd / ProcessCd / MaterialCd | 键 5 列 | 纸器包 ×5 | PaperPack_OrderLineMaterial FK/键 | 随宿主表 |
| MaterialTypeDiv | 工程材料区分（1仕掛/2連産品/3原料/4印刷原紙） | 纸器包 | .MaterialTypeDiv | 「連産品/印刷原紙」值域纸器专有；MES 材料展开同名主数据字段被消费（WorkOrderService） |
| ItemCd / Branch1/2/3 | 品目+枝番（mc，MCNULLVAL 填充） | 淘汰 ×4 | 不迁移 | mc 品目键（OrderService.DtoToMaterial 用 "MCNULLVAL" 占位即其证据） |
| SupplyDiv | 受給区分（1無償/2有償支給） | 纸器包 | .SupplyDiv | 支给概念通用（外协供料）但宿主表随包；v2 通用外协域另建时再上收 |
| SupplyUnitPrice | 支給単価 | 纸器包 | .SupplyUnitPrice | 同上 |
| SortOrder | 並び順 | 纸器包 | .SortOrder | 排序 |

### 3.5 OrderProcessNote（T_OrderProcessNote 受注工程備考，6 字段）

| 实体.字段 | 含义 | 分桶 | 归置目标 | 理由 |
|---|---|---|---|---|
| WebOrderNo / WebOrderDetailNo / ProductCd / OperationCd | 键 4 列 | 纸器包 ×4 | PaperPack_OrderLineProcessNote FK | 随工程表 |
| Note1 / Note2 | 工程備考1/2 | 纸器包 ×2 | .Note1/2 | 内容仅展示（本应 SFS），但按「工程行 1:1」结构挂靠——SFS 只能绑实体详情页表单（07-09 spec §1⑤），无法按工程子行绑定，故随工程表进包 |

## 四、对写 plan 的三点提示（盘点副产品）

1. **HaibaiNo2 是隐性承重墙**：授信检查（OrderService.CheckCreditAsync）、PA080/090 检索、CSV 全用 HaibaiNo2 当客户码而非 header.CustomerCd——迁移映射必须把这些消费点改指 SalesOrder.CustomerCd，否则「淘汰」会断授信。
2. **OrderProcess/OrderMaterial 现状是「写而不读」**：MES 展开读的是产品主数据（ProductProcesses/ProductMaterials）而非受注快照，两者可能不一致——包表设计时应拍板受注快照是否成为 WO 展开的优先源（老 PA070 的原始意图）。
3. **审批相关三字段（WfApprovalFlg/ApprovalStatus + PowerEgg 46 项目起票）** 全部收敛到 IApprovalService+Wf_ApprovalBinding（IPowerEggWorkflowService.cs 的 stub 即 07-08 拍板 #3 说的 PowerEgg 备选实现位），不要按字段平移。
