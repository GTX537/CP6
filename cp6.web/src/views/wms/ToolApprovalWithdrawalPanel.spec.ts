import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { WithdrawalContext, WithdrawalResult } from '@/api/wms/toolWithdrawal'
const api = vi.hoisted(() => ({ withdrawalContext: vi.fn(), businessGrants: vi.fn(), businessDelegates: vi.fn(), withdraw: vi.fn(), withdrawalOperation: vi.fn(), scope: vi.fn(), denied: [] as string[], authenticated: true }))
vi.mock('@/api/wms/toolWithdrawal', () => ({ toolWithdrawalApi: api }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (key: string) => !api.denied.includes(key) }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => api.authenticated }))
import Panel from './ToolApprovalWithdrawalPanel.vue'
import { pendingToolWithdrawal } from '@/auth/toolWithdrawalRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope = { environmentId: id(99), tenantId: id(1), actorId: id(2) }, toolId = id(3), submissionId = id(4)
const ok = <T,>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined, context: WithdrawalContext, accepted: WithdrawalResult | undefined
const button = (text: string) => view!.findAll('button').find(b => b.text() === text)!
async function start() { view = mount(Panel, { props: { toolId, submissionId, scope } }); await flushPromises() }
async function withdraw() { await view!.get('[aria-label="版模撤回原因"]').setValue('Private withdrawal reason'); await view!.get('form').trigger('submit'); await flushPromises() }
beforeEach(() => {
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); api.denied = []; api.authenticated = true; accepted = undefined
  context = { environmentId: scope.environmentId, toolId, submissionId, state: 'IN_REVIEW', controlRowVersion: 'AAAAAAAAAAE=', canWithdraw: true, authorizations: [], delivery: null }
  api.scope.mockResolvedValue(ok(scope)); api.withdrawalContext.mockImplementation(async () => ok(structuredClone(context))); api.businessGrants.mockResolvedValue(ok([])); api.businessDelegates.mockResolvedValue(ok([]))
  api.withdraw.mockImplementation(async (_id, _submission, _version, command) => {
    accepted = { operationId: command.commandId, toolId, state: 'WITHDRAWN', fact: { submissionId, owner: 'Main.Wms.ToolDisposition.v1', scope: { environmentId: scope.environmentId, tenantId: scope.tenantId }, actor: { actualHandlerId: scope.actorId } }, transportCommandId: id(5), previousCommandId: null }
    context = { ...context, state: 'WITHDRAWN', controlRowVersion: 'AAAAAAAAAAI=', canWithdraw: false, authorizations: [{ commandId: id(5), previousCommandId: null, state: 'PENDING', requesterId: scope.actorId, actualHandlerId: scope.actorId, grantId: null, grantRevision: null }], delivery: { state: 'PENDING', error: null } }
    return ok(accepted)
  })
})
afterEach(() => { view?.unmount(); view = undefined; vi.restoreAllMocks() })
it('keeps the original key after a lost response and recovers with read permission after write revocation', async () => {
  const execute = api.withdraw.getMockImplementation()!
  api.withdraw.mockImplementation(async (...args) => { await execute(...args); throw new Error('Committed response lost') })
  await start(); await withdraw()
  const pending = pendingToolWithdrawal(scope)!
  expect(pending.operationId).toBe(accepted!.operationId); expect(pending.kind).toBe('WITHDRAWAL'); expect(JSON.stringify(localStorage)).not.toContain('Private withdrawal reason')
  api.denied.push('wms-plate-mold:dispose-withdraw')
  api.withdrawalOperation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'COMMITTED', result: accepted }))
  await button('查询原版模操作').trigger('click'); await flushPromises()
  expect(pendingToolWithdrawal(scope)).toBeNull(); expect(api.withdraw).toHaveBeenCalledTimes(1)
  expect(view!.text()).toContain('版模审批已撤回'); expect(view!.text()).toContain('审批系统撤回：待确认')
})
it('never sends a command when durable recovery storage fails', async () => {
  await start(); vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('Recovery storage unavailable') })
  await withdraw(); expect(api.withdraw).not.toHaveBeenCalled(); expect(view!.text()).toContain('Recovery storage unavailable')
})
it('explicitly reauthorizes the blocked command with the current control and Owner grant principal', async () => {
  context = { ...context, state: 'WITHDRAWN', canWithdraw: false, authorizations: [{ commandId: id(8), previousCommandId: null, state: 'BLOCKED', actualHandlerId: scope.actorId, requesterId: id(9), grantId: id(10), grantRevision: '1' }] }
  api.businessGrants.mockResolvedValue(ok([{ grantId: id(11), grantorId: id(9), delegateId: scope.actorId, enabled: true, actions: ['dispose-withdraw'], revision: '2' }]))
  await start(); await view!.get('[aria-label="版模撤回业务身份"]').setValue(id(9)); await withdraw()
  expect(api.withdraw).toHaveBeenCalledWith(toolId, submissionId, 'AAAAAAAAAAE=', { commandId: expect.any(String), reason: 'Private withdrawal reason' }, id(9), id(8))
})
it('discards a late private context after the authenticated identity changes', async () => {
  let release!: (value: ReturnType<typeof ok<WithdrawalContext>>) => void
  api.withdrawalContext.mockReturnValueOnce(new Promise(resolve => { release = resolve }))
  view = mount(Panel, { props: { toolId, submissionId, scope } }); await flushPromises()
  api.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT))
  release(ok(context)); await flushPromises()
  expect(view.text()).not.toContain('本次版模审批尚未撤回'); expect(view.find('form').exists()).toBe(false); expect(api.businessGrants).not.toHaveBeenCalled()
})

it('keeps pending operations isolated by environment and original actor', async () => {
  const execute = api.withdraw.getMockImplementation()!
  api.withdraw.mockImplementation(async (...args) => { await execute(...args); throw new Error('Committed response lost') })
  await start(); await withdraw()
  expect(pendingToolWithdrawal(scope)?.operationId).toBe(accepted!.operationId)
  expect(pendingToolWithdrawal({ ...scope, actorId: id(97) })).toBeNull()
  expect(pendingToolWithdrawal({ ...scope, environmentId: id(98) })).toBeNull()
})
