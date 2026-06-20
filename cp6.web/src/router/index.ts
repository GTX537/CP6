import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { ensureNamespacesForPath } from '@/i18n'

// 路由路径 → 组件的映射表（所有可能的页面）
const viewModules: Record<string, () => Promise<any>> = {
  '/dashboard': () => import('@/views/dashboard/DashboardView.vue'),
  // ───── PMS システム管理 (100~199) ─────
  '/role': () => import('@/views/pms/RoleView.vue'),
  '/menu': () => import('@/views/pms/MenuView.vue'),
  '/permission': () => import('@/views/pms/PermissionView.vue'),
  '/user': () => import('@/views/pms/UserView.vue'),
  '/lang': () => import('@/views/pms/LangView.vue'),
  '/dict': () => import('@/views/pms/DictView.vue'),
  '/operlog': () => import('@/views/pms/OperLogView.vue'),
  '/pub/dept': () => import('@/views/pms/DeptTreeView.vue'),   // PUB 章00 组织模型
  '/pub/role-perm': () => import('@/views/pms/RolePermView.vue'),   // PUB 章02 角色功能权限
  '/pub/data-scope': () => import('@/views/pms/DataScopeView.vue'),   // PUB 章03 数据权限
  '/pub/field-perm': () => import('@/views/pms/FieldPermView.vue'),   // PUB 章04 字段权限
  '/pub/seq': () => import('@/views/pms/SeqView.vue'),   // PUB 章05 采番规则
  '/pub/codegen': () => import('@/views/pms/CodeGenView.vue'),   // PUB 章08 代码生成器
  // ───── OA 审批工作流 (Wf) ─────
  '/wf/todo': () => import('@/views/wf/TodoCenter.vue'),               // OA 章04 待办中心
  '/wf/my-applications': () => import('@/views/wf/MyApplications.vue'), // OA 章04 我的申请
  '/wf/form-designer': () => import('@/views/wf/designer/FormDesigner.vue'), // OA 章09 表单设计器
  '/wf/flow-designer': () => import('@/views/wf/designer/FlowDesigner.vue'), // OA 章09 流程设计器
  // ───── 财务 (Fin) 总账内核 ─────
  '/fin/account': () => import('@/views/fin/GlAccountView.vue'),          // 章01 会计科目
  '/fin/journal': () => import('@/views/fin/JournalEntryView.vue'),       // 章01 记账凭证
  '/fin/trial-balance': () => import('@/views/fin/TrialBalanceView.vue'), // 章02 试算平衡表
  '/fin/period': () => import('@/views/fin/PeriodCloseView.vue'),         // 章02 期间/月结
  '/fin/ap-invoice': () => import('@/views/fin/ApInvoiceView.vue'),       // 章03 应付发票
  '/fin/ap-payment': () => import('@/views/fin/PaymentView.vue'),         // 章03 付款/核销
  '/fin/ap-aging': () => import('@/views/fin/ApAgingView.vue'),           // 章03 应付账龄/对账
  '/fin/ar-invoice': () => import('@/views/fin/ArInvoiceView.vue'),       // 章04 应收发票/红字
  '/fin/ar-receipt': () => import('@/views/fin/ReceiptView.vue'),         // 章04 收款/核销
  '/fin/ar-aging': () => import('@/views/fin/ArAgingView.vue'),           // 章04 应收账龄/对账
  '/fin/balance-sheet': () => import('@/views/fin/BalanceSheetView.vue'), // 章08 资产负债表
  '/fin/income-statement': () => import('@/views/fin/IncomeStatementView.vue'), // 章08 损益表
  '/fin/cost': () => import('@/views/fin/CostSheetView.vue'),             // 章06 成本核算
  '/fin/asset-category': () => import('@/views/fin/AssetCategoryView.vue'),   // A3 资产分类
  '/fin/asset-card': () => import('@/views/fin/AssetCardView.vue'),           // A3 资产卡片
  '/fin/asset-deprec': () => import('@/views/fin/AssetDepreciationView.vue'), // A3 折旧计提
  '/fin/asset-disposal': () => import('@/views/fin/AssetDisposalView.vue'),   // A3 资产处置
  '/fin/bank-reconciliation': () => import('@/views/fin/BankReconciliationView.vue'), // A4 对账撮合台（菜单614）
  '/fin/bank-statement': () => import('@/views/fin/BankStatementView.vue'),           // A4 对账会话
  '/fin/bank-import-profile': () => import('@/views/fin/BankImportProfileView.vue'),  // A4 导入模板
  // ───── 采购 (Pur) MVP 章01~04 ─────
  '/pur/supplier-price': () => import('@/views/pur/SupplierPriceView.vue'), // 章01 供应商价表
  '/pur/po': () => import('@/views/pur/PurchaseOrderView.vue'),             // 章02 采购订单
  '/pur/gr': () => import('@/views/pur/GoodsReceiptView.vue'),              // 章03 收货
  '/pur/match': () => import('@/views/pur/ThreeWayMatchView.vue'),          // 章04 三单匹配
  '/pur/rfq': () => import('@/views/pur/RfqView.vue'),                      // 章06 询价比价
  '/pur/pr': () => import('@/views/pur/PrView.vue'),                        // 章05 采购申请
  '/pur/subcontract': () => import('@/views/pur/SubcontractView.vue'),      // 章07 外注加工
  '/pur/reconcile': () => import('@/views/pur/PurReconcileView.vue'),       // 章08/09 采购对账
  // ───── 计划中台 (Plan) P1 MRP ─────
  '/plan/mrp': () => import('@/views/plan/MrpBoardView.vue'),               // MRP 运算看板
  '/plan/item-policy': () => import('@/views/plan/ItemPolicyView.vue'),     // 计划主数据
  // ───── ERP 販売・製品 (200~299) ─────
  '/estimate-calc': () => import('@/views/erp/EstimateCalcView.vue'),
  '/estimate-calc-list': () => import('@/views/erp/EstimateCalcListView.vue'),
  '/quotation': () => import('@/views/erp/QuotationView.vue'),
  '/quotation-list': () => import('@/views/erp/QuotationListView.vue'),
  '/product': () => import('@/views/erp/ProductMasterView.vue'),
  '/product-list': () => import('@/views/erp/ProductMasterListView.vue'),
  '/order': () => import('@/views/erp/OrderEntryView.vue'),
  '/order-list': () => import('@/views/erp/OrderListView.vue'),
  '/order-price-correction': () => import('@/views/erp/OrderPriceCorrectionView.vue'),
  '/erp/order-trace': () => import('@/views/erp/OrderTraceView.vue'),
  '/erp/credit-note': () => import('@/views/erp/CreditNoteListView.vue'),
  '/erp/backorder': () => import('@/views/erp/BackorderListView.vue'),
  '/erp/otd-report': () => import('@/views/erp/OtdReportView.vue'),
  '/erp/fx-rate': () => import('@/views/erp/FxRateView.vue'),
  '/business-partner': () => import('@/views/erp/BusinessPartnerView.vue'),
  '/business-partner-list': () => import('@/views/erp/BusinessPartnerListView.vue'),
  '/fsc-checklist': () => import('@/views/erp/FscChecklistView.vue'),
  '/sheet-unit-price': () => import('@/views/erp/SheetUnitPriceView.vue'),
  '/plate-mold': () => import('@/views/erp/PlateMoldView.vue'),
  '/plate-mold-list': () => import('@/views/erp/PlateMoldListView.vue'),
  // ───── MES 工艺主数据 (A2) ─────
  '/mes/work-center': () => import('@/views/mes/WorkCenterView.vue'),         // A2 工作中心
  '/mes/process-cost-rate': () => import('@/views/mes/ProcessCostRateView.vue'), // A2 工序费率
  // ───── MES 製造執行 (MSBBME020/030/040/050) ─────
  '/mes/work-order': () => import('@/views/mes/WorkOrderEntryView.vue'),
  '/mes/work-order-list': () => import('@/views/mes/WorkOrderListView.vue'),
  '/mes/production-result': () => import('@/views/mes/ProductionResultEntryView.vue'),
  '/mes/production-result-list': () => import('@/views/mes/ProductionResultListView.vue'),
  // ───── MES 品質・不良 (MSBBME060/070/080) ─────
  '/mes/quality-inspection': () => import('@/views/mes/QualityInspectionEntryView.vue'),
  '/mes/quality-inspection-list': () => import('@/views/mes/QualityInspectionListView.vue'),
  '/mes/defect': () => import('@/views/mes/DefectManagementView.vue'),
  // ───── MES 計画・ダッシュボード (MSBBME010/090) ─────
  '/mes/planning-board': () => import('@/views/mes/PlanningBoardView.vue'),
  '/mes/dashboard': () => import('@/views/mes/MesDashboardView.vue'),
  // ───── MES Phase 4：設備・OEE・大屏 ─────
  '/mes/machine-list': () => import('@/views/mes/MachineListView.vue'),
  '/mes/oee': () => import('@/views/mes/OeeAnalysisView.vue'),
  '/mes/plan-achievement': () => import('@/views/mes/PlanAchievementView.vue'),
  // Control Tower 大屏は standalone モード、下の staticRoutes も参照
  '/mes/control-tower': () => import('@/views/mes/ControlTowerView.vue'),
  // ───── WMS 倉庫管理 Phase 1 (MSBBWM010/020) ─────
  '/wms/warehouse': () => import('@/views/wms/WarehouseListView.vue'),
  '/wms/stock': () => import('@/views/wms/StockQueryView.vue'),
  // ───── WMS Phase 2 入庫 (MSBBWM030/040) ─────
  '/wms/inbound-order-list': () => import('@/views/wms/InboundOrderListView.vue'),
  '/wms/inbound-order': () => import('@/views/wms/InboundOrderView.vue'),
  '/wms/inbound-receipt': () => import('@/views/wms/InboundReceiptView.vue'),
  // ───── WMS Phase 3 出庫 (MSBBWM050/070/080) ─────
  '/wms/outbound-order-list': () => import('@/views/wms/OutboundOrderListView.vue'),
  '/wms/outbound-order': () => import('@/views/wms/OutboundOrderView.vue'),
  // ───── WMS Phase 4 棚卸・ダッシュボード (MSBBWM090/-DASH) ─────
  '/wms/stock-take-list': () => import('@/views/wms/StockTakeListView.vue'),
  '/wms/stock-take': () => import('@/views/wms/StockTakeView.vue'),
  '/wms/dashboard': () => import('@/views/wms/WmsDashboardView.vue'),
  '/wms/bridge-health': () => import('@/views/wms/BridgeHealthView.vue'),
  '/wms/material-shortage': () => import('@/views/wms/MaterialShortageView.vue'),
  '/wms/stock-dwell': () => import('@/views/wms/StockDwellView.vue'),
  '/wms/outbound-routing': () => import('@/views/wms/OutboundRoutingView.vue'),
  // ───── WMS Core 補完 ─────
  '/wms/location': () => import('@/views/wms/LocationListView.vue'),
  // ───── Phase WM-3 後続実装予定（占位） ─────
  '/wms/product-inbound':      () => import('@/views/wms/ProductionInboundView.vue'),
  '/wms/shipping-order-list':  () => import('@/views/wms/OutboundOrderListView.vue'),
  '/wms/shipping-order':       () => import('@/views/wms/OutboundOrderView.vue'),
  '/wms/picking':              () => import('@/views/wms/PickingWorkView.vue'),
  '/wms/packaging':            () => import('@/views/wms/PackingShipView.vue'),
  // ───── Phase WM-5 拡張機能 ─────
  '/wms/inspection':           () => import('@/views/wms/QcInspectionView.vue'),
  '/wms/rma':                  () => import('@/views/wms/RmaView.vue'),
  '/wms/expiry':               () => import('@/views/wms/ExpiryView.vue'),
  '/wms/lot-trace':            () => import('@/views/wms/LotTraceView.vue'),
  '/wms/kit':                  () => import('@/views/wms/KitView.vue'),
  '/wms/slotting':             () => import('@/views/wms/SlottingView.vue'),
  '/wms/replenish':            () => import('@/views/wms/ReplenishView.vue'),
  '/wms/cross-dock':           () => import('@/views/wms/CrossDockView.vue'),
  // ───── Phase WM-6 業界特化（占位） ─────
  '/wms/paper-roll':           () => import('@/views/wms/PaperRollView.vue'),
  '/wms/remnant':              () => import('@/views/wms/RemnantView.vue'),
  '/wms/plate-mold-stock':     () => import('@/views/wms/PlateMoldView.vue'),
  '/wms/ink-lot':              () => import('@/views/wms/InkLotView.vue'),
  '/wms/pallet':               () => import('@/views/wms/PalletView.vue'),
  '/wms/vmi':                  () => import('@/views/wms/VmiView.vue'),
  '/wms/sample-stock':         () => import('@/views/wms/SampleStockView.vue'),
  // ───── Phase WM-7 連携（占位） ─────
  '/wms/mobile-task':          () => import('@/views/wms/MobileTaskView.vue'),
  '/wms/wcs-task':             () => import('@/views/wms/WcsTaskView.vue'),
  '/wms/carrier':              () => import('@/views/wms/CarrierView.vue'),
  '/wms/iot-monitor':          () => import('@/views/wms/IotMonitorView.vue'),
  // ───── Phase WM-8 帳票（占位） ─────
  '/wms/report-center':        () => import('@/views/wms/ReportCenterView.vue'),
}

// 静态路由：登录页 / Layout壳子 / 独立窗口
const staticRoutes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'login',
    component: () => import('@/views/LoginView.vue')
  },
  // 独立窗口（popup）模式：不走 LayoutView，没有侧边栏/头部
  {
    path: '/estimate-calc/window',
    name: 'estimate-calc-window',
    component: () => import('@/views/erp/EstimateCalcView.vue'),
    meta: { standalone: true, title: '見積計算書' }
  },
  {
    path: '/quotation/window',
    name: 'quotation-window',
    component: () => import('@/views/erp/QuotationView.vue'),
    meta: { standalone: true, title: '御見積書' }
  },
  {
    path: '/product/window',
    name: 'product-window',
    component: () => import('@/views/erp/ProductMasterView.vue'),
    meta: { standalone: true, title: '製品マスタ' }
  },
  {
    path: '/order/window',
    name: 'order-window',
    component: () => import('@/views/erp/OrderEntryView.vue'),
    meta: { standalone: true, title: '受注入力' }
  },
  {
    path: '/business-partner/window',
    name: 'business-partner-window',
    component: () => import('@/views/erp/BusinessPartnerView.vue'),
    meta: { standalone: true, title: '取引先マスタ' }
  },
  // MES Control Tower 全屏大屏（独立路由、Layout 無し）
  {
    path: '/mes/control-tower/standalone',
    name: 'mes-control-tower-standalone',
    component: () => import('@/views/mes/ControlTowerView.vue'),
    meta: { standalone: true, title: 'MES Control Tower' }
  },
  {
    path: '/',
    name: 'layout',
    component: () => import('@/views/LayoutView.vue'),
    children: [] // 动态填充
  }
]

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: staticRoutes
})

// 标记是否已加载过动态路由
let dynamicRoutesAdded = false

/**
 * 根据菜单列表动态添加路由
 * menus 格式: [{ id, menuName, routePath, icon, parentId, orderNo }]
 */
export function addDynamicRoutes(menus: any[]) {
  // 先找到有 routePath 的菜单
  const routeMenus = menus.filter(m => m.routePath && viewModules[m.routePath])

  // 第一个有效路由作为默认跳转
  const firstRoute = routeMenus[0]?.routePath || '/login'

  routeMenus.forEach(menu => {
    const route: RouteRecordRaw = {
      path: menu.routePath.replace(/^\//, ''), // 去掉开头的 /，变成相对路径
      name: menu.routePath.replace(/^\//, ''),
      component: viewModules[menu.routePath]
    } as RouteRecordRaw
    // 添加为 layout 的子路由
    router.addRoute('layout', route)
  })

  // 更新 layout 的 redirect 为第一个有效页面
  // 通过添加一个带 redirect 的新 layout 路由来覆盖
  router.removeRoute('layout')
  router.addRoute({
    path: '/',
    name: 'layout',
    component: () => import('@/views/LayoutView.vue'),
    redirect: firstRoute,
    children: routeMenus.map(menu => ({
      path: menu.routePath.replace(/^\//, ''),
      name: menu.routePath.replace(/^\//, ''),
      component: viewModules[menu.routePath]
    })) as RouteRecordRaw[]
  })

  dynamicRoutesAdded = true
}

/**
 * 重置路由（退出登录时调用）
 */
export function resetRoutes() {
  dynamicRoutesAdded = false
  // 移除 layout 下的所有子路由
  router.removeRoute('layout')
  router.addRoute({
    path: '/',
    name: 'layout',
    component: () => import('@/views/LayoutView.vue'),
    children: []
  })
}

// 路由守卫
router.beforeEach(async (to, _from, next) => {
  const token = localStorage.getItem('token')

  // 1. 去登录页，放行
  if (to.path === '/login') {
    next()
    return
  }

  // 2. 没有 token，跳登录
  if (!token) {
    next('/login')
    return
  }

  // 3. 独立窗口（popup）：已有 token 即可，不依赖动态菜单
  if (to.meta?.standalone) {
    // i18n 优化 P4：进内容页前确保该路由所需语言命名空间已就绪（失败已在内部兜底全量）。
    await ensureNamespacesForPath(to.path)
    next()
    return
  }

  // 4. 有 token 但还没加载动态路由（页面刷新的情况）
  if (!dynamicRoutesAdded) {
    const menusStr = localStorage.getItem('menus')
    if (menusStr) {
      const menus = JSON.parse(menusStr)
      addDynamicRoutes(menus)
      // 重新导航到目标页面（因为路由刚添加，需要重新匹配）
      next({ ...to, replace: true })
      return
    } else {
      next('/login')
      return
    }
  }

  // 5. 路由已加载：先确保命名空间就绪再放行（i18n 优化 P4 懒加载）。
  await ensureNamespacesForPath(to.path)
  next()
})

export default router
