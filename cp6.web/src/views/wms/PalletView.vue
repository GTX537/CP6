<template>
  <div class="wms-pallet">
    <el-card shadow="never" class="search-card">
      <el-form :model="query" inline size="small">
        <el-form-item :label="t('wms.pallet.fld.no')"><el-input v-model="query.palletNo" clearable style="width: 180px" /></el-form-item>
        <el-form-item :label="t('wms.common.product')"><el-input v-model="query.productCd" clearable style="width: 140px" /></el-form-item>
        <el-form-item :label="t('wms.common.lot')"><el-input v-model="query.lotNo" clearable style="width: 140px" /></el-form-item>
        <el-form-item :label="t('wms.common.warehouse')"><el-input v-model="query.warehouseCd" clearable style="width: 100px" /></el-form-item>
        <el-form-item :label="t('wms.common.status')">
          <el-select v-model="query.status" clearable style="width: 130px">
            <el-option v-for="(l, v) in statusMap" :key="v" :label="l" :value="Number(v)" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="reload" :loading="loading">{{ t('wms.common.search') }}</el-button>
          <el-button @click="openCreate">{{ t('wms.common.create') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <el-table :data="rows" border stripe size="small" max-height="600" highlight-current-row>
        <el-table-column prop="palletNo" :label="t('wms.pallet.fld.no')" width="180" />
        <el-table-column :label="t('wms.common.status')" width="110">
          <template #default="{ row }"><el-tag :type="statusTagOf(row.status)" size="small">{{ statusMap[row.status] }}</el-tag></template>
        </el-table-column>
        <el-table-column prop="productCd" :label="t('wms.common.product')" width="140" />
        <el-table-column prop="productName" :label="t('wms.common.productName')" min-width="160" show-overflow-tooltip />
        <el-table-column prop="lotNo" :label="t('wms.common.lot')" width="140" />
        <el-table-column prop="cartonQty" :label="t('wms.pallet.fld.cartonQty')" width="80" align="right" />
        <el-table-column prop="weightKg" :label="t('wms.pallet.fld.weightKg')" width="100" align="right">
          <template #default="{ row }">{{ formatQty(row.weightKg) }}</template>
        </el-table-column>
        <el-table-column prop="heightMm" :label="t('wms.pallet.fld.heightMm')" width="100" align="right" />
        <el-table-column prop="maxStackLayers" :label="t('wms.pallet.fld.maxStack')" width="100" align="right" />
        <el-table-column prop="warehouseCd" :label="t('wms.common.warehouse')" width="100" />
        <el-table-column prop="locationCd" :label="t('wms.common.location')" width="140" />
        <el-table-column prop="shippedOutboundNo" :label="t('wms.pallet.fld.outboundNo')" width="160" />
        <el-table-column :label="t('wms.common.action')" width="280" fixed="right">
          <template #default="{ row }">
            <el-button v-if="row.status === 0" link type="primary" size="small" @click="onComplete(row)">{{ t('wms.pallet.btn.complete') }}</el-button>
            <el-button v-if="row.status === 1" link type="warning" size="small" @click="openMove(row)">{{ t('wms.pallet.btn.moveShip') }}</el-button>
            <el-button v-if="row.status === 2" link type="success" size="small" @click="openShip(row)">{{ t('wms.pallet.btn.markShipped') }}</el-button>
            <el-button v-if="row.status === 0" link type="danger" size="small" @click="onDelete(row)">{{ t('wms.common.delete') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 新建 Dialog -->
    <el-dialog v-model="createDialog" :title="t('wms.pallet.dlg.create')" width="600">
      <el-form v-if="editing" :model="editing" label-width="140px" size="small">
        <el-row :gutter="12">
          <el-col :span="12"><el-form-item :label="t('wms.common.product')" required><el-input v-model="editing.productCd" maxlength="20" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.common.productName')"><el-input v-model="editing.productName" maxlength="100" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.common.lot')" required><el-input v-model="editing.lotNo" maxlength="30" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.pallet.fld.cartonQty')" required><el-input-number v-model="editing.cartonQty" :min="1" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.pallet.fld.weightKg')"><el-input-number v-model="editing.weightKg" :min="0" :precision="2" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.pallet.fld.heightMm')"><el-input-number v-model="editing.heightMm" :min="0" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.pallet.fld.maxStack')"><el-input-number v-model="editing.maxStackLayers" :min="1" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.common.warehouse')" required><el-input v-model="editing.warehouseCd" maxlength="10" /></el-form-item></el-col>
          <el-col :span="24"><el-form-item :label="t('wms.common.location')" required><el-input v-model="editing.locationCd" maxlength="30" /></el-form-item></el-col>
          <el-col :span="24"><el-form-item :label="t('wms.common.remarks')"><el-input v-model="editing.remarks" type="textarea" :rows="2" /></el-form-item></el-col>
        </el-row>
      </el-form>
      <template #footer>
        <el-button @click="createDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onCreate" :loading="saving">{{ t('wms.common.save') }}</el-button>
      </template>
    </el-dialog>

    <!-- 移动 Dialog -->
    <el-dialog v-model="moveDialog" :title="t('wms.pallet.dlg.moveShip') + ' — ' + moveTarget?.palletNo" width="420">
      <el-form label-width="140px" size="small">
        <el-form-item :label="t('wms.pallet.fld.toLoc')" required>
          <el-input v-model="moveToLocation" maxlength="30" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="moveDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onMove" :loading="saving">{{ t('wms.common.confirm') }}</el-button>
      </template>
    </el-dialog>

    <!-- 出货 Dialog -->
    <el-dialog v-model="shipDialog" :title="t('wms.pallet.dlg.markShipped') + ' — ' + shipTarget?.palletNo" width="420">
      <el-form label-width="140px" size="small">
        <el-form-item :label="t('wms.pallet.fld.outboundNo')" required>
          <el-input v-model="shipOutboundNo" maxlength="25" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="shipDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onShip" :loading="saving">{{ t('wms.common.confirm') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { palletApi } from '@/api/wms/paperIndustry'
import type { Pallet, PalletSearchQuery } from '@/types/wms/wms'

const { t } = useI18n()
const query = reactive<PalletSearchQuery>({ pageSize: 100 })
const rows = ref<Pallet[]>([])
const loading = ref(false)
const saving = ref(false)

const createDialog = ref(false)
const editing = ref<any>(null)

const moveDialog = ref(false)
const moveTarget = ref<Pallet | null>(null)
const moveToLocation = ref('')

const shipDialog = ref(false)
const shipTarget = ref<Pallet | null>(null)
const shipOutboundNo = ref('')

const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.pallet.status.building'),
  1: t('wms.pallet.status.inStock'),
  2: t('wms.pallet.status.waitingShip'),
  3: t('wms.pallet.status.shipped'),
}))

function statusTagOf(s: number): 'info' | 'success' | 'warning' | 'primary' {
  return ({ 0: 'info', 1: 'success', 2: 'warning', 3: 'primary' } as const)[s as 0] || 'info'
}

function formatQty(n: number | undefined | null) {
  if (n == null) return ''
  return Number(n).toLocaleString('ja-JP', { maximumFractionDigits: 2 })
}

async function reload() {
  loading.value = true
  try { rows.value = (await palletApi.search(query)).data || [] }
  finally { loading.value = false }
}

function openCreate() {
  editing.value = {
    productCd: '', productName: '', lotNo: '',
    cartonQty: 1, weightKg: undefined, heightMm: undefined, maxStackLayers: undefined,
    warehouseCd: '', locationCd: '',
  }
  createDialog.value = true
}

async function onCreate() {
  saving.value = true
  try {
    const res = await palletApi.create(editing.value)
    ElMessage.success(`${t('wms.common.success')}: ${res.data.palletNo}`)
    createDialog.value = false
    await reload()
  } finally { saving.value = false }
}

async function onComplete(row: Pallet) {
  try {
    await ElMessageBox.confirm(`${t('wms.pallet.btn.complete')}: ${row.palletNo}`, t('wms.common.confirm'), { type: 'warning' })
    await palletApi.completeBuilding(row.palletNo)
    ElMessage.success(t('wms.common.success'))
    await reload()
  } catch { /* */ }
}

function openMove(row: Pallet) {
  moveTarget.value = row
  moveToLocation.value = ''
  moveDialog.value = true
}

async function onMove() {
  if (!moveTarget.value || !moveToLocation.value) { ElMessage.warning(t('wms.common.required')); return }
  saving.value = true
  try {
    await palletApi.moveToShipping(moveTarget.value.palletNo, moveToLocation.value)
    ElMessage.success(t('wms.common.success'))
    moveDialog.value = false
    await reload()
  } finally { saving.value = false }
}

function openShip(row: Pallet) {
  shipTarget.value = row
  shipOutboundNo.value = ''
  shipDialog.value = true
}

async function onShip() {
  if (!shipTarget.value || !shipOutboundNo.value) { ElMessage.warning(t('wms.common.required')); return }
  saving.value = true
  try {
    await palletApi.markShipped(shipTarget.value.palletNo, shipOutboundNo.value)
    ElMessage.success(t('wms.common.success'))
    shipDialog.value = false
    await reload()
  } finally { saving.value = false }
}

async function onDelete(row: Pallet) {
  try {
    await ElMessageBox.confirm(`${t('wms.common.confirmDelete')}: ${row.palletNo}`, t('wms.common.confirm'), { type: 'warning' })
    await palletApi.delete(row.palletNo)
    ElMessage.success(t('wms.common.success'))
    await reload()
  } catch { /* */ }
}

onMounted(reload)
</script>

<style scoped>
.wms-pallet { padding: 16px; }
.search-card { margin-bottom: 12px; }
</style>
