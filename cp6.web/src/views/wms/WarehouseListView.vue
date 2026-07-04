<!--
  倉庫マスタ —— CpPageShell + CpListPage + CpFormDialog 迁移（WMS 批次1）。
  種別列 kind:'tag'+map；マイナス許可/操作 走 col slot；新建/編集共用 CpFormDialog（default slot 保留 switch/select/disabled 等表单）。
  必填(倉庫CD/倉庫名)由 el-form rules 校验。in-place 变更(新建/編集/削除)后自增 reloadKey 刷新（模板缺口 #12）。
-->
<template>
  <CpPageShell :title="t('wms.warehouse.title')" :count="total">
    <template #actions>
      <el-button @click="openCreate">{{ t('wms.common.create') }}</el-button>
    </template>

    <CpListPage
      :key="reloadKey"
      :columns="columns"
      :fetch="fetchList"
      :search-fields="searchFields"
      :filter-labels="filterLabels"
      @total-change="total = $event"
    >
      <template #col-allowNegative="{ row }">
        <CpTag v-if="row.allowNegative" tone="warn">{{ t('wms.warehouse.fld.allowed') }}</CpTag>
        <span v-else class="cp-dash">—</span>
      </template>
      <template #col-_action="{ row }">
        <el-button link type="primary" size="small" @click="openEdit(row)">{{ t('wms.common.edit') }}</el-button>
        <el-button link type="danger" size="small" @click="onDelete(row)">{{ t('wms.common.delete') }}</el-button>
      </template>
    </CpListPage>

    <CpFormDialog
      v-model="dialogVisible"
      :title="dialogTitle"
      width="560"
      :form="form"
      :rules="rules"
      :submit="onSave"
      :labels="{ cancel: t('wms.common.cancel'), confirm: t('wms.common.save') }"
      @saved="reloadKey++"
    >
      <el-form-item :label="t('wms.warehouse.fld.cd')" prop="warehouseCd">
        <el-input v-model="form.warehouseCd" :disabled="!!form.id" maxlength="10" />
      </el-form-item>
      <el-form-item :label="t('wms.warehouse.fld.name')" prop="warehouseName">
        <el-input v-model="form.warehouseName" maxlength="100" />
      </el-form-item>
      <el-form-item :label="t('wms.warehouse.fld.type')">
        <el-select v-model="form.warehouseType">
          <el-option v-for="(label, val) in warehouseTypeMap" :key="val" :label="label" :value="Number(val)" />
        </el-select>
      </el-form-item>
      <el-form-item :label="t('wms.warehouse.fld.baseCd')"><el-input v-model="form.baseCd" maxlength="10" /></el-form-item>
      <el-form-item :label="t('wms.warehouse.fld.manager')"><el-input v-model="form.managerCd" maxlength="20" /></el-form-item>
      <el-form-item :label="t('wms.warehouse.fld.address')"><el-input v-model="form.addressText" maxlength="200" /></el-form-item>
      <el-form-item :label="t('wms.warehouse.fld.allowNegative')"><el-switch v-model="form.allowNegative" /></el-form-item>
      <el-form-item :label="t('wms.common.remarks')"><el-input v-model="form.remarks" type="textarea" :rows="2" /></el-form-item>
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
import { warehouseApi } from '@/api/wms/warehouse'
import type { Warehouse } from '@/types/wms/wms'

const { t } = useI18n()

const total = ref<number>()
const reloadKey = ref(0)

const warehouseTypeMap = computed<Record<number, string>>(() => ({
  1: t('wms.warehouse.type.raw'),
  2: t('wms.warehouse.type.wip'),
  3: t('wms.warehouse.type.finished'),
  4: t('wms.warehouse.type.defective'),
  5: t('wms.warehouse.type.external'),
}))
// 原 typeTagOf(primary/success/success/danger/info) → 设计系统 Tone（保色）
function typeTone(v: number): Tone {
  return ({ 1: 'info', 2: 'ok', 3: 'ok', 4: 'danger', 5: 'muted' } as const)[v as 1] || 'muted'
}
function codeLabel(m: Record<number, string>, v: unknown): string {
  return m[v as number] || (v == null ? '' : String(v))
}

const columns = computed<ListColumn[]>(() => [
  { prop: 'warehouseCd', label: t('wms.warehouse.fld.cd'), width: 120 },
  { prop: 'warehouseName', label: t('wms.warehouse.fld.name'), minWidth: 200 },
  { prop: 'warehouseType', label: t('wms.warehouse.fld.type'), width: 120, kind: 'tag',
    map: (v) => ({ label: codeLabel(warehouseTypeMap.value, v), tone: typeTone(v as number) }) },
  { prop: 'baseCd', label: t('wms.warehouse.fld.baseCd'), width: 100 },
  { prop: 'managerCd', label: t('wms.warehouse.fld.manager'), width: 120 },
  { prop: 'allowNegative', label: t('wms.warehouse.fld.allowNegative'), width: 120, align: 'center' },
  { prop: 'addressText', label: t('wms.warehouse.fld.address'), minWidth: 200, overflowTooltip: true },
  { prop: '_action', label: t('wms.common.action'), width: 160, fixed: 'right' },
])

const filterLabels = computed(() => ({
  search: t('wms.common.search'),
  reset: t('wms.common.clear'),
}))

const searchFields = computed<FilterField[]>(() => [
  { key: 'warehouseCd', label: t('wms.warehouse.fld.cd'), type: 'text' },
  {
    key: 'warehouseType', label: t('wms.warehouse.fld.type'), type: 'select',
    options: Object.entries(warehouseTypeMap.value).map(([v, l]) => ({ label: l, value: Number(v) })),
  },
  { key: 'baseCd', label: t('wms.warehouse.fld.baseCd'), type: 'text' },
])

const fetchList: ListFetch = async ({ page, size, filters }) => {
  const f = filters as Record<string, unknown>
  const q: { warehouseCd?: string; warehouseType?: number; baseCd?: string } = {}
  if (f.warehouseCd) q.warehouseCd = String(f.warehouseCd)
  if (f.warehouseType !== undefined && f.warehouseType !== '') q.warehouseType = Number(f.warehouseType)
  if (f.baseCd) q.baseCd = String(f.baseCd)
  const res = await warehouseApi.search(q)
  const all = res.data || []
  const start = (page - 1) * size
  return { rows: all.slice(start, start + size), total: all.length }
}

// —— 新建/編集对话框 ——
const dialogVisible = ref(false)
const form = reactive<Warehouse>({ warehouseCd: '', warehouseName: '', warehouseType: 1, allowNegative: false })
const dialogTitle = computed(() => (form.id ? t('wms.warehouse.dlg.edit') : t('wms.warehouse.dlg.create')))
const rules = computed<FormRules>(() => ({
  warehouseCd: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
  warehouseName: [{ required: true, message: t('wms.common.required'), trigger: 'blur' }],
}))

function openCreate() {
  Object.assign(form, {
    id: undefined, warehouseCd: '', warehouseName: '', warehouseType: 1, baseCd: '',
    managerCd: '', addressText: '', allowNegative: false, remarks: '',
  })
  dialogVisible.value = true
}
function openEdit(row: Warehouse) {
  Object.assign(form, { baseCd: '', managerCd: '', addressText: '', remarks: '', ...row })
  dialogVisible.value = true
}

async function onSave() {
  if (form.id) {
    await warehouseApi.update(form.warehouseCd, form)
  } else {
    await warehouseApi.create(form)
  }
  ElMessage.success(t('wms.common.success'))
}

async function onDelete(row: Warehouse) {
  try {
    await ElMessageBox.confirm(`${t('wms.common.confirmDelete')} [${row.warehouseCd}]`, t('wms.common.confirm'), { type: 'warning' })
    await warehouseApi.delete(row.warehouseCd)
    ElMessage.success(t('wms.common.success'))
    reloadKey.value++
  } catch { /* */ }
}
</script>

<style scoped>
.cp-dash { color: var(--cp-muted); }
</style>
