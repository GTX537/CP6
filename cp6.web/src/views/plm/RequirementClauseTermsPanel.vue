<template>
  <section :aria-label="`条款范围 ${index}`">
    <h4>来源确认的适用范围与必须属性</h4>
    <p>仅内部提案的有权来源确认。标识使用来源系统的命名空间和准确键，名称相同不代表同一客户或产品。保存后才形成新来源版本；已有需求须显式采用新版。</p>
    <button v-if="!modelValue" type="button" :disabled="disabled || !canConfirm" @click="enable">填写来源范围</button>
    <p v-if="!modelValue">尚未确认；不会从自由文本推断。</p>
    <fieldset v-else :disabled="disabled || !canConfirm">
      <div v-for="axis in axes" :key="axis.key">
        <label>{{ axis.label }}范围<select :aria-label="`${axis.label}范围 ${index}`" :value="modelValue[axis.key].mode" @change="mode(axis.key, ($event.target as HTMLSelectElement).value as RequirementScopeAxis['mode'])"><option value="UNKNOWN">未知</option><option value="ALL">全部</option><option value="VALUES">指定标识</option></select></label>
        <template v-if="modelValue[axis.key].mode === 'VALUES'">
          <label>标识命名空间<input :aria-label="`${axis.label}命名空间 ${index}`" :value="modelValue[axis.key].namespace || ''" maxlength="256" required @input="axisValue(axis.key, 'namespace', ($event.target as HTMLInputElement).value)" /></label>
          <label>准确标识（每行一个）<textarea :aria-label="`${axis.label}标识 ${index}`" :value="modelValue[axis.key].keys.join('\n')" required @input="axisValue(axis.key, 'keys', ($event.target as HTMLTextAreaElement).value.split('\n').filter(x => x !== ''))" /></label>
        </template>
      </div>
      <p>时间为 UTC [开始, 结束)，结束时刻不包含在范围内。留空且未选无限表示未知。</p>
      <label>开始 UTC<input :aria-label="`范围开始 ${index}`" :value="modelValue.from || ''" :disabled="modelValue.fromUnbounded" placeholder="2026-11-01T00:00:00Z" @input="update({ from: ($event.target as HTMLInputElement).value || null })" /></label>
      <label><input type="checkbox" :aria-label="`开始无限 ${index}`" :checked="modelValue.fromUnbounded" @change="update({ fromUnbounded: ($event.target as HTMLInputElement).checked, from: null })" />开始无限</label>
      <label>结束 UTC<input :aria-label="`范围结束 ${index}`" :value="modelValue.until || ''" :disabled="modelValue.untilUnbounded" placeholder="2026-12-01T00:00:00Z" @input="update({ until: ($event.target as HTMLInputElement).value || null })" /></label>
      <label><input type="checkbox" :aria-label="`结束无限 ${index}`" :checked="modelValue.untilUnbounded" @change="update({ untilUnbounded: ($event.target as HTMLInputElement).checked, until: null })" />结束无限</label>
      <label>必须属性<select :aria-label="`必须属性 ${index}`" :value="modelValue.necessity" @change="update({ necessity: ($event.target as HTMLSelectElement).value as RequirementClauseTerms['necessity'] })"><option value="UNKNOWN">未知</option><option value="MANDATORY">必须满足</option><option value="OPTIONAL">可选</option></select></label>
      <label>可放宽性<select :aria-label="`可放宽性 ${index}`" :value="modelValue.waivability" @change="update({ waivability: ($event.target as HTMLSelectElement).value as RequirementClauseTerms['waivability'] })"><option value="UNKNOWN">未知</option><option value="PROHIBITED">禁止放宽</option><option value="SOURCE_DECISION_REQUIRED">须另行有权裁决</option></select></label>
      <label>来源确认依据<textarea :aria-label="`来源确认依据 ${index}`" :value="modelValue.basis" required maxlength="8000" @input="update({ basis: ($event.target as HTMLTextAreaElement).value })" /></label>
      <button type="button" @click="emit('update:modelValue', null)">本草稿改为范围待确认</button>
    </fieldset>
  </section>
</template>
<script setup lang="ts">
import type { RequirementClauseTerms, RequirementScopeAxis } from '@/api/plm/engineeringRequirements'
const props = defineProps<{ modelValue?: RequirementClauseTerms | null; index: number; disabled: boolean; canConfirm: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [value: RequirementClauseTerms | null] }>()
type Axis = 'customers' | 'products' | 'configurations'
const axes: { key: Axis; label: string }[] = [{ key: 'customers', label: '客户' }, { key: 'products', label: '产品' }, { key: 'configurations', label: '配置' }]
const unknown = (): RequirementScopeAxis => ({ mode: 'UNKNOWN', namespace: null, keys: [] })
function enable() { emit('update:modelValue', { schema: 'requirement-clause-terms/1', customers: unknown(), products: unknown(), configurations: unknown(), from: null, until: null, fromUnbounded: false, untilUnbounded: false, necessity: 'UNKNOWN', waivability: 'UNKNOWN', basis: '' }) }
function update(value: Partial<RequirementClauseTerms>) { if (props.modelValue) emit('update:modelValue', { ...props.modelValue, ...value }) }
function mode(axis: Axis, value: RequirementScopeAxis['mode']) { update({ [axis]: { mode: value, namespace: value === 'VALUES' ? '' : null, keys: [] } }) }
function axisValue(axis: Axis, key: 'namespace' | 'keys', value: string | string[]) { if (props.modelValue) update({ [axis]: { ...props.modelValue[axis], [key]: value } }) }
</script>
<style scoped>
section { margin-block: 1rem; padding: 1rem; border: 1px solid #d9e5ec; border-radius: 8px; }
fieldset { display: grid; gap: 1rem; border: 0; padding: 0; min-width: 0; }
fieldset > div { display: grid; gap: .75rem; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); }
label { display: flex; flex-direction: column; gap: .4rem; }
label:has(input[type="checkbox"]) { flex-direction: row; align-items: center; }
input:not([type="checkbox"]), select, textarea { box-sizing: border-box; width: 100%; min-width: 0; padding: .6rem; border: 1px solid #cbd8e1; border-radius: 4px; font: inherit; }
textarea { min-height: 5rem; resize: vertical; }
button { justify-self: start; padding: .6rem .9rem; border: 1px solid #cbd8e1; border-radius: 4px; font: inherit; cursor: pointer; }
button:disabled { cursor: default; opacity: .6; }
p { line-height: 1.6; }
</style>
