// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { definitionCloneApi as api, type DefinitionKind } from '@/api/oa/definitionClone'
import { formPublicationApi } from '@/api/oa/formPublication'
import { AUTH_EVENT, clearTabIdentity, newEpoch } from '@/auth/identity'
import Panel from './DefinitionClonePanelV2.vue'
import FormPublication from './FormPublicationPanelV2.vue'
vi.mock('@/api/oa/definitionClone', () => ({ definitionCloneApi: { execute: vi.fn(), original: vi.fn() } }))
vi.mock('@/api/oa/formPublication', () => ({ formPublicationApi: { preview: vi.fn(), versions: vi.fn(), execute: vi.fn(), original: vi.fn() } }))
const source = { key: 'leave', versionId: 'published-old', version: 1 }
const mounted: ReturnType<typeof mount>[] = []
const state = (w: any): any => w.vm.$.setupState
function setup(kind: DefinitionKind = 'FORM', disabled = false) {
  const w = mount(Panel, { props: { kind, source, disabled } }); mounted.push(w); state(w).key = 'copy'; state(w).name = '副本'; return w
}
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear() })
afterEach(() => { mounted.forEach(w => w.unmount()); mounted.length = 0; vi.restoreAllMocks() })
it.each(['FORM', 'FLOW'] as const)('clones the selected %s publication and opens the new draft only after success', async kind => {
  vi.mocked(api.execute).mockImplementation(async intent => ({ commandId: intent.command.commandId, outcome: 'SUCCEEDED', result: { key: intent.command.body.newKey } }))
  const w = setup(kind); await state(w).clone()
  const intent = vi.mocked(api.execute).mock.calls[0]![0]
  expect(intent.kind).toBe(kind); expect(intent.sourceKey).toBe('leave'); expect(intent.command.expectedVersion).toBeNull()
  expect(intent.command.body).toEqual({ sourceVersionId: 'published-old', newKey: 'copy', newName: '副本' })
  expect(w.emitted('reload')?.[0]).toEqual(['copy']); expect(w.text()).toContain('停用草稿')
})
it('blocks unsaved edits, missing sources and empty new identities', async () => {
  const w = setup('FORM', true); await state(w).clone(); expect(api.execute).not.toHaveBeenCalled()
  await w.setProps({ disabled: false, source: undefined }); await state(w).clone(); expect(api.execute).not.toHaveBeenCalled()
  await w.setProps({ source }); state(w).key = ' '; await state(w).clone(); expect(api.execute).not.toHaveBeenCalled()
})
it('keeps one immutable intent after an unknown result, denied query, rejected retry and remount', async () => {
  vi.mocked(api.execute).mockRejectedValue({ response: { status: 503 } }); const w = setup(); await state(w).clone()
  const first = JSON.parse(JSON.stringify(state(w).pending)); expect(w.emitted('locked')?.at(-1)).toEqual([true])
  vi.mocked(api.original).mockRejectedValue({ response: { status: 403 } }); await state(w).recover()
  vi.mocked(api.execute).mockRejectedValue({ response: { status: 409 } }); await state(w).retry()
  expect(state(w).pending).toEqual(first); window.dispatchEvent(new StorageEvent('storage', { key: 'theme' })); expect(state(w).pending).toEqual(first)
  w.unmount(); const restored = setup(); await flushPromises(); expect(state(restored).pending).toEqual(first)
  vi.mocked(api.original).mockResolvedValue({ commandId: first.command.commandId, outcome: 'NOT_OBSERVED', result: null }); await state(restored).recover()
  expect(restored.emitted('reload')).toBeUndefined(); expect(state(restored).pending).toEqual(first)
  vi.mocked(api.original).mockResolvedValue({ commandId: first.command.commandId, outcome: 'SUCCEEDED', result: { key: 'copy' } }); await state(restored).recover()
  expect(restored.emitted('reload')?.[0]).toEqual(['copy']); expect(sessionStorage.getItem('cp6-definition-clone-FORM-pending')).toBeNull()
})
it('preserves broken caches and does not send when recovery storage cannot be written', async () => {
  sessionStorage.setItem('cp6-definition-clone-FORM-pending', '{broken'); const bad = setup(); await state(bad).clone()
  expect(api.execute).not.toHaveBeenCalled(); expect(sessionStorage.getItem('cp6-definition-clone-FORM-pending')).toBe('{broken'); bad.unmount()
  sessionStorage.clear(); const w = setup(); const storage = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') })
  await state(w).clone(); await state(w).retry(); expect(api.execute).not.toHaveBeenCalled(); expect(w.text()).toContain('尚未发送')
  storage.mockRestore()
})
it('clears a definite first rejection so the user can correct the target instead of overwriting it', async () => {
  vi.mocked(api.execute).mockRejectedValue({ response: { status: 409 } }); const w = setup(); await state(w).clone()
  expect(state(w).pending).toBeUndefined(); expect(state(w).key).toBe('copy'); expect(w.emitted('reload')).toBeUndefined()
})
it('does not adopt a late response from a previous identity and clears both recovery slots', async () => {
  let resolve!: (r: any) => void; vi.mocked(api.execute).mockImplementation(() => new Promise(r => { resolve = r }))
  const w = setup(); const task = state(w).clone(); const id = state(w).pending.command.commandId
  newEpoch(); window.dispatchEvent(new Event(AUTH_EVENT)); resolve({ commandId: id, outcome: 'SUCCEEDED', result: { key: 'copy' } }); await task
  expect(w.emitted('reload')).toBeUndefined(); expect(state(w).name).toBe('')
  sessionStorage.setItem('cp6-definition-clone-FORM-pending', 'private'); sessionStorage.setItem('cp6-definition-clone-FLOW-pending', 'private'); clearTabIdentity()
  expect(sessionStorage.getItem('cp6-definition-clone-FORM-pending')).toBeNull(); expect(sessionStorage.getItem('cp6-definition-clone-FLOW-pending')).toBeNull()
})
it('uses the actual history selection and locks publication while clone recovery is pending', async () => {
  vi.mocked(formPublicationApi.versions).mockResolvedValue([{ definitionId: 'head', versionId: source.versionId, version: 1, key: 'leave', fields: { Name: 'old', SchemaJson: '{}' }, schemaDigest: 'hash', publisherRef: 'p', publishedAtUtc: '2026-11-01T00:00:00Z', activationChanged: false }])
  vi.mocked(api.execute).mockRejectedValue({ response: { status: 503 } })
  const w = mount(FormPublication, { props: { current: { definitionId: 'head', draftId: 'current', key: 'leave', version: 'token', enabled: false, fields: {}, writableFields: [] }, dirty: false, disabled: false } }); mounted.push(w)
  await state(w).history(); const button = w.findAll('button').find(b => b.text().includes('版本 1 ·'))!; await button.trigger('click')
  const child = w.findComponent(Panel); state(child).key = 'copy'; state(child).name = '副本'; await state(child).clone()
  expect(vi.mocked(api.execute).mock.calls[0]![0].command.body.sourceVersionId).toBe(source.versionId)
  await state(w).prepare(); expect(formPublicationApi.preview).not.toHaveBeenCalled(); expect(w.emitted('locked')?.at(-1)).toEqual([true])
  vi.mocked(api.original).mockRejectedValue({ response: { status: 403 } }); await state(child).recover(); expect(w.find('pre').exists()).toBe(false)
})
