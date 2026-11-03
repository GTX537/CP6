// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { nativeInboxApi as api, type NativeDetail, type NativeInboxPage } from '@/api/oa/nativeInbox'
import { newEpoch } from '@/auth/identity'
import { taskDelegationApi } from '@/api/oa/taskDelegation'
import { getActingAs } from '@/stores/oaActingAs'
import Panel from './NativeInboxPanel.vue'
vi.mock('vue-router', () => ({ useRoute: () => ({ query: {} }) }))
vi.mock('./PersonalTaskPanel.vue', () => ({ default: { template: '<div />' } }))
vi.mock('./AppTaskPanel.vue', () => ({ default: { template: '<div class="app-review" />' } }))
vi.mock('../catalog/PersonalFormFields.vue', () => ({ default: { props: ['fields', 'modelValue', 'mask', 'disabled'], template: '<div />' } }))
vi.mock('@/api/oa/nativeInbox', () => ({ nativeInboxApi: { list: vi.fn(), detail: vi.fn(), read: vi.fn(), original: vi.fn(), close: vi.fn() } }))
vi.mock('@/api/oa/taskDelegation', () => ({ taskDelegationApi: { received: vi.fn() } }))
vi.mock('@/api/oa/nativeTaskOperations', () => ({ nativeTaskOperationsApi: {} }))
vi.mock('./NativeTransferBatchPanel.vue', () => ({ default: { template: '<div />' } }))
const page: NativeInboxPage = { items: [], totalItems: 0, totalGroups: 0, page: 1, pageSize: 20, rowMode: 'expanded', actualUserId: 'me', effectiveUserId: 'me' }
const detail: NativeDetail = { instanceId: 'one', rootInstanceId: 'root', contextKind: 'SFS', actualUserId: 'me', effectiveUserId: 'me', graph: [], tasks: [], cc: [], history: [], votes: [], form: { formDataId: 'data', formVersionId: 'original-form', title: 'private title', schemaJson: '{"fields":[]}', dataJson: '{}', fieldMask: {}, dataSource: 'ROOT_SUBMISSION' }, business: null }
const wrappers: ReturnType<typeof mount>[] = [], state = (w: any): any => w.vm.$.setupState
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear(); vi.mocked(api.list).mockResolvedValue(page); vi.mocked(api.detail).mockResolvedValue(detail) })
afterEach(() => { wrappers.splice(0).forEach(w => w.unmount()); vi.restoreAllMocks() })
async function setup() { const w = mount(Panel, { global: { stubs: { ElTable: true, ElTableColumn: true, PersonalTaskPanel: true, PersonalFormFields: { inheritAttrs: false, template: '<div />' } } } }); wrappers.push(w); await flushPromises(); return w }
it('changes explicit principal only from current grants and clears the prior detail before reloading', async () => {
  const w = await setup(); await state(w).open('one')
  vi.mocked(taskDelegationApi.received).mockResolvedValue([{ userId: 'principal', name: '原职责人', grants: [] }]); await state(w).loadPrincipals()
  state(w).chosenPrincipal = 'principal'; state(w).choosePrincipal(); expect(state(w).detail).toBeUndefined(); await flushPromises()
  expect(getActingAs()?.userId).toBe('principal'); expect(api.list).toHaveBeenCalledTimes(2)
  state(w).chosenPrincipal = 'not-granted'; state(w).choosePrincipal(); expect(getActingAs()?.userId).toBe('principal'); expect(api.list).toHaveBeenCalledTimes(2)
})
it('does not mark reads on GET and recovers an unknown explicit read with the exact same command', async () => {
  const w = await setup(); await state(w).open('one'); expect(api.read).not.toHaveBeenCalled()
  vi.mocked(api.read).mockRejectedValue({ response: { status: 503 } }); state(w).mark('TASK', '11111111-1111-1111-1111-111111111111'); await flushPromises()
  const first = vi.mocked(api.read).mock.calls[0]![0]; expect(sessionStorage.getItem('cp6-native-inbox-read-pending')).not.toContain('private title')
  state(w).mark('TASK', 'second'); expect(api.read).toHaveBeenCalledTimes(1)
  await state(w).recover('send'); expect(vi.mocked(api.read).mock.calls[1]![0]).toEqual(first)
  vi.mocked(api.original).mockResolvedValue({ commandId: first.command, state: 'NOT_OBSERVED', result: null, queryRef: '/original' }); await state(w).recover('original')
  expect(state(w).pending.command).toBe(first.command)
  vi.mocked(api.close).mockResolvedValue({ commandId: first.command, state: 'CLOSED', result: null, queryRef: '/original' }); await state(w).recover('close')
  expect(state(w).pending).toBeUndefined(); expect(sessionStorage.getItem('cp6-native-inbox-read-pending')).toBeNull()
})
it('keeps a newer selection when an older detail request resolves or is forbidden late', async () => {
  const w = await setup(); let first!: (value: NativeDetail) => void, second!: (value: NativeDetail) => void
  vi.mocked(api.detail).mockImplementation(id => new Promise(resolve => { if (id === 'one') first = resolve; else second = resolve }))
  const a = state(w).open('one'), b = state(w).open('two'); first(detail); await a
  expect(state(w).detail).toBeUndefined(); expect(state(w).detailBusy).toBe(true)
  second({ ...detail, instanceId: 'two' }); await b; expect(state(w).detail.instanceId).toBe('two'); expect(state(w).detailBusy).toBe(false)
})
it('clears previously visible data after current denial while preserving recovery identity', async () => {
  const w = await setup(); await state(w).open('one'); vi.mocked(api.read).mockRejectedValue({ response: { status: 503 } })
  state(w).mark('TASK', '11111111-1111-1111-1111-111111111111'); await flushPromises(); const command = state(w).pending.command
  vi.mocked(api.original).mockRejectedValue({ response: { status: 403 } }); await state(w).recover('original')
  expect(state(w).detail).toBeUndefined(); expect(state(w).rows).toBeUndefined(); expect(state(w).pending.command).toBe(command)
})
it('ignores a late receipt after identity changes and never updates the new session view', async () => {
  const w = await setup(); await state(w).open('one'); let resolve!: (value: any) => void
  vi.mocked(api.read).mockImplementation(() => new Promise(r => { resolve = r })); state(w).mark('TASK', '11111111-1111-1111-1111-111111111111')
  const command = state(w).pending.command; newEpoch(); resolve({ commandId: command, state: 'READ', result: { kind: 'TASK', targetId: '11111111-1111-1111-1111-111111111111' } }); await flushPromises()
  expect(state(w).detail).toBeUndefined(); expect(state(w).message).toBe(''); expect(state(w).pending).toBeUndefined()
})
it('fails before sending when the recovery record cannot be persisted', async () => {
  const w = await setup(); vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') })
  state(w).mark('TASK', '11111111-1111-1111-1111-111111111111'); await flushPromises()
  expect(api.read).not.toHaveBeenCalled(); expect(state(w).storageBlocked).toBe(true)
})
it('clears parent read data when the decision component reports current denial while keeping its recovery view', async () => {
  const w = await setup(); await state(w).open('one'); state(w).selectedTask = 'task'; await flushPromises()
  state(w).taskPrivateClear(); await flushPromises()
  expect(state(w).detail).toBeUndefined(); expect(state(w).rows).toBeUndefined(); expect(state(w).selectedTask).toBe('task')
})
it('opens the APP decision panel from the server context kind and retains the correct recovery panel on denial', async () => {
  const w = await setup(); await state(w).open('one'); state(w).selectedTaskKind = 'APP'; state(w).selectedTask = 'app-task'; await flushPromises()
  expect(w.find('.app-review').exists()).toBe(true); state(w).taskPrivateClear(); await flushPromises()
  expect(w.find('.app-review').exists()).toBe(true); expect(state(w).selectedTask).toBe('app-task'); expect(state(w).detail).toBeUndefined()
})
