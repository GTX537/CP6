<template>
  <div class="uom-page" :aria-busy="busy">
    <header><div><h1>单位配置</h1><p>维护单位版本、精度与换算规则，并确认产品使用的单位版本。</p></div>
      <button type="button" :disabled="busy" @click="reload">刷新</button></header>
    <RouterLink v-if="permissions.has('erp-uom-profile:query')" to="/erp/time-unit-bases">登记单位时间含义</RouterLink>
    <p v-if="error" role="alert">{{ error }}</p>
    <p v-if="notice" role="status">{{ notice }}</p>
    <section v-for="kind in channels" v-show="pending[kind]" :key="kind" class="recovery" aria-label="原操作恢复">
      <p>{{ kind === 'PROFILE' ? '单位配置' : '产品绑定' }}上次操作尚未确认。</p>
      <button type="button" :disabled="busy" @click="recover(kind, false)">查询原操作</button>
      <button type="button" :disabled="busy" @click="recover(kind, true)">结束未提交操作</button>
    </section>
    <section class="panel">
      <header><h2>单位目录</h2><button v-if="permissions.has('erp-uom-profile:add')" type="button" :disabled="busy || !!pending.PROFILE || !scope" @click="openNew">新增单位</button></header>
      <form class="inline" @submit.prevent="loadProfiles(1)"><label>单位编码 <input v-model="search" aria-label="单位编码搜索" maxlength="64" /></label><button :disabled="busy">搜索</button></form>
      <div class="table-scroll"><table><thead><tr><th>编码 / 名称</th><th>量纲</th><th>版本</th><th>小数位</th><th>比例</th><th>状态</th><th>操作</th></tr></thead>
        <tbody><tr v-for="row in rows" :key="row.reference.id"><td>{{ row.profile.code }} / {{ row.profile.name }}</td><td>{{ row.profile.dimensionKey }}</td><td>{{ row.reference.version }}</td><td>{{ row.profile.quantityScale }}</td><td class="ratio">{{ row.profile.numerator }} / {{ row.profile.denominator }}</td><td>{{ row.profile.enabled ? '启用' : '停用' }}</td>
          <td><button v-if="permissions.has('erp-uom-profile:edit')" type="button" :disabled="busy || !!pending.PROFILE" @click="openEdit(row)">修订</button><button type="button" :disabled="busy" @click="openHistory(row)">历史</button></td></tr></tbody></table></div>
      <p v-if="!rows.length">当前范围内没有单位配置。</p>
      <nav class="inline" aria-label="单位分页"><button type="button" :disabled="busy || page === 1" @click="loadProfiles(page - 1)">上一页</button><span>第 {{ page }} 页</span><button type="button" :disabled="busy || rows.length < 20" @click="loadProfiles(page + 1)">下一页</button></nav>
    </section>
    <section v-if="draft" class="panel">
      <h2>{{ editing ? '修订单位配置' : '新增单位配置' }}</h2>
      <p v-if="editing">当前版本 {{ editing.reference.version }}。保存将生成新的版本；历史版本保留。</p>
      <form @submit.prevent="saveProfile"><fieldset :disabled="busy || !!pending.PROFILE"><div class="form-grid">
        <label>单位编码<input v-model="draft.code" aria-label="单位编码" :readonly="!!editing" required maxlength="64" /></label>
        <label>名称<input v-model="draft.name" aria-label="单位名称" required maxlength="200" /></label>
        <label>量纲标识<input v-model="draft.dimensionKey" aria-label="量纲标识" required maxlength="128" /></label>
        <label>允许小数位<input v-model.number="draft.quantityScale" aria-label="允许小数位" type="number" min="0" max="1024" step="1" required /></label>
        <label>包装倍数（留空不限）<input v-model="packText" aria-label="包装倍数" inputmode="decimal" /></label>
        <label>单位类型<select v-model="draft.mode" aria-label="单位类型"><option value="BASE">基准单位</option><option value="RATIO">按比例换算</option></select></label>
        <template v-if="draft.mode === 'RATIO'"><label>搜索基准编码<input v-model="baseSearch" aria-label="基准编码搜索" /></label><button type="button" @click="loadBases">查询基准单位</button>
          <label>基准单位版本<select :value="referenceKey(draft.baseRef)" aria-label="基准单位版本" required @change="selectBase">
            <option value="">请选择</option><option v-if="draft.baseRef && !bases.some(row => referenceKey(row.reference) === referenceKey(draft!.baseRef))" :value="referenceKey(draft.baseRef)">原基准版本 {{ draft.baseRef.version }}（停用时可保留）</option>
            <option v-for="row in bases" :key="referenceKey(row.reference)" :value="referenceKey(row.reference)">{{ row.profile.code }} / {{ row.profile.name }} · 版本 {{ row.reference.version }}</option></select></label></template>
        <label>换算分子<input v-model="draft.numerator" aria-label="换算分子" :readonly="draft.mode === 'BASE'" inputmode="numeric" pattern="[1-9][0-9]*" required /></label>
        <label>换算分母<input v-model="draft.denominator" aria-label="换算分母" :readonly="draft.mode === 'BASE'" inputmode="numeric" pattern="[1-9][0-9]*" required /></label>
        <label v-if="!editing && creation">所属部门<select v-model="department" aria-label="单位所属部门" :required="!creation.allowsUnbound"><option value="">{{ creation.allowsUnbound ? '不绑定部门' : '请选择授权部门' }}</option><option v-for="item in creation.departments" :key="item.id" :value="item.id">{{ item.code }} / {{ item.name }}</option></select></label>
        <label class="check"><input v-model="draft.enabled" type="checkbox" aria-label="启用单位" />启用</label>
      </div><div class="inline"><button>保存新版本</button><button type="button" @click="draft = null">取消</button></div></fieldset></form>
    </section>
    <section v-if="historyTarget" class="panel">
      <h2>{{ historyTarget.profile.code }} 的历史版本</h2><form class="inline" @submit.prevent="loadHistory"><label>版本<input v-model="historyVersion" aria-label="历史版本" pattern="[1-9][0-9]*" required /></label><button :disabled="busy">读取历史</button></form>
      <p v-if="history">版本 {{ history.reference.version }}：{{ history.profile.name }}；量纲 {{ history.profile.dimensionKey }}；小数位 {{ history.profile.quantityScale }}；比例 {{ history.profile.numerator }}/{{ history.profile.denominator }}；{{ history.profile.enabled ? '当时启用' : '当时停用' }}。</p>
    </section>
    <section v-if="permissions.has('erp-product:query')" class="panel">
      <h2>产品单位绑定</h2><p>选择原产品与当前单位版本。产品的原单位编码须与配置编码一致；绑定不替代工程或业务准入。</p>
      <form class="inline" @submit.prevent="findProducts"><label>产品编码<input v-model="productSearch" aria-label="产品编码搜索" required maxlength="100" /></label><button :disabled="busy">查询产品</button></form>
      <label>原产品<select v-model="productId" aria-label="原产品" :disabled="busy || !!pending.PRODUCT_UNIT" @change="loadProduct"><option value="">请选择查询结果</option><option v-for="item in products" :key="item.id" :value="item.id">{{ item.productCd }} · 来源版本 {{ item.productVersion }} · {{ item.unitCode || '尚未配置原单位' }}</option></select></label>
      <template v-if="productEditor"><p>原单位：{{ productEditor.unitCode || '尚未配置' }}；来源版本 {{ productEditor.productVersion }}；{{ productEditor.expectedBindingVersion === 'absent' ? '尚未绑定' : '绑定版本 ' + productEditor.expectedBindingVersion }}。</p>
        <label>采用单位版本<select v-model="bindingProfile" aria-label="采用单位版本" :disabled="busy || !!pending.PRODUCT_UNIT"><option value="">请选择</option><option v-for="row in bindingUnits" :key="referenceKey(row.reference)" :value="referenceKey(row.reference)">{{ row.profile.code }} / {{ row.profile.name }} · 版本 {{ row.reference.version }}</option></select></label>
        <button v-if="permissions.has('erp-product:edit')" type="button" :disabled="busy || !!pending.PRODUCT_UNIT || !bindingProfile || !productEditor.productActive" @click="saveBinding">保存产品单位绑定</button>
        <button type="button" :disabled="busy" @click="readProductUnit">核对当前采用</button>
        <p v-if="productUnit">单位 {{ productUnit.unitCode }}，绑定版本 {{ productUnit.version }}，产品来源版本 {{ productUnit.productVersion }}；仍需完成币种和工程映射。</p>
      </template>
    </section>
    <section class="panel"><h2>核对数量换算</h2><form class="inline" @submit.prevent="convert"><label>来源单位<select v-model="fromUnit" aria-label="换算来源单位" :disabled="busy" required><option value="">请选择当前目录中的单位</option><option v-for="row in rows.filter(item => item.profile.enabled)" :key="row.reference.id" :value="referenceKey(row.reference)">{{ row.profile.code }} · {{ row.reference.version }}</option></select></label>
      <label>目标单位<select v-model="toUnit" aria-label="换算目标单位" :disabled="busy" required><option value="">请选择</option><option v-for="row in rows.filter(item => item.profile.enabled)" :key="row.reference.id" :value="referenceKey(row.reference)">{{ row.profile.code }} · {{ row.reference.version }}</option></select></label><label>数量<input v-model="quantity" aria-label="换算数量" :disabled="busy" inputmode="decimal" required /></label><button :disabled="busy">核对换算</button></form><output v-if="converted">{{ converted }}</output></section>
  </div>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { productUnitApi, uomApi, type ProductUnitChoice, type ProductUnitEditor, type ProductUnitVersion,
  type UomCreationContext, type UomOperationKind, type UomProfile, type UomReference, type UomScope, type UomVersion } from '@/api/erp/uomProfiles'
import { pendingUomOperation, resolveUomOperation, saveUomOperation, uomResponse, type PendingUomOperation } from '@/auth/uomOperationRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'

const permissions = usePermissionStore(), channels: UomOperationKind[] = ['PROFILE', 'PRODUCT_UNIT']
const busy = ref(false), error = ref(''), notice = ref(''), scope = ref<UomScope | null>(null), productScope = ref<UomScope | null>(null)
const pending = ref<Record<UomOperationKind, PendingUomOperation | null>>({ PROFILE: null, PRODUCT_UNIT: null })
const rows = ref<UomVersion[]>([]), page = ref(1), search = ref(''), bases = ref<UomVersion[]>([]), baseSearch = ref('')
const draft = ref<UomProfile | null>(null), editing = ref<UomVersion | null>(null), draftId = ref(''), department = ref(''), creation = ref<UomCreationContext | null>(null), packText = ref('')
const historyTarget = ref<UomVersion | null>(null), historyVersion = ref('1'), history = ref<UomVersion | null>(null)
const products = ref<ProductUnitChoice[]>([]), productSearch = ref(''), productId = ref(''), productEditor = ref<ProductUnitEditor | null>(null)
const bindingUnits = ref<UomVersion[]>([]), bindingProfile = ref(''), productUnit = ref<ProductUnitVersion | null>(null)
const fromUnit = ref(''), toUnit = ref(''), quantity = ref(''), converted = ref('')
let live = true, generation = 0
const referenceKey = (value: UomReference | null) => value ? `${value.id}:${value.version}:${value.digest}` : ''
function guard() {
  const current = generation, stamp = identityStamp()
  return () => { requireIdentity(stamp); if (!live || current !== generation) throw new Error('当前页面身份已变化。') }
}
function syncPending() {
  pending.value.PROFILE = scope.value ? pendingUomOperation(scope.value, 'PROFILE') : null
  pending.value.PRODUCT_UNIT = productScope.value ? pendingUomOperation(productScope.value, 'PRODUCT_UNIT') : null
}
function failureMessage(failure: any): string {
  const code = failure?.response?.data?.message || failure?.message || ''
  const known: Record<string, string> = {
    UOM_VERSION_CHANGED: '单位已更新，请刷新后重新修订。',
    UOM_PROFILE_CHANGED: '所选单位或基准版本已变化，请刷新并重新选择。',
    UOM_PROFILE_DISABLED: '所选单位已停用，请选择当前启用的配置。',
    UOM_CONVERSION_NOT_EXACT: '换算结果无法在目标精度内准确表示，请核对比例或精度。',
    UOM_PRECISION_EXCEEDED: '数量超出声明的精度，未进行舍入。',
    UOM_DIMENSION_MISMATCH: '两种单位的量纲不同，不能换算。', UOM_BASE_MISMATCH: '两种单位的基准不同，不能换算。',
    UOM_PACK_MULTIPLE_VIOLATION: '数量不满足已配置的包装倍数。',
    PRODUCT_UNIT_CODE_MISMATCH: '产品原单位编码与所选单位配置不一致，请核对。',
    PRODUCT_UNIT_SOURCE_CHANGED: '产品来源版本已变化，请重新查询。',
    PRODUCT_UNIT_BINDING_CHANGED: '产品、单位或绑定已变化，请重新查询当前版本。',
    PRODUCT_UNIT_BINDING_REQUIRED: '该产品尚未确认单位绑定。',
    CREATION_AUTHORITY_CHANGED: '可用创建范围已变化，请刷新后重新选择部门。',
    CREATION_DEPARTMENT_FORBIDDEN: '请选择当前授权范围内的部门。',
    ACTION_FORBIDDEN: '当前权限无法执行此操作。', OBJECT_FORBIDDEN: '当前权限无法读取或修改所选对象。',
    OPERATIONS_OUTCOME_UNKNOWN: '尚未确认提交结果，请查询原操作。',
    REAUTH_REQUIRED: '登录状态已变化，请重新确认登录。',
  }
  return known[code] || (/^[A-Z][A-Z0-9_]+$/.test(code) ? '暂时无法完成，请核对输入并确认原操作状态。' : code) || '暂时无法完成，请查询原操作。'
}
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const check = guard(); busy.value = true; error.value = ''; notice.value = ''
  try { await action(check); check() }
  catch (failure: any) { try { check(); error.value = failureMessage(failure) } catch { /* Obsolete response stays isolated. */ } }
  finally {
    try {
      check()
      try { syncPending() } catch (failure: any) { error.value = failure.message }
      busy.value = false
    } catch { /* Identity handler owns current state. */ }
  }
}
async function loadProfiles(target = page.value) { await run(async check => { const result = uomResponse(await uomApi.list(search.value, target)); check(); page.value = target; rows.value = result; fromUnit.value = ''; toUnit.value = ''; converted.value = '' }) }
async function reload() {
  await run(async check => {
    if (!permissions.loaded) await permissions.loadMyActions(); check()
    const result = uomResponse(await uomApi.scope()); check(); scope.value = result
    const values = uomResponse(await uomApi.list(search.value, page.value)); check(); rows.value = values
    if (permissions.has('erp-product:query')) { const current = uomResponse(await productUnitApi.scope()); check(); productScope.value = current }
  })
}
async function loadBases() { await run(async check => { const result = uomResponse(await uomApi.list(baseSearch.value, 1, 100)); check(); bases.value = result.filter(row => row.profile.mode === 'BASE' && row.profile.enabled) }) }
function selectBase(event: Event) { const selected = bases.value.find(row => referenceKey(row.reference) === (event.target as HTMLSelectElement).value); if (draft.value && selected) draft.value.baseRef = JSON.parse(JSON.stringify(selected.reference)) }
async function openNew() {
  await run(async check => {
    const result = uomResponse(await uomApi.creationContext()); check(); creation.value = result
    editing.value = null; draftId.value = crypto.randomUUID(); department.value = ''; packText.value = ''
    draft.value = { code: '', name: '', dimensionKey: '', quantityScale: null, packMultiple: null, mode: 'BASE', baseRef: null, numerator: '1', denominator: '1', enabled: true }
  })
}
function openEdit(value: UomVersion) {
  editing.value = value; draftId.value = value.reference.id; draft.value = JSON.parse(JSON.stringify(value.profile)); packText.value = value.profile.packMultiple ?? ''; creation.value = null
}
watch(() => draft.value?.mode, mode => { if (mode === 'BASE' && draft.value) { draft.value.baseRef = null; draft.value.numerator = '1'; draft.value.denominator = '1' } })
async function saveProfile() {
  if (!scope.value || !draft.value) return
  const selectedScope = scope.value, id = draftId.value, original = editing.value
  const profile = JSON.parse(JSON.stringify({ ...draft.value, packMultiple: packText.value === '' ? null : packText.value })) as UomProfile
  if (!profile.code || !profile.name || !profile.dimensionKey || profile.quantityScale === null || !Number.isInteger(profile.quantityScale)
    || profile.mode === 'RATIO' && !profile.baseRef) { error.value = '请完整填写单位、精度及基准版本。'; return }
  const body = { action: original ? 'REVISE' as const : 'CREATE' as const, profileId: id, expectedVersion: original?.reference.version ?? null,
    expectedAuthorityVersion: original ? null : creation.value?.authorityVersion ?? null, departmentId: original ? null : department.value || null, profile }
  await run(async check => {
    await saveUomOperation(selectedScope, 'PROFILE', id, check, operationId => uomApi.save({ ...body, operationId })); check()
    draft.value = null; const current = uomResponse(await uomApi.list(search.value, page.value)); check(); rows.value = current; notice.value = '单位新版本已保存。'
  })
}
async function recover(kind: UomOperationKind, close: boolean) {
  const selected = kind === 'PROFILE' ? scope.value : productScope.value; if (!selected) return
  await run(async check => {
    const result = await resolveUomOperation(selected, kind, close, check); check()
    if (result.state === 'COMMITTED' && kind === 'PROFILE' && result.result?.objectId === draftId.value) draft.value = null
    notice.value = result.state === 'COMMITTED' ? '原操作已提交。请刷新查看当前版本。' : result.state === 'CLOSED' ? '原操作已结束，可以重新填写。' : '仍无法确认原操作，请继续查询或结束未提交操作。'
  })
}
async function loadHistory() { const target = historyTarget.value; if (!target) return; await run(async check => { const result = uomResponse(await uomApi.read(target.reference.id, historyVersion.value)); check(); history.value = result }) }
function openHistory(value: UomVersion) { historyTarget.value = value; history.value = null; historyVersion.value = '1' }
async function findProducts() { await run(async check => { const result = uomResponse(await productUnitApi.lookup(productSearch.value)); check(); products.value = result; productId.value = ''; productEditor.value = null; productUnit.value = null }) }
async function loadProduct() {
  productEditor.value = null; productUnit.value = null; bindingProfile.value = ''; if (!productId.value) return
  const id = productId.value
  await run(async check => {
    const result = uomResponse(await productUnitApi.editor(id)); check(); productEditor.value = result
    if (result.unitCode) { const choices = uomResponse(await uomApi.list(result.unitCode, 1, 100)); check(); bindingUnits.value = choices.filter(row => row.profile.code === result.unitCode && row.profile.enabled) }
    else bindingUnits.value = []
  })
}
async function saveBinding() {
  const editor = productEditor.value, selectedScope = productScope.value
  const selected = bindingUnits.value.find(row => referenceKey(row.reference) === bindingProfile.value)
  if (!editor || !selectedScope || !selected) return
  const body = { productId: editor.productId, expectedBindingVersion: editor.expectedBindingVersion, productVersion: editor.productVersion, profileRef: JSON.parse(JSON.stringify(selected.reference)) as UomReference }
  await run(async check => {
    productUnit.value = null
    await saveUomOperation(selectedScope, 'PRODUCT_UNIT', editor.productId, check, operationId => productUnitApi.save({ ...body, operationId })); check()
    const current = uomResponse(await productUnitApi.editor(editor.productId)); check(); productEditor.value = current; notice.value = '产品单位绑定已保存。'
  })
}
async function readProductUnit() { const id = productEditor.value?.productId; if (!id) return; await run(async check => { productUnit.value = null; const current = uomResponse(await productUnitApi.read(id)); check(); productUnit.value = current }) }
async function convert() {
  const from = rows.value.find(row => referenceKey(row.reference) === fromUnit.value), to = rows.value.find(row => referenceKey(row.reference) === toUnit.value)
  if (!from || !to) return
  const value = quantity.value
  await run(async check => { converted.value = ''; const result = uomResponse(await uomApi.convert(from.reference, to.reference, value)); check(); converted.value = result.convertedQuantity })
}
watch([fromUnit, toUnit, quantity], () => { converted.value = '' })
function identityChanged() {
  generation++; busy.value = false; scope.value = null; productScope.value = null; rows.value = []; bases.value = []
  draft.value = null; editing.value = null; historyTarget.value = null; history.value = null; products.value = []; productId.value = ''; productEditor.value = null; productUnit.value = null; bindingUnits.value = []
  search.value = ''; baseSearch.value = ''; productSearch.value = ''; quantity.value = ''; fromUnit.value = ''; toUnit.value = ''; bindingProfile.value = ''; page.value = 1
  draftId.value = ''; department.value = ''; creation.value = null; packText.value = ''; historyVersion.value = '1'
  converted.value = ''; error.value = ''; notice.value = ''; pending.value = { PROFILE: null, PRODUCT_UNIT: null }
  if (authenticationCanRead()) void reload()
}
function storage(event: StorageEvent) { if (event.key?.startsWith('cp6_uom_operation_v1:')) { try { syncPending() } catch (failure: any) { error.value = failure.message } } }
onMounted(() => { window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', storage); void reload() })
onBeforeUnmount(() => { live = false; generation++; window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', storage) })
</script>

<style scoped>
.uom-page { padding: 20px; display: grid; gap: 18px; color: var(--text, #183848); }
header, .inline { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
h1, h2 { margin: 0 0 8px; } h1 { font-size: 25px; } h2 { font-size: 19px; } p { line-height: 1.6; }
.panel, .recovery { padding: 20px; background: var(--surface, #fff); border: 1px solid var(--line, #dce7eb); border-radius: 14px; }
.recovery { border-color: #bd861b; background: #fff9e9; } [role='alert'] { color: #a32a2a; }
.form-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 16px; margin-bottom: 18px; }
label { display: grid; gap: 6px; margin: 10px 0; font-weight: 600; } label.check { display: flex; align-items: center; }
input, select, button { font: inherit; padding: 9px 12px; border: 1px solid var(--line, #cddbe1); border-radius: 7px; background: #fff; color: inherit; max-width: 100%; }
input:focus, select:focus, button:focus-visible { outline: 2px solid #16a6ae; outline-offset: 2px; }
button { cursor: pointer; } button:disabled { cursor: default; opacity: .55; } fieldset { margin: 0; padding: 0; border: 0; min-width: 0; }
.table-scroll { overflow-x: auto; } table { width: 100%; border-collapse: collapse; min-width: 700px; } th, td { text-align: left; padding: 12px; border-bottom: 1px solid var(--line, #dce7eb); } .ratio { max-width: 220px; overflow-wrap: anywhere; } output { display: block; margin-top: 14px; overflow-wrap: anywhere; }
@media (max-width: 600px) { .uom-page { padding: 10px; } .panel { padding: 14px; } }
</style>
