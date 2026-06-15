<template>
  <div class="income-statement">
    <div class="page-header">
      <h2>{{ t('损益表') }}</h2>
      <span class="subtitle">{{ t('一段时间赚了多少：取收入/成本/费用类本期发生额') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-select v-model="periodId" size="small" style="width: 240px" :placeholder="t('选择会计期间')" @change="build">
          <el-option v-for="p in periods" :key="p.id" :value="p.id" :label="periodLabel(p)" />
        </el-select>
        <el-button size="small" @click="build" :disabled="!periodId">{{ t('刷新') }}</el-button>
      </div>

      <div v-if="pnl" v-loading="loading" class="pnl-summary">
        <div class="pnl-row"><span>{{ t('营业收入') }}</span><b>{{ fmtAmt(pnl.revenue) }}</b></div>
        <div class="pnl-row sub"><span>{{ t('减：营业成本') }}</span><span>{{ fmtAmt(pnl.cost) }}</span></div>
        <div class="pnl-row strong"><span>{{ t('毛利') }}</span><b>{{ fmtAmt(pnl.grossProfit) }}</b></div>
        <div class="pnl-row sub"><span>{{ t('减：营业费用') }}</span><span>{{ fmtAmt(pnl.operatingExpense) }}</span></div>
        <div class="pnl-row strong net"><span>{{ t('净利润') }}</span><b>{{ fmtAmt(pnl.netProfit) }}</b></div>
      </div>

      <el-row :gutter="12" v-if="pnl" class="detail">
        <el-col :span="8">
          <div class="col-title">{{ t('收入明细') }}</div>
          <el-table :data="pnl.revenueLines" border stripe size="small" max-height="360">
            <el-table-column prop="code" :label="t('编码')" width="90" />
            <el-table-column prop="name" :label="t('科目名称')" min-width="120" show-overflow-tooltip />
            <el-table-column :label="t('金额')" width="130" align="right">
              <template #default="{ row }">{{ fmtAmt(row.amount) }}</template>
            </el-table-column>
          </el-table>
        </el-col>
        <el-col :span="8">
          <div class="col-title">{{ t('成本明细') }}</div>
          <el-table :data="pnl.costLines" border stripe size="small" max-height="360">
            <el-table-column prop="code" :label="t('编码')" width="90" />
            <el-table-column prop="name" :label="t('科目名称')" min-width="120" show-overflow-tooltip />
            <el-table-column :label="t('金额')" width="130" align="right">
              <template #default="{ row }">{{ fmtAmt(row.amount) }}</template>
            </el-table-column>
          </el-table>
        </el-col>
        <el-col :span="8">
          <div class="col-title">{{ t('费用明细') }}</div>
          <el-table :data="pnl.expenseLines" border stripe size="small" max-height="360">
            <el-table-column prop="code" :label="t('编码')" width="90" />
            <el-table-column prop="name" :label="t('科目名称')" min-width="120" show-overflow-tooltip />
            <el-table-column :label="t('金额')" width="130" align="right">
              <template #default="{ row }">{{ fmtAmt(row.amount) }}</template>
            </el-table-column>
          </el-table>
        </el-col>
      </el-row>
      <el-empty v-if="!pnl" :description="t('选择会计期间')" :image-size="80" />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { periodApi, reportApi } from '@/api/fin/fin'
import type { IncomeStatement, FiscalPeriod } from '@/types/fin/fin'

const { t } = useI18n()

const periods = ref<FiscalPeriod[]>([])
const periodId = ref<string>('')
const pnl = ref<IncomeStatement | null>(null)
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
    const res = await reportApi.incomeStatement(periodId.value)
    pnl.value = res?.data || null
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
.income-statement { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; }
.pnl-summary { max-width: 520px; margin: 4px 0 16px; }
.pnl-row { display: flex; justify-content: space-between; padding: 8px 12px; font-size: 13px; border-bottom: 1px solid #ebeef5; }
.pnl-row.sub { color: #909399; }
.pnl-row.strong { background: #f5f7fa; font-weight: 600; }
.pnl-row.net b { color: #67c23a; font-size: 15px; }
.col-title { font-weight: 600; color: #303133; margin: 4px 0 6px; }
.detail { margin-top: 8px; }
</style>
