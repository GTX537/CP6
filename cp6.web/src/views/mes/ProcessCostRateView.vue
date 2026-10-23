<template>
  <div class="mes-process-cost-rate">
    <div class="page-header">
      <h2>{{ t('工序费率') }}</h2>
      <span class="subtitle">{{ t('人工费率') }} / {{ t('制造费率') }} / {{ t('生效日') }}</span>
    </div>

    <el-alert v-if="readError" :title="readError" type="error" :closable="false" />
    <el-alert v-if="intent.error.value" :title="intent.error.value" type="warning" :closable="false" />
    <el-card shadow="never">
      <div class="table-toolbar">
        <el-input v-model="filterWgCd" size="small" style="width: 220px" :placeholder="t('工作中心')" clearable @keyup.enter="reload" />
        <el-button type="primary" size="small" @click="reload">{{ t('查询') }}</el-button>
        <el-button v-permission="'mes-process-cost-rate:edit'" size="small" @click="openCreate" :disabled="!canWrite('edit')">{{ t('新增') }}</el-button>
        <el-tag size="small" type="info">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="600" v-loading="loading">
        <el-table-column prop="wgCd" :label="t('工作中心')" width="150" show-overflow-tooltip />
        <el-table-column :label="t('人工费率')" width="180" align="right">
          <template #default="{ row }">{{ row.laborRate ?? '—' }}</template>
        </el-table-column>
        <el-table-column :label="t('制造费率')" width="180" align="right">
          <template #default="{ row }">{{ row.overheadRate ?? '—' }}</template>
        </el-table-column>
        <el-table-column :label="t('生效日')" width="140">
          <template #default="{ row }">{{ fmtDate(row.validFrom) }}</template>
        </el-table-column>
        <el-table-column :label="t('失效日')" width="140">
          <template #default="{ row }">{{ row.validTo ? fmtDate(row.validTo) : t('长期') }}</template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="140" fixed="right">
          <template #default="{ row }">
            <el-button v-permission="'mes-process-cost-rate:edit'" link type="primary" size="small" @click="openEdit(row)" :disabled="!canWrite('edit')">{{ t('编辑') }}</el-button>
            <el-button v-permission="'mes-process-cost-rate:del'" link type="danger" size="small" @click="doDelete(row)" :disabled="!canWrite('del')">{{ t('删除') }}</el-button>
          </template>
        </el-table-column>
        <template #empty><span>{{ t('暂无数据') }}</span></template>
      </el-table>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="t('工序费率')" width="560">
      <el-form :model="form" label-width="100px" size="small">
        <el-form-item :label="t('工作中心')" required>
          <el-input v-model="form.wgCd" maxlength="10" :disabled="editing" />
        </el-form-item>
        <el-row :gutter="12">
          <el-col :span="12"><el-form-item :label="t('人工费率')"><el-input v-model="form.laborRate" inputmode="decimal" :disabled="editing && !form.laborRateWritable" :placeholder="form.laborRate == null ? '—' : ''" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('制造费率')"><el-input v-model="form.overheadRate" inputmode="decimal" :disabled="editing && !form.overheadRateWritable" :placeholder="form.overheadRate == null ? '—' : ''" /></el-form-item></el-col>
        </el-row>
        <el-row :gutter="12">
          <el-col :span="12"><el-form-item :label="t('生效日')" required><el-date-picker v-model="form.validFrom" type="date" value-format="YYYY-MM-DD" style="width:100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('失效日')"><el-date-picker v-model="form.validTo" type="date" value-format="YYYY-MM-DD" style="width:100%" :placeholder="t('长期')" clearable /></el-form-item></el-col>
        </el-row>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false" :disabled="saving">{{ t('取消') }}</el-button>
        <el-button type="primary" :loading="saving" :disabled="!canWrite('edit')" @click="submit">{{ t('确定') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { processCostRateApi } from '@/api/mes/processCost'
import type { ProcessCostRate } from '@/types/mes/processCost'
import { useOperationsIntent } from '@/composables/useOperationsIntent'
import { usePermissionStore } from '@/stores/permission'

const { t } = useI18n(), permission = usePermissionStore()
const rows = ref<ProcessCostRate[]>([]), loading = ref(false), readError = ref('')
const filterWgCd = ref(''), dialogVisible = ref(false), editing = ref(false)
const emptyForm = (): ProcessCostRate => ({ id: undefined, rowVersion: undefined, wgCd: '', laborRate: '', overheadRate: '', laborRateWritable: true, overheadRateWritable: true, validFrom: '', validTo: null })
const form = reactive<ProcessCostRate>(emptyForm())
let readSequence = 0
function clearProtected() {
  readSequence++; rows.value = []; loading.value = false; dialogVisible.value = false
  Object.assign(form, emptyForm()); editing.value = false
}
const intent = useOperationsIntent(() => { clearProtected(); filterWgCd.value = ''; readError.value = '身份已变化，请重新查询。' })
const saving = intent.busy
const permitted = (action: string) => permission.loaded && permission.has(`mes-process-cost-rate:${action}`)
const canWrite = (action: string) => permitted(action) && !saving.value && !intent.uncertain.value
watch(() => permitted('query'), allowed => { if (!allowed) clearProtected() })
async function reload() {
  const token = intent.capture(), sequence = ++readSequence
  const current = () => intent.current(token) && sequence === readSequence
  rows.value = []; loading.value = true; readError.value = ''
  try {
    if (!permitted('query')) throw new Error('Current query action required')
    const result = await processCostRateApi.list(filterWgCd.value || undefined)
    if (!current()) return
    if (!permitted('query') || result?.code !== 0 || !Array.isArray(result.data)) throw new Error('Current data unavailable')
    rows.value = result.data
  } catch {
    if (current()) { clearProtected(); readError.value = '读取失败，请重新查询当前权限范围。' }
  } finally { if (current()) loading.value = false }
}
function openCreate() {
  if (!canWrite('edit')) return
  Object.assign(form, emptyForm()); editing.value = false; dialogVisible.value = true
}
function openEdit(row: ProcessCostRate) {
  if (!canWrite('edit') || !row.id || !row.rowVersion) return
  Object.assign(form, emptyForm(), row); editing.value = true; dialogVisible.value = true
}
async function submit() {
  if (!canWrite('edit') || !form.wgCd?.trim()) return
  if (!form.validFrom) return
  const token = intent.begin(); if (!token) return
  // Explicit payload excludes response metadata and caller identity; strings never pass through Number.
  const input: ProcessCostRate = { id: form.id, rowVersion: form.rowVersion, wgCd: form.wgCd,
    laborRate: !editing.value || form.laborRateWritable ? form.laborRate : undefined,
    overheadRate: !editing.value || form.overheadRateWritable ? form.overheadRate : undefined,
    validFrom: form.validFrom, validTo: form.validTo || null }
  try {
    const result = await processCostRateApi.save(input)
    if (!intent.current(token)) return
    if (result?.code !== 0) throw new Error('Unconfirmed write result')
    ElMessage.success(t('已保存')); dialogVisible.value = false; Object.assign(form, emptyForm()); await reload()
  } catch (error) {
    if (intent.current(token)) { clearProtected(); intent.failed(token, error) }
  } finally { intent.finish(token) }
}
async function doDelete(row: ProcessCostRate) {
  if (!canWrite('del') || !row.id || !row.rowVersion) return
  const token = intent.begin(); if (!token) return
  const id = row.id, version = row.rowVersion
  try {
    await ElMessageBox.confirm(t('确认删除'), t('提示'), { type: 'warning' })
    if (!intent.current(token) || !permitted('del')) return
    const result = await processCostRateApi.remove(id, version)
    if (!intent.current(token)) return
    if (result?.code !== 0) throw new Error('Unconfirmed delete result')
    ElMessage.success(t('已删除')); dialogVisible.value = false; Object.assign(form, emptyForm()); await reload()
  } catch (error) {
    if (error !== 'cancel' && error !== 'close' && intent.current(token)) { clearProtected(); intent.failed(token, error) }
  } finally { intent.finish(token) }
}
// Formatting for display never replaces the original precision-bearing timestamp in the edit payload.
function fmtDate(value?: string | null) { return value ? value.slice(0, 10) : '' }
void reload()
</script>

<style scoped>
.mes-process-cost-rate { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
</style>
