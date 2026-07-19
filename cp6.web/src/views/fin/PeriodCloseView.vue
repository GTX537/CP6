<template>
  <div class="period-close">
    <div class="page-header">
      <h2>{{ t('会计期间 / 月结') }}</h2>
      <span class="subtitle">{{ t('结账=锁期；反结账危险（限高权限）') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-button size="small" @click="reload">{{ t('刷新') }}</el-button>
        <el-tag size="small" type="info">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
        <span class="hint">{{ t('期间在凭证首次记账时自动创建') }}</span>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="620" v-loading="loading">
        <el-table-column :label="t('财年/期次')" width="140">
          <template #default="{ row }">FY{{ row.fiscalYear }} · P{{ row.periodNo }}</template>
        </el-table-column>
        <el-table-column :label="t('会计期间')" width="120">
          <template #default="{ row }">{{ row.year }}-{{ String(row.month).padStart(2, '0') }}</template>
        </el-table-column>
        <el-table-column :label="t('起止日期')" width="220">
          <template #default="{ row }">{{ fmtDate(row.periodStart) }} ~ {{ fmtDate(row.periodEnd) }}</template>
        </el-table-column>
        <el-table-column :label="t('状态')" width="100" align="center">
          <template #default="{ row }">
            <el-tag :type="row.status === 1 ? 'info' : 'success'" size="small">{{ t(PERIOD_STATUS_LABEL[row.status] || '') }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="closedBy" :label="t('结账人')" width="120" show-overflow-tooltip />
        <el-table-column :label="t('结账时间')" width="170">
          <template #default="{ row }">{{ row.closedAt ? fmtDateTime(row.closedAt) : '' }}</template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="200" fixed="right">
          <template #default="{ row }">
            <el-button v-if="row.status === 0" link type="primary" size="small" @click="preCheck(row)">{{ t('结账预检') }}</el-button>
            <el-button v-if="row.status === 0" v-permission="'fin-period:close'" link type="success" size="small" @click="close(row)">{{ t('结账') }}</el-button>
            <el-button v-if="row.status === 1" v-permission="'fin-period:reopen'" link type="warning" size="small" @click="reopen(row)">{{ t('反结账') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { periodApi } from '@/api/fin/fin'
import { PERIOD_STATUS_LABEL, type FiscalPeriod } from '@/types/fin/fin'

const { t } = useI18n()

const rows = ref<FiscalPeriod[]>([])
const loading = ref(false)

async function reload() {
  loading.value = true
  try {
    const res = await periodApi.list()
    rows.value = res?.data || []
  } finally {
    loading.value = false
  }
}

async function preCheck(row: FiscalPeriod) {
  try {
    await periodApi.preCloseCheck(row.id)
    ElMessage.success(t('预检通过，可以结账'))
  } catch { /* 拦截器已提示失败原因 */ }
}

async function close(row: FiscalPeriod) {
  try {
    await ElMessageBox.confirm(
      t('确认结账 {ym}？结账后该期间不能再记账。', { ym: `${row.year}-${String(row.month).padStart(2, '0')}` }),
      t('结账'), { type: 'warning' },
    )
  } catch { return }
  await periodApi.close(row.id)
  ElMessage.success(t('已结账'))
  await reload()
}

async function reopen(row: FiscalPeriod) {
  try {
    await ElMessageBox.confirm(
      t('反结账是危险动作（已报税月份重开=改历史）。确认反结账 {ym}？', { ym: `${row.year}-${String(row.month).padStart(2, '0')}` }),
      t('反结账'), { type: 'warning', confirmButtonClass: 'el-button--danger' },
    )
  } catch { return }
  await periodApi.reopen(row.id)
  ElMessage.success(t('已反结账'))
  await reload()
}

function fmtDate(v?: string): string { return v ? v.slice(0, 10) : '' }
function fmtDateTime(v?: string): string { return v ? v.replace('T', ' ').slice(0, 19) : '' }

onMounted(reload)
</script>

<style scoped>
.period-close { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; }
.hint { color: #909399; font-size: 12px; }
</style>
