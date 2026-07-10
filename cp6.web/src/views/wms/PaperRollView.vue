<!--
  原紙巻取管理 —— CpPageShell + CpListPage + 3×CpFormDialog 迁移（WMS 批次3）。
  状態列 kind:'tag'+map；コア径列 map(加 ″)；残長列走 col slot 保留进度条；操作走 col slot。
  幅(widthMm) 搜索用 FilterField type:'number'（契约扩展二轮 #10）；巾方向 select(T/Y)。
  入庫/消費/スリッター用 CpFormDialog（default slot 保留 input-number/select/switch/date）；スリッター 头部动作触发。
  in-place 变更(入庫/消費/スリッター/廃棄)后 listRef.reload() 保留当前筛选/页码。
-->
<template>
  <CpPageShell :title="t('wms.paperRoll.title')" :count="total">
    <template #actions>
      <el-button @click="openCreate">{{ t('wms.common.create') }}</el-button>
      <el-button v-permission="'wms-paper-roll:slit'" type="warning" @click="openSlit">{{ t('wms.paperRoll.btn.slit') }}</el-button>
    </template>

    <CpListPage
      ref="listRef"
      :columns="columns"
      :fetch="fetchList"
      :search-fields="searchFields"
      :filter-labels="filterLabels"
      @total-change="total = $event"
    >
      <template #col-remaining="{ row }">
        <div>{{ formatQty(row.remainingLengthM) }} / {{ formatQty(row.originalLengthM) }} m</div>
        <el-progress :percentage="row.originalLengthM ? Math.round(row.remainingLengthM / row.originalLengthM * 100) : 0" :stroke-width="6" :show-text="false" />
      </template>

      <template #col-_action="{ row }">
        <el-button v-if="row.status !== 3" v-permission="'wms-paper-roll:consume'" link type="primary" size="small" @click="openConsume(row)">{{ t('wms.paperRoll.btn.consume') }}</el-button>
        <el-button v-if="row.status !== 3" v-permission="'wms-paper-roll:dispose'" link type="danger" size="small" @click="onDispose(row)">{{ t('wms.paperRoll.btn.dispose') }}</el-button>
      </template>
    </CpListPage>

    <!-- 入庫 -->
    <CpFormDialog
      v-model="createDialog"
      :title="t('wms.paperRoll.dlg.create')"
      width="600"
      :form="createForm"
      :rules="createRules"
      :submit="onCreate"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.common.save') }"
      @saved="reloadList"
    >
      <el-row :gutter="12">
        <el-col :span="12"><el-form-item :label="t('wms.paperRoll.fld.grade')" prop="paperGrade"><el-input v-model="createForm.paperGrade" maxlength="10" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.paperRoll.fld.widthMm')" prop="widthMm"><el-input-number v-model="createForm.widthMm" :min="1" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.paperRoll.fld.basis')"><el-input-number v-model="createForm.basisWeight" :min="0" :precision="2" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.paperRoll.fld.grain')"><el-select v-model="createForm.grainDirection" style="width: 100%"><el-option label="T" value="T" /><el-option label="Y" value="Y" /></el-select></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.paperRoll.fld.lengthM')" prop="originalLengthM"><el-input-number v-model="createForm.originalLengthM" :min="0" :precision="2" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.paperRoll.fld.core')"><el-input-number v-model="createForm.coreDiameterInch" :min="1" :precision="2" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.common.warehouse')" prop="warehouseCd"><el-input v-model="createForm.warehouseCd" maxlength="10" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.common.location')" prop="locationCd"><el-input v-model="createForm.locationCd" maxlength="30" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.paperRoll.fld.mfgDate')"><el-date-picker v-model="createForm.mfgDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.paperRoll.fld.mfgLot')"><el-input v-model="createForm.mfgLotNo" maxlength="30" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.paperRoll.fld.disposeTh')"><el-input-number v-model="createForm.disposeThresholdM" :min="0" :precision="2" controls-position="right" style="width: 100%" /></el-form-item></el-col>
      </el-row>
    </CpFormDialog>

    <!-- 消費 -->
    <CpFormDialog
      v-model="consumeDialog"
      :title="t('wms.paperRoll.btn.consume') + ' — ' + (consumeTarget?.rollNo ?? '')"
      width="420"
      :form="consumeForm"
      :rules="consumeRules"
      :submit="onConsume"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.paperRoll.btn.consume') }"
      @saved="reloadList"
    >
      <el-form-item :label="t('wms.paperRoll.fld.remaining')">
        <CpTag tone="info">{{ formatQty(consumeTarget?.remainingLengthM) }} m</CpTag>
      </el-form-item>
      <el-form-item :label="t('wms.paperRoll.fld.consumeLen')" prop="consumeLen">
        <el-input-number v-model="consumeForm.consumeLen" :min="0" :precision="2" controls-position="right" style="width: 100%" />
      </el-form-item>
    </CpFormDialog>

    <!-- スリッター -->
    <CpFormDialog
      v-model="slitDialog"
      :title="t('wms.paperRoll.btn.slit')"
      width="500"
      :form="slitForm"
      :rules="slitRules"
      :submit="onSlit"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.paperRoll.btn.slit') }"
      @saved="reloadList"
    >
      <el-form-item :label="t('wms.paperRoll.fld.parentRoll')" prop="parentRollNo">
        <el-input v-model="slitForm.parentRollNo" :placeholder="t('wms.paperRoll.msg.parentHint')" maxlength="25" />
      </el-form-item>
      <el-form-item :label="t('wms.paperRoll.fld.childWidths')" prop="childWidthsStr">
        <el-input v-model="slitForm.childWidthsStr" :placeholder="t('例: {sample}', { sample: '905,390' })" />
        <span class="cp-hint">{{ t('wms.paperRoll.msg.widthsHint') }}</span>
      </el-form-item>
      <el-form-item :label="t('wms.paperRoll.fld.keepRemnant')">
        <el-switch v-model="slitForm.keepRemnant" />
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
import CpTag, { type Tone } from '@/components/base/CpTag.vue'
import { paperRollApi } from '@/api/wms/paperIndustry'
import type { PaperRoll, PaperRollSearchQuery } from '@/types/wms/wms'
import { formatQty as fmtQty } from '@/utils/format'

const { t } = useI18n()

const total = ref<number>()
const listRef = ref<InstanceType<typeof CpListPage> | null>(null)
function reloadList() { listRef.value?.reload() }

const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.paperRoll.status.inStock'),
  1: t('wms.paperRoll.status.inUse'),
  2: t('wms.paperRoll.status.remnant'),
  3: t('wms.paperRoll.status.disposed'),
}))
// 原 statusTagOf(success/primary/warning/danger) → 设计系统 Tone（保色）
function statusTone(s: number): Tone {
  return ({ 0: 'ok', 1: 'info', 2: 'warn', 3: 'danger' } as const)[s as 0] || 'muted'
}
function codeLabel(m: Record<number, string>, v: unknown): string {
  return m[v as number] || (v == null ? '' : String(v))
}
function formatQty(n: number | undefined | null) {
  if (n == null) return '0'
  return fmtQty(n, 2)
}

const columns = computed<ListColumn[]>(() => [
  { prop: 'rollNo', label: t('wms.paperRoll.fld.rollNo'), kind: 'mono', width: 200 },
  { prop: 'status', label: t('wms.common.status'), width: 100, kind: 'tag',
    map: (v) => ({ label: codeLabel(statusMap.value, v), tone: statusTone(v as number) }) },
  { prop: 'paperGrade', label: t('wms.paperRoll.fld.grade'), width: 80 },
  { prop: 'widthMm', label: t('wms.paperRoll.fld.widthMm'), width: 80, align: 'right' },
  { prop: 'grainDirection', label: t('wms.paperRoll.fld.grain'), width: 60, align: 'center' },
  { prop: 'basisWeight', label: t('wms.paperRoll.fld.basis'), width: 90, align: 'right' },
  { prop: 'remaining', label: t('wms.paperRoll.fld.remaining'), width: 160 },
  { prop: 'coreDiameterInch', label: t('wms.paperRoll.fld.core'), width: 80, align: 'right',
    map: (v) => ({ label: v == null ? '' : `${v}″` }) },
  { prop: 'locationCd', label: t('wms.common.location'), width: 140 },
  { prop: 'parentRollNo', label: t('wms.paperRoll.fld.parentRoll'), width: 180 },
  { prop: '_action', label: t('wms.common.action'), width: 200, fixed: 'right' },
])

const filterLabels = computed(() => ({
  search: t('wms.common.search'),
  reset: t('wms.common.clear'),
}))

const searchFields = computed<FilterField[]>(() => [
  { key: 'rollNo', label: t('wms.paperRoll.fld.rollNo'), type: 'text' },
  { key: 'paperGrade', label: t('wms.paperRoll.fld.grade'), type: 'text' },
  { key: 'widthMm', label: t('wms.paperRoll.fld.widthMm'), type: 'number', min: 0 },
  {
    key: 'grainDirection', label: t('wms.paperRoll.fld.grain'), type: 'select',
    options: [{ label: 'T', value: 'T' }, { label: 'Y', value: 'Y' }],
  },
  {
    key: 'status', label: t('wms.common.status'), type: 'select',
    options: Object.entries(statusMap.value).map(([v, l]) => ({ label: l, value: Number(v) })),
  },
])

const PAGE_CAP = 500
const fetchList: ListFetch = async ({ page, size, filters }) => {
  const f = filters as Record<string, unknown>
  const q: PaperRollSearchQuery = { pageSize: PAGE_CAP }
  if (f.rollNo) q.rollNo = String(f.rollNo)
  if (f.paperGrade) q.paperGrade = String(f.paperGrade)
  if (f.widthMm !== undefined && f.widthMm !== null && f.widthMm !== '') q.widthMm = Number(f.widthMm)
  if (f.grainDirection) q.grainDirection = String(f.grainDirection)
  if (f.status !== undefined && f.status !== '' && f.status !== null) q.status = Number(f.status)
  const res = await paperRollApi.search(q)
  const all = res.data || []
  const start = (page - 1) * size
  return { rows: all.slice(start, start + size), total: all.length }
}

// —— 入庫 ——
const createDialog = ref(false)
const createForm = reactive<Record<string, unknown>>({
  paperGrade: '', widthMm: 905, basisWeight: 280, grainDirection: 'T',
  originalLengthM: 0, coreDiameterInch: 3, warehouseCd: '', locationCd: '',
  mfgDate: undefined, mfgLotNo: '', disposeThresholdM: undefined,
})
const createRules = computed<FormRules>(() => ({
  paperGrade: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
  widthMm: [{ required: true, message: t('wms.common.required'), trigger: 'change' }],
  originalLengthM: [{ required: true, message: t('wms.common.required'), trigger: 'change' }],
  warehouseCd: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
  locationCd: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
}))
function openCreate() {
  Object.assign(createForm, {
    paperGrade: '', widthMm: 905, basisWeight: 280, grainDirection: 'T',
    originalLengthM: 0, coreDiameterInch: 3, warehouseCd: '', locationCd: '',
    mfgDate: undefined, mfgLotNo: '', disposeThresholdM: undefined,
  })
  createDialog.value = true
}
async function onCreate() {
  const res = await paperRollApi.create({ ...createForm } as unknown as PaperRoll)
  ElMessage.success(`${t('wms.common.success')}: ${res.data.rollNo}`)
}

// —— 消費 ——
const consumeDialog = ref(false)
const consumeTarget = ref<PaperRoll | null>(null)
const consumeForm = reactive<Record<string, unknown>>({ consumeLen: 0 })
const consumeRules = computed<FormRules>(() => ({
  consumeLen: [{ required: true, message: t('wms.common.required'), trigger: 'change' },
    { validator: (_r, v, cb) => (Number(v) > 0 ? cb() : cb(new Error(t('wms.common.required')))), trigger: 'change' }],
}))
function openConsume(row: PaperRoll) {
  consumeTarget.value = row
  consumeForm.consumeLen = 0
  consumeDialog.value = true
}
async function onConsume() {
  await paperRollApi.consume(consumeTarget.value!.rollNo, consumeForm.consumeLen as number)
  ElMessage.success(t('wms.common.success'))
}

// —— スリッター ——
const slitDialog = ref(false)
const slitForm = reactive<Record<string, unknown>>({ parentRollNo: '', childWidthsStr: '', keepRemnant: true })
const slitRules = computed<FormRules>(() => ({
  parentRollNo: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
  childWidthsStr: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
}))
function openSlit() {
  Object.assign(slitForm, { parentRollNo: '', childWidthsStr: '', keepRemnant: true })
  slitDialog.value = true
}
async function onSlit() {
  const widths = String(slitForm.childWidthsStr).split(',').map(s => parseInt(s.trim())).filter(n => n > 0)
  if (widths.length === 0) { throw new Error(t('wms.common.required')) }
  const res = await paperRollApi.slit({
    parentRollNo: slitForm.parentRollNo as string,
    childWidths: widths,
    keepRemnant: slitForm.keepRemnant as boolean,
  })
  ElMessage.success(`${t('wms.common.success')}: ${res.data.createdRolls.length} ${t('wms.paperRoll.msg.rollsCreated')}`)
}

async function onDispose(row: PaperRoll) {
  try {
    await ElMessageBox.confirm(`${t('wms.paperRoll.btn.dispose')}: ${row.rollNo}`, t('wms.common.confirm'), { type: 'warning' })
    await paperRollApi.dispose(row.rollNo)
    ElMessage.success(t('wms.common.success'))
    reloadList()
  } catch { /* */ }
}
</script>

<style scoped>
.cp-hint { color: var(--cp-muted); }
</style>
