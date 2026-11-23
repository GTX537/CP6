<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { AUTH_EPOCH, AUTH_RECORD, IMPERSONATION, AUTH_EVENT, identityStamp } from '@/auth/identity'
import { CRON_EXPRESSION_MAX_LENGTH, hasCronInput } from './flowTriggerModel'
import { connectorControl as api, family, type Choices, type Control, type DispatchHistory, type Impact, type Intent, type Kind,
  type Approval, type Receipt, type Selection, type Subject, type Version } from '@/api/oa/connectorControl'

const props = defineProps<{ kind: Kind }>()
const captions: Record<Control, string> = { PUBLISH: '发布', ENABLE: '启用', ACTIVATE: '激活', DISABLE: '停用', RETIRE: '退役' }
const dispatchCaptions: Record<string, string> = {
  DISPATCH_ADMITTED: '已准入', CANCELLATION_REQUESTED: '已请求取消（外部效果仍待确认）',
  SENT: '已发送', HTTP_ACK: '已收到运输确认', OUTCOME_UNKNOWN: '结果未知', RESULT_RECEIVED: '已接收结果',
  NOT_OBSERVED: '尚未查到原操作', ORIGINAL_OPERATION_OBSERVED: '已查到原操作',
}
const choices = ref<Choices>({ profiles: [], definitions: [], actions: [] })
const rows = ref<Version[]>([]), subjects = ref<Subject[]>([]), selected = ref<Version>()
const dispatchHistory = ref<DispatchHistory>()
const operation = ref<Control>(props.kind === 'TRIGGER' ? 'PUBLISH' : 'ACTIVATE'), impact = ref<Impact>(), pending = ref<Intent>()
const busy = ref(false), error = ref(''), notice = ref(''), editing = ref(false), revising = ref(false)
const name = ref(''), label = ref(''), profileId = ref(''), flowId = ref(''), cron = ref(''), vars = ref('{}'), reason = ref('')
const page = ref(1), reviewPage = ref(1), legacyId = ref('')
const key = 'cp6-oa-conn-v1-' + props.kind
const currentProfile = computed(() => choices.value.profiles.find(p => p.id === profileId.value))
const locked = computed(() => busy.value || !!pending.value)
const has = (action: string) => choices.value.actions.includes('conn.' + props.kind.toLowerCase() + '.' + action)
const actions = computed<Control[]>(() => props.kind === 'TRIGGER' ? ['PUBLISH','ENABLE','DISABLE','RETIRE'] : ['ACTIVATE','DISABLE','RETIRE'])
const approved = computed(() => selected.value?.approvals.find(a =>
  a.kind === props.kind + '_' + operation.value && a.decision === 'APPROVE' && (a.impactRef ?? null) === (impact.value?.previewId ?? null)))
const canControl = computed(() => has(operation.value === 'PUBLISH' ? 'publish' : operation.value === 'ACTIVATE' ? 'activate' : 'control'))
let generation = 0, live = true, controller = new AbortController()

function clear() {
  generation++; controller.abort(); controller = new AbortController()
  choices.value = { profiles: [], definitions: [], actions: [] }; rows.value = []; subjects.value = []
  selected.value = undefined; impact.value = undefined; pending.value = undefined; editing.value = false
  dispatchHistory.value = undefined
  name.value = label.value = profileId.value = flowId.value = reason.value = cron.value = ''; vars.value = '{}'
  legacyId.value = ''; busy.value = false; notice.value = ''; error.value = ''; page.value = reviewPage.value = 1
}
function identityChanged() { clear(); sessionStorage.removeItem(key); error.value = '登录身份已变化，请重新读取。' }
function storage(event: StorageEvent) { if (!event.key || [AUTH_EPOCH, AUTH_RECORD, IMPERSONATION].includes(event.key)) identityChanged() }
type Check = () => void
async function run(fn: (check: Check, signal: AbortSignal) => Promise<void>) {
  if (busy.value) return
  busy.value = true; error.value = ''
  const stamp = identityStamp(), at = generation, signal = controller.signal
  const check = () => { if (!live || at !== generation || stamp !== identityStamp()) throw new DOMException('旧请求', 'AbortError') }
  try { await fn(check, signal) }
  catch (e: any) {
    if (!live || at !== generation || stamp !== identityStamp()) return
    const status = e?.response?.status, code = e?.response?.data?.code
    if (status === 401 || status === 403) { identityChanged(); error.value = '当前权限不足，已清除本页资料。' }
    else if (status === 412 || status === 409) { impact.value = undefined; error.value = '版本、复核或影响范围已变化，请重新读取。' + (code ?? '') }
    else error.value = pending.value ? '命令结果尚未确认，请查询原回执。' : (e?.response ? '操作未完成。' + (code ?? status) : e.message)
  } finally { if (at === generation && live) busy.value = false }
}
async function refresh(check: Check, signal: AbortSignal, root?: string, version?: string) {
  const opt = await api.choices(props.kind, signal)
  const list = await api.list(props.kind, page.value, signal)
  const queue = opt.actions.includes('conn.' + props.kind.toLowerCase() + '.review') ? await api.subjects(props.kind, reviewPage.value, signal) : []
  const item = root ? await api.context(props.kind, root, signal, version) : undefined
  check(); choices.value = opt; rows.value = list; subjects.value = queue; selected.value = item; dispatchHistory.value = undefined
}
const resultCaptions: Record<string, string> = {
  SUCCEEDED: '已成功', QUARANTINED: '已隔离', HELD: '待本地应用', APPLIED: '已应用', STALE_TOKEN: '已归档的迟到结果',
  UNPROVEN: '未证实', PENDING_GAP: '回执待补齐', STALE: '已过期', CONFLICT: '有冲突', BLOCKED: '受阻',
}
function reload() { return run((check, signal) => refresh(check, signal, selected.value?.rootId, selected.value?.versionId)) }
function select(row: Version) {
  return run(async (check, signal) => {
    const r = await api.context(props.kind, row.rootId, signal, row.versionId); check()
    selected.value = r; impact.value = undefined; dispatchHistory.value = undefined; editing.value = false; reason.value = ''
    operation.value = props.kind === 'CONNECTOR' ? (r.state === 'ENABLED' ? 'DISABLE' : 'ACTIVATE')
      : (r.published ? (r.state === 'ENABLED' ? 'DISABLE' : 'ENABLE') : 'PUBLISH')
  })
}
function edit(revise: boolean) {
  const row = revise ? selected.value : undefined
  editing.value = true; revising.value = revise; impact.value = undefined
  legacyId.value = row?.content.selection.legacyRootId ?? ''
  name.value = row?.name ?? ''; label.value = row?.displayName ?? ''; profileId.value = row?.profile.id ?? ''
  flowId.value = row?.content.selection.flowVersionId ?? ''; cron.value = row?.content.config?.cron ?? ''
  vars.value = JSON.stringify(row?.content.config?.vars ?? row?.content.config?.varsMap ?? {})
}
function protect(intent: Intent) {
  sessionStorage.setItem(key, JSON.stringify({ stamp: identityStamp(), intent }))
  pending.value = intent
}
function settled() { sessionStorage.removeItem(key); pending.value = undefined }
async function accept(result: Receipt, intent: Intent, check: Check, signal: AbortSignal) {
  if (result.outcome !== 'SUCCEEDED' || result.commandId !== intent.command.commandId) throw new Error('原回执不匹配。')
  check(); settled(); editing.value = false
  if (result.result.previewId) impact.value = result.result as Impact
  else if (result.result.effect || intent.path.endsWith('/versions')) impact.value = undefined
  notice.value = result.result.effect ? '控制已确认，在途保留 ' + (result.result.notRevokedInFlight ?? 0) + ' 项。' : '已取得原命令回执。'
  await refresh(check, signal, result.rootId, result.versionId)
}
async function transmit(intent: Intent, check: Check, signal: AbortSignal, retry = false) {
  if (!retry) protect(intent)
  let receipt: Receipt
  try { receipt = await api.execute(intent, signal) }
  catch (e: any) {
    check()
    if (!retry && [400,401,403,404,409,412,422,428].includes(e?.response?.status)) settled()
    throw e
  }
  await accept(receipt, intent, check, signal)
}
function recover(retry: boolean) {
  const intent = pending.value
  if (!intent) return
  return run(async (check, signal) => {
    if (retry) return transmit(intent, check, signal, true)
    const original = await api.original(intent.command.commandId, signal); check()
    if (original.outcome === 'NOT_OBSERVED') { notice.value = '尚未观察到原命令，可继续查询或按原键重试。'; return }
    await accept(original, intent, check, signal)
  })
}
function send(path: string, expectedVersion: string, body: unknown) {
  return run((check, signal) => transmit({ path, command: { commandId: crypto.randomUUID(), expectedVersion, body } }, check, signal))
}
function save() {
  return run(async (check, signal) => {
    const profile = currentProfile.value
    if (!profile) throw new Error('请选择已登记的依赖。')
    const selection: Selection = { name: name.value, sourceKind: props.kind === 'CONNECTOR' ? profile.sourceKind! : profile.kind, profileId: profile.id }
    if (legacyId.value) selection.legacyRootId = legacyId.value
    const flow = choices.value.definitions.find(f => f.versionId === flowId.value)
    if (props.kind === 'TRIGGER') {
      if (!flow) throw new Error('请选择准确的已发布流程版本。')
      selection.flowKey = flow.flowKey; selection.flowVersionId = flow.versionId
    }
    if (profile.kind === 'TIMER' && !hasCronInput(cron.value))
      throw new Error('请输入 1 至 ' + CRON_EXPRESSION_MAX_LENGTH + ' 字符的 Cron 表达式。')
    const config = profile.kind === 'TIMER' ? { cron: cron.value, vars: JSON.parse(vars.value) }
      : profile.kind === 'EVENT' ? { varsMap: JSON.parse(vars.value) } : null
    const row = selected.value
    const expectedVersion = revising.value && row ? row.revision : (await api.creation(props.kind, selection, signal)).expectedVersion
    check()
    const path = family(props.kind) + (revising.value && row ? '/' + row.rootId + '/versions' : '')
    await transmit({ path, command: { commandId: crypto.randomUUID(), expectedVersion, body: { selection, displayName: label.value, config } } }, check, signal)
  })
}
function action(name: 'preview' | 'submit' | 'control') {
  const row = selected.value
  if (!row) return
  const base = family(props.kind) + '/' + row.rootId + '/'
  if (name === 'preview') return send(base + 'control-impact-previews', row.revision, { versionId: row.versionId, action: operation.value })
  if (name === 'submit') return send(base + 'submit-review', row.revision, { versionId: row.versionId, kind: props.kind + '_' + operation.value, impactPreviewRef: impact.value?.previewId ?? null })
  return send(base + operation.value.toLowerCase(), row.revision, { versionId: row.versionId, impactPreviewRef: impact.value?.previewId ?? null,
    decisionRef: operation.value === 'DISABLE' ? null : approved.value?.decisionRef, reason: reason.value || null })
}
function impactPage(page: number) {
  const id = impact.value?.previewId
  if (!id) return
  return run(async (check, signal) => { const value = await api.preview(id, signal, page); check(); impact.value = value })
}
function readDispatches(page: number) {
  const row = selected.value
  if (!row) return
  dispatchHistory.value = undefined
  return run(async (check, signal) => {
    const history = await api.dispatches(row.rootId, row.versionId, page, signal)
    check()
    dispatchHistory.value = history
  })
}
function openApproved(ref: Approval) {
  return run(async (check, signal) => {
    const preview = ref.impactRef ? await api.preview(ref.impactRef, signal) : undefined
    check(); operation.value = ref.kind.split('_')[1] as Control; impact.value = preview
  })
}
function decide(s: Subject, decision: string) { return send(family(props.kind) + '/' + s.rootId + '/decisions', s.revision, { subjectRef: s.subjectRef, decision }) }
function move(list: 'roots'|'reviews', delta: number) {
  if (list === 'roots') page.value += delta; else reviewPage.value += delta
  return reload()
}
onMounted(() => {
  window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', storage)
  try {
    const saved = JSON.parse(sessionStorage.getItem(key) ?? 'null')
    if (saved?.stamp === identityStamp()) pending.value = saved.intent
    else sessionStorage.removeItem(key)
  } catch { error.value = '无法读取原命令恢复记录。'; return }
  void reload()
})
onBeforeUnmount(() => { live = false; controller.abort(); window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', storage) })
</script>

<template>
  <section class="control-panel" :data-testid="'conn-' + kind" :aria-label="kind === 'TRIGGER' ? '触发器版本控制' : '连接器版本控制'">
    <header><h3>{{ kind === 'TRIGGER' ? '触发器版本控制' : '连接器版本控制' }}</h3>
      <button :disabled="locked" @click="reload">刷新</button><button v-if="has('write')" :disabled="locked" @click="edit(false)">新建版本配置</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><button :disabled="busy" @click="recover(false)">查询原回执</button><button :disabled="busy" @click="recover(true)">按原键重试</button></div>
    <ul><li v-for="row in rows" :key="row.rootId"><button :disabled="locked" @click="select(row)">{{ row.name }} · {{ row.sourceKind }} · v{{ row.number }}</button> {{ row.displayName }} · {{ row.state }}</li></ul>
    <nav v-if="page > 1 || rows.length === 100"><button :disabled="locked || page === 1" @click="move('roots', -1)">上一页配置</button><button :disabled="locked || rows.length < 100" @click="move('roots', 1)">下一页配置</button></nav>
    <form v-if="editing" @submit.prevent="save">
      <label v-if="!revising && choices.legacySources?.length">接管已停用的旧配置<select v-model="legacyId"><option value="">新建配置</option>
        <option v-for="s in choices.legacySources" :key="s.id" :value="s.id">{{ s.name }} · {{ s.sourceKind }} · {{ s.id }}</option>
      </select></label>
      <label>配置名称<input v-model="name" required maxlength="100" :readonly="revising"></label>
      <label>显示名称<input v-model="label" required maxlength="200"></label>
      <label>登记依赖<select v-model="profileId" required><option value="">请选择</option><option v-for="p in choices.profiles" :key="p.id" :value="p.id">{{ p.label }} · {{ p.adoption }}</option></select></label>
      <label v-if="kind === 'TRIGGER'">已发布流程版本<select v-model="flowId" required><option value="">请选择</option><option v-for="f in choices.definitions" :key="f.versionId" :value="f.versionId">{{ f.flowKey }} · v{{ f.version }}</option></select></label>
      <label v-if="currentProfile?.kind === 'TIMER'">定时表达式<input v-model="cron" :maxlength="CRON_EXPRESSION_MAX_LENGTH" required></label>
      <label v-if="currentProfile?.kind === 'TIMER' || currentProfile?.kind === 'EVENT'">变量配置 JSON<textarea v-model="vars" /></label>
      <button :disabled="locked">保存草稿</button><button type="button" :disabled="locked" @click="editing = false">取消</button>
    </form>
    <article v-if="selected && !editing">
      <h4>{{ selected.displayName }} · v{{ selected.number }} · {{ selected.state }}</h4>
      <p>{{ selected.sourceKind }} · {{ selected.profile.adoption }} <span v-if="selected.profile.implementation">· {{ selected.profile.implementation }} {{ selected.profile.implementationVersion }} · {{ selected.profile.operation }}</span></p>
      <p v-if="kind === 'CONNECTOR'">凭据绑定 {{ selected.profile.hasAuth ? '已配置' : '无' }} · {{ selected.profile.credentialGeneration }}
        · 目的策略 {{ selected.profile.destinationPolicy }} · 输入 {{ selected.profile.inputSchema }} · 输出 {{ selected.profile.outputSchema }}
        · 总调用时长 {{ selected.profile.maxCallDurationSec ?? '未证' }} 秒</p>
      <p v-else>流程 {{ selected.content.selection.flowKey }} · {{ selected.content.selection.flowVersionId }}</p>
      <p v-if="selected.sourceKind === 'TIMER'">时区 {{ selected.profile.timeZone }} · {{ selected.profile.timeRules }} · {{ selected.profile.misfirePolicy }}</p>
      <p v-if="selected.profile.registration">来源登记 {{ selected.profile.registration }} · {{ selected.profile.schema }}</p>
      <label v-if="selected.versions.length > 1">查看版本<select :value="selected.versionId" :disabled="locked"
        @change="select({ ...selected, versionId: ($event.target as HTMLSelectElement).value })">
        <option v-for="v in selected.versions" :key="v.id" :value="v.id">v{{ v.number }}</option>
      </select></label>
      <button v-if="has('write') && selected.versionId === selected.latest" :disabled="locked" @click="edit(true)">修订元数据</button>
      <section v-if="kind === 'CONNECTOR' && has('control')">
        <button :disabled="locked" @click="readDispatches(1)">读取派发记录</button>
        <template v-if="dispatchHistory">
          <p v-if="!dispatchHistory.entries.length">本页没有派发记录。</p>
          <ul v-else>
            <li v-for="item in dispatchHistory.entries" :key="item.jobId" :data-dispatch-state="item.state">
              {{ item.jobId }} · {{ item.adoption }} · {{ dispatchCaptions[item.state] ?? item.state }}
              · 运输 {{ dispatchCaptions[item.transportState] ?? item.transportState }}
              <span v-if="item.originalQueryState"> · 查证 {{ dispatchCaptions[item.originalQueryState] ?? item.originalQueryState }}</span>
              <span v-if="item.resultState" :data-result-state="item.resultState"> · 外部结果 {{ resultCaptions[item.resultState] ?? item.resultState }}</span>
              <span v-if="item.workflowResume" :data-workflow-resume="item.workflowResume"> · 流程 {{ resultCaptions[item.workflowResume] ?? item.workflowResume }}</span>
              <span v-if="item.businessOwnerApplication" :data-owner-application="item.businessOwnerApplication">
                · 业务应用 {{ resultCaptions[item.businessOwnerApplication] ?? item.businessOwnerApplication }}
                <template v-if="item.businessOwnerEvidence">（{{ item.businessOwnerEvidence }}）</template>
              </span>
            </li>
          </ul>
          <nav v-if="dispatchHistory.page > 1 || dispatchHistory.hasNext">
            <button :disabled="locked || dispatchHistory.page === 1" @click="readDispatches(dispatchHistory.page - 1)">上一页派发</button>
            <button :disabled="locked || !dispatchHistory.hasNext" @click="readDispatches(dispatchHistory.page + 1)">下一页派发</button>
          </nav>
        </template>
      </section>
      <label>控制动作<select v-model="operation" :disabled="locked" @change="impact = undefined"><option v-for="a in actions" :key="a" :value="a">{{ captions[a] }}</option></select></label>
      <button v-if="operation !== 'PUBLISH' && canControl" :disabled="locked" @click="action('preview')">读取影响预览</button>
      <p v-if="impact">覆盖 {{ impact.coverage }} · Job {{ impact.manifest.jobs }} · 在途 {{ impact.manifest.inFlight }} · 实例 {{ impact.manifest.instances }} · 触发 {{ impact.manifest.invocations }} · 时隙 {{ impact.manifest.slots }}</p>
      <details v-if="impact?.total"><summary>查看影响清单（{{ impact.total }}）</summary>
        <ul><li v-for="item in impact.entries" :key="item.locator">{{ item.kind }} · {{ item.state }} · {{ item.locator }}</li></ul>
        <nav v-if="impact.total > 100"><button :disabled="locked || impact.page === 1" @click="impactPage(impact.page - 1)">上一页影响</button>
          <button :disabled="locked || impact.page * 100 >= impact.total" @click="impactPage(impact.page + 1)">下一页影响</button></nav>
      </details>
      <button v-if="operation !== 'DISABLE' && (operation === 'PUBLISH' ? has('write') : canControl)" :disabled="locked || (operation !== 'PUBLISH' && impact?.coverage !== 'COMPLETE')" @click="action('submit')">提交{{ captions[operation] }}复核</button>
      <ul><li v-for="r in selected.approvals" :key="r.subjectRef">{{ r.kind }} · {{ r.decision }}
        <button v-if="r.decision === 'APPROVE'" :disabled="locked" @click="openApproved(r)">采用{{ captions[r.kind.split('_')[1] as Control] }}决定</button></li></ul>
      <label v-if="operation !== 'PUBLISH'">操作原因<input v-model="reason" maxlength="500"></label>
      <button v-if="canControl" :disabled="locked || (operation !== 'DISABLE' && !approved) || (operation !== 'PUBLISH' && (impact?.coverage !== 'COMPLETE' || !reason.trim()))" @click="action('control')">确认{{ captions[operation] }}</button>
    </article>
    <section v-if="subjects.length"><h4>待复核</h4><div v-for="s in subjects" :key="s.subjectRef">
      <span>{{ s.target.displayName }} · v{{ s.target.number }} · {{ s.kind }} · {{ s.target.profile.adoption }}</span>
      <span v-if="s.impact"> · 影响 {{ s.impact.coverage }} · Job {{ s.impact.manifest.jobs }} · 在途 {{ s.impact.manifest.inFlight }}</span>
      <details><summary>复核的准确版本</summary><p>{{ s.versionId }} · {{ s.target.sourceKind }} · {{ s.target.name }}</p>
        <p v-if="s.target.content.selection.flowVersionId">流程 {{ s.target.content.selection.flowKey }} · {{ s.target.content.selection.flowVersionId }}</p>
        <p v-if="s.target.profile.operation">{{ s.target.profile.implementation }} {{ s.target.profile.implementationVersion }} · {{ s.target.profile.operation }} · {{ s.target.profile.destinationPolicy }}</p>
        <p v-if="s.target.profile.registration || s.target.profile.timeZone">{{ s.target.profile.registration ?? s.target.profile.timeZone }} · {{ s.target.profile.schema ?? s.target.profile.timeRules }}</p>
      </details>
      <button v-if="s.canDecide" :disabled="locked" @click="decide(s, 'APPROVE')">批准{{ captions[s.kind.split('_')[1] as Control] }}</button>
      <button v-if="s.canDecide" :disabled="locked" @click="decide(s, 'REJECT')">驳回</button>
    </div></section>
    <nav v-if="reviewPage > 1 || subjects.length === 100"><button :disabled="locked || reviewPage === 1" @click="move('reviews', -1)">上一页复核</button><button :disabled="locked || subjects.length < 100" @click="move('reviews', 1)">下一页复核</button></nav>
  </section>
</template>

<style scoped>
.control-panel { padding: 16px; margin-bottom: 16px; border: 1px solid var(--el-border-color); border-radius: 8px; }
header { display: flex; align-items: center; gap: 8px; } h3 { margin: 0 auto 0 0; }
label { display: grid; gap: 4px; max-width: 34rem; margin: 10px 0; }
button, input, select, textarea { font: inherit; padding: 5px 8px; } button { margin: 3px; cursor: pointer; }
button:disabled { cursor: default; } ul { padding-left: 1.4rem; } [role=alert] { color: var(--el-color-danger); }
</style>
