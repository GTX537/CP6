import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, afterEach, expect, it, vi } from 'vitest'
import ElementPlus from 'element-plus'
import Panel from './PrivacyRestorePanel.vue'
const mock = vi.hoisted(() => ({ context: vi.fn(), preview: vi.fn(), request: vi.fn(), plan: vi.fn(), decide: vi.fn(), execute: vi.fn(), metadata: vi.fn(), closeMetadata: vi.fn(), operation: vi.fn(), recover: vi.fn(), close: vi.fn(), confirm: vi.fn(), stamp: 'one' }))
vi.mock('@/api/platform/privacyRestore', () => ({ privacyRestoreApi: mock }))
vi.mock('@/auth/identity', () => ({ AUTH_EVENT: 'restore-test-auth', identityStamp: () => mock.stamp }))
vi.mock('element-plus', async original => ({ ...await original<typeof import('element-plus')>(), ElMessageBox: { confirm: mock.confirm } }))
const env = '11111111-1111-1111-1111-111111111111', tenant = '22222222-2222-2222-2222-222222222222', actor = '33333333-3333-3333-3333-333333333333'
const planId = '44444444-4444-4444-4444-444444444444', op = '55555555-5555-5555-5555-555555555555', candidate = '66666666-6666-6666-6666-666666666666'
const key = `cp6-privacy-restore-v1:${env}:${tenant}:${actor}`
const scope = { candidateId: candidate, backupSha256: 'backup', sourceVersion: op, source: { name: 'source' }, target: { name: 'isolated-candidate', oid: 123 }, tombstoneCount: 1, uncoveredOwners: ['CRM_BUSINESS_AND_HISTORY'] }
const plan = () => ({ planId, requesterId: actor, version: op, state: 'APPROVED', scope, scopeDigest: 'approved-scope', approvalCurrent: true, currentBlockers: [], outcome: null })
let wrapper: ReturnType<typeof mount> | undefined
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); mock.stamp = 'one'; mock.confirm.mockResolvedValue('confirm')
  mock.context.mockResolvedValue({ environmentId: env, actorTenantId: tenant, actorId: actor, sourceVersion: op, actions: ['read','restore-prepare','restore-approve','restore-execute'], candidates: [{ id: candidate, displayName: '隔离候选' }] })
  mock.plan.mockResolvedValue(plan()); mock.preview.mockResolvedValue({ scope, scopeDigest: 'preview', blockers: [] })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: vi.fn(async (_: string, fn: () => Promise<void>) => fn()) } })
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks() })
async function open() { wrapper = mount(Panel, { global: { plugins: [ElementPlus] } }); await flushPromises() }
async function input(name: string, value: string) { await wrapper!.get(`input[aria-label="${name}"]`).setValue(value) }
async function click(name: string) { await wrapper!.findAll('button').find(b => b.text() === name)!.trigger('click'); await flushPromises() }
it('cancellation never sends; explicit execution freezes the approved version and does not replay after lost response', async () => {
  await open(); await input('恢复计划 ID', planId); await click('读取恢复计划'); await input('恢复执行确认引用', op)
  mock.confirm.mockRejectedValueOnce('cancel'); await click('确认重施墓碑并核对开放门'); expect(mock.execute).not.toHaveBeenCalled(); expect(localStorage.length).toBe(0)
  mock.execute.mockRejectedValue(new Error('reply lost')); await click('确认重施墓碑并核对开放门')
  const command = mock.execute.mock.calls[0]![1]; expect(command).toMatchObject({ expectedVersion: op, expectedScopeDigest: 'approved-scope', confirmationRef: op })
  expect(localStorage.getItem(key)).toBe(`E:${command.operationId}`); expect(wrapper!.text()).toContain('恢复原操作待确认')
  wrapper!.unmount(); wrapper = undefined; mock.operation.mockResolvedValue({ operationId: command.operationId, state: 'PENDING', units: null })
  await open(); expect(mock.execute).toHaveBeenCalledTimes(1); expect(wrapper!.text()).toContain('尚无已核实的候选结果')
})
it('candidate-committed/source-unknown recovers explicitly with read capability and remains visibly partial', async () => {
  localStorage.setItem(key, `E:${op}`); mock.operation.mockResolvedValue({ operationId: op, state: 'AWAITING_SOURCE_RESULT', units: [{ subjectId: actor, outcome: 'REAPPLIED' }], aggregateState: 'PARTIAL' })
  mock.recover.mockResolvedValue({ operationId: op, state: 'APPLIED', planId, units: [{ subjectId: actor, outcome: 'REAPPLIED' }], aggregateState: 'PARTIAL' })
  mock.plan.mockResolvedValue({ ...plan(), approvalCurrent: false, outcome: { state: 'APPLIED' } })
  await open(); expect(wrapper!.text()).toContain('候选已有原回执，源结果待保存'); expect(mock.execute).not.toHaveBeenCalled()
  await click('核对并保存恢复原结果'); expect(mock.recover).toHaveBeenCalledWith(op); expect(wrapper!.text()).toContain('整体仍为部分范围'); expect(mock.execute).not.toHaveBeenCalled()
})
it('an unknown metadata operation closes without issuing another plan or losing the browser fence', async () => {
  localStorage.setItem(key, `M:${op}`); mock.metadata.mockResolvedValue(null); mock.closeMetadata.mockResolvedValue({ operationId: op, state: 'CLOSED', planId: null })
  await open(); await click('关闭恢复原操作'); expect(mock.closeMetadata).toHaveBeenCalledWith(op); expect(mock.request).not.toHaveBeenCalled(); expect(wrapper!.text()).toContain('原操作已关闭')
})
it('identity changes reject a late plan and clear previous database identity', async () => {
  await open(); let finish!: (v: unknown) => void; mock.plan.mockImplementation(() => new Promise(resolve => { finish = resolve }))
  await input('恢复计划 ID', planId); await click('读取恢复计划'); mock.stamp = 'two'; window.dispatchEvent(new Event('restore-test-auth')); finish(plan()); await flushPromises()
  expect(wrapper!.text()).not.toContain('isolated-candidate'); expect(wrapper!.text()).not.toContain(planId)
})
it('another tab unresolved operation is read under the lock and cannot be overwritten', async () => {
  await open(); await input('恢复计划 ID', planId); await click('读取恢复计划'); await input('恢复执行确认引用', op)
  localStorage.setItem(key, `E:${op}`); mock.operation.mockRejectedValue(new Error('source unavailable')); await click('确认重施墓碑并核对开放门')
  expect(mock.execute).not.toHaveBeenCalled(); expect(localStorage.getItem(key)).toBe(`E:${op}`); expect(wrapper!.text()).toContain('恢复原操作待确认')
})
it('a known applied original cannot expose the old execute action when refreshing its current plan fails', async () => {
  await open(); await input('恢复计划 ID', planId); await click('读取恢复计划'); await input('恢复执行确认引用', op)
  mock.execute.mockResolvedValue({ operationId: op, state: 'APPLIED', planId, units: [], aggregateState: 'PARTIAL' })
  mock.plan.mockRejectedValue(new Error('current plan unavailable')); await click('确认重施墓碑并核对开放门')
  expect(wrapper!.text()).toContain('已接墓碑已重施'); expect(wrapper!.text()).toContain('current plan unavailable')
  expect(wrapper!.findAll('button').some(button => button.text() === '确认重施墓碑并核对开放门')).toBe(false)
  expect(mock.execute).toHaveBeenCalledTimes(1)
})
