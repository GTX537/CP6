### 八、cp6.web/src/views — 前端页面（视图）

#### views/（根级）

- `cp6.web/src/views/LayoutView.vue` — 应用程序的主布局容器，管理导航菜单、顶部栏和整体界面结构，支持桌面端和移动端适配。
- `cp6.web/src/views/LoginView.vue` — 用户登录页面，包含用户名密码表单和动画背景效果。

#### views/dashboard

- `cp6.web/src/views/dashboard/DashboardView.vue` — 业务仪表盘，展示系统关键指标卡片和实时业务通知，支持跳转到相关业务模块。
- `cp6.web/src/views/dashboard/HomeView.vue` — 首页欢迎页，展示欢迎信息组件。

#### views/erp

- `cp6.web/src/views/erp/BackorderListView.vue` — 缺货订单列表，支持按客户和日期搜索，可执行关闭或分割操作。
- `cp6.web/src/views/erp/BusinessPartnerListView.vue` — 业务伙伴列表，支持按多个角色标志和信息条件搜索过滤。
- `cp6.web/src/views/erp/BusinessPartnerView.vue` — 业务伙伴详情编辑页，支持新建、编辑、删除等操作和9个角色属性标签。
- `cp6.web/src/views/erp/CreditNoteListView.vue` — 信用单（退货/换货/报废）列表，支持按客户、订单、类型和日期搜索查询。
- `cp6.web/src/views/erp/EstimateCalcListView.vue` — 估价单列表，支持新建、编辑、复制、删除等操作。
- `cp6.web/src/views/erp/EstimateCalcView.vue` — 估价单编辑向导，分3步骤处理基本信息、工程、结果数据。
- `cp6.web/src/views/erp/FscChecklistView.vue` — FSC检查清单列表，支持按基地、员工、报价日期等条件搜索和批量下达。
- `cp6.web/src/views/erp/FxRateView.vue` — 汇率维护页面，支持创建、编辑、删除货币汇率记录。
- `cp6.web/src/views/erp/OrderCancelDialog.vue` — 订单取消对话框，分2阶段（探查、决策）处理关联工单和出库单。
- `cp6.web/src/views/erp/OrderEntryView.vue` — 受注订单输入编辑向导，分3步骤处理表头明细、基本信息、工程信息。
- `cp6.web/src/views/erp/OrderListView.vue` — 订单列表查询，支持多维度条件搜索和CSV导出。
- `cp6.web/src/views/erp/OrderPriceCorrectionView.vue` — 订单价格修正列表，支持批量修改个别和套装单价及特值标志。
- `cp6.web/src/views/erp/OrderTraceView.vue` — 订单生命周期事件追踪页面，显示工单事件时间线和统计指标。
- `cp6.web/src/views/erp/OtdReportView.vue` — 按时交货率（OTD）报表，按客户或月份分组展示交货统计。
- `cp6.web/src/views/erp/PlateMoldListView.vue` — 版模列表查询，支持按版号、版名、客户等搜索和批量下标签。
- `cp6.web/src/views/erp/PlateMoldView.vue` — 版模详情编辑页，包含基本信息、构成、供应商位置等多个标签页。
- `cp6.web/src/views/erp/ProductMasterListView.vue` — 产品主数据列表，支持多条件搜索和CSV导出。
- `cp6.web/src/views/erp/ProductMasterView.vue` — 产品主数据编辑向导，分5步骤处理零件表、基本信息、工程、物料、价格。
- `cp6.web/src/views/erp/QuotationListView.vue` — 报价单列表，支持按报价号、日期、基地等条件搜索。
- `cp6.web/src/views/erp/QuotationView.vue` — 报价单详情编辑页，包含表头、详细行、附加信息等多个标签页。
- `cp6.web/src/views/erp/SheetUnitPriceView.vue` — 纸板单价管理页面，支持Excel导入或按客户员工查询参考模式。

#### views/erp/bp（业务伙伴属性标签页）

- `cp6.web/src/views/erp/bp/ApTab.vue` — 支付预定管理先（应付）的代码和部门信息编辑表单。
- `cp6.web/src/views/erp/bp/ArTab.vue` — 销售应收账款先的代码和名称编辑表单。
- `cp6.web/src/views/erp/bp/BasicInfoTab.vue` — 业务伙伴基本信息编辑，包括企业号、地址、9种属性标志、10个分类和3项销售分析。
- `cp6.web/src/views/erp/bp/BillingTab.vue` — 销售应收（请求）先信息编辑，含账单地址、打印区分、送付详情。
- `cp6.web/src/views/erp/bp/CustomerTab.vue` — 得意先基本信息和销售计算规则编辑，包括19项销售计算和5项纳品书配置。
- `cp6.web/src/views/erp/bp/DeliveryTab.vue` — 物流配送（纳入）信息编辑，含配送地点、部署、联系人、时间窗口等。
- `cp6.web/src/views/erp/bp/PaySchTab.vue` — 支付预定管理先代码编辑表单。
- `cp6.web/src/views/erp/bp/PaymentTab.vue` — 支付先和部门信息编辑，包括支付截日、税计算、批处理预定配置。
- `cp6.web/src/views/erp/bp/ReceiptTab.vue` — 入金先信息编辑，含汇款人、银行账户、领收书送付地址。
- `cp6.web/src/views/erp/bp/SupplierTab.vue` — 发货（仕入）先信息编辑，含供应商模式、下包对象、支给分类、税金及历法设置。

#### views/erp/estimate（估价计算向导步骤）

- `cp6.web/src/views/erp/estimate/Step1BasicInfo.vue` — 估价计算基本信息录入向导步骤，含拟价号、商品、基地、得意先、品名分类、受注数量、材质及工程规格。
- `cp6.web/src/views/erp/estimate/Step2Processes.vue` — 估价计算工程明细编辑向导步骤，支持添加工程行、规格、版号、备注及回收法规模板选择。
- `cp6.web/src/views/erp/estimate/Step3Result.vue` — 估价计算结果显示向导步骤，展示计算摘要、分量级金额、计算逻辑透明化及再计算功能。

#### views/erp/order（受注订单向导步骤）

- `cp6.web/src/views/erp/order/Step1HeaderAndDetails.vue` — 受注单头和明细编辑向导步骤，含受注类型、客户、数量、部材列表及库存状态。
- `cp6.web/src/views/erp/order/Step2BasicInfo.vue` — 受注明细基本信息编辑向导步骤，包括配送地、客户品名、材料构成、尺寸及购买信息。
- `cp6.web/src/views/erp/order/Step3ProcessInfo.vue` — 受注明细工程和材料编辑向导步骤，支持工程、工程备注及材料行的添加删除和导入。

#### views/erp/product（产品主数据向导步骤）

- `cp6.web/src/views/erp/product/Step1TargetSelect.vue` — 制品主数据部材选择向导步骤，含部材列表、行操作及仕掛检查状态展示。
- `cp6.web/src/views/erp/product/Step2BasicInfo.vue` — 制品主数据基本信息编辑向导步骤，含品名、品番、纸构成、尺寸、销价、FSC容环等。
- `cp6.web/src/views/erp/product/Step3ProcessInfo.vue` — 制品主数据工程编辑向导步骤，含工程详情、规格、版号、副资材及10项制造顺优先和连产品设置。
- `cp6.web/src/views/erp/product/Step4MaterialSetting.vue` — 制品主数据材料设置向导步骤，管理工程关联的主材料、副资材和梱包材的行表。
- `cp6.web/src/views/erp/product/Step5LotPriceOther.vue` — 制品主数据分级定价和其他信息编辑向导步骤，包括分级单价、购买单价、仕掛检查结果。

#### views/fin（财务会计）

- `cp6.web/src/views/fin/ApAgingView.vue` — 应付账龄分析表，按到期日分桶统计未付发票金额并进行子账与总账勾稽。
- `cp6.web/src/views/fin/ApInvoiceView.vue` — 应付发票列表，支持录入、编辑、过账生成凭证、红字冲回等操作。
- `cp6.web/src/views/fin/ArAgingView.vue` — 应收账龄分析表，按到期日分桶统计未收发票金额并进行子账与总账勾稽。
- `cp6.web/src/views/fin/ArInvoiceView.vue` — 应收发票列表，支持录入、过账生成凭证、红字冲回及自动开票操作。
- `cp6.web/src/views/fin/AssetCardView.vue` — 固定资产卡片台账，展示资产原值、累计折旧、净值及激活、查看折旧计划等操作。
- `cp6.web/src/views/fin/AssetCategoryView.vue` — 资产分类主档，定义资产分类、折旧方法、有用生命周期等默认参数。
- `cp6.web/src/views/fin/AssetDepreciationView.vue` — 折旧计提向导，试算、计提、过账固定资产按期折旧。
- `cp6.web/src/views/fin/AssetDisposalView.vue` — 资产处置单，记录出售、报废、转让、盘亏等资产清理和损益结转。
- `cp6.web/src/views/fin/BalanceSheetView.vue` — 资产负债表，展示某时点资产、负债、所有者权益构成及平衡校验。
- `cp6.web/src/views/fin/BankImportProfileView.vue` — 银行导入模板管理，配置银行流水文件格式、编码、金额解析、跳过行数等参数。
- `cp6.web/src/views/fin/BankReconciliationView.vue` — 银行对账工作台，支持自动匹配、手工匹配、生成凭证、标记未勾项、锁定对账单。
- `cp6.web/src/views/fin/BankStatementView.vue` — 银行流水单管理，维护银行对账单及其交易明细，支持逐笔录入和展开查看。
- `cp6.web/src/views/fin/BudgetEditView.vue` — 预算编制工作台，管理预算方案、版本及预算科目行明细。
- `cp6.web/src/views/fin/BudgetVsActualView.vue` — 预算执行分析表，按财年、版本、期间对比预算与实际数据。
- `cp6.web/src/views/fin/CostSheetView.vue` — 成本核算单，从MES真实消耗和BOM单价计算工料费，完工结转WIP至FG。
- `cp6.web/src/views/fin/GlAccountView.vue` — 会计科目表，定义总账科目、科目类型、借贷方向、末级标记及控制科目关联。
- `cp6.web/src/views/fin/IncomeStatementView.vue` — 损益表，展示一段时间的营业收入、成本、费用、毛利、净利润构成。
- `cp6.web/src/views/fin/JournalEntryView.vue` — 记账凭证列表，支持新建、提交、复核、过账、红冲，确保借贷恒等且不可改删。
- `cp6.web/src/views/fin/PaymentView.vue` — 付款单列表，支持录入付款、核销应付、尾差汇差处理、撤销红冲等操作。
- `cp6.web/src/views/fin/PeriodCloseView.vue` — 会计期间与月结管理，支持期间结账、反结账及预检查。
- `cp6.web/src/views/fin/ReceiptView.vue` — 收款单列表，支持录入收款、核销应收、尾差汇差处理、撤销红冲等操作。
- `cp6.web/src/views/fin/TrialBalanceView.vue` — 试算平衡表，展示期初、本期发生、期末余额及双层借贷平衡校验。

#### views/mes（生产制造）

- `cp6.web/src/views/mes/ControlTowerView.vue` — 生产制造控制塔面板，展示当日KPI指标、设备状态、生产进度、不良TOP5和生产趋势。
- `cp6.web/src/views/mes/DefectManagementView.vue` — 不良品管理列表，按不良号、指图号、分类等条件搜索和管理生产中产生的不良品。
- `cp6.web/src/views/mes/MachineListView.vue` — 设备管理列表，展示设备状态网格和详细表格，支持搜索、创建和编辑设备基本信息。
- `cp6.web/src/views/mes/MesDashboardView.vue` — MES总体看板，展示当日KPI、工程别进度、不良TOP5等多维生产数据分析。
- `cp6.web/src/views/mes/OeeAnalysisView.vue` — OEE综合效率分析，展示设备实时OEE、可用率、性能、品质等指标及历史趋势。
- `cp6.web/src/views/mes/PlanAchievementView.vue` — 计划达成率分析，按产品、月份或客户分组统计生产计划完成情况。
- `cp6.web/src/views/mes/PlanningBoardView.vue` — 排程看板，按日/周/月显示工序进度、交付期望、产能排程等，支持自动配置。
- `cp6.web/src/views/mes/ProcessCostRateView.vue` — 工序费率设置列表，管理工作中心的人工费率、制造费率等成本参数。
- `cp6.web/src/views/mes/ProductionResultEntryView.vue` — 制造实绩入力，按工序逐一输入生产数量、良品数、不良品等实绩数据。
- `cp6.web/src/views/mes/ProductionResultListView.vue` — 制造实绩列表，搜索和查看已输入的生产实绩记录。
- `cp6.web/src/views/mes/QualityInspectionEntryView.vue` — 品质检查入力，创建或修正品质检查单，包含检查结果和缺陷详情。
- `cp6.web/src/views/mes/QualityInspectionListView.vue` — 品质检查列表，搜索检查单并创建新检查或导出数据。
- `cp6.web/src/views/mes/WorkCenterView.vue` — 工作中心主数据管理，设定各工作中心的日可用产能和费率等基本信息。
- `cp6.web/src/views/mes/WorkOrderEntryView.vue` — 制造指图三步式入力向导，分别输入基本信息、工程计划和材料手配。
- `cp6.web/src/views/mes/WorkOrderListView.vue` — 制造指图列表，按多条件搜索工单并显示状态、优先级、交期等关键信息。

#### views/mes/work-order/steps（制造指图向导步骤）

- `cp6.web/src/views/mes/work-order/steps/WoStep1BasicInfo.vue` — 指图基本信息录入，包括指图号、拠点、优先度、受注号等初始化数据。
- `cp6.web/src/views/mes/work-order/steps/WoStep2ProcessPlan.vue` — 指图工程计划配置，编辑工序号、工程CD、计划日期、产数量等详情。
- `cp6.web/src/views/mes/work-order/steps/WoStep3MaterialConfirm.vue` — 指图材料确认，配置各工序所需材料、数量、单位和手配状态。

#### views/plan（计划/MRP）

- `cp6.web/src/views/plan/ItemPolicyView.vue` — 计划物料主数据管理，设定物料的安全库存、采购提前期、批量规则和自制采购属性。
- `cp6.web/src/views/plan/MrpBoardView.vue` — MRP运算看板，执行MRP运算并显示净需求、计划订单，支持订单确认和转换功能。

#### views/pms（平台管理/权限）

- `cp6.web/src/views/pms/AboutView.vue` — 关于页面，显示应用程序的版本与版权信息。
- `cp6.web/src/views/pms/CodeGenView.vue` — 代码生成向导，通过配置表元数据和列定义来自动生成CRUD模块代码。
- `cp6.web/src/views/pms/DataScopeView.vue` — 角色数据权限编辑页面，分配不同角色对各资源的数据权限范围（全部/本部门/自定义等）。
- `cp6.web/src/views/pms/DeptTreeView.vue` — 部门组织结构树形列表，支持新增、编辑、删除和移动部门。
- `cp6.web/src/views/pms/DictView.vue` — 数据字典管理页面，分两层展示字典类型和字典值数据，支持CRUD操作。
- `cp6.web/src/views/pms/FieldPermView.vue` — 角色字段权限编辑页面，为不同角色设置字段级的访问权限（可读写/只读/隐藏）。
- `cp6.web/src/views/pms/LangView.vue` — 多语言翻译管理页面，支持编辑多种语言版本并发布翻译版本。
- `cp6.web/src/views/pms/MenuView.vue` — 菜单管理页面，以树形结构展示和编辑系统菜单项。
- `cp6.web/src/views/pms/OperLogView.vue` — 操作日志列表，记录用户的API调用详情（请求方法、URL、状态码、耗时等），只读查看。
- `cp6.web/src/views/pms/PermissionView.vue` — 角色菜单权限编辑页面，通过树形选择为角色分配菜单访问权限。
- `cp6.web/src/views/pms/RolePermView.vue` — 角色功能权限编辑页面，为角色分配菜单权限和其下的操作点权限。
- `cp6.web/src/views/pms/RoleView.vue` — 角色列表CRUD页面，管理系统中的角色信息。
- `cp6.web/src/views/pms/SeqView.vue` — 编号序列规则管理页面，配置各业务对象的自动编号前缀、格式和重置周期。
- `cp6.web/src/views/pms/UserView.vue` — 用户列表CRUD页面，支持管理用户信息并为用户分配多个角色和设置主角色。

#### views/pur（采购）

- `cp6.web/src/views/pur/GoodsReceiptView.vue` — 采购收货单列表与新建，支持按订购量/已收货/本次收货进行数量管理。
- `cp6.web/src/views/pur/PrView.vue` — 采购申请列表，支持从手工/缺料/工单来源创建，送审后可转采购订单。
- `cp6.web/src/views/pur/PurReconcileView.vue` — 采购对账工具，通过PO号核对采购订单、收货、发票三方数据一致性并诊断异常。
- `cp6.web/src/views/pur/PurchaseOrderView.vue` — 采购订单列表编辑页，支持多供应商、多币种、阶梯价自动带出和订单状态管理。
- `cp6.web/src/views/pur/RfqView.vue` — 询价比价平台，支持从PR发起询价、邀请供应商、录入报价、排名比价、选定回写价表及转订单。
- `cp6.web/src/views/pur/SubcontractView.vue` — 外注加工作业页面，管理外注委托单的支给材登记、发料追踪和成品成本核算。
- `cp6.web/src/views/pur/SupplierPriceView.vue` — 供应商价表维护列表，支持按供应商+物料查询阶梯价并提供带价试算功能。
- `cp6.web/src/views/pur/ThreeWayMatchView.vue` — 三单匹配页面，进行采购订单、收货验收、供应商发票的核对并自动/手工建应付。

#### views/wf（工作流/审批）

- `cp6.web/src/views/wf/DynamicForm.vue` — 通用动态表单组件，按schema驱动渲染表单控件，支持规则引擎驱动显隐、必填、禁用和字段权限掩码。
- `cp6.web/src/views/wf/FlowTrace.vue` — 审批痕迹时间线组件，展示流程实例的历史记录包括动作、节点、意见和时间。
- `cp6.web/src/views/wf/MyApplications.vue` — 我的申请列表，展示用户发起的流程实例及其当前状态，支持查看痕迹和撤回。
- `cp6.web/src/views/wf/TodoCenter.vue` — 待办中心列表，展示用户需要处理的审批任务，支持打开表单并同意/驳回操作。
- `cp6.web/src/views/wf/designer/FlowDesigner.vue` — 流程设计器，支持拖拽创建节点、连线、配置审批人策略及保存流程定义。
- `cp6.web/src/views/wf/designer/FormDesigner.vue` — 表单设计器，提供控件库、画布拖拽建表单字段及属性配置面板和实时预览。

#### views/wms（仓储管理）

- `cp6.web/src/views/wms/BridgeHealthView.vue` — 模块集成桥接健康监控页，显示成功率、队列深度、死信数等指标和钩子执行履历。
- `cp6.web/src/views/wms/CarrierView.vue` — 快递运单列表和详情，记录承运商、跟踪号、物流状态变化事件，支持快递流程状态转移。
- `cp6.web/src/views/wms/CrossDockView.vue` — 越库（直转）订单列表，配置临时位置将入库商品直接分配出库，无需中间存储。
- `cp6.web/src/views/wms/ExpiryView.vue` — 即将过期/已过期库存查询，支持批量报废并显示损失金额统计。
- `cp6.web/src/views/wms/InboundOrderListView.vue` — 采购入库单列表，按单号、供应商、仓库、状态筛选，支持创建新单和转入库收单。
- `cp6.web/src/views/wms/InboundOrderView.vue` — 采购入库单详情和编辑页面，维护入库类型、预期到货日期、供应商、商品明细。
- `cp6.web/src/views/wms/InboundReceiptView.vue` — 入库收货单，支持关联采购单或直接录入，记录收货数量、位置、批号。
- `cp6.web/src/views/wms/InkLotView.vue` — 油墨批次管理（双tab页），包括批次主表（开启状态、过期检查）和调色历史记录。
- `cp6.web/src/views/wms/IotMonitorView.vue` — 物联网传感器监控大屏，显示温度、湿度等告警，支持模拟测试和历史读数查询。
- `cp6.web/src/views/wms/KitView.vue` — 产品套件（Kit）主表和BOM组成管理，维护套件SKU、包装清单、激活状态。
- `cp6.web/src/views/wms/LocationListView.vue` — 库位管理二级视图，左侧选仓库右侧管理该仓库库位（位置、等级、坐标、容量、状态）。
- `cp6.web/src/views/wms/LotTraceView.vue` — 批号追溯（前向客户/后向供应商），显示受影响的交易对象和时序交易记录，支持召回标记。
- `cp6.web/src/views/wms/MaterialShortageView.vue` — 物料短缺预警列表，记录工单所需量、可用量、短缺量，支持解决/驳回操作。
- `cp6.web/src/views/wms/MobileTaskView.vue` — 移动端扫描和工作票分配（条码识别、拣货/打包/入库/盘点任务），支持实时数量统计。
- `cp6.web/src/views/wms/OutboundOrderListView.vue` — 出库单列表（工单/网单混合），筛选单号、类型、状态、优先级，支持创建新单和WO扩展。
- `cp6.web/src/views/wms/OutboundOrderView.vue` — 出库单编辑详情，维护类型、计划日期、优先级、收货人地址、商品明细。
- `cp6.web/src/views/wms/OutboundRoutingView.vue` — 出库仓库路由规则配置，按客户/商品前缀/出库类型匹配仓库，含规则预览工具。
- `cp6.web/src/views/wms/PackingShipView.vue` — 梱包出荷作业页，左侧拣货待梱包队列，右侧梱包表单确认包裹号、承运商、配送信息后出荷。
- `cp6.web/src/views/wms/PalletView.vue` — 托盘和整盘管理（创建/完成/移库/出货标记），记录单层重量、堆叠层数、库位分配。
- `cp6.web/src/views/wms/PaperRollView.vue` — 纸卷库存管理（入库/消耗/报废），追踪剩余长度百分比、规格（宽度、纹向、克重）、制造批号。
- `cp6.web/src/views/wms/PickingWorkView.vue` — 拣货工作列表，展示和分配进行中的出库拣货任务。
- `cp6.web/src/views/wms/PlateMoldView.vue` — 印版木型库存管理列表，支持状态、类型、产品等查询和创建。
- `cp6.web/src/views/wms/ProductionInboundView.vue` — 生产入库单据编辑，扫描工单号录入产品批次数量并选择质量等级。
- `cp6.web/src/views/wms/QcInspectionView.vue` — 质检检验单列表和检验详情编辑，管理入库物料的抽样检验。
- `cp6.web/src/views/wms/RemnantView.vue` — 裁断残料库存列表，支持物料类型、等级、尺寸等查询和预留分配。
- `cp6.web/src/views/wms/ReplenishView.vue` — 补充指示单列表，跟踪从保管位置到拣货位置的库存补充任务。
- `cp6.web/src/views/wms/ReportCenterView.vue` — 报表分析中心，支持月度、ABC分类、滞留、进出库等多类报表。
- `cp6.web/src/views/wms/RmaView.vue` — RMA退货单列表和详情编辑，管理退货申请、检查和处理流程。
- `cp6.web/src/views/wms/SampleStockView.vue` — 样品库存清单，跟踪样品出借及其过期状态。
- `cp6.web/src/views/wms/SlottingView.vue` — 货位优化计划列表和详情，基于ABC分析和重量平衡推荐货位调整。
- `cp6.web/src/views/wms/StockDwellView.vue` — 库存滞留分析报表，支持按产品或客户分组查看库龄统计。
- `cp6.web/src/views/wms/StockQueryView.vue` — 库存实时查询界面，显示仓位、物料、批次的实际数、分配数、可用数。
- `cp6.web/src/views/wms/StockTakeListView.vue` — 盘点计划列表，跟踪盘点执行状态和生成盘点快照。
- `cp6.web/src/views/wms/StockTakeView.vue` — 盘点详情编辑界面，录入清点数量计算差异并管理盘点明细。
- `cp6.web/src/views/wms/VmiView.vue` — 供应商寄售库存管理，按客户汇总和明细展示物理数、分配数、可用数。
- `cp6.web/src/views/wms/WarehouseListView.vue` — 仓库主数据列表，管理仓库代码、类型、经理等信息。
- `cp6.web/src/views/wms/WcsTaskView.vue` — WCS自动化设备任务列表，跟踪各类型库内移库、出库等作业。
- `cp6.web/src/views/wms/WmsDashboardView.vue` — WMS仓储综合看板，展示库存价值、滞销SKU、今日进出、盘点告警等KPI。
- `cp6.web/src/views/wms/WmsPlaceholderView.vue` — 开发中功能占位页，显示不同阶段功能规划和操作提示。
