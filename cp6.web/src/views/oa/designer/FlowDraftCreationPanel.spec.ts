// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { flowDraftApi } from '@/api/oa/flowDraft'
import { AUTH_EVENT, newEpoch } from '@/auth/identity'
import Panel from './FlowDraftCreationPanel.vue'
vi.mock('@/api/oa/flowDraft', () => ({ flowDraftApi: { context: vi.fn(), create: vi.fn(), operation: vi.fn() } }))
const context = () => ({ definitionId: 'definition', headVersion: 'head', draftVersionId: null, sources: [
  { versionId: 'v2', version: 2, name: 'Latest', schemaDigest: 'digest2' },
  { versionId: 'v1', version: 1, name: 'Original', schemaDigest: 'digest1' },
] })
const result = (cmd: any) => ({ owner: 'Main.Workflow.Draft.v1', operationId: cmd.operationId, sourceVersionId: cmd.sourceVersionId,
  sourceDigest: cmd.sourceVersionId === 'v1' ? 'digest1' : 'digest2', draft: { definitionId: 'definition', versionId: 'next', version: 3 } })
const mounted: ReturnType<typeof mount>[] = []
async function setup() { const w = mount(Panel, { props: { flowKey: 'leave' }, global: { directives: { permission: () => {} } } }); mounted.push(w); await flushPromises(); return w }
const button = (w: any, label: string) => w.findAll('button').find((x: any) => x.text() === label)!
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); vi.mocked(flowDraftApi.context).mockResolvedValue(context()) })
afterEach(() => { mounted.forEach(w => w.unmount()); mounted.length = 0 })
it('only reads on mount and submits the explicitly selected historical source', async () => {
  vi.mocked(flowDraftApi.create).mockImplementation(async (_key, cmd) => result(cmd))
  const w = await setup(); expect(flowDraftApi.create).not.toHaveBeenCalled()
  await w.get('select').setValue('v1'); await button(w, '从所选版本创建草稿').trigger('click'); await flushPromises()
  expect(flowDraftApi.create).toHaveBeenCalledWith('leave', expect.objectContaining({ sourceVersionId: 'v1', expectedHeadVersion: 'head' }))
  expect(w.emitted('created')).toHaveLength(1)
})
it('keeps the original intent across uncertain response and retry', async () => {
  vi.mocked(flowDraftApi.create).mockRejectedValueOnce(new Error('offline')).mockImplementationOnce(async (_key, cmd) => result(cmd))
  const w = await setup(); await button(w, '从所选版本创建草稿').trigger('click'); await flushPromises()
  expect(w.get('select').attributes('disabled')).toBeDefined()
  await button(w, '以原意图重试创建').trigger('click'); await flushPromises()
  expect(vi.mocked(flowDraftApi.create).mock.calls[0]).toEqual(vi.mocked(flowDraftApi.create).mock.calls[1])
  expect(w.emitted('created')).toHaveLength(1)
})
it('recovers the saved original without a second create', async () => {
  vi.mocked(flowDraftApi.create).mockRejectedValue(new Error('lost response'))
  const w = await setup(); await button(w, '从所选版本创建草稿').trigger('click'); await flushPromises()
  const cmd = vi.mocked(flowDraftApi.create).mock.calls[0]![1]
  vi.mocked(flowDraftApi.operation).mockResolvedValue({ state: 'SUCCEEDED', result: result(cmd) })
  await button(w, '查询原创建结果').trigger('click'); await flushPromises()
  expect(flowDraftApi.create).toHaveBeenCalledTimes(1); expect(w.emitted('created')).toHaveLength(1)
})
it('NOT_OBSERVED does not declare cancellation or generate another intent', async () => {
  vi.mocked(flowDraftApi.create).mockRejectedValue(new Error('lost response'))
  vi.mocked(flowDraftApi.operation).mockResolvedValue({ state: 'NOT_OBSERVED', result: null })
  const w = await setup(); await button(w, '从所选版本创建草稿').trigger('click'); await flushPromises()
  await button(w, '查询原创建结果').trigger('click'); await flushPromises()
  expect(w.text()).toContain('不能证明未创建'); expect(w.emitted('created')).toBeUndefined()
  expect(button(w, '以原意图重试创建')).toBeDefined()
})
it('stale head requires an explicit refresh and never silently recreates', async () => {
  vi.mocked(flowDraftApi.create).mockRejectedValue({ response: { status: 412 } })
  const w = await setup(); await button(w, '从所选版本创建草稿').trigger('click'); await flushPromises()
  expect(w.find('select').exists()).toBe(false); expect(w.text()).toContain('重新读取后明确选择')
  expect(flowDraftApi.create).toHaveBeenCalledTimes(1); expect(flowDraftApi.context).toHaveBeenCalledTimes(1)
})
it('does not create when a concurrent draft is already present', async () => {
  vi.mocked(flowDraftApi.context).mockResolvedValue({ ...context(), draftVersionId: 'another' })
  const w = await setup(); expect(w.text()).toContain('已有草稿'); expect(w.find('select').exists()).toBe(false)
  await button(w, '加载已有草稿').trigger('click'); expect(flowDraftApi.create).not.toHaveBeenCalled()
})
it('rejects mismatched original source digest', async () => {
  vi.mocked(flowDraftApi.create).mockImplementation(async (_key, cmd) => ({ ...result(cmd), sourceDigest: 'wrong' }))
  const w = await setup(); await button(w, '从所选版本创建草稿').trigger('click'); await flushPromises()
  expect(w.emitted('created')).toBeUndefined(); expect(button(w, '查询原创建结果')).toBeDefined()
})
it('drops late response after identity change and clears sensitive context', async () => {
  let complete!: (value: any) => void
  vi.mocked(flowDraftApi.create).mockImplementation(() => new Promise(resolve => { complete = resolve }))
  const w = await setup(); await button(w, '从所选版本创建草稿').trigger('click')
  const cmd = vi.mocked(flowDraftApi.create).mock.calls[0]![1]; newEpoch(); complete(result(cmd)); await flushPromises()
  expect(w.emitted('created')).toBeUndefined(); expect(w.emitted('denied')).toBeDefined(); expect(w.find('select').exists()).toBe(false)
})
it('revoked action clears the pending intent without reporting success', async () => {
  vi.mocked(flowDraftApi.create).mockRejectedValue({ response: { status: 403 } })
  const w = await setup(); await button(w, '从所选版本创建草稿').trigger('click'); await flushPromises()
  expect(w.emitted('denied')).toBeDefined(); expect(w.emitted('created')).toBeUndefined(); expect(w.find('select').exists()).toBe(false)
  window.dispatchEvent(new Event(AUTH_EVENT))
})
