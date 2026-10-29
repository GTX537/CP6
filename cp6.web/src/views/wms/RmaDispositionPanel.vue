<template>
  <section aria-label="退货最终处置">
    <h2>退货最终处置</h2><p>按已采用的质量分区选择处置。准备会保留所选数量，执行后保存库存与业务回执。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>上次处置结果待确认。</p><button :disabled="busy" @click="recover(false)">查询原处置请求</button><button :disabled="busy" @click="recover(true)">查询并重派原处置请求</button></div>
    <label>处置方式<select v-model="kind" aria-label="处置方式" :disabled="busy"><option v-for="(label, value) in dispositionLabels" :key="value" :value="value">{{ label }}</option></select></label>
    <button :disabled="busy" @click="reload">刷新处置</button>
    <template v-if="context">
      <p v-if="!context.choices.length">没有可选择的剩余质量范围，请先完成并采用质量判定。</p>
      <label v-if="context.choices.length">质量范围<select v-model="index" aria-label="处置质量范围" :disabled="blocked" @change="reset"><option v-for="(choice, i) in context.choices" :key="choice.proposal.selectionRef.id" :value="i">{{ i + 1 }} · {{ qualityLabels[choice.proposal.quality] }} {{ choice.proposal.eligibleQuantity.value }} {{ choice.proposal.eligibleQuantity.uom.code }}</option></select></label>
      <template v-if="selected">
        <p>可选范围 {{ rangesText(selected.proposal.eligibleRanges) }}</p>
        <p v-if="!selected.options.length">此质量范围暂不可执行所选处置，请检查当前质量采用状态和业务批准。</p>
        <template v-else>
          <p v-if="selected.options[optionIndex]?.simulation">模拟业务批准与交接资料</p>
          <label>业务选项<select v-model="optionIndex" aria-label="处置业务选项" :disabled="blocked"><option v-for="(option, i) in selected.options" :key="option.reference.id" :value="i">{{ option.label }}</option></select></label>
          <fieldset v-if="can('prepare')" :disabled="blocked"><legend>处置数量范围</legend>
            <div v-for="(range, i) in ranges" :key="i"><input v-model="range.from" :aria-label="`处置起点${i + 1}`" inputmode="decimal" /><input v-model="range.to" :aria-label="`处置终点${i + 1}`" inputmode="decimal" /><button :disabled="ranges.length === 1" @click="ranges.splice(i, 1)">移除处置段</button></div>
            <button :disabled="ranges.length >= 100" @click="ranges.push({ from: '', to: '' })">添加处置段</button><button @click="prepare">准备处置</button>
          </fieldset>
        </template>
      </template>
      <label>处置原因<input v-model="reason" aria-label="处置原因" maxlength="500" :disabled="busy" /></label>
      <article v-for="(work, i) in context.works" :key="work.preparation.workId">
        <h3>处置 {{ i + 1 }} · {{ stateLabels[work.state] }}</h3><p>{{ work.preparation.quantity.value }} {{ work.preparation.quantity.uom.code }} {{ rangesText(work.preparation.ranges) }}</p>
        <p v-if="!work.qualityCurrent">质量依据已变化。未执行的处置可关闭后重新准备，已执行的效果保留并需后续更正。</p>
        <template v-if="work.state === 'PREPARED'"><button v-if="can('post')" :disabled="blocked || !work.qualityCurrent" @click="act('COMMIT', work)">执行此处置</button><button v-if="can('seal')" :disabled="blocked" @click="act('SEAL', work)">关闭未执行处置</button></template>
        <p v-if="work.result">库存处理已保存业务回执。{{ work.result.qualityReleaseRef ? '本次可售数量已通过独立 QC 放行。' : '' }}</p>
        <p v-if="work.noEffectProof">已确认此处置没有库存效果，范围可重新选择。</p>
      </article>
    </template>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, ref, shallowRef, watch } from 'vue'
import type { RmaScope } from '@/api/wms/rmaApplications'
import { returnRangeMeasure, type ReturnRange } from '@/api/wms/rmaAuthorizations'
import { dispositionLabels, rmaDispositionApi as api, type DispositionKind, type DispositionContext, type DispositionPending, type DispositionWork } from '@/api/wms/rmaDispositions'
import { pendingDisposition, recoverDisposition, writeDisposition } from '@/auth/rmaDispositionRecovery'
const props = defineProps<{ scope: RmaScope; applicationId: string }>()
const kind = ref<DispositionKind>('RESALE_STOCK'), context = shallowRef<DispositionContext | null>(null), pending = shallowRef<DispositionPending | null>(null), index = ref(0), optionIndex = ref(0)
const selected = computed(() => context.value?.choices[index.value]), ranges = ref<ReturnRange[]>([]), reason = ref(''), error = ref(''), notice = ref(''), busy = ref(false), storageError = ref(false)
const blocked = computed(() => busy.value || !!pending.value || storageError.value), can = (action: string) => context.value?.capabilities.includes(action)
const qualityLabels = { ACCEPTED: '合格', REJECTED: '不合格', HOLD: '保持', PENDING: '待判' }, stateLabels = { PREPARED: '待执行', APPLIED: '已执行', SEALED_NO_EFFECT: '已关闭且无效果' }
const rangesText = (values: ReturnRange[]) => values.map(x => `[${x.from}, ${x.to})`).join('；')
let epoch = 0, live = true
function clear() { context.value = null; ranges.value = []; reason.value = '' }
function reset() { ranges.value = structuredClone(selected.value?.proposal.eligibleRanges ?? []); optionIndex.value = 0 }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const version = epoch, check = () => { if (!live || epoch !== version) throw new Error('页面范围已变化。') }
  busy.value = true; error.value = ''; notice.value = ''
  try { await action(check) } catch (e: any) { if (live && epoch === version) { if ([401, 403].includes(e?.response?.status)) clear(); error.value = e?.response?.data?.detail || e.message || '处置未完成。' } }
  finally { if (live && epoch === version) {
    try { const value = await pendingDisposition(props.scope); check(); pending.value = value; storageError.value = false } catch (e: any) { if (live && epoch === version) { storageError.value = true; error.value = e.message } }
    if (live && epoch === version) busy.value = false
  } }
}
async function load(check: () => void) { const value = await api.context(kind.value, props.applicationId); check(); context.value = value; index.value = Math.min(index.value, Math.max(0, value.choices.length - 1)); reset() }
async function reload() { await run(async check => { clear(); await load(check) }) }
function requiredReason() { if (!reason.value.trim()) throw new Error('请填写处置原因。'); return reason.value }
async function send(command: DispositionPending, check: () => void) { await writeDisposition(props.scope, command, check); check(); await load(check); notice.value = '处置操作已保存。' }
async function prepare() { await run(async check => {
  returnRangeMeasure(ranges.value)
  await send({ version: 1, disposition: kind.value, applicationId: props.applicationId, commandId: crypto.randomUUID(), kind: 'PREPARE', body: { proposalRef: selected.value!.proposal.proposalRef, selectionRef: selected.value!.proposal.selectionRef, optionRef: selected.value!.options[optionIndex.value]!.reference, ranges: ranges.value.map(x => ({ from: x.from, to: x.to })), reason: requiredReason() } }, check)
}) }
async function act(action: 'COMMIT' | 'SEAL', work: DispositionWork) { await run(check => send({ version: 1, disposition: kind.value, applicationId: props.applicationId, commandId: crypto.randomUUID(), kind: action, workId: work.preparation.workId, body: { expectedWorkRef: work.workRef, reason: requiredReason() } }, check)) }
async function recover(retry: boolean) { await run(async check => { const value = await recoverDisposition(props.scope, retry, check); check(); if (value) { await load(check); notice.value = '已找回原处置请求。' } else notice.value = '尚未观察到原结果，原请求保持待确认。' }) }
watch(() => [props.scope.tenantId, props.scope.actorId, props.applicationId, kind.value], () => { epoch++; busy.value = false; pending.value = null; clear(); void reload() }, { immediate: true })
onBeforeUnmount(() => { live = false; epoch++ })
</script>
