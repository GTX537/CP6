<template>
  <section v-if="selected" class="association" :aria-busy="busy">
    <h2>工程技术版本关联</h2>
    <p>核对准确技术版本与这件实物的对应关系。双方确认后仍需另行取得技术批准、维护资格和生产使用许可。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>关联操作结果尚未确认。</p><button :disabled="busy" @click="recover(false)">查询原关联操作</button><button :disabled="busy" @click="recover(true)">结束未提交关联操作</button></div>
    <button :disabled="busy" @click="load(1)">读取当前关联</button>
    <ul><li v-for="row in rows" :key="row.subject.proposalId"><button :disabled="busy || !!pending" @click="open(row.subject.proposalId)">{{ row.subject.baseCode }} / {{ row.subject.number }} · Rev {{ row.subject.businessRevision }} · 技术版本 {{ row.subject.technical.version }}</button> {{ state(row.state) }}</li></ul>
    <nav v-if="loaded"><button :disabled="busy || page === 1" @click="load(page - 1)">关联上一页</button><span>第 {{ page }} 页</span><button :disabled="busy || rows.length < 20" @click="load(page + 1)">关联下一页</button></nav>
    <article v-if="current">
      <h3>{{ current.subject.number }} / Rev {{ current.subject.businessRevision }} · 技术版本 {{ current.subject.technical.version }}</h3>
      <p>当前：{{ state(current.state) }}；最近保存：{{ state(current.lastKnownState) }}。核验时间：{{ current.asOf }}</p>
      <p>用途：{{ current.subject.scope.purpose }}；产品：{{ current.subject.scope.productApplicability }}；工艺：{{ current.subject.scope.processApplicability }}；配置：{{ current.subject.scope.configurationApplicability }}</p>
      <p>生效区间：{{ current.subject.scope.effectiveFrom }} 至 {{ current.subject.scope.effectiveTo ?? '无止期' }}；{{ current.inEffectiveInterval ? '当前在区间内' : '当前不在区间内' }}。</p>
      <p>实物版本 {{ current.subject.physical.version }} · {{ current.subject.physical.id }}；技术 ID {{ current.subject.technical.id }}</p>
      <p>工程决定：{{ current.engineering?.owner ?? '待确认' }}；实物决定：{{ current.physical?.owner ?? '待确认' }}</p>
      <p v-if="current.unresolvedRoles.length">待核对：{{ current.unresolvedRoles.map(roleName).join('、') }}</p>
      <label>查看已保存的关联版本 <input v-model="historyVersion" aria-label="关联历史版本" inputmode="numeric" /></label><button :disabled="busy" @click="readHistory">读取关联历史</button>
      <p v-if="history">历史版本 {{ history.revision }}：{{ state(history.lastKnownState) }}；当前核验：{{ state(history.state) }}。用途：{{ history.subject.scope.purpose }}</p>
      <fieldset v-if="!terminal" :disabled="busy || !!pending || completed">
        <label>本方核实依据 <textarea v-model="evidence" aria-label="关联确认依据" maxlength="2000" /></label>
        <div v-for="engineering in [true, false]" :key="String(engineering)">
          <template v-if="canDecide(engineering)"><strong>{{ engineering ? '工程确认人' : '实物责任人' }}</strong>
            <button :disabled="!evidence.trim()" @click="decide(engineering, 'CONFIRM')">{{ engineering ? '工程确认对应' : '实物确认对应' }}</button>
            <button :disabled="!evidence.trim()" @click="decide(engineering, 'REJECT')">{{ engineering ? '工程拒绝对应' : '实物拒绝对应' }}</button>
            <button :disabled="!evidence.trim()" @click="decide(engineering, 'WITHDRAW')">{{ engineering ? '工程撤回对应' : '实物撤回对应' }}</button>
            <button :disabled="!evidence.trim()" @click="decide(engineering, 'REFRESH')">{{ engineering ? '工程发起重新核验' : '实物发起重新核验' }}</button>
          </template>
        </div>
      </fieldset>
      <p v-else>此关系已终结。需要重新关联时，请明确创建后继提案。</p>
    </article>
    <form v-if="canDecide(false)" aria-label="提出工程关联" @submit.prevent="propose"><fieldset :disabled="busy || !!pending || completed">
      <h3>提出准确版本关联</h3>
      <label>完整工程编号 <input v-model="engineeringNo" aria-label="完整工程编号" maxlength="40" /></label>
      <label>业务修订号 <input v-model="businessRevision" aria-label="业务修订号" inputmode="numeric" /></label>
      <button type="button" @click="search">查找准确工程版本</button>
      <label>技术来源 <select v-model="candidateKey" aria-label="技术来源"><option value="">请选择</option><option v-for="x in candidates" :key="x.technical.id" :value="x.technical.id">{{ x.baseCode }} / {{ x.number }} / Rev {{ x.businessRevision }} / 技术版本 {{ x.technical.version }} · 草稿</option></select></label>
      <p v-if="candidate">技术要求：{{ candidate.requirements.technicalRequirements }}；文件要求：{{ candidate.requirements.documentRequirements }}。技术批准尚未取得。</p>
      <label v-for="field in scopeFields" :key="field.key">{{ field.label }}<textarea v-model="draft[field.key]" :aria-label="field.label" :maxlength="field.key === 'purpose' ? 1000 : 2000" required /></label>
      <label>生效起日（UTC）<input v-model="from" type="date" aria-label="关联生效起日" required /></label>
      <label>生效止日（UTC，可空）<input v-model="to" type="date" aria-label="关联生效止日" /></label>
      <label>关联理由 <textarea v-model="reason" aria-label="关联理由" maxlength="500" required /></label>
      <label>后继于已终结提案（可空）<input v-model="supersedes" aria-label="后继提案来源" placeholder="原提案 ID" /></label>
      <button :disabled="!candidate || !reason.trim() || !from">保存关联提案</button>
    </fieldset></form>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { EngineeringScope } from '@/api/plm/engineeringProducts'
import type { ToolInstanceView } from '@/api/wms/toolInstances'
import { toolAssociationApi as api, associationEtag, type ToolAssociationCandidate, type ToolAssociationView, type ToolAssociationDecision } from '@/api/wms/toolAssociations'
import { associationResponse as result, pendingToolAssociation, saveToolAssociation, resolveToolAssociation, type PendingToolAssociation } from '@/auth/toolAssociationRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { usePermissionStore } from '@/stores/permission'
const props = defineProps<{ scope: EngineeringScope | null; selected: ToolInstanceView | null }>(), emit = defineEmits<{ 'authority-denied': [] }>()
const permission = usePermissionStore(), busy = ref(false), error = ref(''), notice = ref(''), completed = ref(false), pending = ref<PendingToolAssociation | null>(null)
const rows = ref<ToolAssociationView[]>([]), page = ref(1), loaded = ref(false), current = ref<ToolAssociationView | null>(null), history = ref<ToolAssociationView | null>(null), historyVersion = ref('1')
const candidates = ref<ToolAssociationCandidate[]>([]), candidateKey = ref(''), engineeringNo = ref(''), businessRevision = ref(''), evidence = ref(''), reason = ref(''), from = ref(''), to = ref(''), supersedes = ref('')
const scopeFields = [{ key: 'productApplicability', label: '关联产品范围' }, { key: 'processApplicability', label: '关联工艺范围' }, { key: 'configurationApplicability', label: '关联配置范围' }, { key: 'purpose', label: '关联用途' }]
const draft = ref<Record<string, string>>({}), candidate = computed(() => candidates.value.find(x => x.technical.id === candidateKey.value))
const terminal = computed(() => !!current.value && ['REJECTED', 'REVOKED'].includes(current.value.lastKnownState))
const canDecide = (engineering: boolean) => permission.has(engineering ? 'plm-tool-specification:instance-associate' : 'wms-plate-mold:engineering-link')
function state(value: string) { return ({ UNKNOWN: '待核实', PROPOSED: '已提出', CONFIRMED: '双方已确认对应', REJECTED: '已拒绝', REVOKED: '已撤回' } as Record<string, string>)[value] ?? value }
function roleName(value: string) { return ({ ENG_SPEC_CONFIRMATION: '工程本轮确认', PHYSICAL_INSTANCE_ASSOCIATION: '实物本轮确认', SOURCE_CHANGED: '原来源已变化' } as Record<string, string>)[value] ?? value }
let live = true, generation = 0
function clear() { rows.value = []; current.value = null; history.value = null; candidates.value = []; candidateKey.value = ''; engineeringNo.value = ''; businessRevision.value = ''; draft.value = {}; evidence.value = ''; reason.value = ''; from.value = ''; to.value = ''; supersedes.value = ''; notice.value = ''; completed.value = false; pending.value = null; loaded.value = false; page.value = 1 }
function accept(v: ToolAssociationView) { if (v.subject.tenantId !== props.scope?.tenantId || v.subject.physical.id !== props.selected?.reference.id || v.subject.technical.owner !== 'Main.Plm.ToolSpecification.v1' || v.notExecutionPermit !== true) throw new Error('关联与当前实物或身份不一致。'); return v }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value || !props.scope || !props.selected) return
  const stamp = identityStamp(), g = generation, check = () => { requireIdentity(stamp); if (!live || g !== generation) throw new Error('对象或身份已变化。') }
  busy.value = true; error.value = ''
  try { await action(check); check() } catch (e: any) { try { check(); if ([401, 403].includes(e?.response?.status)) { clear(); emit('authority-denied'); error.value = '当前权限已变化，请重新核对。' }
    else error.value = e?.response?.status === 412 ? '版本已变化，输入和原版本已保留。请先查询或结束原操作，再明确重新读取。' : e?.message || '关联操作未确认，请查询原操作。' } catch { /* stale response */ } }
  finally { if (live && g === generation) { busy.value = false; if (props.scope) pending.value = pendingToolAssociation(props.scope) } }
}
async function load(target: number) { const id = props.selected?.reference.id; if (!id) return; await run(async check => { const list = result(await api.list(id, target)); check(); rows.value = list.map(accept); page.value = target; loaded.value = true; current.value = null; history.value = null; completed.value = false }) }
async function open(id: string) { await run(async check => { const value = result(await api.read(id)); check(); current.value = accept(value); history.value = null; completed.value = false; evidence.value = ''; notice.value = '' }) }
async function readHistory() { const id = current.value?.subject.proposalId; if (!id || !/^[1-9][0-9]*$/.test(historyVersion.value)) return; await run(async check => { const value = result(await api.read(id, historyVersion.value)); check(); history.value = accept(value) }) }
async function search() {
  const id = props.selected?.reference.id, number = engineeringNo.value, revision = businessRevision.value
  if (!id || !number || !/^(0|[1-9][0-9]*)$/.test(revision) || Number(revision) > 2147483647) return
  await run(async check => { candidates.value = []; candidateKey.value = ''; const found = result(await api.candidates(id, number, Number(revision))); check(); if (engineeringNo.value !== number || businessRevision.value !== revision) return; candidates.value = found.filter(x => x.number === number && x.businessRevision === Number(revision) && x.technical.owner === 'Main.Plm.ToolSpecification.v1') })
}
async function propose() {
  const scope = props.scope, physical = props.selected, tech = candidate.value
  if (!scope || !physical || !tech || !canDecide(false) || pending.value || completed.value || !from.value || !reason.value.trim() || scopeFields.some(x => !draft.value[x.key]?.trim())) return
  const proposalId = crypto.randomUUID(), body = { proposalId, technical: { ...tech.technical }, physical: { ...physical.reference },
    scope: { productApplicability: draft.value.productApplicability!, processApplicability: draft.value.processApplicability!, configurationApplicability: draft.value.configurationApplicability!, purpose: draft.value.purpose!, effectiveFrom: `${from.value}T00:00:00Z`, effectiveTo: to.value ? `${to.value}T00:00:00Z` : null }, reason: reason.value, supersedes: supersedes.value || null }
  await run(async check => { await saveToolAssociation(scope, physical.reference.id, proposalId, check, operationId => api.propose(physical.reference.id, physical.etag, { ...body, operationId })); check(); completed.value = true; notice.value = '关联提案已保存。请读取当前关联，由双方分别核对。' })
}
async function decide(engineering: boolean, decision: ToolAssociationDecision['decision']) {
  const scope = props.scope, value = current.value
  if (!scope || !value || !canDecide(engineering) || terminal.value || pending.value || completed.value || !evidence.value.trim()) return
  const body = { proposalId: value.subject.proposalId, roundId: value.roundId, subjectDigest: value.subjectDigest, expectedControlRevision: value.controlRevision, expectedGeneration: value.generation, decision, evidence: evidence.value }
  await run(async check => { const operation = await saveToolAssociation(scope, value.subject.physical.id, value.subject.proposalId, check, operationId => api.decide(engineering, associationEtag(value), { ...body, operationId })); check(); completed.value = true; current.value = null; history.value = null; rows.value = []; notice.value = operation.result?.applied ? '本方操作已保存，请重新读取当前关联。' : '旧轮次或来源已变化，本次未应用确认。请重新核对。' })
}
async function recover(close: boolean) { const scope = props.scope; if (!scope) return; await run(async check => { const operation = await resolveToolAssociation(scope, close, check); check(); completed.value = operation.state === 'COMMITTED'; if (completed.value) { current.value = null; rows.value = []; history.value = null } notice.value = operation.state === 'UNKNOWN' ? '原结果仍未知，请继续查询。' : operation.state === 'CLOSED' ? '未提交操作已结束，输入仍保留。' : '原操作已保存，请重新读取当前关联。' }) }
function changed() { generation++; clear(); busy.value = false; error.value = ''; if (props.scope) pending.value = pendingToolAssociation(props.scope) }
watch(() => [props.scope?.tenantId, props.scope?.actorId, props.selected?.reference.id, props.selected?.reference.version], changed, { immediate: true })
watch([engineeringNo, businessRevision], () => { candidates.value = []; candidateKey.value = '' })
onMounted(() => { window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed) })
onBeforeUnmount(() => { live = false; generation++; clear(); window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>
<style scoped>
.association{margin-top:20px;padding:22px;background:var(--surface,#fff);border:1px solid #d7e3e7;border-radius:12px}label{display:block;margin:12px 0}input,select,textarea,button{font:inherit;color:inherit;padding:8px;border:1px solid #bdcdd3;border-radius:6px;background:#fff}textarea{display:block;width:min(100%,650px);min-height:65px}button{margin:5px;cursor:pointer}button:disabled,fieldset:disabled{opacity:.65}fieldset{border:0;padding:0}article{border-top:1px solid #d7e3e7;padding-top:15px}p{overflow-wrap:anywhere}[role=alert]{color:#a02828}input:focus,textarea:focus,select:focus,button:focus-visible{outline:2px solid #14969e;outline-offset:2px}
</style>
