// @vitest-environment jsdom
import { beforeEach, afterEach, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { personalFormApi as api, type PersonalTask } from '@/api/oa/personalForm'
import { newEpoch } from '@/auth/identity'
import { setActingAs } from '@/stores/oaActingAs'
import Panel from './PersonalTaskPanel.vue'
vi.mock('@/api/oa/personalForm', () => ({ personalFormApi: { task: vi.fn(), decide: vi.fn(), taskOriginal: vi.fn(), close: vi.fn() } }))
const context: PersonalTask = { task: { taskId: 'task', instanceId: 'instance', taskStatus: 0, instanceStatus: 0, taskRowVersion: 'task-version', instanceRowVersion: 'instance-version' }, formDataId: 'form-data', rootInstanceId: 'root', formVersionId: 'pin', name: '审核金额', schemaJson: '{"fields":[{"name":"amount","type":"number"}]}', dataJson: '{"amount":9007199254740993.1}', fieldMask: { amount: 'readonly' } }
const wrappers: ReturnType<typeof mount>[] = []
const state = (w: any): any => w.vm.$.setupState
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear(); vi.mocked(api.task).mockResolvedValue(context) })
afterEach(() => { wrappers.splice(0).forEach(w => w.unmount()) })
async function setup() { const w = mount(Panel, { props: { taskId: 'task' } }); wrappers.push(w); await flushPromises(); return w }
it('uses the actual two versions, rejects empty rejection, and keeps the same command after an unknown result', async () => {
  vi.mocked(api.decide).mockRejectedValue({ response: { status: 503 } }); const w = await setup()
  await state(w).decide('reject'); expect(api.decide).not.toHaveBeenCalled()
  state(w).comment = 'checked'; await state(w).decide('approve'); await state(w).decide('approve'); expect(api.decide).toHaveBeenCalledTimes(1)
  const [task, body, token] = vi.mocked(api.decide).mock.calls[0]!
  expect(task).toBe('task'); expect(body).toMatchObject({ taskRowVersion: 'task-version', instanceRowVersion: 'instance-version', decision: 'approve', comment: 'checked' }); expect(token).toBe('task-version')
  const cached = sessionStorage.getItem('cp6-personal-task-pending')!; expect(cached).not.toContain('checked'); expect(cached).not.toContain('9007199')
  await state(w).run(false, true); expect(vi.mocked(api.decide).mock.calls[1]![1]).toEqual(body)
  setActingAs({ userId: 'different-principal', userName: 'delegate' }); await state(w).run(false, true); expect(api.decide).toHaveBeenCalledTimes(2)
  vi.mocked(api.taskOriginal).mockResolvedValue({ commandId: (body as any).commandId, state: 'COMPLETED', voteId: 'vote', task: { ...context.task, taskStatus: 1 }, queryRef: '/original' })
  await state(w).run(true, true); expect(w.emitted('completed')).toHaveLength(1); expect(state(w).context).toBeUndefined(); expect(sessionStorage.getItem('cp6-personal-task-pending')).toBeNull()
})
it('clears private form content on forbidden recovery but retains the original command for explicit closure', async () => {
  vi.mocked(api.decide).mockRejectedValue({ response: { status: 503 } }); const w = await setup(); await state(w).decide('approve')
  const command = state(w).pending.command
  vi.mocked(api.taskOriginal).mockRejectedValue({ response: { status: 403 } }); await state(w).run(true, true)
  expect(state(w).context).toBeUndefined(); expect(state(w).pending.command).toBe(command)
  expect(w.emitted('privateClear')).toHaveLength(1)
  vi.mocked(api.close).mockResolvedValue({ commandId: command, outcome: 'CLOSED', resourceId: null, resourceVersion: null, result: null })
  vi.mocked(api.taskOriginal).mockResolvedValue({ commandId: command, state: 'CLOSED', voteId: null, task: null, queryRef: '/original' })
  await state(w).run(false, true, true); expect(state(w).result.state).toBe('CLOSED'); expect(api.decide).toHaveBeenCalledTimes(1)
})
it('ignores a late decision after identity changes', async () => {
  let resolve!: (v: any) => void; vi.mocked(api.decide).mockImplementation(() => new Promise(r => { resolve = r }))
  const w = await setup(); const action = state(w).decide('approve'); newEpoch(); resolve({ state: 'COMPLETED' }); await action
  expect(state(w).context).toBeUndefined(); expect(state(w).result).toBeUndefined(); expect(w.emitted('completed')).toBeUndefined()
})
it('switches tasks during an in-flight read without displaying the old task or leaving the new task locked', async () => {
  let first!: (v: PersonalTask) => void, second!: (v: PersonalTask) => void
  vi.mocked(api.task).mockImplementation(id => new Promise(resolve => { if (id === 'one') first = resolve; else second = resolve }))
  const w = mount(Panel, { props: { taskId: 'one' } }); wrappers.push(w)
  await w.setProps({ taskId: 'two' }); first(context); await flushPromises(); expect(state(w).context).toBeUndefined(); expect(state(w).busy).toBe(true)
  second({ ...context, task: { ...context.task, taskId: 'two' } }); await flushPromises()
  expect(state(w).context.task.taskId).toBe('two'); expect(state(w).busy).toBe(false)
})
