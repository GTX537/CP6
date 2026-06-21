### 十、CP6.Tests — 自动化测试

> 范围：`CP6.Tests/` 根目录及子目录 Fin / Pur / Sys / Tenant / Wf 下全部 `.cs`（已排除 bin/obj）。基于类名与测试方法名概括，每文件一句话。

#### (Tests 根)

- `CP6.Tests/TestHelper.cs` — 工具类：提供 InMemory `CP6Context`（每次随机库名隔离）和内存版 `CacheService` 的工厂方法。
- `CP6.Tests/GlobalUsings.cs` — 全局 using 集约文件：把各子命名空间（Erp/Plan/Sys/Integration/Mes/Wms 等实体、DTO、Service）在整个测试工程统一可见。
- `CP6.Tests/ApproverResolverTests.cs` — 审批人解析器：直属上级(链短于 N 返顶/无上级未解析)、部门负责人上溯首位、角色排除停用用户、指定用户(缺失未解析)、发起人返自身。
- `CP6.Tests/AttachmentServiceTests.cs` — 附件服务：同哈希去重复用存储路径、删除保留物理文件至最后引用、超大/非法扩展名报错(E061/E062)、回填 BizId 清 token。
- `CP6.Tests/BackorderServiceTests.cs` — 缺货补单服务：过滤未结有剩余的订单、关闭剩余设缺货量与发货状态、拆分新单复制表头、对已关闭明细操作报错。
- `CP6.Tests/BaseCrudServiceTests.cs` — CRUD 基类：创建时自动分配序列号/部门/创建人、查询注入自身数据域、更新剥离只读字段。
- `CP6.Tests/BridgeHealthServiceTests.cs` — 桥接健康服务：按 Hook 分组统计 24h 指标、排除 24h 外事件、队列深度只数失败、死信限 10 条。
- `CP6.Tests/BridgeHealth_AggregationE2ETests.cs` — 端到端：桥接指标聚合 24h 窗口、按 Hook 分组并计算成功率。
- `CP6.Tests/BridgeHookPersistenceTests.cs` — 桥接 Hook 持久化：成功/业务异常跳过/意外异常失败分别写 IntegrationEvent，多次调用生成唯一关联 ID，MES/ERP Hook 也落库。
- `CP6.Tests/BridgeMetricsSnapshotProviderTests.cs` — 桥接指标快照提供器：按 Hook 与状态分组、统计重试队列与死信并排除软删。
- `CP6.Tests/CacheServiceTests.cs` — 缓存服务：GetOrSet 首调走工厂二调读缓存、Remove 失效、Set/Get 往返、不存在键返回 null。
- `CP6.Tests/CodeGenServiceTests.cs` — 代码生成服务：生成实体实现 IDataScoped、Service 继承 BaseCrud、Controller 带权限常量与路由、前端列驼峰、合并自定义块保留手写代码。
- `CP6.Tests/ConditionEvaluatorTests.cs` — 条件求值器：数值比较 Gt/Le 边界、And/Or 短路、Neq 与布尔、括号改优先级、空表达式恒真、未知字段安全 false、非法表达式安全 false 不抛、点号标识符当未知字段、非数值量级比较安全 false。
- `CP6.Tests/CostCollectLaborOverheadTests.cs` — 成本归集人工/制费：工时×费率（含标准/缺工时回退/严格模式失败/迁移模式回退）。
- `CP6.Tests/CostSettleActualTests.cs` — 成本结算：贷记实际人工与制费。
- `CP6.Tests/CreditNoteServiceTests.cs` — 贷项通知单服务：按客户/日期过滤、关联 BP 取客户名、无 BP 回退客户码。
- `CP6.Tests/CurrentPermissionContextTests.cs` — 当前权限上下文：缓存并在失效后重建、按角色失效移除关联用户、预热、未登录抛错。
- `CP6.Tests/DataScopeFilterTests.cs` — 数据域过滤：五种范围（仅自身/本部门/子树/自定义部门集/全部）及未配置资源默认仅自身。
- `CP6.Tests/DataScopeSqlIntegrationTests.cs` — 数据域 SQL 集成：子树范围 PathStartsWith 翻译为 SQL 并实际过滤。
- `CP6.Tests/DeadLetterNotifierTests.cs` — 死信通知器：写带 IsAlert 的操作日志、推送 SignalR 消息。
- `CP6.Tests/DeptServiceTests.cs` — 部门服务：组织字段往返、根/子建路径、重复编码报错(E001)、移动重算子树路径、移入自身子树报错(E004)、删除带子/带用户报错(E002/E003)、树嵌套带负责人名。
- `CP6.Tests/DictControllerTests.cs` — 字典控制器：增类型(含重复码 400)、分页查、改/删类型(含 404)、取启用选项并验证缓存。
- `CP6.Tests/DictServiceTests.cs` — 字典服务：翻译标签（未知返原值/null 保持）、只取启用按序号排、缓存至失效。
- `CP6.Tests/EstimateCalcServiceTests.cs` — 估价计算服务：增删改查/拷贝/软删、按主数据或回退计算用量与工序成本、零张数返零价、ProductCategorySml 五字符列长往返。
- `CP6.Tests/EstimateCalcRegressionTests.cs` — 估价计算回归：维度用量（含主数据/回退+工序）结果保持不变（基线锁定）。
- `CP6.Tests/ExcelServiceTests.cs` — Excel 服务：导出写表头并翻译字典、模板标必填、导入非法必填行产错误文件留有效行、字典反向翻译、缺必填列报错(E071)。
- `CP6.Tests/FieldPermServiceTests.cs` — 字段权限服务：掩码隐藏字段(Access3)对象/列表、不可空设默认、异步用当前上下文、剥离只读还原原值、无权限不操作。
- `CP6.Tests/FileStoreTests.cs` — 文件存储：保存/读取/删除往返、删除缺失不抛错。
- `CP6.Tests/FlowDefServiceTests.cs` — 流程定义服务：定义保存/读取往返、Schema 变更升版本、提交后取实例详情（历史任务）、未找到返 null。
- `CP6.Tests/FlowEngineTests.cs` — 流程引擎：节点计数 All/Any/Veto、提交进首节点建任务、审批推进至结束、幂等、按条件选分支、会签全员/任一逻辑取消其余、未解析审批人挂起不崩。
- `CP6.Tests/FormServiceTests.cs` — 表单服务：定义往返、有效数据存储、缺必填/类型错抛错不存、Schema 变更升版本不动旧数据。
- `CP6.Tests/FourGranularityIntegrationTests.cs` — 四粒度权限集成：菜单/操作/数据域/字段四粒度同时强制生效。
- `CP6.Tests/FxRateAndOrderFreezeTests.cs` — 汇率解析与订单冻结：基币 JPY 返 1、外币冻结当日及之前最新汇率、无汇率抛错、外币客户下单冻结汇率、JPY 客户默认基率。
- `CP6.Tests/GeneratedModuleCapabilitiesTests.cs` — 代码生成模块能力：生成模块携带全部能力、生成的 C# 大括号配平。
- `CP6.Tests/InboundServiceTests.cs` — 入库服务：分配单号、无明细抛错、确认/取消状态流转、收货建库存与流水、部分/全部/拆分多次收货状态、直入模式、错误状态抛错。
- `CP6.Tests/IntegrationEventDispatcherTests.cs` — 集成事件分发器：MES 桥路由调 MES Hook、WMS 完工路由传良品量、未知路由抛错、Hook 返失败结果返 false。
- `CP6.Tests/IntegrationEventRetryWorkerTests.cs` — 集成事件重试 Worker：重试到期事件标成功、超最大次数转死信、配置禁用不动、跳过未到期、退避逐次增长。
- `CP6.Tests/IntegrationEventRetryDeadLetterE2ETests.cs` — 端到端：重试耗尽进死信并写告警日志、第二次成功标成功不写告警。
- `CP6.Tests/ItemPlanningPolicyServiceTests.cs` — 物料计划策略服务：取策略（缺则默认逐批）、提前期（采购/自制汇总工艺）、Upsert 插改、按关键字列表排除软删、软删。
- `CP6.Tests/KittingServiceTests.cs` — 套件/组装服务：建主件存 BOM(含重复抛错)、组装出组件入成品(库存不足抛错)、拆解逆向、草稿可撤已执行抛错。
- `CP6.Tests/LotTraceServiceTests.cs` — 批次追溯服务：跨库位汇总库存、设召回标更新全批库存、正向追受影响客户、反向追供应商、无库存返 null。
- `CP6.Tests/LowLevelCodeServiceTests.cs` — BOM 低阶码服务：共用件取最深层、去重边、有环抛错、空图返空。
- `CP6.Tests/MaterialShortageServiceTests.cs` — 缺料服务：建单存开放态、解决转态打时间戳、已终态再解决抛错、按开放态过滤。
- `CP6.Tests/MaterialShortage_E2ETests.cs` — 端到端：物料出库库存不足写缺料、通知 NoOp 通知器、出库为部分分配。
- `CP6.Tests/MaterialUsageCalculatorTests.cs` — 物料用量计算器：维度法(面积×成品率×数量)、固定法(单位用量×数量)、与估价计算用量一致。
- `CP6.Tests/MesBridgeHookTests.cs` — MES 桥接 Hook：下单成功、业务异常跳过、意外异常不传播、NoOp 无副作用、下单经 MES 桥自动展开工单、NoOp 桥不展开。
- `CP6.Tests/MrpEngineTests.cs` — MRP 引擎：纸品按产品物料行展开、同料只净一次、净需求扣全部供给、确认计划单不重排作供给、批量规则 MOQ 上取整、下达日=需求日-提前期、自制 WIP 向下级展开。
- `CP6.Tests/MrpControllerTests.cs` — MRP 控制器：显式需求持久化计划单、无需求返 400、从未结订单派生需求。
- `CP6.Tests/OperLogFilterTests.cs` — 操作日志过滤器：传输断开写 DB、连通则发布跳 DB、GET 默认跳/含 IncludeGet 记录、Auth/OperLog 路径跳过。
- `CP6.Tests/OrderCancelBridgeHookTests.cs` — 订单取消桥 Hook：探测模式无副作用判可否自动撤、检半态返需决策、拣货出库不可自动撤、强制按序级联(先出库后工单)、跳不可撤项、NoOp 返跳过、记录撤单失败。
- `CP6.Tests/OrderCancelFullCascadeE2ETests.cs` — 端到端：订单全级联取消所有状态更新、ForceFalse 在全部可撤时自动全撤。
- `CP6.Tests/OrderServiceCancelTests.cs` — 订单取消服务：未找到/已撤/已发/部分发抛拒、探测半态 ForceFalse 需决策无变更、ForceFalse 全可撤自动全撤、ForceTrue 全级联/半态部分撤、空原因抛错。
- `CP6.Tests/OrderTraceServiceTests.cs` — 订单追溯服务：按订单聚合工单/出库事件、订单未找到返 404、汇总统计正确、去重关联 ID 计数。
- `CP6.Tests/OtdReportServiceTests.cs` — 准时交付报表服务：按客户/月聚合、准时率计算、平均延迟天数只算延迟单、导出带 BOM 与表头的 CSV。
- `CP6.Tests/OutboundServiceTests.cs` — 出库服务：分配单号、确认后取消、删已确认抛错、分配按最早效期/FIFO/库存不足抛错/排除召回、发货扣库存消分配、发货建包裹、取消分配释放、从工单/订单展开。
- `CP6.Tests/OutboundService_QcFilter_Tests.cs` — 出库 QC 过滤：分配只拣合格与待检不拣不合格、无合格库存抛不足、待检向后兼容可分配、跳过 Hold 库存。
- `CP6.Tests/Outbound_ShortageBackflowTests.cs` — 出库缺料回流：物料出库库存不足写缺料不抛、发货类出库不足仍抛且不写缺料。
- `CP6.Tests/OutboundRoutingTests.cs` — 出库路由：解析规则优先→优先级→回退去重、产品前缀条件过滤、按规则选目标仓优于优先级与 FEFO、无规则按仓优先级、发货从分配仓出。
- `CP6.Tests/PermissionAggregatorTests.cs` — 权限聚合器：合并多角色并集菜单、主角色重复去重、填部门路径、未知用户抛错、字段权限取跨角色最小访问、数据域取最大并自定义部门并集、操作键并集滤空。
- `CP6.Tests/PermissionChainIntegrationTests.cs` — 权限链集成：授权操作过整条链、未授权操作整条链返 403。
- `CP6.Tests/PermissionServiceTests.cs` — 权限服务：HasAction 与 HasMenu 命中与未命中。
- `CP6.Tests/PlanAchievementServiceTests.cs` — 计划达成率服务：按产品分组算达成率、总体率与达标、只算已完成排除进行中、按实际完工日范围过滤、按月分组。
- `CP6.Tests/PlanConvertServiceTests.cs` — 计划转换服务：建议转已确认、转采购(调 PR 服务带 pegging)、转生产(调工单服务)、已转抛错、忽略置状态。
- `CP6.Tests/ProcessCostRateServiceTests.cs` — 工序成本费率服务：取最新生效、过期不取、重叠期/负费率/未知工作中心抛错。
- `CP6.Tests/ProductionResultHourTests.cs` — 生产实绩工时：机时合并区间/工时按操作员累计、显式工时覆盖时间戳、已覆盖不重算、中断闭合对扣减。
- `CP6.Tests/QualityInspection_AutoQcLinkTests.cs` — 质检自动联动库存：NG 检验自动标关联库存不合格、合格检验不改库存、NG 无关联库存仍存检验单。
- `CP6.Tests/RequirePermissionFilterTests.cs` — 权限校验过滤器：无权限置 403、有权限不置结果、服务缺失置 500。
- `CP6.Tests/RolePermServiceTests.cs` — 角色权限服务：操作不在授权菜单抛错(E021)、差异菜单与操作并失效角色、按操作码差异保存、MyActions 排序、数据域校验(E031/E032)替换持久化 CSV、字段权限校验(E041)只存非默认按资源、MyReadonly 返 Access2 字段。
- `CP6.Tests/RmaCreditNote_E2ETests.cs` — 端到端：RMA 确认生成贷项单、更新退货量、持久化集成事件。
- `CP6.Tests/Rma_ErpCreditNoteE2ETests.cs` — 端到端：RMA 确认生成贷项单并更新订单明细退货量、无匹配明细仍建单记警告、桥失败不回滚 RMA 确认、RMA 未找到返跳过并持久化事件。
- `CP6.Tests/SeqServiceTests.cs` — 序列号服务：构建编号并跨周期重置、不重置跨日累加、未知业务键抛错(E051)、按月重置。
- `CP6.Tests/StockDwellServiceTests.cs` — 库存停留时长服务：按收货账龄分桶、按产品跨库位/批次聚合、按客户用货主与自身桶、按仓/产品/货主过滤。
- `CP6.Tests/StockMovementServiceTests.cs` — 库存移动服务：IN/OUT/RSV/UNRSV/ADJ 各动作对物理/可用/已分配的增减、库存不足/超可用/允负仓、移库发两笔事务拆库存、同位/零负量抛错、WMS 序列连续。
- `CP6.Tests/StockQcServiceTests.cs` — 库存 QC 服务：设状态合法转换、非法状态/库存未找到抛错、按工单标关联库存、无收货返 0、保留无关字段。
- `CP6.Tests/StockQc_AllocateE2ETests.cs` — 端到端：QC 受阻库存分配时跳不合格、预留合格并写事务。
- `CP6.Tests/StockTakeServiceTests.cs` — 盘点服务：建计划快照库存分号、库位前缀过滤、无匹配抛错、录数算差异金额、状态守卫、有未盘/有差异无原因抛错、零差异自动核准、超阈值待审、核准发调整更新库存、取消。
- `CP6.Tests/SupplyServiceTests.cs` — 供给服务：含确认计划单不含建议、排除已取消入库与草稿工单、在途净已收/在制净已完、确认计划超桶不计。
- `CP6.Tests/TaskCenterServiceTests.cs` — 任务中心服务：我的待办只返当前用户未办任务、我的申请按发起人、撤回成功置已撤并取消任务追历史、非发起人/非运行中撤回抛错。
- `CP6.Tests/UnshippedOrderServiceTests.cs` — 未发货订单服务：排除全发/已取消、含部分取消、按客户过滤、只超期、聚合 MES/WMS 状态、算剩余量、无 BP 回退客户码、分页。
- `CP6.Tests/UnshippedOrderCsvExportTests.cs` — 未发货订单 CSV 导出：基本行形状、含逗号字段加引号、含引号转义、无行仍出表头。
- `CP6.Tests/UnshippedOrder_FullCascadeE2ETests.cs` — 端到端：建单+工单领料后查未发货订单返回含 MES/WMS 状态。
- `CP6.Tests/UserRoleServiceTests.cs` — 用户角色服务：int 角色 ID 往返、菜单键往返、保存差异增删并写主角色与失效、主角色不在集合抛错(E011)、读取合并主角色、迁移幂等。
- `CP6.Tests/WorkCenterServiceTests.cs` — 工作中心服务：Upsert 插改、负产能抛错、按关键字列表排除软删。
- `CP6.Tests/WorkOrderServiceCancelTests.cs` — 工单取消服务：允许状态成功/阻断状态抛错、已撤返 false 幂等、未找到抛错、空原因抛错、追加原因保留备注。
- `CP6.Tests/WmsBridgeHookTests.cs` — WMS 桥接 Hook：工单领料成功/业务异常跳过/意外异常不传播、下单成功、NoOp 无副作用、领料调桥、桥失败不阻领料、下单调桥。
- `CP6.Tests/WmsErpClosedLoopTests.cs` — WMS↔ERP 闭环：完工自动建成品入库(幂等)、发货确认回写订单、NoOp 桥不动订单、工单领料展开物料出库且发货扣库存(Phase2/3/4)。
- `CP6.Tests/WmsNotifierTests.cs` — WMS 通知器：库存移动成功调通知、通知异常不破坏移动、入库收货/出库发货分别触发对应通知。
- `CP6.Tests/WmsMobileServiceTests.cs` — WMS 移动作业服务：无任务类型抛错、默认优先级/待处理态、开始/完成/取消状态流转、按指派含自身与未分配池、扫码解析库位/产品/未知、移库完成发事务移库存。
- `CP6.Tests/WmsLogisticsServiceTests.cs` — WMS 物流服务：越库建并执行发出入、重复执行抛错、补货手动/批量(低库存,不重复)、库位优化按出库频次排名、核准转态。
- `CP6.Tests/WmsConnectivityServiceTests.cs` — WMS 连接服务：WCS 状态机/失败路由/非法转换抛错、承运商建运单自动追踪/累加事件/状态流转、IoT 读数检告警/模拟多读数/当前告警反映最新态。
- `CP6.Tests/WmsReportCenterServiceTests.cs` — WMS 报表中心服务：月度库存报表聚合至月底(非法年月抛错)、ABC 分析按累计占比排名、滞销库存检测、出入历史按日期过滤、导出 UTF8 BOM CSV。
- `CP6.Tests/WmsPaperIndustryServiceTests.cs` — WMS 纸业服务：纸卷建/消/超消抛错/匹配最小余量/分切建子件弃母卷、油墨开罐/调配继承最早效期/记录搜索匹配、托盘状态流转、VMI 按客户聚合与月度计费。
- `CP6.Tests/WmsPaperIndustry2ServiceTests.cs` — WMS 纸业服务(二)：余料全生命周期/按类型尺寸匹配、版模记录用量达寿命/保养重置计数/预警列表、样品借还周期/逾期列表。
- `CP6.Tests/WmsPhase5ServiceTests.cs` — WMS 五期服务：效期取窗内排序/处置移库存、QC 从入库快照/自动进检验/判合格自动收货应用库存(判 NG 不收)、RMA 创建自动授权/收货发事务/判转售移库/判报废移库存/关闭。

#### Fin

- `CP6.Tests/Fin/GlAccountTests.cs` — 会计科目实体：全字段往返、树父子链接、成本中心带机器链往返。
- `CP6.Tests/Fin/GlAccountServiceTests.cs` — 科目服务：导入中国/国际模板(角色锚点/同角色异码)、二次导入抛错、重复码抛错、停用不删、按角色只返启用。
- `CP6.Tests/Fin/FinSequenceServiceTests.cs` — 财务序列服务：月内格式化递增、跨月重置、带行的凭证往返。
- `CP6.Tests/Fin/FiscalPeriodServiceTests.cs` — 会计期间服务：算财年(日本四月制/日历年默认)、EnsureOpen 建期间(边界/财年)、幂等、月翻转规范化、IsOpen 与上期。
- `CP6.Tests/Fin/JournalEntryServiceTests.cs` — 凭证分录服务：校验不平/精度/非叶/缺往来/单行/借贷同行均失败、建草稿分号定期、提交后异人过账、制单=审核失败、自动过账拒手工源、非手工直接过账、驳回带原因。
- `CP6.Tests/Fin/JournalReversalTests.cs` — 凭证冲销：建对调分录并冻结原单、自动过账直接过、未过账冲销失败。
- `CP6.Tests/Fin/JournalApprovalIntegrationTests.cs` — 凭证审批集成：审批流通过后过账以审批人作审核、驳回拒凭证、自审(制单=审核)回滚不留痕。
- `CP6.Tests/Fin/TrialBalanceServiceTests.cs` — 试算平衡服务：三栏期初含历史、收入贷方正显、空期间平衡、未知期间抛错。
- `CP6.Tests/Fin/PeriodCloseServiceTests.cs` — 期末关账服务：预检有挂起凭证/上期未关失败、关账锁本开下、关后过账受阻、重开恢复、未关重开失败。
- `CP6.Tests/Fin/AutoVoucherEngineTests.cs` — 自动凭证引擎：固定角色按角色解析科目、文档行按科目展开、幂等跳重复源单、不平被自动过账拒绝。
- `CP6.Tests/Fin/PostingRuleSeedTests.cs` — 过账规则种子：种标准规则且幂等、AP 发票规则在真实科目表生平衡凭证、付款规则表头科目从事件解析银行、外币发票折本币并留原币。
- `CP6.Tests/Fin/ApInvoiceServiceTests.cs` — AP 发票服务：重复供应商发票号拒、可抵扣税生借物料+进项贷应付、不可抵扣税并入费用无单独税行、非草稿拒过账。
- `CP6.Tests/Fin/PaymentServiceTests.cs` — 付款服务：普通付款借应付贷银行、预付借预付科目、红冲冲销并标已冲、带核销红冲恢复发票余额。
- `CP6.Tests/Fin/ApSettlementServiceTests.cs` — AP 核销服务：一付款核多发票全销、超额付款拒、含现金折扣写差子账与 GL 一致、外币已实现汇兑收益入账并对账。
- `CP6.Tests/Fin/ApAgingAndCreditMemoTests.cs` — AP 账龄与贷记：按到期日分桶、贷记过账借应付贷费用税且对账匹配、从采购建发票过账幂等。
- `CP6.Tests/Fin/ArInvoiceServiceTests.cs` — AR 发票服务：过账生收入与成本双凭证、从发货建发票过账幂等、冲销同冲双凭证与发票。
- `CP6.Tests/Fin/ReceiptServiceTests.cs` — 收款服务：普通收款借银行贷应收、预收贷预收科目非应收、红冲冲销并标已冲、带核销红冲恢复发票余额。
- `CP6.Tests/Fin/ArSettlementServiceTests.cs` — AR 核销服务：一收款核多发票全销、超额拒、异客户拒、含销售折扣写差子账与 GL 一致、外币已实现汇兑收益入账并对账。
- `CP6.Tests/Fin/ArCreditMemoServiceTests.cs` — AR 贷记服务：冲回收入与成本、按贷项单幂等、账龄按负值列示。
- `CP6.Tests/Fin/CreditControlServiceTests.cs` — 信用控制服务：无额度不控、额内通过、超额阻断、贷记减少未结应收。
- `CP6.Tests/Fin/FxRevaluationServiceTests.cs` — 汇兑重估服务：AP/AR 外币未结未实现损益借贷与次期冲回、基币不重估、缺期末汇率跳过、已结不重估、幂等、贷项反向、关账触发重估并锁定。
- `CP6.Tests/Fin/FinancialStatementsServiceTests.cs` — 财务报表服务：资产负债表平衡(资产=负债+权益+利润)、损益类不列 BS、利润表毛利净利来自期间发生、COGS 与营业费分离、空期间零平衡。
- `CP6.Tests/Fin/FinReconciliationServiceTests.cs` — 财务对账服务：干净系统全清、GL 应付无子账时报应付异常。
- `CP6.Tests/Fin/FinBridgeHookTests.cs` — 财务桥接 Hook：发货确认自动建发票双凭证持久化、幂等不重复、发货取消冲销、无发票跳过、有工单用真实成品单位成本、完工自动归集料成本。
- `CP6.Tests/Fin/CostCollectServiceTests.cs` — 成本归集服务：料按消耗×BOM 价(实际/标准)、总实际/差异/成品单位成本、行记料工费、工单未找到失败、缺 BOM 价当零、幂等重归覆盖不重复。
- `CP6.Tests/Fin/CostSettleServiceTests.cs` — 成本结算服务：过账 WIP 归集与成品凭证标已结、WIP 净零、成品单位成本取成本单值、未归集失败、已结失败。
- `CP6.Tests/Fin/DepreciationCalculatorTests.cs` — 折旧计算器：直线法均摊末期补差、双倍余额年内恒定末两年转直线/仅两年回退直线、年数总和年加权、工作量法按比例与残值上限/缺总量抛错。
- `CP6.Tests/Fin/AssetDepreciationServiceTests.cs` — 资产折旧服务：在用合规建草稿与分录、当月购入不提、二次批次拒(FA003)、成本中心从机器派生、过账建汇总凭证回写卡片、计提三态、工作量关账前缺工作量(FA008)、冲销红字回滚卡片。
- `CP6.Tests/Fin/AssetDisposalServiceTests.cs` — 资产处置服务：出售有价无银行(FA010)、盘亏解析清理 1901 损益 6711、确认出售平衡凭证置已处置、确认后批量不阻、全计提可处置、冲销恢复前态回滚末次计提、批量含已处置(FA011)。
- `CP6.Tests/Fin/AssetCloseHookTests.cs` — 资产关账 Hook：关账自动计提折旧、工作量法缺工作量硬阻断。
- `CP6.Tests/Fin/BankReconMatchTests.cs` — 银行对账撮合：候选含已过账排已冲与占用、外币用原币、自动撮合一对一/一对多/多对一唯一子集、多解留手动、手动 N:M 平衡成/不平失败/非银行 GL/已占用/外币不符/冲销行失败、解除释放。
- `CP6.Tests/Fin/BankReconLockTests.cs` — 银行对账锁定：已调差零写快照、内部差非零拒、匹配提款与 GL 平衡成、调差非零严格拒、解锁期开成/期闭拒/空原因拒、AC008 调差非零拒。
- `CP6.Tests/Fin/BankReconSqliteTests.cs` — 银行对账守卫(partial)：锁定账户阻过账、非银行分录端到端通过、未锁结单通过、锁定已调原始凭证阻冲销。
- `CP6.Tests/Fin/BankOnlyVoucherTests.cs` — 纯银行凭证：手续费提款建凭证并匹配行、幂等二调拒、批量逐行结果(单失败不回滚其余)、标待处理设状态无凭证、已匹配标待处理失败、冲销后重生清旧 ID 写新。
- `CP6.Tests/Fin/BankStatementImportTests.cs` — 银行流水导入：档案 Upsert/列表(空名失败/更新保租户审计)、预览不落库、确认落库带符号金额、致命解析拒整批、强重复跳、非开放拒、Excel 表头跳行物理行号、跨会话指纹去重、改/删已导入行拒、CSV 引号转义解析。
- `CP6.Tests/Fin/ReconciliationStatementTests.cs` — 银行调节表：内部差零(期初+流量=期末)、账面在途存款调银行侧、外币 GL 银行期末用原币非本币。
- `CP6.Tests/Fin/BudgetVersionStateMachineTests.cs` — 预算版本状态机：重复财年拒、建版本自增版本号、审批激活设激活并归档原激活、未审批激活拒（含测试桩 StubApprovalForTest）。
- `CP6.Tests/Fin/BudgetLineBreakdownTests.cs` — 预算行分解：均摊填年与各期、非叶/非损益拒、重复桶第二次更新不重复、非草稿拒、季节加权和=年、手动用给定期推年、季节全零回退均摊、匹配行版本更新成功。
- `CP6.Tests/Fin/BudgetVsActualTests.cs` — 预算对实际：上卷至最具体桶(非按键长公司)、匹配桶算差异、有实际无预算归未预算组。
- `CP6.Tests/Fin/BudgetGuardTests.cs` — 预算守卫：YTD 超累计预算拒、额内过、无激活版本短路过、同分录同桶多行合并、警告级不被守卫阻、收入超目标不阻、最具体成本中心桶优先、非自然财年经会计期间解析拒。
- `CP6.Tests/Fin/BudgetGuardPostingTests.cs` — 预算守卫过账：阻断超额拒、额内过账、警告超额仍过账、自动过账不受阻、冲销不受预算门控。
- `CP6.Tests/Fin/BudgetPreCheckTests.cs` — 预算预检：警告超额返警告、额内无警告。
- `CP6.Tests/Fin/BudgetCopyImportTests.cs` — 预算复制导入：从版本克隆桶与期、保全维度与控制、致命行拒整批、全有效确认落库、垃圾月单元优雅拒、预览返行错误不落库、非草稿版本拒、从实际汇总上年已过账入新版本。
- `CP6.Tests/Fin/BudgetApprovalIntegrationTests.cs` — 预算审批集成：审批设已批并自动激活、驳回设已拒带原因。
- `CP6.Tests/Fin/BudgetSqliteTests.cs` — 预算 SQLite 占位：BudgetLine 唯一索引 SQLite 结构性测试（当前 Skip）。
- `CP6.Tests/Fin/BudgetExcelFixture.cs` — 夹具/工具类：用 ClosedXML 构造预算导入 Excel 流（标准多行 + 含文本垃圾月单元两种），供预算导入测试复用。

#### Pur

- `CP6.Tests/Pur/PurchaseOrderServiceTests.cs` — 采购订单服务：建单带默认/解析价/算金额/锚点零、非供应商拒、无适用价拒、显式价覆盖阶梯、提交自动核准至已确认、非草稿拒、已确认可取消但已收拒、按锚点刷状态、状态推导矩阵。
- `CP6.Tests/Pur/PurchaseRequestServiceTests.cs` — 采购申请服务：手动建分号草稿态带行、列表填行数、无行拒、提交自动核准回填引用、非草稿拒、未批转换拒、按供应商分组拆多 PO 回填、无建议供应商行留未转、无可转拒、幂等、按状态过滤。
- `CP6.Tests/Pur/GoodsReceiptServiceTests.cs` — 收货服务：应计制立即接受并 PO 已收、检验制仅 QC 合格后接受、超收拒、部分收派生部分已收、草稿 PO 拒。
- `CP6.Tests/Pur/ThreeWayMatchServiceTests.cs` — 三单匹配服务：容差内自动建 AP 填 PoId 累计已开票、超价容差挂起无 AP、不可超验收开票、重复供应商发票拒、配置容差内过、释放挂起建 AP、驳回挂起无 AP、适配器解析存货科目映射委托、无 GL 科目失败。
- `CP6.Tests/Pur/PurchaseToApIntegrationTests.cs` — 采购到 AP 集成：PO→收货→匹配全链建真实已过账 AP 发票带 PoId、幂等同发票不重复计费。
- `CP6.Tests/Pur/PrGenerationServiceTests.cs` — PR 生成服务：从缺料建 PR(源缺料带引用号)、幂等不重复、缺缺料返 null、无价表估价 null 仍建、估价回退历史 PO、从工单短缺料建 PR(无短缺返 null/按料幂等)。
- `CP6.Tests/Pur/SupplierPriceServiceTests.cs` — 供应商价表服务：取最高合格阶梯、无合格返 null、遵守有效期、同阶梯新生效日胜、存取往返、同业务键原地更新不重复、异生效日插新行。
- `CP6.Tests/Pur/RfqServiceTests.cs` — 询价服务：从 PR 建只聚无供应商行带可追溯、加供应商邀请、报价建矩阵、按线排名(价优/同价比交期)、选标(逐线异供应商/重选清旧/过期拒)、回写价表幂等、按选定供应商转 PO 用报价。
- `CP6.Tests/Pur/SubcontractServiceTests.cs` — 外注服务：加委外料(Upsert 去重/各类拒)、发料累计记 WMS 号传外注用途、算成品成本(加工费+委外料合并/过账财务)、三单匹配仅匹加工费委外料不入 AP、按成品量对账标异常、列外注 PO。
- `CP6.Tests/Pur/PurReconcileServiceTests.cs` — 采购对账服务：全匹配已完成无异常、待收/待检/待开票、超开票标舞弊、无验收货开票标舞弊、超收无容差标异常/容差内不标、外注寄存异常标盗用/容差内不标、PO 未找到拒。
- `CP6.Tests/Pur/PurApprovalTests.cs` — 采购审批：PO/PR 从审批确认/驳回(非挂起幂等)、提交转已批、无绑定自动核准、有绑定委托 OA 流。
- `CP6.Tests/Pur/PurApprovalIntegrationTests.cs` — 采购审批集成：PO 提交审批通过确认/驳回回草稿、PR 提交审批通过核准。
- `CP6.Tests/Pur/WmsIssueServiceAdapterTests.cs` — WMS 发料适配器：扣库存返真实事务号、库存不足/无库存抛错(E080)、遵守仓库过滤、记外注事务带引用号。
- `CP6.Tests/Pur/WmsReceiveServiceAdapterTests.cs` — WMS 收货适配器：加库存返入库号、入库单挂 PoNo、按 PO 行返行引用。
- `CP6.Tests/Pur/WmsQcQueryAdapterTests.cs` — WMS QC 查询适配器：无检验返空、已判检验映射合格/不合格/待检、未判检验忽略。
- `CP6.Tests/Pur/FinCostServiceAdapterTests.cs` — 财务成本适配器：生成本凭证借成品贷存货、幂等同凭证号不重复、外注成本规则种子借成品贷存货。

#### Sys

- `CP6.Tests/Sys/PasswordHasherTests.cs` — 密码哈希器：哈希非明文且可验证、IsHashed 识别 BCrypt 格式。
- `CP6.Tests/Sys/PasswordHashMigrationSeedTests.cs` — 密码哈希迁移种子：原地重哈明文并跳过已哈希、空密码不报错跳过。
- `CP6.Tests/Sys/PasswordPolicyServiceTests.cs` — 密码策略服务：强制长度/复杂度规则、历史拒重用。
- `CP6.Tests/Sys/LoginSecurityServiceTests.cs` — 登录安全服务：超阈值锁定且成功重置、滑动窗口在阈值前重置计数。
- `CP6.Tests/Sys/AuthControllerLoginSecurityTests.cs` — 登录控制器安全：错密码记失败审计并抛通用错、未知用户审计抛通用错、停用账号正确密码抛 E_SEC_003 审计、超阈值锁定后即使正确密码也拒。
- `CP6.Tests/Sys/AuthControllerChangePasswordTests.cs` — 改密控制器：成功重哈记历史清强改标、当前密码错抛、弱新密码抛、重用新密码抛、历史裁剪至 HistoryCount。
- `CP6.Tests/Sys/SecurityAuditServiceTests.cs` — 安全审计服务：记带字段安全事件、超长字段裁剪至列长。

#### Tenant

- `CP6.Tests/Tenant/TenantFilterTests.cs` — 租户过滤器：插入盖当前租户且查询受限、同库双租户双向隔离、显式 TenantId 不被覆盖、无上下文用默认租户。
- `CP6.Tests/Tenant/TenantRegistryTests.cs` — 租户注册表：种默认租户幂等、枚举只返启用租户、空表回退默认租户。
- `CP6.Tests/Tenant/TenantScopeRunnerTests.cs` — 租户作用域运行器：ForEachTenant 逐个启用租户带受限上下文访问、单租户抛错其余仍跑。
- `CP6.Tests/Tenant/OperLogTenantTests.cs` — 操作日志租户：盖当前租户且查询受限、IgnoreQueryFilters 见全租户。
- `CP6.Tests/Tenant/TenantUniqueIndexTests.cs` — 租户唯一索引：每个租户实体的唯一索引均以 TenantId 打头（结构契约校验）。

#### Wf

- `CP6.Tests/Wf/ApprovalServiceTests.cs` — 审批服务：无绑定提交抛错、有绑定起流并标业务类型/ID、运行中重复提交抛错、同类型异 ID 允许、无实例状态 None、反映运行中实例。
- `CP6.Tests/Wf/ApprovalDispatcherTests.cs` — 审批分发器：完成-通过调业务回调带上下文、完成-驳回调 OnRejected 带原因、原生表单无业务类型不回调、未注册业务类型抛错、回调抛异常向上传播以回滚（含测试桩业务回调实现）。
- `CP6.Tests/Wf/ExpressionEvaluatorTests.cs` — 表达式求值器：比较/逻辑/算术优先级/一元负与非/字段运算/日期差/聚合函数/三元/字符串拼接、强制布尔与原值、未知字段/函数/元数错/非法算术安全失败、空表达式行为。
- `CP6.Tests/Wf/FormRuleRecomputeTests.cs` — 表单规则重算：服务端权威算天数、条件必填缺则阻/未触发则过、隐藏字段跳校验、提交存重算值、无规则同普通校验。
- `CP6.Tests/Wf/TimeoutScanTests.cs` — 超时扫描：提交设到期(节点有超时)、超时自动通过过账推进/自动驳回/升级改派追溯/提醒推后可重复/硬动作幂等二扫无操作、未到期不动。
- `CP6.Tests/Wf/AdvancedFlowTests.cs` — 高级流程：退回作废活任务重建目标追历史、退回后续继续前进、已处理任务退回无操作、非法目标抛错、后加签等双方再推进、前加签挂原件加签人批后复活、加签非法/非挂起/超限抛错、委托生效换办理人双追溯/过期不换/自委托或坏区间抛错。
- `CP6.Tests/Wf/DesignerIsomorphismTests.cs` — 设计器同构：设计器导出含坐标的 Schema 能在引擎运行、条件 false 时路由别处自动结束。
</content>
</invoke>
