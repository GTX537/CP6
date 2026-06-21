### 一、CP6.Entity — 数据实体层（领域模型 + DTO）

#### (CP6.Entity 根) — 实体基类与基础设施

- `CP6.Entity/BaseEntity.cs` — 所有实体的根基类：Guid 主键（Identity）+ 创建人/创建时间/修改人/修改时间四审计字段，每张表共有。
- `CP6.Entity/BaseTenantEntity.cs` — 多租户实体基类（OA 章10）：在 BaseEntity 上加 TenantId 行级隔离列，继承后自动纳入 CP6Context 全局查询过滤 + 写入盖章；纯系统共享表（字典/语言/菜单）不继承。
- `CP6.Entity/BaseBizEntity.cs` — 业务表公共基类：在 BaseTenantEntity 上再加 IsDeleted 逻辑删除标记 + RowVersion 乐观锁（[Timestamp]）；ERP/MES/WMS 业务实体继承它即同时获得租户隔离 + 软删 + 并发安全；Sys_* 系统表仍直继 BaseEntity。
- `CP6.Entity/IDataScoped.cs` — 数据权限可过滤标记接口（PUB 章03）：暴露 Creator（本人范围）+ DeptId（本部门/及下级/自定义范围），业务实体实现后被 IDataScopeFilter 注入范围过滤。
- `CP6.Entity/GlobalUsings.cs` — 全局 using 集约：把 Erp/Sys/Integration/Common 等子命名空间的 DomainModels 与 DTOs 在 Entity 项目内全程统一可解析（物理文件夹 = 命名空间）。

#### DomainModels/Common
- `CP6.Entity/DomainModels/Common/DocSequence.cs` — 全社统一单据采番计数器主数据，按机能代码(EMC/QTN/PRD/ORD/MLD/FSC)保存永不重置的最终自增值，用于生成"机能码+年月+自增"13位单据号。
- `CP6.Entity/DomainModels/Common/MasterBase.cs` — 据点(事业所)主数据，含据点编码/名称、管理者据点标志及单价折扣阈值，用于见积据点权限与折扣红色预警控制。
- `CP6.Entity/DomainModels/Common/MasterGenericCode.cs` — 通用代码主数据(汎用マスタ)，用GroupCode区分各类下拉代码组(受注区分/印刷/工程/单位等)，Code+GroupCode唯一并带附加字段和数值参数。
- `CP6.Entity/DomainModels/Common/MasterStaff.cs` — 负责人(担当者)主数据，含担当者编码/姓名及所属据点，支持按受注据点联动过滤并可关联登录用户。

#### DomainModels/Erp
- `CP6.Entity/DomainModels/Erp/CreditNote.cs` — 退款/换货/报废贷项通知单据，记录客户、产品批次、数量金额与事由，类型由CreditNoteType常量(REFUND/EXCHANGE/SCRAP)区分。
- `CP6.Entity/DomainModels/Erp/EstimateCalc.cs` — 见积计算书(报价计算书)主表，承载客户/案件/纸板三层构成/战略商品区分/各档报价数量与单价等约55核心字段，并级联工程明细。
- `CP6.Entity/DomainModels/Erp/EstimateCalcProcess.cs` — 见积计算书的加工工程明细行，每行对应一道工程并记录工程/作业代码、7组规格标签值、版型号及工程备注。
- `CP6.Entity/DomainModels/Erp/FscChecklist.cs` — FSC制品化检查表(PA100)发行历史，按FSC管理号采番并记录关联报价书/计算书、客户担当及生成的Excel模板与文件信息。
- `CP6.Entity/DomainModels/Erp/FxRate.cs` — 多币种汇率主数据，按币种与适用日保存"外币1单位对应基轴币(JPY)金额"，受注时冻结当日汇率；同文件FxConstants定义基轴币常量与判定。
- `CP6.Entity/DomainModels/Erp/Order.cs` — 受注(销售订单)表头，含客户/受注区分/数量/多币种冻结汇率/mcframe7转送状态/WMS出货回写及订单生命周期取消链字段，下挂订单明细。
- `CP6.Entity/DomainModels/Erp/OrderDetail.cs` — 受注明细行(约125字段)，受注时从制品主数据快照拷贝构成/尺寸/容器再生法用量等，承载数量单价、FSC、出货回写与单价订正承认信息。
- `CP6.Entity/DomainModels/Erp/OrderMaterial.cs` — 受注加工材料行，按受注号/明细/制品/工程/材料代码记录工程材料区分(仕掛品/连产品/原料/印刷原纸)与有偿/无偿支给单价。
- `CP6.Entity/DomainModels/Erp/OrderProcess.cs` — 受注加工工程行，记录作业/工程代码、机台/外注、10组工程规格、版型与消耗品、采购单价/损耗率/LT及制造顺序优先项和加工预定日。
- `CP6.Entity/DomainModels/Erp/OrderProcessNote.cs` — 受注工程备考行，按受注号/明细/制品/作业代码保存对应工程的备考1与备考2两条文本。
- `CP6.Entity/DomainModels/Erp/PlateMold.cs` — 木型/版型管理主数据(PA140，最复杂主表)，以版型号+Rev做版型履历管理，含构成快照、尺寸付数、外注金额、添付物/必要物及mcframe7连携字段。
- `CP6.Entity/DomainModels/Erp/ProductCoProduct.cs` — 制品连产品明细，针对トムソン(冲切)工程按制品/工程/行号记录各连产品名、产出数量比率与次工程代码。
- `CP6.Entity/DomainModels/Erp/ProductLotPrice.cs` — 制品按批量(ロット)的单价明细主数据，保存现行/新单价、套单价、本支店单价、采购单价及单价适用基准(受注日/纳入日)。
- `CP6.Entity/DomainModels/Erp/ProductMaster.cs` — 制品基本主数据(MSBBPA050)主表，以17位制品代码为业务键关联品目码/枝番、案件与报价计算书号、客户及套品信息。
- `CP6.Entity/DomainModels/Erp/Quotation.cs` — 御见积书(正式报价书)主表/聚合根，聚合多张见积计算书并持有打印明细，含表头/FSC/纳期支付条件/页脚备注及见积确认与主数据确定状态。
- `CP6.Entity/DomainModels/Erp/QuotationCalc.cs` — 御见积书与见积计算书的M:N关联中间表，按报价书号+计算书号保存"使用"勾选及行级见积确认/主数据确定FLG与FSC管理号。
- `CP6.Entity/DomainModels/Erp/QuotationDetail.cs` — 御见积书打印用明细行，记录品名/数量/单价/金额及合计打印标志，可关联来源见积计算书号或手工追加。
- `CP6.Entity/DomainModels/Erp/SheetUnitPrice.cs` — Web纸板(シート)单价主数据(PA130，13项复合PK含改定日/据点/客户/段/三层原纸印刷压纹)记录纸板单价；同文件SheetUnitPriceEstimate为同构的见积用单价表。
- `CP6.Entity/DomainModels/Erp/BusinessPartner.cs` — Web取引先主数据(系统最复杂主表，170+字段)，以取引先代码为键并通过9种属性FLG(得意先/卖挂/请求/入金/纳品/发注/买挂/支付预定/支付)切换各Tab，含信用额度与多币种字段。
- `CP6.Entity/DomainModels/Erp/ProductMaterial.cs` — 制品加工材料主数据，按制品/工程/材料代码记录工程材料区分、有偿/无偿受给单价及MRP用量(尺寸驱动/静态定额、单耗与单位)。
- `CP6.Entity/DomainModels/Erp/ProductProcess.cs` — 制品加工工程主数据，按制品+作业代码记录工程/品目代码、工作组、机台、CP配送区分及10组工程规格(mcframe7连携)。

#### DomainModels/Fin
- `CP6.Entity/DomainModels/Fin/ApInvoice.cs` — 应付发票单据(头+明细行+状态机)，记录供应商发票、全外币金额与核销进度，过账经自动凭证引擎生成借进项/贷应付凭证。
- `CP6.Entity/DomainModels/Fin/ApSettlement.cs` — 应付核销勾稽实体，承载付款与应付发票的多对多核销关系，并记录现金折扣/舍入/汇兑损益等尾差及其冲销凭证。
- `CP6.Entity/DomainModels/Fin/ArInvoice.cs` — 应收发票单据(镜像应付)，对客户开票记录收入与销项税，出货自动开票时附带COGS成本结转，含销售退货红字与出货幂等键。
- `CP6.Entity/DomainModels/Fin/ArSettlement.cs` — 应收核销勾稽实体，承载收款单与应收发票的多对多核销及现金折扣/汇兑损益尾差冲销。
- `CP6.Entity/DomainModels/Fin/BankAccount.cs` — 银行账户主数据，绑定一个GL银行存款科目，作为付款/收款的资金账户并决定凭证银行侧科目。
- `CP6.Entity/DomainModels/Fin/CostCenter.cs` — 成本中心主数据(部门/工序/机台树形)，作为分析性会计维度按机台/工序切分费用，可关联MES机台。
- `CP6.Entity/DomainModels/Fin/FinSequence.cs` — 财务采番计数器，按"键+月度作用域"复合键为凭证/单据生成跨月归零的流水号。
- `CP6.Entity/DomainModels/Fin/FiscalPeriod.cs` — 会计期间主数据(年+月，财年可≠日历年)，凭证按记账日归期、结账后锁期禁记账。
- `CP6.Entity/DomainModels/Fin/GlAccount.cs` — 会计科目(科目表一行)，定义五大类/借贷方向/控制科目/角色锚点，是所有凭证行唯一可引用的科目字典。
- `CP6.Entity/DomainModels/Fin/JournalLine.cs` — 记账凭证分录行，记单一科目的借或贷(本位币)，携带往来单位/成本对象/成本中心及原币多币种信息。
- `CP6.Entity/DomainModels/Fin/Payment.cs` — 付款单据(对供应商付款)，过账生成借应付/贷银行凭证(预付走预付账款)，按付款日汇率与发票汇率之差产生已实现汇兑损益。
- `CP6.Entity/DomainModels/Fin/PostingRule.cs` — 自动凭证引擎的"规则即数据"记账规则(头+行)，按业务事件类型配置借贷科目角色与取数字段，换准则只改seed引擎零改动。
- `CP6.Entity/DomainModels/Fin/Receipt.cs` — 收款单据(客户回款，镜像付款)，过账生成借银行/贷应收凭证(预收走预收账款)并经核销产生已实现汇兑损益。
- `CP6.Entity/DomainModels/Fin/TaxCode.cs` — 税码主数据，定义税率/进项销项方向/可抵扣性，供发票行算税(不可抵扣进项税并入成本)。
- `CP6.Entity/DomainModels/Fin/CostSheet.cs` — 工单成本归集单(头+料工费明细行)，料取MES真实消耗量×BOM供给单价、工费按工时机时×费率，算出FG完工单位成本与实际vs标准差异。
- `CP6.Entity/DomainModels/Fin/AssetEnums.cs` — 固定资产模块枚举集合(折旧方法四法/卡片状态/折旧批次状态与生成路径/处置类型/处置单状态)。
- `CP6.Entity/DomainModels/Fin/AssetCategory.cs` — 资产分类主数据(树形)，驱动折旧方法/年限/残值率默认值及固定资产/累计折旧/折旧费用三科目路由。
- `CP6.Entity/DomainModels/Fin/AssetCard.cs` — 固定资产卡片(核心主数据)，记录原值/残值/折旧方法年限/累计折旧/起折期间，净值为计算属性，含期初建卡与科目覆盖。
- `CP6.Entity/DomainModels/Fin/DepreciationRun.cs` — 折旧批次头(每期一批量批次)，汇总当期折旧总额/资产数并关联汇总凭证，区分手工/Worker/结账钩子/处置补提生成路径。
- `CP6.Entity/DomainModels/Fin/DepreciationEntry.cs` — 资产级折旧明细(每资产每批一行)，记录当期折旧额、期初/期末累计与净值及折旧费用/累计折旧科目，供追溯。
- `CP6.Entity/DomainModels/Fin/AssetDisposal.cs` — 资产处置单(出售/报废/转让/盘亏)，结算原值/累计折旧/净值/价款/处置损益并生成清理凭证，确认时快照卡片原状态供反冲还原。
- `CP6.Entity/DomainModels/Fin/JournalEntry.cs` — 记账凭证头(头-行结构)，强制借贷平衡、不可改不可删只能红冲，手工凭证走maker-checker双人复核、自动凭证可信直过，VoucherSource枚举区分手工/AP/AR/成本/结转/红冲/汇兑/银行对账/折旧来源。
- `CP6.Entity/DomainModels/Fin/BankStatement.cs` — 银行对账会话头(每账户每期一会话)，记期初期末余额与Open/Locked状态，锁定时写调节表快照JSON作审计真相来源。
- `CP6.Entity/DomainModels/Fin/BankStatementLine.cs` — 银行流水行，记交易日/方向/金额与后端物化的带符号金额，承载匹配状态/差异分类/去重指纹及单边项幂等生成凭证引用。
- `CP6.Entity/DomainModels/Fin/BankReconMatch.cs` — 银行对账匹配组，统一承载1:1/1:N/N:1/N:M撮合并约束组内流水带符号金额合计等于凭证银行侧合计。
- `CP6.Entity/DomainModels/Fin/BankReconJournalLink.cs` — 匹配组与凭证行的关联表，凭证行Id唯一保证一行只对账一次，记录该凭证行银行侧带方向金额。
- `CP6.Entity/DomainModels/Fin/BankImportProfile.cs` — 银行流水导入列映射模板，定义CSV/Excel格式、日期/金额/描述列映射及入款出款/带符号取数模式与分隔符。
- `CP6.Entity/DomainModels/Fin/Budget.cs` — 预算方案主数据(按财年每财年唯一)，定义预算范围(损益表)，为下属版本/行的顶层容器。
- `CP6.Entity/DomainModels/Fin/BudgetVersion.cs` — 预算版本，方案下多版本至多一个生效作控制+报表基准，含审批流引用与控制模式(无/警告/阻断)及控制口径(YTD/期间)。
- `CP6.Entity/DomainModels/Fin/BudgetLine.cs` — 预算行=维度桶(科目×成本中心×成本对象，一版一桶唯一)，存年度金额并由真实维度派生not-null规范化键供唯一索引。
- `CP6.Entity/DomainModels/Fin/BudgetLinePeriod.cs` — 预算行按月分解(财年期号1..12)，随预算行整体保存的逐期金额。

#### DomainModels/Integration
- `CP6.Entity/DomainModels/Integration/IntegrationEvent.cs` — 跨模块集成事件持久化记录(T_IntegrationEvent，Phase 6 Bridge Hook)，记录源/目标模块、Hook名、源/目标单号及Pending/Success/Skipped/Failed/DeadLetter生命周期，支撑自动重试、死信与端到端CorrelationId追溯。

#### DomainModels/Mes
- `CP6.Entity/DomainModels/Mes/DefectCategory.cs` — 不良分类主数据(M_DefectCategory)，以大分类CD+小分类CD复合主键维护纸箱缺陷的两级分类(如D01尺寸不良/巾尺寸超差)。
- `CP6.Entity/DomainModels/Mes/DefectRecord.cs` — 不良品记录单据(T_DefectRecord，单号DFyyyyMMdd-NNNN)，记录工单某工序的缺陷数量、原因分析(5Why)与是正处置，含起票/分析/是正/完了四态流转。
- `CP6.Entity/DomainModels/Mes/InspectionTemplate.cs` — 检验项目模板主数据(M_InspectionTemplate)，以模板CD+项目序号定义各检验项的检验方法、规格值与上下限公差及适用工序。
- `CP6.Entity/DomainModels/Mes/MesSequence.cs` — MES按日单据流水号采番表(T_MesSequence)，按采番键(WO/PR/QC/DF)+日期记录当日当前序号值用于生成各类单号。
- `CP6.Entity/DomainModels/Mes/QualityInspection.cs` — 品质检验单头(T_QualityInspection，单号QCyyyyMMdd-NNNN)，记录工单工序的检验类型(受入/工程内/最终/出货前)、抽样数与合格判定及手直/特采/返品/废弃处置。
- `CP6.Entity/DomainModels/Mes/QualityInspectionItem.cs` — 品质检验单明细行(T_QualityInspectionItem)，逐项记录单项检验的规格值/上下限、实测值(数值或目视文本)与单项合格判定。
- `CP6.Entity/DomainModels/Mes/WorkOrderMaterial.cs` — 制造工单材料明细(T_WorkOrderMaterial)，按工单+工序+材料维护计划用量/实际消耗量、工序材料区分(仕掛品/连产品/原料/印刷原纸)及手配状况。
- `CP6.Entity/DomainModels/Mes/Machine.cs` — 设备(号机)主数据(M_Machine)，维护设备种别、所属工序/WG/拠点、状态、计划稼动时间与理论节拍/产能等OEE计算参数及维护计划日期。
- `CP6.Entity/DomainModels/Mes/MachineDowntime.cs` — 设备停机记录单据(T_MachineDowntime，单号DTyyyyMMdd-NNNN)，记录设备停机起止时段、停机区分(计划停机/故障/待料/缺员)及理由，作为OEE可用率计算原始数据。
- `CP6.Entity/DomainModels/Mes/OeeDaily.cs` — OEE设备综合效率日次汇总(T_OeeDaily)，按日期+设备记录计划/实际稼动时间、良品/不良数并算出可用率×性能×品质三率及OEE综合值。
- `CP6.Entity/DomainModels/Mes/WorkOrder.cs` — 制造工单头(T_WorkOrder，单号WOyyyyMMdd-NNNN)，由受注展开或手工创建的生产指图，含九态状态机(下书き/确定/发行/着手/完了/中断/检查/取消)及生产/完了/不良累计数量。
- `CP6.Entity/DomainModels/Mes/WorkCenter.cs` — 工作中心主数据(T_WorkCenter，A2)，承载工时费率与日可用产能(h/日)挂载点，产能字段为CRP地基。
- `CP6.Entity/DomainModels/Mes/ProcessCostRate.cs` — 工序费率主数据(T_ProcessCostRate，A2)，按工作中心×生效区间维护人工费率与制造费率(元/h)的版本化双率。
- `CP6.Entity/DomainModels/Mes/WorkOrderProcess.cs` — 制造工单工序明细(T_WorkOrderProcess)，按工单+工序+作业展开排程，含工序状态、号机/WG、计划实绩时间、良品/不良累计及A2实绩机时/人工工时(派生可覆盖)。
- `CP6.Entity/DomainModels/Mes/ProductionResult.cs` — 制造实绩报工记录(T_ProductionResult，单号PRyyyyMMdd-NNNN)，按时序累积工单工序的开始/中断/再开/完了/数量报告，含良品不良数、损耗率及A2显式工时覆盖。

#### DomainModels/Plan
- `CP6.Entity/DomainModels/Plan/Plan_ItemPlanningPolicy.cs` — 品目计划策略主数据(Plan_ItemPlanningPolicy，MRP P1)，按品目维护安全库存、采购提前期、批量规则(LFL/MOQ/倍数/整卷)与自制采购区分，缺则合成默认策略。
- `CP6.Entity/DomainModels/Plan/Plan_MrpRun.cs` — MRP运算批次(Plan_MrpRun)，一次regenerative运算一条记录，记运算批号/时刻/范围JSON及运算中/已完成/失败状态，下属计划订单与净需求以批次ID关联。
- `CP6.Entity/DomainModels/Plan/Plan_PlannedOrder.cs` — MRP计划订单(Plan_PlannedOrder)，净需求>0时按批量规则生成的采购/生产供给建议，含建议/确认/转单/忽略状态及转单回填的下游PR或工单号。
- `CP6.Entity/DomainModels/Plan/Plan_Pegging.cs` — MRP钉住关系(Plan_Pegging)，将计划订单回溯至其需求来源(受注/MPS/上级计划订单)及各来源贡献量，供"为何要这张计划订单"的全链追溯。
- `CP6.Entity/DomainModels/Plan/Plan_NetRequirement.cs` — MRP净需求明细留痕(Plan_NetRequirement)，逐品目×日桶记录毛需求-库存-在途-在制-已确认供给-安全库存的净算过程，供看板钻取毛-供给-净。

#### DomainModels/Pub
- `CP6.Entity/DomainModels/Pub/GenColumn.cs` — 代码生成器的列元数据(属性名/CLR类型/显示名/是否必填/列表与表单可见性/排序)，隶属某张GenTable，驱动实体与界面的自动生成。
- `CP6.Entity/DomainModels/Pub/GenTable.cs` — 代码生成器的表元数据(实体类名/模块/数据库表名/权限资源键/采番业务键/REST路由/菜单名)，是一键生成实体、API与菜单的配置源。
- `CP6.Entity/DomainModels/Pub/Pub_Attachment.cs` — 统一附件主数据，凭BizType+BizId挂到任意业务单据，支持MD5秒传与引用计数物理删、草稿期DraftToken暂存后回填。
- `CP6.Entity/DomainModels/Pub/Pub_DocSequence.cs` — 富采番规则配置(前缀+日期段+流水补零+按日/月/年周期重置)，按业务键为各单据自动生成单号。

#### DomainModels/Pur
- `CP6.Entity/DomainModels/Pur/SupplierPrice.cs` — 采购价表(供应商×物料的阶梯价+有效期)，建PO时按数量与生效日解析适用单价，价源含手工维护与询价回写。
- `CP6.Entity/DomainModels/Pur/PurchaseOrder.cs` — 采购订单头(发注书)，含PoStatus状态枚举，标准采购与外注委托同表(Type区分)，冻结供应商/币种/汇率快照，状态由三累计锚派生。
- `CP6.Entity/DomainModels/Pur/PurchaseOrderLine.cs` — 采购订单行，承载数量/单价/税额，并以累计收货量/验收量/已开票量三累计锚作为收货回写与三单匹配的共同基准。
- `CP6.Entity/DomainModels/Pur/GoodsReceipt.cs` — 收货单(头+行+GrStatus状态枚举)，采购侧不写物理库存(单向委托WMS落库回填入库单号)，按着荷/检收双基准处理验收与QC。
- `CP6.Entity/DomainModels/Pur/ThreeWayMatch.cs` — 三单匹配记录(头+行+MatchStatus枚举)，对齐PO/收货验收/供应商发票，容差内自动建应付、超容差挂起人工裁决。
- `CP6.Entity/DomainModels/Pur/MatchTolerance.cs` — 三单匹配容差配置(数量/价格百分比容差+金额绝对放行)，可按供应商配置且优先于全局缺省。
- `CP6.Entity/DomainModels/Pur/PurchaseRequest.cs` — 采购申请头(PR，需求入口，含PrStatus状态与PrSource来源常量)，支持手工/缺料反流/工单缺料三种来源，批准后转PO。
- `CP6.Entity/DomainModels/Pur/PurchaseRequestLine.cs` — 采购申请行，记申请量/要求交期/估价与建议供应商，转PO时按建议供应商分组拆单并回填转出PO号以追溯需求。
- `CP6.Entity/DomainModels/Pur/RfqLine.cs` — 询价行(RFQ行，"买什么")，一物料一行记询价量与交期，并以来源PR号/行号实现回到源头需求的行级追溯。
- `CP6.Entity/DomainModels/Pur/RfqSupplier.cs` — 询价单被邀供应商("问谁"，含RfqInviteStatus邀请状态枚举)，复用业务伙伴发注先，记录待邀/已邀/已报价/拒绝的邀请生命周期。
- `CP6.Entity/DomainModels/Pur/RfqQuote.cs` — 报价("各家答什么"，供应商×询价行的报价矩阵)，含报价单价/交期/有效期及比价名次与选中标记。
- `CP6.Entity/DomainModels/Pur/Rfq.cs` — 询价单头(RFQ价格发现机制，含RfqStatus状态枚举)，聚合询价行/被邀供应商/报价三身，多家比价选定后转PO并回写价表。
- `CP6.Entity/DomainModels/Pur/PoConsignMaterial.cs` — 外注有偿支给材追踪，挂在外注PO成品行下记应发/已发量与内部入库成本，以已发量为防吞料对账锚点并回填WMS出库单号。

#### DomainModels/Sys
- `CP6.Entity/DomainModels/Sys/Sys_Dept.cs` — 部门组织树节点主数据，含物化路径与负责人，支撑PUB数据权限子树过滤和OA"部门长"审批路由。
- `CP6.Entity/DomainModels/Sys/Sys_DictData.cs` — 数据字典项明细，挂在某字典类型下保存值编码与显示文本(如1=男)。
- `CP6.Entity/DomainModels/Sys/Sys_DictType.cs` — 数据字典类型主数据(如gender/status)，定义字典分类编码与名称。
- `CP6.Entity/DomainModels/Sys/Sys_Lang.cs` — 多语言词条表，一行一个key含简繁中英日韩六语译文，支持租户覆盖回退与审校状态。
- `CP6.Entity/DomainModels/Sys/Sys_Menu.cs` — 系统菜单/页面定义，含前端路由、稳定业务键MenuKey与父子层级，作为权限资源键前缀。
- `CP6.Entity/DomainModels/Sys/Sys_MenuAction.cs` — 菜单操作点(按钮/功能点)，定义某菜单下可授权的query/add/edit/delete/export等操作。
- `CP6.Entity/DomainModels/Sys/Sys_Role.cs` — 系统角色主数据，含自定义RoleId、名称与描述。
- `CP6.Entity/DomainModels/Sys/Sys_RoleAction.cs` — 角色-操作点授权记录，存某角色对某菜单某操作的授权，多角色聚合取并集。
- `CP6.Entity/DomainModels/Sys/Sys_RoleDataScope.cs` — 角色数据范围配置，定义角色对某资源的可见数据范围(本人/本部门/含下级/自定义/全部)，多角色取最宽。
- `CP6.Entity/DomainModels/Sys/Sys_RoleFieldPerm.cs` — 角色字段权限配置，定义角色对某资源某字段的访问级(可读写/只读/隐藏)，多角色取最可见。
- `CP6.Entity/DomainModels/Sys/Sys_RoleMenu.cs` — 角色-菜单多对多映射表，记录角色可访问的菜单/页面。
- `CP6.Entity/DomainModels/Sys/Sys_UserRole.cs` — 用户-角色多对多中间表，支撑PUB多角色RBAC(操作并集、数据/字段最宽合并)。
- `CP6.Entity/DomainModels/Sys/Sys_Tenant.cs` — 租户主数据/注册表，每租户一行其Id即被各业务表引用的TenantId，供登录消歧与后台Worker按租户循环。
- `CP6.Entity/DomainModels/Sys/Sys_OperLog.cs` — 操作日志表，自动记录谁在何时对哪个接口做了什么(请求方法/路径/参数/状态码/耗时/IP)，按租户隔离含告警标记。
- `CP6.Entity/DomainModels/Sys/Sys_User.cs` — 系统用户主数据，含登录账号/密码哈希/角色/部门/上级，及认证加固的失败计数、锁定、强制改密等密码安全画像字段。
- `CP6.Entity/DomainModels/Sys/Sys_PasswordHistory.cs` — 历史密码哈希表，改密时旧BCrypt哈希入库，用于校验新密码不得与最近N条重用。
- `CP6.Entity/DomainModels/Sys/SecurityEventType.cs` — 安全事件类型枚举(登录成败/账号锁定/登出/改密/令牌刷新/令牌重用检测/权限拒绝)，作为安全日志的EventType取值。
- `CP6.Entity/DomainModels/Sys/Sys_SecurityLog.cs` — 安全事件审计日志，独立记录/api/auth的认证类事件(因全局操作日志主动跳过该路径防密码泄露)，按租户隔离。

#### DomainModels/Wf
- `CP6.Entity/DomainModels/Wf/Wf_ApprovalBinding.cs` — 审批绑定，把业务类型BizType映射到审批流程FlowKey，业务侧提交时据此启流程，一业务类型一条启用绑定。
- `CP6.Entity/DomainModels/Wf/Wf_FlowDelegate.cs` — 审批委派配置，委托人在有效期内把审批权交给代理人，引擎建待办时把审批人替换为代理人并双记代办痕迹。
- `CP6.Entity/DomainModels/Wf/Wf_FlowTask.cs` — 流程待办任务，一节点可建多条(会签多人各一)，记处理人/状态/会签规则/加签来源/到期时间，是动作幂等闸门与超时扫描依据。
- `CP6.Entity/DomainModels/Wf/Wf_FormDef.cs` — 表单定义，以SchemaJson驱动前端动态渲染与后端字段复核，含稳定FormKey、版本号与软停用开关。
- `CP6.Entity/DomainModels/Wf/Wf_FlowHistory.cs` — 流程审批痕迹，每次动作(提交/同意/驳回/撤回/挂起等)仅追加一条，构成不可更新的审批时间线。
- `CP6.Entity/DomainModels/Wf/Wf_FlowInstance.cs` — 流程实例(状态机的状态载体)，记当前节点/实例总态/发起人，并以VarsJson保存表单字段值快照供条件流转取值。
- `CP6.Entity/DomainModels/Wf/Wf_FormData.cs` — 表单数据，一次提交一行以DataJson存字段值快照，经BizId与流程实例/业务单号关联并留存提交时的表单版本。
- `CP6.Entity/DomainModels/Wf/Wf_FlowDef.cs` — 流程定义，以SchemaJson描述节点+边有向图驱动引擎状态机，绑定一张表单并以版本号管理改版。

#### DomainModels/Wms
- `CP6.Entity/DomainModels/Wms/Location.cs` — 仓位(棚位)主数据，5级层次(区域→通路→货架→层→料格)自引用树，含坐标/容量/拣货可否/冻结/条码。
- `CP6.Entity/DomainModels/Wms/StockTransaction.cs` — 库存流水不可变日志(INSERT-only)，IN/OUT/MOVE/ADJ/RSV/UNRSV六类记账，含原纸卷号与消耗米长等纸器扩展字段，是统计报表唯一真源。
- `CP6.Entity/DomainModels/Wms/WmsSequence.cs` — WMS单据号采番表，按(前缀+日期键)保存NextNo自增(IN/OUT/SHIP/ROLL/TXN等)。
- `CP6.Entity/DomainModels/Wms/InboundOrder.cs` — 入库预定(预约)单头，区分采购/外注返入/退货入库，1头N明细，按预定→部分入库→完成流转。
- `CP6.Entity/DomainModels/Wms/InboundOrderDetail.cs` — 入库预定单明细，记录预定数量与累计已收数量(每次实绩登录累加)及预定仓位。
- `CP6.Entity/DomainModels/Wms/InboundReceipt.cs` — 入库实绩单头(实收)，可参照预定或直入，确定时按明细逐条调用StockMovementService发IN流水。
- `CP6.Entity/DomainModels/Wms/InboundReceiptDetail.cs` — 入库实绩明细，记录实收数量/批次/落位仓位/有效期/原纸卷号，确定后回填发出的TXN号供追溯。
- `CP6.Entity/DomainModels/Wms/OutboundOrder.cs` — 出库指示单头(材料出库/出荷/社内调拨)，关联MES制造指图或受注，按确定→引当→拣货→完成流转。
- `CP6.Entity/DomainModels/Wms/ShippingPackage.cs` — 出荷打包(梱包)单，出荷型出库确定时生成，记录箱数/重量/体积/配送商/追踪号。
- `CP6.Entity/DomainModels/Wms/StockTake.cs` — 盘点(实地盘点)单头，区分全盘/循环盘/临时盘，4阶段流程含差异金额阈值与上级审批。
- `CP6.Entity/DomainModels/Wms/StockTakeDetail.cs` — 盘点明细，计划时快照账面数(BookQty)、录入实盘数算差异数/金额，审批后发ADJ流水覆盖在库。
- `CP6.Entity/DomainModels/Wms/QcInspection.cs` — 入荷检品(QC收货)单头，PASS/CONDITIONAL/HOLD/FAIL/RETURN五种判定，合格时自动生成入库实绩号。
- `CP6.Entity/DomainModels/Wms/QcInspectionItem.cs` — 入荷检品明细(简易检品)，记录预定/到货/合格/不良/保留数量及不良原因，CheckItemsJson可扩展AQL详情。
- `CP6.Entity/DomainModels/Wms/RmaHeader.cs` — 退货管理(RMA)单头，含客户/原出荷号/退货理由，按申请→发号→退货入库→检查→判定→后处理完成的5段生命周期。
- `CP6.Entity/DomainModels/Wms/RmaDetail.cs` — RMA退货明细，按行判定振分(再販/修理/廃棄/退供应商)，记录商品状态与入库及振分的TXN号。
- `CP6.Entity/DomainModels/Wms/KitMaster.cs` — 套件(组合品)主数据，以KitSku为业务主键，1套件对应N构成部品BOM。
- `CP6.Entity/DomainModels/Wms/KitMasterComponent.cs` — 套件构成部品(BOM)行，定义每套组装所需部品及数量。
- `CP6.Entity/DomainModels/Wms/KitOrder.cs` — 套件组立/拆解指示单，ASSEMBLE(部品OUT+套件IN)或DISASSEMBLE(套件OUT+部品IN)，记录执行TXN号串。
- `CP6.Entity/DomainModels/Wms/CrossDockOrder.cs` — 越库(Cross-Dock)直转指示，入库品不上架直接转出荷码头，执行时成对发IN+OUT使在库滞留≈0。
- `CP6.Entity/DomainModels/Wms/ReplenishOrder.cs` — 补货指示单，从保管棚(后棚)向拣货棚(前棚)补充，执行时发MOVE对(OUT+IN)，支持日次批量按低于MinQty生成。
- `CP6.Entity/DomainModels/Wms/SlottingPlan.cs` — 库位优化(Slotting)方案，基于过去N日OUT频次做ABC帕累托分析推荐货位，仅记录"提案"JSON不直接发移库。
- `CP6.Entity/DomainModels/Wms/PaperRoll.cs` — 瓦楞原纸卷管理，1卷=1独立在库，以巾幅×流向(T目/Y目)×残米长×芯径识别，核心是残米长减算与低阈值废弃候选化。
- `CP6.Entity/DomainModels/Wms/InkLot.cs` — 油墨/胶粘剂批次管理，含色号/墨种(胶印/柔印/UV)/开封状态/有效期/粘度/固形分，支持新旧批次混合(继承较早有效期)。
- `CP6.Entity/DomainModels/Wms/InkColorMatchHistory.cs` — 油墨色配(调色)历史，按客户×色号留存配方JSON(色+比率)与消耗量，供同色再现复用。
- `CP6.Entity/DomainModels/Wms/Pallet.cs` — 成品瓦楞托盘管理，1托盘1产品1批次(禁混载)，含箱数/重量/堆叠层数限制，主打防垮垛+FIFO+出荷待机区管理。
- `CP6.Entity/DomainModels/Wms/VmiBilling.cs` — VMI客户寄存库存月度保管料计算结果，按客户×年月一笔，用平均在库×单价×天数算请求金额。
- `CP6.Entity/DomainModels/Wms/RemnantMaterial.cs` — 残材(端材)再利用管理，登记原纸残卷/印后余白/断裁端料的尺寸×材质，供小批量订单按尺寸范围检索转用。
- `CP6.Entity/DomainModels/Wms/PlateMoldStock.cs` — 印版/打抜木型(刀模)资产与使用履历，按客户×产品×版型一条，管理最大寿命冲次/累计冲次与维护要请。
- `CP6.Entity/DomainModels/Wms/SampleStock.cs` — 样品(试作/色样/Dummy)在库与借出管理，按保管→借出→返却/失效流转，含借出对象与返还预定日。
- `CP6.Entity/DomainModels/Wms/WcsTask.cs` — WCS仓库控制系统任务(发给传送带/AGV/自动仓ASRS的MOVE/PICK/PUT/COUNT指令)，含设备号与Created→Dispatched→Executing→Completed状态机。
- `CP6.Entity/DomainModels/Wms/CarrierShipment.cs` — 配送商API联动的发货单(雅玛多/佐川/日邮等)，含追踪号/服务种别/运费及Created→PickedUp→InTransit→Delivered状态与事件JSON。
- `CP6.Entity/DomainModels/Wms/IotSensorReading.cs` — 含两个实体：IoT传感器主数据(温湿度/冲击/货架，T_IotSensor，含上下限阈值)与其时序计测值日志(T_IotSensorReading，含超阈警报)。
- `CP6.Entity/DomainModels/Wms/MobileTask.cs` — 移动作业指示(RF手持/平板WMS)，1屏1作业(收货/上架/拣货/盘点/移库/贴标)，MOVE型完成时发MOVE流水对。
- `CP6.Entity/DomainModels/Wms/MaterialShortage.cs` — 材料出库缺料回写记录，关联工单/出库号记录所需量与可用量及OPEN/RESOLVED/DISMISSED状态(内含MaterialShortageStatus常量类)。
- `CP6.Entity/DomainModels/Wms/Stock.cs` — 库存实况核心表(WMS中枢)，业务UK=(仓库,仓位,产品,批次)，物理/引当/可用三数量，含有效期FEFO/召回标志/VMI所有者/原纸卷/QC状态(含StockQcStatus可分配判定)。
- `CP6.Entity/DomainModels/Wms/WmsTxnType.cs` — WMS枚举常量汇总文件，集中定义流水种别/仓库区分/所有者/各单据状态/检品判定/RMA/套件方向等多个静态常量类。
- `CP6.Entity/DomainModels/Wms/OutboundOrderDetail.cs` — 出库指示明细，FIFO+期限优先引当确定批次与仓位并加引当量，出库时回填OUT流水号，含多仓路由实引当仓库列。
- `CP6.Entity/DomainModels/Wms/OutboundRoutingRule.cs` — 出库多仓引当路由规则(Gap4.2/T14)，按客户/产品码前缀/出库区分匹配优先目标仓库，null=通配，SortOrder决定优先序。
- `CP6.Entity/DomainModels/Wms/Warehouse.cs` — 仓库主数据，含仓库区分(原料/半成品/成品/不良/外注)、出库引当优先度、允许负库存等，1仓库N仓位N库存。

#### DTOs/Erp
- `CP6.Entity/DTOs/Erp/BackorderDto.cs` — 受注残注(Backorder)队列的查询条件、明细行与残注切出操作请求/结果，用于残注一览与残注分割。
- `CP6.Entity/DTOs/Erp/BusinessPartnerDto.cs` — PA110/PA120/PA100取引先主数据全Tab详情、一览检索条件/行、FSC检查表发行请求/结果及FLG不可变更校验的取引先管理DTO集。
- `CP6.Entity/DTOs/Erp/CreditNoteDto.cs` — 退货/折让贷项票(Credit Note)的一览查询条件与一览行DTO。
- `CP6.Entity/DTOs/Erp/EstimateCalcDto.cs` — 见积计算书的详情(主表+工程明细)、分页查询、计算结果与一览行的见积原价计算DTO集。
- `CP6.Entity/DTOs/Erp/MasterLookupDtos.cs` — 通用主数据参照弹窗的泛型结果框与得意先/制品查找行，用于检索对话框的DTO集。
- `CP6.Entity/DTOs/Erp/OrderCancelDto.cs` — 受注取消(Phase6)的结果区分、最终结果、关联WorkOrder/Outbound可取消探针及Bridge钩子返回值的受注取消联动DTO集。
- `CP6.Entity/DTOs/Erp/OrderDto.cs` — PA070/080/090受注头+明细+工程+材料一括POST/PUT的详情DTO与一览检索条件/行、与信/仕掛/可编辑校验、单价订正批更新的受注管理DTO集。
- `CP6.Entity/DTOs/Erp/OrderTraceDto.cs` — 单受注跨模块联动(Bridge Hook)的汇总摘要与时间线，用于受注追溯可视化的DTO集。
- `CP6.Entity/DTOs/Erp/OtdReportDto.cs` — 按客户/月别的纳期遵守率(OTD)报表的查询条件、整体摘要与集计行DTO集。
- `CP6.Entity/DTOs/Erp/PlateMoldDto.cs` — PA140/PA150版型/木型主数据的单件详情(含Rev改定)、改定履历、一览检索条件/行、受注联动/PE API联动结果的版型管理DTO集。
- `CP6.Entity/DTOs/Erp/QuotationDto.cs` — MSBBPA030/040御见积书的详情(头+见积计算书关联+打印明细)、分页查询/一览行、关联计算书候选、确定登录/报表发行请求的见积书管理DTO集。
- `CP6.Entity/DTOs/Erp/SheetUnitPriceDto.cs` — PA130纸板单价(标准/见积用)的一览行、导入结果、批更新请求与查询条件的纸板单价导入/更新DTO集。
- `CP6.Entity/DTOs/Erp/UnshippedOrderDto.cs` — 仪表盘未出荷受注的一览行(聚合MES/WMS状态、含延迟判定)与查询条件的未出荷监控DTO集。
- `CP6.Entity/DTOs/Erp/ProductDto.cs` — MSBBPA050/060制品主数据5表一括(部材/基本信息/工程/材料/批别单价)详情DTO与一览行、检索条件、仕掛校验结果的制品主数据管理DTO集。

#### DTOs/Integration
- `CP6.Entity/DTOs/Integration/BridgeHealthDto.cs` — 模块间Bridge Hook健全性指标(按钩子统计/队列深度/死信一览)的联动监控仪表盘DTO集。
- `CP6.Entity/DTOs/Integration/BridgeMetricsSnapshot.cs` — 供Prometheus /metrics输出、从IntegrationEvent聚合的桥接指标快照(按钩子件数/待重试/死信数)DTO。

#### DTOs/Mes
- `CP6.Entity/DTOs/Mes/PagedResultDto.cs` — MES一览查询专用的通用泛型分页结果(总件数+页+Items)DTO。
- `CP6.Entity/DTOs/Mes/ProductionResultDto.cs` — ME040/050制造实绩的显示行、实绩录入请求、一览检索条件及由受注自动展开指图请求的制造实绩登录DTO集。
- `CP6.Entity/DTOs/Mes/WorkOrderDto.cs` — ME020/030制造指图的一览/详情(含工序明细、材料明细、进度率/延迟天数)与检索条件的制造指图管理DTO集。
- `CP6.Entity/DTOs/Mes/DefectRecordDto.cs` — ME080不良品记录的详情、检索条件及不良分类主数据的不良是正管理DTO集。
- `CP6.Entity/DTOs/Mes/MachineDto.cs` — 设备主数据、设备停机记录、OEE日次/检索/重算的设备稼动与OEE管理DTO集。
- `CP6.Entity/DTOs/Mes/MesDashboardDto.cs` — ME090 MES仪表盘的本日KPI、工序别进度、纳期延迟告警、按日推移、不良TOP5、近期完工、设备稼动热力图的仪表盘可视化DTO集。
- `CP6.Entity/DTOs/Mes/PlanningBoardDto.cs` — ME010生产计划看板的甘特条、检索条件、KPI摘要、拖拽再排程/自动配置请求的计划甘特DTO集。
- `CP6.Entity/DTOs/Mes/QualityInspectionDto.cs` — ME060/070品质检验的头、项目明细、检验模板、一览检索条件的品质检验管理DTO集。
- `CP6.Entity/DTOs/Mes/PlanAchievementDto.cs` — 生产计划达成率报表(按产品/月/客户)的集计轴常量、查询条件、整体摘要与集计行报表DTO集。

#### DTOs/Sys
- `CP6.Entity/DTOs/Sys/DataScopeDtos.cs` — 数据权限资源注册表项与角色别数据范围(范围种别+自定义部门Id)的数据范围设置DTO集。
- `CP6.Entity/DTOs/Sys/DeptDtos.cs` — PUB部门CRUD请求、部门树节点、用户组织字段维护的组织/部门管理DTO集。
- `CP6.Entity/DTOs/Sys/FieldPermDtos.cs` — 可控字段定义、字段权限资源、角色别字段权限(读写/只读/隐藏)的字段级权限设置DTO集。
- `CP6.Entity/DTOs/Sys/RolePermDtos.cs` — PUB菜单操作点定义、角色功能权限(菜单集合+操作点集合)的角色功能权限设置DTO集。
- `CP6.Entity/DTOs/Sys/UserRoleDtos.cs` — PUB给用户分配角色(全角色Id+主角色Id)的用户角色设置DTO。
- `CP6.Entity/DTOs/Sys/LoginRequest.cs` — 含用户名/密码+租户编码(多租户同名消歧用)的登录请求参数DTO。

#### DTOs/Wms
- `CP6.Entity/DTOs/Wms/StockDwellDto.cs` — 按产品/客户的库存滞留(龄期分桶0-30/31-60/61-90/90以上)查询条件、整体摘要与集计行的库存滞留分析DTO集。
