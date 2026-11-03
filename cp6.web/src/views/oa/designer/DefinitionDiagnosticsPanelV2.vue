<script setup lang="ts">
import type { DefinitionDiagnostic } from '@/api/oa/definitionDiagnostics'
defineProps<{ items: DefinitionDiagnostic[]; disabled?: boolean }>()
const emit = defineEmits<{ locate: [item: DefinitionDiagnostic] }>()
const descriptions: Record<string, string> = {
  EMPTY_OR_DUPLICATE: '标识为空或重复', UNSUPPORTED_TABLE_CONTROL: '子表不支持此控件', TABLE_ROW_RANGE: '子表行数范围无效',
  EXPRESSION_INVALID: '表达式无效', FIELD_NOT_FOUND: '规则目标字段不存在', SCHEMA_SHAPE_INVALID: '节点、路径或身份结构无效',
  SERVICE_REFERENCE_INVALID: '引用的服务未注册', SERVICE_TIMEOUT_INVALID: '服务超时必须短于租约',
  FORM_FILE_SOURCE_REQUIRED: '此附件字段尚未接入可用的文件来源',
  FORM_RULE_EVALUATION_FAILED: '此规则无法用当前模拟值完成计算', SNAPSHOT_SCHEMA_INVALID: '此表达式无法使用当前模拟值',
  DEPENDENCY_NOT_AVAILABLE: '引用的已发布依赖当前不可用', DEPENDENCY_NOT_READABLE: '当前身份无法读取此依赖', DEPENDENCY_DISABLED: '引用的依赖尚未启用',
  DEPENDENCY_CYCLE: '子流程依赖形成循环', DEPENDENCY_DEPTH_EXCEEDED: '子流程依赖超过允许深度',
  'E-WF-010': '流程结构尚不完整', 'E-WF-011': '审批分档配置无效', 'E-WF-014': '审批人规则缺少配置',
  'E-WF-016': '服务节点配置或成功路径不完整', 'E-WF-017': '错误路径配置无效', 'E-WF-027': '超时政策需要错误路径',
}
</script>
<template>
  <section v-if="items.length" aria-label="设计诊断" class="diagnostics">
    <p>请检查以下位置；诊断对应当前这份结构。</p>
    <ul><li v-for="(item, index) in items" :key="index"><button :disabled="disabled" @click="emit('locate', item)">定位 {{ item.nodeId || item.fieldName || item.edgeId || item.path }}</button>
      <span>{{ item.message || descriptions[item.code] || item.code }}</span><code>{{ item.path }}</code>
    </li></ul>
  </section>
</template>
<style scoped>.diagnostics { padding: 10px; border: 1px solid #d9a748; margin-block: 10px; }li { margin-block: 8px; }button { margin-right: 8px; }code { display: block; overflow-wrap: anywhere; }</style>
