<template>
  <el-card shadow="never">
    <div style="margin-bottom: 8px; display: flex; gap: 8px; align-items: center">
      <el-button
        type="primary"
        :icon="Plus"
        size="small"
        :disabled="!store.canEdit"
        @click="addMaterial"
      >材料行追加</el-button>
      <el-button
        size="small"
        :disabled="!store.canEdit || store.materials.length === 0"
        @click="reSort"
      >並び順再採番</el-button>
      <span style="color: #909399; font-size: 12px">
        ※ 材料区分: 1=主材料 / 2=副資材 / 3=梱包材
      </span>
    </div>

    <el-table
      :data="store.materials"
      border
      stripe
      style="width: 100%"
      size="small"
      max-height="600"
    >
      <el-table-column prop="sortOrder" label="No" width="60" align="center" />
      <el-table-column :label="t('工程CD')" width="120">
        <template #default="{ row }">
          <el-select
            v-model="row.processCd"
            :disabled="!store.canEdit"
            size="small"
            :placeholder="t('選択')"
            style="width: 100%"
          >
            <el-option
              v-for="p in processOptions"
              :key="p.processCd"
              :label="`${p.processCd} (No${p.sortOrder})`"
              :value="p.processCd"
            />
          </el-select>
        </template>
      </el-table-column>
      <el-table-column :label="t('材料CD')" width="180">
        <template #default="{ row }">
          <el-input v-model="row.materialCd" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('材料区分')" width="120">
        <template #default="{ row }">
          <el-select
            v-model="row.materialTypeDiv"
            :disabled="!store.canEdit"
            size="small"
            style="width: 100%"
          >
            <el-option :label="t('主材料')" value="1" />
            <el-option :label="t('副資材')" value="2" />
            <el-option :label="t('梱包材')" value="3" />
          </el-select>
        </template>
      </el-table-column>
      <el-table-column label="ItemCd" width="120">
        <template #default="{ row }">
          <el-input v-model="row.itemCd" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('支給区分')" width="120">
        <template #default="{ row }">
          <el-select
            v-model="row.supplyDiv"
            :disabled="!store.canEdit"
            size="small"
            clearable
            style="width: 100%"
          >
            <el-option :label="t('自社購入')" value="0" />
            <el-option :label="t('顧客支給')" value="1" />
            <el-option :label="t('無償支給')" value="2" />
          </el-select>
        </template>
      </el-table-column>
      <el-table-column :label="t('支給単価')" width="140">
        <template #default="{ row }">
          <el-input-number
            v-model="row.supplyPrice"
            :disabled="!store.canEdit"
            :precision="2"
            :controls="false"
            size="small"
            style="width: 100%"
          />
        </template>
      </el-table-column>
      <el-table-column :label="t('操作')" width="100" align="center" fixed="right">
        <template #default="{ $index }">
          <el-button
            link
            type="danger"
            size="small"
            :disabled="!store.canEdit"
            @click="removeMaterial($index)"
          >削除</el-button>
        </template>
      </el-table-column>
    </el-table>
  </el-card>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { computed } from 'vue'
import { ElMessage } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
import { useProductMasterStore } from '@/stores/productMaster'

const store = useProductMasterStore()

const processOptions = computed(() =>
  store.processes.filter(p => !!p.processCd).map(p => ({
    processCd: p.processCd,
    sortOrder: p.sortOrder,
  })),
)

function addMaterial() {
  const nextSort = store.materials.length === 0
    ? 10
    : Math.max(...store.materials.map(m => m.sortOrder)) + 10
  store.materials.push({
    processCd: '',
    materialCd: '',
    materialTypeDiv: '1',
    sortOrder: nextSort,
  })
  store.markDirty()
}

function removeMaterial(idx: number) {
  store.materials.splice(idx, 1)
  store.markDirty()
}

function reSort() {
  store.materials.forEach((m, i) => {
    m.sortOrder = (i + 1) * 10
  })
  store.markDirty()
  ElMessage.success('並び順を再採番しました')
}

defineExpose({
  validate(): boolean {
    for (const m of store.materials) {
      if (!m.processCd) {
        ElMessage.error('材料行: 工程CDを選択してください')
        return false
      }
      if (!m.materialCd) {
        ElMessage.error('材料行: 材料CDを入力してください')
        return false
      }
      if (!m.materialTypeDiv) {
        ElMessage.error('材料行: 材料区分を選択してください')
        return false
      }
    }
    return true
  },
})
</script>

<style scoped>
:deep(.el-input-number .el-input__inner) {
  text-align: right;
}
</style>
