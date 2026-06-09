<template>
  <div>
    <div style="margin-bottom: 12px;">
      <el-button type="primary" size="small" :icon="Plus" @click="addRow">工程追加</el-button>
      <el-button size="small" :icon="Sort" @click="autoSort">並び順 再付番</el-button>
    </div>
    <el-table :data="model" border stripe size="small" style="width: 100%;">
      <el-table-column :label="t('順')" width="60" align="center">
        <template #default="{ row }">{{ row.sortOrder }}</template>
      </el-table-column>
      <el-table-column :label="t('工程CD')" width="120">
        <template #default="{ row }">
          <el-input v-model="row.processCd" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('作業CD')" width="120">
        <template #default="{ row }">
          <el-input v-model="row.taskCd" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('工程名')" min-width="160">
        <template #default="{ row }">
          <el-input v-model="row.processName" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('号機/発注先CD')" width="160">
        <template #default="{ row }">
          <el-input v-model="row.machineCd" size="small" />
        </template>
      </el-table-column>
      <el-table-column label="WG" width="100">
        <template #default="{ row }">
          <el-input v-model="row.wgCd" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('計画開始日時')" width="180">
        <template #default="{ row }">
          <el-date-picker v-model="row.planStartTime" type="datetime" value-format="YYYY-MM-DDTHH:mm:ss" size="small" style="width: 100%;" />
        </template>
      </el-table-column>
      <el-table-column :label="t('計画完了日時')" width="180">
        <template #default="{ row }">
          <el-date-picker v-model="row.planEndTime" type="datetime" value-format="YYYY-MM-DDTHH:mm:ss" size="small" style="width: 100%;" />
        </template>
      </el-table-column>
      <el-table-column :label="t('計画数量')" width="100">
        <template #default="{ row }">
          <el-input-number v-model="row.planQty" :min="0" :precision="0" size="small" style="width: 100%;" />
        </template>
      </el-table-column>
      <el-table-column :label="t('LT(日)')" width="80">
        <template #default="{ row }">
          <el-input-number v-model="row.leadTime" :min="0" :precision="1" size="small" style="width: 100%;" />
        </template>
      </el-table-column>
      <el-table-column :label="t('状態')" width="90" align="center">
        <template #default="{ row }">
          <el-tag :color="getStatusColor(row.processStatus)" effect="dark" size="small">
            {{ getStatusLabel(row.processStatus) }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column :label="t('操作')" width="80" align="center">
        <template #default="{ $index }">
          <el-button link type="danger" size="small" @click="removeRow($index)">削除</el-button>
        </template>
      </el-table-column>
    </el-table>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { computed } from 'vue'
import { Plus, Sort } from '@element-plus/icons-vue'
import { PROCESS_STATUS_OPTIONS, type WorkOrderProcessDto } from '@/types/mes/mes'

const props = defineProps<{
  modelValue: WorkOrderProcessDto[]
  productionQty: number
}>()
const emit = defineEmits<{ (e: 'update:modelValue', v: WorkOrderProcessDto[]): void }>()

const model = computed({
  get: () => props.modelValue || [],
  set: v => emit('update:modelValue', v),
})

function addRow() {
  const next: WorkOrderProcessDto = {
    workOrderNo: '',
    processCd: '',
    taskCd: '',
    processName: '',
    sortOrder: (model.value.length || 0) + 1,
    processStatus: 0,
    machineCd: '',
    wgCd: '',
    planStartTime: null,
    planEndTime: null,
    actualStartTime: null,
    actualEndTime: null,
    planQty: props.productionQty || 0,
    goodQty: 0,
    defectQty: 0,
    stdLossRate: null,
    leadTime: null,
    prevProcessCd: null,
    remarks: '',
  }
  emit('update:modelValue', [...model.value, next])
}

function removeRow(i: number) {
  const list = [...model.value]
  list.splice(i, 1)
  emit('update:modelValue', list)
}

function autoSort() {
  const list = model.value.map((r, i) => ({ ...r, sortOrder: i + 1 }))
  emit('update:modelValue', list)
}

function getStatusLabel(v: number) {
  return PROCESS_STATUS_OPTIONS.find(s => s.value === v)?.label ?? `${v}`
}
function getStatusColor(v: number) {
  return PROCESS_STATUS_OPTIONS.find(s => s.value === v)?.color ?? '#909399'
}
</script>
