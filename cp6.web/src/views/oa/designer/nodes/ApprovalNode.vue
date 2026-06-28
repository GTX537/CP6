<script setup lang="ts">
import { Handle, Position } from '@vue-flow/core'
import type { NodeProps } from '@vue-flow/core'

const props = defineProps<NodeProps>()

type NodeData = {
  name?: string
  approverStrategy?: string
}
</script>

<template>
  <div :class="['vf-node-approval', { 'vf-node--selected': props.selected }]">
    <Handle type="target" :position="Position.Top" />
    <div class="node-label">{{ (props.data as NodeData)?.name || '審批' }}</div>
    <div v-if="(props.data as NodeData)?.approverStrategy" class="node-strategy">
      {{ (props.data as NodeData).approverStrategy }}
    </div>
    <Handle type="source" :position="Position.Bottom" />
  </div>
</template>

<style scoped>
.vf-node-approval {
  background: #ecf5ff;
  border: 2px solid #409eff;
  border-radius: 6px;
  padding: 8px 16px;
  min-width: 130px;
  text-align: center;
  font-size: 13px;
  color: #333;
  cursor: default;
}
.vf-node-approval.vf-node--selected {
  box-shadow: 0 0 0 2px #409eff80;
}
.node-label {
  font-weight: 500;
  white-space: nowrap;
}
.node-strategy {
  font-size: 11px;
  color: #409eff;
  margin-top: 2px;
  white-space: nowrap;
}
</style>
