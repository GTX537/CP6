// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { legacyDefinitionsApi as api, type LegacyDefinition } from '@/api/oa/legacyDefinitions'
import { AUTH_EVENT, newEpoch } from '@/auth/identity'
import View from './LegacyDefinitionsViewV2.vue'
vi.mock('@/api/oa/legacyDefinitions', () => ({ legacyDefinitionsApi: { read: vi.fn() } }))
const row = (overrides: Partial<LegacyDefinition> = {}): LegacyDefinition => ({ kind: 'FLOW', definitionId: 'head', key: 'legacy', versionId: null, recordedVersion: 7,
  recordedStatus: null, source: 'CURRENT_HEAD_RECONSTRUCTION', fields: { Name: 'Old', SchemaJson: '{"number":9007199254740993.00}' },
  recordedPublishedAtUtc: null, recordedPublishedBy: null, evidenceState: 'UNVERIFIED_LEGACY', canWrite: false, ...overrides })
const mounted: ReturnType<typeof mount>[] = [], state = (w: any): any => w.vm.$.setupState
function setup() { const w = mount(View, { props: { kind: 'FLOW' } }); mounted.push(w); return w }
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); vi.mocked(api.read).mockResolvedValue([row()]) })
afterEach(() => { mounted.forEach(w => w.unmount()); mounted.length = 0 })
it('separates a current head, reconstructed backfill and legacy snapshot without offering a write action', async () => {
  const w = setup(); await flushPromises()
  expect(w.text()).toContain('没有准确历史版本绑定'); expect(w.text()).toContain('9007199254740993.00')
  vi.mocked(api.read).mockResolvedValue([row({ versionId: 'v7', source: 'LEGACY_HEAD_BACKFILL', fields: { Name: 'Old' } })])
  await state(w).load('legacy'); expect(w.text()).toContain('回填时间不能当作历史发布时间'); expect(w.find('pre').exists()).toBe(false)
  vi.mocked(api.read).mockResolvedValue([row({ versionId: 'v6', source: 'LEGACY_VERSION_SNAPSHOT', recordedPublishedBy: 'recorded-user' })])
  await state(w).load('legacy'); expect(w.text()).toContain('recorded-user'); expect(w.text()).toContain('未被认定为已核实事实')
  expect(w.findAll('button').some(b => /保存|发布|创建|克隆/.test(b.text()))).toBe(false)
})
it('late selection and identity responses never restore prior private content', async () => {
  const w = setup(); await flushPromises(); let complete!: (value: LegacyDefinition[]) => void
  vi.mocked(api.read).mockImplementationOnce(() => new Promise(resolve => { complete = resolve })).mockResolvedValueOnce([row({ key: 'new' })])
  const old = state(w).load('old'); await state(w).load('new'); complete([row({ key: 'old' })]); await old
  expect(state(w).rows[0].key).toBe('new')
  vi.mocked(api.read).mockImplementationOnce(() => new Promise(resolve => { complete = resolve }))
  const pending = state(w).load('private'); newEpoch(); window.dispatchEvent(new Event(AUTH_EVENT)); complete([row()]); await pending
  expect(state(w).rows).toEqual([]); expect(w.find('pre').exists()).toBe(false)
})
it('current denial clears data and explains unknown legacy ownership instead of treating a creator label as access', async () => {
  const w = setup(); await flushPromises(); vi.mocked(api.read).mockRejectedValue({ response: { status: 403, data: { code: 'LEGACY_OWNER_SCOPE_REQUIRED' } } })
  await state(w).load('legacy'); expect(state(w).rows).toEqual([]); expect(w.text()).toContain('全部数据范围权限')
})
