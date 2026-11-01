<template>
  <main class="transfer-requests">
    <h1>调拨请求</h1><p>创建移库需求，送审后由另一位有权限的人员批准。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <button :disabled="busy" @click="reload">刷新</button>
    <template v-if="context">
      <p v-if="context.deployment.mode === 'SIMULATION'">当前使用模拟业务数据。</p>
      <section v-if="pending" class="pending"><p>上一笔操作结果待确认。请先查询，避免重复提交。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy" @click="recover(true)">关闭未提交原操作</button></section>
      <section><h2>请求列表</h2>
        <table><thead><tr><th>请求编号</th><th>状态</th><th>版本</th><th>操作</th></tr></thead><tbody>
          <tr v-for="v in rows" :key="v.id"><td>{{ v.draftKey }}</td><td>{{ stateName(v.state) }}</td><td>{{ v.version }}</td><td><button :disabled="busy" @click="open(v.id)">打开</button></td></tr>
        </tbody></table><p v-if="!rows.length">暂无可见请求。</p>
        <button :disabled="busy || page === 1" @click="changePage(-1)">上一页</button><span>第 {{ page }} 页</span><button :disabled="busy" @click="changePage(1)">下一页</button>
        <button v-if="has('add')" :disabled="busy || !!pending" @click="newRequest">新建请求</button>
      </section>
      <section v-if="selected"><h2>{{ selected.draftKey }}</h2><p>{{ stateName(selected.state) }} · 内容版本 {{ selected.version }}</p>
        <table><thead><tr><th>产品</th><th>申请数量</th><th>基本单位数量</th></tr></thead><tbody><tr v-for="line in selected.lines" :key="line.id"><td>{{ line.product.label }}</td><td>{{ line.input.inputQuantity }}</td><td>{{ line.baseQuantity }}</td></tr></tbody></table>
        <p>{{ executionName(selected.executionState) }}</p>
        <label>操作原因<textarea v-model="reason" aria-label="操作原因" maxlength="1000" /></label>
        <button v-for="action in selected.allowedActions.filter(x => x !== 'EDIT')" v-show="has(permissionFor(action))" :key="action" :disabled="busy || !!pending || !reason.trim()" @click="act(action)">{{ actionName(action) }}</button>
        <template v-if="selected.state === 'APPROVED'">
          <h3>调拨执行阶段</h3>
          <article v-for="stage in selected.stages || []" :key="stage.id">
            <p>{{ stage.scanOccurrenceKey }} · {{ stageName(stage.state) }}</p>
            <ul><li v-for="(line, i) in stage.lines" :key="i">批次 {{ line.lot || '无批次' }}，数量 {{ line.selection.quantity }}，请求起点 {{ line.selection.requestFrom }}</li></ul>
            <template v-if="stage.state === 'PREPARED'">
              <label><input v-model="confirmedStages[stage.id]" type="checkbox" :aria-label="`确认现场状态 ${stage.scanOccurrenceKey}`" />我已核实本次现场状态</label>
              <button v-if="has('execute') && stage.performerId === context.actorId" :disabled="busy || !!pending || !reason.trim() || !confirmedStages[stage.id]" @click="decideStage(stage, true)">确认移库并记账</button>
              <button v-if="has('close-stage')" :disabled="busy || !!pending || !reason.trim() || !confirmedStages[stage.id]" @click="decideStage(stage, false)">确认未发生并关闭</button>
            </template>
          </article>
          <fieldset v-if="has('prepare') && selected.executionState !== 'COMPLETED'" :disabled="busy || !!pending">
            <legend>准备新的移库阶段</legend>
            <button type="button" @click="loadStockChoices">读取当前可用库存</button>
            <label>设备标识<input v-model="device" aria-label="设备标识" maxlength="100" /></label>
            <label>本次扫描流水<input v-model="scanKey" aria-label="本次扫描流水" maxlength="200" /></label>
            <article v-for="(part, i) in stageParts" :key="i">
              <label>库存<select v-model="part.choice" :aria-label="`阶段库存 ${i + 1}`"><option disabled value="">请选择库存</option><option v-for="choice in stockChoices" :key="`${choice.requestLineId}/${choice.stockId}`" :value="`${choice.requestLineId}/${choice.stockId}`">{{ selected.lines.find(l => l.id === choice.requestLineId)?.product.label }} · {{ choice.lot || '无批次' }} · 可用 {{ choice.available }} {{ choice.unitCode }}</option></select></label>
              <label>请求数量起点<input v-model="part.from" :aria-label="`请求起点 ${i + 1}`" inputmode="decimal" /></label>
              <label>本次数量<input v-model="part.quantity" :aria-label="`阶段数量 ${i + 1}`" inputmode="decimal" /></label>
              <button type="button" @click="stageParts.splice(i, 1)">移除此段</button>
            </article>
            <button type="button" :disabled="stageParts.length >= 500" @click="stageParts.push({ choice: '', from: '0', quantity: '' })">添加移库段</button>
            <button type="button" :disabled="!reason.trim() || !device.trim() || !scanKey.trim() || !stageParts.length" @click="prepareStage">保存待执行阶段</button>
          </fieldset>
        </template>
      </section>
      <form v-if="editing" @submit.prevent="save">
        <h2>{{ selected ? '编辑草稿' : '新建调拨请求' }}</h2>
        <fieldset :disabled="busy || !!pending || !has(selected ? 'edit' : 'add')">
          <template v-if="!selected"><label>请求编号<input v-model="draftKey" aria-label="请求编号" maxlength="200" required /></label>
            <label>所属部门<select v-model="department" aria-label="所属部门"><option v-if="context.allowsUnbound" value="">不指定部门</option><option v-for="d in context.departments" :key="d.id" :value="d.id">{{ d.name }}</option></select></label></template>
          <article v-for="(line, index) in lines" :key="line.key" class="line"><h3>明细 {{ index + 1 }}</h3>
            <label>产品<select v-model="line.product" :aria-label="`产品 ${index + 1}`" required><option disabled value="">请选择</option><option v-for="p in context.products" :key="p.catalogueRef.id" :value="p.catalogueRef.id">{{ p.label }}</option></select></label>
            <label>计量单位<select v-model="line.unit" :aria-label="`单位 ${index + 1}`" required><option disabled value="">请选择</option><option v-for="u in context.units" :key="u.reference.id" :value="u.reference.id">{{ u.profile.name }}（{{ u.profile.code }}）</option></select></label>
            <label>数量<input v-model="line.quantity" :aria-label="`数量 ${index + 1}`" inputmode="decimal" required /></label>
            <label>源库位<select v-model="line.from" :aria-label="`源库位 ${index + 1}`" required><option disabled value="">请选择</option><option v-for="l in context.locations" :key="l.locationId" :value="l.locationId">{{ l.label }}</option></select></label>
            <label>目标库位<select v-model="line.to" :aria-label="`目标库位 ${index + 1}`" required><option disabled value="">请选择</option><option v-for="l in context.locations" :key="l.locationId" :value="l.locationId">{{ l.label }}</option></select></label>
            <button type="button" :disabled="lines.length === 1" @click="lines.splice(index, 1)">移除明细</button>
          </article>
          <button type="button" :disabled="lines.length >= 500" @click="addLine">添加明细</button>
          <label v-if="!selected">操作原因<textarea v-model="reason" aria-label="操作原因" maxlength="1000" required /></label><button :disabled="!reason.trim()">保存草稿</button>
        </fieldset>
      </form>
    </template>
  </main>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { transferRequestsApi as api, type TransferLineInput, type TransferRequest, type TransferRequestContext, type TransferStage, type TransferStockChoice } from '@/api/wms/transferRequests'
import { pendingTransferRequest, recoverTransferRequest, sendTransferRequest, transferQuantity, transferRequestData as data } from '@/auth/transferRequestRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const permissions = usePermissionStore(), has = (action: string) => permissions.has(`wms-transfer-request:${action}`)
const context = ref<TransferRequestContext | null>(null), rows = ref<TransferRequest[]>([]), selected = ref<TransferRequest | null>(null)
const pending = ref<string | null>(null), busy = ref(false), error = ref(''), notice = ref(''), reason = ref(''), draftKey = ref(''), department = ref(''), page = ref(1), creating = ref(false)
interface Line { key: string; product: string; unit: string; quantity: string; from: string; to: string }
const lines = ref<Line[]>([]), editing = computed(() => creating.value || selected.value?.state === 'DRAFT')
const stockChoices = ref<TransferStockChoice[]>([]), device = ref(''), scanKey = ref(''), confirmedStages = ref<Record<string, boolean>>({})
const stageParts = ref<{ choice: string; from: string; quantity: string }[]>([])
let generation = 0, live = true
const stateName = (s: string) => ({ DRAFT: '草稿', SUBMITTED: '待审批', APPROVED: '已批准', REJECTED: '已驳回', WITHDRAWN: '已撤回' }[s] || s)
const actionName = (s: string) => ({ SUBMIT: '送审', APPROVE: '批准', REJECT: '驳回', WITHDRAW: '撤回' }[s] || s)
const permissionFor = (s: string) => s === 'APPROVE' || s === 'REJECT' ? 'approve' : s.toLowerCase()
const executionName = (s: string) => ({ NOT_DISPATCHED: '尚未移动库存。', PARTIALLY_MOVED: '已完成部分移库。', COMPLETED: '请求数量已全部移动到目标库位。' }[s] || s)
const stageName = (s: string) => ({ PREPARED: '待执行', POSTED: '已记账', CLOSED_NO_EFFECT: '未发生，已关闭' }[s] || s)
function clearStage() { stockChoices.value = []; stageParts.value = []; device.value = ''; scanKey.value = ''; confirmedStages.value = {} }
function clear() { context.value = null; rows.value = []; selected.value = null; pending.value = null; lines.value = []; reason.value = ''; creating.value = false; clearStage() }
async function run(work: (check: () => void) => Promise<void>) {
  if (busy.value) return; const epoch = generation, stamp = identityStamp(); busy.value = true; error.value = ''; notice.value = ''
  const check = () => { requireIdentity(stamp); if (!live || generation !== epoch || !authenticationCanRead()) throw new Error('当前身份已变化。') }
  try { check(); await work(check) } catch (e: any) { if (live && epoch === generation) { if ([401, 403].includes(e?.response?.status)) clear(); error.value = e?.message || '操作失败。' } }
  finally { if (live && epoch === generation) { busy.value = false; if (context.value) { try { pending.value = pendingTransferRequest(context.value) } catch (e: any) { pending.value = 'invalid'; error.value = e.message } } } }
}
async function refreshRows(check: () => void) { const r = data(await api.list(page.value)); check(); rows.value = r }
async function reload() { await run(async check => { clear(); if (!permissions.loaded) await permissions.loadMyActions(); check(); const c = data(await api.context()); check(); context.value = c; await refreshRows(check) }) }
function addLine() { lines.value.push({ key: crypto.randomUUID(), product: '', unit: '', quantity: '', from: '', to: '' }) }
function newRequest() { selected.value = null; creating.value = true; lines.value = []; addLine(); draftKey.value = `TF-${crypto.randomUUID()}`; department.value = context.value?.allowsUnbound ? '' : context.value?.departments[0]?.id || ''; reason.value = '' }
function select(v: TransferRequest) { clearStage(); selected.value = v; creating.value = false; reason.value = ''; lines.value = v.lines.map(l => ({ key: l.input.clientLineKey, product: l.input.productCatalogueRef.id, unit: l.input.inputUomRef.id, quantity: l.input.inputQuantity, from: l.input.sourceLocationId, to: l.input.destinationLocationId })) }
async function loadRequest(id: string, check: () => void) { const v = data(await api.read(id)); check(); select(v) }
async function open(id: string) { await run(check => loadRequest(id, check)) }
async function changePage(delta: number) { await run(async check => { page.value += delta; await refreshRows(check) }) }
function inputs(): TransferLineInput[] {
  const c = context.value!
  return lines.value.map(l => {
    const product = c.products.find(x => x.catalogueRef.id === l.product), unit = c.units.find(x => x.reference.id === l.unit), from = c.locations.find(x => x.locationId === l.from), to = c.locations.find(x => x.locationId === l.to)
    if (!product || !unit || !from || !to || from.locationId === to.locationId) throw new Error('请选择产品、单位及不同的源和目标库位。')
    return { clientLineKey: l.key, productCatalogueRef: product.catalogueRef, inputUomRef: unit.reference, inputQuantity: transferQuantity(l.quantity), sourceWarehouseId: from.warehouseId, sourceLocationId: from.locationId, destinationWarehouseId: to.warehouseId, destinationLocationId: to.locationId }
  })
}
async function save() { await run(async check => {
  const current = selected.value, c = context.value!; if (!has(current ? 'edit' : 'add') || !reason.value.trim()) throw new Error('请确认操作权限并填写原因。')
  const items = inputs(), result = await sendTransferRequest(c, operationId => current
    ? api.edit(current.id, { operationId, expectedRevision: current.revision, etag: current.etag, lines: items, reason: reason.value })
    : api.create({ operationId, draftKey: draftKey.value, departmentId: department.value || null, lines: items, reason: reason.value }), check)
  check(); select(result.request); notice.value = result.changed ? '草稿已保存。' : '内容没有变化。'
  await loadRequest(result.request.id, check); await refreshRows(check)
}) }
async function act(action: string) { await run(async check => {
  const v = selected.value!; if (!has(permissionFor(action)) || !reason.value.trim()) throw new Error('请确认权限并填写操作原因。')
  const result = await sendTransferRequest(context.value!, operationId => api.act(v.id, action, { operationId, expectedRevision: v.revision, etag: v.etag, contentDigest: v.contentDigest, reason: reason.value }), check)
  check(); select(result.request); notice.value = '请求状态已更新。'
  await loadRequest(result.request.id, check); await refreshRows(check)
}) }
async function loadStockChoices() { await run(async check => { const choices = data(await api.stockChoices(selected.value!.id)); check(); stockChoices.value = choices }) }
async function prepareStage() { await run(async check => {
  const v = selected.value!; if (!has('prepare') || !reason.value.trim()) throw new Error('请确认权限并填写操作原因。')
  const items = stageParts.value.map(part => {
    const choice = stockChoices.value.find(x => `${x.requestLineId}/${x.stockId}` === part.choice)
    if (!choice) throw new Error('请先选择当前库存。')
    const from = /^0(?:\.0{1,8})?$/.test(part.from.trim()) ? '0.00000000' : transferQuantity(part.from)
    return { requestLineId: choice.requestLineId, stockId: choice.stockId, requestFrom: from, quantity: transferQuantity(part.quantity) }
  })
  const result = await sendTransferRequest(context.value!, operationId => api.prepareStage(v.id, { operationId, expectedRevision: v.revision, etag: v.etag, contentDigest: v.contentDigest, device: device.value, scanOccurrenceKey: scanKey.value, lines: items, reason: reason.value }), check)
  check(); select(result.request); notice.value = '移库阶段已保存，尚未改变库存。'; await loadRequest(v.id, check); await refreshRows(check)
}) }
async function decideStage(stage: TransferStage, execute: boolean) { await run(async check => {
  const v = selected.value!; if (!has(execute ? 'execute' : 'close-stage') || !reason.value.trim() || !confirmedStages.value[stage.id]) throw new Error('请核实现场状态并填写原因。')
  const result = await sendTransferRequest(context.value!, operationId => api.decideStage(v.id, stage.id, execute, { operationId, expectedRevision: v.revision, etag: v.etag, stageDigest: stage.digest, physicalNotOccurred: !execute, reason: reason.value }), check)
  check(); select(result.request); notice.value = execute ? '移库已记账。' : '未发生的阶段已永久关闭。'; await loadRequest(v.id, check); await refreshRows(check)
}) }
async function recover(close: boolean) { await run(async check => {
  const r = await recoverTransferRequest(context.value!, close, check); check()
  notice.value = r.state === 'UNKNOWN' ? '结果仍未知，请继续查询。' : r.state === 'COMMITTED' ? '已找回原操作结果。' : '未提交原操作已关闭。'
  if (r.state === 'COMMITTED') { select(r.result!.request); await loadRequest(r.result!.request.id, check); await refreshRows(check) }
}) }
function reset() { generation++; busy.value = false; clear(); error.value = ''; notice.value = ''; if (authenticationCanRead()) void reload() }
onMounted(() => { window.addEventListener(AUTH_EVENT, reset); void reload() })
onBeforeUnmount(() => { live = false; generation++; clear(); window.removeEventListener(AUTH_EVENT, reset) })
</script>
<style scoped>
.transfer-requests{max-width:1100px;margin:auto;padding:24px}.transfer-requests section,.transfer-requests fieldset{margin:20px 0;padding:16px;border:1px solid #c7d2dc;border-radius:8px}.transfer-requests label{display:flex;flex-direction:column;gap:5px;margin:10px 0}.transfer-requests input,.transfer-requests select,.transfer-requests textarea{padding:8px}.transfer-requests button{padding:8px 12px;margin:5px}.transfer-requests table{width:100%;text-align:left;border-collapse:collapse}.transfer-requests th,.transfer-requests td{padding:10px;border-bottom:1px solid #dbe3eb}.line{display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:12px;border-bottom:1px solid #dbe3eb;padding:12px 0}.line h3{grid-column:1/-1}.pending{background:#fff8e6}[role=alert]{color:#a32626}
</style>
