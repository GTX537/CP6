<template>
  <section class="rebind-panel" aria-label="资源换机">
    <h2>资源换机</h2><p>换机保留工序与实际记录。完成后仍禁止新增执行，需另行取得执行许可。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <button :disabled="busy || !(initialWorkId ? can('work.read') : can('query'))" @click="load">刷新换机入口</button>
    <div v-if="context && !initialPlan && !initialWorkId && !saved.pending && !saved.workId">
      <select v-model="rootId" aria-label="换机工单" @change="clearSelection"><option value="">选择工单</option><option v-for="r in context.roots" :key="r.rootId" :value="r.rootId">{{ r.workOrderNo }}</option></select>
      <button :disabled="busy || !rootId" @click="readRoot">读取工单工序</button>
      <select v-if="capture" v-model="operationId" aria-label="换机工序" @change="rebind = null; plan = null"><option value="">选择工序</option><option v-for="o in capture.root.operations" :key="o.operationId" :value="o.operationId">{{ o.processLabel }}</option></select>
      <button :disabled="busy || !operationId" @click="readRebind">读取换机上下文</button>
    </div>
    <form v-if="rebind && !work && !saved.pending" @submit.prevent="prepare">
      <p>{{ rebind.dataKind }} · 原绑定 {{ rebind.oldBindingRef.id }} · 旧资源 Claim {{ rebind.oldClaims.length }} · 旧执行记录 {{ rebind.oldExecutions.length }}</p>
      <select v-model="choiceId" aria-label="新资源" required @change="plan = null"><option value="">选择合格资源</option><option v-for="c in rebind.choices" :key="c.choiceRef.id" :value="c.choiceRef.id" :disabled="!c.qualified">{{ c.resourceRef.id }} · {{ c.qualified ? '合格' : '不合格' }}</option></select>
      <input v-model="start" aria-label="换机开始时间" placeholder="带时区的完整开始时间" required @input="plan = null" />
      <input v-model="end" aria-label="换机结束时间" placeholder="带时区的完整结束时间" required @input="plan = null" />
      <input v-model="reason" aria-label="换机理由" maxlength="500" required @input="plan = null" />
      <button :disabled="busy || !can('plan.prepare') || !rebind.completeForRebind">准备换机计划</button>
    </form>
    <section v-if="plan && !work && !saved.pending">
      <a :href="'/mes/planning-board/changes/' + plan.planId">打开独立换机计划</a>
      <p>根计划 {{ plan.rootApplyGroup.oldRootSchedule.interval }} → {{ plan.rootApplyGroup.newRootSchedule.interval }}</p>
      <p>完整工单计划：变更 {{ plan.rootApplyGroup.changedOperations.length }} 项，保留 {{ plan.rootApplyGroup.unchangedOperationRefs.length }} 项。</p>
      <p v-for="g in plan.gates" :key="g.code">{{ g.code }}</p>
      <button :disabled="busy || !plan.eligible || !canWrite" @click="apply">申请换机</button>
    </section>
    <p v-if="saved.workId && !work">原换机工作已保存，需要当前 work.read 权限读取进度。</p>
    <section v-if="saved.pending"><p>原操作结果待核对；未观测到不能认定未提交。</p>
      <button :disabled="busy || !can('command.read')" @click="recover">查询原换机命令</button>
      <button :disabled="busy || !canWrite" @click="retry">重试原换机命令</button>
    </section>
    <section v-if="work">
      <a :href="'/mes/planning-board/work/' + work.workId + '?kind=rebind'">打开独立换机工作</a>
      <p>当前自身五域 {{ work.accepted }} · 原计划 {{ work.planId }}</p>
      <p>状态 {{ work.state }} · 版本 {{ work.revision }}</p><p v-if="work.pendingPhase">原阶段 {{ work.pendingPhase }} 结果待查询</p>
      <p v-if="work.noNewExecution">NO_NEW_EXECUTION · 未授予执行许可</p>
      <p>队列迁移 {{ work.queueMigrationState }}</p>
      <ul><li v-for="r in work.receipts" :key="r.phase">{{ r.phase }}：{{ r.outcome }} {{ r.failureCode }}</li></ul>
      <button :disabled="busy || !can('work.read')" @click="refreshWork">刷新换机进度</button>
      <button :disabled="busy || !can('work.reconcile')" @click="reconcile">查询原阶段回执</button>
      <button :disabled="busy || !!saved.pending || !canWrite || terminal || ['PAUSED_STALE', 'PAUSED_REJECTED'].includes(work.state)" @click="act('continue')">继续换机阶段</button>
      <button :disabled="busy || !!saved.pending || !canWrite || terminal || work.stopped || !!work.bindingResult" @click="act('stop')">停止未提交换机</button>
      <button v-if="work.bindingResult && work.queueMigrationState !== 'COMPLETED'" :disabled="busy || !can('work.reconcile')" @click="migrate">完成原绑定队列迁移</button>
      <button v-if="terminal && !saved.pending" :disabled="busy" @click="finishView">返回选择工单</button>
    </section>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { rebindApi as api, type RebindContext, type RebindWork, type RebindReply } from '@/api/mes/rebind'
import type { SchedContext, SchedCapture, SchedPlan } from '@/api/mes/scheduling'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const props = defineProps<{ initialPlan?: SchedPlan; initialWorkId?: string }>()
const permissions = usePermissionStore(), can = (action: string) => permissions.has(`mes-planning-board:${action}`)
const canWrite = computed(() => can('change.apply') && permissions.has('mes-work-order:change.schedule'))
type Pending = { kind: 'apply'; commandId: string; planId: string; revision: string; digest: string } | { kind: 'continue' | 'stop'; commandId: string; workId: string; revision: string }
const saved = ref<{ workId?: string; pending?: Pending }>({})
const context = ref<SchedContext | null>(null), capture = ref<SchedCapture | null>(null), rebind = ref<RebindContext | null>(null), plan = ref<SchedPlan | null>(null), work = ref<RebindWork | null>(null)
const rootId = ref(''), operationId = ref(''), choiceId = ref(''), start = ref(''), end = ref(''), reason = ref(''), busy = ref(false), error = ref(''), notice = ref('')
const storage = computed(() => context.value ? `cp6_rebind_v1:${context.value.environmentId}:${context.value.tenantId}:${context.value.actorId}` : '')
const terminal = computed(() => ['COMPLETED', 'STOPPED_NO_BINDING_CHANGE'].includes(work.value?.state ?? ''))
let epoch = 0, live = true
function persist() { localStorage.setItem(storage.value, JSON.stringify(saved.value)) }
function clearSelection() { capture.value = null; rebind.value = null; plan.value = null; operationId.value = ''; choiceId.value = ''; reason.value = '' }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const version = epoch, identity = identityStamp(); busy.value = true; error.value = ''; notice.value = ''
  const check = () => { requireIdentity(identity); if (!live || version !== epoch || !authenticationCanRead()) throw new Error('当前身份已变化。') }
  try { check(); await action(check) } catch (e: any) { if (live && version === epoch) { error.value = e?.response?.data?.message || e?.message || '换机失败。'; if ([401, 403, 404].includes(e?.response?.status)) { clearSelection(); work.value = null } } }
  finally { if (live && version === epoch) busy.value = false }
}
async function load() { await run(async check => {
  if (props.initialWorkId ? !can('work.read') : !can('query')) throw new Error('没有读取权限。')
  const c = props.initialWorkId ? { ...await api.workContext(), roots: [] } : await api.context(); check(); context.value = c; clearSelection()
  const raw = localStorage.getItem(storage.value); saved.value = raw ? JSON.parse(raw) : {}
  if (props.initialWorkId) {
    if (saved.value.pending && saved.value.workId !== props.initialWorkId) throw new Error('请先核对已保存的另一个原命令，再打开此工作。')
    const w = await api.read(props.initialWorkId); check(); saved.value = { ...saved.value, workId: w.workId }; work.value = w
  } else if (saved.value.workId && can('work.read')) { const w = await api.read(saved.value.workId); check(); work.value = w; if (props.initialPlan) notice.value = '当前显示已保存的原换机工作。完成后点击返回选择工单，继续查看链接中的目标计划。' }
  else if (props.initialPlan) plan.value = props.initialPlan
}) }
async function readRoot() { await run(async check => { const c = await api.capture(rootId.value); check(); clearSelection(); capture.value = c }) }
async function readRebind() { await run(async check => { const c = await api.rebindContext(rootId.value, operationId.value); check(); rebind.value = c; plan.value = null; choiceId.value = ''; const op = c.capture.root.operations.find(o => o.operationId === c.operationId)!; start.value = op.interval?.start ?? ''; end.value = op.interval?.end ?? '' }) }
async function prepare() { await run(async check => {
  if (!can('plan.prepare')) throw new Error('没有准备换机权限。')
  if (![start.value, end.value].every(x => /(?:Z|[+-]\d{2}:\d{2})$/.test(x))) throw new Error('请输入带时区的完整时间。')
  const c = rebind.value!, op = c.capture.root.operations.find(o => o.operationId === c.operationId)!, choice = c.choices.find(x => x.choiceRef.id === choiceId.value)
  if (!choice) throw new Error('请选择上下文中的资源。')
  const p = await api.prepare({ captureId: c.contextId, operationId: op.operationId, expectedScheduleRevision: op.scheduleRevision, woExpected: c.capture.root.expected, kind: 'RESOURCE_CHANGE', resourceChoiceRef: choice.choiceRef, proposedStart: start.value, proposedEnd: end.value, proposedPriority: null, reason: reason.value }); check(); plan.value = p
}) }
async function lock(check: () => void, action: () => Promise<void>) {
  if (!navigator.locks?.request) throw new Error('浏览器不支持多页面提交协调。')
  await navigator.locks.request(storage.value, { mode: 'exclusive', ifAvailable: true }, async active => { check(); if (!active) throw new Error('另一页面正在处理换机。'); await action() })
}
async function accept(reply: RebindReply, check: () => void) { saved.value = { workId: reply.work.workId }; persist(); work.value = reply.work; if (can('work.read')) { const w = await api.read(reply.work.workId); check(); work.value = w } else notice.value = '当前显示原命令快照，最新进度需要 work.read 权限。'; plan.value = null }
async function send(p: Pending, check: () => void, firstAttempt = false) {
  try {
  let reply: RebindReply
  if (p.kind === 'apply') { const original = await api.plan(p.planId); check(); reply = await api.apply({ commandId: p.commandId, planId: p.planId, expectedPlanRevision: p.revision, expectedPlanDigest: p.digest, reason: original.input.reason }) }
  else reply = await api[p.kind](p.workId, { commandId: p.commandId, expectedWorkRevision: p.revision, reason: p.kind === 'continue' ? '继续既有换机计划' : '停止未提交换机' })
  check(); await accept(reply, check)
  } catch (e: any) {
    check()
    // Apply is one transaction. Continue may have committed its inbox before transport fails, so its command must always be retained.
    if (firstAttempt && p.kind === 'apply' && [400, 404, 409, 412, 422].includes(e?.response?.status)) { saved.value = {}; persist(); plan.value = null; rebind.value = null }
    throw e
  }
}
async function apply() { await run(async check => { if (!canWrite.value) throw new Error('没有完整换机权限。'); await lock(check, async () => {
  if (JSON.parse(localStorage.getItem(storage.value) || '{}').pending) throw new Error('请先核对原操作。')
  const p = plan.value!; saved.value = { pending: { kind: 'apply', commandId: crypto.randomUUID(), planId: p.planId, revision: p.revision, digest: p.digest } }; persist(); await send(saved.value.pending!, check, true)
}) }) }
async function act(kind: 'continue' | 'stop') { await run(async check => { if (!canWrite.value) throw new Error('没有完整换机权限。'); await lock(check, async () => {
  if (JSON.parse(localStorage.getItem(storage.value) || '{}').pending) throw new Error('请先核对原操作。')
  const w = work.value!; saved.value = { workId: w.workId, pending: { kind, commandId: crypto.randomUUID(), workId: w.workId, revision: w.revision } }; persist(); await send(saved.value.pending!, check, true)
}) }) }
async function recover() { await run(async check => { const r = await api.command(saved.value.pending!.commandId); check(); if (r.status === 'FOUND' && r.rebindReply) await accept(r.rebindReply, check); else notice.value = '原键尚未观测到，不能据此认定未提交。' }) }
async function retry() { await run(async check => { if (!canWrite.value) throw new Error('没有完整换机权限。'); await lock(check, async () => { await send(saved.value.pending!, check) }) }) }
async function refreshWork() { await run(async check => { const w = await api.read(saved.value.workId!); check(); work.value = w }) }
async function reconcile() { await run(async check => { const w = await api.reconcile(saved.value.workId!); check(); work.value = w }) }
async function migrate() { await run(async check => { await api.migrate(work.value!.workId); check(); const w = await api.read(work.value!.workId); check(); work.value = w }) }
function finishView() { saved.value = {}; persist(); work.value = null; clearSelection(); if (props.initialPlan) plan.value = props.initialPlan }
function changed() { epoch++; busy.value = false; context.value = null; clearSelection(); work.value = null; saved.value = {}; error.value = ''; notice.value = '' }
onMounted(() => { window.addEventListener(AUTH_EVENT, changed); void load() })
onBeforeUnmount(() => { live = false; epoch++; window.removeEventListener(AUTH_EVENT, changed) })
</script>
<style scoped>
.rebind-panel { border: 1px solid #cbd5e1; border-radius: 8px; padding: 16px; margin-bottom: 20px; }
form, .rebind-panel > div { display: flex; gap: 8px; flex-wrap: wrap; margin: 12px 0; } form p { width: 100%; }
button, input, select { min-height: 36px; padding: 6px 10px; } button { margin: 4px; } [role=alert] { color: #b91c1c; }
</style>
