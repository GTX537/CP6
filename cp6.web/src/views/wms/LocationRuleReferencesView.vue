<template>
  <main class="rule-references">
    <h1>库位规则字典</h1><p>为本仓库明确指标、单位、精度和允许范围。保存字典不会批准候选或启用库位。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p><button :disabled="busy" @click="reload">重新载入</button>
    <template v-if="context">
      <p>仓库 {{ context.warehouseCode }} · 字典版本 {{ context.version }} · {{ context.policy?.original.definition.enabled ? '已配置，使用前核对当前来源' : '未启用' }}</p>
      <section v-if="pending"><p>有一笔结果待确认。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy || !permission.has('wms-warehouse:abandon')" @click="recover(true)">关闭未提交原操作</button></section>
      <form @submit.prevent="save"><fieldset :disabled="busy || !!pending || !canEdit"><legend>字典内容</legend>
        <label>字典状态<select v-model="state" aria-label="字典状态" required><option disabled value="">请选择</option><option value="enabled">启用</option><option v-if="context.policy" value="disabled">停用当前版本</option></select></label>
        <p v-if="state === 'disabled'">停用只保存当前版本的停用决定，不同时修改字典内容。</p>
        <fieldset :disabled="state === 'disabled'"><legend>指标与真实单位</legend>
          <label>查找单位<input v-model="unitSearch" aria-label="查找单位" maxlength="128" /></label><button type="button" @click="searchUnits">查询单位</button>
          <p>单位列表仅显示当前有权读取的一页，不代表全部单位。选择后会固定准确版本；保存时重新核对。</p>
          <button type="button" :disabled="unitPage === 1" @click="pageUnits(-1)">上一页单位</button><button type="button" :disabled="units.length < 20" @click="pageUnits(1)">下一页单位</button>
          <article v-for="(m, i) in definition.metrics" :key="i"><h3>指标 {{ i + 1 }}</h3>
            <label>指标 ID<input v-model="m.id" :aria-label="`指标${i + 1} ID`" maxlength="100" required /></label><label>量纲代码<input v-model="m.dimensionKey" :aria-label="`指标${i + 1} 量纲`" maxlength="128" required /></label>
            <p v-for="u in m.units" :key="u.id">{{ unitName(u) }} · 版本 {{ u.version }} <button type="button" @click="m.units = m.units.filter(x => x.id !== u.id)">移除此单位</button></p>
            <label v-for="u in units" :key="u.reference.id" class="choice"><input type="checkbox" :checked="m.units.some(x => x.id === u.reference.id && x.version === u.reference.version && x.digest === u.reference.digest)" :disabled="!u.profile.enabled" :aria-label="`指标${i + 1} 单位 ${u.profile.code}`" @change="choose(m, u.reference, ($event.target as HTMLInputElement).checked)" />{{ u.profile.code }} · {{ u.profile.name }} · {{ u.profile.dimensionKey }} · v{{ u.reference.version }}{{ u.profile.enabled ? '' : '（已停用）' }}</label>
            <button type="button" @click="definition.metrics.splice(i, 1)">移除指标</button>
          </article><button type="button" :disabled="definition.metrics.length >= 100" @click="definition.metrics.push({ id: '', dimensionKey: '', units: [] })">添加指标</button>
        </fieldset>
        <fieldset :disabled="state === 'disabled'"><legend>精度政策</legend>
          <article v-for="(p, i) in definition.precisions" :key="i"><label>精度政策 ID<input v-model="p.id" :aria-label="`精度${i + 1} ID`" maxlength="100" required /></label>
            <label>允许小数位数<input v-model.number="p.scale" type="number" min="0" max="8" step="1" :aria-label="`精度${i + 1} 位数`" required /></label><p>超过精度时拒绝，不自动舍入。</p><button type="button" @click="definition.precisions.splice(i, 1)">移除精度政策</button></article>
          <button type="button" :disabled="definition.precisions.length >= 100" @click="definition.precisions.push({ id: '', scale: 8, rounding: 'REJECT' })">添加精度政策</button>
        </fieldset>
        <fieldset :disabled="state === 'disabled'"><legend>适用域</legend>
          <article v-for="(d, i) in definition.domains" :key="i"><label>适用域 ID<input v-model="d.id" :aria-label="`适用域${i + 1} ID`" maxlength="100" required /></label>
            <label v-for="f in codeFields" :key="f.key">{{ f.label }}（每行一个代码）<textarea :value="d[f.key].join('\n')" :aria-label="`适用域${i + 1} ${f.label}`" @input="d[f.key] = lines(($event.target as HTMLTextAreaElement).value)" /></label>
            <label v-for="f in flags" :key="f.key" class="choice"><input v-model="d[f.key]" type="checkbox" :aria-label="`适用域${i + 1} ${f.label}`" />{{ f.label }}</label>
            <button type="button" @click="definition.domains.splice(i, 1)">移除适用域</button>
          </article><button type="button" :disabled="definition.domains.length >= 100" @click="addDomain">添加适用域</button>
          <p>品类和动作是本字典显式定义的代码。未列出的代码不能通过来源绑定；空列表、无限容量和无结束时刻均按勾选政策判断。</p>
        </fieldset>
        <label>操作原因<textarea v-model="reason" aria-label="操作原因" maxlength="1000" required /></label><button>保存字典</button>
      </fieldset></form>
      <form @submit.prevent="readHistory"><label>历史版本<input v-model="historyVersion" aria-label="历史版本" pattern="[1-9][0-9]*" required /></label><button :disabled="busy">读取历史</button></form>
      <article v-if="history"><h2>历史版本 {{ history.reference.version }}</h2><p>{{ history.original.reason }}</p><details><summary>查看保存原件</summary><pre>{{ history.originalJson }}</pre></details></article>
    </template><p><RouterLink to="/wms/warehouse">返回仓库查询</RouterLink></p>
  </main>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ruleReferencesApi as api, type RuleReferenceContext, type RuleReferencePolicy, type RuleDefinition, type RuleMetric } from '@/api/wms/locationRuleReferences'
import { uomApi, type UomReference, type UomVersion } from '@/api/erp/uomProfiles'
import { ruleReferencesKey, pendingRuleReferences, saveRuleReferences, recoverRuleReferences } from '@/auth/locationRuleReferencesRecovery'
import { transferResponse as value } from '@/auth/transferEnvironmentRecovery'
import { identityStamp, requireIdentity, AUTH_EVENT } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { masterUuid } from '@/types/wms/warehouseOwner'
import { usePermissionStore } from '@/stores/permission'
const route = useRoute(), permission = usePermissionStore(), warehouse = computed(() => String(route.params.id || '')), canEdit = computed(() => permission.has('wms-warehouse:rule-policy'))
const context = ref<RuleReferenceContext | null>(null), history = ref<RuleReferencePolicy | null>(null), units = ref<UomVersion[]>([])
const blank = (): RuleDefinition => ({ enabled: false, metrics: [], precisions: [], domains: [] })
const definition = ref<RuleDefinition>(blank()), state = ref(''), reason = ref(''), historyVersion = ref(''), unitSearch = ref(''), unitPage = ref(1)
const busy = ref(false), error = ref(''), notice = ref(''), pending = ref<string | null>(null)
const codeFields = [{ key: 'ruleFamilies', label: '规则族' }, { key: 'metricIds', label: '指标 ID' }, { key: 'precisionIds', label: '精度政策 ID' }, { key: 'productTypes', label: '允许品类' }, { key: 'operations', label: '允许动作' }] as const
const flags = [{ key: 'allowEmptyProducts', label: '允许空品类集合' }, { key: 'allowEmptyOperations', label: '允许空动作集合' }, { key: 'allowUnlimited', label: '允许不限指标容量' }, { key: 'allowOpenEnded', label: '允许无结束时刻' }, { key: 'allowPickable', label: '允许可拣规则' }] as const
let live = true, generation = 0
const lines = (text: string) => text === '' ? [] : text.split('\n')
const copy = <T,>(o: T): T => JSON.parse(JSON.stringify(o))
function addDomain() { definition.value.domains.push({ id: '', ruleFamilies: [], metricIds: [], precisionIds: [], productTypes: [], operations: [], allowEmptyProducts: false, allowEmptyOperations: false, allowUnlimited: false, allowOpenEnded: false, allowPickable: false }) }
function choose(m: RuleMetric, u: UomReference, checked: boolean) { m.units = m.units.filter(x => x.id !== u.id); if (checked) m.units.push(copy(u)) }
function unitName(u: UomReference) { return units.value.find(x => x.reference.id === u.id && x.reference.version === u.version)?.profile.code || context.value?.policy?.original.units.find(x => x.reference.id === u.id && x.reference.version === u.version)?.code || u.id }
function clear() { context.value = null; history.value = null; definition.value = blank(); state.value = ''; reason.value = ''; units.value = []; pending.value = null; historyVersion.value = ''; unitSearch.value = ''; unitPage.value = 1 }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return; const epoch = generation, stamp = identityStamp(), id = warehouse.value; busy.value = true; error.value = ''; notice.value = ''
  const check = () => { requireIdentity(stamp); if (!live || epoch !== generation || id !== warehouse.value || !authenticationCanRead()) throw new Error('当前身份或仓库已变化。') }
  try { check(); if (!masterUuid(id)) throw new Error('请从仓库查询进入字典。'); await action(check) }
  catch (e: any) { if (live && epoch === generation) { if ([401, 403, 404].includes(e?.response?.status)) clear(); error.value = e?.message || '操作未完成，请核对权限和来源。' } }
  finally { if (live && epoch === generation) { busy.value = false; if (context.value) try { pending.value = pendingRuleReferences(context.value) } catch (e: any) { pending.value = 'invalid'; error.value = e.message } } }
}
async function load(check: () => void) {
  const c = value(await api.context(warehouse.value)); check(); ruleReferencesKey(c); if (c.warehouseId !== warehouse.value) throw new Error('返回仓库不一致。')
  context.value = c; pending.value = pendingRuleReferences(c); definition.value = c.policy ? copy(c.policy.original.definition) : blank(); state.value = c.policy ? c.policy.original.definition.enabled ? 'enabled' : 'disabled' : ''
}
async function reload() { await run(async check => { clear(); if (!permission.loaded) await permission.loadMyActions(); check(); await load(check) }) }
async function loadUnits(check: () => void) {
  units.value = []; if (!context.value || !permission.has('erp-uom-profile:query')) throw new Error('需要当前单位查询权限。')
  const scope = value(await uomApi.scope()); check(); if (scope.tenantId !== context.value.tenantId || scope.actorId !== context.value.actorId) throw new Error('单位来源身份已变化。')
  const list = value(await uomApi.list(unitSearch.value, unitPage.value, 20)); check()
  if (list.some(u => u.tenantId !== scope.tenantId || !u.isCurrent || u.reference.owner !== 'Main.Erp.UomProfile.v1' || !masterUuid(u.reference.id) || !/^[1-9][0-9]*$/.test(u.reference.version) || !/^[a-f0-9]{64}$/.test(u.reference.digest))) throw new Error('单位来源或版本无法核对。')
  units.value = list
}
async function searchUnits() { await run(async check => { unitPage.value = 1; await loadUnits(check) }) }
async function pageUnits(delta: number) { await run(async check => { unitPage.value += delta; await loadUnits(check) }) }
async function save() { await run(async check => {
  const c = context.value; if (!c || !canEdit.value || !['enabled', 'disabled'].includes(state.value) || !reason.value.trim()) throw new Error('请明确字典状态并填写原因。')
  const d = state.value === 'disabled' && c.policy ? { ...copy(c.policy.original.definition), enabled: false } : { ...copy(definition.value), enabled: true }
  if (!d.metrics.length || !d.precisions.length || !d.domains.length || d.metrics.some(x => !x.units.length)) throw new Error('请填写指标、精度、适用域，并选择真实单位。')
  await saveRuleReferences(c, { definition: d, reason: reason.value }, check); check(); reason.value = ''; history.value = null; units.value = []; await load(check); notice.value = '字典已保存；候选来源须单独绑定，规则尚未批准或生效。'
}) }
async function recover(close: boolean) { await run(async check => {
  if (!context.value || close && !permission.has('wms-warehouse:abandon')) throw new Error('当前权限不足。')
  const r = await recoverRuleReferences(context.value, close, check); check(); if (r.state !== 'UNKNOWN') { clear(); await load(check) }
  notice.value = r.state === 'UNKNOWN' ? '结果仍未知，请继续查询。' : r.state === 'COMMITTED' ? '已找回原操作并载入当前字典。' : '未提交原操作已关闭。'
}) }
async function readHistory() { await run(async check => { history.value = null; const v = historyVersion.value; if (!/^[1-9][0-9]*$/.test(v)) throw new Error('请输入准确历史版本。'); const r = value(await api.history(warehouse.value, v)); check(); if (r.original.warehouseId !== warehouse.value || r.original.tenantId !== context.value?.tenantId || r.reference.version !== v || r.reference.id !== context.value?.policy?.reference.id) throw new Error('历史原件不一致。'); history.value = r }) }
function reset() { generation++; busy.value = false; clear(); error.value = ''; notice.value = ''; if (authenticationCanRead()) void reload() }
watch(warehouse, reset); onMounted(() => { window.addEventListener(AUTH_EVENT, reset); void reload() }); onBeforeUnmount(() => { live = false; generation++; clear(); window.removeEventListener(AUTH_EVENT, reset) })
</script>
<style scoped>
.rule-references{max-width:1000px;margin:auto;padding:24px}.rule-references fieldset,.rule-references article,.rule-references section{border:1px solid #b5c4cf;border-radius:8px;padding:16px;margin:16px 0}.rule-references label{display:flex;flex-direction:column;gap:6px;margin:12px 0}.rule-references .choice{flex-direction:row}.rule-references input,.rule-references textarea,.rule-references select{padding:8px}.rule-references button{padding:8px 12px;margin:6px}.rule-references pre{white-space:pre-wrap;overflow-wrap:anywhere}[role=alert]{color:#a32626}
</style>
