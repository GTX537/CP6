<template>
  <div class="mes-process-cost-rate">
    <div class="page-header">
      <h2>{{ t('工序费率') }}</h2>
      <span class="subtitle">{{ t('人工费率') }} / {{ t('制造费率') }} / {{ t('生效日') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-input v-model="filterWgCd" size="small" style="width: 220px" :placeholder="t('工作中心')" clearable @keyup.enter="reload" />
        <el-button type="primary" size="small" @click="reload">{{ t('查询') }}</el-button>
        <el-button size="small" @click="openCreate">{{ t('新增') }}</el-button>
        <el-tag size="small" type="info">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="600" v-loading="loading">
        <el-table-column prop="wgCd" :label="t('工作中心')" width="150" show-overflow-tooltip />
        <el-table-column prop="laborRate" :label="t('人工费率')" width="130" align="right" />
        <el-table-column prop="overheadRate" :label="t('制造费率')" width="130" align="right" />
        <el-table-column :label="t('生效日')" width="140">
          <template #default="{ row }">{{ fmtDate(row.validFrom) }}</template>
        </el-table-column>
        <el-table-column :label="t('失效日')" width="140">
          <template #default="{ row }">{{ row.validTo ? fmtDate(row.validTo) : t('长期') }}</template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="140" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="openEdit(row)">{{ t('编辑') }}</el-button>
            <el-button link type="danger" size="small" @click="doDelete(row)">{{ t('删除') }}</el-button>
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
          <el-col :span="12"><el-form-item :label="t('人工费率')"><el-input-number v-model="form.laborRate" :min="0" :precision="2" controls-position="right" style="width:100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('制造费率')"><el-input-number v-model="form.overheadRate" :min="0" :precision="2" controls-position="right" style="width:100%" /></el-form-item></el-col>
        </el-row>
        <el-row :gutter="12">
          <el-col :span="12"><el-form-item :label="t('生效日')" required><el-date-picker v-model="form.validFrom" type="date" value-format="YYYY-MM-DD" style="width:100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('失效日')"><el-date-picker v-model="form.validTo" type="date" value-format="YYYY-MM-DD" style="width:100%" :placeholder="t('长期')" clearable /></el-form-item></el-col>
        </el-row>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false" :disabled="saving">{{ t('取消') }}</el-button>
        <el-button type="primary" :loading="saving" @click="submit">{{ t('确定') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { processCostRateApi } from '@/api/mes/processCost'
import type { ProcessCostRate } from '@/types/mes/processCost'

const { t } = useI18n()
const rows = ref<ProcessCostRate[]>([])
const loading = ref(false)
const saving = ref(false)
const filterWgCd = ref('')
const dialogVisible = ref(false)
const editing = ref(false)

function emptyForm(): ProcessCostRate {
  return { wgCd: '', laborRate: 0, overheadRate: 0, validFrom: '', validTo: null }
}
const form = reactive<ProcessCostRate>(emptyForm())

async function reload() {
  loading.value = true
  try {
    const res = await processCostRateApi.list(filterWgCd.value.trim() || undefined)
    rows.value = res?.data || []
  } finally {
    loading.value = false
  }
}

function openCreate() {
  Object.assign(form, emptyForm())
  if (filterWgCd.value.trim()) form.wgCd = filterWgCd.value.trim()
  editing.value = false
  dialogVisible.value = true
}

function openEdit(row: ProcessCostRate) {
  Object.assign(form, { ...row })
  editing.value = true
  dialogVisible.value = true
}

async function submit() {
  if (!form.wgCd?.trim()) { ElMessage.warning(t('工作中心')); return }
  if (!form.validFrom) { ElMessage.warning(t('生效日')); return }
  saving.value = true
  try {
    await processCostRateApi.save({ ...form, wgCd: form.wgCd.trim(), validTo: form.validTo || null })
    ElMessage.success(t('已保存'))
    dialogVisible.value = false
    await reload()
  } finally {
    saving.value = false
  }
}

// 生效/失效日为日期字段（后端返 ISO datetime），仅展示日期部分
function fmtDate(v?: string | null): string { return v ? String(v).slice(0, 10) : '' }

async function doDelete(row: ProcessCostRate) {
  if (!row.id) { ElMessage.warning(t('确认删除')); return }
  await ElMessageBox.confirm(t('确认删除'), t('提示'), { type: 'warning' })
  await processCostRateApi.remove(row.id)
  ElMessage.success(t('已删除'))
  await reload()
}

reload()
</script>

<style scoped>
.mes-process-cost-rate { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
</style>
