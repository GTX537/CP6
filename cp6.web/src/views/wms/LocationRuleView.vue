<template>
  <main class="location-rules">
    <h1>库位规则候选</h1><p>保存候选后尚未批准或生效。候选容量不会改变库存、预占数量或库位封锁。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <button :disabled="busy" @click="reload">重新载入</button>
    <section v-if="pending"><p>有一笔结果待确认，请先查询原操作。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy || !permissions.has('wms-location-rule:abandon')" @click="recover(true)">关闭未提交原操作</button></section>
    <section v-if="context">
      <h2>当前来源与规则</h2><p>库位 {{ context.locationCode }} · {{ context.initializationState === 'QUARANTINED' ? '隔离草稿，尚未启用' : '旧记录，初始化来源未核定' }}</p>
      <p>{{ context.referenceAuthorities === 'CONFIGURED' ? '已配置规则字典；候选须单独绑定当前准确来源。' : context.referenceAuthorities === 'DISABLED' ? '规则字典已停用，不能建立新绑定。' : '规则字典尚未配置，候选引用待核定。' }} 当前不能送审或应用规则。</p>
      <RouterLink v-if="context.warehouseId" :to="`/wms/warehouse/${context.warehouseId}/rule-references`">查看仓库规则字典</RouterLink>
      <details v-if="context.referenceCatalog"><summary>查看当前字典的准确代码与版本</summary><p>指标、适用域及精度引用版本：{{ context.referencePolicy?.version }}。单位使用各自原生版本。</p>
        <ul><li v-for="m in context.referenceCatalog.original.definition.metrics" :key="m.id">指标 {{ m.id }} · 量纲 {{ m.dimensionKey }}</li><li v-for="u in context.referenceCatalog.original.units" :key="u.reference.id">单位 {{ u.code }} · 版本 {{ u.reference.version }} · {{ u.reference.owner }}</li><li v-for="p in context.referenceCatalog.original.definition.precisions" :key="p.id">精度 {{ p.id }} · {{ p.scale }} 位小数，超出拒绝</li><li v-for="d in context.referenceCatalog.original.definition.domains" :key="d.id">适用域 {{ d.id }} · 规则族 {{ d.ruleFamilies.join('、') }} · 品类 {{ d.productTypes.join('、') }} · 动作 {{ d.operations.join('、') }}</li></ul>
      </details>
      <p v-if="!context.selectors.length">当前可见范围没有已登记的规则选择器。</p>
      <ul><li v-for="s in context.selectors" :key="s.id">{{ s.ruleFamily }} · {{ s.metricId }} · {{ s.applicabilityDomainId }}：选择器版本 {{ s.revision }}，{{ s.currentRuleVersionId ? '已有规则引用' : '尚无生效规则' }}</li></ul>
    </section>
    <h2>候选与历史</h2>
    <table v-if="rows.length"><thead><tr><th>候选 ID</th><th>版本</th><th>状态</th><th>操作</th></tr></thead><tbody><tr v-for="r in rows" :key="r.reference.id"><td>{{ r.reference.id }}</td><td>{{ r.reference.version }}</td><td>{{ r.state === 'WITHDRAWN' ? '已撤回' : '草稿' }}</td><td><button :disabled="busy || !!pending" @click="select(r)">查看候选</button></td></tr></tbody></table>
    <p v-else>当前页没有可读取的候选。</p><button :disabled="busy || page === 1" @click="move(-1)">上一页</button><span>第 {{ page }} 页</span><button :disabled="busy || rows.length < 50" @click="move(1)">下一页</button>
    <section v-if="selected"><p>当前候选版本 {{ selected.reference.version }}；{{ selected.state === 'DRAFT' ? '草稿' : '已撤回' }}。</p>
      <p>{{ selected.referenceResolution === 'RESOLVED' ? '本版本已保存准确来源绑定；后续采用前仍须重核当前来源，尚未批准或生效。' : '本版本来源尚未绑定。' }}</p>
      <p v-if="selected.referenceBinding">绑定字典版本 {{ selected.referenceBinding.policy.version }}，来源登记 {{ selected.referenceBinding.registryVersion }}。</p>
      <button :disabled="busy || !!pending || !canResolve" @click="resolve">绑定已保存候选来源</button><p>绑定针对上方已保存版本；下方尚未保存的修改不会一并提交。</p>
      <p v-if="selected.redactedFields.length">部分字段无读取权限，相关原文已隐藏。</p>
      <label>历史版本<input v-model="historyVersion" aria-label="历史版本" inputmode="numeric" /></label><button :disabled="busy" @click="readHistory">读取历史</button>
      <pre v-if="history">{{ JSON.stringify(history.candidate, null, 2) }}</pre><p v-if="history">上方为版本 {{ history.reference.version }} 的原候选，只供读取。</p>
      <label>撤回原因<input v-model="reason" aria-label="撤回原因" maxlength="1000" /></label><button :disabled="busy || !!pending || selected.state !== 'DRAFT' || !permissions.has('wms-location-rule:withdraw') || !reason.trim()" @click="withdraw">撤回候选</button>
    </section>
    <button :disabled="busy || !!pending || !permissions.has('wms-location-rule:draft')" @click="newDraft">填写新候选</button>
    <form v-if="context && editing" @submit.prevent="save"><fieldset :disabled="busy || !!pending"><legend>{{ selected ? '修改候选' : '新建候选' }}</legend>
      <fieldset :disabled="!!selected || !writable('Selector')"><legend>规则域</legend>
        <label>规则族<input v-model="family" aria-label="规则族" maxlength="64" /></label><label>指标 ID<input v-model="metric" aria-label="指标 ID" maxlength="100" /></label>
        <label>适用域 ID<input v-model="domain" aria-label="适用域 ID" maxlength="100" /></label><label>适用域版本<input v-model="domainVersion" aria-label="适用域版本" maxlength="64" /></label>
      </fieldset>
      <fieldset :disabled="!writable('Capacity')"><legend>候选容量</legend>
        <label>容量模式<select v-model="mode" aria-label="容量模式"><option value="" disabled>请选择</option><option value="LIMITED">有限容量</option><option value="UNLIMITED">不限该指标容量</option><option value="UNKNOWN">尚未核定</option></select></label>
        <template v-if="mode === 'LIMITED'"><label>容量值<input v-model="quantity" aria-label="容量值" inputmode="decimal" /></label><label>指标版本<input v-model="metricVersion" aria-label="指标版本" maxlength="64" /></label>
          <label>单位 ID<input v-model="uom" aria-label="单位 ID" maxlength="100" /></label><label>单位版本<input v-model="uomVersion" aria-label="单位版本" maxlength="64" /></label>
          <label>精度政策 ID<input v-model="precision" aria-label="精度政策 ID" maxlength="100" /></label><label>精度政策版本<input v-model="precisionVersion" aria-label="精度政策版本" maxlength="64" /></label><p>最多 13 位整数、8 位小数；有限 0 不代表不限容量。</p></template>
        <label v-if="mode === 'UNKNOWN'">待核原因<input v-model="unknownReason" aria-label="待核原因" maxlength="1000" /></label>
      </fieldset>
      <label>允许品类（每行一个代码）<textarea v-model="products" aria-label="允许品类" :disabled="!writable('AllowedProductTypes')" /></label>
      <label>允许动作（每行一个代码）<textarea v-model="operations" aria-label="允许动作" :disabled="!writable('AllowedOperations')" /></label><p>空列表明确表示没有声明允许项，不会解释为全部允许。</p>
      <label><input v-model="pickable" type="checkbox" aria-label="可拣候选" :disabled="!writable('IsPickable')" />可拣候选</label>
      <label>拟生效时刻（含时区，可留空待补）<input v-model="from" aria-label="拟生效时刻" :disabled="!writable('Validity')" placeholder="2026-10-01T08:00:00+08:00" /></label>
      <label>拟结束时刻（含时区，可留空待补）<input v-model="to" aria-label="拟结束时刻" :disabled="!writable('Validity')" /></label>
      <button :disabled="!canSave">保存候选</button>
    </fieldset></form>
  </main>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { locationRuleApi as api, type RuleDraft, type RuleCapacity, type RuleContext, type RuleView, type RulePatch } from '@/api/wms/locationRule'
import { submitLocationRule, recoverLocationRule, pendingLocationRule, locationRuleKey } from '@/auth/locationRuleRecovery'
import { transferResponse as value } from '@/auth/transferEnvironmentRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const route = useRoute(), location = computed(() => String(route.params.id || '')), permissions = usePermissionStore()
const context = ref<RuleContext | null>(null), rows = ref<RuleView[]>([]), selected = ref<RuleView | null>(null), history = ref<RuleView | null>(null)
const historyVersion = ref(''), reason = ref(''), busy = ref(false), pending = ref<string | null>(null), page = ref(1), editing = ref(false), error = ref(''), notice = ref('')
const family = ref(''), metric = ref(''), domain = ref(''), domainVersion = ref(''), mode = ref(''), quantity = ref(''), metricVersion = ref(''), uom = ref(''), uomVersion = ref(''), precision = ref(''), precisionVersion = ref(''), unknownReason = ref(''), products = ref(''), operations = ref(''), pickable = ref(false), from = ref(''), to = ref('')
let live = true, generation = 0
const writable = (field: string) => !!context.value?.fields.some(f => f.field === field && f.canWrite && (!selected.value || f.canRead))
const canSave = computed(() => selected.value ? selected.value.state === 'DRAFT' && permissions.has('wms-location-rule:edit') : permissions.has('wms-location-rule:draft') && !!context.value?.fields.every(f => f.canWrite))
const canResolve = computed(() => selected.value?.state === 'DRAFT' && permissions.has('wms-location-rule:resolve') && context.value?.referenceAuthorities === 'CONFIGURED' && !!context.value.referencePolicy && context.value.fields.every(f => f.canRead && f.canWrite) && selected.value.redactedFields.length === 0)
function form(d?: Partial<RuleDraft>) {
  family.value = d?.selectorKey?.ruleFamily || ''; metric.value = d?.selectorKey?.metricId || (d?.capacity?.mode === 'LIMITED' ? d.capacity.metricRef.id : ''); domain.value = d?.selectorKey?.applicabilityDomainRef.id || ''; domainVersion.value = d?.selectorKey?.applicabilityDomainRef.version || ''
  const c = d?.capacity; mode.value = c?.mode || ''; quantity.value = c?.mode === 'LIMITED' ? c.value : ''; metricVersion.value = c?.mode === 'LIMITED' ? c.metricRef.version : ''; uom.value = c?.mode === 'LIMITED' ? c.uomRef.id : ''; uomVersion.value = c?.mode === 'LIMITED' ? c.uomRef.version : ''; precision.value = c?.mode === 'LIMITED' ? c.precisionPolicyRef.id : ''; precisionVersion.value = c?.mode === 'LIMITED' ? c.precisionPolicyRef.version : ''; unknownReason.value = c?.mode === 'UNKNOWN' ? c.reason : ''
  products.value = d?.allowedProductTypes?.join('\n') || ''; operations.value = d?.allowedOperations?.join('\n') || ''; pickable.value = d?.isPickable ?? false; from.value = d?.validFrom || ''; to.value = d?.validTo || ''
}
function clear() { context.value = null; rows.value = []; selected.value = null; history.value = null; pending.value = null; editing.value = false; historyVersion.value = ''; reason.value = ''; form() }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return; const epoch = generation, stamp = identityStamp(), id = location.value; busy.value = true; error.value = ''; notice.value = ''
  const check = () => { requireIdentity(stamp); if (!live || epoch !== generation || id !== location.value || !authenticationCanRead()) throw new Error('当前身份或库位已变化。') }
  try { check(); await action(check) } catch (e: any) { if (live && epoch === generation) { if ([401, 403, 404].includes(e?.response?.status)) clear(); error.value = e?.message || '操作未完成。' } }
  finally { if (live && epoch === generation) { busy.value = false; if (context.value) { try { pending.value = pendingLocationRule(context.value.scope) } catch (e: any) { error.value = e.message; pending.value = 'invalid' } } } }
}
async function load(check: () => void) {
  const c = value(await api.context(location.value)); check(); locationRuleKey(c.scope); if (c.scope.locationId !== location.value) throw new Error('返回库位不一致。'); context.value = c; pending.value = pendingLocationRule(c.scope)
  const list = value(await api.list(location.value, page.value)); check(); if (list.some(r => r.locationId !== location.value)) throw new Error('返回候选范围不一致。'); rows.value = list
}
async function reload() { await run(async check => { clear(); if (!permissions.loaded) await permissions.loadMyActions(); check(); await load(check) }) }
function newDraft() { selected.value = null; history.value = null; reason.value = ''; form(); editing.value = true }
async function select(row: RuleView) { await run(async check => { const v = value(await api.read(location.value, row.reference.id)); check(); if (v.locationId !== location.value || v.reference.id !== row.reference.id) throw new Error('返回候选身份不一致。'); selected.value = v; history.value = null; reason.value = ''; form(v.candidate); editing.value = v.state === 'DRAFT' }) }
function capacity(): RuleCapacity {
  if (mode.value === 'UNLIMITED') return { mode: 'UNLIMITED' }
  if (mode.value === 'UNKNOWN' && unknownReason.value.trim()) return { mode: 'UNKNOWN', reason: unknownReason.value }
  if (mode.value === 'LIMITED' && /^[0-9]{1,13}(\.[0-9]{1,8})?$/.test(quantity.value) && [metric.value, metricVersion.value, uom.value, uomVersion.value, precision.value, precisionVersion.value].every(x => !!x.trim())) return { mode: 'LIMITED', value: quantity.value, metricRef: { id: metric.value, version: metricVersion.value }, uomRef: { id: uom.value, version: uomVersion.value }, precisionPolicyRef: { id: precision.value, version: precisionVersion.value } }
  throw new Error('请填写容量模式及其必要字段，数量需保持普通十进制格式。')
}
function codes(text: string) { return text === '' ? [] : text.split('\n') }
async function save() { await run(async check => {
  if (!context.value || !canSave.value) throw new Error('当前保存权限不足。')
  const s = context.value.scope, target = selected.value
  if (!target) {
    if (![family.value, metric.value, domain.value, domainVersion.value].every(x => !!x.trim())) throw new Error('请填写规则域和明确版本。')
    const draft: RuleDraft = { selectorKey: { ruleFamily: family.value, metricId: metric.value, applicabilityDomainRef: { id: domain.value, version: domainVersion.value } }, capacity: capacity(), allowedProductTypes: codes(products.value), allowedOperations: codes(operations.value), isPickable: pickable.value, validFrom: from.value || null, validTo: to.value || null }
    await submitLocationRule(s, null, operation => api.create(s.locationId, operation, draft), check)
  } else {
    const patch: RulePatch = {}, baseline = target.candidate
    const changes: [Exclude<keyof RuleDraft, 'selectorKey'>, string, () => unknown][] = [['capacity', 'Capacity', capacity], ['allowedProductTypes', 'AllowedProductTypes', () => codes(products.value)], ['allowedOperations', 'AllowedOperations', () => codes(operations.value)], ['isPickable', 'IsPickable', () => pickable.value], ['validFrom', 'Validity', () => from.value || null], ['validTo', 'Validity', () => to.value || null]]
    for (const [key, field, read] of changes) if (writable(field) && Object.hasOwn(baseline, key)) { const next = read(); if (JSON.stringify(next) !== JSON.stringify(baseline[key])) Object.assign(patch, { [key]: next === null ? { op: 'CLEAR' } : { op: 'SET', value: next } }) }
    await submitLocationRule(s, target.reference.id, operation => api.patch(s.locationId, target.reference.id, target.etag, operation, patch), check)
  }
  check(); selected.value = null; history.value = null; editing.value = false; form(); await load(check); notice.value = '候选已保存，尚未批准或生效。'
}) }
async function withdraw() { await run(async check => { const target = selected.value, s = context.value?.scope; if (!target || !s || !permissions.has('wms-location-rule:withdraw') || !reason.value.trim()) throw new Error('请核对权限并填写撤回原因。'); await submitLocationRule(s, target.reference.id, operation => api.withdraw(s.locationId, target.reference.id, target.etag, operation, reason.value), check); check(); selected.value = null; history.value = null; editing.value = false; form(); await load(check); notice.value = '候选已撤回，历史版本保留。' }) }
async function resolve() { await run(async check => {
  const target = selected.value, c = context.value; if (!target || !c || !canResolve.value || !c.referencePolicy) throw new Error('需要当前字典、完整字段权限和来源绑定权限。')
  const fresh = value(await api.context(location.value)); check()
  if (locationRuleKey(fresh.scope) !== locationRuleKey(c.scope) || fresh.referenceAuthorities !== 'CONFIGURED' || JSON.stringify(fresh.referencePolicy) !== JSON.stringify(c.referencePolicy)) throw new Error('规则字典或当前身份已变化，请重新载入。')
  await submitLocationRule(c.scope, target.reference.id, operation => api.resolve(c.scope.locationId, target.reference.id, target.etag, operation, c.referencePolicy!), check)
  check(); selected.value = null; history.value = null; editing.value = false; form(); await load(check); notice.value = '准确来源已绑定到候选版本，尚未批准或生效。'
}) }
async function recover(close: boolean) { await run(async check => { const s = context.value?.scope; if (!s || close && !permissions.has('wms-location-rule:abandon')) throw new Error('当前权限不足。'); const r = await recoverLocationRule(s, close, check); check(); if (r.state !== 'UNKNOWN') { selected.value = null; history.value = null; editing.value = false; form(); await load(check) } notice.value = r.state === 'COMMITTED' ? '已找回原操作；当前候选已重新载入。' : r.state === 'CLOSED' ? '未提交原操作已关闭。' : '结果仍未知，请继续查询。' }) }
async function readHistory() { await run(async check => { if (!selected.value || !/^[1-9][0-9]*$/.test(historyVersion.value)) throw new Error('请输入准确历史版本。'); const id = selected.value.reference.id, version = historyVersion.value; const v = value(await api.read(location.value, id, version)); check(); if (v.locationId !== location.value || v.reference.id !== id || v.reference.version !== version) throw new Error('历史引用不一致。'); history.value = v }) }
async function move(delta: number) { await run(async check => { page.value += delta; selected.value = null; history.value = null; editing.value = false; form(); await load(check) }) }
function reset() { generation++; busy.value = false; page.value = 1; clear(); error.value = ''; notice.value = ''; if (authenticationCanRead()) void reload() }
watch(location, reset)
onMounted(() => { window.addEventListener(AUTH_EVENT, reset); void reload() })
onBeforeUnmount(() => { live = false; generation++; clear(); window.removeEventListener(AUTH_EVENT, reset) })
</script>
<style scoped>
.location-rules{max-width:960px;margin:auto;padding:24px}.location-rules label{display:flex;flex-direction:column;gap:5px;margin:10px 0}.location-rules input,.location-rules textarea,.location-rules select{padding:8px}.location-rules fieldset,.location-rules section{padding:16px;border:1px solid #bdcbd4;border-radius:8px;margin:16px 0}.location-rules button{padding:8px 12px;margin:6px}.location-rules table{width:100%;border-collapse:collapse}.location-rules td,.location-rules th{padding:8px;text-align:left;border-bottom:1px solid #ccd5dc}.location-rules pre{white-space:pre-wrap;overflow-wrap:anywhere}[role=alert]{color:#a32626}
</style>
