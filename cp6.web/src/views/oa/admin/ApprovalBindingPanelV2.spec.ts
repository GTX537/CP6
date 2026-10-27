// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { approvalBindingApi as api, type BindingView } from '@/api/oa/approvalBinding'
import { AUTH_EVENT, clearTabIdentity, newEpoch } from '@/auth/identity'
import Panel from './ApprovalBindingPanelV2.vue'
vi.mock('@/api/oa/approvalBinding', () => ({ approvalBindingApi: { capabilities: vi.fn(), options: vi.fn(), list: vi.fn(), history: vi.fn(), timeline: vi.fn(), execute: vi.fn(), original: vi.fn() } }))
const row: BindingView = { bindingId: 'binding', revisionId: 'revision', revision: 1, state: 'ACTIVE', owner: 'Registered Owner', rowVersion: 'AAAAAAAAAAE=', createdBy: 'actor', createdAt: '2026-10-27T01:00:00.000Z', timelineRevisionId: 'timeline', effectiveInterval: null,
  scheduledIntervals: [{ from: '2026-12-01T00:00:00.000Z', to: null, revisionId: 'future-revision', state: 'ACTIVE' }], commandId: 'original', bizType: 'TOOL', subjectKind: 'DISPOSE', selectionMode: 'EXACT_PINNED', flowKey: 'approval', flowDefVersionId: 'version', effectiveFrom: '2026-10-27T01:00:00.000Z', effectiveTo: null, ruleMode: 'EXCLUSIVE_MATCH', rules: [], detailRoute: '/tools/{bizId}', remark: 'kept' }
const wrappers: ReturnType<typeof mount>[] = [], state = (w: any): any => w.vm.$.setupState
function setup() { const w = mount(Panel); wrappers.push(w); return w }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear()
  vi.mocked(api.capabilities).mockResolvedValue({ schema: 'oa.app/2.0', bindingRead: true, bindingConfigure: true })
  vi.mocked(api.options).mockResolvedValue({ businessTypes: [{ bizType: 'TOOL', owner: 'Registered Owner', subjectKinds: ['DISPOSE'], snapshotSchema: 'tool/1' }], flowVersions: [{ flowKey: 'approval', versionId: 'version', version: '1', status: 'Published', headEnabled: true }], asOf: 'now' })
  vi.mocked(api.list).mockResolvedValue({ items: [row], nextCursor: null }); vi.mocked(api.execute).mockRejectedValue({ response: { status: 504 } })
})
afterEach(() => { wrappers.forEach(w => w.unmount()); wrappers.length = 0 })
it('edits a registered binding with its own CAS and closed body, keeping Owner out of input', async () => {
  const w = setup(); await state(w).load(); state(w).edit(row); state(w).from = '2026-12-02T10:00:00'; await state(w).save()
  const intent = vi.mocked(api.execute).mock.calls[0]![0]
  expect(intent.bindingId).toBe('binding'); expect(intent.etag).toBe('"AAAAAAAAAAE="'); expect(intent.command).toMatchObject({ bizType: 'TOOL', subjectKind: 'DISPOSE', flowDefVersionId: 'version' })
  expect(intent.command).not.toHaveProperty('owner'); expect(intent.command).not.toHaveProperty('rowVersion')
  expect(w.text()).toContain('审批发起尚未接入'); expect(w.text()).toContain('future-revision')
})
it('disables with exact current CAS and reason; exposes the future schedule being cancelled', async () => {
  const w = setup(); await state(w).load(); state(w).edit(row); await state(w).disable(); expect(api.execute).not.toHaveBeenCalled()
  state(w).reason = 'cancel future plans'; await state(w).disable()
  expect(vi.mocked(api.execute).mock.calls[0]![0]).toMatchObject({ bindingId: 'binding', etag: '"AAAAAAAAAAE="', operation: 'DISABLE', command: { reason: 'cancel future plans' } })
})
it('keeps an unknown command through remount and a 404 query, and retries identical input', async () => {
  const w = setup(); await state(w).load(); state(w).edit(row); state(w).reason = 'stop'; await state(w).disable(); const intent = vi.mocked(api.execute).mock.calls[0]![0]
  w.unmount(); const restored = setup(); await flushPromises(); vi.mocked(api.original).mockRejectedValue({ response: { status: 404 } }); await state(restored).recover()
  expect(state(restored).pending).toEqual(intent); expect(restored.text()).toContain('不能判断未提交'); await state(restored).retry(); expect(vi.mocked(api.execute).mock.calls[1]![0]).toEqual(intent)
})
it('checks original command and target, then clears stale configuration instead of displaying it as current', async () => {
  const w = setup(); await state(w).load(); state(w).edit(row); state(w).reason = 'stop'; await state(w).disable(); const id = state(w).pending.command.commandId
  const original = { commandId: id, state: 'COMPLETED', targetType: 'BINDING', targetId: 'wrong', resultRef: { owner: 'Main Workflow', id: 'timeline', version: 'version' }, errorCode: null, recordedAt: 'now' }
  vi.mocked(api.original).mockResolvedValue(original); await state(w).recover(); expect(state(w).pending).toBeDefined()
  vi.mocked(api.original).mockResolvedValue({ ...original, targetId: 'binding' }); await state(w).recover(); expect(state(w).pending).toBeUndefined(); expect(state(w).rows).toEqual([]); expect(state(w).form).toBeUndefined()
  expect(w.text()).toContain('后续命令可能已改变排程')
})
it('keeps read-only users from writing, requires fresh state on CAS conflict, and clears on denial/logout', async () => {
  const w = setup(); vi.mocked(api.capabilities).mockResolvedValue({ schema: 'oa.app/2.0', bindingRead: true, bindingConfigure: false }); await state(w).load(); state(w).edit(row); expect(state(w).form).toBeUndefined()
  vi.mocked(api.capabilities).mockResolvedValue({ schema: 'oa.app/2.0', bindingRead: true, bindingConfigure: true }); await state(w).load(); state(w).edit(row); state(w).reason = 'stop'; vi.mocked(api.execute).mockRejectedValue({ response: { status: 412 } }); await state(w).disable(); expect(state(w).pending).toBeUndefined(); expect(state(w).selected).toBeUndefined()
  vi.mocked(api.options).mockRejectedValue({ response: { status: 403 } }); await state(w).load(); expect(state(w).rows).toEqual([])
  sessionStorage.setItem('cp6-approval-binding-pending', 'private'); clearTabIdentity(); expect(sessionStorage.getItem('cp6-approval-binding-pending')).toBeNull()
})
it('ignores delayed catalog results after an identity switch', async () => {
  const w = setup(); let complete!: (v: any) => void; vi.mocked(api.options).mockImplementation(() => new Promise(resolve => { complete = resolve }))
  const request = state(w).load(); await flushPromises(); newEpoch(); window.dispatchEvent(new Event(AUTH_EVENT)); complete({ businessTypes: [], flowVersions: [], asOf: 'old' }); await request
  expect(state(w).options).toBeUndefined(); expect(api.list).not.toHaveBeenCalled()
})
