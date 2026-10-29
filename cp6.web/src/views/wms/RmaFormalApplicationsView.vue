<template>
  <main class="applications">
    <header><div><h1>退货申请</h1><p>选择已保存的发货来源，填写申请并提交。申请仍需独立授权，不会直接收货、退款或变更库存。</p></div><button :disabled="busy" @click="reload">重新载入</button></header>
    <RouterLink to="/wms/rma-applications/native-v1">查看早期申请与原操作</RouterLink>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <section v-if="pending" class="recovery"><h2>原请求待确认</h2><p>先查询原结果；尚未观察到结果时，可以重派同一个请求。确认前无法新建其他操作。</p>
      <button :disabled="busy" @click="recover(false)">查询原请求</button><button :disabled="busy" @click="recover(true)">查询并重派原请求</button>
    </section>
    <section v-if="context"><label>仓库<select v-model="scopeIndex" aria-label="仓库" :disabled="busy || blocked" @change="changeScope"><option v-for="(option, index) in context.scopeOptions" :key="option.optionRef.id" :value="index">{{ option.label }}</option></select></label>
      <p v-if="!context.scopeOptions.length">当前没有可用的仓库范围，请联系管理员核对仓库与站点配置及访问权限。</p>
    </section>
    <RmaPolicyPanel v-if="selectedScope && identity" :key="selectedScope.optionRef.id" :scope="identity" :option="selectedScope.optionRef" :can-publish="context!.capabilities.includes('wms.rma.policy.publish')" />
    <RmaDeliveryEvidencePanel v-if="selectedScope && identity && selectedDelivery" :scope="identity" :option="selectedScope.optionRef" :original="selectedDelivery" :can-record="context!.capabilities.includes('wms.rma.evidence.record')" :can-withdraw="context!.capabilities.includes('wms.rma.evidence.withdraw')" />
    <section v-if="selectedScope"><h2>已保存申请</h2>
      <label>状态<select v-model="stateFilter" aria-label="申请状态" :disabled="busy" @change="searchApplications"><option value="">全部</option><option v-for="(label, state) in states" :key="state" :value="state">{{ label }}</option></select></label>
      <div class="scroll"><table><thead><tr><th>申请编号</th><th>状态</th><th>申请时间</th><th>查看</th></tr></thead><tbody><tr v-for="row in applications?.items" :key="row.rmaId"><td>{{ row.displayNo }}</td><td>{{ states[row.applicationState] }}</td><td>{{ row.recordedAt }}</td><td><button :disabled="busy" @click="open(row.rmaId)">查看申请</button></td></tr></tbody></table></div>
      <p v-if="applications && !applications.items.length">当前筛选下没有可读取的申请。</p>
      <nav aria-label="申请分页"><button :disabled="busy || applicationCursors.length === 1" @click="moveApplications(false)">上一页申请</button><span>第 {{ applicationCursors.length }} 页</span><button :disabled="busy || !applications?.nextCursor" @click="moveApplications(true)">下一页申请</button></nav>
    </section>
    <section v-if="selectedScope && canWrite"><h2>添加退货来源</h2>
      <form class="search" @submit.prevent="searchSources"><label>发货单或客户编码<input v-model="keyword" aria-label="来源关键词" maxlength="200" :disabled="busy" /></label><button :disabled="busy || blocked || !!editing">查找来源</button></form>
      <p>显示已保存客户身份的发货来源。可退额度尚待授权核定，原发货数量不代表可批准额度。</p>
      <div class="scroll"><table><thead><tr><th>发货单</th><th>客户</th><th>产品</th><th>原发货数量</th><th>选择</th></tr></thead><tbody><tr v-for="choice in sources" :key="choice.optionRef.id"><td>{{ choice.shipment }}</td><td>{{ choice.customer }}</td><td>{{ choice.product }}</td><td>{{ choice.original.baseQty.value }} {{ choice.original.baseQty.uom.code }}</td><td><button :disabled="busy || blocked || !!editing" @click="add(choice)">添加 {{ choice.product }}</button><button :disabled="busy" @click="selectedDelivery = choice.original">签收证据 {{ choice.product }}</button></td></tr></tbody></table></div>
      <p v-if="!sources.length">当前页没有可选来源。</p>
      <nav aria-label="来源分页"><button :disabled="busy || blocked || sourceCursors.length === 1 || !!editing" @click="moveSources(false)">上一页来源</button><button :disabled="busy || blocked || !sourceNext || !!editing" @click="moveSources(true)">下一页来源</button></nav>
    </section>
    <section v-if="(lines.length || editing) && canWrite"><h2>{{ editing ? '编辑草稿' : '新建申请' }}</h2>
      <form @submit.prevent="save"><fieldset :disabled="busy || blocked">
        <label>申请原因代码<input v-model="reasonCode" aria-label="申请原因代码" required maxlength="200" /></label>
        <label>期望处理<select v-model="outcome" aria-label="期望处理"><option v-for="item in outcomes" :key="item[0]" :value="item[0]">{{ item[1] }}</option></select></label>
        <div v-for="(line, index) in lines" :key="line.lineId" class="line"><strong>{{ line.label }}</strong>
          <label>申请数量（{{ line.requestedQuantity.uom.code }}）<input v-model="line.requestedQuantity.value" :aria-label="`第${index + 1}行申请数量`" inputmode="decimal" required pattern="(0|[1-9][0-9]{0,12})(\.[0-9]{1,8})?" /></label>
          <label>明细原因<textarea v-model="line.reasonDetail" :aria-label="`第${index + 1}行原因`" maxlength="1000" /></label>
          <p v-if="line.evidenceRefs.length">保留 {{ line.evidenceRefs.length }} 项已有证据。</p><button type="button" @click="lines.splice(index, 1)">移除明细</button>
        </div>
        <label>备注<textarea v-model="remarks" aria-label="备注" maxlength="1000" /></label>
        <label v-if="editing">修改原因<input v-model="changeReason" aria-label="修改原因" required maxlength="500" /></label>
        <button type="submit">{{ editing ? '保存修改' : '保存草稿' }}</button><button type="button" @click="clearDraft">取消编辑</button>
      </fieldset></form>
    </section>
    <section v-if="detail"><h2>申请详情</h2><p>{{ states[detail.state] }} · 版本 {{ detail.applicationVersion }} / {{ headVersion }}</p><p>原因：{{ detail.reasonCode }}</p><p>期望处理：{{ outcomes.find(x => x[0] === detail!.requestedOutcome)?.[1] }}</p>
      <div class="scroll"><table><thead><tr><th>明细</th><th>申请数量</th><th>原因</th><th>原订单商业记录</th></tr></thead><tbody><tr v-for="(line, index) in detail.requestedLines" :key="line.lineId"><td>{{ index + 1 }}</td><td>{{ line.requestedQuantity.value }} {{ line.requestedQuantity.uom.code }}</td><td>{{ line.reasonDetail }}</td><td><button v-if="line.originalSnapshot.commercialSnapshotRef" :disabled="busy" @click="readCommercial(line.originalSnapshot)">查看原商业记录</button><span v-else>发运时未保存</span><button :disabled="busy" @click="selectedDelivery = line.originalSnapshot">签收证据 {{ index + 1 }}</button></td></tr></tbody></table></div>
      <section v-if="commercial" aria-label="原订单商业记录"><h3>{{ commercial.original.orderNumber }} · 原订单行 {{ commercial.original.orderLineNumber }} · {{ commercial.original.productCode }}</h3>
        <p>发运时保存于 {{ commercial.original.capturedAt }}。此记录用于追溯，不代表可退额度、贷项或退款批准。</p>
        <p>{{ commercialStates[commercial.currentState] }}</p>
        <p v-if="commercial.current">当前物料：{{ commercial.current.productCode }}；客户：{{ commercial.current.customerCode }}<span v-if="commercial.current.cancelledAt">；订单已取消</span><span v-if="commercial.current.orderDeleted || commercial.current.lineDeleted">；原记录已删除</span></p>
        <div class="scroll"><table><thead><tr><th>字段</th><th>发运时原值</th><th>当前值</th></tr></thead><tbody><tr v-for="field in commercialFields" :key="field.key"><td>{{ field.label }}</td><td>{{ commercialValue(commercial.original.terms[field.key]) }}</td><td>{{ commercial.current ? commercialValue(commercial.current.terms[field.key]) : '原明细已不存在' }}</td></tr></tbody></table></div>
      </section>
      <button :disabled="busy || detail.applicationVersion === 1" @click="history(detail.applicationVersion - 1)">上一版本</button><button :disabled="busy || detail.applicationVersion === headVersion" @click="history(detail.applicationVersion + 1)">下一版本</button>
      <div v-if="isHead && selectedScope && !editing">
        <button v-if="canWrite && detail.state === 'DRAFT'" :disabled="busy || blocked" @click="edit">编辑草稿</button>
        <label v-if="canSubmit && detail.state === 'DRAFT' || canWithdraw && ['DRAFT', 'SUBMITTED'].includes(detail.state)">操作原因<input v-model="transitionReason" aria-label="操作原因" maxlength="500" :disabled="busy || blocked" /></label>
        <button v-if="canSubmit && detail.state === 'DRAFT'" :disabled="busy || blocked || !transitionReason.trim()" @click="transition('SUBMIT')">提交申请</button>
        <button v-if="canWithdraw && authorization?.outcome !== 'APPROVED' && ['DRAFT', 'SUBMITTED'].includes(detail.state)" :disabled="busy || blocked || !transitionReason.trim()" @click="transition('WITHDRAW')">撤回申请</button>
      </div>
      <RmaAuthorizationPanel v-if="identity && isHead && !editing" :scope="identity" :application-id="detail.applicationId" :submitted="detail.state === 'SUBMITTED'" :can-decide="context!.capabilities.includes('wms.rma.authorization.decide')" :can-revoke="context!.capabilities.includes('wms.rma.authorization.revoke')" @current="authorization = $event" />
    </section>
  </main>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, shallowRef, toRaw } from 'vue'
import { rmaFormalApi as api, type FormalApplication, type RmaContext, type RmaOption, type RmaOrderFact, type RmaOriginal, type RmaPage, type RmaPending, type RmaRef, type RmaRequestedLine, type RmaSummary, type SalesReturnSource, type SalesReturnTerms } from '@/api/wms/rmaFormalApplications'
import { rmaApplicationsApi, type RmaScope } from '@/api/wms/rmaApplications'
import { pendingFormal, recoverFormal, writeFormal } from '@/auth/rmaFormalRecovery'
import { AUTH_EVENT } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import RmaPolicyPanel from './RmaPolicyPanel.vue'
import RmaDeliveryEvidencePanel from './RmaDeliveryEvidencePanel.vue'
import RmaAuthorizationPanel from './RmaAuthorizationPanel.vue'
import type { AuthorizationDecision } from '@/api/wms/rmaAuthorizations'

type Choice = RmaOption & { shipment: string; customer: string; product: string; customerRef: RmaRef }
type DraftLine = RmaRequestedLine & { label: string }
const context = shallowRef<RmaContext | null>(null), identity = shallowRef<RmaScope | null>(null), scopeIndex = ref(0)
const selectedScope = computed(() => context.value?.scopeOptions[scopeIndex.value])
const capable = (action: string) => computed(() => context.value?.capabilities.includes('wms.rma.application.' + action) ?? false)
const canWrite = capable('write'), canSubmit = capable('submit'), canWithdraw = capable('withdraw')
const busy = ref(false), error = ref(''), notice = ref(''), pending = shallowRef<RmaPending | null>(null), storageError = ref(false)
const blocked = computed(() => !!pending.value || storageError.value)
const applications = shallowRef<RmaPage<RmaSummary> | null>(null), applicationCursors = ref<(string | null)[]>([null]), stateFilter = ref('')
const sources = shallowRef<Choice[]>([]), sourceCursors = ref<(string | null)[]>([null]), sourceNext = ref<string | null>(null), keyword = ref(''), searchedKeyword = ref('')
const lines = ref<DraftLine[]>([]), customer = shallowRef<RmaRef | null>(null), reasonCode = ref(''), outcome = ref('TO_BE_DECIDED'), remarks = ref(''), changeReason = ref('')
const editing = shallowRef<FormalApplication | null>(null), detail = shallowRef<FormalApplication | null>(null), headVersion = ref(1), transitionReason = ref('')
const isHead = computed(() => detail.value?.applicationVersion === headVersion.value)
const selectedDelivery = shallowRef<RmaOriginal | null>(null)
const authorization = shallowRef<AuthorizationDecision | null>(null)
const commercial = shallowRef<SalesReturnSource | null>(null)
const commercialStates = { LINE_MISSING: '当前原明细已不存在；原商业记录仍保留。', TERMS_UNCHANGED: '当前商业字段与发运时一致。', TERMS_CHANGED: '当前商业字段已变化，原值保持不变。' }
const commercialFields: { key: keyof SalesReturnTerms; label: string }[] = [
  { key: 'currencyCode', label: '币种' }, { key: 'fxRate', label: '汇率' }, { key: 'quantity', label: '原订单数量' }, { key: 'quantityUnit', label: '数量单位' },
  { key: 'priceUnit', label: '计价单位' }, { key: 'setUnitPrice', label: '设定单价' }, { key: 'individualUnitPrice', label: '个别单价' }, { key: 'amount', label: '原订单行金额' },
  { key: 'headerSalesPriceDivision', label: '订单计价区分' }, { key: 'lineSalesPriceDivision', label: '明细计价区分' }, { key: 'specialPriceFlag', label: '特殊价格标记' }, { key: 'provisionalPrice', label: '暂定价格' },
]
const commercialValue = (value: string | boolean | null) => value === null ? '未记录' : typeof value === 'boolean' ? value ? '是' : '否' : value
async function readCommercial(original: RmaOriginal) { await run(async check => { commercial.value = null; const value = await api.commercial(original.shipmentRef.id, original.shipmentLineId); check(); commercial.value = value }) }
const states = { DRAFT: '草稿', SUBMITTED: '已提交', WITHDRAWN: '已撤回', SUPERSEDED: '已替代' }
const outcomes = [['TO_BE_DECIDED', '待协商'], ['RETURN_ONLY', '仅退回实物'], ['CREDIT', '贷项'], ['REFUND', '退款'], ['REPLACEMENT', '换货'], ['SUPPLEMENTARY_SHIPMENT', '补发']] as const
let live = true, generation = 0
function clearDraft() { authorization.value = null; selectedDelivery.value = null; commercial.value = null; lines.value = []; customer.value = null; editing.value = null; reasonCode.value = ''; outcome.value = 'TO_BE_DECIDED'; remarks.value = ''; changeReason.value = '' }
function clear() { context.value = null; identity.value = null; pending.value = null; storageError.value = false; applications.value = null; sources.value = []; detail.value = null; clearDraft(); transitionReason.value = '' }
async function refreshPending(check: () => void) {
  if (!identity.value) return
  try { const value = await pendingFormal(identity.value); check(); pending.value = value; storageError.value = false }
  catch (e) { check(); storageError.value = true; throw e }
}
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const epoch = generation; busy.value = true; error.value = ''; notice.value = ''
  const check = () => { if (!live || epoch !== generation) throw new Error('页面身份已变化。') }
  try { await action(check) }
  catch (e: any) { if (live && epoch === generation) { if ([401, 403].includes(e?.response?.status)) clear(); error.value = e?.response?.data?.detail || e?.response?.data?.message || e?.message || '请求未完成。' } }
  finally {
    if (live && epoch === generation) {
      try { await refreshPending(check) } catch (e: any) { if (live && epoch === generation) error.value = e.message }
      if (live && epoch === generation) busy.value = false
    }
  }
}
async function loadApplications(check: () => void) {
  const value = await api.list({ scopeOptionRef: selectedScope.value!.optionRef, states: stateFilter.value ? [stateFilter.value] : Object.keys(states), cursor: applicationCursors.value.at(-1)!, pageSize: 25 })
  check(); applications.value = value
}
async function loadSources(check: () => void) {
  const page = await api.sources({ scopeOptionRef: selectedScope.value!.optionRef, keyword: searchedKeyword.value || null, customerRef: null, shipmentRef: null, deliveredOnly: false, cursor: sourceCursors.value.at(-1)!, pageSize: 25 }); check()
  const labels = new Map(await Promise.all([...new Set(page.items.map(x => x.original.shipmentRef.id))].map(async id => [id, await api.labels(id)] as const))); check()
  sources.value = page.items.map(option => {
    const snapshot = labels.get(option.original.shipmentRef.id)!
    const order = JSON.parse(snapshot.facts.find(x => x.reference.owner === 'Main.Erp.OrderFulfilment.v1')!.originalJson) as RmaOrderFact
    return { ...option, shipment: snapshot.outboundNumber, customer: snapshot.customerCode, customerRef: snapshot.customerRef, product: order.lines.find(x => x.id === option.original.orderLineId)!.productCode }
  }); sourceNext.value = page.nextCursor
}
async function reload() { await run(async check => {
  clear(); const [ctx, who] = await Promise.all([api.context(), rmaApplicationsApi.scope()]); check(); context.value = ctx; identity.value = who; scopeIndex.value = 0
  applicationCursors.value = [null]; sourceCursors.value = [null]; await refreshPending(check)
  if (selectedScope.value) { await loadApplications(check); if (canWrite.value) await loadSources(check) }
}) }
async function changeScope() { await run(async check => { clearDraft(); detail.value = null; applications.value = null; sources.value = []; applicationCursors.value = [null]; sourceCursors.value = [null]; await loadApplications(check); if (canWrite.value) await loadSources(check) }) }
async function searchApplications() { await run(async check => { applicationCursors.value = [null]; applications.value = null; await loadApplications(check) }) }
async function moveApplications(next: boolean) { await run(async check => { if (next) applicationCursors.value.push(applications.value!.nextCursor); else applicationCursors.value.pop(); applications.value = null; await loadApplications(check) }) }
async function searchSources() { await run(async check => { searchedKeyword.value = keyword.value; sourceCursors.value = [null]; sources.value = []; await loadSources(check) }) }
async function moveSources(next: boolean) { await run(async check => { if (next) sourceCursors.value.push(sourceNext.value); else sourceCursors.value.pop(); sources.value = []; await loadSources(check) }) }
function add(choice: Choice) {
  error.value = ''
  if (customer.value && customer.value.id !== choice.customerRef.id) { error.value = '同一申请请选择同一客户的来源。'; return }
  if (lines.value.length >= 100) { error.value = '一份申请最多包含100行。'; return }
  customer.value = choice.customerRef
  lines.value.push({ lineId: crypto.randomUUID(), originalSelectionRef: choice.optionRef, requestedQuantity: { value: choice.original.baseQty.value, uom: choice.original.baseQty.uom }, reasonDetail: null, evidenceRefs: [], label: `${choice.shipment} · ${choice.product}` })
}
async function showCurrent(id: string, check: () => void) { const value = await api.read(id); check(); detail.value = value; headVersion.value = value.applicationVersion }
async function open(id: string) { await run(async check => { detail.value = null; clearDraft(); await showCurrent(id, check) }) }
async function history(version: number) { await run(async check => { clearDraft(); const value = await api.read(detail.value!.applicationId, version); check(); detail.value = value }) }
async function edit() { await run(async check => {
  const value = await api.editContext(detail.value!.applicationId); check()
  if (value.application.state !== 'DRAFT') throw new Error('申请已变化，请重新读取当前版本。')
  const app = value.application; detail.value = app; headVersion.value = app.applicationVersion; editing.value = app; customer.value = app.customerRef
  reasonCode.value = app.reasonCode; outcome.value = app.requestedOutcome; remarks.value = value.remarks ?? ''; changeReason.value = ''
  lines.value = app.requestedLines.map((line, index) => ({ lineId: line.lineId, originalSelectionRef: line.originalSelectionRef,
    requestedQuantity: { value: line.requestedQuantity.value, uom: line.requestedQuantity.uom }, reasonDetail: line.reasonDetail, evidenceRefs: line.evidenceRefs, label: `明细 ${index + 1}` }))
}) }
async function complete(result: FormalApplication, check: () => void) {
  clearDraft(); detail.value = result; headVersion.value = result.applicationVersion; transitionReason.value = ''; applicationCursors.value = [null]
  await showCurrent(result.applicationId, check); await loadApplications(check)
}
async function save() { await run(async check => {
  const intent = { customerRef: customer.value!, reasonCode: reasonCode.value, requestedOutcome: outcome.value, remarks: remarks.value || null,
    lines: lines.value.map(x => ({ lineId: x.lineId, originalSelectionRef: toRaw(x.originalSelectionRef), requestedQuantity: { value: x.requestedQuantity.value, uom: toRaw(x.requestedQuantity.uom) }, reasonDetail: x.reasonDetail || null, evidenceRefs: x.evidenceRefs.map(toRaw) })) }
  const app = editing.value, applicationId = app?.applicationId ?? crypto.randomUUID()
  const body = app ? { ...intent, expectedApplicationVersion: app.applicationVersion, expectedApplicationHeadRef: app.applicationRef, reason: changeReason.value }
    : { ...intent, scopeOptionRef: selectedScope.value!.optionRef, clientApplicationId: applicationId }
  const result = await writeFormal(identity.value!, { version: 1, commandId: crypto.randomUUID(), applicationId, kind: app ? 'EDIT' : 'CREATE', scopeOptionRef: selectedScope.value!.optionRef, body, etag: app ? `"${app.applicationRef.digest}"` : null }, check)
  check(); await complete(result, check); notice.value = '申请草稿已保存。'
}) }
async function transition(kind: 'SUBMIT' | 'WITHDRAW') { await run(async check => {
  const app = detail.value!
  const result = await writeFormal(identity.value!, { version: 1, commandId: crypto.randomUUID(), applicationId: app.applicationId, kind, scopeOptionRef: selectedScope.value!.optionRef,
    body: { expectedApplicationVersion: app.applicationVersion, expectedApplicationHeadRef: app.applicationRef, reason: transitionReason.value }, etag: `"${app.applicationRef.digest}"` }, check)
  check(); await complete(result, check); notice.value = kind === 'SUBMIT' ? '申请已提交，仍待独立授权。' : '申请已撤回。'
}) }
async function recover(retry: boolean) { await run(async check => {
  const result = await recoverFormal(identity.value!, retry, check); check()
  if (result) {
    clearDraft(); detail.value = result; headVersion.value = result.applicationVersion; await showCurrent(result.applicationId, check)
    scopeIndex.value = context.value!.scopeOptions.findIndex(x => x.warehouseId === result.scope.warehouseId && x.siteId === result.scope.siteId)
    applicationCursors.value = [null]; sourceCursors.value = [null]; sources.value = []; sourceNext.value = null
    if (selectedScope.value) await loadApplications(check)
    notice.value = '已找回原请求结果，显示当前申请版本。'
  }
  else notice.value = '尚未观察到原请求结果，记录已保留。可以稍后查询或重派同一请求。'
}) }
function reset() { generation++; busy.value = false; clear(); error.value = ''; notice.value = ''; keyword.value = ''; searchedKeyword.value = ''; if (authenticationCanRead()) void reload() }
onMounted(() => { window.addEventListener(AUTH_EVENT, reset); void reload() })
onBeforeUnmount(() => { live = false; generation++; clear(); window.removeEventListener(AUTH_EVENT, reset) })
</script>

<style scoped>
.applications{max-width:1160px;margin:auto;padding:24px;color:var(--el-text-color-primary)}header{display:flex;justify-content:space-between;align-items:start;gap:20px}h1{margin:0}h2{font-size:18px}header p{max-width:760px}section{margin:20px 0;padding:20px;border:1px solid var(--el-border-color);border-radius:10px;background:var(--el-bg-color)}label{display:flex;flex-direction:column;gap:6px;margin:12px 0}input,select,textarea,button{font:inherit;padding:8px;border:1px solid var(--el-border-color);border-radius:5px;background:var(--el-fill-color-blank);color:inherit}button{margin:4px;cursor:pointer;color:var(--el-color-primary)}button:disabled{opacity:.5;cursor:default}fieldset{border:0;padding:0}.line{padding:12px;border:1px solid var(--el-border-color-lighter);margin:12px 0}.search{display:flex;gap:12px;align-items:end}.search label{flex:1}.scroll{overflow:auto}table{width:100%;border-collapse:collapse}th,td{padding:10px;text-align:left;border-bottom:1px solid var(--el-border-color-lighter);white-space:nowrap}.recovery{border-color:var(--el-color-warning)}[role=alert]{color:var(--el-color-danger)}[role=status]{color:var(--el-color-success)}@media(max-width:650px){.applications{padding:12px}header,.search{display:block}section{padding:12px}}
</style>
