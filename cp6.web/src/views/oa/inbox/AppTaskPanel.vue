<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { ACTING_AS_EVENT, getActingAs } from '@/stores/oaActingAs'
import { taskActionApi as api, type TaskActionContext, type AppTaskResult } from '@/api/oa/taskAction'
import { personalFormError } from '../catalog/personalFormError'
const props = defineProps<{ taskId: string }>()
const emit = defineEmits<{ completed: []; privateClear: [] }>()
interface Intent { identity: string; task: string; command: string; behalf: string | null }
const cache = 'cp6-app-task-pending', context = ref<TaskActionContext>(), comment = ref(''), pending = ref<Intent>()
const busy = ref(false), error = ref(''), message = ref(''), conflict = ref(false), storageBlocked = ref(false), result = ref<AppTaskResult>()
let body: string | undefined, generation = 0, live = true, stamp = identityStamp(), principal = getActingAs()?.userId ?? null
const locked = computed(() => busy.value || !!pending.value || !!result.value || storageBlocked.value)
const commentLength = computed(() => [...comment.value].length)
try {
  const raw = sessionStorage.getItem(cache)
  if (raw) {
    const value = JSON.parse(raw) as Intent
    if (value.identity !== stamp) sessionStorage.removeItem(cache)
    else if (typeof value.task !== 'string' || typeof value.command !== 'string' || value.behalf !== null && typeof value.behalf !== 'string') throw new Error()
    else pending.value = value
  }
} catch { storageBlocked.value = true; error.value = '无法读取原办理标识，已停止新操作。' }
function clearPrivate() { context.value = undefined; comment.value = ''; result.value = undefined; conflict.value = false }
function changed() {
  const identity = identityStamp(), acting = getActingAs()?.userId ?? null
  if (identity === stamp && acting === principal) return
  if (identity !== stamp) pending.value = undefined
  stamp = identity; principal = acting; generation++; busy.value = false; body = undefined; clearPrivate()
  error.value = '身份或代理人已改变，请重新读取；未确认的原办理仍可查询。'
}
window.addEventListener(AUTH_EVENT, changed); window.addEventListener(ACTING_AS_EVENT, changed); window.addEventListener('storage', changed)
async function load() {
  if (busy.value) return
  const identity = identityStamp(), acting = getActingAs()?.userId ?? null, current = ++generation; busy.value = true; error.value = ''
  try {
    const value = await api.context(props.taskId); requireIdentity(identity)
    if (live && current === generation && acting === (getActingAs()?.userId ?? null)) {
      if (value.contextKind !== 'APP') throw new Error('当前任务不是业务审批，请从任务详情重新进入。')
      context.value = value; conflict.value = false; result.value = undefined
    }
  } catch (e: any) {
    if (live && current === generation && identity === identityStamp()) { clearPrivate(); if ([401, 403, 404].includes(e?.response?.status)) emit('privateClear'); error.value = personalFormError(e) }
  } finally { if (live && current === generation) busy.value = false }
}
async function run(read: boolean, unknown: boolean, close = false) {
  const intent = pending.value
  if (!intent || busy.value || storageBlocked.value) return
  const current = generation, acting = getActingAs()?.userId ?? null; busy.value = true; error.value = ''; message.value = ''
  const check = () => { requireIdentity(intent.identity); if (!live || generation !== current || acting !== (getActingAs()?.userId ?? null)) throw new Error('STALE') }
  try {
    if (!read && !close && (!body || intent.behalf !== acting)) throw new Error('填写内容或代理身份已改变，请先查询原办理结果。')
    const resolution = close ? await api.closeApp(intent.task, intent.command) : read ? await api.originalApp(intent.task, intent.command)
      : { commandId: intent.command, state: 'COMPLETED', result: await api.decideApp(intent.task, body!) }
    check()
    if (resolution.state === 'NOT_OBSERVED') { message.value = '尚未观察到原结果，请继续查询；这不证明办理失败。'; return }
    sessionStorage.removeItem(cache); pending.value = undefined; body = undefined; clearPrivate()
    if (resolution.state === 'COMPLETED') { result.value = resolution.result!; emit('completed') }
    else message.value = '原命令已关闭，没有产生办理效果；可重新读取任务。'
  } catch (e: any) {
    try {
      check(); const status = e?.response?.status
      if ([401, 403, 404].includes(status)) { clearPrivate(); body = undefined; emit('privateClear') }
      if (!read && !unknown && status >= 400 && status < 500 && ![408, 429].includes(status)) {
        sessionStorage.removeItem(cache); pending.value = undefined; body = undefined
        if ([409, 412, 428].includes(status)) conflict.value = true
      }
      error.value = personalFormError(e)
    } catch { /* Keep the original identifier; a late response cannot populate a new principal's page. */ }
  } finally { if (live && current === generation) busy.value = false }
}
function canDecide(decision: string) {
  return !!context.value && !locked.value && !conflict.value && context.value.allowedActions.includes(decision)
    && commentLength.value <= context.value.commentLimit && (!(context.value.commentRequired || decision === 'REJECT') || !!comment.value.trim())
}
async function decide(decision: string) {
  if (!canDecide(decision)) return
  const c = context.value!, intent: Intent = { identity: identityStamp(), task: c.taskId, command: crypto.randomUUID(), behalf: getActingAs()?.userId ?? null }
  const raw = JSON.stringify({ commandId: intent.command, expectedVersion: c.taskRowVersion, body: { kind: c.contextKind, taskId: c.taskId,
    decision, comment: comment.value, expectedTaskVersion: c.taskRowVersion, expectedInstanceVersion: c.instanceRowVersion } })
  try { sessionStorage.setItem(cache, JSON.stringify(intent)) } catch { storageBlocked.value = true; error.value = '无法保存恢复标识，未发送办理请求。'; return }
  body = raw; pending.value = intent; await run(false, false)
}
watch(() => props.taskId, () => { generation++; busy.value = false; clearPrivate(); void load() }, { immediate: true })
onBeforeUnmount(() => { live = false; generation++; window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener(ACTING_AS_EVENT, changed); window.removeEventListener('storage', changed) })
</script>
<template>
  <section class="app-task-panel">
    <h3>业务审批办理</h3>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <p v-if="message" role="status">{{ message }}</p>
    <div v-if="pending"><p>原办理结果尚待确认：{{ pending.command }}</p><el-button :loading="busy" @click="run(true, true)">查询原结果</el-button><el-button v-if="body" :disabled="busy" @click="run(false, true)">重试原办理</el-button><el-button :disabled="busy" @click="run(false, true, true)">关闭未执行命令 / 取回已执行结果</el-button></div>
    <p v-if="conflict" role="alert">任务或流程版本已经变化，意见已保留。请重新读取并核对后再办理。</p>
    <el-button v-if="!result" :disabled="locked" @click="load">重新读取待办</el-button>
    <template v-if="context">
      <p v-if="context.approvalSubjectRef">审批对象：{{ context.approvalSubjectRef.id }} · 固定业务版本 {{ context.approvalSubjectRef.version }}</p>
      <p>当前处理人 {{ context.currentPrincipalId }} · 实际办理人 {{ context.actualHandlerId }}<span v-if="context.createdPrincipalId !== context.currentPrincipalId"> · 最初处理人 {{ context.createdPrincipalId }}</span></p>
      <p>请核对详情中的业务内容和受控原件。审批结果与业务处理结果分别显示。</p>
      <el-input v-model="comment" type="textarea" placeholder="办理意见" :disabled="locked || conflict" />
      <p>{{ commentLength }} / {{ context.commentLimit }} 字符<span v-if="context.commentRequired">，办理意见必填</span><span v-else>，拒绝时必填</span></p>
      <el-button type="primary" :disabled="!canDecide('APPROVE')" @click="decide('APPROVE')">同意</el-button>
      <el-button type="danger" :disabled="!canDecide('REJECT')" @click="decide('REJECT')">拒绝</el-button>
    </template>
    <p v-if="result" role="status">当前任务已办理。{{ result.result.task.instanceStatus === 0 ? '流程仍在审批中。' : '流程状态已更新，请查看详情。' }}</p>
  </section>
</template>
<style scoped>.app-task-panel { padding: 16px; border: 1px solid var(--el-border-color); margin-bottom: 16px; }</style>
