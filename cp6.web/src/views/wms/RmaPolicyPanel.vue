<template>
  <section aria-label="退货政策">
    <h2>退货政策</h2><p>政策用于申请参考；实际退货仍需独立批准、来源和额度核验。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <p v-if="loaded && !current">当前仓库和站点尚未配置退货政策。</p>
    <div v-if="shown"><h3>{{ shown.policy.name }} · 版本 {{ shown.reference.version }}</h3>
      <p>{{ shown.policy.enabled ? '启用' : '停用' }} · {{ shown.publishedAt }}</p>
      <p>签收后期限：{{ shown.policy.returnWindowDaysAfterDelivery === null ? '政策未限定天数' : `${shown.policy.returnWindowDaysAfterDelivery}天` }}；{{ shown.policy.requireIndependentApprover ? '要求独立审批人' : '政策未要求申请人与审批人分离' }}</p>
      <p>退货原因：{{ shown.policy.reasonOptions.map(x => `${x.code}：${x.label}`).join('；') }}</p>
      <p>允许处理方式：{{ shown.policy.allowedOutcomes.map(x => outcomeNames[x]).join('、') }}</p><p>发布原因：{{ shown.reason }}</p>
      <button v-if="shown.previousRef" :disabled="busy" @click="history(shown.previousRef)">上一政策版本</button>
    </div>
    <button :disabled="busy" @click="reload">读取当前政策</button>
    <div v-if="pending"><p>原政策发布请求待确认，确认前不可发布新请求。</p><button :disabled="busy" @click="recover(false)">查询原政策请求</button><button :disabled="busy" @click="recover(true)">查询并重派原政策请求</button></div>
    <details v-if="canPublish"><summary>发布政策新版本</summary>
      <form @submit.prevent="publish"><fieldset :disabled="busy || !!pending || storageError || !loaded">
        <label>名称<input v-model="name" aria-label="政策名称" required maxlength="200" /></label>
        <label><input v-model="enabled" type="checkbox" aria-label="政策启用" />启用（取消勾选可发布停用版本）</label>
        <div v-for="(item, index) in reasons" :key="index"><label>原因代码<input v-model="item.code" :aria-label="`政策原因代码${index + 1}`" required maxlength="200" /></label><label>名称<input v-model="item.label" :aria-label="`政策原因名称${index + 1}`" required maxlength="200" /></label><button type="button" :disabled="reasons.length === 1" @click="reasons.splice(index, 1)">移除原因</button></div>
        <button type="button" :disabled="reasons.length >= 50" @click="reasons.push({ code: '', label: '' })">添加政策原因</button>
        <label v-for="(label, value) in outcomeNames" :key="value"><input v-model="allowed" type="checkbox" :value="value" :aria-label="`政策允许${label}`" />{{ label }}</label>
        <label>签收后期限<select v-model="windowKind" aria-label="政策期限类型" required><option value="">请选择</option><option value="LIMITED">限定天数</option><option value="UNLIMITED">不限定天数</option></select></label>
        <input v-if="windowKind === 'LIMITED'" v-model.number="days" aria-label="政策期限天数" type="number" min="1" max="36500" required />
        <label>审批人分离<select v-model="independent" aria-label="政策独立审批" required><option value="">请选择</option><option value="YES">必须与申请人不同</option><option value="NO">不要求分离</option></select></label>
        <label>发布原因<input v-model="reason" aria-label="政策发布原因" required maxlength="500" /></label>
        <button type="submit">发布政策</button>
      </fieldset></form>
    </details>
  </section>
</template>
<script setup lang="ts">
import { onBeforeUnmount, ref, shallowRef, toRaw, watch } from 'vue'
import type { RmaRef } from '@/api/wms/rmaFormalApplications'
import type { RmaScope } from '@/api/wms/rmaApplications'
import { rmaPolicyApi as api, type RmaPolicyLookup, type RmaPolicyPending, type RmaPolicyVersion } from '@/api/wms/rmaPolicies'
import { pendingPolicy, publishPolicy, recoverPolicy } from '@/auth/rmaPolicyRecovery'
const props = defineProps<{ scope: RmaScope; option: RmaRef; canPublish: boolean }>()
const emit = defineEmits<{ current: [value: RmaPolicyLookup | null] }>()
const current = shallowRef<RmaPolicyVersion | null>(null), shown = shallowRef<RmaPolicyVersion | null>(null), pending = shallowRef<RmaPolicyPending | null>(null)
const busy = ref(false), loaded = ref(false), storageError = ref(false), error = ref(''), notice = ref('')
const name = ref(''), enabled = ref(true), reasons = ref([{ code: '', label: '' }]), allowed = ref<string[]>([]), windowKind = ref(''), days = ref<number | null>(null), independent = ref(''), reason = ref('')
const outcomeNames: Record<string, string> = { TO_BE_DECIDED: '待协商', RETURN_ONLY: '仅退回实物', CREDIT: '贷项', REFUND: '退款', REPLACEMENT: '换货', SUPPLEMENTARY_SHIPMENT: '补发' }
let epoch = 0, live = true
function clear() { current.value = null; shown.value = null; loaded.value = false; emit('current', null); name.value = ''; reasons.value = [{ code: '', label: '' }]; allowed.value = []; windowKind.value = ''; days.value = null; independent.value = ''; reason.value = '' }
async function refreshPending(check: () => void) { try { const value = await pendingPolicy(props.scope); check(); pending.value = value; storageError.value = false } catch (e) { check(); storageError.value = true; throw e } }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const version = epoch; const check = () => { if (!live || version !== epoch) throw new Error('页面范围已变化。') }
  busy.value = true; error.value = ''; notice.value = ''
  try { await action(check) } catch (e: any) { if (live && version === epoch) { if ([401, 403].includes(e?.response?.status)) clear(); error.value = e?.response?.data?.detail || e.message || '政策请求未完成。' } }
  finally { if (live && version === epoch) { try { await refreshPending(check) } catch (e: any) { if (live && version === epoch) error.value = e.message } if (live && version === epoch) busy.value = false } }
}
async function load(check: () => void) {
  const value = await api.read(props.option); check(); current.value = value.value; shown.value = value.value; loaded.value = true; emit('current', value)
  if (value.value) { const p = value.value.policy; name.value = p.name; enabled.value = p.enabled; reasons.value = p.reasonOptions.map(x => ({ ...x })); allowed.value = [...p.allowedOutcomes]; days.value = p.returnWindowDaysAfterDelivery; windowKind.value = days.value === null ? 'UNLIMITED' : 'LIMITED'; independent.value = p.requireIndependentApprover ? 'YES' : 'NO' }
  reason.value = ''
}
async function reload() { await run(async check => { clear(); await load(check) }) }
async function history(reference: RmaRef) { await run(async check => { const value = await api.read(props.option, reference); check(); shown.value = value.value }) }
async function publish() { await run(async check => {
  if (!allowed.value.length || !windowKind.value || !independent.value || windowKind.value === 'LIMITED' && !days.value) throw new Error('请完整选择处理方式、期限及审批人分离要求。')
  const body = { scopeOptionRef: toRaw(props.option), expectedPolicyRef: current.value?.reference ?? null, policy: { name: name.value, enabled: enabled.value, reasonOptions: reasons.value.map(x => ({ code: x.code, label: x.label })), allowedOutcomes: [...allowed.value], returnWindowDaysAfterDelivery: windowKind.value === 'UNLIMITED' ? null : days.value, requireIndependentApprover: independent.value === 'YES' }, reason: reason.value }
  await publishPolicy(props.scope, { version: 1, commandId: crypto.randomUUID(), body }, check); check(); await load(check); notice.value = '政策新版本已发布；未批准任何退货。'
}) }
async function recover(retry: boolean) { await run(async check => { const value = await recoverPolicy(props.scope, retry, check); check(); if (value) { await load(check); notice.value = `已找回原政策版本${value.reference.version}，当前政策已重新读取。` } else notice.value = '尚未观察到原结果，原请求保持待确认。' }) }
watch(() => [props.scope.tenantId, props.scope.actorId, props.option.id], () => { epoch++; busy.value = false; pending.value = null; clear(); void reload() }, { immediate: true })
onBeforeUnmount(() => { live = false; epoch++ })
</script>
