import { beforeEach, afterEach, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import type { RevisionSnapshot } from '@/api/plm/engineeringRevisions'
import type { CheckCommand, CheckView } from '@/api/plm/engineeringSourceCheck'
const api = vi.hoisted(() => ({ scope: vi.fn(), save: vi.fn(), read: vi.fn(), history: vi.fn(), operation: vi.fn(), relations: vi.fn(), allowed: true, authenticated: true }))
vi.mock('@/api/plm/engineeringOutputRelations', () => ({ outputRelationsApi: { list: api.relations } }))
vi.mock('@/api/plm/engineeringSourceCheck', () => ({ sourceCheckApi: api }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: () => api.allowed }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => api.authenticated }))
import View from './EngineeringSourceCheckView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import { pendingSourceCheck } from '@/auth/engineeringCheckRecovery'
const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const id = '30000000-0000-4000-8000-000000000003', runId = '40000000-0000-4000-8000-000000000004'
const reference = { owner: 'Main.Plm.EngineeringRevision.v1', id, version: '4', digest: 'a'.repeat(64) }
const candidate = { ...reference, owner: 'Main.Plm.EngineeringCandidateDraft.v1', id: '50000000-0000-4000-8000-000000000005', digest: 'b'.repeat(64) }
const policy = { ...reference, owner: 'Main.Plm.ConfigurationRule.v1', id: '60000000-0000-4000-8000-000000000006', version: '3', digest: 'c'.repeat(64) }
const revision = { reference, candidateRef: candidate, etag: '70000000-0000-4000-8000-000000000007',
  draft: { productRef: null, goal: 'Protected engineering input', configuration: { reference, input: { ruleRef: policy, options: [], applicability: 'APPLIES', reason: '' } }, components: [] } } as unknown as RevisionSnapshot
const result = (operationId: string) => ({ operationId, revisionId: id, checkRunId: runId, digest: 'd'.repeat(64) })
const ok = <T>(data: T) => ({ code: 0, data })
let wrapper: VueWrapper | undefined
const saved = (): CheckView => ({ reference: { ...reference, owner: 'Main.Plm.EngineeringSourceCheck.v1', id: runId, version: '1', digest: 'd'.repeat(64) }, originalJson: '{}', freshness: 'CURRENT', changes: [], freeze: 'NOT_REQUESTED',
  original: { tenantId: scope.tenantId, id: runId, actorId: scope.actorId, checkedAt: '2026-10-28T03:00:00Z', command: { operationId: crypto.randomUUID(), revision: reference, candidate, expectedEtag: revision.etag, policy, reason: 'Exact check' },
    input: revision.draft, policyBody: {} as any, inputDigest: 'e'.repeat(64), manifest: null, sources: [], state: 'UNKNOWN', authorityWatermark: 'f'.repeat(64), purpose: 'TECHNICAL_PRECHECK_ONLY',
    items: [{ path: '/quality', state: 'UNKNOWN', code: 'SOURCE_UNAVAILABLE', reason: 'Original source is temporarily unavailable', source: null }] } })
async function render() { wrapper = mount(View, { props: { revision: structuredClone(revision) } }); await flushPromises() }
async function click(text: string) { await wrapper!.findAll('button').find(x => x.text() === text)!.trigger('click'); await flushPromises() }
async function start() { await wrapper!.get('[aria-label="工程检查原因"]').setValue('Exact current check'); await click('检查当前候选') }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); api.allowed = true; api.authenticated = true
  api.scope.mockResolvedValue(ok(scope)); api.save.mockImplementation(async (body: CheckCommand) => ok(result(body.operationId)))
  api.read.mockImplementation(async () => ok(saved())); api.history.mockResolvedValue(ok([]))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined })
it('checks the saved exact candidate and policy and preserves UNKNOWN without claiming a freeze', async () => {
  await render(); await start()
  expect(api.save).toHaveBeenCalledOnce(); expect(api.save.mock.calls[0]![0]).toMatchObject({ revision: reference, candidate, policy, expectedEtag: revision.etag })
  expect(wrapper!.text()).toContain('原检查：未知'); expect(wrapper!.text()).toContain('尚未冻结，未取得用途许可')
  expect(pendingSourceCheck(scope)).toBeNull()
})
it('selects the exact independently qualified relation for this revision and passes its full reference', async () => {
  const relation = { owner: 'Main.Plm.EngineeringOutputRelations.v1', id: crypto.randomUUID(), version: '9', digest: '8'.repeat(64) }
  api.relations.mockResolvedValue(ok([{ version: { reference: relation, original: { revisionOrigin: reference, body: { name: 'Qualified complete outputs' } } }, control: { state: 'QUALIFIED' }, missing: [] }]))
  await render(); await click('查找准确输出关系'); await wrapper!.get('[aria-label="准确输出关系"]').setValue('0'); await start()
  expect(api.save.mock.calls[0]![0].outputRelations).toEqual(relation)
  await wrapper!.setProps({ revision: { ...structuredClone(revision), reference: { ...reference, digest: '9'.repeat(64) } } }); await flushPromises(); await start()
  expect(api.save.mock.calls[1]![0].outputRelations).toBeUndefined()
})
it('recovers the original key after a lost response without automatic resend or persisting private inputs', async () => {
  api.save.mockRejectedValue(new Error('network interrupted')); await render(); await start()
  const pending = pendingSourceCheck(scope)!; expect(pending.objectId).toBe(id)
  expect(JSON.stringify(localStorage)).not.toContain('Protected engineering input'); expect(Object.keys(pending)).toEqual(['operationId', 'objectId'])
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'COMMITTED', result: result(pending.operationId) }))
  await click('查询原检查'); expect(api.save).toHaveBeenCalledOnce(); expect(api.operation).toHaveBeenCalledWith(pending.operationId, false); expect(pendingSourceCheck(scope)).toBeNull()
})
it('keeps UNKNOWN and mismatched original receipts recoverable', async () => {
  api.save.mockRejectedValue(new Error('lost')); await render(); await start(); const pending = pendingSourceCheck(scope)!
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'UNKNOWN' })); await click('查询原检查'); expect(pendingSourceCheck(scope)).toEqual(pending)
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'COMMITTED', result: { ...result(pending.operationId), revisionId: crypto.randomUUID() } }))
  await click('查询原检查'); expect(pendingSourceCheck(scope)).toEqual(pending); expect(wrapper!.text()).toContain('返回结果与原操作不一致')
})
it('shows stale source observations separately from the immutable original PASS', async () => {
  const old = saved(); old.original.state = 'PASS'; old.freshness = 'STALE'; old.changes = ['CHECK_POLICY_CHANGED']; api.read.mockResolvedValue(ok(old))
  await render(); await start(); expect(wrapper!.text()).toContain('原检查：通过'); expect(wrapper!.text()).toContain('此原检查已失效'); expect(wrapper!.text()).toContain('尚未冻结')
})
it('does not show a late source response after identity changes', async () => {
  let release!: (v: unknown) => void; api.read.mockImplementation(() => new Promise(resolve => { release = resolve }))
  await render(); await start(); api.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT))
  release(ok(saved())); await flushPromises(); expect(wrapper!.text()).not.toContain('Original source is temporarily unavailable'); expect(wrapper!.text()).not.toContain('原检查：')
})
it('clears disclosed results when current source access fails on a later read', async () => {
  await render(); await start(); expect(wrapper!.text()).toContain('原检查：')
  api.history.mockRejectedValue({ response: { data: { message: 'OBJECT_FORBIDDEN' } } }); await click('读取检查记录')
  expect(wrapper!.text()).not.toContain('原检查：'); expect(wrapper!.text()).toContain('当前无权读取检查涉及的来源')
})
