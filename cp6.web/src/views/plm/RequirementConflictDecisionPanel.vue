<template>
  <section aria-label="来源有权裁决">
    <p v-if="conflict.decision">本草稿裁决：{{ label(conflict.decision.kind) }}。{{ conflict.decision.basis }}</p>
    <p>澄清仅确认双方要求能够同时满足，不豁免原要求。范围分离由来源范围计算核对。工程变更或偏离仍须负责团队后续处理，不能因此批准集合。</p>
    <fieldset :disabled="disabled || !canDecide">
      <label>裁决依据<textarea v-model="basis" aria-label="裁决依据" maxlength="8000" /></label>
      <label><input v-model="preserves" type="checkbox" aria-label="保留双方原要求" />确认保留双方原要求及禁止放宽条件</label>
      <button type="button" :disabled="!basis.trim() || !preserves || conflict.proposal === 'UNRESOLVED'" @click="prepare">准备来源裁决</button>
      <div v-if="prepared" role="dialog" aria-label="确认来源裁决">
        <p>{{ label(prepared.kind) }}：需求 {{ conflict.left.id }} / {{ conflict.left.version }} 与 {{ conflict.right.id }} / {{ conflict.right.version }}</p>
        <p>{{ prepared.basis }}</p><p>确认仅编入当前草稿；保存新版本时核验双方来源与当前权限。</p>
        <button type="button" @click="confirm">确认编入裁决</button><button type="button" @click="prepared = null">取消裁决</button>
      </div>
      <button v-if="conflict.decision" type="button" @click="emit('decision', null)">本草稿移除裁决</button>
    </fieldset>
  </section>
</template>
<script setup lang="ts">
import { ref, watch } from 'vue'
import type { RequirementConflictDecision, RequirementSetConflict } from '@/api/plm/requirementSet'
const props = defineProps<{ conflict: RequirementSetConflict; disabled: boolean; canDecide: boolean }>()
const emit = defineEmits<{ decision: [value: RequirementConflictDecision | null] }>()
const basis = ref(props.conflict.decision?.basis || ''), preserves = ref(false), prepared = ref<RequirementConflictDecision | null>(null)
const label = (kind: string) => ({ CLARIFY: '来源澄清', SCOPE_SEPARATION: '范围分离', ECM: '转工程变更评估', DEV: '转偏离评估' } as Record<string, string>)[kind] || kind
function prepare() { if (props.conflict.proposal !== 'UNRESOLVED') prepared.value = { schema: 'requirement-conflict-decision/1', kind: props.conflict.proposal, basis: basis.value, preservesRequirements: true } }
function confirm() { if (prepared.value && !props.disabled && props.canDecide) { emit('decision', prepared.value); prepared.value = null; preserves.value = false } }
watch(() => [props.conflict.left, props.conflict.right, props.conflict.proposal, props.disabled, props.canDecide], () => { prepared.value = null; preserves.value = false })
watch([basis, preserves], () => { prepared.value = null })
</script>
<style scoped>
section { margin-block: 1rem; padding: 1rem; border: 1px solid #d9e5ec; border-radius: 8px; }
fieldset { display: flex; flex-wrap: wrap; align-items: center; gap: 1rem; border: 0; padding: 0; min-width: 0; }
label:first-child { display: flex; flex-direction: column; gap: .4rem; flex-basis: 100%; }
textarea { box-sizing: border-box; width: 100%; min-height: 6rem; padding: .6rem; border: 1px solid #cbd8e1; border-radius: 4px; font: inherit; resize: vertical; }
button { padding: .6rem .9rem; border: 1px solid #cbd8e1; border-radius: 4px; font: inherit; cursor: pointer; }
button:disabled { cursor: default; opacity: .6; }
[role="dialog"] { width: 100%; padding: 1rem; background: #f0f7fa; border: 1px solid #b5d3e0; border-radius: 6px; overflow-wrap: anywhere; }
[role="dialog"] button { margin-right: .75rem; }
p { line-height: 1.6; overflow-wrap: anywhere; }
</style>
