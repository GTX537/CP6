<template>
  <div class="currency-page" :aria-busy="busy">
    <header><div><h1>币种政策</h1><p>维护金额和单价的小数位数；修订会保留历史版本。</p></div><button :disabled="busy" @click="reload">刷新</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <section v-if="pending" aria-label="原操作恢复"><p>上次保存结果尚未确认。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy" @click="recover(true)">结束未提交操作</button></section>
    <section>
      <header><h2>币种目录</h2><button v-if="permissions.has('erp-currency-policy:add')" :disabled="busy || !!pending || !scope" @click="openNew">新增政策</button></header>
      <form class="inline" @submit.prevent="loadList(1)"><label>币种编码<input v-model="search" aria-label="币种编码搜索" maxlength="128" :disabled="busy" /></label><button :disabled="busy || !canRead">搜索</button></form>
      <div class="table-scroll"><table><thead><tr><th>编码 / 名称</th><th>版本</th><th>金额位数</th><th>单价位数</th><th>状态</th><th>操作</th></tr></thead>
        <tbody><tr v-for="row in rows" :key="row.reference.id"><td>{{ row.policy.code }} / {{ row.policy.name }}</td><td>{{ row.reference.version }}</td><td>{{ row.policy.minorDigits }}</td><td>{{ row.policy.priceDigits }}</td><td>{{ row.policy.enabled ? '启用' : '停用' }}</td><td><button v-if="permissions.has('erp-currency-policy:edit')" :disabled="busy || !!pending" @click="openEdit(row)">修订</button><button :disabled="busy" @click="openHistory(row)">历史</button></td></tr></tbody></table></div>
      <p v-if="!rows.length">当前范围内没有币种政策。</p><nav class="inline" aria-label="政策分页"><button :disabled="busy || page === 1 || !canRead" @click="loadList(page - 1)">上一页</button><span>第 {{ page }} 页</span><button :disabled="busy || rows.length < 20 || !canRead" @click="loadList(page + 1)">下一页</button></nav>
    </section>
    <section v-if="draft"><h2>{{ editing ? '修订币种政策' : '新增币种政策' }}</h2><p>使用四舍六入五成双（HALF_EVEN）；不进行汇率换算。</p>
      <form @submit.prevent="save"><fieldset :disabled="busy || !!pending"><div class="grid">
        <label>币种编码<input v-model="draft.code" aria-label="币种编码" :readonly="!!editing" maxlength="128" required /></label><label>名称<input v-model="draft.name" aria-label="币种名称" maxlength="200" required /></label>
        <label>金额小数位<input v-model.number="draft.minorDigits" aria-label="金额小数位" type="number" min="0" max="4" step="1" required /></label><label>单价小数位<input v-model.number="draft.priceDigits" aria-label="单价小数位" type="number" min="0" max="6" step="1" required /></label>
        <label v-if="!editing && creation">所属部门<select v-model="department" aria-label="政策所属部门" :required="!creation.allowsUnbound"><option value="">{{ creation.allowsUnbound ? '不绑定部门' : '请选择授权部门' }}</option><option v-for="d in creation.departments" :key="d.id" :value="d.id">{{ d.code }} / {{ d.name }}</option></select></label>
        <label><input v-model="draft.enabled" type="checkbox" aria-label="启用政策" />启用</label>
      </div><button>保存新版本</button><button type="button" @click="draft = null">取消</button></fieldset></form>
    </section>
    <section v-if="historyTarget"><h2>{{ historyTarget.policy.code }} 的历史</h2><form class="inline" @submit.prevent="loadHistory"><label>版本<input v-model="historyVersion" aria-label="历史版本" pattern="[1-9][0-9]*" :disabled="busy" required /></label><button :disabled="busy">读取历史</button></form><p v-if="history">版本 {{ history.reference.version }}：{{ history.policy.name }}，金额 {{ history.policy.minorDigits }} 位，单价 {{ history.policy.priceDigits }} 位，{{ history.policy.enabled ? '当时启用' : '当时停用' }}。{{ history.isCurrent ? '当前版本。' : '历史记录，不作为当前政策使用。' }}</p></section>
    <section><h2>核对舍入结果</h2><p>选择当前启用的政策。结果仅用于核对，不产生财务凭证。</p>
      <form class="inline" @submit.prevent="quantize"><label>政策版本<select v-model="selected" aria-label="舍入政策" :disabled="busy" required><option value="">请选择</option><option v-for="row in rows.filter(r => r.policy.enabled)" :key="key(row.reference)" :value="key(row.reference)">{{ row.policy.code }} · 版本 {{ row.reference.version }}</option></select></label>
        <label>数值类型<select v-model="kind" aria-label="数值类型" :disabled="busy"><option value="AMOUNT">金额</option><option value="PRICE">单价</option></select></label><label>原数值<input v-model="value" aria-label="原数值" :disabled="busy" inputmode="decimal" required /></label><button :disabled="busy || !canRead">核对舍入</button></form><output v-if="rounded">{{ rounded }}</output>
    </section>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { currencyApi, type CurrencyCreationContext, type CurrencyPolicy, type CurrencyReference, type CurrencyScope, type CurrencyVersion } from '@/api/erp/currencyPolicies'
import { currencyResponse, pendingCurrencyOperation, resolveCurrencyOperation, saveCurrencyOperation, type PendingCurrencyOperation } from '@/auth/currencyOperationRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const permissions = usePermissionStore(), canRead = computed(() => permissions.loaded && permissions.has('erp-currency-policy:query'))
const busy = ref(false), error = ref(''), notice = ref(''), rows = ref<CurrencyVersion[]>([]), scope = ref<CurrencyScope | null>(null)
const pending = ref<PendingCurrencyOperation | null>(null), search = ref(''), page = ref(1)
const draft = ref<CurrencyPolicy | null>(null), editing = ref<CurrencyVersion | null>(null), draftId = ref(''), creation = ref<CurrencyCreationContext | null>(null), department = ref('')
const historyTarget = ref<CurrencyVersion | null>(null), history = ref<CurrencyVersion | null>(null), historyVersion = ref('1')
const selected = ref(''), kind = ref<'AMOUNT' | 'PRICE'>('AMOUNT'), value = ref(''), rounded = ref('')
let generation = 0, live = true
const key = (r: CurrencyReference) => `${r.owner}:${r.id}:${r.version}:${r.digest}`
function syncPending() { pending.value = scope.value ? pendingCurrencyOperation(scope.value, 'POLICY') : null }
function message(failure: any) {
  const code = failure?.response?.data?.message || failure?.message || ''
  const known: Record<string, string> = { CURRENCY_VERSION_CHANGED: '政策已更新，请刷新后重新修订。', CURRENCY_POLICY_CHANGED: '所选政策版本已变化，请刷新后重新选择。', CURRENCY_POLICY_DISABLED: '所选政策已停用。', CURRENCY_IDENTITY_CONFLICT: '该编码或身份已存在，请核对。', CURRENCY_POLICY_INCOMPLETE: '请明确填写有效的小数位数。', CREATION_AUTHORITY_CHANGED: '可用创建范围已变化，请重新选择部门。', CREATION_DEPARTMENT_FORBIDDEN: '请选择当前授权范围内的部门。', ACTION_FORBIDDEN: '当前权限无法执行此操作。', OBJECT_FORBIDDEN: '当前权限无法读取或修改该政策。', OPERATIONS_OUTCOME_UNKNOWN: '保存结果尚未确认，请查询原操作。' }
  return known[code] || (/^[A-Z][A-Z0-9_]+$/.test(code) ? '无法完成，请核对输入和原操作状态。' : code) || '暂时无法完成。'
}
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const stamp = identityStamp(), current = generation
  const check = () => { requireIdentity(stamp); if (!live || current !== generation || !authenticationCanRead()) throw new Error('当前身份已变化。') }
  busy.value = true; error.value = ''; notice.value = ''
  try { check(); await action(check); check() }
  catch (failure) { try { check(); error.value = message(failure) } catch { /* Obsolete response. */ } }
  finally { try { check(); syncPending(); busy.value = false } catch { if (live && current === generation) busy.value = false } }
}
async function reload() {
  await run(async check => {
    rows.value = []; history.value = null; rounded.value = ''; selected.value = ''; scope.value = null
    if (!permissions.loaded) await permissions.loadMyActions(); check()
    if (!canRead.value) throw new Error('当前没有读取币种政策的权限。')
    const current = currencyResponse(await currencyApi.scope()); check(); scope.value = current
    const result = currencyResponse(await currencyApi.list(search.value, page.value)); check(); rows.value = result
  })
}
async function loadList(target: number) {
  await run(async check => { rows.value = []; selected.value = ''; rounded.value = ''; const result = currencyResponse(await currencyApi.list(search.value, target)); check(); rows.value = result; page.value = target })
}
async function openNew() {
  await run(async check => { const result = currencyResponse(await currencyApi.creationContext()); check(); creation.value = result; department.value = ''; editing.value = null; draftId.value = crypto.randomUUID(); draft.value = { code: '', name: '', minorDigits: null, priceDigits: null, rounding: 'HALF_EVEN', enabled: true } })
}
function openEdit(row: CurrencyVersion) { editing.value = row; draftId.value = row.reference.id; draft.value = structuredClone(JSON.parse(JSON.stringify(row.policy))); creation.value = null; department.value = '' }
async function save() {
  if (!draft.value || !scope.value) return
  const selectedScope = scope.value, id = draftId.value, action = editing.value ? 'REVISE' as const : 'CREATE' as const
  const body = { action, policyId: id, expectedVersion: editing.value?.reference.version ?? null, expectedAuthorityVersion: editing.value ? null : creation.value?.authorityVersion ?? null, departmentId: editing.value ? null : department.value || null, policy: JSON.parse(JSON.stringify(draft.value)) as CurrencyPolicy }
  await run(async check => {
    const authorized = () => { check(); if (!canRead.value || !permissions.has(`erp-currency-policy:${action === 'CREATE' ? 'add' : 'edit'}`)) throw new Error('当前权限无法保存政策。') }
    await saveCurrencyOperation(selectedScope, 'POLICY', id, authorized, operationId => currencyApi.save({ ...body, operationId })); check(); draft.value = null; rounded.value = ''; selected.value = ''; rows.value = []
    const result = currencyResponse(await currencyApi.list(search.value, page.value)); check(); rows.value = result; notice.value = '政策新版本已保存。'
  })
}
async function recover(close: boolean) {
  const current = scope.value; if (!current) return
  await run(async check => { const result = await resolveCurrencyOperation(current, 'POLICY', close, check); check(); if (result.state === 'COMMITTED' && result.result?.objectId === draftId.value) draft.value = null; notice.value = result.state === 'COMMITTED' ? '原操作已提交，请刷新查看。' : result.state === 'CLOSED' ? '原操作已结束，可以重新填写。' : '仍无法确认，请继续查询或结束未提交操作。' })
}
function openHistory(row: CurrencyVersion) { historyTarget.value = row; history.value = null; historyVersion.value = '1' }
async function loadHistory() { const id = historyTarget.value?.reference.id; if (!id) return; await run(async check => { history.value = null; const result = currencyResponse(await currencyApi.read(id, historyVersion.value)); check(); history.value = result }) }
async function quantize() {
  const policy = rows.value.find(row => key(row.reference) === selected.value); if (!policy) return
  const input = value.value, purpose = kind.value, reference = JSON.parse(JSON.stringify(policy.reference)) as CurrencyReference
  await run(async check => { rounded.value = ''; const result = currencyResponse(await currencyApi.quantize(reference, purpose, input)); check(); if (result.value !== input || result.kind !== purpose || key(result.policyRef) !== key(reference) || selected.value !== key(reference) || value.value !== input || kind.value !== purpose) throw new Error('输入或政策已变化，请重新核对。'); rounded.value = result.roundedValue })
}
watch([selected, value, kind], () => { rounded.value = '' })
function clear() { generation++; busy.value = false; rows.value = []; scope.value = null; pending.value = null; draft.value = null; editing.value = null; creation.value = null; department.value = ''; draftId.value = ''; search.value = ''; page.value = 1; historyTarget.value = null; history.value = null; historyVersion.value = '1'; selected.value = ''; value.value = ''; kind.value = 'AMOUNT'; rounded.value = ''; error.value = ''; notice.value = '' }
function identityChanged() { clear(); if (authenticationCanRead()) void reload() }
function storage(event: StorageEvent) { if (event.key?.startsWith('cp6_currency_operation_v1:')) { try { syncPending() } catch (failure) { error.value = message(failure) } } }
watch(canRead, allowed => { if (!allowed) clear() })
onMounted(() => { window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', storage); void reload() })
onBeforeUnmount(() => { live = false; generation++; window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', storage) })
</script>

<style scoped>
.currency-page { display: grid; gap: 16px; padding: 24px; color: var(--el-text-color-primary); }
header,.inline { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; } header { justify-content: space-between; }
h1,h2 { margin: 0 0 8px; } section { background: var(--el-bg-color); border: 1px solid var(--el-border-color); border-radius: 8px; padding: 18px; }
label { display: grid; gap: 6px; } input,select,button { font: inherit; padding: 7px 10px; border: 1px solid var(--el-border-color); border-radius: 4px; background: var(--el-bg-color); color: inherit; }
button { cursor: pointer; margin: 4px; } button:disabled { opacity: .5; cursor: default; } .grid { display: grid; grid-template-columns: repeat(auto-fit,minmax(200px,1fr)); gap: 14px; }
.table-scroll { overflow: auto; margin-top: 16px; } table { width: 100%; border-collapse: collapse; } th,td { text-align: left; padding: 10px; border-bottom: 1px solid var(--el-border-color-lighter); }
fieldset { border: 0; padding: 0; } [role=alert] { color: var(--el-color-danger); } output { display: block; margin-top: 12px; overflow-wrap: anywhere; }
</style>
