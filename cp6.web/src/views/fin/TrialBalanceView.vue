<template>
  <div class="trial-balance">
    <div class="page-header">
      <h2>{{ t('试算平衡表') }}</h2>
      <span class="subtitle">{{ t('期初（含历史）+ 本期发生 + 期末，两层平衡校验') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-select v-model="periodId" size="small" style="width: 220px" :placeholder="t('选择会计期间')" @change="build">
          <el-option v-for="p in periods" :key="p.id" :value="p.id" :label="periodLabel(p)" />
        </el-select>
        <el-button size="small" @click="build" :disabled="!periodId">{{ t('刷新') }}</el-button>
        <template v-if="tb">
          <el-tag :type="tb.movementBalanced ? 'success' : 'danger'" size="small">
            {{ t('本期借贷') }}: {{ tb.movementBalanced ? t('平') : t('不平') }}
          </el-tag>
          <el-tag :type="tb.closingBalanced ? 'success' : 'danger'" size="small">
            {{ t('期末借贷') }}: {{ tb.closingBalanced ? t('平') : t('不平') }}
          </el-tag>
        </template>
      </div>

      <el-table :data="tb?.rows || []" border stripe size="small" max-height="620" v-loading="loading" show-summary :summary-method="summary">
        <el-table-column prop="code" :label="t('编码')" width="110" />
        <el-table-column prop="name" :label="t('科目名称')" min-width="180" show-overflow-tooltip />
        <el-table-column :label="t('期初余额')" width="150" align="right">
          <template #default="{ row }">{{ fmtAmt(row.openBal) }}</template>
        </el-table-column>
        <el-table-column :label="t('本期借方')" width="150" align="right">
          <template #default="{ row }">{{ fmtAmt(row.periodDebit) }}</template>
        </el-table-column>
        <el-table-column :label="t('本期贷方')" width="150" align="right">
          <template #default="{ row }">{{ fmtAmt(row.periodCredit) }}</template>
        </el-table-column>
        <el-table-column :label="t('期末余额')" width="150" align="right">
          <template #default="{ row }">{{ fmtAmt(row.closeBal) }}</template>
        </el-table-column>
        <el-table-column :label="t('方向')" width="70" align="center">
          <template #default="{ row }">{{ t(ACCOUNT_SIDE_LABEL[row.normalSide] || '') }}</template>
        </el-table-column>
      </el-table>
      <el-empty v-if="tb && !tb.rows.length" :description="t('本期间无已过账凭证')" :image-size="80" />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { TableColumnCtx } from 'element-plus'
import { periodApi, trialBalanceApi } from '@/api/fin/fin'
import { ACCOUNT_SIDE_LABEL, type FiscalPeriod, type TrialBalance, type TrialBalanceRow } from '@/types/fin/fin'

const { t } = useI18n()

const periods = ref<FiscalPeriod[]>([])
const periodId = ref<string>('')
const tb = ref<TrialBalance | null>(null)
const loading = ref(false)

async function loadPeriods() {
  const res = await periodApi.list()
  periods.value = res?.data || []
  const first = periods.value[0]
  if (first && !periodId.value) {
    periodId.value = first.id
    await build()
  }
}

async function build() {
  if (!periodId.value) return
  loading.value = true
  try {
    const res = await trialBalanceApi.build(periodId.value)
    tb.value = res?.data || null
  } finally {
    loading.value = false
  }
}

function summary({ columns }: { columns: TableColumnCtx<TrialBalanceRow>[] }): string[] {
  const out: string[] = []
  const rowsv = tb.value?.rows || []
  columns.forEach((_c, i) => {
    if (i === 0) { out.push(t('合计')); return }
    if (i === 3) { out.push(fmtAmt(rowsv.reduce((s, r) => s + r.periodDebit, 0))); return }
    if (i === 4) { out.push(fmtAmt(rowsv.reduce((s, r) => s + r.periodCredit, 0))); return }
    out.push('')
  })
  return out
}

function periodLabel(p: FiscalPeriod): string {
  return `${p.year}-${String(p.month).padStart(2, '0')} (FY${p.fiscalYear} P${p.periodNo}) ${p.status === 1 ? t('已结账') : t('开启')}`
}
function fmtAmt(v: number): string { return (v || 0).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) }

onMounted(loadPeriods)
</script>

<style scoped>
.trial-balance { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; }
</style>
