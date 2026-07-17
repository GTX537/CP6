<!--
  為替レート —— CpPageShell + CpListPage + CpFormDialog 迁移（ERP 批次1）。
  查询列表页（onMounted 自动取数、无强制查询条件）：currencyCd 过滤 → searchFields text；rateDate → kind:'date'；
  rate 6 桁固定小数 → col-rate slot（formatQty(v,6) 保原样）；remarks → overflowTooltip。
  base:JPY 信息标签 → toolbar slot（CpTag tone:info）；subtitle 同置 toolbar 保留原文案。
  新建/编辑 → CpFormDialog default slot（保留 uppercase / input-number precision6 step0.5 / textarea 特有控件）；
  删除 → ElMessageBox.confirm；in-place 変更後 listRef.reload() 保留当前筛选/页码。
-->
<template>
  <CpPageShell :title="t('erp.fxRate.title')" :count="total">
    <template #actions>
      <el-button v-permission="'erp-fx-rate:add'" type="primary" @click="openCreate">{{ t('erp.fxRate.btn.create') }}</el-button>
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
        <span class="fx-sub">{{ t('erp.fxRate.subtitle') }}</span>
        <CpTag tone="info">{{ t('erp.fxRate.base') }}: JPY</CpTag>
      </template>

      <template #col-rate="{ row }">
        <span class="num">{{ formatRate(row.rate) }}</span>
      </template>

      <template #col-_action="{ row }">
        <el-button v-permission="'erp-fx-rate:edit'" link type="primary" size="small" @click="openEdit(row)">{{ t('erp.fxRate.btn.edit') }}</el-button>
        <el-button v-permission="'erp-fx-rate:del'" link type="danger" size="small" @click="remove(row)">{{ t('erp.fxRate.btn.delete') }}</el-button>
      </template>
    </CpListPage>

    <CpFormDialog
      v-model="dialogVisible"
      :title="dialogTitle"
      width="480"
      :form="form"
      :rules="rules"
      :submit="onSubmit"
      :labels="{ cancel: t('erp.fxRate.btn.cancel'), confirm: t('erp.fxRate.btn.confirm') }"
      @saved="reloadList"
    >
      <el-form-item :label="t('erp.fxRate.col.currency')" prop="currencyCd">
        <el-input v-model="form.currencyCd" maxlength="3" style="text-transform: uppercase" :placeholder="t('erp.fxRate.hint.currency')" />
      </el-form-item>
      <el-form-item :label="t('erp.fxRate.col.rateDate')" prop="rateDate">
        <el-date-picker v-model="form.rateDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
      </el-form-item>
      <el-form-item :label="t('erp.fxRate.col.rate')" prop="rate">
        <el-input-number v-model="form.rate" :min="0" :precision="6" :step="0.5" style="width: 100%" />
        <span class="hint">{{ t('erp.fxRate.hint.rate') }}</span>
      </el-form-item>
      <el-form-item :label="t('erp.fxRate.col.remarks')" prop="remarks">
        <el-input v-model="form.remarks" type="textarea" :rows="2" maxlength="200" />
      </el-form-item>
    </CpFormDialog>
  </CpPageShell>
</template>

<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox, type FormRules } from 'element-plus'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import CpFormDialog from '@/components/templates/CpFormDialog.vue'
import CpTag from '@/components/base/CpTag.vue'
import { fxRateApi } from '@/api/erp/fxRate'
import { formatQty } from '@/utils/format'
import type { FxRate } from '@/types/erp/fxRate'

const { t } = useI18n()

const total = ref<number>()
const listRef = ref<InstanceType<typeof CpListPage> | null>(null)
function reloadList() { listRef.value?.reload() }

// —— 列定义（rate 6 桁固定 → col slot；rateDate → kind:'date'）——
const columns = computed<ListColumn[]>(() => [
  { prop: 'currencyCd', label: t('erp.fxRate.col.currency'), width: 120 },
  { prop: 'rateDate', label: t('erp.fxRate.col.rateDate'), width: 150, kind: 'date' },
  { prop: 'rate', label: t('erp.fxRate.col.rate'), width: 160, align: 'right' },
  { prop: 'remarks', label: t('erp.fxRate.col.remarks'), minWidth: 180, overflowTooltip: true },
  { prop: '_action', label: t('erp.fxRate.col.action'), width: 140, fixed: 'right' },
])

// filterLabels：search→refresh（原页 filter 即「通貨で再読込」语义）、reset→既存の sales.btn.clear
const filterLabels = computed(() => ({
  search: t('erp.fxRate.btn.refresh'),
  reset: t('sales.btn.clear'),
}))

const searchFields = computed<FilterField[]>(() => [
  { key: 'currencyCd', label: t('erp.fxRate.col.currency'), type: 'text', placeholder: t('erp.fxRate.filter.currency') },
])

// —— 取数：fxRateApi.list(currency?)；后端返回扁平数组无 total → 客户端分页 ——
const fetchList: ListFetch = async ({ page, size, filters }) => {
  const f = filters as Record<string, unknown>
  const currency = f.currencyCd ? String(f.currencyCd).trim() : undefined
  const res = await fxRateApi.list(currency || undefined)
  const all = (res?.data || []) as FxRate[]
  const start = (page - 1) * size
  return { rows: all.slice(start, start + size), total: all.length }
}

function formatRate(value: number): string {
  return formatQty(value || 0, 6)
}

// —— 新建 / 编辑弹窗 ——
const dialogVisible = ref(false)
const editingId = ref<string | null>(null)
const dialogTitle = computed(() =>
  editingId.value ? t('erp.fxRate.dlg.editTitle') : t('erp.fxRate.dlg.createTitle'),
)

function emptyForm(): FxRate {
  return { currencyCd: '', rateDate: formatToday(), rate: 1, remarks: null }
}
const form = reactive<FxRate>(emptyForm())

const rules = computed<FormRules>(() => ({
  currencyCd: [{ required: true, message: t('erp.fxRate.msg.required'), trigger: 'blur' }],
  rateDate: [{ required: true, message: t('erp.fxRate.msg.required'), trigger: 'change' }],
  rate: [{ required: true, message: t('erp.fxRate.msg.required'), trigger: 'change' }],
}))

function openCreate() {
  editingId.value = null
  Object.assign(form, emptyForm())
  dialogVisible.value = true
}

function openEdit(row: FxRate) {
  editingId.value = row.id || null
  Object.assign(form, { ...emptyForm(), ...row, rateDate: (row.rateDate || '').slice(0, 10) })
  dialogVisible.value = true
}

async function onSubmit() {
  const payload: FxRate = { ...form, currencyCd: form.currencyCd.trim().toUpperCase(), remarks: form.remarks || null }
  if (editingId.value) {
    await fxRateApi.update(editingId.value, payload)
    ElMessage.success(t('erp.fxRate.msg.updated'))
  } else {
    await fxRateApi.create(payload)
    ElMessage.success(t('erp.fxRate.msg.created'))
  }
}

async function remove(row: FxRate) {
  await ElMessageBox.confirm(
    t('erp.fxRate.msg.deleteConfirm', { cur: row.currencyCd, date: (row.rateDate || '').slice(0, 10) }),
    t('erp.fxRate.btn.delete'),
    { type: 'warning' },
  )
  if (!row.id) return
  await fxRateApi.remove(row.id)
  ElMessage.success(t('erp.fxRate.msg.deleted'))
  reloadList()
}

function formatToday(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}
</script>

<style scoped>
.fx-sub { font-size: var(--cp-fs-sm); color: var(--cp-muted); margin-right: auto; }
.hint { color: var(--cp-muted); font-size: var(--cp-fs-sm); margin-left: 8px; }
</style>
