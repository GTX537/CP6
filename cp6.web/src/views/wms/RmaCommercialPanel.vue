<template>
  <section aria-label="退货商业与财务结果">
    <h2>商业处理与财务结果</h2>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>商业申请结果待确认。</p><button :disabled="busy" @click="recover(false)">查询原商业申请</button><button :disabled="busy" @click="recover(true)">查询并重派原商业申请</button></div>
    <button :disabled="busy" @click="reload">刷新商业结果</button>
    <template v-if="context">
      <fieldset v-if="context.authorization?.outcome === 'APPROVED' && context.capabilities.includes('commercial-request')" :disabled="blocked"><legend>申请商业处理</legend>
        <label v-for="(share, i) in context.shareRefs" :key="share.id"><input v-model="selected" type="checkbox" :value="share.id" :aria-label="`退货份额${i + 1}`" />退货份额 {{ i + 1 }}</label>
        <label>处理方式<select v-model="outcome" aria-label="商业处理方式"><option v-for="(label, code) in commercialOutcomes" :key="code" :value="code">{{ label }}</option></select></label>
        <label>申请原因<input v-model="reason" aria-label="商业申请原因" maxlength="200" /></label><button @click="request">保存商业申请</button>
      </fieldset>
      <article v-for="(work, i) in context.requests" :key="work.request.requestId">
        <h3>申请 {{ i + 1 }} · {{ commercialOutcomes[work.request.requestedOutcome] }}</h3>
        <p>本次查询：{{ state(work.observationState) }}。{{ work.evidenceMode === 'SIMULATED' ? '外部商业与财务数据为模拟输入。' : '' }}</p>
        <p v-if="work.observationState !== 'FOUND'">当前查询尚未确认最新商业结果；已保存的结果保留，不能据此认定没有发生。</p>
        <button :disabled="blocked" @click="observe(work, false)">查询原商业及财务结果</button>
        <button v-if="context.capabilities.includes('commercial-dispatch')" :disabled="blocked" @click="observe(work, true)">重派原商业请求</button>
        <template v-if="work.commercialOutcome"><h4>已确认商业结果 · 版本 {{ work.commercialOutcome.resultVersion }}</h4>
          <p v-for="(component, j) in work.commercialOutcome.components" :key="j">{{ state(component.kind) }}
            {{ component.approvedQuantity?.value ?? component.quantity?.value }} {{ component.approvedQuantity?.uom.code ?? component.quantity?.uom.code }}
            <span v-if="component.authorizedAmount">{{ component.authorizedAmount.value }} {{ component.authorizedAmount.currency }}</span>
            <span v-for="(allocation, k) in component.invoiceAllocations" :key="k"> {{ allocation.gross.value }} {{ allocation.gross.currency }}</span>
          </p>
        </template>
        <h4>财务状态：{{ state(work.financialOutcome?.aggregateState ?? 'NOT_OBSERVED') }}</h4>
        <p v-if="work.financialSourceCurrent === false">商业结果已有后继版本，以下财务记录仍引用旧版本，需重新核对。</p>
        <p v-for="(axis, j) in work.financialOutcome?.axes" :key="j">{{ state(axis.axis) }} · {{ state(axis.state) }} · {{ axis.amount === null ? '金额未确定' : `${axis.amount} ${axis.currency}` }} · {{ state(axis.coverage) }} · 入账凭据 {{ axis.postingReceiptRefs.length }}</p>
      </article>
    </template>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, ref, shallowRef, watch } from 'vue'
import type { RmaScope } from '@/api/wms/rmaApplications'
import { commercialOutcomes, rmaCommercialApi as api, type CommercialContext, type CommercialPending, type CommercialWork } from '@/api/wms/rmaCommercial'
import { pendingCommercial, recoverCommercial, writeCommercial } from '@/auth/rmaCommercialRecovery'
const props = defineProps<{ scope: RmaScope; applicationId: string }>()
const context = shallowRef<CommercialContext | null>(null), pending = shallowRef<CommercialPending | null>(null)
const selected = ref<string[]>([]), outcome = ref<keyof typeof commercialOutcomes>('CREDIT'), reason = ref(''), busy = ref(false), storageError = ref(false), error = ref(''), notice = ref('')
const blocked = computed(() => busy.value || !!pending.value || storageError.value)
const labels: Record<string, string> = { FOUND: '已查询到', NOT_OBSERVED: '尚未观察到', UNKNOWN: '结果未知', PENDING: '处理中', STALE_HEAD: '版本已变化', REQUIRED_FENCED: '前置未满足', CREDIT: '贷项', REFUND_AUTHORIZATION: '退款授权', REPLACEMENT: '换货', SUPPLEMENTARY_SHIPMENT: '补发', RETURN_ONLY: '仅退货', REJECTED: '已拒绝', AR_CREDIT: '应收贷项', REFUND_CASH: '现金退款', INVENTORY_VALUE: '库存价值', COGS: '销售成本', GL_JOURNAL: '总账', POSTED: '已入账', PARTIAL: '部分完成', COMPLETE: '完整', REVERSED: '已冲正', WAITING_COST: '等待成本', WAITING_PRICE: '等待价格', WAITING_POLICY: '等待政策', NOT_APPLICABLE: '不适用', DRAFT: '草稿', NOT_STARTED: '未开始' }
const state = (value: string) => labels[value] ?? value
let epoch = 0, live = true
function clear() { context.value = null; selected.value = []; reason.value = '' }
async function load(check: () => void) { const value = await api.context(props.applicationId); check(); context.value = value }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const version = epoch, check = () => { if (!live || epoch !== version) throw new Error('页面范围已变化。') }
  busy.value = true; error.value = ''; notice.value = ''
  try { await action(check) } catch (e: any) { if (live && epoch === version) { if ([401, 403].includes(e?.response?.status)) clear(); error.value = e?.response?.data?.detail || e.message || '商业处理未完成。' } }
  finally { if (live && epoch === version) {
    try { const value = await pendingCommercial(props.scope); check(); pending.value = value; storageError.value = false } catch (e: any) { if (live && epoch === version) { storageError.value = true; error.value = e.message } }
    if (live && epoch === version) busy.value = false
  } }
}
async function reload() { await run(async check => { clear(); await load(check) }) }
async function request() { await run(async check => {
  if (!reason.value.trim() || !selected.value.length) throw new Error('请选择退货份额并填写申请原因。')
  await writeCommercial(props.scope, { version: 1, kind: 'request', commandId: crypto.randomUUID(), applicationId: props.applicationId,
    body: { expectedRmaHeadRef: context.value!.applicationRef, expectedAuthorizationRef: context.value!.authorization!.decisionRef, returnShareRefs: context.value!.shareRefs.filter(x => selected.value.includes(x.id)), requestedOutcome: outcome.value, reasonCode: reason.value } }, check)
  check(); selected.value = []; await load(check); notice.value = '商业申请已保存，可提交原请求至商业系统。'
}) }
async function recover(retry: boolean) { await run(async check => { const value = await recoverCommercial(props.scope, retry, check); check(); if (value) { await load(check); notice.value = '已找回原商业申请。' } else notice.value = '尚未观察到原申请，原键继续保留。' }) }
async function observe(work: CommercialWork, submit: boolean) { await run(async check => { await api.observe(props.applicationId, work.request.requestId, submit); check(); await load(check) }) }
watch(() => [props.scope.tenantId, props.scope.actorId, props.applicationId], () => { epoch++; busy.value = false; pending.value = null; clear(); void reload() }, { immediate: true })
onBeforeUnmount(() => { live = false; epoch++ })
</script>
