### 四、CP6.Core/Services/Pur + Mes + Plan + Integration — 采购/制造/计划/集成服务

#### Services/Pur

主流程服务（接口 + 实现）：

- `CP6.Core/Services/Pur/IPurchaseOrderService.cs` — 采购订单服务接口（章02）：建单带出价/税码/币种/冻结汇率 + 派生状态机 + 送审/审批回调；含 PoCreateDto/PoLineCreateDto。
- `CP6.Core/Services/Pur/PurchaseOrderService.cs` — 采购订单服务实现：校供应商、阶梯价解析、净税合计、三累计锚刷新、OA 审批送审/确认/驳回。
- `CP6.Core/Services/Pur/IPurchaseRequestService.cs` — 采购申请服务接口（章05）：手工建单 CRUD + 送审 + PR→PO 按建议供应商分组转单；含 PrCreateDto/PrLineCreateDto。
- `CP6.Core/Services/Pur/PurchaseRequestService.cs` — 采购申请服务实现：采番、送审、按建议供应商分组复用 PO 服务建单并回填 ConvertedPoNo。
- `CP6.Core/Services/Pur/IPrGenerationService.cs` — 采购申请需求驱动生成服务接口（章05 §3）：缺料反流 / 工单 BOM 缺料 → 自动建 PR（带幂等锚防重复）。
- `CP6.Core/Services/Pur/PrGenerationService.cs` — 上述实现：扫 MaterialShortage 与工单材料缺口，估价取价表/历史，按 SourceRefNo 幂等建 PR。
- `CP6.Core/Services/Pur/IGoodsReceiptService.cs` — 收货服务接口（章03）：双基准（着荷/检收）收货 + 委托 WMS 入库 + 应用检收结果 + 回写 PO 累计锚；含 GrCreateDto/GrLineCreateDto。
- `CP6.Core/Services/Pur/GoodsReceiptService.cs` — 收货服务实现：委托 WMS 入库、超收挡单、检收基准查 WMS 质检累加合格/不良、刷新 PO 状态。
- `CP6.Core/Services/Pur/IThreeWayMatchService.cs` — 三单匹配服务接口（章04，MVP 核心）：发票↔PO↔验收容差匹配→自动建 AP / 超容差挂起→人工放行或拒绝；含 MatchInvoiceDto/MatchResult。
- `CP6.Core/Services/Pur/ThreeWayMatchService.cs` — 三单匹配服务实现：逐行比可开票量与价差，容差内委托财务建 AP、超容差挂起、累加 InvoicedQty 并刷新 PO。
- `CP6.Core/Services/Pur/ISupplierPriceService.cs` — 采购价表服务接口（章01）：供应商×物料阶梯价维护 + 建 PO 时按数量/日期解析取价 + RFQ 成果幂等回写。
- `CP6.Core/Services/Pur/SupplierPriceService.cs` — 采购价表服务实现：阶梯价 ResolvePrice、列表、Save/Upsert/软删。
- `CP6.Core/Services/Pur/IRfqService.cs` — 询价服务接口（RFQ，章06）：从 PR 发起询价、邀供应商、收报价、比价排名、选定、回写价表、选中转 PO；含 RfqQuoteLineDto/RfqSelectionDto。
- `CP6.Core/Services/Pur/RfqService.cs` — 询价服务实现：RFQ 全生命周期（建/邀/报价/排名/选定/回写/转 PO）。
- `CP6.Core/Services/Pur/ISubcontractService.cs` — 外注加工服务接口（章07）：登记/发支給材（委托 WMS 出库防吞料）+ 收成品成本核算（接财务）+ 防吞料对账；含 Consign/SubcontractCost 等 DTO。
- `CP6.Core/Services/Pur/SubcontractService.cs` — 外注加工服务实现：支給材 upsert/发料追踪 IssuedQty、成品成本（加工费+支給材并入）、按实收反推应耗对账。
- `CP6.Core/Services/Pur/IPurReconcileService.cs` — 采购对账服务接口（章08/09）：PO↔GR↔AP 三方累计量核对表 + 虚开/超收/吞料完整性诊断；含 PoReconcileReport/PoReconcileLine。
- `CP6.Core/Services/Pur/PurReconcileService.cs` — 采购对账服务实现：逐行三方核对 + 外注并入支給材防吞料对账。
- `CP6.Core/Services/Pur/PurApprovalCallback.cs` — PO/PR 审批终态回调（BizType PUR_PO / PUR_PR）：OA 通过/驳回时回调采购服务推进单据状态，IServiceProvider 延迟解析打破 DI 循环。

Contracts 子目录（跨模块委托契约 / 桩 / 真实适配器）：

- `CP6.Core/Services/Pur/Contracts/IApprovalService.cs` — 审批委托契约（采购侧端口）：送审 PR/PO 请审批中台裁决；含 ApprovalSubmitRequest/Result。
- `CP6.Core/Services/Pur/Contracts/StubApprovalService.cs` — 审批桩（MVP）：任何送审即时通过返回 AUTO-{key}。
- `CP6.Core/Services/Pur/Contracts/ApprovalServiceAdapter.cs` — 审批适配器（真实）：有启用绑定则委托 OA 审批引擎起流程，无绑定则自动放行。
- `CP6.Core/Services/Pur/Contracts/IWmsReceiveService.cs` — WMS 物理入库委托契约：GR 确认时单向同步委托 WMS 落库存；含 WmsReceiveRequest/Result/Line。
- `CP6.Core/Services/Pur/Contracts/WmsReceiveServiceAdapter.cs` — WMS 入库适配器（真实）：委托 InboundService 预定→确定→实绩入库，库存真增加，落收货暂存位 RECV。
- `CP6.Core/Services/Pur/Contracts/IWmsQcQuery.cs` — WMS 检收结果查询契约：按入库号查每 PO 行合格/不良/待检判定；含 WmsQcVerdict。
- `CP6.Core/Services/Pur/Contracts/WmsQcQueryAdapter.cs` — WMS 检收查询适配器（真实）：按入库号查 QcInspection 判定済映射 PASS/FAIL/PENDING。
- `CP6.Core/Services/Pur/Contracts/IWmsIssueService.cs` — WMS 物理出库委托契约（外注支給材，Purpose=subcontract）；含 WmsIssueRequest/Result。
- `CP6.Core/Services/Pur/Contracts/WmsIssueServiceAdapter.cs` — WMS 出库适配器（真实）：委托 StockMovementService 按物料选库存源 OUT 扣减，库存不足转采购侧错误码。
- `CP6.Core/Services/Pur/Contracts/StubWmsServices.cs` — 三个 WMS 桩（入库/检收/出库 MVP）：返回假单号/空判定/全额实出，不落真实库存。
- `CP6.Core/Services/Pur/Contracts/IFinApService.cs` — 财务建应付契约（采购侧端口）：三单匹配通过后请财务建并过账应付发票（填 PurchaseOrderId）；含 PurApInvoiceDto/PurApResult。
- `CP6.Core/Services/Pur/Contracts/FinApServiceAdapter.cs` — 财务建应付适配器（真实）：映射到财务 IFinAp，借方按 GL 角色锚点 INVENTORY 解析。
- `CP6.Core/Services/Pur/Contracts/IFinCostService.cs` — 财务成本入账契约（外注侧端口）+ 内含 StubFinCostService 桩：外注成品成本（加工费+支給材并入）结转财务成本会计。
- `CP6.Core/Services/Pur/Contracts/FinCostServiceAdapter.cs` — 财务成本入账适配器（真实）：经自动凭证引擎按 Subcontract.CostPosted 规则生成成本凭证（借 FG / 贷 INVENTORY）。

#### Services/Mes

主流程服务（接口 + 实现，按 MSBBME 仕様对应）：

- `CP6.Core/Services/Mes/IWorkOrderService.cs` — 製造指図（工单）服务接口（ME020/030）：3 表一次提交、检索分页、CRUD、采番、由受注展开工单（ExpandFromOrder）。
- `CP6.Core/Services/Mes/WorkOrderService.cs` — 工单服务实现：工单/工程/材料三表写入、状态机、采番，发行时触发 WMS Bridge 出库。
- `CP6.Core/Services/Mes/IProductionResultService.cs` — 製造実績（报工）服务接口（ME040/050）：工程开始/中断/解除/完了/数量报告 + 状态联动指图。
- `CP6.Core/Services/Mes/ProductionResultService.cs` — 报工服务实现：实绩种别状态迁移、良品/不良累计、SignalR 通知、完工触发 WMS 完成品入库 Bridge。
- `CP6.Core/Services/Mes/IQualityInspectionService.cs` — 品質検査服务接口（ME060/070）：检查单 CRUD + 自动合否判定 + 检索。
- `CP6.Core/Services/Mes/QualityInspectionService.cs` — 品質検査服务实现：检查单采番、按检查项自动判合否，与 WMS 联动。
- `CP6.Core/Services/Mes/IDefectRecordService.cs` — 不良品管理服务接口（ME080）：不良票 CRUD + 不良分类主数据查询。
- `CP6.Core/Services/Mes/DefectRecordService.cs` — 不良品管理服务实现：不良票状态迁移（起票→分析→是正→完了）+ 分类维护。
- `CP6.Core/Services/Mes/IMachineService.cs` — 設備管理服务接口：设备主数据 CRUD + 实时状态变更 + 停止记录。
- `CP6.Core/Services/Mes/MachineService.cs` — 設備管理服务实现：设备主数据、状态变更通知、停机登记采番。
- `CP6.Core/Services/Mes/IOeeService.cs` — OEE 计算分析服务接口：日次检索、当日实时再计算、批量重算、N 日推移。
- `CP6.Core/Services/Mes/OeeService.cs` — OEE 服务实现：可用率×性能×品质三因子计算与趋势。
- `CP6.Core/Services/Mes/IPlanningBoardService.cs` — 生産計画ボード服务接口（ME010）：甘特条/KPI 取得、改期、自动排程。
- `CP6.Core/Services/Mes/PlanningBoardService.cs` — 生産計画ボード服务实现：计划条/KPI 查询与排期。
- `CP6.Core/Services/Mes/IMesDashboardService.cs` — MES 仪表盘服务接口（ME090）：汇总/工程进度/延迟告警/日趋势/不良 Top5/近完工/设备热力图。
- `CP6.Core/Services/Mes/MesDashboardService.cs` — MES 仪表盘服务实现（EF Core 版）：各看板聚合查询。
- `CP6.Core/Services/Mes/MesDashboardDapperService.cs` — MES 仪表盘 Dapper + 存储过程版实现：大量聚合查询走 SP，单往复强类型映射（与 EF 版并列）。
- `CP6.Core/Services/Mes/IMesSequenceService.cs` — MES 采番服务接口：WO/PR/QC/DF 前缀 + 年月 + 4 位连番。
- `CP6.Core/Services/Mes/MesSequenceService.cs` — MES 采番服务实现：全社统一 {功能码}{yyyyMM}{NNNN} 全期间累计不重置。
- `CP6.Core/Services/Mes/IMesNotifier.cs` — MES 实时通知契约（依赖倒置，WebApi 层 SignalR 实现）：报工/不良/设备状态/指图状态变更推送。
- `CP6.Core/Services/Mes/IPlanAchievementService.cs` — 生産計画達成率报表服务接口（Gap 3.3）：按产品/月/客户聚合达成率+不良率 + CSV 导出。
- `CP6.Core/Services/Mes/PlanAchievementService.cs` — 计划达成率服务实现：基于工单计划/良品/不良量聚合并导 CSV（无新建表）。
- `CP6.Core/Services/Mes/IWorkCenterService.cs` — 工作中心（WorkCenter）主数据服务接口：列表/取/upsert/删。
- `CP6.Core/Services/Mes/WorkCenterService.cs` — 工作中心主数据服务实现。
- `CP6.Core/Services/Mes/IProcessCostRateService.cs` — 工序费率（ProcessCostRate）服务接口：按工作中心列表 + 按日期解析有效费率 + upsert/删。
- `CP6.Core/Services/Mes/ProcessCostRateService.cs` — 工序费率服务实现（A2 成本做真的工时×费率基础）。
- `CP6.Core/Services/Mes/MesBridgeHook.cs` — MES Bridge 钩子实现：ERP 受注创建 → 自动展开製造指図；继承 BridgeHookBase 持久化 IntegrationEvent，业务错误转 Skipped 不拖垮受注。

#### Services/Plan

MRP 计划引擎（接口 + 实现，含 Contracts 转单契约）：

- `CP6.Core/Services/Plan/IMrpEngine.cs` — MRP 净需求引擎接口（P1）：低层码逐层、每 Item×日桶只 net 一次，regenerative 复算；含 MrpDemand/MrpRunRequest 记录。
- `CP6.Core/Services/Plan/MrpEngine.cs` — MRP 引擎实现：展开 BOM→扣供给+安全库存→净需求→计划订单+Pegging。
- `CP6.Core/Services/Plan/ILowLevelCodeService.cs` — 低层码计算服务接口：算各物料 BOM 最深层级（共用料取最深），检循环依赖；含 BomEdge。
- `CP6.Core/Services/Plan/LowLevelCodeService.cs` — 低层码计算实现：Kahn 拓扑排序逐层定层码，环检测抛错。
- `CP6.Core/Services/Plan/ISupplyService.cs` — MRP 供给汇总服务接口：汇总现库存+在途+在制+已确认计划订单四源；含 SupplyBreakdown。
- `CP6.Core/Services/Plan/SupplyService.cs` — 供给汇总服务实现：截至日桶取四源供给分解/合计。
- `CP6.Core/Services/Plan/IItemPlanningPolicyService.cs` — 品目计划策略服务接口：策略取值（缺则默认 lot-for-lot+0 提前期）+ 提前期汇总 + 主数据 CRUD。
- `CP6.Core/Services/Plan/ItemPlanningPolicyService.cs` — 品目计划策略服务实现：采购类取采购提前期/自制类取路线汇总，按 ItemCd upsert。
- `CP6.Core/Services/Plan/IPlanConvertService.cs` — 计划订单转单服务接口：建议→确认（进供给）→转单（采购 PR/生产工单）/ 忽略。
- `CP6.Core/Services/Plan/PlanConvertService.cs` — 计划订单转单服务实现：按类型分派到 PlanToPr / PlanToWorkOrder 契约，回填下游单号与状态。
- `CP6.Core/Services/Plan/Contracts/IPlanToPrService.cs` — 计划订单→采购 PR 转单契约（采购模块实现）+ 内含 PlanToPrServiceStub 桩（返回桩单号）。
- `CP6.Core/Services/Plan/Contracts/IPlanToWorkOrderService.cs` — 计划订单→MES 工单转单契约（MES 实现）+ 内含 PlanToWorkOrderServiceStub 桩（返回桩单号）。

#### Services/Integration

Bridge Hook 跨模块联动基建（Phase6 持久化/重试/健康度）：

- `CP6.Core/Services/Integration/BridgeHookBase.cs` — Bridge Hook 持久化基底类（Phase6）：各分支写 IntegrationEvent，失败设 NextRetryAt 供 Worker 自动重试。
- `CP6.Core/Services/Integration/IMesBridgeHook.cs` — ERP 受注→MES 製造指図前向钩子契约（best-effort/幂等/可配开关）+ MesBridgeResult。
- `CP6.Core/Services/Integration/IWmsBridgeHook.cs` — MES/PA→WMS 自动展开钩子契约：工单发行→材料出库、受注→出荷指示、完工→完成品入库 + WmsBridgeResult。
- `CP6.Core/Services/Integration/IErpBridgeHook.cs` — WMS→ERP 逆向回写钩子契约：出货确认→回写受注出荷实绩、RMA 确认→建 CreditNote。
- `CP6.Core/Services/Integration/IFinBridgeHook.cs` — WMS/MES→财务钩子契约：出货确认→AR 自动开票（收入+成本双凭证）、取消→红冲、工单完工→归集成本 + NoOpFinBridgeHook。
- `CP6.Core/Services/Integration/IOrderCancelBridgeHook.cs` — 受注取消反向级联钩子契约：探查/实施模式取消关联工单/出库（二段确认、幂等）。
- `CP6.Core/Services/Integration/OrderCancelBridgeHook.cs` — 受注取消级联钩子实现：按 Outbound 先 WorkOrder 后顺序取消，保库存整合不二重解除。
- `CP6.Core/Services/Integration/IIntegrationEventDispatcher.cs` — 集成事件重试派发器接口：按 (源/目标模块, HookName) 路由重投原钩子。
- `CP6.Core/Services/Integration/IntegrationEventDispatcher.cs` — 重试派发器实现：路由表 + payload 反序列化，重投 MES/WMS/ERP/Fin 各钩子。
- `CP6.Core/Services/Integration/IDeadLetterNotifier.cs` — 死信通知契约：重试耗尽的集成事件告警运维。
- `CP6.Core/Services/Integration/DeadLetterNotifier.cs` — 死信通知实现：经 SignalR（反射解析 WmsHub）+ Sys_OperLog 写死信告警。
- `CP6.Core/Services/Integration/IBridgeHealthService.cs` — Bridge 健康度服务接口：24h 窗口指标 + 单事件人工补偿。
- `CP6.Core/Services/Integration/BridgeHealthService.cs` — Bridge 健康度服务实现：按 Hook 聚合成功/失败/重试统计 + Compensate 补偿。
- `CP6.Core/Services/Integration/IBridgeMetricsSnapshotProvider.cs` — Bridge Prometheus 指标快照契约（T15/Gap2.3）：按 (HookName,Status) 聚合，隔离 prometheus-net 依赖。
- `CP6.Core/Services/Integration/BridgeMetricsSnapshotProvider.cs` — 指标快照实现：聚合 IntegrationEvent 状态计数 + 重试待/死信件数供 WebApi 采集器。
