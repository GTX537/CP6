<template>
  <section class="review-actions" :aria-busy="busy">
    <header><h3>本轮评审处理</h3><button :disabled="busy || disabled" @click="reload">刷新评审处理</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>有评审操作结果待确认，请先恢复。</p><button :disabled="busy || disabled" @click="recover(false)">查询原处理结果</button><button :disabled="busy || disabled" @click="recover(true)">结束未提交处理</button></div>
    <template v-if="application && issues">
      <p>当前业务阶段：{{ stage(application.businessStage) }} · 审批决定：{{ decision(application.decision) }}</p>
      <p>业务应用：{{ applicationState(application.receipt?.application) }}<span v-if="application.receipt"> · 回执版本 {{ application.receipt.receiptRevision }} · {{ outcome(application.receipt.outcomeCode) }}</span></p>
      <p v-if="application.withdrawalDelivery">审批撤回通知：{{ withdrawalState(application.withdrawalDelivery.state) }}</p>
      <form v-if="canDecide && tasks.length" @submit.prevent="vote"><fieldset :disabled="locked || !active">
        <h4>专业评审</h4><label>待办<select v-model="taskId" aria-label="迭代评审待办" required><option value="">请选择当前待办</option><option v-for="t in tasks" :key="t.task.taskId" :value="t.task.taskId">{{ taskState(t.task.taskStatus) }} · {{ t.task.taskId }}</option></select></label>
        <label>结论<select v-model="voteValue" aria-label="迭代评审结论"><option value="approve">通过</option><option value="reject">不通过</option></select></label>
        <label>专业意见<textarea v-model="comment" aria-label="迭代专业意见" required maxlength="8000" /></label><button>提交专业评审</button>
      </fieldset></form>
      <p v-else-if="canDecide">当前没有可处理的本轮评审待办。</p>
      <h4>问题清单</h4><p v-if="!issues.issues.length">尚无评审问题。</p>
      <article v-for="i in issues.issues" :key="i.id">
        <h5>{{ i.text }}</h5><p>{{ i.severity === 'BLOCKING' ? '阻塞问题' : '非阻塞问题' }} · {{ issueState(i.state) }} · 截止 {{ i.dueDate }}</p>
        <p>责任人：{{ person(i.responsibleId) }} · {{ i.target.kind === 'PROCESS' ? '评审流程问题' : `候选版本 ${i.target.candidate?.version} ${i.target.path || ''}` }}</p>
        <ul><li v-for="r in i.responses" :key="r.id">{{ r.correctiveNote ? '补充说明' : '答复' }}：{{ r.text }} · {{ person(r.actorId) }} · {{ r.at }}<span v-if="r.evidence.length"> · 受控证据 {{ r.evidence.length }} 项</span></li></ul>
        <button v-if="can('review-issue') && active && i.responsibleId === scope.actorId && i.state !== 'VERIFIED_CLOSED'" :disabled="locked" @click="editIssue(i, 'RESPOND')">答复问题</button>
        <button v-if="can('review-issue-verify') && active && independent(i)" :disabled="locked" @click="editIssue(i, 'VERIFY')">独立验证关闭</button>
        <button v-if="can('review-issue-degrade') && active && i.state !== 'VERIFIED_CLOSED' && i.severity === 'BLOCKING'" :disabled="locked" @click="editIssue(i, 'DOWNGRADE')">降为非阻塞</button>
        <button v-if="can('review-issue')" :disabled="locked" @click="editIssue(i, 'NOTE')">补充说明</button>
      </article>
      <button v-if="can('review-issue') && active" :disabled="locked" @click="newIssue">登记评审问题</button>
      <form v-if="issueAction" @submit.prevent="saveIssue"><fieldset :disabled="locked">
        <h4>{{ issueActionName }}</h4>
        <template v-if="issueAction === 'CREATE'">
          <label>问题位置<select v-model="candidateId" aria-label="评审问题位置"><option value="">评审流程</option><option v-for="c in detail.package.candidates" :key="c.reference.id" :value="c.reference.id">{{ c.original.definition.title }} · 版本 {{ c.reference.version }}</option></select></label>
          <label v-if="candidateId">候选内字段路径（留空表示整个候选）<input v-model="targetPath" aria-label="候选字段路径" maxlength="1000" placeholder="/definition/changeSummary" /></label>
          <label>责任人<select v-model="responsible" aria-label="问题责任人" required><option value="">请选择项目成员</option><option v-for="p in members" :key="p.id" :value="p.id">{{ p.name }}</option></select></label>
          <label>截止日期<input v-model="due" aria-label="问题截止日期" type="date" required /></label>
        </template>
        <label>内容<textarea v-model="issueText" aria-label="问题处理内容" required maxlength="8000" /></label>
        <label>原因<textarea v-model="issueReason" aria-label="问题处理原因" required maxlength="1000" /></label>
        <template v-if="issueAction === 'RESPOND' || issueAction === 'NOTE'">
          <label>查找受控证据<input v-model="documentSearch" aria-label="评审证据搜索" maxlength="256" /></label><button type="button" @click="findDocuments(1)">查找证据文件</button>
          <p v-if="documents.length">第 {{ documentPage }} 页</p><button v-if="documentPage > 1" type="button" @click="findDocuments(documentPage - 1)">上一页证据</button><button v-if="documents.length === 20" type="button" @click="findDocuments(documentPage + 1)">下一页证据</button>
          <div v-for="d in documents" :key="d.reference.id"><span>{{ d.body.name }} · 版本 {{ d.reference.version }} · {{ d.state === 'APPROVED' ? '已批准' : '未批准' }}</span><button type="button" :disabled="d.state !== 'APPROVED' || d.readiness !== 'CONTROLLED'" @click="addEvidence(d)">采用准确版本</button></div>
          <ul><li v-for="(d, index) in evidence" :key="d.reference.id">{{ d.name }} · 版本 {{ d.reference.version }}<button type="button" @click="evidence.splice(index, 1)">移除证据引用</button></li></ul>
        </template>
        <button>保存问题处理</button><button type="button" @click="issueAction = null">取消编辑</button>
      </fieldset></form>
      <form v-if="can('review-apply') && application.decision && !terminal" @submit.prevent="apply"><fieldset :disabled="locked"><label>应用原因<textarea v-model="applyReason" aria-label="迭代批准应用原因" required maxlength="1000" /></label><button>应用本轮审批决定</button></fieldset></form>
      <form v-if="can('review-withdraw') && active" @submit.prevent="withdraw"><fieldset :disabled="locked"><label>撤回原因<textarea v-model="withdrawReason" aria-label="迭代撤回原因" required maxlength="500" /></label><p>撤回后返回设计，本轮原件和问题记录继续保留。</p><button>撤回本轮评审</button></fieldset></form>
    </template>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { EngineeringIterationScope } from '@/api/plm/engineeringIteration'
import type { EngineeringReference } from '@/api/plm/engineeringProducts'
import { iterationReviewApi, type ReviewDetail, type ReviewSubmission } from '@/api/plm/iterationReview'
import { iterationReviewActions as api, type IterationApplication, type IssueContext, type IssueCommand, type ReviewIssue, type IterationTask, type ActionKind, type ActionResult } from '@/api/plm/iterationReviewActions'
import type { EngineeringEnvelope } from '@/api/plm/engineeringProducts'
import { documentApi, type DocumentSnapshot } from '@/api/plm/controlledDocument'
import { requirementSetResponse as response } from '@/auth/requirementSetRecovery'
import { pendingIterationAction, resolveIterationAction, writeIterationAction, writeIterationDecision, type PendingIterationAction } from '@/auth/iterationReviewActionRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const props = defineProps<{ iterationId: string; detail: ReviewDetail; submissions: ReviewSubmission[]; currentSubmission: string | null; scope: EngineeringIterationScope; disabled?: boolean }>()
const emit = defineEmits<{ changed: [] }>(), permission = usePermissionStore()
const can = (action: string) => permission.loaded && permission.has('plm-engineering-iteration:query') && permission.has(`plm-engineering-iteration:${action}`)
const canDecide = computed(() => can('review-decide') && permission.has('oa-inbox:query') && permission.has('oa-inbox:approve'))
const busy = ref(false), error = ref(''), notice = ref(''), application = ref<IterationApplication | null>(null), issues = ref<IssueContext | null>(null), pending = ref<PendingIterationAction | null>(null)
const tasks = ref<IterationTask[]>([]), taskId = ref(''), voteValue = ref<'approve' | 'reject'>('approve'), comment = ref(''), applyReason = ref(''), withdrawReason = ref('')
const members = ref<{ id: string; name: string }[]>([]), issueAction = ref<IssueCommand['action'] | null>(null), selectedIssue = ref<ReviewIssue | null>(null)
const candidateId = ref(''), targetPath = ref(''), responsible = ref(''), due = ref(''), issueText = ref(''), issueReason = ref('')
const documents = ref<DocumentSnapshot[]>([]), documentSearch = ref(''), documentPage = ref(1), evidence = ref<{ reference: EngineeringReference; name: string }[]>([])
const locked = computed(() => busy.value || props.disabled || !!pending.value)
const active = computed(() => props.currentSubmission === props.detail.submission.submissionId && application.value?.businessStage === 'IN_REVIEW')
const terminal = computed(() => ['APPLIED', 'STALE', 'CONFLICT'].includes(application.value?.receipt?.application ?? ''))
const issueActionName = computed(() => ({ CREATE: '登记阻塞问题', RESPOND: '答复问题', VERIFY: '独立验证关闭', DOWNGRADE: '降为非阻塞', NOTE: '补充说明' } as Record<string, string>)[issueAction.value ?? ''])
const stage = (v: string) => ({ IN_DESIGN: '设计中', IN_REVIEW: '评审中', REVIEW_APPROVED: '评审批准', AWAITING_EVALUATION: '待重新评估' } as Record<string, string>)[v] ?? v
const decision = (v: string | null) => ({ APPROVED: '通过', REJECTED: '不通过', WITHDRAWN: '已撤回' } as Record<string, string>)[v ?? ''] ?? '尚未收到'
const applicationState = (v?: string) => ({ APPLIED: '已应用', BLOCKED: '待解除阻塞', STALE: '原轮次已失效', CONFLICT: '冲突' } as Record<string, string>)[v ?? ''] ?? '尚未应用'
const outcome = (v: string) => ({ ITERATION_APPROVED: '迭代评审批准', ITERATION_REJECTED: '已按本轮规则处理不通过', BLOCKED_OPEN_ISSUES: '仍有未独立关闭的阻塞问题', STALE_RECEIPT: '保留原轮次结果，当前轮次不变' } as Record<string, string>)[v] ?? v
const issueState = (v: string) => ({ OPEN: '待答复', RESPONDED: '待独立验证', VERIFIED_CLOSED: '已验证关闭' } as Record<string, string>)[v] ?? v
const withdrawalState = (v: string) => ({ PENDING: '待投递', OUTCOME_UNKNOWN: '结果待确认', BLOCKED: '待恢复原人权限或审批状态', WITHDRAWN: '审批已撤回', TERMINAL_CONFLICT: '审批已有终态，保留原决定', CONFLICT: '原件冲突，需核查' } as Record<string, string>)[v] ?? v
const taskState = (v: number) => v === 0 ? '待评审' : v === 4 ? '已挂起' : '已处理'
const person = (id: string) => members.value.find(v => v.id === id)?.name ?? id
const copy = <T,>(v: T): T => JSON.parse(JSON.stringify(v))
const independent = (i: ReviewIssue) => i.state !== 'VERIFIED_CLOSED' && i.responses.some(r => !r.correctiveNote) && i.authorId !== props.scope.actorId && i.responsibleId !== props.scope.actorId && !i.responses.some(r => !r.correctiveNote && r.actorId === props.scope.actorId) && props.detail.package.reviewers.includes(props.scope.actorId)
let generation = 0, live = true
function syncPending() { pending.value = pendingIterationAction(props.scope) }
function clear() { generation++; busy.value = false; application.value = null; issues.value = null; tasks.value = []; members.value = []; pending.value = null; documents.value = []; evidence.value = []; issueAction.value = null; selectedIssue.value = null; taskId.value = comment.value = applyReason.value = withdrawReason.value = candidateId.value = targetPath.value = responsible.value = due.value = issueText.value = issueReason.value = documentSearch.value = error.value = notice.value = '' }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value || props.disabled || !can('query') || !authenticationCanRead()) return
  const current = ++generation, stamp = identityStamp(), identity = JSON.stringify(props.scope), id = props.iterationId, submission = props.detail.submission.submissionId
  const check = () => { requireIdentity(stamp); if (!live || current !== generation || props.disabled || !can('query') || !authenticationCanRead() || JSON.stringify(props.scope) !== identity || props.iterationId !== id || props.detail.submission.submissionId !== submission) throw new Error('当前身份或评审轮次已变化。') }
  busy.value = true; error.value = notice.value = ''
  try { check(); syncPending(); await action(check); check(); syncPending() }
  catch (e) { if (live && current === generation) { application.value = null; issues.value = null; tasks.value = []; documents.value = []; evidence.value = []; members.value = []; error.value = e instanceof Error ? e.message : '评审处理失败。'; try { syncPending() } catch { error.value = '原操作记录异常，请保留记录。' } } }
  finally { if (current === generation) busy.value = false }
}
async function load(check: () => void) {
  const a = response(await api.context(props.iterationId, props.detail.submission.submissionId)); check()
  const i = response(await api.issues(props.iterationId)); check()
  if (a.iterationId !== props.iterationId || a.submissionId !== props.detail.submission.submissionId || i.iterationId !== props.iterationId) throw new Error('返回内容与当前轮次不一致。')
  const people = response(await iterationReviewApi.reviewers(props.iterationId)); check()
  const t = canDecide.value ? await api.tasks(props.detail.submission.submissionId) : []; check()
  if (t.some(v => v.iterationId !== props.iterationId || v.submissionId !== props.detail.submission.submissionId)) throw new Error('待办与当前轮次不一致。')
  application.value = a; issues.value = i; tasks.value = t.filter(v => v.task.taskStatus === 0 || v.task.taskStatus === 4); members.value = people; taskId.value = ''
}
async function reload() { await run(load) }
async function recover(close: boolean) { await run(async check => { const r = await resolveIterationAction(props.scope, close, check); check(); await load(check); notice.value = r.state === 'UNKNOWN' ? '原结果仍未知，请继续查询。' : r.state === 'CLOSED' ? '未提交处理已结束。' : '已读取原处理结果。'; if (r.state === 'COMMITTED' || r.state === 'COMPLETED') emit('changed') }) }
async function write(kind: ActionKind, action: string, targetId: string | null, submission: ReviewSubmission, call: (id: string) => Promise<EngineeringEnvelope<ActionResult>>, success: string) {
  if (locked.value || !can(action)) return
  await run(async check => { const r = await writeIterationAction(props.scope, { kind, objectId: props.iterationId, submissionId: submission.submissionId, roundId: submission.roundId, targetId }, () => { check(); if (!can(action)) throw new Error('当前操作权限已变化。') }, call); check(); issueAction.value = null; await load(check); notice.value = success; if (kind === 'APPLICATION' && r.result && 'receipt' in r.result) notice.value = applicationState(r.result.receipt.application); if (kind === 'WITHDRAWAL' && r.result && 'state' in r.result && r.result.state === 'CONFLICT') notice.value = '本轮已经应用，撤回未执行。'; emit('changed') })
}
async function vote() {
  if (locked.value || !active.value || !canDecide.value || !comment.value.trim()) return
  const selected = tasks.value.find(t => t.task.taskId === taskId.value); if (!selected) return
  const body = { decision: voteValue.value, comment: comment.value, taskRowVersion: selected.task.taskRowVersion, instanceRowVersion: selected.task.instanceRowVersion }
  await run(async check => { await writeIterationDecision(props.scope, { kind: 'DECISION', objectId: props.iterationId, submissionId: props.detail.submission.submissionId, roundId: props.detail.submission.roundId, targetId: selected.task.taskId }, () => { check(); if (!canDecide.value) throw new Error('评审权限已变化。') }, commandId => api.decide(selected.task.taskId, { ...body, commandId })); check(); await load(check); comment.value = ''; notice.value = '专业意见已记录，业务应用状态以回执为准。'; emit('changed') })
}
async function apply() { const a = application.value; if (!a || terminal.value || !applyReason.value.trim()) return; await write('APPLICATION', 'review-apply', null, props.detail.submission, operationId => api.apply(props.iterationId, a.etag, { operationId, submissionId: a.submissionId, expectedReceiptId: a.receipt?.receiptId ?? null, reason: applyReason.value }), '审批决定已处理。') }
async function withdraw() { const a = application.value; if (!a?.controlRowVersion || !active.value || !withdrawReason.value.trim()) return; await write('WITHDRAWAL', 'review-withdraw', null, props.detail.submission, commandId => api.withdraw(props.iterationId, a.submissionId, a.controlRowVersion!, { commandId, reason: withdrawReason.value }), '本轮已撤回，审批撤回状态等待投递确认。') }
function newIssue() { if (locked.value || !active.value) return; selectedIssue.value = null; issueAction.value = 'CREATE'; issueText.value = issueReason.value = candidateId.value = targetPath.value = responsible.value = due.value = ''; evidence.value = []; documents.value = [] }
function editIssue(i: ReviewIssue, action: IssueCommand['action']) { if (locked.value) return; selectedIssue.value = copy(i); issueAction.value = action; issueText.value = issueReason.value = ''; evidence.value = []; documents.value = [] }
async function saveIssue() {
  const i = selectedIssue.value, action = issueAction.value, c = issues.value; if (!action || !c || !issueText.value.trim() || !issueReason.value.trim()) return
  const submission = i ? props.submissions.find(s => s.submissionId === i.submissionId) : props.detail.submission; if (!submission) return
  const candidate = props.detail.package.candidates.find(v => v.reference.id === candidateId.value)
  if (action === 'CREATE' && (!members.value.some(m => m.id === responsible.value) || !due.value || candidateId.value && !candidate)) return
  const issueId = i?.id ?? crypto.randomUUID(), target = candidate ? { kind: 'CANDIDATE' as const, candidate: copy(candidate.reference), path: targetPath.value } : { kind: 'PROCESS' as const, candidate: null, path: null }
  const input = { submissionId: submission.submissionId, issueId, action, expectedRevision: i?.revision ?? null, text: issueText.value, target: action === 'CREATE' ? target : null, responsibleId: action === 'CREATE' ? responsible.value : null, dueDate: action === 'CREATE' ? due.value : null, evidence: action === 'RESPOND' || action === 'NOTE' ? copy(evidence.value.map(v => v.reference)) : null, reason: issueReason.value }
  await write('ISSUE', action === 'VERIFY' ? 'review-issue-verify' : action === 'DOWNGRADE' ? 'review-issue-degrade' : 'review-issue', issueId, submission, operationId => api.issue(props.iterationId, c.etag, { ...input, operationId }), '问题处理已保存。')
}
async function findDocuments(page: number) { await run(async check => { const list = response(await documentApi.list(undefined, documentSearch.value, page)); check(); if (list.some(v => v.tenantId !== props.scope.tenantId)) throw new Error('证据与当前租户不一致。'); documents.value = list; documentPage.value = page }) }
async function addEvidence(d: DocumentSnapshot) { await run(async check => { const exact = response(await documentApi.read(d.reference.id, d.reference.version)); check(); if (exact.tenantId !== props.scope.tenantId || JSON.stringify(exact.reference) !== JSON.stringify(d.reference) || exact.state !== 'APPROVED' || exact.readiness !== 'CONTROLLED') throw new Error('证据已变化，请重新读取。'); if (!evidence.value.some(v => v.reference.id === exact.reference.id)) evidence.value.push({ reference: copy(exact.reference), name: exact.body.name }) }) }
function changed() { clear(); if (authenticationCanRead()) void reload() }
function storage(e: StorageEvent) { if (e.key?.startsWith('cp6_iteration_review_action_v1:')) { try { syncPending() } catch { clear(); error.value = '原操作记录异常，请保留记录。' } } }
watch(() => [props.iterationId, props.detail.submission.submissionId, JSON.stringify(props.scope)], changed)
watch(() => can('query'), allowed => { if (!allowed) clear() })
watch(() => props.disabled, disabled => { if (!disabled && !application.value && !busy.value && !error.value) void reload() })
onMounted(() => { window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', storage); void reload() })
onBeforeUnmount(() => { live = false; clear(); window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', storage) })
</script>
<style scoped>
.review-actions{margin-top:16px;border-top:1px solid var(--el-border-color);padding-top:12px}header{display:flex;justify-content:space-between;gap:12px;flex-wrap:wrap}article{border:1px solid var(--el-border-color);padding:12px;margin:12px 0;border-radius:6px}h5{font-size:inherit}fieldset{border:0;padding:0}label{display:grid;gap:6px;margin:10px 0}input,select,textarea,button{font:inherit;padding:8px;border:1px solid var(--el-border-color);border-radius:4px;background:var(--el-bg-color);color:inherit;max-width:100%;box-sizing:border-box}button{margin:4px;cursor:pointer}button:disabled{opacity:.5;cursor:default}[role=alert]{color:var(--el-color-danger)}:focus-visible{outline:2px solid var(--el-color-primary);outline-offset:2px}
</style>
