<template>
  <main class="routing-page" :aria-busy="busy">
    <header><div><h1>版本化工艺</h1><p>维护工程修订的工艺步骤、前后依赖与准确来源，保留每次修改的历史。</p></div><button :disabled="busy" @click="reload">刷新</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <section v-if="pending"><p>上次保存结果尚未确认，请先恢复原操作。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy" @click="recover(true)">结束未提交操作</button></section>
    <section><header><h2>工艺目录</h2><button v-if="permissions.has('plm-engineering-routing:add')" :disabled="locked || !scope" @click="newRoute">新建工艺</button></header>
      <form class="inline" @submit.prevent="loadPage(1)"><label>工艺名称<input v-model="search" aria-label="工艺搜索" :disabled="busy" maxlength="256" /></label><button :disabled="busy || !canRead">搜索</button></form>
      <div class="scroll"><table><thead><tr><th>名称</th><th>版本</th><th>步骤</th><th>操作</th></tr></thead><tbody><tr v-for="row in rows" :key="row.reference.id"><td>{{ row.body.name }}</td><td>{{ row.reference.version }}</td><td>{{ row.body.operations.length }}</td><td><button :disabled="busy" @click="open(row.reference.id)">打开工艺</button></td></tr></tbody></table></div>
      <p v-if="!rows.length">当前范围内没有工艺。</p><nav><button :disabled="busy || page === 1" @click="loadPage(page - 1)">上一页</button><span>第 {{ page }} 页</span><button :disabled="busy || rows.length < 20" @click="loadPage(page + 1)">下一页</button></nav>
    </section>
    <section v-if="editing"><h2>{{ current ? '修订工艺' : '新建工艺草稿' }}</h2><p>草稿尚未评审或冻结。保存缺项不会表示工艺已获准生产。</p>
      <p v-if="body">工程修订 {{ body.revisionOrigin.id }} · 来源版本 {{ body.revisionOrigin.version }}；产品 {{ body.productRef.id }} · 版本 {{ body.productRef.version }}</p>
      <form class="inline" @submit.prevent="findParents"><label>工程修订编号<input v-model="parentSearch" aria-label="工程修订搜索" :disabled="locked" /></label><button :disabled="locked || !permissions.has('plm-engineering-revision:query')">查找工程修订</button></form>
      <label>准确工程修订<select aria-label="工程修订选择" :disabled="locked || !canWrite" :value="''" @change="chooseParent(($event.target as HTMLSelectElement).value)"><option value="">请选择已有产品来源的当前草稿</option><option v-for="p in parents" :key="key(p.reference)" :value="key(p.reference)">{{ p.revisionCode }} · v{{ p.reference.version }} · {{ p.draft.goal }}</option></select></label>
      <p v-if="current">若工程组成已变化，请重新查找并明确选择同一修订的当前版本。已录入步骤会保留。</p>
      <ul v-if="current?.missing.length" aria-label="工艺缺项"><li v-for="item in current.missing" :key="item">{{ finding(item) }}</li></ul>
      <form v-if="body" @submit.prevent="save"><fieldset :disabled="locked || !canWrite"><div class="grid">
        <label>工艺名称<input v-model="body.name" aria-label="工艺名称" required maxlength="256" /></label><label>制造视图要求<textarea v-model="body.manufacturingViewRequirement" aria-label="制造视图要求" maxlength="4000" /></label>
        <label>修改原因<textarea v-model="reason" aria-label="工艺修改原因" required maxlength="4000" /></label>
        <label>场所<select :value="body.siteRef ? key(body.siteRef) : ''" aria-label="工艺场所" @change="chooseSite(($event.target as HTMLSelectElement).value)"><option value="">暂缺场所</option><option v-if="body.siteRef && !sites.some(s => key(s.reference) === key(body!.siteRef!))" :value="key(body.siteRef)">已保存场所 · v{{ body.siteRef.version }}</option><option v-for="s in sites" :key="key(s.reference)" :value="key(s.reference)">{{ s.code }} / {{ s.name }} · v{{ s.reference.version }}</option></select></label>
      </div><button type="button" @click="findSites">查找当前场所</button>
      <article v-for="(step, index) in body.operations" :key="step.occurrenceId" class="step"><header><h3>步骤 {{ index + 1 }} · {{ step.name }}</h3><button type="button" @click="body.operations.splice(index, 1)">移除步骤</button></header>
        <div class="grid"><label>步骤名称<input v-model="step.name" :aria-label="`步骤名称 ${index + 1}`" required maxlength="256" /></label><label>工序代码<input v-model="step.processCode" :aria-label="`工序代码 ${index + 1}`" maxlength="100" /></label>
          <label>语义顺序<input v-model.number="step.semanticOrder" :aria-label="`步骤顺序 ${index + 1}`" type="number" min="0" step="1" required /></label>
          <label>前置步骤<select v-model="step.dependsOn" :aria-label="`前置步骤 ${index + 1}`" multiple><option v-for="p in body.operations.filter(p => p.occurrenceId !== step.occurrenceId)" :key="p.occurrenceId" :value="p.occurrenceId">{{ p.semanticOrder }} / {{ p.name }}</option><option v-for="id in step.dependsOn.filter(id => !body!.operations.some(p => p.occurrenceId === id))" :key="id" :value="id">已移除步骤 {{ id }}</option></select></label>
          <label>替代步骤组<input :value="step.alternativeGroup || ''" :aria-label="`替代步骤组 ${index + 1}`" maxlength="256" @input="step.alternativeGroup = ($event.target as HTMLInputElement).value || null" /></label>
          <label>适用性<select v-model="step.applicability" :aria-label="`步骤适用性 ${index + 1}`"><option value="UNKNOWN">待确定</option><option value="APPLIES">拟适用</option><option value="NOT_APPLICABLE">拟不适用</option></select></label>
          <label>适用性依据<textarea v-model="step.reason" :required="step.applicability === 'NOT_APPLICABLE'" maxlength="8000" /></label><label>工艺说明<textarea v-model="step.instructions" :aria-label="`工艺说明 ${index + 1}`" maxlength="8000" /></label><label>受控文档要求<textarea v-model="step.documentRequirement" maxlength="4000" /></label>
        </div>
        <h4>设备与工作中心</h4><ul><li v-for="(r, i) in step.resources" :key="key(r)">{{ resourceName(r) }} · v{{ r.version }} <button type="button" @click="step.resources.splice(i, 1)">移除资源</button></li></ul>
        <label>添加准确资源<select :aria-label="`步骤资源 ${index + 1}`" :value="''" @change="addResource(step, ($event.target as HTMLSelectElement).value)"><option value="">从下方查找结果中选择</option><option v-for="r in resources" :key="key(r.reference)" :value="key(r.reference)">{{ resourceName(r.reference) }} · v{{ r.reference.version }}</option></select></label>
        <h4>工具技术规格</h4><ul><li v-for="(r, i) in step.tools" :key="key(r)">{{ toolName(r) }} · v{{ r.version }} <button type="button" @click="step.tools.splice(i, 1)">移除工具</button></li></ul>
        <label>添加准确规格<select :aria-label="`步骤工具 ${index + 1}`" :value="''" @change="addTool(step, ($event.target as HTMLSelectElement).value)"><option value="">请选择本产品的准确技术草稿</option><option v-for="t in tools" :key="key(t.specification!.reference)" :value="key(t.specification!.reference)">{{ t.source.source.identity.number }} · v{{ t.specification!.reference.version }}</option></select></label>
        <h4>参数与精确单位</h4><div v-for="(p, i) in step.parameters" :key="i" class="parameter"><div class="grid"><label>参数名<input v-model="p.name" :aria-label="`参数名 ${index + 1}-${i + 1}`" required maxlength="256" /></label>
          <label v-for="field in numericFields" :key="field.key">{{ field.name }}<input :value="p[field.key] ?? ''" :aria-label="`${field.name} ${index + 1}-${i + 1}`" inputmode="decimal" maxlength="2050" @input="p[field.key] = ($event.target as HTMLInputElement).value || null" /></label>
          <label>单位<select :value="p.unitRef ? key(p.unitRef) : ''" :aria-label="`参数单位 ${index + 1}-${i + 1}`" @change="chooseUnit(p, ($event.target as HTMLSelectElement).value)"><option value="">待确认</option><option v-if="p.unitRef && !units.some(u => key(u.reference) === key(p.unitRef!))" :value="key(p.unitRef)">已保存单位 · v{{ p.unitRef.version }}</option><option v-for="u in units" :key="key(u.reference)" :value="key(u.reference)">{{ u.profile.code }} / {{ u.profile.dimensionKey }} · v{{ u.reference.version }}</option></select></label>
          <label>测量或计算方法<textarea v-model="p.method" maxlength="4000" /></label></div><button type="button" @click="step.parameters.splice(i, 1)">移除参数</button></div>
        <button type="button" @click="step.parameters.push({ name: '', value: null, minimum: null, maximum: null, unitRef: null, method: '' })">添加参数</button>
      </article><button type="button" @click="addStep">添加工艺步骤</button><button>保存工艺版本</button></fieldset></form>
    </section>
    <section v-if="body && editing"><h2>查找准确来源</h2><p>选择结果中的具体版本。资源身份和技术草稿仍需后续资格核准。</p>
      <form class="inline" @submit.prevent="findResources(1)"><label>资源类型<select v-model="resourceKind" :disabled="locked"><option value="MACHINE">设备</option><option value="WORK_CENTER">工作中心</option></select></label><label>编码或名称<input v-model="resourceSearch" aria-label="资源搜索" :disabled="locked" /></label><button :disabled="locked">查找资源</button><button type="button" :disabled="locked || !moreResources" @click="findResources(resourcePage + 1)">更多资源</button></form>
      <form class="inline" @submit.prevent="findTools(1)"><label>工具编号<input v-model="toolSearch" aria-label="规格搜索" :disabled="locked" /></label><button :disabled="locked">查找工具规格</button><button type="button" :disabled="locked || !moreTools" @click="findTools(toolPage + 1)">更多规格</button></form>
      <form class="inline" @submit.prevent="findUnits(1)"><label>单位编码<input v-model="unitSearch" aria-label="单位搜索" :disabled="locked" /></label><button :disabled="locked">查找单位</button><button type="button" :disabled="locked || !moreUnits" @click="findUnits(unitPage + 1)">更多单位</button></form>
      <h3>采用现有产品工艺</h3><p>先选择已确认的产品映射，再预览原工艺。原来的资源编号保留在原件中，仍需明确选择准确资源。</p><button :disabled="locked || !canWrite" @click="findMappings">查找确认映射</button>
      <label>经营产品映射<select v-model="mappingChoice" aria-label="工艺导入映射" :disabled="locked || !canWrite"><option value="">请选择</option><option v-for="m in mappings" :key="key(m.reference)" :value="key(m.reference)">{{ m.candidate.purpose }} · {{ m.candidate.businessRef?.id }} · v{{ m.reference.version }}</option></select></label>
      <button :disabled="locked || !mappingChoice || !canWrite" @click="previewLegacy">预览现有工艺</button>
      <div v-if="preview"><p>原件含 {{ preview.rows.length }} 个步骤。采用后替换本页步骤，提交时再次核对原件是否变化。</p><ol><li v-for="s in preview.suggestedOperations" :key="s.occurrenceId">{{ s.name }} / {{ s.processCode }}<span v-for="p in s.parameters" :key="p.name"> · {{ p.name }}：{{ p.value }}</span></li></ol><button :disabled="locked || !canWrite" @click="adoptLegacy">采用预览步骤</button></div>
    </section>
    <section v-if="current"><h2>历史与差异</h2><form class="inline" @submit.prevent="readHistory"><label>历史版本<input v-model="historyVersion" aria-label="工艺历史版本" pattern="[1-9][0-9]*" required /></label><button :disabled="busy">读取历史</button></form>
      <div v-if="history"><h3>{{ history.body.name }} · v{{ history.reference.version }}</h3><p>{{ history.reason }}</p><ol><li v-for="s in history.body.operations" :key="s.occurrenceId">{{ s.semanticOrder }} / {{ s.name }} / {{ s.instructions }}</li></ol><details><summary>保存的原件</summary><pre>{{ history.originalJson }}</pre></details></div>
      <form class="inline" @submit.prevent="readDiff"><label>起始版本<input v-model="beforeVersion" aria-label="工艺差异起始版本" pattern="[1-9][0-9]*" required /></label><label>结束版本<input v-model="afterVersion" aria-label="工艺差异结束版本" pattern="[1-9][0-9]*" required /></label><button :disabled="busy">比较版本</button></form>
      <div v-if="diff"><p v-if="!diff.changes.length">两个版本没有内容差异。</p><details v-for="change in diff.changes" :key="change.path"><summary>{{ change.path }}</summary><div class="grid"><pre>{{ change.before }}</pre><pre>{{ change.after }}</pre></div></details></div>
    </section>
  </main>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { routingApi, type RoutingBody, type RoutingSnapshot, type RoutingLegacy, type RoutingStep, type RoutingParameter, type RoutingCommand } from '@/api/plm/engineeringRouting'
import { revisionApi, type RevisionSnapshot, type RevisionDiff } from '@/api/plm/engineeringRevisions'
import { engineeringApi, type EngineeringReference, type EngineeringScope, type MappingVersion, type SourceChoice } from '@/api/plm/engineeringProducts'
import { resourceApi, type ResourceKind, type ResourceVersion, type ResourceReference } from '@/api/mes/resourceSources'
import { toolSpecificationApi, type ToolSpecificationView } from '@/api/plm/toolSpecifications'
import { uomApi, type UomVersion } from '@/api/erp/uomProfiles'
import { pendingRouting, routingResponse as result, saveRouting, resolveRouting, type PendingRouting } from '@/auth/engineeringRoutingRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const permissions = usePermissionStore(), canRead = computed(() => permissions.loaded && permissions.has('plm-engineering-routing:query'))
const canWrite = computed(() => canRead.value && permissions.has(`plm-engineering-routing:${current.value ? 'edit' : 'add'}`))
const busy = ref(false), error = ref(''), notice = ref(''), scope = ref<EngineeringScope | null>(null), pending = ref<PendingRouting | null>(null), locked = computed(() => busy.value || !!pending.value)
const rows = ref<RoutingSnapshot[]>([]), search = ref(''), page = ref(1), current = ref<RoutingSnapshot | null>(null), body = ref<RoutingBody | null>(null), editing = ref(false), id = ref(''), reason = ref('')
const parentSearch = ref(''), parents = ref<RevisionSnapshot[]>([]), sites = ref<SourceChoice[]>([]), resources = ref<ResourceVersion[]>([]), tools = ref<ToolSpecificationView[]>([]), units = ref<UomVersion[]>([])
const resourceKind = ref<ResourceKind>('MACHINE'), resourceSearch = ref(''), toolSearch = ref(''), unitSearch = ref(''), resourcePage = ref(1), toolPage = ref(1), unitPage = ref(1), moreResources = ref(false), moreTools = ref(false), moreUnits = ref(false)
const mappings = ref<MappingVersion[]>([]), mappingChoice = ref(''), preview = ref<RoutingLegacy | null>(null), adopted = ref<RoutingLegacy | null>(null)
const history = ref<RoutingSnapshot | null>(null), historyVersion = ref('1'), beforeVersion = ref('1'), afterVersion = ref('1'), diff = ref<RevisionDiff | null>(null)
const numericFields = [{ key: 'value', name: '精确值' }, { key: 'minimum', name: '下限' }, { key: 'maximum', name: '上限' }] as const
const copy = <T,>(v: T): T => JSON.parse(JSON.stringify(v)) as T
const key = (v: EngineeringReference & { kind?: string }) => `${v.owner}:${v.kind ? v.kind + ':' : ''}${v.id}:${v.version}:${v.digest}`
let generation = 0, live = true
function finding(s: string) {
  const labels: Record<string, string> = { OPERATIONS_REQUIRED: '工艺步骤待补齐', SITE_REQUIRED: '场所待确认', MANUFACTURING_VIEW_OWNER_REQUIRED: '制造视图来源待补齐', CONTROLLED_DOCUMENT_OWNER_REQUIRED: '受控文档来源待补齐', DEPENDENCY_CYCLE_OR_UNRESOLVED: '前置步骤存在循环或缺项', DEPENDENCY_MISSING: '前置步骤已缺失', DEPENDENCY_ORDER_CONFLICT: '顺序与前置步骤冲突', PARAMETER_LIMIT_CONFLICT: '参数值与上下限冲突', PARAMETER_UNIT_REQUIRED: '参数单位待确认', PARAMETER_METHOD_REQUIRED: '参数方法待补齐', PARAMETER_VALUE_REQUIRED: '参数值或范围待补齐', PARAMETER_UNIT_PRECISION_CONFLICT: '参数精度与单位不符', RESOURCE_REQUIRED: '资源待选择', RESOURCE_QUALIFICATION_REQUIRED: '资源生产资格待核准', TOOL_APPROVAL_AND_PHYSICAL_QUALIFICATION_REQUIRED: '工具批准及实物资格待核准', APPLICABILITY_OWNER_REQUIRED: '适用性或替代关系待核准', LEGACY_TECHNICAL_REVIEW_REQUIRED: '导入工艺的顺序、单位及技术要求待核准', PROCESS_REQUIRED: '工序待补齐' }
  const code = s.split(':')[0] ?? s
  return labels[code] || (code.endsWith('SOURCE_CHANGED') ? '引用来源已变化，请重新核对' : '来源或资格仍待核准')
}
function clearEditor() { current.value = null; body.value = null; editing.value = false; id.value = ''; reason.value = ''; parents.value = []; parentSearch.value = ''; sites.value = []; resources.value = []; tools.value = []; units.value = []; mappings.value = []; mappingChoice.value = ''; preview.value = null; adopted.value = null; history.value = null; diff.value = null; resourceSearch.value = ''; toolSearch.value = ''; unitSearch.value = ''; moreResources.value = moreTools.value = moreUnits.value = false }
function clearPrivate() { clearEditor(); rows.value = []; search.value = ''; page.value = 1 }
function syncPending() { pending.value = scope.value ? pendingRouting(scope.value) : null }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const stamp = identityStamp(), epoch = generation
  const check = () => { requireIdentity(stamp); if (!live || generation !== epoch || !authenticationCanRead()) throw new Error('当前身份已变化。') }
  busy.value = true; error.value = ''; notice.value = ''
  try { check(); await action(check); check() }
  catch (failure: any) { try { check(); const status = failure?.response?.status; if ([401, 403].includes(status)) clearPrivate(); error.value = [401, 403].includes(status) ? '当前无权读取工艺或引用来源。' : status === 412 ? '工艺或来源版本已变化，请查询原操作后重新核对。' : /^[A-Z][A-Z0-9_]+$/.test(failure?.response?.data?.message || '') ? '请核对输入、来源和原操作状态。' : failure?.message || '操作结果尚未确认。' } catch { /* Ignore old identity responses. */ } }
  finally { try { check(); syncPending(); busy.value = false } catch { if (live && epoch === generation) busy.value = false } }
}
function setCurrent(value: RoutingSnapshot) { clearEditor(); current.value = value; body.value = copy(value.body); editing.value = true; id.value = value.reference.id; afterVersion.value = value.reference.version }
async function reload() { await run(async check => { clearPrivate(); scope.value = null; if (!permissions.loaded) await permissions.loadMyActions(); check(); if (!canRead.value) throw new Error('当前没有读取工艺的权限。'); const s = result(await routingApi.scope()); check(); scope.value = s; const list = result(await routingApi.list(undefined, search.value, page.value)); check(); rows.value = list }) }
async function loadPage(target: number) { await run(async check => { rows.value = []; const list = result(await routingApi.list(undefined, search.value, target)); check(); rows.value = list; page.value = target }) }
async function open(value: string) { await run(async check => { clearEditor(); const saved = result(await routingApi.read(value)); check(); if (saved.tenantId !== scope.value?.tenantId) throw new Error('租户已变化。'); setCurrent(saved) }) }
function newRoute() { if (locked.value) return; clearEditor(); editing.value = true; id.value = crypto.randomUUID() }
async function findParents() { await run(async check => { parents.value = []; const values = result(await revisionApi.list(parentSearch.value)); check(); parents.value = values.filter(p => p.isCurrent && p.state === 'DRAFT' && p.draft.productRef && (!current.value || p.reference.id === current.value.body.revisionOrigin.id)) }) }
function chooseParent(selected: string) { if (locked.value || !canWrite.value) return; const p = parents.value.find(p => key(p.reference) === selected); if (!p?.draft.productRef) return; body.value = body.value ? { ...body.value, revisionOrigin: copy(p.reference), productRef: copy(p.draft.productRef) } : { name: '', revisionOrigin: copy(p.reference), productRef: copy(p.draft.productRef), siteRef: null, manufacturingViewRequirement: '', operations: [] }; preview.value = adopted.value = null }
function addStep() { if (!body.value) return; body.value.operations.push({ occurrenceId: crypto.randomUUID(), name: '', processCode: '', semanticOrder: Math.max(0, ...body.value.operations.map(s => s.semanticOrder)) + 10, dependsOn: [], alternativeGroup: null, applicability: 'UNKNOWN', reason: '', instructions: '', resources: [], tools: [], parameters: [], documentRequirement: '', legacyRowId: null }) }
async function findSites() { await run(async check => { sites.value = []; const values = result(await engineeringApi.siteChoices('')); check(); sites.value = values.filter(s => s.enabled) }) }
function chooseSite(selected: string) { if (!body.value) return; body.value.siteRef = selected ? copy(sites.value.find(s => key(s.reference) === selected)?.reference ?? body.value.siteRef) : null }
async function findResources(target: number) { const kind = resourceKind.value; await run(async check => { if (target === 1) resources.value = []; const values = result(await resourceApi(kind).list(resourceSearch.value, target)); check(); resources.value.push(...values.filter(r => r.isCurrent && r.sourcePresent && r.sourceEnabled)); resourcePage.value = target; moreResources.value = values.length === 20 }) }
function resourceName(r: ResourceReference) { const v = resources.value.find(v => key(v.reference) === key(r)); return `${r.kind === 'MACHINE' ? '设备' : '工作中心'} / ${v?.source.master.name || v?.source.master.code || r.id}` }
function addResource(step: RoutingStep, selected: string) { const r = resources.value.find(r => key(r.reference) === selected); if (r && !step.resources.some(s => s.kind === r.reference.kind && s.id === r.reference.id)) step.resources.push(copy(r.reference)) }
async function findTools(target: number) { await run(async check => { if (target === 1) tools.value = []; const values = result(await toolSpecificationApi.list(toolSearch.value, target)); check(); tools.value.push(...values.filter(t => t.isCurrent && t.sourceMatches && t.productMatches && t.specification && body.value && key(t.specification.requirements.productRef) === key(body.value.productRef))); toolPage.value = target; moreTools.value = values.length === 20 }) }
function toolName(r: EngineeringReference) { return tools.value.find(t => t.specification && key(t.specification.reference) === key(r))?.source.source.identity.number || r.id }
function addTool(step: RoutingStep, selected: string) { const t = tools.value.find(t => t.specification && key(t.specification.reference) === selected)?.specification; if (t && !step.tools.some(s => s.id === t.reference.id)) step.tools.push(copy(t.reference)) }
async function findUnits(target: number) { await run(async check => { if (target === 1) units.value = []; const values = result(await uomApi.list(unitSearch.value, target)); check(); units.value.push(...values.filter(u => u.isCurrent && u.profile.enabled)); unitPage.value = target; moreUnits.value = values.length === 20 }) }
function chooseUnit(p: RoutingParameter, selected: string) { if (!selected) p.unitRef = null; else { const u = units.value.find(u => key(u.reference) === selected); if (u) p.unitRef = copy(u.reference) } }
async function findMappings() { const product = body.value?.productRef; if (!product) return; await run(async check => { mappings.value = []; mappingChoice.value = ''; preview.value = null; const values = result(await engineeringApi.mappings(product.id)); check(); mappings.value = values.filter(m => m.canAdoptIdentitySite && key(m.candidate.engineeringRef) === key(product)) }) }
async function previewLegacy() { const mapping = mappings.value.find(m => key(m.reference) === mappingChoice.value); if (!mapping) return; await run(async check => { preview.value = null; const value = result(await routingApi.legacy(mapping.reference)); check(); preview.value = value }) }
function adoptLegacy() { if (!body.value || !preview.value || locked.value || !canWrite.value) return; adopted.value = copy(preview.value); body.value.operations = copy(preview.value.suggestedOperations); const mapping = mappings.value.find(m => key(m.reference) === key(preview.value!.mappingRef)); body.value.siteRef = copy(mapping?.candidate.siteRef ?? null); notice.value = '预览步骤已放入草稿，请核对顺序、单位和资源后保存。' }
async function save() {
  const s = scope.value, draft = body.value; if (!s || !draft || !canWrite.value || pending.value) return
  const command: Omit<RoutingCommand, 'operationId'> = { routingId: id.value, action: current.value ? 'REVISE' : 'CREATE', expectedVersion: current.value?.reference.version ?? null, expectedEtag: current.value?.etag ?? null, body: copy(draft), reason: reason.value, legacyMappingRef: adopted.value?.mappingRef ?? null, legacyDigest: adopted.value?.digest ?? null }
  await run(async check => { const guard = () => { check(); if (!canWrite.value) throw new Error('当前无权保存工艺。') }; await saveRouting(s, command.routingId, guard, operationId => routingApi.save({ ...command, operationId })); check(); const saved = result(await routingApi.read(command.routingId)); check(); setCurrent(saved); rows.value = []; const values = result(await routingApi.list()); check(); rows.value = values; page.value = 1; notice.value = '工艺版本已保存，评审与冻结仍待完成。' })
}
async function recover(close: boolean) { const s = scope.value; if (!s) return; await run(async check => { const original = await resolveRouting(s, close, check); check(); if (original.state === 'COMMITTED') clearEditor(); notice.value = original.state === 'COMMITTED' ? '原操作已提交，请重新打开工艺。' : original.state === 'CLOSED' ? '原操作已结束，可以继续编辑。' : '结果仍未知，请继续查询或结束未提交操作。' }) }
async function readHistory() { const route = current.value; if (!route) return; await run(async check => { history.value = null; const value = result(await routingApi.read(route.reference.id, historyVersion.value)); check(); history.value = value }) }
async function readDiff() { const route = current.value; if (!route) return; await run(async check => { diff.value = null; const value = result(await routingApi.diff(route.reference.id, beforeVersion.value, afterVersion.value)); check(); diff.value = value }) }
function clear() { generation++; busy.value = false; clearPrivate(); scope.value = null; pending.value = null; error.value = ''; notice.value = '' }
function identityChanged() { clear(); if (authenticationCanRead()) void reload() }
function storage(e: StorageEvent) { if (e.key?.startsWith('cp6_engineering_routing_operation_v1:')) { try { syncPending() } catch { error.value = '原操作记录异常，请保留记录。' } } }
watch(canRead, allowed => { if (!allowed) clear() })
onMounted(() => { window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', storage); void reload() })
onBeforeUnmount(() => { live = false; generation++; clearPrivate(); window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', storage) })
</script>
<style scoped>
.routing-page{display:grid;gap:18px;padding:24px;color:var(--el-text-color-primary)}header,.inline,nav{display:flex;gap:12px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}section,.step{padding:18px;border:1px solid var(--el-border-color);border-radius:8px;background:var(--el-bg-color)}.step{margin:18px 0}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:14px}label{display:grid;gap:6px;margin:8px 0}input,select,textarea,button{font:inherit;padding:8px;border:1px solid var(--el-border-color);border-radius:4px;color:inherit;background:var(--el-bg-color);max-width:100%;box-sizing:border-box}button{margin:4px;cursor:pointer}button:disabled{opacity:.5;cursor:default}fieldset{border:0;padding:0}.scroll{overflow:auto}table{width:100%;border-collapse:collapse}td,th{text-align:left;padding:12px;border-bottom:1px solid var(--el-border-color)}.parameter{border-top:1px solid var(--el-border-color);margin-top:16px}pre{white-space:pre-wrap;overflow-wrap:anywhere;max-height:400px;overflow:auto}[role=alert]{color:var(--el-color-danger)}:focus-visible{outline:2px solid var(--el-color-primary);outline-offset:2px}@media(max-width:600px){.routing-page{padding:12px}section,.step{padding:12px}}
</style>
