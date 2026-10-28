import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
const f = vi.hoisted(() => ({ scope: vi.fn(), iteration: vi.fn(), list: vi.fn(), read: vi.fn(), save: vi.fn(), operation: vi.fn(), compare: vi.fn(), revisions: vi.fn(), checks: vi.fn(), check: vi.fn(), denied: [] as string[], authenticated: true }))
vi.mock('@/api/plm/engineeringIteration', () => ({ engineeringIterationApi: { scope: f.scope, read: f.iteration } }))
vi.mock('@/api/plm/iterationCandidate', () => ({ candidateApi: f }))
vi.mock('@/api/plm/engineeringRevisions', () => ({ revisionApi: { list: f.revisions } }))
vi.mock('@/api/plm/engineeringSourceCheck', () => ({ sourceCheckApi: { history: f.checks, read: f.check } }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (key: string) => !f.denied.includes(key) }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => f.authenticated }))
import Page from './IterationCandidatesView.vue'
import { pendingCandidate } from '@/auth/iterationCandidateRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope = { environmentId: id(99), evidenceMode: 'DEMO' as const, tenantId: id(1), actorId: id(2) }
const ref = (n: number, version = '1') => ({ owner: 'NativeOwner', id: id(n), version, digest: 'a'.repeat(64) })
const ok = <T,>(data: T) => ({ code: 0, data })
const iteration = () => ({ reference: ref(8), etag: id(80), body: { name: 'Native iteration', iteration: { stage: 'IN_DESIGN', holdState: 'NORMAL', inputVersion: '3' } } })
const source = () => ({ reference: ref(12), original: { checkedAt: '2026-10-28', command: { revision: ref(11, '4'), candidate: ref(13, '4') }, state: 'PASS', input: { components: [] }, manifest: { digest: 'b'.repeat(64) } }, originalJson: 'Exact technical source', freshness: 'CURRENT', changes: [], freeze: 'NOT_REQUESTED' })
const candidate = () => ({ reference: ref(9), isCurrent: true, original: { iterationId: id(8), etag: id(90), definition: { title: '方案甲', changeSummary: 'Exact difference', assumptions: 'Pending assumption', parentCandidate: null, engineeringRevision: ref(11, '4'), engineeringCandidate: ref(13, '4'), sourceCheck: ref(12), technicalSource: source().original, technicalSourceOriginal: source().originalJson }, previousChanges: [], parentChanges: [] }, originalJson: 'Private candidate original', sourceDrift: 'UNCHANGED', findings: [], review: 'NOT_SUBMITTED', freeze: 'NOT_REQUESTED' })
let value = iteration(), selected = candidate(), checkValue = source(), view: VueWrapper | undefined
const button = (name: string) => view!.findAll('button').find(b => b.text() === name)!
async function click(name: string) { await button(name).trigger('click'); await flushPromises() }
async function start() { view = mount(Page, { props: { iterationId: id(8) } }); await flushPromises() }
async function choose() { await click('查找工程修订'); await view!.get('[aria-label="工程修订"]').setValue(id(11)); await click('读取技术检查记录'); await view!.get('[aria-label="准确检查记录"]').setValue(id(12)); await click('核对并选择检查原件') }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); f.authenticated = true; f.denied = []; value = iteration(); selected = candidate(); checkValue = source()
  f.scope.mockResolvedValue(ok(scope)); f.iteration.mockImplementation(() => Promise.resolve(ok(value))); f.list.mockImplementation(() => Promise.resolve(ok([selected]))); f.read.mockImplementation(() => Promise.resolve(ok(selected)))
  f.revisions.mockResolvedValue(ok([{ reference: ref(11, '4'), revisionCode: 'ENG0001', draft: { goal: 'Native design' } }]))
  f.checks.mockImplementation(() => Promise.resolve(ok([checkValue]))); f.check.mockImplementation(() => Promise.resolve(ok(checkValue))); f.save.mockRejectedValue(new Error('Response lost'))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_a: string, _b: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('adopts an exact native check and keeps only recovery identities after response loss', async () => {
  await start(); await click('新建方案'); await choose(); await view!.get('[aria-label="方案名称"]').setValue('New candidate'); await view!.get('[aria-label="差异说明"]').setValue('Private analysis'); await view!.get('[aria-label="操作原因"]').setValue('Explicit adoption')
  await view!.get('form').trigger('submit'); await flushPromises(); expect(f.save).toHaveBeenCalledTimes(1)
  expect(f.save.mock.calls[0]![0]).toMatchObject({ iterationId: id(8), expectedIteration: ref(8), expectedIterationEtag: id(80), sourceCheck: ref(12), action: 'CREATE', sourceDecision: 'ADOPT_CURRENT' })
  expect(Object.keys(pendingCandidate(scope)!).sort()).toEqual(['candidateId', 'iterationId', 'operationId']); expect(JSON.stringify(localStorage)).not.toContain('Private analysis'); expect(view!.text()).toContain('原操作待确认')
})
it('requires an explicit retained-original decision for a stale source', async () => {
  checkValue.freshness = 'STALE'; await start(); await click('新建方案'); await choose(); await view!.get('[aria-label="方案名称"]').setValue('Old source'); await view!.get('[aria-label="操作原因"]').setValue('Compare only')
  await view!.get('form').trigger('submit'); await flushPromises(); expect(f.save).not.toHaveBeenCalled(); expect(view!.text()).toContain('来源已变化')
  await view!.get('[aria-label="来源处理"]').setValue('KEEP_ORIGINAL'); await view!.get('[aria-label="保留原版原因"]').setValue('Preserve for explicit comparison'); await view!.get('form').trigger('submit'); await flushPromises()
  expect(f.save.mock.calls[0]![0]).toMatchObject({ sourceDecision: 'KEEP_ORIGINAL', driftReason: 'Preserve for explicit comparison', sourceCheck: ref(12) })
})
it('shows exact history without offering to overwrite it and keeps precheck separate from review', async () => {
  await start(); await click('打开方案'); expect(view!.text()).toContain('评审：尚未提交'); expect(view!.text()).toContain('冻结：尚未请求'); expect(view!.text()).toContain('Private candidate original')
  selected = { ...selected, isCurrent: false, reference: ref(9, '2') }; await view!.get('[aria-label="历史版本"]').setValue('2'); await click('读取历史版本')
  expect(f.read).toHaveBeenLastCalledWith(id(8), id(9), '2'); expect(view!.text()).not.toContain('修订方案'); expect(view!.text()).toContain('从此版本派生方案')
})
it('branches from the selected immutable candidate reference', async () => {
  await start(); await click('打开方案'); await click('从此版本派生方案'); await choose(); await view!.get('[aria-label="方案名称"]').setValue('Alternative'); await view!.get('[aria-label="操作原因"]').setValue('Branch explicit original')
  await view!.get('form').trigger('submit'); await flushPromises(); expect(f.save.mock.calls[0]![0]).toMatchObject({ action: 'CREATE', parentCandidate: selected.reference }); expect(f.save.mock.calls[0]![0].candidateId).not.toBe(selected.reference.id)
})
it('holds writes when the iteration is paused and hides write controls for query-only access', async () => {
  value.body.iteration.holdState = 'HELD'; await start(); expect(button('新建方案').attributes('disabled')).toBeDefined(); view!.unmount(); view = undefined
  f.denied = ['plm-engineering-iteration:candidate-write']; await start(); expect(view!.text()).not.toContain('新建方案')
})
it('clears private data on current denial and discards delayed sources from an earlier identity', async () => {
  await start(); await click('打开方案'); f.read.mockRejectedValueOnce({ response: { status: 403 } }); await click('打开方案'); expect(view!.text()).not.toContain('Private candidate original')
  await click('重新核对当前状态'); await click('新建方案'); let resolve!: (x: unknown) => void; f.revisions.mockReturnValueOnce(new Promise(done => { resolve = done })); await click('查找工程修订')
  f.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT)); resolve(ok([{ reference: ref(11), revisionCode: 'Private obsolete source', draft: { goal: 'Secret' } }])); await flushPromises()
  expect(view!.text()).not.toContain('Private obsolete source'); expect(view!.find('form').exists()).toBe(false)
})
