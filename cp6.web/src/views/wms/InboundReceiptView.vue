<template>
  <div class="wms-inbound-receipt">
    <el-card shadow="never">
      <template #header><span style="font-weight: 600">{{ t('wms.receipt.title') }}（{{ mode === 'direct' ? t('wms.receipt.modeDirect') : t('wms.receipt.modeRef') }}）</span></template>

      <el-form :model="form" label-width="160px" size="small">
        <el-row :gutter="16">
          <el-col :span="8">
            <el-form-item :label="t('wms.receipt.fld.refNo')">
              <el-input v-model="form.inboundNo" :disabled="!!form.inboundNo" :placeholder="t('wms.receipt.fld.refNoHint')">
                <template #append>
                  <el-button @click="loadOrder" :disabled="!form.inboundNo">{{ t('wms.receipt.btn.loadOrder') }}</el-button>
                </template>
              </el-input>
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item :label="t('wms.receipt.fld.sourceType')">
              <el-select v-model="form.sourceType" style="width: 100%">
                <el-option :label="t('wms.receipt.source.purchase')" value="PURCHASE" />
                <el-option :label="t('wms.receipt.source.production')" value="PRODUCTION" />
                <el-option :label="t('wms.receipt.source.rma')" value="RMA" />
                <el-option :label="t('wms.receipt.source.manual')" value="MANUAL" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item :label="t('wms.inbound.fld.inboundWh')" required><el-input v-model="form.warehouseCd" maxlength="10" /></el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item :label="t('wms.receipt.fld.receiveDateTime')">
              <el-date-picker v-model="form.receiveDateTime" type="datetime" value-format="YYYY-MM-DDTHH:mm:ss" style="width: 100%" />
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item :label="t('wms.common.operator')"><el-input v-model="form.operatorCd" maxlength="20" /></el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item :label="t('wms.receipt.fld.workOrderNo')"><el-input v-model="form.workOrderNo" maxlength="20" :placeholder="t('wms.receipt.fld.workOrderHint')" /></el-form-item>
          </el-col>
          <el-col :span="24">
            <el-form-item :label="t('wms.common.remarks')"><el-input v-model="form.remarks" type="textarea" :rows="2" /></el-form-item>
          </el-col>
        </el-row>
      </el-form>
    </el-card>

    <el-card shadow="never" style="margin-top: 12px">
      <template #header>
        <div style="display: flex; justify-content: space-between; align-items: center">
          <span style="font-weight: 600">{{ t('wms.common.detail') }}</span>
          <el-button type="primary" size="small" @click="addLine">{{ t('wms.common.addLine') }}</el-button>
        </div>
      </template>

      <el-table :data="form.details" border size="small" max-height="450">
        <el-table-column type="index" :label="t('wms.common.line')" width="60" align="center" />
        <el-table-column :label="t('wms.receipt.col.refLine')" width="80" align="center">
          <template #default="{ row }">{{ row.refOrderLineNo || '—' }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.common.product')" min-width="140">
          <template #default="{ row }"><el-input v-model="row.productCd" maxlength="20" /></template>
        </el-table-column>
        <el-table-column :label="t('wms.common.productName')" min-width="180">
          <template #default="{ row }"><el-input v-model="row.productName" maxlength="100" /></template>
        </el-table-column>
        <el-table-column :label="t('wms.common.lot')" min-width="140">
          <template #default="{ row }"><el-input v-model="row.lotNo" :placeholder="t('wms.receipt.col.lotHint')" maxlength="30" /></template>
        </el-table-column>
        <el-table-column :label="t('wms.receipt.col.receivedQty')" width="130">
          <template #default="{ row }">
            <el-input-number v-model="row.receivedQty" :min="0" :precision="2" controls-position="right" style="width: 100%" />
          </template>
        </el-table-column>
        <el-table-column :label="t('wms.common.unit')" width="80">
          <template #default="{ row }"><el-input v-model="row.unitCd" maxlength="10" /></template>
        </el-table-column>
        <el-table-column :label="t('wms.receipt.col.locCd')" min-width="160">
          <template #default="{ row }"><el-input v-model="row.locationCd" maxlength="30" /></template>
        </el-table-column>
        <el-table-column :label="t('wms.common.unitPrice')" width="120">
          <template #default="{ row }">
            <el-input-number v-model="row.unitPrice" :min="0" :precision="4" controls-position="right" style="width: 100%" />
          </template>
        </el-table-column>
        <el-table-column :label="t('wms.common.expiryDate')" width="160">
          <template #default="{ row }">
            <el-date-picker v-model="row.expiryDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
          </template>
        </el-table-column>
        <el-table-column :label="t('wms.common.action')" width="80" fixed="right">
          <template #default="{ $index }">
            <el-button link type="danger" size="small" @click="removeLine($index)">{{ t('wms.common.delete') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-affix position="bottom" :offset="0">
      <div class="action-bar">
        <el-button @click="goBack">{{ t('wms.common.back') }}</el-button>
        <el-button type="primary" @click="onConfirm" :loading="saving">{{ t('wms.receipt.btn.confirmReceive') }}</el-button>
      </div>
    </el-affix>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { inboundReceiptApi } from '@/api/wms/inboundReceipt'
import { inboundOrderApi } from '@/api/wms/inboundOrder'
import type { InboundReceipt, InboundReceiptDetail } from '@/types/wms/wms'

const route = useRoute()
const router = useRouter()
const { t } = useI18n()
const saving = ref(false)

const initialForm = (): InboundReceipt => ({
  sourceType: 'PURCHASE',
  receiveDateTime: new Date().toISOString().slice(0, 19),
  warehouseCd: '', status: 0, details: [],
})

const form = reactive<InboundReceipt>(initialForm())
const mode = computed(() => (form.inboundNo ? 'order' : 'direct'))

function addLine() {
  form.details.push({ lineNo: form.details.length + 1, productCd: '', lotNo: '', receivedQty: 0, locationCd: '' } as InboundReceiptDetail)
}
function removeLine(idx: number) {
  form.details.splice(idx, 1)
  form.details.forEach((d, i) => (d.lineNo = i + 1))
}

async function loadOrder() {
  if (!form.inboundNo) return
  try {
    const res = await inboundOrderApi.get(form.inboundNo)
    const order = res.data
    if (order.status !== 1 && order.status !== 2) {
      ElMessage.warning(t('wms.receipt.msg.invalidStatus'))
      return
    }
    form.warehouseCd = order.warehouseCd
    form.sourceType = 'PURCHASE'
    form.details = order.details
      .filter(d => Number(d.expectedQty) - Number(d.receivedQty || 0) > 0)
      .map((d, i) => ({
        lineNo: i + 1, refOrderLineNo: d.lineNo,
        productCd: d.productCd, productName: d.productName,
        lotNo: d.lotNo || '',
        receivedQty: Number(d.expectedQty) - Number(d.receivedQty || 0),
        unitCd: d.unitCd, locationCd: d.expectedLocationCd || '',
        unitPrice: d.unitPrice,
      } as InboundReceiptDetail))
    ElMessage.success(t('wms.receipt.msg.loadOk', { n: form.details.length }))
  } catch { /* */ }
}

async function onConfirm() {
  if (form.details.length === 0) { ElMessage.warning(t('wms.inbound.msg.noDetail')); return }
  if (!form.warehouseCd) { ElMessage.warning(t('wms.common.required')); return }
  for (const d of form.details) {
    if (!d.productCd || !d.locationCd || d.receivedQty <= 0) {
      ElMessage.warning(t('wms.receipt.msg.requireAll'))
      return
    }
  }
  saving.value = true
  try {
    const res = await inboundReceiptApi.confirm(form)
    ElMessage.success(`${t('wms.common.success')}: ${res.data.receiptNo}`)
    router.push('/wms/inbound-order-list')
  } finally { saving.value = false }
}

function goBack() { router.back() }

onMounted(async () => {
  const inboundNo = route.query.inboundNo as string | undefined
  if (inboundNo) {
    form.inboundNo = inboundNo
    await loadOrder()
  }
})
</script>

<style scoped>
.wms-inbound-receipt { padding: 16px; padding-bottom: 60px; }
.action-bar { background: var(--cp-card); border-top: 1px solid var(--cp-line-soft); padding: 12px 16px; text-align: right; }
.action-bar > * { margin-left: 8px; }
</style>
