<template>
  <main class="engineering-page" :aria-busy="busy">
    <header><div><h1>工程产品与经营映射</h1><p>分别维护技术身份与经营料号；相同名称或编码不会自动合并。</p></div><button :disabled="busy" @click="reload">刷新目录</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <section v-for="item in pendingItems" :key="item.kind" aria-label="原操作恢复"><p>{{ item.kind === 'ENGINEERING_PRODUCT' ? '工程身份' : '经营映射' }}的上次保存结果尚未确认。</p><button :disabled="busy" @click="recover(item.kind, false)">查询原操作</button><button :disabled="busy" @click="recover(item.kind, true)">结束未提交操作</button></section>
    <section><header><h2>工程产品</h2><button v-if="permissions.has('plm-engineering-product:add')" :disabled="busy || !scope || !!pending.ENGINEERING_PRODUCT" @click="openProduct">新增技术身份</button></header>
      <form class="inline" @submit.prevent="searchProducts(1)"><label>工程编码<input v-model="search" aria-label="工程编码搜索" maxlength="256" :disabled="busy" /></label><button :disabled="busy || !canRead">搜索</button></form>
      <div class="table-scroll"><table><thead><tr><th>编码 / 名称</th><th>类别</th><th>身份版本</th><th>状态</th><th>操作</th></tr></thead><tbody><tr v-for="row in rows" :key="row.reference.id"><td>{{ row.product.code }} / {{ row.product.name }}</td><td>{{ row.product.category }}</td><td>{{ row.reference.version }}</td><td>{{ row.product.enabled ? '启用' : '停用' }}</td><td><button :disabled="busy" @click="selectProduct(row)">查看映射</button><button v-if="permissions.has('plm-engineering-product:edit')" :disabled="busy || !!pending.ENGINEERING_PRODUCT" @click="editProduct(row)">修订身份</button><button :disabled="busy" @click="openHistory('ENGINEERING_PRODUCT', row.reference.id)">身份历史</button></td></tr></tbody></table></div>
      <p v-if="!rows.length">当前范围内没有工程身份。</p><nav class="inline" aria-label="工程身份分页"><button :disabled="busy || !canRead || page === 1" @click="searchProducts(page - 1)">上一页</button><span>第 {{ page }} 页</span><button :disabled="busy || !canRead || rows.length < 20" @click="searchProducts(page + 1)">下一页</button></nav>
    </section>
    <section v-if="productDraft"><h2>{{ productEditing ? '修订技术身份' : '新增技术身份' }}</h2><form @submit.prevent="saveProduct"><fieldset :disabled="busy || !!pending.ENGINEERING_PRODUCT"><div class="grid">
      <label>工程编码<input v-model="productDraft.code" aria-label="工程编码" :readonly="!!productEditing" maxlength="256" required /></label><label>名称<input v-model="productDraft.name" aria-label="工程名称" maxlength="512" required /></label><label>类别<input v-model="productDraft.category" aria-label="工程类别" maxlength="64" required /></label>
      <label>技术定义<textarea v-model="productDraft.definition" aria-label="技术定义" maxlength="8000" rows="4" required /></label><label>识别依据<textarea v-model="productDraft.identificationSource" aria-label="识别依据" maxlength="8000" rows="4" required /></label>
      <label v-if="creation">所属部门<select v-model="department" aria-label="工程所属部门" :required="!creation.allowsUnbound"><option value="">{{ creation.allowsUnbound ? '不绑定部门' : '请选择授权部门' }}</option><option v-for="item in creation.departments" :key="item.id" :value="item.id">{{ item.code }} / {{ item.name }}</option></select></label><label><input v-model="productDraft.enabled" type="checkbox" />启用</label>
      </div><button>保存身份版本</button><button type="button" @click="productDraft = null">取消</button></fieldset></form></section>
    <section v-if="selected"><header><div><h2>{{ selected.product.code }} 的经营映射</h2><p>{{ selected.product.definition }}</p></div><button v-if="permissions.has('plm-engineering-product:edit')" :disabled="busy || !!pending.ENGINEERING_MAPPING || !selected.product.enabled" @click="openMapping()">新增映射草稿</button></header>
      <p>确认只覆盖指定 Site 的身份关系；法人范围、技术修订与配置、制造用途仍须分别核准。</p>
      <div class="table-scroll"><table><thead><tr><th>用途 / 依据</th><th>版本</th><th>确认状态</th><th>当前可用性</th><th>操作</th></tr></thead><tbody><tr v-for="row in mappings" :key="row.reference.id"><td>{{ row.candidate.purpose || '用途待填写' }}<br />{{ row.candidate.evidence }}</td><td>{{ row.reference.version }}</td><td>{{ stateText(row.state) }}</td><td>{{ row.canAdoptIdentitySite ? 'Site 身份关系当前有效' : '尚未满足当前采用条件' }}</td><td><button v-if="permissions.has('plm-engineering-product:edit')" :disabled="busy || !!pending.ENGINEERING_MAPPING || !selected.product.enabled" @click="openMapping(row)">修订草稿</button><button v-if="row.state === 'DRAFT' && canConfirm" :disabled="busy || !!pending.ENGINEERING_MAPPING" @click="mappingAction(row, 'CONFIRM')">双方 Owner 确认</button><button v-if="row.state !== 'DISABLED' && permissions.has('plm-engineering-product:edit')" :disabled="busy || !!pending.ENGINEERING_MAPPING" @click="mappingAction(row, 'DISABLE')">停用映射</button><button :disabled="busy" @click="openHistory('ENGINEERING_MAPPING', row.reference.id)">映射历史</button></td></tr></tbody></table></div>
      <p v-if="!mappings.length">尚无映射。可保存未解决草稿，核对来源后再确认。</p><nav class="inline" aria-label="映射分页"><button :disabled="busy || mappingPage === 1" @click="loadMappings(mappingPage - 1)">上一页</button><span>第 {{ mappingPage }} 页</span><button :disabled="busy || mappings.length < 20" @click="loadMappings(mappingPage + 1)">下一页</button></nav>
    </section>
    <section v-if="mappingDraft"><h2>映射草稿</h2><p>选定准确来源版本；缺失或有冲突时可先保存草稿。</p>
      <fieldset :disabled="busy || !!pending.ENGINEERING_MAPPING"><div class="inline"><label>经营料号搜索<input v-model="businessSearch" aria-label="经营料号搜索" maxlength="100" /></label><button type="button" @click="findBusiness">查找 ERP 原对象</button><label>Site 编码搜索<input v-model="siteSearch" aria-label="Site 编码搜索" maxlength="100" /></label><button type="button" @click="findSites">查找实际 Site</button></div>
        <form @submit.prevent="saveMapping"><div class="grid"><label>经营料号版本<select v-model="businessSelection" aria-label="经营料号版本"><option value="">尚未确认</option><option v-for="item in businessChoices" :key="key(item.reference)" :value="key(item.reference)">{{ item.code }} · 版本 {{ item.reference.version }}</option></select></label><label>Site 版本<select v-model="siteSelection" aria-label="Site 版本"><option value="">尚未确认</option><option v-for="item in siteChoices" :key="key(item.reference)" :value="key(item.reference)">{{ item.code }} · 版本 {{ item.reference.version }}</option></select></label>
        <label>用途<textarea v-model="mappingDraft.purpose" aria-label="映射用途" maxlength="8000" /></label><label>复核依据<textarea v-model="mappingDraft.evidence" aria-label="映射依据" maxlength="8000" /></label><label>差异或冲突<textarea v-model="mappingDraft.conflicts" aria-label="映射冲突" maxlength="8000" /></label><label><input v-model="mappingDraft.conflictsResolved" aria-label="冲突已核对" type="checkbox" />已核对并解决身份及范围冲突</label></div><button>保存映射草稿</button><button type="button" @click="mappingDraft = null">取消</button></form>
      </fieldset>
    </section>
    <section v-if="historyId"><h2>历史版本</h2><form class="inline" @submit.prevent="readHistory"><label>版本<input v-model="historyVersion" aria-label="历史版本" pattern="[1-9][0-9]*" :disabled="busy" required /></label><button :disabled="busy">读取历史</button></form><pre v-if="historyText">{{ historyText }}</pre></section>
  </main>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { engineeringApi, type EngineeringCreation, type EngineeringOperationKind, type EngineeringProduct, type EngineeringReference, type EngineeringScope, type EngineeringVersion, type MappingCandidate, type MappingCommand, type MappingVersion, type SourceChoice } from '@/api/plm/engineeringProducts'
import { engineeringResponse, pendingEngineeringOperation, resolveEngineeringOperation, saveEngineeringOperation, type PendingEngineeringOperation } from '@/auth/engineeringOperationRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const permissions = usePermissionStore(), canRead = computed(() => permissions.loaded && permissions.has('plm-engineering-product:query'))
const canConfirm = computed(() => canRead.value && permissions.has('plm-engineering-product:confirm') && permissions.has('erp-product:confirm-engineering'))
const kinds: EngineeringOperationKind[] = ['ENGINEERING_PRODUCT', 'ENGINEERING_MAPPING']
const pending = ref<Record<EngineeringOperationKind, PendingEngineeringOperation | null>>({ ENGINEERING_PRODUCT: null, ENGINEERING_MAPPING: null })
const pendingItems = computed(() => kinds.map(kind => pending.value[kind]).filter((item): item is PendingEngineeringOperation => item !== null))
const busy = ref(false), error = ref(''), notice = ref(''), scope = ref<EngineeringScope | null>(null)
const rows = ref<EngineeringVersion[]>([]), search = ref(''), page = ref(1), selected = ref<EngineeringVersion | null>(null)
const productDraft = ref<EngineeringProduct | null>(null), productEditing = ref<EngineeringVersion | null>(null), productId = ref(''), creation = ref<EngineeringCreation | null>(null), department = ref('')
const mappings = ref<MappingVersion[]>([]), mappingPage = ref(1), mappingDraft = ref<MappingCandidate | null>(null), mappingEditing = ref<MappingVersion | null>(null), mappingId = ref('')
const businessChoices = ref<SourceChoice[]>([]), siteChoices = ref<SourceChoice[]>([]), businessSearch = ref(''), siteSearch = ref(''), businessSelection = ref(''), siteSelection = ref('')
const historyKind = ref<EngineeringOperationKind>('ENGINEERING_PRODUCT'), historyId = ref(''), historyVersion = ref('1'), historyText = ref('')
let generation = 0, live = true
const copy = <T,>(value: T): T => JSON.parse(JSON.stringify(value))
const key = (reference: EngineeringReference) => `${reference.owner}:${reference.id}:${reference.version}:${reference.digest}`
const stateText = (state: MappingVersion['state']) => ({ DRAFT: '待双方确认', CONFIRMED: '双方已确认', DISABLED: '已停用' })[state]
function syncPending() { for (const kind of kinds) pending.value[kind] = scope.value ? pendingEngineeringOperation(scope.value, kind) : null }
function failureText(failure: any) {
  const text = failure?.response?.data?.message || failure?.message || ''
  const known: Record<string, string> = { ENGINEERING_MAPPING_REVIEW_REQUIRED: '请补齐用途、依据并核对冲突。', ENGINEERING_BUSINESS_SOURCE_REQUIRED: '请明确选择 ERP 原料号版本。', ENGINEERING_SITE_SOURCE_REQUIRED: '请明确选择实际 Site 版本。', ENGINEERING_PRODUCT_CHANGED: '工程身份已修订，请刷新后重新核对。', ENGINEERING_PRODUCT_DISABLED: '工程身份已停用。', ENGINEERING_MAPPING_CHANGED: '映射已变化，请刷新后重新核对。', ENGINEERING_BUSINESS_SOURCE_CHANGED: 'ERP 原料号已变化，请重新选择准确版本。', ENGINEERING_SITE_SOURCE_CHANGED: 'Site 已变化，请重新选择准确版本。', ACTION_FORBIDDEN: '当前缺少所需 Owner 的操作权限。', OBJECT_FORBIDDEN: '当前范围不允许读取或操作该对象。', CREATION_AUTHORITY_CHANGED: '创建范围已变化，请重新选择部门。', OPERATIONS_OUTCOME_UNKNOWN: '保存结果尚未确认，请查询原操作。' }
  return known[text] || (/^[A-Z][A-Z0-9_]+$/.test(text) ? '无法完成，请核对输入、来源版本及原操作状态。' : text) || '暂时无法完成。'
}
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const stamp = identityStamp(), current = generation
  const check = () => { requireIdentity(stamp); if (!live || current !== generation || !authenticationCanRead()) throw new Error('当前身份已变化。') }
  busy.value = true; error.value = ''; notice.value = ''
  try { check(); await action(check); check() } catch (failure) { try { check(); error.value = failureText(failure) } catch { /* Old identity response. */ } }
  finally { try { check(); syncPending(); busy.value = false } catch { if (live && current === generation) busy.value = false } }
}
function permit(action: string, check: () => void) { check(); if (!canRead.value || !permissions.has(`plm-engineering-product:${action}`)) throw new Error('当前权限无法执行此操作。') }
async function fetchRows(target: number, check: () => void) {
  rows.value = []; selected.value = null; mappings.value = []; mappingDraft.value = null; historyId.value = ''; historyText.value = ''
  const result = engineeringResponse(await engineeringApi.list(search.value, target)); check(); rows.value = result; page.value = target
}
async function reload() { await run(async check => { rows.value = []; scope.value = null; if (!permissions.loaded) await permissions.loadMyActions(); check(); if (!canRead.value) throw new Error('当前没有读取工程身份的权限。'); const current = engineeringResponse(await engineeringApi.scope()); check(); scope.value = current; await fetchRows(1, check) }) }
async function searchProducts(target: number) { await run(async check => { permit('query', check); await fetchRows(target, check) }) }
async function openProduct() { await run(async check => { permit('add', check); const value = engineeringResponse(await engineeringApi.creationContext()); check(); creation.value = value; department.value = ''; productEditing.value = null; productId.value = crypto.randomUUID(); productDraft.value = { code: '', name: '', category: '', definition: '', identificationSource: '', enabled: true } }) }
function editProduct(row: EngineeringVersion) { productEditing.value = row; productId.value = row.reference.id; productDraft.value = copy(row.product); creation.value = null; department.value = '' }
async function saveProduct() {
  if (!scope.value || !productDraft.value) return
  const current = scope.value, id = productId.value, action = productEditing.value ? 'REVISE' as const : 'CREATE' as const
  const body = { productId: id, action, product: copy(productDraft.value), expectedVersion: productEditing.value?.reference.version ?? null, expectedAuthorityVersion: productEditing.value ? null : creation.value?.authorityVersion ?? null, departmentId: productEditing.value ? null : department.value || null }
  await run(async check => { await saveEngineeringOperation(current, 'ENGINEERING_PRODUCT', id, () => permit(action === 'CREATE' ? 'add' : 'edit', check), operationId => engineeringApi.save({ ...body, operationId })); check(); productDraft.value = null; await fetchRows(page.value, check); notice.value = '工程身份版本已保存。' })
}
async function fetchMappings(target: number, check: () => void) { const id = selected.value?.reference.id; if (!id) return; mappings.value = []; const result = engineeringResponse(await engineeringApi.mappings(id, target)); check(); mappings.value = result; mappingPage.value = target }
async function selectProduct(row: EngineeringVersion) { await run(async check => { selected.value = row; mappingDraft.value = null; historyId.value = ''; historyText.value = ''; await fetchMappings(1, check) }) }
async function loadMappings(target: number) { await run(async check => { await fetchMappings(target, check) }) }
function openMapping(row?: MappingVersion) {
  if (!selected.value) return
  mappingEditing.value = row ?? null; mappingId.value = row?.reference.id ?? crypto.randomUUID()
  mappingDraft.value = row ? { ...copy(row.candidate), engineeringRef: copy(selected.value.reference), conflictsResolved: false } : { engineeringRef: copy(selected.value.reference), businessRef: null, siteRef: null, purpose: '', evidence: '', conflicts: '', conflictsResolved: false }
  businessChoices.value = row?.candidate.businessRef ? [{ reference: copy(row.candidate.businessRef), code: '已保存的经营原对象' }] : []
  siteChoices.value = row?.candidate.siteRef ? [{ reference: copy(row.candidate.siteRef), code: '已保存的 Site' }] : []
  businessSelection.value = row?.candidate.businessRef ? key(row.candidate.businessRef) : ''; siteSelection.value = row?.candidate.siteRef ? key(row.candidate.siteRef) : ''; businessSearch.value = ''; siteSearch.value = ''
}
async function findBusiness() { await run(async check => { businessChoices.value = []; businessSelection.value = ''; const value = engineeringResponse(await engineeringApi.businessChoices(businessSearch.value)); check(); businessChoices.value = value }) }
async function findSites() { await run(async check => { siteChoices.value = []; siteSelection.value = ''; const value = engineeringResponse(await engineeringApi.siteChoices(siteSearch.value)); check(); siteChoices.value = value }) }
async function mappingWrite(body: Omit<MappingCommand, 'operationId'>) {
  const current = scope.value; if (!current) return
  await run(async check => {
    const guard = () => { permit(body.action === 'CONFIRM' ? 'confirm' : 'edit', check); if (body.action === 'CONFIRM' && !canConfirm.value) throw new Error('需要工程与 ERP 两侧确认权限。') }
    await saveEngineeringOperation(current, 'ENGINEERING_MAPPING', body.mappingId, guard, operationId => engineeringApi.saveMapping({ ...body, operationId })); check(); mappingDraft.value = null; await fetchMappings(mappingPage.value, check); notice.value = body.action === 'CONFIRM' ? '双方 Owner 确认已保存，仅适用于指定 Site 身份关系。' : body.action === 'DISABLE' ? '映射已停用，历史保留。' : '映射草稿已保存，尚未确认。'
  })
}
async function saveMapping() { if (!mappingDraft.value) return; const candidate = copy(mappingDraft.value); candidate.businessRef = copy(businessChoices.value.find(item => key(item.reference) === businessSelection.value)?.reference ?? null); candidate.siteRef = copy(siteChoices.value.find(item => key(item.reference) === siteSelection.value)?.reference ?? null); await mappingWrite({ mappingId: mappingId.value, action: 'DRAFT', expectedVersion: mappingEditing.value?.reference.version ?? 'absent', candidate }) }
async function mappingAction(row: MappingVersion, action: 'CONFIRM' | 'DISABLE') { await mappingWrite({ mappingId: row.reference.id, action, expectedVersion: row.reference.version, candidate: null }) }
function openHistory(kind: EngineeringOperationKind, id: string) { historyKind.value = kind; historyId.value = id; historyVersion.value = '1'; historyText.value = '' }
async function readHistory() { await run(async check => { historyText.value = ''; if (historyKind.value === 'ENGINEERING_PRODUCT') { const value = engineeringResponse(await engineeringApi.read(historyId.value, historyVersion.value)); check(); historyText.value = `身份版本 ${value.reference.version}\n${value.product.code} / ${value.product.name}\n${value.product.definition}\n依据：${value.product.identificationSource}` } else { const value = engineeringResponse(await engineeringApi.mapping(historyId.value, historyVersion.value)); check(); historyText.value = `映射版本 ${value.reference.version} · ${stateText(value.state)}\n用途：${value.candidate.purpose}\n依据：${value.candidate.evidence}\n差异：${value.candidate.conflicts}\n${value.canAdoptIdentitySite ? 'Site 身份关系当前有效' : '历史事实不作为当前可用证明'}` } }) }
async function recover(kind: EngineeringOperationKind, close: boolean) {
  const current = scope.value; if (!current) return
  await run(async check => {
    const result = await resolveEngineeringOperation(current, kind, close, check); check()
    if (result.state === 'COMMITTED') {
      if (kind === 'ENGINEERING_PRODUCT' && productId.value === result.result?.objectId) productDraft.value = null
      if (kind === 'ENGINEERING_MAPPING' && mappingId.value === result.result?.objectId) mappingDraft.value = null
    }
    notice.value = result.state === 'COMMITTED' ? '原操作已提交，请刷新查看。' : result.state === 'CLOSED' ? '原操作已结束，可以重新填写。' : '仍无法确认，请继续查询或结束未提交操作。'
  })
}
function clear() { generation++; busy.value = false; error.value = ''; notice.value = ''; scope.value = null; rows.value = []; search.value = ''; page.value = 1; selected.value = null; productDraft.value = null; productEditing.value = null; productId.value = ''; creation.value = null; department.value = ''; mappings.value = []; mappingPage.value = 1; mappingDraft.value = null; mappingEditing.value = null; mappingId.value = ''; businessChoices.value = []; siteChoices.value = []; businessSearch.value = ''; siteSearch.value = ''; businessSelection.value = ''; siteSelection.value = ''; historyId.value = ''; historyVersion.value = '1'; historyText.value = ''; for (const kind of kinds) pending.value[kind] = null }
function identityChanged() { clear(); if (authenticationCanRead()) void reload() }
function storage(event: StorageEvent) { if (event.key?.startsWith('cp6_engineering_operation_v1:')) { try { syncPending() } catch (failure) { error.value = failureText(failure) } } }
watch(canRead, allowed => { if (!allowed) clear() })
onMounted(() => { window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', storage); void reload() })
onBeforeUnmount(() => { live = false; generation++; window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', storage) })
</script>

<style scoped>
.engineering-page { display: grid; gap: 16px; padding: 24px; color: var(--el-text-color-primary); }
header,.inline { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; } header { justify-content: space-between; } h1,h2 { margin: 0 0 8px; }
section { padding: 18px; border: 1px solid var(--el-border-color); border-radius: 8px; background: var(--el-bg-color); } label { display: grid; gap: 6px; }
input,select,textarea,button { font: inherit; padding: 7px 10px; border: 1px solid var(--el-border-color); border-radius: 4px; background: var(--el-bg-color); color: inherit; } textarea { resize: vertical; }
.grid { display: grid; grid-template-columns: repeat(auto-fit,minmax(230px,1fr)); gap: 14px; } button { margin: 4px; cursor: pointer; } button:disabled { opacity: .5; cursor: default; }
fieldset { border: 0; padding: 0; } .table-scroll { overflow: auto; margin-top: 12px; } table { border-collapse: collapse; width: 100%; } th,td { padding: 10px; text-align: left; border-bottom: 1px solid var(--el-border-color-lighter); } [role=alert] { color: var(--el-color-danger); } pre { white-space: pre-wrap; overflow-wrap: anywhere; font: inherit; }
</style>
