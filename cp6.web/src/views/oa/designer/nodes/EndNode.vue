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
  background: var(--cp-line-soft);
  border: 2px solid var(--cp-muted);
  border-radius: var(--cp-r-xl);
  padding: 8px 20px;
  min-width: 120px;
  text-align: center;
  font-size: 13px;
  color: var(--cp-text);
  cursor: default;
}
.vf-node-end.vf-node--selected {
  box-shadow: 0 0 0 2px color-mix(in srgb, var(--cp-muted) 50%, transparent);
}
.node-label {
  font-weight: 500;
  white-space: nowrap;
}
</style>
