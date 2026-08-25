<!--
  クロスドック —— CpPageShell + CpListPage + CpFormDialog 迁移（WMS 批次1）。
  状態列 kind:'tag'+map；数量/実行日時/操作 走 col slot；新建用 CpFormDialog（default slot 保留 input-number/placeholder/maxlength 等 fields 声明表达不了的表单）。
  必填(品目/倉庫/一時ロケ)改由 el-form rules 校验（CpFormDialog validate() 门禁），等价原 onSave 手工校验。
  in-place 变更(新建/実行/取消)后 listRef.reload() 命令式刷新（契约扩展二轮 #12），保留当前筛选/页码。
-->
<template>
  <CpPageShell :title="t('wms.xdock.title')" :count="total">
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
      <template #col-qty="{ row }">{{ formatQty(row.qty) }}</template>
      <template #col-executedAt="{ row }">{{ row.executedAt?.replace('T', ' ').slice(0, 16) || '—' }}</template>
      <template #col-_action="{ row }">
        <template v-if="row.status === 0">
          <el-button v-permission="'wms-cross-dock:execute'" link type="success" size="small" @click="onExecute(row)">{{ t('wms.kit.btn.execute') }}</el-button>
          <el-button link type="danger" size="small" @click="onCancel(row)">{{ t('wms.outbound.btn.cancel') }}</el-button>
        </template>
        <span v-else class="cp-dash">—</span>
      </template>
    </CpListPage>

    <CpFormDialog
      v-model="dialogVisible"
      :title="t('wms.xdock.dlg.create')"
      width="560"
      :form="form"
      :rules="rules"
      :submit="onSave"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.common.save') }"
      @saved="reloadList"
    >
      <el-form-item :label="t('wms.common.product')" prop="productCd">
        <el-input v-model="form.productCd" maxlength="20" />
      </el-form-item>
      <el-form-item :label="t('wms.common.productName')">
        <el-input v-model="form.productName" maxlength="100" />
      </el-form-item>
      <el-form-item :label="t('wms.common.qty')" prop="qty">
        <el-input-number v-model="form.qty" :min="0" :precision="2" controls-position="right" style="width: 100%" />
      </el-form-item>
      <el-form-item :label="t('wms.common.warehouse')" prop="warehouseCd">
        <el-input v-model="form.warehouseCd" maxlength="10" />
      </el-form-item>
      <el-form-item :label="t('wms.xdock.fld.tempLoc')" prop="tempLocationCd">
        <el-input v-model="form.tempLocationCd" maxlength="30" />
      </el-form-item>
      <el-form-item :label="t('wms.common.lot')">
        <el-input v-model="form.lotNo" placeholder="auto: XD<date>-<seq>" maxlength="30" />
      </el-form-item>
      <el-form-item :label="t('wms.xdock.fld.fromDock')"><el-input v-model="form.fromDock" maxlength="30" /></el-form-item>
      <el-form-item :label="t('wms.xdock.fld.toDock')"><el-input v-model="form.toDock" maxlength="30" /></el-form-item>
      <el-form-item :label="t('wms.inbound.fld.supplierCd')"><el-input v-model="form.supplierCd" maxlength="20" /></el-form-item>
      <el-form-item :label="t('wms.outbound.fld.customerCd')"><el-input v-model="form.customerCd" maxlength="20" /></el-form-item>
      <el-form-item :label="t('wms.common.remarks')"><el-input v-model="form.remarks" type="textarea" :rows="2" /></el-form-item>
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
import { crossDockApi } from '@/api/wms/logistics'
import type { CrossDockOrder, CrossDockSearchQuery } from '@/types/wms/wms'
import { formatQty } from '@/utils/format'

const { t } = useI18n()

const total = ref<number>()
// in-place 变更后命令式刷新（保留当前筛选/页码）
const listRef = ref<ListPageExpose | null>(null)
function reloadList() { listRef.value?.reload() }

const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.xdock.status.planned'),
  1: t('wms.xdock.status.executed'),
  9: t('wms.xdock.status.cancelled'),
}))
// 原 statusTagOf(info/success/danger) → 设计系统 Tone（保色）
function statusTone(s: number): Tone {
  return ({ 0: 'muted', 1: 'ok', 9: 'danger' } as const)[s as 0] || 'muted'
}
function codeLabel(m: Record<number, string>, v: unknown): string {
  return m[v as number] || (v == null ? '' : String(v))
}

const columns = computed<ListColumn<CrossDockOrder>[]>(() => [
  { prop: 'xDockNo', label: t('wms.xdock.fld.no'), kind: 'mono', width: 180 },
  { prop: 'status', label: t('wms.common.status'), width: 110, kind: 'tag',
    map: (v) => ({ label: codeLabel(statusMap.value, v), tone: statusTone(v as number) }) },
  { prop: 'productCd', label: t('wms.common.product'), width: 120 },
  { prop: 'qty', label: t('wms.common.qty'), width: 100, align: 'right' },
  { prop: 'supplierCd', label: t('wms.inbound.fld.supplierCd'), width: 120 },
  { prop: 'customerCd', label: t('wms.outbound.fld.customerCd'), width: 120 },
  { prop: 'fromDock', label: t('wms.xdock.fld.fromDock'), width: 140 },
  { prop: 'toDock', label: t('wms.xdock.fld.toDock'), width: 140 },
  { prop: 'tempLocationCd', label: t('wms.xdock.fld.tempLoc'), width: 140 },
  { prop: 'executedAt', label: t('wms.kit.fld.executedAt'), kind: 'datetime', width: 180 },
  { prop: '_action', label: t('wms.common.action'), width: 160, fixed: 'right' },
])

const filterLabels = computed(() => ({
  search: t('wms.common.search'),
  reset: t('wms.common.clear'),
}))

const searchFields = computed<FilterField[]>(() => [
  { key: 'xdockNo', label: t('wms.xdock.fld.no'), type: 'text' },
  { key: 'productCd', label: t('wms.common.product'), type: 'text' },
  {
    key: 'status', label: t('wms.common.status'), type: 'select',
    options: Object.entries(statusMap.value).map(([v, l]) => ({ label: l, value: Number(v) })),
  },
])

const PAGE_CAP = 500
const fetchList: ListFetch<CrossDockOrder> = async ({ page, size, filters }) => {
  const f = filters as Record<string, unknown>
  const q: CrossDockSearchQuery = { pageSize: PAGE_CAP }
  if (f.xdockNo) q.xdockNo = String(f.xdockNo)
  if (f.productCd) q.productCd = String(f.productCd)
  if (f.status !== undefined && f.status !== '') q.status = Number(f.status)
  const res = await crossDockApi.search(q)
  const all = res.data || []
  const start = (page - 1) * size
  return { rows: all.slice(start, start + size), total: all.length }
}

// —— 新建对话框 ——
const dialogVisible = ref(false)
const form = reactive<CrossDockOrder>({
  productCd: '', qty: 0, warehouseCd: '', tempLocationCd: '', lotNo: '', status: 0,
})
const rules = computed<FormRules>(() => ({
  productCd: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
  // qty：原页有 required 视觉星号但无校验；按 Warehouse 先例补规则（星号还原 + 校验强化）
  qty: [{ required: true, message: t('wms.common.required'), trigger: 'change' }],
  warehouseCd: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
  tempLocationCd: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
}))

function openCreate() {
  Object.assign(form, {
    productCd: '', productName: '', qty: 0, warehouseCd: '', tempLocationCd: '', lotNo: '',
    fromDock: '', toDock: '', supplierCd: '', customerCd: '', remarks: '', status: 0,
  })
  dialogVisible.value = true
}

async function onSave() {
  const res = await crossDockApi.create(form)
  ElMessage.success(`${t('wms.common.success')}: ${res.data.xdockNo}`)
}

async function onExecute(row: CrossDockOrder) {
  try {
    await ElMessageBox.confirm(t('wms.xdock.msg.executeAsk'), t('wms.common.confirm'), { type: 'warning' })
    await crossDockApi.execute(row.xDockNo!)
    ElMessage.success(t('wms.common.success'))
    reloadList()
  } catch { /* */ }
}

async function onCancel(row: CrossDockOrder) {
  try {
    await ElMessageBox.confirm(t('wms.inbound.msg.cancelAsk'), t('wms.common.confirm'), { type: 'warning' })
    await crossDockApi.cancel(row.xDockNo!)
    ElMessage.success(t('wms.common.success'))
    reloadList()
  } catch { /* */ }
}
</script>

<style scoped>
.cp-dash { color: var(--cp-muted); }
</style>
