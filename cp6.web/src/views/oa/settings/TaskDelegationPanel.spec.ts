// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { taskDelegationApi as api, type TaskGrant, type TaskGrantList } from '@/api/oa/taskDelegation'
import { newEpoch } from '@/auth/identity'
import Panel from './TaskDelegationPanel.vue'
vi.mock('@/api/oa/taskDelegation', () => ({ taskDelegationApi: { list: vi.fn(), targets: vi.fn(), history: vi.fn(), write: vi.fn(), original: vi.fn(), close: vi.fn() } }))
const grant: TaskGrant = { grantId: '11111111-1111-1111-1111-111111111111', revision: '3', version: '22222222-2222-2222-2222-222222222222', grantorId: 'me', delegateId: 'delegate', fromUtc: '2026-11-02T10:00:00Z', toUtc: '2026-11-03T10:00:00Z', scope: { actionKinds: ['TASK_READ', 'TASK_APPROVE'], flowKeys: ['exact-flow'] }, enabled: true, revocationEpoch: '8', grantedAtUtc: '2026-11-01T00:00:00Z', grantedBy: 'me', revokedAtUtc: null, revokedBy: null, reason: 'private reason', sourceRef: '/history' }
const list: TaskGrantList = { granted: [grant], received: [], actualUserId: 'me', observedAtUtc: '2026-11-02T10:00:00Z', legacyState: 'LEGACY_SCOPE_TIME_UNVERIFIED' }
const wrappers: ReturnType<typeof mount>[] = [], state = (w: any): any => w.vm.$.setupState
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear(); vi.mocked(api.list).mockResolvedValue(list) })
afterEach(() => { wrappers.splice(0).forEach(w => w.unmount()); vi.restoreAllMocks() })
async function setup() { const w = mount(Panel); wrappers.push(w); await flushPromises(); return w }
it('preserves the exact UTC bounds when only an existing grant scope changes', async () => {
  const w = await setup(), precise = { ...grant, fromUtc: '2026-11-02T10:00:00.1234567Z', toUtc: '2026-11-03T10:00:00.7654321Z' }
  state(w).edit(precise); await flushPromises()
  expect((w.find('input[type="datetime-local"]').element as HTMLInputElement).value).toBe('2026-11-02T10:00:00.123')
  state(w).selectedActions = ['TASK_READ']; vi.mocked(api.write).mockRejectedValue({ response: { status: 503 } }); await state(w).submit()
  const raw = JSON.parse(vi.mocked(api.write).mock.calls[0]![1]); expect(raw.body.validFromUtc).toBe(precise.fromUtc); expect(raw.body.validToUtc).toBe(precise.toUtc); expect(raw.expectedVersion).toBe(grant.version)
})
function draft(w: any) { Object.assign(state(w), { delegate: 'delegate', from: '2026-11-02T10:00', to: '2026-11-03T10:00', flows: 'exact-flow', types: 'APP_TYPE', remark: 'private reason', selectedActions: ['TASK_APPROVE'] }) }
it('sends an exact UTC scoped intent once and recovers unknown results without storing grant contents', async () => {
  const w = await setup(); draft(w); vi.mocked(api.write).mockRejectedValue({ response: { status: 503 } }); await state(w).submit()
  const [intent, raw] = vi.mocked(api.write).mock.calls[0]!; const body = JSON.parse(raw)
  expect(body.body).toMatchObject({ validFromUtc: '2026-11-02T10:00:00.000Z', scope: { actionKinds: ['TASK_READ', 'TASK_APPROVE'], flowKeys: ['exact-flow'], bizTypes: ['APP_TYPE'] } })
  expect(sessionStorage.getItem('cp6-task-delegation-pending')).not.toContain('private reason'); expect(sessionStorage.getItem('cp6-task-delegation-pending')).not.toContain('APP_TYPE')
  await state(w).submit(); expect(api.write).toHaveBeenCalledTimes(1)
  await state(w).run('send'); expect(vi.mocked(api.write).mock.calls[1]).toEqual([intent, raw])
  vi.mocked(api.original).mockResolvedValue({ commandId: intent.command, state: 'NOT_OBSERVED', result: null, queryRef: '/original' }); await state(w).run('original'); expect(state(w).pending.command).toBe(intent.command)
  vi.mocked(api.close).mockResolvedValue({ commandId: intent.command, state: 'CLOSED', result: null, queryRef: '/original' }); await state(w).run('close'); expect(state(w).pending).toBeUndefined()
})
it('revokes only the selected current version and does not clear an unknown original on forbidden recovery', async () => {
  const w = await setup(); state(w).edit(grant); state(w).reason = 'end assignment'; vi.mocked(api.write).mockRejectedValue({ response: { status: 503 } }); await state(w).submit(true)
  const [intent, raw] = vi.mocked(api.write).mock.calls[0]!; expect(intent).toMatchObject({ operation: 'REVOKE', resource: grant.grantId }); expect(JSON.parse(raw)).toMatchObject({ expectedVersion: grant.version, body: { reason: 'end assignment' } })
  vi.mocked(api.original).mockRejectedValue({ response: { status: 403 } }); await state(w).run('original'); expect(state(w).list).toBeUndefined(); expect(state(w).edited).toBeUndefined(); expect(state(w).pending.command).toBe(intent.command)
})
it('keeps authorization history selection current when responses arrive out of order', async () => {
  const w = await setup(); let late!: (value: TaskGrant[]) => void
  vi.mocked(api.history).mockReturnValueOnce(new Promise(resolve => { late = resolve })).mockResolvedValueOnce([{ ...grant, revision: '4' }])
  const first = state(w).showHistory(grant); await state(w).showHistory({ ...grant, grantId: 'different' }); late([grant]); await first; expect(state(w).history[0].revision).toBe('4')
})
it('ignores old identity responses and unrelated storage changes do not discard a pending command', async () => {
  const w = await setup(); draft(w); vi.mocked(api.write).mockRejectedValue({ response: { status: 503 } }); await state(w).submit(); const command = state(w).pending.command
  window.dispatchEvent(new StorageEvent('storage', { key: 'unrelated' })); expect(state(w).pending.command).toBe(command)
  let late!: (value: TaskGrantList) => void; vi.mocked(api.list).mockReturnValueOnce(new Promise(resolve => { late = resolve })); const request = state(w).load()
  newEpoch(); late(list); await request; expect(state(w).list).toBeUndefined(); expect(state(w).pending).toBeUndefined()
})
it('does not send a new authorization when recovery storage cannot be written', async () => {
  const w = await setup(); draft(w); vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') }); await state(w).submit(); expect(api.write).not.toHaveBeenCalled(); expect(state(w).storageBlocked).toBe(true)
})
