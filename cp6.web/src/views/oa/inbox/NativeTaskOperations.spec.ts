// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { nativeTaskOperationsApi as api, type OperationContext, type ReturnedContext } from '@/api/oa/nativeTaskOperations'
import { newEpoch } from '@/auth/identity'
import { FormNumber } from '../catalog/personalFormJson'
import TaskPanel from './NativeTaskOperations.vue'
import ReturnedPanel from './ReturnedFormPanel.vue'
import ResolutionPanel from './PendingResolutionPanel.vue'
vi.mock('@/api/oa/nativeTaskOperations', () => ({ nativeTaskOperationsApi: { context: vi.fn(), preview: vi.fn(), returned: vi.fn(), resolutions: vi.fn(), resolution: vi.fn(), send: vi.fn(), original: vi.fn(), close: vi.fn() } }))
vi.mock('../catalog/PersonalFormFields.vue', () => ({ default: { props: ['fields', 'modelValue', 'mask', 'disabled'], template: '<div />' } }))
const task = '11111111-1111-1111-1111-111111111111', instance = '22222222-2222-2222-2222-222222222222'
const context: OperationContext = { task: { taskId: task, instanceId: instance, taskStatus: 0, instanceStatus: 0, taskRowVersion: 'task-version', instanceRowVersion: 'instance-version' }, allowedActions: ['transfer', 'sendback', 'addsign'], candidates: [{ userId: 'target', name: '合格人员' }], sendBackTargets: [{ targetKind: 'STARTER', targetNodeId: null, name: '发起人' }, { targetKind: 'NODE', targetNodeId: 'a', name: '审核' }] }
const returned: ReturnedContext = { instanceId: instance, formDataId: 'data', returnRevision: 2, executionCycleId: 'cycle', formDefVersionId: 'form', flowDefVersionId: 'flow', instanceVersion: 'rv', formDataVersion: 'dv', name: '退回申请', schemaJson: '{"fields":[{"name":"amount","type":"number"}]}', dataJson: '{"amount":9007199254740993.123456789}', fieldMask: { amount: 'edit' }, canEdit: true, canResubmit: true, reasons: [] }
const wrappers: ReturnType<typeof mount>[] = [], state = (w: any): any => w.vm.$.setupState
function setup(component: any = TaskPanel, props: any = { taskId: task, contextKind: 'SFS' }) {
  const w = mount(component, { props, global: { stubs: { ElButton: { template: '<button><slot /></button>' } } } }); wrappers.push(w); return w
}
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear(); vi.mocked(api.context).mockResolvedValue(structuredClone(context)); vi.mocked(api.returned).mockResolvedValue(structuredClone(returned)) })
afterEach(() => { wrappers.splice(0).forEach(w => w.unmount()); vi.restoreAllMocks() })
it('freezes exact transfer versions and retries the original bytes after an unknown outcome without persisting the reason', async () => {
  const w = setup(), s = state(w); await s.load(); await flushPromises(); s.target = 'target'; s.reason = 'private transfer reason'
  vi.mocked(api.send).mockRejectedValue({ response: { status: 503 } }); await s.submit()
  const [intent, raw] = vi.mocked(api.send).mock.calls[0]!
  expect(JSON.parse(raw)).toMatchObject({ expectedVersion: 'instance-version', body: { taskId: task, toUserId: 'target', expectedTaskVersion: 'task-version' } })
  expect(sessionStorage.getItem('cp6-native-task-operation')).not.toContain('private transfer reason')
  s.reason = 'later edit'; await s.submit(); expect(api.send).toHaveBeenCalledTimes(1)
  await s.op.recover('send'); expect(vi.mocked(api.send).mock.calls[1]).toEqual([intent, raw])
  vi.mocked(api.original).mockResolvedValue(null); await s.op.recover('original'); expect(s.pending.command).toBe(intent.command)
  vi.mocked(api.close).mockResolvedValue({ commandId: intent.command, outcome: 'CLOSED', result: null }); await s.op.recover('close'); expect(s.pending).toBeUndefined()
})
it('requires a new preview after changing the sendback target and submits only the confirmed digest', async () => {
  const w = setup(), s = state(w); await s.load(); await flushPromises(); s.action = 'sendback'; await flushPromises(); s.reason = 'return'
  await s.submit(); expect(api.send).not.toHaveBeenCalled()
  vi.mocked(api.preview).mockResolvedValue({ ...context.sendBackTargets[0]!, taskId: task, instanceId: instance, scope: 'ROOT_INSTANCE', affectedTaskIds: [task], affectedTokenIds: ['token'], wholeInstanceRefill: true, taskVersion: 'fresh-task', instanceVersion: 'fresh-instance', previewDigest: 'exact-preview' })
  await s.inspect(); expect(s.preview.previewDigest).toBe('exact-preview'); s.backIndex = 1; await flushPromises(); expect(s.preview).toBeUndefined()
  await s.inspect(); vi.mocked(api.send).mockRejectedValue({ response: { status: 409 } }); await s.submit()
  expect(JSON.parse(vi.mocked(api.send).mock.calls[0]![1])).toMatchObject({ expectedVersion: 'fresh-instance', body: { previewDigest: 'exact-preview', expectedTaskVersion: 'fresh-task' } })
})
it('restores only the original command after reload and clears private context on current denial while keeping recovery', async () => {
  const first = setup(), s = state(first); await s.load(); await flushPromises(); s.target = 'target'; s.reason = 'private'
  vi.mocked(api.send).mockRejectedValue({ response: { status: 503 } }); await s.submit(); const command = s.pending.command; first.unmount()
  const w = setup(), restored = state(w); expect(restored.pending.command).toBe(command); expect(restored.canRetry).toBe(false)
  await restored.op.recover('send'); expect(api.send).toHaveBeenCalledTimes(1)
  vi.mocked(api.original).mockRejectedValue({ response: { status: 403 } }); await restored.op.recover('original')
  expect(restored.context).toBeUndefined(); expect(restored.pending.command).toBe(command); expect(w.emitted('privateClear')).toHaveLength(1)
})
it('does not send when recovery storage fails and ignores a late result after identity changes', async () => {
  const w = setup(), s = state(w); await s.load(); await flushPromises(); s.target = 'target'; s.reason = 'reason'
  const spy = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') }); await s.submit(); expect(api.send).not.toHaveBeenCalled(); spy.mockRestore()
  const other = setup(), x = state(other); await x.load(); await flushPromises(); x.target = 'target'; x.reason = 'reason'; let resolve!: (v: any) => void
  vi.mocked(api.send).mockImplementation(() => new Promise(r => { resolve = r })); const sending = x.submit(); await flushPromises(); const command = x.pending.command
  newEpoch(); resolve({ commandId: command, outcome: 'SUCCEEDED', result: {} }); await sending
  expect(x.context).toBeUndefined(); expect(x.pending).toBeUndefined(); expect(other.emitted('completed')).toBeUndefined()
})
it('edits returned numeric data without rounding and requires save and fresh versions before resubmission', async () => {
  const w = setup(ReturnedPanel, { instanceId: instance }), s = state(w); await s.load(); s.data.amount = new FormNumber('9007199254740993.987654321'); await flushPromises()
  await s.resubmit(); expect(api.send).not.toHaveBeenCalled()
  vi.mocked(api.send).mockRejectedValue({ response: { status: 503 } }); await s.save()
  expect(vi.mocked(api.send).mock.calls[0]![1]).toContain('"amount":9007199254740993.987654321')
  expect(sessionStorage.getItem('cp6-returned-form-operation')).not.toContain('9007199254740993')
  const intent = vi.mocked(api.send).mock.calls[0]![0]; vi.mocked(api.original).mockResolvedValue({ commandId: intent.command, outcome: 'SUCCEEDED', result: {} }); await s.op.recover('original')
  expect(s.context).toBeUndefined(); await s.resubmit(); expect(api.send).toHaveBeenCalledTimes(1)
  vi.mocked(api.returned).mockResolvedValue({ ...returned, instanceVersion: 'fresh-rv', formDataVersion: 'fresh-dv' }); await s.load(); await s.resubmit()
  const body = JSON.parse(vi.mocked(api.send).mock.calls[1]![1]); expect(body.expectedVersion).toBe('fresh-rv'); expect(body.body.expectedFormDataVersion).toBe('fresh-dv'); expect(body.body).not.toHaveProperty('dataPatch')
})
it('assigns only the freshly selected frozen resolution slot and never expands its candidate list', async () => {
  const w = setup(ResolutionPanel, { instanceId: instance }), s = state(w)
  vi.mocked(api.resolution).mockResolvedValue({ resolutionId: task, resolutionVersion: 'resolution-v', instanceId: instance, instanceVersion: 'instance-v', executionCycleId: 'cycle', nodeId: 'a', tokenId: 'token', stageIndex: 1, stageRound: 2, flowDefVersionId: 'flow', routePolicyRef: 'exact-policy', reason: 'missing approver', state: 'PENDING', candidatePrincipalIds: ['b', 'a'], candidates: [{ userId: 'a', name: '合格审批人甲' }, { userId: 'b', name: '合格审批人乙' }], canAssign: true, blockingReasons: [] })
  await s.load(task); await flushPromises(); expect(w.text()).toContain('合格审批人甲'); s.targets = ['outside']; s.reason = 'restore'; await s.assign(); expect(api.send).not.toHaveBeenCalled()
  s.targets = ['b', 'a']; vi.mocked(api.send).mockRejectedValue({ response: { status: 503 } }); await s.assign()
  expect(JSON.parse(vi.mocked(api.send).mock.calls[0]![1])).toMatchObject({ expectedVersion: 'resolution-v', body: { targetPrincipalIds: ['a', 'b'], routePolicyRef: 'exact-policy', executionCycleId: 'cycle', tokenId: 'token', stageIndex: 1, stageRound: 2 } })
})
