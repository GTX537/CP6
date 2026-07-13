<script setup lang="ts">
import { computed } from 'vue'
import { Handle, Position } from '@vue-flow/core'
import type { NodeProps } from '@vue-flow/core'
import { useI18n } from 'vue-i18n'

const props = defineProps<NodeProps>()
const { t } = useI18n()

type NodeData = { type?: string }
const isJoin = computed(() => (props.data as NodeData)?.type === 'inclusiveJoin')
</script>

<template>
  <!-- Inclusive gateway: 菱形 + 内嵌空心圆（BPMN inclusive 记号），区别 GatewayNode 的实心菱形 -->
  <div :class="['vf-node-inclusive-wrap', { 'vf-node--selected': props.selected }]">
    <Handle type="target" :position="Position.Top" />
    <div class="vf-node-inclusive">
      <span class="inc-circle" />
      <span class="inc-label">{{ isJoin ? t('oa.designer.gw.inclusiveJoin') : t('oa.designer.gw.inclusiveSplit') }}</span>
    </div>
    <Handle type="source" :position="Position.Bottom" />
  </div>
</template>

<style scoped>
.vf-node-inclusive-wrap {
  display: flex;
  flex-direction: column;
  align-items: center;
  background: transparent;
  cursor: default;
}
.vf-node-inclusive-wrap.vf-node--selected .vf-node-inclusive {
  box-shadow: 0 0 0 2px color-mix(in srgb, var(--cp-warn) 50%, transparent);
}
.vf-node-inclusive {
  position: relative;
  width: 60px;
  height: 60px;
  background: var(--cp-warn-bg);
  border: 2px solid var(--cp-warn);
  transform: rotate(45deg);
  display: flex;
  align-items: center;
  justify-content: center;
}
.inc-circle {
  position: absolute;
  inset: 8px;
  border: 2px solid var(--cp-warn);
  border-radius: 50%;
}
.inc-label {
  transform: rotate(-45deg);
  font-size: 10px;
  font-weight: 500;
  color: var(--cp-warn);
  white-space: nowrap;
  z-index: 1;
}
</style>
