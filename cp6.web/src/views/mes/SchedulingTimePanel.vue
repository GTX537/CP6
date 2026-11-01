<template>
  <section aria-label="完整工单改期" class="schedule-time-panel">
    <h3>工单操作改期</h3><p>先准备并确认完整工单的前后计划，再应用改期。改期不代表释放工单或开始执行。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <button :disabled="busy || !can('query')" @click="loadContext">读取可用工单</button>
    <div v-if="pending"><p>原改期结果待核，请保留原操作。</p><button :disabled="busy || !can('command.read')" @click="recover">查询原改期结果</button><button :disabled="busy || !can('change.apply')" @click="retry">重试原改期</button></div>
    <fieldset :disabled="busy || !!pending">
      <label>工单<select v-model="rootId" aria-label="改期工单" @change="clearPlan"><option value="">请选择</option><option v-for="r in context?.roots" :key="r.rootId" :value="r.rootId">{{ r.workOrderNo }} · {{ r.zoneId }}</option></select></label>
      <button :disabled="!rootId || !can('query')" @click="captureRoot">读取完整工单</button>
      <template v-if="capture">
        <p>工厂时区 {{ capture.root.zoneId }} · 读取于 {{ capture.asOf }} · {{ capture.dataKind === 'SIMULATED' ? '上游资料为模拟数据' : capture.dataKind }}</p>
        <p>工单版本 {{ capture.root.expected.revision }} · 当前根计划 {{ interval(capture.root.schedule.interval) }}</p>
        <ul><li v-for="o in capture.root.operations" :key="o.operationId">{{ o.processLabel }} · {{ o.occurrenceRef.id }} · {{ interval(o.interval) }} · 良品 {{ o.goodQty?.value ?? '未知' }}</li></ul>
        <form @submit.prevent="prepare">
          <label>操作<select v-model="operationId" aria-label="改期操作" @change="choose"><option v-for="o in capture.root.operations" :key="o.operationId" :value="o.operationId">{{ o.processLabel }} · {{ o.occurrenceRef.id }}</option></select></label>
          <label>开始时间（含时区）<input v-model="start" aria-label="改期开始" placeholder="2026-11-02T09:00:00-05:00" @input="plan = null" /></label>
          <label>结束时间（含时区）<input v-model="end" aria-label="改期结束" placeholder="2026-11-02T11:00:00-05:00" @input="plan = null" /></label>
          <label>优先级<select v-model="priority" aria-label="改期优先级" @change="plan = null"><option value="">保持原值</option><option value="NORMAL">普通</option><option value="URGENT">紧急</option><option value="EXPEDITE">加急</option></select></label>
          <label>理由<input v-model="reason" aria-label="改期理由" maxlength="500" @input="plan = null" /></label>
          <button type="submit" :disabled="!can('plan.prepare') || !reason.trim() || !capture.completeForRoot">准备改期</button>
        </form>
      </template>
    </fieldset>
    <section v-if="plan">
      <h4>确认完整工单变化</h4><p>根计划：{{ interval(plan.rootApplyGroup.oldRootSchedule.interval) }} → {{ interval(plan.rootApplyGroup.newRootSchedule.interval) }}</p>
      <p>根优先级：{{ plan.rootApplyGroup.oldRootSchedule.priority }} → {{ plan.rootApplyGroup.newRootSchedule.priority }}</p>
      <ul><li v-for="o in plan.capture.root.operations" :key="o.operationId">{{ o.processLabel }}：{{ interval(o.interval) }} → {{ interval(plan.rootApplyGroup.changedOperations.find(c => c.operationId === o.operationId)?.newInterval ?? o.interval) }} {{ plan.rootApplyGroup.changedOperations.some(c => c.operationId === o.operationId) ? '改期' : '保持' }}</li></ul>
      <p v-for="g in plan.gates" :key="g.code + g.operationId">{{ g.code }} · {{ g.operationId }}</p>
      <button :disabled="busy || !!pending || !plan.eligible || !can('change.apply') || !permissions.has('mes-work-order:change.schedule')" @click="apply">确认应用改期</button>
    </section>
    <section v-if="result"><p>改期已提交 · 原回执 {{ result.result.applicationRef.id }}</p><p>工单版本 {{ result.result.oldWoExpected.revision }} → {{ result.result.newWoExpected.revision }}</p><p>自身后继 {{ result.result.ownTransitionRef.id }}</p></section>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { schedulingApi as api, type SchedContext, type SchedCapture, type SchedPlan, type SchedApply, type SchedReply, type SchedInterval } from '@/api/mes/scheduling'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const permissions = usePermissionStore(), can = (action: string) => permissions.has(`mes-planning-board:${action}`)
type Pending = Omit<SchedApply, 'reason'>
const context = ref<SchedContext | null>(null), capture = ref<SchedCapture | null>(null), plan = ref<SchedPlan | null>(null), result = ref<SchedReply | null>(null), pending = ref<Pending | null>(null)
const rootId = ref(''), operationId = ref(''), start = ref(''), end = ref(''), reason = ref(''), priority = ref(''), error = ref(''), notice = ref(''), busy = ref(false)
const storage = computed(() => context.value ? `cp6_sched_v1:${context.value.environmentId}:${context.value.tenantId}:${context.value.actorId}` : '')
let epoch = 0, live = true
const interval = (x: SchedInterval | null) => x ? `${x.start} — ${x.end}` : '未排'
function clearPlan() { capture.value = null; plan.value = null; result.value = null; operationId.value = ''; start.value = ''; end.value = ''; reason.value = ''; priority.value = '' }
function choose() { plan.value = null; const op = capture.value?.root.operations.find(x => x.operationId === operationId.value); start.value = op?.interval?.start ?? ''; end.value = op?.interval?.end ?? '' }
async function run(work: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const version = epoch, identity = identityStamp(); busy.value = true; error.value = ''; notice.value = ''
  const check = () => { requireIdentity(identity); if (!live || epoch !== version || !authenticationCanRead()) throw new Error('当前身份已变化。') }
  try { check(); await work(check) } catch (e: any) { if (live && epoch === version) { error.value = e?.response?.data?.message || e?.message || '改期失败。'; if ([401, 403, 404].includes(e?.response?.status)) clearPlan() } }
  finally { if (live && epoch === version) busy.value = false }
}
async function loadContext() { await run(async check => { if (!can('query')) throw new Error('没有读取权限。'); const value = await api.context(); check(); context.value = value; clearPlan(); rootId.value = ''; const saved = localStorage.getItem(storage.value); pending.value = saved ? JSON.parse(saved) : null }) }
async function captureRoot() { await run(async check => { const value = await api.capture(rootId.value); check(); clearPlan(); capture.value = value; operationId.value = value.root.operations[0]!.operationId; choose() }) }
async function prepare() { await run(async check => {
  if (!can('plan.prepare')) throw new Error('没有准备改期权限。')
  if (![start.value, end.value].every(x => /(?:Z|[+-]\d{2}:\d{2})$/.test(x))) throw new Error('请输入带时区的完整时间。')
  const c = capture.value!, op = c.root.operations.find(x => x.operationId === operationId.value)!
  const value = await api.prepare({ captureId: c.captureId, operationId: op.operationId, expectedScheduleRevision: op.scheduleRevision, woExpected: c.root.expected, kind: 'TIME_ONLY', proposedStart: start.value, proposedEnd: end.value, proposedPriority: priority.value || null, resourceChoiceRef: null, reason: reason.value }); check(); plan.value = value
}) }
async function lock(check: () => void, work: () => Promise<void>) {
  if (!navigator.locks?.request) throw new Error('浏览器不支持多页面提交协调。')
  await navigator.locks.request(storage.value, { mode: 'exclusive', ifAvailable: true }, async active => { check(); if (!active) throw new Error('另一页面正在提交。'); await work() })
}
function accepted(value: SchedReply) { result.value = value; localStorage.removeItem(storage.value); pending.value = null; plan.value = null; capture.value = null; notice.value = '原改期结果已保存。' }
async function send(body: SchedApply, check: () => void, firstAttempt = false) {
  try { const reply = await api.apply(body); check(); accepted(reply) }
  catch (e: any) { check(); if (firstAttempt && [400, 403, 404, 409, 412, 422].includes(e?.response?.status)) { localStorage.removeItem(storage.value); pending.value = null }; throw e }
}
async function apply() { await run(async check => {
  if (!can('change.apply') || !permissions.has('mes-work-order:change.schedule')) throw new Error('没有完整改期权限。')
  const p = plan.value!; await lock(check, async () => {
    if (localStorage.getItem(storage.value)) throw new Error('请先核对原操作。')
    const item: Pending = { commandId: crypto.randomUUID(), planId: p.planId, expectedPlanRevision: p.revision, expectedPlanDigest: p.digest }
    localStorage.setItem(storage.value, JSON.stringify(item)); pending.value = item; await send({ ...item, reason: p.input.reason }, check, true)
  })
}) }
async function recover() { await run(async check => { if (!can('command.read')) throw new Error('没有原结果读取权限。'); await lock(check, async () => { const lookup = await api.command(pending.value!.commandId); check(); if (lookup.status === 'FOUND') accepted(lookup.reply!); else notice.value = '尚未观察到原结果，不能据此判定未提交。' }) }) }
async function retry() { await run(async check => {
  if (!can('change.apply')) throw new Error('没有改期权限，请查询原结果。')
  await lock(check, async () => { const item = pending.value!; const original = await api.plan(item.planId); check(); if (original.digest !== item.expectedPlanDigest || original.revision !== item.expectedPlanRevision) throw new Error('原计划身份不符。'); await send({ ...item, reason: original.input.reason }, check) })
}) }
function identityChanged() { epoch++; busy.value = false; clearPlan(); context.value = null; pending.value = null; rootId.value = ''; error.value = ''; notice.value = '' }
onMounted(() => { window.addEventListener(AUTH_EVENT, identityChanged); if (authenticationCanRead() && can('query')) void loadContext() })
onBeforeUnmount(() => { live = false; epoch++; window.removeEventListener(AUTH_EVENT, identityChanged) })
</script>
<style scoped>
.schedule-time-panel { padding: 16px; margin-bottom: 16px; background: var(--el-bg-color, white); border: 1px solid var(--el-border-color, #ddd); border-radius: 6px; }
fieldset { border: 0; padding: 0; } label { display: inline-flex; flex-direction: column; margin: 6px 12px 6px 0; } input { min-width: 240px; } button { margin: 6px; } [role=alert] { color: #a62626; }
</style>
