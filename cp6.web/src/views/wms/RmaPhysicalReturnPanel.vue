<template>
  <section aria-label="退货实物收货">
    <h2>退货实物收货</h2><p>登记到货范围，准备入库后确认。收货进入待检库位，后续质量处置与商业处理分别办理。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>申请 {{ pending.applicationId }} 的原收货请求待确认。</p><button :disabled="busy" @click="recover(false)">查询原收货请求</button><button :disabled="busy" @click="recover(true)">查询并重派原收货请求</button></div>
    <button :disabled="busy" @click="reload">刷新实物收货</button>
    <fieldset v-if="context?.authorization?.outcome === 'APPROVED' && can('register')" :disabled="busy || !!pending || storageError">
      <p v-if="!context.locations.length">当前没有可用的待检收货库位。</p>
      <label>批准份额<select v-model="shareIndex" aria-label="收货批准份额" @change="resetRanges"><option v-for="(share, i) in context.shares" :key="share.shareRef.id" :value="i">份额 {{ i + 1 }} · {{ rangeText(share.availableRanges) || '无可收货范围' }}</option></select></label>
      <label>待检收货库位<select v-model="locationIndex" aria-label="待检收货库位"><option v-for="(location, i) in context.locations" :key="location.locationId" :value="i">{{ location.warehouseCode }} / {{ location.locationCode }} · {{ location.label }}</option></select></label>
      <div v-for="(range, i) in ranges" :key="i"><label>范围起点<input v-model="range.from" :aria-label="`收货起点${i + 1}`" inputmode="decimal" /></label><label>范围终点<input v-model="range.to" :aria-label="`收货终点${i + 1}`" inputmode="decimal" /></label><button :disabled="ranges.length === 1" @click="ranges.splice(i, 1)">移除收货区间</button></div>
      <button :disabled="ranges.length >= 100" @click="ranges.push({ from: '', to: '' })">添加收货区间</button>
      <label>到货时间（含时区）<input v-model="occurredAt" aria-label="实物到货时间" /></label><label>收货备注<textarea v-model="remarks" aria-label="收货备注" maxlength="1000" /></label>
      <button :disabled="!context.locations.length || !ranges.length" @click="register">登记实物到货</button>
    </fieldset>
    <label v-if="context?.works.some(x => ['REGISTERED', 'PREPARED'].includes(x.work.state))">处理原因<input v-model="reason" aria-label="收货处理原因" maxlength="500" :disabled="busy" /></label>
    <article v-for="work in context?.works" :key="work.work.workId" :aria-label="`收货批次 ${work.work.workId}`">
      <h3>{{ states[work.work.state] }} · 版本 {{ work.work.revision }}</h3><p v-for="line in work.lines" :key="line.reference.id">{{ line.input.quantity.value }} {{ line.input.quantity.uom.code }} → {{ line.location.warehouseCode }}/{{ line.location.locationCode }} · {{ rangeText(line.input.ranges) }}</p>
      <p v-for="(plan, i) in work.preparations" :key="i">{{ plan.stockPostingPlanRequest.use.action === 'RETURN' ? '追溯原库存根退回' : '新建可追溯收货来源' }}</p>
      <div v-for="result in work.results" :key="result.receiptOperationRef.id"><p>{{ result.stockResult.simulation ? '模拟业务场景' : '业务场景' }} · {{ result.stockResult.commitKnowledge === 'COMMITTED' ? '库存已提交' : result.stockResult.commitKnowledge }}</p><p v-for="receipt in result.physicalReceipts" :key="receipt.movementRef.id">已入库 {{ receipt.quantity.value }} {{ receipt.quantity.uom.code }} · 库存流水 {{ receipt.movementRef.id }}</p></div>
      <p v-if="work.noEffectProofRef">本请求已关闭且没有库存效果，实物登记记录保留。</p>
      <button v-if="work.work.state === 'REGISTERED' && can('prepare')" :disabled="busy || !!pending || storageError" @click="act(work, 'PREPARE')">准备此批入库</button>
      <button v-if="work.work.state === 'PREPARED' && can('post')" :disabled="busy || !!pending || storageError" @click="act(work, 'COMMIT')">确认此批入库</button>
      <button v-if="['REGISTERED', 'PREPARED'].includes(work.work.state) && can('seal')" :disabled="busy || !!pending || storageError" @click="act(work, 'SEAL')">关闭此批未入库请求</button>
    </article>
  </section>
</template>
<script setup lang="ts">
import { onBeforeUnmount, ref, shallowRef, watch } from 'vue'
import type { RmaScope } from '@/api/wms/rmaApplications'
import { returnRangeMeasure, type ReturnRange } from '@/api/wms/rmaAuthorizations'
import { rmaPhysicalApi as api, type PhysicalContext, type PhysicalPending, type PhysicalWork } from '@/api/wms/rmaPhysicalReturns'
import { pendingPhysical, recoverPhysical, writePhysical } from '@/auth/rmaPhysicalRecovery'
const props = defineProps<{ scope: RmaScope; applicationId: string; capabilities: string[] }>()
const context = shallowRef<PhysicalContext | null>(null), pending = shallowRef<PhysicalPending | null>(null)
const ranges = ref<ReturnRange[]>([]), shareIndex = ref(0), locationIndex = ref(0), occurredAt = ref(''), remarks = ref(''), reason = ref('')
const busy = ref(false), storageError = ref(false), error = ref(''), notice = ref('')
const states = { REGISTERED: '已登记，待准备', PREPARED: '已准备，待确认入库', SUCCEEDED: '收货已完成', SEALED_NO_EFFECT: '未入库请求已关闭' }
const can = (action: string) => props.capabilities.includes(`wms.rma.physical.${action}`)
const rangeText = (value: ReturnRange[]) => value.map(x => `[${x.from}, ${x.to})`).join('；')
let epoch = 0, live = true
function clear() { context.value = null; ranges.value = []; remarks.value = ''; reason.value = '' }
function resetRanges() { ranges.value = context.value!.shares[shareIndex.value]!.availableRanges.map(x => ({ ...x })); occurredAt.value = new Date().toISOString() }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const version = epoch, check = () => { if (!live || version !== epoch) throw new Error('页面范围已变化。') }
  busy.value = true; error.value = ''; notice.value = ''
  try { await action(check) } catch (e: any) { if (live && version === epoch) { if ([401, 403].includes(e?.response?.status)) clear(); error.value = e?.response?.data?.detail || e.message || '收货请求未完成。' } }
  finally { if (live && version === epoch) {
    try { const value = await pendingPhysical(props.scope); check(); pending.value = value; storageError.value = false } catch (e: any) { if (live && version === epoch) { storageError.value = true; error.value = e.message } }
    if (live && version === epoch) busy.value = false
  } }
}
async function load(check: () => void) { const value = await api.context(props.applicationId); check(); context.value = value; shareIndex.value = 0; locationIndex.value = 0; if (value.shares.length) resetRanges(); else ranges.value = [] }
async function reload() { await run(async check => { clear(); await load(check) }) }
async function register() { await run(async check => {
  const current = context.value!, share = current.shares[shareIndex.value]!, location = current.locations[locationIndex.value]!
  if (!/(Z|[+-]\d{2}:\d{2})$/.test(occurredAt.value) || !Number.isFinite(Date.parse(occurredAt.value))) throw new Error('到货时间必须有效并包含时区。')
  const selected = ranges.value.map(x => ({ ...x })), quantity = { value: returnRangeMeasure(selected), uom: share.share.quantity.uom }
  await writePhysical(props.scope, { version: 1, commandId: crypto.randomUUID(), applicationId: props.applicationId, kind: 'REGISTER', body: {
    expectedAuthorizationHeadRef: current.authorization!.decisionRef, expectedRmaHeadRef: current.headRef, remarks: remarks.value || null,
    receiptLines: [{ lineId: crypto.randomUUID(), returnShareRef: share.shareRef, ranges: selected, quantity, locationOptionRef: location.optionRef,
      lotRefs: share.share.original.lotRefs, serialRefs: share.share.original.serialRefs, lpnRefs: share.share.original.lpnRefs, occurredAt: occurredAt.value }],
  } }, check); check(); await load(check); notice.value = '实物到货已登记。'
}) }
async function act(work: PhysicalWork, kind: 'PREPARE' | 'COMMIT' | 'SEAL') { await run(async check => {
  if (!reason.value.trim()) throw new Error('请填写处理原因。')
  await writePhysical(props.scope, { version: 1, commandId: crypto.randomUUID(), applicationId: props.applicationId, workId: work.work.workId, kind,
    body: { expectedWorkRef: work.workRef, expectedRmaHeadRef: context.value!.headRef, reason: reason.value } }, check)
  check(); await load(check); notice.value = kind === 'COMMIT' ? '收货与库存已提交。' : kind === 'SEAL' ? '此批未入库请求已关闭。' : '此批入库已准备。'
}) }
async function recover(retry: boolean) { await run(async check => { const result = await recoverPhysical(props.scope, retry, check); check(); if (result) { await load(check); notice.value = `已找回原收货请求：${states[result.work.state]}。` } else notice.value = '尚未观察到原结果，原请求保持待确认。' }) }
watch(() => [props.scope.tenantId, props.scope.actorId, props.applicationId, props.capabilities.join('|')], () => { epoch++; busy.value = false; pending.value = null; clear(); void reload() }, { immediate: true })
onBeforeUnmount(() => { live = false; epoch++ })
</script>
