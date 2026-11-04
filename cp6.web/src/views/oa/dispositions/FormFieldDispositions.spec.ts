// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { formDispositionApi as api } from '@/api/oa/formDisposition'
import { newEpoch } from '@/auth/identity'
import Page from './FormFieldDispositions.vue'
vi.mock('@/api/oa/formDisposition', () => ({ formDispositionApi: { read: vi.fn(), write: vi.fn(), original: vi.fn() } }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ has: () => true }) }))
const id = '10000000-0000-0000-0000-000000000001'
const view = { requestId: id, draftId: 'draft', sourceVersionId: 'source', targetVersionId: 'target', version: id, state: 'REQUESTED', recordedAtUtc: '2026-11-04', hold: null, dispositionRef: null, retainUntilUtc: null }
const detail = { request: view, historyId: 'original-history', retentionPolicyRef: 'simulated-retention', losses: [{ field: 'secret', path: 'secret', kind: 'REMOVED', valueJson: '9007199254740993.0001' }], history: [] }
const wrappers: ReturnType<typeof mount>[] = []
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear(); vi.mocked(api.read).mockResolvedValue(detail) })
afterEach(() => wrappers.splice(0).forEach(x => x.unmount()))
function page() { const w = mount(Page); wrappers.push(w); return w }
async function click(w: ReturnType<typeof mount>, text: string) { await w.findAll('button').find(x => x.text() === text)!.trigger('click'); await flushPromises() }
async function load(w: ReturnType<typeof mount>) { await w.get('input[aria-label="处置请求引用"]').setValue(id); await w.get('form').trigger('submit'); await flushPromises() }
it('shows exact authorized numeric text and clears it after current read denial', async () => {
  const w = page(); await load(w); expect(w.get('pre').text()).toBe('9007199254740993.0001')
  vi.mocked(api.read).mockRejectedValue({ response: { status: 403 } }); await load(w)
  expect(w.text()).not.toContain('9007199254740993.0001'); expect(w.get('[role=alert]').text()).toContain('无权')
})
it('renders only a safe state for the requesting owner', async () => {
  vi.mocked(api.read).mockResolvedValue({ ...detail, historyId: null, retentionPolicyRef: null, losses: null, history: null })
  const w = page(); await load(w); expect(w.text()).toContain('等待独立审批'); expect(w.find('table').exists()).toBe(false)
  expect(w.findAll('button').map(x => x.text())).not.toContain('批准处置')
})
it('keeps only recovery metadata after a lost write and restores the original without another POST', async () => {
  const w = page(); await load(w); await w.get('textarea').setValue('sensitive approval explanation')
  vi.mocked(api.write).mockRejectedValue(new Error('connection lost')); await click(w, '批准处置')
  expect(api.write).toHaveBeenCalledOnce(); const input = vi.mocked(api.write).mock.calls[0]![2] as { operationId: string }
  const metadata = sessionStorage.getItem('cp6-form-disposition-pending')!
  expect(metadata).toContain(input.operationId); expect(metadata).not.toContain('sensitive'); expect(metadata).not.toContain('9007199254740993')
  expect(w.text()).not.toContain('9007199254740993.0001'); expect(w.text()).toContain('尚待确认')
  vi.mocked(api.original).mockResolvedValue({ operationId: input.operationId, state: 'UNKNOWN', request: null }); await click(w, '查询原结果')
  expect(w.text()).toContain('尚未观察到原操作结果'); expect(api.write).toHaveBeenCalledOnce()
  vi.mocked(api.original).mockResolvedValue({ operationId: input.operationId, state: 'COMMITTED', request: { ...view, state: 'APPROVED' } })
  vi.mocked(api.read).mockResolvedValue({ ...detail, request: { ...view, state: 'APPROVED' } }); await click(w, '查询原结果')
  expect(w.text()).toContain('已批准，等待留存'); expect(sessionStorage.getItem('cp6-form-disposition-pending')).toBeNull(); expect(api.write).toHaveBeenCalledOnce()
})
it('uses the exact current hold version and blocks retention while a hold is active', async () => {
  vi.mocked(api.read).mockResolvedValue({ ...detail, request: { ...view, state: 'APPROVED', hold: { version: 'hold-version', active: true } } })
  const w = page(); await load(w); await w.get('textarea').setValue('resolve investigation'); await w.get('input[aria-label="保留限制依据"]').setValue('simulated closure')
  expect(w.findAll('button').find(x => x.text() === '登记原件留存')!.attributes('disabled')).toBeDefined()
  vi.mocked(api.write).mockResolvedValue({ operationId: 'op', state: 'COMMITTED', request: { ...view, state: 'APPROVED' } }); await click(w, '解除保留限制')
  expect(api.write).toHaveBeenCalledWith(id, 'hold', expect.objectContaining({ expectedVersion: 'hold-version', active: false, basis: 'simulated closure' }), expect.any(AbortSignal))
})
it('discards an in-flight private read after identity changes', async () => {
  let resolve!: (value: typeof detail) => void; vi.mocked(api.read).mockImplementation(() => new Promise(r => { resolve = r }))
  const w = page(); await w.get('input[aria-label="处置请求引用"]').setValue(id); await w.get('form').trigger('submit')
  newEpoch(); resolve(detail); await flushPromises(); expect(w.find('table').exists()).toBe(false); expect(w.text()).not.toContain('9007199254740993')
})
it('does not send any command when the recovery marker cannot be saved', async () => {
  const w = page(); await load(w); await w.get('textarea').setValue('approved')
  const set = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') })
  await click(w, '批准处置'); expect(api.write).not.toHaveBeenCalled(); expect(w.text()).toContain('未发送操作'); set.mockRestore()
})
