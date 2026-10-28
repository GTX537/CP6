<template>
  <main class="requirements" :aria-busy="busy">
    <header><div><h1>需求集评审与批准</h1><p>固定准确成员和适用范围，经 FLOW 评审后供工程引用。集合批准不表示工程冻结或制造准入。</p></div><button :disabled="busy" @click="reload">刷新</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <section v-if="pending"><p>上次保存结果尚未确认。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy" @click="recover(true)">结束未提交操作</button></section>
    <section><header><h2>需求集目录</h2><button v-if="canAdd" :disabled="locked" @click="newDraft">新建需求集</button></header>
      <form @submit.prevent="list(1)"><label>名称<input v-model="search" aria-label="需求集搜索" maxlength="256" /></label><button :disabled="busy || !canRead">搜索</button></form>
      <table><thead><tr><th>名称</th><th>版本</th><th>状态</th><th>操作</th></tr></thead><tbody><tr v-for="row in rows" :key="row.reference.id"><td>{{ row.body.name }}</td><td>{{ row.reference.version }}</td><td>{{ row.state === 'APPROVED' ? '该版本已批准' : '待评审' }}</td><td><button :disabled="locked" @click="open(row)">打开</button></td></tr></tbody></table>
      <p v-if="!rows.length">当前范围没有需求集。</p><button :disabled="busy || page === 1" @click="list(page - 1)">上一页</button><span>第 {{ page }} 页</span><button :disabled="busy || rows.length < 20" @click="list(page + 1)">下一页</button>
    </section>
    <section v-if="draft"><h2>{{ current ? `版本 ${current.reference.version}` : '新需求集' }}</h2>
      <ul><li v-for="missing in current?.missing || []" :key="missing">{{ missingText(missing) }}</li></ul>
      <form @submit.prevent="save"><fieldset :disabled="locked || (current ? !canEdit : !canAdd)">
        <label>名称<input v-model="draft.name" aria-label="需求集名称" required maxlength="512" /></label>
        <label v-if="creation">所属部门<select v-model="department" aria-label="所属部门" :required="!creation.allowsUnbound"><option value="">{{ creation.allowsUnbound ? '不绑定部门' : '请选择授权部门' }}</option><option v-for="d in creation.departments" :key="d.id" :value="d.id">{{ d.name }}</option></select></label>
        <label>适用范围<textarea v-model="draft.scope" aria-label="适用范围" maxlength="8000" /></label><label>范围内成员完整性依据<textarea v-model="draft.completenessBasis" aria-label="完整性依据" maxlength="8000" /></label>
        <h3>准确需求成员</h3><p>必要性和判据保留在原需求版本中。改版需要明确重新选取，不会自动换成最新版本。</p>
        <label>查找需求<input v-model="memberSearch" aria-label="成员搜索" maxlength="256" /></label><button type="button" @click="findMembers(1)">查找需求</button><button type="button" :disabled="memberPage === 1" @click="findMembers(memberPage - 1)">上一页需求</button><button type="button" :disabled="choices.length < 20" @click="findMembers(memberPage + 1)">下一页需求</button>
        <label>需求版本<select v-model="choice" aria-label="需求版本"><option value="">请选择</option><option v-for="item in choices" :key="item.reference.id" :value="item.reference.id">{{ item.body.name }} · {{ item.reference.version }}</option></select></label><button type="button" :disabled="!choice" @click="selectMember">核对批准并加入或更新成员</button>
        <article v-for="(member, i) in draft.members" :key="member.requirement.id"><p>{{ memberName(member.requirement.id) }} · 版本 {{ member.requirement.version }}</p><p>必要性原文：{{ memberNecessity(member.requirement.id) }}</p>
          <label>成员处置<select v-model="member.disposition" :aria-label="`成员处置 ${i + 1}`"><option value="INCLUDED">纳入</option><option value="EXCLUDED">提出排除（待来源裁决）</option></select></label><label>纳入或排除依据<textarea v-model="member.basis" :aria-label="`成员依据 ${i + 1}`" maxlength="8000" /></label>
          <button type="button" @click="draft.members.splice(i, 1)">移除此草案成员</button></article>
        <h3>冲突提案</h3><p>澄清、范围分离、工程变更或偏离仅登记拟议去向。有权裁决缺失时集合不能批准。</p>
        <article v-for="(conflict, i) in draft.conflicts" :key="conflict.id"><label>左侧需求<select v-model="conflict.left" :aria-label="`冲突左侧 ${i + 1}`"><option v-for="m in draft.members" :key="m.requirement.id" :value="m.requirement">{{ memberName(m.requirement.id) }}</option></select></label><label>右侧需求<select v-model="conflict.right" :aria-label="`冲突右侧 ${i + 1}`"><option v-for="m in draft.members" :key="m.requirement.id" :value="m.requirement">{{ memberName(m.requirement.id) }}</option></select></label><label>适用交集<textarea v-model="conflict.intersection" :aria-label="`适用交集 ${i + 1}`" maxlength="8000" /></label><label>处理提案<select v-model="conflict.proposal" :aria-label="`处理提案 ${i + 1}`"><option value="UNRESOLVED">待裁决</option><option value="CLARIFY">来源澄清</option><option value="SCOPE_SEPARATION">范围分离</option><option value="ECM">工程变更</option><option value="DEV">偏离评估</option></select></label><label>提案依据<textarea v-model="conflict.basis" :aria-label="`提案依据 ${i + 1}`" maxlength="8000" /></label><button type="button" @click="draft.conflicts.splice(i, 1)">移除此草案提案</button></article>
        <button type="button" :disabled="draft.members.length < 2" @click="addConflict">登记冲突</button>
        <label>保存原因<textarea v-model="reason" aria-label="保存原因" required maxlength="8000" /></label><button>{{ current ? '保存后继版本' : '保存草稿' }}</button>
      </fieldset></form>
      <RequirementSetReviewPanel v-if="current && scope" :requirement-set="current" :scope="scope" :disabled="locked" />
      <details v-if="current"><summary>历史版本（只读）</summary><label>版本<input v-model="historyVersion" aria-label="历史版本" /></label><button :disabled="busy" @click="readHistory">读取历史</button><article v-if="history"><p>{{ history.body.name }} · {{ history.reference.version }} · {{ history.body.scope }}</p><p v-for="m in history.body.members" :key="m.requirement.id">{{ m.requirement.id }} · 版本 {{ m.requirement.version }} · {{ m.disposition }}</p><p>保存原因：{{ history.reason }} · {{ history.recordedAt }}</p></article></details>
    </section>
  </main>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { requirementSetApi, requirementSetMissingText as missingText, type RequirementSetBody, type RequirementSetSnapshot, type RequirementSetCreation, type RequirementSetScope, type RequirementSetCommand } from '@/api/plm/requirementSet'
import { requirementApi, type RequirementSnapshot } from '@/api/plm/engineeringRequirements'
import { evaluationApi } from '@/api/plm/requirementEvaluation'
import { pendingRequirementSet, requirementSetResponse, resolveRequirementSet, saveRequirementSet, type PendingRequirementSet } from '@/auth/requirementSetRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
import RequirementSetReviewPanel from './RequirementSetReviewPanel.vue'
const permission = usePermissionStore(), canRead = computed(() => permission.loaded && permission.has('plm-requirement-set:query')), canAdd = computed(() => canRead.value && permission.has('plm-requirement-set:add')), canEdit = computed(() => canRead.value && permission.has('plm-requirement-set:edit'))
const busy = ref(false), error = ref(''), notice = ref(''), scope = ref<RequirementSetScope | null>(null), pending = ref<PendingRequirementSet | null>(null), locked = computed(() => busy.value || !!pending.value)
const search = ref(''), page = ref(1), rows = ref<RequirementSetSnapshot[]>([]), current = ref<RequirementSetSnapshot | null>(null), creation = ref<RequirementSetCreation | null>(null), draft = ref<RequirementSetBody | null>(null), objectId = ref(''), department = ref(''), reason = ref('')
const choices = ref<RequirementSnapshot[]>([]), originals = ref<RequirementSnapshot[]>([]), memberSearch = ref(''), memberPage = ref(1), choice = ref(''), historyVersion = ref('1'), history = ref<RequirementSetSnapshot | null>(null)
const copy = <T,>(value: T): T => JSON.parse(JSON.stringify(value)) as T
let generation = 0, live = true
function resetEditor() { current.value = null; creation.value = null; draft.value = null; objectId.value = ''; department.value = reason.value = ''; choices.value = originals.value = []; choice.value = memberSearch.value = ''; memberPage.value = 1; history.value = null }
function clearPrivate() { resetEditor(); rows.value = []; scope.value = null; pending.value = null; notice.value = '' }
function syncPending() { pending.value = scope.value ? pendingRequirementSet(scope.value) : null }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const stamp = identityStamp(), epoch = generation
  const check = () => { requireIdentity(stamp); if (!live || epoch !== generation || !authenticationCanRead()) throw new Error('身份已变化，请重新载入。') }
  busy.value = true; error.value = notice.value = ''
  try { check(); await action(check); check() }
  catch (e: any) { try { check(); if (e?.response?.status === 403) clearPrivate(); const message = e?.response?.data?.message || e?.message || ''; error.value = /^[A-Z][A-Z0-9_]+$/.test(message) ? '无法完成，请核对准确版本、来源资格和当前权限。' : message || '暂时无法完成。' } catch { /* Obsolete identity response. */ } }
  finally { try { check(); syncPending(); busy.value = false } catch { if (live && epoch === generation) busy.value = false } }
}
async function reload() { await run(async check => { clearPrivate(); if (!permission.loaded) await permission.loadMyActions(); check(); if (!canRead.value) throw new Error('当前无权读取需求集。'); const s = requirementSetResponse(await requirementSetApi.scope()); check(); scope.value = s; const values = requirementSetResponse(await requirementSetApi.list(search.value, page.value)); check(); rows.value = values }) }
async function list(p: number) { await run(async check => { rows.value = []; const values = requirementSetResponse(await requirementSetApi.list(search.value, p)); check(); rows.value = values; page.value = p }) }
function loadEditor(value: RequirementSetSnapshot) { resetEditor(); current.value = value; objectId.value = value.reference.id; draft.value = copy(value.body); originals.value = (value.memberStatus || []).map(x => x.requirement) }
async function open(value: RequirementSetSnapshot) { await run(async check => { resetEditor(); const v = requirementSetResponse(await requirementSetApi.read(value.reference.id)); check(); loadEditor(v) }) }
async function newDraft() { await run(async check => { resetEditor(); const c = requirementSetResponse(await requirementSetApi.creationContext()); check(); creation.value = c; objectId.value = crypto.randomUUID(); draft.value = { kind: 'SET', name: '', scope: '', completenessBasis: '', members: [], conflicts: [] } }) }
async function findMembers(p: number) { await run(async check => { choices.value = []; choice.value = ''; const found = requirementSetResponse(await requirementApi.list('REQUIREMENT', memberSearch.value, p)); check(); choices.value = found; memberPage.value = p }) }
async function selectMember() { await run(async check => {
  const selected = choices.value.find(x => x.reference.id === choice.value); if (!selected || !draft.value) return
  const proof = requirementSetResponse(await evaluationApi.source(selected.reference)); check()
  if (proof.environmentId !== scope.value?.environmentId || proof.tenantId !== scope.value?.tenantId || proof.evidenceMode !== scope.value?.evidenceMode || proof.availability !== 'AVAILABLE' || !proof.approval || !proof.control || JSON.stringify(proof.requirement) !== JSON.stringify(selected.reference)) throw new Error('此版本尚无可用批准，请先在工程需求入口完成专业评审。')
  const member = { requirement: copy(selected.reference), evaluationControl: proof.control, approval: proof.approval, disposition: 'INCLUDED' as const, basis: '' }
  const i = draft.value.members.findIndex(x => x.requirement.id === selected.reference.id); if (i < 0) draft.value.members.push(member); else draft.value.members[i] = member
  originals.value = originals.value.filter(x => x.reference.id !== selected.reference.id).concat(selected); choice.value = ''
}) }
function memberName(id: string) { return originals.value.find(x => x.reference.id === id)?.body.name || id }
function memberNecessity(id: string) { return originals.value.find(x => x.reference.id === id)?.body.requirement?.necessity || '请重新核对原需求' }
function addConflict() { if (draft.value && draft.value.members.length >= 2) draft.value.conflicts.push({ id: crypto.randomUUID(), left: copy(draft.value.members[0]!.requirement), right: copy(draft.value.members[1]!.requirement), intersection: '', proposal: 'UNRESOLVED', basis: '' }) }
async function save() { await run(async check => { if (!draft.value || !scope.value || pending.value) return
  const command: Omit<RequirementSetCommand, 'operationId'> = { objectId: objectId.value, action: current.value ? 'REVISE' : 'CREATE', expectedVersion: current.value?.reference.version ?? null, expectedEtag: current.value?.etag ?? null, expectedAuthorityVersion: creation.value?.authorityVersion ?? null, departmentId: creation.value ? department.value || null : null, reason: reason.value, body: copy(draft.value) }
  const saved = await saveRequirementSet(scope.value, objectId.value, check, operationId => requirementSetApi.save({ ...command, operationId })); check()
  if (saved.result) { const v = requirementSetResponse(await requirementSetApi.read(saved.result.objectId)); check(); loadEditor(v); notice.value = '需求集版本已保存，需单独送审和采用。' }
}) }
async function recover(close: boolean) { await run(async check => { if (!scope.value) return; const result = await resolveRequirementSet(scope.value, close, check); check(); if (result.result) { const v = requirementSetResponse(await requirementSetApi.read(result.result.objectId)); check(); loadEditor(v) } notice.value = result.state === 'UNKNOWN' ? '原结果仍未确认，请保留记录。' : '原操作已确认。' }) }
async function readHistory() { await run(async check => { if (!current.value) return; history.value = null; const v = requirementSetResponse(await requirementSetApi.read(current.value.reference.id, historyVersion.value)); check(); history.value = v }) }
function invalidate() { generation++; busy.value = false; error.value = ''; clearPrivate() }
watch(() => [permission.loaded, canRead.value], () => { if (!canRead.value) invalidate() })
onMounted(() => { window.addEventListener(AUTH_EVENT, invalidate); void reload() })
onBeforeUnmount(() => { live = false; generation++; window.removeEventListener(AUTH_EVENT, invalidate) })
</script>
<style scoped>
.requirements{max-width:1200px;margin:auto;padding:24px;color:var(--el-text-color-primary,#253047)}header,.inline{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap}section,article{border:1px solid var(--el-border-color,#d8dce3);border-radius:8px;padding:18px;margin:18px 0}article{background:var(--el-fill-color-light,#f7f9fc)}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:14px}label{display:flex;flex-direction:column;gap:6px;margin:10px 0}input,textarea,select,button{font:inherit;padding:8px;border:1px solid var(--el-border-color,#c4cbd6);border-radius:5px}textarea{min-height:80px}button{cursor:pointer;margin:5px}button:disabled{cursor:default;opacity:.55}fieldset{border:0;padding:0;min-width:0}table{width:100%;text-align:left;border-collapse:collapse}th,td{border-bottom:1px solid #d8dce3;padding:10px}blockquote{white-space:pre-wrap;overflow-wrap:anywhere;margin:12px}p,li{overflow-wrap:anywhere}[role=alert]{color:#b42318}[role=status]{color:#167044}@media(max-width:640px){.requirements{padding:12px}section,article{padding:12px}.grid{grid-template-columns:1fr}}
</style>
