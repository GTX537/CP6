<template>
  <main class="location-hierarchy">
    <h1>库位层级策略</h1><p>明确仓库允许的根层级和父子关系。结构检查通过后，创建库位仍须通过初始化隔离检查。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <button :disabled="busy" @click="reload">重新载入</button>
    <template v-if="context">
      <p>仓库 {{ context.warehouseCode }} · 策略 {{ context.version }} · 结构版本 {{ context.hierarchyRevision }}</p>
      <p>{{ states[context.compatibility] || '状态待核验' }}</p>
      <section v-if="pending"><p>有一笔策略提交结果待确认。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy || !canClose" @click="recover(true)">关闭未提交原操作</button></section>
      <form @submit.prevent="save"><fieldset :disabled="busy || !!pending || !canEdit"><legend>发布明确的层级策略</legend>
        <label>策略状态<select v-model="enabled" aria-label="策略状态" required><option disabled value="">请选择</option><option value="enabled">启用</option><option v-if="context.policy" value="disabled">停用</option></select></label>
        <fieldset><legend>允许的根层级</legend><label v-for="l in levels" :key="l.value" class="choice"><input v-model="roots" type="checkbox" :value="l.value" :aria-label="`根层级${l.value}`" />{{ l.name }}</label></fieldset>
        <fieldset><legend>允许的父子关系</legend><label v-for="e in edges" :key="e" class="choice"><input v-model="selectedEdges" type="checkbox" :value="e" :aria-label="`父子关系${e}`" />{{ edgeName(e) }}</label></fieldset>
        <label>操作原因<textarea v-model="reason" aria-label="操作原因" maxlength="1000" required /></label><button>保存策略</button>
      </fieldset></form>
      <form v-if="context.policy" @submit.prevent="readHistory"><label>历史版本<input v-model="historyVersion" aria-label="历史版本" pattern="[1-9][0-9]*" required /></label><button :disabled="busy">读取历史</button></form>
      <article v-if="history"><h2>历史 v{{ history.reference.version }}</h2><p>{{ history.original.reason }}</p><details><summary>查看保存原件</summary><pre>{{ history.originalJson }}</pre></details></article>
      <section v-if="context.policy?.original.enabled"><h2>拟建库位结构预检</h2><p>仅检查层级关系，当前不能创建或启用库位。</p>
        <form @submit.prevent="checkStructure"><fieldset :disabled="busy || !!pending">
          <label>父节点<select v-model="parent" aria-label="父节点" required><option disabled value="">请选择</option><option value="root">仓库根节点</option><option v-for="l in parents" :key="l.id" :value="l.id">{{ l.locationCd }} · {{ l.nameReadable ? l.locationName || '未命名' : '名称无权限' }}</option></select></label>
          <p>下列候选只包含当前有权读取的库位；未显示的节点不代表不存在。</p>
          <button type="button" :disabled="parentPage === 1 || !canReadLocations" @click="move(-1)">上一页库位</button><button type="button" :disabled="!moreParents || !canReadLocations" @click="move(1)">下一页库位</button>
          <label>新节点层级<select v-model="level" aria-label="新节点层级" required><option disabled value="">请选择</option><option v-for="l in levels" :key="l.value" :value="String(l.value)">{{ l.name }}</option></select></label><button>检查结构</button>
        </fieldset></form><p v-if="checked" role="status">当前结构检查通过。尚无创建许可；实际创建时必须重新核验。</p>
      </section>
    </template>
    <p><RouterLink to="/wms/warehouse">返回仓库查询</RouterLink></p>
  </main>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { locationHierarchyApi as api, type HierarchyContext, type HierarchyPolicy } from '@/api/wms/locationHierarchy'
import { warehouseOwnerApi } from '@/api/wms/warehouseOwner'
import { checkMasterPage, masterUuid, type LocationObservation } from '@/types/wms/warehouseOwner'
import { hierarchyKey, pendingHierarchy, saveHierarchy, recoverHierarchy } from '@/auth/locationHierarchyRecovery'
import { transferResponse as value } from '@/auth/transferEnvironmentRecovery'
import { identityStamp, requireIdentity, AUTH_EVENT } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const route = useRoute(), permission = usePermissionStore(), warehouse = computed(() => String(route.params.id || ''))
const canEdit = computed(() => permission.has('wms-warehouse:hierarchy-policy')), canClose = computed(() => permission.has('wms-warehouse:abandon')), canReadLocations = computed(() => permission.has('wms-location:query'))
const context = ref<HierarchyContext | null>(null), history = ref<HierarchyPolicy | null>(null), pending = ref<string | null>(null), parents = ref<LocationObservation[]>([])
const busy = ref(false), error = ref(''), notice = ref(''), enabled = ref(''), roots = ref<number[]>([]), selectedEdges = ref<string[]>([]), reason = ref(''), historyVersion = ref('1')
const parent = ref(''), level = ref(''), parentPage = ref(1), moreParents = ref(false), checked = ref(false)
const levels = [{ value: 1, name: '1 区' }, { value: 2, name: '2 通路' }, { value: 3, name: '3 架' }, { value: 4, name: '4 层' }, { value: 5, name: '5 格' }]
const edges = levels.flatMap(p => levels.filter(c => c.value > p.value).map(c => `${p.value}-${c.value}`))
const edgeName = (edge: string) => { const [p, c] = edge.split('-').map(Number); return `${levels.find(x => x.value === p)!.name} → ${levels.find(x => x.value === c)!.name}` }
const states: Record<string, string> = { POLICY_UNAVAILABLE: '尚未发布层级策略。', DISABLED: '策略已停用。', COMPATIBLE: '现有库位结构与策略兼容。', REQUIRES_REPAIR: '现有结构与策略不兼容，请先核对结构。' }
let live = true, generation = 0
function clear() { context.value = null; history.value = null; pending.value = null; parents.value = []; enabled.value = ''; roots.value = []; selectedEdges.value = []; reason.value = ''; parent.value = ''; level.value = ''; checked.value = false; parentPage.value = 1; moreParents.value = false }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return; const epoch = generation, stamp = identityStamp(), w = warehouse.value; busy.value = true; error.value = ''; notice.value = ''
  const check = () => { requireIdentity(stamp); if (!live || epoch !== generation || w !== warehouse.value || !authenticationCanRead()) throw new Error('当前身份或仓库已变化。') }
  try { check(); if (!masterUuid(w)) throw new Error('请从仓库查询进入层级策略。'); await action(check) }
  catch (e: any) { if (live && epoch === generation) { if ([401, 403, 404].includes(e?.response?.status)) clear(); error.value = e?.message || '当前操作未完成，请核对权限和版本。' } }
  finally { if (live && epoch === generation) { busy.value = false; if (context.value) { try { pending.value = pendingHierarchy(context.value) } catch (e: any) { pending.value = 'invalid'; error.value = e.message } } } }
}
async function loadParents(check: () => void) {
  parents.value = []; moreParents.value = false
  if (!canReadLocations.value) return
  const q = { page: parentPage.value, pageSize: 50 }, r = await warehouseOwnerApi.locations(warehouse.value, q); check()
  if (r.code !== 0) throw new Error('无法读取父节点候选。')
  const p = checkMasterPage(r.data, 'locations', q, warehouse.value); parents.value = p.items; moreParents.value = p.total > p.page * p.pageSize
}
async function load(check: () => void) {
  const s = value(await api.context(warehouse.value)); check(); hierarchyKey(s)
  if (s.warehouseId !== warehouse.value) throw new Error('返回仓库与当前选择不一致。')
  context.value = s; pending.value = pendingHierarchy(s); enabled.value = s.policy ? s.policy.original.enabled ? 'enabled' : 'disabled' : ''
  roots.value = [...(s.policy?.original.rootLevels || [])]; selectedEdges.value = s.policy?.original.edges.map(x => `${x.parentLevel}-${x.childLevel}`) || []
  if (s.policy?.original.enabled) await loadParents(check)
}
async function reload() { await run(async check => { clear(); if (!permission.loaded) await permission.loadMyActions(); check(); await load(check) }) }
async function save() { await run(async check => {
  checked.value = false
  if (!context.value || !canEdit.value || !roots.value.length || !['enabled', 'disabled'].includes(enabled.value) || !reason.value.trim()) throw new Error('请选择明确的策略状态、根层级并填写原因。')
  await saveHierarchy(context.value, { enabled: enabled.value === 'enabled', rootLevels: [...roots.value], edges: selectedEdges.value.map(x => { const [p, c] = x.split('-').map(Number); return { parentLevel: p!, childLevel: c! } }), reason: reason.value }, check)
  check(); reason.value = ''; history.value = null; await load(check); notice.value = '策略已保存。'
}) }
async function recover(close: boolean) { await run(async check => {
  if (!context.value || close && !canClose.value) throw new Error('当前操作权限不足。')
  const r = await recoverHierarchy(context.value, close, check); check()
  if (r.state !== 'UNKNOWN') { clear(); await load(check) }
  notice.value = r.state === 'COMMITTED' ? '已找回原操作。' : r.state === 'CLOSED' ? '未提交原操作已关闭。' : '结果仍未知，请继续查询。'
}) }
async function readHistory() { await run(async check => { history.value = null; const r = value(await api.history(warehouse.value, historyVersion.value)); check(); if (r.original.warehouseId !== warehouse.value) throw new Error('历史原件范围不一致。'); history.value = r }) }
async function move(delta: number) { await run(async check => { parentPage.value += delta; parent.value = ''; checked.value = false; await loadParents(check) }) }
async function checkStructure() { await run(async check => {
  checked.value = false
  const s = context.value; if (!s || !parent.value || !level.value) throw new Error('请选择父节点和新节点层级。')
  const p = parent.value === 'root' ? null : parent.value, l = Number(level.value)
  const r = value(await api.precheck(s.warehouseId, { expectedVersion: s.version, expectedHierarchyRevision: s.hierarchyRevision, parentLocationId: p, locationLevel: l })); check()
  if (r.warehouseId !== s.warehouseId || r.parentLocationId !== p || r.locationLevel !== l || r.policyRef?.id !== s.policy?.reference.id || r.policyRef.version !== s.version || r.policyRef.digest !== s.policy?.reference.digest
    || r.hierarchyRevision !== s.hierarchyRevision || r.creationGrant !== false || r.structureValid !== true || r.requiresFinalRecheck !== true || r.initializationFence !== 'UNAVAILABLE') throw new Error('无法确认本次结构检查结果。')
  checked.value = true
}) }
function reset() { generation++; busy.value = false; clear(); error.value = ''; notice.value = ''; if (authenticationCanRead()) void reload() }
watch([parent, level, enabled, roots, selectedEdges], () => { checked.value = false }, { deep: true }); watch(warehouse, reset)
onMounted(() => { window.addEventListener(AUTH_EVENT, reset); void reload() }); onBeforeUnmount(() => { live = false; generation++; clear(); window.removeEventListener(AUTH_EVENT, reset) })
</script>
<style scoped>
.location-hierarchy{max-width:900px;margin:auto;padding:24px}.location-hierarchy fieldset,.location-hierarchy section{padding:16px;border:1px solid #bac7d1;border-radius:8px;margin:14px 0}.location-hierarchy label{display:flex;flex-direction:column;gap:5px;margin:12px 0}.location-hierarchy .choice{display:inline-flex;flex-direction:row;margin:8px 16px 8px 0}.location-hierarchy button{padding:8px 14px;margin:6px}.location-hierarchy select,.location-hierarchy textarea,.location-hierarchy input:not([type=checkbox]){padding:8px}.location-hierarchy pre{white-space:pre-wrap;overflow-wrap:anywhere}[role=alert]{color:#a32626}
</style>
