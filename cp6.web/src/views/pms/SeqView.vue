<template>
  <div>
    <VolTable :columns="columns" :api="seqApi">
      <template #extra-actions="{ row }">
        <el-button link type="primary" @click="preview(row)">预览</el-button>
      </template>
    </VolTable>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { ElMessage } from 'element-plus'
import VolTable from '@/components/VolTable.vue'
import type { ColumnConfig } from '@/components/VolTable.vue'
import { seqApi } from '@/api/pub/seq'

const resetOptions = [
  { label: '不重置', value: 0 },
  { label: '每日', value: 1 },
  { label: '每月', value: 2 },
  { label: '每年', value: 3 },
]

const columns = computed<ColumnConfig[]>(() => [
  { prop: 'bizKey', label: '业务键', required: true },
  { prop: 'prefix', label: '前缀', required: true },
  { prop: 'dateFormat', label: '日期格式' },        // 如 yyyyMMdd，空=无日期
  { prop: 'seqLength', label: '流水位数', width: 100 },
  { prop: 'resetCycle', label: '重置周期', formType: 'select', options: resetOptions },
  { prop: 'currentValue', label: '当前流水', width: 100, formType: 'none' },
])

async function preview(row: any) {
  const sample = await seqApi.preview(row.bizKey)
  ElMessage.success(`下一个号码示例：${sample}`)
}
</script>
