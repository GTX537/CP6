<script setup lang="ts">
import { Handle, Position } from '@vue-flow/core'
import type { NodeProps } from '@vue-flow/core'

const props = defineProps<NodeProps>()
</script>

<template>
  <div :class="['vf-node-end', { 'vf-node--selected': props.selected }]">
    <Handle type="target" :position="Position.Top" />
    <div class="node-label">{{ (props.data as Record<string, unknown>)?.name as string || '結束' }}</div>
  </div>
</template>

<style scoped>
.vf-node-end {
  background: #f4f4f5;
  border: 2px solid #909399;
  border-radius: 20px;
  padding: 8px 20px;
  min-width: 120px;
  text-align: center;
  font-size: 13px;
  color: #333;
  cursor: default;
}
.vf-node-end.vf-node--selected {
  box-shadow: 0 0 0 2px #90939980;
}
.node-label {
  font-weight: 500;
  white-space: nowrap;
}
</style>
