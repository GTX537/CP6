import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'

// 路由路径 → 组件的映射表（所有可能的页面）
const viewModules: Record<string, () => Promise<any>> = {
  '/dashboard': () => import('@/views/DashboardView.vue'),
  '/article': () => import('@/views/ArticleView.vue'),
  '/role': () => import('@/views/RoleView.vue'),
  '/menu': () => import('@/views/MenuView.vue'),
  '/permission': () => import('@/views/PermissionView.vue'),
  '/user': () => import('@/views/UserView.vue'),
  '/lang': () => import('@/views/LangView.vue'),
  '/dict': () => import('@/views/DictView.vue'),
  '/operlog': () => import('@/views/OperLogView.vue'),
  '/estimate-calc': () => import('@/views/EstimateCalcView.vue'),
  '/estimate-calc-list': () => import('@/views/EstimateCalcListView.vue'),
  '/quotation': () => import('@/views/QuotationView.vue'),
  '/quotation-list': () => import('@/views/QuotationListView.vue'),
  '/product': () => import('@/views/ProductMasterView.vue'),
  '/product-list': () => import('@/views/ProductMasterListView.vue'),
  '/order': () => import('@/views/OrderEntryView.vue'),
  '/order-list': () => import('@/views/OrderListView.vue'),
  '/order-price-correction': () => import('@/views/OrderPriceCorrectionView.vue'),
  '/business-partner': () => import('@/views/BusinessPartnerView.vue'),
  '/business-partner-list': () => import('@/views/BusinessPartnerListView.vue'),
  '/fsc-checklist': () => import('@/views/FscChecklistView.vue'),
  '/sheet-unit-price': () => import('@/views/SheetUnitPriceView.vue'),
  '/plate-mold': () => import('@/views/PlateMoldView.vue'),
  '/plate-mold-list': () => import('@/views/PlateMoldListView.vue'),
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
    component: () => import('@/views/EstimateCalcView.vue'),
    meta: { standalone: true, title: '見積計算書' }
  },
  {
    path: '/quotation/window',
    name: 'quotation-window',
    component: () => import('@/views/QuotationView.vue'),
    meta: { standalone: true, title: '御見積書' }
  },
  {
    path: '/product/window',
    name: 'product-window',
    component: () => import('@/views/ProductMasterView.vue'),
    meta: { standalone: true, title: '製品マスタ' }
  },
  {
    path: '/order/window',
    name: 'order-window',
    component: () => import('@/views/OrderEntryView.vue'),
    meta: { standalone: true, title: '受注入力' }
  },
  {
    path: '/business-partner/window',
    name: 'business-partner-window',
    component: () => import('@/views/BusinessPartnerView.vue'),
    meta: { standalone: true, title: '取引先マスタ' }
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
router.beforeEach((to, _from, next) => {
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

  // 5. 路由已加载，正常放行
  next()
})

export default router
