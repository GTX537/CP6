<template>
  <div class="wms-warehouse">
    <el-card shadow="never" class="search-card">
      <el-form :model="query" inline size="small">
        <el-form-item :label="t('wms.warehouse.fld.cd')"><el-input v-model="query.warehouseCd" clearable style="width: 140px" /></el-form-item>
        <el-form-item :label="t('wms.warehouse.fld.type')">
          <el-select v-model="query.warehouseType" clearable style="width: 140px">
            <el-option v-for="(label, val) in warehouseTypeMap" :key="val" :label="label" :value="Number(val)" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('wms.warehouse.fld.baseCd')"><el-input v-model="query.baseCd" clearable style="width: 120px" /></el-form-item>
        <el-form-item>
          <el-button type="primary" @click="reload" :loading="loading">{{ t('wms.common.search') }}</el-button>
          <el-button @click="openCreate">{{ t('wms.common.create') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <el-table :data="rows" border stripe size="small" max-height="600" highlight-current-row>
        <el-table-column prop="warehouseCd" :label="t('wms.warehouse.fld.cd')" width="120" />
        <el-table-column prop="warehouseName" :label="t('wms.warehouse.fld.name')" min-width="200" />
        <el-table-column :label="t('wms.warehouse.fld.type')" width="120">
          <template #default="{ row }">
            <el-tag :type="typeTagOf(row.warehouseType)" size="small">{{ warehouseTypeMap[row.warehouseType] || row.warehouseType }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="baseCd" :label="t('wms.warehouse.fld.baseCd')" width="100" />
        <el-table-column prop="managerCd" :label="t('wms.warehouse.fld.manager')" width="120" />
        <el-table-column :label="t('wms.warehouse.fld.allowNegative')" width="120" align="center">
          <template #default="{ row }">
            <el-tag v-if="row.allowNegative" type="warning" size="small">{{ t('wms.warehouse.fld.allowed') }}</el-tag>
            <span v-else>—</span>
          </template>
        </el-table-column>
        <el-table-column prop="addressText" :label="t('wms.warehouse.fld.address')" min-width="200" show-overflow-tooltip />
        <el-table-column :label="t('wms.common.action')" width="160" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="openEdit(row)">{{ t('wms.common.edit') }}</el-button>
            <el-button link type="danger" size="small" @click="onDelete(row)">{{ t('wms.common.delete') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="editing?.id ? t('wms.warehouse.dlg.edit') : t('wms.warehouse.dlg.create')" width="560">
      <el-form v-if="editing" :model="editing" label-width="160px" size="small">
        <el-form-item :label="t('wms.warehouse.fld.cd')" required>
          <el-input v-model="editing.warehouseCd" :disabled="!!editing.id" maxlength="10" />
        </el-form-item>
        <el-form-item :label="t('wms.warehouse.fld.name')" required><el-input v-model="editing.warehouseName" maxlength="100" /></el-form-item>
        <el-form-item :label="t('wms.warehouse.fld.type')">
          <el-select v-model="editing.warehouseType">
            <el-option v-for="(label, val) in warehouseTypeMap" :key="val" :label="label" :value="Number(val)" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('wms.warehouse.fld.baseCd')"><el-input v-model="editing.baseCd" maxlength="10" /></el-form-item>
        <el-form-item :label="t('wms.warehouse.fld.manager')"><el-input v-model="editing.managerCd" maxlength="20" /></el-form-item>
        <el-form-item :label="t('wms.warehouse.fld.address')"><el-input v-model="editing.addressText" maxlength="200" /></el-form-item>
        <el-form-item :label="t('wms.warehouse.fld.allowNegative')"><el-switch v-model="editing.allowNegative" /></el-form-item>
        <el-form-item :label="t('wms.common.remarks')"><el-input v-model="editing.remarks" type="textarea" :rows="2" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onSave" :loading="saving">{{ t('wms.common.save') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, reactive, computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { warehouseApi } from '@/api/wms/warehouse'
import type { Warehouse } from '@/types/wms/wms'

const { t } = useI18n()

const warehouseTypeMap = computed<Record<number, string>>(() => ({
  1: t('wms.warehouse.type.raw'),
  2: t('wms.warehouse.type.wip'),
  3: t('wms.warehouse.type.finished'),
  4: t('wms.warehouse.type.defective'),
  5: t('wms.warehouse.type.external'),
}))

const query = reactive<{ warehouseCd?: string; warehouseType?: number; baseCd?: string }>({})
const rows = ref<Warehouse[]>([])
const loading = ref(false)

const dialogVisible = ref(false)
const editing = ref<Warehouse | null>(null)
const saving = ref(false)

function typeTagOf(t: number): 'primary' | 'success' | 'warning' | 'danger' | 'info' {
  return ({ 1: 'primary', 2: 'success', 3: 'success', 4: 'danger', 5: 'info' } as const)[t] || 'info'
}

async function reload() {
  loading.value = true
  try {
    const res = await warehouseApi.search(query)
    rows.value = res.data || []
  } finally { loading.value = false }
}

function openCreate() {
  editing.value = { warehouseCd: '', warehouseName: '', warehouseType: 1, allowNegative: false }
  dialogVisible.value = true
}

function openEdit(row: Warehouse) {
  editing.value = { ...row }
  dialogVisible.value = true
}

async function onSave() {
  if (!editing.value) return
  saving.value = true
  try {
    if (editing.value.id) {
      await warehouseApi.update(editing.value.warehouseCd, editing.value)
      ElMessage.success(t('wms.common.success'))
    } else {
      await warehouseApi.create(editing.value)
      ElMessage.success(t('wms.common.success'))
    }
    dialogVisible.value = false
    reload()
  } finally { saving.value = false }
}

async function onDelete(row: Warehouse) {
  try {
    await ElMessageBox.confirm(`${t('wms.common.confirmDelete')} [${row.warehouseCd}]`, t('wms.common.confirm'), { type: 'warning' })
    await warehouseApi.delete(row.warehouseCd)
    ElMessage.success(t('wms.common.success'))
    reload()
  } catch { /* */ }
}

onMounted(reload)
</script>

<style scoped>
.wms-warehouse { padding: 16px; }
.search-card { margin-bottom: 12px; }
</style>
