import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import type { CurrencyReference, CurrencyVersion } from '@/api/erp/currencyPolicies'
const api = vi.hoisted(() => ({ scope: vi.fn(), creationContext: vi.fn(), list: vi.fn(), read: vi.fn(), save: vi.fn(), operation: vi.fn(), quantize: vi.fn(), authenticated: true, allowed: true }))
vi.mock('@/api/erp/currencyPolicies', () => ({ currencyApi: api }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: () => api.allowed, loadMyActions: vi.fn() }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => api.authenticated }))
import View from './CurrencyPoliciesView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import { pendingCurrencyOperation } from '@/auth/currencyOperationRecovery'
const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const reference: CurrencyReference = { owner: 'Main.Erp.CurrencyPolicy.v1', id: '30000000-0000-4000-8000-000000000003', version: '2', digest: 'a'.repeat(64) }
const row: CurrencyVersion = { tenantId: scope.tenantId, reference, isCurrent: true, policy: { code: 'JPY', name: '円政策', minorDigits: 0, priceDigits: 3, rounding: 'HALF_EVEN', enabled: true }, originalJson: '{}' }
const department = '40000000-0000-4000-8000-000000000004', key = `${reference.owner}:${reference.id}:${reference.version}:${reference.digest}`
const ok = <T>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined
async function render() { view = mount(View); await flushPromises(); return view }
const button = (text: string) => view!.findAll('button').find(b => b.text() === text)!
async function submit(label: string) { view!.get(`[aria-label="${label}"]`).element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises() }
async function form() { await button('新增政策').trigger('click'); await flushPromises(); await view!.get('[aria-label="币种编码"]').setValue('USD'); await view!.get('[aria-label="币种名称"]').setValue('Explicit policy'); await view!.get('[aria-label="金额小数位"]').setValue('2'); await view!.get('[aria-label="单价小数位"]').setValue('6') }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); api.authenticated = true; api.allowed = true
  api.scope.mockResolvedValue(ok(scope)); api.list.mockResolvedValue(ok([structuredClone(row)])); api.creationContext.mockResolvedValue(ok({ ...scope, authorityVersion: 'current', allowsUnbound: false, departments: [{ id: department, code: 'D', name: 'Department' }] }))
  api.save.mockImplementation(async body => ok({ operationId: body.operationId, kind: 'POLICY', objectId: body.policyId, version: '1', digest: 'b'.repeat(64) }))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('creates only explicit digits and selected current department, then revises exact version', async () => {
  await render(); await button('新增政策').trigger('click'); await flushPromises()
  expect((view!.get('[aria-label="金额小数位"]').element as HTMLInputElement).value).toBe('')
  expect((view!.get('[aria-label="币种编码"]').element as HTMLInputElement).value).toBe('')
  await form(); await view!.get('[aria-label="政策所属部门"]').setValue(department); await submit('币种编码')
  expect(api.save.mock.calls[0]![0]).toMatchObject({ action: 'CREATE', expectedAuthorityVersion: 'current', expectedVersion: null, departmentId: department, policy: { code: 'USD', minorDigits: 2, priceDigits: 6, rounding: 'HALF_EVEN' } })
  await button('修订').trigger('click'); await view!.get('[aria-label="金额小数位"]').setValue('1'); await submit('币种编码')
  expect(api.save.mock.calls[1]![0]).toMatchObject({ action: 'REVISE', policyId: reference.id, expectedVersion: '2', expectedAuthorityVersion: null, departmentId: null })
})
it('preserves uncertain writes and queries or closes without resending', async () => {
  api.save.mockRejectedValue(new Error('response lost')); await render(); await form(); await submit('币种编码')
  const original = pendingCurrencyOperation(scope, 'POLICY')!; expect(view!.text()).toContain('上次保存结果尚未确认')
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'UNKNOWN' })); await button('查询原操作').trigger('click'); await flushPromises()
  expect(pendingCurrencyOperation(scope, 'POLICY')).not.toBeNull(); expect(api.save).toHaveBeenCalledTimes(1)
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'CLOSED' })); await button('结束未提交操作').trigger('click'); await flushPromises()
  expect(pendingCurrencyOperation(scope, 'POLICY')).toBeNull(); expect(api.save).toHaveBeenCalledTimes(1)
})
it('keeps money as exact strings and invalidates the previous result on input change', async () => {
  const input = '1000000000000000000000000000000000000.125'
  api.quantize.mockResolvedValue(ok({ policyRef: reference, kind: 'AMOUNT', value: input, roundedValue: '1000000000000000000000000000000000000' }))
  await render(); await view!.get('[aria-label="舍入政策"]').setValue(key); await view!.get('[aria-label="原数值"]').setValue(input); await submit('原数值')
  expect(api.quantize).toHaveBeenCalledWith(reference, 'AMOUNT', input); expect(view!.get('output').text()).toBe('1000000000000000000000000000000000000')
  await view!.get('[aria-label="原数值"]').setValue('2'); expect(view!.find('output').exists()).toBe(false)
})
it('rejects late identity responses and clears all private input', async () => {
  await render(); await view!.get('[aria-label="原数值"]').setValue('123.45'); await view!.get('[aria-label="币种编码搜索"]').setValue('private-search')
  let done!: (value: ReturnType<typeof ok<CurrencyVersion[]>>) => void
  api.list.mockImplementationOnce(() => new Promise(resolve => { done = resolve })); await button('刷新').trigger('click'); await flushPromises()
  api.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT)); done(ok([{ ...row, policy: { ...row.policy, name: 'old-private' } }]))
  await flushPromises(); expect(view!.text()).not.toContain('old-private'); expect((view!.get('[aria-label="原数值"]').element as HTMLInputElement).value).toBe(''); expect((view!.get('[aria-label="币种编码搜索"]').element as HTMLInputElement).value).toBe('')
})
it('does not query or offer writes when current read permission is missing', async () => {
  api.allowed = false; await render(); expect(api.scope).not.toHaveBeenCalled(); expect(api.list).not.toHaveBeenCalled(); expect(view!.text()).toContain('没有读取'); expect(view!.findAll('button').some(b => b.text() === '新增政策')).toBe(false)
})
