<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import { getActingAs, clearActingAs } from '@/stores/oaActingAs'
import { taskDelegationApi as api, type TaskAction, type TaskGrant, type TaskGrantInput, type TaskGrantIntent, type TaskGrantList, type TaskGrantResult } from '@/api/oa/taskDelegation'

const list = ref<TaskGrantList>(), history = ref<TaskGrant[]>([]), edited = ref<TaskGrant>(), busy = ref(false), writing = ref(false)
const error = ref(''), message = ref(''), pending = ref<TaskGrantIntent>(), storageBlocked = ref(false)
const search = ref(''), choices = ref<{ id: string; name: string }[]>([]), delegate = ref(''), from = ref(''), to = ref(''), flows = ref(''), types = ref(''), remark = ref('')
const selectedActions = ref<TaskAction[]>(['TASK_READ']), reason = ref(''), actingAs = ref(getActingAs())
const actions: { value: TaskAction; label: string }[] = [{ value: 'TASK_READ', label: '查看任务' }, { value: 'TASK_APPROVE', label: '同意' }, { value: 'TASK_REJECT', label: '拒绝' }, { value: 'TASK_SENDBACK', label: '退回' }, { value: 'TASK_TRANSFER', label: '转交' }]
const cache = 'cp6-task-delegation-pending', stamp = () => JSON.stringify([identityStamp(), getActingAs()?.userId ?? null])
let live = true, epoch = 0, generation = 0, searchGeneration = 0, historyGeneration = 0, raw: string | undefined, lastIdentity = stamp()
function remember() { if (pending.value) sessionStorage.setItem(cache, JSON.stringify(pending.value)); else sessionStorage.removeItem(cache) }
try {
  const value = sessionStorage.getItem(cache)
  if (value) {
    const intent = JSON.parse(value) as TaskGrantIntent
    if (intent.identity === stamp() && ['CREATE', 'REVISE', 'REVOKE'].includes(intent.operation) && /^[A-Za-z0-9_.-]{1,100}$/.test(intent.command)
      && (intent.resource === 'new' && intent.operation === 'CREATE' || /^[0-9a-f-]{36}$/i.test(intent.resource) && intent.operation !== 'CREATE')) pending.value = intent
    else sessionStorage.removeItem(cache)
  }
} catch { storageBlocked.value = true; error.value = '无法读取未确认的授权命令，暂时不能发起新变更。' }
function current(captured: number, identity: string) { if (!live || epoch !== captured || identity !== stamp()) throw new Error('STALE') }
function clearPrivate() { list.value = undefined; history.value = []; edited.value = undefined; choices.value = []; delegate.value = ''; from.value = to.value = flows.value = types.value = remark.value = reason.value = search.value = ''; selectedActions.value = ['TASK_READ'] }
function identityChanged() {
  if (lastIdentity === stamp()) return
  lastIdentity = stamp()
  epoch++; generation++; searchGeneration++; historyGeneration++; clearPrivate(); raw = undefined; pending.value = undefined; busy.value = writing.value = false; actingAs.value = getActingAs()
  message.value = ''; error.value = '身份已改变，请重新读取。'
  try { remember(); storageBlocked.value = false } catch { storageBlocked.value = true }
}
function failed(e: any) {
  if ([401, 403, 404].includes(e?.response?.status)) { clearPrivate(); raw = undefined }
  const code = e?.response?.data?.code
  error.value = code === 'GRANT_REDELEGATION_FORBIDDEN' ? '不能代他人授出委派，也不能形成范围重叠的转授链。'
    : code === 'GRANT_VERSION_CONFLICT' ? '授权已改变，请重新读取后再决定。'
      : code === 'GRANT_SCOPE_KEY_UNREGISTERED' ? '流程或业务类型标识不存在，请核对准确标识。'
        : [401, 403, 404].includes(e?.response?.status) ? '当前身份无权执行或读取，已清除原内容。' : '操作未确认，请查询原结果；尚未观察到结果不代表未执行。'
}
async function load() {
  const captured = epoch, identity = stamp(), request = ++generation; list.value = undefined; history.value = []; busy.value = true; error.value = ''
  try { const value = await api.list(); current(captured, identity); if (request === generation) list.value = value }
  catch (e) { try { current(captured, identity); if (request === generation) failed(e) } catch { /* obsolete identity */ } }
  finally { if (live && captured === epoch && request === generation) busy.value = false }
}
async function findUsers() {
  const captured = epoch, identity = stamp(), request = ++searchGeneration; choices.value = []
  if (!search.value.trim()) return
  try { const value = await api.targets(search.value); current(captured, identity); if (request === searchGeneration) choices.value = value }
  catch (e) { try { current(captured, identity); if (request === searchGeneration) failed(e) } catch { /* obsolete selection */ } }
}
const utcInput = (value: string) => new Date(value).toISOString().slice(0, -1)
function edit(value?: TaskGrant) {
  if (pending.value || writing.value) return
  edited.value = value; delegate.value = value?.delegateId ?? ''; choices.value = value ? [{ id: value.delegateId, name: value.delegateId }] : []
  from.value = value ? utcInput(value.fromUtc) : ''; to.value = value ? utcInput(value.toUtc) : ''
  flows.value = value?.scope.flowKeys?.join(', ') ?? ''; types.value = value?.scope.bizTypes?.join(', ') ?? ''; selectedActions.value = value ? [...value.scope.actionKinds] : ['TASK_READ']; remark.value = value?.reason ?? ''; reason.value = ''; error.value = ''
}
async function showHistory(value: TaskGrant) {
  const captured = epoch, identity = stamp(), request = ++historyGeneration; history.value = []
  try { const rows = await api.history(value.grantId); current(captured, identity); if (request === historyGeneration) history.value = rows }
  catch (e) { try { current(captured, identity); if (request === historyGeneration) failed(e) } catch { /* obsolete selection */ } }
}
function accept(value: TaskGrantResult, intent: TaskGrantIntent) {
  if (value.commandId !== intent.command) throw new Error('RESULT_MISMATCH')
  if (value.state === 'NOT_OBSERVED') { message.value = '尚未观察到原结果；可用原命令重试，或显式关闭该命令。'; return }
  if (value.state !== 'SUCCEEDED' && value.state !== 'CLOSED' || value.state === 'SUCCEEDED' && (!value.result || intent.operation !== 'CREATE' && value.result.grantId !== intent.resource)) throw new Error('RESULT_MISMATCH')
  pending.value = undefined; raw = undefined; remember(); list.value = undefined; history.value = []; edited.value = undefined
  message.value = value.state === 'CLOSED' ? '该命令已关闭，没有产生授权变更。' : '授权变更已确认，请刷新查看当前版本。'
}
async function run(mode: 'send' | 'original' | 'close', first = false) {
  if (!pending.value || writing.value || storageBlocked.value || mode === 'send' && !raw) return
  const intent = { ...pending.value }, captured = epoch; writing.value = true; error.value = ''; message.value = ''
  try {
    current(captured, intent.identity)
    const result = await (mode === 'send' ? api.write(intent, raw!) : mode === 'original' ? api.original(intent) : api.close(intent))
    current(captured, intent.identity); accept(result, intent)
  } catch (e: any) {
    try {
      current(captured, intent.identity); failed(e)
      if (first && [400, 401, 403, 404, 409, 412, 422].includes(e?.response?.status)) { pending.value = undefined; raw = undefined; remember() }
    } catch { /* preserve unknown identity only in its original scope */ }
  } finally { if (live && captured === epoch) writing.value = false }
}
async function submit(revoke = false) {
  if (pending.value || writing.value || storageBlocked.value || actingAs.value) return
  let body: TaskGrantInput | { reason: string }
  try {
    if (revoke) { if (!edited.value || !reason.value.trim()) throw new Error(); body = { reason: reason.value } }
    else {
      const keys = (value: string) => value.split(',').map(x => x.trim()).filter(Boolean)
      if (!delegate.value || !from.value || !to.value || !flows.value.trim() && !types.value.trim()) throw new Error()
      const instant = (value: string, previous?: string) => previous && value === utcInput(previous) ? previous : new Date(value + 'Z').toISOString()
      const validFromUtc = instant(from.value, edited.value?.fromUtc), validToUtc = instant(to.value, edited.value?.toUtc)
      // The Owner compares exact instants; JavaScript's millisecond clock must not truncate
      // an unchanged higher-precision grant into an apparently empty interval.
      if (Date.parse(validToUtc) < Date.parse(validFromUtc)) throw new Error()
      body = { delegateId: delegate.value, validFromUtc, validToUtc, scope: { actionKinds: [...new Set<TaskAction>(['TASK_READ', ...selectedActions.value])], ...(flows.value.trim() ? { flowKeys: keys(flows.value) } : {}), ...(types.value.trim() ? { bizTypes: keys(types.value) } : {}) }, remark: remark.value }
    }
  } catch { error.value = '请选择代理人、有效 UTC 时间和至少一项范围；撤销必须填写原因。'; return }
  const intent: TaskGrantIntent = { identity: stamp(), operation: revoke ? 'REVOKE' : edited.value ? 'REVISE' : 'CREATE', resource: edited.value?.grantId ?? 'new', command: crypto.randomUUID() }
  raw = JSON.stringify({ commandId: intent.command, expectedVersion: edited.value?.version ?? null, body }); pending.value = intent
  try { remember() } catch { pending.value = undefined; raw = undefined; storageBlocked.value = true; error.value = '无法保存恢复标识，未发送变更。'; return }
  await run('send', true)
}
function self() { clearActingAs(); identityChanged(); void load() }
onMounted(() => { window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged); void load() })
onBeforeUnmount(() => { live = false; epoch++; window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged) })
</script>
<template>
  <section class="delegations" aria-label="当前任务委派">
    <h3>当前任务委派</h3><p>授权用于指定流程或业务类型的任务操作，不授予业务撤回、应用或私人通知访问权。旧版委派保留历史用途。</p>
    <p v-if="actingAs">授权管理须由本人执行。<button :disabled="!!pending || writing" @click="self">切回本人</button></p>
    <button :disabled="busy || writing" @click="load">刷新授权</button><p v-if="error" role="alert">{{ error }}</p><p v-if="message" role="status">{{ message }}</p>
    <aside v-if="pending"><p>未确认命令：{{ pending.command }}</p><button :disabled="writing" @click="run('original')">查询原结果</button><button v-if="raw" :disabled="writing" @click="run('send')">原命令重试</button><button :disabled="writing" @click="run('close')">关闭原命令</button></aside>
    <template v-if="list">
      <h4>我授出的委派</h4><p v-if="!list.granted.length">当前没有授出的新版委派。</p>
      <ul><li v-for="g in list.granted" :key="g.grantId">{{ g.delegateId }} · 版本 {{ g.revision }} · {{ g.enabled ? '已启用（仍须在有效期内）' : '已撤销' }}<p>{{ g.fromUtc }} 至 {{ g.toUtc }}（不含结束时点）</p><p>动作 {{ g.scope.actionKinds.join(', ') }}；流程 {{ g.scope.flowKeys?.join(', ') || '不另限制' }}；业务类型 {{ g.scope.bizTypes?.join(', ') || '不另限制' }}</p><button :disabled="!!pending || writing" @click="edit(g)">修订 / 撤销</button><button @click="showHistory(g)">历史版本</button></li></ul>
      <h4>我收到的委派</h4><ul><li v-for="g in list.received" :key="g.grantId">原职责人 {{ g.grantorId }} · 版本 {{ g.revision }} · {{ g.fromUtc }} 至 {{ g.toUtc }} · {{ g.enabled ? '已启用' : '已撤销' }}<button @click="showHistory(g)">历史版本</button></li></ul>
      <button :disabled="!!pending || writing" @click="edit()">新建委派</button>
      <fieldset :disabled="!!pending || writing || storageBlocked || !!actingAs">
        <legend>{{ edited ? '修订当前委派' : '新建本人委派' }}</legend>
        <label>搜索同租户代理人 <input v-model="search" maxlength="100" @keyup.enter.prevent="findUsers" /></label><button @click="findUsers">搜索人员</button>
        <label>代理人 <select v-model="delegate"><option value="">请选择</option><option v-for="u in choices" :key="u.id" :value="u.id">{{ u.name }}</option></select></label>
        <label>开始时间（UTC，含）<input v-model="from" type="datetime-local" step="0.001" /></label><label>结束时间（UTC，不含）<input v-model="to" type="datetime-local" step="0.001" /></label>
        <div><label v-for="a in actions" :key="a.value"><input v-model="selectedActions" type="checkbox" :value="a.value" :disabled="a.value === 'TASK_READ'" />{{ a.label }}</label></div>
        <label>流程标识，逗号分隔 <input v-model="flows" /></label><label>业务类型，逗号分隔 <input v-model="types" /></label><p>至少填写一项范围；两项都填时必须同时匹配，标识区分大小写。</p>
        <label>说明 <textarea v-model="remark" maxlength="1000" /></label><button @click="submit(false)">{{ edited ? '保存新修订' : '创建授权' }}</button>
        <template v-if="edited"><label>撤销原因 <textarea v-model="reason" maxlength="1000" /></label><button @click="submit(true)">撤销当前授权</button></template>
      </fieldset>
    </template>
    <section v-if="history.length" aria-label="授权历史"><h4>历史版本</h4><ul><li v-for="g in history" :key="g.version">版本 {{ g.revision }} · {{ g.enabled ? '授予' : '撤销' }} · {{ g.reason }} · 授予人 {{ g.grantedBy }}<span v-if="g.revokedBy"> · 撤销人 {{ g.revokedBy }} / {{ g.revokedAtUtc }}</span></li></ul></section>
  </section>
</template>
<style scoped>.delegations{padding:16px;max-width:960px}fieldset{display:grid;gap:12px;margin-top:16px}label{display:block}input,select,textarea{padding:6px;margin:4px}button{margin:4px;padding:6px 12px}li{margin-bottom:16px;overflow-wrap:anywhere}aside{border:1px solid var(--el-color-warning);padding:12px}[role=alert]{color:var(--el-color-danger)}</style>
