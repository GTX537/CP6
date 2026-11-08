// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, afterEach, expect, it, vi } from 'vitest'
import ElementPlus from 'element-plus'
import Panel from './LegacyFormSourceMapping.vue'
const mock = vi.hoisted(() => ({ versions: vi.fn(), sourcePreview: vi.fn(), configure: vi.fn(), operation: vi.fn(), close: vi.fn(), confirm: vi.fn(), stamp: 'source-admin-one' }))
vi.mock('@/api/oa/formPublication', () => ({ formPublicationApi: { versions: mock.versions } }))
vi.mock('@/api/oa/legacyDraftAdoption', () => ({ legacyDraftAdoptionApi: mock }))
vi.mock('@/auth/identity', () => ({ AUTH_EVENT: 'legacy-map-identity', identityStamp: () => mock.stamp }))
vi.mock('element-plus', async load => ({ ...await load<typeof import('element-plus')>(), ElMessageBox: { confirm: mock.confirm } }))
const source = '11111111-1111-1111-1111-111111111111', pin = '22222222-2222-2222-2222-222222222222', op = '33333333-3333-3333-3333-333333333333', key = 'cp6-legacy-form-source-v1:source-admin-one'
const preview = () => ({ legacyVersionId: source, sourceRevision: pin, recordedVersion: 3, targetKey: 'qualified', targetVersionId: pin, targetVersion: 2, compatible: true, current: null, canConfigure: true })
let wrapper: ReturnType<typeof mount> | undefined
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); mock.stamp = 'source-admin-one'; mock.confirm.mockResolvedValue('confirm'); mock.versions.mockResolvedValue([{ versionId: pin, version: 2 }]); mock.sourcePreview.mockResolvedValue(preview())
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: vi.fn(async (_: string, run: () => Promise<void>) => run()) } })
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks() })
async function open() { wrapper = mount(Panel, { props: { legacyVersionId: source }, global: { plugins: [ElementPlus] } }); await flushPromises() }
async function click(name: string) { await wrapper!.findAll('button').find(x => x.text() === name)!.trigger('click'); await flushPromises() }
async function prepare() { await open(); await wrapper!.get('input[aria-label="目标原生表单 Key"]').setValue('qualified'); await click('读取已核实的发布版本'); await click('核对准确来源映射'); await wrapper!.get('input[aria-label="当前来源映射依据"]').setValue('explicit current basis') }
it('cancellation sends nothing; an uncertain write freezes the exact source and persists only the UUID', async () => {
  await prepare(); mock.confirm.mockRejectedValueOnce('cancel'); await click('保存当前来源映射'); expect(mock.configure).not.toHaveBeenCalled(); expect(localStorage.length).toBe(0)
  mock.configure.mockRejectedValue(new Error('lost reply')); await click('保存当前来源映射'); const input = mock.configure.mock.calls[0]![0]
  expect(input).toMatchObject({ legacyVersionId: source, expectedSourceRevision: pin, targetKey: 'qualified', targetVersionId: pin, expectedVersion: 'absent', enabled: true, reason: 'explicit current basis' }); expect(localStorage.getItem(key)).toBe(input.operationId); expect(wrapper!.text()).toContain('原操作待确认')
})
it('refresh only recovers the original mapping and never submits it again', async () => {
  localStorage.setItem(key, op); mock.operation.mockResolvedValue({ operationId: op, state: 'COMMITTED', result: { legacyVersionId: source, targetKey: 'qualified', formVersionId: pin, enabled: true, boundAtUtc: '2026-11-08T00:00:00Z' } })
  await open(); expect(mock.operation).toHaveBeenCalledWith(op); expect(mock.configure).not.toHaveBeenCalled(); expect(wrapper!.text()).toContain('已保存准确映射'); expect(wrapper!.text()).toContain('原历史仍未核实')
})
it('another tab unresolved UUID cannot be overwritten by a new intent', async () => {
  await prepare(); localStorage.setItem(key, op); mock.operation.mockResolvedValue(null); await click('保存当前来源映射')
  expect(mock.configure).not.toHaveBeenCalled(); expect(localStorage.getItem(key)).toBe(op); expect(wrapper!.text()).toContain('不代表没有效果')
  mock.close.mockResolvedValue({ operationId: op, state: 'CLOSED', result: null }); await click('关闭映射原操作'); expect(mock.close).toHaveBeenCalledWith(op); expect(wrapper!.text()).toContain('迟到配置不会生效')
})
it('identity replacement rejects an obsolete source preview', async () => {
  await open(); await wrapper!.get('input[aria-label="目标原生表单 Key"]').setValue('qualified'); await click('读取已核实的发布版本')
  let finish!: (value: unknown) => void; mock.sourcePreview.mockImplementation(() => new Promise(resolve => { finish = resolve })); await click('核对准确来源映射')
  mock.stamp = 'source-admin-two'; window.dispatchEvent(new Event('legacy-map-identity')); finish(preview()); await flushPromises(); expect(wrapper!.find('.mapping-preview').exists()).toBe(false); expect(mock.configure).not.toHaveBeenCalled()
})
