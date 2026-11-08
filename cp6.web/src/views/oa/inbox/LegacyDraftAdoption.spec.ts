// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, afterEach, expect, it, vi } from 'vitest'
import ElementPlus from 'element-plus'
import Panel from './LegacyInboxDraft.vue'
const mock = vi.hoisted(() => ({ list: vi.fn(), preview: vi.fn(), send: vi.fn(), original: vi.fn(), close: vi.fn(), confirm: vi.fn(), stamp: 'legacy-owner-one' }))
vi.mock('@/api/oa/legacyPersonalDrafts', () => ({ legacyPersonalDrafts: mock.list }))
vi.mock('@/api/oa/legacyDraftAdoption', () => ({ legacyDraftAdoptionApi: { preview: mock.preview } }))
vi.mock('@/api/oa/personalForm', async load => { const actual = await load<typeof import('@/api/oa/personalForm')>(); return { ...actual, personalFormApi: { ...actual.personalFormApi, send: mock.send, original: mock.original, close: mock.close } } })
vi.mock('@/auth/identity', () => ({ AUTH_EVENT: 'legacy-adoption-identity', identityStamp: () => mock.stamp, requireIdentity: (value: string) => { if (value !== mock.stamp) throw new Error('STALE') } }))
vi.mock('element-plus', async load => ({ ...await load<typeof import('element-plus')>(), ElMessageBox: { confirm: mock.confirm } }))
const source = '11111111-1111-1111-1111-111111111111', target = '22222222-2222-2222-2222-222222222222', token = '33333333-3333-3333-3333-333333333333'
const preview = () => ({ legacyDraftId: source, legacyVersionId: token, sourceVersion: token, mappingVersion: token, sourceEvidenceState: 'UNVERIFIED_LEGACY', targetKey: 'adopted-form', formVersionId: token, targetVersion: 2, schemaJson: '{"fields":[]}', dataJson: '{"visible":"authorized old value"}', title: 'old title', fieldMask: {}, canAdopt: true, blockers: [], adoptUntilUtc: '2027-01-01T00:00:00Z' })
let wrapper: ReturnType<typeof mount> | undefined
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear(); mock.stamp = 'legacy-owner-one'; mock.confirm.mockResolvedValue('confirm')
  mock.list.mockResolvedValue({ items: [{ id: source, title: 'old title', formKey: 'old-form', recordedVersion: 3, status: 0, source: 'LEGACY_HEAD_BACKFILL' }], total: 1, page: 1, pageSize: 20 }); mock.preview.mockResolvedValue(preview())
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks() })
async function open() { wrapper = mount(Panel, { global: { plugins: [ElementPlus], stubs: { RouterLink: { template: '<a><slot /></a>' }, PersonalFormEditor: true } } }); await flushPromises() }
async function click(name: string) { await wrapper!.findAll('button').find(x => x.text() === name)!.trigger('click'); await flushPromises() }
async function prepare() { await open(); await click('查看受控内容'); await wrapper!.get('input[aria-label="旧草稿采用说明"]').setValue('explicit copy reason') }
it('reads only projected content and cancellation leaves the source without sending a copy', async () => {
  await prepare(); expect(wrapper!.text()).toContain('authorized old value'); expect(wrapper!.text()).toContain('历史仍未核实')
  mock.confirm.mockRejectedValueOnce('cancel'); await click('复制为新本人草稿'); expect(mock.send).not.toHaveBeenCalled(); expect(sessionStorage.length).toBe(0)
})
it('a lost reply keeps only original metadata and refresh recovers once without reissuing the copy', async () => {
  await prepare(); mock.send.mockRejectedValue(new Error('real reply lost')); await click('复制为新本人草稿')
  const request = JSON.parse(mock.send.mock.calls[0]![2]); expect(request.body).toEqual({ mappingVersion: token, reason: 'explicit copy reason' }); expect(request.expectedVersion).toBe(token)
  const metadata = JSON.parse(sessionStorage.getItem('cp6-legacy-draft-adoption-pending')!); expect(Object.keys(metadata).sort()).toEqual(['commandId', 'identity', 'operation', 'resource']); expect(metadata.resource).toBe(source)
  mock.original.mockResolvedValue({ commandId: metadata.commandId, outcome: 'SUCCEEDED', resourceId: target, result: { draftId: target, formKey: 'adopted-form', formVersion: 2 } })
  wrapper!.unmount(); wrapper = undefined; await open(); expect(mock.original).toHaveBeenCalledWith('FORM_DRAFT_ADOPT_LEGACY', source, metadata.commandId, expect.any(AbortSignal)); expect(mock.send).toHaveBeenCalledTimes(1); expect(wrapper!.text()).toContain('已复制为新草稿'); expect(wrapper!.text()).toContain(target)
})
it('an identity change discards a late private preview and offers no stale adoption action', async () => {
  await open(); let finish!: (value: unknown) => void; mock.preview.mockImplementation(() => new Promise(resolve => { finish = resolve })); await click('查看受控内容')
  mock.stamp = 'legacy-owner-two'; window.dispatchEvent(new Event('legacy-adoption-identity')); finish(preview()); await flushPromises()
  expect(wrapper!.text()).not.toContain('authorized old value'); expect(wrapper!.findAll('button').some(b => b.text() === '复制为新本人草稿')).toBe(false)
})
it('a failed current disclosure clears previously returned content instead of displaying it as still authorized', async () => {
  await prepare(); mock.preview.mockRejectedValue({ response: { status: 403 } }); await click('查看受控内容')
  expect(wrapper!.text()).not.toContain('authorized old value'); expect(wrapper!.text()).toContain('访问权限'); expect(mock.send).not.toHaveBeenCalled()
})
it('a failed directory refresh clears the old content preview and its adoption action', async () => {
  await prepare(); mock.list.mockRejectedValue(new Error('directory is no longer readable')); await click('刷新')
  expect(wrapper!.text()).not.toContain('authorized old value'); expect(wrapper!.findAll('button').some(b => b.text() === '复制为新本人草稿')).toBe(false); expect(mock.send).not.toHaveBeenCalled()
})
