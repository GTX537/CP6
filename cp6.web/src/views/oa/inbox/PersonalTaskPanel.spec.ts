// @vitest-environment jsdom
import { beforeEach, afterEach, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { personalFormApi as api, type PersonalTask } from '@/api/oa/personalForm'
import { identityStamp, newEpoch } from '@/auth/identity'
import { setActingAs } from '@/stores/oaActingAs'
import { FormNumber, writeFormJson } from '../catalog/personalFormJson'
import Panel from './PersonalTaskPanel.vue'
vi.mock('@/api/oa/personalForm', () => ({ personalFormApi: { task: vi.fn(), decideSfs: vi.fn(), original: vi.fn(), taskOriginal: vi.fn(), close: vi.fn() } }))
const context: PersonalTask = { task: { taskId: 'task', instanceId: 'instance', taskStatus: 0, instanceStatus: 0, taskRowVersion: 'task-version', instanceRowVersion: 'instance-version' }, formDataId: 'form-data', rootInstanceId: 'root', formVersionId: 'pin', name: '审核金额', schemaJson: '{"fields":[{"name":"amount","type":"number"},{"name":"locked","type":"input"}]}', dataJson: '{"amount":9007199254740993.1,"locked":"readonly"}', fieldMask: { amount: 'edit', locked: 'readonly' }, allowedDecisions: ['approve', 'reject'], kind: 'SFS', formDataVersion: 'data-version', formKey: 'form', flowVersionId: 'flow', nodeId: 'node', tokenId: 'token', stageIndex: 0, stageRound: 0 }
const wrappers: ReturnType<typeof mount>[] = []
const state = (w: any): any => w.vm.$.setupState
const succeeded = (commandId: string) => ({ commandId, outcome: 'SUCCEEDED', resourceId: 'task', resourceVersion: 'next', result: null })
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear(); vi.mocked(api.task).mockResolvedValue(context) })
afterEach(() => { wrappers.splice(0).forEach(w => w.unmount()) })
async function setup() { const w = mount(Panel, { props: { taskId: 'task' } }); wrappers.push(w); await flushPromises(); return w }
it('sends an exact numeric patch and all three observed versions, keeping the same raw command after an unknown result', async () => {
  vi.mocked(api.decideSfs).mockRejectedValue({ response: { status: 503 } }); const w = await setup()
  state(w).data.amount = new FormNumber('12345678901234567890.0001'); state(w).data.locked = 'forged client edit'
  state(w).comment = 'checked'; await state(w).decide('approve'); await state(w).decide('approve'); expect(api.decideSfs).toHaveBeenCalledTimes(1)
  const [task, raw] = vi.mocked(api.decideSfs).mock.calls[0]!
  expect(task).toBe('task'); expect(raw).toContain('12345678901234567890.0001'); expect(raw).not.toContain('forged client edit')
  const command = JSON.parse(raw); expect(command.expectedVersion).toBe('task-version')
  expect(command.body).toMatchObject({ expectedTaskVersion: 'task-version', expectedInstanceVersion: 'instance-version', expectedFormDataVersion: 'data-version', decision: 'APPROVE', kind: 'SFS', comment: 'checked' })
  const cached = sessionStorage.getItem('cp6-personal-task-pending')!; expect(cached).not.toContain('checked'); expect(cached).not.toContain('123456789')
  await state(w).run(false, true); expect(vi.mocked(api.decideSfs).mock.calls[1]![1]).toBe(raw)
  vi.mocked(api.original).mockResolvedValue({ ...succeeded(command.commandId), outcome: 'NOT_OBSERVED' }); await state(w).run(true, true); expect(state(w).pending).toBeDefined()
  vi.mocked(api.original).mockResolvedValue(succeeded(command.commandId)); await state(w).run(true, true)
  expect(w.emitted('completed')).toHaveLength(1); expect(state(w).context).toBeUndefined(); expect(sessionStorage.getItem('cp6-personal-task-pending')).toBeNull()
})
it('does not send decisions outside current grant projection and clears private data on forbidden', async () => {
  vi.mocked(api.task).mockResolvedValue({ ...context, allowedDecisions: ['reject'] }); const w = await setup()
  await state(w).decide('approve'); expect(api.decideSfs).not.toHaveBeenCalled()
  vi.mocked(api.decideSfs).mockRejectedValue({ response: { status: 403 } }); await state(w).decide('reject')
  expect(api.decideSfs).toHaveBeenCalledTimes(1); expect(state(w).context).toBeUndefined(); expect(state(w).data).toEqual({})
})
it('preserves entered values on conflict and requires an explicit refresh before a new command', async () => {
  vi.mocked(api.decideSfs).mockRejectedValue({ response: { status: 409 } }); const w = await setup()
  state(w).data.amount = new FormNumber('42.001'); await state(w).decide('approve')
  expect(writeFormJson(state(w).data)).toContain('42.001'); expect(state(w).conflict).toBe(true); expect(state(w).pending).toBeUndefined()
  await state(w).decide('approve'); expect(api.decideSfs).toHaveBeenCalledTimes(1)
  await state(w).load(); expect(state(w).conflict).toBe(false); expect(writeFormJson(state(w).data)).toContain('9007199254740993.1')
})
it('keeps unknown original identifiers but immediately clears content on principal changes', async () => {
  vi.mocked(api.decideSfs).mockRejectedValue({ response: { status: 503 } }); const w = await setup(); await state(w).decide('approve')
  const command = state(w).pending.command; setActingAs({ userId: 'different-principal', userName: 'delegate' })
  expect(state(w).context).toBeUndefined(); expect(state(w).data).toEqual({}); expect(state(w).pending.command).toBe(command)
  await state(w).run(false, true); expect(api.decideSfs).toHaveBeenCalledTimes(1)
  vi.mocked(api.original).mockRejectedValue({ response: { status: 403 } }); await state(w).run(true, true); expect(state(w).pending.command).toBe(command)
  vi.mocked(api.close).mockResolvedValue({ ...succeeded(command), outcome: 'CLOSED' }); await state(w).run(false, true, true)
  expect(state(w).result.state).toBe('CLOSED'); expect(api.close).toHaveBeenCalledWith('SFS_TASK_DECISION', 'task', command)
})
it('recovers a pre-upgrade native command without resending it as a new SFS operation', async () => {
  sessionStorage.setItem('cp6-personal-task-pending', JSON.stringify({ identity: identityStamp(), task: 'old-task', command: 'old-command', behalf: null }))
  const w = await setup(); vi.mocked(api.taskOriginal).mockResolvedValue({ commandId: 'old-command', state: 'COMPLETED', voteId: 'vote', task: context.task, queryRef: '/original' })
  await state(w).run(true, true); expect(api.taskOriginal).toHaveBeenCalledWith('old-task', 'old-command'); expect(api.decideSfs).not.toHaveBeenCalled(); expect(state(w).result.state).toBe('COMPLETED')
})
it('ignores late task reads after changing the acting principal', async () => {
  let resolve!: (v: PersonalTask) => void; vi.mocked(api.task).mockImplementation(() => new Promise(r => { resolve = r }))
  const w = mount(Panel, { props: { taskId: 'task' } }); wrappers.push(w); setActingAs({ userId: 'other', userName: 'other' }); resolve(context); await flushPromises()
  expect(state(w).context).toBeUndefined(); expect(state(w).busy).toBe(false)
})
it('ignores a late decision after identity changes', async () => {
  let resolve!: (v: any) => void; vi.mocked(api.decideSfs).mockImplementation(() => new Promise(r => { resolve = r }))
  const w = await setup(); const action = state(w).decide('approve'); newEpoch(); resolve(succeeded('command')); await action
  expect(state(w).context).toBeUndefined(); expect(state(w).result).toBeUndefined(); expect(w.emitted('completed')).toBeUndefined()
})
it('switches tasks during an in-flight read without displaying the old task or locking the new one', async () => {
  let first!: (v: PersonalTask) => void, second!: (v: PersonalTask) => void
  vi.mocked(api.task).mockImplementation(id => new Promise(resolve => { if (id === 'one') first = resolve; else second = resolve }))
  const w = mount(Panel, { props: { taskId: 'one' } }); wrappers.push(w)
  await w.setProps({ taskId: 'two' }); first(context); await flushPromises(); expect(state(w).context).toBeUndefined(); expect(state(w).busy).toBe(true)
  second({ ...context, task: { ...context.task, taskId: 'two' } }); await flushPromises(); expect(state(w).context.task.taskId).toBe('two'); expect(state(w).busy).toBe(false)
})
