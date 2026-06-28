<script setup lang="ts">
import { Handle, Position } from '@vue-flow/core'
import type { NodeProps } from '@vue-flow/core'

const props = defineProps<NodeProps>()
</script>

<template>
  <div :class="['vf-node-start', { 'vf-node--selected': props.selected }]">
    <div class="node-label">{{ (props.data as Record<string, unknown>)?.name as string || '填單(发起)' }}</div>
    <Handle type="source" :position="Position.Bottom" />
  </div>
</template>

<style scoped>
.vf-node-start {
  background: #f0faf0;
  border: 2px solid #67c23a;
  border-radius: 20px;
  padding: 8px 20px;
  min-width: 120px;
  text-align: center;
  font-size: 13px;
  color: #333;
  cursor: default;
}
.vf-node-start.vf-node--selected {
  box-shadow: 0 0 0 2px #67c23a80;
}
.node-label {
  font-weight: 500;
  white-space: nowrap;
}
</style>
