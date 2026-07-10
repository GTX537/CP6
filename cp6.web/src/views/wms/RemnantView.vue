<!--
  端材在庫 —— CpPageShell + CpListPage + 2×CpFormDialog 迁移（WMS 批次5）。
  状態列 kind:'tag'+map；種別列 map（无 tag，仅换文案）；寸法列 kind:'num'；数量列 map（拼 unitCd）；操作走 col slot（状态条件按钮）。
  新建/予約用 CpFormDialog（default slot 保留 input-number/select/textarea）；必填改 el-form rules。
  再利用検索(match) 是「查询工具+结果表」而非编辑表单，模板表达不了 → 保留原 el-dialog（逃生舱）。
  端材列表原本单表滚动无分页 → :paginated="false"。in-place 变更(新建/予約/解除/使用/廃棄)后 listRef.reload()。
-->
<template>
  <CpPageShell :title="t('wms.remnant.title')" :count="total">
    <template #actions>
      <el-button @click="openCreate">{{ t('wms.common.create') }}</el-button>
      <el-button type="success" @click="matchDialog = true">{{ t('wms.remnant.btn.match') }}</el-button>
    </template>

    <CpListPage
      ref="listRef"
      :columns="columns"
      :fetch="fetchList"
      :search-fields="searchFields"
      :filter-labels="filterLabels"
      :paginated="false"
      @total-change="total = $event"
    >
      <template #col-_action="{ row }">
        <el-button v-if="row.status === 0" link type="primary" size="small" @click="openReserve(row)">{{ t('wms.remnant.btn.reserve') }}</el-button>
        <el-button v-if="row.status === 1" link type="warning" size="small" @click="onUnreserve(row)">{{ t('wms.remnant.btn.unreserve') }}</el-button>
        <el-button v-if="row.status === 0 || row.status === 1" v-permission="'wms-remnant:use'" link type="success" size="small" @click="onUse(row)">{{ t('wms.remnant.btn.use') }}</el-button>
        <el-button v-if="row.status !== 3" v-permission="'wms-remnant:dispose'" link type="danger" size="small" @click="onDispose(row)">{{ t('wms.remnant.btn.dispose') }}</el-button>
      </template>
    </CpListPage>

    <!-- 新建 -->
    <CpFormDialog
      v-model="createDialog"
      :title="t('wms.remnant.dlg.create')"
      width="600"
      :form="createForm"
      :rules="createRules"
      :submit="onCreate"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.common.save') }"
      @saved="reloadList"
    >
      <el-row :gutter="12">
        <el-col :span="12"><el-form-item :label="t('wms.remnant.fld.matType')" prop="materialType">
          <el-select v-model="createForm.materialType" style="width: 100%">
            <el-option v-for="(l, v) in matTypeMap" :key="v" :label="l" :value="v" />
          </el-select>
        </el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.remnant.fld.matGrade')"><el-input v-model="createForm.materialGrade" maxlength="20" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.remnant.fld.widthMm')" prop="widthMm"><el-input-number v-model="createForm.widthMm" :min="1" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.remnant.fld.lengthMm')" prop="lengthMm"><el-input-number v-model="createForm.lengthMm" :min="1" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.remnant.fld.thickness')"><el-input-number v-model="createForm.thicknessUm" :min="0" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.remnant.fld.qty')" prop="quantity"><el-input-number v-model="createForm.quantity" :min="0" :precision="4" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item label="Unit"><el-input v-model="createForm.unitCd" maxlength="10" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.remnant.fld.sourceWO')"><el-input v-model="createForm.sourceWorkOrderNo" maxlength="25" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.remnant.fld.sourceRoll')"><el-input v-model="createForm.sourceRollNo" maxlength="25" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item :label="t('wms.common.warehouse')" prop="warehouseCd"><el-input v-model="createForm.warehouseCd" maxlength="10" /></el-form-item></el-col>
        <el-col :span="24"><el-form-item :label="t('wms.common.location')" prop="locationCd"><el-input v-model="createForm.locationCd" maxlength="30" /></el-form-item></el-col>
        <el-col :span="24"><el-form-item :label="t('wms.common.remarks')"><el-input v-model="createForm.remarks" type="textarea" :rows="2" /></el-form-item></el-col>
      </el-row>
    </CpFormDialog>

    <!-- 予約 -->
    <CpFormDialog
      v-model="reserveDialog"
      :title="t('wms.remnant.dlg.reserve') + ' — ' + (reserveTarget?.remnantNo ?? '')"
      width="420"
      :form="reserveForm"
      :rules="reserveRules"
      :submit="onReserve"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.common.save') }"
      @saved="reloadList"
    >
      <el-form-item :label="t('wms.remnant.fld.reservedFor')" prop="reservedFor">
        <el-input v-model="reserveForm.reservedFor" maxlength="30" />
      </el-form-item>
    </CpFormDialog>

    <!-- 再利用検索 Dialog（查询工具 + 结果表，保留原机制） -->
    <el-dialog v-model="matchDialog" :title="t('wms.remnant.dlg.match')" width="700">
      <el-alert :title="t('wms.remnant.msg.matchHint')" type="info" :closable="false" show-icon style="margin-bottom: 12px" />
      <el-form :model="matchForm" inline label-width="100px" size="small">
        <el-form-item :label="t('wms.remnant.fld.matType')" required>
          <el-select v-model="matchForm.materialType" style="width: 120px">
            <el-option v-for="(l, v) in matTypeMap" :key="v" :label="l" :value="v" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('wms.remnant.fld.minWidth')" required>
          <el-input-number v-model="matchForm.minWidthMm" :min="1" controls-position="right" />
        </el-form-item>
        <el-form-item :label="t('wms.remnant.fld.minLength')" required>
          <el-input-number v-model="matchForm.minLengthMm" :min="1" controls-position="right" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="runMatch" :loading="matching">{{ t('wms.common.search') }}</el-button>
        </el-form-item>
      </el-form>
      <el-table :data="matchResults" border stripe size="small" max-height="350">
        <el-table-column prop="remnantNo" :label="t('wms.remnant.fld.no')" width="160" />
        <el-table-column prop="materialGrade" :label="t('wms.remnant.fld.matGrade')" width="100" />
        <el-table-column prop="widthMm" :label="t('wms.remnant.fld.widthMm')" width="90" align="right" />
        <el-table-column prop="lengthMm" :label="t('wms.remnant.fld.lengthMm')" width="100" align="right" />
        <el-table-column prop="quantity" :label="t('wms.remnant.fld.qty')" width="90" align="right" />
        <el-table-column prop="locationCd" :label="t('wms.common.location')" width="140" />
      </el-table>
    </el-dialog>
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
import { remnantApi } from '@/api/wms/paperIndustry2'
import type { RemnantMaterial, RemnantSearchQuery } from '@/types/wms/wms'
import { formatQty as fmtQty } from '@/utils/format'

const { t } = useI18n()

const total = ref<number>()
const listRef = ref<InstanceType<typeof CpListPage> | null>(null)
function reloadList() { listRef.value?.reload() }

const matTypeMap = computed<Record<string, string>>(() => ({
  PAPER: t('wms.remnant.mat.paper'),
  FILM: t('wms.remnant.mat.film'),
  OTHER: t('wms.remnant.mat.other'),
}))
const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.remnant.status.available'),
  1: t('wms.remnant.status.reserved'),
  2: t('wms.remnant.status.used'),
  3: t('wms.remnant.status.disposed'),
}))
// 原 statusTagOf(success/warning/info/danger) → 设计系统 Tone（保色）
function statusTone(s: number): Tone {
  return ({ 0: 'ok', 1: 'warn', 2: 'info', 3: 'danger' } as const)[s as 0] || 'info'
}
function codeLabel(m: Record<number, string>, v: unknown): string {
  return m[v as number] || (v == null ? '' : String(v))
}
function formatQty(n: number | undefined | null) {
  if (n == null) return '0'
  return fmtQty(n, 4)
}

const columns = computed<ListColumn[]>(() => [
  { prop: 'remnantNo', label: t('wms.remnant.fld.no'), kind: 'mono', width: 180 },
  { prop: 'status', label: t('wms.common.status'), width: 100, kind: 'tag',
    map: (v) => ({ label: codeLabel(statusMap.value, v), tone: statusTone(v as number) }) },
  { prop: 'materialType', label: t('wms.remnant.fld.matType'), width: 80,
    map: (v) => ({ label: matTypeMap.value[v as string] || (v == null ? '' : String(v)) }) },
  { prop: 'materialGrade', label: t('wms.remnant.fld.matGrade'), width: 100 },
  { prop: 'widthMm', label: t('wms.remnant.fld.widthMm'), width: 90, kind: 'num' },
  { prop: 'lengthMm', label: t('wms.remnant.fld.lengthMm'), width: 100, kind: 'num' },
  { prop: 'thicknessUm', label: t('wms.remnant.fld.thickness'), width: 100, kind: 'num' },
  { prop: 'quantity', label: t('wms.remnant.fld.qty'), width: 100, align: 'right',
    map: (v, row) => ({ label: `${formatQty(v as number)} ${(row as RemnantMaterial).unitCd ?? ''}` }) },
  { prop: 'sourceWorkOrderNo', label: t('wms.remnant.fld.sourceWO'), width: 160 },
  { prop: 'sourceRollNo', label: t('wms.remnant.fld.sourceRoll'), width: 160 },
  { prop: 'warehouseCd', label: t('wms.common.warehouse'), width: 80 },
  { prop: 'locationCd', label: t('wms.common.location'), width: 120 },
  { prop: 'reservedFor', label: t('wms.remnant.fld.reservedFor'), width: 140 },
  { prop: 'registeredAt', label: t('wms.sample.fld.registeredAt'), width: 170 },
  { prop: '_action', label: t('wms.common.action'), width: 240, fixed: 'right' },
])

const filterLabels = computed(() => ({
  search: t('wms.common.search'),
  reset: t('wms.common.clear'),
}))

const searchFields = computed<FilterField[]>(() => [
  { key: 'remnantNo', label: t('wms.remnant.fld.no'), type: 'text' },
  {
    key: 'materialType', label: t('wms.remnant.fld.matType'), type: 'select',
    options: Object.entries(matTypeMap.value).map(([v, l]) => ({ label: l, value: v })),
  },
  { key: 'materialGrade', label: t('wms.remnant.fld.matGrade'), type: 'text' },
  {
    key: 'status', label: t('wms.common.status'), type: 'select',
    options: Object.entries(statusMap.value).map(([v, l]) => ({ label: l, value: Number(v) })),
  },
  { key: 'sourceWorkOrderNo', label: t('wms.remnant.fld.sourceWO'), type: 'text' },
])

const fetchList: ListFetch = async ({ filters }) => {
  const f = filters as Record<string, unknown>
  const q: RemnantSearchQuery = { pageSize: 1000 }
  if (f.remnantNo) q.remnantNo = String(f.remnantNo)
  if (f.materialType) q.materialType = String(f.materialType)
  if (f.materialGrade) q.materialGrade = String(f.materialGrade)
  if (f.status !== undefined && f.status !== '' && f.status !== null) q.status = Number(f.status)
  if (f.sourceWorkOrderNo) q.sourceWorkOrderNo = String(f.sourceWorkOrderNo)
  const all = (await remnantApi.search(q)).data || []
  return { rows: all, total: all.length }
}

// —— 新建 ——
const createDialog = ref(false)
const createForm = reactive<Record<string, unknown>>({
  materialType: 'PAPER', materialGrade: '', widthMm: 500, lengthMm: 700, thicknessUm: undefined,
  quantity: 1, unitCd: 'SHT', sourceWorkOrderNo: '', sourceRollNo: '', warehouseCd: '', locationCd: '', remarks: '',
})
const createRules = computed<FormRules>(() => ({
  materialType: [{ required: true, message: t('wms.common.required'), trigger: 'change' }],
  widthMm: [{ required: true, message: t('wms.common.required'), trigger: 'change' }],
  lengthMm: [{ required: true, message: t('wms.common.required'), trigger: 'change' }],
  quantity: [{ required: true, message: t('wms.common.required'), trigger: 'change' }],
  warehouseCd: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
  locationCd: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
}))
function openCreate() {
  Object.assign(createForm, {
    materialType: 'PAPER', materialGrade: '', widthMm: 500, lengthMm: 700, thicknessUm: undefined,
    quantity: 1, unitCd: 'SHT', sourceWorkOrderNo: '', sourceRollNo: '', warehouseCd: '', locationCd: '', remarks: '',
  })
  createDialog.value = true
}
async function onCreate() {
  const res = await remnantApi.create({ ...createForm } as unknown as RemnantMaterial)
  ElMessage.success(`${t('wms.common.success')}: ${res.data.remnantNo}`)
}

// —— 予約 ——
const reserveDialog = ref(false)
const reserveTarget = ref<RemnantMaterial | null>(null)
const reserveForm = reactive<Record<string, unknown>>({ reservedFor: '' })
const reserveRules = computed<FormRules>(() => ({
  reservedFor: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
}))
function openReserve(row: RemnantMaterial) {
  reserveTarget.value = row
  Object.assign(reserveForm, { reservedFor: '' })
  reserveDialog.value = true
}
async function onReserve() {
  await remnantApi.reserve(reserveTarget.value!.remnantNo, reserveForm.reservedFor as string)
  ElMessage.success(t('wms.common.success'))
}

async function onUnreserve(row: RemnantMaterial) {
  try {
    await ElMessageBox.confirm(`${t('wms.remnant.btn.unreserve')}: ${row.remnantNo}`, t('wms.common.confirm'), { type: 'warning' })
    await remnantApi.unreserve(row.remnantNo)
    ElMessage.success(t('wms.common.success'))
    reloadList()
  } catch { /* */ }
}
async function onUse(row: RemnantMaterial) {
  try {
    await ElMessageBox.confirm(`${t('wms.remnant.btn.use')}: ${row.remnantNo}`, t('wms.common.confirm'), { type: 'warning' })
    await remnantApi.markUsed(row.remnantNo)
    ElMessage.success(t('wms.common.success'))
    reloadList()
  } catch { /* */ }
}
async function onDispose(row: RemnantMaterial) {
  try {
    await ElMessageBox.confirm(`${t('wms.remnant.btn.dispose')}: ${row.remnantNo}`, t('wms.common.confirm'), { type: 'warning' })
    await remnantApi.dispose(row.remnantNo)
    ElMessage.success(t('wms.common.success'))
    reloadList()
  } catch { /* */ }
}

// —— 再利用検索 ——
const matchDialog = ref(false)
const matching = ref(false)
const matchForm = reactive({ materialType: 'PAPER', minWidthMm: 500, minLengthMm: 700 })
const matchResults = ref<RemnantMaterial[]>([])
async function runMatch() {
  matching.value = true
  try { matchResults.value = (await remnantApi.match(matchForm.materialType, matchForm.minWidthMm, matchForm.minLengthMm)).data || [] }
  finally { matching.value = false }
}
</script>
