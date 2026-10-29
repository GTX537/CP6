<template>
  <section aria-label="退货纠错冲正">
    <h2>退货纠错冲正</h2><p>用于修正记录错误。原业务和库存历史保留，独立批准后执行逆向动作。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>上次冲正请求结果待确认。</p><button :disabled="busy" @click="recover(false)">查询原冲正请求</button><button :disabled="busy" @click="recover(true)">查询并重派原冲正请求</button></div>
    <button :disabled="busy" @click="reload">刷新冲正</button>
    <template v-if="context">
      <label>原记录<select v-model="index" aria-label="冲正原记录" :disabled="blocked" @change="reset"><option v-for="(choice, i) in context.targets" :key="choice.target.id" :value="i">{{ i + 1 }} · {{ targetLabel(choice.target.kind) }} · {{ rangesText(choice.availableRanges) }}</option></select></label>
      <p v-if="selected?.policy?.simulation">模拟记录错误证据和独立审批：{{ selected.policy.label }}</p>
      <p v-if="selected && !selected.policy">该记录尚未配置纠错依据。</p>
      <fieldset v-if="selected?.policy && context.capabilities.includes('draft')" :disabled="blocked"><legend>{{ editing ? '修改冲正草稿' : '冲正数量范围' }}</legend>
        <div v-for="(range, i) in ranges" :key="i"><input v-model="range.from" :aria-label="`冲正起点${i + 1}`" inputmode="decimal" /><input v-model="range.to" :aria-label="`冲正终点${i + 1}`" inputmode="decimal" /><button :disabled="ranges.length === 1" @click="ranges.splice(i, 1)">移除冲正段</button></div>
        <button :disabled="ranges.length >= 100" @click="ranges.push({ from: '', to: '' })">添加冲正段</button>
        <label>错误证据<select v-model="evidenceIndex" aria-label="冲正错误证据"><option v-for="(evidence, i) in selected.policy.errorEvidenceRefs" :key="evidence.id" :value="i">证据 {{ i + 1 }}</option></select></label>
        <button @click="draft">{{ editing ? '保存冲正草稿' : '创建冲正草稿' }}</button><button v-if="editing" @click="reset">取消修改</button>
      </fieldset>
      <label>纠错原因<input v-model="reason" aria-label="纠错原因" maxlength="500" :disabled="busy" /></label>
      <article v-for="(work, i) in context.proposals" :key="work.candidate.id">
        <h3>冲正 {{ i + 1 }} · {{ stateLabels[work.state] }} · 版本 {{ work.candidate.version }}</h3><p>{{ targetLabel(work.candidate.target.kind) }} {{ rangesText(work.candidate.ranges) }} · {{ work.candidate.quantity.value }} {{ work.candidate.quantity.uom.code }}</p>
        <button v-if="work.allowedActions.includes('draft')" :disabled="blocked" @click="edit(work)">修改此草稿</button>
        <template v-for="(label, action) in correctionActions" :key="action"><button v-if="work.allowedActions.includes(action)" :disabled="blocked" @click="act(action, work)">{{ label }}</button></template>
        <p v-if="work.preparation && !work.result && !work.noEffectProof">方案已冻结，库存或审批变化时需撤回后重新申请。</p>
        <p v-if="work.result">冲正已执行，原范围已关闭，业务后继记录已保存。{{ work.result.qualityHeadRef ? '对应实收范围已从有效质量范围中扣除。' : '' }}</p>
        <p v-if="work.noEffectProof">未执行冲正已撤回，原方案不会再执行。</p>
      </article>
    </template>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, ref, shallowRef, watch } from 'vue'
import type { RmaScope } from '@/api/wms/rmaApplications'
import { returnRangeMeasure, type ReturnRange } from '@/api/wms/rmaAuthorizations'
import { dispositionLabels } from '@/api/wms/rmaDispositions'
import { correctionActions, rmaCorrectionApi as api, type CorrectionAction, type CorrectionContext, type CorrectionPending, type CorrectionWork } from '@/api/wms/rmaCorrections'
import { pendingCorrection, recoverCorrection, writeCorrection } from '@/auth/rmaCorrectionRecovery'
const props = defineProps<{ scope: RmaScope; applicationId: string }>()
const context = shallowRef<CorrectionContext | null>(null), pending = shallowRef<CorrectionPending | null>(null), index = ref(0), evidenceIndex = ref(0), editing = shallowRef<CorrectionWork | null>(null)
const selected = computed(() => context.value?.targets[index.value]), ranges = ref<ReturnRange[]>([]), reason = ref(''), error = ref(''), notice = ref(''), busy = ref(false), storageError = ref(false)
const blocked = computed(() => busy.value || !!pending.value || storageError.value)
const stateLabels = { DRAFT: '草稿', SUBMITTED: '等待独立审批', APPROVED: '已批准', POSTED: '已冲正', WITHDRAWN: '已撤回' }
const targetLabel = (kind: string) => kind === 'RECEIPT' ? '退货实收' : dispositionLabels[kind as keyof typeof dispositionLabels]
const rangesText = (values: ReturnRange[]) => values.map(x => `[${x.from}, ${x.to})`).join('；')
let epoch = 0, live = true
function clear() { context.value = null; ranges.value = []; reason.value = ''; editing.value = null }
function reset() { ranges.value = structuredClone(selected.value?.availableRanges ?? []); evidenceIndex.value = 0; editing.value = null }
function edit(work: CorrectionWork) { index.value = context.value!.targets.findIndex(x => x.target.id === work.candidate.target.id); editing.value = work; ranges.value = structuredClone(work.candidate.ranges); reason.value = work.candidate.reason; evidenceIndex.value = selected.value!.policy!.errorEvidenceRefs.findIndex(x => x.id === work.candidate.errorEvidenceRef.id) }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const version = epoch, check = () => { if (!live || epoch !== version) throw new Error('页面范围已变化。') }
  busy.value = true; error.value = ''; notice.value = ''
  try { await action(check) } catch (e: any) { if (live && epoch === version) { if ([401, 403].includes(e?.response?.status)) clear(); error.value = e?.response?.data?.detail || e.message || '冲正未完成。' } }
  finally { if (live && epoch === version) {
    try { const value = await pendingCorrection(props.scope); check(); pending.value = value; storageError.value = false } catch (e: any) { if (live && epoch === version) { storageError.value = true; error.value = e.message } }
    if (live && epoch === version) busy.value = false
  } }
}
async function load(check: () => void) { const value = await api.context(props.applicationId); check(); context.value = value; index.value = Math.min(index.value, Math.max(0, value.targets.length - 1)); reset() }
async function reload() { await run(async check => { clear(); await load(check) }) }
function requiredReason() { if (!reason.value.trim()) throw new Error('请填写纠错原因。'); return reason.value }
async function send(command: CorrectionPending, check: () => void) { await writeCorrection(props.scope, command, check); check(); await load(check); notice.value = '冲正操作已保存。' }
async function draft() { await run(async check => {
  returnRangeMeasure(ranges.value)
  const common = { version: 1 as const, applicationId: props.applicationId, commandId: crypto.randomUUID() }, body = { ranges: ranges.value.map(x => ({ from: x.from, to: x.to })), errorEvidenceRef: selected.value!.policy!.errorEvidenceRefs[evidenceIndex.value]!, reason: requiredReason() }
  await send(editing.value ? { ...common, kind: 'edit', proposalId: editing.value.candidate.id, body: { ...body, expectedHeadRef: editing.value.headRef } } : { ...common, kind: 'draft', body: { ...body, axis: selected.value!.target.axis, targetId: selected.value!.target.id, expectedTargetRef: selected.value!.target.reference } }, check)
}) }
async function act(action: CorrectionAction, work: CorrectionWork) { await run(check => send({ version: 1, applicationId: props.applicationId, commandId: crypto.randomUUID(), kind: action, proposalId: work.candidate.id, body: { expectedHeadRef: work.headRef, reason: requiredReason() } }, check)) }
async function recover(retry: boolean) { await run(async check => { const value = await recoverCorrection(props.scope, retry, check); check(); if (value) { await load(check); notice.value = '已找回原冲正请求。' } else notice.value = '尚未观察到原结果，原请求保持待确认。' }) }
watch(() => [props.scope.tenantId, props.scope.actorId, props.applicationId], () => { epoch++; busy.value = false; pending.value = null; clear(); void reload() }, { immediate: true })
onBeforeUnmount(() => { live = false; epoch++ })
</script>
