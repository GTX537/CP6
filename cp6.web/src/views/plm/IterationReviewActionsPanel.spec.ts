import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { ReviewDetail } from '@/api/plm/iterationReview'
import type { IssueCommand } from '@/api/plm/iterationReviewActions'
const api = vi.hoisted(() => ({ context: vi.fn(), issues: vi.fn(), issue: vi.fn(), apply: vi.fn(), withdraw: vi.fn(), businessGrants: vi.fn(), grantBusiness: vi.fn(), tasks: vi.fn(), decide: vi.fn(), operation: vi.fn(), decisionOperation: vi.fn(), reviewers: vi.fn(), scope: vi.fn(), list: vi.fn(), read: vi.fn(), authenticated: true, denied: [] as string[] }))
vi.mock('@/api/plm/iterationReviewActions', () => ({ iterationReviewActions: api }))
vi.mock('@/api/plm/iterationReview', () => ({ iterationReviewApi: api }))
vi.mock('@/api/plm/engineeringIteration', () => ({ engineeringIterationApi: api }))
vi.mock('@/api/plm/controlledDocument', () => ({ documentApi: api }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (key: string) => !api.denied.includes(key) }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => api.authenticated }))
import Panel from './IterationReviewActionsPanel.vue'
import { pendingIterationAction } from '@/auth/iterationReviewActionRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope = { environmentId: id(99), evidenceMode: 'DEMO' as const, tenantId: id(1), actorId: id(2) }, ok = <T,>(data: T) => ({ code: 0, data })
const candidate = { owner: 'Main.Plm.IterationCandidate.v1', id: id(8), version: '2', digest: 'a'.repeat(64) }
const detail = { submission: { submissionId: id(4), roundId: id(5) }, package: { iteration: { id: id(3) }, reviewers: [id(6)], candidates: [{ reference: candidate, original: { definition: { title: 'Exact native candidate' } } }] } } as unknown as ReviewDetail
const receipt = { receiptId: id(7), submissionId: id(4), producerOwner: 'Main.Plm.EngineeringIteration.v1', action: 'PLM_ITER_REVIEW_DECISION', application: 'BLOCKED', outcomeCode: 'BLOCKED_OPEN_ISSUES', receiptRevision: '1', subject: { subjectId: id(5) } }
let view: VueWrapper | undefined
const button = (s: string) => view!.findAll('button').find(v => v.text() === s)!
const field = (s: string) => view!.get(`[aria-label="${s}"]`)
async function click(s: string) { await button(s).trigger('click'); await flushPromises() }
async function start() { view = mount(Panel, { props: { iterationId: id(3), detail, submissions: [detail.submission], currentSubmission: id(4), scope } }); await flushPromises() }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); api.authenticated = true; api.denied = []
  api.scope.mockResolvedValue(ok(scope)); api.context.mockResolvedValue(ok({ iterationId: id(3), submissionId: id(4), etag: '"application-original"', businessStage: 'IN_REVIEW', controlRowVersion: 'AAAAAAAAAAE=', decision: 'APPROVED', receipt: null })); api.issues.mockResolvedValue(ok({ iterationId: id(3), etag: '"issues-original"', issues: [] })); api.tasks.mockResolvedValue([]); api.reviewers.mockResolvedValue(ok([{ id: id(2), name: 'Author' }, { id: id(6), name: 'Independent reviewer' }]))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_a: string, _b: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('creates exact round business authorization and retains only original recovery identifiers after a lost response', async () => {
  api.businessGrants.mockResolvedValue(ok([])); api.grantBusiness.mockRejectedValue(new Error('lost grant acknowledgement'))
  await start(); await field('业务受托人').setValue(id(6)); await field('业务委派生效时间').setValue('2026-11-02T10:00'); await field('业务委派结束时间').setValue('2026-11-03T10:00'); await field('业务委派原因').setValue('Private exact-round authorization')
  await button('保存业务授权').trigger('submit'); await flushPromises()
  expect(api.grantBusiness).toHaveBeenCalledTimes(1); expect(api.grantBusiness.mock.calls[0]?.[1]).toMatchObject({ submissionId: id(4), delegateId: id(6), fromUtc: '2026-11-02T10:00:00.000Z', actions: ['review-withdraw'], grantId: null })
  const pending = pendingIterationAction(scope)!; expect(pending.kind).toBe('BUSINESS_GRANT'); expect(JSON.stringify(localStorage)).not.toContain('Private exact-round authorization')
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'UNKNOWN' })); await click('查询原处理结果'); expect(pendingIterationAction(scope)).toEqual(pending); expect(api.grantBusiness).toHaveBeenCalledTimes(1)
})
it('requires explicit withdrawal reauthorization and links its new command to the exact blocked predecessor', async () => {
  api.context.mockResolvedValue(ok({ iterationId: id(3), submissionId: id(4), etag: '"current"', businessStage: 'IN_DESIGN', controlRowVersion: 'AAAAAAAAAAI=', decision: null, receipt: null,
    withdrawalAuthorizations: [{ commandId: id(30), previousCommandId: null, state: 'BLOCKED', actualHandlerId: id(6), requesterId: id(2), grantId: id(31), grantRevision: '1' }] }))
  api.businessGrants.mockResolvedValue(ok([{ grantId: id(32), grantorId: id(6), delegateId: scope.actorId, enabled: true, fromUtc: '2000-01-01T00:00:00Z', toUtc: '2100-01-01T00:00:00Z', actions: ['review-withdraw'] }]))
  api.withdraw.mockRejectedValue(new Error('lost new command')); await start(); expect(api.withdraw).not.toHaveBeenCalled(); await click('读取本轮业务委派'); await field('业务委托人').setValue(id(6)); await field('撤回重新授权原因').setValue('Current new authority')
  await button('以当前身份明确重新授权撤回').trigger('submit'); await flushPromises()
  expect(api.withdraw).toHaveBeenCalledTimes(1); expect(api.withdraw.mock.calls[0]?.slice(0, 3)).toEqual([id(3), id(4), 'AAAAAAAAAAI=']); expect(api.withdraw.mock.calls[0]?.slice(4)).toEqual([id(6), id(30)])
  expect(pendingIterationAction(scope)).toMatchObject({ kind: 'WITHDRAWAL', targetId: id(30) }); expect(view!.text()).not.toContain('实际操作人')
})
it('uses the separate explicit application authorization endpoint only after the user requests it', async () => {
  api.context.mockResolvedValue(ok({ iterationId: id(3), submissionId: id(4), etag: '"blocked"', businessStage: 'IN_REVIEW', decision: 'APPROVED', receipt }))
  api.apply.mockRejectedValue(new Error('unknown explicit authorization')); await start(); expect(api.apply).not.toHaveBeenCalled(); await field('迭代批准应用原因').setValue('Take current responsibility'); await click('以当前身份明确重新授权应用')
  expect(api.apply).toHaveBeenCalledTimes(1); expect(api.apply.mock.calls[0]?.[2]).toMatchObject({ expectedReceiptId: id(7), reason: 'Take current responsibility' }); expect(api.apply.mock.calls[0]?.[4]).toBe(true)
})
it('creates a blocking issue at the selected exact candidate and assigned project member', async () => {
  api.issue.mockImplementation(async (_id, _etag, c: IssueCommand) => ok({ operationId: c.operationId, iterationId: id(3), issue: { id: c.issueId, iterationId: id(3), submissionId: c.submissionId, revision: '1' } }))
  await start(); await click('登记评审问题'); await field('评审问题位置').setValue(id(8)); await field('候选字段路径').setValue('/definition/changeSummary'); await field('问题责任人').setValue(id(6)); await field('问题截止日期').setValue('2026-11-01'); await field('问题处理内容').setValue('Clarify source'); await field('问题处理原因').setValue('Independent review'); await button('保存问题处理').trigger('submit'); await flushPromises()
  expect(api.issue.mock.calls[0]?.slice(0, 2)).toEqual([id(3), '"issues-original"']); expect(api.issue.mock.calls[0]?.[2]).toMatchObject({ action: 'CREATE', target: { kind: 'CANDIDATE', candidate, path: '/definition/changeSummary' }, responsibleId: id(6), dueDate: '2026-11-01' }); expect(pendingIterationAction(scope)).toBeNull()
})
it('retains a lost application result and recovers BLOCKED without resubmission or false approval', async () => {
  api.apply.mockRejectedValueOnce(new Error('Connection lost')); await start(); await field('迭代批准应用原因').setValue('Apply exact round'); await button('应用本轮审批决定').trigger('submit'); await flushPromises(); const p = pendingIterationAction(scope)!
  api.operation.mockResolvedValueOnce(ok({ operationId: p.operationId, state: 'COMMITTED', result: { operationId: p.operationId, objectId: id(3), receipt } })); api.context.mockResolvedValue(ok({ iterationId: id(3), submissionId: id(4), businessStage: 'IN_REVIEW', decision: 'APPROVED', receipt })); await click('查询原处理结果')
  expect(api.apply).toHaveBeenCalledTimes(1); expect(pendingIterationAction(scope)).toBeNull(); expect(view!.text()).toContain('待解除阻塞'); expect(view!.text()).not.toContain('业务应用：已应用')
})
it('uses the actual control row version and only the human command/reason for withdrawal', async () => {
  api.withdraw.mockImplementation(async (_id, _submission, _version, c) => ok({ operationId: c.commandId, iterationId: id(3), state: 'WITHDRAWN', transportCommandId: id(11), appliedReceipt: null, fact: { ownerCommandId: c.commandId, owner: 'Main.Plm.EngineeringIteration.v1', submissionId: id(4), subject: { subjectId: id(5) }, scope, actor: { actualHandlerId: scope.actorId } } }))
  await start(); await field('迭代撤回原因').setValue('Correct exact package'); await button('撤回本轮评审').trigger('submit'); await flushPromises()
  expect(api.withdraw.mock.calls[0]?.slice(0, 3)).toEqual([id(3), id(4), 'AAAAAAAAAAE=']); expect(Object.keys(api.withdraw.mock.calls[0]?.[3]).sort()).toEqual(['commandId', 'reason']); expect(pendingIterationAction(scope)).toBeNull()
})
it('hides independent verification from the author or responder and respects write permissions', async () => {
  api.issues.mockResolvedValue(ok({ iterationId: id(3), etag: '"i"', issues: [{ id: id(20), submissionId: id(4), text: 'Private issue', severity: 'BLOCKING', state: 'RESPONDED', responsibleId: scope.actorId, authorId: id(6), target: { kind: 'PROCESS' }, responses: [{ id: id(21), actorId: scope.actorId, text: 'Response', evidence: [], correctiveNote: false }] }] })); api.denied = ['plm-engineering-iteration:review-withdraw', 'plm-engineering-iteration:review-apply']; await start()
  expect(view!.text()).not.toContain('独立验证关闭'); expect(view!.text()).not.toContain('撤回本轮评审'); expect(view!.text()).not.toContain('应用本轮审批决定')
})
it('discards late private content after identity changes', async () => {
  let release!: (v: unknown) => void; api.context.mockReturnValueOnce(new Promise(resolve => { release = resolve })); await start(); api.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT)); release(ok({ iterationId: id(3), submissionId: id(4), businessStage: 'IN_REVIEW', decision: 'APPROVED' })); await flushPromises()
  expect(view!.text()).not.toContain('当前业务阶段'); expect(api.issues).not.toHaveBeenCalled(); expect(api.apply).not.toHaveBeenCalled()
})
it('submits the original native task versions and explicit professional comment', async () => {
  const task = { taskId: id(25), instanceId: id(26), taskStatus: 0, instanceStatus: 0, taskRowVersion: 'task-v1', instanceRowVersion: 'instance-v1' }
  api.tasks.mockResolvedValue([{ submissionId: id(4), iterationId: id(3), task }]); api.decide.mockImplementation(async (_task, c) => ({ commandId: c.commandId, state: 'COMPLETED', result: { voteId: id(27), decisionId: null, task }, queryRef: `/api/oa/tasks/decision-commands/${c.commandId}` }))
  await start(); await field('迭代评审待办').setValue(id(25)); await field('迭代评审结论').setValue('reject'); await field('迭代专业意见').setValue('Exact technical gap identified'); await button('提交专业评审').trigger('submit'); await flushPromises()
  expect(api.decide).toHaveBeenCalledWith(id(25), expect.objectContaining({ decision: 'reject', comment: 'Exact technical gap identified', taskRowVersion: 'task-v1', instanceRowVersion: 'instance-v1' })); expect(pendingIterationAction(scope)).toBeNull()
})
it('re-reads the exact controlled document before attaching it to the original issue response', async () => {
  const issue = { id: id(20), iterationId: id(3), submissionId: id(4), revision: '9007199254740993', text: 'Clarify clearance', severity: 'BLOCKING', state: 'OPEN', responsibleId: scope.actorId, authorId: id(6), target: { kind: 'PROCESS' }, responses: [] }
  const document = { tenantId: scope.tenantId, reference: { owner: 'Main.Plm.ControlledDocument.v1', id: id(28), version: '4', digest: 'b'.repeat(64) }, body: { name: 'Exact source proof' }, state: 'APPROVED', readiness: 'CONTROLLED' }
  api.issues.mockResolvedValue(ok({ iterationId: id(3), etag: '"issues-original"', issues: [issue] })); api.list.mockResolvedValue(ok([document])); api.read.mockResolvedValue(ok(document)); api.issue.mockImplementation(async (_id, _etag, c) => ok({ operationId: c.operationId, iterationId: id(3), issue: { ...issue, revision: '9007199254740994' } }))
  await start(); await click('答复问题'); await click('查找证据文件'); await click('采用准确版本'); await field('问题处理内容').setValue('See approved evidence'); await field('问题处理原因').setValue('Answer original concern'); await button('保存问题处理').trigger('submit'); await flushPromises()
  expect(api.read).toHaveBeenCalledWith(id(28), '4'); expect(api.issue.mock.calls[0]?.[2]).toMatchObject({ action: 'RESPOND', issueId: id(20), expectedRevision: '9007199254740993', evidence: [document.reference] }); expect(pendingIterationAction(scope)).toBeNull()
})
