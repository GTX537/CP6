import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import ElementPlus from 'element-plus'
import Panel from './PrivacyExportPanel.vue'
const mock = vi.hoisted(() => ({ context: vi.fn(), generate: vi.fn(), operation: vi.fn(), close: vi.fn(), artifact: vi.fn(), revoke: vi.fn(), download: vi.fn(), save: vi.fn(), confirm: vi.fn(), stamp: 'actor-one' }))
vi.mock('@/api/platform/privacyExport', () => ({ privacyExportApi: mock, savePrivacyDownload: mock.save }))
vi.mock('@/auth/identity', () => ({ AUTH_EVENT: 'privacy-test-identity', identityStamp: () => mock.stamp }))
vi.mock('element-plus', async original => ({ ...await original<typeof import('element-plus')>(), ElMessageBox: { confirm: mock.confirm } }))
const tenant = '11111111-1111-1111-1111-111111111111', actor = '22222222-2222-2222-2222-222222222222', subject = '33333333-3333-3333-3333-333333333333', environment = '44444444-4444-4444-4444-444444444444'
const operationId = '55555555-5555-5555-5555-555555555555', artifactId = '66666666-6666-6666-6666-666666666666'
const storageKey = `cp6-privacy-export-v1:${environment}:${tenant}:${actor}`
const context = () => ({ environmentId: environment, actorTenantId: tenant, actorId: actor, policyVersion: 'policy-v1', purposes: ['SUBJECT_ACCESS_REQUEST'], sources: ['identity'], actions: ['read', 'export', 'download', 'revoke', 'scope-identity'], maximumAccessMinutes: 1440 })
const artifact = () => ({ receipt: { operationId, artifactId, requestRef: operationId, requesterId: actor, recipientId: actor, purpose: 'SUBJECT_ACCESS_REQUEST', capturedAt: '2026-11-04T00:00:00Z', expiresAt: '2026-11-04T01:00:00Z', manifest: { subjectId: subject, tenantId: tenant, complete: false, state: 'PARTIAL', sources: [], consistency: 'SEPARATE_OWNER_READS' } }, availability: 'AVAILABLE' })
const original = () => ({ operationId, state: 'COMMITTED', receipt: artifact().receipt })
let wrapper: ReturnType<typeof mount> | undefined
beforeEach(() => {
  vi.clearAllMocks(); localStorage.clear(); mock.stamp = 'actor-one'
  mock.context.mockResolvedValue(context()); mock.confirm.mockResolvedValue('confirm'); mock.artifact.mockResolvedValue(artifact()); mock.operation.mockResolvedValue(original())
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: vi.fn(async (_: string, fn: () => Promise<void>) => fn()) } })
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks() })
async function open() { wrapper = mount(Panel, { global: { plugins: [ElementPlus] } }); await flushPromises(); return wrapper }
async function fill() { await wrapper!.get('input[aria-label="目标租户 ID"]').setValue(tenant); await wrapper!.get('input[aria-label="主体用户 ID"]').setValue(subject) }
async function click(label: string) { const button = wrapper!.findAll('button').find(b => b.text() === label)!; expect(button).toBeTruthy(); await button.trigger('click'); await flushPromises() }
describe('durable privacy export interaction', () => {
  it('keeps only an identity-bound operation id after lost response; recovery never submits or downloads again', async () => {
    mock.generate.mockRejectedValue(new Error('response lost')); await open(); await fill(); await click('确认范围并生成制品')
    expect(mock.generate).toHaveBeenCalledTimes(1); const sent = mock.generate.mock.calls[0]![0]
    expect(localStorage.getItem(storageKey)).toBe(sent.operationId)
    expect(localStorage.length).toBe(1); expect(localStorage.getItem(storageKey)).not.toContain(subject)
    expect(wrapper!.text()).toContain('原操作结果待确认'); expect(mock.download).not.toHaveBeenCalled()
    await click('查询原操作'); expect(mock.generate).toHaveBeenCalledTimes(1); expect(wrapper!.text()).toContain('可申请下载')
    mock.download.mockResolvedValue(new Blob(['private bytes'])); await click('按当前权限下载'); expect(mock.download).toHaveBeenCalledWith(artifactId); expect(mock.save).toHaveBeenCalledTimes(1)
  })
  it('confirmation cancellation creates neither operation record nor request', async () => {
    mock.confirm.mockRejectedValue('cancel'); await open(); await fill(); await click('确认范围并生成制品')
    expect(mock.generate).not.toHaveBeenCalled(); expect(localStorage.getItem(storageKey)).toBeNull()
  })
  it('restores unknown operation after reload and closes it explicitly without resubmission', async () => {
    localStorage.setItem(storageKey, operationId); mock.operation.mockResolvedValue({ operationId, state: 'NOT_FOUND', receipt: null }); mock.close.mockResolvedValue({ operationId, state: 'CLOSED', receipt: null })
    await open(); expect(wrapper!.text()).toContain('尚未查到原结果'); await click('关闭未完成操作')
    expect(mock.close).toHaveBeenCalledWith(operationId); expect(mock.generate).not.toHaveBeenCalled(); expect(wrapper!.text()).toContain('原操作已关闭')
  })
  it('does not deliver a stale download after identity changes', async () => {
    localStorage.setItem(storageKey, operationId); await open(); let finish!: (b: Blob) => void
    mock.download.mockImplementation(() => new Promise<Blob>(resolve => { finish = resolve }))
    await click('按当前权限下载'); mock.stamp = 'actor-two'; window.dispatchEvent(new Event('privacy-test-identity')); await flushPromises()
    finish(new Blob(['private data'])); await flushPromises(); expect(mock.save).not.toHaveBeenCalled(); expect(wrapper!.text()).not.toContain(artifactId); expect(wrapper!.text()).not.toContain(subject)
  })
  it('observes another tab operation under the browser lock instead of generating a duplicate', async () => {
    await open(); await fill(); (navigator.locks.request as any).mockImplementation(async (_: string, fn: () => Promise<void>) => { localStorage.setItem(storageKey, operationId); await fn() })
    await click('确认范围并生成制品'); expect(mock.generate).not.toHaveBeenCalled(); expect(mock.operation).toHaveBeenCalledWith(operationId); expect(wrapper!.text()).toContain(artifactId)
  })
  it('does not send when durable operation recording fails', async () => {
    await open(); await fill(); vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked storage') })
    await click('确认范围并生成制品'); expect(mock.generate).not.toHaveBeenCalled(); expect(wrapper!.text()).toContain('尚未发送生成请求')
  })
  it('cannot skip an unknown other-tab operation when its original query fails', async () => {
    await open(); await fill(); localStorage.setItem(storageKey, operationId); mock.operation.mockRejectedValue(new Error('read unavailable'))
    await click('确认范围并生成制品'); expect(mock.generate).not.toHaveBeenCalled(); expect(wrapper!.text()).toContain('原操作结果待确认')
    await click('查询原操作'); expect(mock.generate).not.toHaveBeenCalled(); expect(localStorage.getItem(storageKey)).toBe(operationId)
  })
})
