<template>
  <section aria-label="退货授权">
    <h2>退货授权</h2><p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>申请 {{ pending.applicationId }} 的原授权请求待确认。</p><button :disabled="busy" @click="recover(false)">查询原授权请求</button><button :disabled="busy" @click="recover(true)">查询并重派原授权请求</button></div>
    <button :disabled="busy" @click="reload">刷新授权</button>
    <article v-if="decision"><h3>{{ outcomes[decision.outcome] }} · 决定版本 {{ decision.decisionVersion }} / {{ headVersion }}</h3>
      <p>决定于 {{ decision.decidedAt }} · 原因 {{ decision.reasonCodes.join('；') }}</p>
      <p>生效时间 {{ decision.validFrom }}；到期时间 {{ decision.validTo || '未设到期时间' }}</p>
      <p v-for="share in decision.authorizedShares" :key="share.shareId">批准数量 {{ share.quantity.value }} {{ share.quantity.uom.code }} · {{ rangeText(share.ranges) }} · 签收来源 {{ share.original.deliveryProofRef?.owner || '未提供' }}</p>
      <button :disabled="busy || decision.decisionVersion === 1" @click="history(decision.decisionVersion - 1)">上一授权版本</button><button :disabled="busy || decision.decisionVersion === headVersion" @click="history(decision.decisionVersion + 1)">下一授权版本</button>
    </article><p v-else-if="loaded">尚无授权决定。</p>
    <div v-if="context && submitted">
      <p>适用政策：{{ context.policy.policy.name }} · 版本 {{ context.policy.reference.version }}<span v-if="context.policy.policy.requireIndependentApprover"> · 由申请人以外的审批人决定</span></p>
      <fieldset v-if="canDecide && context.currentDecision?.outcome !== 'APPROVED'" :disabled="busy || !!pending || storageError">
        <label>决定原因<input v-model="reason" aria-label="授权决定原因" maxlength="200" /></label>
        <button @click="loadEntitlements">读取可退额度</button>
        <div v-for="(line, index) in context.lines" :key="line.requestedLineId">
          <h3>申请行 {{ index + 1 }}<span v-if="line.evidenceMode === 'SIMULATED'"> · 模拟业务数据</span></h3>
          <p>已占用 {{ line.entitlement.activeClaimQty.value }}；已收货 {{ line.entitlement.receivedQty.value }}；已贷记 {{ line.entitlement.creditedQty.value }}；剩余可批准 {{ line.entitlement.remainingAuthorizableQty.value }} {{ line.coordinate.baseUom.code }}</p>
          <p>本申请可选区间：{{ rangeText(line.availableRanges) || '无' }}</p>
          <label><input v-model="drafts[index]!.selected" type="checkbox" :aria-label="`批准申请行${index + 1}`" :disabled="!line.availableRanges.length" />批准此行</label>
          <div v-if="drafts[index]!.selected">
            <div v-for="(range, ri) in drafts[index]!.ranges" :key="ri"><label>起点<input v-model="range.from" :aria-label="`批准起点${index + 1}-${ri + 1}`" inputmode="decimal" /></label><label>终点<input v-model="range.to" :aria-label="`批准终点${index + 1}-${ri + 1}`" inputmode="decimal" /></label><button :disabled="drafts[index]!.ranges.length === 1" @click="drafts[index]!.ranges.splice(ri, 1)">移除批准区间</button></div>
            <button :disabled="drafts[index]!.ranges.length >= 100" @click="drafts[index]!.ranges.push({ from: '', to: '' })">添加批准区间</button>
          </div>
        </div>
        <label>批准到期时间（可选，含时区）<input v-model="validTo" aria-label="批准到期时间" placeholder="2026-11-30T23:59:59+08:00" /></label>
        <button :disabled="!context.lines.length" @click="decide('APPROVED')">批准退货</button><button @click="decide('REJECTED')">拒绝退货</button>
      </fieldset>
      <fieldset v-if="canRevoke && context.currentDecision?.outcome === 'APPROVED'" :disabled="busy || !!pending || storageError">
        <p>撤销释放尚未使用的批准范围，保留已收货及结果待确认的占用。</p><label>撤销原因<input v-model="revokeReason" aria-label="授权撤销原因" maxlength="500" /></label><button @click="revoke">撤销当前授权</button>
      </fieldset>
    </div>
  </section>
</template>
<script setup lang="ts">
import { onBeforeUnmount, ref, shallowRef, watch } from 'vue'
import type { RmaScope } from '@/api/wms/rmaApplications'
import { rmaAuthorizationApi as api, returnRangeMeasure, type AuthorizationContext, type AuthorizationDecision, type AuthorizationPending, type ReturnRange } from '@/api/wms/rmaAuthorizations'
import { pendingAuthorization, recoverAuthorization, writeAuthorization } from '@/auth/rmaAuthorizationRecovery'
const props = defineProps<{ scope: RmaScope; applicationId: string; submitted: boolean; canDecide: boolean; canRevoke: boolean }>()
const emit = defineEmits<{ current: [value: AuthorizationDecision | null] }>()
const outcomes = { APPROVED: '已批准', REJECTED: '已拒绝', REVOKED: '已撤销', EXPIRED: '已到期关闭' }
const context = shallowRef<AuthorizationContext | null>(null), decision = shallowRef<AuthorizationDecision | null>(null), pending = shallowRef<AuthorizationPending | null>(null)
const drafts = ref<{ selected: boolean; ranges: ReturnRange[] }[]>([]), reason = ref(''), revokeReason = ref(''), validTo = ref('')
const busy = ref(false), loaded = ref(false), storageError = ref(false), error = ref(''), notice = ref(''), headVersion = ref(0)
let epoch = 0, live = true
const rangeText = (ranges: ReturnRange[]) => ranges.map(x => `[${x.from}, ${x.to})`).join('；')
function clear() { context.value = null; decision.value = null; loaded.value = false; drafts.value = []; reason.value = ''; revokeReason.value = ''; validTo.value = ''; headVersion.value = 0; emit('current', null) }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const version = epoch, check = () => { if (!live || version !== epoch) throw new Error('页面范围已变化。') }
  busy.value = true; error.value = ''; notice.value = ''
  try { await action(check) } catch (e: any) { if (live && version === epoch) { if ([401, 403].includes(e?.response?.status)) clear(); error.value = e?.response?.data?.detail || e.message || '授权请求未完成。' } }
  finally { if (live && version === epoch) {
    try { const value = await pendingAuthorization(props.scope); check(); pending.value = value; storageError.value = false } catch (e: any) { if (live && version === epoch) { storageError.value = true; error.value = e.message } }
    if (live && version === epoch) busy.value = false
  } }
}
async function load(check: () => void) {
  const value = await api.read(props.applicationId); check(); decision.value = value; headVersion.value = value?.decisionVersion ?? 0; loaded.value = true; emit('current', value)
  if (props.submitted && (props.canDecide || props.canRevoke)) { const action = await api.context(props.applicationId, false); check(); context.value = action }
}
async function reload() { await run(async check => { clear(); await load(check) }) }
async function history(version: number) { await run(async check => { const value = await api.read(props.applicationId, version); check(); decision.value = value }) }
async function loadEntitlements() { await run(async check => {
  context.value = null; drafts.value = []
  const value = await api.context(props.applicationId, true); check()
  context.value = value; drafts.value = value.lines.map(x => ({ selected: x.availableRanges.length > 0, ranges: x.availableRanges.map(r => ({ ...r })) }))
}) }
async function decide(outcome: 'APPROVED' | 'REJECTED') { await run(async check => {
  if (!reason.value.trim()) throw new Error('请填写决定原因。')
  const current = context.value!, approved = outcome === 'APPROVED'
  const lines = approved ? current.lines.filter((_x, i) => drafts.value[i]!.selected) : []
  if (approved && !lines.length) throw new Error('请选择有可退额度的申请行。')
  if (approved && validTo.value && (!/(Z|[+-]\d{2}:\d{2})$/.test(validTo.value) || !Number.isFinite(Date.parse(validTo.value)))) throw new Error('到期时间必须包含时区。')
  const shares = lines.map(line => { const ranges = drafts.value[current.lines.indexOf(line)]!.ranges.map(r => ({ from: r.from, to: r.to })); return { requestedLineId: line.requestedLineId, entitlementSnapshotRef: line.entitlement.ref, ranges, quantity: { value: returnRangeMeasure(ranges), uom: line.coordinate.baseUom } } })
  const heads = [...new Map(lines.map(x => [x.entitlement.currentHeadRef.id, x.entitlement.currentHeadRef])).values()]
  const body = { expectedApplicationHeadRef: current.applicationRef, expectedEntitlementHeadRefs: heads, outcome, authorizedShares: shares, authorityRef: current.authorityRef, reasonCodes: [reason.value], validFrom: new Date().toISOString(), validTo: approved && validTo.value ? validTo.value : null, exceptionApprovalRef: null }
  await writeAuthorization(props.scope, { version: 1, commandId: crypto.randomUUID(), applicationId: props.applicationId, kind: 'DECIDE', body }, check); check(); clear(); await load(check); notice.value = '授权决定已保存。'
}) }
async function revoke() { await run(async check => {
  if (!revokeReason.value.trim()) throw new Error('请填写撤销原因。')
  const current = context.value!
  await writeAuthorization(props.scope, { version: 1, commandId: crypto.randomUUID(), applicationId: props.applicationId, kind: 'REVOKE', body: { expectedDecisionHeadRef: current.currentDecision!.decisionRef, affectedReturnShareRefs: current.currentShareRefs, authorityRef: current.authorityRef, reason: revokeReason.value } }, check)
  check(); clear(); await load(check); notice.value = '当前授权已关闭，原决定保留。'
}) }
async function recover(retry: boolean) { await run(async check => {
  const result = await recoverAuthorization(props.scope, retry, check); check()
  if (result) { clear(); await load(check); notice.value = `已找回原授权请求：${outcomes[result.outcome]}。` } else notice.value = '尚未观察到原结果，原请求保持待确认。'
}) }
watch(() => [props.scope.tenantId, props.scope.actorId, props.applicationId, props.submitted, props.canDecide, props.canRevoke], () => { epoch++; busy.value = false; pending.value = null; clear(); void reload() }, { immediate: true })
onBeforeUnmount(() => { live = false; epoch++ })
</script>
