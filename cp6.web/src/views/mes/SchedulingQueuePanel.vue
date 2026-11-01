<template>
  <section class="queue-panel" aria-label="资源执行队列">
    <h3>资源执行队列</h3><p>顺序是执行建议，不授予开工许可，也不改变工艺路线或资源绑定。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <button :disabled="busy || !can('queue.read')" @click="load">读取队列范围</button>
    <fieldset :disabled="busy || !!pending">
      <label>工厂与资源<select v-model="choiceId" aria-label="队列资源" @change="clear"><option value="">请选择资源</option><option v-for="c in context?.resources" :key="c.queueId" :value="c.queueId">{{ c.siteId }} / {{ c.resourceRef.id }}</option></select></label>
      <button :disabled="!choiceId || !can('queue.read')" @click="read(0, true)">读取当前队列</button>
    </fieldset>
    <template v-if="view">
      <a :href="'/mes/resource-queues/' + view.resourceRef.id + '?siteId=' + view.siteId">打开独立资源队列</a>
      <button :disabled="busy || !can('history.read')" @click="readHistory">读取队列历史</button>
      <ul><li v-for="h in history" :key="h.revision">队列版本 {{ h.revision }} · {{ h.members.map(m => m.operationId).join(' → ') }}</li></ul>
      <p>{{ view.authorizationScope }} · {{ view.asOf }} · 队列版本 {{ view.queueRevision }} · 总数 {{ view.totalCount ?? '未知' }} <strong v-if="stale">STALE：请重新读取</strong></p>
      <p v-if="!view.completeForWrite">当前范围不完整，只能查看，不能重排隐藏成员。</p>
      <table><thead><tr><th>位置 / 操作</th><th>执行状态</th><th>工艺路线序号</th></tr></thead><tbody><tr v-for="i in view.items" :key="i.member.itemId"><td>{{ i.queueOrdinal }} / {{ i.member.operationId }}</td><td>{{ i.state }} <strong v-if="i.reassessRequired">QUEUE_REASSESS_REQUIRED：迟到执行事实，请重新评估</strong></td><td>{{ i.member.routeSortOrder }}</td></tr></tbody></table>
      <button :disabled="busy || cursor === 0 || stale" @click="read(cursor - 20)">上一页</button><button :disabled="busy || view.nextCursor === null || stale" @click="read(view!.nextCursor!)">下一页</button>
      <fieldset :disabled="busy || !!pending || !view.completeForWrite || stale">
        <form @submit.prevent="prepare">
          <label>移动项<select v-model="move" aria-label="队列移动项" @change="invalidate"><option value="">请选择</option><option v-for="i in view.items" :key="i.member.itemId" :value="i.member.itemId" :disabled="i.state !== 'WAITING'">{{ i.member.operationId }}</option></select></label>
          <label>位置<select v-model="position" aria-label="队列位置" @change="invalidate"><option value="BEFORE">指定项之前</option><option value="AFTER">指定项之后</option><option value="END">末尾</option></select></label>
          <label v-if="position !== 'END'">相邻项<select v-model="anchor" aria-label="队列相邻项" @change="invalidate"><option value="">请选择</option><option v-for="i in view.items.filter(i => i.member.itemId !== move)" :key="i.member.itemId" :value="i.member.itemId">{{ i.member.operationId }}</option></select></label>
          <label>理由<input v-model="reason" aria-label="队列理由" maxlength="500" @input="invalidate" /></label>
          <button :disabled="!can('queue.prepare') || !move || (position !== 'END' && !anchor) || !reason.trim()">准备队列顺序</button>
        </form>
      </fieldset>
    </template>
    <section v-if="plan">
      <h4>确认完整队列变更</h4><p>完整 {{ plan.oldOrder.length }} 项，{{ plan.affectedItems.length }} 项位置受影响；包含当前页以外成员。</p>
      <p>原顺序：{{ plan.oldOrder.join(' → ') }}</p><p>新顺序：{{ plan.newOrder.join(' → ') }}</p>
      <label><input v-model="confirmed" type="checkbox" aria-label="确认完整队列" :disabled="busy || !!pending" />确认完整前后顺序</label>
      <button :disabled="busy || !!pending || !confirmed || !can('queue.apply')" @click="apply">应用队列顺序</button>
    </section>
    <section v-if="pending"><p>保留原队列命令，核对原结果后继续。</p><button :disabled="busy || !can('command.read')" @click="recover">查询原队列结果</button><button :disabled="busy || !can('queue.apply')" @click="retry">重试原队列命令</button></section>
    <p v-if="result" role="status">队列顺序已提交，版本 {{ result.queueRef.version }}；未授予执行许可。</p>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { queueApi as api, type QueueContext, type QueueView, type QueuePlan, type QueueReply, type QueueMove } from '@/api/mes/queue'
import type { SchedApply } from '@/api/mes/scheduling'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const props = defineProps<{ initialResourceId?: string; initialSiteId?: string }>()
const permissions = usePermissionStore(), can = (action: string) => permissions.has(`mes-planning-board:${action}`)
const context = ref<QueueContext | null>(null), choiceId = ref(''), view = ref<QueueView | null>(null), plan = ref<QueuePlan | null>(null), result = ref<QueueReply | null>(null)
const history = ref<Awaited<ReturnType<typeof api.history>>>([])
async function readHistory() { await run(async check => { const v = view.value!; const h = await api.history(v.siteId, v.resourceRef.id); check(); history.value = h }) }
const pending = ref<Omit<SchedApply, 'reason'> | null>(null), move = ref(''), anchor = ref(''), position = ref<QueueMove['position']>('BEFORE'), reason = ref(''), confirmed = ref(false), cursor = ref(0), stale = ref(false), busy = ref(false), error = ref(''), notice = ref('')
const key = computed(() => context.value ? `cp6_queue_v1:${context.value.environmentId}:${context.value.tenantId}:${context.value.actorId}` : '')
let epoch = 0, live = true
function invalidate() { plan.value = null; confirmed.value = false }
function clear() { history.value = []; view.value = null; invalidate(); move.value = ''; anchor.value = ''; cursor.value = 0; stale.value = false; result.value = null }
async function run(work: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const version = epoch, identity = identityStamp(); busy.value = true; error.value = ''; notice.value = ''
  const check = () => { requireIdentity(identity); if (!live || version !== epoch || !authenticationCanRead()) throw new Error('当前身份已变化。') }
  try { check(); await work(check) } catch (e: any) {
    if (live && epoch === version) { error.value = e?.response?.data?.message || e?.message || '队列处理失败。'; if (e?.response?.data?.message === 'QUEUE_CAPTURE_EXPIRED') { stale.value = true; invalidate() }; if ([401, 403, 404].includes(e?.response?.status)) clear() }
  } finally { if (live && epoch === version) busy.value = false }
}
async function locked(check: () => void, work: () => Promise<void>) {
  if (!navigator.locks?.request) throw new Error('浏览器不支持多页面提交协调。')
  await navigator.locks.request(key.value, { mode: 'exclusive', ifAvailable: true }, async lock => { check(); if (!lock) throw new Error('另一页面正在处理原队列命令。'); await work() })
}
async function load() { await run(async check => { const value = await api.context(); check(); context.value = value; clear(); choiceId.value = ''; const saved = localStorage.getItem(key.value); pending.value = saved ? JSON.parse(saved) : null }) }
async function read(next: number, fresh = false) { await run(async check => { const c = context.value!.resources.find(x => x.queueId === choiceId.value)!; const value = await api.read(c.siteId, c.resourceRef.id, fresh ? undefined : view.value!.captureId, next); check(); view.value = value; cursor.value = next; stale.value = false; invalidate() }) }
async function prepare() { await run(async check => {
  if (!can('queue.prepare') || !view.value?.completeForWrite || stale.value) throw new Error('需要完整当前队列及准备权限。')
  const v = view.value; const value = await api.prepare({ resourceRef: v.resourceRef, expectedQueueRevision: v.queueRevision, captureId: v.captureId, moveItemId: move.value, position: position.value, beforeItemId: position.value === 'BEFORE' ? anchor.value : null, afterItemId: position.value === 'AFTER' ? anchor.value : null, expectedManifestDigest: v.manifestDigest, reason: reason.value })
  check(); plan.value = value; confirmed.value = false
}) }
function accepted(value: QueueReply) { result.value = value; pending.value = null; localStorage.removeItem(key.value); view.value = null; invalidate() }
async function send(body: SchedApply, check: () => void, first = false) {
  try { const value = await api.apply(body); check(); accepted(value) } catch (e: any) {
    check(); if (first && ['QUEUE_CHANGED', 'ITEM_LOCKED', 'QUEUE_LOCKED_ANCHOR', 'QUEUE_CURRENT_STATE_CHANGED', 'QUEUE_PLAN_EXPIRED', 'QUEUE_ROUTE_PREDECESSOR_VIOLATED'].includes(e?.response?.data?.message)) { pending.value = null; localStorage.removeItem(key.value); stale.value = true; invalidate() }; throw e
  }
}
async function apply() { await run(async check => {
  if (!confirmed.value || !can('queue.apply')) throw new Error('请确认完整队列及提交权限。')
  const p = plan.value!; await locked(check, async () => { if (localStorage.getItem(key.value)) throw new Error('请先核对原队列命令。'); const item = { commandId: crypto.randomUUID(), planId: p.planId, expectedPlanRevision: p.revision, expectedPlanDigest: p.digest }; localStorage.setItem(key.value, JSON.stringify(item)); pending.value = item; await send({ ...item, reason: p.input.reason }, check, true) })
}) }
async function recover() { await run(async check => { if (!can('command.read')) throw new Error('没有原结果读取权限。'); await locked(check, async () => { const value = await api.command(pending.value!.commandId); check(); if (value.status === 'FOUND') accepted(value.queueReply!); else notice.value = '尚未观察到结果，不能据此认定未提交。' }) }) }
async function retry() { await run(async check => { if (!can('queue.apply')) throw new Error('没有提交权限，请查询原结果。'); await locked(check, async () => { const item = pending.value!, p = await api.plan(item.planId); check(); await send({ ...item, reason: p.input.reason }, check) }) }) }
function identityChanged() { epoch++; busy.value = false; clear(); context.value = null; choiceId.value = ''; pending.value = null; reason.value = ''; error.value = ''; notice.value = '' }
onMounted(async () => {
  window.addEventListener(AUTH_EVENT, identityChanged)
  if (!authenticationCanRead() || !can('queue.read')) return
  await load(); if (!live || !context.value || !props.initialResourceId) return
  const selected = context.value.resources.find(c => c.resourceRef.id === props.initialResourceId && c.siteId === props.initialSiteId)
  if (!selected) { error.value = '当前资源队列不在可见范围'; return }
  choiceId.value = selected.queueId; await read(0, true)
})
onBeforeUnmount(() => { live = false; epoch++; window.removeEventListener(AUTH_EVENT, identityChanged) })
</script>
<style scoped>
.queue-panel { padding: 16px; margin-bottom: 16px; border: 1px solid var(--el-border-color, #ddd); border-radius: 6px; } fieldset { border: 0; padding: 0; } label { display: inline-flex; gap: 6px; margin: 6px 12px 6px 0; } button { margin: 6px; } table { width: 100%; border-collapse: collapse; } td, th { padding: 8px; border-bottom: 1px solid #ddd; text-align: left; } [role=alert] { color: #a62626; }
</style>
