// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import type { AuthoringView } from '@/api/oa/flowAuthoring'
import { flowSimulationApi as api, type FlowSimulation } from '@/api/oa/flowSimulation'
import { AUTH_EVENT, newEpoch } from '@/auth/identity'
import Panel from './FlowSimulationPanelV2.vue'
vi.mock('@/api/oa/flowSimulation', () => ({ flowSimulationApi: { simulate: vi.fn() } }))
const current: AuthoringView = { definitionId: 'head', draftId: 'draft', key: 'leave', version: 'token', enabled: false,
  fields: { SchemaJson: '{}', FormKey: null }, writableFields: [], diagnostics: [] }
const prediction: FlowSimulation = { mode: 'EXPLANATION_ONLY', definitionId: 'head', draftVersionId: 'draft', expectedVersion: 'token', assumptions: [],
  nodes: [{ nodeId: 'approval-private', type: 'approval', stages: [] }], edges: [], dependencies: [], diagnostics: [{ path: '/nodes/1', code: 'APPROVER_AUTHORITY_UNPROVEN', evidenceState: 'UNPROVEN' }] }
const mounted: ReturnType<typeof mount>[] = [], state = (w: any): any => w.vm.$.setupState
function setup(extra = {}) { const w = mount(Panel, { props: { current, dirty: false, disabled: false, ...extra } }); mounted.push(w); return w }
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear(); vi.mocked(api.simulate).mockResolvedValue(prediction) })
afterEach(() => { mounted.forEach(w => w.unmount()); mounted.length = 0 })
it('sends exact mock numeric text for the selected saved draft and explains unresolved authority', async () => {
  const w = setup(); await w.get('button').trigger('click')
  const raw = '{"amount":9007199254740993.00}'
  await w.get('textarea').setValue(raw); await state(w).simulate()
  expect(api.simulate).toHaveBeenCalledWith('leave', { draftVersionId: 'draft', expectedVersion: 'token', varsJson: raw }, expect.any(AbortSignal))
  expect(w.text()).toContain('仅解释'); expect(w.text()).toContain('未披露候选人'); expect(w.text()).toContain('approval-private')
  window.dispatchEvent(new StorageEvent('storage', { key: 'unrelated-ui-setting', newValue: 'changed' })); await flushPromises()
  expect(state(w).result).toBeDefined(); expect(state(w).varsJson).toBe(raw)
  expect(sessionStorage.length).toBe(0)
})
it('blocks unsaved, published and hidden content; invalidates predictions when the draft or variables change', async () => {
  const w = setup({ dirty: true }); await state(w).simulate(); expect(api.simulate).not.toHaveBeenCalled()
  await w.setProps({ dirty: false, current: { ...current, published: true } }); await state(w).simulate(); expect(api.simulate).not.toHaveBeenCalled()
  await w.setProps({ current: { ...current, fields: { FormKey: null } } }); await state(w).simulate(); expect(api.simulate).not.toHaveBeenCalled()
  await w.setProps({ current }); await state(w).simulate(); expect(state(w).result).toBeDefined()
  state(w).varsJson = '{"changed":true}'; await flushPromises(); expect(state(w).result).toBeUndefined()
  await state(w).simulate(); await w.setProps({ current: { ...current, version: 'new-token' } }); expect(state(w).result).toBeUndefined()
})
it('discards late replies after version or identity changes and clears private state after denial', async () => {
  let complete!: (value: FlowSimulation) => void
  vi.mocked(api.simulate).mockImplementation(() => new Promise(resolve => { complete = resolve }))
  const w = setup(); const first = state(w).simulate(); await w.setProps({ current: { ...current, version: 'new-token' } }); complete(prediction); await first
  expect(state(w).result).toBeUndefined()
  const second = state(w).simulate(); newEpoch(); window.dispatchEvent(new Event(AUTH_EVENT)); complete(prediction); await second
  expect(state(w).result).toBeUndefined(); expect(state(w).varsJson).toBe('{}')
  vi.mocked(api.simulate).mockRejectedValue({ response: { status: 403 } }); await state(w).simulate()
  expect(w.emitted('privateClear')).toBeDefined(); expect(state(w).result).toBeUndefined()
})
it('retains input and reports stale, invalid and unavailable results without claiming successful prediction', async () => {
  const w = setup(); state(w).open(); state(w).varsJson = '{"amount":1}'; await flushPromises()
  for (const status of [409, 400, 503]) {
    vi.mocked(api.simulate).mockRejectedValue({ response: { status } }); await state(w).simulate()
    expect(state(w).varsJson).toBe('{"amount":1}'); expect(state(w).result).toBeUndefined(); expect(w.get('[role=alert]').text()).not.toBe('')
  }
})
