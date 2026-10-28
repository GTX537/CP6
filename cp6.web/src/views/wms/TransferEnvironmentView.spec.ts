import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import type { TransferEnvironmentContext, TransferEnvironmentCommand } from '@/api/wms/transferEnvironment'
const mock = vi.hoisted(() => ({ context: vi.fn(), history: vi.fn(), save: vi.fn(), operation: vi.fn(), allowed: true, authenticated: true }))
vi.mock('@/api/wms/transferEnvironment', () => ({ transferEnvironmentApi: mock }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => mock.authenticated }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: () => mock.allowed, loadMyActions: vi.fn() }) }))
import View from './TransferEnvironmentView.vue'
import { AUTH_EVENT, AUTH_EPOCH } from '@/auth/identity'
import { pendingTransferEnvironment, transferContextKey } from '@/auth/transferEnvironmentRecovery'
const guid = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const reference = { owner: 'Main.Wms.TransferEnvironment.v1', id: guid(5), version: '1', digest: 'a'.repeat(64) }
const scope: TransferEnvironmentContext = { tenantId: guid(1), actorId: guid(2), deployment: { environmentId: guid(3), mode: 'SIMULATION', sandboxScopeId: guid(4) }, version: 'absent', registration: null, availableOwners: ['Main.Erp.ProductIdentity.v1'], qualification: 'NATIVE_SOURCE_REGISTRATION_ONLY' }
const registration = { reference, original: { tenantId: scope.tenantId, actorId: scope.actorId, deployment: scope.deployment, enabled: true, profile: 'MAIN_NATIVE_TRANSFER_SOURCES_V1', owners: scope.availableOwners, reason: 'Private registered reason', recordedAt: '' }, originalJson: 'Private original' }
const ok = <T,>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined
const field = (name: string) => view!.get(`[aria-label="${name}"]`), button = (name: string) => view!.findAll('button').find(x => x.text() === name)!
async function render() { view = mount(View); await flushPromises() }
async function click(name: string) { await button(name).trigger('click'); await flushPromises() }
async function save() { await field('注册状态').setValue('enabled'); await field('操作原因').setValue('Private registration reason'); await field('操作原因').element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises() }
beforeEach(() => {
  vi.resetAllMocks(); mock.allowed = mock.authenticated = true; localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first')
  mock.context.mockResolvedValue(ok(structuredClone(scope))); mock.history.mockResolvedValue(ok(structuredClone(registration)))
  mock.save.mockImplementation(async (c: TransferEnvironmentCommand) => ok({ operationId: c.operationId, reference, enabled: c.enabled, changed: true }))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('uses server deployment and an explicit status without sending environmental overrides', async () => {
  await render(); expect(field('注册状态').element).toHaveProperty('value', ''); expect(view!.text()).toContain('模拟'); expect(view!.find('[aria-label="环境"]').exists()).toBe(false)
  await save(); const c = mock.save.mock.calls[0]![0] as TransferEnvironmentCommand
  expect(c).toEqual({ operationId: expect.any(String), expectedVersion: 'absent', enabled: true, reason: 'Private registration reason' }); expect(pendingTransferEnvironment(scope)).toBeNull()
})
it('preserves only original identifiers after lost ACK and queries UNKNOWN without repeating the command', async () => {
  mock.save.mockRejectedValue(new Error('response lost')); await render(); await save(); const p = pendingTransferEnvironment(scope)!
  expect(Object.keys(p).sort()).toEqual(['operationId', 'registrationId']); expect(JSON.stringify(localStorage)).not.toContain('Private registration reason')
  mock.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'UNKNOWN' })); await click('查询原操作'); expect(pendingTransferEnvironment(scope)).toEqual(p); expect(mock.save).toHaveBeenCalledTimes(1)
  mock.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'CLOSED' })); await click('关闭未提交原操作'); expect(pendingTransferEnvironment(scope)).toBeNull(); expect(mock.save).toHaveBeenCalledTimes(1)
})
it('recovers the exact committed result and rejects mismatched receipts without discarding the pending key', async () => {
  mock.save.mockRejectedValue(new Error('lost')); await render(); await save(); const p = pendingTransferEnvironment(scope)!
  mock.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'COMMITTED', result: { operationId: guid(99), reference, enabled: true, changed: true } }))
  await click('查询原操作'); expect(pendingTransferEnvironment(scope)).toEqual(p)
  mock.operation.mockResolvedValue(ok({ operationId: p.operationId, state: 'COMMITTED', result: { operationId: p.operationId, reference, enabled: true, changed: true } }))
  await click('查询原操作'); expect(pendingTransferEnvironment(scope)).toBeNull(); expect(mock.save).toHaveBeenCalledTimes(1)
})
it.each(['tenant', 'actor', 'environment', 'sandbox', 'mode', 'version'])('rejects changed %s before writing', async kind => {
  await render(); const changed = structuredClone(scope)
  if (kind === 'tenant') changed.tenantId = guid(99)
  if (kind === 'actor') changed.actorId = guid(99)
  if (kind === 'environment') changed.deployment.environmentId = guid(99)
  if (kind === 'sandbox') changed.deployment.sandboxScopeId = guid(99)
  if (kind === 'mode') { changed.deployment.mode = 'PRODUCTION'; changed.deployment.sandboxScopeId = null }
  if (kind === 'version') changed.version = '2'
  mock.context.mockResolvedValue(ok(changed)); await save(); expect(mock.save).not.toHaveBeenCalled(); expect(pendingTransferEnvironment(scope)).toBeNull()
})
it('requires dedicated edit permission and browser exclusion', async () => {
  mock.allowed = false; await render(); expect(view!.get('fieldset').attributes('disabled')).toBeDefined(); await save(); expect(mock.save).not.toHaveBeenCalled()
  mock.allowed = true; Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); await save(); expect(mock.save).not.toHaveBeenCalled()
})
it('clears sensitive historical data on denial and ignores responses after an identity change', async () => {
  mock.context.mockResolvedValue(ok({ ...scope, version: '1', registration })); await render(); await field('历史版本').element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises(); expect(view!.text()).toContain('Private registered reason')
  mock.history.mockRejectedValueOnce({ response: { status: 403 } }); await field('历史版本').element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises(); expect(view!.text()).not.toContain('Private registered reason'); expect(view!.find('[aria-label="操作原因"]').exists()).toBe(false)
  let done!: (r: ReturnType<typeof ok<TransferEnvironmentContext>>) => void; mock.context.mockImplementationOnce(() => new Promise(resolve => { done = resolve })); void click('重新载入'); await flushPromises()
  mock.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'changed'); window.dispatchEvent(new Event(AUTH_EVENT)); done(ok({ ...scope, version: '1', registration })); await flushPromises(); expect(view!.find('[aria-label="操作原因"]').exists()).toBe(false)
})
it('keeps distinct recovery records for simulation and production and blocks malformed storage', async () => {
  const production = { ...structuredClone(scope), deployment: { ...scope.deployment, mode: 'PRODUCTION' as const, sandboxScopeId: null } }
  expect(transferContextKey(production)).not.toBe(transferContextKey(scope)); localStorage.setItem(transferContextKey(scope), '{invalid')
  await render(); expect(view!.get('fieldset').attributes('disabled')).toBeDefined(); await save(); expect(mock.save).not.toHaveBeenCalled(); expect(localStorage.getItem(transferContextKey(scope))).toBe('{invalid')
})
