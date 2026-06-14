<template>
  <div class="mes-dashboard">
    <!-- 本日 KPI -->
    <div class="kpi-row">
      <div class="kpi-card kpi-blue">
        <div class="kpi-label">{{ t('着手中 指図') }}</div>
        <div class="kpi-value">{{ summary.inProgressCount }}</div>
        <div class="kpi-sub">{{ t('本日完了') }} {{ summary.completedCount }}</div>
      </div>
      <div class="kpi-card kpi-green">
        <div class="kpi-label">{{ t('本日 良品数') }}</div>
        <div class="kpi-value">{{ summary.totalGoodQty }}</div>
      </div>
      <div class="kpi-card kpi-red">
        <div class="kpi-label">{{ t('本日 不良率') }}</div>
        <div class="kpi-value">{{ summary.defectRate }}%</div>
        <div class="kpi-sub">{{ t('不良') }} {{ summary.totalDefectQty }}</div>
      </div>
      <div class="kpi-card kpi-orange">
        <div class="kpi-label">{{ t('遅延件数') }}</div>
        <div class="kpi-value">{{ summary.delayedCount }}</div>
      </div>
    </div>

    <el-row :gutter="12" style="margin-top: 12px;">
      <!-- 工程別進捗（横棒積上） -->
      <el-col :span="12">
        <el-card shadow="never" class="chart-card">
          <template #header>{{ t('工程別進捗') }}</template>
          <div v-if="processProgress.length === 0" class="empty">{{ t('データなし') }}</div>
          <div v-else class="process-bars">
            <div v-for="p in processProgress" :key="p.processCd" class="proc-row">
              <div class="proc-label">{{ p.processCd }} {{ p.processName || '' }}</div>
              <div class="proc-bar" :style="{ background: '#f5f5f5' }">
                <div class="proc-seg" :style="{ width: pct(p, 'completed') + '%', background: '#67C23A' }" :title="t('完了 {n}', { n: p.completed })"></div>
                <div class="proc-seg" :style="{ width: pct(p, 'inProgress') + '%', background: '#E6A23C' }" :title="t('着手中 {n}', { n: p.inProgress })"></div>
                <div class="proc-seg" :style="{ width: pct(p, 'notStarted') + '%', background: '#909399' }" :title="t('未着手 {n}', { n: p.notStarted })"></div>
              </div>
              <div class="proc-count">
                <span style="color: #67C23A;">{{ p.completed }}</span> /
                <span style="color: #E6A23C;">{{ p.inProgress }}</span> /
                <span style="color: #909399;">{{ p.notStarted }}</span>
              </div>
            </div>
          </div>
        </el-card>
      </el-col>

      <!-- 不良 TOP5（円グラフ） -->
      <el-col :span="12">
        <el-card shadow="never" class="chart-card">
          <template #header>{{ t('不良 TOP5（過去30日）') }}</template>
          <div v-if="defectTop5.length === 0" class="empty">{{ t('データなし') }}</div>
          <div v-else style="display: flex; align-items: center; gap: 16px;">
            <svg :width="200" :height="200" viewBox="0 0 200 200">
              <g v-for="(slice, i) in pieSlices" :key="i">
                <path :d="slice.d" :fill="slice.color" stroke="white" stroke-width="2" />
              </g>
            </svg>
            <div class="legend-list">
              <div v-for="(slice, i) in pieSlices" :key="i" class="legend-row">
                <span class="dot" :style="{ background: slice.color }"></span>
                <span class="legend-name">{{ slice.label }}</span>
                <span class="legend-val">{{ slice.value }}{{ t('件') }}</span>
              </div>
            </div>
          </div>
        </el-card>
      </el-col>
    </el-row>

    <el-row :gutter="12" style="margin-top: 12px;">
      <!-- 日別生産推移（折線） -->
      <el-col :span="24">
        <el-card shadow="never" class="chart-card">
          <template #header>
            <div style="display: flex; justify-content: space-between; align-items: center;">
              <span>{{ t('日別生産推移') }}</span>
              <el-radio-group v-model="trendDays" size="small" @change="loadTrend">
                <el-radio-button :value="7">{{ t('7日') }}</el-radio-button>
                <el-radio-button :value="14">{{ t('14日') }}</el-radio-button>
                <el-radio-button :value="30">{{ t('30日') }}</el-radio-button>
              </el-radio-group>
            </div>
          </template>
          <div v-if="dailyTrend.length === 0" class="empty">{{ t('データなし') }}</div>
          <svg v-else :viewBox="'0 0 ' + chartW + ' ' + chartH" :width="chartW" :height="chartH" style="max-width: 100%;">
            <!-- グリッド線 -->
            <line v-for="i in 4" :key="i" x1="40" :x2="chartW - 10"
              :y1="paddingTop + (chartH - paddingTop - paddingBottom) * (i - 1) / 4"
              :y2="paddingTop + (chartH - paddingTop - paddingBottom) * (i - 1) / 4"
              stroke="#eee" stroke-width="1" />
            <!-- Y軸ラベル -->
            <text v-for="i in 5" :key="'y'+i" x="35"
              :y="paddingTop + (chartH - paddingTop - paddingBottom) * (i - 1) / 4 + 4"
              text-anchor="end" font-size="10" fill="#666">
              {{ Math.round(maxY - (maxY * (i - 1) / 4)) }}
            </text>
            <!-- 折線 good -->
            <polyline :points="goodPoints" fill="none" stroke="#67C23A" stroke-width="2" />
            <!-- 折線 defect -->
            <polyline :points="defectPoints" fill="none" stroke="#F56C6C" stroke-width="2" />
            <!-- 点 -->
            <g v-for="(p, i) in dailyTrend" :key="'p'+i">
              <circle :cx="pointX(i)" :cy="pointY(p.goodQty)" r="3" fill="#67C23A">
                <title>{{ t('{date}: 良品{good} / 不良{defect}', { date: p.date, good: p.goodQty, defect: p.defectQty }) }}</title>
              </circle>
              <circle :cx="pointX(i)" :cy="pointY(p.defectQty)" r="3" fill="#F56C6C">
                <title>{{ t('{date}: 良品{good} / 不良{defect}', { date: p.date, good: p.goodQty, defect: p.defectQty }) }}</title>
              </circle>
            </g>
            <!-- X軸ラベル（隔列） -->
            <text v-for="(p, i) in dailyTrend" :key="'x'+i"
              v-show="i % Math.max(1, Math.floor(dailyTrend.length / 10)) === 0"
              :x="pointX(i)" :y="chartH - paddingBottom + 15"
              text-anchor="middle" font-size="10" fill="#666">
              {{ p.date.substring(5) }}
            </text>
          </svg>
          <div style="margin-top: 8px; font-size: 12px;">
            <span style="color: #67C23A;">●</span> {{ t('良品数') }}
            <span style="color: #F56C6C; margin-left: 16px;">●</span> {{ t('不良数') }}
          </div>
        </el-card>
      </el-col>
    </el-row>

    <el-row :gutter="12" style="margin-top: 12px;">
      <!-- 工程稼働ヒートマップ -->
      <el-col :span="12">
        <el-card shadow="never" class="chart-card">
          <template #header>{{ t('工程稼働ヒートマップ（過去7日, 号機×時間帯）') }}</template>
          <div v-if="heatmap.length === 0" class="empty">{{ t('データなし') }}</div>
          <div v-else class="heatmap">
            <div class="heatmap-header">
              <div class="heat-row-label"></div>
              <div v-for="h in 24" :key="h" class="heat-col-label">{{ h - 1 }}</div>
            </div>
            <div v-for="m in machineList" :key="m" class="heat-row">
              <div class="heat-row-label">{{ m }}</div>
              <div v-for="h in 24" :key="h" class="heat-cell"
                :style="{ background: heatColor(getHeat(m, h - 1)) }"
                :title="t('{m} {h}時: {n}件', { m, h: h - 1, n: getHeat(m, h - 1) })">
              </div>
            </div>
          </div>
        </el-card>
      </el-col>

      <!-- 納期遅延アラート -->
      <el-col :span="12">
        <el-card shadow="never" class="chart-card">
          <template #header>{{ t('納期遅延アラート（{n}件）', { n: delayAlerts.length }) }}</template>
          <el-table :data="delayAlerts" border stripe size="small" max-height="320">
            <el-table-column prop="workOrderNo" :label="t('指図NO')" width="140" />
            <el-table-column prop="productName" :label="t('製品名')" min-width="120" />
            <el-table-column :label="t('計画完了日')" width="110">
              <template #default="{ row }">{{ formatDate(row.planEndDate) }}</template>
            </el-table-column>
            <el-table-column :label="t('遅延')" width="80" align="center">
              <template #default="{ row }">
                <el-tag type="danger" size="small">{{ row.delayDays }}{{ t('日') }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column :label="t('進捗')" width="100">
              <template #default="{ row }">{{ row.progressRate }}%</template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-col>
    </el-row>

    <el-row :gutter="12" style="margin-top: 12px;">
      <!-- 直近完了 -->
      <el-col :span="24">
        <el-card shadow="never" class="chart-card">
          <template #header>{{ t('直近完了指図（{n}件）', { n: recentCompleted.length }) }}</template>
          <el-table :data="recentCompleted" border stripe size="small">
            <el-table-column prop="workOrderNo" :label="t('指図NO')" width="160" />
            <el-table-column prop="productCd" :label="t('製品CD')" width="140" />
            <el-table-column prop="productName" :label="t('製品名')" min-width="180" />
            <el-table-column prop="productionQty" :label="t('生産数量')" width="110" align="right" />
            <el-table-column prop="completedQty" :label="t('完了数量')" width="110" align="right" />
            <el-table-column :label="t('完了日時')" width="170">
              <template #default="{ row }">{{ formatDateTime(row.actualEndDate) }}</template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { ref, reactive, computed, onMounted, onBeforeUnmount } from 'vue'
import { mesDashboardApi } from '@/api/mes/mes'
import type {
  MesDashboardSummaryDto,
  ProcessProgressDto,
  DailyTrendDto,
  DefectTop5Dto,
  DelayAlertDto,
  RecentCompletedDto,
  MachineHeatmapDto,
} from '@/types/mes/mes'

// ─── KPI ───
const summary = reactive<MesDashboardSummaryDto>({
  inProgressCount: 0, completedCount: 0, totalGoodQty: 0, totalDefectQty: 0,
  defectRate: 0, delayedCount: 0,
})

const processProgress = ref<ProcessProgressDto[]>([])
const dailyTrend = ref<DailyTrendDto[]>([])
const defectTop5 = ref<DefectTop5Dto[]>([])
const delayAlerts = ref<DelayAlertDto[]>([])
const recentCompleted = ref<RecentCompletedDto[]>([])
const heatmap = ref<MachineHeatmapDto[]>([])
const trendDays = ref(30)

// ─── 工程別 ───
function pct(p: ProcessProgressDto, key: 'notStarted' | 'inProgress' | 'completed') {
  const total = p.notStarted + p.inProgress + p.completed
  return total > 0 ? Math.round((p[key] / total) * 100) : 0
}

// ─── 折線図 ───
const chartW = 1100
const chartH = 240
const paddingTop = 10
const paddingBottom = 24
const paddingLeft = 40

const maxY = computed(() => {
  const arr = dailyTrend.value
  if (arr.length === 0) return 100
  const m = Math.max(...arr.map(x => Math.max(x.goodQty, x.defectQty)), 10)
  // 切り上げ
  return Math.ceil(m / 100) * 100 || m
})

function pointX(i: number) {
  if (dailyTrend.value.length <= 1) return paddingLeft
  return paddingLeft + ((chartW - paddingLeft - 10) * i / (dailyTrend.value.length - 1))
}
function pointY(v: number) {
  const range = chartH - paddingTop - paddingBottom
  return paddingTop + range - (v / Number(maxY.value)) * range
}

const goodPoints = computed(() => dailyTrend.value.map((p, i) => `${pointX(i)},${pointY(p.goodQty)}`).join(' '))
const defectPoints = computed(() => dailyTrend.value.map((p, i) => `${pointX(i)},${pointY(p.defectQty)}`).join(' '))

// ─── 円グラフ ───
const pieColors = ['#409EFF', '#67C23A', '#E6A23C', '#F56C6C', '#909399']
const pieSlices = computed(() => {
  const total = defectTop5.value.reduce((s, x) => s + x.count, 0)
  if (total === 0) return []
  let startAngle = -Math.PI / 2
  const cx = 100, cy = 100, r = 80
  return defectTop5.value.map((x, i) => {
    const angle = (x.count / total) * 2 * Math.PI
    const x1 = cx + r * Math.cos(startAngle)
    const y1 = cy + r * Math.sin(startAngle)
    const x2 = cx + r * Math.cos(startAngle + angle)
    const y2 = cy + r * Math.sin(startAngle + angle)
    const largeArc = angle > Math.PI ? 1 : 0
    const d = `M ${cx},${cy} L ${x1},${y1} A ${r},${r} 0 ${largeArc} 1 ${x2},${y2} Z`
    const slice = {
      d,
      color: pieColors[i % pieColors.length],
      label: `${x.categoryCd} ${x.categoryName || ''}`,
      value: x.count,
    }
    startAngle += angle
    return slice
  })
})

// ─── ヒートマップ ───
const machineList = computed(() => Array.from(new Set(heatmap.value.map(x => x.machineCd))).sort())
const heatMap = computed(() => {
  const m: Record<string, Record<number, number>> = {}
  for (const h of heatmap.value) {
    if (!m[h.machineCd]) m[h.machineCd] = {}
    m[h.machineCd]![h.hour] = h.count
  }
  return m
})
const maxHeat = computed(() => Math.max(1, ...heatmap.value.map(x => x.count)))

function getHeat(m: string, h: number) {
  return heatMap.value[m]?.[h] ?? 0
}
function heatColor(v: number) {
  if (v === 0) return '#fafafa'
  const intensity = v / maxHeat.value
  const r = 64
  const g = Math.round(158 - 80 * intensity)
  const b = Math.round(255 - 100 * intensity)
  return `rgb(${r}, ${g}, ${b})`
}

// ─── format ───
function formatDate(v?: string | null) { return v ? v.substring(0, 10) : '' }
function formatDateTime(v?: string | null) { return v ? v.substring(0, 16).replace('T', ' ') : '' }

// ─── load ───
async function loadAll() {
  const [s, pp, da, top5, rc, hm] = await Promise.all([
    mesDashboardApi.summary(),
    mesDashboardApi.processProgress(),
    mesDashboardApi.delayAlerts(50),
    mesDashboardApi.defectTop5(),
    mesDashboardApi.recentCompleted(10),
    mesDashboardApi.machineHeatmap(7),
  ])
  Object.assign(summary, s.data || summary)
  processProgress.value = pp.data || []
  delayAlerts.value = da.data || []
  defectTop5.value = top5.data || []
  recentCompleted.value = rc.data || []
  heatmap.value = hm.data || []
  await loadTrend()
}

async function loadTrend() {
  const r = await mesDashboardApi.dailyTrend(trendDays.value)
  dailyTrend.value = r.data || []
}

let summaryTimer: number | undefined
let delayTimer: number | undefined

onMounted(() => {
  loadAll()
  // 60 秒間隔で全体更新
  summaryTimer = window.setInterval(loadAll, 60000)
  // 10 秒間隔で遅延アラート更新
  delayTimer = window.setInterval(async () => {
    const r = await mesDashboardApi.delayAlerts(50)
    delayAlerts.value = r.data || []
  }, 10000)
})

onBeforeUnmount(() => {
  if (summaryTimer) clearInterval(summaryTimer)
  if (delayTimer) clearInterval(delayTimer)
})
</script>

<style scoped>
.mes-dashboard { padding: 16px; }

/* ─── KPI ─── */
.kpi-row {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 12px;
}
.kpi-card {
  padding: 16px 20px;
  border-radius: 8px;
  color: white;
}
.kpi-label { font-size: 13px; opacity: 0.9; }
.kpi-value { font-size: 32px; font-weight: 700; margin-top: 6px; line-height: 1; }
.kpi-sub { font-size: 12px; opacity: 0.85; margin-top: 4px; }
.kpi-blue { background: linear-gradient(135deg, #409EFF, #66B1FF); }
.kpi-orange { background: linear-gradient(135deg, #E6A23C, #EBB563); }
.kpi-green { background: linear-gradient(135deg, #67C23A, #85CE61); }
.kpi-red { background: linear-gradient(135deg, #F56C6C, #F89898); }

.chart-card { min-height: 280px; }
.empty {
  display: flex; justify-content: center; align-items: center;
  min-height: 200px; color: #909399; font-size: 14px;
}

/* ─── process-bars ─── */
.process-bars { display: flex; flex-direction: column; gap: 8px; }
.proc-row {
  display: grid; grid-template-columns: 150px 1fr 120px; align-items: center; gap: 12px;
  font-size: 12px;
}
.proc-label { font-weight: 600; }
.proc-bar {
  display: flex; height: 18px; border-radius: 3px; overflow: hidden;
}
.proc-seg { height: 100%; transition: width 0.3s; }
.proc-count { text-align: right; font-size: 12px; }

/* ─── pie legend ─── */
.legend-list { flex: 1; display: flex; flex-direction: column; gap: 6px; font-size: 12px; }
.legend-row { display: flex; align-items: center; gap: 6px; }
.dot { width: 10px; height: 10px; border-radius: 50%; }
.legend-name { flex: 1; }
.legend-val { color: #606266; }

/* ─── heatmap ─── */
.heatmap { font-size: 11px; }
.heatmap-header { display: grid; grid-template-columns: 70px repeat(24, 1fr); margin-bottom: 4px; }
.heat-row { display: grid; grid-template-columns: 70px repeat(24, 1fr); }
.heat-row-label { padding: 2px 6px; font-weight: 600; }
.heat-col-label { text-align: center; color: #909399; }
.heat-cell {
  height: 22px; border: 1px solid white; cursor: default;
}
</style>
