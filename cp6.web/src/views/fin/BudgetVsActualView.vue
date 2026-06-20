<template>
  <div class="budget-vs-actual">
    <div class="page-header">
      <h2>{{ t('执行分析') }}</h2>
      <span class="subtitle">{{ t('budget.vsActual.subtitle') }}</span>
    </div>

    <!-- ── 筛选工具栏 ── -->
    <el-card shadow="never" style="margin-bottom:10px">
      <div class="table-toolbar">
        <!-- 财年 -->
        <span class="toolbar-label">{{ t('budget.field.fiscalYear') }}</span>
        <el-select
          v-model="filterFiscalYear"
          size="small"
          style="width:120px"
          :placeholder="t('budget.field.fiscalYear')"
          @change="onFiscalYearChange"
        >
          <el-option
            v-for="y in fiscalYears"
            :key="y"
            :value="y"
            :label="String(y)"
          />
        </el-select>

        <!-- 版本 -->
        <span class="toolbar-label">{{ t('budget.field.version') }}</span>
        <el-select
          v-model="filterVersionId"
          size="small"
          style="width:180px"
          clearable
          :placeholder="t('budget.msg.activeVersion')"
          :disabled="!filterFiscalYear"
        >
          <el-option
            v-for="v in versionsForYear"
            :key="v.id!"
            :value="v.id!"
            :label="`V${v.versionNo} ${v.name}`"
          />
        </el-select>

        <!-- 期间区间 -->
        <span class="toolbar-label">{{ t('budget.field.periodFrom') }}</span>
        <el-select v-model="filterFrom" size="small" style="width:80px">
          <el-option v-for="m in 12" :key="m" :value="m" :label="`M${m}`" />
        </el-select>
        <span class="toolbar-sep">—</span>
        <el-select v-model="filterTo" size="small" style="width:80px">
          <el-option v-for="m in 12" :key="m" :value="m" :label="`M${m}`" />
        </el-select>

        <!-- 成本中心（客户端过滤） -->
        <span class="toolbar-label">{{ t('budget.field.costCenter') }}</span>
        <el-select
          v-model="filterCostCenterId"
          size="small"
          style="width:180px"
          clearable
          filterable
          :placeholder="t('budget.msg.allCostCenters')"
        >
          <el-option
            v-for="cc in costCenters"
            :key="cc.id"
            :value="cc.id"
            :label="`${cc.code} ${cc.name}`"
          />
        </el-select>

        <el-button type="primary" size="small" :loading="loading" @click="doQuery">{{ t('查询') }}</el-button>
        <el-button size="small" @click="doRefresh">{{ t('刷新') }}</el-button>
      </div>
    </el-card>

    <!-- ── 汇总卡片 ── -->
    <el-row :gutter="12" style="margin-bottom:10px" v-if="report">
      <el-col :span="6">
        <el-card shadow="never" class="summary-card">
          <div class="summary-label">{{ t('budget.summary.totalBudget') }}</div>
          <div class="summary-value">{{ fmtAmt(report.totalBudget) }}</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="never" class="summary-card">
          <div class="summary-label">{{ t('budget.summary.totalActual') }}</div>
          <div class="summary-value">{{ fmtAmt(report.totalActual) }}</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="never" class="summary-card">
          <div class="summary-label">{{ t('budget.summary.totalVariance') }}</div>
          <div
            class="summary-value"
            :class="{ 'text-danger': report.totalActual > report.totalBudget && report.totalBudget > 0 }"
          >
            {{ fmtAmt(report.totalBudget - report.totalActual) }}
          </div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="never" class="summary-card">
          <div class="summary-label">{{ t('budget.summary.overallRate') }}</div>
          <div
            class="summary-value"
            :class="{ 'text-danger': report.totalBudget > 0 && report.totalActual / report.totalBudget > 1 }"
          >
            {{ overallRate }}
          </div>
        </el-card>
      </el-col>
    </el-row>

    <!-- ── 主表格 ── -->
    <el-card shadow="never">
      <el-table
        :data="filteredRows"
        border
        stripe
        size="small"
        max-height="600"
        v-loading="loading"
        show-summary
        :summary-method="tableSummary"
        row-key="accountId"
      >
        <!-- 展开行：12 期明细 -->
        <el-table-column type="expand" width="36">
          <template #default="{ row }">
            <div class="period-drill">
              <table class="period-table">
                <thead>
                  <tr>
                    <th>{{ t('budget.field.period') }}</th>
                    <th v-for="m in 12" :key="m">M{{ m }}</th>
                  </tr>
                </thead>
                <tbody>
                  <tr>
                    <td class="period-row-label">{{ t('budget.field.budget') }}</td>
                    <td v-for="m in 12" :key="m" class="period-cell">
                      {{ fmtAmt(row.budgetPeriods[m - 1]) }}
                    </td>
                  </tr>
                  <tr>
                    <td class="period-row-label">{{ t('budget.field.actual') }}</td>
                    <td
                      v-for="m in 12"
                      :key="m"
                      class="period-cell"
                      :class="{ 'text-danger': !row.isUnbudgeted && row.budgetPeriods[m-1] > 0 && row.actualPeriods[m-1] > row.budgetPeriods[m-1] }"
                    >
                      {{ fmtAmt(row.actualPeriods[m - 1]) }}
                    </td>
                  </tr>
                  <tr>
                    <td class="period-row-label">{{ t('budget.field.variance') }}</td>
                    <td
                      v-for="m in 12"
                      :key="m"
                      class="period-cell"
                      :class="{ 'text-danger': !row.isUnbudgeted && row.budgetPeriods[m-1] > 0 && row.actualPeriods[m-1] > row.budgetPeriods[m-1] }"
                    >
                      {{ fmtAmt(row.budgetPeriods[m - 1] - row.actualPeriods[m - 1]) }}
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </template>
        </el-table-column>

        <!-- 科目 -->
        <el-table-column :label="t('budget.field.account')" min-width="180" show-overflow-tooltip>
          <template #default="{ row }">
            <span>{{ row.accountCode }} {{ row.accountName }}</span>
          </template>
        </el-table-column>

        <!-- 成本中心/成本对象 -->
        <el-table-column :label="t('budget.field.costCenter') + '/' + t('budget.field.costObject')" width="160" show-overflow-tooltip>
          <template #default="{ row }">
            <span v-if="row.costCenterId">
              {{ costCenterLabel(row.costCenterId) }}
              <span v-if="row.costObjectType" class="dim-text"> / {{ row.costObjectType }}/{{ row.costObjectId }}</span>
            </span>
            <span v-else class="dim-text">{{ t('budget.field.companyLevel') }}</span>
          </template>
        </el-table-column>

        <!-- 预算 -->
        <el-table-column :label="t('budget.field.budget')" width="120" align="right">
          <template #default="{ row }">
            <span v-if="row.isUnbudgeted" class="dim-text">—</span>
            <span v-else>{{ fmtAmt(row.budget) }}</span>
          </template>
        </el-table-column>

        <!-- 实际 -->
        <el-table-column :label="t('budget.field.actual')" width="120" align="right">
          <template #default="{ row }">
            {{ fmtAmt(row.actual) }}
          </template>
        </el-table-column>

        <!-- 差异 -->
        <el-table-column :label="t('budget.field.variance')" width="120" align="right">
          <template #default="{ row }">
            <span :class="rowVarianceClass(row)">
              <span v-if="row.isUnbudgeted">—</span>
              <span v-else>{{ fmtAmt(row.variance) }}</span>
            </span>
          </template>
        </el-table-column>

        <!-- 差异率 -->
        <el-table-column :label="t('budget.field.variancePct')" width="90" align="right">
          <template #default="{ row }">
            <span v-if="row.isUnbudgeted || row.variancePct == null" class="dim-text">—</span>
            <span v-else :class="rowVarianceClass(row)">
              {{ fmtPct(row.variancePct) }}
            </span>
          </template>
        </el-table-column>

        <!-- 执行率 -->
        <el-table-column :label="t('budget.field.executionRate')" width="90" align="right">
          <template #default="{ row }">
            <span v-if="row.isUnbudgeted || row.budget === 0" class="dim-text">—</span>
            <span v-else :class="row.actual > row.budget ? 'text-danger' : ''">
              {{ fmtPct(row.actual / row.budget) }}
            </span>
          </template>
        </el-table-column>

        <!-- 标签 -->
        <el-table-column :label="t('budget.field.status')" width="90" align="center">
          <template #default="{ row }">
            <el-tag v-if="row.isUnbudgeted" type="warning" size="small">{{ t('budget.tag.unbudgeted') }}</el-tag>
            <el-tag v-else-if="isOverBudget(row)" type="danger" size="small">{{ t('budget.tag.overBudget') }}</el-tag>
          </template>
        </el-table-column>
      </el-table>

      <!-- 空态 -->
      <el-empty
        v-if="!loading && !report"
        :description="t('budget.msg.noReport')"
        :image-size="80"
      />
      <el-empty
        v-else-if="!loading && report && filteredRows.length === 0"
        :description="t('budget.msg.noData')"
        :image-size="80"
      />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { TableColumnCtx } from 'element-plus'
import { budgetApi, budgetVersionApi, budgetReportApi, costCenterApi } from '@/api/fin/budget'
import type { Budget, BudgetVersion, BudgetVsActualReport, BudgetVsActualRow, CostCenter } from '@/types/fin/budget'

const { t } = useI18n()

// ── 筛选状态 ──
const filterFiscalYear = ref<number | null>(null)
const filterVersionId = ref<string | null>(null)
const filterFrom = ref(1)
const filterTo = ref(12)
const filterCostCenterId = ref<string | null>(null)

// ── 数据状态 ──
const loading = ref(false)
const budgets = ref<Budget[]>([])
const versionsForYear = ref<BudgetVersion[]>([])
const costCenters = ref<CostCenter[]>([])
const report = ref<BudgetVsActualReport | null>(null)

// ── 财年列表（从方案去重） ──
const fiscalYears = computed<number[]>(() => {
  const set = new Set<number>(budgets.value.map(b => b.fiscalYear))
  return Array.from(set).sort((a, b) => b - a) // 降序
})

// ── 客户端成本中心过滤后的行 ──
const filteredRows = computed<BudgetVsActualRow[]>(() => {
  if (!report.value) return []
  if (!filterCostCenterId.value) return report.value.rows
  return report.value.rows.filter(r => r.costCenterId === filterCostCenterId.value)
})

// ── 整体执行率 ──
const overallRate = computed<string>(() => {
  if (!report.value || report.value.totalBudget === 0) return '—'
  return fmtPct(report.value.totalActual / report.value.totalBudget)
})

// ── 加载方案列表（初始化财年下拉） ──
async function loadBudgets() {
  try {
    const res = await budgetApi.list()
    budgets.value = res?.data || []
    // 默认选中最近财年
    if (fiscalYears.value.length && !filterFiscalYear.value) {
      filterFiscalYear.value = fiscalYears.value[0] ?? null
      await loadVersionsForYear()
    }
  } catch { /* ignore */ }
}

// ── 加载某财年版本 ──
async function loadVersionsForYear() {
  versionsForYear.value = []
  filterVersionId.value = null
  if (!filterFiscalYear.value) return
  const budget = budgets.value.find(b => b.fiscalYear === filterFiscalYear.value)
  if (!budget?.id) return
  try {
    const res = await budgetVersionApi.list(budget.id)
    versionsForYear.value = res?.data || []
    // 默认选中已激活版本（不强制选）
    const active = versionsForYear.value.find(v => v.isActive)
    if (active) filterVersionId.value = active.id ?? null
  } catch { /* ignore */ }
}

// ── 加载成本中心 ──
async function loadCostCenters() {
  try {
    const res = await costCenterApi.list()
    costCenters.value = res?.data || []
  } catch { /* ignore */ }
}

// ── 财年变更 ──
async function onFiscalYearChange() {
  report.value = null
  await loadVersionsForYear()
}

// ── 查询 ──
async function doQuery() {
  if (!filterFiscalYear.value) return
  loading.value = true
  report.value = null
  try {
    const res = await budgetReportApi.vsActual({
      fiscalYear: filterFiscalYear.value,
      versionId: filterVersionId.value || null,
      from: filterFrom.value,
      to: filterTo.value,
    })
    report.value = res?.data || null
  } finally {
    loading.value = false
  }
}

// ── 刷新 ──
async function doRefresh() {
  await doQuery()
}

// ── 表格合计行 ──
function tableSummary({ columns }: { columns: TableColumnCtx<BudgetVsActualRow>[] }): string[] {
  const rows = filteredRows.value
  const out: string[] = []
  columns.forEach((_c, i) => {
    if (i === 0) { out.push(''); return }                           // expand column
    if (i === 1) { out.push(t('合计')); return }                    // 科目
    if (i === 2) { out.push(''); return }                           // 成本中心
    if (i === 3) { out.push(fmtAmt(rows.reduce((s, r) => s + (r.isUnbudgeted ? 0 : r.budget), 0))); return }  // 预算
    if (i === 4) { out.push(fmtAmt(rows.reduce((s, r) => s + r.actual, 0))); return }                         // 实际
    if (i === 5) { out.push(fmtAmt(rows.reduce((s, r) => s + (r.isUnbudgeted ? 0 : r.variance), 0))); return } // 差异
    out.push('')
  })
  return out
}

// ── 成本中心标签 ──
function costCenterLabel(id?: string | null): string {
  if (!id) return ''
  const cc = costCenters.value.find(x => x.id === id)
  return cc ? `${cc.code} ${cc.name}` : id
}

// ── 超支判断（费用超预算：actual > budget & 有预算）──
function isOverBudget(row: BudgetVsActualRow): boolean {
  return !row.isUnbudgeted && row.budget > 0 && row.actual > row.budget
}

// ── 差异着色 class ──
function rowVarianceClass(row: BudgetVsActualRow): string {
  if (isOverBudget(row)) return 'text-danger'
  return ''
}

// ── 格式化 ──
function fmtAmt(v?: number | null): string {
  if (v == null) return '0.00'
  return v.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

function fmtPct(v?: number | null): string {
  if (v == null) return '—'
  return (v * 100).toFixed(1) + '%'
}

onMounted(async () => {
  await Promise.all([loadBudgets(), loadCostCenters()])
})
</script>

<style scoped>
.budget-vs-actual { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }

.table-toolbar { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.toolbar-label { font-size: 12px; color: #606266; white-space: nowrap; }
.toolbar-sep { color: #909399; }

/* 汇总卡片 */
.summary-card :deep(.el-card__body) { padding: 16px 20px; }
.summary-label { font-size: 12px; color: #909399; margin-bottom: 6px; }
.summary-value { font-size: 20px; font-weight: 650; color: #303133; }

/* 着色 */
.text-danger { color: #f56c6c; }
.dim-text { color: #c0c4cc; }

/* 12期展开内表 */
.period-drill { padding: 8px 16px 12px; background: #fafafa; }
.period-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 11px;
}
.period-table th,
.period-table td {
  border: 1px solid #ebeef5;
  padding: 4px 8px;
  text-align: right;
  white-space: nowrap;
}
.period-table th { background: #f5f7fa; color: #606266; font-weight: 600; }
.period-table th:first-child,
.period-table td:first-child { text-align: left; }
.period-row-label { font-weight: 500; color: #606266; background: #f5f7fa; }
.period-cell { color: #303133; }
</style>
