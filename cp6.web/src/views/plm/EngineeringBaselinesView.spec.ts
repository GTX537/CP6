import { beforeEach, afterEach, it, expect, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
const api = vi.hoisted(() => ({ scope: vi.fn(), list: vi.fn(), read: vi.fn(), choices: vi.fn(), freeze: vi.fn(), cancel: vi.fn(), successor: vi.fn(), operation: vi.fn(), usage: vi.fn(), diff: vi.fn(), iterations: vi.fn(), authenticated: true, denied: [] as string[] }))
vi.mock('@/api/plm/engineeringBaselines', () => ({ baselineApi: api }))
vi.mock('@/api/plm/engineeringIteration', () => ({ engineeringIterationApi: { list: api.iterations } }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (key: string) => !api.denied.includes(key), loadMyActions: vi.fn() }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => api.authenticated }))
import View from './EngineeringBaselinesView.vue'
import { pendingBaseline } from '@/auth/baselineRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`, ref = (owner: string, n: number) => ({ owner, id: id(n), version: '1', digest: 'a'.repeat(64) }), ok = <T,>(data: T) => ({ code: 0, data })
const scope = { environmentId: id(1), tenantId: id(2), actorId: id(3), evidenceMode: 'DEMO' }, revision = ref('Main.Plm.EngineeringRevision.v1', 5), candidate = ref('Main.Plm.EngineeringCandidateDraft.v1', 6), baseline = ref('Main.Plm.EngineeringBaseline.v1', 7)
const check = { command: { revision, candidate, expectedEtag: id(10) }, input: { configuration: { reference: ref('Main.Plm.EngineeringConfigurationDraft.v1', 11) } }, manifest: { originalJson: 'precise native technical original', digest: 'b'.repeat(64) }, state: 'PASS' }
const choice = { iterationId: id(4), submission: { submissionId: id(12), roundId: id(13), package: { ...ref('Main.Plm.IterationReviewPackage.v1', 12), digest: 'c'.repeat(64) } }, application: { businessStage: 'REVIEW_APPROVED', decision: 'APPROVED', receipt: { receiptId: id(14), application: 'APPLIED', outcomeCode: 'ITERATION_APPROVED' } }, candidates: [{ reference: ref('Main.Plm.IterationCandidate.v1', 15), original: { definition: { title: '准确方案', engineeringRevision: revision, engineeringCandidate: candidate, technicalSource: check, sourceCheck: ref('Main.Plm.EngineeringSourceCheck.v1', 16) } } }] }
const view = { reference: baseline, original: { ...scope, frozenAt: '2026-10-28T12:00:00Z', command: { revision, expectedEtag: id(10) }, revision: { revisionCode: 'ENG-42' }, outputApplicability: 'NOT_APPLICABLE_BY_APPROVED_POLICY' }, originalJson: 'exact historical baseline original', outputBinding: null, sourceStatus: 'CHANGED', changes: ['CHECK_POLICY_CHANGED'], state: 'FROZEN', release: 'NOT_REQUESTED', verification: 'NOT_REQUESTED' }
const receipt = (operationId: string) => ({ operationId, baseline, originalOperationId: operationId, state: 'ACCEPTED', technicalManifestDigest: 'b'.repeat(64), outputBindingDigest: null, resultIntentId: id(17) })
let wrapper: VueWrapper | undefined
const button = (name: string) => wrapper!.findAll('button').find(x => x.text() === name)!
async function click(name: string) { await button(name).trigger('click'); await flushPromises() }
async function selectCandidate() { wrapper = mount(View); await flushPromises(); await wrapper.get('form').trigger('submit'); await flushPromises(); await wrapper.get('[aria-label="实际迭代"]').setValue(id(4)); await flushPromises(); await wrapper.get('[aria-label="准确评审候选"]').setValue('0'); await wrapper.get('[aria-label="冻结原因"]').setValue('Exact approved original') }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); api.authenticated = true; api.denied = []
  api.scope.mockResolvedValue(ok(scope)); api.list.mockResolvedValue(ok([view])); api.read.mockResolvedValue(ok(view)); api.choices.mockResolvedValue(ok([choice])); api.iterations.mockResolvedValue(ok([{ reference: { id: id(4) }, body: { name: '真实迭代', number: 'ITER-42' } }]))
  api.freeze.mockImplementation(async c => ok(receipt(c.operationId))); api.usage.mockResolvedValue(ok([{ owner: 'REL/VER/WO/SCHED', status: 'UNKNOWN', references: [], reason: 'Not yet adopted' }]))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_: string, __: unknown, work: (lock: object) => unknown) => work({}) } })
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined })
it('submits the actual applied round, both exact candidates and independent technical and review digests', async () => {
  await selectCandidate(); await click('冻结准确候选'); expect(api.freeze).toHaveBeenCalledOnce()
  expect(api.freeze.mock.calls[0]![0]).toMatchObject({ iterationId: id(4), sourceCandidate: choice.candidates[0]!.reference, revision, engineeringCandidate: candidate, reviewSubmissionId: id(12), reviewRoundId: id(13), applicationReceiptId: id(14), technicalManifestDigest: 'b'.repeat(64), reviewPackageDigest: 'c'.repeat(64), expectedEtag: id(10), configuration: check.input.configuration.reference })
  expect(pendingBaseline(scope)).toBeNull(); expect(api.read).toHaveBeenCalledWith(baseline.id); expect(wrapper!.text()).toContain('exact historical baseline original')
})
it('does not allow merely received approval to freeze before actual application', async () => {
  api.choices.mockResolvedValue(ok([{ ...choice, application: { ...choice.application, receipt: null } }])); await selectCandidate()
  expect(button('冻结准确候选').attributes('disabled')).toBeDefined(); await click('冻结准确候选'); expect(api.freeze).not.toHaveBeenCalled()
})
it('recovers a lost freeze response by the original key, rejecting wrong owners without resending', async () => {
  await selectCandidate(); api.freeze.mockRejectedValue(new Error('Lost response')); await click('冻结准确候选'); const p = pendingBaseline(scope)!
  expect(JSON.stringify(localStorage)).not.toContain('precise native'); api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'COMMITTED', result: { ...receipt(p.operationId), baseline: { ...baseline, owner: 'Other' } } }))
  await click('查询原操作'); expect(pendingBaseline(scope)).toEqual(p)
  api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'COMMITTED', result: receipt(p.operationId) })); await click('查询原操作'); expect(pendingBaseline(scope)).toBeNull(); expect(api.freeze).toHaveBeenCalledOnce()
})
it('preserves unknown state and reuses a durable cancellation key when its response is lost', async () => {
  await selectCandidate(); api.freeze.mockRejectedValue(new Error('Lost')); await click('冻结准确候选'); const p = pendingBaseline(scope)!
  api.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'UNKNOWN' })); await click('查询原操作'); expect(pendingBaseline(scope)).toEqual(p)
  api.cancel.mockRejectedValueOnce(new Error('Cancel response lost')); await click('结束未提交操作'); const saved = pendingBaseline(scope)!
  api.cancel.mockImplementation(async (operationId, targetOperationId) => ok({ operationId, targetOperationId, outcome: { operationId: targetOperationId, state: 'CLOSED' } })); await click('结束未提交操作')
  expect(api.cancel.mock.calls[1]![0]).toBe(saved.cancelOperationId); expect(pendingBaseline(scope)).toBeNull(); expect(api.freeze).toHaveBeenCalledOnce()
})
it('shows historical freeze separately from changed source qualification and unknown downstream coverage', async () => {
  wrapper = mount(View); await flushPromises(); await wrapper.findAll('button').find(x => x.text().includes('ENG-42'))!.trigger('click'); await flushPromises(); await click('读取引用情况')
  expect(wrapper.text()).toContain('冻结：已固定'); expect(wrapper.text()).toContain('已有变化'); expect(wrapper.text()).toContain('覆盖范围未知'); expect(wrapper.text()).toContain('exact historical baseline original')
})
it('uses the selected baseline original for successor creation with its own recoverable operation', async () => {
  wrapper = mount(View); await flushPromises(); await wrapper.findAll('button').find(x => x.text().includes('ENG-42'))!.trigger('click'); await flushPromises(); await wrapper.get('[aria-label="后继设计原因"]').setValue('New work')
  api.successor.mockImplementation(async operationId => ok({ operationId, parentBaseline: baseline, revision: { ...revision, version: '2' }, candidate: { ...candidate, version: '2' }, etag: id(20) })); await click('开始后继设计')
  expect(api.successor.mock.calls[0]![1]).toEqual(view); expect(pendingBaseline(scope)).toBeNull()
})
it('discards late private responses after identity changes and keeps the original recovery namespace', async () => {
  await selectCandidate(); let release!: (v: any) => void; api.freeze.mockImplementation(() => new Promise(resolve => { release = resolve }))
  await button('冻结准确候选').trigger('click'); await flushPromises(); const p = pendingBaseline(scope)!
  api.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT)); release(ok(receipt(p.operationId))); await flushPromises()
  expect(wrapper!.text()).not.toContain('precise native'); expect(wrapper!.text()).not.toContain('工程基线已固定'); expect(pendingBaseline(scope)).toEqual(p)
})
it('blocks writes when browser locks are unavailable or the freeze action is absent', async () => {
  await selectCandidate(); Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); await click('冻结准确候选'); expect(api.freeze).not.toHaveBeenCalled(); expect(pendingBaseline(scope)).toBeNull()
  wrapper!.unmount(); api.denied = ['plm-engineering-baseline:freeze']; wrapper = mount(View); await flushPromises(); expect(wrapper.text()).not.toContain('冻结准确候选')
})
