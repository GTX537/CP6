<script setup lang="ts">
import { computed } from 'vue'
import type { NodeProps } from '@vue-flow/core'
import { useI18n } from 'vue-i18n'
import FourWayHandles from './FourWayHandles.vue'

const props = defineProps<NodeProps>()
const { t } = useI18n()

type ServiceKind = 'dataWriteback' | 'webApi' | 'timer'
type NodeData = {
  name?: string
  serviceKind?: ServiceKind
}

// 机器节点双重编码之一：图标字符（不依赖色相辨识，与虚线笔触并列）。
const KIND_ICON: Record<ServiceKind, string> = {
  dataWriteback: '⤓',
  webApi: '⚡',
  timer: '⏱',
}

const kind = computed<ServiceKind>(() => {
  const k = (props.data as NodeData)?.serviceKind
  return k === 'webApi' || k === 'timer' ? k : 'dataWriteback'
})
const icon = computed(() => KIND_ICON[kind.value])
const kindLabel = computed(() => t(`oa.designer.svc.kind.${kind.value}`))
const name = computed(() => (props.data as NodeData)?.name || t('oa.designer.svc.title'))
</script>

<template>
  <div :class="['vf-node-service', { 'vf-node--selected': props.selected }]">
    <FourWayHandles />
    <div class="node-label">
      <span class="node-kind-icon" aria-hidden="true">{{ icon }}</span>
      <span class="node-label-text">{{ name }}</span>
    </div>
    <div class="node-strategy">{{ kindLabel }}</div>
  </div>
</template>

<style scoped>
/* 机器节点（serviceTask）：虚线笔触区分人类节点（填單/審批）的实线；
   三 kind 共用 brand 青，由图标+线型双重编码辨识，不发三色相。 */
.vf-node-service {
  background: var(--cp-brand-bg);
  border: 2px dashed var(--cp-brand);
  border-radius: var(--cp-r-sm);
  padding: 8px 16px;
  min-width: 130px;
  text-align: center;
  font-size: 13px;
  color: var(--cp-text);
  cursor: default;
}
.vf-node-service.vf-node--selected {
  box-shadow: 0 0 0 2px color-mix(in srgb, var(--cp-brand) 50%, transparent);
}
.node-label {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  font-weight: 500;
  white-space: nowrap;
}
.node-kind-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 16px;
  height: 16px;
  flex-shrink: 0;
  background: var(--cp-brand-bg);
  border-radius: 4px;
  font-size: 11px;
  line-height: 1;
  color: var(--cp-brand);
}
.node-label-text {
  white-space: nowrap;
}
.node-strategy {
  font-size: 11px;
  color: var(--cp-brand);
  margin-top: 2px;
  white-space: nowrap;
}
</style>
