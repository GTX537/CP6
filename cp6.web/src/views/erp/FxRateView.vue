<template>
  <div class="fx-rate">
    <div class="page-header">
      <h2>{{ t('erp.fxRate.title') }}</h2>
      <span class="subtitle">{{ t('erp.fxRate.subtitle') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-button type="primary" size="small" @click="openCreate">{{ t('erp.fxRate.btn.create') }}</el-button>
        <el-input
          v-model="filterCurrency"
          size="small"
          clearable
          :placeholder="t('erp.fxRate.filter.currency')"
          style="width: 140px"
          @keyup.enter="reload"
          @clear="reload"
        />
        <el-button size="small" @click="reload">{{ t('erp.fxRate.btn.refresh') }}</el-button>
        <el-tag size="small" type="info">{{ t('erp.fxRate.base') }}: JPY</el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="560" v-loading="loading">
        <el-table-column prop="currencyCd" :label="t('erp.fxRate.col.currency')" width="120" />
        <el-table-column prop="rateDate" :label="t('erp.fxRate.col.rateDate')" width="150">
          <template #default="{ row }">{{ formatDate(row.rateDate) }}</template>
        </el-table-column>
        <el-table-column prop="rate" :label="t('erp.fxRate.col.rate')" width="160" align="right">
          <template #default="{ row }">{{ formatRate(row.rate) }}</template>
        </el-table-column>
        <el-table-column prop="remarks" :label="t('erp.fxRate.col.remarks')" min-width="180" show-overflow-tooltip />
        <el-table-column :label="t('erp.fxRate.col.action')" width="140" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="openEdit(row)">{{ t('erp.fxRate.btn.edit') }}</el-button>
            <el-button link type="danger" size="small" @click="remove(row)">{{ t('erp.fxRate.btn.delete') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="dialogTitle" width="480">
      <el-form :model="form" label-width="110px">
        <el-form-item :label="t('erp.fxRate.col.currency')" required>
          <el-input v-model="form.currencyCd" maxlength="3" style="text-transform: uppercase" :placeholder="t('erp.fxRate.hint.currency')" />
        </el-form-item>
        <el-form-item :label="t('erp.fxRate.col.rateDate')" required>
          <el-date-picker v-model="form.rateDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
        </el-form-item>
        <el-form-item :label="t('erp.fxRate.col.rate')" required>
          <el-input-number v-model="form.rate" :min="0" :precision="6" :step="0.5" style="width: 100%" />
          <span class="hint">{{ t('erp.fxRate.hint.rate') }}</span>
        </el-form-item>
        <el-form-item :label="t('erp.fxRate.col.remarks')">
          <el-input v-model="form.remarks" type="textarea" :rows="2" maxlength="200" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false" :disabled="saving">{{ t('erp.fxRate.btn.cancel') }}</el-button>
        <el-button type="primary" :loading="saving" @click="submit">{{ t('erp.fxRate.btn.confirm') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { fxRateApi } from '@/api/erp/fxRate'
import type { FxRate } from '@/types/erp/fxRate'

const { t } = useI18n()

const rows = ref<FxRate[]>([])
const loading = ref(false)
const saving = ref(false)
const filterCurrency = ref('')

const dialogVisible = ref(false)
const editingId = ref<string | null>(null)
const dialogTitle = computed(() =>
  editingId.value ? t('erp.fxRate.dlg.editTitle') : t('erp.fxRate.dlg.createTitle'),
)

function emptyForm(): FxRate {
  return { currencyCd: '', rateDate: formatToday(), rate: 1, remarks: null }
}
const form = reactive<FxRate>(emptyForm())

async function reload() {
  loading.value = true
  try {
    const res = await fxRateApi.list(filterCurrency.value?.trim() || undefined)
    rows.value = (res?.data || []) as FxRate[]
  } finally {
    loading.value = false
  }
}

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

async function submit() {
  if (!form.currencyCd?.trim() || !form.rateDate || !form.rate) {
    ElMessage.warning(t('erp.fxRate.msg.required'))
    return
  }
  saving.value = true
  try {
    const payload: FxRate = { ...form, currencyCd: form.currencyCd.trim().toUpperCase(), remarks: form.remarks || null }
    if (editingId.value) {
      await fxRateApi.update(editingId.value, payload)
      ElMessage.success(t('erp.fxRate.msg.updated'))
    } else {
      await fxRateApi.create(payload)
      ElMessage.success(t('erp.fxRate.msg.created'))
    }
    dialogVisible.value = false
    await reload()
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || t('erp.fxRate.msg.failed'))
  } finally {
    saving.value = false
  }
}

async function remove(row: FxRate) {
  await ElMessageBox.confirm(
    t('erp.fxRate.msg.deleteConfirm', { cur: row.currencyCd, date: formatDate(row.rateDate) }),
    t('erp.fxRate.btn.delete'),
    { type: 'warning' },
  )
  if (!row.id) return
  await fxRateApi.remove(row.id)
  ElMessage.success(t('erp.fxRate.msg.deleted'))
  await reload()
}

function formatDate(value?: string): string {
  return value ? value.slice(0, 10) : ''
}

function formatRate(value: number): string {
  return Number(value || 0).toLocaleString('ja-JP', { maximumFractionDigits: 6 })
}

function formatToday(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

onMounted(reload)
</script>

<style scoped>
.fx-rate {
  padding: 16px;
}
.page-header {
  margin-bottom: 12px;
}
.page-header h2 {
  margin: 0;
  color: #303133;
  font-size: 20px;
  font-weight: 650;
}
.subtitle {
  color: #909399;
  font-size: 12px;
}
.table-toolbar {
  margin-bottom: 8px;
  display: flex;
  gap: 8px;
  align-items: center;
}
.hint {
  color: #909399;
  font-size: 12px;
  margin-left: 8px;
}
</style>
