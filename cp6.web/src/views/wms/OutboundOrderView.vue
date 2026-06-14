<template>
  <div class="wms-outbound-order">
    <el-card shadow="never">
      <template #header>
        <div style="display: flex; align-items: center; gap: 12px">
          <span style="font-weight: 600">{{ isNew ? t('wms.outbound.titleNew') : `${t('wms.outbound.title')} [${form.outboundNo}]` }}</span>
          <el-tag :type="statusTagOf(form.status)" size="small">{{ statusMap[form.status] || '—' }}</el-tag>
          <el-tag size="small" :type="form.outboundType === 1 ? 'info' : 'warning'">{{ typeMap[form.outboundType] }}</el-tag>
        </div>
      </template>

      <el-form :model="form" label-width="160px" size="small">
        <el-row :gutter="16">
          <el-col :span="8">
            <el-form-item :label="t('wms.common.type')" required>
              <el-select v-model="form.outboundType" :disabled="!editable">
                <el-option v-for="(l, v) in typeMap" :key="v" :label="l" :value="Number(v)" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item :label="t('wms.outbound.fld.warehouseCd')" required>
              <el-input v-model="form.warehouseCd" :disabled="!editable" maxlength="10" />
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item :label="t('wms.outbound.fld.plannedDate')">
              <el-date-picker v-model="form.plannedDate" type="date" value-format="YYYY-MM-DD" :disabled="!editable" style="width: 100%" />
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item :label="t('wms.outbound.fld.priority')">
              <el-select v-model="form.priority" :disabled="!editable">
                <el-option :label="t('wms.outbound.priority.normal')" :value="1" />
                <el-option :label="t('wms.outbound.priority.urgent')" :value="2" />
                <el-option :label="t('wms.outbound.priority.express')" :value="3" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col v-if="form.outboundType === 1" :span="8">
            <el-form-item :label="t('wms.outbound.fld.workOrderNo')"><el-input v-model="form.workOrderNo" :disabled="!editable" maxlength="20" /></el-form-item>
          </el-col>
          <el-col v-if="form.outboundType === 2" :span="8">
            <el-form-item :label="t('wms.outbound.fld.webOrderNo')"><el-input v-model="form.webOrderNo" :disabled="!editable" maxlength="20" /></el-form-item>
          </el-col>
          <el-col v-if="form.outboundType === 2" :span="8">
            <el-form-item :label="t('wms.outbound.fld.customerCd')"><el-input v-model="form.customerCd" :disabled="!editable" maxlength="20" /></el-form-item>
          </el-col>
          <el-col v-if="form.outboundType === 2" :span="8">
            <el-form-item :label="t('wms.outbound.fld.customerName')"><el-input v-model="form.customerName" :disabled="!editable" maxlength="100" /></el-form-item>
          </el-col>
          <el-col v-if="form.outboundType === 2" :span="8">
            <el-form-item :label="t('wms.outbound.fld.carrierCd')"><el-input v-model="form.carrierCd" :disabled="!editable" maxlength="20" /></el-form-item>
          </el-col>
          <el-col v-if="form.outboundType === 2" :span="24">
            <el-form-item :label="t('wms.outbound.fld.shipToAddress')"><el-input v-model="form.shipToAddress" :disabled="!editable" maxlength="500" /></el-form-item>
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
        <el-table-column type="index" :label="t('wms.common.line')" width="50" align="center" />
        <el-table-column :label="t('wms.common.product')" min-width="140">
          <template #default="{ row }"><el-input v-model="row.productCd" :disabled="!editable" maxlength="20" /></template>
        </el-table-column>
        <el-table-column :label="t('wms.common.productName')" min-width="180">
          <template #default="{ row }"><el-input v-model="row.productName" :disabled="!editable" maxlength="100" /></template>
        </el-table-column>
        <el-table-column :label="t('wms.outbound.col.requiredQty')" width="120">
          <template #default="{ row }">
            <el-input-number v-model="row.requiredQty" :min="0" :precision="2" :disabled="!editable" controls-position="right" style="width: 100%" />
          </template>
        </el-table-column>
        <el-table-column :label="t('wms.outbound.col.allocatedQty')" width="90" align="right">
          <template #default="{ row }">
            <span :class="{ ok: row.allocatedQty >= row.requiredQty }">{{ formatQty(row.allocatedQty) }}</span>
          </template>
        </el-table-column>
        <el-table-column :label="t('wms.outbound.col.shippedQty')" width="90" align="right">
          <template #default="{ row }">{{ formatQty(row.shippedQty) }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.common.lot')" width="120">
          <template #default="{ row }">{{ row.lotNo || '—' }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.common.location')" width="120">
          <template #default="{ row }">{{ row.locationCd || '—' }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.common.unit')" width="80">
          <template #default="{ row }"><el-input v-model="row.unitCd" :disabled="!editable" maxlength="10" /></template>
        </el-table-column>
        <el-table-column :label="t('wms.common.unitPrice')" width="100">
          <template #default="{ row }">
            <el-input-number v-model="row.unitPrice" :min="0" :precision="4" :disabled="!editable" controls-position="right" style="width: 100%" />
          </template>
        </el-table-column>
        <el-table-column label="TXN" width="140">
          <template #default="{ row }">
            <el-tooltip v-if="row.allocateTxnNo" :content="`RSV: ${row.allocateTxnNo}`"><el-tag size="small">RSV</el-tag></el-tooltip>
            <el-tooltip v-if="row.shipTxnNo" :content="`OUT: ${row.shipTxnNo}`"><el-tag type="success" size="small" style="margin-left: 4px">OUT</el-tag></el-tooltip>
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
        <el-button v-if="canConfirm" type="success" @click="onConfirm">{{ t('wms.outbound.btn.confirm') }}</el-button>
        <el-button v-if="canAllocate" type="warning" @click="onAllocate">{{ t('wms.outbound.btn.allocate') }}</el-button>
        <el-button v-if="canShip" type="success" @click="onShipClick">{{ t('wms.outbound.btn.ship') }}</el-button>
        <el-button v-if="canCancel" type="warning" plain @click="onCancel">{{ t('wms.outbound.btn.cancel') }}</el-button>
        <el-button v-if="canDelete" type="danger" @click="onDelete">{{ t('wms.outbound.btn.delete') }}</el-button>
      </div>
    </el-affix>

    <el-dialog v-model="shipDialog" :title="t('wms.outbound.dlg.ship')" width="500">
      <el-form v-if="form.outboundType === 2" :model="shipReq" label-width="160px" size="small">
        <el-form-item :label="t('wms.outbound.dlg.caseQty')"><el-input-number v-model="shipReq.caseQty" :min="1" /></el-form-item>
        <el-form-item :label="t('wms.outbound.dlg.totalWeight')"><el-input-number v-model="shipReq.totalWeightKg" :min="0" :precision="3" /></el-form-item>
        <el-form-item :label="t('wms.outbound.dlg.totalVolume')"><el-input-number v-model="shipReq.totalVolumeM3" :min="0" :precision="4" /></el-form-item>
        <el-form-item :label="t('wms.outbound.fld.carrierCd')"><el-input v-model="shipReq.carrierCd" maxlength="20" /></el-form-item>
        <el-form-item :label="t('wms.outbound.dlg.trackingNo')"><el-input v-model="shipReq.trackingNo" maxlength="50" /></el-form-item>
        <el-form-item :label="t('wms.common.remarks')"><el-input v-model="shipReq.remarks" type="textarea" :rows="2" /></el-form-item>
      </el-form>
      <div v-else>{{ t('wms.outbound.dlg.materialNote') }}</div>
      <template #footer>
        <el-button @click="shipDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onConfirmShip" :loading="saving">{{ t('wms.outbound.btn.confirm') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { outboundOrderApi } from '@/api/wms/outboundOrder'
import type { OutboundOrder, OutboundOrderDetail, ShipRequest } from '@/types/wms/wms'
import { formatQty as fmtQty } from '@/utils/format'

const route = useRoute()
const router = useRouter()
const { t } = useI18n()
const saving = ref(false)

const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.outbound.status.draft'),
  1: t('wms.outbound.status.confirmed'),
  2: t('wms.outbound.status.allocated'),
  3: t('wms.outbound.status.picking'),
  4: t('wms.outbound.status.completed'),
  9: t('wms.outbound.status.cancelled'),
}))
const typeMap = computed<Record<number, string>>(() => ({
  1: t('wms.outbound.type.material'),
  2: t('wms.outbound.type.shipping'),
  3: t('wms.outbound.type.transfer'),
  9: t('wms.outbound.type.other'),
}))

const initialForm = (): OutboundOrder => ({
  outboundType: 1, warehouseCd: '',
  plannedDate: new Date().toISOString().slice(0, 10),
  status: 0, priority: 1, details: [],
})

const form = reactive<OutboundOrder>(initialForm())
const isNew = computed(() => !form.outboundNo)
const editable = computed(() => form.status === 0)
const canConfirm = computed(() => !isNew.value && form.status === 0)
const canAllocate = computed(() => !isNew.value && form.status === 1)
const canShip = computed(() => !isNew.value && (form.status === 2 || form.status === 3))
const canCancel = computed(() => !isNew.value && form.status !== 4 && form.status !== 9)
const canDelete = computed(() => !isNew.value && form.status === 0)

const shipDialog = ref(false)
const shipReq = reactive<ShipRequest>({ caseQty: 1 })

function statusTagOf(s: number): 'info' | 'primary' | 'warning' | 'success' | 'danger' {
  return ({ 0: 'info', 1: 'primary', 2: 'warning', 3: 'warning', 4: 'success', 9: 'danger' } as const)[s as 0] || 'info'
}
function formatQty(n: number | null | undefined): string {
  if (n == null) return '0'
  return fmtQty(n, 4)
}

function addLine() {
  form.details.push({ lineNo: form.details.length + 1, productCd: '', requiredQty: 0, allocatedQty: 0, shippedQty: 0 } as OutboundOrderDetail)
}
function removeLine(idx: number) {
  form.details.splice(idx, 1)
  form.details.forEach((d, i) => (d.lineNo = i + 1))
}

async function loadByNo(no: string) {
  const res = await outboundOrderApi.get(no)
  Object.assign(form, res.data)
  form.plannedDate = form.plannedDate?.slice(0, 10)
}

async function onSave() {
  if (form.details.length === 0) { ElMessage.warning(t('wms.inbound.msg.noDetail')); return }
  saving.value = true
  try {
    if (isNew.value) {
      const res = await outboundOrderApi.create(form)
      ElMessage.success(t('wms.common.success'))
      form.outboundNo = res.data.outboundNo
      router.replace({ path: '/wms/outbound-order', query: { no: form.outboundNo } })
    } else {
      await outboundOrderApi.update(form.outboundNo!, form)
      ElMessage.success(t('wms.common.success'))
      await loadByNo(form.outboundNo!)
    }
  } finally { saving.value = false }
}

async function onConfirm() {
  try {
    await ElMessageBox.confirm(t('wms.inbound.msg.confirmAsk'), t('wms.common.confirm'), { type: 'warning' })
    await outboundOrderApi.confirm(form.outboundNo!)
    ElMessage.success(t('wms.common.success'))
    await loadByNo(form.outboundNo!)
  } catch { /* */ }
}

async function onAllocate() {
  try {
    await ElMessageBox.confirm(t('wms.outbound.msg.allocateAsk'), t('wms.common.confirm'), { type: 'warning' })
    await outboundOrderApi.allocate(form.outboundNo!)
    ElMessage.success(t('wms.outbound.msg.allocateOk'))
    await loadByNo(form.outboundNo!)
  } catch { /* */ }
}

function onShipClick() { shipDialog.value = true }

async function onConfirmShip() {
  saving.value = true
  try {
    const res = await outboundOrderApi.ship(form.outboundNo!, shipReq)
    if (res.data.packageNo) {
      ElMessage.success(t('wms.outbound.msg.shipOkPkg', { no: res.data.packageNo }))
    } else {
      ElMessage.success(t('wms.outbound.msg.shipOkMat'))
    }
    shipDialog.value = false
    await loadByNo(form.outboundNo!)
  } finally { saving.value = false }
}

async function onCancel() {
  try {
    await ElMessageBox.confirm(t('wms.outbound.msg.cancelAsk'), t('wms.common.confirm'), { type: 'warning' })
    await outboundOrderApi.cancel(form.outboundNo!)
    ElMessage.success(t('wms.common.success'))
    await loadByNo(form.outboundNo!)
  } catch { /* */ }
}

async function onDelete() {
  try {
    await ElMessageBox.confirm(t('wms.inbound.msg.deleteAsk'), t('wms.common.confirm'), { type: 'warning' })
    await outboundOrderApi.delete(form.outboundNo!)
    ElMessage.success(t('wms.common.success'))
    router.push('/wms/outbound-order-list')
  } catch { /* */ }
}

function goBack() { router.push('/wms/outbound-order-list') }

onMounted(async () => {
  const no = route.query.no as string | undefined
  if (no) await loadByNo(no)
})
</script>

<style scoped>
.wms-outbound-order { padding: 16px; padding-bottom: 60px; }
.action-bar { background: var(--el-bg-color); border-top: 1px solid var(--el-border-color-lighter); padding: 12px 16px; text-align: right; }
.action-bar > * { margin-left: 8px; }
.ok { color: var(--el-color-success); font-weight: 600; }
</style>
