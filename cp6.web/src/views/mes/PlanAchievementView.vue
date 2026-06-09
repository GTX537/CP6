<template>
  <div class="plan-achievement">
    <div class="page-header">
      <h2>{{ t('mes.planAchievement.title') }}</h2>
      <el-button :icon="Refresh" circle :loading="loading" @click="loadSummary" />
    </div>

    <el-card shadow="never" class="filter-card">
      <el-form :model="query" inline size="small" @submit.prevent>
        <el-form-item :label="t('mes.planAchievement.filter.dateFrom')">
          <el-date-picker v-model="query.dateFrom" type="date" value-format="YYYY-MM-DD" clearable class="date-input" />
        </el-form-item>
        <el-form-item :label="t('mes.planAchievement.filter.dateTo')">
          <el-date-picker v-model="query.dateTo" type="date" value-format="YYYY-MM-DD" clearable class="date-input" />
        </el-form-item>
        <el-form-item :label="t('mes.planAchievement.filter.groupBy.label')">
          <el-radio-group v-model="query.groupBy">
            <el-radio-button label="product">{{ t('mes.planAchievement.filter.groupBy.product') }}</el-radio-button>
            <el-radio-button label="month">{{ t('mes.planAchievement.filter.groupBy.month') }}</el-radio-button>
            <el-radio-button label="customer">{{ t('mes.planAchievement.filter.groupBy.customer') }}</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item :label="t('mes.planAchievement.filter.product')">
          <el-input v-model="query.productCd" clearable class="text-input" @keyup.enter="search" />
        </el-form-item>
        <el-form-item :label="t('mes.planAchievement.filter.onlyCompleted')">
          <el-switch v-model="query.onlyCompleted" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" :loading="loading" @click="search">
            {{ t('mes.planAchievement.btn.search') }}
          </el-button>
          <el-button :icon="Download" :loading="exporting" @click="exportCsv">
            {{ t('mes.planAchievement.btn.exportCsv') }}
          </el-button>
          <el-button :icon="RefreshLeft" @click="resetQuery">{{ t('mes.planAchievement.btn.reset') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-row :gutter="12" class="kpi-row">
      <el-col :xs="12" :sm="6">
        <el-card shadow="never" class="kpi-card">
          <div class="kpi-label">{{ t('mes.planAchievement.kpi.overallRate') }}</div>
          <div class="kpi-value rate">{{ formatPercent(summary.achievementRate) }}</div>
        </el-card>
      </el-col>
      <el-col :xs="12" :sm="6">
        <el-card shadow="never" class="kpi-card">
          <div class="kpi-label">{{ t('mes.planAchievement.kpi.totalWo') }}</div>
          <div class="kpi-value">{{ formatNumber(summary.totalWorkOrders) }}</div>
        </el-card>
      </el-col>
      <el-col :xs="12" :sm="6">
        <el-card shadow="never" class="kpi-card">
          <div class="kpi-label">{{ t('mes.planAchievement.kpi.onTarget') }}</div>
          <div class="kpi-value good">{{ formatNumber(summary.onTargetCount) }}</div>
        </el-card>
      </el-col>
      <el-col :xs="12" :sm="6">
        <el-card shadow="never" class="kpi-card" :class="{ warn: summary.defectRate > 0 }">
          <div class="kpi-label">{{ t('mes.planAchievement.kpi.defectRate') }}</div>
          <div class="kpi-value late">{{ formatPercent(summary.defectRate) }}</div>
        </el-card>
      </el-col>
    </el-row>

    <el-card shadow="never" class="chart-card">
      <template #header>
        <div class="panel-header">
          <span>{{ t('mes.planAchievement.chart.title') }}</span>
          <el-tag size="small" type="info">{{ rows.length }}</el-tag>
        </div>
      </template>
      <div v-loading="loading" class="bar-list">
        <el-empty v-if="!rows.length && !loading" :image-size="80" />
        <div v-for="row in chartRows" :key="row.groupKey" class="bar-row">
          <div class="bar-label" :title="row.groupLabel">{{ row.groupLabel }}</div>
          <div class="bar-track">
            <div class="bar-fill" :class="barClass(row.achievementRate)" :style="{ width: progressPercent(row.achievementRate) + '%' }" />
          </div>
          <div class="bar-value">{{ formatPercent(row.achievementRate) }}</div>
        </div>
      </div>
    </el-card>

    <el-card shadow="never" class="table-card">
      <el-table :data="rows" border stripe size="small" max-height="620" v-loading="loading">
        <el-table-column prop="groupLabel" :label="t('mes.planAchievement.col.group')" min-width="180" show-overflow-tooltip />
        <el-table-column prop="workOrderCount" :label="t('mes.planAchievement.col.woCount')" width="100" align="right" />
        <el-table-column prop="plannedQty" :label="t('mes.planAchievement.col.planned')" width="120" align="right">
          <template #default="{ row }">{{ formatQty(row.plannedQty) }}</template>
        </el-table-column>
        <el-table-column prop="goodQty" :label="t('mes.planAchievement.col.good')" width="120" align="right">
          <template #default="{ row }">{{ formatQty(row.goodQty) }}</template>
        </el-table-column>
        <el-table-column prop="defectQty" :label="t('mes.planAchievement.col.defect')" width="110" align="right">
          <template #default="{ row }">
            <span :class="{ lateText: row.defectQty > 0 }">{{ formatQty(row.defectQty) }}</span>
          </template>
        </el-table-column>
        <el-table-column :label="t('mes.planAchievement.col.achievementRate')" min-width="190" align="right">
          <template #default="{ row }">
            <div class="progress-cell">
              <el-progress :percentage="progressPercent(row.achievementRate)" :status="progressStatus(row.achievementRate)" :stroke-width="10" />
              <span>{{ formatPercent(row.achievementRate) }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="defectRate" :label="t('mes.planAchievement.col.defectRate')" width="110" align="right">
          <template #default="{ row }">{{ formatPercent(row.defectRate) }}</template>
        </el-table-column>
        <el-table-column prop="onTargetCount" :label="t('mes.planAchievement.col.onTarget')" width="110" align="right" />
      </el-table>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { Download, Refresh, RefreshLeft, Search } from '@element-plus/icons-vue'
import { planAchievementApi } from '@/api/mes/planAchievement'
import type { PlanAchievementGroupBy, PlanAchievementQuery, PlanAchievementSummary } from '@/types/mes/planAchievement'

const { t } = useI18n()

const query = reactive<PlanAchievementQuery>({
  dateFrom: formatDate(addDays(new Date(), -90)),
  dateTo: formatDate(new Date()),
  groupBy: 'product',
  onlyCompleted: true,
})
const summary = reactive<PlanAchievementSummary>({
  totalWorkOrders: 0,
  totalPlannedQty: 0,
  totalGoodQty: 0,
  totalDefectQty: 0,
  achievementRate: 0,
  defectRate: 0,
  onTargetCount: 0,
  rows: [],
})
const loading = ref(false)
const exporting = ref(false)

const rows = computed(() => summary.rows || [])
const chartRows = computed(() => rows.value.slice(0, 12))

async function loadSummary() {
  loading.value = true
  try {
    const res = await planAchievementApi.summary(toPayload())
    Object.assign(summary, {
      totalWorkOrders: res.data?.totalWorkOrders ?? 0,
      totalPlannedQty: res.data?.totalPlannedQty ?? 0,
      totalGoodQty: res.data?.totalGoodQty ?? 0,
      totalDefectQty: res.data?.totalDefectQty ?? 0,
      achievementRate: res.data?.achievementRate ?? 0,
      defectRate: res.data?.defectRate ?? 0,
      onTargetCount: res.data?.onTargetCount ?? 0,
      rows: res.data?.rows ?? [],
    })
  } finally {
    loading.value = false
  }
}

function search() {
  loadSummary()
}

function resetQuery() {
  query.dateFrom = formatDate(addDays(new Date(), -90))
  query.dateTo = formatDate(new Date())
  query.groupBy = 'product'
  query.productCd = undefined
  query.customerCd = undefined
  query.onlyCompleted = true
  loadSummary()
}

async function exportCsv() {
  exporting.value = true
  try {
    const blob = (await planAchievementApi.exportCsv(toPayload())) as unknown as Blob
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `plan-achievement_${new Date().toISOString().slice(0, 10)}.csv`
    a.click()
    URL.revokeObjectURL(url)
    ElMessage.success(t('mes.planAchievement.msg.exported'))
  } finally {
    exporting.value = false
  }
}

function toPayload(): PlanAchievementQuery {
  return {
    dateFrom: query.dateFrom,
    dateTo: query.dateTo,
    groupBy: query.groupBy as PlanAchievementGroupBy,
    productCd: query.productCd?.trim() || undefined,
    customerCd: query.customerCd?.trim() || undefined,
    onlyCompleted: query.onlyCompleted,
  }
}

function progressPercent(rate: number): number {
  return Math.min(100, Math.round(Number(rate || 0) * 1000) / 10)
}

function progressStatus(rate: number): 'success' | 'warning' | 'exception' | undefined {
  if (rate >= 1) return 'success'
  if (rate >= 0.8) return 'warning'
  return 'exception'
}

function barClass(rate: number): string {
  if (rate >= 1) return 'good'
  if (rate >= 0.8) return 'warn'
  return 'bad'
}

function formatPercent(rate: number): string {
  return `${(Math.round(Number(rate || 0) * 1000) / 10).toFixed(1)}%`
}

function formatNumber(value: number): string {
  return Number(value || 0).toLocaleString('ja-JP')
}

function formatQty(value: number): string {
  return Number(value || 0).toLocaleString('ja-JP', { maximumFractionDigits: 4 })
}

function addDays(date: Date, days: number): Date {
  const next = new Date(date)
  next.setDate(next.getDate() + days)
  return next
}

function formatDate(date: Date): string {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

onMounted(loadSummary)
</script>

<style scoped>
.plan-achievement {
  padding: 16px;
}
.page-header {
  align-items: center;
  display: flex;
  justify-content: space-between;
  margin-bottom: 12px;
}
.page-header h2 {
  color: #303133;
  font-size: 20px;
  font-weight: 650;
  margin: 0;
}
.filter-card,
.kpi-row,
.chart-card {
  margin-bottom: 12px;
}
.date-input {
  width: 150px;
}
.text-input {
  width: 150px;
}
.kpi-card :deep(.el-card__body) {
  padding: 14px 16px;
}
.kpi-card.warn {
  border-color: #f3d19e;
}
.kpi-label {
  color: #909399;
  font-size: 12px;
  margin-bottom: 6px;
}
.kpi-value {
  color: #303133;
  font-size: 24px;
  font-weight: 700;
  line-height: 1.1;
}
.kpi-value.rate {
  color: #2f8f63;
}
.kpi-value.good {
  color: #2f8f63;
}
.kpi-value.late,
.lateText {
  color: #c45656;
  font-weight: 650;
}
.panel-header {
  align-items: center;
  display: flex;
  justify-content: space-between;
}
.bar-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-height: 84px;
}
.bar-row {
  align-items: center;
  display: grid;
  gap: 10px;
  grid-template-columns: minmax(120px, 220px) minmax(120px, 1fr) 62px;
}
.bar-label {
  color: #303133;
  font-size: 13px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.bar-track {
  background: #edf2f7;
  border-radius: 6px;
  height: 12px;
  overflow: hidden;
}
.bar-fill {
  height: 100%;
  min-width: 2px;
}
.bar-fill.good {
  background: #2f8f63;
}
.bar-fill.warn {
  background: #d99b2b;
}
.bar-fill.bad {
  background: #c45656;
}
.bar-value {
  color: #606266;
  font-size: 12px;
  text-align: right;
}
.progress-cell {
  align-items: center;
  display: grid;
  gap: 8px;
  grid-template-columns: 1fr 54px;
}
@media (max-width: 767px) {
  .plan-achievement {
    padding: 12px;
  }
  .bar-row {
    grid-template-columns: 1fr 56px;
  }
  .bar-track {
    grid-column: 1 / -1;
    order: 3;
  }
}
</style>
