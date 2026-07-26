<!--
  サンプル在庫 —— CpPageShell + CpListPage + 2×CpFormDialog 迁移（WMS 批次3）。
  状態列 kind:'tag'+map；種別/数量列 map（数量拼 unitCd，无 tag）；expectedReturnDate 走 col slot 保留逾期红字；操作走 col slot。
  overdueOnly 复选（CpFilterBar 无 boolean 字段类型，缺口 #15）→ 放 CpListPage toolbar slot，fetch 闭包读取 + 切换后 reload()。
  新建/借出用 CpFormDialog（default slot 保留 input-number/select/date-picker）；必填(種別/数量、貸出先)改 el-form rules。
  in-place 变更(新建/借出/返却/廃棄)后 listRef.reload() 保留当前筛选/页码。
-->
<template>
  <CpPageShell :title="t('wms.sample.title')" :count="total">
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
      <template #toolbar>
        <el-checkbox v-model="overdueOnly" @change="reloadList">{{ t('wms.sample.btn.overdue') }}</el-checkbox>
      </template>

      <template #col-expectedReturnDate="{ row }">
        <span :class="overdueClass(row)">{{ row.expectedReturnDate || '—' }}</span>
      </template>

      <template #col-_action="{ row }">
        <el-button v-if="row.status === 0 || row.status === 2" v-permission="'wms-sample-stock:lend'" link type="primary" size="small" @click="openLend(row)">{{ t('wms.sample.btn.lend') }}</el-button>
        <el-button v-if="row.status === 1" v-permission="'wms-sample-stock:return'" link type="success" size="small" @click="onReturn(row)">{{ t('wms.sample.btn.return') }}</el-button>
        <el-button v-if="row.status !== 3" v-permission="'wms-sample-stock:expire'" link type="danger" size="small" @click="onExpire(row)">{{ t('wms.sample.btn.expire') }}</el-button>
      </template>
    </CpListPage>

    <!-- 新建 -->
    <CpFormDialog
      v-model="createDialog"
      :title="t('wms.sample.dlg.create')"
      width="600"
      :form="createForm"
      :rules="createRules"
      :submit="onCreate"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.common.save') }"
      @saved="reloadList"
    >
      <el-row :gutter="12">
        <el-col :span="12"><el-form-item :label="t('wms.sample.fld.type')" prop="sampleType">
          <el-select v-model="createForm.sampleType" style="width: 100%">
            <el-option v-for="(l, v) in typeMap" :key="v" :label="l" :value="v" />
          </el-select>
        </el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.sample.fld.qty')" prop="quantity"><el-input-number v-model="createForm.quantity" :min="0" :precision="4" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.sample.fld.customer')"><el-input v-model="createForm.customerCd" maxlength="20" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.vmi.fld.customerName')"><el-input v-model="createForm.customerName" maxlength="100" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.sample.fld.product')"><el-input v-model="createForm.productCd" maxlength="20" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.common.productName')"><el-input v-model="createForm.productName" maxlength="100" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item label="Unit"><el-input v-model="createForm.unitCd" maxlength="10" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.common.warehouse')"><el-input v-model="createForm.warehouseCd" maxlength="10" /></el-form-item></el-col>
        <el-col :span="24"><el-form-item :label="t('wms.common.location')"><el-input v-model="createForm.locationCd" maxlength="30" /></el-form-item></el-col>
        <el-col :span="24"><el-form-item :label="t('wms.common.remarks')"><el-input v-model="createForm.remarks" type="textarea" :rows="2" /></el-form-item></el-col>
      </el-row>
    </CpFormDialog>

    <!-- 借出 -->
    <CpFormDialog
      v-model="lendDialog"
      :title="t('wms.sample.dlg.lend') + ' — ' + (lendTarget?.sampleNo ?? '')"
      width="460"
      :form="lendForm"
      :rules="lendRules"
      :submit="onLend"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.sample.btn.lend') }"
      @saved="reloadList"
    >
      <el-form-item :label="t('wms.sample.fld.lentTo')" prop="lentTo">
        <el-input v-model="lendForm.lentTo" maxlength="60" />
      </el-form-item>
      <el-form-item :label="t('wms.sample.fld.expReturn')">
        <el-date-picker v-model="lendForm.expectedReturnDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
      </el-form-item>
    </CpFormDialog>
  </CpPageShell>
</template>

<script setup lang="ts">
import { ref, reactive, computed } from 'vue'
import { ElMessage, ElMessageBox, type FormRules } from 'element-plus'
import { useI18n } from 'vue-i18n'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch, type ListPageExpose } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import CpFormDialog from '@/components/templates/CpFormDialog.vue'
import { type Tone } from '@/components/base/CpTag.vue'
import { sampleApi } from '@/api/wms/paperIndustry2'
import type { SampleStock, SampleSearchQuery } from '@/types/wms/wms'
import { formatQty as fmtQty } from '@/utils/format'

const { t } = useI18n()

const total = ref<number>()
const listRef = ref<ListPageExpose | null>(null)
function reloadList() { listRef.value?.reload() }

// overdueOnly：CpFilterBar 无 boolean 字段类型（缺口 #15）→ toolbar slot 复选，fetch 闭包读取
const overdueOnly = ref(false)

const typeMap = computed<Record<string, string>>(() => ({
  PROTO: t('wms.sample.type.proto'),
  COLOR: t('wms.sample.type.color'),
  DUMMY: t('wms.sample.type.dummy'),
  OTHER: t('wms.sample.type.other'),
}))
const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.sample.status.inStock'),
  1: t('wms.sample.status.lentOut'),
  2: t('wms.sample.status.returned'),
  3: t('wms.sample.status.expired'),
}))
// 原 statusTagOf(success/warning/primary/info) → 设计系统 Tone（保色）
function statusTone(s: number): Tone {
  return ({ 0: 'ok', 1: 'warn', 2: 'info', 3: 'muted' } as const)[s as 0] || 'muted'
}
function codeLabel(m: Record<number, string>, v: unknown): string {
  return m[v as number] || (v == null ? '' : String(v))
}
function formatQty(n: number | undefined | null) {
  if (n == null) return '0'
  return fmtQty(n, 4)
}
function overdueClass(row: SampleStock): string {
  if (row.status !== 1 || !row.expectedReturnDate) return ''
  return new Date(row.expectedReturnDate) < new Date() ? 'sample-overdue' : ''
}

const columns = computed<ListColumn<SampleStock>[]>(() => [
  { prop: 'sampleNo', label: t('wms.sample.fld.no'), kind: 'mono', width: 180 },
  { prop: 'status', label: t('wms.common.status'), width: 100, kind: 'tag',
    map: (v) => ({ label: codeLabel(statusMap.value, v), tone: statusTone(v as number) }) },
  { prop: 'sampleType', label: t('wms.sample.fld.type'), width: 100,
    map: (v) => ({ label: typeMap.value[v as string] || (v == null ? '' : String(v)) }) },
  { prop: 'customerCd', label: t('wms.sample.fld.customer'), width: 120 },
  { prop: 'customerName', label: t('wms.vmi.fld.customerName'), minWidth: 140, overflowTooltip: true },
  { prop: 'productCd', label: t('wms.sample.fld.product'), width: 120 },
  { prop: 'productName', label: t('wms.common.productName'), minWidth: 140, overflowTooltip: true },
  { prop: 'quantity', label: t('wms.sample.fld.qty'), width: 100, align: 'right',
    map: (v, row) => ({ label: `${formatQty(v as number)} ${(row as SampleStock).unitCd ?? ''}` }) },
  { prop: 'locationCd', label: t('wms.common.location'), width: 140 },
  { prop: 'lentTo', label: t('wms.sample.fld.lentTo'), width: 140 },
  { prop: 'lentAt', label: t('wms.sample.fld.lentAt'), width: 170 },
  { prop: 'expectedReturnDate', label: t('wms.sample.fld.expReturn'), width: 130 },
  { prop: 'returnedAt', label: t('wms.sample.fld.returnedAt'), width: 170 },
  { prop: '_action', label: t('wms.common.action'), width: 240, fixed: 'right' },
])

const filterLabels = computed(() => ({
  search: t('wms.common.search'),
  reset: t('wms.common.clear'),
}))

const searchFields = computed<FilterField[]>(() => [
  { key: 'sampleNo', label: t('wms.sample.fld.no'), type: 'text' },
  {
    key: 'sampleType', label: t('wms.sample.fld.type'), type: 'select',
    options: Object.entries(typeMap.value).map(([v, l]) => ({ label: l, value: v })),
  },
  { key: 'customerCd', label: t('wms.sample.fld.customer'), type: 'text' },
  { key: 'productCd', label: t('wms.sample.fld.product'), type: 'text' },
  {
    key: 'status', label: t('wms.common.status'), type: 'select',
    options: Object.entries(statusMap.value).map(([v, l]) => ({ label: l, value: Number(v) })),
  },
])

const PAGE_CAP = 500
const fetchList: ListFetch<SampleStock> = async ({ page, size, filters }) => {
  const f = filters as Record<string, unknown>
  const q: SampleSearchQuery = { pageSize: PAGE_CAP, overdueOnly: overdueOnly.value }
  if (f.sampleNo) q.sampleNo = String(f.sampleNo)
  if (f.sampleType) q.sampleType = String(f.sampleType)
  if (f.customerCd) q.customerCd = String(f.customerCd)
  if (f.productCd) q.productCd = String(f.productCd)
  if (f.status !== undefined && f.status !== '' && f.status !== null) q.status = Number(f.status)
  const res = await sampleApi.search(q)
  const all = res.data || []
  const start = (page - 1) * size
  return { rows: all.slice(start, start + size), total: all.length }
}

// —— 新建 ——
const createDialog = ref(false)
const createForm = reactive<Record<string, unknown>>({
  sampleType: 'PROTO', customerCd: '', customerName: '', productCd: '', productName: '',
  quantity: 1, unitCd: 'PCS', warehouseCd: '', locationCd: '', remarks: '',
})
const createRules = computed<FormRules>(() => ({
  sampleType: [{ required: true, message: t('wms.common.required'), trigger: 'change' }],
  quantity: [{ required: true, message: t('wms.common.required'), trigger: 'change' }],
}))
function openCreate() {
  Object.assign(createForm, {
    sampleType: 'PROTO', customerCd: '', customerName: '', productCd: '', productName: '',
    quantity: 1, unitCd: 'PCS', warehouseCd: '', locationCd: '', remarks: '',
  })
  createDialog.value = true
}
async function onCreate() {
  const res = await sampleApi.create({ ...createForm } as unknown as SampleStock)
  ElMessage.success(`${t('wms.common.success')}: ${res.data.sampleNo}`)
}

// —— 借出 ——
const lendDialog = ref(false)
const lendTarget = ref<SampleStock | null>(null)
const lendForm = reactive<Record<string, unknown>>({ lentTo: '', expectedReturnDate: undefined })
const lendRules = computed<FormRules>(() => ({
  lentTo: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
}))
function openLend(row: SampleStock) {
  lendTarget.value = row
  Object.assign(lendForm, { lentTo: '', expectedReturnDate: undefined })
  lendDialog.value = true
}
async function onLend() {
  await sampleApi.lend(lendTarget.value!.sampleNo, lendForm.lentTo as string, lendForm.expectedReturnDate as string | undefined)
  ElMessage.success(t('wms.common.success'))
}

async function onReturn(row: SampleStock) {
  try {
    await ElMessageBox.confirm(`${t('wms.sample.btn.return')}: ${row.sampleNo}`, t('wms.common.confirm'), { type: 'warning' })
    await sampleApi.return(row.sampleNo)
    ElMessage.success(t('wms.common.success'))
    reloadList()
  } catch { /* */ }
}
async function onExpire(row: SampleStock) {
  try {
    await ElMessageBox.confirm(`${t('wms.sample.btn.expire')}: ${row.sampleNo}`, t('wms.common.confirm'), { type: 'warning' })
    await sampleApi.expire(row.sampleNo)
    ElMessage.success(t('wms.common.success'))
    reloadList()
  } catch { /* */ }
}
</script>

<style scoped>
.sample-overdue { color: var(--cp-danger); font-weight: bold; }
</style>
