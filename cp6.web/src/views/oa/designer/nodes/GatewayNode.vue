<script setup lang="ts">
import { Handle, Position } from '@vue-flow/core'
import type { NodeProps } from '@vue-flow/core'

const props = defineProps<NodeProps>()

type NodeData = {
  type?: string
}
</script>

<template>
  <!-- Gateway node: diamond shape for parallel split / join -->
  <div :class="['vf-node-gateway-wrap', { 'vf-node--selected': props.selected }]">
    <Handle type="target" :position="Position.Top" />
    <div class="vf-node-gateway">
      <span class="gw-label">{{ (props.data as NodeData)?.type === 'parallelJoin' ? '汇聚' : '分叉' }}</span>
    </div>
    <Handle type="source" :position="Position.Bottom" />
  </div>
</template>

<style scoped>
.vf-node-gateway-wrap {
  display: flex;
  flex-direction: column;
  align-items: center;
  background: transparent;
  cursor: default;
}
.vf-node-gateway-wrap.vf-node--selected .vf-node-gateway {
  box-shadow: 0 0 0 2px #e6a23c80;
}
.vf-node-gateway {
  width: 60px;
  height: 60px;
  background: #fdf6ec;
  border: 2px solid #e6a23c;
  transform: rotate(45deg);
  display: flex;
  align-items: center;
  justify-content: center;
}
.gw-label {
  transform: rotate(-45deg);
  font-size: 11px;
  font-weight: 500;
  color: #e6a23c;
  white-space: nowrap;
}
</style>
