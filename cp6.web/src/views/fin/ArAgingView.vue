<template>
  <div class="ar-aging">
    <div class="page-header">
      <h2>{{ t('应收账龄') }}</h2>
      <span class="subtitle">{{ t('按到期日分桶 + 子账↔GL 勾稽') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-date-picker v-model="asOf" type="date" size="small" :placeholder="t('账龄基准日')" value-format="YYYY-MM-DD" @change="reload" />
        <el-input v-model="customerId" size="small" style="width: 160px" :placeholder="t('客户')" clearable @change="reload" />
        <el-button size="small" @click="reload">{{ t('刷新') }}</el-button>
        <el-tag v-if="recon" size="small" :type="recon.isMatched ? 'success' : 'danger'">
          {{ recon.isMatched ? t('子账与GL相符') : t('子账与GL不符') }}（{{ recon.subLedger }} / {{ recon.glBalance }}）
        </el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="620" v-loading="loading">
        <el-table-column prop="customerId" :label="t('客户')" min-width="140" />
        <el-table-column prop="notDue" :label="t('未到期')" width="120" align="right" />
        <el-table-column prop="days1To30" :label="t('逾期1-30')" width="120" align="right" />
        <el-table-column prop="days31To60" :label="t('逾期31-60')" width="120" align="right" />
        <el-table-column prop="days60Plus" :label="t('逾期60+')" width="120" align="right" />
        <el-table-column :label="t('逾期合计')" width="120" align="right">
          <template #default="{ row }"><span :class="{ overdue: row.overdue > 0 }">{{ row.overdue }}</span></template>
        </el-table-column>
        <el-table-column prop="total" :label="t('未收合计')" width="130" align="right" />
      </el-table>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { arInvoiceApi } from '@/api/fin/fin'
import type { ArAgingRow, ArReconcileResult } from '@/types/fin/fin'

const { t } = useI18n()
const rows = ref<ArAgingRow[]>([])
const recon = ref<ArReconcileResult | null>(null)
const loading = ref(false)
const asOf = ref<string>(new Date().toISOString().slice(0, 10))
const customerId = ref('')

async function reload() {
  loading.value = true
  try {
    const [a, r] = await Promise.all([
      arInvoiceApi.aging(asOf.value, customerId.value || undefined),
      arInvoiceApi.reconcile(),
    ])
    rows.value = a?.data || []
    recon.value = r?.data || null
  } finally {
    loading.value = false
  }
}

onMounted(reload)
</script>

<style scoped>
.ar-aging { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.overdue { color: #f56c6c; font-weight: 600; }
</style>
