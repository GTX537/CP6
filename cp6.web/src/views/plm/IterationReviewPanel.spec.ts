import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { EngineeringIterationSnapshot } from '@/api/plm/engineeringIteration'
import type { CandidateSnapshot } from '@/api/plm/iterationCandidate'
import type { ReviewContext, ReviewResult, ReviewSubmitCommand } from '@/api/plm/iterationReview'
const api = vi.hoisted(() => ({ context: vi.fn(), roles: vi.fn(), reviewers: vi.fn(), policy: vi.fn(), submit: vi.fn(), detail: vi.fn(), operation: vi.fn(), scope: vi.fn(), authenticated: true, denied: [] as string[] }))
vi.mock('@/api/plm/iterationReview', () => ({ iterationReviewApi: api }))
vi.mock('@/api/plm/engineeringIteration', () => ({ engineeringIterationApi: { scope: api.scope } }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (key: string) => !api.denied.includes(key) }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => api.authenticated }))
import Panel from './IterationReviewPanel.vue'
import { pendingIterationReview } from '@/auth/iterationReviewRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const guid = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope = { environmentId: guid(99), evidenceMode: 'DEMO' as const, tenantId: guid(1), actorId: guid(2) }
const reference = { owner: 'Main.Plm.EngineeringIteration.v1', id: guid(3), version: '3', digest: 'a'.repeat(64) }
const doc = { tenantId: scope.tenantId, reference, etag: guid(4), isCurrent: true, body: { kind: 'ITERATION', name: 'Private engineering iteration', project: null, iteration: { stage: 'IN_DESIGN', holdState: 'NORMAL', inputVersion: '2' } } } as EngineeringIterationSnapshot
const policy = { reference: { owner: 'Main.Plm.IterationReviewPolicy.v1', id: guid(7), version: '1', digest: 'b'.repeat(64) }, fact: { recordedAt: '2026-10-28T00:00:00Z', body: { operationId: guid(8), state: 'ENABLED' as const, requirements: [{ code: 'TECHNICAL', description: 'Private independent review requirement', roleId: 73, minimumApprovals: 1 }], rejectDisposition: 'RETURN' as const, separateAuthorAndReviewer: true as const, effectiveFrom: '2026-10-27T00:00:00Z', effectiveTo: '2026-10-30T00:00:00Z', reason: 'Explicit policy' } } }
const candidates = Array.from({ length: 21 }, (_, i) => ({ reference: { owner: 'Main.Plm.IterationCandidate.v1', id: guid(100 + i), version: `${i + 1}`, digest: 'c'.repeat(64) }, isCurrent: true, original: { definition: { title: `方案 ${i + 1}`, changeSummary: 'Complete source difference', assumptions: 'Explicit limits', technicalSource: { state: 'PASS' }, technicalSourceOriginal: '{"exact":true}' }, previousChanges: [], parentChanges: [] }, findings: [], sourceDrift: 'UNCHANGED' } as unknown as CandidateSnapshot))
const initial: ReviewContext = { iterationId: reference.id, etag: '"ir1-original"', iteration: doc, candidates, policies: [policy], submissions: [] }
const ok = <T,>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined, context: ReviewContext, receipt: ReviewResult | undefined
const field = (name: string) => view!.get(`[aria-label="${name}"]`)
const button = (text: string) => view!.findAll('button').find(x => x.text() === text)!
async function click(text: string) { await button(text).trigger('click'); await flushPromises() }
async function start() { view = mount(Panel, { props: { iteration: doc, scope } }); await flushPromises() }
async function submit() { await click('读取项目评审成员'); await field('独立评审人').setValue([guid(5)]); await field('迭代评审依据').setValue('Check all exact candidate originals'); await field('迭代送审原因').setValue('Ready for independent review'); await button('提交迭代评审').trigger('submit'); await flushPromises() }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); api.authenticated = true; api.denied = []; context = structuredClone(initial); receipt = undefined
  api.scope.mockResolvedValue(ok(scope)); api.context.mockImplementation(async () => ok(structuredClone(context))); api.reviewers.mockResolvedValue(ok([{ id: scope.actorId, name: 'Author' }, { id: guid(5), name: 'Independent reviewer' }]))
  api.roles.mockResolvedValue(ok([{ roleId: 73, roleName: 'Engineering professional' }]))
  api.submit.mockImplementation(async (_id: string, _etag: string, input: ReviewSubmitCommand) => {
    const submission = { submissionId: guid(9), roundId: guid(10), iteration: input.iteration, policy: input.policy, package: { owner: 'Main.Plm.IterationReviewPackage.v1', id: guid(9), version: '1', digest: 'd'.repeat(64) }, snapshotDigest: 'e'.repeat(64), dispatchState: 'PENDING', businessStage: 'IN_REVIEW' as const, applicationState: 'NOT_RECEIVED' as const }
    receipt = { operationId: input.operationId, iterationId: input.iteration.id, kind: 'SUBMIT', reference: { owner: 'Main.Plm.IterationReviewSubmission.v1', id: guid(9), version: '1', digest: 'f'.repeat(64) }, submission }
    context.submissions = [submission]; context.iteration.body.iteration!.stage = 'IN_REVIEW'; return ok(receipt)
  })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('submits all 21 displayed exact candidate versions and assigned independent reviewers', async () => {
  await start(); await submit(); expect(api.submit).toHaveBeenCalledTimes(1)
  expect(api.submit.mock.calls[0]!.slice(0, 2)).toEqual([reference.id, initial.etag]); expect(api.submit.mock.calls[0]![2]).toMatchObject({ iteration: reference, inputVersion: '2', candidates: candidates.map(c => c.reference), reviewers: [guid(5)] })
  expect(view!.text()).toContain('业务批准尚未应用'); expect(view!.text()).toContain('已进入评审'); expect(view!.emitted('changed')).toHaveLength(1); expect(pendingIterationReview(scope)).toBeNull()
})
it('retains the exact lost-response key and recovers without resubmitting private contents', async () => {
  const save = api.submit.getMockImplementation()!; api.submit.mockImplementationOnce(async (...args) => { await save(...args); throw new Error('Connection lost') })
  await start(); await submit(); const pending = pendingIterationReview(scope)!; expect(pending.operationId).toBe(receipt!.operationId)
  expect(localStorage.getItem(`cp6_iteration_review_operation_v1:${scope.environmentId}:${scope.evidenceMode}:${scope.tenantId}:${scope.actorId}`)).not.toContain('Check all exact')
  api.operation.mockResolvedValueOnce(ok({ operationId: pending.operationId, state: 'UNKNOWN' })); await click('查询原评审操作'); expect(pendingIterationReview(scope)).toEqual(pending)
  api.operation.mockResolvedValueOnce(ok({ operationId: pending.operationId, state: 'COMMITTED', result: receipt })); await click('查询原评审操作'); expect(pendingIterationReview(scope)).toBeNull(); expect(api.submit).toHaveBeenCalledTimes(1)
})
it('rejects an apparent business-approved receipt and retains recovery', async () => {
  const save = api.submit.getMockImplementation()!; api.submit.mockImplementationOnce(async (...args) => { const v = await save(...args); return ok({ ...v.data, submission: { ...v.data.submission, applicationState: 'APPLIED' } }) })
  await start(); await submit(); expect(pendingIterationReview(scope)).not.toBeNull(); expect(view!.text()).toContain('返回结果与原评审操作不一致')
})
it.each(['source', 'stage', 'version'])('blocks submission when the displayed %s is no longer eligible', async issue => {
  if (issue === 'source') context.candidates[0]!.sourceDrift = 'CHANGED'
  if (issue === 'stage') context.iteration.body.iteration!.stage = 'IN_REVIEW'
  if (issue === 'version') context.iteration.reference.digest = 'f'.repeat(64)
  await start(); expect((button('提交迭代评审').element.closest('fieldset') as HTMLFieldSetElement).disabled).toBe(true); await button('提交迭代评审').trigger('submit'); expect(api.submit).not.toHaveBeenCalled()
})
it('clears private candidates and ignores late responses after identity changes', async () => {
  let release!: (value: ReturnType<typeof ok<ReviewContext>>) => void; api.context.mockReturnValueOnce(new Promise(resolve => { release = resolve }))
  await start(); api.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT)); release(ok(initial)); await flushPromises()
  expect(view!.text()).not.toContain('Private engineering'); expect(view!.text()).not.toContain('Complete source difference'); expect(api.submit).not.toHaveBeenCalled()
})
it('uses explicitly selected professional policy, dates and rejection disposition', async () => {
  await start(); await click('读取可用角色'); await field('要求编号 1').setValue('TECH'); await field('审查内容 1').setValue('Complete native sources'); await field('专业角色 1').setValue('73')
  await field('不通过处理').setValue('REJECT'); await field('评审规则生效时间').setValue('2026-10-28T09:00'); await field('评审规则截止时间').setValue('2026-10-29T09:00'); await field('评审规则登记原因').setValue('Professional policy')
  api.policy.mockImplementation(async (_id, _etag, input) => ok({ operationId: input.operationId, iterationId: reference.id, kind: 'POLICY', reference: policy.reference }))
  await button('保存评审规则').trigger('submit'); await flushPromises(); expect(api.policy.mock.calls[0]![2]).toMatchObject({ rejectDisposition: 'REJECT', separateAuthorAndReviewer: true, requirements: [{ code: 'TECH', description: 'Complete native sources', roleId: 73, minimumApprovals: 1 }] }); expect(pendingIterationReview(scope)).toBeNull()
})
it('respects independent write permissions', async () => { api.denied = ['plm-engineering-iteration:review-policy', 'plm-engineering-iteration:review-submit']; await start(); expect(view!.text()).not.toContain('保存评审规则'); expect(view!.text()).not.toContain('提交迭代评审') })
