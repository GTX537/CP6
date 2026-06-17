<template>
  <div class="pur-recon">
    <div class="page-header">
      <h2>{{ t('采购对账') }}</h2>
      <span class="subtitle">{{ t('完整性收口：PO↔GR↔AP三方核对，堵三个漏(虚开/超收·重复收货/外协吞料)') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-input v-model="poNo" size="small" style="width: 220px" :placeholder="t('请输入采购订单号')" clearable @keyup.enter="run" />
        <el-button type="primary" size="small" :loading="loading" @click="run">{{ t('对账') }}</el-button>
      </div>

      <template v-if="report">
        <el-descriptions :column="3" size="small" border style="margin-bottom:10px">
          <el-descriptions-item :label="t('采购订单号')">{{ report.poNo }}</el-descriptions-item>
          <el-descriptions-item :label="t('外协厂')">{{ report.supplierId }}</el-descriptions-item>
          <el-descriptions-item :label="t('类型')">
            <el-tag size="small" :type="report.type === 2 ? 'warning' : 'info'">{{ t(report.type === 2 ? '外注委托' : '标准采购') }}</el-tag>
          </el-descriptions-item>
        </el-descriptions>

        <el-alert :type="report.hasIssue ? 'error' : 'success'" :closable="false" show-icon style="margin-bottom:10px"
          :title="report.hasIssue ? t('存在完整性异常，请挂起核查') : t('对账正常，三方一致')" />

        <el-table :data="report.lines" border stripe size="small">
          <el-table-column prop="lineNo" :label="t('行')" width="50" />
          <el-table-column prop="itemId" :label="t('物料')" min-width="120" show-overflow-tooltip />
          <el-table-column prop="ordered" :label="t('订购')" width="90" align="right" />
          <el-table-column prop="received" :label="t('收货')" width="90" align="right" />
          <el-table-column prop="accepted" :label="t('合格')" width="90" align="right" />
          <el-table-column prop="invoiced" :label="t('开票')" width="90" align="right" />
          <el-table-column prop="openToReceive" :label="t('待收')" width="90" align="right" />
          <el-table-column prop="openToInvoice" :label="t('待开票')" width="90" align="right" />
          <el-table-column :label="t('诊断')" width="110" align="center">
            <template #default="{ row }">
              <el-tag :type="RECON_STATUS_TAG[row.status] || 'info'" size="small">{{ t(row.status) }}</el-tag>
            </template>
          </el-table-column>
        </el-table>

        <!-- 外注：支給材防吞料对账 -->
        <template v-if="report.consignReconciles?.length">
          <div class="section-head">{{ t('支給材防吞料对账') }}</div>
          <div v-for="(cr, idx) in report.consignReconciles" :key="idx" class="consign-block">
            <span class="cr-label">{{ t('行') }} #{{ cr.lineNo }} · {{ t('收成品数') }} {{ cr.finishedQty }}</span>
            <el-table :data="cr.lines" border size="small">
              <el-table-column prop="consignItemId" :label="t('支給材物料')" min-width="120" show-overflow-tooltip />
              <el-table-column prop="issuedQty" :label="t('实发')" width="90" align="right" />
              <el-table-column prop="expectedQty" :label="t('应耗')" width="90" align="right" />
              <el-table-column prop="variance" :label="t('差异')" width="90" align="right" />
              <el-table-column prop="allowedVariance" :label="t('容许差异')" width="90" align="right" />
              <el-table-column :label="t('是否异常')" width="90" align="center">
                <template #default="{ row }">
                  <el-tag :type="row.isAnomaly ? 'danger' : 'success'" size="small">{{ row.isAnomaly ? t('异常') : t('正常') }}</el-tag>
                </template>
              </el-table-column>
            </el-table>
          </div>
        </template>
      </template>
      <el-empty v-else-if="!loading" :description="t('输入采购订单号后对账')" />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { reconcileApi } from '@/api/pur/pur'
import { RECON_STATUS_TAG, type PoReconcileReport } from '@/types/pur/pur'

const { t } = useI18n()

const poNo = ref('')
const loading = ref(false)
const report = ref<PoReconcileReport | null>(null)

async function run() {
  if (!poNo.value.trim()) { ElMessage.warning(t('请输入采购订单号')); return }
  loading.value = true
  try {
    const res = await reconcileApi.reconcile(poNo.value.trim())
    report.value = res?.data || null
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.pur-recon { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 12px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.section-head { margin: 14px 0 8px; font-weight: 600; }
.consign-block { margin-bottom: 12px; }
.cr-label { display: inline-block; margin-bottom: 6px; color: #606266; font-size: 13px; }
</style>
