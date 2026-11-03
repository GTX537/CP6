// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { definitionComparisonApi as api, type DefinitionComparison } from '@/api/oa/definitionComparison'
import { AUTH_EVENT, newEpoch } from '@/auth/identity'
import Panel from './DefinitionComparisonPanelV2.vue'
vi.mock('@/api/oa/definitionComparison', () => ({ definitionComparisonApi: { compare: vi.fn() } }))
const current = { key: 'orders', version: 'head-token' }, versions = [{ versionId: 'one', version: 1 }, { versionId: 'two', version: 2 }]
const metadata = { versionId: 'one', version: 1, nameSnapshot: 'Orders', publishedAtUtc: '2026-11-01T00:00:00Z', publisherRef: 'author', schemaDigest: 'hash', evidenceState: 'ORIGINAL_PUBLICATION', dependencyRefs: [], status: 'Published' as const }
const comparison: DefinitionComparison = { kind: 'FORM', key: 'orders', left: metadata, right: { ...metadata, versionId: 'two', version: 2 },
  scope: 'ALL_FIELDS', omittedFields: [], dependenciesVisible: true, page: 1, pageSize: 200, totalChanges: 1, hasMore: false,
  changes: [{ path: '/SchemaJson/fields/@amount/value', category: 'FIELD', change: 'CHANGED', beforeJson: '9007199254740993.00', afterJson: '9007199254740992.00' }] }
const mounted: ReturnType<typeof mount>[] = [], state = (w: any): any => w.vm.$.setupState
function setup() { const w = mount(Panel, { props: { kind: 'FORM', current, versions, disabled: false } }); mounted.push(w); return w }
async function choose(w: ReturnType<typeof mount>) { const selects = w.findAll('select'); await selects[0]!.setValue('one'); await selects[1]!.setValue('two') }
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear(); vi.mocked(api.compare).mockResolvedValue(comparison) })
afterEach(() => { mounted.forEach(w => w.unmount()); mounted.length = 0 })
it('requires explicit versions and preserves exact text; selections remain usable across history pages', async () => {
  const w = setup(); await state(w).compare(); expect(api.compare).not.toHaveBeenCalled()
  await choose(w); await state(w).compare()
  expect(api.compare).toHaveBeenCalledWith('FORM', 'orders', 'one', 'two', 1, expect.any(AbortSignal))
  expect(w.text()).toContain('9007199254740993.00'); expect(w.text()).toContain('9007199254740992.00')
  await w.setProps({ versions: [{ versionId: 'older', version: 0 }] }); expect(state(w).result).toBeUndefined()
  expect(w.findAll('select')[0]!.element.value).toBe('one'); expect(w.findAll('select')[1]!.element.value).toBe('two')
  expect(sessionStorage.length).toBe(0)
})
it('never claims complete equality for restricted results and paginates exact selected versions', async () => {
  const w = setup(); await choose(w)
  vi.mocked(api.compare).mockResolvedValue({ ...comparison, scope: 'VISIBLE_FIELDS_ONLY', omittedFields: ['SchemaJson'], dependenciesVisible: false, totalChanges: 0, changes: [] })
  await state(w).compare(); expect(w.text()).toContain('不能据此判断完整版本相同'); expect(w.text()).toContain('依赖未参与比较')
  vi.mocked(api.compare).mockResolvedValue({ ...comparison, page: 2, totalChanges: 201 }); await state(w).compare(2)
  expect(api.compare).toHaveBeenLastCalledWith('FORM', 'orders', 'one', 'two', 2, expect.any(AbortSignal))
})
it('discards late data when scope, version selection or identity changes; denial clears private state', async () => {
  let complete!: (value: DefinitionComparison) => void
  vi.mocked(api.compare).mockImplementation(() => new Promise(resolve => { complete = resolve }))
  const w = setup(); await choose(w); const first = state(w).compare()
  await w.setProps({ current: { ...current, version: 'new-head' } }); complete(comparison); await first; expect(state(w).result).toBeUndefined()
  await choose(w); const second = state(w).compare(); newEpoch(); window.dispatchEvent(new Event(AUTH_EVENT)); complete(comparison); await second
  expect(state(w).left).toBe(''); expect(state(w).result).toBeUndefined()
  await choose(w); vi.mocked(api.compare).mockRejectedValue({ response: { status: 403 } }); await state(w).compare()
  expect(w.emitted('privateClear')).toBeDefined(); expect(state(w).left).toBe(''); expect(w.text()).not.toContain('9007199254740993.00')
})
it('invalidates results during parent operations and reports read failures without substituting empty success', async () => {
  const w = setup(); await choose(w); await state(w).compare()
  await w.setProps({ disabled: true }); expect(state(w).result).toBeUndefined(); await state(w).compare(); expect(api.compare).toHaveBeenCalledTimes(1)
  await w.setProps({ disabled: false }); vi.mocked(api.compare).mockRejectedValue({ response: { status: 503 } }); await state(w).compare()
  expect(w.get('[role=alert]').text()).toContain('未能完成比较'); expect(w.text()).not.toContain('没有差异')
})
