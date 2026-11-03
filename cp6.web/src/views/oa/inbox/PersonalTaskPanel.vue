<script setup lang="ts">
import { personalFormError } from '../catalog/personalFormError'
import { computed, ref, watch, onBeforeUnmount } from 'vue'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { getActingAs } from '@/stores/oaActingAs'
import { personalFormApi as api, type PersonalTask, type PersonalTaskResult } from '@/api/oa/personalForm'
import { schemaFields, formObject, readFormJson } from '../catalog/personalFormJson'
import PersonalFormFields from '../catalog/PersonalFormFields.vue'
const props = defineProps<{ taskId: string }>()
const emit = defineEmits<{ completed: []; privateClear: [] }>()
interface Intent { identity: string; task: string; command: string; behalf: string | null }
interface Decision { commandId: string; decision: string; comment: string; taskRowVersion: string; instanceRowVersion: string }
const cache = 'cp6-personal-task-pending'
const context = ref<PersonalTask>(), result = ref<PersonalTaskResult>(), comment = ref(''), pending = ref<Intent>(), busy = ref(false), error = ref(''), message = ref(''), storageBlocked = ref(false)
let body: Decision | undefined, live = true, generation = 0, stamp = identityStamp()
const fields = computed(() => context.value ? schemaFields(context.value.schemaJson) : [])
const data = computed(() => context.value ? formObject(readFormJson(context.value.dataJson)) : {})
const locked = computed(() => busy.value || !!pending.value || !!result.value || storageBlocked.value)
try {
  const raw = sessionStorage.getItem(cache)
  if (raw) {
    const value = JSON.parse(raw) as Intent
    if (value.identity !== stamp) sessionStorage.removeItem(cache)
    else if (typeof value.task !== 'string' || typeof value.command !== 'string' || value.behalf !== null && typeof value.behalf !== 'string') throw new Error()
    else pending.value = value
  }
} catch { storageBlocked.value = true; error.value = '无法读取办理恢复标识，已停止新操作。' }
function clearPrivate() { context.value = undefined; result.value = undefined; comment.value = '' }
function changed() {
  if (stamp === identityStamp()) return
  stamp = identityStamp(); generation++; clearPrivate(); body = undefined; pending.value = undefined; busy.value = false
  error.value = '身份已改变，请重新读取待办。'
}
window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed)
async function load() {
  if (busy.value) return
  const identity = identityStamp(), current = ++generation; busy.value = true; error.value = ''
  try { const value = await api.task(props.taskId); requireIdentity(identity); if (live && current === generation) { context.value = value; result.value = undefined } }
  catch (e: any) { if (live && current === generation && identity === identityStamp()) { clearPrivate(); if ([401, 403, 404].includes(e?.response?.status)) emit('privateClear'); error.value = personalFormError(e) } }
  finally { if (live && current === generation) busy.value = false }
}
async function run(read: boolean, wasUnknown: boolean, close = false) {
  const intent = pending.value
  if (!intent || busy.value || storageBlocked.value) return
  const current = generation; busy.value = true; error.value = ''; message.value = ''
  const check = () => { requireIdentity(intent.identity); if (!live || generation !== current) throw new Error('STALE') }
  try {
    if (!read && !close && (!body || intent.behalf !== (getActingAs()?.userId ?? null))) throw new Error('原填写内容或代理身份已改变，请先查询原结果。')
    if (close) await api.close('FORM_TASK_DECISION', intent.task, intent.command)
    check()
    const value = read || close ? await api.taskOriginal(intent.task, intent.command) : await api.decide(intent.task, body, body!.taskRowVersion)
    check()
    if (value === null) { message.value = '尚未观察到原办理结果，请继续查询；此状态不证明失败。'; return }
    sessionStorage.removeItem(cache); pending.value = undefined; body = undefined; clearPrivate(); result.value = value; emit('completed')
  } catch (e: any) {
    try {
      check(); const status = e?.response?.status
      if ([401, 403, 404].includes(status)) { clearPrivate(); body = undefined; emit('privateClear') }
      if (!read && !wasUnknown && status >= 400 && status < 500 && status !== 408 && status !== 429) { sessionStorage.removeItem(cache); pending.value = undefined; body = undefined }
      error.value = personalFormError(e)
    } catch { /* obsolete identity */ }
  } finally { if (live && generation === current) busy.value = false }
}
async function decide(decision: string) {
  if (!context.value || locked.value || !context.value.allowedDecisions?.includes(decision)) return
  if (decision === 'reject' && !comment.value.trim()) { error.value = '拒绝时请填写意见。'; return }
  const intent: Intent = { identity: identityStamp(), task: props.taskId, command: crypto.randomUUID(), behalf: getActingAs()?.userId ?? null }
  try { sessionStorage.setItem(cache, JSON.stringify(intent)) } catch { storageBlocked.value = true; error.value = '无法保存恢复标识，未发送办理请求。'; return }
  body = { commandId: intent.command, decision, comment: comment.value, taskRowVersion: context.value.task.taskRowVersion, instanceRowVersion: context.value.task.instanceRowVersion }
  pending.value = intent; await run(false, false)
}
watch(() => props.taskId, () => { generation++; busy.value = false; clearPrivate(); void load() }, { immediate: true })
onBeforeUnmount(() => { live = false; generation++; window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>
<template>
  <section class="task-panel">
    <h3>表单待办办理</h3>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <el-alert v-if="message" :title="message" type="info" :closable="false" />
    <div v-if="pending"><p>原办理结果尚待确认：{{ pending.command }}</p><el-button :loading="busy" @click="run(true, true)">查询原结果</el-button><el-button v-if="body" :disabled="busy" @click="run(false, true)">重试原办理</el-button><el-button :disabled="busy" @click="run(false, true, true)">关闭未执行命令 / 取回已执行结果</el-button></div>
    <el-button v-if="!result" :disabled="locked" @click="load">重新读取待办</el-button>
    <template v-if="context">
      <h4>{{ context.name }}</h4><p>表单编号：{{ context.formDataId }}</p>
      <PersonalFormFields :fields="fields" :model-value="data" :mask="context.fieldMask" :file-context="{ instanceId: context.task.instanceId }" disabled @forbidden="clearPrivate(); emit('privateClear')" />
      <el-input v-model="comment" type="textarea" maxlength="500" placeholder="办理意见" :disabled="locked" />
      <div class="actions"><el-button type="primary" :disabled="locked || !context.allowedDecisions?.includes('approve')" @click="decide('approve')">同意</el-button><el-button type="danger" :disabled="locked || !context.allowedDecisions?.includes('reject')" @click="decide('reject')">拒绝</el-button></div>
    </template>
    <p v-if="result" role="status">{{ result.state === 'CLOSED' ? '原命令已关闭，没有产生办理效果。' : '已办理。' }}原命令：{{ result.commandId }}</p>
    <el-button v-if="result?.state === 'CLOSED'" @click="load">重新读取待办</el-button>
  </section>
</template>
<style scoped>.task-panel { padding: 16px; border: 1px solid var(--el-border-color); margin-bottom: 16px; }.actions { margin-top: 12px; display: flex; gap: 12px; }</style>
