### 六、CP6.WebApi/Controllers — REST 接口控制器

> 共 112 个文件（111 个控制器 + 1 个本地化基类 LocalizedControllerBase），按子目录分组。每行格式：`相对路径` — 暴露的核心 API（管理资源 + 关键动作）。

#### Controllers 根

- `Controllers/LocalizedControllerBase.cs` — 控制器本地化基类：惰性暴露请求级 IStringLocalizer（读 Sys_Lang 按当前 culture 解析），把硬编码 message 文案本地化；无 HttpContext 时回退 EchoStringLocalizer 原样回显 key。

#### Controllers/Erp

- `Controllers/Erp/BackorderController.cs` — 缺货积压订单管理：查询积压队列、关闭剩余数量、拆单到新订单。
- `Controllers/Erp/BusinessPartnerController.cs` — 取引先（供应商/客户）主数据：按编码查询、编码去重校验、新建/修改/删除/软删除、批量搜索导出 CSV。
- `Controllers/Erp/CreditNoteController.cs` — 信用单据（贷项凭证）：按多条件分页查询。
- `Controllers/Erp/EstimateCalcController.cs` — 见积计算书：分页列表、单条查询（含工程明细）、新建/修改/逻辑删除、复制新建、成本计算引擎。
- `Controllers/Erp/FscChecklistController.cs` — FSC 认证检查表：多条件搜索、获取输出格式、发行/下载已生成的 Excel。
- `Controllers/Erp/FxRateController.cs` — 多币种换汇汇率主数据：查询列表、指定货币/基准日解析汇率、新建/修改/删除汇率记录。
- `Controllers/Erp/MasterDataController.cs` — 表单级联下拉依赖数据：据点、担当者、通用代码组、客户/产品主数据查询。
- `Controllers/Erp/OrderController.cs` — 受注全生命周期（PA070/080/090）：采番、引入手配 NO/套装品/产品主数据、LT 计算、分类自动设置、材料 BOM 展开、受注列表/CSV/PDF 伝票发行、单价订正批量更新、受注取消级联。
- `Controllers/Erp/OrderTraceController.cs` — 订单追踪：查询订单当前产期状态与工序进展。
- `Controllers/Erp/OtdReportController.cs` — 准时交付（OTD）报表：汇总及 CSV 导出。
- `Controllers/Erp/PlateMoldController.cs` — 版型/木型主数据（PA140/150）：按见积书/产品查询、按序号查询（版本）、新建/修改（版本化）、删除、历史查询、批量标签发行。
- `Controllers/Erp/ProductController.cs` — 产品主数据（PA050/060）：分页一览、采番、按见积书/估计单查询部材、CSV 导出、仕掛检查、产品新建/修改/删除/复制。
- `Controllers/Erp/QuotationController.cs` — 御见积书（销售报价单，PA030/040）：分页列表、单条查询、新建/修改/删除/复制、报价确定登记/取消确定、帐票发行、关联见积计算书候选查询。
- `Controllers/Erp/SheetUnitPriceController.cs` — 纸张单价主数据（PA130）：按基准日/据点/客户查询、Excel 批量上传、选定行批量 UPSERT。
- `Controllers/Erp/UnshippedOrderController.cs` — 未发货订单清单：多条件分页搜索、CSV 导出。

#### Controllers/Fin

- `Controllers/Fin/ApInvoiceController.cs` — 应付发票：录入草稿、过账生成凭证、账龄与对账查询。
- `Controllers/Fin/ApMasterController.cs` — 应付主数据：银行账户与税码的列表查询及新建。
- `Controllers/Fin/ArInvoiceController.cs` — 应收发票：录入草稿、过账生成收入与成本双凭证、销售退货红字、红冲、账龄对账查询。
- `Controllers/Fin/AssetCardController.cs` — 资产卡片建档：按分类建档自动采编号、启用定格、查看折旧计划。
- `Controllers/Fin/AssetCategoryController.cs` — 资产分类：增删改查及删除守卫检查。
- `Controllers/Fin/AssetDepreciationController.cs` — 资产折旧：预览、执行折旧、设置工作量、过账与红冲。
- `Controllers/Fin/AssetDisposalController.cs` — 资产处置：新建、确认、撤销红冲。
- `Controllers/Fin/BankImportProfileController.cs` — 银行导入配置文件：按银行账户列表、上传/编辑、删除配置。
- `Controllers/Fin/BankReconciliationController.cs` — 银行对账：候选匹配、自动/手工匹配、解除匹配、生成凭证、标记待处理、对账报表查询、锁定/解锁。
- `Controllers/Fin/BankStatementController.cs` — 银行对账单：建档、导入文件或手工添加明细行、编辑、删除。
- `Controllers/Fin/BudgetController.cs` — 预算方案与版本：方案 CRUD 及版本草稿/送审/激活状态机。
- `Controllers/Fin/BudgetLineController.cs` — 预算明细行：新增/编辑/删除及 Excel 批量导入（预览+确认两步）。
- `Controllers/Fin/BudgetReportController.cs` — 预算分析：预算 vs 实际对比报告、凭证过账前预控预检。
- `Controllers/Fin/CostCenterController.cs` — 成本中心：只读查询用于下拉列表。
- `Controllers/Fin/CostController.cs` — 成本核算：按工单归集料工费并完工结转 WIP 至 FG。
- `Controllers/Fin/CreditControlController.cs` — 信用额度：出货前校验客户应收余额与本单是否超额度。
- `Controllers/Fin/GlAccountController.cs` — 会计科目：CRUD、停用、按模板方案导入。
- `Controllers/Fin/JournalEntryController.cs` — 记账凭证：提交/过账/驳回/红冲状态操作（不可改删）。
- `Controllers/Fin/PaymentController.cs` — 付款：付款/预付过账、撤销、核销（含尾差/汇差处理）。
- `Controllers/Fin/PeriodController.cs` — 会计期间与月结：期间列表、结账前检查、结账、反结账。
- `Controllers/Fin/ReceiptController.cs` — 收款：收款/预收过账、撤销、核销（含尾差/汇差处理）。
- `Controllers/Fin/ReportController.cs` — 财务报表：按期间生成资产负债表与损益表。
- `Controllers/Fin/TrialBalanceController.cs` — 试算平衡表：按期间查询试算结果。

#### Controllers/Integration

- `Controllers/Integration/BridgeHealthController.cs` — 跨模块集成桥健康监控：GET metrics 查集成事件指标、POST compensate/{eventId} 对死信事件人工补偿重放。

#### Controllers/Mes

- `Controllers/Mes/DefectRecordController.cs` — 不良品记录：搜索、单条查询、创建/更新/删除，及维护不良分类主数据。
- `Controllers/Mes/MachineController.cs` — 设备主数据与停机记录：设备增删改查、状态变更、停机记录登记与关闭。
- `Controllers/Mes/MesDashboardController.cs` — 生产看板：生产汇总、工程进度、延期告警、日趋势、不良 TOP5、完成订单、设备热力图（EF/存储过程双模式）。
- `Controllers/Mes/OeeController.cs` — 设备综合效率（OEE）：历史搜索、当日实时计算、趋势分析、指定日期重算。
- `Controllers/Mes/PlanAchievementController.cs` — 生产计划达成率报表：汇总数据与 CSV 导出。
- `Controllers/Mes/PlanningBoardController.cs` — 甘特图排程板：工单条形图、KPI 汇总、拖拽后日期更新、自动排程。
- `Controllers/Mes/ProcessCostRateController.cs` — 工程成本费率：列表查询、指定日期费率解析、新增/修改、删除。
- `Controllers/Mes/ProductionResultController.cs` — 制造实绩与工程进度：搜索实绩、获取工单全工程状况、工程开始/中断/恢复/完成、数量报告。
- `Controllers/Mes/QualityInspectionController.cs` — 品质检查记录：搜索、单条查询、新建/修改检验数据，及维护检查项目模板。
- `Controllers/Mes/WorkCenterController.cs` — 工作中心（产线）主数据：列表、详情、新增/修改、删除。
- `Controllers/Mes/WorkOrderController.cs` — 制造工单全生命周期：采番、搜索、单条查询、新建/修改/删除、工单发行、从销售订单自动展开。

#### Controllers/Plan

- `Controllers/Plan/ItemPlanningPolicyController.cs` — 品目计划策略：列表、获取策略、新增/修改、删除。
- `Controllers/Plan/MrpController.cs` — MRP 运算与计划订单：运行 MRP（显式或开口订单派生）、查看运算批次、计划订单与净需求钻取、确认/转单/忽略计划订单。

#### Controllers/Pub

- `Controllers/Pub/AttachmentController.cs` — 统一附件（PUB 章06）：multipart 上传（支持草稿 draftToken）、按业务列附件、下载/预览（可选业务权限校验）、删除、草稿转正 rebind 回填 BizId。
- `Controllers/Pub/CodeGenController.cs` — 代码生成（PUB 章08）：表元数据列表、保存表+列元数据（整体 upsert）、按持久化元数据预览产物、即时内联预览。
- `Controllers/Pub/SeqController.cs` — 富采番规则配置（PUB 章05）：流水规则分页查询、新增/修改/删除（业务键去重）、预览号码格式（不消费流水）。

#### Controllers/Pur

- `Controllers/Pur/GoodsReceiptController.cs` — 收货（章03）/api/pur/gr：列表/单条查询、确认收货（着荷即验收或检收转待检）、应用检收结果回写 PO 验收锚。
- `Controllers/Pur/PurReconcileController.cs` — 采购对账（章08/09）/api/pur/reconcile：对某 PO 出 PO↔GR↔AP 三方累计量对账表（逐行诊断 + 防虚开/超收/外协吞料完整性异常汇总）。
- `Controllers/Pur/PurchaseOrderController.cs` — 采购订单（章02）/api/pur/po：列表/单条查询、建单带出、送审（草稿→审批→确认）、取消（未收货可取消）。
- `Controllers/Pur/PurchaseRequestController.cs` — 采购申请（章05）/api/pur/pr：列表/单条查询、手工建单、送审、PR→PO 按建议供应商分组转单。
- `Controllers/Pur/RfqController.cs` — 询价 RFQ（章06）/api/pur/rfq：从 PR 发起询价、邀供应商、收报价、比价排名、按行选定、回写价表（Source=rfq）、选中转 PO。
- `Controllers/Pur/SubcontractController.cs` — 外注加工（章07）/api/pur/subcontract：列外注 PO、登记/发支给材（IssuedQty 防吞料）、收成品成本核算（加工费+料并入）、防吞料对账。
- `Controllers/Pur/SupplierPriceController.cs` — 采购价表（章01）/api/pur/supplier-price：供应商×物料阶梯价列表、按量/有效期解析采购单价、保存、删除。
- `Controllers/Pur/ThreeWayMatchController.cs` — 三单匹配（章04）/api/pur/match：列表/单条查询、匹配供应商发票（容差内自动建应付，超容差挂起）、人工放行、拒绝。

#### Controllers/Sys

- `Controllers/Sys/AuthController.cs` — 登录认证与改密：跨租户用户查询、BCrypt 密码校验、JWT 生成、权限预热、菜单聚合、登录安全审计。
- `Controllers/Sys/DashboardController.cs` — 经营总览 KPI（今日订单/制造进度/出货/库存/待批）：Dapper 跨表聚合并缓存 1 分钟。
- `Controllers/Sys/DeptController.cs` — 部门（组织树）：增删改查、树形结构拉取、移动节点、设置部门负责人。
- `Controllers/Sys/DictController.cs` — 字典类型与数据：增删改查、分页搜索、选项列表缓存（30 分钟）及缓存失效。
- `Controllers/Sys/LangController.cs` — 多语言词条管理：按语言拉全局词条、命名空间懒加载、发布版本控制、审校流程、缓存与回滚。
- `Controllers/Sys/MenuController.cs` — 菜单：增删改查、树形排序，删除时级联删除角色-菜单关联。
- `Controllers/Sys/OperLogController.cs` — 操作日志：分页查询、清空。
- `Controllers/Sys/RoleController.cs` — 角色：增删改查、启用/禁用、获取所有启用角色、绑定与解绑菜单权限。
- `Controllers/Sys/RolePermController.cs` — 角色授权配置：操作点/菜单权限/数据权限范围/字段权限四类权限读写、当前用户权限查询。
- `Controllers/Sys/UserController.cs` — 用户：增删改查、启用/禁用、所属部门与上级关联。
- `Controllers/Sys/UserRoleController.cs` — 用户角色分配：角色分配、主角色设置、历史数据迁移（Sys_User.RoleId → Sys_UserRole）。

#### Controllers/Wf

- `Controllers/Wf/AdvancedFlowController.cs` — 流程高级动作：任务退回指定节点、加签（前/后）、委派维护。
- `Controllers/Wf/ApprovalController.cs` — 业务模块接入 OA 入口：按 bizType 绑定流程起审批、查单据审批状态。
- `Controllers/Wf/FlowController.cs` — 流程定义：保存/获取定义、起流程、任务办理（批准/拒绝）、实例详情查看。
- `Controllers/Wf/FormController.cs` — 表单定义：保存/获取定义、表单数据提交与服务端 schema 复核。
- `Controllers/Wf/TaskController.cs` — 待办中心：我的待办、我的申请列表查询、流程实例撤回。

#### Controllers/Wms

- `Controllers/Wms/CarrierController.cs` — 配送业者配送单：创建配送单、添加事件、领取、签收、查询。
- `Controllers/Wms/CrossDockController.cs` — 跨库越库（Cross-Dock）订单：创建、执行、取消。
- `Controllers/Wms/ExpiryController.cs` — 过期商品：查询临期库存、批量废弃。
- `Controllers/Wms/InboundOrderController.cs` — 入库预定单：创建、修改、删除、查询。
- `Controllers/Wms/InboundReceiptController.cs` — 入库实绩：查询、确认入库、库存反映。
- `Controllers/Wms/InkController.cs` — 墨水/胶水批次：创建/查询/开启批次、混合批次、颜色匹配。
- `Controllers/Wms/IotMonitorController.cs` — IoT 传感器监控：传感器信息管理、读数记录、告警设置。
- `Controllers/Wms/KittingController.cs` — 套件组装（Kitting）：套件主档、组装指示单、执行组装。
- `Controllers/Wms/LotTraceController.cs` — 批次追溯与召回：顺向追溯（到客户）、逆向追溯（到供应商）、召回标记。
- `Controllers/Wms/MaterialShortageController.cs` — 物料短缺：查询短缺、处理/驳回短缺单。
- `Controllers/Wms/MobileController.cs` — 移动作业指示（RF 手持机）：作业任务分配、扫描、完成、库存移动。
- `Controllers/Wms/OutboundOrderController.cs` — 出库指示单：创建、修改、删除、查询。
- `Controllers/Wms/OutboundRoutingController.cs` — 出库路由规则：配置仓库引当逻辑、规则建档/修改/删除。
- `Controllers/Wms/PalletController.cs` — 栈板：创建、修改、完成堆栈、移至出货区。
- `Controllers/Wms/PaperRollController.cs` — 原纸卷：创建、消耗、规格匹配、分条。
- `Controllers/Wms/PlateMoldController.cs` — 印版/木型：版型档案、使用次数记录、保养排程。
- `Controllers/Wms/QcInspectionController.cs` — 入荷检品：从入库单创建/直入检品、保存检验结果、拒绝处理。
- `Controllers/Wms/RemnantController.cs` — 余料：余料登记、规格匹配、预留、出库。
- `Controllers/Wms/ReplenishController.cs` — 补货指示：创建补充单、批量生成、执行、取消。
- `Controllers/Wms/ReportCenterController.cs` — 仓储报表中心：月度库存、ABC 分析、滞销品、出入库历史。
- `Controllers/Wms/RmaController.cs` — RMA 退货：创建退货单、领取、检验、入库、销毁/退货。
- `Controllers/Wms/SampleStockController.cs` — 样品库存：创建、修改、借出、归还。
- `Controllers/Wms/ShippingController.cs` — 出货打包照会：查询包裹、追踪编号、运输商信息。
- `Controllers/Wms/SlottingController.cs` — 库位优化（Slotting）：分析库位布局、批准方案、取消。
- `Controllers/Wms/StockController.cs` — 库存照会与变动：多条件库存查询、库存移动、调整。
- `Controllers/Wms/StockDwellController.cs` — 库存滞留分析：统计库位停留时间。
- `Controllers/Wms/StockQcController.cs` — 库存品质状态：设置库存 QC 状态、批量标记工单相关库存。
- `Controllers/Wms/StockTakeController.cs` — 实地盘点：计划建档、盘点开始、计数更新、差异处理、确认。
- `Controllers/Wms/VmiController.cs` — 客先寄售（VMI）库存：客户库存查询、月结计费、确认发票。
- `Controllers/Wms/WarehouseController.cs` — 仓库与库位主档：仓库建档、库位管理、设备配置。
- `Controllers/Wms/WcsTaskController.cs` — WCS 自动化任务：任务派遣、设备控制、开始、完成。
- `Controllers/Wms/WmsDashboardController.cs` — WMS 仪表板：KPI 卡片、进出库趋势、仓库库存价值、告警。
