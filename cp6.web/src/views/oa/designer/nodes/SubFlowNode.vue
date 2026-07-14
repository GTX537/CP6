<script setup lang="ts">
import { computed } from 'vue'
import { Handle, Position } from '@vue-flow/core'
import type { NodeProps } from '@vue-flow/core'
import { useI18n } from 'vue-i18n'
import type { SchemaNode } from '../designerModel'

const props = defineProps<NodeProps>()
const { t } = useI18n()

const data = computed(() => (props.data ?? {}) as SchemaNode)
const name = computed(() => data.value.name || t('oa.designer.subflow.title'))
const subKey = computed(() => data.value.subFlowKey || '—')
// 多实例：集合变量非空串时展示 ×N 角标 + 完成策略（策略缺省 all）。
const multiVar = computed(() => {
  const v = data.value.subCollectionVar
  return v != null && v !== '' ? v : ''
})
const policyLabel = computed(() =>
  t(`oa.designer.subflow.policy.${data.value.subCompletionPolicy || 'all'}`),
)
</script>

<template>
  <div :class="['vf-node-subflow', { 'vf-node--selected': props.selected }]">
    <Handle type="target" :position="Position.Top" />
    <div class="sf-title">
      <span class="sf-dot" aria-hidden="true" />
      <span class="sf-title-text">{{ name }}</span>
    </div>
    <div class="sf-key">{{ subKey }}</div>
    <div v-if="multiVar" class="sf-multi">×N {{ multiVar }} · {{ policyLabel }}</div>
    <Handle type="source" :position="Position.Bottom" />
  </div>
</template>

<style scoped>
/* 子流程（subFlow）节点：双线边框＝容器语义（BPMN call-activity 惯例），
   区分 serviceTask 的单虚线机器节点与人类节点的单实线。配色全走 Design System token，
   info 家族与 palette `.dot-subFlow` 保持 lockstep（同 DesignerCanvas 图例注释约定）。 */
.vf-node-subflow {
  background: var(--cp-card);
  border: 2px double var(--cp-info);
  border-radius: var(--cp-r-sm);
  padding: 8px 14px;
  min-width: 140px;
  text-align: center;
  font-size: 13px;
  color: var(--cp-text);
  cursor: default;
}
.vf-node-subflow.vf-node--selected {
  box-shadow: 0 0 0 2px color-mix(in srgb, var(--cp-brand) 50%, transparent);
}
.sf-title {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  font-weight: 500;
  white-space: nowrap;
}
/* 方形 dot（border-radius 小）＝容器记号，与 palette `.dot-subFlow` 一致，区分圆形节点 dot。 */
.sf-dot {
  width: 10px;
  height: 10px;
  flex-shrink: 0;
  border-radius: 2px;
  background: var(--cp-info);
}
.sf-title-text {
  white-space: nowrap;
}
.sf-key {
  font-size: 11px;
  color: var(--cp-muted);
  margin-top: 2px;
  white-space: nowrap;
}
.sf-multi {
  font-size: 11px;
  color: var(--cp-warn);
  margin-top: 2px;
  white-space: nowrap;
}
</style>
