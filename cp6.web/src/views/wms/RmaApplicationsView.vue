<template>
  <main class="rma-applications">
    <header><div><h1>退货申请</h1><p>选择原发货明细，保存并提交申请。申请提交后仍须独立授权，尚不能收货或退款。</p></div><RouterLink to="/wms/rma">返回退货查询</RouterLink></header>
    <RouterLink to="/wms/rma-applications">打开正式退货申请</RouterLink>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <button :disabled="busy" @click="reload">重新载入</button>
    <section v-if="pending" class="recovery"><h2>确认上一笔操作</h2><p>上一笔操作的结果尚未确认，查询完成前不能再次提交。</p>
      <button :disabled="busy" @click="recover(false)">查询原操作</button>
      <button :disabled="busy || !canClose" @click="recover(true)">关闭未提交原操作</button>
    </section>
    <section v-if="scope && canAdd">
      <h2>选择原发货单</h2>
      <form class="search" @submit.prevent="searchSources(1)"><label>发货单号或客户编码<input v-model="keyword" aria-label="发货单号或客户编码" maxlength="100" /></label><button :disabled="busy">查找发货单</button></form>
      <div class="table-scroll"><table><thead><tr><th>发货单</th><th>客户编码</th><th>选择</th></tr></thead><tbody>
        <tr v-for="row in sources" :key="row.id"><td>{{ row.number }}</td><td>{{ row.customerCode ?? '未记录客户' }}</td><td><button :disabled="busy || blocked" @click="choose(row.id)">选择 {{ row.number }}</button></td></tr>
      </tbody></table></div><p v-if="!sources.length">当前页没有可选择的已发货单。</p>
      <nav aria-label="发货单分页"><button :disabled="busy || sourcePage === 1" @click="searchSources(sourcePage - 1)">上一页发货单</button><span>第 {{ sourcePage }} 页</span><button :disabled="busy || sources.length < 50" @click="searchSources(sourcePage + 1)">下一页发货单</button></nav>
      <form v-if="source" @submit.prevent="create">
        <fieldset :disabled="busy || blocked"><legend>新建申请 · {{ source.number }}</legend>
          <div class="table-scroll"><table><thead><tr><th>退货</th><th>行 / 产品</th><th>已发数量</th><th>申请数量</th><th>单位</th><th>批次</th></tr></thead><tbody>
            <tr v-for="(line, index) in source.lines" :key="line.id"><td><input v-model="draftLines[index]!.selected" type="checkbox" :aria-label="`选择第${line.lineNumber}行`" /></td><td>{{ line.lineNumber }} · {{ line.productCode }}</td><td>{{ line.shippedQuantity }}</td><td><input v-model="draftLines[index]!.quantity" inputmode="decimal" :aria-label="`第${line.lineNumber}行申请数量`" :disabled="!draftLines[index]!.selected" :required="draftLines[index]!.selected" /></td><td>{{ line.unitCode }}</td><td>{{ line.lotNumber ?? '—' }}</td></tr>
          </tbody></table></div>
          <label>申请原因<textarea v-model="reason" aria-label="申请原因" required maxlength="1000" rows="3" /></label>
          <label>期望处理<select v-model="outcome" aria-label="期望处理"><option v-for="item in outcomes" :key="item[0]" :value="item[0]">{{ item[1] }}</option></select></label>
          <label>备注<textarea v-model="remarks" aria-label="备注" maxlength="1000" rows="2" /></label>
          <button type="submit">保存申请草稿</button>
        </fieldset>
      </form>
    </section>
    <section v-if="scope"><h2>已保存申请</h2>
      <div class="table-scroll"><table><thead><tr><th>原发货单</th><th>客户编码</th><th>状态</th><th>版本</th><th>查看</th></tr></thead><tbody>
        <tr v-for="row in rows" :key="row.id"><td>{{ row.outboundNumber }}</td><td>{{ row.customerCode }}</td><td>{{ stateLabel(row.state) }}</td><td>{{ row.version }}</td><td><button :disabled="busy" @click="open(row.id)">查看申请</button></td></tr>
      </tbody></table></div><p v-if="!rows.length">当前页没有可读取的申请。</p>
      <nav aria-label="申请分页"><button :disabled="busy || page === 1" @click="move(page - 1)">上一页申请</button><span>第 {{ page }} 页</span><button :disabled="busy || rows.length < 50" @click="move(page + 1)">下一页申请</button></nav>
    </section>
    <section v-if="detail" class="detail"><h2>申请详情 · {{ detail.outboundNumber }}</h2><p>{{ stateLabel(detail.state) }} · 版本 {{ detail.version }} / {{ headVersion }}</p>
      <p>原因：{{ detail.reason ?? '无读取权限' }}</p><p>期望处理：{{ outcomes.find(x => x[0] === detail!.requestedOutcome)?.[1] }}</p><p>备注：{{ detail.remarks ?? '未填写或无读取权限' }}</p>
      <div class="table-scroll"><table><thead><tr><th>产品</th><th>申请数量</th><th>单位</th></tr></thead><tbody><tr v-for="line in detail.lines" :key="line.source.id"><td>{{ line.source.productCode }}</td><td>{{ line.requestedQuantity }}</td><td>{{ line.source.unitCode }}</td></tr></tbody></table></div>
      <button :disabled="busy || detail.version === 1" @click="history(detail.version - 1)">上一版本</button><button :disabled="busy || detail.version === headVersion" @click="history(detail.version + 1)">下一版本</button>
      <button v-if="canAdd && detail.state === 'DRAFT' && detail.version === headVersion" :disabled="busy || blocked" @click="submit">提交申请</button>
    </section>
  </main>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { rmaApplicationsApi as api, type RmaScope, type RmaSource, type RmaSourceChoice, type RmaApplication } from '@/api/wms/rmaApplications'
import { pendingRma, recoverRma, writeRma } from '@/auth/rmaApplicationRecovery'
import { AUTH_EVENT } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'

const permissions = usePermissionStore(), canAdd = computed(() => permissions.has('wms-rma:add')), canClose = computed(() => permissions.has('wms-rma:cancel'))
const scope = ref<RmaScope | null>(null), sources = ref<RmaSourceChoice[]>([]), source = ref<RmaSource | null>(null)
const rows = ref<RmaApplication[]>([]), detail = ref<RmaApplication | null>(null), headVersion = ref(1)
const pending = ref<string | null>(null), recoveryError = ref(false), blocked = computed(() => !!pending.value || recoveryError.value)
const busy = ref(false), error = ref(''), notice = ref(''), keyword = ref(''), page = ref(1), sourcePage = ref(1)
const reason = ref(''), remarks = ref(''), outcome = ref('TO_BE_DECIDED'), draftLines = ref<{ id: string; selected: boolean; quantity: string }[]>([])
const outcomes = [['TO_BE_DECIDED', '待协商'], ['RETURN_ONLY', '仅退回实物'], ['CREDIT', '贷项'], ['REFUND', '退款'], ['REPLACEMENT', '换货'], ['SUPPLEMENTARY_SHIPMENT', '补发']] as const
const stateLabel = (state: RmaApplication['state']) => state === 'DRAFT' ? '草稿' : '已提交 · 待授权'
let live = true, generation = 0
function clearForm() { source.value = null; draftLines.value = []; reason.value = ''; remarks.value = ''; outcome.value = 'TO_BE_DECIDED' }
function clear() { scope.value = null; sources.value = []; rows.value = []; detail.value = null; pending.value = null; recoveryError.value = false; clearForm() }
function refreshPending() {
  if (!scope.value) return
  try { pending.value = pendingRma(scope.value); recoveryError.value = false }
  catch (e) { recoveryError.value = true; error.value = (e as Error).message }
}
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const epoch = generation; busy.value = true; error.value = ''; notice.value = ''
  const check = () => { if (!live || generation !== epoch) throw new Error('页面身份已变化。') }
  try { check(); await action(check) }
  catch (e: any) { if (live && generation === epoch) { if (e?.response?.status === 401 || e?.response?.status === 403) clear(); error.value = e?.response?.data?.message || e?.message || '请求未完成。' } }
  finally { if (live && generation === epoch) { busy.value = false; refreshPending() } }
}
async function loadRows(check: () => void) { const value = await api.list(page.value); check(); rows.value = value }
async function reload() { await run(async check => {
  clear(); if (!permissions.loaded) await permissions.loadMyActions(); check()
  if (!permissions.loaded) throw new Error('权限未载入，请重新载入页面。')
  const current = await api.scope(); check(); scope.value = current; refreshPending()
  await loadRows(check)
  if (canAdd.value) { const value = await api.sources(keyword.value, sourcePage.value); check(); sources.value = value }
}) }
async function searchSources(next: number) { await run(async check => {
  sourcePage.value = next; sources.value = []; clearForm()
  const value = await api.sources(keyword.value, next); check(); sources.value = value
}) }
async function choose(id: string) { await run(async check => {
  clearForm(); const value = await api.source(id); check(); source.value = value
  draftLines.value = value.lines.map(line => ({ id: line.id, selected: false, quantity: line.shippedQuantity }))
}) }
async function readDetail(id: string, check: () => void) {
  detail.value = null; const value = await api.read(id); check(); detail.value = value; headVersion.value = value.version
}
async function create() { await run(async check => {
  if (!scope.value || !source.value || !canAdd.value || blocked.value) throw new Error('请先选择发货单并确认原操作。')
  const lines = draftLines.value.filter(x => x.selected).map(x => ({ outboundLineId: x.id, quantity: x.quantity }))
  if (!lines.length || !reason.value.trim()) throw new Error('请选择退货明细并填写申请原因。')
  const command = { applicationId: crypto.randomUUID(), outboundId: source.value.id, sourceVersion: source.value.version, reason: reason.value, requestedOutcome: outcome.value, lines, remarks: remarks.value || null }
  const result = await writeRma(scope.value, operationId => api.create({ ...command, operationId }), check)
  check(); clearForm(); page.value = 1; await loadRows(check); await readDetail(result.applicationId, check); notice.value = '申请草稿已保存，可核对后提交。'
}) }
async function submit() { await run(async check => {
  if (!scope.value || !detail.value || !canAdd.value || blocked.value) throw new Error('当前不能提交申请。')
  const application = detail.value
  await writeRma(scope.value, operationId => api.submit(application, operationId), check)
  check(); await readDetail(application.id, check); await loadRows(check); notice.value = '申请已提交，仍待独立授权。'
}) }
async function recover(close: boolean) { await run(async check => {
  if (!scope.value || close && !canClose.value) throw new Error('当前操作权限不足。')
  const value = await recoverRma(scope.value, close, check); check()
  if (value.state === 'COMMITTED') { clearForm(); await loadRows(check); await readDetail(value.result!.applicationId, check); notice.value = '已找回原操作结果。' }
  else notice.value = value.state === 'CLOSED' ? '未提交原操作已关闭，可以重新操作。' : '结果仍未知，请继续查询原操作。'
}) }
async function open(id: string) { await run(check => readDetail(id, check)) }
async function history(version: number) { await run(async check => { const value = await api.read(detail.value!.id, version); check(); detail.value = value }) }
async function move(next: number) { await run(async check => { page.value = next; rows.value = []; await loadRows(check) }) }
function reset() { generation++; busy.value = false; clear(); page.value = 1; sourcePage.value = 1; keyword.value = ''; error.value = ''; notice.value = ''; if (authenticationCanRead()) void reload() }
onMounted(() => { window.addEventListener(AUTH_EVENT, reset); void reload() })
onBeforeUnmount(() => { live = false; generation++; clear(); window.removeEventListener(AUTH_EVENT, reset) })
</script>

<style scoped>
.rma-applications{max-width:1160px;margin:auto;padding:24px;color:var(--el-text-color-primary)}
header{display:flex;align-items:start;justify-content:space-between;gap:24px}h1{margin:0}h2{font-size:18px}header p{max-width:760px;color:var(--el-text-color-secondary)}
section{margin:20px 0;padding:20px;border:1px solid var(--el-border-color);border-radius:10px;background:var(--el-bg-color)}
fieldset{margin-top:20px;padding:16px;border:1px solid var(--el-border-color);border-radius:8px}.recovery{border-color:var(--el-color-warning)}
label{display:flex;flex-direction:column;gap:6px;margin:12px 0}input,textarea,select{font:inherit;padding:8px;border:1px solid var(--el-border-color);border-radius:5px;background:var(--el-fill-color-blank);color:inherit}
button{font:inherit;padding:8px 12px;margin:4px;border:1px solid var(--el-border-color);border-radius:6px;background:var(--el-fill-color-blank);color:var(--el-color-primary);cursor:pointer}button:disabled{opacity:.5;cursor:default}
.search{display:flex;align-items:end;gap:12px}.search label{flex:1;margin-bottom:4px}.table-scroll{overflow:auto}table{width:100%;border-collapse:collapse}th,td{padding:10px;text-align:left;border-bottom:1px solid var(--el-border-color-lighter);white-space:nowrap}td input:not([type=checkbox]){width:180px}nav{display:flex;align-items:center;gap:10px;margin-top:10px}[role=alert]{color:var(--el-color-danger)}[role=status]{color:var(--el-color-success)}
@media(max-width:650px){.rma-applications{padding:12px}header,.search{display:block}section{padding:12px}}
</style>
