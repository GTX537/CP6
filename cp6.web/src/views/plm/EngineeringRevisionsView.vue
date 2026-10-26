<template>
  <div class="revision-page" :aria-busy="busy">
    <header><div><h1>工程修订与工作快照</h1><p>逐次保存设计草稿及修改原因，保留每个组成位置和历史版本。</p></div><button :disabled="busy" @click="reload">刷新</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <section v-if="pending" aria-label="原操作恢复"><p>上次保存结果尚未确认。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy" @click="recover(true)">结束未提交操作</button></section>
    <section>
      <header><h2>修订目录</h2><button v-if="permissions.has('plm-engineering-revision:add')" :disabled="locked || !scope" @click="openNew">新建修订</button></header>
      <form class="inline" @submit.prevent="loadList(1)"><label>修订编号<input v-model="search" aria-label="修订编号搜索" maxlength="256" :disabled="busy" /></label><button :disabled="busy || !canRead">搜索</button></form>
      <div class="table-scroll"><table><thead><tr><th>修订编号</th><th>工作版本</th><th>设计目标</th><th>操作</th></tr></thead><tbody>
        <tr v-for="row in rows" :key="row.reference.id"><td>{{ row.revisionCode }}</td><td>{{ row.reference.version }}</td><td>{{ row.draft.goal }}</td><td><button :disabled="busy" @click="open(row)">打开</button></td></tr>
      </tbody></table></div><p v-if="!rows.length">当前范围内没有工程修订。</p>
      <nav class="inline" aria-label="修订分页"><button :disabled="busy || page === 1 || !canRead" @click="loadList(page - 1)">上一页</button><span>第 {{ page }} 页</span><button :disabled="busy || rows.length < 20 || !canRead" @click="loadList(page + 1)">下一页</button></nav>
    </section>
    <section v-if="creating || current">
      <h2>{{ creating ? '新产品修订草稿' : current?.revisionCode }}</h2>
      <p v-if="current">工作版本 {{ current.reference.version }} · 草稿 · 尚未评审或冻结</p>
      <p>此处保存工作草稿。配置规则、完整组成来源和适用性核准齐备后，才能进入后续评审。</p>
      <ul v-if="current?.missing.length" aria-label="草稿缺项"><li v-for="item in current.missing" :key="item">{{ missingText(item) }}</li></ul>
      <form @submit.prevent="saveGoal"><fieldset :disabled="locked || (!creating && !canEdit)"><div class="grid">
        <label>设计目标<textarea v-model="goal" aria-label="设计目标" maxlength="8000" required /></label>
        <label v-if="creating">无父基线原因<textarea v-model="noParentReason" aria-label="无父基线原因" maxlength="8000" required /></label>
        <label v-if="creating && creation">所属部门<select v-model="department" aria-label="修订所属部门" :required="!creation.allowsUnbound"><option value="">{{ creation.allowsUnbound ? '不绑定部门' : '请选择授权部门' }}</option><option v-for="d in creation.departments" :key="d.id" :value="d.id">{{ d.code }} / {{ d.name }}</option></select></label>
        <label>修改原因<textarea v-model="reason" aria-label="修改原因" maxlength="8000" required /></label>
      </div><button>{{ creating ? '创建草稿' : '保存设计目标' }}</button></fieldset></form>
      <p>工程产品：{{ current?.draft.productRef ? `${current.draft.productRef.id} · 来源版本 ${current.draft.productRef.version}` : '尚未解析，可继续保存草稿' }}。绑定后保留同一产品身份。</p>
      <form class="inline" @submit.prevent="findProducts"><label>工程产品搜索<input v-model="productSearch" aria-label="工程产品搜索" :disabled="busy" /></label><button :disabled="busy || !permissions.has('plm-engineering-product:query')">查找工程产品</button></form>
      <div class="inline"><label>本修订的工程产品<select v-model="rootProduct" aria-label="本修订的工程产品" :disabled="locked"><option value="">请选择准确来源；新草稿也可暂不选择</option><option v-for="row in products.filter(p => !current?.draft.productRef || p.reference.id === current.draft.productRef.id)" :key="refKey(row.reference)" :value="refKey(row.reference)">{{ row.product.code }} / {{ row.product.name }} · {{ row.reference.id }} · v{{ row.reference.version }}</option></select></label>
        <button v-if="current && canEdit" :disabled="locked || !rootProduct" @click="bindProduct">保存产品引用</button></div>
    </section>
    <section v-if="current"><h2>配置草稿</h2><p>选项值保持原样；小数按文本保存。可明确选择当前获准的选项规则，完整适用范围仍需核准。</p>
      <p v-if="current.draft.configuration?.optionCheck" role="status">选项检查：{{ optionStatus(current.draft.configuration.optionCheck.status) }}；规则版本 {{ current.draft.configuration.optionCheck.ruleRef.version }}。仅覆盖选项约束。</p>
      <ul v-if="current.draft.configuration?.optionCheck?.findings.length"><li v-for="(f, i) in current.draft.configuration.optionCheck.findings" :key="i">{{ findingText(f.code) }}：{{ f.name }}</li></ul>
      <button :disabled="busy || !current.draft.productRef || !permissions.has('plm-configuration-rule:query')" @click="findRules">查找获准选项规则</button>
      <label>获准选项规则<select v-model="ruleSelection" aria-label="获准选项规则" :disabled="locked"><option value="">{{ configuration.ruleRef ? `保留规则版本 ${configuration.ruleRef.version}` : '暂不选择规则' }}</option><option v-for="r in rules" :key="refKey(r.reference)" :value="refKey(r.reference)">{{ r.body.name }} · v{{ r.reference.version }} · {{ r.reference.id }}</option></select></label>
      <button :disabled="locked || !canEdit" @click="configuration.ruleRef = null; ruleSelection = ''">清除所选规则</button>
      <form @submit.prevent="saveConfiguration"><fieldset :disabled="locked || !canEdit"><div class="grid">
        <label>配置适用性<select v-model="configuration.applicability" aria-label="配置适用性"><option value="UNKNOWN">待确定</option><option value="APPLIES">拟适用</option><option value="NOT_APPLICABLE">拟不适用</option></select></label>
        <label>配置说明<textarea v-model="configuration.reason" aria-label="配置说明" :required="configuration.applicability === 'NOT_APPLICABLE'" /></label>
      </div><div v-for="(option, index) in configuration.options" :key="index" class="inline">
        <label>选项名<input v-model="option.name" :aria-label="`选项名 ${index + 1}`" required maxlength="256" /></label>
        <label>类型<select :value="option.type" :aria-label="`选项类型 ${index + 1}`" @change="optionType(index, ($event.target as HTMLSelectElement).value)"><option value="STRING">文本</option><option value="DECIMAL">小数</option><option value="BOOLEAN">是 / 否</option></select></label>
        <label v-if="option.type === 'STRING'">值<input v-model="option.text" :aria-label="`选项值 ${index + 1}`" /></label>
        <label v-else-if="option.type === 'DECIMAL'">精确小数<input v-model="option.decimal" :aria-label="`选项值 ${index + 1}`" inputmode="decimal" required /></label>
        <label v-else>值<select :value="option.boolean == null ? '' : String(option.boolean)" :aria-label="`选项值 ${index + 1}`" required @change="option.boolean = ($event.target as HTMLSelectElement).value === '' ? null : ($event.target as HTMLSelectElement).value === 'true'"><option value="">请选择</option><option value="true">是</option><option value="false">否</option></select></label>
        <button type="button" @click="configuration.options.splice(index, 1)">移除选项</button>
      </div><button type="button" @click="configuration.options.push({ name: '', type: 'STRING', text: '' })">添加选项</button><button>保存配置草稿</button><button type="button" @click="clearConfiguration">清除配置草稿</button></fieldset></form>
    </section>
    <section v-if="current"><header><h2>组成位置</h2><button v-if="canEdit" :disabled="locked" @click="newComponent">添加组成位置</button></header>
      <div class="table-scroll"><table><thead><tr><th>位置 / 顺序</th><th>内容</th><th>来源</th><th>操作</th></tr></thead><tbody><tr v-for="item in current.draft.components" :key="item.occurrenceId"><td>{{ item.path }} / {{ item.semanticOrder }}</td><td>{{ kindNames[item.kind] }}</td><td>{{ item.sourceRef ? `${item.sourceRef.id} · v${item.sourceRef.version}` : '来源待补齐' }}</td><td><button v-if="canEdit" :disabled="locked" @click="editComponent(item)">编辑位置</button><button v-if="canEdit" :disabled="locked" @click="removeComponent(item)">移除位置</button></td></tr></tbody></table></div>
      <form v-if="component" @submit.prevent="saveComponent"><fieldset :disabled="locked || !canEdit"><div class="grid">
        <label>位置路径<input v-model="component.path" aria-label="位置路径" maxlength="2048" required /></label><label>语义顺序<input v-model.number="component.semanticOrder" aria-label="语义顺序" type="number" min="0" step="1" required /></label>
        <label>内容类型<select v-model="component.kind" aria-label="组成类型" @change="component.sourceRef = null; componentSource = ''"><option v-for="(label, value) in kindNames" :key="value" :value="value">{{ label }}</option></select></label>
        <label>适用性<select v-model="component.applicability" aria-label="组成适用性"><option value="UNKNOWN">待确定</option><option value="APPLIES">拟适用</option><option value="NOT_APPLICABLE">拟不适用</option></select></label>
        <label>适用性说明<textarea v-model="component.reason" aria-label="组成适用性说明" :required="component.applicability === 'NOT_APPLICABLE'" /></label><label>说明<textarea v-model="component.note" aria-label="组成说明" /></label>
        <label v-if="component.kind === 'PRODUCT'">工程产品来源<select v-model="componentSource" aria-label="组成来源"><option value="">{{ component.sourceRef ? '保留已记录来源' : '来源待补齐' }}</option><option v-for="row in products" :key="refKey(row.reference)" :value="refKey(row.reference)">{{ row.product.code }} / {{ row.product.name }} · {{ row.reference.id }} · v{{ row.reference.version }}</option></select></label>
      </div><p v-if="component.kind !== 'PRODUCT'">该内容的正式来源入口尚未接入。可记录缺项说明，后续补齐准确来源。</p>
      <button type="button" @click="component.sourceRef = null; componentSource = ''">清除来源引用</button><button>保存组成位置</button><button type="button" @click="component = null">取消编辑位置</button></fieldset></form>
    </section>
    <section v-if="current"><h2>工作历史与差异</h2><form class="inline" @submit.prevent="loadHistory"><label>工作版本<input v-model="historyVersion" aria-label="历史工作版本" pattern="[1-9][0-9]*" :disabled="busy" required /></label><button :disabled="busy">读取历史</button></form>
      <div v-if="history"><p>版本 {{ history.reference.version }}：{{ history.draft.goal }}。原因：{{ history.reason }}</p><p>记录了 {{ history.draft.components.length }} 个组成位置。{{ history.isCurrent ? '当前工作快照。' : '历史工作快照，不覆盖当前草稿。' }}</p><details><summary>历史配置与组成</summary><pre>{{ JSON.stringify(history.draft, null, 2) }}</pre></details></div>
      <form class="inline" @submit.prevent="loadDiff"><label>起始工作版本<input v-model="beforeVersion" aria-label="差异起始版本" pattern="[1-9][0-9]*" :disabled="busy" required /></label><label>结束工作版本<input v-model="afterVersion" aria-label="差异结束版本" pattern="[1-9][0-9]*" :disabled="busy" required /></label><button :disabled="busy">比较工作版本</button></form>
      <div v-if="diff"><p v-if="!diff.changes.length">两个工作快照没有内容差异。</p><div v-for="change in diff.changes" :key="change.path"><h3>{{ change.path }}</h3><div class="grid"><pre>{{ JSON.stringify(change.before, null, 2) }}</pre><pre>{{ JSON.stringify(change.after, null, 2) }}</pre></div></div></div>
    </section>
  </div>
</template>

<script setup lang="ts">
import { configurationApi, type ConfigurationSnapshot } from '@/api/plm/configurationRules'
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { revisionApi, type RevisionCommand, type RevisionComponent, type RevisionConfiguration, type RevisionCreation, type RevisionDiff, type RevisionPatch, type RevisionScope, type RevisionSnapshot } from '@/api/plm/engineeringRevisions'
import { engineeringApi, type EngineeringReference, type EngineeringVersion } from '@/api/plm/engineeringProducts'
import { revisionResponse, pendingRevisionOperation, resolveRevisionOperation, saveRevisionOperation, type PendingRevisionOperation } from '@/auth/revisionOperationRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const permissions = usePermissionStore(), canRead = computed(() => permissions.loaded && permissions.has('plm-engineering-revision:query')), canEdit = computed(() => canRead.value && permissions.has('plm-engineering-revision:edit'))
const busy = ref(false), error = ref(''), notice = ref(''), scope = ref<RevisionScope | null>(null), pending = ref<PendingRevisionOperation | null>(null), locked = computed(() => busy.value || !!pending.value)
const rows = ref<RevisionSnapshot[]>([]), search = ref(''), page = ref(1), current = ref<RevisionSnapshot | null>(null), creating = ref(false), creation = ref<RevisionCreation | null>(null), draftId = ref('')
const goal = ref(''), reason = ref(''), noParentReason = ref(''), department = ref(''), productSearch = ref(''), products = ref<EngineeringVersion[]>([]), rootProduct = ref('')
const rules = ref<ConfigurationSnapshot[]>([]), ruleSelection = ref('')
const emptyConfiguration = (): RevisionConfiguration => ({ options: [], applicability: 'UNKNOWN', reason: '' })
const configuration = ref<RevisionConfiguration>(emptyConfiguration()), component = ref<RevisionComponent | null>(null), componentSource = ref('')
const historyVersion = ref('1'), beforeVersion = ref('1'), afterVersion = ref('1'), history = ref<RevisionSnapshot | null>(null), diff = ref<RevisionDiff | null>(null)
const kindNames = { PRODUCT: '工程产品', EBOM: '工程 BOM', MBOM: '制造 BOM', ROUTING: '工艺', DOCUMENT: '文档', QUALITY_SPEC: '质量规范' }
let generation = 0, live = true
const copy = <T,>(value: T): T => JSON.parse(JSON.stringify(value)) as T
const refKey = (value: EngineeringReference) => `${value.owner}:${value.id}:${value.version}:${value.digest}`
function missingText(value: string) {
  const text: Record<string, string> = { PRODUCT_UNRESOLVED: '工程产品待解析', PRODUCT_SOURCE_CHANGED: '工程产品来源已变化，需重新核对', CONFIGURATION_MISSING: '配置草稿待补齐', CONFIGURATION_RULE_CHANGED: '选项规则已撤回、替换或来源已变化，需重新核对', CONFIGURATION_OPTIONS_UNRESOLVED: '选项仍有缺项或冲突', CONFIGURATION_APPLICABILITY_REQUIRED: '完整配置适用范围仍待核准', CONFIGURATION_RULE_OWNER_REQUIRED: '配置规则待接入并核准', COMPOSITION_MISSING: '组成内容待补齐' }
  return text[value] || (value.startsWith('SOURCE_CHANGED:') ? '组成来源已变化：' : value.startsWith('SOURCE_REQUIRED:') ? '组成来源待补齐：' : '组成适用性待核准：') + value.split(':').slice(1).join(':')
}
function message(failure: any) {
  const code = failure?.response?.data?.message || failure?.message || ''
  const known: Record<string, string> = { ENGINEERING_NUMBER_RULE_REQUIRED: '尚未配置工程修订编号规则，请联系编号规则管理员。', ENGINEERING_NUMBER_RULE_INVALID: '当前编号规则无法分配修订号，请核对规则。', ENGINEERING_WORKING_VERSION_CHANGED: '工作草稿已变化，请重新打开后核对。', ENGINEERING_PRODUCT_CHANGED: '工程产品来源已变化或停用，请重新选择。', ENGINEERING_REVISION_PRODUCT_IMMUTABLE: '已绑定的工程产品身份不可替换。', CREATION_AUTHORITY_CHANGED: '当前创建范围已变化，请重新新建并选择部门。', CREATION_DEPARTMENT_FORBIDDEN: '请选择授权范围内的部门。', ACTION_FORBIDDEN: '当前权限无法执行此操作。', OBJECT_FORBIDDEN: '当前权限无法访问所选对象。', OPERATIONS_OUTCOME_UNKNOWN: '保存结果尚未确认，请查询原操作。' }
  return known[code] || (/^[A-Z][A-Z0-9_]+$/.test(code) ? '无法保存，请核对输入和原操作状态。' : code) || '暂时无法完成。'
}
function syncPending() { pending.value = scope.value ? pendingRevisionOperation(scope.value, 'ENGINEERING_REVISION') : null }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const stamp = identityStamp(), epoch = generation
  const check = () => { requireIdentity(stamp); if (!live || epoch !== generation || !authenticationCanRead()) throw new Error('当前身份已变化。') }
  busy.value = true; error.value = ''; notice.value = ''
  try { check(); await action(check); check() }
  catch (failure: any) { try { check(); if (failure?.response?.status === 403) clearPrivate(); error.value = message(failure) } catch { /* Ignore obsolete responses. */ } }
  finally { try { check(); syncPending(); busy.value = false } catch { if (live && epoch === generation) busy.value = false } }
}
function clearEditor() { rules.value = []; ruleSelection.value = '';  current.value = null; creating.value = false; creation.value = null; draftId.value = ''; goal.value = ''; reason.value = ''; noParentReason.value = ''; department.value = ''; rootProduct.value = ''; products.value = []; productSearch.value = ''; component.value = null; componentSource.value = ''; configuration.value = emptyConfiguration(); history.value = null; diff.value = null; historyVersion.value = '1'; beforeVersion.value = '1'; afterVersion.value = '1' }
function setCurrent(value: RevisionSnapshot) { clearEditor(); current.value = value; draftId.value = value.reference.id; goal.value = value.draft.goal; configuration.value = copy(value.draft.configuration?.input ?? emptyConfiguration()); afterVersion.value = value.reference.version }
async function reload() { await run(async check => { rows.value = []; clearEditor(); scope.value = null; if (!permissions.loaded) await permissions.loadMyActions(); check(); if (!canRead.value) throw new Error('当前没有读取工程修订的权限。'); const s = revisionResponse(await revisionApi.scope()); check(); scope.value = s; const list = revisionResponse(await revisionApi.list(search.value, page.value)); check(); rows.value = list }) }
async function loadList(target: number) { await run(async check => { rows.value = []; const list = revisionResponse(await revisionApi.list(search.value, target)); check(); rows.value = list; page.value = target }) }
async function open(row: RevisionSnapshot) { const id = row.reference.id; await run(async check => { clearEditor(); const value = revisionResponse(await revisionApi.read(id)); check(); setCurrent(value) }) }
async function openNew() { await run(async check => { clearEditor(); const value = revisionResponse(await revisionApi.creationContext()); check(); creation.value = value; creating.value = true; draftId.value = crypto.randomUUID() }) }
async function findProducts() { const text = productSearch.value; await run(async check => { products.value = []; rootProduct.value = ''; componentSource.value = ''; const values = revisionResponse(await engineeringApi.list(text)); check(); products.value = values.filter(row => row.isCurrent && row.product.enabled) }) }
function selectedProduct(key: string) { const value = products.value.find(row => refKey(row.reference) === key); if (!value) throw new Error('请重新查找并明确选择工程产品来源。'); return copy(value.reference) }
async function write(command: Omit<RevisionCommand, 'operationId'>) {
  const s = scope.value; if (!s) return
  const body = copy(command)
  await run(async check => {
    const authorized = () => { check(); if (!canRead.value || !permissions.has(`plm-engineering-revision:${body.action === 'CREATE' ? 'add' : 'edit'}`)) throw new Error('当前权限无法保存工程修订。') }
    await saveRevisionOperation(s, 'ENGINEERING_REVISION', body.revisionId, authorized, operationId => revisionApi.save({ ...body, operationId })); check(); clearEditor(); rows.value = []
    const value = revisionResponse(await revisionApi.read(body.revisionId)); check(); setCurrent(value); const list = revisionResponse(await revisionApi.list(search.value, page.value)); check(); rows.value = list; notice.value = '工作快照已保存。'
  })
}
async function patch(patches: RevisionPatch[]) { const value = current.value; if (!value || !reason.value.trim()) { error.value = '请填写修改原因。'; return } await write({ revisionId: value.reference.id, action: 'PATCH', expectedVersion: value.reference.version, expectedEtag: value.etag, reason: reason.value, patches }) }
async function saveGoal() {
  if (!creating.value) { await patch([{ op: 'SET_GOAL', goal: goal.value }]); return }
  try { await write({ revisionId: draftId.value, action: 'CREATE', expectedAuthorityVersion: creation.value?.authorityVersion, departmentId: department.value || null, origin: { type: 'NEW_PRODUCT', noParentReason: noParentReason.value }, goal: goal.value, reason: reason.value, productRef: rootProduct.value ? selectedProduct(rootProduct.value) : null }) } catch (failure) { error.value = message(failure) }
}
async function bindProduct() { try { await patch([{ op: 'SET_PRODUCT', productRef: selectedProduct(rootProduct.value) }]) } catch (failure) { error.value = message(failure) } }
function optionType(index: number, type: string) { const original = configuration.value.options[index]; if (!original) return; configuration.value.options[index] = type === 'DECIMAL' ? { name: original.name, type, decimal: '' } : type === 'BOOLEAN' ? { name: original.name, type, boolean: null } : { name: original.name, type: 'STRING', text: '' } }
const optionStatus = (s: string) => ({ OPTIONS_VALIDATED: '选项约束已通过', UNKNOWN: '缺少必要输入', CONFLICT: '存在约束冲突', NO_MATCH: '选项值不满足规则' }[s] || s)
const findingText = (s: string) => ({ REQUIRED_OPTION_MISSING: '必选项待补齐', DEPENDENCY_OPTION_MISSING: '依赖选项待补齐', OPTION_CONSTRAINT_CONFLICT: '选项约束冲突', OPTION_VALUE_NOT_ALLOWED: '选项值不在允许范围' }[s] || s)
async function findRules() { const product = current.value?.draft.productRef; if (!product) return; await run(async check => { rules.value = []; ruleSelection.value = ''; const list = revisionResponse(await configurationApi.list(product.id)); check(); for (const row of list) { if (!row.activeRef) continue; const approved = revisionResponse(await configurationApi.read(row.activeRef.id, row.activeRef.version)); check(); if (approved.isActive && !approved.missing.length && refKey(approved.body.productRef) === refKey(product)) rules.value.push(approved) } }) }
async function saveConfiguration() { const input = copy(configuration.value); if (ruleSelection.value) { const rule = rules.value.find(r => refKey(r.reference) === ruleSelection.value); if (!rule) { error.value = '请重新查找获准规则。'; return } input.ruleRef = copy(rule.reference) } await patch([{ op: 'SET_CONFIGURATION', configuration: input }]) }
async function clearConfiguration() { await patch([{ op: 'CLEAR_CONFIGURATION' }]) }
function newComponent() { componentSource.value = ''; component.value = { occurrenceId: crypto.randomUUID(), kind: 'PRODUCT', path: '', semanticOrder: 0, applicability: 'UNKNOWN', reason: '', note: '', sourceRef: null } }
function editComponent(value: RevisionComponent) { componentSource.value = ''; component.value = copy(value) }
async function saveComponent() { if (!component.value) return; try { const value = copy(component.value); if (componentSource.value) value.sourceRef = selectedProduct(componentSource.value); await patch([{ op: 'UPSERT_COMPONENT', component: value }]) } catch (failure) { error.value = message(failure) } }
async function removeComponent(value: RevisionComponent) { await patch([{ op: 'REMOVE_COMPONENT', occurrenceId: value.occurrenceId }]) }
async function loadHistory() { const id = current.value?.reference.id, version = historyVersion.value; if (!id) return; await run(async check => { history.value = null; const value = revisionResponse(await revisionApi.read(id, version)); check(); history.value = value }) }
async function loadDiff() { const id = current.value?.reference.id, before = beforeVersion.value, after = afterVersion.value; if (!id) return; await run(async check => { diff.value = null; const value = revisionResponse(await revisionApi.diff(id, before, after)); check(); diff.value = value }) }
async function recover(close: boolean) { const s = scope.value; if (!s) return; await run(async check => { const result = await resolveRevisionOperation(s, 'ENGINEERING_REVISION', close, check); check(); if (result.state === 'COMMITTED') { clearEditor(); rows.value = []; const values = revisionResponse(await revisionApi.list(search.value, page.value)); check(); rows.value = values } notice.value = result.state === 'COMMITTED' ? '原操作已提交，请重新打开最新工作快照。' : result.state === 'CLOSED' ? '原操作已结束，可以继续编辑。' : '仍无法确认，请继续查询或结束未提交操作。' }) }
function clearPrivate() { clearEditor(); rows.value = []; search.value = ''; page.value = 1 }
function clear() { generation++; busy.value = false; clearPrivate(); scope.value = null; pending.value = null; error.value = ''; notice.value = '' }
function identityChanged() { clear(); if (authenticationCanRead()) void reload() }
function storage(event: StorageEvent) { if (event.key?.startsWith('cp6_revision_operation_v1:')) { try { syncPending() } catch (failure) { error.value = message(failure) } } }
watch(canRead, allowed => { if (!allowed) clear() })
onMounted(() => { window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', storage); void reload() })
onBeforeUnmount(() => { live = false; generation++; window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', storage) })
</script>

<style scoped>
.revision-page { display: grid; gap: 16px; padding: 24px; color: var(--el-text-color-primary); }
header,.inline { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; } header { justify-content: space-between; }
h1,h2 { margin: 0 0 8px; } section { background: var(--el-bg-color); border: 1px solid var(--el-border-color); border-radius: 8px; padding: 18px; }
label { display: grid; gap: 6px; } input,select,textarea,button { font: inherit; padding: 7px 10px; border: 1px solid var(--el-border-color); border-radius: 4px; background: var(--el-bg-color); color: inherit; max-width: 100%; box-sizing: border-box; }
button { cursor: pointer; margin: 4px; } button:disabled { opacity: .5; cursor: default; } .grid { display: grid; grid-template-columns: repeat(auto-fit,minmax(220px,1fr)); gap: 14px; }
.table-scroll { overflow: auto; margin-top: 16px; } table { width: 100%; border-collapse: collapse; } th,td { text-align: left; padding: 10px; border-bottom: 1px solid var(--el-border-color-lighter); }
fieldset { border: 0; padding: 0; } [role=alert] { color: var(--el-color-danger); } pre { white-space: pre-wrap; overflow-wrap: anywhere; }
</style>
