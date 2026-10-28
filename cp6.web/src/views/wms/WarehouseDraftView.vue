<template>
  <main class="warehouse-drafts">
    <h1>仓库草稿</h1><p>保存仓库身份和站点关系。草稿尚未启用，不能用于库存、入出库或库位作业。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <button :disabled="busy" @click="reload">重新载入</button>
    <section v-if="pending"><p>有一笔创建结果待确认。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy || !canClose" @click="recover(true)">关闭未提交原操作</button></section>
    <form v-if="context" @submit.prevent="save">
      <fieldset :disabled="busy || !!pending || !canAdd"><legend>新建仓库草稿</legend>
        <label>物理站点<select v-model="site" aria-label="物理站点" required><option disabled value="">请选择</option><option v-for="s in context.sites" :key="s.reference.id" :value="s.reference.id">{{ s.code }} · {{ s.name }}</option></select></label>
        <label>仓库类型<select v-model="kind" aria-label="仓库类型" required><option disabled value="">请选择</option><option v-for="t in context.types" :key="t.code" :value="String(t.code)">{{ t.name }}</option></select></label>
        <label>归属部门<select v-model="department" aria-label="归属部门" :required="!context.creation.allowsUnbound"><option v-if="context.creation.allowsUnbound" value="">不绑定部门</option><option v-else disabled value="">请选择</option><option v-for="d in context.creation.departments" :key="d.id" :value="d.id">{{ d.code }} · {{ d.name }}</option></select></label>
        <label>仓库编码<input v-model="code" aria-label="仓库编码" maxlength="10" required /></label>
        <label>仓库名称<input v-model="name" aria-label="仓库名称" maxlength="100" required /></label>
        <label>地址<input v-model="address" aria-label="地址" maxlength="200" /></label><label>备注<textarea v-model="remarks" aria-label="备注" maxlength="500" /></label>
        <p>据点与负责人关系将在来源核验完成后开放。</p><button>保存草稿</button>
      </fieldset>
    </form>
    <h2>已保存草稿</h2><p v-if="!rows.length">当前页没有可读取的仓库草稿。</p>
    <table v-else><thead><tr><th>编码</th><th>名称</th><th>状态</th><th>业务版本</th></tr></thead><tbody><tr v-for="r in rows" :key="r.warehouse.id"><td>{{ r.warehouse.warehouseCd }}</td><td>{{ r.warehouse.warehouseName ?? '无读取权限' }}</td><td>草稿 · 未启用</td><td>{{ r.businessRevision }}</td></tr></tbody></table>
    <button :disabled="busy || page === 1" @click="move(-1)">上一页</button><span>第 {{ page }} 页</span><button :disabled="busy || rows.length < 50" @click="move(1)">下一页</button>
    <p><RouterLink to="/wms/warehouse">进入仓库查询与名称维护</RouterLink></p>
  </main>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { warehouseDraftApi as api, type DraftContext, type DraftScope, type DraftView } from '@/api/wms/warehouseDraft'
import { createDraft, recoverDraft, pendingDraft, draftKey } from '@/auth/warehouseDraftRecovery'
import { transferResponse as value } from '@/auth/transferEnvironmentRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const permissions = usePermissionStore(), canAdd = computed(() => permissions.has('wms-warehouse:add')), canClose = computed(() => permissions.has('wms-warehouse:abandon'))
const scope = ref<DraftScope | null>(null), context = ref<DraftContext | null>(null), rows = ref<DraftView[]>([])
const pending = ref<string | null>(null), busy = ref(false), page = ref(1), error = ref(''), notice = ref('')
const site = ref(''), kind = ref(''), department = ref(''), code = ref(''), name = ref(''), address = ref(''), remarks = ref('')
let live = true, generation = 0
function fields() { site.value = ''; kind.value = ''; department.value = ''; code.value = ''; name.value = ''; address.value = ''; remarks.value = '' }
function clear() { scope.value = null; context.value = null; rows.value = []; pending.value = null; fields() }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return; const epoch = generation, stamp = identityStamp(); busy.value = true; error.value = ''; notice.value = ''
  const check = () => { requireIdentity(stamp); if (!live || epoch !== generation || !authenticationCanRead()) throw new Error('当前身份已变化。') }
  try { check(); await action(check) }
  catch (e: any) { if (live && epoch === generation) { if ([401, 403].includes(e?.response?.status)) clear(); error.value = e?.message || '操作未完成，请核对权限和来源。' } }
  finally { if (live && epoch === generation) { busy.value = false; if (scope.value) { try { pending.value = pendingDraft(scope.value) } catch (e: any) { error.value = e.message; pending.value = 'invalid' } } } }
}
async function load(check: () => void) {
  const s = value(await api.scope()); check(); draftKey(s); scope.value = s; pending.value = pendingDraft(s)
  const list = value(await api.list(page.value)); check(); rows.value = list
  context.value = null
  if (canAdd.value && !pending.value) { const c = value(await api.context()); check(); if (draftKey(c.creation) !== draftKey(s)) throw new Error('当前身份已变化。'); context.value = c }
}
async function reload() { await run(async check => { clear(); if (!permissions.loaded) await permissions.loadMyActions(); check(); await load(check) }) }
async function save() { await run(async check => {
  if (!context.value || !canAdd.value || !site.value || !kind.value || !code.value.trim() || !name.value.trim()) throw new Error('请填写站点、类型、编码和名称。')
  await createDraft(context.value, { siteId: site.value, warehouseType: Number(kind.value), warehouseCode: code.value, warehouseName: name.value, departmentId: department.value || null, address: address.value || null, remarks: remarks.value || null, baseCode: null, managerCode: null }, check)
  check(); fields(); page.value = 1; await load(check); notice.value = '仓库草稿已保存，尚未启用。'
}) }
async function recover(close: boolean) { await run(async check => {
  if (!scope.value || close && !canClose.value) throw new Error('当前操作权限不足。')
  const r = await recoverDraft(scope.value, close, check); check()
  if (r.state !== 'UNKNOWN') { fields(); await load(check) }
  notice.value = r.state === 'COMMITTED' ? '已找回原仓库草稿。' : r.state === 'CLOSED' ? '未提交原操作已关闭。' : '结果仍未知，请继续查询。'
}) }
async function move(delta: number) { await run(async check => { page.value += delta; rows.value = []; const r = value(await api.list(page.value)); check(); rows.value = r }) }
function reset() { generation++; busy.value = false; page.value = 1; clear(); error.value = ''; notice.value = ''; if (authenticationCanRead()) void reload() }
onMounted(() => { window.addEventListener(AUTH_EVENT, reset); void reload() })
onBeforeUnmount(() => { live = false; generation++; clear(); window.removeEventListener(AUTH_EVENT, reset) })
</script>
<style scoped>
.warehouse-drafts{max-width:900px;padding:24px;margin:auto}.warehouse-drafts label{display:flex;flex-direction:column;gap:5px;margin:12px 0}.warehouse-drafts input,.warehouse-drafts select,.warehouse-drafts textarea{padding:8px}.warehouse-drafts fieldset,.warehouse-drafts section{padding:16px;border:1px solid #bbc8d2;border-radius:8px;margin:16px 0}.warehouse-drafts button{margin:6px;padding:8px 14px}.warehouse-drafts table{width:100%;border-collapse:collapse}.warehouse-drafts th,.warehouse-drafts td{text-align:left;padding:8px;border-bottom:1px solid #ccd5dc}[role=alert]{color:#a32626}
</style>
