<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { approvalBindingApi as api, type BindingView, type BindingOptions, type BindingWrite, type BindingIntent, type BindingTimeline } from '@/api/oa/approvalBinding'
const rows = ref<BindingView[]>([]), history = ref<BindingView[]>([]), options = ref<BindingOptions>(), timeline = ref<BindingTimeline>()
const pending = ref<BindingIntent>(), selected = ref<BindingView>(), form = ref<BindingWrite>(), canConfigure = ref(false)
const busy = ref(false), error = ref(''), message = ref(''), reason = ref(''), typeIndex = ref(''), from = ref(''), until = ref(''), nextCursor = ref<string | null>(null), historyCursor = ref<string | null>(null)
const locked = computed(() => busy.value || !!pending.value), cache = 'cp6-approval-binding-pending'
const flowKeys = computed(() => [...new Set(options.value?.flowVersions.map(v => v.flowKey) ?? [])])
let live = true, generation = 0, controller: AbortController | undefined
function remember() { if (pending.value) sessionStorage.setItem(cache, JSON.stringify({ identity: identityStamp(), pending: pending.value })); else sessionStorage.removeItem(cache) }
function clear() {
  generation++; controller?.abort(); rows.value = []; history.value = []; timeline.value = undefined; options.value = undefined
  pending.value = undefined; selected.value = undefined; form.value = undefined; canConfigure.value = false; error.value = ''; message.value = ''; reason.value = ''; from.value = ''; until.value = ''; typeIndex.value = ''; busy.value = false; remember()
}
async function run(action: (check: () => void, signal: AbortSignal) => Promise<void>, querying = false) {
  if (busy.value) return
  const identity = identityStamp(), request = generation; controller = new AbortController(); busy.value = true; error.value = ''
  const check = () => { requireIdentity(identity); if (!live || generation !== request) throw new Error('STALE') }
  try { await action(check, controller.signal); check() }
  catch (e: any) {
    try {
      check(); const status = e?.response?.status
      if (status === 401 || status === 403) { clear(); error.value = '当前权限不允许读取或操作，已清除页面内容。' }
      else if (querying && status === 404) message.value = '暂未观察到原命令，不能判断未提交。继续查询或以原命令重试。'
      else if ([400, 404, 409, 412, 422, 428].includes(status)) { pending.value = undefined; form.value = undefined; selected.value = undefined; remember(); error.value = `配置未获准（${e?.response?.data?.code ?? status}），请重新读取后处理。` }
      else error.value = pending.value ? '提交结果尚未确认，请查询原命令或以原意图重试。' : '暂时无法读取审批绑定。'
    } catch { /* Identity changes cannot restore old content. */ }
  } finally { if (live && generation === request) busy.value = false }
}
function load(cursor: string | null = null) {
  if (locked.value) return
  return run(async (check, signal) => {
    const capabilities = await api.capabilities(signal); check()
    if (capabilities.schema !== 'oa.app/2.0' || !capabilities.bindingRead) { clear(); error.value = '当前没有审批绑定读取权限。'; return }
    const catalog = await api.options(signal); check(); const list = await api.list(cursor, signal); check()
    canConfigure.value = capabilities.bindingConfigure; options.value = catalog; rows.value = list.items; nextCursor.value = list.nextCursor
    selected.value = undefined; form.value = undefined; history.value = []; timeline.value = undefined
  })
}
const local = (utc: string) => { const d = new Date(utc); return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 19) }
function edit(row?: BindingView) {
  if (locked.value || !canConfigure.value) return
  selected.value = row; reason.value = ''; history.value = []; timeline.value = undefined
  form.value = row ? { commandId: '', bizType: row.bizType, subjectKind: row.subjectKind, selectionMode: row.selectionMode, flowKey: row.flowKey, flowDefVersionId: row.flowDefVersionId,
    effectiveFrom: '', effectiveTo: null, ruleMode: row.ruleMode, rules: structuredClone(JSON.parse(JSON.stringify(row.rules))), detailRoute: row.detailRoute, remark: row.remark }
    : { commandId: '', bizType: '', subjectKind: '', selectionMode: 'EXACT_PINNED', flowKey: '', flowDefVersionId: null, effectiveFrom: '', effectiveTo: null, ruleMode: 'EXCLUSIVE_MATCH', rules: [], detailRoute: null, remark: '' }
  typeIndex.value = row ? String(options.value?.businessTypes.findIndex(t => t.bizType === row.bizType && t.subjectKinds.includes(row.subjectKind)) ?? -1) : ''
  from.value = local(new Date(Math.ceil((Date.now() + 60000) / 1000) * 1000).toISOString()); until.value = ''
}
function chooseType() { const t = options.value?.businessTypes[Number(typeIndex.value)]; if (form.value && t) { form.value.bizType = t.bizType; form.value.subjectKind = t.subjectKinds[0] ?? '' } }
function selectionChanged() { if (form.value) { form.value.flowDefVersionId = null; form.value.rules.forEach(r => { r.flowDefVersionId = null }) } }
function addRule() { if (form.value && !locked.value) form.value.rules.push({ ruleId: crypto.randomUUID(), priority: form.value.rules.length, when: '', flowKey: '', flowDefVersionId: null }) }
function confirmed() { pending.value = undefined; remember(); form.value = undefined; selected.value = undefined; rows.value = []; history.value = []; timeline.value = undefined; message.value = '原命令已确认。请重新读取当前配置；后续命令可能已改变排程。' }
function retry() {
  if (!pending.value) return
  return run(async (check, signal) => {
    const intent = JSON.parse(JSON.stringify(pending.value)) as BindingIntent, result = await api.execute(intent, signal); check()
    if (result.commandId !== intent.command.commandId || result.state !== 'COMPLETED' || !result.result?.bindingId || (intent.bindingId && result.result.bindingId !== intent.bindingId)
      || intent.operation === 'SAVE' && ('bizType' in intent.command) && (result.result.bizType !== intent.command.bizType || result.result.subjectKind !== intent.command.subjectKind)) throw new Error('原结果不匹配')
    confirmed()
  })
}
function save() {
  if (locked.value || !form.value || !canConfigure.value || !form.value.bizType || !form.value.subjectKind || !form.value.flowKey || !from.value) return
  try {
    const body = JSON.parse(JSON.stringify(form.value)) as BindingWrite
    body.commandId = crypto.randomUUID(); body.effectiveFrom = new Date(from.value).toISOString(); body.effectiveTo = until.value ? new Date(until.value).toISOString() : null
    pending.value = { bindingId: selected.value?.bindingId ?? null, etag: selected.value ? `"${selected.value.rowVersion}"` : null, operation: 'SAVE', command: body }; remember(); return retry()
  } catch { error.value = '请输入有效生效时间。' }
}
function disable() {
  if (locked.value || !selected.value || !canConfigure.value || !reason.value.trim()) return
  pending.value = { bindingId: selected.value.bindingId, etag: `"${selected.value.rowVersion}"`, operation: 'DISABLE', command: { commandId: crypto.randomUUID(), reason: reason.value } }; remember(); return retry()
}
function recover() {
  if (!pending.value) return
  return run(async (check, signal) => {
    const intent = pending.value!, result = await api.original(intent.command.commandId, signal); check()
    if (result.commandId !== intent.command.commandId || result.targetType !== 'BINDING' || intent.bindingId && result.targetId !== intent.bindingId) throw new Error('原命令不匹配')
    if (result.state === 'COMPLETED' && result.resultRef?.owner === 'Main Workflow' && result.resultRef.id && result.resultRef.version) confirmed()
    else message.value = '原命令尚未完成，请保留原意图继续查询。'
  }, true)
}
function readHistory(row: BindingView, cursor: string | null = null) {
  if (locked.value) return
  return run(async (check, signal) => { const result = await api.history(row.bindingId, cursor, signal); check(); selected.value = row; history.value = result.items; historyCursor.value = result.nextCursor; form.value = undefined })
}
function readTimeline(row: BindingView) {
  if (locked.value) return
  return run(async (check, signal) => { const result = await api.timeline(row.bindingId, row.timelineRevisionId, signal); check(); timeline.value = result })
}
function identityChanged() { clear(); error.value = '登录身份已变化，请重新读取。' }
onMounted(() => {
  try { const saved = JSON.parse(sessionStorage.getItem(cache) ?? 'null'), intent = saved?.pending
    if (saved?.identity === identityStamp() && ['SAVE', 'DISABLE'].includes(intent?.operation) && typeof intent.command?.commandId === 'string'
      && (intent.bindingId === null || typeof intent.bindingId === 'string') && (intent.etag === null || typeof intent.etag === 'string')) { pending.value = intent; message.value = '有一次审批绑定命令待确认，请先查询原命令。'; return }
  } catch { /* Invalid saved data cannot be automatically submitted. */ }
  sessionStorage.removeItem(cache)
})
window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged)
onBeforeUnmount(() => { live = false; generation++; controller?.abort(); window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged) })
</script>
<template>
  <section class="binding" aria-label="审批业务绑定">
    <h3>审批业务绑定</h3><p>配置业务审批的流程选择和生效排程。审批发起尚未接入，保存配置不会启动审批。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="message" role="status">{{ message }}</p>
    <button :disabled="locked" @click="load()">读取绑定</button><button v-if="canConfigure" :disabled="locked || !options?.businessTypes.length" @click="edit()">新增绑定</button>
    <template v-if="pending"><button :disabled="busy" @click="recover">查询原命令</button><button :disabled="busy" @click="retry">以原意图重试</button></template>
    <p v-if="options && !options.businessTypes.length">尚无已登记的业务 Owner，请先完成该业务的可信登记。</p>
    <ul><li v-for="row in rows" :key="row.bindingId">{{ row.bizType }} / {{ row.subjectKind }} · {{ row.owner }} · {{ row.state }}
      <button v-if="canConfigure" :disabled="locked" @click="edit(row)">修订或停用</button><button :disabled="locked" @click="readHistory(row)">历史修订</button><button :disabled="locked" @click="readTimeline(row)">当前时间线</button>
    </li></ul><button v-if="nextCursor" :disabled="locked" @click="load(nextCursor)">下一页绑定</button>
    <div v-if="form">
      <label>业务类型<select v-model="typeIndex" :disabled="locked || !!selected" @change="chooseType"><option value="">请选择已登记业务</option><option v-for="(t, i) in options?.businessTypes" :key="i" :value="String(i)">{{ t.bizType }} / {{ t.subjectKinds.join(', ') }} · {{ t.owner }}</option></select></label>
      <p>Owner：{{ options?.businessTypes[Number(typeIndex)]?.owner ?? selected?.owner ?? '未选择' }}</p>
      <label>版本选择方式<select v-model="form.selectionMode" :disabled="locked" @change="selectionChanged"><option value="EXACT_PINNED">固定准确发布版本</option><option value="RESOLVE_PUBLISHED_ON_SUBMIT">在提交时解析发布版本</option></select></label>
      <label>默认流程<select v-model="form.flowKey" :disabled="locked" @change="form.flowDefVersionId = null"><option value="">请选择</option><option v-for="key in flowKeys" :key="key">{{ key }}</option></select></label>
      <label v-if="form.selectionMode === 'EXACT_PINNED'">准确发布版本<select v-model="form.flowDefVersionId" :disabled="locked"><option :value="null">请选择</option><option v-for="v in options?.flowVersions.filter(x => x.flowKey === form!.flowKey)" :key="v.versionId" :value="v.versionId" :disabled="!v.headEnabled">版本 {{ v.version }} · {{ v.headEnabled ? '已启用' : '未启用' }}</option></select></label>
      <label>生效时间（本地时间）<input v-model="from" type="datetime-local" step="1" :disabled="locked"></label><label>结束时间（留空表示持续生效）<input v-model="until" type="datetime-local" step="1" :disabled="locked"></label>
      <label>规则匹配<select v-model="form.ruleMode" :disabled="locked"><option value="EXCLUSIVE_MATCH">全部匹配检查，仅允许一条命中</option><option value="ORDERED_FIRST_MATCH">按优先级选择首条命中</option></select></label>
      <p>规则只读取已登记快照字段。未命中规则时采用默认流程；无效规则会阻止保存。</p>
      <fieldset v-for="(rule, i) in form.rules" :key="rule.ruleId" :disabled="locked"><legend>规则 {{ i + 1 }}</legend>
        <label>优先级<input v-model.number="rule.priority" type="number" min="0" max="9999"></label><label>匹配表达式<input v-model="rule.when" maxlength="4000"></label>
        <label>流程<select v-model="rule.flowKey" @change="rule.flowDefVersionId = null"><option value="">请选择</option><option v-for="key in flowKeys" :key="key">{{ key }}</option></select></label>
        <label v-if="form.selectionMode === 'EXACT_PINNED'">发布版本<select v-model="rule.flowDefVersionId"><option :value="null">请选择</option><option v-for="v in options?.flowVersions.filter(x => x.flowKey === rule.flowKey)" :key="v.versionId" :value="v.versionId" :disabled="!v.headEnabled">{{ v.version }}</option></select></label>
        <button @click="form.rules.splice(i, 1)">移除规则</button>
      </fieldset><button :disabled="locked" @click="addRule">增加规则</button>
      <label>业务详情站内路径<input v-model="form.detailRoute" maxlength="300" placeholder="/业务路径/{bizId}" :disabled="locked"></label><label>备注<textarea v-model="form.remark" maxlength="200" :disabled="locked" /></label>
      <p>发布修订会保留历史；已有未来切换不会被静默覆盖，发生冲突时需调整结束时间。</p><button :disabled="locked" @click="save">发布绑定修订</button>
      <div v-if="selected"><p>停用将取消以下未来排程，既有固定实例不变：</p><ul><li v-for="item in selected.scheduledIntervals" :key="item.from">{{ item.from }} · {{ item.revisionId ?? '停用区间' }}</li></ul>
        <label>停用原因<textarea v-model="reason" maxlength="500" :disabled="locked" /></label><button :disabled="locked || !reason.trim()" @click="disable">停用并取消未来排程</button>
      </div>
    </div>
    <ol><li v-for="row in history" :key="row.revisionId">修订 {{ row.revision }} · {{ row.createdAt }} · {{ row.flowKey }} · {{ row.remark }}<button :disabled="locked" @click="readTimeline(row)">查看该次时间线</button></li></ol>
    <button v-if="selected && historyCursor" :disabled="locked" @click="readHistory(selected, historyCursor)">下一页历史</button>
    <div v-if="timeline"><h4>时间线 {{ timeline.timelineNo }}</h4><p>{{ timeline.publishedAt }}</p><ol><li v-for="item in timeline.intervals" :key="item.from">{{ item.from }} — {{ item.to ?? '持续' }} · {{ item.state }} · {{ item.revisionId ?? '无启用修订' }}</li></ol></div>
  </section>
</template>
<style scoped>
.binding { padding: 16px; border: 1px solid #dbe3ed; border-radius: 8px; background: white; } button { margin: 4px; padding: 8px 12px; } label { display: grid; max-width: 680px; gap: 6px; margin: 12px 0; } textarea { min-height: 60px; } fieldset { margin: 12px 0; } [role=alert] { color: #a63727; } li { margin: 8px 0; }
</style>
