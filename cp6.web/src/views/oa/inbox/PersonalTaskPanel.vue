<script setup lang="ts">
import { personalFormError } from '../catalog/personalFormError'
import { computed, ref, watch, onBeforeUnmount } from 'vue'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { ACTING_AS_EVENT, getActingAs } from '@/stores/oaActingAs'
import { personalFormApi as api, type PersonalTask } from '@/api/oa/personalForm'
import type { FormFileReadContext } from '@/api/oa/formFiles'
import { schemaFields, formObject, readFormJson, writeFormJson, type FormObject } from '../catalog/personalFormJson'
import PersonalFormFields from '../catalog/PersonalFormFields.vue'
const props = defineProps<{ taskId: string }>()
const emit = defineEmits<{ completed: []; privateClear: [] }>()
interface Intent { identity: string; task: string; command: string; behalf: string | null; operation?: 'SFS_TASK_DECISION' }
const cache = 'cp6-personal-task-pending'
const context = ref<PersonalTask>(), result = ref<{ commandId: string; state: string }>(), comment = ref(''), pending = ref<Intent>()
const data = ref<FormObject>({}), busy = ref(false), error = ref(''), message = ref(''), storageBlocked = ref(false), valid = ref(true), conflict = ref(false)
let body: string | undefined, live = true, generation = 0, stamp = identityStamp(), behalf = getActingAs()?.userId ?? null
const fields = computed(() => context.value ? schemaFields(context.value.schemaJson) : [])
const locked = computed(() => busy.value || !!pending.value || !!result.value || storageBlocked.value)
const commentLength = computed(() => [...comment.value].length)
const commentTooLong = computed(() => commentLength.value > 1000)
const fileContext = computed<FormFileReadContext | undefined>(() => context.value ? { scope: { kind: 'TASK', key: null, id: context.value.task.taskId,
  formVersionId: context.value.formVersionId, expectedVersion: context.value.task.taskRowVersion, expectedDataVersion: context.value.formDataVersion } } : undefined)
try {
  const raw = sessionStorage.getItem(cache)
  if (raw) {
    const value = JSON.parse(raw) as Intent
    if (value.identity !== stamp) sessionStorage.removeItem(cache)
    else if (typeof value.task !== 'string' || typeof value.command !== 'string' || value.behalf !== null && typeof value.behalf !== 'string'
      || value.operation !== undefined && value.operation !== 'SFS_TASK_DECISION') throw new Error()
    else pending.value = value
  }
} catch { storageBlocked.value = true; error.value = '无法读取办理恢复标识，已停止新操作。' }
function clearPrivate() { context.value = undefined; data.value = {}; result.value = undefined; comment.value = ''; conflict.value = false }
function changed() {
  const identity = identityStamp(), principal = getActingAs()?.userId ?? null
  if (stamp === identity && behalf === principal) return
  if (stamp !== identity) pending.value = undefined
  stamp = identity; behalf = principal; generation++; clearPrivate(); body = undefined; busy.value = false
  error.value = '身份或代理人已改变，请重新读取待办；未确认的原命令仍可查询。'
}
window.addEventListener(AUTH_EVENT, changed); window.addEventListener(ACTING_AS_EVENT, changed); window.addEventListener('storage', changed)
async function load() {
  if (busy.value) return
  const identity = identityStamp(), principal = getActingAs()?.userId ?? null, current = ++generation; busy.value = true; error.value = ''
  try {
    const value = await api.task(props.taskId); requireIdentity(identity)
    if (live && current === generation && principal === (getActingAs()?.userId ?? null)) {
      context.value = value; data.value = formObject(readFormJson(value.dataJson)); result.value = undefined; conflict.value = false; valid.value = true
    }
  } catch (e: any) {
    if (live && current === generation && identity === identityStamp()) { clearPrivate(); if ([401, 403, 404].includes(e?.response?.status)) emit('privateClear'); error.value = personalFormError(e) }
  } finally { if (live && current === generation) busy.value = false }
}
async function run(read: boolean, wasUnknown: boolean, close = false) {
  const intent = pending.value
  if (!intent || busy.value || storageBlocked.value) return
  const current = generation, principal = getActingAs()?.userId ?? null; busy.value = true; error.value = ''; message.value = ''
  const check = () => { requireIdentity(intent.identity); if (!live || generation !== current || principal !== (getActingAs()?.userId ?? null)) throw new Error('STALE') }
  try {
    if (!read && !close && (!body || !intent.operation || intent.behalf !== principal)) throw new Error('原填写内容或代理身份已改变，请先查询原结果。')
    const operation = intent.operation ?? 'FORM_TASK_DECISION'
    const value = close ? await api.close(operation, intent.task, intent.command)
      : read ? intent.operation ? await api.original(operation, intent.task, intent.command) : await api.taskOriginal(intent.task, intent.command)
        : await api.decideSfs(intent.task, body!)
    check()
    if (value === null || 'outcome' in value && value.outcome === 'NOT_OBSERVED') { message.value = '尚未观察到原办理结果，请继续查询；此状态不证明失败。'; return }
    const state = 'outcome' in value ? value.outcome === 'CLOSED' ? 'CLOSED' : 'COMPLETED' : value.state
    sessionStorage.removeItem(cache); pending.value = undefined; body = undefined; clearPrivate(); result.value = { commandId: value.commandId, state }; emit('completed')
  } catch (e: any) {
    try {
      check(); const status = e?.response?.status
      if ([401, 403, 404].includes(status)) { clearPrivate(); body = undefined; emit('privateClear') }
      if (!read && !wasUnknown && status >= 400 && status < 500 && status !== 408 && status !== 429) {
        sessionStorage.removeItem(cache); pending.value = undefined; body = undefined
        if (status === 409) conflict.value = true
      }
      error.value = personalFormError(e)
    } catch { /* Obsolete identity or principal: retain the original operation identifier. */ }
  } finally { if (live && generation === current) busy.value = false }
}
async function decide(decision: string) {
  const c = context.value
  if (!c || locked.value || conflict.value || !valid.value || commentTooLong.value || !c.allowedDecisions.includes(decision)) return
  const patch: FormObject = {}, before = formObject(readFormJson(c.dataJson))
  for (const [name, value] of Object.entries(data.value))
    if (value !== undefined && c.fieldMask[name] === 'edit' && (!Object.hasOwn(before, name) || writeFormJson(value) !== writeFormJson(before[name]!))) patch[name] = value
  const intent: Intent = { identity: identityStamp(), task: c.task.taskId, command: crypto.randomUUID(), behalf: getActingAs()?.userId ?? null, operation: 'SFS_TASK_DECISION' }
  const raw = writeFormJson({ commandId: intent.command, expectedVersion: c.task.taskRowVersion, body: { kind: c.kind, taskId: c.task.taskId,
    decision: decision.toUpperCase(), comment: comment.value, dataPatch: patch, expectedTaskVersion: c.task.taskRowVersion,
    expectedInstanceVersion: c.task.instanceRowVersion, expectedFormDataVersion: c.formDataVersion } })
  try { sessionStorage.setItem(cache, JSON.stringify(intent)) } catch { storageBlocked.value = true; error.value = '无法保存恢复标识，未发送办理请求。'; return }
  body = raw; pending.value = intent; await run(false, false)
}
watch(() => props.taskId, () => { generation++; busy.value = false; clearPrivate(); void load() }, { immediate: true })
onBeforeUnmount(() => { live = false; generation++; window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener(ACTING_AS_EVENT, changed); window.removeEventListener('storage', changed) })
</script>
<template>
  <section class="task-panel">
    <h3>表单待办办理</h3>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <el-alert v-if="message" :title="message" type="info" :closable="false" />
    <div v-if="pending"><p>原办理结果尚待确认：{{ pending.command }}</p><el-button :loading="busy" @click="run(true, true)">查询原结果</el-button><el-button v-if="body" :disabled="busy" @click="run(false, true)">重试原办理</el-button><el-button :disabled="busy" @click="run(false, true, true)">关闭未执行命令 / 取回已执行结果</el-button></div>
    <p v-if="conflict" role="alert">待办或表单已经变化，当前填写内容已保留。重新读取会替换当前内容，请核对后再办理。</p>
    <el-button v-if="!result" :disabled="locked" @click="load">重新读取待办</el-button>
    <template v-if="context">
      <h4>{{ context.name }}</h4><p>表单编号：{{ context.formDataId }}</p>
      <PersonalFormFields :key="context.task.taskId + ':' + context.formDataVersion" v-model="data" :fields="fields" :mask="context.fieldMask"
        :file-context="fileContext" :disabled="locked || conflict || !context.allowedDecisions.length" :allow-unset="false" @validity="valid = $event" @forbidden="clearPrivate(); emit('privateClear')" />
      <el-input v-model="comment" type="textarea" placeholder="办理意见" :disabled="locked || conflict" />
      <p>{{ commentLength }} / 1000 字符</p><p v-if="commentTooLong" role="alert">办理意见最多 1000 个字符，请修改后再提交；已保留全部填写内容。</p>
      <div class="actions"><el-button type="primary" :disabled="locked || conflict || !valid || commentTooLong || !context.allowedDecisions.includes('approve')" @click="decide('approve')">同意</el-button><el-button type="danger" :disabled="locked || conflict || !valid || commentTooLong || !context.allowedDecisions.includes('reject')" @click="decide('reject')">拒绝</el-button></div>
    </template>
    <p v-if="result" role="status">{{ result.state === 'CLOSED' ? '原命令已关闭，没有产生办理效果。' : '已办理。' }}原命令：{{ result.commandId }}</p>
    <el-button v-if="result?.state === 'CLOSED'" @click="load">重新读取待办</el-button>
  </section>
</template>
<style scoped>.task-panel { padding: 16px; border: 1px solid var(--el-border-color); margin-bottom: 16px; }.actions { margin-top: 12px; display: flex; gap: 12px; }</style>
