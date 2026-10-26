<template>
  <main class="tool-specifications" :aria-busy="busy">
    <header><div><h1>工具技术规格草稿</h1><p>按准确版模修订编制技术要求；草稿、技术批准和实物资格分别核对。</p></div><button :disabled="busy" @click="reload">刷新列表</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <section v-if="pending" class="panel"><p>有待确认的原操作，请先查询结果。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy" @click="recover(true)">结束未提交操作</button></section>
    <section class="panel">
      <form @submit.prevent="loadPage(1)"><label>基地、编号或名称 <input v-model="search" aria-label="工具搜索" maxlength="100" /></label><button :disabled="busy">搜索工具</button></form>
      <div class="table-scroll"><table><thead><tr><th>基地 / 完整编号</th><th>业务修订</th><th>技术草稿版本</th><th>来源核对</th><th>操作</th></tr></thead><tbody>
        <tr v-for="row in rows" :key="row.source.reference.id"><td>{{ row.source.source.identity.baseCode }} / {{ row.source.source.identity.number }}</td><td>{{ row.source.source.identity.businessRevision }}</td><td>{{ row.specification?.reference.version || '尚未编制' }}</td><td>{{ row.specification ? row.sourceMatches && row.productMatches ? '原来源相符' : '来源已变化' : '待编制' }}</td><td><button :disabled="busy" @click="open(row.source.reference.id)">打开草稿</button></td></tr>
      </tbody></table></div><p v-if="!rows.length">当前权限范围内没有工具。</p>
      <nav aria-label="工具分页"><button :disabled="busy || page === 1" @click="loadPage(page - 1)">上一页</button><span>第 {{ page }} 页</span><button :disabled="busy || rows.length < 20" @click="loadPage(page + 1)">下一页</button></nav>
    </section>
    <section v-if="selected" class="panel" aria-label="技术规格草稿">
      <h2>{{ selected.source.source.identity.name }}</h2><p>{{ selected.source.source.identity.baseCode }} / {{ selected.source.source.identity.number }} / 业务修订 {{ selected.source.source.identity.businessRevision }}</p>
      <p>技术草稿版本 {{ selected.specification?.reference.version || '尚未创建' }}；批准：未请求；冻结：未请求；实物资格：未知。</p>
      <p v-if="selected.specification && (!selected.sourceMatches || !selected.productMatches)" role="status">来源已变化。已保存草稿原件保持不变，请明确核对来源后编制下一版。</p>
      <p>工艺与配置填写适用要求；正式适用来源、受控文件和实物关联仍待核对，不能据此执行生产。</p>
      <button :disabled="busy" @click="open(selected.source.reference.id)">重新载入当前草稿</button><p>重新载入会替换本页未保存的输入和预期版本。</p>
      <details><summary>原版模与当前控制版本</summary><p>{{ selected.currentControl.owner }} / {{ selected.currentControl.version }}</p><p>{{ selected.source.currentNativeRowVersion }}</p><pre>{{ selected.source.originalJson }}</pre></details>
      <form v-if="canWrite" aria-label="选择工程产品" @submit.prevent="findProducts"><label>工程产品编码 <input v-model="productSearch" aria-label="工程产品搜索" maxlength="256" /></label><button :disabled="busy || !!pending || completed">查找工程产品</button>
        <label>当前工程产品 <select v-model="productChoice" aria-label="工程产品选择" :disabled="busy || !!pending || completed"><option value="">请选择</option><option v-for="product in products" :key="product.reference.id" :value="product.reference.id">{{ product.product.code }} / {{ product.product.name }} / 版本 {{ product.reference.version }}</option></select></label><button type="button" :disabled="busy || !!pending || completed || !productChoice" @click="chooseProduct">采用所选产品</button>
      </form>
      <form aria-label="保存技术草稿" @submit.prevent="save"><fieldset :disabled="busy || !!pending || completed || !canWrite">
        <p>已选择工程产品：{{ requirements.productRef?.id || '未选择' }}；版本 {{ requirements.productRef?.version || '未选择' }}。</p>
        <label>产品适用范围 <textarea v-model="requirements.productApplicability" aria-label="产品适用范围" maxlength="4000" required /></label>
        <label>工艺要求 <textarea v-model="requirements.processRequirements" aria-label="工艺要求" maxlength="4000" required /></label>
        <label>配置适用要求 <textarea v-model="requirements.configurationApplicability" aria-label="配置适用要求" maxlength="4000" required /></label>
        <label>技术规格要求 <textarea v-model="requirements.technicalRequirements" aria-label="技术规格要求" maxlength="8000" required /></label>
        <label>后续受控文件要求 <textarea v-model="requirements.documentRequirements" aria-label="受控文件要求" maxlength="4000" required /></label>
        <label>编制或修订理由 <textarea v-model="reason" aria-label="草稿理由" maxlength="500" required /></label>
        <button v-if="canWrite" :disabled="!requirements.productRef">保存技术草稿</button>
      </fieldset></form>
      <p v-if="completed">原操作已确认。请重新载入当前草稿后继续编制。</p>
      <form aria-label="读取技术历史" @submit.prevent="readHistory"><label>技术草稿历史版本 <input v-model="historyVersion" aria-label="技术历史版本" pattern="[1-9][0-9]*" required /></label><button :disabled="busy">读取技术历史</button></form>
      <article v-if="history" aria-label="技术草稿历史"><h3>技术草稿 {{ history.reference.version }}</h3><p>原编制理由：{{ history.reason }}</p><pre>{{ history.originalJson }}</pre></article>
    </section>
  </main>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { engineeringApi, type EngineeringScope, type EngineeringReference, type EngineeringVersion } from '@/api/plm/engineeringProducts'
import { toolSpecificationApi as api, type ToolRequirements, type ToolSpecification, type ToolSpecificationView } from '@/api/plm/toolSpecifications'
import { pendingToolSpecification, resolveToolSpecification, saveToolSpecification, specificationResponse as result, type PendingToolSpecification } from '@/auth/toolSpecificationRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const permission = usePermissionStore(), busy = ref(false), error = ref(''), notice = ref('')
const scope = ref<EngineeringScope | null>(null), pending = ref<PendingToolSpecification | null>(null), rows = ref<ToolSpecificationView[]>([])
const selected = ref<ToolSpecificationView | null>(null), history = ref<ToolSpecification | null>(null), completed = ref(false)
const search = ref(''), page = ref(1), historyVersion = ref('1'), reason = ref(''), productSearch = ref(''), productChoice = ref(''), products = ref<EngineeringVersion[]>([])
const empty = () => ({ productRef: null as EngineeringReference | null, productApplicability: '', processRequirements: '', configurationApplicability: '', technicalRequirements: '', documentRequirements: '' })
const requirements = ref(empty()), canWrite = computed(() => !!selected.value && !selected.value.source.source.control.isDeleted && permission.has(`plm-tool-specification:${selected.value.specification ? 'edit' : 'add'}`))
let live = true, generation = 0
function clear() { scope.value = null; pending.value = null; rows.value = []; selected.value = null; history.value = null; requirements.value = empty(); reason.value = ''; productSearch.value = ''; productChoice.value = ''; products.value = []; completed.value = false; notice.value = ''; search.value = ''; page.value = 1 }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const stamp = identityStamp(), current = generation, check = () => { requireIdentity(stamp); if (!live || generation !== current) throw new Error('页面身份已变化。') }
  busy.value = true; error.value = ''
  try { await action(check); check() }
  catch (failure: any) { try { check(); if ([401, 403].includes(failure?.response?.status)) { clear(); error.value = '当前无权读取此工具或来源，请核对权限。' }
    else error.value = failure?.response?.status === 412 ? '原来源或技术控制版本已变化。输入和预期版本已保留；请查询或结束原操作，再明确重新载入。' : failure?.response?.status === 404 ? '该技术草稿历史不存在；不会替换为当前版本。' : failure?.message || '操作尚未确认，请查询原操作。'
  } catch { /* Ignore responses from a previous identity. */ } }
  finally { try { check(); if (scope.value) pending.value = pendingToolSpecification(scope.value); busy.value = false } catch { if (live && generation === current) busy.value = false } }
}
async function reload() { await run(async check => { rows.value = []; selected.value = null; history.value = null; products.value = []; requirements.value = empty(); reason.value = ''; completed.value = false
  const identity = result(await api.scope()); check(); scope.value = identity; const list = result(await api.list(search.value, page.value)); check(); rows.value = list
}) }
async function loadPage(target: number) { await run(async check => { rows.value = []; const list = result(await api.list(search.value, target)); check(); rows.value = list; page.value = target }) }
async function open(id: string) { await run(async check => { const value = result(await api.read(id)); check(); if (value.source.tenantId !== scope.value?.tenantId) throw new Error('来源租户已变化。')
  selected.value = value; history.value = null; historyVersion.value = '1'; products.value = []; productChoice.value = ''; requirements.value = value.specification ? structuredClone(value.specification.requirements) : empty(); reason.value = ''; completed.value = false; notice.value = ''
}) }
async function findProducts() { await run(async check => { const list = result(await engineeringApi.list(productSearch.value)); check(); products.value = list.filter(p => p.isCurrent && p.product.enabled); productChoice.value = '' }) }
function chooseProduct() { if (busy.value || pending.value || completed.value || !canWrite.value) return; const choice = products.value.find(p => p.reference.id === productChoice.value); if (choice) requirements.value.productRef = { ...choice.reference } }
async function save() {
  if (!scope.value || !selected.value || !canWrite.value || pending.value || completed.value || !requirements.value.productRef) return
  const identity = scope.value, view = selected.value, body = { action: view.specification ? 'REVISE' as const : 'CREATE' as const, source: { ...view.source.reference }, expectedNativeRowVersion: view.source.currentNativeRowVersion, expectedTechnicalControl: { ...view.currentControl }, requirements: JSON.parse(JSON.stringify(requirements.value)) as ToolRequirements, reason: reason.value }
  await run(async check => { await saveToolSpecification(identity, body.source.id, check, operationId => api.save({ operationId, ...body })); check(); completed.value = true; notice.value = '技术草稿已保存；批准和冻结尚未请求。' })
}
async function recover(close: boolean) { const identity = scope.value; if (!identity) return; await run(async check => { const operation = await resolveToolSpecification(identity, close, check); check(); completed.value = operation.state === 'COMMITTED'; notice.value = operation.state === 'UNKNOWN' ? '结果仍未知，请继续查询或结束未提交操作。' : operation.state === 'CLOSED' ? '未提交操作已结束；输入仍保留。' : '原操作已提交，请重新载入当前草稿。' }) }
async function readHistory() { const id = selected.value?.source.reference.id; if (!id) return; await run(async check => { history.value = null; const value = result(await api.read(id, historyVersion.value)); check(); history.value = value.specification }) }
function identityChanged() { generation++; clear(); busy.value = false; error.value = ''; if (authenticationCanRead()) void reload() }
onMounted(() => { window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged); void reload() })
onBeforeUnmount(() => { live = false; generation++; clear(); window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged) })
</script>
<style scoped>
.tool-specifications{max-width:1200px;margin:auto;padding:24px;color:var(--text,#18343d)}header,nav{display:flex;justify-content:space-between;gap:14px;align-items:center}.panel{background:var(--surface,#fff);border:1px solid #d7e3e7;border-radius:12px;padding:22px;margin-top:20px}button,input,select,textarea{font:inherit;padding:8px 12px;border:1px solid #bdcdd3;border-radius:6px;color:inherit;background:#fff}button{cursor:pointer;margin:6px}button:disabled{opacity:.5;cursor:default}button:focus-visible,input:focus,select:focus,textarea:focus{outline:2px solid #14969e;outline-offset:2px}label{display:block;margin:12px 0}textarea{display:block;box-sizing:border-box;width:100%;min-height:80px}fieldset{border:0;padding:0}.table-scroll{overflow:auto}table{width:100%;min-width:680px;border-collapse:collapse}th,td{text-align:left;padding:14px;border-bottom:1px solid #dce5e9;overflow-wrap:anywhere}p,pre{overflow-wrap:anywhere}pre{white-space:pre-wrap;background:#f3f6f7;padding:14px;max-height:320px;overflow:auto}[role=alert]{color:#a02828}@media(max-width:600px){.tool-specifications{padding:10px}.panel{padding:14px}header{align-items:flex-start;flex-direction:column}}
</style>
