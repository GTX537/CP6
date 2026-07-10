<!--
  WCS タスク一覧 —— CpPageShell + CpListPage + CpFormDialog 迁移（WMS 批次4）。
  查询列表页：taskNo/taskType/deviceCd/status 四搜索项 → CpFilterBar；状態列 kind:'tag'+map；種別列纯 map（原页无 tag 视觉）；
  優先度/From/To/数量 用 col slot 保原样（条件 tag / 复合 WH·Loc / formatQty）；作成/完了は datetime 原样文本（不 slice）。
  新建/派発/失败三弹窗迁 CpFormDialog；行操作 start/complete 直接调用后 listRef.reload()（数据源无 total → 客户端分页）。
-->
<template>
  <CpPageShell :title="t('wms.wcs.title')" :count="total">
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
      <template #col-priority="{ row }">
        <CpTag v-if="row.priority === 3" tone="danger">{{ t('急') }}</CpTag>
        <CpTag v-else-if="row.priority === 2" tone="warn">↑</CpTag>
        <span v-else>—</span>
      </template>
      <template #col-_from="{ row }">{{ row.fromWarehouseCd || '' }}/{{ row.fromLocationCd || '' }}</template>
      <template #col-_to="{ row }">{{ row.toWarehouseCd || '' }}/{{ row.toLocationCd || '' }}</template>
      <template #col-qty="{ row }">{{ row.qty != null ? formatQty(row.qty) : '' }}</template>
      <template #col-_action="{ row }">
        <el-button v-if="row.status === 0" link type="primary" size="small" @click="openDispatch(row)">{{ t('wms.wcs.btn.dispatch') }}</el-button>
        <el-button v-if="row.status === 1" link type="warning" size="small" @click="onStart(row)">{{ t('wms.wcs.btn.start') }}</el-button>
        <el-button v-if="row.status === 2" v-permission="'wms-wcs-task:complete'" link type="success" size="small" @click="onComplete(row)">{{ t('wms.wcs.btn.complete') }}</el-button>
        <el-button v-if="row.status === 1 || row.status === 2" link type="danger" size="small" @click="openFail(row)">{{ t('wms.wcs.btn.fail') }}</el-button>
      </template>
    </CpListPage>

    <!-- 新建 -->
    <CpFormDialog
      v-model="createDialog"
      :title="t('wms.wcs.dlg.create')"
      width="600"
      :form="createForm"
      :rules="createRules"
      :submit="onCreate"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.common.save') }"
      @saved="listRef?.reload()"
    >
      <el-row :gutter="12">
        <el-col :span="12"><el-form-item :label="t('wms.wcs.fld.type')" prop="taskType">
          <el-select v-model="createForm.taskType">
            <el-option v-for="(l, v) in typeMap" :key="v" :label="l" :value="v" />
          </el-select>
        </el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.wcs.fld.priority')">
          <el-input-number v-model="createForm.priority" :min="1" :max="3" controls-position="right" style="width: 100%" />
        </el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.wcs.fld.related')">
          <el-input v-model="createForm.relatedNo" maxlength="25" />
        </el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="'Related Type'">
          <el-input v-model="createForm.relatedType" maxlength="20" />
        </el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="'From WH'"><el-input v-model="createForm.fromWarehouseCd" maxlength="10" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="'From Loc'"><el-input v-model="createForm.fromLocationCd" maxlength="30" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="'To WH'"><el-input v-model="createForm.toWarehouseCd" maxlength="10" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="'To Loc'"><el-input v-model="createForm.toLocationCd" maxlength="30" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.common.product')"><el-input v-model="createForm.productCd" maxlength="20" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.common.lot')"><el-input v-model="createForm.lotNo" maxlength="30" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.common.qty')"><el-input-number v-model="createForm.qty" :min="0" :precision="4" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="'Unit'"><el-input v-model="createForm.unitCd" maxlength="10" /></el-form-item></el-col>
        <el-col :span="24"><el-form-item :label="t('wms.common.remarks')"><el-input v-model="createForm.remarks" type="textarea" :rows="2" /></el-form-item></el-col>
      </el-row>
    </CpFormDialog>

    <!-- 派発 -->
    <CpFormDialog
      v-model="dispatchDialog"
      :title="t('wms.wcs.dlg.dispatch') + ' — ' + (dispatchTarget?.taskNo ?? '')"
      width="420"
      :form="dispatchForm"
      :rules="dispatchRules"
      :submit="onDispatch"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.wcs.btn.dispatch') }"
      @saved="listRef?.reload()"
    >
      <el-form-item :label="t('wms.wcs.fld.device')" prop="device">
        <el-input v-model="dispatchForm.device" maxlength="20" placeholder="AGV01 / CONV-A / ..." />
      </el-form-item>
    </CpFormDialog>

    <!-- 失败 -->
    <CpFormDialog
      v-model="failDialog"
      :title="t('wms.wcs.dlg.fail') + ' — ' + (failTarget?.taskNo ?? '')"
      width="420"
      :form="failForm"
      :rules="failRules"
      :submit="onFail"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.wcs.btn.fail') }"
      @saved="listRef?.reload()"
    >
      <el-form-item :label="t('wms.wcs.fld.error')" prop="error">
        <el-input v-model="failForm.error" type="textarea" :rows="3" />
      </el-form-item>
    </CpFormDialog>
  </CpPageShell>
</template>

<script setup lang="ts">
import { ref, reactive, computed } from 'vue'
import { ElMessage, type FormRules } from 'element-plus'
import { useI18n } from 'vue-i18n'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import CpFormDialog from '@/components/templates/CpFormDialog.vue'
import CpTag, { type Tone } from '@/components/base/CpTag.vue'
import { wcsApi } from '@/api/wms/connectivity'
import type { WcsTask } from '@/types/wms/wms'
import { formatQty } from '@/utils/format'

const { t } = useI18n()

const total = ref<number>()
const listRef = ref<InstanceType<typeof CpListPage>>()

const typeMap = computed<Record<string, string>>(() => ({
  MOVE: t('wms.wcs.type.move'),
  PICK: t('wms.wcs.type.pick'),
  PUT: t('wms.wcs.type.put'),
  COUNT: t('wms.wcs.type.count'),
}))
const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.wcs.status.created'),
  1: t('wms.wcs.status.dispatched'),
  2: t('wms.wcs.status.executing'),
  3: t('wms.wcs.status.completed'),
  9: t('wms.wcs.status.failed'),
}))
// 原 statusTag(info/warning/primary/success/danger) → 设计系统 Tone（保色）
function statusTone(s: number): Tone {
  return ({ 0: 'info', 1: 'warn', 2: 'info', 3: 'ok', 9: 'danger' } as const)[s as 0] || 'info'
}
function codeLabel(m: Record<string | number, string>, v: unknown): string {
  return m[v as number] || (v == null ? '' : String(v))
}

const columns = computed<ListColumn[]>(() => [
  { prop: 'taskNo', label: t('wms.wcs.fld.no'), kind: 'mono', width: 170 },
  { prop: 'status', label: t('wms.common.status'), width: 110, kind: 'tag',
    map: (v) => ({ label: codeLabel(statusMap.value, v), tone: statusTone(v as number) }) },
  { prop: 'taskType', label: t('wms.wcs.fld.type'), width: 100,
    map: (v) => ({ label: codeLabel(typeMap.value, v) }) },
  { prop: 'priority', label: t('wms.wcs.fld.priority'), width: 80, align: 'center' },
  { prop: 'deviceCd', label: t('wms.wcs.fld.device'), width: 100 },
  { prop: '_from', label: t('wms.wcs.fld.from'), width: 160 },
  { prop: '_to', label: t('wms.wcs.fld.to'), width: 160 },
  { prop: 'productCd', label: t('wms.common.product'), width: 120 },
  { prop: 'qty', label: t('wms.common.qty'), width: 100, align: 'right' },
  { prop: 'relatedNo', label: t('wms.wcs.fld.related'), width: 160 },
  { prop: 'createdAt', label: t('wms.wcs.fld.created'), width: 160 },
  { prop: 'completedAt', label: t('wms.wcs.fld.completed'), width: 160 },
  { prop: '_action', label: t('wms.common.action'), width: 280, fixed: 'right' },
])

const filterLabels = computed(() => ({
  search: t('wms.common.search'),
  reset: t('wms.common.clear'),
}))

const searchFields = computed<FilterField[]>(() => [
  { key: 'taskNo', label: t('wms.wcs.fld.no'), type: 'text' },
  {
    key: 'taskType', label: t('wms.wcs.fld.type'), type: 'select',
    options: Object.entries(typeMap.value).map(([v, l]) => ({ label: l, value: v })),
  },
  { key: 'deviceCd', label: t('wms.wcs.fld.device'), type: 'text' },
  {
    key: 'status', label: t('wms.common.status'), type: 'select',
    options: Object.entries(statusMap.value).map(([v, l]) => ({ label: l, value: Number(v) })),
  },
])

const fetchList: ListFetch = async ({ page, size, filters }) => {
  const f = filters as Record<string, unknown>
  const q: Record<string, unknown> = { pageSize: 100 }
  if (f.taskNo) q.taskNo = String(f.taskNo)
  if (f.taskType) q.taskType = String(f.taskType)
  if (f.deviceCd) q.deviceCd = String(f.deviceCd)
  if (f.status !== undefined && f.status !== '') q.status = Number(f.status)
  const res = await wcsApi.search(q)
  const all = res.data || []
  const start = (page - 1) * size
  return { rows: all.slice(start, start + size), total: all.length }
}

// —— 新建 ——
const createDialog = ref(false)
const createForm = reactive<Record<string, unknown>>({ taskType: 'MOVE', priority: 1, qty: 0 })
const createRules = computed<FormRules>(() => ({
  taskType: [{ required: true, message: t('wms.common.required'), trigger: 'change' }],
}))
function openCreate() {
  Object.assign(createForm, {
    taskType: 'MOVE', priority: 1, relatedNo: '', relatedType: '',
    fromWarehouseCd: '', fromLocationCd: '', toWarehouseCd: '', toLocationCd: '',
    productCd: '', lotNo: '', qty: 0, unitCd: '', remarks: '',
  })
  createDialog.value = true
}
async function onCreate() {
  const res = await wcsApi.create(createForm)
  ElMessage.success(`${t('wms.common.success')}: ${res.data.taskNo}`)
}

// —— 派発 ——
const dispatchDialog = ref(false)
const dispatchTarget = ref<WcsTask | null>(null)
const dispatchForm = reactive<Record<string, unknown>>({ device: '' })
const dispatchRules = computed<FormRules>(() => ({
  device: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
}))
function openDispatch(row: WcsTask) {
  dispatchTarget.value = row
  dispatchForm.device = ''
  dispatchDialog.value = true
}
async function onDispatch() {
  await wcsApi.dispatch(dispatchTarget.value!.taskNo, String(dispatchForm.device))
  ElMessage.success(t('wms.common.success'))
}

// —— 行操作：開始 / 完了 ——
async function onStart(row: WcsTask) {
  await wcsApi.start(row.taskNo)
  ElMessage.success(t('wms.common.success'))
  listRef.value?.reload()
}
async function onComplete(row: WcsTask) {
  await wcsApi.complete(row.taskNo)
  ElMessage.success(t('wms.common.success'))
  listRef.value?.reload()
}

// —— 失败 ——
const failDialog = ref(false)
const failTarget = ref<WcsTask | null>(null)
const failForm = reactive<Record<string, unknown>>({ error: '' })
const failRules = computed<FormRules>(() => ({
  error: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
}))
function openFail(row: WcsTask) {
  failTarget.value = row
  failForm.error = ''
  failDialog.value = true
}
async function onFail() {
  await wcsApi.fail(failTarget.value!.taskNo, String(failForm.error))
  ElMessage.success(t('wms.common.success'))
}
</script>
