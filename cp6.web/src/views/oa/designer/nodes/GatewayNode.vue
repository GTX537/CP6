<script setup lang="ts">
import type { NodeProps } from '@vue-flow/core'
import FourWayHandles from './FourWayHandles.vue'

const props = defineProps<NodeProps>()

type NodeData = {
  type?: string
}
</script>

<template>
  <!-- Gateway node: diamond shape for parallel split / join -->
  <div :class="['vf-node-gateway-wrap', { 'vf-node--selected': props.selected }]">
    <FourWayHandles />
    <div class="vf-node-gateway">
      <span class="gw-label">{{ (props.data as NodeData)?.type === 'parallelJoin' ? '汇聚' : '分叉' }}</span>
    </div>
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
  box-shadow: 0 0 0 2px color-mix(in srgb, var(--cp-warn) 50%, transparent);
}
.vf-node-gateway {
  width: 60px;
  height: 60px;
  background: var(--cp-warn-bg);
  border: 2px solid var(--cp-warn);
  transform: rotate(45deg);
  display: flex;
  align-items: center;
  justify-content: center;
}
.gw-label {
  transform: rotate(-45deg);
  font-size: 11px;
  font-weight: 500;
  color: var(--cp-warn);
  white-space: nowrap;
}
</style>
