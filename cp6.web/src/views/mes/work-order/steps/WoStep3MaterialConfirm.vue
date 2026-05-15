<template>
  <div>
    <div style="margin-bottom: 12px;">
      <el-button type="primary" size="small" :icon="Plus" @click="addRow">材料追加</el-button>
    </div>
    <el-table :data="model" border stripe size="small" style="width: 100%;">
      <el-table-column label="順" width="60" align="center">
        <template #default="{ row }">{{ row.sortOrder }}</template>
      </el-table-column>
      <el-table-column label="工程CD" width="120">
        <template #default="{ row }">
          <el-input v-model="row.processCd" size="small" />
        </template>
      </el-table-column>
      <el-table-column label="材料CD" width="150">
        <template #default="{ row }">
          <el-input v-model="row.materialCd" size="small" />
        </template>
      </el-table-column>
      <el-table-column label="材料名" min-width="180">
        <template #default="{ row }">
          <el-input v-model="row.materialName" size="small" />
        </template>
      </el-table-column>
      <el-table-column label="区分" width="120">
        <template #default="{ row }">
          <el-select v-model="row.materialTypeDiv" size="small" style="width: 100%;">
            <el-option label="1=仕掛品" value="1" />
            <el-option label="2=連産品" value="2" />
            <el-option label="3=原料" value="3" />
            <el-option label="4=印刷原紙" value="4" />
          </el-select>
        </template>
      </el-table-column>
      <el-table-column label="計画数量" width="120">
        <template #default="{ row }">
          <el-input-number v-model="row.planQty" :min="0" :precision="2" size="small" style="width: 100%;" />
        </template>
      </el-table-column>
      <el-table-column label="単位" width="80">
        <template #default="{ row }">
          <el-input v-model="row.unit" size="small" />
        </template>
      </el-table-column>
      <el-table-column label="手配状況" width="110">
        <template #default="{ row }">
          <el-select v-model="row.supplyStatus" size="small" style="width: 100%;">
            <el-option label="0=未手配" :value="0" />
            <el-option label="1=手配中" :value="1" />
            <el-option label="2=入荷済" :value="2" />
            <el-option label="3=不足" :value="3" />
          </el-select>
        </template>
      </el-table-column>
      <el-table-column label="実績消費" width="120" align="right">
        <template #default="{ row }">{{ row.actualQty }}</template>
      </el-table-column>
      <el-table-column label="備考" min-width="160">
        <template #default="{ row }">
          <el-input v-model="row.remarks" size="small" />
        </template>
      </el-table-column>
      <el-table-column label="操作" width="80" align="center">
        <template #default="{ $index }">
          <el-button link type="danger" size="small" @click="removeRow($index)">削除</el-button>
        </template>
      </el-table-column>
    </el-table>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { Plus } from '@element-plus/icons-vue'
import type { WorkOrderMaterialDto } from '@/types/mes'

const props = defineProps<{
  modelValue: WorkOrderMaterialDto[]
  productionQty: number
}>()
const emit = defineEmits<{ (e: 'update:modelValue', v: WorkOrderMaterialDto[]): void }>()

const model = computed({
  get: () => props.modelValue || [],
  set: v => emit('update:modelValue', v),
})

function addRow() {
  const next: WorkOrderMaterialDto = {
    workOrderNo: '',
    processCd: '',
    materialCd: '',
    materialName: '',
    materialTypeDiv: '3',
    planQty: props.productionQty,
    actualQty: 0,
    unit: '',
    supplyStatus: 0,
    sortOrder: (model.value.length || 0) + 1,
    remarks: '',
  }
  emit('update:modelValue', [...model.value, next])
}

function removeRow(i: number) {
  const list = [...model.value]
  list.splice(i, 1)
  emit('update:modelValue', list)
}
</script>
