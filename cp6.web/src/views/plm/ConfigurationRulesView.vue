<template>
  <div class="rules-page" :aria-busy="busy">
    <header><div><h1>工程选项规则</h1><p>定义必选项、允许值、互斥和依赖，经批准后供工程工作快照明确选用。</p></div><button :disabled="busy" @click="reload">刷新</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <section v-if="pending" aria-label="原操作恢复"><p>上次操作结果尚未确认。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy" @click="recover(true)">结束未提交操作</button></section>
    <section><header><h2>规则目录</h2><button v-if="has('add')" :disabled="locked || !scope" @click="openNew">新建规则</button></header>
      <form class="inline" @submit.prevent="loadList(1)"><label>规则或工程产品身份<input v-model="search" aria-label="规则搜索" maxlength="256" :disabled="busy" /></label><button :disabled="busy || !canRead">搜索</button></form>
      <table><thead><tr><th>规则</th><th>工作版本与状态</th><th>当前获准版本</th><th>操作</th></tr></thead><tbody><tr v-for="row in rows" :key="row.reference.id"><td>{{ row.body.name }}</td><td>{{ row.reference.version }} · {{ stateName(row.state) }}</td><td>{{ row.activeRef?.version || '尚无批准' }}</td><td><button :disabled="busy" @click="open(row)">打开</button></td></tr></tbody></table>
      <p v-if="!rows.length">当前范围内没有规则。</p><nav class="inline"><button :disabled="busy || page === 1 || !canRead" @click="loadList(page - 1)">上一页</button><span>第 {{ page }} 页</span><button :disabled="busy || rows.length < 20 || !canRead" @click="loadList(page + 1)">下一页</button></nav>
    </section>
    <section v-if="creating || current"><h2>{{ creating ? '新规则草稿' : current?.body.name }}</h2>
      <p>工程选项规则可明确包含完整性政策。批准只覆盖此版本声明的规则，工程检查与正式冻结分别办理。</p>
      <p v-if="current">工作版本 {{ current.reference.version }} · {{ stateName(current.state) }}；当前获准版本 {{ current.activeRef?.version || '无' }}。保存新草稿会保留现有批准，替换和撤回需要明确操作。</p>
      <p v-if="current?.missing.length" role="status">工程来源已变化，批准或采用前须重新核对。</p>
      <form class="inline" @submit.prevent="findProducts"><label>工程产品<input v-model="productSearch" aria-label="工程产品搜索" :disabled="busy" /></label><button :disabled="busy || !permissions.has('plm-engineering-product:query')">查找工程产品</button></form>
      <form @submit.prevent="saveDraft"><fieldset :disabled="locked || !canEdit"><div class="grid">
        <label>规则名称<input v-model="name" aria-label="规则名称" required maxlength="256" /></label>
        <label>准确工程来源<select v-model="productSelection" aria-label="规则工程来源"><option value="">{{ current ? '保留当前工程来源' : '请选择工程产品' }}</option><option v-for="p in products.filter(p => !current || p.reference.id === current.body.productRef.id)" :key="refKey(p.reference)" :value="refKey(p.reference)">{{ p.product.code }} / {{ p.product.name }} · v{{ p.reference.version }} · {{ p.reference.id }}</option></select></label>
        <label v-if="creating && creation">所属部门<select v-model="department" aria-label="规则所属部门" :required="!creation.allowsUnbound"><option value="">{{ creation.allowsUnbound ? '不绑定部门' : '请选择授权部门' }}</option><option v-for="d in creation.departments" :key="d.id" :value="d.id">{{ d.code }} / {{ d.name }}</option></select></label>
      </div><h3>选项定义</h3>
      <article v-for="(d, index) in definitions" :key="index"><div class="inline">
        <label>选项名<input v-model="d.name" :aria-label="`定义名 ${index + 1}`" required maxlength="256" @change="renameValues(d)" /></label>
        <label>类型<select v-model="d.type" :aria-label="`定义类型 ${index + 1}`" @change="d.allowedValues = []"><option value="STRING">文本</option><option value="DECIMAL">精确小数</option><option value="BOOLEAN">是 / 否</option></select></label>
        <label>必选<select v-model="d.required" :aria-label="`定义必选 ${index + 1}`" required><option :value="null">请选择</option><option :value="true">必选</option><option :value="false">可选</option></select></label>
        <label>允许值范围<select v-model="d.allowAnyValue" :aria-label="`定义范围 ${index + 1}`" required @change="d.allowedValues = []"><option :value="null">请选择</option><option :value="true">允许此类型任意值</option><option :value="false">仅允许列出的值</option></select></label><button type="button" @click="definitions.splice(index, 1)">移除定义</button>
      </div><div v-if="d.allowAnyValue === false"><div v-for="(_, n) in d.allowedValues" :key="n" class="inline"><OptionValue v-model="d.allowedValues[n]!" :label="`定义 ${index + 1} 允许值 ${n + 1}`" /><button type="button" @click="d.allowedValues.splice(n, 1)">移除值</button></div><button type="button" @click="d.allowedValues.push(option(d.name, d.type))">添加允许值</button></div></article>
      <button type="button" @click="definitions.push({ name: '', type: 'STRING', required: null, allowAnyValue: null, allowedValues: [] })">添加选项定义</button>
      <EngineeringCheckPolicyEditor v-model="engineeringChecks" /><h3>互斥与依赖</h3><article v-for="(c, index) in constraints" :key="c.id"><div class="inline">
        <label>约束<select v-model="c.kind" :aria-label="`约束类型 ${index + 1}`"><option value="REQUIRES">满足前项时必须满足后项</option><option value="MUTEX">前后两项不可同时满足</option></select></label>
        <template v-for="side in (['when', 'then'] as const)" :key="side"><label>{{ side === 'when' ? '前项' : '后项' }}<select :value="c[side].name" :aria-label="`约束 ${index + 1} ${side}`" required @change="chooseCondition(c, side, ($event.target as HTMLSelectElement).value)"><option value="">请选择定义</option><option v-for="d in definitions" :key="d.name" :value="d.name">{{ d.name }}</option></select></label><OptionValue v-model="c[side]" :label="`约束 ${index + 1} ${side} 值`" /></template>
        <label>约束依据<textarea v-model="c.reason" :aria-label="`约束依据 ${index + 1}`" required /></label><button type="button" @click="constraints.splice(index, 1)">移除约束</button>
      </div></article><button type="button" @click="addConstraint">添加约束</button><button>{{ creating ? '创建规则草稿' : '保存新草稿' }}</button></fieldset></form>
      <label>本次操作原因<textarea v-model="reason" aria-label="操作原因" maxlength="8000" :disabled="locked" required /></label>
      <div v-if="current" class="inline"><button v-if="current.state === 'DRAFT' && has('submit')" :disabled="locked" @click="transition('SUBMIT')">提交审核</button>
        <button v-if="current.state === 'UNDER_REVIEW' && has('approve')" :disabled="locked" @click="transition('RETURN')">退回草稿</button>
        <template v-if="current.state === 'UNDER_REVIEW' && has('approve')"><label><input v-model="approveAcknowledged" type="checkbox" aria-label="确认规则批准范围" :disabled="locked" />我已核对工程来源、全部选项约束及声明的完整性政策，批准此准确版本{{ current.activeRef ? `并替换获准版本 ${current.activeRef.version}` : '' }}</label><button :disabled="locked || !approveAcknowledged" @click="transition('APPROVE')">批准此版本</button></template>
        <template v-if="current.activeRef && has('withdraw')"><label><input v-model="withdrawAcknowledged" type="checkbox" aria-label="确认撤回批准" :disabled="locked" />撤回当前获准版本 {{ current.activeRef.version }}，后续不再采用</label><button :disabled="locked || !withdrawAcknowledged" @click="transition('WITHDRAW')">撤回批准</button></template>
      </div>
    </section>
    <section v-if="current"><h2>规则历史</h2><form class="inline" @submit.prevent="loadHistory"><label>版本<input v-model="historyVersion" aria-label="规则历史版本" pattern="[1-9][0-9]*" required :disabled="busy" /></label><button :disabled="busy">读取历史</button></form><div v-if="history"><p>版本 {{ history.reference.version }} · {{ stateName(history.state) }} · {{ history.isActive ? '当前获准' : '当前未生效' }}。{{ history.reason }}</p><details><summary>原规则定义</summary><pre>{{ JSON.stringify(history.body, null, 2) }}</pre></details></div></section>
  </div>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { configurationApi, type ConfigurationBody, type ConfigurationCommand, type ConfigurationCreation, type ConfigurationScope, type ConfigurationSnapshot, type OptionConstraint, type OptionDefinition } from '@/api/plm/configurationRules'
import { engineeringApi, type EngineeringReference, type EngineeringVersion } from '@/api/plm/engineeringProducts'
import type { RevisionOption } from '@/api/plm/engineeringRevisions'
import { configurationResponse, pendingConfigurationOperation, resolveConfigurationOperation, saveConfigurationOperation, type PendingConfigurationOperation } from '@/auth/configurationOperationRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
import OptionValue from './ConfigurationOptionValue.vue'
import EngineeringCheckPolicyEditor from './EngineeringCheckPolicyEditor.vue'
import type { EngineeringCheckPolicy } from '@/api/plm/engineeringSourceCheck'
const engineeringChecks = ref<EngineeringCheckPolicy | null>(null)
const permissions = usePermissionStore(), has = (action: string) => permissions.has(`plm-configuration-rule:${action}`), canRead = computed(() => permissions.loaded && has('query'))
const busy = ref(false), error = ref(''), notice = ref(''), scope = ref<ConfigurationScope | null>(null), pending = ref<PendingConfigurationOperation | null>(null), locked = computed(() => busy.value || !!pending.value)
const rows = ref<ConfigurationSnapshot[]>([]), search = ref(''), page = ref(1), current = ref<ConfigurationSnapshot | null>(null), creating = ref(false), creation = ref<ConfigurationCreation | null>(null), draftId = ref('')
const name = ref(''), reason = ref(''), department = ref(''), productSearch = ref(''), products = ref<EngineeringVersion[]>([]), productSelection = ref('')
const definitions = ref<OptionDefinition[]>([]), constraints = ref<OptionConstraint[]>([]), history = ref<ConfigurationSnapshot | null>(null), historyVersion = ref('1'), approveAcknowledged = ref(false), withdrawAcknowledged = ref(false)
const canEdit = computed(() => canRead.value && (creating.value ? has('add') : has('edit') && current.value?.state !== 'UNDER_REVIEW'))
let live = true, generation = 0
const copy = <T,>(v: T): T => JSON.parse(JSON.stringify(v))
const refKey = (r: EngineeringReference) => `${r.id}:${r.version}:${r.digest}`
const stateName = (s: string) => ({ DRAFT: '草稿', UNDER_REVIEW: '审核中', APPROVED: '已批准', WITHDRAWN: '已撤回' }[s] || s)
const option = (name: string, type: RevisionOption['type']): RevisionOption => type === 'DECIMAL' ? { name, type, decimal: '' } : type === 'BOOLEAN' ? { name, type, boolean: null } : { name, type, text: '' }
function renameValues(d: OptionDefinition) { for (const v of d.allowedValues) v.name = d.name }
function chooseCondition(c: OptionConstraint, side: 'when' | 'then', name: string) { const d = definitions.value.find(d => d.name === name); c[side] = option(name, d?.type ?? 'STRING') }
function addConstraint() { constraints.value.push({ id: crypto.randomUUID(), kind: 'REQUIRES', when: option('', 'STRING'), then: option('', 'STRING'), reason: '' }) }
function message(failure: any) { const code = failure?.response?.data?.message || failure?.message || ''; const known: Record<string, string> = { CONFIGURATION_RULE_CHANGED: '规则或批准版本已变化，请重新打开后核对。', CONFIGURATION_RULE_IDENTITY_CONFLICT: '该工程产品已有规则，请在规则目录查找。', CONFIGURATION_RULE_TRANSITION_INVALID: '当前规则状态不允许该操作，请刷新。', CONFIGURATION_DEFINITION_INVALID: '请核对选项类型、必选和允许值范围。', CONFIGURATION_CONSTRAINT_INVALID: '请核对约束两侧的选项名、类型和允许值。', ENGINEERING_PRODUCT_CHANGED: '工程来源已变化，请重新查找并核对。', CREATION_AUTHORITY_CHANGED: '创建范围已变化，请重新新建。', ACTION_FORBIDDEN: '当前权限无法执行该操作。', OBJECT_FORBIDDEN: '当前权限无法访问该对象。', OPERATIONS_OUTCOME_UNKNOWN: '结果尚未确认，请查询原操作。' }; return known[code] || (/^[A-Z][A-Z0-9_]+$/.test(code) ? '操作未完成，请核对输入或查询原操作。' : code) || '暂时无法完成。' }
function syncPending() { pending.value = scope.value ? pendingConfigurationOperation(scope.value, 'CONFIGURATION_RULE') : null }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return; const stamp = identityStamp(), epoch = generation
  const check = () => { requireIdentity(stamp); if (!live || epoch !== generation || !authenticationCanRead()) throw new Error('当前身份已变化。') }
  busy.value = true; error.value = ''; notice.value = ''
  try { check(); await action(check); check() } catch (failure: any) { try { check(); if (failure?.response?.status === 403) clearPrivate(); error.value = message(failure) } catch { /* Ignore obsolete response. */ } }
  finally { try { check(); syncPending(); busy.value = false } catch { if (live && epoch === generation) busy.value = false } }
}
function clearEditor() { engineeringChecks.value = null; current.value = null; creating.value = false; creation.value = null; draftId.value = ''; name.value = ''; reason.value = ''; department.value = ''; products.value = []; productSearch.value = ''; productSelection.value = ''; definitions.value = []; constraints.value = []; history.value = null; historyVersion.value = '1'; approveAcknowledged.value = false; withdrawAcknowledged.value = false }
function setCurrent(v: ConfigurationSnapshot) { clearEditor(); current.value = v; draftId.value = v.reference.id; name.value = v.body.name; definitions.value = copy(v.body.definitions); constraints.value = copy(v.body.constraints); engineeringChecks.value = copy(v.body.engineeringChecks ?? null) }
async function reload() { await run(async check => { rows.value = []; clearEditor(); scope.value = null; if (!permissions.loaded) await permissions.loadMyActions(); check(); if (!canRead.value) throw new Error('当前没有读取规则的权限。'); const s = configurationResponse(await configurationApi.scope()); check(); scope.value = s; const list = configurationResponse(await configurationApi.list(search.value, page.value)); check(); rows.value = list }) }
async function loadList(target: number) { await run(async check => { rows.value = []; const list = configurationResponse(await configurationApi.list(search.value, target)); check(); rows.value = list; page.value = target }) }
async function open(row: ConfigurationSnapshot) { const id = row.reference.id; await run(async check => { clearEditor(); const v = configurationResponse(await configurationApi.read(id)); check(); setCurrent(v) }) }
async function openNew() { await run(async check => { clearEditor(); const v = configurationResponse(await configurationApi.creationContext()); check(); creation.value = v; creating.value = true; draftId.value = crypto.randomUUID() }) }
async function findProducts() { const text = productSearch.value; await run(async check => { products.value = []; productSelection.value = ''; const list = configurationResponse(await engineeringApi.list(text)); check(); products.value = list.filter(p => p.isCurrent && p.product.enabled) }) }
async function write(body: Omit<ConfigurationCommand, 'operationId'>) {
  const s = scope.value; if (!s) return; const command = copy(body)
  await run(async check => { const guard = () => { check(); const action = { CREATE: 'add', REVISE: 'edit', SUBMIT: 'submit', APPROVE: 'approve', RETURN: 'approve', WITHDRAW: 'withdraw' }[command.action]; if (!canRead.value || !has(action)) throw new Error('当前权限无法执行该操作。') }
    await saveConfigurationOperation(s, 'CONFIGURATION_RULE', command.ruleId, guard, operationId => configurationApi.save({ ...command, operationId })); check(); clearEditor(); rows.value = []
    const value = configurationResponse(await configurationApi.read(command.ruleId)); check(); setCurrent(value); const list = configurationResponse(await configurationApi.list(search.value, page.value)); check(); rows.value = list; notice.value = '规则操作已记录。'
  })
}
function command(action: ConfigurationCommand['action']): Omit<ConfigurationCommand, 'operationId'> {
  if (!reason.value.trim()) throw new Error('请填写本次操作原因。'); const v = current.value
  return { ruleId: draftId.value, action, reason: reason.value, ...(v ? { expectedVersion: v.reference.version, expectedEtag: v.etag, expectedActiveRef: copy(v.activeRef) } : { expectedAuthorityVersion: creation.value?.authorityVersion, departmentId: department.value || null }) }
}
async function saveDraft() { try { let source = current.value?.body.productRef; if (productSelection.value) source = products.value.find(p => refKey(p.reference) === productSelection.value)?.reference; if (!source) throw new Error('请查找并明确选择工程来源。'); const body: ConfigurationBody = { scope: engineeringChecks.value ? 'ENGINEERING_OPTIONS_AND_CHECKS_V1' : 'ENGINEERING_OPTIONS_ONLY', ...(engineeringChecks.value ? { engineeringChecks: copy(engineeringChecks.value) } : {}), name: name.value, productRef: copy(source), definitions: copy(definitions.value), constraints: copy(constraints.value) }; await write({ ...command(creating.value ? 'CREATE' : 'REVISE'), body }) } catch (failure) { error.value = message(failure) } }
function draftChanged() { const v = current.value; return !!v && (name.value !== v.body.name || JSON.stringify(engineeringChecks.value) !== JSON.stringify(v.body.engineeringChecks ?? null) || JSON.stringify(definitions.value) !== JSON.stringify(v.body.definitions) || JSON.stringify(constraints.value) !== JSON.stringify(v.body.constraints) || !!productSelection.value && productSelection.value !== refKey(v.body.productRef)) }
async function transition(action: ConfigurationCommand['action']) { try { if (action === 'SUBMIT' && draftChanged()) throw new Error('请先保存草稿，再提交审核。'); if (action === 'APPROVE' && !approveAcknowledged.value || action === 'WITHDRAW' && !withdrawAcknowledged.value) return; await write(command(action)) } catch (failure) { error.value = message(failure) } }
async function loadHistory() { const id = current.value?.reference.id, version = historyVersion.value; if (!id) return; await run(async check => { history.value = null; const v = configurationResponse(await configurationApi.read(id, version)); check(); history.value = v }) }
async function recover(close: boolean) { const s = scope.value; if (!s) return; await run(async check => { const r = await resolveConfigurationOperation(s, 'CONFIGURATION_RULE', close, check); check(); if (r.state === 'COMMITTED') { clearEditor(); rows.value = []; const list = configurationResponse(await configurationApi.list(search.value, page.value)); check(); rows.value = list } notice.value = r.state === 'COMMITTED' ? '原操作已提交，请重新打开当前规则。' : r.state === 'CLOSED' ? '原操作已结束，可以继续。' : '仍无法确认，请继续查询或结束未提交操作。' }) }
function clearPrivate() { clearEditor(); rows.value = []; search.value = ''; page.value = 1 }
function clear() { generation++; busy.value = false; clearPrivate(); scope.value = null; pending.value = null; error.value = ''; notice.value = '' }
function identityChanged() { clear(); if (authenticationCanRead()) void reload() }
function storage(event: StorageEvent) { if (event.key?.startsWith('cp6_configuration_operation_v1:')) { try { syncPending() } catch (failure) { error.value = message(failure) } } }
watch(canRead, allowed => { if (!allowed) clear() })
onMounted(() => { window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', storage); void reload() })
onBeforeUnmount(() => { live = false; generation++; window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', storage) })
</script>
<style scoped>
.rules-page { display: grid; gap: 16px; padding: 24px; color: var(--el-text-color-primary); }
header,.inline { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; } header { justify-content: space-between; }
section { background: var(--el-bg-color); border: 1px solid var(--el-border-color); border-radius: 8px; padding: 18px; overflow: auto; }
label { display: grid; gap: 6px; } :deep(input),:deep(select),:deep(textarea),button { font: inherit; padding: 7px 10px; border: 1px solid var(--el-border-color); border-radius: 4px; background: var(--el-bg-color); color: inherit; max-width: 100%; box-sizing: border-box; }
button { cursor: pointer; margin: 4px; } button:disabled { opacity: .5; cursor: default; } .grid { display: grid; grid-template-columns: repeat(auto-fit,minmax(220px,1fr)); gap: 14px; }
table { width: 100%; border-collapse: collapse; } th,td { text-align: left; padding: 10px; border-bottom: 1px solid var(--el-border-color-lighter); }
fieldset { border: 0; padding: 0; } article { border-bottom: 1px solid var(--el-border-color); padding: 12px 0; } [role=alert] { color: var(--el-color-danger); } pre { white-space: pre-wrap; overflow-wrap: anywhere; }
</style>
