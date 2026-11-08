<template>
  <section aria-label="可选成员排除决定">
    <p>排除仍保留原成员、来源和冲突事实，只适用于来源明确可选、允许来源决定的条款；不豁免必需条目或不利冲突。</p>
    <p v-if="member.exclusion">本草稿排除依据：{{ member.exclusion.basis }}；来源版本 {{ member.exclusion.source.version }}</p>
    <p v-if="!source">请先保存草稿并读取准确来源，再登记排除决定。</p>
    <fieldset :disabled="disabled || !canDecide || !source?.isCurrent">
      <label>排除决定依据<textarea v-model="basis" aria-label="排除决定依据" maxlength="8000" /></label>
      <button type="button" :disabled="!basis.trim()" @click="prepare">准备可选成员排除</button>
      <div v-if="prepared" role="dialog" aria-label="确认可选成员排除">
        <p>需求 {{ member.requirement.id }} / {{ member.requirement.version }}；准确来源 {{ prepared.source.id }} / {{ prepared.source.version }}</p>
        <p>集合适用范围：{{ scopeText }}</p><p>{{ prepared.basis }}</p><p>确认只编入本草稿，保存时核验当前来源必要性、权限及冲突义务。</p>
        <button type="button" @click="confirm">确认编入排除决定</button><button type="button" @click="prepared = null">取消排除决定</button>
      </div>
      <button v-if="member.exclusion" type="button" @click="emit('decision', null)">本草稿移除排除决定</button>
    </fieldset>
  </section>
</template>
<script setup lang="ts">
import { ref, watch } from 'vue'
import type { RequirementExclusionDecision, RequirementSetMember } from '@/api/plm/requirementSet'
import type { RequirementSnapshot } from '@/api/plm/engineeringRequirements'
const props = defineProps<{ member: RequirementSetMember; source: RequirementSnapshot | null; disabled: boolean; canDecide: boolean; scopeText: string }>()
const emit = defineEmits<{ decision: [value: RequirementExclusionDecision | null] }>()
const basis = ref(props.member.exclusion?.basis || ''), prepared = ref<RequirementExclusionDecision | null>(null)
function prepare() { if (!props.disabled && props.canDecide && props.source?.isCurrent && basis.value.trim()) prepared.value = { schema: 'requirement-member-exclusion/1', source: { ...props.source.reference }, basis: basis.value } }
function confirm() { if (prepared.value && !props.disabled && props.canDecide && props.source?.isCurrent) { emit('decision', prepared.value); prepared.value = null } }
watch(() => [props.member.requirement, props.member.disposition, props.source, props.disabled, props.canDecide, props.scopeText], () => { prepared.value = null })
watch(basis, () => { prepared.value = null })
</script>
<style scoped>
section { margin-block: 1rem; padding: 1rem; border: 1px solid #d9e5ec; border-radius: 8px; }
fieldset { border: 0; padding: 0; min-width: 0; }
label { display: flex; flex-direction: column; gap: .4rem; }
textarea { width: 100%; min-height: 5rem; box-sizing: border-box; padding: .6rem; font: inherit; }
button { margin: .6rem .6rem .2rem 0; padding: .6rem .9rem; font: inherit; }
[role="dialog"] { padding: 1rem; background: #f0f7fa; border-radius: 6px; }
p { line-height: 1.6; overflow-wrap: anywhere; }
</style>
