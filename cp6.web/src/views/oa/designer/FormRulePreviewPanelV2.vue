<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import type { FormAuthoringView } from '@/api/oa/formAuthoring'
import { formRulePreviewApi as api, type FormRulePreview } from '@/api/oa/formRulePreview'
import { applyRules, type FieldEffect } from '@/views/wf/ruleEngine'
import type { FormSchema } from '@/types/wf/wf'
import type { DefinitionDiagnostic } from '@/api/oa/definitionDiagnostics'
import DefinitionDiagnosticsPanelV2 from './DefinitionDiagnosticsPanelV2.vue'
const props = defineProps<{ current?: FormAuthoringView; dirty: boolean; disabled: boolean }>()
const emit = defineEmits<{ privateClear: []; locate: [item: DefinitionDiagnostic] }>()
const opened = ref(false), raw = ref('{}'), busy = ref(false), error = ref(''), result = ref<FormRulePreview>()
const clientJson = ref(''), clientEffects = ref<Record<string, FieldEffect>>({})
const diagnostics = ref<DefinitionDiagnostic[]>([])
const available = computed(() => !!props.current && !props.current.published && !props.dirty && !props.disabled && typeof props.current.fields.SchemaJson === 'string')
let generation = 0, live = true, controller: AbortController | undefined, identity = identityStamp()
function invalidate() { generation++; controller?.abort(); busy.value = false; result.value = undefined; clientJson.value = ''; clientEffects.value = {}; error.value = ''; diagnostics.value = [] }
function clear() { invalidate(); raw.value = '{}'; opened.value = false }
watch(() => [props.current?.definitionId, props.current?.draftId, props.current?.version, props.current?.published], clear)
watch(() => [props.dirty, props.disabled, available.value], invalidate)
watch(raw, invalidate)
function identityChanged() { const next = identityStamp(); if (next !== identity) { identity = next; clear() } }
window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged)
onBeforeUnmount(() => { live = false; invalidate(); window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged) })
async function preview() {
  if (!available.value || busy.value) return
  const current = props.current!, epoch = identityStamp(), request = generation, input = raw.value
  controller = new AbortController(); busy.value = true; result.value = undefined; clientJson.value = ''; clientEffects.value = {}; error.value = ''; diagnostics.value = []
  const check = () => { requireIdentity(epoch); if (!live || request !== generation) throw new Error('STALE') }
  try {
    // The raw text is authoritative input. Browser number parsing is used only for
    // the separately labelled existing client experience after the server succeeds.
    const value = await api.preview(current.key, { draftVersionId: current.draftId, expectedVersion: current.version, dataJson: input }, controller.signal)
    check(); const client = JSON.parse(input)
    clientEffects.value = applyRules(JSON.parse(current.fields.SchemaJson!) as FormSchema, client)
    clientJson.value = JSON.stringify(client, null, 2); result.value = value
  } catch (e: any) {
    try {
      check(); const status = e?.response?.status
      if ([401, 403].includes(status)) { clear(); emit('privateClear'); error.value = '读取权限已变化，请重新读取草稿。' }
      else if ([404, 409].includes(status)) error.value = '草稿版本已变化，请重新读取后预览。'
      else if ([400, 422].includes(status)) { error.value = `未能计算，请检查模拟数据和规则：${e?.response?.data?.code ?? '输入无效'}`; diagnostics.value = e?.response?.data?.fieldErrors ?? [] }
      else error.value = '规则预览未完成，请稍后重试。'
    } catch { /* Ignore responses for a previous identity, version or input. */ }
  } finally { if (live && request === generation) busy.value = false }
}
</script>
<template>
  <section aria-label="表单规则预览" class="rule-preview">
    <button :disabled="!available || busy" @click="opened = true">预览表单规则</button><span v-if="dirty">请先保存当前修改。</span>
    <div v-if="opened">
      <p>输入非敏感模拟值，按顺序计算一次规则。不会保存业务数据或发起审批；实际提交时仍会检查当前字段权限及文件、选项来源。</p>
      <label>模拟数据（JSON）<textarea v-model="raw" rows="5" spellcheck="false" :disabled="busy" /></label>
      <button :disabled="!available || busy" @click="preview">{{ busy ? '正在计算…' : '运行规则预览' }}</button>
      <p v-if="error" role="alert">{{ error }}</p>
      <DefinitionDiagnosticsPanelV2 :items="diagnostics" :disabled="busy" @locate="emit('locate', $event)" />
      <div v-if="result" aria-live="polite">
        <div class="comparison"><section><h4>服务端结果（准确数值）</h4><pre data-test="server-rule-result">{{ result.dataJson }}</pre></section>
          <section><h4>浏览器体验结果</h4><p>浏览器数值可能舍入；请与左侧准确结果比较，最终提交以后端为准。</p><pre data-test="client-rule-result">{{ clientJson }}</pre></section></div>
        <p v-if="result.validationErrors.length">当前模拟值尚未满足：{{ result.validationErrors.join('；') }}</p>
        <table><caption>浏览器控件体验（不授予写入权限）</caption><thead><tr><th>字段</th><th>可见</th><th>必填</th><th>禁用</th><th>选项</th></tr></thead>
          <tbody><tr v-for="(effect, name) in clientEffects" :key="name"><th>{{ name }}</th><td>{{ effect.visible ? '是' : '否' }}</td><td>{{ effect.required ? '是' : '否' }}</td><td>{{ effect.disabled ? '是' : '否' }}</td><td>{{ effect.options ? JSON.stringify(effect.options) : '无' }}</td></tr></tbody></table>
        <details><summary>服务端实际命中的规则</summary><ol><li v-for="effect in result.appliedEffects" :key="effect.path">{{ effect.path }} · {{ effect.action }} → {{ effect.target }} <code>{{ effect.valueJson }}</code></li></ol></details>
      </div>
    </div>
  </section>
</template>
<style scoped>.rule-preview { padding: 12px; border: 1px solid #cbd5e1; margin-block: 12px; }label { display: grid; gap: 6px; }textarea { max-width: 760px; font-family: monospace; }.comparison { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }pre { white-space: pre-wrap; overflow-wrap: anywhere; }th, td { padding: 6px 10px; border: 1px solid #cbd5e1; }table { border-collapse: collapse; }[role=alert] { color: #a63727; }@media(max-width: 760px){.comparison { grid-template-columns: 1fr; }}</style>
