import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { LocationRuleReviewSource } from '@/api/wms/locationRuleReview'
import type { ReviewContext, ReviewResult, ReviewSubmitCommand } from '@/api/wms/locationRuleReview'
const api = vi.hoisted(() => ({ context: vi.fn(), roles: vi.fn(), policy: vi.fn(), submit: vi.fn(), operation: vi.fn(), scope: vi.fn(), authenticated: true, denied: [] as string[] }))
vi.mock('@/api/wms/locationRuleReview', () => ({ locationRuleReviewApi: api }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (key: string) => !api.denied.includes(key) }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => api.authenticated }))
import Panel from './LocationRuleReviewPanel.vue'
import { pendingLocationRuleReview } from '@/auth/locationRuleReviewRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const guid = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope = { environmentId: guid(99), evidenceMode: 'DEMO' as const, tenantId: guid(1), actorId: guid(2) }
const reference = { owner: 'Main.Wms.LocationRuleProposal.v1', id: guid(3), version: '2', digest: 'a'.repeat(64) }
const doc: LocationRuleReviewSource = { reference, etag: guid(4), isCurrent: true, body: { tenantId: scope.tenantId, id: reference.id, locationId: guid(40), selectorId: guid(41), version: '2', draft: { selectorKey: { ruleFamily: 'Frozen private rule', metricId: 'count', applicabilityDomainRef: { id: 'storage', version: '1' } }, capacity: { mode: 'UNLIMITED' as const }, allowedProductTypes: ['RAW'], allowedOperations: ['PICK'], isPickable: false, validFrom: '2026-10-27T00:00:00Z', validTo: null }, referenceResolution: 'RESOLVED', referenceBinding: null }, parentVersion: '1', authorId: scope.actorId, actorId: scope.actorId, originalJson: '{}', missing: [], state: 'DRAFT', activeSubmissionId: null }
const policy = { reference: { owner: 'Main.Wms.LocationRuleReviewPolicy.v1', id: guid(7), version: '1', digest: 'b'.repeat(64) }, fact: { actorId: scope.actorId, recordedAt: '2026-10-27T00:00:00Z', body: { operationId: guid(8), state: 'ENABLED' as const, requirements: [{ code: 'TECHNICAL', description: 'Private professional requirement', roleId: 73, minimumApprovals: 1 }], externalRequirements: [], separateAuthorAndReviewer: true, effectiveFrom: '2026-10-27T00:00:00Z', effectiveTo: '2026-10-30T00:00:00Z', reason: 'Explicit policy', rejectionDisposition: 'RETURNED' as const } } }
const initial: ReviewContext = { tenantId: scope.tenantId, locationRuleId: reference.id, etag: '"dr1-original"', locationRule: doc, policies: [policy], submissions: [] }
const ok = <T,>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined, context: ReviewContext, receipt: ReviewResult | undefined
const field = (name: string) => view!.get(`[aria-label="${name}"]`)
const button = (text: string) => view!.findAll('button').find(x => x.text() === text)!
async function click(text: string) { await button(text).trigger('click'); await flushPromises() }
async function start() { view = mount(Panel, { props: { locationRule: doc, scope } }); await flushPromises() }
async function submit() { await field('库位规则评审依据').setValue('Check the exact original and source language'); await field('库位规则送审原因').setValue('Ready for professional review'); await button('提交库位规则评审').trigger('submit'); await flushPromises() }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); api.authenticated = true; api.denied = []; context = structuredClone(initial); receipt = undefined
  api.scope.mockResolvedValue(ok(scope)); api.context.mockImplementation(async () => ok(structuredClone(context))); api.roles.mockResolvedValue(ok([{ roleId: 73, roleName: 'Professional reviewer' }]))
  api.submit.mockImplementation(async (_id: string, _etag: string, input: ReviewSubmitCommand) => {
    const submission = { submissionId: guid(9), locationRule: input.locationRule, policy: input.policy, starterId: scope.actorId, frozenAt: '2026-10-27T00:00:00Z', snapshotDigest: 'c'.repeat(64), state: 'SUBMITTED', transportState: 'PENDING', isApproved: false }
    receipt = { operationId: input.operationId, locationRuleId: input.locationRule.id, kind: 'SUBMIT', reference: { owner: 'Main.Wms.LocationRuleReviewSubmission.v1', id: submission.submissionId, version: '1', digest: 'd'.repeat(64) }, submission }
    context.submissions = [submission]; return ok(receipt)
  })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('submits the displayed exact version and policy without claiming approval', async () => {
  await start(); await submit(); expect(api.submit).toHaveBeenCalledTimes(1)
  expect(api.submit.mock.calls[0]!.slice(0, 2)).toEqual([reference.id, initial.etag])
  expect(api.submit.mock.calls[0]![2]).toMatchObject({ locationRule: reference, policy: policy.reference, replacesSubmissionId: null })
  expect(view!.text()).toContain('已送审，待专业评审'); expect(view!.text()).not.toContain('该准确库位规则版本已形成受控批准')
  expect(pendingLocationRuleReview(scope)).toBeNull()
})
it('keeps a lost-response operation and recovers only its original result', async () => {
  const save = api.submit.getMockImplementation()!; api.submit.mockImplementationOnce(async (...args) => { await save(...args); throw new Error('Connection lost') })
  await start(); await submit(); const pending = pendingLocationRuleReview(scope)!; expect(pending.operationId).toBe(receipt!.operationId)
  expect(localStorage.getItem(`cp6_locationRule_review_operation_v1:${scope.environmentId}:${scope.evidenceMode}:${scope.tenantId}:${scope.actorId}`)).not.toContain('Check the exact original')
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'COMMITTED', result: receipt }))
  await click('查询原评审操作'); expect(api.operation).toHaveBeenCalledWith(pending.operationId, false); expect(pendingLocationRuleReview(scope)).toBeNull(); expect(api.submit).toHaveBeenCalledTimes(1)
})
it('retains UNKNOWN until an explicit closed original result', async () => {
  api.submit.mockRejectedValue(new Error('Unknown outcome')); await start(); await submit(); const pending = pendingLocationRuleReview(scope)!
  api.operation.mockResolvedValueOnce(ok({ operationId: pending.operationId, state: 'UNKNOWN' })); await click('查询原评审操作'); expect(pendingLocationRuleReview(scope)).toEqual(pending)
  api.operation.mockResolvedValueOnce(ok({ operationId: pending.operationId, state: 'CLOSED' })); await click('结束未提交评审操作'); expect(pendingLocationRuleReview(scope)).toBeNull()
})
it('rejects a forged approved submit result and preserves the recovery key', async () => {
  const save = api.submit.getMockImplementation()!; api.submit.mockImplementationOnce(async (...args) => { const value = await save(...args); return ok({ ...value.data, submission: { ...value.data.submission, isApproved: true } }) })
  await start(); await submit(); expect(pendingLocationRuleReview(scope)).not.toBeNull(); expect(view!.text()).toContain('返回结果与原评审操作不一致')
})
it('clears private evidence and ignores a late response after identity changes', async () => {
  let release!: (value: ReturnType<typeof ok<ReviewContext>>) => void; api.context.mockReturnValueOnce(new Promise(resolve => { release = resolve }))
  await start(); api.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT)); release(ok(initial)); await flushPromises()
  expect(view!.text()).not.toContain('Private professional requirement'); expect(api.submit).not.toHaveBeenCalled()
})
it('prevents submitting an unseen replacement locationRule version', async () => {
  context.locationRule.reference.digest = 'f'.repeat(64); context.locationRule.reference.version = '3'; await start()
  expect(view!.text()).toContain('库位规则已有新版本'); expect((button('提交库位规则评审').element.closest('fieldset') as HTMLFieldSetElement).disabled).toBe(true)
})
it('uses explicitly chosen roles and dates when saving a policy', async () => {
  await start(); await click('读取可用角色'); await field('要求编号 1').setValue('TECHNICAL'); await field('审查内容 1').setValue('Exact source qualification')
  await field('专业角色 1').setValue('73'); await field('评审规则生效时间').setValue('2026-10-27T09:00'); await field('评审规则截止时间').setValue('2026-10-29T09:00'); await field('评审规则登记原因').setValue('Authorized professional rule')
  api.policy.mockImplementation(async (_id, _etag, input) => ok({ operationId: input.operationId, locationRuleId: reference.id, kind: 'POLICY', reference: policy.reference }))
  await button('保存评审规则').trigger('submit'); await flushPromises()
  expect(api.policy).toHaveBeenCalledTimes(1); expect(api.policy.mock.calls[0]![2].requirements).toEqual([{ code: 'TECHNICAL', description: 'Exact source qualification', roleId: 73, minimumApprovals: 1 }])
  expect(pendingLocationRuleReview(scope)).toBeNull()
})
it('does not offer policy or submit actions without their independent permissions', async () => {
  api.denied = ['wms-location-rule:review-policy', 'wms-location-rule:review-submit']; await start()
  expect(view!.text()).not.toContain('保存评审规则'); expect(view!.text()).not.toContain('提交库位规则评审'); expect(api.roles).not.toHaveBeenCalled()
})

it('uses explicit separation and rejection disposition choices', async () => {
  await start(); await click('读取可用角色'); await field('要求编号 1').setValue('CAPACITY'); await field('审查内容 1').setValue('Capacity suitability'); await field('专业角色 1').setValue('73')
  await field('评审规则生效时间').setValue('2026-10-27T09:00'); await field('评审规则截止时间').setValue('2026-10-29T09:00'); await field('评审规则登记原因').setValue('Explicit authorized policy')
  await field('作者与审批人分离').setValue(false); await field('拒绝后处理').setValue('REJECTED')
  api.policy.mockImplementation(async (_id, _etag, input) => ok({ operationId: input.operationId, locationRuleId: reference.id, kind: 'POLICY', reference: policy.reference }))
  await button('保存评审规则').trigger('submit'); await flushPromises(); expect(api.policy.mock.calls[0]![2]).toMatchObject({ separateAuthorAndReviewer: false, rejectionDisposition: 'REJECTED' })
})
it('locks resubmission while the exact candidate is already submitted', async () => {
  context.locationRule.state='SUBMITTED'; await start(); expect((button('提交库位规则评审').element.closest('fieldset') as HTMLFieldSetElement).disabled).toBe(true)
})
