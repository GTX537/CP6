<template>
  <div class="dashboard" :class="{ 'is-ready': dashboardReady }">
    <!-- 实时业务通知横幅 -->
    <transition name="el-fade-in">
      <el-alert
        v-if="latestNotice"
        :title="`[${latestNotice.source || 'SYS'}] ${latestNotice.title} ${latestNotice.refNo || ''}`"
        :description="latestNotice.message"
        :type="alertType(latestNotice.level)"
        show-icon
        :closable="true"
        @close="latestNotice = null"
        style="margin-bottom: 12px"
      />
    </transition>

    <!-- KPI 卡片 -->
    <el-row :gutter="16" class="stat-cards">
      <el-col
        v-for="(card, index) in statCards"
        :key="card.key"
        :xs="12"
        :sm="12"
        :md="6"
        :lg="6"
        class="reveal-item"
        :style="{ '--delay': `${index * 70}ms` }"
      >
        <el-card
          shadow="hover"
          class="stat-card"
          :class="{ clickable: canGo(card.to) }"
          :body-style="{ padding: '20px' }"
          @click="onCardClick(card.to)"
        >
          <div class="stat-card-inner">
            <div>
              <div class="stat-label">
                {{ t('dashboard.' + card.key) }}
                <el-icon v-if="canGo(card.to)" class="goto-hint"><component :is="'Right'" /></el-icon>
              </div>
              <div class="stat-value" :class="{ 'is-alert': card.alert && card.value > 0 }">
                {{ card.value }}
              </div>
            </div>
            <el-icon :size="38" :color="card.alert && card.value > 0 ? '#f56c6c' : card.color">
              <component :is="card.icon" />
            </el-icon>
          </div>
        </el-card>
      </el-col>
    </el-row>

    <!-- 快捷入口 -->
    <el-card shadow="hover" class="quick-card reveal-item" style="--delay: 560ms; margin-top: 16px">
      <template #header>{{ t('dashboard.quickEntry') }}</template>
      <div class="quick-grid">
        <div
          v-for="link in quickLinks"
          :key="link.path"
          class="quick-item"
          @click="go(link.path)"
        >
          <el-icon :size="22" :color="link.color"><component :is="link.icon" /></el-icon>
          <span>{{ t('dashboard.' + link.key) }}</span>
        </div>
      </div>
    </el-card>

    <!-- 面板：最近受注 / 制造进度 / 实时通知 -->
    <el-row :gutter="16" style="margin-top: 16px">
      <el-col :xs="24" :sm="24" :md="10" :lg="10" class="dash-col reveal-item" style="--delay: 640ms">
        <el-card shadow="hover" class="dash-panel">
          <template #header>{{ t('dashboard.recentOrders') }}</template>

          <el-table v-if="recentOrders.length && !isMobile" :data="recentOrders" stripe size="small">
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
        </el-card>
      </el-col>

      <el-col :xs="24" :sm="24" :md="7" :lg="7" class="dash-col reveal-item" style="--delay: 720ms">
        <el-card shadow="hover" class="dash-panel">
          <template #header>{{ t('dashboard.workOrderStatus') }}</template>

          <div v-if="workOrderStatus.length">
            <div v-for="s in workOrderStatus" :key="s.status" class="method-row">
              <el-tag :type="woColor(s.status)" size="small" style="width: 78px; text-align: center">
                {{ woLabel(s.status) }}
              </el-tag>
              <el-progress
                :percentage="woPercent(s.count)"
                :stroke-width="16"
                :show-text="true"
                :format="() => String(s.count)"
                style="flex: 1; margin-left: 12px"
              />
            </div>
          </div>

          <el-empty v-else :description="t('dashboard.noData')" :image-size="60" />
        </el-card>
      </el-col>

      <el-col :xs="24" :sm="24" :md="7" :lg="7" class="dash-col reveal-item" style="--delay: 800ms">
        <el-card shadow="hover" class="dash-panel">
          <template #header>{{ t('dashboard.liveFeed') }}</template>

          <div v-if="feed.length" class="feed-list">
            <div v-for="(n, i) in feed" :key="i" class="feed-row">
              <span class="feed-dot" :class="`lvl-${(n.level || 'info').toLowerCase()}`"></span>
              <div class="feed-body">
                <div class="feed-title">{{ n.title }} <em>{{ n.refNo }}</em></div>
                <div class="feed-msg">{{ n.message }}</div>
              </div>
              <span class="feed-time">{{ fmtTime(n.createDate) }}</span>
            </div>
          </div>

          <el-empty v-else :description="t('dashboard.waitingFeed')" :image-size="60" />
        </el-card>
      </el-col>
    </el-row>

    <!-- Phase 8 — 受注済未出荷 Dashboard (Gap 3.4) -->
    <el-row :gutter="16" style="margin-top: 16px">
      <el-col :xs="24" :sm="24" :md="24" :lg="24" class="dash-col reveal-item" style="--delay: 880ms">
        <el-card shadow="hover" class="dash-panel">
          <template #header>
            <div style="display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap">
              <span>
                {{ t('dashboard.unshipped.title') }}
                <el-tag v-if="unshippedTotal > 0" size="small" type="warning" style="margin-left: 8px">
                  {{ unshippedTotal }}
                </el-tag>
              </span>
              <div style="display: flex; gap: 12px; align-items: center">
                <el-switch
                  v-model="onlyOverdue"
                  :active-text="t('dashboard.unshipped.onlyOverdue')"
                  @change="loadUnshipped"
                />
                <el-button :icon="Refresh" size="small" @click="loadUnshipped" :loading="unshippedLoading" />
              </div>
            </div>
          </template>

          <el-table
            v-if="unshippedRows.length && !isMobile"
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
        </el-card>
      </el-col>
    </el-row>

    <el-row :gutter="16" style="margin-top: 16px">
      <el-col :xs="24" :sm="24" :md="24" :lg="24" class="dash-col reveal-item" style="--delay: 960ms">
        <el-card shadow="hover" class="dash-panel">
          <template #header>
            <div style="display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap">
              <span>
                {{ t('dashboard.stockDwell.title') }}
                <el-tag
                  v-if="(stockDwellSummary?.over90Qty || 0) > 0"
                  size="small"
                  type="danger"
                  style="margin-left: 8px"
                >
                  {{ fmtQty(stockDwellSummary?.over90Qty) }}
                </el-tag>
              </span>
              <div style="display: flex; gap: 12px; align-items: center">
                <el-button :icon="Refresh" size="small" @click="loadStockDwell" :loading="stockDwellLoading" />
                <el-button link type="primary" @click="go('/wms/stock-dwell')">
                  {{ t('dashboard.stockDwell.open') }}
                </el-button>
              </div>
            </div>
          </template>

          <div v-if="stockDwellRows.length" class="stock-dwell-widget">
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
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { Refresh } from '@element-plus/icons-vue'
import { dashboardApi } from '@/api/sys/dashboard'
import { orderApi } from '@/api/erp/order'
import { stockDwellApi } from '@/api/wms/stockDwell'
import { getConnection, startConnection } from '@/utils/signalr'
import { useBreakpoint } from '@/composables/useBreakpoint'
import type { UnshippedOrderItemDto, UnshippedOrderQuery } from '@/types/erp/order'
import type { StockDwellRow, StockDwellSummary } from '@/types/wms/stockDwell'

const { t } = useI18n()
const router = useRouter()
const { isMobile } = useBreakpoint()

interface Summary {
  todayOrders: number
  monthOrders: number
  activeWorkOrders: number
  monthCompleted: number
  pendingOutbound: number
  stockWarnings: number
  pendingApprovals: number
  totalProducts: number
}
interface RecentOrder {
  webOrderNo: string
  customerCd?: string
  quantity?: number
  orderDate?: string
  shipStatus: number
}
interface StatusCount { status: number; count: number }
interface Notice {
  eventType?: string
  level?: string
  title?: string
  message?: string
  source?: string
  refNo?: string
  createDate?: string
}

const summary = ref<Summary>({
  todayOrders: 0, monthOrders: 0, activeWorkOrders: 0, monthCompleted: 0,
  pendingOutbound: 0, stockWarnings: 0, pendingApprovals: 0, totalProducts: 0
})
const recentOrders = ref<RecentOrder[]>([])
const workOrderStatus = ref<StatusCount[]>([])
const latestNotice = ref<Notice | null>(null)
const feed = ref<Notice[]>([])
const dashboardReady = ref(false)

interface StatCard {
  key: string
  value: number
  icon: string
  color: string
  to?: string
  alert?: boolean
}

const statCards = computed<StatCard[]>(() => [
  { key: 'todayOrders',      value: summary.value.todayOrders,      icon: 'ShoppingCart', color: '#409eff', to: '/order-list' },
  { key: 'monthOrders',      value: summary.value.monthOrders,      icon: 'ShoppingBag',  color: '#67c23a', to: '/order-list' },
  { key: 'activeWorkOrders', value: summary.value.activeWorkOrders, icon: 'SetUp',        color: '#e6a23c', to: '/mes/work-order-list' },
  { key: 'monthCompleted',   value: summary.value.monthCompleted,   icon: 'CircleCheck',  color: '#67c23a', to: '/mes/work-order-list' },
  { key: 'pendingOutbound',  value: summary.value.pendingOutbound,  icon: 'Van',          color: '#409eff', alert: true, to: '/wms/outbound-order-list' },
  { key: 'stockWarnings',    value: summary.value.stockWarnings,    icon: 'Warning',      color: '#e6a23c', alert: true, to: '/wms/stock' },
  { key: 'pendingApprovals', value: summary.value.pendingApprovals, icon: 'Stamp',        color: '#e6a23c', alert: true, to: '/product-list' },
  { key: 'totalProducts',    value: summary.value.totalProducts,    icon: 'Goods',        color: '#909399', to: '/product-list' }
])

// 仅当目标路由已注册时，卡片才可点击跳转
function canGo(to?: string) {
  return !!to && router.resolve(to).matched.length > 0
}
function onCardClick(to?: string) {
  if (canGo(to)) go(to as string)
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

// ── 出荷ステータス ──
function shipLabel(s: number) { return t(`dashboard.ship${s}`) }
function shipColor(s: number) {
  return ({ 0: 'info', 5: 'warning', 9: 'success' } as Record<number, any>)[s] || 'info'
}

// ── 製造指図ステータス ──
const maxWo = computed(() => Math.max(...workOrderStatus.value.map(s => s.count), 1))
function woPercent(c: number) { return Math.round((c / maxWo.value) * 100) }
function woLabel(s: number) { return t(`dashboard.wo${s}`) }
function woColor(s: number) {
  return ({ 0: 'info', 1: 'primary', 2: 'primary', 3: 'warning', 4: 'success', 5: 'danger', 6: 'success', 9: 'info' } as Record<number, any>)[s] || 'info'
}

function alertType(level?: string) {
  const l = (level || '').toLowerCase()
  if (l === 'error') return 'error'
  if (l === 'warning') return 'warning'
  return 'success'
}

function fmtQty(q?: number) {
  if (q === null || q === undefined) return '-'
  return Number(q).toLocaleString()
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
const unshippedRows = ref<UnshippedOrderItemDto[]>([])
const unshippedTotal = ref(0)
const unshippedLoading = ref(false)
const onlyOverdue = ref(false)
const unshippedQuery = reactive<UnshippedOrderQuery>({
  page: 1,
  pageSize: 10,
  sortField: 'deliveryDate',
  sortOrder: 'asc',
})

async function loadUnshipped() {
  unshippedLoading.value = true
  try {
    const res = await orderApi.searchUnshipped({
      ...unshippedQuery,
      onlyOverdue: onlyOverdue.value || undefined,
    })
    if (res.code === 0 && res.data) {
      // codex 后端用 PagedResultDto<T>（Items / Total / PageIndex / PageSize），
      // 与 PagedResult<T>（rows / total）字段名不同。兼容两种形状。
      const d: any = res.data
      unshippedRows.value = d.items ?? d.rows ?? []
      unshippedTotal.value = d.total ?? 0
    }
  } catch {
    /* 静默：axios 拦截器已经弹了 toast；这里不要再弹避免双重提示 */
  } finally {
    unshippedLoading.value = false
  }
}

function goOrderDetail(row: UnshippedOrderItemDto) {
  router.push(`/order?webOrderNo=${encodeURIComponent(row.webOrderNo)}`)
}

// ───── T13 stock dwell widget ─────
const stockDwellSummary = ref<StockDwellSummary | null>(null)
const stockDwellLoading = ref(false)
const stockDwellRows = computed<StockDwellRow[]>(() =>
  (stockDwellSummary.value?.rows ?? [])
    .filter(row => Number(row.bucketOver90Qty || 0) > 0)
    .slice(0, 6),
)
const maxStockDwellOver90 = computed(() =>
  Math.max(...stockDwellRows.value.map(row => Number(row.bucketOver90Qty || 0)), 1),
)

function stockDwellPercent(qty: number) {
  return Math.round((Number(qty || 0) / maxStockDwellOver90.value) * 100)
}

async function loadStockDwell() {
  stockDwellLoading.value = true
  try {
    const res = await stockDwellApi.summary({
      groupBy: 'product',
      asOfDate: todayString(),
    })
    if (res.code === 0 && res.data) {
      stockDwellSummary.value = res.data
    }
  } catch {
    /* axios interceptor already shows the error toast. */
  } finally {
    stockDwellLoading.value = false
  }
}

function orderLifecycleColor(s: string): 'success' | 'warning' | 'danger' | 'info' | '' {
  switch (s) {
    case 'CONFIRMED': return 'info'
    case 'IN_PRODUCTION': return 'warning'
    case 'PARTIALLY_CANCELLED': return 'danger'
    default: return ''
  }
}

function revealDashboard() {
  if (dashboardReady.value) return
  requestAnimationFrame(() => { dashboardReady.value = true })
}

async function loadData() {
  try {
    const res = await dashboardApi.getSummary() as any
    summary.value = res.summary
    recentOrders.value = res.recentOrders || []
    workOrderStatus.value = res.workOrderStatus || []
    // Phase 8 widget は loadData() に組み込まない:
    // dashboard 主体は NewOperLog SignalR push で頻繁にリフレッシュされる。
    // unshipped widget は別系統（onMounted で一度 + 手動 refresh ボタンのみ）に切り離して、
    // 1 リクエスト → OperLog → SignalR → loadData の正回归ループを回避する。
  } finally {
    revealDashboard()
  }
}

onMounted(async () => {
  await loadData()
  // Phase 8 widget の初回ロードは独立にトリガー（loadData の SignalR ループに巻き込まれない）
  loadUnshipped()
  loadStockDwell()

  try {
    await startConnection()
    const conn = getConnection()

    // 业务通知（RabbitMQ → SignalR fanout）
    conn.on('BusinessNotification', (n: Notice) => {
      latestNotice.value = n
      feed.value = [n, ...feed.value].slice(0, 12)
      window.setTimeout(() => {
        if (latestNotice.value === n) latestNotice.value = null
      }, 6000)
      loadData()
    })

    // 操作日志推送 → 触发 KPI 刷新（受注/製造等写操作会改变统计）
    conn.on('NewOperLog', () => { loadData() })
  } catch {
    // SignalR 失败不阻塞仪表盘外壳
  }
})

onUnmounted(() => {
  const conn = getConnection()
  conn.off('BusinessNotification')
  conn.off('NewOperLog')
})
</script>

<style scoped>
.dashboard {
  position: relative;
}

.stat-cards {
  margin-bottom: 0;
}

.reveal-item {
  opacity: 0;
  transform: translateY(22px) scale(0.985);
  filter: blur(10px);
}

.dashboard.is-ready .reveal-item {
  animation: dashboard-rise 0.7s cubic-bezier(0.22, 1, 0.36, 1) forwards;
  animation-delay: var(--delay, 0ms);
}

.stat-card,
.dash-panel,
.quick-card {
  border: 1px solid rgba(255, 255, 255, 0.72);
  background: linear-gradient(180deg, rgba(255, 255, 255, 0.96), rgba(248, 251, 255, 0.92));
  box-shadow:
    0 18px 40px rgba(148, 163, 184, 0.12),
    inset 0 1px 0 rgba(255, 255, 255, 0.8);
  backdrop-filter: blur(14px);
  transition: transform 0.24s ease, box-shadow 0.24s ease;
}

.stat-card:hover,
.dash-panel:hover {
  transform: translateY(-4px);
  box-shadow:
    0 24px 52px rgba(148, 163, 184, 0.16),
    inset 0 1px 0 rgba(255, 255, 255, 0.86);
}

.stat-cards :deep(.el-col) {
  margin-bottom: 16px;
}

.stat-card-inner {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.stat-label {
  color: #909399;
  font-size: 14px;
  margin-bottom: 8px;
}

.stat-value {
  font-size: 28px;
  font-weight: bold;
  color: #303133;
}

.stat-value.is-alert {
  color: #f56c6c;
}

.stat-card.clickable {
  cursor: pointer;
}

.goto-hint {
  font-size: 12px;
  color: #c0c4cc;
  vertical-align: middle;
  opacity: 0;
  transform: translateX(-3px);
  transition: opacity 0.2s ease, transform 0.2s ease;
}

.stat-card.clickable:hover .goto-hint {
  opacity: 1;
  transform: translateX(0);
}

/* 快捷入口 */
.quick-grid {
  display: grid;
  grid-template-columns: repeat(6, 1fr);
  gap: 12px;
}

.quick-item {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  padding: 16px 8px;
  border-radius: 10px;
  cursor: pointer;
  font-size: 13px;
  color: #303133;
  background: rgba(64, 158, 255, 0.04);
  border: 1px solid rgba(64, 158, 255, 0.08);
  transition: transform 0.18s ease, background 0.18s ease;
}

.quick-item:hover {
  transform: translateY(-3px);
  background: rgba(64, 158, 255, 0.1);
}

.method-row {
  display: flex;
  align-items: center;
  margin-bottom: 12px;
}

/* 实时通知 feed */
.feed-list {
  display: flex;
  flex-direction: column;
  max-height: 320px;
  overflow-y: auto;
}

.feed-row {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 10px 0;
  border-bottom: 1px solid #f0f2f5;
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
  background: #409eff;
}

.feed-dot.lvl-warning { background: #e6a23c; }
.feed-dot.lvl-error { background: #f56c6c; }

.feed-body {
  flex: 1;
  min-width: 0;
}

.feed-title {
  font-size: 13px;
  font-weight: 600;
  color: #303133;
}

.feed-title em {
  font-style: normal;
  color: #909399;
  font-weight: 400;
}

.feed-msg {
  font-size: 12px;
  color: #909399;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.feed-time {
  font-size: 11px;
  color: #c0c4cc;
  flex-shrink: 0;
}

.simple-list {
  display: flex;
  flex-direction: column;
}

.simple-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 0;
  border-bottom: 1px solid #f0f2f5;
  font-size: 14px;
}

.simple-row:last-child {
  border-bottom: none;
}

.simple-row-name {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: #303133;
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
  color: #303133;
  font-size: 13px;
}

.stock-dwell-widget-qty {
  color: #d93026;
  font-weight: 650;
  font-size: 13px;
  text-align: right;
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
  .dashboard.is-ready .reveal-item,
  .stat-card,
  .dash-panel,
  .quick-card {
    animation: none !important;
    transition: none !important;
    transform: none !important;
    filter: none !important;
    opacity: 1 !important;
  }
}

@media (max-width: 991px) {
  .quick-grid {
    grid-template-columns: repeat(3, 1fr);
  }

  .stock-dwell-widget-row {
    grid-template-columns: 1fr 92px;
  }

  .stock-dwell-widget-row :deep(.el-progress) {
    grid-column: 1 / -1;
  }
}

@media (max-width: 767px) {
  .dash-col {
    margin-bottom: 12px;
  }
  .stat-value {
    font-size: 22px;
  }
  .stat-label {
    font-size: 12px;
  }
  .quick-grid {
    grid-template-columns: repeat(3, 1fr);
  }
}
</style>
