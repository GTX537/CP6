// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { taskActionApi as api, type TaskActionContext } from '@/api/oa/taskAction'
import { identityStamp, newEpoch } from '@/auth/identity'
import { setActingAs } from '@/stores/oaActingAs'
import Panel from './AppTaskPanel.vue'
vi.mock('@/api/oa/taskAction', () => ({ taskActionApi: { context: vi.fn(), decideApp: vi.fn(), originalApp: vi.fn(), closeApp: vi.fn() } }))
const context: TaskActionContext = { taskId: 'task', instanceId: 'instance', contextKind: 'APP', flowDefVersionId: 'flow', formDefVersionId: null,
  taskRowVersion: 'task-version', instanceRowVersion: 'instance-version', formDataVersion: null, tokenId: 'token', nodeId: 'review', stageIndex: 0, stageRound: 1,
  expectedAssigneeId: 'principal', currentPrincipalId: 'principal', createdPrincipalId: 'original-principal', actualHandlerId: 'actual', onBehalfOfId: 'principal',
  currentStatus: 0, instanceStatus: 0, allowedActions: ['APPROVE', 'REJECT'], fieldMask: {}, grantId: 'grant', grantRevision: 'revision',
  approvalSubjectRef: { owner: 'business', id: 'subject', version: 'frozen-version' }, form: null, commentRequired: false, commentLimit: 500, commandIdProfile: 'APP_ACCEPTED_GUID_2' }
const wrappers: ReturnType<typeof mount>[] = [], state = (w: any): any => w.vm.$.setupState
const completed = (commandId: string): any => ({ commandId, state: 'COMPLETED', result: { voteId: 'vote', decisionId: null, task: { instanceStatus: 0 } }, queryRef: '/original' })
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear(); vi.mocked(api.context).mockResolvedValue(context) })
afterEach(() => { wrappers.splice(0).forEach(w => w.unmount()); vi.restoreAllMocks() })
async function setup() { const w = mount(Panel, { props: { taskId: 'task' } }); wrappers.push(w); await flushPromises(); return w }
it('sends only accepted APP input with the observed two versions and retries identical bytes after unknown completion', async () => {
  vi.mocked(api.decideApp).mockRejectedValue({ response: { status: 503 } }); const w = await setup(); state(w).comment = 'reviewed'; await state(w).decide('REJECT')
  const [task, raw] = vi.mocked(api.decideApp).mock.calls[0]!, command = JSON.parse(raw)
  expect(task).toBe('task'); expect(command.expectedVersion).toBe('task-version'); expect(command.commandId).toMatch(/^[0-9a-f-]{36}$/)
  expect(command.body).toEqual({ kind: 'APP', taskId: 'task', decision: 'REJECT', comment: 'reviewed', expectedTaskVersion: 'task-version', expectedInstanceVersion: 'instance-version' })
  expect(sessionStorage.getItem('cp6-app-task-pending')).not.toContain('reviewed'); expect(sessionStorage.getItem('cp6-app-task-pending')).not.toContain('frozen-version')
  await state(w).decide('APPROVE'); expect(api.decideApp).toHaveBeenCalledTimes(1)
  await state(w).run(false, true); expect(vi.mocked(api.decideApp).mock.calls[1]![1]).toBe(raw)
  vi.mocked(api.originalApp).mockResolvedValue({ commandId: command.commandId, state: 'NOT_OBSERVED', result: null }); await state(w).run(true, true)
  expect(state(w).pending.command).toBe(command.commandId)
  vi.mocked(api.originalApp).mockResolvedValue({ commandId: command.commandId, state: 'COMPLETED', result: completed(command.commandId) }); await state(w).run(true, true)
  expect(w.emitted('completed')).toHaveLength(1); expect(state(w).context).toBeUndefined(); expect(sessionStorage.getItem('cp6-app-task-pending')).toBeNull()
  expect(w.text()).toContain('流程仍在审批中')
})
it('counts Unicode code points without truncation and requires a rejection reason and owner-required review comment', async () => {
  const w = await setup(); expect(state(w).canDecide('APPROVE')).toBe(true); expect(state(w).canDecide('REJECT')).toBe(false)
  state(w).comment = ' \t\r\n'; expect(state(w).canDecide('REJECT')).toBe(false)
  state(w).comment = '🙂'.repeat(500); expect(state(w).commentLength).toBe(500); expect(state(w).canDecide('REJECT')).toBe(true)
  state(w).comment += '🙂'; expect(state(w).canDecide('APPROVE')).toBe(false); expect(state(w).comment.length).toBe(1002)
  state(w).context = { ...context, commentRequired: true }; state(w).comment = ''; expect(state(w).canDecide('APPROVE')).toBe(false)
})
it('honors current delegated actions and clears private content after a current denial', async () => {
  vi.mocked(api.context).mockResolvedValue({ ...context, allowedActions: ['REJECT'] }); const w = await setup(); state(w).comment = 'current review'
  await state(w).decide('APPROVE'); expect(api.decideApp).not.toHaveBeenCalled()
  vi.mocked(api.decideApp).mockRejectedValue({ response: { status: 403 } }); await state(w).decide('REJECT')
  expect(state(w).context).toBeUndefined(); expect(state(w).comment).toBe(''); expect(w.emitted('privateClear')).toHaveLength(1)
})
it('preserves comments on a stale version and requires explicit refresh before a new command', async () => {
  vi.mocked(api.decideApp).mockRejectedValue({ response: { status: 412 } }); const w = await setup(); state(w).comment = 'keep this review'; await state(w).decide('REJECT')
  expect(state(w).comment).toBe('keep this review'); expect(state(w).conflict).toBe(true); expect(state(w).pending).toBeUndefined()
  await state(w).decide('REJECT'); expect(api.decideApp).toHaveBeenCalledTimes(1)
  vi.mocked(api.context).mockResolvedValue({ ...context, instanceRowVersion: 'new-version' }); await state(w).load()
  expect(state(w).comment).toBe('keep this review'); expect(state(w).conflict).toBe(false)
  await state(w).decide('REJECT'); expect(JSON.parse(vi.mocked(api.decideApp).mock.calls[1]![1]).body.expectedInstanceVersion).toBe('new-version')
})
it('restores only an original identity after reload and closes the same accepted command', async () => {
  sessionStorage.setItem('cp6-app-task-pending', JSON.stringify({ identity: identityStamp(), task: 'old-task', command: 'old-command', behalf: null }))
  const w = await setup(); await state(w).run(false, true); expect(api.decideApp).not.toHaveBeenCalled()
  vi.mocked(api.closeApp).mockResolvedValue({ commandId: 'old-command', state: 'CLOSED', result: null }); await state(w).run(false, true, true)
  expect(api.closeApp).toHaveBeenCalledWith('old-task', 'old-command'); expect(state(w).pending).toBeUndefined(); expect(state(w).context).toBeUndefined()
})
it('drops private content on principal change while keeping unknown recovery identity and ignores late responses', async () => {
  let resolve!: (value: any) => void; vi.mocked(api.decideApp).mockImplementation(() => new Promise(r => { resolve = r }))
  const w = await setup(); state(w).comment = 'private'; const action = state(w).decide('APPROVE'), command = state(w).pending.command
  setActingAs({ userId: 'other', userName: 'Other' }); resolve(completed(command)); await action
  expect(state(w).context).toBeUndefined(); expect(state(w).comment).toBe(''); expect(state(w).pending.command).toBe(command); expect(w.emitted('completed')).toBeUndefined()
  await state(w).run(false, true); expect(api.decideApp).toHaveBeenCalledTimes(1)
  newEpoch(); expect(state(w).pending).toBeUndefined(); expect(state(w).result).toBeUndefined()
})
it('fails before sending if recovery identity cannot be persisted', async () => {
  const w = await setup(); vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') })
  await state(w).decide('APPROVE'); expect(api.decideApp).not.toHaveBeenCalled(); expect(state(w).storageBlocked).toBe(true)
})
it('does not display an old task after navigation while reads are in flight', async () => {
  let first!: (value: TaskActionContext) => void, second!: (value: TaskActionContext) => void
  vi.mocked(api.context).mockImplementation(task => new Promise(r => { if (task === 'one') first = r; else second = r }))
  const w = mount(Panel, { props: { taskId: 'one' } }); wrappers.push(w); await w.setProps({ taskId: 'two' })
  first(context); await flushPromises(); expect(state(w).context).toBeUndefined(); expect(state(w).busy).toBe(true)
  second({ ...context, taskId: 'two' }); await flushPromises(); expect(state(w).context.taskId).toBe('two'); expect(state(w).busy).toBe(false)
})
