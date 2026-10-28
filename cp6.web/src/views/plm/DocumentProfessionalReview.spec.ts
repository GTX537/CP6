import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { ApplicationCommand, ApplicationResult, NativeReviewCommand, NativeReviewResult, ReviewDetail } from '@/api/plm/documentReview'
const api = vi.hoisted(() => ({ detail: vi.fn(), tasks: vi.fn(), task: vi.fn(), decide: vi.fn(), apply: vi.fn(), applicationOperation: vi.fn(), decisionOperation: vi.fn(), scope: vi.fn(), diff: vi.fn(), download: vi.fn(), authenticated: true, denied: [] as string[] }))
vi.mock('@/api/plm/documentReview', () => ({ documentReviewApi: api }))
vi.mock('@/api/plm/controlledDocument', () => ({ documentApi: { scope: api.scope, diff: api.diff, download: api.download } }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (key: string) => !api.denied.includes(key) }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => api.authenticated }))
import Review from './DocumentProfessionalReview.vue'
import { pendingDocumentApproval } from '@/auth/documentApprovalRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope = { tenantId: id(1), actorId: id(2) }, documentId = id(3), submissionId = id(4), taskId = id(5)
const reference = { owner: 'Main.Plm.ControlledDocument.v1', id: documentId, version: '2', digest: 'a'.repeat(64) }
const previous = { ...reference, version: '1', digest: 'b'.repeat(64) }
const policy = { reference: { ...reference, owner: 'Main.Plm.DocumentReviewPolicy.v1', id: id(6), version: '1' }, fact: { actorId: id(7), recordedAt: '2026-10-27T00:00:00.000Z', body: { operationId: id(8), state: 'ENABLED' as const, requirements: [{ code: 'CONTENT', description: 'Private professional requirement', roleId: 73, minimumApprovals: 1 }], externalRequirements: ['Original external requirement'], separateAuthorAndReviewer: true, effectiveFrom: '2026-10-27T00:00:00.000Z', effectiveTo: '2026-10-30T00:00:00.000Z', reason: 'Explicit policy' } } }
const body = { name: 'Frozen private drawing', revisionLabel: 'B', revisionOrigin: { ...reference, owner: 'Main.Plm.EngineeringRevision.v1', id: id(9) }, productRef: { ...reference, owner: 'Main.Plm.EngineeringProduct.v1', id: id(10) }, source: { kind: 'EXTERNAL' as const, ownerName: 'Original authority', organization: 'Source organization', sourceVersion: 'A', sourceDate: '2026-10-27', acquisitionBasis: 'Actual source', authorityLanguage: 'zh-CN', authorityBasis: 'Qualified language basis' }, contents: [] }
const doc = { tenantId: scope.tenantId, reference, etag: id(11), isCurrent: false, body, action: 'REVISE', reason: 'Original change', parentVersion: '1', actorId: id(7), recordedAt: '2026-10-27T00:00:00.000Z', originalJson: '{}', missing: [], contentStatus: [], state: 'DRAFT' as const, readiness: 'REVIEW_REQUIRED' as const }
const initial: ReviewDetail = { document: doc, previous: { ...doc, reference: previous }, policy,
  submission: { submissionId, document: reference, policy: policy.reference, starterId: id(7), frozenAt: '2026-10-27T00:00:00.000Z', snapshotDigest: 'c'.repeat(64), state: 'SUBMITTED', transportState: 'RECEIVED', isApproved: false },
  package: { document: reference, previous, changeDigest: 'd'.repeat(64), reviewBasis: 'Exact original review basis', declaredChangeRelations: ['Changed source drawing'], source: body.source, contents: [] },
  application: { documentId, submissionId, etag: '"application-original"', state: 'NOT_RECEIVED', receiptStreamId: null, decisionId: null, decision: null, receipt: null, application: null } }
const nativeTask = { taskId, instanceId: id(12), taskStatus: 0, instanceStatus: 0, taskRowVersion: 'AAAAAAAAAAE=', instanceRowVersion: 'AAAAAAAAAAI=' }
const ok = <T,>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined, detail: ReviewDetail, signature: NativeReviewResult | undefined, application: ApplicationResult | undefined
const button = (text: string) => view!.findAll('button').find(x => x.text() === text)!
async function click(text: string) { await button(text).trigger('click'); await flushPromises() }
async function start() { view = mount(Review, { props: { documentId, submissionId, scope } }); await flushPromises() }
async function vote() { await view!.get('[aria-label="文档专业评审意见"]').setValue('Private signed professional opinion'); await button('同意本次文档评审').trigger('submit'); await flushPromises() }
async function apply() { await view!.get('[aria-label="文档批准应用原因"]').setValue('Exact signed decision'); await button('应用文档最终决定').trigger('submit'); await flushPromises() }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); api.authenticated = true; api.denied = []; detail = structuredClone(initial); signature = application = undefined
  api.scope.mockResolvedValue(ok(scope)); api.detail.mockImplementation(async () => ok(structuredClone(detail)))
  api.diff.mockResolvedValue(ok({ before: previous, after: reference, changes: [{ path: 'revisionLabel', before: 'A', after: 'B' }] }))
  api.tasks.mockResolvedValue([{ documentId, submissionId, documentVersion: '2', task: nativeTask }]); api.task.mockResolvedValue(nativeTask)
  api.decide.mockImplementation(async (_task: string, input: NativeReviewCommand) => {
    signature = { commandId: input.commandId, state: 'COMPLETED', result: { voteId: id(13), decisionId: null, task: { ...nativeTask, taskStatus: 1 } }, queryRef: `/api/oa/tasks/decision-commands/${input.commandId}` }
    api.tasks.mockResolvedValue([]); return signature
  })
  api.apply.mockImplementation(async (_id: string, _etag: string, input: ApplicationCommand) => {
    const receipt = { receiptId: id(14), receiptStreamId: id(15), submissionId, decisionId: id(16), producerOwner: reference.owner, subject: { subjectId: documentId, businessRevision: '2' }, application: 'APPLIED', outcomeCode: 'DOCUMENT_APPROVED', businessResultRef: { owner: 'Main.Plm.DocumentApproval.v1', id: id(17), version: '1' } }
    application = { operationId: input.operationId, objectId: documentId, receipt }
    detail.application.application = { id: id(17), document: reference, policy: policy.reference, submissionId, decisionId: id(16), approved: true, actorId: scope.actorId, appliedAt: '2026-10-27T00:00:00.000Z' }
    return ok(application)
  })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('shows the frozen historical version and exact preceding difference without claiming applied approval', async () => {
  await start(); expect(view!.text()).toContain('送审版本 2'); expect(view!.text()).toContain('已冻结的历史送审版本')
  expect(view!.text()).toContain('Exact original review basis'); expect(view!.text()).toContain('Original external requirement')
  expect(api.diff).toHaveBeenCalledWith(documentId, '1', '2'); expect(view!.text()).not.toContain('该准确版本已批准')
})
it('signs the actual native task with current row versions and keeps business approval separate', async () => {
  await start(); await vote(); expect(api.decide).toHaveBeenCalledTimes(1)
  expect(api.decide.mock.calls[0]).toEqual([taskId, expect.objectContaining({ taskRowVersion: nativeTask.taskRowVersion, instanceRowVersion: nativeTask.instanceRowVersion, comment: 'Private signed professional opinion', decision: 'approve' })])
  expect(pendingDocumentApproval(scope)).toBeNull(); expect(view!.text()).toContain('本次专业意见已签署'); expect(api.apply).not.toHaveBeenCalled()
})
it('recovers a lost signed response using the original command without persisting private comments or voting again', async () => {
  const save = api.decide.getMockImplementation()!; api.decide.mockImplementationOnce(async (...args) => { await save(...args); throw new Error('Lost response') })
  await start(); await vote(); const pending = pendingDocumentApproval(scope)!; expect(pending.operationId).toBe(signature!.commandId)
  expect(localStorage.getItem(`cp6_document_approval_operation_v1:${scope.tenantId}:${scope.actorId}`)).not.toContain('Private signed')
  api.decisionOperation.mockResolvedValue({ commandId: pending.operationId, state: 'COMPLETED', result: signature }); await click('查询原批准操作')
  expect(api.decisionOperation).toHaveBeenCalledWith(pending.operationId, taskId, false); expect(pendingDocumentApproval(scope)).toBeNull(); expect(api.decide).toHaveBeenCalledTimes(1)
})
it('retains an unknown signing command until the native closed receipt confirms it', async () => {
  api.decide.mockRejectedValue(new Error('Unknown')); await start(); await vote(); const pending = pendingDocumentApproval(scope)!
  api.decisionOperation.mockResolvedValueOnce({ commandId: pending.operationId, state: 'UNKNOWN' }); await click('查询原批准操作'); expect(pendingDocumentApproval(scope)).toEqual(pending)
  api.decisionOperation.mockResolvedValueOnce({ commandId: pending.operationId, state: 'CLOSED' }); await click('结束未提交批准操作'); expect(pendingDocumentApproval(scope)).toBeNull()
})
it('applies only the selected submission and recovers its lost result without repeating the effect', async () => {
  detail.application.decision = 'APPROVED'; detail.application.state = 'RECEIVED'
  const save = api.apply.getMockImplementation()!; api.apply.mockImplementationOnce(async (...args) => { await save(...args); throw new Error('Lost application response') })
  await start(); await apply(); const pending = pendingDocumentApproval(scope)!
  expect(api.apply.mock.calls[0]!.slice(0, 2)).toEqual([documentId, initial.application.etag]); expect(api.apply.mock.calls[0]![2]).toMatchObject({ submissionId, expectedReceiptId: null })
  api.applicationOperation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'COMMITTED', result: application })); await click('查询原批准操作')
  expect(pendingDocumentApproval(scope)).toBeNull(); expect(api.apply).toHaveBeenCalledTimes(1); expect(view!.text()).toContain('该准确版本已批准')
})
it('retains the original key when a result names a different task or document', async () => {
  const save = api.decide.getMockImplementation()!; api.decide.mockImplementationOnce(async (...args) => { const result = await save(...args); return { ...result, result: { ...result.result, task: { ...result.result.task, taskId: id(99) } } } })
  await start(); await vote(); expect(pendingDocumentApproval(scope)).not.toBeNull(); expect(view!.text()).toContain('返回结果与原专业评审不一致')
})
it('rejects a mismatched frozen package before exposing its private review material', async () => {
  detail.package.document = { ...detail.package.document, digest: 'f'.repeat(64) }; await start(); expect(view!.text()).toContain('送审资料的准确来源不一致'); expect(view!.text()).not.toContain('Frozen private drawing'); expect(api.tasks).not.toHaveBeenCalled()
})
it('clears private material and ignores a late result after identity changes', async () => {
  let release!: (value: ReturnType<typeof ok<ReviewDetail>>) => void; api.detail.mockReturnValueOnce(new Promise(resolve => { release = resolve }))
  await start(); api.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT)); release(ok(initial)); await flushPromises()
  expect(view!.text()).not.toContain('Frozen private drawing'); expect(api.decide).not.toHaveBeenCalled()
})
it('keeps review and application actions behind their independent current permissions', async () => {
  api.denied = ['plm-controlled-document:review-decide', 'plm-controlled-document:review-apply']; detail.application.decision = 'APPROVED'; await start()
  expect(api.tasks).not.toHaveBeenCalled(); expect(view!.text()).not.toContain('同意本次文档评审'); expect(view!.text()).not.toContain('应用文档最终决定')
})
