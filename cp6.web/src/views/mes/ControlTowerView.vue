<template>
  <div class="control-tower" :class="{ fullscreen: isFullscreen }">
    <!-- ヘッダー -->
    <div class="tower-header">
      <div class="title">
        <span class="logo">⚙</span>
        CP6 MES Control Tower
      </div>
      <div class="header-info">
        <span class="clock">{{ clock }}</span>
        <span class="sep">|</span>
        <span :class="['hub-status', hubConnected ? 'ok' : 'ng']">
          {{ hubConnected ? '● LIVE' : '○ Offline' }}
        </span>
        <el-button size="small" :icon="isFullscreen ? 'Close' : 'FullScreen'" circle @click="toggleFullscreen" style="margin-left: 16px;" />
        <el-button size="small" :icon="Back" circle @click="$router.back()" style="margin-left: 4px;" />
      </div>
    </div>

    <!-- 4 象限 -->
    <div class="grid">
      <!-- 左上：KPI -->
      <div class="quad q-kpi">
        <div class="quad-title">本日 KPI</div>
        <div class="kpi-grid">
          <div class="kpi-cell blue">
            <div class="cell-label">着手中 指図</div>
            <div class="cell-value">{{ summary.inProgressCount }}</div>
            <div class="cell-sub">完了 {{ summary.completedCount }}</div>
          </div>
          <div class="kpi-cell green">
            <div class="cell-label">良品数</div>
            <div class="cell-value">{{ summary.totalGoodQty }}</div>
          </div>
          <div class="kpi-cell red">
            <div class="cell-label">不良率</div>
            <div class="cell-value">{{ summary.defectRate }}%</div>
            <div class="cell-sub">不良 {{ summary.totalDefectQty }}</div>
          </div>
          <div class="kpi-cell orange">
            <div class="cell-label">遅延件数</div>
            <div class="cell-value">{{ summary.delayedCount }}</div>
          </div>
        </div>
      </div>

      <!-- 右上：設備状態グリッド -->
      <div class="quad q-machines">
        <div class="quad-title">設備状態 ({{ machines.length }} 台)</div>
        <div class="machine-grid">
          <div v-for="m in machines" :key="m.machineCd"
            class="m-cell"
            :class="'light-' + getLight(m.status)"
            :title="`${m.machineName} | ${getStatusLabel(m.status)} | OEE ${m.todayOee ?? '-'}%`">
            <div class="m-cd">{{ m.machineCd }}</div>
            <div class="m-stat">{{ getStatusLabel(m.status) }}</div>
            <div v-if="m.todayOee != null" class="m-oee">{{ m.todayOee }}%</div>
          </div>
        </div>
      </div>

      <!-- 左下：日次生産推移 -->
      <div class="quad q-trend">
        <div class="quad-title">日別生産推移（14日）</div>
        <svg v-if="trend.length > 0" :viewBox="'0 0 ' + trendW + ' ' + trendH" preserveAspectRatio="none" class="trend-svg">
          <g v-for="i in 4" :key="'gy'+i">
            <line x1="0" :x2="trendW"
              :y1="(trendH * (i - 1) / 4)" :y2="(trendH * (i - 1) / 4)"
              stroke="rgba(255,255,255,0.08)" stroke-width="1" />
          </g>
          <polyline :points="goodPoints" fill="none" stroke="#67C23A" stroke-width="2" />
          <polyline :points="defectPoints" fill="none" stroke="#F56C6C" stroke-width="2" />
          <g v-for="(p, i) in trend" :key="'p'+i">
            <circle :cx="trendX(i)" :cy="trendY(p.goodQty)" r="3" fill="#67C23A" />
            <circle :cx="trendX(i)" :cy="trendY(p.defectQty)" r="3" fill="#F56C6C" />
          </g>
        </svg>
        <div class="trend-legend">
          <span><span class="dot" style="background:#67C23A;"></span>良品</span>
          <span><span class="dot" style="background:#F56C6C;"></span>不良</span>
        </div>
      </div>

      <!-- 右下：実時間イベント + 遅延アラート -->
      <div class="quad q-events">
        <div class="quad-title">
          リアルタイムイベント
          <span class="event-count">最新 {{ events.length }} 件</span>
        </div>
        <div class="event-list">
          <transition-group name="event">
            <div v-for="ev in events" :key="ev.id" class="event-row" :class="'ev-' + ev.type">
              <span class="ev-time">{{ ev.time }}</span>
              <span class="ev-tag">{{ ev.tag }}</span>
              <span class="ev-msg">{{ ev.msg }}</span>
            </div>
          </transition-group>
          <div v-if="events.length === 0" class="empty">イベント待機中...</div>
        </div>

        <div class="quad-title" style="margin-top: 12px;">
          納期遅延 TOP 5
        </div>
        <div class="delay-list">
          <div v-for="d in delayAlerts.slice(0, 5)" :key="d.workOrderNo" class="delay-row">
            <span class="d-no">{{ d.workOrderNo }}</span>
            <span class="d-name">{{ d.productName || '-' }}</span>
            <span class="d-days">遅延 {{ d.delayDays }}日</span>
          </div>
          <div v-if="delayAlerts.length === 0" class="empty">遅延なし</div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted, onBeforeUnmount } from 'vue'
import { Back } from '@element-plus/icons-vue'
import { mesDashboardApi, machineApi } from '@/api/mes/mes'
import {
  MACHINE_STATUS_OPTIONS,
  type MesDashboardSummaryDto,
  type DailyTrendDto,
  type DelayAlertDto,
  type MachineDto,
} from '@/types/mes/mes'
import { getMesHub, startMesHub, stopMesHub } from '@/utils/mesHub'
import { formatDateTime } from '@/utils/format'

const summary = reactive<MesDashboardSummaryDto>({
  inProgressCount: 0, completedCount: 0, totalGoodQty: 0, totalDefectQty: 0,
  defectRate: 0, delayedCount: 0,
})
const trend = ref<DailyTrendDto[]>([])
const delayAlerts = ref<DelayAlertDto[]>([])
const machines = ref<MachineDto[]>([])

const clock = ref(formatNow())
const hubConnected = ref(false)
const isFullscreen = ref(false)

// ─── 折線図 ───
const trendW = 800
const trendH = 200
const maxY = computed(() => {
  const m = Math.max(...trend.value.map(p => Math.max(p.goodQty, p.defectQty)), 10)
  return Math.ceil(m / 100) * 100 || m
})
function trendX(i: number) {
  if (trend.value.length <= 1) return 0
  return (trendW * i / (trend.value.length - 1))
}
function trendY(v: number) {
  return trendH - (v / maxY.value) * trendH
}
const goodPoints = computed(() => trend.value.map((p, i) => `${trendX(i)},${trendY(p.goodQty)}`).join(' '))
const defectPoints = computed(() => trend.value.map((p, i) => `${trendX(i)},${trendY(p.defectQty)}`).join(' '))

// ─── 設備グリッド ───
function getStatusLabel(v: number) { return MACHINE_STATUS_OPTIONS.find(o => o.value === v)?.label ?? `${v}` }
function getLight(v: number) { return MACHINE_STATUS_OPTIONS.find(o => o.value === v)?.light ?? 'gray' }

// ─── 実時間イベント ───
let evId = 0
interface TowerEvent { id: number; time: string; type: string; tag: string; msg: string }
const events = ref<TowerEvent[]>([])
function pushEvent(type: string, tag: string, msg: string) {
  events.value.unshift({
    id: ++evId,
    time: new Date().toLocaleTimeString('ja-JP'),
    type, tag, msg,
  })
  if (events.value.length > 12) events.value = events.value.slice(0, 12)
}

// ─── データ取得 ───
async function loadAll() {
  const [s, tr, da, ms] = await Promise.all([
    mesDashboardApi.summary(),
    mesDashboardApi.dailyTrend(14),
    mesDashboardApi.delayAlerts(20),
    machineApi.search({ activeOnly: true }),
  ])
  Object.assign(summary, s.data || summary)
  trend.value = tr.data || []
  delayAlerts.value = da.data || []
  machines.value = ms.data || []
}

async function refreshMachines() {
  const ms = await machineApi.search({ activeOnly: true })
  machines.value = ms.data || []
}

// ─── SignalR ───
async function setupHub() {
  const hub = getMesHub()
  hub.onreconnected(() => { hubConnected.value = true })
  hub.onreconnecting(() => { hubConnected.value = false })
  hub.onclose(() => { hubConnected.value = false })

  hub.on('ProductionReported', (p: { workOrderNo: string; processCd: string; goodQty: number; defectQty: number }) => {
    pushEvent('prod', '実績', `${p.workOrderNo} / ${p.processCd}: 良品 ${p.goodQty} 不良 ${p.defectQty}`)
    loadAll() // 軽量再取得
  })
  hub.on('DefectIssued', (p: { defectNo: string; workOrderNo: string; categoryCd: string }) => {
    pushEvent('defect', '不良', `${p.defectNo} (${p.categoryCd}) at ${p.workOrderNo}`)
  })
  hub.on('MachineStatusChanged', (p: { machineCd: string; newStatus: number }) => {
    pushEvent('machine', '設備', `${p.machineCd} → ${getStatusLabel(p.newStatus)}`)
    refreshMachines()
  })
  hub.on('WorkOrderStatusChanged', (p: { workOrderNo: string; newStatus: number }) => {
    pushEvent('wo', '指図', `${p.workOrderNo} ステータス ${p.newStatus}`)
  })
  hub.on('DowntimeRegistered', (p: { downtimeNo: string; machineCd: string; downtimeType: number }) => {
    pushEvent('downtime', '停止', `${p.machineCd} 停止登録 (${p.downtimeNo})`)
  })

  await startMesHub()
  hubConnected.value = hub.state === 'Connected'
}

// ─── Clock ───
function formatNow() {
  const d = new Date()
  return formatDateTime(d)
}
let clockTimer: number | undefined

// ─── Fullscreen ───
function toggleFullscreen() {
  if (!document.fullscreenElement) {
    document.documentElement.requestFullscreen?.()
    isFullscreen.value = true
  } else {
    document.exitFullscreen?.()
    isFullscreen.value = false
  }
}

let refreshTimer: number | undefined

onMounted(async () => {
  await loadAll()
  await setupHub()
  clockTimer = window.setInterval(() => { clock.value = formatNow() }, 1000)
  refreshTimer = window.setInterval(loadAll, 10000) // 10 秒ポーリングフォールバック
})

onBeforeUnmount(() => {
  if (clockTimer) clearInterval(clockTimer)
  if (refreshTimer) clearInterval(refreshTimer)
  stopMesHub()
})
</script>

<style scoped>
.control-tower {
  background: #0c1a2c;
  color: #e0e6ed;
  min-height: 100vh;
  padding: 16px;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
}
.control-tower.fullscreen { padding: 12px; }

/* ─── Header ─── */
.tower-header {
  display: flex; justify-content: space-between; align-items: center;
  margin-bottom: 12px;
  padding: 8px 16px;
  background: linear-gradient(90deg, rgba(64,158,255,0.15), transparent);
  border-radius: 6px;
  border-left: 4px solid #409EFF;
}
.title { font-size: 24px; font-weight: 700; display: flex; gap: 10px; align-items: center; }
.logo { color: #409EFF; font-size: 28px; }
.header-info { display: flex; align-items: center; gap: 10px; font-size: 14px; }
.clock { font-family: 'Consolas', monospace; color: #66B1FF; font-size: 16px; }
.sep { color: rgba(255,255,255,0.2); }
.hub-status.ok { color: #67C23A; }
.hub-status.ng { color: #F56C6C; }

/* ─── 4 象限 ─── */
.grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  grid-template-rows: 1fr 1fr;
  gap: 12px;
  height: calc(100vh - 100px);
}
.quad {
  background: linear-gradient(135deg, rgba(255,255,255,0.04), rgba(255,255,255,0.02));
  border: 1px solid rgba(64,158,255,0.2);
  border-radius: 8px;
  padding: 12px;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.quad-title {
  font-size: 14px;
  color: #409EFF;
  border-bottom: 1px solid rgba(64,158,255,0.15);
  padding-bottom: 6px;
  margin-bottom: 10px;
  display: flex; justify-content: space-between; align-items: center;
}

/* ─── KPI ─── */
.kpi-grid {
  display: grid; grid-template-columns: 1fr 1fr; gap: 12px;
  flex: 1;
}
.kpi-cell {
  border-radius: 6px; padding: 16px;
  display: flex; flex-direction: column; justify-content: center;
}
.cell-label { font-size: 13px; opacity: 0.85; }
.cell-value { font-size: 42px; font-weight: 800; line-height: 1; margin-top: 4px; }
.cell-sub { font-size: 12px; opacity: 0.75; margin-top: 4px; }
.kpi-cell.blue { background: linear-gradient(135deg, rgba(64,158,255,0.7), rgba(102,177,255,0.5)); }
.kpi-cell.green { background: linear-gradient(135deg, rgba(103,194,58,0.7), rgba(133,206,97,0.5)); }
.kpi-cell.red { background: linear-gradient(135deg, rgba(245,108,108,0.7), rgba(248,152,152,0.5)); }
.kpi-cell.orange { background: linear-gradient(135deg, rgba(230,162,60,0.7), rgba(235,181,99,0.5)); }

/* ─── 設備グリッド ─── */
.machine-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(110px, 1fr));
  gap: 8px;
  flex: 1; overflow-y: auto;
}
.m-cell {
  border-radius: 6px;
  padding: 10px 6px;
  text-align: center;
  font-size: 11px;
  color: white;
  position: relative;
}
.m-cell::before {
  content: ''; position: absolute; top: 5px; right: 5px;
  width: 8px; height: 8px; border-radius: 50%; background: white;
  animation: blink 1.2s infinite;
}
@keyframes blink { 0%,50%{opacity:1;} 51%,100%{opacity:0.3;} }
.light-green { background: linear-gradient(135deg, #389e0d, #67C23A); }
.light-red { background: linear-gradient(135deg, #cf1322, #F56C6C); }
.light-yellow { background: linear-gradient(135deg, #d48806, #E6A23C); }
.light-blue { background: linear-gradient(135deg, #096dd9, #409EFF); }
.light-gray { background: linear-gradient(135deg, #595959, #909399); }
.m-cd { font-weight: 700; font-size: 13px; }
.m-stat { font-size: 11px; opacity: 0.9; margin-top: 2px; }
.m-oee { font-size: 14px; font-weight: 600; margin-top: 2px; }

/* ─── 折線 ─── */
.trend-svg { width: 100%; height: calc(100% - 30px); }
.trend-legend {
  display: flex; gap: 16px; font-size: 12px; justify-content: center;
  margin-top: 4px;
}
.dot { display: inline-block; width: 10px; height: 10px; border-radius: 50%; margin-right: 4px; vertical-align: middle; }

/* ─── イベント ─── */
.event-list {
  height: 200px; overflow-y: auto;
  font-size: 12px;
}
.event-row {
  display: grid; grid-template-columns: 80px 60px 1fr; gap: 8px;
  padding: 4px 8px;
  border-radius: 3px;
  margin-bottom: 3px;
  align-items: center;
}
.ev-time { color: #909399; font-family: 'Consolas', monospace; }
.ev-tag { padding: 1px 6px; border-radius: 3px; font-size: 10px; text-align: center; font-weight: 600; }
.ev-prod .ev-tag { background: #67C23A; color: white; }
.ev-defect .ev-tag { background: #F56C6C; color: white; }
.ev-machine .ev-tag { background: #E6A23C; color: white; }
.ev-wo .ev-tag { background: #409EFF; color: white; }
.ev-downtime .ev-tag { background: #909399; color: white; }
.ev-msg { color: #e0e6ed; }

.event-enter-active { transition: all 0.3s ease; }
.event-leave-active { transition: all 0.2s ease; }
.event-enter-from { opacity: 0; transform: translateX(-20px); }
.event-leave-to { opacity: 0; transform: translateX(20px); }

.delay-list { font-size: 12px; }
.delay-row {
  display: grid; grid-template-columns: 140px 1fr 100px; gap: 8px;
  padding: 4px 8px;
  border-bottom: 1px dashed rgba(255,255,255,0.08);
}
.d-no { font-family: 'Consolas', monospace; color: #66B1FF; }
.d-days { color: #F56C6C; text-align: right; font-weight: 600; }

.empty {
  text-align: center; padding: 30px; color: #606266; font-style: italic;
}
.event-count {
  font-size: 11px; color: #909399; font-weight: normal;
}
</style>
