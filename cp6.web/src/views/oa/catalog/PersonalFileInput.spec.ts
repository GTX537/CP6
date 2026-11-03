import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import PersonalFileInput from './PersonalFileInput.vue'
const mocks = vi.hoisted(() => ({ upload: vi.fn(), command: vi.fn(), original: vi.fn(), close: vi.fn(), read: vi.fn(), download: vi.fn(), identity: 'actor-a' }))
vi.mock('@/api/oa/formFiles', () => ({ formFileApi: mocks }))
vi.mock('@/auth/identity', () => ({ AUTH_EVENT: 'auth-change', identityStamp: () => mocks.identity, requireIdentity: (value: string) => { if (value !== mocks.identity) throw new Error('identity changed') } }))
vi.mock('@/stores/oaActingAs', () => ({ getActingAs: () => null }))
const scope = { kind: 'FORM' as const, key: 'personal', id: null, formVersionId: 'published-1' }
const reference = 'cp6-form-file-v2/00000000-0000-0000-0000-000000000001/1/' + 'a'.repeat(64)
const view = { reference, fileName: 'private-original.txt', contentRef: { id: 'file-1', digest: 'a'.repeat(64), byteLength: 4, mediaType: 'text/plain' }, stateVersion: '1', inspection: { status: 'CLEAN', provider: 'test', reason: '' }, held: false, availability: 'AVAILABLE' }
let uuid = 0
beforeEach(() => {
  vi.clearAllMocks(); sessionStorage.clear(); mocks.identity = 'actor-a'; uuid = 0
  vi.stubGlobal('crypto', { randomUUID: () => `00000000-0000-0000-0000-${String(++uuid).padStart(12, '0')}`, subtle: { digest: vi.fn(async () => new Uint8Array(32).buffer) } })
  mocks.read.mockResolvedValue(view); mocks.upload.mockResolvedValue({ operationId: 'op', state: 'COMMITTED', result: view })
})
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks() })
function create(extra = {}) { return mount(PersonalFileInput, { props: { context: { scope }, field: 'files', modelValue: [], ...extra } }) }
async function choose(wrapper: ReturnType<typeof create>) {
  await flushPromises(); const file = new File(['test'], 'private-original.txt', { type: 'text/plain' })
  Object.defineProperty(file, 'arrayBuffer', { value: async () => new Uint8Array([116, 101, 115, 116]).buffer })
  Object.defineProperty(wrapper.get('input[type=file]').element, 'files', { configurable: true, value: [file] })
  await wrapper.get('input[type=file]').trigger('change'); await flushPromises(); return file
}
const button = (wrapper: ReturnType<typeof create>, text: string) => wrapper.findAll('button').find(b => b.text() === text)!
describe('ordinary FORM files', () => {
  it('uploads exact selected bytes with the current pinned scope then emits only the content reference', async () => {
    const wrapper = create(); const file = await choose(wrapper)
    expect(mocks.upload).toHaveBeenCalledWith(expect.objectContaining({ scope, field: 'files', byteLength: 4, digest: '0'.repeat(64) }), file, expect.any(AbortSignal))
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([[reference]])
    expect(sessionStorage.length).toBe(0); expect(wrapper.emitted('validity')?.at(-1)).toEqual([true]); wrapper.unmount()
  })
  it('retains an unknown original through a later 403 and keeps names and bytes out of browser storage', async () => {
    mocks.upload.mockRejectedValueOnce(new Error('lost response')).mockRejectedValueOnce({ response: { status: 403 } })
    const wrapper = create(); await choose(wrapper)
    const stored = sessionStorage.getItem(sessionStorage.key(0)!)!; expect(stored).not.toContain('private-original'); expect(stored).not.toContain('byteLength'); expect(stored).not.toContain(reference)
    await button(wrapper, '重试原附件操作').trigger('click'); await flushPromises()
    expect(sessionStorage.getItem(sessionStorage.key(0)!)).toBe(stored); expect(wrapper.text()).toContain('附件原操作待确认'); expect(wrapper.emitted('forbidden')).toBeTruthy(); wrapper.unmount()
  })
  it('queries a persisted unknown after refresh and never resends missing file bytes', async () => {
    mocks.upload.mockRejectedValue(new Error('unknown')); const first = create(); await choose(first); first.unmount()
    mocks.original.mockResolvedValue({ operationId: 'op', state: 'UNKNOWN' }); const restored = create(); await flushPromises()
    expect(restored.text()).not.toContain('重试原附件操作'); await button(restored, '查询附件原结果').trigger('click'); await flushPromises(); expect(sessionStorage.length).toBe(1)
    mocks.original.mockResolvedValue({ operationId: 'op', state: 'COMMITTED', result: view }); await button(restored, '查询附件原结果').trigger('click'); await flushPromises()
    expect(restored.emitted('update:modelValue')?.at(-1)).toEqual([[reference]]); expect(mocks.upload).toHaveBeenCalledTimes(1); restored.unmount()
  })
  it('does not upload when recovery storage cannot be written', async () => {
    const wrapper = create(); await flushPromises(); vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('storage failed') })
    await choose(wrapper); expect(mocks.upload).not.toHaveBeenCalled(); expect(wrapper.text()).toContain('未发送'); wrapper.unmount()
  })
  it('refreshes current availability after an immutable original state-command receipt', async () => {
    const wrapper = create({ modelValue: [reference] }); await flushPromises()
    mocks.command.mockResolvedValue({ operationId: 'op', state: 'COMMITTED', result: view })
    mocks.read.mockResolvedValue({ ...view, stateVersion: '3', held: true, availability: 'HELD' })
    await button(wrapper, '重新检查').trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('保留锁定'); expect(button(wrapper, '下载原件').attributes('disabled')).toBeDefined()
    expect(sessionStorage.length).toBe(0); wrapper.unmount()
  })
  it('clears a prior available view if its current reread fails', async () => {
    const wrapper = create({ modelValue: [reference] }); await flushPromises()
    mocks.read.mockRejectedValue({ response: { status: 503 } })
    await button(wrapper, '重新读取附件状态').trigger('click'); await flushPromises()
    expect(wrapper.text()).not.toContain('private-original.txt'); expect(wrapper.text()).toContain('附件信息尚未读取')
    expect(button(wrapper, '下载原件')).toBeUndefined(); wrapper.unmount()
  })
  it('reads under the actual business context and hides upload controls in a readonly field', async () => {
    const wrapper = create({ disabled: true, context: { instanceId: 'actual-instance' }, modelValue: [reference] }); await flushPromises()
    expect(mocks.read).toHaveBeenCalledWith({ instanceId: 'actual-instance' }, 'files', reference, expect.any(AbortSignal)); expect(wrapper.text()).toContain('private-original.txt'); expect(wrapper.find('input[type=file]').exists()).toBe(false)
    mocks.read.mockRejectedValue({ response: { status: 403 } }); await button(wrapper, '重新读取附件状态').trigger('click'); await flushPromises()
    expect(wrapper.text()).not.toContain('private-original.txt'); expect(wrapper.emitted('forbidden')).toBeTruthy(); wrapper.unmount()
  })
  it('ignores a response after identity changes without disclosing the prior file name', async () => {
    let resolve!: (value: unknown) => void; mocks.read.mockReturnValue(new Promise(r => { resolve = r }))
    const wrapper = create({ disabled: true, modelValue: [reference] }); await flushPromises(); mocks.identity = 'actor-b'; window.dispatchEvent(new Event('auth-change'))
    resolve(view); await flushPromises(); expect(wrapper.text()).not.toContain('private-original.txt'); wrapper.unmount()
  })
})
