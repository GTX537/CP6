<template>
  <div class="planning-board">
    <!-- 検索 + KPI -->
    <el-card shadow="never" class="header-card">
      <el-form :model="query" inline size="small">
        <el-form-item :label="t('拠点CD')">
          <el-input v-model="query.baseCd" style="width: 120px;" />
        </el-form-item>
        <el-form-item :label="t('期間')">
          <el-date-picker v-model="dateRange" type="daterange" value-format="YYYY-MM-DD" :start-placeholder="t('開始')" :end-placeholder="t('終了')" style="width: 280px;" />
        </el-form-item>
        <el-form-item :label="t('工程CD')">
          <el-input v-model="query.processCd" style="width: 120px;" />
        </el-form-item>
        <el-form-item label="WG">
          <el-input v-model="query.wgCd" style="width: 100px;" />
        </el-form-item>
        <el-form-item :label="t('号機')">
          <el-input v-model="query.machineCd" style="width: 120px;" />
        </el-form-item>
        <el-form-item :label="t('ステータス')">
          <el-checkbox-group v-model="query.statuses">
            <el-checkbox v-for="o in PROCESS_STATUS_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</el-checkbox>
          </el-checkbox-group>
        </el-form-item>
        <el-form-item :label="t('表示単位')">
          <el-radio-group v-model="viewMode" size="small">
            <el-radio-button value="day">{{ t('日') }}</el-radio-button>
            <el-radio-button value="week">{{ t('週') }}</el-radio-button>
            <el-radio-button value="month">{{ t('月') }}</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="loading" @click="reload">{{ t('検索') }}</el-button>
          <el-button @click="resetQuery">{{ t('クリア') }}</el-button>
          <el-button type="warning" :icon="MagicStick" :loading="arranging" @click="onAutoArrange">{{ t('自動配置') }}</el-button>
        </el-form-item>
      </el-form>

      <!-- KPI 4 カード -->
      <div class="kpi-row">
        <div class="kpi-card kpi-blue">
          <div class="kpi-label">{{ t('総指図件数') }}</div>
          <div class="kpi-value">{{ kpi.totalOrders }}</div>
        </div>
        <div class="kpi-card kpi-orange">
          <div class="kpi-label">{{ t('着手中') }}</div>
          <div class="kpi-value">{{ kpi.inProgressOrders }}</div>
        </div>
        <div class="kpi-card kpi-green">
          <div class="kpi-label">{{ t('完了') }}</div>
          <div class="kpi-value">{{ kpi.completedOrders }}</div>
        </div>
        <div class="kpi-card kpi-red">
          <div class="kpi-label">{{ t('遅延') }}</div>
          <div class="kpi-value">{{ kpi.delayedOrders }}</div>
          <div class="kpi-sub">{{ t('平均') }} {{ kpi.avgDelayDays }} {{ t('日') }}</div>
        </div>
        <div class="kpi-card kpi-purple">
          <div class="kpi-label">{{ t('完了率') }}</div>
          <div class="kpi-value">{{ kpi.completionRate }}%</div>
        </div>
      </div>
    </el-card>

    <!-- 凡例 -->
    <el-card shadow="never" style="margin-top: 8px;">
      <div style="display: flex; gap: 16px; flex-wrap: wrap; align-items: center;">
        <strong>{{ t('凡例：') }}</strong>
        <span v-for="o in PROCESS_STATUS_OPTIONS" :key="o.value" class="legend-item">
          <span class="legend-color" :style="{ background: o.color }"></span>{{ o.label }}
        </span>
        <span class="legend-item">
          <span class="legend-color legend-actual"></span>{{ t('実績 overlay') }}
        </span>
        <span class="legend-item">
          <span class="legend-color" style="background: #F56C6C; width: 4px;"></span>{{ t('納期ライン') }}
        </span>
      </div>
    </el-card>

    <!-- ガント本体 -->
    <el-card shadow="never" style="margin-top: 8px;">
      <div v-loading="loading" class="gantt-container">
        <!-- 列ヘッダ：日付 -->
        <div class="gantt-header" :style="{ width: bodyWidth + 'px' }">
          <div class="header-left">{{ t('号機 / 工程') }}</div>
          <div class="header-right">
            <div class="header-tick" v-for="(t, i) in ticks" :key="i" :style="{ left: (i * cellWidth) + 'px', width: cellWidth + 'px' }">
              {{ t.label }}
            </div>
          </div>
        </div>

        <!-- 行：号機 grouping -->
        <div v-for="(group, gi) in machineGroups" :key="gi" class="gantt-row" :style="{ width: bodyWidth + 'px' }">
          <div class="row-left">{{ group.machine }}</div>
          <div class="row-right" :style="{ height: (group.bars.length * 28 + 8) + 'px' }">
            <!-- 縦軸グリッド -->
            <div v-for="(t, i) in ticks" :key="i" class="grid-line" :style="{ left: (i * cellWidth) + 'px' }"></div>

            <!-- 各 Bar -->
            <div v-for="(b, bi) in group.bars" :key="b.id"
              class="bar plan-bar"
              :style="barStyle(b, bi)"
              :title="barTooltip(b)"
              @click="onBarClick(b)">
              <span class="bar-text">{{ b.workOrderNo }} / {{ b.processName || b.processCd }}</span>
            </div>

            <!-- 実績 overlay -->
            <div v-for="(b, bi) in group.bars" :key="'a-' + b.id">
              <div v-if="b.actualStartTime" class="bar actual-bar"
                :style="actualBarStyle(b, bi)"
                :title="t('実績: {start} ~ {end}', { start: formatDateTime(b.actualStartTime), end: formatDateTime(b.actualEndTime) })">
              </div>
            </div>

            <!-- 納期ライン -->
            <div v-for="(b, bi) in group.bars" :key="'d-' + b.id">
              <div v-if="b.deliveryDate" class="deadline-line"
                :style="deadlineStyle(b)"
                :title="t('納期: {date}', { date: formatDate(b.deliveryDate) })">
              </div>
            </div>
          </div>
        </div>

        <div v-if="bars.length === 0 && !loading" class="empty">
          <el-empty :description="t('表示できる工程がありません')" />
        </div>
      </div>
    </el-card>

    <!-- バー編集 ダイアログ -->
    <el-dialog v-model="dialogVisible" :title="t('工程編集')" width="500px">
      <el-form v-if="editing" :model="editing" label-width="120px" size="small">
        <el-form-item :label="t('指図NO')">
          <el-tag>{{ editing.workOrderNo }}</el-tag>
        </el-form-item>
        <el-form-item :label="t('工程')">
          <span>{{ editing.processCd }} - {{ editing.processName }}</span>
        </el-form-item>
        <el-form-item :label="t('号機CD')">
          <el-input v-model="editing.machineCd" />
        </el-form-item>
        <el-form-item :label="t('計画開始日時')">
          <el-date-picker v-model="editing.planStartTime" type="datetime" value-format="YYYY-MM-DDTHH:mm:ss" style="width: 100%;" />
        </el-form-item>
        <el-form-item :label="t('計画完了日時')">
          <el-date-picker v-model="editing.planEndTime" type="datetime" value-format="YYYY-MM-DDTHH:mm:ss" style="width: 100%;" />
        </el-form-item>
        <el-form-item :label="t('状態')">
          <el-tag>{{ getStatusLabel(editing.processStatus) }}</el-tag>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">{{ t('キャンセル') }}</el-button>
        <el-button type="primary" :loading="saving" @click="onReschedule" :disabled="!canEdit">{{ t('保存') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { ref, reactive, computed, onMounted, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { MagicStick } from '@element-plus/icons-vue'
import { planningBoardApi } from '@/api/mes/mes'
import {
  PROCESS_STATUS_OPTIONS,
  type PlanningBarDto,
  type PlanningBoardQuery,
  type PlanningKpiDto,
} from '@/types/mes/mes'

const loading = ref(false)
const arranging = ref(false)
const saving = ref(false)
const bars = ref<PlanningBarDto[]>([])
const kpi = ref<PlanningKpiDto>({
  totalOrders: 0, inProgressOrders: 0, completedOrders: 0,
  delayedOrders: 0, completionRate: 0, avgDelayDays: 0,
})

const dateRange = ref<[string, string]>([
  new Date(Date.now() - 7 * 86400000).toISOString().slice(0, 10),
  new Date(Date.now() + 14 * 86400000).toISOString().slice(0, 10),
])

const defaultQuery = (): PlanningBoardQuery => ({
  dateFrom: dateRange.value[0],
  dateTo: dateRange.value[1],
  processCd: '',
  wgCd: '',
  machineCd: '',
  baseCd: '',
  statuses: [0, 1, 3],
})
const query = reactive<PlanningBoardQuery>(defaultQuery())

watch(dateRange, ([from, to]) => {
  query.dateFrom = from
  query.dateTo = to
})

const viewMode = ref<'day' | 'week' | 'month'>('day')

// セル幅（px）
const cellWidth = computed(() => {
  if (viewMode.value === 'day') return 60   // 1日 = 60px（時間単位は1セル=24h、画面は1日1セル）
  if (viewMode.value === 'week') return 80  // 1週間 = 80px
  return 32                                  // 月：1日 32px
})

// 時間範囲（dateFrom/To から ticks 計算）
const ticks = computed(() => {
  if (!query.dateFrom || !query.dateTo) return []
  const from = new Date(query.dateFrom)
  const to = new Date(query.dateTo)
  const arr: { date: Date; label: string }[] = []
  const cur = new Date(from)
  while (cur <= to) {
    let label: string
    if (viewMode.value === 'day') label = `${cur.getMonth() + 1}/${cur.getDate()}`
    else if (viewMode.value === 'month') label = `${cur.getMonth() + 1}/${cur.getDate()}`
    else label = `W${getWeek(cur)}`
    arr.push({ date: new Date(cur), label })
    if (viewMode.value === 'week') cur.setDate(cur.getDate() + 7)
    else cur.setDate(cur.getDate() + 1)
  }
  return arr
})

const bodyWidth = computed(() => 200 + ticks.value.length * cellWidth.value)

const machineGroups = computed(() => {
  const map: Record<string, PlanningBarDto[]> = {}
  for (const b of bars.value) {
    const m = b.machineCd || t('(未設定)')
    ;(map[m] ??= []).push(b)
  }
  return Object.entries(map).map(([machine, list]) => ({
    machine,
    bars: list.sort((a, b) => (a.planStartTime || '').localeCompare(b.planStartTime || '')),
  }))
})

function getWeek(d: Date): number {
  const onejan = new Date(d.getFullYear(), 0, 1)
  return Math.ceil(((d.getTime() - onejan.getTime()) / 86400000 + onejan.getDay() + 1) / 7)
}

function getStatusLabel(v: number) {
  return PROCESS_STATUS_OPTIONS.find(o => o.value === v)?.label ?? `${v}`
}
function getStatusColor(v: number) {
  return PROCESS_STATUS_OPTIONS.find(o => o.value === v)?.color ?? '#909399'
}

function formatDate(v?: string | null) { return v ? v.substring(0, 10) : '' }
function formatDateTime(v?: string | null) { return v ? v.substring(0, 16).replace('T', ' ') : '' }

// バーの left/width 計算（px）
function calcLeftWidth(start: string, end: string) {
  if (!query.dateFrom || !query.dateTo) return { left: 0, width: 0 }
  const from = new Date(query.dateFrom).getTime()
  const to = new Date(query.dateTo).getTime() + 86400000
  const totalMs = to - from
  const px = bodyWidth.value - 200
  const s = new Date(start).getTime()
  const e = new Date(end).getTime()
  const left = ((Math.max(s, from) - from) / totalMs) * px
  const width = Math.max(4, ((Math.min(e, to) - Math.max(s, from)) / totalMs) * px)
  return { left, width }
}

function barStyle(b: PlanningBarDto, idx: number) {
  if (!b.planStartTime || !b.planEndTime) return { display: 'none' }
  const { left, width } = calcLeftWidth(b.planStartTime, b.planEndTime)
  return {
    left: left + 'px',
    width: width + 'px',
    top: (idx * 28 + 4) + 'px',
    background: getStatusColor(b.processStatus),
    border: b.priority === 3 ? '2px solid #F56C6C' : (b.priority === 2 ? '2px solid #E6A23C' : 'none'),
  }
}

function actualBarStyle(b: PlanningBarDto, idx: number) {
  if (!b.actualStartTime) return { display: 'none' }
  const end = b.actualEndTime || new Date().toISOString()
  const { left, width } = calcLeftWidth(b.actualStartTime, end)
  return {
    left: left + 'px',
    width: width + 'px',
    top: (idx * 28 + 18) + 'px',
    background: 'rgba(103, 194, 58, 0.55)',
    height: '8px',
  }
}

function deadlineStyle(b: PlanningBarDto) {
  if (!b.deliveryDate) return { display: 'none' }
  const { left } = calcLeftWidth(b.deliveryDate, b.deliveryDate)
  return {
    left: left + 'px',
    width: '2px',
  }
}

function barTooltip(b: PlanningBarDto): string {
  return [
    t('指図: {no}', { no: b.workOrderNo }),
    t('製品: {cd} {name}', { cd: b.productCd, name: b.productName || '' }),
    t('工程: {cd} {name}', { cd: b.processCd, name: b.processName || '' }),
    t('号機: {cd}', { cd: b.machineCd || '-' }),
    t('計画: {start} ~ {end}', { start: formatDateTime(b.planStartTime), end: formatDateTime(b.planEndTime) }),
    b.actualStartTime ? t('実績: {start} ~ {end}', { start: formatDateTime(b.actualStartTime), end: formatDateTime(b.actualEndTime) }) : '',
    t('状態: {status}', { status: getStatusLabel(b.processStatus) }),
    t('進捗: {good}/{plan} 不良 {defect}', { good: b.goodQty, plan: b.planQty || '?', defect: b.defectQty }),
  ].filter(Boolean).join('\n')
}

// ──────────────────────────────────────
// CRUD
// ──────────────────────────────────────
async function reload() {
  loading.value = true
  try {
    const [barsRes, kpiRes] = await Promise.all([
      planningBoardApi.getBars(query),
      planningBoardApi.getKpi(query),
    ])
    bars.value = barsRes.data || []
    kpi.value = kpiRes.data || kpi.value
  } finally {
    loading.value = false
  }
}

function resetQuery() {
  Object.assign(query, defaultQuery())
  reload()
}

async function onAutoArrange() {
  try {
    await ElMessageBox.confirm(t('未着手の工程に対し優先度+納期で自動配置します。続行しますか？'), t('確認'), { type: 'info' })
    arranging.value = true
    const res = await planningBoardApi.autoArrange({
      baseDate: new Date().toISOString().slice(0, 10),
      wgCd: query.wgCd,
      processCd: query.processCd,
      defaultHoursPerJob: 2,
    })
    ElMessage.success(t('{n}件の工程を自動配置しました', { n: res.data?.changed ?? 0 }))
    await reload()
  } catch (e) { /* cancelled */ } finally { arranging.value = false }
}

// ──────────────────────────────────────
// Bar クリック → 編集
// ──────────────────────────────────────
const dialogVisible = ref(false)
const editing = ref<PlanningBarDto | null>(null)
const canEdit = computed(() => editing.value?.processStatus === 0)

function onBarClick(b: PlanningBarDto) {
  editing.value = { ...b }
  dialogVisible.value = true
}

async function onReschedule() {
  if (!editing.value) return
  if (!editing.value.planStartTime || !editing.value.planEndTime) {
    ElMessage.error(t('計画日時を入力してください'))
    return
  }
  saving.value = true
  try {
    await planningBoardApi.reschedule({
      id: editing.value.id,
      planStartTime: editing.value.planStartTime,
      planEndTime: editing.value.planEndTime,
      machineCd: editing.value.machineCd,
    })
    ElMessage.success(t('ME-MSG-041: 保存しました'))
    dialogVisible.value = false
    await reload()
  } finally {
    saving.value = false
  }
}

onMounted(() => {
  reload()
  // 60 秒間隔で自動更新
  setInterval(reload, 60000)
})
</script>

<style scoped>
.planning-board {
  padding: 16px;
}
.header-card :deep(.el-form-item) {
  margin-bottom: 8px;
}

/* ─── KPI ─── */
.kpi-row {
  display: grid;
  grid-template-columns: repeat(5, 1fr);
  gap: 12px;
  margin-top: 8px;
}
.kpi-card {
  padding: 12px 16px;
  border-radius: 6px;
  color: white;
  position: relative;
}
.kpi-label { font-size: 12px; opacity: 0.85; }
.kpi-value { font-size: 28px; font-weight: 700; margin-top: 4px; }
.kpi-sub { font-size: 12px; opacity: 0.85; }
.kpi-blue { background: linear-gradient(135deg, #409EFF, #66B1FF); }
.kpi-orange { background: linear-gradient(135deg, #E6A23C, #EBB563); }
.kpi-green { background: linear-gradient(135deg, #67C23A, #85CE61); }
.kpi-red { background: linear-gradient(135deg, #F56C6C, #F89898); }
.kpi-purple { background: linear-gradient(135deg, #909399, #B1B3B8); }

/* ─── 凡例 ─── */
.legend-item {
  display: inline-flex; align-items: center; gap: 4px; font-size: 12px;
}
.legend-color { display: inline-block; width: 16px; height: 12px; border-radius: 2px; }
.legend-actual { background: rgba(103, 194, 58, 0.55); }

/* ─── ガント ─── */
.gantt-container {
  overflow-x: auto;
  overflow-y: visible;
  min-height: 200px;
  border: 1px solid #ebeef5;
  border-radius: 4px;
}
.gantt-header {
  display: flex;
  background: #f5f7fa;
  border-bottom: 2px solid #ddd;
  font-size: 12px;
  font-weight: 600;
  position: sticky; top: 0; z-index: 5;
}
.header-left {
  width: 200px; padding: 8px; border-right: 1px solid #ddd;
  position: sticky; left: 0; background: #f5f7fa; z-index: 6;
}
.header-right {
  position: relative; flex: 1; height: 28px;
}
.header-tick {
  position: absolute; top: 0; padding: 6px 0; text-align: center;
  border-right: 1px solid #ebeef5;
}

.gantt-row {
  display: flex;
  border-bottom: 1px solid #ebeef5;
}
.row-left {
  width: 200px; padding: 8px; border-right: 1px solid #ddd; font-weight: 600;
  position: sticky; left: 0; background: white; z-index: 4;
  font-size: 13px;
}
.row-right {
  flex: 1; position: relative; min-height: 32px;
  background-image: repeating-linear-gradient(to right, transparent 0, transparent 0, rgba(0,0,0,0.02) 0);
}
.grid-line {
  position: absolute; top: 0; bottom: 0; width: 1px; background: #f0f0f0;
}

.bar {
  position: absolute; height: 20px; border-radius: 3px;
  color: white; font-size: 11px; padding: 2px 6px;
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  cursor: pointer; user-select: none;
  box-shadow: 0 1px 2px rgba(0,0,0,0.15);
  transition: transform 0.1s;
}
.bar:hover {
  transform: scale(1.02);
  z-index: 3;
  box-shadow: 0 2px 8px rgba(0,0,0,0.25);
}
.bar-text { display: inline-block; }
.actual-bar {
  position: absolute; height: 8px !important; border-radius: 2px;
  pointer-events: none;
}
.deadline-line {
  position: absolute; top: 0; bottom: 0; background: #F56C6C;
  pointer-events: none;
}

.empty {
  padding: 40px;
  text-align: center;
}
</style>
