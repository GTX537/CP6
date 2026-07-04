<template>
  <div class="otd-report">
    <div class="page-header">
      <h2>{{ t('erp.otdReport.title') }}</h2>
      <el-button :icon="Refresh" circle :loading="loading" @click="loadSummary" />
    </div>

    <el-card shadow="never" class="filter-card">
      <el-form :model="query" inline size="small" @submit.prevent>
        <el-form-item :label="t('erp.otdReport.filter.dateFrom')">
          <el-date-picker
            v-model="query.dateFrom"
            type="date"
            value-format="YYYY-MM-DD"
            clearable
            class="date-input"
          />
        </el-form-item>
        <el-form-item :label="t('erp.otdReport.filter.dateTo')">
          <el-date-picker
            v-model="query.dateTo"
            type="date"
            value-format="YYYY-MM-DD"
            clearable
            class="date-input"
          />
        </el-form-item>
        <el-form-item :label="t('erp.otdReport.filter.groupBy.label')">
          <el-radio-group v-model="query.groupBy">
            <el-radio-button label="customer">{{ t('erp.otdReport.filter.groupBy.customer') }}</el-radio-button>
            <el-radio-button label="month">{{ t('erp.otdReport.filter.groupBy.month') }}</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item :label="t('erp.otdReport.filter.customer')">
          <el-input v-model="query.customerCd" clearable class="customer-input" @keyup.enter="search" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" :loading="loading" @click="search">
            {{ t('erp.otdReport.btn.search') }}
          </el-button>
          <el-button :icon="Download" :loading="exporting" @click="exportCsv">
            {{ t('erp.otdReport.btn.exportCsv') }}
          </el-button>
          <el-button :icon="RefreshLeft" @click="resetQuery">
            {{ t('erp.otdReport.btn.reset') }}
          </el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-row :gutter="12" class="kpi-row">
      <el-col :xs="24" :sm="8">
        <el-card shadow="never" class="kpi-card">
          <div class="kpi-label">{{ t('erp.otdReport.kpi.overallRate') }}</div>
          <div class="kpi-value rate">{{ formatPercent(summary.onTimeRate) }}</div>
        </el-card>
      </el-col>
      <el-col :xs="24" :sm="8">
        <el-card shadow="never" class="kpi-card">
          <div class="kpi-label">{{ t('erp.otdReport.kpi.totalShipped') }}</div>
          <div class="kpi-value">{{ formatNumber(summary.totalShippedOrders) }}</div>
        </el-card>
      </el-col>
      <el-col :xs="24" :sm="8">
        <el-card shadow="never" class="kpi-card" :class="{ warn: summary.lateCount > 0 }">
          <div class="kpi-label">{{ t('erp.otdReport.kpi.lateCount') }}</div>
          <div class="kpi-value late">{{ formatNumber(summary.lateCount) }}</div>
        </el-card>
      </el-col>
    </el-row>

    <el-card shadow="never" class="chart-card">
      <template #header>
        <div class="panel-header">
          <span>{{ t('erp.otdReport.chart.title') }}</span>
          <CpTag tone="info">{{ rows.length }}</CpTag>
        </div>
      </template>
      <div v-loading="loading" class="bar-list">
        <el-empty v-if="!rows.length && !loading" :image-size="80" />
        <div v-for="row in chartRows" :key="row.groupKey" class="bar-row">
          <div class="bar-label" :title="row.groupLabel">{{ row.groupLabel }}</div>
          <div class="bar-track">
            <div class="bar-fill" :class="barClass(row.onTimeRate)" :style="{ width: progressPercent(row.onTimeRate) + '%' }" />
          </div>
          <div class="bar-value">{{ formatPercent(row.onTimeRate) }}</div>
        </div>
      </div>
    </el-card>

    <el-card shadow="never" class="table-card">
      <el-table :data="rows" border stripe size="small" max-height="620" v-loading="loading">
        <el-table-column prop="groupLabel" :label="t('erp.otdReport.col.group')" min-width="180" show-overflow-tooltip />
        <el-table-column prop="totalShippedOrders" :label="t('erp.otdReport.col.total')" width="110" align="right" />
        <el-table-column prop="onTimeCount" :label="t('erp.otdReport.col.onTime')" width="110" align="right" />
        <el-table-column prop="lateCount" :label="t('erp.otdReport.col.late')" width="110" align="right">
          <template #default="{ row }">
            <span :class="{ lateText: row.lateCount > 0 }">{{ row.lateCount }}</span>
          </template>
        </el-table-column>
        <el-table-column :label="t('erp.otdReport.col.onTimeRate')" min-width="180" align="right">
          <template #default="{ row }">
            <div class="progress-cell">
              <el-progress
                :percentage="progressPercent(row.onTimeRate)"
                :status="progressStatus(row.onTimeRate)"
                :stroke-width="10"
              />
              <span>{{ formatPercent(row.onTimeRate) }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="avgLateDays" :label="t('erp.otdReport.col.avgLateDays')" width="140" align="right">
          <template #default="{ row }">{{ formatDays(row.avgLateDays) }}</template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { Download, Refresh, RefreshLeft, Search } from '@element-plus/icons-vue'
import { otdReportApi } from '@/api/erp/otdReport'
import { formatQty } from '@/utils/format'
import CpTag from '@/components/base/CpTag.vue'
import type { OtdReportGroupBy, OtdReportQuery, OtdReportRow, OtdReportSummary } from '@/types/erp/otdReport'

const { t } = useI18n()

const query = reactive<OtdReportQuery>({
  dateFrom: formatDate(addDays(new Date(), -90)),
  dateTo: formatDate(new Date()),
  groupBy: 'customer',
})
const summary = reactive<OtdReportSummary>({
  totalShippedOrders: 0,
  onTimeCount: 0,
  lateCount: 0,
  onTimeRate: 0,
  rows: [],
})
const loading = ref(false)
const exporting = ref(false)

const rows = computed(() => summary.rows || [])
const chartRows = computed(() => rows.value.slice(0, 12))

async function loadSummary() {
  loading.value = true
  try {
    const res = await otdReportApi.summary(toPayload())
    Object.assign(summary, {
      totalShippedOrders: res.data?.totalShippedOrders ?? 0,
      onTimeCount: res.data?.onTimeCount ?? 0,
      lateCount: res.data?.lateCount ?? 0,
      onTimeRate: res.data?.onTimeRate ?? 0,
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
  query.groupBy = 'customer'
  query.customerCd = undefined
  loadSummary()
}

async function exportCsv() {
  exporting.value = true
  try {
    const blob = await otdReportApi.exportCsv(toPayload()) as unknown as Blob
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `otd-report_${new Date().toISOString().slice(0, 10)}.csv`
    a.click()
    URL.revokeObjectURL(url)
    ElMessage.success(t('erp.otdReport.msg.exported'))
  } finally {
    exporting.value = false
  }
}

function toPayload(): OtdReportQuery {
  return {
    dateFrom: query.dateFrom,
    dateTo: query.dateTo,
    groupBy: query.groupBy as OtdReportGroupBy,
    customerCd: query.customerCd?.trim() || undefined,
  }
}

function progressPercent(rate: number): number {
  return Math.round(Number(rate || 0) * 1000) / 10
}

function progressStatus(rate: number): 'success' | 'warning' | 'exception' | undefined {
  if (rate >= 0.95) return 'success'
  if (rate >= 0.8) return 'warning'
  return 'exception'
}

function barClass(rate: number): string {
  if (rate >= 0.95) return 'good'
  if (rate >= 0.8) return 'warn'
  return 'bad'
}

function formatPercent(rate: number): string {
  return `${progressPercent(rate).toFixed(1)}%`
}

function formatNumber(value: number): string {
  return formatQty(value || 0)
}

function formatDays(value: number): string {
  return formatQty(value || 0, 2)
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
.otd-report {
  padding: 16px;
}

.page-header {
  align-items: center;
  display: flex;
  justify-content: space-between;
  margin-bottom: 12px;
}

.page-header h2 {
  color: var(--cp-ink);
  font-size: 20px;
  font-weight: 650;
  line-height: 1.3;
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

.customer-input {
  width: 150px;
}

.kpi-card :deep(.el-card__body) {
  padding: 14px 16px;
}

.kpi-card.warn {
  border-color: var(--cp-warn);
}

.kpi-label {
  color: var(--cp-muted);
  font-size: 12px;
  line-height: 1.2;
  margin-bottom: 6px;
}

.kpi-value {
  color: var(--cp-ink);
  font-size: 24px;
  font-weight: 700;
  line-height: 1.1;
}

.kpi-value.rate {
  color: var(--cp-ok);
}

.kpi-value.late,
.lateText {
  color: var(--cp-danger);
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
  color: var(--cp-ink);
  font-size: 13px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.bar-track {
  background: var(--cp-line-soft);
  border-radius: 6px;
  height: 12px;
  overflow: hidden;
}

.bar-fill {
  height: 100%;
  min-width: 2px;
}

.bar-fill.good {
  background: var(--cp-ok);
}

.bar-fill.warn {
  background: var(--cp-warn);
}

.bar-fill.bad {
  background: var(--cp-danger);
}

.bar-value {
  color: var(--cp-text);
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
  .otd-report {
    padding: 12px;
  }

  .filter-card :deep(.el-card__body),
  .chart-card :deep(.el-card__body),
  .table-card :deep(.el-card__body) {
    padding: 12px;
  }

  .filter-card :deep(.el-form-item) {
    display: flex;
    flex-direction: column;
    align-items: stretch;
    margin-right: 0;
    margin-bottom: 12px;
    width: 100%;
  }

  .filter-card :deep(.el-form-item__label) {
    height: auto;
    justify-content: flex-start;
    line-height: 20px;
    margin-bottom: 4px;
  }

  .filter-card :deep(.el-form-item__content),
  .date-input,
  .customer-input {
    width: 100% !important;
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
