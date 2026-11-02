// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { formPublicationApi as api, type PublicationPreview, type PublishedForm } from '@/api/oa/formPublication'
import type { FormAuthoringView } from '@/api/oa/formAuthoring'
import { AUTH_EVENT, clearTabIdentity, newEpoch } from '@/auth/identity'
import Panel from './FormPublicationPanelV2.vue'
vi.mock('@/api/oa/formPublication', () => ({ formPublicationApi: { preview: vi.fn(), versions: vi.fn(), execute: vi.fn(), original: vi.fn() } }))
const current: FormAuthoringView = { definitionId: 'head', draftId: 'draft', key: 'leave', version: 'token', enabled: false,
  fields: { Name: 'Leave', SchemaJson: '{}' }, writableFields: ['Name', 'SchemaJson'] }
const preview: PublicationPreview = { id: 'preview', digest: 'digest', draftVersionId: 'draft', expectedVersion: 'token', schemaDigest: 'hash', expiresAtUtc: '2099-01-01T00:00:00Z', diagnostics: [], dependencies: [], canPublish: true }
const version: PublishedForm = { definitionId: 'head', versionId: 'old-source', version: 1, key: 'leave', fields: { Name: 'Old name', SchemaJson: '{"old":true}' }, schemaDigest: 'hash', publisherRef: 'publisher', publishedAtUtc: '2026-10-27T00:00:00Z', activationChanged: false }
const mounted: ReturnType<typeof mount>[] = []
const state = (w: any): any => w.vm.$.setupState
function setup(props: Partial<{ current: FormAuthoringView; dirty: boolean; disabled: boolean }> = {}) {
  const w = mount(Panel, { props: { current, dirty: false, disabled: false, ...props } }); mounted.push(w); return w
}
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear(); vi.mocked(api.preview).mockResolvedValue(preview); vi.mocked(api.versions).mockResolvedValue([version]) })
afterEach(() => { mounted.forEach(w => w.unmount()); mounted.length = 0; vi.restoreAllMocks() })
it('does not publish unsaved edits or a preview for a different saved token', async () => {
  const w = setup({ dirty: true }); await state(w).prepare(); expect(api.preview).not.toHaveBeenCalled()
  await w.setProps({ dirty: false }); await state(w).prepare(); expect(state(w).preview.id).toBe('preview')
  await w.setProps({ current: { ...current, version: 'changed' } }); await state(w).publish(); expect(api.execute).not.toHaveBeenCalled()
})
it('retains exact publication intent through unknown outcomes and reloads only a matching original', async () => {
  vi.mocked(api.execute).mockRejectedValue({ response: { status: 503 } })
  const w = setup(); await state(w).prepare(); state(w).comment = 'reviewed'; await state(w).publish()
  const intent = vi.mocked(api.execute).mock.calls[0]![0]
  vi.mocked(api.original).mockResolvedValue({ commandId: intent.command.commandId, outcome: 'NOT_OBSERVED', resourceId: null, resourceVersion: null, result: null })
  await state(w).recover(); await state(w).retry(); expect(vi.mocked(api.execute).mock.calls[1]![0]).toEqual(intent)
  expect(sessionStorage.getItem('cp6-form-publication-pending')).toContain(intent.command.commandId)
  vi.mocked(api.original).mockResolvedValue({ commandId: intent.command.commandId, outcome: 'SUCCEEDED', resourceId: 'head', resourceVersion: 'new-token',
    result: { publication: { ...version, versionId: 'draft' }, draft: null } })
  await state(w).recover(); expect(state(w).pending).toBeUndefined(); expect(w.emitted('reload')?.[0]).toEqual(['leave'])
  expect(w.text()).toContain('启停配置保持不变'); expect(sessionStorage.getItem('cp6-form-publication-pending')).toBeNull()
})
it('restores unresolved original intent after remount and rejects mismatched results', async () => {
  vi.mocked(api.execute).mockRejectedValue({ response: { status: 503 } })
  const first = setup(); await state(first).prepare(); await state(first).publish(); const intent = state(first).pending; first.unmount()
  const restored = setup(); await flushPromises(); expect(state(restored).pending.command.commandId).toBe(intent.command.commandId)
  vi.mocked(api.original).mockResolvedValue({ commandId: intent.command.commandId, outcome: 'SUCCEEDED', resourceId: 'foreign', resourceVersion: 't', result: { publication: version, draft: null } })
  await state(restored).recover(); expect(state(restored).pending).toBeDefined(); expect(restored.emitted('reload')).toBeUndefined()
})
it('creates a next draft from the explicitly chosen old version with current head CAS', async () => {
  vi.mocked(api.execute).mockRejectedValue({ response: { status: 503 } })
  const w = setup({ current: { ...current, published: true, version: 'current-head-token' } }); await state(w).history(); await state(w).next(version)
  const intent = vi.mocked(api.execute).mock.calls[0]![0]
  expect(intent.operation).toBe('FORM_CREATE_DRAFT'); expect(intent.command.expectedVersion).toBe('current-head-token'); expect(intent.command.body).toEqual({ sourceVersionId: 'old-source' })
  expect(w.text()).toContain('Old name')
})
it('keeps hidden historical schema absent and blocks unqualified publication', async () => {
  vi.mocked(api.versions).mockResolvedValue([{ ...version, fields: { Name: 'visible' }, schemaDigest: null }])
  vi.mocked(api.preview).mockResolvedValue({ ...preview, canPublish: false, diagnostics: ['FORM_PUBLICATION_SOURCE_UNPROVEN'] })
  const w = setup(); await state(w).history(); state(w).selected = state(w).versions[0]; await flushPromises()
  expect(w.find('pre').exists()).toBe(false); await state(w).prepare(); await state(w).publish(); expect(api.execute).not.toHaveBeenCalled()
})
it('clears private pending state on denial, logout, and ignores late identity responses', async () => {
  vi.mocked(api.preview).mockRejectedValue({ response: { status: 403 } }); const w = setup(); await state(w).prepare()
  expect(w.emitted('privateClear')).toBeDefined(); expect(state(w).versions).toEqual([])
  sessionStorage.setItem('cp6-form-publication-pending', 'private'); clearTabIdentity(); expect(sessionStorage.getItem('cp6-form-publication-pending')).toBeNull()
  let complete!: (value: PublicationPreview) => void; vi.mocked(api.preview).mockImplementation(() => new Promise(resolve => { complete = resolve }))
  const request = state(w).prepare(); newEpoch(); window.dispatchEvent(new Event(AUTH_EVENT)); complete(preview); await request
  expect(state(w).preview).toBeUndefined()
})

it('persists before send and keeps unknown publication after denied query or rejected retry', async () => {
  const w=setup();await state(w).prepare()
  const storage=vi.spyOn(Storage.prototype,'setItem').mockImplementation(()=>{throw new Error('quota')})
  await state(w).publish();await state(w).retry();expect(api.execute).not.toHaveBeenCalled();expect(state(w).error).toContain('尚未发送')
  storage.mockRestore();vi.mocked(api.execute).mockRejectedValue({response:{status:503}});await state(w).retry();const intent=state(w).pending
  vi.mocked(api.original).mockRejectedValue({response:{status:403}});await state(w).recover();expect(state(w).pending.command).toEqual(intent.command)
  vi.mocked(api.execute).mockRejectedValue({response:{status:409}});await state(w).retry();expect(state(w).pending.command).toEqual(intent.command)
  window.dispatchEvent(new StorageEvent('storage',{key:'theme'}));expect(state(w).pending.command).toEqual(intent.command)
})

it('preserves unreadable recovery cache and blocks a new mutation until storage is recovered',async()=>{
  sessionStorage.setItem('cp6-form-publication-pending','{broken')
  const w=setup();await state(w).prepare();expect(api.preview).not.toHaveBeenCalled();expect(state(w).cacheUnavailable).toBe(true)
  expect(sessionStorage.getItem('cp6-form-publication-pending')).toBe('{broken')
})
