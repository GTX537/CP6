// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { nativeTaskBatchApi as api, type TransferPreview } from '@/api/oa/nativeTaskBatch'
import { newEpoch } from '@/auth/identity'
import Panel from './NativeTransferBatchPanel.vue'
vi.mock('@/api/oa/nativeTaskBatch', () => ({ nativeTaskBatchApi: { preview: vi.fn(), original: vi.fn(), execute: vi.fn(), report: vi.fn() } }))
vi.mock('../query/QueryUserPicker.vue', () => ({ default: { template: '<div />' } }))
const task = '11111111-1111-1111-1111-111111111111'
const snapshot: TransferPreview = { batchId: 'batch', previewDigest: 'frozen', previewedAt: '2026-11-03T00:00:00Z', filter: { fromUserId: 'from', toUserId: 'to', reason: 'private reason', flowKey: null, beforeUtc: null, explicitTaskIds: [task] }, items: [{ taskId: task, instanceId: 'instance', contextKind: 'SFS', flowKey: 'flow', taskVersion: 'task-v', instanceVersion: 'instance-v', itemCommandId: 'fixed-item' }] }
const wrappers: ReturnType<typeof mount>[] = []
function setup() { const w = mount(Panel); wrappers.push(w); const s: any = (w.vm.$ as any).setupState; s.from = 'from'; s.to = 'to'; s.reason = 'private reason'; s.taskIds = task; return { w, s } }
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear(); vi.mocked(api.preview).mockResolvedValue(structuredClone(snapshot)) })
afterEach(() => { wrappers.splice(0).forEach(w => w.unmount()); vi.restoreAllMocks() })
it('confirms only the frozen preview and recovers per-item outcomes after an unknown result without storing private inputs', async () => {
  const { s } = setup(); await s.inspect(); expect(api.execute).not.toHaveBeenCalled()
  expect(sessionStorage.getItem('cp6-native-transfer-batch')).not.toContain('private reason'); expect(sessionStorage.getItem('cp6-native-transfer-batch')).not.toContain(task)
  s.to = 'later edit'; vi.mocked(api.execute).mockRejectedValue({ response: { status: 503 } }); await s.execute()
  expect(api.execute).toHaveBeenCalledWith('batch', 'frozen'); s.reset(); expect(s.recovery).toBeDefined()
  vi.mocked(api.report).mockResolvedValue({ batchId: 'batch', items: [{ taskId: task, itemCommandId: 'fixed-item', state: 'SUCCEEDED', error: null, result: null }] }); await s.refresh(); s.reset(); expect(s.recovery).toBeUndefined()
})
it('restores only the preview identity after reload and retains the recovery marker after current access is denied', async () => {
  const first = setup(); await first.s.inspect(); first.w.unmount()
  const { s } = setup(); expect(s.preview).toBeUndefined(); expect(s.canExecute).toBe(false)
  vi.mocked(api.original).mockResolvedValue(structuredClone(snapshot)); await s.recoverPreview(); expect(s.canExecute).toBe(true)
  vi.mocked(api.execute).mockRejectedValue({ response: { status: 403 } }); await s.execute()
  expect(s.preview).toBeUndefined(); expect(s.reason).toBe(''); expect(s.recovery.batchId).toBe('batch')
})
it('does not send without storage and discards a late preview after an identity change', async () => {
  const { s } = setup(); const spy = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw Error('quota') })
  await s.inspect(); expect(api.preview).not.toHaveBeenCalled(); spy.mockRestore()
  let finish!: (value: TransferPreview) => void; vi.mocked(api.preview).mockImplementation(() => new Promise(r => { finish = r }))
  const call = s.inspect(); newEpoch(); finish(structuredClone(snapshot)); await call
  expect(s.preview).toBeUndefined(); expect(s.recovery).toBeUndefined()
})
it('retries unknown preview with original bytes and rejects an explicit list larger than 500', async () => {
  const { s } = setup(); s.taskIds = Array.from({ length: 501 }, () => crypto.randomUUID()).join(','); await s.inspect(); expect(api.preview).not.toHaveBeenCalled()
  s.taskIds = task; vi.mocked(api.preview).mockRejectedValue({ response: { status: 503 } }); await s.inspect(); const raw = vi.mocked(api.preview).mock.calls[0]![0]
  s.reason = 'changed'; await s.recoverPreview(true); expect(vi.mocked(api.preview).mock.calls[1]![0]).toBe(raw)
})
