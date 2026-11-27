<template>
  <main class="requirements" :aria-busy="busy">
    <header><div><h1>工程需求与来源</h1><p>保留来源原文，逐版编制需求解释、覆盖关系和评价判据。</p></div><button :disabled="busy" @click="reload">刷新</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <section v-if="pending" aria-label="原操作恢复"><p>上次保存结果尚未确认，请先核对原操作。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy" @click="recover(true)">结束未提交操作</button></section>
    <section>
      <header><h2>来源与需求目录</h2><div><button v-if="canAdd" :disabled="locked" @click="newDraft('SOURCE')">新建来源包</button><button v-if="canAdd" :disabled="locked" @click="newDraft('REQUIREMENT')">新建需求</button></div></header>
      <form class="inline" @submit.prevent="list(1)"><label>类别<select v-model="kind" aria-label="目录类别"><option value="SOURCE">来源包</option><option value="REQUIREMENT">需求</option></select></label><label>名称<input v-model="search" aria-label="目录搜索" maxlength="256" /></label><button :disabled="busy || !canRead">搜索</button><button v-if="canExport" type="button" :disabled="busy || !displayedQuery" @click="exportPage">导出当前页</button></form>
      <table><thead><tr><th>名称</th><th>版本</th><th>状态</th><th>操作</th></tr></thead><tbody><tr v-for="r in rows" :key="r.reference.id"><td>{{ r.body.name }}</td><td>{{ r.reference.version }}</td><td>草稿</td><td><button :disabled="locked" @click="open(r)">打开</button></td></tr></tbody></table>
      <p v-if="!rows.length">当前范围没有记录。</p><nav class="inline"><button :disabled="busy || page === 1 || !canRead" @click="list(page - 1)">上一页</button><span>第 {{ page }} 页</span><button :disabled="busy || rows.length < 20 || !canRead" @click="list(page + 1)">下一页</button></nav>
    </section>
    <section v-if="draft">
      <h2>{{ draft.kind === 'SOURCE' ? '来源包原文' : '需求定义' }} · {{ current ? `版本 ${current.reference.version}` : '新草稿' }}</h2>
      <p>保存草稿不表示客户确认、关系采纳或判据批准。未知输入会保留为缺项。</p>
      <ul v-if="current?.missing.length" aria-label="需求缺项"><li v-for="m in current.missing" :key="m">{{ missingText(m) }}</li></ul>
      <form @submit.prevent="save"><fieldset :disabled="locked || (!current && !canAdd) || (!!current && !canEdit)">
        <div class="grid"><label>名称<input v-model="draft.name" aria-label="草稿名称" maxlength="512" required /></label><label v-if="creation">所属部门<select v-model="department" aria-label="所属部门" :required="!creation.allowsUnbound"><option value="">{{ creation.allowsUnbound ? '不绑定部门' : '请选择授权部门' }}</option><option v-for="d in creation.departments" :key="d.id" :value="d.id">{{ d.code }} / {{ d.name }}</option></select></label></div>
        <template v-if="draft.source">
          <div class="grid"><label>来源类别<select v-model="draft.source.kind" aria-label="来源类别" @change="changeSourceKind"><option value="INTERNAL_PROPOSAL">内部提案</option><option value="UNRESOLVED">来源待澄清</option><option value="OWNER_ORIGINAL">受权原件</option></select></label><label>来源描述<textarea v-model="draft.source.originDescription" aria-label="来源描述" required maxlength="8000" /></label><label>来源版本标记<input v-model="draft.source.sourceLabel" aria-label="来源版本标记" maxlength="8000" /></label><label>取得依据<textarea v-model="draft.source.acquisitionBasis" aria-label="取得依据" maxlength="8000" /></label></div>
          <button v-if="draft.source.kind === 'UNRESOLVED' && !draft.source.origin" type="button" @click="retainPendingOrigin">保留待补证原件引用</button>
          <RequirementOriginFields v-if="draft.source.origin" v-model="draft.source.origin" :pending="draft.source.kind === 'UNRESOLVED'" />
          <p v-if="draft.source.kind === 'OWNER_ORIGINAL'">文本原件按 LINE:行号逐字核对。销售原件按 JSON:/字段路径核对。其他文档可先登记准确引用。</p>
          <p v-if="draft.source.kind === 'UNRESOLVED' && draft.source.origin">草稿只保存待补证引用。当前原件和客户确认仍待核验。</p>
          <template v-if="draft.source.kind !== 'UNRESOLVED' || !draft.source.origin">
            <article v-for="(c, i) in draft.source.clauses" :key="i"><h3>原条款 {{ i + 1 }}</h3><div class="grid"><label>条款标识<input v-model="c.id" :aria-label="`条款标识 ${i + 1}`" required maxlength="128" /></label><label>原文位置<input v-model="c.locator" :aria-label="`原文位置 ${i + 1}`" :required="draft.source.kind === 'OWNER_ORIGINAL'" maxlength="8000" /></label><label>来源原文<textarea v-model="c.originalText" :aria-label="`来源原文 ${i + 1}`" required maxlength="8000" /></label></div><RequirementClauseTermsPanel v-model="c.terms" :index="i + 1" :disabled="locked" :can-confirm="draft.source.kind === 'INTERNAL_PROPOSAL' && permission.has('plm-engineering-requirement:source-confirm')" /><button type="button" :disabled="draft.source.kind !== 'OWNER_ORIGINAL' && draft.source.clauses.length === 1" @click="draft.source.clauses.splice(i, 1)">移除此版条款</button></article>
            <button type="button" @click="draft.source.clauses.push({ id: '', locator: '', originalText: '' })">添加原条款</button>
          </template>
        </template>
        <template v-else>
          <h3>准确来源</h3><div class="inline"><label>来源名称<input v-model="sourceSearch" aria-label="来源搜索" /></label><button type="button" @click="findSources(1)">查找来源包</button><button type="button" :disabled="sourcePage === 1" @click="findSources(sourcePage - 1)">上一页来源</button><button type="button" :disabled="sources.length < 20" @click="findSources(sourcePage + 1)">下一页来源</button></div>
          <label>来源包版本<select v-model="sourceKey" aria-label="来源包版本"><option value="">请选择准确来源</option><option v-for="s in sources" :key="key(s.reference)" :value="key(s.reference)">{{ s.body.name }} · 版本 {{ s.reference.version }}</option></select></label><button type="button" :disabled="!sourceKey" @click="chooseSource">采用所选来源</button>
          <p v-if="draft.requirement">当前引用：{{ source?.body.name || draft.requirement.source.id }} · 来源版本 {{ draft.requirement.source.version }}。更换来源会清空所选条款，需重新核对。</p>
          <template v-if="draft.requirement">
            <article v-for="c in source?.body.source?.clauses || []" :key="c.id"><label><input v-model="draft.requirement.clauseIds" type="checkbox" :value="c.id" :aria-label="`引用条款 ${c.id}`" /> {{ c.id }} · {{ c.locator }}</label><blockquote>{{ c.originalText }}</blockquote></article>
            <div class="grid"><label>需求解释<textarea v-model="draft.requirement.interpretation" aria-label="需求解释" required maxlength="8000" /></label><label>适用对象<textarea v-model="draft.requirement.applicableObject" aria-label="适用对象" maxlength="8000" /></label><label>必要性<textarea v-model="draft.requirement.necessity" aria-label="必要性" maxlength="8000" /></label></div>
            <h3>待澄清问题</h3><div v-for="(_, i) in draft.requirement.clarificationQuestions" :key="i" class="inline"><label>问题<textarea v-model="draft.requirement.clarificationQuestions[i]" :aria-label="`澄清问题 ${i + 1}`" required maxlength="8000" /></label><button type="button" @click="draft.requirement.clarificationQuestions.splice(i, 1)">移除问题</button></div><button type="button" @click="draft.requirement.clarificationQuestions.push('')">添加澄清问题</button>
            <button type="button" :disabled="draft.requirement.clauseIds.length === 0" @click="addQuestion">按原条款发起澄清</button>
            <article v-for="(question, i) in draft.requirement.clarifications" :key="question.id">
              <p>原来源版 {{ question.source.version }} · {{ question.clauseId }}</p>
              <label v-if="!savedQuestion(question.id)">原条款<select v-model="question.clauseId" :aria-label="`澄清原条款 ${i + 1}`"><option v-for="clause in draft.requirement.clauseIds" :key="clause">{{ clause }}</option></select></label>
              <label>原问题<textarea v-model="question.question" :aria-label="`原版澄清问题 ${i + 1}`" :readonly="!!savedQuestion(question.id)" required maxlength="8000" /></label>
              <button v-if="!savedQuestion(question.id)" type="button" @click="draft.requirement.clarifications?.splice(i, 1)">移除未保存的澄清</button>
              <p v-if="!question.reply">待答复</p>
              <p v-else>已关联答复证据，原渠道确认待核。</p>
              <button v-if="!question.reply && savedQuestion(question.id)" type="button" @click="question.reply = blankOrigin()">关联准确答复原件</button>
              <RequirementOriginFields v-if="question.reply" v-model="question.reply" :prefix="`答复 ${i + 1}`" :inert="!!savedQuestion(question.id)?.reply" />
              <button v-if="question.reply && !savedQuestion(question.id)?.reply" type="button" @click="question.reply = null">清除未保存的答复</button>
              <button v-if="current && savedQuestion(question.id)?.reply" type="button" @click="downloadOriginal(current, question.id)">下载原答复</button>
            </article>
            <h3>评价判据草案</h3><p>单位、阈值、精度及方法需求均保留来源依据；未填写时不会默认补值。</p>
            <article v-for="(c, i) in draft.requirement.criteria" :key="c.id"><div class="grid"><label>判据标识<input v-model="c.id" :aria-label="`判据标识 ${i + 1}`" required maxlength="128" /></label><label>类别<select v-model="c.kind" :aria-label="`判据类别 ${i + 1}`" @change="changeCriterion(c)"><option value="QUANTITATIVE">定量区间</option><option value="QUALITATIVE">定性评审</option></select></label><label>评价对象<input v-model="c.object" :aria-label="`评价对象 ${i + 1}`" maxlength="8000" /></label><label>适用条件<textarea v-model="c.conditions" :aria-label="`适用条件 ${i + 1}`" maxlength="8000" /></label><label>方法需求<textarea v-model="c.methodRequirement" :aria-label="`方法需求 ${i + 1}`" maxlength="8000" /></label>
              <template v-if="c.kind === 'QUANTITATIVE'"><label>下界<input v-model="c.lower" :aria-label="`下界 ${i + 1}`" maxlength="2050" /></label><label>下界包含端点<select v-model="c.lowerInclusive" :aria-label="`下界包含 ${i + 1}`"><option :value="null">未定</option><option :value="true">包含</option><option :value="false">不包含</option></select></label><label>上界<input v-model="c.upper" :aria-label="`上界 ${i + 1}`" maxlength="2050" /></label><label>上界包含端点<select v-model="c.upperInclusive" :aria-label="`上界包含 ${i + 1}`"><option :value="null">未定</option><option :value="true">包含</option><option :value="false">不包含</option></select></label><RequirementCriterionEvaluation :criterion="c" :index="i + 1" /><label>拟用单位<input v-model="c.proposedUnit" :aria-label="`拟用单位 ${i + 1}`" maxlength="8000" /></label><label>单位依据<textarea v-model="c.unitBasis" :aria-label="`单位依据 ${i + 1}`" maxlength="8000" /></label><label>精度要求<input v-model="c.precision" :aria-label="`精度要求 ${i + 1}`" maxlength="8000" /></label></template>
              <label v-else>定性判定依据<textarea v-model="c.qualitativeBasis" :aria-label="`定性依据 ${i + 1}`" maxlength="8000" /></label></div>
              <div v-if="draft.requirement.clauseIds.length">
                <p>方法与判据依据的来源条款</p>
                <label v-for="clause in draft.requirement.clauseIds" :key="clause">
                  <input type="checkbox" :checked="c.sourceClauseIds?.includes(clause)" @change="toggleCriterionSource(c, clause, $event)" /> {{ clause }}
                </label>
              </div>
              <button type="button" @click="draft.requirement.criteria.splice(i, 1)">移除判据</button>
            </article><button type="button" @click="addCriterion">添加判据</button>
            <h3>分解与关系</h3><p>关系指向所选需求的准确版本。未覆盖部分应保留，关系数量不表示满足率。</p><div class="inline"><label>关联需求搜索<input v-model="relationSearch" aria-label="关联需求搜索" /></label><button type="button" @click="findRelations(1)">查找关联需求</button><button type="button" :disabled="relationPage === 1" @click="findRelations(relationPage - 1)">上一页关联</button><button type="button" :disabled="relations.length < 20" @click="findRelations(relationPage + 1)">下一页关联</button></div><label>关联版本<select v-model="relationKey" aria-label="关联版本"><option value="">请选择准确需求</option><option v-for="r in relations" :key="key(r.reference)" :value="key(r.reference)">{{ r.body.name }} · 版本 {{ r.reference.version }}</option></select></label><button type="button" :disabled="!relationKey" @click="addRelation">添加关系</button>
            <article v-for="(l, i) in draft.requirement.relations" :key="l.id">
              <p>目标 {{ l.target.id }} · 版本 {{ l.target.version }}</p>
              <div class="grid"><label>关系类型<select v-model="l.kind" :aria-label="`关系类型 ${i + 1}`" @change="changeRelationKind(l)"><option value="PARENT">父需求</option><option value="DERIVED">派生来源</option><option value="CONSTRAINT">约束来源</option><option value="CONFLICT">冲突对象</option><option value="DEPENDENCY">依赖对象</option></select></label><label>覆盖理由<textarea v-model="l.coverageReason" :aria-label="`覆盖理由 ${i + 1}`" maxlength="8000" /></label><label>未覆盖内容<textarea v-model="l.uncovered" :aria-label="`未覆盖内容 ${i + 1}`" maxlength="8000" /></label><label>关系必要性<textarea v-model="l.necessity" :aria-label="`关系必要性 ${i + 1}`" maxlength="8000" /></label></div>
              <div v-for="(link, j) in l.coverage" :key="j" class="grid">
                <label>父条款 ID<input v-model="link.parentClauseId" required maxlength="128" /></label>
                <label>子条款<select v-model="link.childClauseId" required><option value="">请选择子条款</option><option v-for="clause in draft.requirement.clauseIds" :key="clause">{{ clause }}</option></select></label>
                <label>该条覆盖依据<textarea v-model="link.reason" required maxlength="8000" /></label>
                <button type="button" @click="l.coverage?.splice(j, 1)">移除条款映射</button>
              </div>
              <button v-if="l.kind === 'PARENT'" type="button" @click="addCoverageLink(l)">添加父子条款映射</button>
              <button type="button" @click="draft.requirement.relations.splice(i, 1)">移除关系</button>
            </article>
            <button v-if="current" type="button" :disabled="!relationKey" @click="addCoverageChild">加入所选子版复核</button>
            <article v-if="draft.requirement.decomposition">
              <h4>逐条款覆盖复核</h4>
              <p>父版 {{ draft.requirement.decomposition.parent.version }}</p>
              <div v-for="(child, i) in draft.requirement.decomposition.children" :key="key(child)" class="inline">
                <span>{{ child.id }} · 版本 {{ child.version }}</span>
                <button type="button" @click="removeCoverageChild(i)">移除子版</button>
              </div>
              <label>覆盖复核依据<textarea v-model="draft.requirement.decomposition.basis" required maxlength="8000" /></label>
            </article>
          </template>
        </template>
        <label>保存原因<textarea v-model="reason" aria-label="保存原因" required maxlength="8000" /></label><button :disabled="draft.kind === 'REQUIREMENT' && !draft.requirement?.clauseIds.length">保存草稿</button>
      </fieldset></form>
    </section>
    <section v-if="current">
      <h2>历史版本</h2>
      <button v-if="current.body.source?.kind === 'OWNER_ORIGINAL' || source?.body.source?.kind === 'OWNER_ORIGINAL'" :disabled="busy" @click="downloadOriginal(current)">下载所引用原件</button>
      <div v-if="current.coverage">
        <h3>逐条覆盖 · 父版 {{ current.coverage.parent.version }}</h3>
        <ul>
          <li v-for="clause in current.coverage.clauses" :key="clause.clauseId">
            {{ clause.clauseId }} · {{ coverageLabel(clause.state) }}
            <p>{{ termsLabel(clause.terms) }}</p>
            <ul>
              <li v-for="child in clause.children" :key="`${key(child.child)}:${child.childClauseId}`">
                子版 {{ child.child.id }} / {{ child.child.version }} · {{ child.childClauseId }} · {{ coverageLabel(child.scope) }} · {{ child.reason }}
                <p>{{ termsLabel(child.terms) }}</p>
              </li>
            </ul>
          </li>
        </ul>
      </div>
      <form class="inline" @submit.prevent="readHistory"><label>版本<input v-model="historyVersion" aria-label="历史版本" pattern="[1-9][0-9]*" required /></label><button :disabled="busy">读取历史</button></form>
      <article v-if="history">
        <h3>{{ history.body.name }} · 版本 {{ history.reference.version }}</h3>
        <p>原因　{{ history.reason }}</p><p>记录时间　{{ history.recordedAt }}</p>
        <button v-if="history.body.source?.kind === 'OWNER_ORIGINAL'" :disabled="busy" @click="downloadOriginal(history)">下载历史原件</button>
        <button v-if="history.reference.version !== current.reference.version" :disabled="busy" @click="compareVersions">比较当前版</button>
        <ul><li v-for="m in history.missing" :key="m">{{ missingText(m) }}</li></ul>
        <fieldset v-if="history.body.source?.origin?.actFile" disabled>
          <RequirementOriginFields :model-value="history.body.source.origin" prefix="历史" pending />
        </fieldset>
        <blockquote v-for="c in history.body.source?.clauses || []" :key="c.id">{{ c.id }} · {{ c.originalText }}</blockquote>
        <template v-if="history.body.requirement">
          <p>{{ history.body.requirement.interpretation }}</p><p>来源版本 {{ history.body.requirement.source.version }} · {{ history.body.requirement.clauseIds.join('、') }}</p>
          <p v-for="c in history.body.requirement.criteria" :key="c.id">{{ c.id }} · {{ c.object }} / {{ c.lower ?? '下界未定' }} — {{ c.upper ?? '上界未定' }} / {{ c.proposedUnit ?? c.qualitativeBasis ?? '依据未定' }}</p>
          <p v-for="question in history.body.requirement.clarifications" :key="question.id">原版 {{ question.source.version }} · {{ question.question }} <button v-if="question.reply" :disabled="busy" @click="downloadOriginal(history, question.id)">下载历史答复</button></p>
        </template>
      </article>
      <article v-if="diff">
        <h3>版本 {{ diff.before.version }} 到 {{ diff.after.version }} 的差异</h3>
        <p v-if="diff.changes.length === 0">两版业务内容相同。</p>
        <div v-for="change in diff.changes" :key="change.path">
          <h4>{{ differenceLabel(change.path) }}</h4>
          <div class="grid"><div>原版<pre>{{ JSON.stringify(change.before, null, 2) }}</pre></div><div>后继版<pre>{{ JSON.stringify(change.after, null, 2) }}</pre></div></div>
        </div>
      </article>
    </section>
    <RequirementEvaluationPanel v-if="current?.body.kind === 'REQUIREMENT'" :key="current.reference.id" :requirement-id="current.reference.id" />
  </main>
</template>

<script setup lang="ts">
import RequirementEvaluationPanel from './RequirementEvaluationPanel.vue'
import RequirementCriterionEvaluation from './RequirementCriterionEvaluation.vue'
import RequirementClauseTermsPanel from './RequirementClauseTermsPanel.vue'
import RequirementOriginFields from './RequirementOriginFields.vue'
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { requirementApi, type RequirementBody, type RequirementClauseTerms, type RequirementCommand, type RequirementCreation, type RequirementCriterion, type RequirementDiff, type RequirementOperationKind, type RequirementOrigin, type RequirementRelation, type RequirementScope, type RequirementScopeAxis, type RequirementSnapshot } from '@/api/plm/engineeringRequirements'
import type { EngineeringReference } from '@/api/plm/engineeringProducts'
import { pendingRequirementOperation, requirementResponse, resolveRequirementOperation, saveRequirementOperation, type PendingRequirementOperation } from '@/auth/requirementOperationRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const permission = usePermissionStore(), canRead = computed(() => permission.loaded && permission.has('plm-engineering-requirement:query')), canAdd = computed(() => canRead.value && permission.has('plm-engineering-requirement:add')), canEdit = computed(() => canRead.value && permission.has('plm-engineering-requirement:edit'))
const canExport = computed(() => canRead.value && permission.has('plm-engineering-requirement:export'))
const busy = ref(false), error = ref(''), notice = ref(''), scope = ref<RequirementScope | null>(null), pending = ref<PendingRequirementOperation | null>(null), locked = computed(() => busy.value || !!pending.value)
const kind = ref<RequirementOperationKind>('SOURCE'), search = ref(''), rows = ref<RequirementSnapshot[]>([]), current = ref<RequirementSnapshot | null>(null), creation = ref<RequirementCreation | null>(null), draft = ref<RequirementBody | null>(null), objectId = ref(''), department = ref(''), reason = ref('')
const displayedQuery = ref<{ kind: RequirementOperationKind; search: string; page: number } | null>(null)
const page = computed(() => displayedQuery.value?.page ?? 1)
const source = ref<RequirementSnapshot | null>(null), sources = ref<RequirementSnapshot[]>([]), sourceSearch = ref(''), sourceKey = ref(''), sourcePage = ref(1), relations = ref<RequirementSnapshot[]>([]), relationSearch = ref(''), relationKey = ref(''), relationPage = ref(1), historyVersion = ref('1'), history = ref<RequirementSnapshot | null>(null)
const diff = ref<RequirementDiff | null>(null)
const copy = <T,>(v: T): T => JSON.parse(JSON.stringify(v)) as T
const key = (r: EngineeringReference) => `${r.owner}:${r.id}:${r.version}:${r.digest}`
let generation = 0, live = true
function resetEditor() { current.value = null; creation.value = null; draft.value = null; objectId.value = ''; department.value = ''; reason.value = ''; source.value = null; sources.value = []; sourceSearch.value = ''; sourceKey.value = ''; sourcePage.value = 1; relations.value = []; relationSearch.value = ''; relationKey.value = ''; relationPage.value = 1; history.value = null; historyVersion.value = '1'; diff.value = null }
function clearPrivate() { resetEditor(); rows.value = []; displayedQuery.value = null; scope.value = null; pending.value = null; search.value = ''; notice.value = '' }
function syncPending() { pending.value = scope.value ? pendingRequirementOperation(scope.value, 'SOURCE') || pendingRequirementOperation(scope.value, 'REQUIREMENT') : null }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const stamp = identityStamp(), epoch = generation
  const check = () => { requireIdentity(stamp); if (!live || epoch !== generation || !authenticationCanRead()) throw new Error('身份已变化，请重新载入。') }
  busy.value = true; error.value = ''; notice.value = ''
  try { check(); await action(check); check() }
  catch (e: any) {
    try {
      check()
      const response = e?.response
      let code = response?.data?.message || e?.message || ''
      if (response?.data instanceof Blob && response.data.type.includes('json')) {
        try { code = JSON.parse(await response.data.text()).message ?? code } catch { /* 保留原错误。 */ }
      }
      check()
      if (response?.status === 403 || ['FILE_CONTENT_NOT_AVAILABLE', 'FILE_CONTENT_INTEGRITY_UNAVAILABLE',
        'REQUIREMENT_DOCUMENT_UNAVAILABLE', 'UNPROVEN_G03'].includes(code)) clearPrivate()
      error.value = failureText(code)
    } catch { /* 丢弃上一个身份的响应。 */ }
  }
  finally { try { check(); syncPending(); busy.value = false } catch { if (live && epoch === generation) busy.value = false } }
}
function failureText(code: string) {
  const text: Record<string, string> = { REQUIREMENT_REVIEW_FROZEN: '此需求已送审，请先退回或由政策负责人调整评审。', REQUIREMENT_UNIT_REFERENCE_CHANGED: '单位版本已变化，请核对。', REQUIREMENT_RELATION_CYCLE: '该关系会回到自身需求，请核对关系方向。', REQUIREMENT_VERSION_CHANGED: '草稿已被修订，请重新打开当前版本。', REQUIREMENT_INTERVAL_EMPTY: '上下界或端点形成空区间，请核对原要求。', REQUIREMENT_NUMBER_INVALID: '请使用完整十进制数值，保留原精度。', REQUIREMENT_CLAUSE_NOT_FOUND: '所选条款不属于准确来源版本。', OBJECT_FORBIDDEN: '当前无权查看对象或其来源。', FIELD_READ_FORBIDDEN: '当前无权读取来源或需求定义。', CREATION_AUTHORITY_CHANGED: '创建范围已变化，请重新新建。', OPERATIONS_OUTCOME_UNKNOWN: '保存结果未确认，请查询原操作。' }
  const originErrors: Record<string, string> = {
    REQUIREMENT_ORIGIN_CHANGED: '所选版本或摘要与原件不符。',
    REQUIREMENT_ORIGINAL_TEXT_MISMATCH: '引用文本与指定原文位置不一致。',
    REQUIREMENT_ORIGIN_PENDING: '原件仍待核验。',
    COMMERCIAL_HISTORY_UNPROVEN: 'CRM 商业历史原件仍缺证。',
    REQUIREMENT_CRM_ACT_AUTHORITY_UNPROVEN: 'CRM 活动的当前读取授权仍缺证。',
    UNPROVEN_G03: 'CRM 活动附件的独立下载授权仍缺证。',
    REQUIREMENT_CLARIFICATION_IMMUTABLE: '已保存的原版澄清及答复须保留。',
    REQUIREMENT_COVERAGE_CHILD_UNLINKED: '所选子版缺少指向准确父版的条款映射。',
    REQUIREMENT_COVERAGE_PARENT_CHANGED: '父条款或来源版已变化，请重新复核。',
    REQUIREMENT_COVERAGE_DUPLICATE_CHILD: '每个子需求只选一个准确版本。',
  }
  return text[code] || originErrors[code] || (/^[A-Z][A-Z0-9_]+$/.test(code) ? '无法完成，请核对输入、权限和原操作。' : code) || '暂时无法完成。'
}
function missingText(m: string) {
  const [code, id] = m.split(':'); const text: Record<string, string> = { SOURCE_UNRESOLVED: '来源待澄清', SOURCE_LABEL_REQUIRED: '来源版本标记未定', ACQUISITION_BASIS_REQUIRED: '取得依据未定', REQUIREMENT_APPROVAL_PENDING: '需求尚待批准', APPLICABLE_OBJECT_REQUIRED: '适用对象未定', NECESSITY_REQUIRED: '必要性未定', SOURCE_CLARIFICATION_PENDING: '来源问题尚待澄清', CRITERIA_REQUIRED: '评价判据未编制', CRITERION_OBJECT_REQUIRED: '判据对象未定', CONDITIONS_REQUIRED: '适用条件未定', METHOD_REQUIREMENT_REQUIRED: '方法需求未定', BOUND_REQUIRED: '比较边界未定', ENDPOINT_REQUIRED: '端点是否包含未定', UNIT_BASIS_REQUIRED: '单位或依据未定', PRECISION_REQUIRED: '精度要求未定', QUALITATIVE_BASIS_REQUIRED: '定性依据未定', CRITERION_REVIEW_PENDING: '判据尚待评审', COVERAGE_BASIS_REQUIRED: '覆盖理由或必要性未定', UNCOVERED: '仍有未覆盖内容', RELATION_REVIEW_PENDING: '关系尚待评审', SOURCE_VERSION_CHANGED: '来源已改版，原引用保持不变，请核对', RELATION_VERSION_CHANGED: '关系目标已改版，请核对' }
  const details: Record<string, string> = {
    SOURCE_CLAUSES_REQUIRED: '待从准确原件提取条款',
    SOURCE_REPLY_AUTHORITY_UNPROVEN: '已关联答复证据，原渠道确认仍待核',
    CRITERION_SOURCE_BASIS_REQUIRED: '待核判据和方法的来源条款',
    PARENT_CLAUSE_COVERAGE_PENDING: '父条款覆盖待复核',
  }
  return (text[code!] || details[code!] || '待核对事项') + (id ? `（${id}）` : '')
}
function coverageLabel(state: string) {
  return { COVERED: '范围覆盖', MISSING: '缺覆盖证据', DISJOINT: '适用范围无交集', UNKNOWN: '范围或依据待核', PARTIAL: '部分覆盖', NECESSITY_UNPROVEN: '必要性或可放宽性待核' }[state] ?? '待核'
}
function termsLabel(terms: RequirementClauseTerms | null) {
  if (!terms) return '来源适用范围与必要性待核'
  const axis = (value: RequirementScopeAxis) => value.mode === 'VALUES' ? `${value.namespace} / ${value.keys.join('、')}` : value.mode === 'ALL' ? '全部' : '待核'
  const necessity = { MANDATORY: '必须', OPTIONAL: '可选', UNKNOWN: '待核' }[terms.necessity]
  const waiver = { PROHIBITED: '不可放宽', SOURCE_DECISION_REQUIRED: '须来源裁决', UNKNOWN: '待核' }[terms.waivability]
  return [`客户 ${axis(terms.customers)}`, `产品 ${axis(terms.products)}`, `配置 ${axis(terms.configurations)}`,
    `起止 ${terms.from ?? (terms.fromUnbounded ? '不限' : '待核')} 至 ${terms.until ?? (terms.untilUnbounded ? '不限' : '待核')}`,
    `必要性 ${necessity}`, `可放宽性 ${waiver}`].join(' · ')
}
function differenceLabel(path: string) {
  const labels: Record<string, string> = { name: '名称', origin: '原件引用', sourceDescription: '来源描述', sourceLabel: '来源标记', acquisitionBasis: '取得依据', sourceKind: '来源类别', source: '需求来源', clauseIds: '引用条款', interpretation: '工程解释', applicableObject: '适用对象', necessity: '必要性', questions: '待澄清问题', clarifications: '原版问答', criteria: '评价判据', relations: '关系', decomposition: '覆盖复核' }
  return path.startsWith('clauses/') ? `来源条款 ${path.slice(8)}` : labels[path] ?? path
}
function blankOrigin(): RequirementOrigin {
  return { reference: { owner: 'Main.File.Content.v1', id: '', version: '', digest: '' }, occurrenceId: null }
}
function changeSourceKind() {
  const draftSource = draft.value?.source
  if (!draftSource) return
  if (draftSource.kind === 'OWNER_ORIGINAL') { draftSource.origin ??= blankOrigin(); return }
  draftSource.origin = null
  if (!draftSource.clauses.length) draftSource.clauses.push({ id: '', locator: '', originalText: '' })
}
function retainPendingOrigin() {
  const draftSource = draft.value?.source
  if (!draftSource) return
  draftSource.origin = blankOrigin()
  draftSource.clauses = []
}
function savedQuestion(id: string) { return current.value?.body.requirement?.clarifications?.find(question => question.id === id) }
function addQuestion() {
  const definition = draft.value?.requirement
  if (!definition?.clauseIds[0]) return
  const questions = definition.clarifications ??= []
  questions.push({ id: crypto.randomUUID(), source: copy(definition.source), clauseId: definition.clauseIds[0], question: '' })
}
function changeRelationKind(relation: RequirementRelation) {
  if (relation.kind !== 'PARENT') relation.coverage = null
}
function addCoverageLink(relation: RequirementRelation) {
  const coverage = relation.coverage ??= []
  coverage.push({ parentClauseId: '', childClauseId: '', reason: '' })
}
function toggleCriterionSource(criterion: RequirementCriterion, clause: string, event: Event) {
  const selected = (event.target as HTMLInputElement).checked
  criterion.sourceClauseIds = selected ? [...(criterion.sourceClauseIds ?? []), clause] : (criterion.sourceClauseIds ?? []).filter(id => id !== clause)
}
function addCoverageChild() {
  const child = relations.value.find(v => key(v.reference) === relationKey.value)
  const definition = draft.value?.requirement
  if (!definition || !current.value || !child || child.reference.id === current.value.reference.id) return
  const review = definition.decomposition ??= { parent: copy(current.value.reference), children: [], basis: '' }
  if (!review.children.some(reference => reference.id === child.reference.id)) review.children.push(copy(child.reference))
  relationKey.value = ''
}
function removeCoverageChild(index: number) {
  const definition = draft.value?.requirement
  if (!definition?.decomposition) return
  definition.decomposition.children.splice(index, 1)
  if (!definition.decomposition.children.length) definition.decomposition = null
}
async function readDirectory(p: number, check: () => void) {
  const query = { kind: kind.value, search: search.value, page: p }
  const data = requirementResponse(await requirementApi.list(query.kind, query.search, query.page))
  check()
  rows.value = data
  displayedQuery.value = query
}
async function reload() { await run(async check => { clearPrivate(); if (!permission.loaded) await permission.loadMyActions(); check(); if (!canRead.value) throw new Error('当前无权读取工程需求。'); const s = requirementResponse(await requirementApi.scope()); check(); scope.value = s; await readDirectory(1, check) }) }
async function list(p: number) { await run(check => readDirectory(p, check)) }
async function loadEditor(v: RequirementSnapshot, check: () => void) {
  const origin = v.body.requirement ? requirementResponse(await requirementApi.read(v.body.requirement.source.id, v.body.requirement.source.version)) : null; check()
  resetEditor(); current.value = v; objectId.value = v.reference.id; draft.value = copy(v.body); source.value = origin
}
async function open(v: RequirementSnapshot) { await run(async check => { resetEditor(); const data = requirementResponse(await requirementApi.read(v.reference.id)); check(); await loadEditor(data, check) }) }
async function newDraft(k: RequirementOperationKind) { await run(async check => { resetEditor(); const c = requirementResponse(await requirementApi.creationContext()); check(); creation.value = c; objectId.value = crypto.randomUUID(); draft.value = { kind: k, name: '', source: k === 'SOURCE' ? { kind: 'UNRESOLVED', originDescription: '', sourceLabel: '', acquisitionBasis: '', clauses: [{ id: '', originalText: '', locator: '' }] } : null, requirement: null } }) }
async function findSources(p: number) { await run(async check => { sources.value = []; sourceKey.value = ''; const data = requirementResponse(await requirementApi.list('SOURCE', sourceSearch.value, p)); check(); sources.value = data; sourcePage.value = p }) }
async function chooseSource() {
  await run(async check => {
    const choice = sources.value.find(s => key(s.reference) === sourceKey.value)
    if (!choice || !draft.value) return
    const v = requirementResponse(await requirementApi.read(choice.reference.id, choice.reference.version))
    check()
    if (key(v.reference) !== key(choice.reference) || !v.body.source) throw new Error('来源已变化，请重新选择。')
    source.value = v
    const prior = draft.value.requirement
    if (prior && key(prior.source) === key(v.reference)) return
    draft.value.requirement = {
      ...(prior || { interpretation: '', applicableObject: '', necessity: '', clarificationQuestions: [] }),
      source: v.reference, clauseIds: [], decomposition: null,
      criteria: (prior?.criteria ?? []).map(criterion => ({ ...criterion, sourceClauseIds: null })),
      relations: (prior?.relations ?? []).map(relation => ({ ...relation, coverage: null })),
    }
  })
}
async function findRelations(p: number) { await run(async check => { relations.value = []; relationKey.value = ''; const data = requirementResponse(await requirementApi.list('REQUIREMENT', relationSearch.value, p)); check(); relations.value = data; relationPage.value = p }) }
function addRelation() { const r = relations.value.find(v => key(v.reference) === relationKey.value); if (r && draft.value?.requirement) draft.value.requirement.relations.push({ id: crypto.randomUUID(), kind: 'PARENT', target: copy(r.reference), coverageReason: '', uncovered: '', necessity: '' }); relationKey.value = '' }
function addCriterion() { draft.value?.requirement?.criteria.push({ id: crypto.randomUUID(), kind: 'QUANTITATIVE', object: '', conditions: '', methodRequirement: '', lower: null, lowerInclusive: null, upper: null, upperInclusive: null, proposedUnit: null, unitBasis: null, precision: null, qualitativeBasis: null }) }
function changeCriterion(c: RequirementCriterion) { c.evaluation = null; c.lower = c.upper = c.proposedUnit = c.unitBasis = c.precision = c.qualitativeBasis = null; c.lowerInclusive = c.upperInclusive = null }
async function save() { await run(async check => {
  if (!draft.value || !scope.value || pending.value) return
  const body = copy(draft.value)
  for (const c of body.requirement?.criteria || []) { for (const k of ['lower', 'upper', 'proposedUnit', 'unitBasis', 'precision', 'qualitativeBasis'] as const) if (c[k] === '') c[k] = null; if (c.lower === null) c.lowerInclusive = null; if (c.upper === null) c.upperInclusive = null }
  for (const relation of body.requirement?.relations ?? []) if (relation.coverage?.length === 0) relation.coverage = null
  const command: Omit<RequirementCommand, 'operationId'> = { objectId: objectId.value, action: current.value ? 'REVISE' : 'CREATE', expectedVersion: current.value?.reference.version ?? null, expectedEtag: current.value?.etag ?? null, expectedAuthorityVersion: creation.value?.authorityVersion ?? null, departmentId: creation.value ? department.value || null : null, reason: reason.value, body }
  const result = await saveRequirementOperation(scope.value, body.kind, objectId.value, check, operationId => requirementApi.save({ ...command, operationId })); check()
  if (result.result) { const v = requirementResponse(await requirementApi.read(result.result.objectId)); check(); await loadEditor(v, check); notice.value = '草稿已保存，批准与满足状态保持待确认。' }
}) }
async function recover(close: boolean) { await run(async check => { if (!scope.value || !pending.value) return; const result = await resolveRequirementOperation(scope.value, pending.value.kind, close, check); check(); if (result.result) { const v = requirementResponse(await requirementApi.read(result.result.objectId)); check(); await loadEditor(v, check) } notice.value = result.state === 'UNKNOWN' ? '结果仍未确认，请保留原操作。' : result.state === 'CLOSED' ? '原操作已结束，可重新编制。' : '已取得原保存结果。' }) }
async function readHistory() { await run(async check => { if (!current.value) return; history.value = null; diff.value = null; const v = requirementResponse(await requirementApi.read(current.value.reference.id, historyVersion.value)); check(); history.value = v }) }
async function compareVersions() {
  await run(async check => {
    if (!current.value || !history.value) return
    const value = requirementResponse(await requirementApi.diff(current.value.reference.id, history.value.reference.version, current.value.reference.version))
    check()
    diff.value = value
  })
}
function downloadBlob(blob: Blob, name: string) {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = name
  link.click()
  URL.revokeObjectURL(url)
}
async function downloadOriginal(snapshot: RequirementSnapshot, question?: string) {
  await run(async check => {
    const blob = await requirementApi.original(snapshot.reference.id, snapshot.reference.version, question)
    check()
    downloadBlob(blob, question ? '来源答复原件' : `来源原件-${snapshot.reference.version}`)
  })
}
async function exportPage() {
  const query = displayedQuery.value
  if (!query) return
  await run(async check => {
    const blob = await requirementApi.exportPage(query.kind, query.search, query.page)
    check()
    downloadBlob(blob, '工程需求.json')
  })
}
function invalidate() { generation++; busy.value = false; error.value = ''; clearPrivate() }
watch(() => [permission.loaded, canRead.value], () => { if (!canRead.value) invalidate() })
onMounted(() => { window.addEventListener(AUTH_EVENT, invalidate); void reload() })
onBeforeUnmount(() => { live = false; generation++; window.removeEventListener(AUTH_EVENT, invalidate) })
</script>

<style scoped>
.requirements{max-width:1200px;margin:auto;padding:24px;color:var(--el-text-color-primary,#253047)}header,.inline{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap}section,article{border:1px solid var(--el-border-color,#d8dce3);border-radius:8px;padding:18px;margin:18px 0}article{background:var(--el-fill-color-light,#f7f9fc)}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:14px}label{display:flex;flex-direction:column;gap:6px;margin:10px 0}input,textarea,select,button{font:inherit;padding:8px;border:1px solid var(--el-border-color,#c4cbd6);border-radius:5px}textarea{min-height:80px}button{cursor:pointer;margin:5px}button:disabled{cursor:default;opacity:.55}fieldset{border:0;padding:0;min-width:0}table{width:100%;text-align:left;border-collapse:collapse}th,td{border-bottom:1px solid #d8dce3;padding:10px}blockquote{white-space:pre-wrap;overflow-wrap:anywhere;margin:12px}p,li{overflow-wrap:anywhere}[role=alert]{color:#b42318}[role=status]{color:#167044}@media(max-width:640px){.requirements{padding:12px}section,article{padding:12px}.grid{grid-template-columns:1fr}}
pre { white-space: pre-wrap; overflow-wrap: anywhere; }
</style>
