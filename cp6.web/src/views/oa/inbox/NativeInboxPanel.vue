<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { AUTH_EVENT, AUTH_EPOCH, AUTH_RECORD, IMPERSONATION, identityStamp } from '@/auth/identity'
import { clearActingAs, getActingAs, setActingAs } from '@/stores/oaActingAs'
import { taskDelegationApi, type TaskGrantPrincipal } from '@/api/oa/taskDelegation'
import { nativeInboxApi as api, type InboxKind, type NativeDetail, type NativeInboxPage, type NativeReadIntent, type NativeReadResult } from '@/api/oa/nativeInbox'
import { formObject, readFormJson, schemaFields } from '../catalog/personalFormJson'
import PersonalFormFields from '../catalog/PersonalFormFields.vue'
import PersonalTaskPanel from './PersonalTaskPanel.vue'
import NativeTaskOperations from './NativeTaskOperations.vue'
import ReturnedFormPanel from './ReturnedFormPanel.vue'
import PendingResolutionPanel from './PendingResolutionPanel.vue'
import NativeTransferBatchPanel from './NativeTransferBatchPanel.vue'
import NativeTimeoutStatus from './NativeTimeoutStatus.vue'

const route = useRoute(), kind = ref<InboxKind>('pending'), mode = ref<'expanded' | 'merged'>('expanded'), search = ref(''), page = ref(1)
const rows = ref<NativeInboxPage>(), detail = ref<NativeDetail>(), busy = ref(false), detailBusy = ref(false), commandBusy = ref(false)
const error = ref(''), message = ref(''), selected = ref(''), selectedTask = ref(typeof route.query.personalTask === 'string' ? route.query.personalTask : '')
const pending = ref<NativeReadIntent>(), storageBlocked = ref(false), actingAs = ref(getActingAs())
const principals = ref<TaskGrantPrincipal[]>([]), grantError = ref(''), chosenPrincipal = ref(getActingAs()?.userId ?? '')
const selectedOperation = ref(''), selectedResolution = ref('')
const returnedInstance = computed(() => !actingAs.value && detail.value?.contextKind === 'SFS' && detail.value.graph.some(i => i.instanceId === detail.value?.rootInstanceId && i.status === 5) ? detail.value.rootInstanceId : '')
const cache = 'cp6-native-inbox-read-pending'
const folders: { value: InboxKind; label: string }[] = [{ value: 'pending', label: '待办' }, { value: 'cc', label: '抄送' }, { value: 'running', label: '参与的在途' }, { value: 'done', label: '已办' }]
const fields = computed(() => detail.value?.form ? schemaFields(detail.value.form.schemaJson) : [])
const data = computed(() => detail.value?.form ? formObject(readFormJson(detail.value.form.dataJson)) : {})
const pageTotal = computed(() => rows.value ? mode.value === 'expanded' ? rows.value.totalItems : rows.value.totalGroups : 0)
let live = true, epoch = 0, listGeneration = 0, detailGeneration = 0, listController: AbortController | undefined, detailController: AbortController | undefined
const stamp = () => JSON.stringify([identityStamp(), getActingAs()?.userId ?? null])
function remember() {
  if (pending.value) sessionStorage.setItem(cache, JSON.stringify(pending.value)); else sessionStorage.removeItem(cache)
}
try {
  const raw = sessionStorage.getItem(cache)
  if (raw) {
    const intent = JSON.parse(raw) as NativeReadIntent
    if (intent.identity === stamp() && ['TASK', 'CC'].includes(intent.kind) && /^[0-9a-f-]{36}$/i.test(intent.target) && /^[A-Za-z0-9_.-]{1,100}$/.test(intent.command)) pending.value = intent
    else sessionStorage.removeItem(cache)
  }
} catch { storageBlocked.value = true; error.value = '无法读取浏览器中的未确认命令，暂时不能发送新的已读操作。' }
function current(identity: string, captured: number) {
  if (!live || epoch !== captured || identity !== stamp()) throw new Error('STALE')
}
function clearView() {
  selectedOperation.value = ''; selectedResolution.value = ''
  rows.value = undefined; detail.value = undefined; selected.value = ''; selectedTask.value = ''
  listGeneration++; detailGeneration++; listController?.abort(); detailController?.abort(); busy.value = detailBusy.value = false
}
function identityChanged() {
  epoch++; clearView(); commandBusy.value = false; pending.value = undefined; message.value = ''; error.value = '身份已改变，请刷新读取当前任务。'; actingAs.value = getActingAs()
  try { remember(); storageBlocked.value = false } catch { storageBlocked.value = true }
  principals.value = []; chosenPrincipal.value = getActingAs()?.userId ?? ''
}
async function loadPrincipals() {
  const identity = stamp(), captured = epoch; grantError.value = ''; principals.value = []
  try { const value = await taskDelegationApi.received(); current(identity, captured); principals.value = value }
  catch { try { current(identity, captured); grantError.value = '暂时无法读取当前可代理人员，请重试。' } catch { /* obsolete identity */ } }
}
function choosePrincipal() {
  if (pending.value || commandBusy.value) return
  try {
    const selected = principals.value.find(x => x.userId === chosenPrincipal.value)
    if (chosenPrincipal.value && !selected) { grantError.value = '当前选择不在有效委派中，请重新读取。'; return }
    if (selected) setActingAs({ userId: selected.userId, userName: selected.name }); else clearActingAs()
    identityChanged(); void load(true); void loadPrincipals()
  } catch { clearView(); grantError.value = '无法保存身份选择，请重试。' }
}
function taskPrivateClear() {
  const task = selectedTask.value; clearView(); selectedTask.value = task
  error.value = '当前办理入口拒绝访问，已清除先前读取的内容；原办理命令仍可恢复。'
}
function rejected(e: any) {
  const code = e?.response?.data?.code, status = e?.response?.status
  if ([401, 403, 404].includes(status)) clearView()
  error.value = ['GRANT_SCOPE_DENIED', 'GRANT_AUTHORITY_CHANGED', 'GRANT_PRINCIPAL_UNAVAILABLE'].includes(code) ? '代理授权已到期、撤销或不覆盖当前操作，请重新读取授权或切回本人。'
    : [401, 403, 404].includes(status) ? '当前身份已不能读取这些内容，页面已清除。'
      : pending.value ? '已读命令的结果尚未确认，请查询原结果或按原命令重试。' : '暂时无法读取当前任务，请重试。'
}
async function load(reset = false) {
  selectedOperation.value = ''; selectedResolution.value = ''
  if (reset) page.value = 1
  const identity = stamp(), captured = epoch, request = ++listGeneration
  listController?.abort(); listController = new AbortController(); rows.value = undefined; detail.value = undefined; selected.value = ''; selectedTask.value = ''
  detailGeneration++; detailController?.abort(); detailBusy.value = false; busy.value = true; error.value = ''
  try {
    const value = await api.list(kind.value, page.value, mode.value, search.value, listController.signal)
    current(identity, captured); if (request !== listGeneration) return; rows.value = value
  } catch (e) { try { current(identity, captured); if (request === listGeneration) rejected(e) } catch { /* Obsolete identity. */ } }
  finally { if (live && request === listGeneration && captured === epoch) busy.value = false }
}
async function open(id: string) {
  selectedOperation.value = ''; selectedResolution.value = ''
  const identity = stamp(), captured = epoch, request = ++detailGeneration
  detailController?.abort(); detailController = new AbortController(); selected.value = id; detail.value = undefined; selectedTask.value = ''; detailBusy.value = true; error.value = ''
  try {
    const value = await api.detail(id, detailController.signal); current(identity, captured)
    if (request !== detailGeneration || selected.value !== id) return; detail.value = value
  } catch (e) { try { current(identity, captured); if (request === detailGeneration) rejected(e) } catch { /* Obsolete identity. */ } }
  finally { if (live && request === detailGeneration && captured === epoch) detailBusy.value = false }
}
function accept(value: NativeReadResult, intent: NativeReadIntent) {
  if (value.commandId !== intent.command || value.state === 'READ' && (!value.result || value.result.kind !== intent.kind || value.result.targetId !== intent.target)) throw new Error('READ_RESULT_MISMATCH')
  if (value.state === 'NOT_OBSERVED') { message.value = '尚未观察到原结果，这不表示操作没有发生；可原键重试或关闭该命令。'; return }
  if (value.state !== 'READ' && value.state !== 'CLOSED') throw new Error('READ_RESULT_MISMATCH')
  pending.value = undefined; remember(); message.value = value.state === 'READ' ? '已确认首次阅读记录；审批状态未改变。' : '原命令已关闭，不会再执行。'
  const result = value.result
  if (result && detail.value) {
    if (result.kind === 'TASK') { const task = detail.value.tasks.find(t => t.taskId === result.targetId); if (task) task.read = result }
    else { const cc = detail.value.cc.find(c => c.ccId === result.targetId); if (cc) cc.read = result }
  }
  // Aggregate badges are a prior snapshot; require an explicit refresh instead of guessing a new count.
  rows.value = undefined
}
async function recover(action: 'send' | 'original' | 'close') {
  if (!pending.value || commandBusy.value || storageBlocked.value) return
  const intent = { ...pending.value }, captured = epoch; commandBusy.value = true; error.value = ''; message.value = ''
  try {
    current(intent.identity, captured)
    const result = await (action === 'send' ? api.read(intent) : action === 'original' ? api.original(intent) : api.close(intent))
    current(intent.identity, captured); accept(result, intent)
  } catch (e) { try { current(intent.identity, captured); rejected(e) } catch { /* Obsolete identity cannot display a receipt. */ } }
  finally { if (live && captured === epoch) commandBusy.value = false }
}
function mark(kind: 'TASK' | 'CC', target: string) {
  if (pending.value || commandBusy.value || storageBlocked.value) return
  pending.value = { identity: stamp(), kind, target, command: crypto.randomUUID() }
  try { remember() } catch { pending.value = undefined; storageBlocked.value = true; error.value = '浏览器无法保存恢复记录，本次已读请求未发送。'; return }
  void recover('send')
}
function self() { clearActingAs(); identityChanged(); void load(true) }
const status = (value: number) => ['进行中', '已通过', '已拒绝', '已撤回或取消', '已挂起', '退回待修改'][value] ?? `状态 ${value}`
const taskStatus = (value: number) => ['待办', '已同意', '已拒绝', '已作废', '已挂起'][value] ?? `状态 ${value}`
onMounted(() => { if (!selectedTask.value) void load() })
window.addEventListener(AUTH_EVENT, identityChanged)
function storageChanged(e: StorageEvent) { if (e.key === null || [AUTH_EPOCH, AUTH_RECORD, IMPERSONATION].includes(e.key)) identityChanged() }
window.addEventListener('storage', storageChanged)
onBeforeUnmount(() => { live = false; epoch++; listController?.abort(); detailController?.abort(); window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', storageChanged) })
</script>

<template>
  <section class="native-inbox">
    <h2>当前任务</h2>
    <p>列表和详情按当前权限读取。打开详情不会标记已读，也不会办理审批。</p>
    <p v-if="actingAs" role="status">当前保存了代理身份：{{ actingAs.userName }}。<el-button :disabled="!!pending || commandBusy" @click="self">切回本人</el-button></p>
    <div class="toolbar"><label>查看身份 <select v-model="chosenPrincipal" aria-label="当前本人或代理身份" :disabled="!!pending || commandBusy" @change="choosePrincipal"><option value="">本人</option><option v-for="p in principals" :key="p.userId" :value="p.userId">代理 {{ p.name }}</option></select></label><el-button @click="loadPrincipals">读取可代理人员</el-button></div><p v-if="grantError" role="alert">{{ grantError }}</p>
    <div class="toolbar">
      <el-select v-model="kind" aria-label="任务分类" @change="load(true)"><el-option v-for="f in folders" :key="f.value" :label="f.label" :value="f.value" /></el-select>
      <el-select v-model="mode" aria-label="显示方式" @change="load(true)"><el-option label="逐项显示" value="expanded" /><el-option label="按实例合并" value="merged" /></el-select>
      <el-input v-model="search" aria-label="搜索当前可见任务" placeholder="流程、可见标题或业务类型" maxlength="200" @keyup.enter="load(true)" />
      <el-button :loading="busy" @click="load(true)">刷新</el-button>
    </div>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="message" role="status">{{ message }}</p>
    <aside v-if="pending" class="recovery" aria-label="未确认的已读命令">
      <p>未确认命令：{{ pending.command }}，对象：{{ pending.target }}</p>
      <el-button :disabled="commandBusy" @click="recover('original')">查询原结果</el-button>
      <el-button :disabled="commandBusy" @click="recover('send')">原命令重试</el-button>
      <el-button :disabled="commandBusy" @click="recover('close')">关闭原命令</el-button>
    </aside>
    <template v-if="rows">
      <p>当前可见 {{ rows.totalItems }} 项，{{ rows.totalGroups }} 个实例。实际登录人：{{ rows.actualUserId }}；当前职责人：{{ rows.effectiveUserId }}</p>
      <el-table :data="rows.items" aria-label="当前任务列表">
        <el-table-column label="流程"><template #default="{ row }">{{ row.flowKey }} · {{ row.contextKind === 'APP' ? '业务审批' : '表单审批' }}</template></el-table-column>
        <el-table-column label="可见摘要"><template #default="{ row }">{{ row.title || row.businessType || '—' }}</template></el-table-column>
        <el-table-column label="流程状态"><template #default="{ row }">{{ status(row.instanceStatus) }}</template></el-table-column>
        <el-table-column prop="unreadCount" label="未读项" />
        <el-table-column label="操作"><template #default="{ row }"><el-button @click="open(row.instanceId)">查看详情</el-button><span>{{ row.taskIds.length }} 个任务 / {{ row.ccIds.length }} 个抄送</span></template></el-table-column>
      </el-table>
      <el-pagination v-model:current-page="page" :page-size="rows.pageSize" :total="pageTotal" layout="prev, pager, next" @current-change="load()" />
    </template>
    <p v-if="detailBusy">正在读取当前详情…</p>
    <article v-if="detail" aria-label="当前授权详情">
      <h3>{{ detail.form?.title || detail.business?.businessType || '流程详情' }}</h3>
      <p>当前查看：{{ detail.instanceId }}；根实例：{{ detail.rootInstanceId }}</p>
      <section v-if="detail.form"><p>{{ detail.form.dataSource === 'RETURNED_CURRENT_CYCLE' ? '内容为退回后当前审批周期的表单' : '内容来自原始提交表单' }}，版本 {{ detail.form.formVersionId }}。</p><PersonalFormFields :fields="fields" :model-value="data" :mask="detail.form.fieldMask" :file-context="{ instanceId: detail.instanceId }" disabled @forbidden="detail = undefined" /></section>
      <section v-if="detail.business">
        <p>业务对象：{{ detail.business.subjectId }}，提交时版本 {{ detail.business.businessRevision }}。</p>
        <p>流程审批状态与业务应用状态分别记录，请在业务页面查询应用结果。</p>
        <router-link v-if="detail.business.detailRoute" :to="detail.business.detailRoute">打开业务审批页面</router-link>
        <p v-else>请从对应业务模块打开该对象的审批页面。</p>
      </section>
      <h4>实例与固定版本</h4>
      <ul><li v-for="pin in detail.graph" :key="pin.instanceId">{{ pin.flowKey }}：{{ status(pin.status) }}；流程版本 {{ pin.flowVersionId }}；此实例表单版本 {{ pin.formVersionId || '无' }}；父实例 {{ pin.parentInstanceId || '无' }}<el-button v-if="pin.status === 4 && !actingAs" @click="selectedResolution = pin.instanceId">无人审批恢复条件</el-button></li></ul>
      <NativeTimeoutStatus v-if="detail.timeout" :value="detail.timeout" />
      <h4>具体任务</h4>
      <ul><li v-for="task in detail.tasks" :key="task.taskId" class="task-item">
        <p>任务 {{ task.taskId }} · {{ taskStatus(task.status) }} · 节点 {{ task.nodeId }} · 阶段 {{ task.stageIndex }} / 轮次 {{ task.stageRound }} · 当前处理人 {{ task.principalId }}<span v-if="task.createdPrincipalId && task.createdPrincipalId !== task.principalId"> · 最初处理人 {{ task.createdPrincipalId }}</span></p>
        <details><summary>核对版本与流转位置</summary><p>任务版本 {{ task.taskRowVersion }}；流转位置 {{ task.tokenId || '无' }}；所属实例 {{ task.instanceId }}。</p></details>
        <p>{{ task.read ? `首次已读 ${task.read.firstReadAtUtc}，实际阅读人 ${task.read.actualUserId}` : '尚无阅读记录' }}</p>
        <el-button v-if="task.principalId === detail.effectiveUserId && !task.read" :disabled="!!pending || commandBusy || storageBlocked" @click="mark('TASK', task.taskId)">标记已读</el-button>
        <el-button v-if="detail.contextKind === 'SFS' && task.canRequestDecisionContext" @click="selectedTask = task.taskId">读取当前办理条件</el-button>
        <el-button v-if="task.canRequestDecisionContext" @click="selectedOperation = task.taskId">退回、转交或加签</el-button>
      </li></ul>
      <h4 v-if="detail.cc.length">我的抄送</h4>
      <ul><li v-for="cc in detail.cc" :key="cc.ccId">{{ cc.ccId }} · {{ cc.read ? `首次已读 ${cc.read.firstReadAtUtc}` : '未读' }} <el-button v-if="!cc.read" :disabled="!!pending || commandBusy || storageBlocked" @click="mark('CC', cc.ccId)">标记抄送已读</el-button></li></ul>
      <h4>实际办理记录</h4>
      <ul><li v-for="vote in detail.votes" :key="vote.voteId">{{ vote.occurredAtUtc }} · {{ vote.approved ? '同意' : '拒绝' }} · 指定处理人 {{ vote.principalId }} · 实际办理人 {{ vote.actualUserId }}<span v-if="vote.onBehalfOfId"> · 代理 {{ vote.onBehalfOfId }}</span> · {{ vote.comment }}</li></ul>
      <h4>流程历史</h4>
      <ul><li v-for="event in detail.history" :key="event.eventId">{{ event.recordedAt }} · {{ event.action }} · {{ event.actorKind === 'SYSTEM' ? '系统事件' : event.actorId }} · {{ event.comment }}</li></ul>
    </article>
    <PersonalTaskPanel v-if="selectedTask" :key="selectedTask" :task-id="selectedTask" @completed="selected ? open(selected) : load()" @private-clear="taskPrivateClear" />
    <NativeTaskOperations :task-id="selectedOperation" :context-kind="detail?.contextKind ?? 'APP'" @completed="selected ? open(selected) : load()" @private-clear="clearView" />
    <ReturnedFormPanel :instance-id="returnedInstance" @completed="selected ? open(selected) : load()" @private-clear="clearView" />
    <PendingResolutionPanel :instance-id="selectedResolution" @completed="selected ? open(selected) : load()" @private-clear="clearView" />
    <NativeTransferBatchPanel />
  </section>
</template>
<style scoped>
.native-inbox { padding: 16px; }.toolbar { display: flex; flex-wrap: wrap; gap: 8px; }.toolbar .el-select { width: 160px; }.toolbar .el-input { width: min(360px, 100%); }.recovery { padding: 12px; border: 1px solid var(--el-color-warning); }.task-item { margin-bottom: 16px; }article { margin-top: 24px; }li, p { overflow-wrap: anywhere; }
</style>
