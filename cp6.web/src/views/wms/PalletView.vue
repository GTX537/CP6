<!--
  パレット管理 —— CpPageShell + CpListPage + 3×CpFormDialog 迁移（WMS 批次3）。
  状態列 kind:'tag'+map；重量列 map(formatQty)；操作走 col slot（状态相关 4 动作）。
  新建/移動/出荷用 CpFormDialog（default slot 保留 input-number/maxlength）；必填改 el-form rules。
  in-place 变更(新建/完成/移動/出荷/削除)后 listRef.reload() 保留当前筛选/页码。
-->
<template>
  <CpPageShell :title="t('wms.pallet.title')" :count="total">
    <template #actions>
      <el-button @click="openCreate">{{ t('wms.common.create') }}</el-button>
    </template>

    <CpListPage
      ref="listRef"
      :columns="columns"
      :fetch="fetchList"
      :search-fields="searchFields"
      :filter-labels="filterLabels"
      @total-change="total = $event"
    >
      <template #col-_action="{ row }">
        <el-button v-if="row.status === 0" link type="primary" size="small" @click="onComplete(row)">{{ t('wms.pallet.btn.complete') }}</el-button>
        <el-button v-if="row.status === 1" v-permission="'wms-pallet:move'" link type="warning" size="small" @click="openMove(row)">{{ t('wms.pallet.btn.moveShip') }}</el-button>
        <el-button v-if="row.status === 2" v-permission="'wms-pallet:ship'" link type="success" size="small" @click="openShip(row)">{{ t('wms.pallet.btn.markShipped') }}</el-button>
        <el-button v-if="row.status === 0" v-permission="'wms-pallet:del'" link type="danger" size="small" @click="onDelete(row)">{{ t('wms.common.delete') }}</el-button>
      </template>
    </CpListPage>

    <!-- 新建 -->
    <CpFormDialog
      v-model="createDialog"
      :title="t('wms.pallet.dlg.create')"
      width="600"
      :form="createForm"
      :rules="createRules"
      :submit="onCreate"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.common.save') }"
      @saved="reloadList"
    >
      <el-row :gutter="12">
        <el-col :span="12"><el-form-item :label="t('wms.common.product')" prop="productCd"><el-input v-model="createForm.productCd" maxlength="20" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.common.productName')"><el-input v-model="createForm.productName" maxlength="100" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.common.lot')" prop="lotNo"><el-input v-model="createForm.lotNo" maxlength="30" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.pallet.fld.cartonQty')" prop="cartonQty"><el-input-number v-model="createForm.cartonQty" :min="1" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.pallet.fld.weightKg')"><el-input-number v-model="createForm.weightKg" :min="0" :precision="2" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.pallet.fld.heightMm')"><el-input-number v-model="createForm.heightMm" :min="0" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.pallet.fld.maxStack')"><el-input-number v-model="createForm.maxStackLayers" :min="1" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.common.warehouse')" prop="warehouseCd"><el-input v-model="createForm.warehouseCd" maxlength="10" /></el-form-item></el-col>
        <el-col :span="24"><el-form-item :label="t('wms.common.location')" prop="locationCd"><el-input v-model="createForm.locationCd" maxlength="30" /></el-form-item></el-col>
        <el-col :span="24"><el-form-item :label="t('wms.common.remarks')"><el-input v-model="createForm.remarks" type="textarea" :rows="2" /></el-form-item></el-col>
      </el-row>
    </CpFormDialog>

    <!-- 移動 -->
    <CpFormDialog
      v-model="moveDialog"
      :title="t('wms.pallet.dlg.moveShip') + ' — ' + (moveTarget?.palletNo ?? '')"
      width="420"
      :form="moveForm"
      :rules="moveRules"
      :submit="onMove"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.common.confirm') }"
      @saved="reloadList"
    >
      <el-form-item :label="t('wms.pallet.fld.toLoc')" prop="toLocation">
        <el-input v-model="moveForm.toLocation" maxlength="30" />
      </el-form-item>
    </CpFormDialog>

    <!-- 出荷 -->
    <CpFormDialog
      v-model="shipDialog"
      :title="t('wms.pallet.dlg.markShipped') + ' — ' + (shipTarget?.palletNo ?? '')"
      width="420"
      :form="shipForm"
      :rules="shipRules"
      :submit="onShip"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.common.confirm') }"
      @saved="reloadList"
    >
      <el-form-item :label="t('wms.pallet.fld.outboundNo')" prop="outboundNo">
        <el-input v-model="shipForm.outboundNo" maxlength="25" />
      </el-form-item>
    </CpFormDialog>
  </CpPageShell>
</template>

<script setup lang="ts">
import { ref, reactive, computed } from 'vue'
import { ElMessage, ElMessageBox, type FormRules } from 'element-plus'
import { useI18n } from 'vue-i18n'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import CpFormDialog from '@/components/templates/CpFormDialog.vue'
import { type Tone } from '@/components/base/CpTag.vue'
import { palletApi } from '@/api/wms/paperIndustry'
import type { Pallet, PalletSearchQuery } from '@/types/wms/wms'
import { formatQty as fmtQty } from '@/utils/format'

const { t } = useI18n()

const total = ref<number>()
const listRef = ref<InstanceType<typeof CpListPage> | null>(null)
function reloadList() { listRef.value?.reload() }

const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.pallet.status.building'),
  1: t('wms.pallet.status.inStock'),
  2: t('wms.pallet.status.waitingShip'),
  3: t('wms.pallet.status.shipped'),
}))
// 原 statusTagOf(info/success/warning/primary) → 设计系统 Tone（保色）
function statusTone(s: number): Tone {
  return ({ 0: 'muted', 1: 'ok', 2: 'warn', 3: 'info' } as const)[s as 0] || 'muted'
}
function codeLabel(m: Record<number, string>, v: unknown): string {
  return m[v as number] || (v == null ? '' : String(v))
}
function formatQty(n: number | undefined | null) {
  if (n == null) return ''
  return fmtQty(n, 2)
}

const columns = computed<ListColumn[]>(() => [
  { prop: 'palletNo', label: t('wms.pallet.fld.no'), kind: 'mono', width: 180 },
  { prop: 'status', label: t('wms.common.status'), width: 110, kind: 'tag',
    map: (v) => ({ label: codeLabel(statusMap.value, v), tone: statusTone(v as number) }) },
  { prop: 'productCd', label: t('wms.common.product'), width: 140 },
  { prop: 'productName', label: t('wms.common.productName'), minWidth: 160, overflowTooltip: true },
  { prop: 'lotNo', label: t('wms.common.lot'), width: 140 },
  { prop: 'cartonQty', label: t('wms.pallet.fld.cartonQty'), width: 80, align: 'right' },
  { prop: 'weightKg', label: t('wms.pallet.fld.weightKg'), width: 100, align: 'right',
    map: (v) => ({ label: formatQty(v as number) }) },
  { prop: 'heightMm', label: t('wms.pallet.fld.heightMm'), width: 100, align: 'right' },
  { prop: 'maxStackLayers', label: t('wms.pallet.fld.maxStack'), width: 100, align: 'right' },
  { prop: 'warehouseCd', label: t('wms.common.warehouse'), width: 100 },
  { prop: 'locationCd', label: t('wms.common.location'), width: 140 },
  { prop: 'shippedOutboundNo', label: t('wms.pallet.fld.outboundNo'), width: 160 },
  { prop: '_action', label: t('wms.common.action'), width: 280, fixed: 'right' },
])

const filterLabels = computed(() => ({
  search: t('wms.common.search'),
  reset: t('wms.common.clear'),
}))

const searchFields = computed<FilterField[]>(() => [
  { key: 'palletNo', label: t('wms.pallet.fld.no'), type: 'text' },
  { key: 'productCd', label: t('wms.common.product'), type: 'text' },
  { key: 'lotNo', label: t('wms.common.lot'), type: 'text' },
  { key: 'warehouseCd', label: t('wms.common.warehouse'), type: 'text' },
  {
    key: 'status', label: t('wms.common.status'), type: 'select',
    options: Object.entries(statusMap.value).map(([v, l]) => ({ label: l, value: Number(v) })),
  },
])

const PAGE_CAP = 500
const fetchList: ListFetch = async ({ page, size, filters }) => {
  const f = filters as Record<string, unknown>
  const q: PalletSearchQuery = { pageSize: PAGE_CAP }
  if (f.palletNo) q.palletNo = String(f.palletNo)
  if (f.productCd) q.productCd = String(f.productCd)
  if (f.lotNo) q.lotNo = String(f.lotNo)
  if (f.warehouseCd) q.warehouseCd = String(f.warehouseCd)
  if (f.status !== undefined && f.status !== '' && f.status !== null) q.status = Number(f.status)
  const res = await palletApi.search(q)
  const all = res.data || []
  const start = (page - 1) * size
  return { rows: all.slice(start, start + size), total: all.length }
}

// —— 新建 ——
const createDialog = ref(false)
const createForm = reactive<Record<string, unknown>>({
  productCd: '', productName: '', lotNo: '', cartonQty: 1,
  weightKg: undefined, heightMm: undefined, maxStackLayers: undefined,
  warehouseCd: '', locationCd: '', remarks: '',
})
const createRules = computed<FormRules>(() => ({
  productCd: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
  lotNo: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
  cartonQty: [{ required: true, message: t('wms.common.required'), trigger: 'change' }],
  warehouseCd: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
  locationCd: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
}))
function openCreate() {
  Object.assign(createForm, {
    productCd: '', productName: '', lotNo: '', cartonQty: 1,
    weightKg: undefined, heightMm: undefined, maxStackLayers: undefined,
    warehouseCd: '', locationCd: '', remarks: '',
  })
  createDialog.value = true
}
async function onCreate() {
  const res = await palletApi.create({ ...createForm } as unknown as Pallet)
  ElMessage.success(`${t('wms.common.success')}: ${res.data.palletNo}`)
}

async function onComplete(row: Pallet) {
  try {
    await ElMessageBox.confirm(`${t('wms.pallet.btn.complete')}: ${row.palletNo}`, t('wms.common.confirm'), { type: 'warning' })
    await palletApi.completeBuilding(row.palletNo)
    ElMessage.success(t('wms.common.success'))
    reloadList()
  } catch { /* */ }
}

// —— 移動 ——
const moveDialog = ref(false)
const moveTarget = ref<Pallet | null>(null)
const moveForm = reactive<Record<string, unknown>>({ toLocation: '' })
const moveRules = computed<FormRules>(() => ({
  toLocation: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
}))
function openMove(row: Pallet) {
  moveTarget.value = row
  moveForm.toLocation = ''
  moveDialog.value = true
}
async function onMove() {
  await palletApi.moveToShipping(moveTarget.value!.palletNo, moveForm.toLocation as string)
  ElMessage.success(t('wms.common.success'))
}

// —— 出荷 ——
const shipDialog = ref(false)
const shipTarget = ref<Pallet | null>(null)
const shipForm = reactive<Record<string, unknown>>({ outboundNo: '' })
const shipRules = computed<FormRules>(() => ({
  outboundNo: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
}))
function openShip(row: Pallet) {
  shipTarget.value = row
  shipForm.outboundNo = ''
  shipDialog.value = true
}
async function onShip() {
  await palletApi.markShipped(shipTarget.value!.palletNo, shipForm.outboundNo as string)
  ElMessage.success(t('wms.common.success'))
}

async function onDelete(row: Pallet) {
  try {
    await ElMessageBox.confirm(`${t('wms.common.confirmDelete')}: ${row.palletNo}`, t('wms.common.confirm'), { type: 'warning' })
    await palletApi.delete(row.palletNo)
    ElMessage.success(t('wms.common.success'))
    reloadList()
  } catch { /* */ }
}
</script>
