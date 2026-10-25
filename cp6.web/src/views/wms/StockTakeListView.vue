<!--
  棚卸一覧 —— CpPageShell + CpListPage + CpFormDialog 迁移（WMS 批次1）。
  種別列纯 map（原页无 tag 视觉）；状態列 kind:'tag'+map；予定日 kind:'date'；実施/完了日 走 col slot（保 '—' 空态）；操作 col slot。
  スナップショット(計画作成)用 CpFormDialog（default slot 保留 date-picker/input-number/placeholder）；成功后 router.push 至明细，无需刷新列表。
-->
<template>
  <CpPageShell :title="t('wms.stocktake.titleList')" :count="total">
    <el-alert v-if="intent.error.value" :title="intent.error.value" type="warning" :closable="false" />
    <el-alert v-if="creationError" :title="creationError" type="warning" :closable="false" />
    <div v-if="allowed('query')">
      <el-button :disabled="intent.busy.value" @click="loadCreationScope">{{ t('登録操作の状態を更新') }}</el-button>
      <template v-if="creationPending">
        <span>{{ t('確認が必要な登録操作') }}: {{ creationPending }}</span>
        <el-button :disabled="intent.busy.value" @click="resolveCreation(false)">{{ t('元の結果を確認') }}</el-button>
        <el-button :disabled="intent.busy.value" @click="resolveCreation(true)">{{ t('未実行なら登録を取り消す') }}</el-button>
      </template>
      <span v-if="creationResult">{{ creationResult.status === 'COMMITTED' ? t('登録済み') : creationResult.status === 'CLOSED' ? t('未実行の登録を取り消しました') : t('結果は未確認です。再送せず、確認または取消を行ってください。') }}</span>
    </div>
    <div v-if="lastSavedNo">{{ lastSavedNo }}</div>
    <template #actions>
      <el-button @click="openPlan" :disabled="intent.busy.value || intent.uncertain.value || !!creationPending" v-permission="'wms-stocktake:add'">{{ t('wms.stocktake.btn.snapshot') }}</el-button>
    </template>

    <CpListPage :key="intent.epoch.value" clear-on-load
      :columns="columns"
      :fetch="fetchList"
      :search-fields="searchFields"
      :filter-labels="filterLabels"
      @total-change="total = $event"
    >
      <template #col-actualDate="{ row }">{{ row.actualDate?.slice(0, 10) || '—' }}</template>
      <template #col-completedDate="{ row }">{{ row.completedDate?.slice(0, 10) || '—' }}</template>
      <template #col-_action="{ row }">
        <el-button link type="primary" size="small" @click="goDetail(row)">{{ t('wms.common.open') }}</el-button>
      </template>
    </CpListPage>

    <CpFormDialog :key="intent.epoch.value"
      v-model="planDialog"
      :title="t('wms.stocktake.btn.snapshot')"
      width="540"
      :form="planForm"
      :rules="rules"
      :submit="onCreatePlan"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.common.create') }"
    >
      <el-form-item v-if="creationContext" :label="t('所属部門')" :required="!creationContext.allowsUnbound">
        <el-select v-model="creationDepartment" clearable :disabled="intent.busy.value" :placeholder="t('部門を選択してください')" @clear="creationDepartment = null">
          <el-option v-for="department in creationContext.departments" :key="department.id" :value="department.id" :label="department.code + ' — ' + department.name" />
        </el-select>
        <span v-if="creationContext.allowsUnbound">{{ t('部門を指定しない登録も可能です') }}</span>
      </el-form-item>
      <el-form-item :label="t('wms.common.type')">
        <el-select v-model="planForm.stockTakeType">
          <el-option v-for="(l, v) in typeMap" :key="v" :label="l" :value="Number(v)" />
        </el-select>
      </el-form-item>
      <el-form-item :label="t('wms.stocktake.fld.plannedDate')">
        <el-date-picker v-model="planForm.plannedDate" type="date" value-format="YYYY-MM-DD" />
      </el-form-item>
      <el-form-item :label="t('wms.stocktake.fld.targetWh')" prop="targetWarehouseCd">
        <el-input v-model="planForm.targetWarehouseCd" maxlength="10" />
      </el-form-item>
      <el-form-item :label="t('wms.stocktake.fld.targetLocPrefix')">
        <el-input v-model="planForm.targetLocationPrefix" :placeholder="t('例: {sample}', { sample: 'A-01-' })" maxlength="30" />
      </el-form-item>
      <el-form-item :label="t('wms.stocktake.fld.targetProduct')">
        <el-input v-model="planForm.targetProductCd" maxlength="20" />
      </el-form-item>
      <el-form-item :label="t('wms.stocktake.fld.threshold')">
        <el-input-number v-model="planForm.approvalThresholdAmount" :min="0" :precision="2" controls-position="right" />
      </el-form-item>
      <el-form-item :label="t('wms.common.remarks')">
        <el-input v-model="planForm.remarks" type="textarea" :rows="2" />
      </el-form-item>
    </CpFormDialog>
  </CpPageShell>
</template>

<script setup lang="ts">
import { useOperationsIntent } from '@/composables/useOperationsIntent'
import { ref, reactive, computed, onMounted, onBeforeUnmount } from 'vue'
import { usePermissionStore } from '@/stores/permission'
import { stocktakeResponse, pendingStocktakeCreation, saveStocktakeCreation, resolveStocktakeCreation } from '@/auth/stocktakeCreationRecovery'
import type { StocktakeCreationContext, StocktakeCreationScope, StocktakeCreationOperation } from '@/types/wms/stocktakeCreation'
import { useRouter } from 'vue-router'
import { ElMessage, type FormRules } from 'element-plus'
import { useI18n } from 'vue-i18n'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import CpFormDialog from '@/components/templates/CpFormDialog.vue'
import { type Tone } from '@/components/base/CpTag.vue'
import { stockTakeApi } from '@/api/wms/stockTake'
import type { StockTake, StockTakeSearchQuery, StockTakePlanRequest } from '@/types/wms/wms'

const router = useRouter()
const { t } = useI18n()

const total = ref<number>()
const lastSavedNo = ref('')
const permissions = usePermissionStore()
const creationContext = ref<StocktakeCreationContext | null>(null), creationScope = ref<StocktakeCreationScope | null>(null)
const creationDepartment = ref<string | null>(null), creationPending = ref<string | null>(null)
const creationError = ref(''), creationResult = ref<StocktakeCreationOperation | null>(null)
let creationReads = 0
const intent = useOperationsIntent(clearProtected)
function clearProtected() {
  reads++; creationReads++; total.value = 0; planDialog.value = false; lastSavedNo.value = ''
  creationContext.value = null; creationScope.value = null; creationDepartment.value = null
  creationPending.value = null; creationError.value = ''; creationResult.value = null
  Object.keys(planForm).forEach(key => delete (planForm as any)[key])
}
function allowed(action: string) { return permissions.loaded && permissions.has('wms-stocktake:' + action) }
function forbidden(error: any) {
  if (![401, 403].includes(error?.response?.status)) return false
  intent.invalidate(); clearProtected(); return true
}
let reads = 0

const typeMap = computed<Record<number, string>>(() => ({
  1: t('wms.stocktake.type.full'),
  2: t('wms.stocktake.type.cycle'),
  3: t('wms.stocktake.type.adhoc'),
}))
const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.stocktake.status.planned'),
  1: t('wms.stocktake.status.counting'),
  2: t('wms.stocktake.status.diffReview'),
  3: t('wms.stocktake.status.awaitingApproval'),
  4: t('wms.stocktake.status.completed'),
  9: t('wms.stocktake.status.cancelled'),
}))
// 原 statusTagOf(info/primary/warning/danger/success/info) → 设计系统 Tone（保色）
function statusTone(s: number): Tone {
  return ({ 0: 'muted', 1: 'info', 2: 'warn', 3: 'danger', 4: 'ok', 9: 'muted' } as const)[s as 0] || 'muted'
}
function codeLabel(m: Record<number, string>, v: unknown): string {
  return m[v as number] || (v == null ? '' : String(v))
}

const columns = computed<ListColumn<StockTake>[]>(() => [
  { prop: 'stockTakeNo', label: t('wms.stocktake.fld.no'), kind: 'mono', width: 180 },
  { prop: 'stockTakeType', label: t('wms.common.type'), width: 100,
    map: (v) => ({ label: codeLabel(typeMap.value, v) }) },
  { prop: 'status', label: t('wms.common.status'), width: 130, kind: 'tag',
    map: (v) => ({ label: codeLabel(statusMap.value, v), tone: statusTone(v as number) }) },
  { prop: 'targetWarehouseCd', label: t('wms.common.warehouse'), width: 100 },
  { prop: 'targetLocationPrefix', label: t('wms.common.location'), width: 120 },
  { prop: 'targetProductCd', label: t('wms.common.product'), width: 120 },
  { prop: 'plannedDate', label: t('wms.stocktake.fld.plannedDate'), width: 120, kind: 'date' },
  { prop: 'actualDate', label: t('wms.stocktake.fld.actualDate'), width: 120 },
  { prop: 'completedDate', label: t('wms.stocktake.fld.completedDate'), width: 120 },
  { prop: '_action', label: t('wms.common.action'), width: 100, fixed: 'right' },
])

const filterLabels = computed(() => ({
  search: t('wms.common.search'),
  reset: t('wms.common.clear'),
}))

const searchFields = computed<FilterField[]>(() => [
  { key: 'stockTakeNo', label: t('wms.stocktake.fld.no'), type: 'text' },
  {
    key: 'stockTakeType', label: t('wms.common.type'), type: 'select',
    options: Object.entries(typeMap.value).map(([v, l]) => ({ label: l, value: Number(v) })),
  },
  {
    key: 'status', label: t('wms.common.status'), type: 'select',
    options: Object.entries(statusMap.value).map(([v, l]) => ({ label: l, value: Number(v) })),
  },
  { key: 'targetWarehouseCd', label: t('wms.common.warehouse'), type: 'text' },
])

const fetchList: ListFetch<StockTake> = async ({ page, size, filters }) => {
  const f = filters as Record<string, unknown>
  const q: StockTakeSearchQuery = { pageSize: 100 }
  if (f.stockTakeNo) q.stockTakeNo = String(f.stockTakeNo)
  if (f.stockTakeType !== undefined && f.stockTakeType !== '') q.stockTakeType = Number(f.stockTakeType)
  if (f.status !== undefined && f.status !== '') q.status = Number(f.status)
  if (f.targetWarehouseCd) q.targetWarehouseCd = String(f.targetWarehouseCd)
  const token = intent.capture(), id = ++reads
  try {
    const res = await stockTakeApi.search(q)
    if (!intent.current(token) || id !== reads) return { rows: [], total: 0 }
    const all = res.data || []
    const start = (page - 1) * size
    return { rows: all.slice(start, start + size), total: all.length }
  } catch (error) {
    if (intent.current(token) && id === reads) { total.value = 0; forbidden(error) }
    throw error
  }
}

function goDetail(row: StockTake) {
  router.push({ path: '/wms/stock-take', query: { no: row.stockTakeNo } })
}

// —— 計画作成对话框 ——
const planDialog = ref(false)
const planForm = reactive<StockTakePlanRequest>({
  stockTakeType: 1,
  plannedDate: new Date().toISOString().slice(0, 10),
  targetWarehouseCd: '',
})
const rules = computed<FormRules>(() => ({
  targetWarehouseCd: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
}))

async function openPlan() {
  if (!allowed('add') || intent.busy.value || intent.uncertain.value) return
  const token = intent.capture(), request = ++creationReads
  planDialog.value = false; creationContext.value = null; creationDepartment.value = null; creationError.value = ''
  Object.assign(planForm, { stockTakeType: 1, plannedDate: new Date().toISOString().slice(0, 10), targetWarehouseCd: '',
    targetLocationPrefix: '', targetProductCd: '', approvalThresholdAmount: undefined, remarks: '' })
  try {
    const context = stocktakeResponse(await stockTakeApi.creationContext())
    if (!intent.current(token) || request !== creationReads || !allowed('add')) return
    creationScope.value = { tenantId: context.tenantId, actorId: context.actorId }; syncCreationPending()
    if (creationPending.value) { creationError.value = '先に前回の登録結果を確認してください。'; return }
    creationContext.value = context; planDialog.value = true
  } catch (error) {
    if (intent.current(token) && request === creationReads && !forbidden(error)) creationError.value = '登録できる部門を確認できません。権限と最新状態を確認してください。'
  }
}
function syncCreationPending() {
  if (creationScope.value) creationPending.value = pendingStocktakeCreation(creationScope.value)
}
async function loadCreationScope() {
  if (intent.busy.value || !allowed('query')) return
  const token = intent.capture(), request = ++creationReads
  planDialog.value = false; creationContext.value = null; creationDepartment.value = null
  creationScope.value = null; creationPending.value = null; creationError.value = ''; creationResult.value = null
  try {
    const scope = stocktakeResponse(await stockTakeApi.creationScope())
    if (!intent.current(token) || request !== creationReads || !allowed('query')) return
    const pending = pendingStocktakeCreation(scope); creationScope.value = scope; creationPending.value = pending
  } catch (error) {
    if (intent.current(token) && request === creationReads && !forbidden(error)) creationError.value = '登録操作の状態を確認できません。保存された操作番号を確認してください。'
  }
}
async function onCreatePlan() {
  const context = creationContext.value, department = creationDepartment.value || null
  if (!planDialog.value || !context || !allowed('add')) throw new Error('登録できる部門を再確認してください。')
  if (department === null ? !context.allowsUnbound : !context.departments.some(d => d.id === department)) {
    creationError.value = '棚卸計画の所属部門を選択してください。'; throw new Error(creationError.value)
  }
  const token = intent.begin(); if (!token) throw new Error('処理中または結果確認待ちです。')
  const draft = JSON.parse(JSON.stringify(planForm)) as StockTakePlanRequest
  creationError.value = ''; creationResult.value = null
  let committed = false
  const guard = () => { if (!intent.current(token) || !allowed('add')) throw new Error('操作画面が変わりました。') }
  try {
    const result = await saveStocktakeCreation(context, guard, operationId => stockTakeApi.createOperation({
      operationId, expectedAuthorityVersion: context.authorityVersion, departmentId: department, plan: draft,
    }))
    guard(); committed = true; creationResult.value = result; lastSavedNo.value = result.result!.stockTakeNo; planDialog.value = false
    ElMessage.success(t('wms.common.success') + ': ' + lastSavedNo.value)
    await router.push({ path: '/wms/stock-take', query: { no: lastSavedNo.value } })
  } catch (error) {
    if (intent.current(token)) {
      planDialog.value = false; creationContext.value = null; creationDepartment.value = null
      Object.keys(planForm).forEach(key => delete (planForm as any)[key])
      if (!forbidden(error)) creationError.value = committed
        ? '登録は完了しました。保存された番号を使って一覧または単票で確認してください。'
        : '結果を確認できません。元の登録操作を確認してください。再送は行いません。'
    }
    throw error
  } finally {
    if (intent.current(token)) { try { syncCreationPending() } catch { creationScope.value = null; creationError.value = '保存された操作番号を確認してください。' } }
    intent.finish(token)
  }
}
async function resolveCreation(close: boolean) {
  if (intent.busy.value || !creationScope.value || !allowed('query')) return
  const token = intent.capture(); intent.busy.value = true
  const scope = { ...creationScope.value }; creationError.value = ''; creationResult.value = null
  const guard = () => { if (!intent.current(token) || !allowed('query')) throw new Error('操作画面が変わりました。') }
  try {
    const result = await resolveStocktakeCreation(scope, close, guard); guard(); creationResult.value = result
    if (result.status === 'COMMITTED') lastSavedNo.value = result.result!.stockTakeNo
  } catch (error) {
    if (intent.current(token) && !forbidden(error)) creationError.value = '元の結果を確認できません。操作番号は保持されます。'
  } finally {
    if (intent.current(token)) { try { syncCreationPending() } catch { creationScope.value = null; creationError.value = '保存された操作番号を確認してください。' } }
    intent.finish(token)
  }
}
function creationStorageChanged(event: StorageEvent) {
  if (event.key !== null && !event.key.startsWith('cp6_stocktake_creation_v1:') || intent.busy.value || !creationScope.value) return
  creationResult.value = null; planDialog.value = false; creationContext.value = null; creationDepartment.value = null
  try { syncCreationPending() } catch { creationScope.value = null; creationError.value = '保存された操作番号を確認してください。' }
}
onMounted(() => { void loadCreationScope(); window.addEventListener('storage', creationStorageChanged) })
onBeforeUnmount(() => window.removeEventListener('storage', creationStorageChanged))
</script>
