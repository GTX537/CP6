<script setup lang="ts">
import type { NodeProps } from '@vue-flow/core'
import FourWayHandles from './FourWayHandles.vue'

const props = defineProps<NodeProps>()

type NodeData = {
  name?: string
  approverStrategy?: string
}
</script>

<template>
  <div :class="['vf-node-approval', { 'vf-node--selected': props.selected }]">
    <FourWayHandles />
    <div class="node-label">{{ (props.data as NodeData)?.name || '審批' }}</div>
    <div v-if="(props.data as NodeData)?.approverStrategy" class="node-strategy">
      {{ (props.data as NodeData).approverStrategy }}
    </div>
  </div>
</template>

<style scoped>
.vf-node-approval {
  background: var(--cp-info-bg);
  border: 2px solid var(--cp-info);
  border-radius: var(--cp-r-sm);
  padding: 8px 16px;
  min-width: 130px;
  text-align: center;
  font-size: 13px;
  color: var(--cp-text);
  cursor: default;
}
.vf-node-approval.vf-node--selected {
  box-shadow: 0 0 0 2px color-mix(in srgb, var(--cp-info) 50%, transparent);
}
.node-label {
  font-weight: 500;
  white-space: nowrap;
}
.node-strategy {
  font-size: 11px;
  color: var(--cp-info);
  margin-top: 2px;
  white-space: nowrap;
}
</style>
