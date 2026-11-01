<template>
  <section class="replenish-planning">
    <h2>补货建议与登记</h2><p>按正式规则计算缺额。登记后立即计入未到量，库存搬运另行执行。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <button :disabled="busy" @click="reload">读取补货规则</button>
    <template v-if="context">
      <p v-if="!context.scopes.length">当前没有可用的补货观察资料。</p>
      <div v-if="pending"><p>上一笔操作结果待核，请查询或使用原请求重试。</p><button :disabled="busy" @click="recover">查询原操作</button><button :disabled="busy" @click="retry">按原请求重试</button></div>
      <fieldset :disabled="busy || !!pending || !allowed">
        <label>仓库<select v-model="scopeIndex" aria-label="补货仓库" @change="resetSelection"><option value="">请选择</option><option v-for="(s, i) in context.scopes" :key="s.scope.warehouseRef.id" :value="String(i)">{{ s.scope.warehouseRef.id }}</option></select></label>
        <template v-if="selectedScope">
          <p v-if="selectedScope.dataKind === 'SIMULATED'">当前使用模拟业务数据。</p>
          <label v-for="r in selectedScope.rules" :key="r.ref.id"><input v-model="ruleIds" type="checkbox" :value="r.ref.id" :aria-label="`规则 ${r.ref.id}`" @change="clearSuggestion" />{{ r.productRef.id }}：下限 {{ r.minQuantity.value }}，上限 {{ r.maxQuantity.value }}，单位 {{ r.baseUomRef.id }}</label>
          <button :disabled="!ruleIds.length" @click="suggest">计算补货建议</button>
        </template>
      </fieldset>
      <section v-if="suggestion"><h3>补货建议</h3>
        <table><thead><tr><th>选择</th><th>产品／目标</th><th>现量</th><th>未到量</th><th>有效位置</th><th>建议量</th><th>原因</th></tr></thead><tbody>
          <tr v-for="row in suggestion.rows" :key="row.lineKey"><td><input v-model="selectedLines" type="checkbox" :value="row.lineKey" :disabled="busy || !!pending || !row.allocationPlanRef || !allowed" :aria-label="`建议 ${row.lineKey}`" /></td><td>{{ row.productRef.id }}／{{ row.targetLocationRef.id }}</td><td>{{ amount(row.onHand) }}</td><td>{{ amount(row.openIncoming) }}</td><td>{{ amount(row.effectivePosition) }}</td><td>{{ amount(row.proposed) }}</td><td>{{ row.reasons.map(reasonName).join('；') }}</td></tr>
        </tbody></table>
        <details v-for="row in suggestion.rows" :key="`sources-${row.lineKey}`"><summary>{{ row.targetLocationRef.id }} 来源分配</summary><p v-for="source in row.sources" :key="source.sourceLocationRef.id">{{ source.sourceLocationRef.id }}：{{ source.quantity.value }} {{ source.quantity.uomRef.id }}</p></details>
        <button :disabled="busy || !!pending || !selectedLines.length || !allowed" @click="register">登记所选补货</button>
      </section>
      <section v-if="batch"><h3>已登记补货</h3><p>登记已提交，尚未派发或搬运库存。</p><ul><template v-for="item in batch.items" :key="item.lineKey"><li v-for="split in item.splits" :key="split.orderRef.id">订单 {{ split.orderRef.id }} · {{ split.quantity.value }} {{ split.quantity.uomRef.id }} · {{ split.outcome === 'EXISTING' ? '原有登记' : '已登记' }}</li></template></ul></section>
    </template>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { replenishPlanningApi as api, repSelectionDigest, type RepContext, type RepSuggestion, type RepBatch, type RepPending, type RepQuantity, type RepSelection } from '@/api/wms/replenishPlanning'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const permissions = usePermissionStore(), allowed = computed(() => permissions.has('wms-replenish:generate'))
const context = ref<RepContext | null>(null), suggestion = ref<RepSuggestion | null>(null), batch = ref<RepBatch | null>(null), pending = ref<RepPending | null>(null)
const busy = ref(false), error = ref(''), notice = ref(''), scopeIndex = ref(''), ruleIds = ref<string[]>([]), selectedLines = ref<string[]>([])
const selectedScope = computed(() => scopeIndex.value === '' ? null : context.value!.scopes[Number(scopeIndex.value)])
let generation = 0, live = true
const key = () => `cp6_rep_v1:${context.value!.environmentId}:${context.value!.tenantId}:${context.value!.actorId}`
const amount = (q: RepQuantity | null) => q ? `${q.value} ${q.uomRef.id}` : '未知'
const reasonName = (s: string) => ({ READY: '可登记', NO_NEED: '无需补货', NO_MOVABLE_SOURCE: '没有可移动来源', NO_FULL_MULTIPLE: '不足一个补货批量', COMPLETE_CUT_REQUIRED: '观察资料不完整', CAPACITY_UNKNOWN: '容量未知', CAPACITY_INSUFFICIENT: '容量不足', CAPACITY_CONVERSION_UNKNOWN: '容量换算未知' }[s] || s)
function clearSuggestion() { suggestion.value = null; selectedLines.value = []; batch.value = null }
function resetSelection() { ruleIds.value = []; clearSuggestion() }
async function run(work: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const stamp = identityStamp(), epoch = generation; busy.value = true; error.value = ''; notice.value = ''
  const check = () => { requireIdentity(stamp); if (!live || epoch !== generation || !authenticationCanRead()) throw new Error('当前身份已变化。') }
  try { check(); await work(check) } catch (e: any) { if (live && epoch === generation) error.value = e?.response?.data?.message || e?.message || '补货操作失败。' }
  finally { if (live && epoch === generation) busy.value = false }
}
async function reload() { await run(async check => {
  const c = await api.context(); check(); context.value = c; scopeIndex.value = ''; resetSelection()
  const saved = localStorage.getItem(key()); pending.value = saved ? JSON.parse(saved) : null
}) }
function apply(p: RepPending, result: RepSuggestion | RepBatch) {
  if (p.kind === 'suggest') { suggestion.value = result as RepSuggestion; selectedLines.value = []; batch.value = null }
  else { batch.value = result as RepBatch; selectedLines.value = []; notice.value = '补货登记已提交，未到量已计入。' }
}
async function locked(check: () => void, work: () => Promise<void>) {
  if (!navigator.locks?.request) throw new Error('浏览器不支持多页面提交协调。')
  await navigator.locks.request(key(), { mode: 'exclusive', ifAvailable: true }, async lock => { check(); if (!lock) throw new Error('另一页面正在提交。'); await work() })
}
async function send(p: RepPending, check: () => void) {
  try {
    const result = p.kind === 'suggest' ? await api.suggest(p.body) : await api.generate(p.body); check()
    localStorage.removeItem(key()); pending.value = null; apply(p, result)
  } catch (e: any) {
    check()
    if ([400, 403, 404, 409, 412, 422].includes(e?.response?.status)) { localStorage.removeItem(key()); pending.value = null }
    throw e
  }
}
async function submit(p: RepPending, check: () => void) { await locked(check, async () => {
  if (localStorage.getItem(key())) throw new Error('请先确认上一笔操作结果。')
  check(); localStorage.setItem(key(), JSON.stringify(p)); pending.value = p; await send(p, check)
}) }
async function suggest() { await run(async check => {
  if (!allowed.value || !selectedScope.value) throw new Error('请选择有权补货范围。')
  const scope = selectedScope.value, rules = scope.rules.filter(x => ruleIds.value.includes(x.ref.id))
  const targets = [...new Map(rules.flatMap(x => x.targetLocationRefs).map(x => [JSON.stringify(x), x])).values()]
  await submit({ kind: 'suggest', body: { command: { commandKey: crypto.randomUUID(), expectedRevision: null, reason: '用户计算补货建议' }, scope: scope.scope, ruleRefs: rules.map(x => x.ref), targetLocationRefs: targets } }, check)
}) }
async function register() { await run(async check => {
  if (!allowed.value) throw new Error('没有登记权限。')
  const s = suggestion.value!, selectedPlans: RepSelection[] = s.rows.filter(x => selectedLines.value.includes(x.lineKey)).map(x => ({ lineKey: x.lineKey, allocationPlanRef: x.allocationPlanRef! }))
  const selectionDigest = await repSelectionDigest(selectedPlans); check()
  await submit({ kind: 'batch', body: { command: { commandKey: crypto.randomUUID(), expectedRevision: null, reason: '用户确认补货登记' }, suggestionRef: s.ref, selectedPlans, selectionDigest } }, check)
}) }
async function retry() { await run(async check => { await locked(check, async () => { const saved = localStorage.getItem(key()); if (!saved) throw new Error('没有待确认操作。'); await send(JSON.parse(saved), check) }) }) }
async function recover() { await run(async check => { await locked(check, async () => {
  const saved = localStorage.getItem(key()); if (!saved) throw new Error('没有待确认操作。')
  const p = JSON.parse(saved) as RepPending, result = await api.operation(p.body.command.commandKey); check()
  if (result.state === 'COMMITTED') { localStorage.removeItem(key()); pending.value = null; apply(p, result.result!); notice.value = '已找回原操作结果。' }
  else notice.value = '原操作结果仍待核；可以使用同一原请求重试。'
}) }) }
function identityChanged() { generation++; busy.value = false; context.value = null; pending.value = null; scopeIndex.value = ''; resetSelection(); error.value = ''; notice.value = '' }
onMounted(() => { window.addEventListener(AUTH_EVENT, identityChanged); void reload() })
onBeforeUnmount(() => { live = false; generation++; window.removeEventListener(AUTH_EVENT, identityChanged) })
</script>
<style scoped>
.replenish-planning { margin: 1rem 0; padding: 1rem; border: 1px solid #ccd4df; border-radius: .5rem; }
fieldset { display: flex; flex-wrap: wrap; gap: .8rem; margin: 1rem 0; } table { width: 100%; border-collapse: collapse; } th, td { padding: .5rem; text-align: left; border-bottom: 1px solid #ddd; } button { margin: .3rem; } [role=alert] { color: #a32121; } [role=status] { color: #176a3b; }
</style>
