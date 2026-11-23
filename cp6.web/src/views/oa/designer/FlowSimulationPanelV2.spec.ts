// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import type { AuthoringView } from '@/api/oa/flowAuthoring'
import { flowSimulationApi as api, type FlowForecast } from '@/api/oa/flowSimulation'
import { AUTH_EVENT, newEpoch } from '@/auth/identity'
import Panel from './FlowSimulationPanelV2.vue'

vi.mock('@/api/oa/flowSimulation', () => ({ flowSimulationApi: { forecast: vi.fn() } }))
const draftId = '565b6d92-d4ad-4f34-bc88-d0c4d108d090'
const publishedId = '1d21e285-2fcb-45a1-a6a3-849816f9d713'
const current: AuthoringView = { definitionId: 'head', draftId, key: 'leave', version: 'token', enabled: false,
  fields: { SchemaJson: '{}', FormKey: null }, writableFields: [], diagnostics: [] }
const prediction: FlowForecast = {
  mode: 'EXPLANATION_ONLY', isExecutionPermit: false, definitionId: 'head', versionId: draftId, draftRevision: 'token',
  source: 'DRAFT_SIMULATION', starterBasis: 'CURRENT_ACTOR_AS_STARTER', knowledgeCutUtc: '2027-04-01T09:00:00Z',
  coverage: 'PARTIAL', visitedNodeCount: 2, totalNodeCount: 3, traversedEdgeCount: 1, totalEdgeCount: 2,
  nodes: [
    { nodeId: 'approval-private', type: 'approval', stages: [{ stageIndex: 0, state: 'REDACTED', people: [] }] },
    { nodeId: 'approval-later', type: 'approval', stages: [{ stageIndex: 0, state: 'ARRIVAL_TIME', people: [] }] },
  ],
  edges: [], dependencies: [], diagnostics: [], unvisited: [],
}
const mounted: ReturnType<typeof mount>[] = [], state = (w: any): any => w.vm.$.setupState
function setup(extra = {}) { const w = mount(Panel, { props: { current, dirty: false, disabled: false, ...extra } }); mounted.push(w); return w }
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear(); vi.mocked(api.forecast).mockResolvedValue(prediction) })
afterEach(() => { mounted.forEach(w => w.unmount()); mounted.length = 0 })

it('sends the exact saved draft and variable text and distinguishes redacted and arrival-time staff', async () => {
  const w = setup(); await w.get('button').trigger('click')
  const raw = '{"amount":9007199254740993.00}'
  await w.get('textarea').setValue(raw); await state(w).simulate()
  expect(api.forecast).toHaveBeenCalledWith('leave',
    { draftVersionId: draftId, expectedVersion: 'token', varsJson: raw, draftDependencies: [] }, expect.any(AbortSignal), false)
  expect(w.text()).toContain('仅解释')
  expect(w.text()).toContain('当前未获人员读取权限')
  expect(w.text()).toContain('到达时解析')
  expect(w.text()).toContain('approval-private')
  window.dispatchEvent(new StorageEvent('storage', { key: 'unrelated-ui-setting', newValue: 'changed' }))
  await flushPromises()
  expect(state(w).result).toBeDefined()
  expect(state(w).varsJson).toBe(raw)
  expect(sessionStorage.length).toBe(0)
})

it('offers an exact published pin and only sends the selected source', async () => {
  const w = setup({ current: { ...current, draftId: publishedId, published: true } })
  const published = { ...prediction, source: 'PUBLISHED_PIN' as const, versionId: publishedId, draftRevision: null }
  vi.mocked(api.forecast).mockResolvedValue(published)
  await w.get('button').trigger('click')
  await state(w).simulate()
  expect(api.forecast).toHaveBeenCalledWith('leave', { publishedVersionId: publishedId, varsJson: '{}' }, expect.any(AbortSignal), false)
  expect(w.text()).toContain('PUBLISHED_PIN')
})

it('blocks unsaved and hidden content and expires results on input or version changes', async () => {
  const w = setup({ dirty: true }); await state(w).simulate(); expect(api.forecast).not.toHaveBeenCalled()
  await w.setProps({ dirty: false, current: { ...current, fields: { FormKey: null } } })
  await state(w).simulate(); expect(api.forecast).not.toHaveBeenCalled()
  await w.setProps({ current }); await state(w).simulate(); expect(state(w).result).toBeDefined()
  state(w).varsJson = '{"changed":true}'; await flushPromises(); expect(state(w).result).toBeUndefined()
  await state(w).simulate(); await w.setProps({ current: { ...current, version: 'new-token' } }); expect(state(w).result).toBeUndefined()
})

it('ignores a late forecast and late export after identity invalidation and clears private state on current denial', async () => {
  let complete!: (value: FlowForecast) => void
  vi.mocked(api.forecast).mockImplementation(() => new Promise(resolve => { complete = resolve }))
  const w = setup()
  const first = state(w).simulate()
  await w.setProps({ current: { ...current, version: 'new-token' } })
  complete(prediction); await first
  expect(state(w).result).toBeUndefined()
  await w.setProps({ current })
  const exporting = state(w).simulate(true)
  newEpoch(); window.dispatchEvent(new Event(AUTH_EVENT)); complete(prediction); await exporting
  expect(state(w).result).toBeUndefined()
  expect(state(w).varsJson).toBe('{}')
  vi.mocked(api.forecast).mockRejectedValue({ response: { status: 403 } })
  await state(w).simulate(true)
  expect(vi.mocked(api.forecast).mock.calls.at(-1)?.at(-1)).toBe(true)
  expect(w.emitted('privateClear')).toBeDefined()
  expect(w.text()).not.toContain('approval-private')
  expect(state(w).result).toBeUndefined()
})

it('retains typed input for stale, invalid and temporary errors', async () => {
  const w = setup()
  state(w).open()
  state(w).varsJson = '{"amount":1}'
  await flushPromises()
  for (const status of [409, 400, 503]) {
    vi.mocked(api.forecast).mockRejectedValue({ response: { status } })
    await state(w).simulate()
    expect(state(w).varsJson).toBe('{"amount":1}')
    expect(state(w).result).toBeUndefined()
    expect(w.get('[role=alert]').text()).not.toBe('')
  }
})

it('clears a late export after an authoritative workspace policy change in the same session', async () => {
  let complete!: (value: FlowForecast) => void
  vi.mocked(api.forecast).mockImplementation(() => new Promise(resolve => { complete = resolve }))
  const w = setup()
  state(w).open()
  state(w).varsJson = '{"memo":"临时预演数据"}'
  const pending = state(w).simulate(true)
  const signal = vi.mocked(api.forecast).mock.calls.at(-1)?.[2]
  window.dispatchEvent(new Event('cp6-workspace-authority-changed'))
  expect(signal?.aborted).toBe(true)
  complete(prediction)
  await pending
  expect(state(w).result).toBeUndefined()
  expect(state(w).varsJson).toBe('{}')
  expect(w.text()).not.toContain('approval-private')
})

it.each([
  [503, 'FORECAST_AUTHORITY_UNPROVEN', '预演权限映射尚未采用'],
  [403, 'FORECAST_ACTION_FORBIDDEN', '当前没有流程预演权限'],
])('shows the forecast block and keeps independent definition read on %s', async (status, code, message) => {
  const w = setup()
  state(w).open()
  state(w).varsJson = '{"memo":"合成私有变量"}'
  await state(w).simulate()
  expect(w.text()).toContain('approval-private')
  vi.mocked(api.forecast).mockRejectedValue({ response: { status, data: { code } } })
  await state(w).simulate(true)
  expect(w.get('[role=alert]').text()).toContain(message)
  expect(w.text()).not.toContain('approval-private')
  expect(state(w).varsJson).toBe('{}')
  expect(w.emitted('privateClear')).toBeUndefined()
})
