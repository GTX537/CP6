<!--
  補充指示一覧 —— WMS 迁移批次2（CpListPage 消费者）。
  结构：CpPageShell（标题 + 计数 pill）→ CpListPage（列/搜索/取数包装/行操作 col slot）＋ 两个 CpFormDialog（新規作成 / バッチ生成）。
  码值列（状態/優先度/トリガ）走 ListColumn map + kind:'tag'（label 走 t() computed，tone 用共享 Tone）；
  数量列用 map(formatQty)+kind:'num'；実行日時用 map 自定义 datetime 格式；操作列走 col-_action 具名插槽。
  数据源为当前Owner服务器分页；页内変更后用 listRef.reload() 就地刷新。
-->
<template>
  <CpPageShell :title="t('wms.replenish.title')" :count="total">
    <template #actions>
      <el-button @click="onReload">{{ t('刷新') }}</el-button>
      <el-button v-permission="'wms-replenish:add'" :disabled="!canWrite('add')" @click="openCreate">{{ t('wms.common.create') }}</el-button>
      <el-button v-permission="'wms-replenish:generate'" :disabled="!canWrite('generate')" type="warning" @click="openBatch">{{ t('wms.replenish.btn.genBatch') }}</el-button>
    </template>

    <el-alert v-if="!writesAvailable" :title="t('补充指示暂只支持查询，生成和派发功能尚未开通。')" type="info" :closable="false" show-icon />
    <el-alert v-if="readError || intent.error.value" :title="readError || intent.error.value" type="warning" :closable="false" show-icon />
    <CpListPage
      :key="listEpoch"
      :lazy="reloadRequired"
      :clear-on-load="true"
      :empty-text="readError || undefined"
      ref="listRef"
      :columns="columns"
      :fetch="fetchList"
      :search-fields="searchFields"
      :filter-labels="filterLabels"
      @total-change="total = $event"
    >
      <template #col-_action="{ row }">
        <template v-if="writesAvailable && (row.status === 0 || row.status === 2)">
          <el-button v-permission="'wms-replenish:add'" link type="primary" size="small" @click="openEdit(row)">{{ t('wms.common.edit') }}</el-button>
          <el-button v-if="row.status === 0" v-permission="'wms-replenish:execute'" link type="success" size="small" @click="onExecute(row)">{{ t('wms.kit.btn.execute') }}</el-button>
          <el-button v-permission="'wms-replenish:cancel'" link type="danger" size="small" @click="onCancel(row)">{{ t('wms.outbound.btn.cancel') }}</el-button>
        </template>
        <span v-else class="cp-dash">—</span>
      </template>
    </CpListPage>

    <!-- 新規作成 -->
    <CpFormDialog
      v-model="dialogVisible"
      :title="editingNo ? t('wms.common.edit') : t('wms.replenish.dlg.create')"
      width="500"
      :form="createForm"
      :rules="createRules"
      :submit="submitCreate"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.common.save') }"
      @saved="onReload"
    >
      <el-form-item :label="t('wms.replenish.fld.priority')" prop="priority">
        <el-select v-model="createForm.priority">
          <el-option :label="t('wms.replenish.priority.urgent')" :value="1" />
          <el-option :label="t('wms.replenish.priority.normal')" :value="2" />
        </el-select>
      </el-form-item>
      <el-form-item :label="t('wms.common.product')" prop="productCd">
        <el-input v-model="createForm.productCd" maxlength="20" />
      </el-form-item>
      <el-form-item :label="t('wms.common.warehouse')" prop="warehouseCd">
        <el-input v-model="createForm.warehouseCd" maxlength="10" />
      </el-form-item>
      <el-form-item :label="t('wms.replenish.fld.fromLoc')" prop="fromLocationCd">
        <el-input v-model="createForm.fromLocationCd" placeholder="RES-A-01" maxlength="30" />
      </el-form-item>
      <el-form-item :label="t('wms.replenish.fld.toLoc')" prop="toLocationCd">
        <el-input v-model="createForm.toLocationCd" placeholder="PIK-A-01" maxlength="30" />
      </el-form-item>
      <el-form-item :label="t('wms.common.lot')" prop="lotNo">
        <el-input v-model="createForm.lotNo" maxlength="30" />
      </el-form-item>
      <el-form-item :label="t('wms.common.qty')" prop="qty">
        <el-input-number v-model="createForm.qty" :min="0" :precision="2" controls-position="right" style="width: 100%" />
      </el-form-item>
    </CpFormDialog>

    <!-- バッチ生成 -->
    <CpFormDialog
      v-model="batchDialog"
      :title="t('wms.replenish.dlg.batch')"
      width="500"
      :form="batchForm"
      :rules="batchRules"
      :submit="submitBatch"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.replenish.btn.genBatch') }"
      @saved="onReload"
    >
      <el-form-item :label="t('wms.common.warehouse')" prop="warehouseCd">
        <el-input v-model="batchForm.warehouseCd" maxlength="10" />
      </el-form-item>
      <el-form-item :label="t('wms.replenish.fld.minQty')" prop="minQty">
        <el-input-number v-model="batchForm.minQty" :min="1" :precision="2" controls-position="right" />
      </el-form-item>
      <el-alert type="info" :closable="false" :title="t('wms.replenish.msg.batchHint')" />
    </CpFormDialog>
  </CpPageShell>
</template>

<script setup lang="ts">
import { ref, reactive, computed } from 'vue'
import { ElMessage, ElMessageBox, type FormRules } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { readWmsPage } from '@/api/wms/readWmsPage'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch, type ListPageExpose } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import CpFormDialog from '@/components/templates/CpFormDialog.vue'
import { type Tone } from '@/components/base/CpTag.vue'
import { replenishApi } from '@/api/wms/logistics'
import type { ReplenishOrder, ReplenishSearchQuery } from '@/types/wms/wms'
import { formatQty } from '@/utils/format'
import { useOperationsIntent } from '@/composables/useOperationsIntent'
import { usePermissionStore } from '@/stores/permission'

const { t } = useI18n()

// —— 头部计数 pill ——
const total = ref<number>()
const listRef = ref<ListPageExpose>()
const writesAvailable = ref(false), readError = ref(''), listEpoch = ref(0), reloadRequired = ref(false)
let readSequence = 0
const permission = usePermissionStore()
const intent = useOperationsIntent(() => {
  readSequence++; clearForms(); writesAvailable.value = false; total.value = undefined; readError.value = ''
  reloadRequired.value = true; listEpoch.value++
})
const allowed = (action: string) => permission.loaded && permission.has(`wms-replenish:${action}`)
const writable = (action: string) => writesAvailable.value && allowed(action) && !intent.uncertain.value
const canWrite = (action: string) => writable(action) && !intent.busy.value
function onReload() { reloadRequired.value = false; listRef.value?.reload() }

// —— 码值映射（i18n 反应式） ——
const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.replenish.status.pending'),
  1: t('旧记录报告完成'),
  2: t('已派发任务'),
  9: t('wms.replenish.status.cancelled'),
}))
const priorityMap = computed<Record<number, string>>(() => ({
  1: t('wms.replenish.priority.urgent'),
  2: t('wms.replenish.priority.normal'),
}))
const triggerMap = computed<Record<string, string>>(() => ({
  BATCH: t('wms.replenish.trigger.batch'),
  MANUAL: t('wms.replenish.trigger.manual'),
  ALERT: t('wms.replenish.trigger.alert'),
}))

// —— EP type → 共享 Tone（保色：info→muted / success→ok / danger→danger / warning→warn） ——
function statusTone(s: number): Tone {
  return ({ 0: 'muted', 1: 'ok', 2: 'info', 9: 'danger' } as const)[s as 0] || 'muted'
}
function triggerTone(v: string): Tone {
  return v === 'BATCH' ? 'ok' : v === 'ALERT' ? 'warn' : 'muted'
}
function codeLabel(m: Record<string, string> | Record<number, string>, v: unknown): string {
  return (m as Record<string, string>)[String(v)] || (v == null ? '' : String(v))
}

// —— 列定义 ——
const columns = computed<ListColumn<ReplenishOrder>[]>(() => [
  { prop: 'replenishNo', label: t('wms.replenish.fld.no'), kind: 'mono', width: 180 },
  { prop: 'status', label: t('wms.common.status'), width: 100, kind: 'tag',
    map: (v) => ({ label: codeLabel(statusMap.value, v), tone: statusTone(v as number) }) },
  { prop: 'priority', label: t('wms.replenish.fld.priority'), width: 90, kind: 'tag',
    map: (v) => ({ label: codeLabel(priorityMap.value, v), tone: v === 1 ? 'danger' : 'muted' }) },
  { prop: 'triggerType', label: t('wms.replenish.fld.trigger'), width: 100, kind: 'tag',
    map: (v) => ({ label: codeLabel(triggerMap.value, v), tone: triggerTone(v as string) }) },
  { prop: 'productCd', label: t('wms.common.product'), width: 120 },
  { prop: 'fromLocationCd', label: t('wms.replenish.fld.fromLoc'), width: 140 },
  { prop: 'toLocationCd', label: t('wms.replenish.fld.toLoc'), width: 140 },
  { prop: 'lotNo', label: t('wms.common.lot'), width: 120 },
  { prop: 'qty', label: t('wms.common.qty'), width: 100, kind: 'num', map: (v) => ({ label: formatQty(v as number) }) },
  { prop: 'executedAt', label: t('wms.kit.fld.executedAt'), kind: 'datetime', width: 180 },
  { prop: '_action', label: t('wms.common.action'), width: 220, fixed: 'right' },
])

const filterLabels = computed(() => ({ search: t('wms.common.search'), reset: t('wms.common.clear') }))

const searchFields = computed<FilterField[]>(() => [
  { key: 'replenishNo', label: t('wms.replenish.fld.no'), type: 'text' },
  { key: 'productCd', label: t('wms.common.product'), type: 'text' },
  { key: 'warehouseCd', label: t('wms.common.warehouse'), type: 'text' },
  {
    key: 'status', label: t('wms.common.status'), type: 'select',
    options: Object.entries(statusMap.value).map(([v, l]) => ({ label: l, value: Number(v) })),
  },
])

// 当前Owner返回本页与有权总数。
const fetchList: ListFetch<ReplenishOrder> = async ({ page, size, filters }) => {
  const token = intent.capture(), sequence = ++readSequence
  clearForms(); writesAvailable.value = false; total.value = undefined; readError.value = ''
  const f = filters as Record<string, unknown>
  const q: ReplenishSearchQuery = { page, pageSize: size }
  if (f.replenishNo) q.replenishNo = String(f.replenishNo)
  if (f.productCd) q.productCd = String(f.productCd)
  if (f.warehouseCd) q.warehouseCd = String(f.warehouseCd)
  if (f.status !== undefined && f.status !== '') q.status = Number(f.status)
  try {
    if (!allowed('query')) throw new Error('Missing query permission')
    const capabilities = await replenishApi.capabilities()
    if (!intent.current(token) || sequence !== readSequence) throw new Error('Stale read')
    if (capabilities.code !== 0 || typeof capabilities.data?.writesAvailable !== 'boolean') throw new Error('Unavailable capabilities')
    const res = await replenishApi.page(q)
    if (!intent.current(token) || sequence !== readSequence || !allowed('query')) throw new Error('Stale read')
    const result = readWmsPage(res, page, size)
    writesAvailable.value = capabilities.data.writesAvailable && capabilities.data.status === 'LEGACY'
    return result
  } catch {
    if (intent.current(token) && sequence === readSequence) {
      clearForms(); writesAvailable.value = false; total.value = undefined
      readError.value = t('补充指示未能读取，请刷新重试。')
    }
    throw new Error(t('补充指示未能读取，请刷新重试。'))
  }
}

// —— 新規作成弹窗 ——
const dialogVisible = ref(false)
const editingNo = ref<string>()
const editingRow = ref<ReplenishOrder>()
const createForm = reactive({
  priority: 2, productCd: '', warehouseCd: '', fromLocationCd: '', toLocationCd: '', lotNo: '', qty: 0,
})
const createRules = computed<FormRules>(() => ({
  productCd: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
  warehouseCd: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
  fromLocationCd: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
  toLocationCd: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
  qty: [{ required: true, message: t('wms.common.required'), trigger: 'change' }],
}))
function openCreate() {
  if (!canWrite('add')) return
  editingNo.value = undefined
  editingRow.value = undefined
  Object.assign(createForm, { priority: 2, productCd: '', warehouseCd: '', fromLocationCd: '', toLocationCd: '', lotNo: '', qty: 0 })
  dialogVisible.value = true
}
function openEdit(row: ReplenishOrder) {
  if (!canWrite('add')) return
  editingNo.value = row.replenishNo
  editingRow.value = row
  Object.assign(createForm, {
    priority: row.priority,
    productCd: row.productCd,
    warehouseCd: row.warehouseCd,
    fromLocationCd: row.fromLocationCd,
    toLocationCd: row.toLocationCd,
    lotNo: row.lotNo,
    qty: row.qty,
  })
  dialogVisible.value = true
}
async function submitCreate() {
  if (!canWrite('add')) throw new Error(t('补充指示暂只支持查询，生成和派发功能尚未开通。'))
  const dto: ReplenishOrder = editingRow.value
    ? { ...editingRow.value, ...createForm }
    : { ...createForm, triggerType: 'MANUAL', status: 0 }
  const body: ReplenishOrder = JSON.parse(JSON.stringify(dto)), no = editingNo.value
  if (no) return formWrite('add', () => replenishApi.update(no, body), () => ElMessage.success(t('wms.common.success')))
  return formWrite('add', () => replenishApi.create(body), res => {
    if (!res.data?.replenishNo) throw new Error('Unavailable result')
    ElMessage.success(`${t('wms.common.success')}: ${res.data.replenishNo}`)
  })
}

// —— バッチ生成弹窗 ——
const batchDialog = ref(false)
const batchForm = reactive({ warehouseCd: '', minQty: 10 })
const batchRules = computed<FormRules>(() => ({
  warehouseCd: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
}))
async function submitBatch() {
  const warehouse = batchForm.warehouseCd, quantity = batchForm.minQty
  return formWrite('generate', () => replenishApi.generateBatch(warehouse, quantity), res => {
    if (!Number.isInteger(res.data?.generated) || res.data.generated < 0) throw new Error('Unavailable result')
    ElMessage.success(t('wms.replenish.msg.batchGen', { n: res.data.generated }))
  })
}

function openBatch() { if (canWrite('generate')) batchDialog.value = true }
function clearForms() {
  dialogVisible.value = false; batchDialog.value = false; editingNo.value = undefined; editingRow.value = undefined
  Object.assign(createForm, { priority: 2, productCd: '', warehouseCd: '', fromLocationCd: '', toLocationCd: '', lotNo: '', qty: 0 })
  Object.assign(batchForm, { warehouseCd: '', minQty: 10 })
}
async function formWrite<T extends { code: number }>(action: string, send: () => Promise<T>, success: (result: T) => void) {
  if (!canWrite(action)) throw new Error(t('补充指示暂只支持查询，生成和派发功能尚未开通。'))
  const token = intent.begin()
  if (!token) throw new Error('Operation unavailable')
  try {
    if (!intent.current(token) || !writable(action)) throw new Error('Operation unavailable')
    const result = await send()
    if (!intent.current(token)) throw new Error('Identity changed')
    if (result.code !== 0) throw new Error('Unavailable result')
    success(result)
  } catch (error) { intent.failed(token, error); throw error }
  finally { intent.finish(token) }
}

// —— 行操作 ——
async function onExecute(row: ReplenishOrder) {
  if (!canWrite('execute') || row.status !== 0 || !row.replenishNo) return
  const no = row.replenishNo, token = intent.begin()
  if (!token) return
  try {
    await ElMessageBox.confirm(t('wms.replenish.msg.executeAsk'), t('wms.common.confirm'), { type: 'warning' })
    if (!intent.current(token) || !writable('execute')) return
    const res = await replenishApi.execute(no)
    if (!intent.current(token)) return
    if (res.code !== 0 || !res.data?.taskNo) throw new Error('Unavailable result')
    ElMessage.success(`${t('wms.common.success')}: ${res.data.taskNo}`)
    onReload()
  } catch (error) { intent.failed(token, error) }
  finally { intent.finish(token) }
}
async function onCancel(row: ReplenishOrder) {
  if (!canWrite('cancel') || (row.status !== 0 && row.status !== 2) || !row.replenishNo) return
  const no = row.replenishNo, token = intent.begin()
  if (!token) return
  try {
    await ElMessageBox.confirm(t('wms.inbound.msg.cancelAsk'), t('wms.common.confirm'), { type: 'warning' })
    if (!intent.current(token) || !writable('cancel')) return
    const res = await replenishApi.cancel(no)
    if (!intent.current(token)) return
    if (res.code !== 0) throw new Error('Unavailable result')
    ElMessage.success(t('wms.common.success'))
    onReload()
  } catch (error) { intent.failed(token, error) }
  finally { intent.finish(token) }
}
</script>

<style scoped>
.cp-dash { color: var(--cp-muted); }
</style>
