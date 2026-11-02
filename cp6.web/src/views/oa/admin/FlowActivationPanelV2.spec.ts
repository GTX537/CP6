// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { flowActivationApi as api, type ActivationPreview } from '@/api/oa/flowActivation'
import { AUTH_EVENT, clearTabIdentity, newEpoch } from '@/auth/identity'
import Panel from './FlowActivationPanelV2.vue'
vi.mock('@/api/oa/flowActivation', () => ({ flowActivationApi: { list: vi.fn(), preview: vi.fn(), history: vi.fn(), execute: vi.fn(), original: vi.fn() } }))
const preview: ActivationPreview = { current: { definitionId: 'definition', key: 'approval', revision: 'activation-token', enabled: true,
  publishedVersionId: 'version-old', changedAtUtc: null, inflightUnchanged: true, consumerState: 'NOT_ADOPTED' },
  versions: [{ versionId: 'version-new', number: 2, name: 'new', formKey: 'form-new', formVersionId: 'form-v2' }, { versionId: 'version-old', number: 1, name: 'old', formKey: 'form-old', formVersionId: 'form-v1' }], page: 1, affectedEntries: [], inflightCount: null, formKey: 'form-old', formVersionId: 'form-v1' }
const wrappers: ReturnType<typeof mount>[] = [], state = (w: any): any => w.vm.$.setupState
function setup() { const w = mount(Panel); wrappers.push(w); return w }
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear(); vi.mocked(api.preview).mockResolvedValue(preview); vi.mocked(api.list).mockResolvedValue([]) })
afterEach(() => { wrappers.forEach(w => w.unmount()); wrappers.length = 0; vi.restoreAllMocks() })
it('requires explicit version and reason and sends activation CAS without draft token', async () => {
  vi.mocked(api.execute).mockRejectedValue({ response: { status: 503 } }); const w = setup(); await state(w).open('approval')
  await state(w).change(true); expect(api.execute).not.toHaveBeenCalled()
  state(w).reason = 'choose older'; state(w).selected = 'version-old'; await state(w).change(true)
  const intent = vi.mocked(api.execute).mock.calls[0]![0]
  expect(intent.command.expectedVersion).toBe('activation-token'); expect(intent.command.body).toEqual({ enabled: true, expectedActivationRevision: 'activation-token', publishedVersionId: 'version-old', reason: 'choose older' })
  expect(w.text()).toContain('运行入口尚未接入'); expect(w.text()).toContain('在途数量暂不可用')
})
it('disables the current pin even when a different version is selected', async () => {
  vi.mocked(api.execute).mockRejectedValue({ response: { status: 503 } }); const w = setup(); await state(w).open('approval')
  state(w).reason = 'stop new admissions'; state(w).selected = 'version-new'; await state(w).change(false)
  expect(vi.mocked(api.execute).mock.calls[0]![0].command.body).toMatchObject({ enabled: false, publishedVersionId: 'version-old' })
})
it('retains original intent on unknown outcomes and remount; NOT_OBSERVED never changes command', async () => {
  vi.mocked(api.execute).mockRejectedValue({ response: { status: 503 } }); const w = setup(); await state(w).open('approval'); state(w).reason = 'stop'; await state(w).change(false)
  const intent = vi.mocked(api.execute).mock.calls[0]![0]; w.unmount(); const restored = setup(); await flushPromises()
  expect(state(restored).pending.command.commandId).toBe(intent.command.commandId)
  vi.mocked(api.original).mockResolvedValue({ commandId: intent.command.commandId, outcome: 'NOT_OBSERVED', resourceId: null, resourceVersion: null, result: null })
  await state(restored).recover(); await state(restored).retry(); expect(vi.mocked(api.execute).mock.calls[1]![0]).toEqual(intent)
  expect(sessionStorage.getItem('cp6-workflow-activation-pending')).toContain(intent.command.commandId)
})
it('rejects mismatched originals and never displays an old successful result as current configuration', async () => {
  vi.mocked(api.execute).mockRejectedValue({ response: { status: 503 } }); const w = setup(); await state(w).open('approval'); state(w).reason = 'stop'; await state(w).change(false)
  const id = state(w).pending.command.commandId
  const result = { commandId: id, outcome: 'SUCCEEDED', resourceId: 'definition', resourceVersion: 'new-token', result: { ...preview.current, revision: 'new-token', enabled: false } }
  vi.mocked(api.original).mockResolvedValue({ ...result, resourceId: 'wrong' }); await state(w).recover(); expect(state(w).pending).toBeDefined()
  vi.mocked(api.original).mockResolvedValue(result); await state(w).recover(); expect(state(w).pending).toBeUndefined(); expect(state(w).preview).toBeUndefined()
  expect(w.text()).toContain('后续命令可能已改变它'); expect(sessionStorage.getItem('cp6-workflow-activation-pending')).toBeNull()
})
it('conflict requires fresh selection; denial and identity changes clear all private state', async () => {
  vi.mocked(api.execute).mockRejectedValue({ response: { status: 409 } }); const w = setup(); await state(w).open('approval'); state(w).reason = 'stop'; await state(w).change(false)
  expect(state(w).preview).toBeUndefined(); expect(state(w).pending).toBeUndefined()
  vi.mocked(api.preview).mockRejectedValue({ response: { status: 403 } }); await state(w).open('approval'); expect(state(w).rows).toEqual([])
  sessionStorage.setItem('cp6-workflow-activation-pending', 'private'); clearTabIdentity(); expect(sessionStorage.getItem('cp6-workflow-activation-pending')).toBeNull()
  let complete!: (v: ActivationPreview) => void; vi.mocked(api.preview).mockImplementation(() => new Promise(resolve => { complete = resolve }))
  const request = state(w).open('approval'); newEpoch(); window.dispatchEvent(new Event(AUTH_EVENT)); complete(preview); await request; expect(state(w).preview).toBeUndefined()
})
it('displays the selected exact FORM and reports unique binding conflict without switching another flow', async () => {
  vi.mocked(api.execute).mockRejectedValue({response:{status:409,data:{code:'E-WF-008'}}}); const w=setup();await state(w).open('approval')
  state(w).selected='version-new';state(w).reason='choose';await flushPromises();expect(w.text()).toContain('form-v2');expect(w.text()).toContain('form-v1')
  await state(w).change(true);expect(w.text()).toContain('该表单已有启用的流程');expect(api.execute).toHaveBeenCalledTimes(1);expect(state(w).pending).toBeUndefined()
})
it('preserves an unknown command after conflict or denied original read and ignores unrelated storage events', async () => {
  vi.mocked(api.execute).mockRejectedValueOnce({response:{status:503}}).mockRejectedValueOnce({response:{status:409,data:{code:'E-WF-008'}}})
  const w=setup();await state(w).open('approval');state(w).reason='stop';await state(w).change(false);const id=state(w).pending.command.commandId
  await state(w).retry();expect(state(w).pending.command.commandId).toBe(id)
  vi.mocked(api.original).mockRejectedValue({response:{status:403}});await state(w).recover();expect(state(w).pending.command.commandId).toBe(id);expect(state(w).preview).toBeUndefined()
  window.dispatchEvent(new StorageEvent('storage',{key:'unrelated'}));expect(state(w).pending.command.commandId).toBe(id)
})
it('does not send without durable recovery storage and keeps malformed recovery data locked', async () => {
  const w=setup();await state(w).open('approval');state(w).reason='stop'
  const set=vi.spyOn(Storage.prototype,'setItem').mockImplementation(()=>{throw new Error('full')});await state(w).change(false)
  expect(api.execute).not.toHaveBeenCalled();expect(w.text()).toContain('本次启停请求尚未发送');set.mockRestore();w.unmount()
  sessionStorage.setItem('cp6-workflow-activation-pending','{broken');const broken=setup();await state(broken).list()
  expect(api.list).not.toHaveBeenCalled();expect(state(broken).locked).toBe(true);expect(sessionStorage.getItem('cp6-workflow-activation-pending')).toBe('{broken')
})
