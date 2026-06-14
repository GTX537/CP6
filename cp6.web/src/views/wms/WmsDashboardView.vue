<template>
  <div class="wms-dashboard">
    <el-row :gutter="12">
      <el-col :span="6">
        <el-card shadow="hover" class="kpi-card">
          <div class="kpi-label">{{ t('wms.dashboard.kpi.totalValue') }}</div>
          <div class="kpi-value">¥{{ formatMoney(kpi.totalStockValue) }}</div>
          <div class="kpi-sub">{{ kpi.activeSkuCount }} SKU / {{ formatQty(kpi.totalPhysicalQty) }}</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="hover" class="kpi-card kpi-warn">
          <div class="kpi-label">{{ t('wms.dashboard.kpi.dormant') }}</div>
          <div class="kpi-value">{{ kpi.stagnantSkuCount }}</div>
          <div class="kpi-sub">SKU</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="hover" class="kpi-card">
          <div class="kpi-label">{{ t('wms.dashboard.kpi.todayInbound') }}</div>
          <div class="kpi-value">{{ kpi.todayInboundPlanCount }}</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="hover" class="kpi-card">
          <div class="kpi-label">{{ t('wms.dashboard.kpi.todayShipping') }}</div>
          <div class="kpi-value">{{ kpi.todayShippingPlanCount }}</div>
        </el-card>
      </el-col>
    </el-row>

    <el-row :gutter="12" style="margin-top: 12px">
      <el-col :span="6">
        <el-card shadow="hover" class="kpi-card">
          <div class="kpi-label">{{ t('wms.stock.col.allocated') }}</div>
          <div class="kpi-value">{{ formatQty(kpi.totalAllocatedQty) }}</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="hover" class="kpi-card" :class="{ 'kpi-danger': kpi.openStockTakeCount > 0 }">
          <div class="kpi-label">{{ t('wms.stocktake.title') }}</div>
          <div class="kpi-value">{{ kpi.openStockTakeCount }}</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="hover" class="kpi-card" :class="{ 'kpi-danger': alerts.expiryAlerts.length > 0 }">
          <div class="kpi-label">{{ t('wms.dashboard.alert.expiry') }}</div>
          <div class="kpi-value">{{ alerts.expiryAlerts.length }}</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="hover" class="kpi-card" :class="{ 'kpi-warn': alerts.delayedInbounds.length > 0 }">
          <div class="kpi-label">{{ t('wms.dashboard.alert.delayed') }}</div>
          <div class="kpi-value">{{ alerts.delayedInbounds.length }}</div>
        </el-card>
      </el-col>
    </el-row>

    <!-- リアルタイム タイムライン -->
    <el-row :gutter="12" style="margin-top: 12px">
      <el-col :span="24">
        <el-card shadow="never">
          <template #header>
            <div style="display: flex; justify-content: space-between; align-items: center">
              <span style="font-weight: 600">
                {{ t('wms.dashboard.realtime.title') }}
                <el-tag :type="rtTagType" size="small" style="margin-left: 12px">{{ rtStatus }}</el-tag>
              </span>
              <el-button v-if="events.length > 0" link size="small" @click="events = []">{{ t('wms.dashboard.realtime.clear') }}</el-button>
            </div>
          </template>
          <div v-if="events.length === 0" class="rt-empty">{{ t('wms.dashboard.realtime.noEvents') }}</div>
          <el-timeline v-else>
            <el-timeline-item v-for="(ev, idx) in events.slice(0, 10)" :key="idx"
                :type="txnTimelineTypeOf(ev.txnType)" :timestamp="formatTime(ev.txnAt)" size="normal" hollow>
              <el-tag :type="txnTagOf(ev.txnType)" size="small" style="margin-right: 8px">{{ ev.txnType }}</el-tag>
              <strong>{{ ev.productCd }}</strong>
              <span v-if="ev.lotNo"> / Lot:{{ ev.lotNo }}</span>
              <span> @ {{ ev.warehouseCd }} - {{ ev.locationCd }}</span>
              <span :class="ev.qty < 0 ? 'qty-out' : 'qty-in'" style="margin-left: 8px; font-weight: 600">
                {{ ev.qty > 0 ? '+' : '' }}{{ formatQty(ev.qty) }}
              </span>
              <span v-if="ev.relatedNo" style="color: #909399; margin-left: 8px">[{{ ev.relatedNo }}]</span>
            </el-timeline-item>
          </el-timeline>
        </el-card>
      </el-col>
    </el-row>

    <el-row :gutter="12" style="margin-top: 12px">
      <el-col :span="16">
        <el-card shadow="never">
          <template #header>
            <div style="display: flex; justify-content: space-between; align-items: center">
              <span style="font-weight: 600">{{ t('wms.dashboard.chart.trend') }}（{{ trendDays }}）</span>
              <el-radio-group v-model="trendDays" size="small" @change="loadTrend">
                <el-radio-button :value="7">7</el-radio-button>
                <el-radio-button :value="30">30</el-radio-button>
                <el-radio-button :value="90">90</el-radio-button>
              </el-radio-group>
            </div>
          </template>
          <div v-if="trend.length === 0" class="empty">—</div>
          <div v-else class="trend-chart">
            <div v-for="(p, i) in trend" :key="i" class="trend-col" :title="`${p.date}\nIN: ${p.inQty}\nOUT: ${p.outQty}\nADJ: ${p.adjQty}`">
              <div class="bar bar-in" :style="{ height: heightOf(p.inQty) + '%' }"></div>
              <div class="bar bar-out" :style="{ height: heightOf(p.outQty) + '%' }"></div>
              <div class="bar bar-adj" :style="{ height: heightOf(p.adjQty) + '%' }"></div>
              <div class="trend-date" v-if="i % Math.ceil(trend.length / 10) === 0">{{ p.date.slice(5) }}</div>
            </div>
          </div>
          <div class="legend">
            <span><i class="dot bar-in"></i> IN</span>
            <span><i class="dot bar-out"></i> OUT</span>
            <span><i class="dot bar-adj"></i> ADJ</span>
          </div>
        </el-card>
      </el-col>

      <el-col :span="8">
        <el-card shadow="never">
          <template #header><span style="font-weight: 600">{{ t('wms.dashboard.chart.warehouseValue') }}</span></template>
          <el-table :data="warehouseValue" size="small" border>
            <el-table-column prop="warehouseCd" :label="t('wms.warehouse.fld.cd')" width="100" />
            <el-table-column prop="warehouseName" :label="t('wms.warehouse.fld.name')" />
            <el-table-column prop="skuCount" label="SKU" width="70" align="right" />
            <el-table-column :label="t('wms.dashboard.kpi.totalValue')" align="right">
              <template #default="{ row }">¥{{ formatMoney(row.stockValue) }}</template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-col>
    </el-row>

    <el-row :gutter="12" style="margin-top: 12px">
      <el-col :span="12">
        <el-card shadow="never">
          <template #header><span style="font-weight: 600; color: #e6a23c">{{ t('wms.dashboard.alert.expiry') }}</span></template>
          <el-table :data="alerts.expiryAlerts" size="small" border max-height="320">
            <el-table-column prop="productCd" :label="t('wms.common.product')" width="120" />
            <el-table-column prop="lotNo" :label="t('wms.common.lot')" width="120" />
            <el-table-column prop="locationCd" :label="t('wms.common.location')" width="140" />
            <el-table-column :label="t('wms.common.expiryDate')" width="120">
              <template #default="{ row }">{{ row.expiryDate?.slice(0, 10) }}</template>
            </el-table-column>
            <el-table-column label="DDay" width="80" align="right">
              <template #default="{ row }">
                <span :class="{ 'minus': row.daysUntilExpiry < 0, 'warn': row.daysUntilExpiry < 7 }">
                  {{ row.daysUntilExpiry }}
                </span>
              </template>
            </el-table-column>
            <el-table-column prop="physicalQty" :label="t('wms.common.qty')" align="right">
              <template #default="{ row }">{{ formatQty(row.physicalQty) }}</template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-col>

      <el-col :span="12">
        <el-card shadow="never">
          <template #header><span style="font-weight: 600; color: #f56c6c">{{ t('wms.dashboard.alert.delayed') }}</span></template>
          <el-table :data="alerts.delayedInbounds" size="small" border max-height="320">
            <el-table-column prop="inboundNo" :label="t('wms.inbound.fld.no')" width="180" />
            <el-table-column prop="supplierName" :label="t('wms.inbound.fld.supplierName')" />
            <el-table-column :label="t('wms.inbound.fld.expectedArrival')" width="120">
              <template #default="{ row }">{{ row.expectedArrivalDate?.slice(0, 10) }}</template>
            </el-table-column>
            <el-table-column label="DDay" width="100" align="right">
              <template #default="{ row }"><span class="minus">{{ row.delayDays }}</span></template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted, onBeforeUnmount, computed } from 'vue'
import { ElMessage } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { wmsDashboardApi } from '@/api/wms/wmsDashboard'
import type { WmsKpi, WmsTrendPoint, WmsWarehouseValue, WmsAlerts } from '@/types/wms/wms'
import { getWmsConnection, startWmsConnection, type StockChangedPayload, type InboundReceivedPayload, type OutboundShippedPayload } from '@/utils/wmsHub'
import * as signalR from '@microsoft/signalr'
import { formatQty } from '@/utils/format'

const { t } = useI18n()

const kpi = reactive<WmsKpi>({
  totalStockValue: 0, activeSkuCount: 0, totalPhysicalQty: 0,
  totalAllocatedQty: 0, stagnantSkuCount: 0,
  todayInboundPlanCount: 0, todayShippingPlanCount: 0, openStockTakeCount: 0,
})

const trendDays = ref(30)
const trend = ref<WmsTrendPoint[]>([])
const warehouseValue = ref<WmsWarehouseValue[]>([])
const alerts = reactive<WmsAlerts>({ expiryAlerts: [], delayedInbounds: [], pendingApprovalStockTakeCount: 0 })

const trendMax = computed(() => {
  if (trend.value.length === 0) return 1
  return Math.max(1, ...trend.value.flatMap(p => [p.inQty, p.outQty, Math.abs(p.adjQty)]))
})

function heightOf(qty: number): number { return Math.max(2, (Math.abs(qty) / trendMax.value) * 100) }
function formatMoney(n: number): string { return formatQty(Math.round(Number(n) || 0), 0) }

async function loadKpi() { const r = await wmsDashboardApi.kpi(); Object.assign(kpi, r.data) }
async function loadTrend() { const r = await wmsDashboardApi.trend(trendDays.value); trend.value = r.data }
async function loadWarehouseValue() { const r = await wmsDashboardApi.warehouseValue(); warehouseValue.value = r.data }
async function loadAlerts() { const r = await wmsDashboardApi.alerts(); Object.assign(alerts, r.data) }

// ─── SignalR リアルタイム ─────────────────────────
const events = ref<StockChangedPayload[]>([])
const rtState = ref<signalR.HubConnectionState>(signalR.HubConnectionState.Disconnected)

const rtStatus = computed(() => {
  switch (rtState.value) {
    case signalR.HubConnectionState.Connected: return t('wms.dashboard.realtime.connected')
    case signalR.HubConnectionState.Connecting:
    case signalR.HubConnectionState.Reconnecting: return t('wms.dashboard.realtime.connecting')
    default: return t('wms.dashboard.realtime.disconnected')
  }
})
const rtTagType = computed<'success' | 'warning' | 'info'>(() => {
  switch (rtState.value) {
    case signalR.HubConnectionState.Connected: return 'success'
    case signalR.HubConnectionState.Connecting:
    case signalR.HubConnectionState.Reconnecting: return 'warning'
    default: return 'info'
  }
})

function txnTagOf(t: string): 'success' | 'danger' | 'warning' | 'info' | 'primary' {
  return ({ IN: 'success', OUT: 'danger', RSV: 'warning', UNRSV: 'info', MOVE: 'primary', ADJ: 'info' } as const)[t as 'IN'] || 'info'
}
function txnTimelineTypeOf(t: string): 'success' | 'danger' | 'warning' | 'info' | 'primary' {
  return txnTagOf(t)
}
function formatTime(s: string): string {
  return s?.replace('T', ' ').slice(11, 19) || ''
}

// 高頻度イベント対策：300ms ごとに KPI/Alerts を最大1回再ロード
let kpiReloadTimer: number | null = null
function scheduleKpiReload() {
  if (kpiReloadTimer) return
  kpiReloadTimer = window.setTimeout(async () => {
    kpiReloadTimer = null
    await Promise.all([loadKpi(), loadAlerts()])
  }, 300)
}

let conn: signalR.HubConnection | null = null
const handlers: Array<[string, (...args: any[]) => void]> = []

onMounted(async () => {
  await Promise.all([loadKpi(), loadTrend(), loadWarehouseValue(), loadAlerts()])

  conn = getWmsConnection()
  conn.onreconnecting(() => { rtState.value = signalR.HubConnectionState.Reconnecting })
  conn.onreconnected(() => { rtState.value = signalR.HubConnectionState.Connected })
  conn.onclose(() => { rtState.value = signalR.HubConnectionState.Disconnected })

  const onStock = (evt: StockChangedPayload) => {
    events.value.unshift(evt)
    if (events.value.length > 50) events.value = events.value.slice(0, 50)
    scheduleKpiReload()
  }
  const onInbound = (evt: InboundReceivedPayload) => {
    ElMessage.success(t('wms.dashboard.realtime.toastInbound', { no: evt.receiptNo }))
    scheduleKpiReload()
  }
  const onOutbound = (evt: OutboundShippedPayload) => {
    ElMessage.success(t('wms.dashboard.realtime.toastOutbound', { no: evt.outboundNo }))
    scheduleKpiReload()
  }
  conn.on('StockChanged', onStock); handlers.push(['StockChanged', onStock])
  conn.on('InboundReceived', onInbound); handlers.push(['InboundReceived', onInbound])
  conn.on('OutboundShipped', onOutbound); handlers.push(['OutboundShipped', onOutbound])

  await startWmsConnection()
  rtState.value = conn.state
})

onBeforeUnmount(() => {
  if (conn) {
    handlers.forEach(([ev, fn]) => conn!.off(ev, fn))
  }
  if (kpiReloadTimer) {
    window.clearTimeout(kpiReloadTimer)
    kpiReloadTimer = null
  }
})
</script>

<style scoped>
.wms-dashboard { padding: 16px; }
.kpi-card { text-align: center; }
.kpi-label { font-size: 13px; color: #909399; }
.kpi-value { font-size: 28px; font-weight: 700; margin: 8px 0; color: #303133; }
.kpi-sub { font-size: 12px; color: #909399; }
.kpi-warn .kpi-value { color: #e6a23c; }
.kpi-danger .kpi-value { color: #f56c6c; }

.empty { text-align: center; padding: 40px 0; color: #909399; }
.trend-chart { display: flex; align-items: flex-end; gap: 2px; height: 200px; padding: 8px 0; border-bottom: 1px solid #eee; }
.trend-col { flex: 1; display: flex; flex-direction: column; justify-content: flex-end; align-items: center; position: relative; min-width: 8px; }
.bar { width: 100%; min-height: 1px; transition: all 0.2s; }
.bar-in { background: #67c23a; }
.bar-out { background: #f56c6c; }
.bar-adj { background: #909399; }
.trend-date { position: absolute; bottom: -20px; font-size: 10px; color: #999; white-space: nowrap; transform: rotate(-45deg); transform-origin: top right; }
.legend { display: flex; gap: 16px; padding: 12px 0 0; font-size: 12px; color: #909399; justify-content: center; }
.legend i.dot { display: inline-block; width: 10px; height: 10px; margin-right: 4px; vertical-align: middle; }

.minus { color: var(--el-color-danger); font-weight: 600; }
.warn  { color: var(--el-color-warning); font-weight: 600; }
.rt-empty { padding: 20px 0; text-align: center; color: #909399; font-size: 13px; }
.qty-in { color: var(--el-color-success); }
.qty-out { color: var(--el-color-danger); }
</style>
