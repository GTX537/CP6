<template>
  <div class="wms-inbound-order">
    <el-card shadow="never" class="header-card">
      <template #header>
        <div style="display: flex; align-items: center; gap: 12px">
          <span style="font-weight: 600">{{ isNew ? t('wms.inbound.titleNew') : `${t('wms.inbound.titleEdit')} [${form.inboundNo}]` }}</span>
          <CpTag :tone="statusTone(form.status)">{{ statusMap[form.status] || '—' }}</CpTag>
        </div>
      </template>

      <el-form :model="form" label-width="140px" size="small">
        <el-row :gutter="16">
          <el-col :span="8">
            <el-form-item :label="t('wms.common.type')" required>
              <el-select v-model="form.inboundType" :disabled="!editable">
                <el-option v-for="(l, v) in typeMap" :key="v" :label="l" :value="Number(v)" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item :label="t('wms.inbound.fld.inboundWh')" required>
              <el-input v-model="form.warehouseCd" :disabled="!editable" maxlength="10" />
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item :label="t('wms.inbound.fld.expectedArrival')" required>
              <el-date-picker v-model="form.expectedArrivalDate" type="date" value-format="YYYY-MM-DD" :disabled="!editable" style="width: 100%" />
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item :label="t('wms.inbound.fld.supplierCd')"><el-input v-model="form.supplierCd" :disabled="!editable" maxlength="20" /></el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item :label="t('wms.inbound.fld.supplierName')"><el-input v-model="form.supplierName" :disabled="!editable" maxlength="100" /></el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item :label="t('wms.inbound.fld.poNo')"><el-input v-model="form.poNo" :disabled="!editable" maxlength="20" /></el-form-item>
          </el-col>
          <el-col :span="24">
            <el-form-item :label="t('wms.common.remarks')"><el-input v-model="form.remarks" type="textarea" :rows="2" :disabled="!editable" /></el-form-item>
          </el-col>
        </el-row>
      </el-form>
    </el-card>

    <el-card shadow="never" style="margin-top: 12px">
      <template #header>
        <div style="display: flex; justify-content: space-between; align-items: center">
          <span style="font-weight: 600">{{ t('wms.common.detail') }}</span>
          <el-button v-if="editable" type="primary" size="small" @click="addLine">{{ t('wms.common.addLine') }}</el-button>
        </div>
      </template>

      <el-table :data="form.details" border size="small" max-height="400">
        <el-table-column type="index" :label="t('wms.common.line')" width="60" align="center" />
        <el-table-column :label="t('wms.common.product')" min-width="160">
          <template #default="{ row }"><el-input v-model="row.productCd" :disabled="!editable" maxlength="20" /></template>
        </el-table-column>
        <el-table-column :label="t('wms.common.productName')" min-width="200">
          <template #default="{ row }"><el-input v-model="row.productName" :disabled="!editable" maxlength="100" /></template>
        </el-table-column>
        <el-table-column :label="t('wms.common.lot')" min-width="140">
          <template #default="{ row }"><el-input v-model="row.lotNo" :disabled="!editable" :placeholder="t('wms.receipt.col.lotHint')" /></template>
        </el-table-column>
        <el-table-column :label="t('wms.inbound.col.expectedQty')" width="140">
          <template #default="{ row }">
            <el-input-number v-model="row.expectedQty" :min="0" :precision="2" :disabled="!editable" controls-position="right" style="width: 100%" />
          </template>
        </el-table-column>
        <el-table-column :label="t('wms.inbound.col.receivedQtyAccum')" width="100" align="right">
          <template #default="{ row }">{{ formatQty(row.receivedQty || 0) }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.common.unit')" width="80">
          <template #default="{ row }"><el-input v-model="row.unitCd" :disabled="!editable" maxlength="10" /></template>
        </el-table-column>
        <el-table-column :label="t('wms.inbound.col.expectedLoc')" width="140">
          <template #default="{ row }"><el-input v-model="row.expectedLocationCd" :disabled="!editable" maxlength="30" /></template>
        </el-table-column>
        <el-table-column :label="t('wms.common.unitPrice')" width="120">
          <template #default="{ row }">
            <el-input-number v-model="row.unitPrice" :min="0" :precision="4" :disabled="!editable" controls-position="right" style="width: 100%" />
          </template>
        </el-table-column>
        <el-table-column :label="t('wms.common.action')" width="80" fixed="right">
          <template #default="{ $index }">
            <el-button v-if="editable" link type="danger" size="small" @click="removeLine($index)">{{ t('wms.common.delete') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-affix position="bottom" :offset="0">
      <div class="action-bar">
        <el-button @click="goBack">{{ t('wms.common.back') }}</el-button>
        <el-button v-if="editable" type="primary" @click="onSave" :loading="saving">{{ t('wms.common.save') }}</el-button>
        <el-button v-if="canConfirm" type="success" @click="onConfirm">{{ t('wms.inbound.btn.confirm') }}</el-button>
        <el-button v-if="canCancel" type="warning" @click="onCancel">{{ t('wms.inbound.btn.cancel') }}</el-button>
        <el-button v-if="canDelete" type="danger" @click="onDelete">{{ t('wms.common.delete') }}</el-button>
        <el-button v-if="canReceive" type="success" plain @click="goReceive">{{ t('wms.inbound.btn.receiptLong') }}</el-button>
      </div>
    </el-affix>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { inboundOrderApi } from '@/api/wms/inboundOrder'
import type { InboundOrder, InboundOrderDetail } from '@/types/wms/wms'
import { formatQty } from '@/utils/format'
import CpTag, { type Tone } from '@/components/base/CpTag.vue'

const route = useRoute()
const router = useRouter()
const { t } = useI18n()
const saving = ref(false)

const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.inbound.status.draft'),
  1: t('wms.inbound.status.confirmed'),
  2: t('wms.inbound.status.partial'),
  3: t('wms.inbound.status.completed'),
  9: t('wms.inbound.status.cancelled'),
}))
const typeMap = computed<Record<number, string>>(() => ({
  1: t('wms.inbound.type.purchase'),
  2: t('wms.inbound.type.rework'),
  3: t('wms.inbound.type.return'),
  9: t('wms.inbound.type.other'),
}))

const initialForm = (): InboundOrder => ({
  inboundType: 1,
  expectedArrivalDate: new Date().toISOString().slice(0, 10),
  warehouseCd: '', status: 0, details: [],
})

const form = reactive<InboundOrder>(initialForm())
const isNew = computed(() => !form.inboundNo)
const editable = computed(() => form.status === 0)
const canConfirm = computed(() => !isNew.value && form.status === 0)
const canCancel = computed(() => !isNew.value && form.status !== 9 && form.status !== 3)
const canDelete = computed(() => !isNew.value && form.status === 0)
const canReceive = computed(() => !isNew.value && (form.status === 1 || form.status === 2))

// 原 statusTagOf(info/primary/warning/success/danger) → 设计系统 Tone（保色）
function statusTone(s: number): Tone {
  return ({ 0: 'muted', 1: 'info', 2: 'warn', 3: 'ok', 9: 'danger' } as const)[s as 0] || 'muted'
}

function addLine() {
  form.details.push({ lineNo: form.details.length + 1, productCd: '', expectedQty: 0, receivedQty: 0 } as InboundOrderDetail)
}
function removeLine(idx: number) {
  form.details.splice(idx, 1)
  form.details.forEach((d, i) => (d.lineNo = i + 1))
}

async function loadByNo(no: string) {
  const res = await inboundOrderApi.get(no)
  Object.assign(form, res.data)
  form.expectedArrivalDate = form.expectedArrivalDate?.slice(0, 10)
}

async function onSave() {
  if (form.details.length === 0) { ElMessage.warning(t('wms.inbound.msg.noDetail')); return }
  saving.value = true
  try {
    if (isNew.value) {
      const res = await inboundOrderApi.create(form)
      ElMessage.success(t('wms.common.success'))
      form.inboundNo = res.data.inboundNo
      router.replace({ path: '/wms/inbound-order', query: { no: form.inboundNo } })
    } else {
      await inboundOrderApi.update(form.inboundNo!, form)
      ElMessage.success(t('wms.common.success'))
      await loadByNo(form.inboundNo!)
    }
  } finally { saving.value = false }
}

async function onConfirm() {
  try {
    await ElMessageBox.confirm(t('wms.inbound.msg.confirmAsk'), t('wms.common.confirm'), { type: 'warning' })
    await inboundOrderApi.confirm(form.inboundNo!)
    ElMessage.success(t('wms.common.success'))
    await loadByNo(form.inboundNo!)
  } catch { /* */ }
}
async function onCancel() {
  try {
    await ElMessageBox.confirm(t('wms.inbound.msg.cancelAsk'), t('wms.common.confirm'), { type: 'warning' })
    await inboundOrderApi.cancel(form.inboundNo!)
    ElMessage.success(t('wms.common.success'))
    await loadByNo(form.inboundNo!)
  } catch { /* */ }
}
async function onDelete() {
  try {
    await ElMessageBox.confirm(t('wms.inbound.msg.deleteAsk'), t('wms.common.confirm'), { type: 'warning' })
    await inboundOrderApi.delete(form.inboundNo!)
    ElMessage.success(t('wms.common.success'))
    router.push('/wms/inbound-order-list')
  } catch { /* */ }
}

function goBack() { router.push('/wms/inbound-order-list') }
function goReceive() { router.push({ path: '/wms/inbound-receipt', query: { inboundNo: form.inboundNo } }) }

onMounted(async () => {
  const no = route.query.no as string | undefined
  if (no) await loadByNo(no)
})
</script>

<style scoped>
.wms-inbound-order { padding: 16px; padding-bottom: 60px; }
.header-card :deep(.el-card__header) { padding: 10px 16px; }
.action-bar { background: var(--el-bg-color); border-top: 1px solid var(--el-border-color-lighter); padding: 12px 16px; text-align: right; }
.action-bar > * { margin-left: 8px; }
</style>
