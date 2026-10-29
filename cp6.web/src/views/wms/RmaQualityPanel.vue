<template>
  <section aria-label="退货质量检验">
    <h2>退货质量检验</h2><p>质量人员独立签署检验分区，仓库采用当前结果后再办理后续处置。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>申请 {{ pending.applicationId }} 的原质量请求待确认。</p><button :disabled="busy" @click="recover(false)">查询原质量请求</button><button :disabled="busy" @click="recover(true)">查询并重派原质量请求</button></div>
    <button :disabled="busy" @click="reload">刷新退货质量</button><p v-if="context && !context.receipts.length">当前没有已入库的退货实收行。</p>
    <label v-if="context?.receipts.length">实收行<select v-model="index" aria-label="质量实收行" :disabled="busy" @change="reset"><option v-for="(row, i) in context.receipts" :key="row.source.lineId" :value="i">{{ i + 1 }} · {{ row.source.receipt.quantity.value }} {{ row.source.receipt.quantity.uom.code }}</option></select></label>
    <template v-if="selected">
      <p>实收范围 {{ rangesText(selected.source.receipt.receivedRanges) }}</p>
      <p v-if="selected.policy?.simulation">模拟质量政策与检验证据</p><p v-if="!selected.policy">尚未配置此实收行的质量政策。</p>
      <label>质量处理原因<input v-model="reason" aria-label="质量处理原因" maxlength="500" :disabled="busy" /></label>
      <button v-if="!selected.inspection && can('start')" :disabled="blocked || !selected.policy" @click="start">建立退货检验</button>
      <template v-if="selected.inspection">
        <p v-if="!selected.inspection.decision">检验任务已建立，尚无质量判定。</p>
        <div v-else><h3>{{ selected.inspection.decision.result.decisionState === 'ACTIVE' ? '当前质量判定' : '质量判定已撤回' }} · 版本 {{ selected.inspection.decision.result.decisionVersion }}</h3>
          <p v-for="part in selected.inspection.decision.result.partitions" :key="part.quality">{{ labels[part.quality] }}：{{ part.quantity.value }} {{ part.quantity.uom.code }} {{ rangesText(part.ranges) }}</p>
          <p v-if="selected.inspection.decision.result.voidRanges.length">已冲正范围 {{ rangesText(selected.inspection.decision.result.voidRanges) }}</p>
          <p>{{ selected.inspection.observationCurrent ? '仓库已采用当前判定' : selected.inspection.observation ? '仓库观察已过期，需要重新采用当前判定' : '仓库尚未采用此判定' }}</p>
          <button v-if="can('observe')" :disabled="blocked" @click="observe">采用当前质量结果</button>
          <button v-if="can('withdraw') && selected.inspection.decision.result.decisionState === 'ACTIVE'" :disabled="blocked" @click="withdraw">撤回质量判定</button>
        </div>
        <p v-if="selected.policy?.requireIndependentRegistrar && selected.source.registrarId === scope.actorId">本政策要求由实物登记人以外的质量人员签署。</p>
        <fieldset v-if="can('decide') && selected.policy" :disabled="blocked || selected.policy.requireIndependentRegistrar && selected.source.registrarId === scope.actorId">
          <legend>完整质量分区</legend><p>每段必须归入一种质量状态，全部范围应覆盖扣除已冲正数量后的有效实收。</p>
          <div v-for="(row, i) in partitions" :key="i"><select v-model="row.quality" :aria-label="`质量状态${i + 1}`"><option v-for="state in states" :key="state" :value="state">{{ labels[state] }}</option></select><input v-model="row.from" :aria-label="`质量起点${i + 1}`" inputmode="decimal" /><input v-model="row.to" :aria-label="`质量终点${i + 1}`" inputmode="decimal" /><button :disabled="partitions.length === 1" @click="partitions.splice(i, 1)">移除质量段</button></div>
          <button :disabled="partitions.length >= 400" @click="partitions.push({ quality: 'PENDING', from: '', to: '' })">添加质量段</button>
          <label v-for="(evidence, i) in selected.policy.evidenceRefs" :key="evidence.id"><input v-model="evidenceIndexes" type="checkbox" :value="i" :aria-label="`检验证据${i + 1}`" />检验证据 {{ i + 1 }} · {{ evidence.owner }} / {{ evidence.id }}</label>
          <button @click="decide">签署质量判定</button>
        </fieldset>
      </template>
    </template>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, ref, shallowRef, watch } from 'vue'
import type { RmaScope } from '@/api/wms/rmaApplications'
import { returnRangeMeasure, type ReturnRange } from '@/api/wms/rmaAuthorizations'
import { rmaQualityApi as api, type QualityContext, type QualityPending, type QualityState } from '@/api/wms/rmaQuality'
import { pendingQuality, recoverQuality, writeQuality } from '@/auth/rmaQualityRecovery'
const props = defineProps<{ scope: RmaScope; applicationId: string }>()
const states: QualityState[] = ['ACCEPTED', 'REJECTED', 'HOLD', 'PENDING'], labels = { ACCEPTED: '合格', REJECTED: '不合格', HOLD: '保持', PENDING: '待判' }
const context = shallowRef<QualityContext | null>(null), pending = shallowRef<QualityPending | null>(null), index = ref(0), selected = computed(() => context.value?.receipts[index.value])
const reason = ref(''), partitions = ref<(ReturnRange & { quality: QualityState })[]>([]), evidenceIndexes = ref<number[]>([])
const busy = ref(false), storageError = ref(false), error = ref(''), notice = ref(''), blocked = computed(() => busy.value || !!pending.value || storageError.value)
const can = (action: string) => context.value?.capabilities.includes(action), rangesText = (r: ReturnRange[]) => r.map(x => `[${x.from}, ${x.to})`).join('；')
let epoch = 0, live = true
function clear() { context.value = null; partitions.value = []; evidenceIndexes.value = []; reason.value = '' }
function reset() { const decision = selected.value?.inspection?.decision?.result; partitions.value = decision ? decision.partitions.flatMap(p => p.ranges.map(x => ({ ...x, quality: p.quality }))) : selected.value?.source.receipt.receivedRanges.map(x => ({ ...x, quality: 'PENDING' })) ?? []; evidenceIndexes.value = [] }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const version = epoch, check = () => { if (!live || epoch !== version) throw new Error('页面范围已变化。') }
  busy.value = true; error.value = ''; notice.value = ''
  try { await action(check) } catch (e: any) { if (live && epoch === version) { if ([401, 403].includes(e?.response?.status)) clear(); error.value = e?.response?.data?.detail || e.message || '质量操作未完成。' } }
  finally { if (live && epoch === version) {
    try { const value = await pendingQuality(props.scope); check(); pending.value = value; storageError.value = false } catch (e: any) { if (live && epoch === version) { storageError.value = true; error.value = e.message } }
    if (live && epoch === version) busy.value = false
  } }
}
async function load(check: () => void) { const value = await api.context(props.applicationId); check(); context.value = value; index.value = Math.min(index.value, Math.max(0, value.receipts.length - 1)); reset() }
async function reload() { await run(async check => { clear(); await load(check) }) }
function requiredReason() { if (!reason.value.trim()) throw new Error('请填写质量处理原因。'); return reason.value }
async function send(p: QualityPending, check: () => void) { await writeQuality(props.scope, p, check); check(); await load(check); notice.value = '质量操作已保存。' }
async function start() { await run(check => send({ version: 1, commandId: crypto.randomUUID(), applicationId: props.applicationId, kind: 'START', body: { lineId: selected.value!.source.lineId, expectedSourceRef: selected.value!.source.reference, expectedRmaHeadRef: selected.value!.source.physicalHeadRef, expectedPolicyRef: selected.value!.policy!.reference, reason: requiredReason() } }, check)) }
async function decide() { await run(async check => {
  const choice = selected.value!, basisRefs = evidenceIndexes.value.map(i => choice.policy!.evidenceRefs[i]!)
  if (partitions.value.length) returnRangeMeasure(partitions.value)
  const values = states.map(quality => ({ quality, ranges: partitions.value.filter(x => x.quality === quality).map(x => ({ from: x.from, to: x.to })), basisRefs, reasonCodes: ['INSPECTED'] }))
  if (values.some(x => x.quality !== 'PENDING' && x.ranges.length && !x.basisRefs.length)) throw new Error('请选择检验证据。')
  await send({ version: 1, commandId: crypto.randomUUID(), applicationId: props.applicationId, inspectionId: choice.inspection!.inspection.id, kind: 'DECIDE', body: { expectedHeadRef: choice.inspection!.headRef, expectedPolicyRef: choice.policy!.reference, partitions: values, reason: requiredReason() } }, check)
}) }
async function withdraw() { await run(check => send({ version: 1, commandId: crypto.randomUUID(), applicationId: props.applicationId, inspectionId: selected.value!.inspection!.inspection.id, kind: 'WITHDRAW', body: { expectedHeadRef: selected.value!.inspection!.headRef, reason: requiredReason() } }, check)) }
async function observe() { await run(check => send({ version: 1, commandId: crypto.randomUUID(), applicationId: props.applicationId, inspectionId: selected.value!.inspection!.inspection.id, kind: 'OBSERVE', body: { expectedDecisionRef: selected.value!.inspection!.decision!.result.decisionRef, expectedRmaHeadRef: selected.value!.source.physicalHeadRef } }, check)) }
async function recover(retry: boolean) { await run(async check => { const result = await recoverQuality(props.scope, retry, check); check(); if (result) { await load(check); notice.value = '已找回原质量请求。' } else notice.value = '尚未观察到原结果，原请求保持待确认。' }) }
watch(() => [props.scope.tenantId, props.scope.actorId, props.applicationId], () => { epoch++; busy.value = false; pending.value = null; clear(); void reload() }, { immediate: true })
onBeforeUnmount(() => { live = false; epoch++ })
</script>
