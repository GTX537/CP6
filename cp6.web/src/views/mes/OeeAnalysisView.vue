<template>
  <div class="oee-analysis">
    <el-card shadow="never" class="search-card">
      <el-form inline size="small">
        <el-form-item :label="t('設備CD')">
          <el-select v-model="machineCd" clearable filterable style="width: 200px;" :placeholder="t('全設備')">
            <el-option v-for="m in allMachines" :key="m.machineCd"
              :label="`${m.machineCd} - ${m.machineName}`" :value="m.machineCd" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('期間')">
          <el-date-picker v-model="dateRange" type="daterange" value-format="YYYY-MM-DD" style="width: 280px;" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="loading" @click="loadAll">{{ t('検索') }}</el-button>
          <el-button v-permission="'mes-oee:recalculate'" type="success" :loading="recalcing" @click="onRecalc">{{ t('本日 OEE 再計算') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- 本日 OEE カード -->
    <el-card shadow="never" class="today-card">
      <template #header>{{ t('本日リアルタイム OEE') }}</template>
      <div class="oee-cards">
        <div v-for="o in todayOee" :key="o.machineCd" class="oee-card">
          <div class="oee-cd">{{ o.machineCd }} {{ o.machineName }}</div>
          <div class="oee-main" :style="{ color: oeeColor(o.oee) }">{{ o.oee }}%</div>
          <div class="oee-sub">
            <div>{{ t('可用率') }} <strong>{{ o.availability }}%</strong></div>
            <div>{{ t('性能') }} <strong>{{ o.performance }}%</strong></div>
            <div>{{ t('品質') }} <strong>{{ o.quality }}%</strong></div>
          </div>
          <div class="oee-detail">
            <span>{{ t('稼働') }} {{ o.actualRunMinutes }}/{{ o.plannedRunMinutes }}{{ t('分') }}</span>
            <span style="margin-left: 8px;">{{ t('良品') }} {{ o.goodQty }} / {{ t('不良') }} {{ o.defectQty }}</span>
          </div>
        </div>
      </div>
    </el-card>

    <!-- OEE 推移グラフ -->
    <el-card shadow="never" style="margin-top: 12px;">
      <template #header>
        <div style="display: flex; justify-content: space-between; align-items: center;">
          <span>{{ t('OEE 推移（過去 {trendDays} 日）', { trendDays }) }}</span>
          <el-radio-group v-model="trendDays" size="small" @change="loadTrend">
            <el-radio-button :value="7">{{ t('7日') }}</el-radio-button>
            <el-radio-button :value="14">{{ t('14日') }}</el-radio-button>
            <el-radio-button :value="30">{{ t('30日') }}</el-radio-button>
          </el-radio-group>
        </div>
      </template>
      <div v-if="trendMachines.length === 0" class="empty">{{ t('データなし') }}</div>
      <svg v-else :viewBox="'0 0 ' + chartW + ' ' + chartH" :width="chartW" :height="chartH" style="max-width: 100%;">
        <!-- グリッド & Y軸 -->
        <g v-for="i in 6" :key="'gy'+i">
          <line :x1="40" :x2="chartW - 10"
            :y1="paddingTop + (chartH - paddingTop - paddingBottom) * (i - 1) / 5"
            :y2="paddingTop + (chartH - paddingTop - paddingBottom) * (i - 1) / 5"
            stroke="#eee" stroke-width="1" />
          <text x="35"
            :y="paddingTop + (chartH - paddingTop - paddingBottom) * (i - 1) / 5 + 4"
            text-anchor="end" font-size="10" fill="#666">
            {{ 100 - (i - 1) * 20 }}
          </text>
        </g>
        <!-- 折線：設備別 -->
        <g v-for="(m, mi) in trendMachines" :key="m.machineCd">
          <polyline :points="getLinePoints(m)" fill="none" :stroke="lineColors[mi % lineColors.length]" stroke-width="2" />
          <circle v-for="(p, pi) in m.points" :key="pi"
            :cx="pointX(pi, m.points.length)" :cy="pointY(p.oee)" r="3"
            :fill="lineColors[mi % lineColors.length]">
            <title>{{ formatDate(p.oeeDate) }}: OEE {{ p.oee }}%</title>
          </circle>
        </g>
        <!-- X 軸ラベル -->
        <text v-for="(d, i) in xLabels" :key="'x'+i"
          :x="pointX(i, xLabels.length)" :y="chartH - paddingBottom + 15"
          text-anchor="middle" font-size="10" fill="#666">
          {{ d }}
        </text>
      </svg>
      <div v-if="trendMachines.length > 0" class="trend-legend">
        <span v-for="(m, mi) in trendMachines" :key="m.machineCd" class="legend-tag">
          <span class="dot" :style="{ background: lineColors[mi % lineColors.length] }"></span>
          {{ m.machineCd }}
        </span>
      </div>
    </el-card>

    <!-- OEE 一覧表 -->
    <el-card shadow="never" style="margin-top: 12px;">
      <template #header>{{ t('OEE 日次データ') }}</template>
      <el-table :data="rows" border stripe size="small" max-height="500">
        <el-table-column :label="t('日付')" width="110">
          <template #default="{ row }">{{ formatDate(row.oeeDate) }}</template>
        </el-table-column>
        <el-table-column prop="machineCd" :label="t('設備CD')" width="100" />
        <el-table-column prop="machineName" :label="t('設備名')" min-width="160" />
        <el-table-column prop="plannedRunMinutes" :label="t('計画(分)')" width="100" align="right" />
        <el-table-column prop="actualRunMinutes" :label="t('実稼働(分)')" width="110" align="right" />
        <el-table-column prop="downtimeMinutes" :label="t('停止(分)')" width="100" align="right" />
        <el-table-column prop="goodQty" :label="t('良品')" width="100" align="right" />
        <el-table-column prop="defectQty" :label="t('不良')" width="100" align="right" />
        <el-table-column :label="t('可用率')" width="120">
          <template #default="{ row }">
            <el-progress :percentage="Math.round(row.availability)" :stroke-width="10" :color="'#409EFF'" />
          </template>
        </el-table-column>
        <el-table-column :label="t('性能')" width="120">
          <template #default="{ row }">
            <el-progress :percentage="Math.round(row.performance)" :stroke-width="10" :color="'#E6A23C'" />
          </template>
        </el-table-column>
        <el-table-column :label="t('品質')" width="120">
          <template #default="{ row }">
            <el-progress :percentage="Math.round(row.quality)" :stroke-width="10" :color="'#67C23A'" />
          </template>
        </el-table-column>
        <el-table-column label="OEE" width="100" align="center">
          <template #default="{ row }">
            <strong :style="{ color: oeeColor(row.oee), fontSize: '16px' }">{{ row.oee }}%</strong>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { ElMessage } from 'element-plus'
import { machineApi, oeeApi } from '@/api/mes/mes'
import type { MachineDto, OeeDailyDto } from '@/types/mes/mes'

const loading = ref(false)
const recalcing = ref(false)
const allMachines = ref<MachineDto[]>([])
const todayOee = ref<OeeDailyDto[]>([])
const rows = ref<OeeDailyDto[]>([])
const trendMachines = ref<{ machineCd: string; points: OeeDailyDto[] }[]>([])
const trendDays = ref(14)
const machineCd = ref('')

const dateRange = ref<[string, string]>([
  new Date(Date.now() - 14 * 86400000).toISOString().slice(0, 10),
  new Date().toISOString().slice(0, 10),
])

// 折線図
const chartW = 1100
const chartH = 240
const paddingTop = 10
const paddingBottom = 24
const paddingLeft = 40

const lineColors = ['#409EFF', '#67C23A', '#E6A23C', '#F56C6C', '#909399', '#36CFC9', '#9254DE', '#FA8C16']

const xLabels = computed(() => {
  if (trendMachines.value.length === 0) return []
  const first = trendMachines.value[0]
  if (!first) return []
  return first.points.map(p => formatDate(p.oeeDate).substring(5))
})

function pointX(i: number, total: number) {
  if (total <= 1) return paddingLeft
  return paddingLeft + ((chartW - paddingLeft - 10) * i / (total - 1))
}
function pointY(v: number) {
  const range = chartH - paddingTop - paddingBottom
  return paddingTop + range - (v / 100) * range
}
function getLinePoints(m: { points: OeeDailyDto[] }) {
  return m.points.map((p, i) => `${pointX(i, m.points.length)},${pointY(p.oee)}`).join(' ')
}

function oeeColor(v: number) {
  if (v >= 80) return '#67C23A'
  if (v >= 60) return '#E6A23C'
  return '#F56C6C'
}

function formatDate(v?: string | null) { return v ? v.substring(0, 10) : '' }

async function loadAll() {
  loading.value = true
  try {
    const [t, hist] = await Promise.all([
      oeeApi.today(machineCd.value || undefined),
      oeeApi.search({
        machineCd: machineCd.value,
        dateFrom: dateRange.value[0],
        dateTo: dateRange.value[1],
      }),
    ])
    todayOee.value = t.data || []
    rows.value = hist.data || []
  } finally {
    loading.value = false
  }
  await loadTrend()
}

async function loadTrend() {
  const r = await oeeApi.trend(trendDays.value, machineCd.value || undefined)
  const data = r.data || {}
  trendMachines.value = Object.entries(data).map(([cd, pts]) => ({
    machineCd: cd,
    points: pts,
  }))
}

async function onRecalc() {
  recalcing.value = true
  try {
    const r = await oeeApi.recalculate({
      targetDate: new Date().toISOString().slice(0, 10),
      machineCd: machineCd.value,
    })
    ElMessage.success(t('{count} 件再計算しました', { count: r.data?.recalculated }))
    await loadAll()
  } finally {
    recalcing.value = false
  }
}

let refreshTimer: number | undefined

onMounted(async () => {
  const ms = await machineApi.search({ activeOnly: true })
  allMachines.value = ms.data || []
  await loadAll()
  // 30 秒間隔で本日 OEE 再描画
  refreshTimer = window.setInterval(async () => {
    const t = await oeeApi.today(machineCd.value || undefined)
    todayOee.value = t.data || []
  }, 30000)
})

onBeforeUnmount(() => {
  if (refreshTimer) clearInterval(refreshTimer)
})
</script>

<style scoped>
.oee-analysis { padding: 16px; }
.search-card { margin-bottom: 12px; }
.today-card { margin-bottom: 12px; }

.oee-cards {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 12px;
}
.oee-card {
  border: 1px solid #ebeef5;
  border-radius: 6px;
  padding: 12px;
  text-align: center;
  background: linear-gradient(135deg, #fafbfc, white);
}
.oee-cd { font-size: 12px; color: #606266; }
.oee-main { font-size: 36px; font-weight: 700; margin: 8px 0; line-height: 1; }
.oee-sub {
  display: grid; grid-template-columns: repeat(3, 1fr); gap: 4px;
  font-size: 11px; color: #606266;
}
.oee-detail {
  margin-top: 8px; padding-top: 6px; border-top: 1px solid #f0f0f0;
  font-size: 11px; color: #909399;
}

.trend-legend { margin-top: 12px; display: flex; gap: 12px; flex-wrap: wrap; }
.legend-tag {
  font-size: 12px; display: inline-flex; align-items: center; gap: 4px;
}
.dot { width: 10px; height: 10px; border-radius: 50%; display: inline-block; }

.empty { padding: 60px; text-align: center; color: #909399; }
</style>
