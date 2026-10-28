<template>
  <section class="professional-review" :aria-busy="busy">
    <header><h3>准确版本评审与批准</h3><button :disabled="busy || disabled" @click="reload">重新核对送审资料</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>原签署或批准操作尚未确认。</p><button :disabled="busy || disabled" @click="recover(false)">查询原批准操作</button><button :disabled="busy || disabled" @click="recover(true)">结束未提交批准操作</button></div>
    <template v-if="detail">
      <p><strong>{{ detail.document.body.name }} · 送审版本 {{ detail.document.reference.version }} · {{ detail.document.body.revisionLabel }}</strong></p>
      <p v-if="!detail.document.isCurrent">这是已冻结的历史送审版本；当前工作版本的修改不属于本次签署。</p>
      <p class="digest">内容摘要：{{ detail.document.reference.digest }}</p>
      <p>评审依据：{{ detail.package.reviewBasis }}</p>
      <ul><li v-for="(relation, index) in detail.package.declaredChangeRelations" :key="index">{{ relation }}</li></ul>
      <h4>来源与原件</h4>
      <dl><dt>原始权威</dt><dd>{{ detail.package.source.ownerName }} · {{ detail.package.source.organization }}</dd><dt>来源版本与日期</dt><dd>{{ detail.package.source.sourceVersion }} · {{ detail.package.source.sourceDate }}</dd><dt>取得依据</dt><dd>{{ detail.package.source.acquisitionBasis }}</dd><dt>权威语言与依据</dt><dd>{{ detail.package.source.authorityLanguage }} · {{ detail.package.source.authorityBasis }}</dd></dl>
      <article v-for="content in detail.package.contents" :key="content.occurrenceId">
        <p>{{ content.role }} · {{ content.language }} · {{ detail.document.contentStatus.find(x => x.occurrenceId === content.occurrenceId)?.fileName }}</p>
        <p v-if="content.derivedFromOccurrenceId">派生自：{{ content.derivedFromOccurrenceId }}；{{ content.derivationBasis }}</p>
        <button :disabled="busy || disabled || detail.document.contentStatus.find(x => x.occurrenceId === content.occurrenceId)?.availability !== 'AVAILABLE'" @click="download(content.occurrenceId)">下载本次评审原件</button>
      </article>
      <h4>前版差异</h4>
      <p v-if="!detail.previous">首次修订，没有前版。</p>
      <p v-else>前版 {{ detail.previous.reference.version }} → 本次 {{ detail.document.reference.version }}</p>
      <ul v-if="differences"><li v-for="change in differences.changes" :key="change.path">{{ change.path }}：{{ display(change.before) }} → {{ display(change.after) }}</li></ul>
      <h4>本次冻结的专业规则</h4>
      <ul><li v-for="item in detail.policy.fact.body.requirements" :key="item.code">{{ item.code }}：{{ item.description }}；专业角色 {{ item.roleId }}，至少 {{ item.minimumApprovals }} 人。</li></ul>
      <ul><li v-for="(item, index) in detail.policy.fact.body.externalRequirements" :key="index">{{ item }}</li></ul>
      <form v-if="canDecide && tasks.length" @submit.prevent="vote('approve')"><fieldset :disabled="locked">
        <h4>我的专业评审任务</h4><label>待办任务<select v-model="taskId" aria-label="文档专业评审任务"><option v-for="task in tasks" :key="task.task.taskId" :value="task.task.taskId">{{ task.task.taskId }}</option></select></label>
        <label>评审意见<textarea v-model="comment" aria-label="文档专业评审意见" maxlength="1000" required /></label>
        <p>意见和签署绑定上方准确版本。签署完成后由文档 Owner 应用最终决定。</p>
        <button :disabled="!taskId || !comment.trim() || Array.from(comment).length > 500">同意本次文档评审</button>
        <button type="button" :disabled="!taskId || !comment.trim() || Array.from(comment).length > 500" @click="vote('reject')">驳回本次文档评审</button>
      </fieldset></form>
      <p v-else-if="canDecide">当前没有属于我的本次评审待办。</p>
      <h4>文档 Owner 应用结果</h4>
      <p v-if="detail.application.application">{{ detail.application.application.approved ? '该准确版本已批准' : '该次评审已驳回' }} · {{ detail.application.application.appliedAt }}</p>
      <p v-else>{{ detail.application.decision ? `FLOW 最终决定：${detail.application.decision}；待文档 Owner 应用。` : '尚未收到 FLOW 最终决定。' }}</p>
      <form v-if="canApply && detail.application.decision && !detail.application.application" @submit.prevent="apply"><fieldset :disabled="locked">
        <label>应用原因<textarea v-model="reason" aria-label="文档批准应用原因" maxlength="4000" required /></label><button>应用文档最终决定</button>
      </fieldset></form>
    </template>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { documentApi } from '@/api/plm/controlledDocument'
import { documentReviewApi, type DocumentReviewTask, type ReviewDetail } from '@/api/plm/documentReview'
import type { EngineeringScope } from '@/api/plm/engineeringProducts'
import type { RevisionDiff } from '@/api/plm/engineeringRevisions'
import { documentResponse } from '@/auth/documentRecovery'
import { pendingDocumentApproval, resolveDocumentApproval, writeDocumentApplication, writeDocumentDecision, type PendingDocumentApproval } from '@/auth/documentApprovalRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'

const props = defineProps<{ documentId: string; submissionId: string; scope: EngineeringScope; disabled?: boolean }>()
const emit = defineEmits<{ changed: [] }>(), permissions = usePermissionStore()
const canRead = computed(() => permissions.loaded && permissions.has('plm-controlled-document:query'))
const canDecide = computed(() => canRead.value && permissions.has('plm-controlled-document:review-decide') && permissions.has('oa-inbox:approve'))
const canApply = computed(() => canRead.value && permissions.has('plm-controlled-document:review-apply'))
const detail = ref<ReviewDetail | null>(null), differences = ref<RevisionDiff | null>(null), tasks = ref<DocumentReviewTask[]>([])
const busy = ref(false), error = ref(''), notice = ref(''), taskId = ref(''), comment = ref(''), reason = ref('')
const pending = ref<PendingDocumentApproval | null>(null), locked = computed(() => busy.value || props.disabled || !!pending.value)
const urls = new Set<string>(); let generation = 0, live = true
const display = (value: unknown) => typeof value === 'string' ? value : JSON.stringify(value)
const same = (a: unknown, b: unknown) => JSON.stringify(a) === JSON.stringify(b)
function syncPending() { pending.value = pendingDocumentApproval(props.scope) }
function reset() {
  generation++; busy.value = false; detail.value = null; differences.value = null; tasks.value = []; pending.value = null
  error.value = notice.value = taskId.value = comment.value = reason.value = ''
  for (const url of urls) URL.revokeObjectURL(url); urls.clear()
}
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value || props.disabled || !canRead.value || !authenticationCanRead()) return
  const current = ++generation, stamp = identityStamp(), identity = [props.documentId, props.submissionId, props.scope.tenantId, props.scope.actorId].join('/')
  const check = () => { requireIdentity(stamp); if (!live || generation !== current || !canRead.value || !authenticationCanRead()
    || identity !== [props.documentId, props.submissionId, props.scope.tenantId, props.scope.actorId].join('/')) throw new Error('当前评审或身份已变化。') }
  busy.value = true; error.value = notice.value = ''
  try { check(); syncPending(); await action(check); check(); syncPending() }
  catch (e) { if (live && generation === current) { detail.value = differences.value = null; tasks.value = []; error.value = e instanceof Error ? e.message : '评审操作失败。'; try { syncPending() } catch { error.value = '批准原操作记录异常，请保留记录。' } } }
  finally { if (generation === current) busy.value = false }
}
async function load(check: () => void) {
  const value = documentResponse(await documentReviewApi.detail(props.documentId, props.submissionId)); check()
  if (value.document.tenantId !== props.scope.tenantId || value.submission.submissionId !== props.submissionId
    || value.document.reference.id !== props.documentId || !same(value.document.reference, value.submission.document)
    || !same(value.package.document, value.submission.document) || !same(value.policy.reference, value.submission.policy)
    || !same(value.package.previous, value.previous?.reference ?? null) || value.application.documentId !== props.documentId || value.application.submissionId !== props.submissionId)
    throw new Error('送审资料的准确来源不一致。')
  let diff: RevisionDiff | null = null
  if (value.previous) {
    diff = documentResponse(await documentApi.diff(props.documentId, value.previous.reference.version, value.document.reference.version)); check()
    if (!same(diff.before, value.previous.reference) || !same(diff.after, value.document.reference)) throw new Error('前版差异与本次送审不一致。')
  }
  const available = canDecide.value ? await documentReviewApi.tasks(props.documentId) : []; check()
  if (!Array.isArray(available) || available.some(x => x.documentId !== props.documentId)) throw new Error('专业评审任务来源不一致。')
  detail.value = value; differences.value = diff; tasks.value = available.filter(x => x.submissionId === props.submissionId && x.documentVersion === value.document.reference.version)
  taskId.value = tasks.value[0]?.task.taskId ?? ''
}
async function reload() { await run(load) }
async function vote(decision: 'approve' | 'reject') {
  const selected = tasks.value.find(x => x.task.taskId === taskId.value), message = comment.value
  if (locked.value || !canDecide.value || !selected || !message.trim() || Array.from(message).length > 500) return
  await run(async check => {
    const current = await documentReviewApi.task(selected.task.taskId); check()
    if (current.taskId !== selected.task.taskId || current.instanceId !== selected.task.instanceId) throw new Error('评审任务已变化。')
    await writeDocumentDecision(props.scope, props.documentId, props.submissionId, current.taskId, () => { check(); if (!canDecide.value) throw new Error('当前无专业评审权限。') },
      commandId => documentReviewApi.decide(current.taskId, { commandId, decision, comment: message, taskRowVersion: current.taskRowVersion, instanceRowVersion: current.instanceRowVersion }))
    check(); await load(check); comment.value = ''; notice.value = '本次专业意见已签署。'; emit('changed')
  })
}
async function apply() {
  const value = detail.value, message = reason.value
  if (!value || locked.value || !canApply.value || !message.trim()) return
  await run(async check => {
    await writeDocumentApplication(props.scope, props.documentId, props.submissionId, () => { check(); if (!canApply.value) throw new Error('当前无批准应用权限。') },
      operationId => documentReviewApi.apply(props.documentId, value.application.etag, { operationId, submissionId: props.submissionId, expectedReceiptId: value.application.receipt?.receiptId ?? null, reason: message }))
    check(); await load(check); reason.value = ''; notice.value = '文档最终决定已应用。'; emit('changed')
  })
}
async function recover(close: boolean) { await run(async check => { const state = await resolveDocumentApproval(props.scope, close, check); check(); await load(check); notice.value = state === 'UNKNOWN' ? '原结果仍未知，请继续查询。' : state === 'CLOSED' ? '未提交原操作已结束。' : '原签署或应用结果已恢复。'; emit('changed') }) }
async function download(occurrence: string) {
  const value = detail.value; if (!value) return
  await run(async check => {
    const member = value.package.contents.find(x => x.occurrenceId === occurrence); if (!member) throw new Error('缺少准确内容引用。')
    const blob = await documentApi.download(props.documentId, value.document.reference.version, occurrence); check()
    const bytes = await blob.arrayBuffer(); check()
    const digest = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes))).map(x => x.toString(16).padStart(2, '0')).join(''); check()
    if (bytes.byteLength !== member.file.byteLength || digest !== member.file.digest) throw new Error('下载内容与本次送审版本不一致。')
    const url = URL.createObjectURL(new Blob([bytes], { type: 'application/octet-stream' })); urls.add(url)
    const a = document.createElement('a'); a.href = url; a.download = value.document.contentStatus.find(x => x.occurrenceId === occurrence)?.fileName ?? 'document'; a.click()
    window.setTimeout(() => { URL.revokeObjectURL(url); urls.delete(url) }, 1000)
  })
}
function changed() { reset(); if (authenticationCanRead()) void reload() }
function storage(e: StorageEvent) { if (e.key?.startsWith('cp6_document_approval_operation_v1:')) { try { syncPending() } catch { reset(); error.value = '批准原操作记录异常，请保留记录。' } } }
watch(() => [props.documentId, props.submissionId, props.scope.tenantId, props.scope.actorId], changed)
watch(canRead, allowed => { if (!allowed) reset() })
watch(() => props.disabled, disabled => { if (!disabled && !detail.value && !error.value && !busy.value) void reload() })
onMounted(() => { window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', storage); void reload() })
onBeforeUnmount(() => { live = false; reset(); window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', storage) })
</script>
<style scoped>
.professional-review{padding:16px;margin-top:16px;border:1px solid var(--el-border-color);border-radius:8px}header{display:flex;justify-content:space-between;gap:12px;flex-wrap:wrap}label{display:grid;gap:6px;margin:12px 0}fieldset{border:0;padding:0}button,input,select,textarea{font:inherit;padding:8px;max-width:100%;box-sizing:border-box;color:inherit;background:var(--el-bg-color);border:1px solid var(--el-border-color);border-radius:4px}button{cursor:pointer;margin:4px}button:disabled{opacity:.5;cursor:default}.digest,dd,li{overflow-wrap:anywhere}dt{font-weight:600;margin-top:8px}dd{margin-left:0}article{padding:8px 0}[role=alert]{color:var(--el-color-danger)}:focus-visible{outline:2px solid var(--el-color-primary);outline-offset:2px}
</style>
