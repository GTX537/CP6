<!--
  補充指示一覧 —— WMS 迁移批次2（CpListPage 消费者）。
  结构：CpPageShell（标题 + 计数 pill）→ CpListPage（列/搜索/取数包装/行操作 col slot）＋ 两个 CpFormDialog（新規作成 / バッチ生成）。
  码值列（状態/優先度/トリガ）走 ListColumn map + kind:'tag'（label 走 t() computed，tone 用共享 Tone）；
  数量列用 map(formatQty)+kind:'num'；実行日時用 map 自定义 datetime 格式；操作列走 col-_action 具名插槽。
  数据源 replenishApi.search 返回扁平数组无 total → fetch 包装内客户端分页；页内変更后用 listRef.reload() 就地刷新。
-->
<template>
  <CpPageShell :title="t('wms.replenish.title')" :count="total">
    <template #actions>
      <el-button @click="openCreate">{{ t('wms.common.create') }}</el-button>
      <el-button type="warning" @click="batchDialog = true">{{ t('wms.replenish.btn.genBatch') }}</el-button>
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
        <template v-if="row.status === 0">
          <el-button link type="success" size="small" @click="onExecute(row)">{{ t('wms.kit.btn.execute') }}</el-button>
          <el-button link type="danger" size="small" @click="onCancel(row)">{{ t('wms.outbound.btn.cancel') }}</el-button>
        </template>
        <span v-else class="cp-dash">—</span>
      </template>
    </CpListPage>

    <!-- 新規作成 -->
    <CpFormDialog
      v-model="dialogVisible"
      :title="t('wms.replenish.dlg.create')"
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
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import CpFormDialog from '@/components/templates/CpFormDialog.vue'
import { type Tone } from '@/components/base/CpTag.vue'
import { replenishApi } from '@/api/wms/logistics'
import type { ReplenishOrder, ReplenishSearchQuery } from '@/types/wms/wms'
import { formatQty } from '@/utils/format'

const { t } = useI18n()

// —— 头部计数 pill ——
const total = ref<number>()
const listRef = ref<InstanceType<typeof CpListPage>>()
function onReload() { listRef.value?.reload() }

// —— 码值映射（i18n 反应式） ——
const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.replenish.status.pending'),
  1: t('wms.replenish.status.executed'),
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
  return ({ 0: 'muted', 1: 'ok', 9: 'danger' } as const)[s as 0] || 'muted'
}
function triggerTone(v: string): Tone {
  return v === 'BATCH' ? 'ok' : v === 'ALERT' ? 'warn' : 'muted'
}
function codeLabel(m: Record<string, string> | Record<number, string>, v: unknown): string {
  return (m as Record<string, string>)[String(v)] || (v == null ? '' : String(v))
}

// —— 列定义 ——
const columns = computed<ListColumn[]>(() => [
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
  { prop: 'executedAt', label: t('wms.kit.fld.executedAt'), width: 160,
    map: (v) => ({ label: v ? String(v).replace('T', ' ').slice(0, 16) : '—' }) },
  { prop: '_action', label: t('wms.common.action'), width: 160, fixed: 'right' },
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

// —— 取数：包装 replenishApi.search；扁平数组无 total → 客户端分页 ——
const PAGE_CAP = 500
const fetchList: ListFetch = async ({ page, size, filters }) => {
  const f = filters as Record<string, unknown>
  const q: ReplenishSearchQuery = { pageSize: PAGE_CAP }
  if (f.replenishNo) q.replenishNo = String(f.replenishNo)
  if (f.productCd) q.productCd = String(f.productCd)
  if (f.warehouseCd) q.warehouseCd = String(f.warehouseCd)
  if (f.status !== undefined && f.status !== '') q.status = Number(f.status)
  const res = await replenishApi.search(q)
  const all = res.data || []
  const start = (page - 1) * size
  return { rows: all.slice(start, start + size), total: all.length }
}

// —— 新規作成弹窗 ——
const dialogVisible = ref(false)
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
  Object.assign(createForm, { priority: 2, productCd: '', warehouseCd: '', fromLocationCd: '', toLocationCd: '', lotNo: '', qty: 0 })
  dialogVisible.value = true
}
async function submitCreate() {
  const dto: ReplenishOrder = { ...createForm, triggerType: 'MANUAL', status: 0 }
  const res = await replenishApi.create(dto)
  ElMessage.success(`${t('wms.common.success')}: ${res.data.replenishNo}`)
}

// —— バッチ生成弹窗 ——
const batchDialog = ref(false)
const batchForm = reactive({ warehouseCd: '', minQty: 10 })
const batchRules = computed<FormRules>(() => ({
  warehouseCd: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
}))
async function submitBatch() {
  const res = await replenishApi.generateBatch(batchForm.warehouseCd, batchForm.minQty)
  ElMessage.success(t('wms.replenish.msg.batchGen', { n: res.data.generated }))
}

// —— 行操作 ——
async function onExecute(row: ReplenishOrder) {
  try {
    await ElMessageBox.confirm(t('wms.replenish.msg.executeAsk'), t('wms.common.confirm'), { type: 'warning' })
    await replenishApi.execute(row.replenishNo!)
    ElMessage.success(t('wms.common.success'))
    onReload()
  } catch { /* */ }
}
async function onCancel(row: ReplenishOrder) {
  try {
    await ElMessageBox.confirm(t('wms.inbound.msg.cancelAsk'), t('wms.common.confirm'), { type: 'warning' })
    await replenishApi.cancel(row.replenishNo!)
    ElMessage.success(t('wms.common.success'))
    onReload()
  } catch { /* */ }
}
</script>

<style scoped>
.cp-dash { color: var(--cp-muted); }
</style>
