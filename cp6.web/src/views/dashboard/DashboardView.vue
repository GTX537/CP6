<template>
  <div class="cp-dash" :class="{ 'is-ready': dashboardReady }">
    <!-- page-head：标题 + 日期 + 预警摘要，右侧操作钮 -->
    <div class="page-head reveal-item" style="--delay: 0ms">
      <div>
        <h1>{{ t('dashboard.title') }}</h1>
        <p>
          {{ pageDateLabel }}
          <template v-if="headAlertParts.length"> · <b>{{ headAlertParts.join('、') }}</b></template>
        </p>
      </div>
      <div class="head-actions">
        <el-button :icon="Refresh" :loading="summaryState.status === 'loading'" :aria-label="t('刷新')" @click="loadData" />
        <button v-if="canGo('/order')" class="btn btn-primary" @click="go('/order')">
          <el-icon class="i"><DocumentAdd /></el-icon>{{ t('dashboard.qOrder') }}
        </button>
      </div>
    </div>

    <!-- KPI 卡片 -->
    <section class="kpis reveal-item" style="--delay: 70ms">
      <div
        v-for="card in statCards"
        :key="card.key"
        class="kpi-cell"
        @click="onCardClick(card.to)"
      >
        <CpStatCard
          :label="t('dashboard.' + card.key)"
          :value="card.value"
          :sub="stateLabel(summaryState.status)"
          :tone="kpiTone(card)"
          :clickable="canGo(card.to)"
        >
          <template #icon><component :is="card.icon" /></template>
        </CpStatCard>
      </div>
    </section>

    <!-- 双列 -->
    <section class="cols">
      <!-- 左列 -->
      <div class="col">
        <!-- 出货状态 -->
        <div class="card reveal-item" style="--delay: 140ms">
          <div class="card-head"><h2><el-icon class="i"><Van /></el-icon>{{ t('dashboard.shipStatus') }}</h2></div>
          <p v-if="summaryState.status !== 'ready'" class="load-state" role="status">{{ stateLabel(summaryState.status) }}</p>
          <div v-else class="ship">
            <div class="block">
              <div class="v num">{{ shipSummary.shipped }}</div>
              <div class="l">{{ t('dashboard.ship9') }}</div>
            </div>
            <div class="track">
              <div class="bar">
                <i :style="{ width: shipSummary.shippedPct + '%', background: 'var(--cp-brand)' }" />
                <i :style="{ width: shipSummary.partialPct + '%', background: 'var(--cp-warn)' }" />
                <i :style="{ width: (100 - shipSummary.shippedPct - shipSummary.partialPct) + '%', background: 'var(--cp-line-soft)' }" />
              </div>
              <div class="bl"><span class="num">{{ shipSummary.shippedPct }}%</span></div>
            </div>
            <div class="block">
              <div class="v num" style="color: var(--cp-warn)">{{ shipSummary.partial }}</div>
              <div class="l">{{ t('dashboard.ship5') }}</div>
            </div>
            <div class="block">
              <div class="v num" style="color: var(--cp-muted)">{{ shipSummary.unshipped }}</div>
              <div class="l">{{ t('dashboard.ship0') }}</div>
            </div>
          </div>
        </div>

        <!-- 最近受注 -->
        <div class="card reveal-item" style="--delay: 200ms">
          <div class="card-head"><h2><el-icon class="i"><List /></el-icon>{{ t('dashboard.recentOrders') }}</h2></div>

          <p v-if="summaryState.status !== 'ready'" class="load-state" role="status">{{ stateLabel(summaryState.status) }}</p>
          <el-table v-else-if="recentOrders.length && !isMobile" :data="recentOrders" stripe size="small">
            <el-table-column prop="webOrderNo" :label="t('dashboard.orderNo')" min-width="130" />
            <el-table-column prop="customerCd" :label="t('dashboard.customer')" min-width="90" />
            <el-table-column prop="quantity" :label="t('dashboard.qty')" width="80" align="right">
              <template #default="{ row }">{{ fmtQty(row.quantity) }}</template>
            </el-table-column>
            <el-table-column :label="t('dashboard.shipStatus')" width="90">
              <template #default="{ row }">
                <el-tag :type="shipColor(row.shipStatus)" size="small">{{ shipLabel(row.shipStatus) }}</el-tag>
              </template>
            </el-table-column>
          </el-table>

          <div v-else-if="recentOrders.length && isMobile" class="simple-list">
            <div v-for="o in recentOrders" :key="o.webOrderNo" class="simple-row">
              <span class="simple-row-name">{{ o.webOrderNo }}</span>
              <el-tag :type="shipColor(o.shipStatus)" size="small">{{ shipLabel(o.shipStatus) }}</el-tag>
            </div>
          </div>

          <el-empty v-else :description="t('dashboard.noData')" :image-size="60" />
        </div>

        <!-- 快捷入口 -->
        <div class="card reveal-item" style="--delay: 260ms">
          <div class="card-head"><h2><el-icon class="i"><Grid /></el-icon>{{ t('dashboard.quickEntry') }}</h2></div>
          <div class="quick">
            <div v-for="link in quickLinks" :key="link.path" class="q" @click="go(link.path)">
              <el-icon class="i" :size="21"><component :is="link.icon" /></el-icon>
              <span>{{ t('dashboard.' + link.key) }}</span>
            </div>
          </div>
        </div>
      </div>

      <!-- 右列 -->
      <div class="col">
        <!-- 预警条幅：实时业务通知（最新一条） -->
        <transition name="el-fade-in">
          <div v-if="latestNotice" class="alertbar reveal-item" style="--delay: 140ms" :style="alertBarStyle(latestNotice.level)">
            <el-icon class="i"><Warning /></el-icon>
            <p>
              <small>{{ latestNotice.source || 'SYS' }}</small>
              {{ latestNotice.title }} {{ latestNotice.refNo }} — {{ latestNotice.message }}
            </p>
            <span class="act" @click="latestNotice = null">✕</span>
          </div>
        </transition>

        <!-- 制造进度 -->
        <div class="card reveal-item" style="--delay: 200ms">
          <div class="card-head"><h2><el-icon class="i"><SetUp /></el-icon>{{ t('dashboard.workOrderStatus') }}</h2></div>
          <p v-if="summaryState.status !== 'ready'" class="load-state" role="status">{{ stateLabel(summaryState.status) }}</p>
          <DonutChart v-else :segments="donutSegments" :center-label="t('dashboard.workOrderStatus')">
            <template #empty><span class="lg-empty">{{ t('dashboard.noData') }}</span></template>
          </DonutChart>
        </div>

        <!-- 实时业务通知 feed -->
        <div class="card reveal-item" style="--delay: 260ms">
          <div class="card-head"><h2><el-icon class="i"><Bell /></el-icon>{{ t('dashboard.liveFeed') }}</h2></div>

          <div v-if="feed.length" class="feed-list">
            <div v-for="(n, i) in feed" :key="i" class="feed-row">
              <span class="feed-dot" :class="`lvl-${(n.level || 'info').toLowerCase()}`" />
              <div class="feed-body">
                <div class="feed-title">{{ n.title }} <em>{{ n.refNo }}</em></div>
                <div class="feed-msg">{{ n.message }}</div>
              </div>
              <span class="feed-time">{{ fmtTime(n.createDate) }}</span>
            </div>
          </div>

          <el-empty v-else :description="t('dashboard.waitingFeed')" :image-size="60" />
        </div>
      </div>
    </section>

    <!-- Phase 8 — 受注済未出荷 Dashboard (Gap 3.4) -->
    <div class="card reveal-item" style="--delay: 320ms">
      <div class="card-head card-head-wrap">
        <h2>
          {{ t('dashboard.unshipped.title') }}
          <el-tag v-if="unshippedTotal > 0" size="small" type="warning" style="margin-left: 8px">
            {{ unshippedTotal }}
          </el-tag>
        </h2>
        <div class="card-head-controls">
          <el-switch
            v-model="onlyOverdue"
            :active-text="t('dashboard.unshipped.onlyOverdue')"
            @change="loadUnshipped"
          />
          <el-button :icon="Refresh" size="small" @click="loadUnshipped" :loading="unshippedLoading" :aria-label="t('刷新')" />
        </div>
      </div>

      <div class="card-body">
        <p v-if="unshippedState.status !== 'ready'" class="load-state" role="status">{{ stateLabel(unshippedState.status) }}</p>
        <el-table
          v-else-if="unshippedRows.length && !isMobile"
          :data="unshippedRows"
          stripe
          size="small"
          @row-click="goOrderDetail"
          style="cursor: pointer"
        >
          <el-table-column prop="webOrderNo" :label="t('dashboard.unshipped.no')" min-width="130" />
          <el-table-column :label="t('dashboard.unshipped.customer')" min-width="120">
            <template #default="{ row }">
              {{ row.customerName || row.customerCd }}
            </template>
          </el-table-column>
          <el-table-column :label="t('dashboard.unshipped.deliveryDate')" min-width="140">
            <template #default="{ row }">
              <span :class="{ 'overdue-text': row.isOverdue }">
                {{ fmtDate(row.customerDeliveryDate) }}
              </span>
              <el-tag
                v-if="row.isOverdue"
                type="danger"
                size="small"
                style="margin-left: 6px"
              >
                {{ t('dashboard.unshipped.overdue') }}
              </el-tag>
              <el-tag
                v-else-if="row.daysUntilDue !== null && row.daysUntilDue !== undefined && row.daysUntilDue <= 3"
                type="warning"
                size="small"
                style="margin-left: 6px"
              >
                D-{{ row.daysUntilDue }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column :label="t('dashboard.unshipped.status')" min-width="120">
            <template #default="{ row }">
              <el-tag :type="orderLifecycleColor(row.orderStatus)" size="small">
                {{ t(`dashboard.unshipped.lifecycle.${row.orderStatus}`) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column :label="t('dashboard.unshipped.qty')" width="170" align="right">
            <template #default="{ row }">
              {{ fmtQty(row.shippedQty) }} / {{ fmtQty(row.orderedQty) }}
              <span style="color: #f56c6c; margin-left: 4px">(-{{ fmtQty(row.remainingQty) }})</span>
            </template>
          </el-table-column>
          <el-table-column :label="t('dashboard.unshipped.mes')" min-width="180">
            <template #default="{ row }">
              <span style="font-size: 12px; color: #606266">{{ row.mesStatusSummary || '—' }}</span>
            </template>
          </el-table-column>
          <el-table-column :label="t('dashboard.unshipped.wms')" min-width="180">
            <template #default="{ row }">
              <span style="font-size: 12px; color: #606266">{{ row.wmsStatusSummary || '—' }}</span>
            </template>
          </el-table-column>
        </el-table>

        <!-- 手机端简化列表 -->
        <div v-else-if="unshippedRows.length && isMobile" class="simple-list">
          <div
            v-for="row in unshippedRows"
            :key="row.webOrderNo"
            class="simple-row"
            @click="goOrderDetail(row)"
            style="cursor: pointer"
          >
            <span class="simple-row-name">
              {{ row.webOrderNo }} — {{ row.customerName || row.customerCd }}
            </span>
            <el-tag v-if="row.isOverdue" type="danger" size="small">
              {{ t('dashboard.unshipped.overdue') }}
            </el-tag>
          </div>
        </div>

        <el-empty v-else :description="t('dashboard.noData')" :image-size="60" />

        <el-pagination
          v-if="unshippedTotal > unshippedQuery.pageSize"
          v-model:current-page="unshippedQuery.page"
          v-model:page-size="unshippedQuery.pageSize"
          :page-sizes="[10, 25, 50, 100]"
          :total="unshippedTotal"
          layout="total, sizes, prev, pager, next"
          small
          background
          style="margin-top: 12px; justify-content: flex-end"
          @current-change="loadUnshipped"
          @size-change="loadUnshipped"
        />
      </div>
    </div>

    <!-- T13 — 90天以上滞留库存 -->
    <div class="card reveal-item" style="--delay: 380ms">
      <div class="card-head card-head-wrap">
        <h2>
          {{ t('dashboard.stockDwell.title') }}
          <el-tag
            v-if="stockDwellSummary && stockDwellSummary.over90Qty > 0"
            size="small"
            type="danger"
            style="margin-left: 8px"
          >
            {{ fmtQty(stockDwellSummary?.over90Qty) }}
          </el-tag>
        </h2>
        <div class="card-head-controls">
          <el-button :icon="Refresh" size="small" @click="loadStockDwell" :loading="stockDwellLoading" :aria-label="t('刷新')" />
          <a href="javascript:void(0)" @click="go('/wms/stock-dwell')">{{ t('dashboard.stockDwell.open') }}</a>
        </div>
      </div>

      <div class="card-body">
        <p v-if="stockDwellState.status !== 'ready'" class="load-state" role="status">{{ stateLabel(stockDwellState.status) }}</p>
        <div v-else-if="stockDwellRows.length" class="stock-dwell-widget">
          <div v-for="row in stockDwellRows" :key="row.groupKey" class="stock-dwell-widget-row">
            <span class="stock-dwell-widget-name">{{ row.groupLabel }}</span>
            <span class="stock-dwell-widget-qty">{{ fmtQty(row.bucketOver90Qty) }}</span>
            <el-progress
              :percentage="stockDwellPercent(row.bucketOver90Qty)"
              :stroke-width="12"
              :show-text="false"
            />
          </div>
        </div>

        <el-empty v-else :description="t('dashboard.noData')" :image-size="60" />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { Refresh, DocumentAdd, Van, List, Grid, Warning, SetUp, Bell } from '@element-plus/icons-vue'
import { dashboardApi } from '@/api/sys/dashboard'
import { orderApi } from '@/api/erp/order'
import { stockDwellApi } from '@/api/wms/stockDwell'
import { formatQty } from '@/utils/format'
import { getConnection, startConnection } from '@/utils/signalr'
import { useBreakpoint } from '@/composables/useBreakpoint'
import CpStatCard from '@/components/templates/CpStatCard.vue'
import DonutChart from './components/DonutChart.vue'
import type { UnshippedOrderItemDto, UnshippedOrderQuery } from '@/types/erp/order'
import type { StockDwellRow } from '@/types/wms/stockDwell'
import { usePlatformStore } from '@/stores/platform'
import { createDashboardLoader, parseDashboard, parseUnshipped, parseStockDwell, type LoadStatus } from './dashboardLoadState'

const { t, locale } = useI18n()
const router = useRouter()
const { isMobile } = useBreakpoint()

interface Notice {
  eventType?: string
  level?: string
  title?: string
  message?: string
  source?: string
  refNo?: string
  createDate?: string
}

const summaryLoader = createDashboardLoader(parseDashboard)
const unshippedLoader = createDashboardLoader(parseUnshipped)
const stockDwellLoader = createDashboardLoader(parseStockDwell)
const summaryState = summaryLoader.state
const unshippedState = unshippedLoader.state
const stockDwellState = stockDwellLoader.state
const summary = computed(() => summaryState.value.data?.summary)
const recentOrders = computed(() => summaryState.value.data?.recentOrders ?? [])
const workOrderStatus = computed(() => summaryState.value.data?.workOrderStatus ?? [])
function stateLabel(status: LoadStatus) {
  if (status === 'loading') return `${t('加载')}…`
  if (status === 'forbidden') return `403 · ${t('sec.event.8')}`
  if (status === 'unavailable') return t('请求失败')
  return ''
}
const latestNotice = ref<Notice | null>(null)
const feed = ref<Notice[]>([])
const dashboardReady = ref(false)

interface StatCard {
  key: string
  value: number | string
  icon: string
  color: string
  to?: string
  alert?: boolean
}

const statCards = computed<StatCard[]>(() => [
  { key: 'todayOrders',      value: summary.value?.todayOrders ?? '—',      icon: 'ShoppingCart', color: '#409eff', to: '/order-list' },
  { key: 'monthOrders',      value: summary.value?.monthOrders ?? '—',      icon: 'ShoppingBag',  color: '#67c23a', to: '/order-list' },
  { key: 'activeWorkOrders', value: summary.value?.activeWorkOrders ?? '—', icon: 'SetUp',        color: '#e6a23c', to: '/mes/work-order-list' },
  { key: 'monthCompleted',   value: summary.value?.monthCompleted ?? '—',   icon: 'CircleCheck',  color: '#67c23a', to: '/mes/work-order-list' },
  { key: 'pendingOutbound',  value: summary.value?.pendingOutbound ?? '—',  icon: 'Van',          color: '#409eff', alert: true, to: '/wms/outbound-order-list' },
  { key: 'stockWarnings',    value: summary.value?.stockWarnings ?? '—',    icon: 'Warning',      color: '#e6a23c', alert: true, to: '/wms/stock' },
  { key: 'pendingApprovals', value: summary.value?.pendingApprovals ?? '—', icon: 'Stamp',        color: '#e6a23c', alert: true, to: '/product-list' },
  { key: 'totalProducts',    value: summary.value?.totalProducts ?? '—',    icon: 'Goods',        color: '#909399', to: '/product-list' }
])

// 仅当目标路由已注册时，卡片才可点击跳转
function canGo(to?: string) {
  return !!to && router.resolve(to).matched.length > 0
}
function onCardClick(to?: string) {
  if (canGo(to)) go(to as string)
}

// KPI 卡视觉基调：alert 系一律 danger；其余按原有 color 语义映射到 token 基调
function kpiTone(card: StatCard): 'brand' | 'info' | 'warn' | 'danger' {
  if (card.alert && typeof card.value === 'number' && card.value > 0) return 'danger'
  if (card.color === '#e6a23c') return 'warn'
  if (card.color === '#409eff') return 'info'
  return 'brand'
}

// 快捷入口（仅显示已注册的路由）
const allQuickLinks = [
  { key: 'qOrder',     path: '/order',                 icon: 'DocumentAdd', color: '#409eff' },
  { key: 'qWorkOrder', path: '/mes/work-order',        icon: 'SetUp',       color: '#e6a23c' },
  { key: 'qInbound',   path: '/wms/inbound-receipt',   icon: 'Download',    color: '#67c23a' },
  { key: 'qOutbound',  path: '/wms/outbound-order',    icon: 'Upload',      color: '#f56c6c' },
  { key: 'qStock',     path: '/wms/stock',             icon: 'Box',         color: '#909399' },
  { key: 'qStockTake', path: '/wms/stock-take-list',   icon: 'Files',       color: '#606266' }
]
const quickLinks = computed(() =>
  allQuickLinks.filter(l => router.resolve(l.path).matched.length > 0)
)

function go(path: string) {
  router.push(path).catch(() => {})
}

// ── page-head：日期 + 预警摘要（沿用现有 summary 字段与既有 key，不新增 API/文案）──
const pageDateLabel = computed(() => {
  const map: Record<string, string> = { 'zh-CN': 'zh-CN', 'zh-TW': 'zh-TW', ja: 'ja-JP', ko: 'ko-KR', en: 'en-US' }
  const loc = map[locale.value] || 'ja-JP'
  try {
    return new Date().toLocaleDateString(loc, { year: 'numeric', month: 'long', day: 'numeric', weekday: 'long' })
  } catch {
    return new Date().toLocaleDateString()
  }
})
const headAlertParts = computed(() => {
  const parts: string[] = []
  if (summary.value && summary.value.stockWarnings > 0) parts.push(`${summary.value.stockWarnings} ${t('dashboard.stockWarnings')}`)
  if (summary.value && summary.value.pendingApprovals > 0) parts.push(`${summary.value.pendingApprovals} ${t('dashboard.pendingApprovals')}`)
  return parts
})

// ── 出货状态条（来自已加载的 recentOrders，非新 API，仅为该列表样本的出货占比）──
const shipSummary = computed(() => {
  const total = recentOrders.value.length
  const shipped = recentOrders.value.filter(o => o.shipStatus === 9).length
  const partial = recentOrders.value.filter(o => o.shipStatus === 5).length
  const unshipped = recentOrders.value.filter(o => o.shipStatus === 0).length
  const shippedPct = total ? Math.round((shipped / total) * 100) : 0
  const partialPct = total ? Math.round((partial / total) * 100) : 0
  return { total, shipped, partial, unshipped, shippedPct, partialPct }
})

// ── 制造进度环图（来自既有 workOrderStatus）──
const woChartColor: Record<number, string> = {
  0: 'var(--cp-faint)',
  1: 'var(--cp-info)',
  2: 'var(--cp-brand)',
  3: 'var(--cp-warn)',
  4: 'var(--cp-brand-2)',
  5: 'var(--cp-danger)',
  6: 'var(--cp-ok)',
  9: 'var(--cp-faint)'
}
const donutSegments = computed(() =>
  workOrderStatus.value.map(s => ({
    label: woLabel(s.status),
    value: s.count,
    color: woChartColor[s.status] || 'var(--cp-faint)'
  }))
)

// ── 出荷ステータス ──
function shipLabel(s: number) { return t(`dashboard.ship${s}`) }
function shipColor(s: number) {
  return ({ 0: 'info', 5: 'warning', 9: 'success' } as Record<number, any>)[s] || 'info'
}

// ── 製造指図ステータス ──
function woLabel(s: number) { return t(`dashboard.wo${s}`) }

function alertBarStyle(level?: string) {
  const l = (level || '').toLowerCase()
  if (l === 'error') return { background: 'linear-gradient(115deg, var(--cp-danger), #F0777B)' }
  if (l === 'warning') return { background: 'linear-gradient(115deg, var(--cp-warn), #F5B65C)' }
  return { background: 'linear-gradient(115deg, var(--cp-brand-deep), var(--cp-brand-2))' }
}

function fmtQty(q?: number | null) {
  if (q === null || q === undefined) return '-'
  return formatQty(q)
}
function fmtTime(d?: string) {
  if (!d) return ''
  const dt = new Date(d)
  return `${String(dt.getHours()).padStart(2, '0')}:${String(dt.getMinutes()).padStart(2, '0')}`
}

function fmtDate(d?: string | null) {
  if (!d) return '—'
  const dt = new Date(d)
  return `${dt.getFullYear()}-${String(dt.getMonth() + 1).padStart(2, '0')}-${String(dt.getDate()).padStart(2, '0')}`
}

function todayString() {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

// ───── Phase 8 受注済未出荷 widget ─────
const unshippedRows = computed(() => unshippedState.value.data?.rows ?? [])
const unshippedTotal = computed(() => unshippedState.value.data?.total ?? 0)
const unshippedLoading = computed(() => unshippedState.value.status === 'loading')
const onlyOverdue = ref(false)
const unshippedQuery = reactive<UnshippedOrderQuery>({
  page: 1,
  pageSize: 10,
  sortField: 'deliveryDate',
  sortOrder: 'asc',
})

function loadUnshipped() {
  checkIdentity()
  return unshippedLoader.load(() => orderApi.searchUnshipped({
    ...unshippedQuery,
    onlyOverdue: onlyOverdue.value || undefined,
  }), checkIdentity)
}

function goOrderDetail(row: UnshippedOrderItemDto) {
  router.push(`/order?webOrderNo=${encodeURIComponent(row.webOrderNo)}`)
}

// ───── T13 stock dwell widget ─────
const stockDwellSummary = computed(() => stockDwellState.value.data)
const stockDwellLoading = computed(() => stockDwellState.value.status === 'loading')
const stockDwellRows = computed<StockDwellRow[]>(() =>
  (stockDwellSummary.value?.rows ?? [])
    .filter(row => row.bucketOver90Qty > 0)
    .slice(0, 6),
)
const maxStockDwellOver90 = computed(() =>
  Math.max(...stockDwellRows.value.map(row => row.bucketOver90Qty), 1),
)

function stockDwellPercent(qty: number) {
  return Math.round((qty / maxStockDwellOver90.value) * 100)
}

function loadStockDwell() {
  checkIdentity()
  return stockDwellLoader.load(() => stockDwellApi.summary({
    groupBy: 'product',
    asOfDate: todayString(),
  }), checkIdentity)
}

function orderLifecycleColor(s: string): 'success' | 'warning' | 'danger' | 'info' | '' {
  switch (s) {
    case 'CONFIRMED': return 'info'
    case 'IN_PRODUCTION': return 'warning'
    case 'PARTIALLY_CANCELLED': return 'danger'
    default: return ''
  }
}

const platformStore = usePlatformStore()
let disposed = false
let realtimeInvalidated = false
let noticeTimer: number | undefined
let connection: ReturnType<typeof getConnection> | undefined

// Existing client signals only; this is not a server permission/tenant version.
function identitySnapshot() {
  return JSON.stringify([
    localStorage.getItem('cp6_authed'), localStorage.getItem('userName'),
    localStorage.getItem('menus'), localStorage.getItem('cp6_isPlatformAdmin'),
    platformStore.isPlatformAdmin, platformStore.impersonating,
  ])
}
let identity = identitySnapshot()
function checkIdentity() {
  if (disposed) return false
  const next = identitySnapshot()
  if (next === identity) return true
  identity = next
  summaryLoader.invalidate()
  unshippedLoader.invalidate()
  stockDwellLoader.invalidate()
  latestNotice.value = null
  feed.value = []
  window.clearTimeout(noticeTimer)
  // The shared connection may still belong to the previous session until navigation/reload.
  realtimeInvalidated = true
  return false
}
watch(identitySnapshot, checkIdentity, { flush: 'sync' })

function loadData() {
  checkIdentity()
  // SignalR refreshes summary only: widget reads must not create an OperLog refresh loop.
  return summaryLoader.load(() => dashboardApi.getSummary(), checkIdentity)
}
function onBusinessNotification(n: Notice) {
  if (!checkIdentity() || realtimeInvalidated) return
  latestNotice.value = n
  feed.value = [n, ...feed.value].slice(0, 12)
  window.clearTimeout(noticeTimer)
  noticeTimer = window.setTimeout(() => {
    if (!disposed) latestNotice.value = null
  }, 6000)
  void loadData()
}
function onOperLog() {
  if (checkIdentity() && !realtimeInvalidated) void loadData()
}

onMounted(() => {
  dashboardReady.value = true
  window.addEventListener('storage', checkIdentity)
  window.addEventListener('focus', checkIdentity)
  window.addEventListener('cp6-menu-updated', checkIdentity)
  void loadData()
  void loadUnshipped()
  void loadStockDwell()
  // Start independently even if the first summary request fails or never settles.
  void (async () => {
    try {
      await startConnection()
      if (disposed) return
      connection = getConnection()
      connection.on('BusinessNotification', onBusinessNotification)
      connection.on('NewOperLog', onOperLog)
    } catch {
      // SignalR failure does not block the dashboard shell or HTTP groups.
    }
  })()
})

onUnmounted(() => {
  disposed = true
  summaryLoader.dispose()
  unshippedLoader.dispose()
  stockDwellLoader.dispose()
  window.clearTimeout(noticeTimer)
  window.removeEventListener('storage', checkIdentity)
  window.removeEventListener('focus', checkIdentity)
  window.removeEventListener('cp6-menu-updated', checkIdentity)
  connection?.off('BusinessNotification', onBusinessNotification)
  connection?.off('NewOperLog', onOperLog)
})
</script>

<style scoped>
.load-state {
  padding: 20px;
  color: var(--cp-muted);
  text-align: center;
}

.cp-dash {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.reveal-item {
  opacity: 0;
  transform: translateY(22px) scale(0.985);
  filter: blur(10px);
}

.cp-dash.is-ready .reveal-item {
  animation: dashboard-rise 0.7s cubic-bezier(0.22, 1, 0.36, 1) forwards;
  animation-delay: var(--delay, 0ms);
}

/* ===== page-head ===== */
.page-head {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 14px;
  flex-wrap: wrap;
}
.page-head h1 {
  font-size: var(--cp-fs-3xl);
  font-weight: 800;
  color: var(--cp-ink);
  letter-spacing: -.3px;
}
.page-head p {
  font-size: var(--cp-fs-base);
  color: var(--cp-muted);
  margin-top: 5px;
  font-weight: 600;
}
.page-head p b {
  color: var(--cp-danger);
}
.btn {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  border: none;
  border-radius: 11px;
  padding: 9px 16px;
  font-size: var(--cp-fs-base);
  font-weight: 800;
  cursor: pointer;
  font-family: inherit;
  transition: var(--cp-t-base);
}
.btn-primary {
  background: var(--cp-brand-grad);
  color: #fff;
  box-shadow: var(--cp-brand-glow);
}
.btn-primary:hover {
  transform: translateY(-1px);
}

/* ===== KPI 区 ===== */
.kpis {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 16px;
}
.kpi-cell {
  min-width: 0;
}

/* ===== 双列 ===== */
.cols {
  display: grid;
  grid-template-columns: 1.62fr 1fr;
  gap: 18px;
  align-items: start;
}
.col {
  display: flex;
  flex-direction: column;
  gap: 18px;
  min-width: 0;
}
.card {
  background: var(--cp-card);
  border-radius: var(--cp-r-lg);
  box-shadow: var(--cp-shadow-1);
}
.card-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 20px;
  border-bottom: 1px solid var(--cp-line-soft);
  gap: 12px;
  flex-wrap: wrap;
}
.card-head h2 {
  font-size: var(--cp-fs-lg);
  font-weight: 800;
  color: var(--cp-ink);
  display: flex;
  align-items: center;
  gap: 9px;
}
.card-head h2 .i {
  font-size: 16px;
  color: var(--cp-brand-deep);
}
.card-head a {
  font-size: var(--cp-fs-xs);
  font-weight: 800;
  color: var(--cp-brand-deep);
  cursor: pointer;
}
.card-head a:hover {
  text-decoration: underline;
}
.card-head-wrap {
  justify-content: space-between;
}
.card-head-controls {
  display: flex;
  gap: 12px;
  align-items: center;
}
.card-body {
  padding: 14px 20px 18px;
}

/* 出货焦点条 */
.ship {
  display: flex;
  align-items: center;
  gap: 22px;
  padding: 18px 22px;
}
.ship .block {
  text-align: center;
  min-width: 74px;
}
.ship .block .v {
  font-size: 30px;
  font-weight: 800;
  color: var(--cp-ink);
  line-height: 1;
}
.ship .block .l {
  font-size: var(--cp-fs-2xs);
  color: var(--cp-muted);
  font-weight: 800;
  margin-top: 5px;
  letter-spacing: .8px;
}
.ship .track {
  flex: 1;
}
.ship .bar {
  height: 9px;
  border-radius: 6px;
  background: var(--cp-line-soft);
  overflow: hidden;
  display: flex;
  gap: 2px;
}
.ship .bar i {
  height: 100%;
  border-radius: 6px;
}
.ship .bl {
  display: flex;
  justify-content: flex-end;
  font-size: 11.5px;
  color: var(--cp-muted);
  font-weight: 700;
  margin-top: 8px;
}

/* 快捷入口 */
.quick {
  display: grid;
  grid-template-columns: repeat(6, 1fr);
  gap: 12px;
  padding: 16px 20px;
}
.q {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 9px;
  padding: 15px 6px;
  border-radius: var(--cp-r-md);
  border: 1px solid var(--cp-line);
  font-size: var(--cp-fs-sm);
  font-weight: 800;
  color: var(--cp-ink);
  cursor: pointer;
  transition: var(--cp-t-base);
}
.q .i {
  font-size: 21px;
  color: var(--cp-brand-deep);
}
.q:hover {
  border-color: var(--cp-brand);
  background: var(--cp-brand-bg);
  transform: translateY(-3px);
  box-shadow: var(--cp-shadow-1);
}

/* 预警条幅 */
.alertbar {
  border-radius: var(--cp-r-lg);
  padding: 18px 20px;
  display: flex;
  align-items: center;
  gap: 14px;
  color: #fff;
  box-shadow: var(--cp-brand-glow);
  position: relative;
  overflow: hidden;
}
.alertbar .i {
  font-size: 22px;
  flex-shrink: 0;
}
.alertbar p {
  flex: 1;
  font-size: var(--cp-fs-md);
  font-weight: 800;
  line-height: 1.4;
  position: relative;
  z-index: 1;
}
.alertbar p small {
  display: block;
  font-size: var(--cp-fs-2xs);
  font-weight: 700;
  opacity: .8;
  letter-spacing: 1.4px;
  margin-bottom: 3px;
}
.alertbar .act {
  cursor: pointer;
  position: relative;
  z-index: 1;
  font-weight: 800;
  opacity: .85;
  flex-shrink: 0;
}
.alertbar .act:hover {
  opacity: 1;
}

/* 实时通知 feed */
.feed-list {
  display: flex;
  flex-direction: column;
  max-height: 320px;
  overflow-y: auto;
  padding: 4px 20px 12px;
}
.feed-row {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 10px 0;
  border-bottom: 1px dashed var(--cp-line);
}
.feed-row:last-child {
  border-bottom: none;
}
.feed-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  margin-top: 6px;
  flex-shrink: 0;
  background: var(--cp-brand);
}
.feed-dot.lvl-warning { background: var(--cp-warn); }
.feed-dot.lvl-error { background: var(--cp-danger); }
.feed-body {
  flex: 1;
  min-width: 0;
}
.feed-title {
  font-size: var(--cp-fs-sm);
  font-weight: 700;
  color: var(--cp-ink);
}
.feed-title em {
  font-style: normal;
  color: var(--cp-muted);
  font-weight: 400;
}
.feed-msg {
  font-size: var(--cp-fs-xs);
  color: var(--cp-muted);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.feed-time {
  font-size: var(--cp-fs-2xs);
  color: var(--cp-faint);
  flex-shrink: 0;
}

.lg-empty {
  font-size: var(--cp-fs-xs);
  color: var(--cp-muted);
  font-weight: 700;
}

.simple-list {
  display: flex;
  flex-direction: column;
  padding: 4px 20px 12px;
}
.simple-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 0;
  border-bottom: 1px solid var(--cp-line-soft);
  font-size: var(--cp-fs-base);
}
.simple-row:last-child {
  border-bottom: none;
}
.simple-row-name {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--cp-ink);
  margin-right: 8px;
}

.stock-dwell-widget {
  display: grid;
  gap: 12px;
}
.stock-dwell-widget-row {
  display: grid;
  grid-template-columns: minmax(180px, 1fr) 110px minmax(160px, 260px);
  align-items: center;
  gap: 12px;
}
.stock-dwell-widget-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--cp-ink);
  font-size: var(--cp-fs-base);
}
.stock-dwell-widget-qty {
  color: var(--cp-danger);
  font-weight: 650;
  font-size: var(--cp-fs-base);
  text-align: right;
}
.overdue-text {
  color: var(--cp-danger);
}

@keyframes dashboard-rise {
  from {
    opacity: 0;
    transform: translateY(22px) scale(0.985);
    filter: blur(10px);
  }
  to {
    opacity: 1;
    transform: translateY(0) scale(1);
    filter: blur(0);
  }
}

@media (prefers-reduced-motion: reduce) {
  .reveal-item,
  .cp-dash.is-ready .reveal-item {
    animation: none !important;
    transform: none !important;
    filter: none !important;
    opacity: 1 !important;
  }
}

@media (max-width: 1180px) {
  .kpis {
    grid-template-columns: repeat(2, 1fr);
  }
  .cols {
    grid-template-columns: 1fr;
  }
  .quick {
    grid-template-columns: repeat(3, 1fr);
  }
}

@media (max-width: 767px) {
  .kpis {
    grid-template-columns: repeat(2, 1fr);
  }
  .quick {
    grid-template-columns: repeat(3, 1fr);
  }
  .stock-dwell-widget-row {
    grid-template-columns: 1fr 92px;
  }
  .stock-dwell-widget-row :deep(.el-progress) {
    grid-column: 1 / -1;
  }
}
</style>
