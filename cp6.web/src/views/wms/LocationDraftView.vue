<template>
  <main class="location-drafts">
    <h1>库位草稿</h1><p>保存仓库内的库位身份和父子关系。草稿尚未启用，不能用于库存或作业；容量与预占数量尚未核定。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <button :disabled="busy" @click="reload">重新载入</button>
    <section v-if="pending"><p>有一笔创建结果待确认。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy || !canClose" @click="recover(true)">关闭未提交原操作</button></section>
    <form v-if="context" @submit.prevent="save">
      <fieldset :disabled="busy || !!pending || !canAdd"><legend>新建库位草稿</legend>
        <p>层级策略版本 {{ context.policy.reference.version }} · 仓库结构版本 {{ context.hierarchyRevision }}</p>
        <label>归属部门<select v-model="department" aria-label="归属部门" :required="!context.creation.allowsUnbound"><option v-if="context.creation.allowsUnbound" value="">不绑定部门</option><option v-else disabled value="">请选择</option><option v-for="d in context.creation.departments" :key="d.id" :value="d.id">{{ d.code }} · {{ d.name }}</option></select></label>
        <label>库位编码<input v-model="code" aria-label="库位编码" maxlength="30" required /></label>
        <label>库位名称<input v-model="name" aria-label="库位名称" maxlength="100" /></label>
        <label>层级<select v-model="level" aria-label="层级" required><option disabled value="">请选择</option><option v-for="n in 5" :key="n" :value="String(n)">{{ n }}</option></select></label>
        <label>父节点<select v-model="parent" aria-label="父节点" required><option disabled value="unset">请选择</option><option v-if="rootAllowed" value="root">仓库直属根节点</option><option v-for="r in parents" :key="r.id" :value="r.id">{{ r.code }} · 第 {{ r.level }} 级草稿</option></select></label>
        <p>父节点只列出当前页有权读取且符合策略的草稿。可翻页选择；当前列表不证明整个仓库没有其他节点。</p>
        <p>条码将在扫描来源核验完成后开放。保存不会启用库位。</p><button>保存草稿</button>
      </fieldset>
    </form>
    <h2>已保存草稿</h2><p v-if="!rows.length">当前页没有可读取的库位草稿。</p>
    <table v-else><thead><tr><th>编码</th><th>名称</th><th>层级</th><th>状态</th><th>业务版本</th></tr></thead><tbody><tr v-for="r in rows" :key="r.id"><td>{{ r.code }}</td><td>{{ r.canReadName ? r.name ?? '未设置' : '无读取权限' }}</td><td>{{ r.level }}</td><td>草稿 · 未启用</td><td>{{ r.businessRevision }}</td></tr></tbody></table>
    <button :disabled="busy || page === 1" @click="move(-1)">上一页</button><span>第 {{ page }} 页</span><button :disabled="busy || rows.length < 50" @click="move(1)">下一页</button>
    <p><RouterLink :to="`/wms/warehouse/${warehouse}/hierarchy`">库位层级策略</RouterLink> · <RouterLink to="/wms/warehouse-drafts">仓库草稿</RouterLink></p>
  </main>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { locationDraftApi as api, type LocationDraftContext, type LocationDraftScope, type LocationDraftView } from '@/api/wms/locationDraft'
import { createLocationDraft, recoverLocationDraft, pendingLocationDraft, locationDraftKey } from '@/auth/locationDraftRecovery'
import { transferResponse as value } from '@/auth/transferEnvironmentRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const route = useRoute(), warehouse = computed(() => String(route.params.id || ''))
const permissions = usePermissionStore(), canAdd = computed(() => permissions.has('wms-location:add')), canClose = computed(() => permissions.has('wms-location:abandon'))
const scope = ref<LocationDraftScope | null>(null), context = ref<LocationDraftContext | null>(null), rows = ref<LocationDraftView[]>([])
const pending = ref<string | null>(null), busy = ref(false), page = ref(1), error = ref(''), notice = ref('')
const department = ref(''), code = ref(''), name = ref(''), level = ref(''), parent = ref('unset')
const rootAllowed = computed(() => context.value?.policy.original.rootLevels.includes(Number(level.value)) ?? false)
const parents = computed(() => rows.value.filter(r => context.value?.policy.original.edges.some(e => e.parentLevel === r.level && e.childLevel === Number(level.value))))
watch(level, () => { parent.value = 'unset' })
let live = true, generation = 0
function fields() { department.value = ''; code.value = ''; name.value = ''; level.value = ''; parent.value = 'unset' }
function clear() { scope.value = null; context.value = null; rows.value = []; pending.value = null; fields() }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return; const epoch = generation, stamp = identityStamp(), wh = warehouse.value; busy.value = true; error.value = ''; notice.value = ''
  const check = () => { requireIdentity(stamp); if (!live || epoch !== generation || wh !== warehouse.value || !authenticationCanRead()) throw new Error('当前身份或仓库已变化。') }
  try { check(); await action(check) }
  catch (e: any) { if (live && epoch === generation) { if ([401, 403, 404].includes(e?.response?.status)) clear(); error.value = e?.message || '操作未完成，请核对权限和来源。' } }
  finally { if (live && epoch === generation) { busy.value = false; if (scope.value) { try { pending.value = pendingLocationDraft(scope.value) } catch (e: any) { error.value = e.message; pending.value = 'invalid' } } } }
}
async function load(check: () => void) {
  const s = value(await api.scope(warehouse.value)); check(); locationDraftKey(s); if (s.warehouseId !== warehouse.value) throw new Error('返回仓库不一致。'); scope.value = s; pending.value = pendingLocationDraft(s)
  const list = value(await api.list(warehouse.value, page.value)); check(); rows.value = list; context.value = null
  if (canAdd.value && !pending.value) { const c = value(await api.context(warehouse.value)); check(); if (locationDraftKey(c.scope) !== locationDraftKey(s) || c.creation.tenantId !== s.tenantId || c.creation.actorId !== s.actorId) throw new Error('当前身份已变化。'); context.value = c }
}
async function reload() { await run(async check => { clear(); if (!permissions.loaded) await permissions.loadMyActions(); check(); await load(check) }) }
async function save() { await run(async check => {
  if (!context.value || !canAdd.value || !code.value.trim() || !level.value || parent.value === 'unset' || (parent.value === 'root' ? !rootAllowed.value : !parents.value.some(x => x.id === parent.value))) throw new Error('请选择编码、层级及符合策略的父节点。')
  await createLocationDraft(context.value, { locationCode: code.value, locationName: name.value || null, departmentId: department.value || null, parentLocationId: parent.value === 'root' ? null : parent.value, locationLevel: Number(level.value) }, check)
  check(); fields(); page.value = 1; await load(check); notice.value = '库位草稿已保存，尚未启用。'
}) }
async function recover(close: boolean) { await run(async check => {
  if (!scope.value || close && !canClose.value) throw new Error('当前操作权限不足。')
  const r = await recoverLocationDraft(scope.value, close, check); check(); if (r.state !== 'UNKNOWN') { fields(); await load(check) }
  notice.value = r.state === 'COMMITTED' ? '已找回原库位草稿。' : r.state === 'CLOSED' ? '未提交原操作已关闭。' : '结果仍未知，请继续查询。'
}) }
async function move(delta: number) { await run(async check => { page.value += delta; parent.value = 'unset'; rows.value = []; const r = value(await api.list(warehouse.value, page.value)); check(); rows.value = r }) }
function reset() { generation++; busy.value = false; page.value = 1; clear(); error.value = ''; notice.value = ''; if (authenticationCanRead()) void reload() }
watch(warehouse, reset)
onMounted(() => { window.addEventListener(AUTH_EVENT, reset); void reload() })
onBeforeUnmount(() => { live = false; generation++; clear(); window.removeEventListener(AUTH_EVENT, reset) })
</script>
<style scoped>
.location-drafts{max-width:900px;padding:24px;margin:auto}.location-drafts label{display:flex;flex-direction:column;gap:5px;margin:12px 0}.location-drafts input,.location-drafts select{padding:8px}.location-drafts fieldset,.location-drafts section{padding:16px;border:1px solid #bbc8d2;border-radius:8px;margin:16px 0}.location-drafts button{margin:6px;padding:8px 14px}.location-drafts table{width:100%;border-collapse:collapse}.location-drafts th,.location-drafts td{text-align:left;padding:8px;border-bottom:1px solid #ccd5dc}[role=alert]{color:#a32626}
</style>
