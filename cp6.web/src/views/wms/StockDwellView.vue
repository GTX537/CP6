<template>
  <div class="stock-dwell">
    <div class="page-header">
      <h2>{{ t('wms.stockDwell.title') }}</h2>
    </div>

    <el-card shadow="never" class="search-card">
      <el-form :model="query" inline size="small">
        <el-form-item :label="t('wms.stockDwell.filter.groupBy')">
          <el-radio-group v-model="query.groupBy">
            <el-radio-button label="product">{{ t('wms.stockDwell.groupBy.product') }}</el-radio-button>
            <el-radio-button label="customer">{{ t('wms.stockDwell.groupBy.customer') }}</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item :label="t('wms.common.warehouse')">
          <el-input v-model="query.warehouseCd" clearable style="width: 130px" />
        </el-form-item>
        <el-form-item :label="t('wms.common.product')">
          <el-input v-model="query.productCd" clearable style="width: 150px" />
        </el-form-item>
        <el-form-item :label="t('wms.stockDwell.filter.owner')">
          <el-input v-model="query.ownerCd" clearable style="width: 150px" />
        </el-form-item>
        <el-form-item :label="t('wms.stockDwell.filter.asOfDate')">
          <el-date-picker
            v-model="query.asOfDate"
            type="date"
            value-format="YYYY-MM-DD"
            style="width: 150px"
          />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" :loading="loading" @click="load">
            {{ t('wms.stockDwell.btn.search') }}
          </el-button>
          <el-button :icon="Refresh" @click="resetQuery">
            {{ t('wms.stockDwell.btn.reset') }}
          </el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-row :gutter="12" class="kpi-row">
      <el-col :xs="12" :sm="6" :md="6">
        <el-card shadow="never" class="kpi-card">
          <div class="kpi-label">{{ t('wms.stockDwell.kpi.totalQty') }}</div>
          <div class="kpi-value">{{ formatQty(summary?.totalQty) }}</div>
        </el-card>
      </el-col>
      <el-col :xs="12" :sm="6" :md="6">
        <el-card shadow="never" class="kpi-card danger">
          <div class="kpi-label">{{ t('wms.stockDwell.kpi.over90Qty') }}</div>
          <div class="kpi-value">{{ formatQty(summary?.over90Qty) }}</div>
        </el-card>
      </el-col>
      <el-col :xs="12" :sm="6" :md="6">
        <el-card shadow="never" class="kpi-card warning">
          <div class="kpi-label">{{ t('wms.stockDwell.kpi.over90Rate') }}</div>
          <div class="kpi-value">{{ formatPercent(over90Rate) }}</div>
        </el-card>
      </el-col>
      <el-col :xs="12" :sm="6" :md="6">
        <el-card shadow="never" class="kpi-card value">
          <div class="kpi-label">{{ t('wms.stockDwell.kpi.totalValue') }}</div>
          <div class="kpi-value">{{ formatAmount(summary?.totalValue) }}</div>
        </el-card>
      </el-col>
    </el-row>

    <el-card shadow="never" class="chart-card">
      <template #header>
        <div class="panel-header">
          <span>{{ t('wms.stockDwell.chart.title') }}</span>
          <CpTag tone="info">{{ t('wms.stockDwell.filter.asOfDate') }}: {{ formatDate(summary?.asOfDate) }}</CpTag>
        </div>
      </template>

      <div v-if="barRows.length" class="bucket-list">
        <div v-for="row in barRows" :key="row.groupKey" class="bucket-row">
          <div class="bucket-label">
            <span>{{ row.groupLabel }}</span>
            <strong>{{ formatQty(row.totalQty) }}</strong>
          </div>
          <div class="bucket-bar" :aria-label="row.groupLabel">
            <span
              v-if="row.bucket0To30Qty > 0"
              class="bucket-segment b0"
              :style="segmentStyle(row.bucket0To30Qty, row.totalQty)"
            />
            <span
              v-if="row.bucket31To60Qty > 0"
              class="bucket-segment b1"
              :style="segmentStyle(row.bucket31To60Qty, row.totalQty)"
            />
            <span
              v-if="row.bucket61To90Qty > 0"
              class="bucket-segment b2"
              :style="segmentStyle(row.bucket61To90Qty, row.totalQty)"
            />
            <span
              v-if="row.bucketOver90Qty > 0"
              class="bucket-segment b3"
              :style="segmentStyle(row.bucketOver90Qty, row.totalQty)"
            />
          </div>
        </div>
      </div>
      <CpEmpty v-else :text="t('wms.stockDwell.empty')" />
    </el-card>

    <el-card shadow="never" class="table-card">
      <div class="table-toolbar">
        <CpTag tone="info">{{ t('wms.common.total') }}: {{ rows.length }}</CpTag>
      </div>

      <el-table
        v-if="!isMobile"
        :data="rows"
        border
        stripe
        size="small"
        max-height="560"
        v-loading="loading"
      >
        <el-table-column prop="groupLabel" :label="t('wms.stockDwell.col.group')" min-width="180" show-overflow-tooltip />
        <el-table-column :label="t('wms.stockDwell.col.totalQty')" width="120" align="right">
          <template #default="{ row }">{{ formatQty(row.totalQty) }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.stockDwell.col.totalValue')" width="130" align="right">
          <template #default="{ row }">{{ formatAmount(row.totalValue) }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.stockDwell.bucket.b0')" width="105" align="right">
          <template #default="{ row }">{{ formatQty(row.bucket0To30Qty) }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.stockDwell.bucket.b1')" width="105" align="right">
          <template #default="{ row }">{{ formatQty(row.bucket31To60Qty) }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.stockDwell.bucket.b2')" width="105" align="right">
          <template #default="{ row }">{{ formatQty(row.bucket61To90Qty) }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.stockDwell.bucket.b3')" width="105" align="right">
          <template #default="{ row }">
            <span class="over90-text">{{ formatQty(row.bucketOver90Qty) }}</span>
          </template>
        </el-table-column>
        <el-table-column :label="t('wms.stockDwell.col.oldest')" width="150">
          <template #default="{ row }">{{ formatDate(row.oldestReceiveDate) }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.stockDwell.col.oldestAge')" width="120" align="right">
          <template #default="{ row }">
            <CpTag :tone="row.oldestAgeDays > 90 ? 'danger' : 'info'">
              {{ row.oldestAgeDays }}
            </CpTag>
          </template>
        </el-table-column>
      </el-table>

      <div v-else class="mobile-list" v-loading="loading">
        <div v-for="row in rows" :key="row.groupKey" class="mobile-row">
          <div class="mobile-main">
            <span>{{ row.groupLabel }}</span>
            <strong>{{ formatQty(row.totalQty) }}</strong>
          </div>
          <div class="mobile-meta">
            <span>{{ t('wms.stockDwell.bucket.b3') }}: <b>{{ formatQty(row.bucketOver90Qty) }}</b></span>
            <span>{{ t('wms.stockDwell.col.oldestAge') }}: {{ row.oldestAgeDays }}</span>
          </div>
        </div>
        <CpEmpty v-if="!rows.length && !loading" :text="t('wms.stockDwell.empty')" />
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Refresh, Search } from '@element-plus/icons-vue'
import CpTag from '@/components/base/CpTag.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'
import { stockDwellApi } from '@/api/wms/stockDwell'
import { useBreakpoint } from '@/composables/useBreakpoint'
import type { StockDwellQuery, StockDwellRow, StockDwellSummary } from '@/types/wms/stockDwell'
import { formatQty as fmtQty } from '@/utils/format'

const { t } = useI18n()
const { isMobile } = useBreakpoint()

const query = reactive<StockDwellQuery>({
  groupBy: 'product',
  asOfDate: todayString(),
})
const summary = ref<StockDwellSummary | null>(null)
const loading = ref(false)

const rows = computed<StockDwellRow[]>(() => summary.value?.rows ?? [])
const barRows = computed(() => rows.value.slice(0, 8))
const over90Rate = computed(() => {
  const total = Number(summary.value?.totalQty ?? 0)
  return total > 0 ? Number(summary.value?.over90Qty ?? 0) / total : 0
})

function todayString(): string {
  const d = new Date()
  const year = d.getFullYear()
  const month = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

function clean(value?: string) {
  const v = value?.trim()
  return v ? v : undefined
}

function payload(): StockDwellQuery {
  return {
    groupBy: query.groupBy,
    warehouseCd: clean(query.warehouseCd),
    productCd: clean(query.productCd),
    ownerCd: clean(query.ownerCd),
    asOfDate: query.asOfDate || todayString(),
  }
}

async function load() {
  loading.value = true
  try {
    const res = await stockDwellApi.summary(payload())
    summary.value = res.data
  } finally {
    loading.value = false
  }
}

function resetQuery() {
  query.groupBy = 'product'
  query.warehouseCd = undefined
  query.productCd = undefined
  query.ownerCd = undefined
  query.asOfDate = todayString()
  load()
}

function segmentStyle(qty: number, total: number) {
  const pct = total > 0 ? (qty / total) * 100 : 0
  return { flexBasis: `${pct}%` }
}

function formatQty(n: number | null | undefined): string {
  if (n == null) return '-'
  return fmtQty(n, 4)
}

function formatAmount(n: number | null | undefined): string {
  if (n == null) return '-'
  return fmtQty(n, 0)
}

function formatPercent(n: number): string {
  return `${(n * 100).toFixed(1)}%`
}

function formatDate(value?: string | null): string {
  return value ? value.slice(0, 10) : '-'
}

onMounted(load)
</script>

<style scoped>
.stock-dwell {
  padding: 12px;
}

.page-header {
  margin-bottom: 12px;
}

.page-header h2 {
  margin: 0;
  color: var(--cp-ink);
  font-size: 20px;
  font-weight: 650;
  line-height: 1.3;
}

.search-card,
.kpi-row,
.chart-card {
  margin-bottom: 12px;
}

.kpi-card {
  min-height: 86px;
  border-left: 4px solid var(--cp-info);
}

.kpi-card.danger {
  border-left-color: var(--cp-danger);
}

.kpi-card.warning {
  border-left-color: var(--cp-warn);
}

.kpi-card.value {
  border-left-color: var(--cp-ok);
}

.kpi-label {
  color: var(--cp-muted);
  font-size: 12px;
  margin-bottom: 8px;
}

.kpi-value {
  color: var(--cp-ink);
  font-size: 24px;
  font-weight: 650;
  line-height: 1.15;
  word-break: break-word;
}

.panel-header,
.table-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.bucket-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.bucket-row {
  display: grid;
  grid-template-columns: minmax(150px, 220px) 1fr;
  gap: 12px;
  align-items: center;
}

.bucket-label {
  min-width: 0;
  display: flex;
  justify-content: space-between;
  gap: 8px;
  color: var(--cp-ink);
  font-size: 13px;
}

.bucket-label span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.bucket-label strong {
  color: var(--cp-muted);
  font-weight: 600;
  flex-shrink: 0;
}

.bucket-bar {
  height: 18px;
  display: flex;
  overflow: hidden;
  border-radius: 6px;
  background: var(--cp-line-soft);
}

.bucket-segment {
  min-width: 3px;
}

/* 滞留日数バケット＝意味づけ色（新鮮→期限超過）。設計トークンに直接対応するため §2.5 図表免除は使わずトークン化 */
.bucket-segment.b0 {
  background: var(--cp-ok);
}

.bucket-segment.b1 {
  background: var(--cp-info);
}

.bucket-segment.b2 {
  background: var(--cp-warn);
}

.bucket-segment.b3 {
  background: var(--cp-danger);
}

.over90-text {
  color: var(--cp-danger);
  font-weight: 650;
}

.mobile-list {
  display: flex;
  flex-direction: column;
}

.mobile-row {
  padding: 12px 0;
  border-bottom: 1px solid var(--cp-line);
}

.mobile-row:last-child {
  border-bottom: none;
}

.mobile-main,
.mobile-meta {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  align-items: center;
}

.mobile-main {
  color: var(--cp-ink);
  font-size: 14px;
  margin-bottom: 6px;
}

.mobile-main span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.mobile-main strong {
  flex-shrink: 0;
}

.mobile-meta {
  color: var(--cp-muted);
  font-size: 12px;
}

@media (max-width: 767px) {
  .stock-dwell {
    padding: 8px;
  }

  .bucket-row {
    grid-template-columns: 1fr;
    gap: 6px;
  }

  .kpi-value {
    font-size: 20px;
  }
}
</style>
