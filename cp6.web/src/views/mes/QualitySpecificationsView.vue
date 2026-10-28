<template>
  <main class="quality-page" :aria-busy="busy">
    <header><div><h1>独立质量规格</h1><p>保存完整检验要求，由专业人员签署准确版本后供工程引用。</p></div><button :disabled="busy" @click="reload">刷新</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <section v-if="pending"><h2>原操作待确认</h2><p>请先确认上次操作，再继续修改。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy" @click="recover(true)">结束未提交操作</button></section>
    <section v-if="canRead"><h2>规格目录</h2><form @submit.prevent="loadList(1)"><label>规格搜索<input v-model="search" aria-label="规格搜索" :disabled="busy" /></label><button :disabled="busy">搜索</button></form>
      <button v-if="has('add')" :disabled="locked || !scope" @click="create">新建质量规格</button>
      <ul><li v-for="row in rows" :key="row.version.reference.id"><button :disabled="locked" @click="open(row.version.reference.id)">{{ row.version.body.name }} · v{{ row.version.reference.version }} · {{ state(row.control.state) }}</button></li></ul>
      <nav><button :disabled="busy || page === 1" @click="loadList(page - 1)">上一页</button><span>第 {{ page }} 页</span><button :disabled="busy || rows.length < 20" @click="loadList(page + 1)">下一页</button></nav>
    </section>
    <section v-if="body"><h2>{{ current ? `规格 v${current.version.reference.version}` : '新规格草稿' }}</h2>
      <p v-if="current">{{ state(current.control.state) }}；当前有效批准：{{ current.currentApproval ? `版本 ${current.currentApproval.body.specification.version}` : '无' }}。</p>
      <ul v-if="current?.missing.length"><li v-for="finding in current.missing" :key="finding">{{ missingText(finding) }}</li></ul>
      <form @submit.prevent="saveBody"><fieldset :disabled="locked || !canEdit || current?.control.state === 'IN_REVIEW'">
        <div class="grid"><label>规格名称<input v-model="body.name" aria-label="规格名称" maxlength="512" required /></label><label>检验阶段<select v-model="body.stage" aria-label="检验阶段"><option value="IQC">来料检验</option><option value="IPQC">过程检验</option><option value="FQC">最终检验</option><option value="TRIAL_INSPECTION">试制检验</option></select></label>
          <QualitySourcePicker v-model="body.product" kind="product" label="工程产品" :identity="current?.version.body.product.id" :disabled="locked || !canEdit" />
          <QualitySourcePicker v-model="body.site" kind="site" label="厂区" :identity="current?.version.body.site.id" :disabled="locked || !canEdit" />
          <label>专业用途说明（每行一项）<textarea v-model="purposeText" aria-label="专业用途说明" required /></label>
          <QualitySourcePicker v-model="body.completenessDocument" kind="document" label="完整性批准原文" :disabled="locked || !canEdit" />
          <label>要求完整性说明<textarea v-model="body.completenessStatement" aria-label="要求完整性说明" required /></label>
        </div><p>用途说明与完整性声明由专业人员核对。空要求清单也必须有完整性依据并独立批准。</p>
        <article v-for="(item, index) in body.requirements" :key="index"><h3>要求 {{ index + 1 }}</h3><div class="grid">
          <label>要求标识<input v-model="item.id" :aria-label="`要求标识 ${index + 1}`" required /></label>
          <label>质量特性<input v-model="item.characteristic" :aria-label="`质量特性 ${index + 1}`" required /></label>
          <label>测量或检查方法<textarea v-model="item.method" :aria-label="`检查方法 ${index + 1}`" required /></label>
          <label>判定定义<textarea v-model="item.definition" :aria-label="`判定定义 ${index + 1}`" required /></label>
          <QualitySourcePicker v-model="item.standardDocument" kind="document" :label="`标准原文 ${index + 1}`" :disabled="locked || !canEdit" />
          <QualitySourcePicker :model-value="item.unit" kind="unit" :label="`测量单位 ${index + 1}`" optional :disabled="locked || !canEdit" @update:model-value="setUnit(item, $event)" />
          <label>下限（精确数值）<input v-model="item.lowerLimit" :aria-label="`下限 ${index + 1}`" /></label><label>上限（精确数值）<input v-model="item.upperLimit" :aria-label="`上限 ${index + 1}`" /></label><label>目标值（精确数值）<input v-model="item.target" :aria-label="`目标值 ${index + 1}`" /></label>
          <label>适用性<select v-model="item.applicability" :aria-label="`适用性 ${index + 1}`" @change="item.applicabilityBasis = null"><option value="UNKNOWN">待确定</option><option value="APPLIES">适用</option><option value="NOT_APPLICABLE">不适用，有批准依据</option></select></label>
          <QualitySourcePicker v-if="item.applicability === 'NOT_APPLICABLE'" v-model="item.applicabilityBasis" kind="document" :label="`不适用依据 ${index + 1}`" :disabled="locked || !canEdit" />
          <label><input v-model="item.mandatory" type="checkbox" :aria-label="`强制要求 ${index + 1}`" />强制要求</label>
          <label><input v-model="item.waivable" type="checkbox" :aria-label="`允许豁免 ${index + 1}`" @change="item.waiverBasis = null" />允许豁免，有批准依据</label>
          <QualitySourcePicker v-if="item.waivable" v-model="item.waiverBasis" kind="document" :label="`豁免依据 ${index + 1}`" :disabled="locked || !canEdit" />
        </div><button type="button" @click="body.requirements.splice(index, 1)">移除此要求</button></article>
        <button type="button" @click="addRequirement">添加要求</button><label>保存原因<textarea v-model="reason" aria-label="保存原因" required /></label><button>保存工作版本</button>
      </fieldset></form>
    </section>
    <section v-if="current"><h2>专业审查与当前批准</h2><p v-if="dirty">存在未保存修改。请保存新版本或重新打开后再送审、签署。</p>
      <p v-if="current.packet">受审版本 {{ current.packet.body.specification.version }}，固定要求摘要 {{ current.packet.body.requirementsDigest }}。</p>
      <ul><li v-for="vote in current.votes" :key="vote.reference.id">{{ vote.body.actorId }} · {{ vote.body.approved ? '同意' : '退回' }} · {{ vote.body.comment }}</li></ul>
      <label>审查、签署或撤回原因<textarea v-model="reviewReason" aria-label="审查原因" :disabled="locked" /></label>
      <button v-if="has('review-submit')" :disabled="locked || dirty || !['DRAFT', 'RETURNED'].includes(current.control.state) || !current.policy || !!current.missing.length || !reviewReason.trim()" @click="action('SUBMIT')">提交专业审查</button>
      <button v-if="has('review-decide')" :disabled="locked || dirty || current.control.state !== 'IN_REVIEW' || current.packet?.body.authorId === scope?.actorId || !reviewReason.trim()" @click="action('APPROVE')">签署同意</button>
      <button v-if="has('review-decide')" :disabled="locked || dirty || current.control.state !== 'IN_REVIEW' || current.packet?.body.authorId === scope?.actorId || !reviewReason.trim()" @click="action('RETURN')">签署退回</button>
      <button v-if="has('withdraw')" :disabled="locked || !current.currentApproval || !reviewReason.trim()" @click="action('WITHDRAW')">撤回当前批准</button>
      <button :disabled="busy" @click="observeSource">检查当前可用性</button><p v-if="proof" role="status">{{ proof.availability === 'AVAILABLE' ? '此准确版本当前可供引用。' : '此准确版本当前不可采用。' }} 本次查看不锁定后续工程操作。</p>
      <details v-if="has('review-policy')"><summary>专业角色与批准人数</summary><button :disabled="busy" @click="loadRoles">读取专业角色</button>
        <form @submit.prevent="savePolicy"><fieldset :disabled="locked || current.control.state === 'IN_REVIEW'"><div v-for="(role, index) in policy.roles" :key="index" class="grid"><label>专业要求标识<input v-model="role.code" :aria-label="`专业要求标识 ${index + 1}`" required /></label><label>专业角色<select v-model.number="role.roleId" :aria-label="`专业角色 ${index + 1}`" required><option :value="0">请选择</option><option v-for="item in roles" :key="item.roleId" :value="item.roleId">{{ item.roleName }}</option></select></label><label>独立批准人数<input v-model.number="role.minimumApprovals" type="number" min="1" max="100" required /></label><button type="button" @click="policy.roles.splice(index, 1)">移除角色要求</button></div>
          <button type="button" @click="policy.roles.push({ code: '', roleId: 0, minimumApprovals: 1 })">添加角色要求</button><label>规则状态<select v-model="policy.state"><option value="ENABLED">启用</option><option value="DISABLED">停用</option></select></label>
          <label>生效时间<input v-model="effectiveFrom" type="datetime-local" required /></label><label>到期时间<input v-model="effectiveTo" type="datetime-local" required /></label><label>规则修改原因<textarea v-model="policyReason" required /></label><button :disabled="!policy.roles.length">保存专业规则</button>
        </fieldset></form>
      </details>
      <form @submit.prevent="readHistory"><label>历史版本<input v-model="historyVersion" aria-label="历史版本" inputmode="numeric" pattern="[1-9][0-9]*" required /></label><button :disabled="busy">读取历史版本</button></form>
      <article v-if="history"><h3>{{ history.version.body.name }} · v{{ history.version.reference.version }}</h3><p>{{ history.version.body.completenessStatement }}</p><ul><li v-for="item in history.version.body.requirements" :key="item.id">{{ item.characteristic }}：{{ item.method }}；{{ item.definition }}；下限 {{ item.lowerLimit ?? '未设置' }}，上限 {{ item.upperLimit ?? '未设置' }}。{{ item.mandatory ? '强制' : '非强制' }}，{{ item.waivable ? '有豁免依据' : '不可豁免' }}。</li></ul></article>
    </section>
  </main>
</template>
<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import { qualityApi, type QualityScope, type QualityView, type QualityBody, type QualityRequirement, type QualityPolicy, type QualityAction, type QualityProof } from '@/api/mes/qualitySpecification'
import type { EngineeringReference } from '@/api/plm/engineeringProducts'
import { pendingQuality, qualityResponse as result, saveQuality, resolveQuality, type PendingQuality } from '@/auth/qualityRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
import QualitySourcePicker from './QualitySourcePicker.vue'
type EditableRequirement = Omit<QualityRequirement, 'standardDocument'> & { standardDocument: EngineeringReference | null }
type EditableBody = Omit<QualityBody, 'product' | 'site' | 'completenessDocument' | 'requirements'> & { product: EngineeringReference | null; site: EngineeringReference | null; completenessDocument: EngineeringReference | null; requirements: EditableRequirement[] }
const permissions = usePermissionStore(), has = (action: string) => permissions.has(`quality-specification:${action}`), canRead = computed(() => permissions.loaded && has('query'))
const busy = ref(false), error = ref(''), notice = ref(''), scope = ref<QualityScope | null>(null), pending = ref<PendingQuality | null>(null), locked = computed(() => busy.value || !!pending.value)
const rows = ref<QualityView[]>([]), search = ref(''), page = ref(1), current = ref<QualityView | null>(null), history = ref<QualityView | null>(null), proof = ref<QualityProof | null>(null)
const body = ref<EditableBody | null>(null), objectId = ref(''), reason = ref(''), reviewReason = ref(''), purposeText = ref(''), historyVersion = ref('1')
const policy = ref<QualityPolicy>({ state: 'ENABLED', roles: [], effectiveFrom: '', effectiveTo: '' }), roles = ref<{ roleId: number; roleName: string }[]>([]), effectiveFrom = ref(''), effectiveTo = ref(''), policyReason = ref('')
const canEdit = computed(() => has(current.value ? 'edit' : 'add'))
const copy = <T,>(x: T): T => JSON.parse(JSON.stringify(x))
const normalized = () => body.value && ({ ...copy(body.value), purposes: purposeText.value.split('\n').map(x => x.trim()).filter(Boolean), requirements: body.value.requirements.map(x => ({ ...copy(x), lowerLimit: x.lowerLimit === '' ? null : x.lowerLimit, upperLimit: x.upperLimit === '' ? null : x.upperLimit, target: x.target === '' ? null : x.target })) })
const dirty = computed(() => !!current.value && JSON.stringify(normalized()) !== JSON.stringify(current.value.version.body))
let epoch = 0, live = true
const state = (s: string) => ({ DRAFT: '草稿', IN_REVIEW: '审查中', APPROVED: '已批准', RETURNED: '已退回', WITHDRAWN: '已撤回' }[s] ?? '状态待确认')
const missingText = (s: string) => s.includes('APPLICABILITY_UNKNOWN') ? '有要求的适用性尚未确定。' : s.includes('DOCUMENT') ? '原文批准或内容当前不可用。' : s.includes('WITHDRAWN') ? '质量规格批准已撤回。' : '产品、厂区、单位或要求来源已变化，请核对准确版本。'
function clearEditor() { current.value = history.value = null; body.value = null; proof.value = null; objectId.value = reason.value = reviewReason.value = purposeText.value = ''; roles.value = []; policy.value = { state: 'ENABLED', roles: [], effectiveFrom: '', effectiveTo: '' }; effectiveFrom.value = effectiveTo.value = policyReason.value = '' }
function clearPrivate() { clearEditor(); rows.value = [] }
function syncPending() { pending.value = scope.value ? pendingQuality(scope.value) : null }
function verify(v: QualityView) { if (!scope.value || v.version.tenantId !== scope.value.tenantId || v.version.environmentId !== scope.value.environmentId || v.version.evidenceMode !== scope.value.evidenceMode) throw new Error('规格来源与当前上下文不一致。') }
async function run(work: (check: () => void) => Promise<void>) {
  if (busy.value) return; const stamp = identityStamp(), generation = epoch
  const check = () => { requireIdentity(stamp); if (!live || generation !== epoch || !authenticationCanRead() || !canRead.value) throw new Error('当前身份或权限已变化。') }
  busy.value = true; error.value = notice.value = ''
  try { check(); await work(check); check() }
  catch (e: any) { try { check(); const status = e?.response?.status; if ([401, 403].includes(status)) clearPrivate(); error.value = [401, 403].includes(status) ? '当前权限或专业资格不允许此操作。' : status === 412 ? '规格或来源已变化，请查询原操作并刷新。' : [409, 422, 503].includes(status) ? '当前来源、规则或审查状态不满足要求，请核对后继续。' : e?.message || '结果未确认，请查询原操作。' } catch { /* Never disclose a stale identity response. */ } }
  finally { try { check(); syncPending() } catch { /* Keep durable pending record. */ } if (live && epoch === generation) busy.value = false }
}
async function list(target: number, check: () => void) { rows.value = []; const values = result(await qualityApi.list(search.value, target)); check(); values.forEach(verify); rows.value = values; page.value = target }
async function reload() { if (!permissions.loaded) await permissions.loadMyActions(); await run(async check => { clearPrivate(); scope.value = null; const s = result(await qualityApi.scope()); check(); scope.value = s; await list(1, check) }) }
async function loadList(target: number) { await run(check => list(target, check)) }
function localTime(value: string) { const d = new Date(value); return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16) }
function adopt(v: QualityView) { verify(v); clearEditor(); current.value = v; objectId.value = v.version.reference.id; body.value = copy(v.version.body); purposeText.value = v.version.body.purposes.join('\n'); if (v.policy) { policy.value = copy(v.policy.body.input); effectiveFrom.value = localTime(policy.value.effectiveFrom); effectiveTo.value = localTime(policy.value.effectiveTo) } }
async function open(id: string) { await run(async check => { clearEditor(); const v = result(await qualityApi.read(id)); check(); adopt(v) }) }
function create() { if (locked.value || !has('add')) return; clearEditor(); objectId.value = crypto.randomUUID(); body.value = { name: '', product: null, site: null, stage: 'IQC', purposes: [], completenessDocument: null, completenessStatement: '', requirements: [] } }
function addRequirement() { body.value?.requirements.push({ id: '', characteristic: '', method: '', definition: '', standardDocument: null, unit: null, lowerLimit: null, upperLimit: null, target: null, applicability: 'UNKNOWN', applicabilityBasis: null, mandatory: true, waivable: false, waiverBasis: null }) }
function setUnit(item: EditableRequirement, value: EngineeringReference | null) { if (value === null) item.unit = null; else if (value.owner === 'Main.Erp.UomProfile.v1') item.unit = { ...value, owner: 'Main.Erp.UomProfile.v1' } }
const permission = (a: QualityAction) => ({ CREATE: 'add', REVISE: 'edit', POLICY: 'review-policy', SUBMIT: 'review-submit', APPROVE: 'review-decide', RETURN: 'review-decide', WITHDRAW: 'withdraw' }[a])
async function commit(action: QualityAction, why: string, extra: { body?: QualityBody; policy?: QualityPolicy } = {}) {
  const s = scope.value, id = objectId.value, etag = current.value?.etag ?? null; if (!s || locked.value || !why.trim() || !has(permission(action))) return
  await run(async check => { await saveQuality(s, id, action, () => { check(); if (!has(permission(action))) throw new Error('当前操作权限已变化。') }, operationId => qualityApi.save({ operationId, specificationId: id, action, expectedEtag: etag, reason: why, ...extra })); check(); const v = result(await qualityApi.read(id)); check(); adopt(v); await list(page.value, check); notice.value = '操作已保存。' })
}
async function saveBody() { const value = normalized(); if (!value?.product || !value.site || !value.completenessDocument || value.requirements.some(x => !x.standardDocument)) { error.value = '请从当前来源中选择产品、厂区和完整批准原文。'; return } await commit(current.value ? 'REVISE' : 'CREATE', reason.value, { body: value as QualityBody }) }
async function action(a: QualityAction) { if (a !== 'WITHDRAW' && dirty.value) return; await commit(a, reviewReason.value) }
async function loadRoles() { await run(async check => { const r = result(await qualityApi.roles()); check(); roles.value = r }) }
async function savePolicy() { try { const p = copy(policy.value); p.effectiveFrom = new Date(effectiveFrom.value).toISOString(); p.effectiveTo = new Date(effectiveTo.value).toISOString(); await commit('POLICY', policyReason.value, { policy: p }) } catch { error.value = '请选择有效的专业规则时间。' } }
async function readHistory() { const id = current.value?.version.reference.id; if (!id) return; await run(async check => { history.value = null; const v = result(await qualityApi.read(id, historyVersion.value)); check(); verify(v); history.value = v }) }
async function observeSource() { const reference = current.value?.version.reference; if (!reference) return; await run(async check => { proof.value = null; const p = result(await qualityApi.source(reference)); check(); if (p.specification.owner !== reference.owner || p.specification.id !== reference.id || p.specification.version !== reference.version || p.specification.digest !== reference.digest || p.observation !== 'READ_ONLY_OBSERVATION') throw new Error('规格来源结果与当前版本不一致。'); proof.value = p }) }
async function recover(close: boolean) { const s = scope.value; if (!s) return; await run(async check => { const value = await resolveQuality(s, close, check); check(); clearEditor(); await list(1, check); notice.value = value.state === 'COMMITTED' ? '原操作已提交，请重新打开规格。' : value.state === 'CLOSED' ? '未提交操作已结束。' : '原结果仍未知，请继续查询。' }) }
function clear() { epoch++; busy.value = false; clearPrivate(); scope.value = null; pending.value = null; error.value = notice.value = '' }
function changed() { clear(); if (authenticationCanRead()) void reload() }
function storage(e: StorageEvent) { if (e.key?.startsWith('cp6_quality_operation_v1:')) { try { syncPending() } catch { error.value = '原操作记录异常，请保留记录。' } } }
watch(canRead, ok => { if (!ok) clear() }); onMounted(() => { window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', storage); void reload() })
onBeforeUnmount(() => { live = false; clear(); window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', storage) })
</script>
<style scoped>
.quality-page{display:grid;gap:18px;padding:24px;color:var(--el-text-color-primary)}header,nav{display:flex;gap:12px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}section,article{padding:18px;border:1px solid var(--el-border-color);border-radius:8px;background:var(--el-bg-color)}article{margin:12px 0}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(230px,1fr));gap:12px}label{display:grid;gap:6px;margin:8px 0}input,select,textarea,button{font:inherit;padding:8px;border:1px solid var(--el-border-color);border-radius:4px;color:inherit;background:var(--el-bg-color);max-width:100%;box-sizing:border-box}button{cursor:pointer;margin:4px}button:disabled{opacity:.5;cursor:default}fieldset{border:0;padding:0;width:100%}p,li{overflow-wrap:anywhere}[role=alert]{color:var(--el-color-danger)}:focus-visible{outline:2px solid var(--el-color-primary);outline-offset:2px}@media(max-width:600px){.quality-page{padding:12px}section,article{padding:12px}}
</style>
