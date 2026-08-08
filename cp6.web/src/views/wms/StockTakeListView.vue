<!--
  棚卸一覧 —— CpPageShell + CpListPage + CpFormDialog 迁移（WMS 批次1）。
  種別列纯 map（原页无 tag 视觉）；状態列 kind:'tag'+map；予定日 kind:'date'；実施/完了日 走 col slot（保 '—' 空态）；操作 col slot。
  スナップショット(計画作成)用 CpFormDialog（default slot 保留 date-picker/input-number/placeholder）；成功后 router.push 至明细，无需刷新列表。
-->
<template>
  <CpPageShell :title="t('wms.stocktake.titleList')" :count="total">
    <template #actions>
      <el-button @click="openPlan">{{ t('wms.stocktake.btn.snapshot') }}</el-button>
    </template>

    <CpListPage
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

    <CpFormDialog
      v-model="planDialog"
      :title="t('wms.stocktake.btn.snapshot')"
      width="540"
      :form="planForm"
      :rules="rules"
      :submit="onCreatePlan"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.common.create') }"
    >
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
import { ref, reactive, computed } from 'vue'
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
  const res = await stockTakeApi.search(q)
  const all = res.data || []
  const start = (page - 1) * size
  return { rows: all.slice(start, start + size), total: all.length }
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

function openPlan() {
  Object.assign(planForm, {
    stockTakeType: 1,
    plannedDate: new Date().toISOString().slice(0, 10),
    targetWarehouseCd: '',
    targetLocationPrefix: '',
    targetProductCd: '',
    approvalThresholdAmount: undefined,
    remarks: '',
  })
  planDialog.value = true
}

async function onCreatePlan() {
  const res = await stockTakeApi.createPlan(planForm)
  ElMessage.success(`${t('wms.common.success')}: ${res.data.stockTakeNo}`)
  router.push({ path: '/wms/stock-take', query: { no: res.data.stockTakeNo } })
}
</script>
