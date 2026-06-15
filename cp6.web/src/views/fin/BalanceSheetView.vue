<template>
  <div class="balance-sheet">
    <div class="page-header">
      <h2>{{ t('资产负债表') }}</h2>
      <span class="subtitle">{{ t('某时点家底：资产 = 负债 + 所有者权益（含本年利润）') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-select v-model="periodId" size="small" style="width: 240px" :placeholder="t('选择会计期间')" @change="build">
          <el-option v-for="p in periods" :key="p.id" :value="p.id" :label="periodLabel(p)" />
        </el-select>
        <el-button size="small" @click="build" :disabled="!periodId">{{ t('刷新') }}</el-button>
        <el-tag v-if="bs" :type="bs.isBalanced ? 'success' : 'danger'" size="small">
          {{ t('平衡校验') }}: {{ bs.isBalanced ? t('平') : t('不平') }}
        </el-tag>
      </div>

      <el-row :gutter="12" v-loading="loading">
        <el-col :span="12">
          <div class="col-title">{{ t('资产') }}</div>
          <el-table :data="bs?.assets || []" border stripe size="small" max-height="520">
            <el-table-column prop="code" :label="t('编码')" width="110" />
            <el-table-column prop="name" :label="t('科目名称')" min-width="150" show-overflow-tooltip />
            <el-table-column :label="t('期末余额')" width="160" align="right">
              <template #default="{ row }">{{ fmtAmt(row.amount) }}</template>
            </el-table-column>
          </el-table>
          <div class="total-row"><span>{{ t('资产合计') }}</span><b>{{ fmtAmt(bs?.totalAssets) }}</b></div>
        </el-col>

        <el-col :span="12">
          <div class="col-title">{{ t('负债与所有者权益') }}</div>
          <el-table :data="liabEquityRows" border stripe size="small" max-height="520">
            <el-table-column prop="code" :label="t('编码')" width="110" />
            <el-table-column prop="name" :label="t('科目名称')" min-width="150" show-overflow-tooltip>
              <template #default="{ row }">{{ row.code ? row.name : t(row.name) }}</template>
            </el-table-column>
            <el-table-column :label="t('期末余额')" width="160" align="right">
              <template #default="{ row }">{{ fmtAmt(row.amount) }}</template>
            </el-table-column>
          </el-table>
          <div class="total-row"><span>{{ t('负债与权益合计') }}</span><b>{{ fmtAmt(bs?.totalLiabEquity) }}</b></div>
        </el-col>
      </el-row>
      <el-empty v-if="!bs" :description="t('选择会计期间')" :image-size="80" />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { periodApi, reportApi } from '@/api/fin/fin'
import type { BalanceSheet, FinReportLine, FiscalPeriod } from '@/types/fin/fin'

const { t } = useI18n()

const periods = ref<FiscalPeriod[]>([])
const periodId = ref<string>('')
const bs = ref<BalanceSheet | null>(null)
const loading = ref(false)

// 负债 + 权益 + 本年利润（并入权益侧，name 走 t() 翻译）
const liabEquityRows = computed<FinReportLine[]>(() => {
  if (!bs.value) return []
  const rows = [...bs.value.liabilities, ...bs.value.equity]
  rows.push({ code: '', name: '本年利润', amount: bs.value.currentProfit })
  return rows
})

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
    const res = await reportApi.balanceSheet(periodId.value)
    bs.value = res?.data || null
  } finally {
    loading.value = false
  }
}

function periodLabel(p: FiscalPeriod): string {
  return `${p.year}-${String(p.month).padStart(2, '0')} (FY${p.fiscalYear} P${p.periodNo}) ${p.status === 1 ? t('已结账') : t('开启')}`
}
function fmtAmt(v?: number): string { return (v || 0).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) }

onMounted(loadPeriods)
</script>

<style scoped>
.balance-sheet { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; }
.col-title { font-weight: 600; color: #303133; margin: 4px 0 6px; }
.total-row { display: flex; justify-content: space-between; padding: 8px 12px; margin-top: 4px; background: #f5f7fa; border-radius: 4px; font-size: 13px; }
.total-row b { color: #303133; }
</style>
