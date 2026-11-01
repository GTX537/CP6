<template>
  <section>
    <h3>人工补货</h3><p>保存草稿不占补货预算。准备时登记目标未到量，等待调拨准备完成后才能派发。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>原操作结果待核。</p><button :disabled="busy" @click="recover">查询草稿原操作</button><button :disabled="busy" @click="retry">重试草稿原请求</button></div>
    <fieldset :disabled="busy || !!pending">
      <label>订单编号<input v-model="orderId" aria-label="人工补货订单编号" /></label><button :disabled="!orderId" @click="loadOrder">读取订单</button>
      <button @click="newDraft">新草稿</button>
      <template v-if="!order || order.lifecycle === 'DRAFT'">
        <label>产品、来源与目标<select v-model="choice" aria-label="人工补货来源" @change="choose"><option value="">请选择</option><option v-for="(o, i) in options" :key="i" :value="String(i)">{{ o.productRef.id }} / {{ o.source.sourceLocationRef.id }} → {{ o.toLocationRef.id }} / {{ o.ownerRef.id }} / {{ o.baseUomRef.id }}</option></select></label>
        <template v-if="input">
          <p>产品 {{ input.productRef.id }} · 技术 {{ input.technicalRef.id }} · 货主 {{ input.ownerRef.id }} · 单位 {{ input.baseUomRef.id }}</p>
          <label>数量<input v-model="input.quantity.value" aria-label="人工补货数量" inputmode="decimal" /></label>
          <div v-for="(span, i) in input.source.spans" :key="i"><span>{{ span.rootRef.id }}</span><label>起点<input v-model="span.from" :aria-label="`来源 ${i} 起点`" inputmode="decimal" /></label><label>终点<input v-model="span.to" :aria-label="`来源 ${i} 终点`" inputmode="decimal" /></label><button v-if="input.source.spans.length > 1" @click="input.source.spans.splice(i, 1)">移除此来源</button></div>
          <label>优先级<select v-model.number="input.priority" aria-label="人工补货优先级"><option :value="2">普通</option><option :value="1" :disabled="!can('expedite')">加急</option></select></label>
          <label>理由<input v-model="input.reason" aria-label="人工补货理由" maxlength="500" /></label>
          <button :disabled="!can(order ? 'edit' : 'add') || !input.reason.trim()" @click="save">保存草稿</button>
        </template>
      </template>
    </fieldset>
    <section v-if="order">
      <p>订单 {{ order.ref.id }} · 版本 {{ order.revision }} · {{ lifecycle(order.lifecycle) }}</p>
      <p>计划 {{ order.planned.value }}，已移 {{ order.moved?.value ?? '未知' }} {{ order.planned.uomRef.id }}</p>
      <p v-if="dirty">请先保存草稿修改，再准备补货。</p>
      <button :disabled="busy || !!pending || dirty || !can('prepare') || !['DRAFT', 'REGISTERED'].includes(order.lifecycle)" @click="prepare">准备补货</button>
      <button :disabled="busy" @click="loadHistory(false)">读取版本历史</button>
      <ul><li v-for="h in history" :key="h.revision">版本 {{ h.revision }} · {{ lifecycle(h.lifecycle) }} · {{ h.planned.value }} {{ h.planned.uomRef.id }}</li></ul>
      <button v-if="nextRevision" :disabled="busy" @click="loadHistory(true)">更多历史</button>
    </section>
    <p v-if="prepared" role="status">{{ prepared.canDispatch ? '调拨准备完成，可派发。' : '补货义务已登记，等待调拨准备；尚未派发或搬运库存。' }}</p>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { replenishPlanningApi as api, type RepContext, type RepOrderInput, type RepDraftInput, type RepOrder, type RepPrepare, type RepCommand } from '@/api/wms/replenishPlanning'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
type Pending = { kind: 'create' | 'edit'; id: string | null; body: RepDraftInput } | { kind: 'prepare'; id: string; body: RepCommand }
const props = defineProps<{ context: RepContext }>(), permissions = usePermissionStore()
const can = (action: string) => permissions.has(`wms-replenish:${action}`)
const options = computed(() => props.context.scopes.flatMap(s => s.draftOptions))
const order = ref<RepOrder | null>(null), input = ref<RepOrderInput | null>(null), prepared = ref<RepPrepare | null>(null), pending = ref<Pending | null>(null)
const choice = ref(''), orderId = ref(''), error = ref(''), notice = ref(''), busy = ref(false), history = ref<RepOrder[]>([]), nextRevision = ref<string | null>(null)
const dirty = computed(() => order.value?.lifecycle === 'DRAFT' && JSON.stringify(input.value) !== JSON.stringify(order.value.input))
const storage = computed(() => `cp6_rep_draft_v1:${props.context.environmentId}:${props.context.tenantId}:${props.context.actorId}`)
let epoch = 0, live = true
const clone = <T,>(v: T): T => JSON.parse(JSON.stringify(v))
const lifecycle = (value: string) => ({ DRAFT: '草稿，未计入补货义务', REGISTERED: '已登记，未派发', PREPARED: '已准备' }[value] || value)
function reset() { order.value = null; input.value = null; prepared.value = null; choice.value = ''; orderId.value = ''; history.value = []; nextRevision.value = null }
function newDraft() { reset(); notice.value = ''; error.value = '' }
function choose() { input.value = choice.value === '' ? null : clone(options.value[Number(choice.value)]!); prepared.value = null }
function show(o: RepOrder) { order.value = o; orderId.value = o.ref.id; input.value = clone(o.input); choice.value = ''; history.value = []; nextRevision.value = null }
async function run(work: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const version = epoch, identity = identityStamp(); busy.value = true; error.value = ''; notice.value = ''
  const check = () => { requireIdentity(identity); if (!live || version !== epoch || !authenticationCanRead()) throw new Error('当前身份已变化。') }
  try { check(); await work(check) } catch (e: any) { if (live && version === epoch) error.value = e?.response?.data?.message || e?.message || '补货操作失败。' }
  finally { if (live && version === epoch) busy.value = false }
}
async function lock(check: () => void, work: () => Promise<void>) {
  if (!navigator.locks?.request) throw new Error('浏览器不支持多页面提交协调。')
  await navigator.locks.request(storage.value, { mode: 'exclusive', ifAvailable: true }, async active => { check(); if (!active) throw new Error('另一页面正在提交。'); await work() })
}
async function accept(p: Pending, result: RepOrder | RepPrepare, check: () => void) {
  localStorage.removeItem(storage.value); pending.value = null
  if (p.kind === 'prepare') { prepared.value = result as RepPrepare; const current = await api.order(p.id); check(); show(current) }
  else { prepared.value = null; show(result as RepOrder); notice.value = '草稿已保存，未计入补货义务。' }
}
async function send(p: Pending, check: () => void) {
  let result: RepOrder | RepPrepare
  try {
    result = p.kind === 'prepare' ? await api.prepare(p.id, p.body) : p.kind === 'edit' ? await api.editDraft(p.id!, p.body) : await api.createDraft(p.body); check()
  } catch (e: any) { check(); if ([400, 403, 404, 409, 412, 422].includes(e?.response?.status)) { localStorage.removeItem(storage.value); pending.value = null }; throw e }
  await accept(p, result, check)
}
async function submit(p: Pending, check: () => void) { await lock(check, async () => {
  if (localStorage.getItem(storage.value)) throw new Error('请先确认上一笔操作结果。')
  localStorage.setItem(storage.value, JSON.stringify(p)); pending.value = p; await send(p, check)
}) }
async function save() { await run(async check => {
  if (!can(order.value ? 'edit' : 'add')) throw new Error('没有草稿保存权限。')
  const value = clone(input.value!); value.source.quantity = { ...value.quantity }
  await submit({ kind: order.value ? 'edit' : 'create', id: order.value?.ref.id ?? null, body: { command: { commandKey: crypto.randomUUID(), expectedRevision: order.value?.revision ?? null, reason: value.reason }, input: value } }, check)
}) }
async function prepare() { await run(async check => {
  if (!can('prepare')) throw new Error('没有准备权限。')
  await submit({ kind: 'prepare', id: order.value!.ref.id, body: { commandKey: crypto.randomUUID(), expectedRevision: order.value!.revision, reason: '用户准备补货' } }, check)
}) }
async function loadOrder() { await run(async check => { const o = await api.order(orderId.value); check(); prepared.value = null; show(o) }) }
async function loadHistory(more: boolean) { await run(async check => { const page = await api.history(order.value!.ref.id, more ? nextRevision.value! : '0'); check(); history.value = more ? [...history.value, ...page.items] : page.items; nextRevision.value = page.nextRevision }) }
async function retry() { await run(async check => { await lock(check, async () => { const saved = localStorage.getItem(storage.value); if (!saved) throw new Error('没有待确认操作。'); await send(JSON.parse(saved), check) }) }) }
async function recover() { await run(async check => { await lock(check, async () => {
  const saved = localStorage.getItem(storage.value); if (!saved) throw new Error('没有待确认操作。')
  const p = JSON.parse(saved) as Pending, key = p.kind === 'prepare' ? p.body.commandKey : p.body.command.commandKey
  const outcome = await api.operation<RepOrder | RepPrepare>(key); check()
  if (outcome.state === 'COMMITTED') { await accept(p, outcome.result!, check); notice.value = '已找回原操作结果。' }
  else notice.value = '原结果仍待核，请使用原请求重试。'
}) }) }
function identityChanged() { epoch++; busy.value = false; reset(); pending.value = null; error.value = ''; notice.value = '' }
function restore() { const saved = localStorage.getItem(storage.value); pending.value = saved ? JSON.parse(saved) : null }
watch(storage, () => { identityChanged(); restore() })
onMounted(() => { window.addEventListener(AUTH_EVENT, identityChanged); restore() })
onBeforeUnmount(() => { live = false; epoch++; window.removeEventListener(AUTH_EVENT, identityChanged) })
</script>
<style scoped>
fieldset { display: grid; gap: .75rem; margin: 1rem 0; } label { margin-right: .75rem; } input, select { margin-left: .3rem; } button { margin: .3rem; } [role=alert] { color: #a32121; }
</style>
