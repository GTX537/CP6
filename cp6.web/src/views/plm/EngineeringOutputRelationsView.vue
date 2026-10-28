<template>
  <main class="relations-page" :aria-busy="busy">
    <header><div><h1>工程输出关系</h1><p>按完整制造版本固定输入、工序、产物和批准证据，再由独立专业人员确认。下游工程冻结和用途放行另行办理。</p></div><button :disabled="busy" @click="reload">刷新</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p><p v-if="!canRead">当前没有读取工程输出关系的权限。</p>
    <section v-if="pending"><h2>原操作结果待确认</h2><p>请查询原操作后继续。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy" @click="recover(true)">结束未提交操作</button></section>
    <section v-if="canRead"><form @submit.prevent="loadList(1)"><label>关系名称<input v-model="search" aria-label="关系搜索" /></label><button :disabled="busy">查找</button></form><button v-if="has('add')" :disabled="locked" @click="create">创建输出关系</button>
      <nav><button v-for="row in rows" :key="row.version.reference.id" :disabled="busy" @click="open(row.version.reference.id)">{{ row.version.original.body.name }} · v{{ row.version.reference.version }} · {{ state(row.control.state) }}</button></nav>
      <button :disabled="busy || page === 1" @click="loadList(page - 1)">上一页</button><button :disabled="busy || rows.length < 20" @click="loadList(page + 1)">下一页</button>
    </section>
    <section v-if="editing"><h2>{{ current ? '工作版本与专业状态' : '建立准确输出关系' }}</h2>
      <p v-if="current">{{ state(current.control.state) }} · {{ current.missing.length ? '当前尚不可采用' : '准确关系当前可供引用' }}</p>
      <ul v-if="current"><li v-for="gap in current.missing" :key="gap">{{ finding(gap) }} <details><summary>缺项位置</summary>{{ gap }}</details></li></ul>
      <details v-if="current?.downstreamMissing.length"><summary>仍需完成的下游制造资格</summary><ul><li v-for="gap in current.downstreamMissing" :key="gap">{{ finding(gap) }} <code>{{ gap }}</code></li></ul></details>
      <SourcePicker kind="manufacturing" label="制造版本" :model-value="body?.manufacturing ?? null" :revision-id="current?.version.original.revisionOrigin.id" :disabled="locked || !canEdit" @update:model-value="adoptManufacturing" />
      <form v-if="body && manufacturing" @submit.prevent="saveBody"><fieldset :disabled="locked || !canEdit"><label>输出关系名称<input v-model="body.name" aria-label="输出关系名称" required /></label>
        <p>制造版本 {{ manufacturing.reference.version }} · {{ manufacturing.body.name }}。保留全部输入及每个输出位置。</p>
        <h3>制造输入规格</h3><article v-for="row in body.inputs" :key="row.occurrenceId"><h4>{{ inputName(row.occurrenceId) }}</h4><SourcePicker v-model="row.specification" kind="quality" :label="`输入规格 ${inputName(row.occurrenceId)}`" :product-id="inputProduct(row.occurrenceId)" :disabled="locked || !canEdit" /></article>
        <h3>输出位置、规格和关系依据</h3><article v-for="row in body.outputs" :key="row.occurrenceId"><h4>{{ output(row.occurrenceId)?.name }} · {{ row.occurrenceId }}</h4><p>生成工序 {{ output(row.occurrenceId)?.generatingOperationOccurrenceId }}；数量 {{ output(row.occurrenceId)?.quantity ?? '按制造原件范围' }}，配比分母 {{ output(row.occurrenceId)?.denominator ?? '不适用' }}。</p>
          <SourcePicker v-model="row.specification" kind="quality" :label="`输出规格 ${row.occurrenceId}`" :product-id="output(row.occurrenceId)?.productRef?.id" :disabled="locked || !canEdit" />
          <DocumentPicker v-model="row.relationDocument" kind="document" :label="`关系依据 ${row.occurrenceId}`" :disabled="locked || !canEdit" />
          <DocumentPicker v-if="noInput(row.occurrenceId)" v-model="row.noInputDocument" kind="document" :label="`无输入生成依据 ${row.occurrenceId}`" :disabled="locked || !canEdit" />
        </article>
        <label>保存原因<textarea v-model="reason" aria-label="关系保存原因" required /></label><button>保存关系版本</button>
      </fieldset></form>
    </section>
    <section v-if="current"><h2>独立专业确认</h2><p v-if="dirty">存在未保存修改，请先保存或重新打开。</p><p v-if="current.qualification">确认人 {{ current.qualification.body.actorId }} · {{ current.qualification.body.recordedAt }} · {{ current.qualification.body.statement }}</p>
      <label>确认或撤回原因<textarea v-model="actionReason" aria-label="关系确认原因" :disabled="locked" /></label>
      <button v-if="has('qualify')" :disabled="locked || dirty || !current.policy || current.control.state === 'QUALIFIED' || current.version.original.authorId === scope?.actorId || !actionReason.trim()" @click="action('QUALIFY')">独立确认准确关系</button>
      <button v-if="has('withdraw')" :disabled="locked || current.control.state !== 'QUALIFIED' || !actionReason.trim()" @click="action('WITHDRAW')">撤回当前确认</button>
      <details v-if="has('review-policy')"><summary>专业角色与有效期</summary><button :disabled="busy" @click="loadRoles">读取专业角色</button><form @submit.prevent="savePolicy"><fieldset :disabled="locked"><label>专业角色<select v-model.number="policy.roleId" aria-label="关系专业角色" required><option :value="0">请选择</option><option v-for="role in roles" :key="role.roleId" :value="role.roleId">{{ role.roleName }}</option></select></label><label>生效时间<input v-model="effectiveFrom" aria-label="关系规则生效" type="datetime-local" required /></label><label>到期时间<input v-model="effectiveTo" aria-label="关系规则到期" type="datetime-local" required /></label><label>专业确认依据<textarea v-model="policy.basis" aria-label="关系规则依据" required /></label><label>规则修改原因<textarea v-model="policyReason" aria-label="关系规则原因" required /></label><button>保存专业规则</button></fieldset></form></details>
      <form @submit.prevent="readHistory"><label>历史版本<input v-model="historyVersion" aria-label="关系历史版本" pattern="[1-9][0-9]*" required /></label><button :disabled="busy">读取历史版本</button></form>
      <details v-if="history"><summary>历史关系与准确原件</summary><pre>{{ history.version.originalJson }}</pre></details>
      <form @submit.prevent="readDiff"><label>比较起始版本<input v-model="beforeVersion" aria-label="关系比较起始" pattern="[1-9][0-9]*" required /></label><label>比较结束版本<input v-model="afterVersion" aria-label="关系比较结束" pattern="[1-9][0-9]*" required /></label><button :disabled="busy">比较版本</button></form><details v-for="item in diff" :key="item.path"><summary>{{ item.path }}</summary><pre>{{ item.before }}</pre><pre>{{ item.after }}</pre></details>
      <details><summary>完整关系原件及独立摘要</summary><p>{{ current.version.original.wire.bodyDigest }}</p><pre>{{ current.version.original.wireOriginal }}</pre></details>
    </section>
  </main>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { outputRelationsApi, type OutputScope, type OutputView, type OutputBody, type OutputPolicy, type OutputAction } from '@/api/plm/engineeringOutputRelations'
import { manufacturingApi, type ManufacturingSnapshot } from '@/api/plm/engineeringManufacturing'
import { bomApi, type BomSnapshot } from '@/api/plm/engineeringBom'
import type { EngineeringReference } from '@/api/plm/engineeringProducts'
import { pendingOutput, outputResponse as result, saveOutput, resolveOutput, type PendingOutput } from '@/auth/outputRelationsRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
import SourcePicker from './EngineeringOutputSourcePicker.vue'
import DocumentPicker from '../mes/QualitySourcePicker.vue'
type Editable = Omit<OutputBody, 'inputs' | 'outputs'> & { inputs: { occurrenceId: string; specification: EngineeringReference | null }[]; outputs: { occurrenceId: string; specification: EngineeringReference | null; relationDocument: EngineeringReference | null; noInputDocument: EngineeringReference | null }[] }
const permissions = usePermissionStore(), has = (action: string) => permissions.has(`plm-engineering-output-relations:${action}`), canRead = computed(() => permissions.loaded && has('query'))
const busy = ref(false), error = ref(''), notice = ref(''), scope = ref<OutputScope | null>(null), pending = ref<PendingOutput | null>(null), locked = computed(() => busy.value || !!pending.value)
const rows = ref<OutputView[]>([]), search = ref(''), page = ref(1), current = ref<OutputView | null>(null), history = ref<OutputView | null>(null), diff = ref<{ path: string; before: unknown; after: unknown }[]>([])
const editing = ref(false), body = ref<Editable | null>(null), manufacturing = ref<ManufacturingSnapshot | null>(null), mbom = ref<BomSnapshot | null>(null), objectId = ref(''), reason = ref(''), actionReason = ref('')
const policy = ref<OutputPolicy>({ roleId: 0, effectiveFrom: '', effectiveTo: '', basis: '' }), roles = ref<{ roleId: number; roleName: string }[]>([]), effectiveFrom = ref(''), effectiveTo = ref(''), policyReason = ref('')
const historyVersion = ref('1'), beforeVersion = ref('1'), afterVersion = ref('1'), canEdit = computed(() => has(current.value ? 'edit' : 'add'))
const copy = <T,>(v: T): T => JSON.parse(JSON.stringify(v)), key = (r: EngineeringReference) => `${r.owner}:${r.id}:${r.version}:${r.digest}`
const dirty = computed(() => !!current.value && JSON.stringify(body.value) !== JSON.stringify(current.value.version.original.body))
const output = (id: string) => manufacturing.value?.body.outputs.find(x => x.outputOccurrenceId === id)
const noInput = (id: string) => { const v = output(id); return v && !v.inputOccurrenceIds.length && !v.fromOutputOccurrenceIds.length }
const inputName = (id: string) => { const v = mbom.value?.body.occurrences.find(x => x.occurrenceId === id); return v ? `${v.path} / ${v.name}` : id }
const inputProduct = (id: string) => mbom.value?.body.occurrences.find(x => x.occurrenceId === id)?.itemRef?.id
const state = (v: string) => ({ DRAFT: '草稿', QUALIFIED: '已独立确认', WITHDRAWN: '已撤回' }[v] ?? '状态待核对')
const finding = (v: string) => v.includes('QUALIFICATION_REQUIRED') ? '尚需独立专业确认。' : v.includes('WITHDRAWN') ? '当前确认已撤回。' : v.includes('SPEC') ? '规格来源、产品或厂区仍需核对。' : v.includes('DOCUMENT') ? '受控文档批准或内容仍需核对。' : v.includes('RESOURCE') || v.includes('TOOL') ? '设备、资源或工装的实际制造资格仍待完成。' : v.includes('CHANGED') || v.includes('AUTHORITY') ? '准确来源或当前授权已变化。' : '当前关系、政策或下游制造依据仍不满足要求。'
let live = true, epoch = 0
function clearEditor() { current.value = history.value = null; body.value = null; manufacturing.value = null; mbom.value = null; editing.value = false; objectId.value = reason.value = actionReason.value = ''; roles.value = []; diff.value = []; policy.value = { roleId: 0, effectiveFrom: '', effectiveTo: '', basis: '' }; effectiveFrom.value = effectiveTo.value = policyReason.value = '' }
function clearPrivate() { clearEditor(); rows.value = [] }
function sync() { pending.value = scope.value ? pendingOutput(scope.value) : null }
function verify(v: OutputView) { const o = v.version.original; if (!scope.value || o.tenantId !== scope.value.tenantId || o.environmentId !== scope.value.environmentId || o.evidenceMode !== scope.value.evidenceMode) throw new Error('来源上下文与当前身份不一致。') }
async function run(work: (check: () => void) => Promise<void>) {
  if (busy.value) return; const stamp = identityStamp(), generation = epoch
  const check = () => { requireIdentity(stamp); if (!live || generation !== epoch || !authenticationCanRead() || !canRead.value) throw new Error('当前身份或权限已变化。') }
  busy.value = true; error.value = notice.value = ''
  try { check(); await work(check); check() } catch (e: any) { try { check(); const status = e?.response?.status; if ([401, 403].includes(status)) clearPrivate(); error.value = [401, 403].includes(status) ? '当前权限或专业资格不允许此操作。' : status === 412 ? '关系或来源已变化，请查询原操作并重新核对。' : [409, 422, 503].includes(status) ? '当前关系、来源或专业状态不满足要求。' : e?.message || '结果未确认，请查询原操作。' } catch { /* obsolete identity */ } }
  finally { try { check(); sync() } catch { /* retain durable recovery identity */ } if (live && generation === epoch) busy.value = false }
}
async function list(target: number, check: () => void) { rows.value = []; const v = result(await outputRelationsApi.list(search.value, target)); check(); v.forEach(verify); rows.value = v; page.value = target }
async function reload() { if (!permissions.loaded) await permissions.loadMyActions(); await run(async check => { clearPrivate(); scope.value = null; const s = result(await outputRelationsApi.scope()); check(); scope.value = s; await list(1, check) }) }
async function loadList(target: number) { await run(check => list(target, check)) }
function localTime(value: string) { const d = new Date(value); return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16) }
async function sources(reference: EngineeringReference, check: () => void) { const m = result(await manufacturingApi.read(reference.id, reference.version)); check(); if (key(m.reference) !== key(reference) || m.tenantId !== scope.value?.tenantId) throw new Error('制造来源不一致。'); const b = result(await bomApi.read(m.body.mbomRef.id, m.body.mbomRef.version)); check(); if (key(b.reference) !== key(m.body.mbomRef)) throw new Error('制造输入来源不一致。'); manufacturing.value = m; mbom.value = b }
async function adopt(v: OutputView, check: () => void) { verify(v); clearEditor(); current.value = v; editing.value = true; objectId.value = v.version.reference.id; body.value = copy(v.version.original.body); afterVersion.value = v.version.reference.version; if (v.policy) { policy.value = copy(v.policy.body.input); effectiveFrom.value = localTime(policy.value.effectiveFrom); effectiveTo.value = localTime(policy.value.effectiveTo) } await sources(v.version.original.body.manufacturing, check) }
async function open(id: string) { await run(async check => { clearEditor(); const v = result(await outputRelationsApi.read(id)); check(); await adopt(v, check) }) }
function create() { if (locked.value || !has('add')) return; clearEditor(); editing.value = true; objectId.value = crypto.randomUUID() }
async function adoptManufacturing(reference: EngineeringReference | null) { if (!reference || locked.value || !canEdit.value) return; await run(async check => { await sources(reference, check); const b = mbom.value!, m = manufacturing.value!; body.value = { name: body.value?.name ?? m.body.name, manufacturing: copy(reference), inputs: b.body.occurrences.map(x => ({ occurrenceId: x.occurrenceId, specification: null })), outputs: m.body.outputs.map(x => ({ occurrenceId: x.outputOccurrenceId, specification: null, relationDocument: null, noInputDocument: null })) } }) }
const permission = (action: OutputAction) => ({ CREATE: 'add', REVISE: 'edit', POLICY: 'review-policy', QUALIFY: 'qualify', WITHDRAW: 'withdraw' }[action])
async function commit(action: OutputAction, why: string, extra: { body?: OutputBody; policy?: OutputPolicy } = {}) {
  const s = scope.value, id = objectId.value, etag = current.value?.etag ?? null; if (!s || locked.value || !why.trim() || !has(permission(action))) return
  await run(async check => { await saveOutput(s, id, action, () => { check(); if (!has(permission(action))) throw new Error('当前操作权限已变化。') }, operationId => outputRelationsApi.save({ operationId, relationId: id, action, expectedEtag: etag, reason: why, ...extra })); check(); const v = result(await outputRelationsApi.read(id)); check(); await adopt(v, check); await list(page.value, check); notice.value = '操作已保存。' })
}
async function saveBody() { const b = body.value; if (!b || b.inputs.some(x => !x.specification) || b.outputs.some(x => !x.specification || !x.relationDocument || (noInput(x.occurrenceId) && !x.noInputDocument))) { error.value = '请为每个位置选择准确规格和批准依据。'; return } await commit(current.value ? 'REVISE' : 'CREATE', reason.value, { body: copy(b) as OutputBody }) }
async function action(a: OutputAction) { if (a !== 'WITHDRAW' && dirty.value) return; await commit(a, actionReason.value) }
async function loadRoles() { await run(async check => { const v = result(await outputRelationsApi.roles()); check(); roles.value = v }) }
async function savePolicy() { try { const p = { ...copy(policy.value), effectiveFrom: new Date(effectiveFrom.value).toISOString(), effectiveTo: new Date(effectiveTo.value).toISOString() }; await commit('POLICY', policyReason.value, { policy: p }) } catch { error.value = '请选择有效的规则时间。' } }
async function readHistory() { const id = current.value?.version.reference.id; if (!id) return; await run(async check => { history.value = null; const v = result(await outputRelationsApi.read(id, historyVersion.value)); check(); verify(v); history.value = v }) }
async function readDiff() { const id = current.value?.version.reference.id; if (!id) return; await run(async check => { diff.value = []; const v = result(await outputRelationsApi.diff(id, beforeVersion.value, afterVersion.value)); check(); diff.value = v }) }
async function recover(close: boolean) { const s = scope.value; if (!s) return; await run(async check => { const v = await resolveOutput(s, close, check); check(); clearEditor(); await list(1, check); notice.value = v.state === 'COMMITTED' ? '原操作已提交，请重新打开关系。' : v.state === 'CLOSED' ? '未提交操作已结束。' : '结果仍未知，请继续查询。' }) }
function clear() { epoch++; busy.value = false; clearPrivate(); scope.value = null; pending.value = null; error.value = notice.value = '' }
function changed() { clear(); if (authenticationCanRead()) void reload() }
function storage(e: StorageEvent) { if (e.key?.startsWith('cp6_output_relations_operation_v1:')) { try { sync() } catch { error.value = '原操作记录异常，请保留记录。' } } }
watch(canRead, yes => { if (!yes) clear() }); onMounted(() => { window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', storage); void reload() })
onBeforeUnmount(() => { live = false; clear(); window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', storage) })
</script>
<style scoped>
.relations-page{display:grid;gap:18px;padding:24px;color:var(--el-text-color-primary)}header,nav{display:flex;gap:12px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}section,article{padding:18px;border:1px solid var(--el-border-color);border-radius:8px;background:var(--el-bg-color)}article{margin:12px 0}label{display:grid;gap:6px;margin:8px 0}input,select,textarea,button{font:inherit;padding:8px;max-width:100%;border:1px solid var(--el-border-color);border-radius:4px;background:var(--el-bg-color);color:inherit;box-sizing:border-box}button{margin:4px;cursor:pointer}button:disabled{opacity:.5;cursor:default}fieldset{border:0;padding:0}pre,p,li,code{white-space:pre-wrap;overflow-wrap:anywhere}[role=alert]{color:var(--el-color-danger)}:focus-visible{outline:2px solid var(--el-color-primary);outline-offset:2px}@media(max-width:600px){.relations-page{padding:12px}section,article{padding:12px}}
</style>
