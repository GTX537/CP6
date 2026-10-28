import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import type { ConfigurationCommand, ConfigurationSnapshot } from '@/api/plm/configurationRules'
import type { EngineeringVersion } from '@/api/plm/engineeringProducts'
const api = vi.hoisted(() => ({ scope: vi.fn(), creationContext: vi.fn(), list: vi.fn(), read: vi.fn(), save: vi.fn(), operation: vi.fn(), products: vi.fn(), authenticated: true, denied: [] as string[] }))
vi.mock('@/api/plm/configurationRules', () => ({ configurationApi: api }))
vi.mock('@/api/plm/engineeringProducts', () => ({ engineeringApi: { list: api.products } }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (key: string) => !api.denied.includes(key), loadMyActions: vi.fn() }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => api.authenticated }))
import View from './ConfigurationRulesView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import { pendingConfigurationOperation } from '@/auth/configurationOperationRecovery'
const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const id = '30000000-0000-4000-8000-000000000003', department = '40000000-0000-4000-8000-000000000004'
const product: EngineeringVersion = { tenantId: scope.tenantId, reference: { owner: 'Main.Plm.EngineeringProduct.v1', id: '70000000-0000-4000-8000-000000000007', version: '3', digest: 'c'.repeat(64) }, isCurrent: true, product: { code: 'P', name: 'Original product', category: 'PART', definition: 'Original source', identificationSource: 'Explicit identity', enabled: true }, originalJson: '{}' }
const row: ConfigurationSnapshot = { tenantId: scope.tenantId, reference: { owner: 'Main.Plm.ConfigurationRule.v1', id, version: '2', digest: 'a'.repeat(64) }, etag: '50000000-0000-4000-8000-000000000005', isCurrent: true, isActive: false, activeRef: null, state: 'DRAFT', body: { scope: 'ENGINEERING_OPTIONS_ONLY', name: 'Original option rules', productRef: product.reference, definitions: [{ name: 'Length', type: 'DECIMAL', required: true, allowAnyValue: true, allowedValues: [] }], constraints: [] }, action: 'REVISE', reason: 'Current design', parentVersion: '1', actorId: scope.actorId, recordedAt: '2026-10-26T00:00:00Z', originalJson: '{}', missing: [] }
const ok = <T>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined, persisted: ConfigurationSnapshot
const button = (text: string) => view!.findAll('button').find(value => value.text() === text)!
const field = (label: string) => view!.get(`[aria-label="${label}"]`)
async function click(text: string) { await button(text).trigger('click'); await flushPromises() }
async function submit(label: string) { field(label).element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises() }
async function render() { view = mount(View); await flushPromises() }
async function opened() { await render(); await click('打开'); await field('操作原因').setValue('Authorized decision') }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); api.authenticated = true; api.denied = []; persisted = structuredClone(row)
  api.scope.mockResolvedValue(ok(scope)); api.list.mockImplementation(async () => ok([structuredClone(persisted)])); api.read.mockImplementation(async () => ok(structuredClone(persisted)))
  api.creationContext.mockResolvedValue(ok({ ...scope, authorityVersion: 'current', allowsUnbound: false, departments: [{ id: department, code: 'D', name: 'Department' }] })); api.products.mockResolvedValue(ok([product]))
  api.save.mockImplementation(async (body: ConfigurationCommand) => {
    if (body.body) persisted.body = structuredClone(body.body)
    persisted.reference = { ...persisted.reference, id: body.ruleId, version: String(Number(persisted.reference.version) + 1) }
    return ok({ operationId: body.operationId, kind: 'CONFIGURATION_RULE', objectId: body.ruleId, version: persisted.reference.version, digest: 'b'.repeat(64) })
  })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('requires explicit applicability policy input and saves it as a new approval scope without upgrading old rules', async () => {
  await opened(); expect((field('包含工程完整性政策').element as HTMLInputElement).checked).toBe(false)
  await field('包含工程完整性政策').setValue(true)
  expect((field('政策制造方式').element as HTMLSelectElement).value).toBe('')
  expect((field('EBOM 政策适用性').element as HTMLSelectElement).value).toBe('')
  await field('政策产品类别').setValue('PART'); await field('政策制造方式').setValue('PURCHASED_FINISHED'); await field('政策配置适用性').setValue('APPLIES')
  await field('工程政策依据').setValue('Explicit purchased finished product policy')
  for (const kind of ['EBOM', 'MBOM', 'ROUTING', 'DOCUMENT', 'QUALITY_SPEC']) {
    const required = ['DOCUMENT', 'QUALITY_SPEC'].includes(kind)
    await field(`${kind} 政策适用性`).setValue(required ? 'APPLIES' : 'NOT_APPLICABLE')
    await field(`${kind} 最少位置数`).setValue(required ? '1' : '0'); await field(`${kind} 政策依据`).setValue('Exact category basis')
  }
  await click('提交审核'); expect(api.save).not.toHaveBeenCalled(); expect(view!.text()).toContain('请先保存草稿')
  await submit('规则名称'); expect(api.save.mock.calls[0]![0].body).toMatchObject({ scope: 'ENGINEERING_OPTIONS_AND_CHECKS_V1', engineeringChecks: { productCategory: 'PART', manufacturing: 'PURCHASED_FINISHED' } })
  expect(api.save.mock.calls[0]![0].body.engineeringChecks.composition).toHaveLength(5)
})
it('creates a rule with explicit source, department and exact allowed decimal values', async () => {
  await render(); await click('新建规则'); await field('规则名称').setValue('New options'); await field('操作原因').setValue('New rule'); await field('规则所属部门').setValue(department)
  await submit('工程产品搜索'); expect((field('规则工程来源').element as HTMLSelectElement).value).toBe('')
  await field('规则工程来源').setValue(`${product.reference.id}:${product.reference.version}:${product.reference.digest}`)
  await click('添加选项定义'); await field('定义名 1').setValue('Length'); await field('定义类型 1').setValue('DECIMAL'); await field('定义必选 1').setValue('true'); await field('定义范围 1').setValue('false'); await click('添加允许值')
  const exact = '123456789012345678901234567890.00000000000000100'; await field('定义 1 允许值 1').setValue(exact); await submit('规则名称')
  expect(api.save.mock.calls[0]![0]).toMatchObject({ action: 'CREATE', departmentId: department, expectedAuthorityVersion: 'current', body: { productRef: product.reference, scope: 'ENGINEERING_OPTIONS_ONLY', definitions: [{ name: 'Length', type: 'DECIMAL', required: true, allowAnyValue: false, allowedValues: [{ name: 'Length', type: 'DECIMAL', decimal: exact }] }] } })
})
it('requires review acknowledgement and sends the exact expected active version when approving', async () => {
  persisted.state = 'UNDER_REVIEW'; persisted.activeRef = { ...row.reference, version: '1', digest: 'd'.repeat(64) }
  await opened(); expect(button('批准此版本').attributes('disabled')).toBeDefined(); expect(button('保存新草稿').element.closest('fieldset')?.disabled).toBe(true)
  await field('确认规则批准范围').setValue(true); await click('批准此版本')
  expect(api.save.mock.calls[0]![0]).toMatchObject({ action: 'APPROVE', expectedVersion: '2', expectedEtag: row.etag, expectedActiveRef: { ...row.reference, version: '1', digest: 'd'.repeat(64) } }); expect(api.save.mock.calls[0]![0]).not.toHaveProperty('body')
})
it('withdraws an active old approval without sending or overwriting the current draft content', async () => {
  persisted.activeRef = { ...row.reference, version: '1', digest: 'd'.repeat(64) }; await opened(); await field('规则名称').setValue('Unsaved draft content')
  expect(button('撤回批准').attributes('disabled')).toBeDefined(); await field('确认撤回批准').setValue(true); await click('撤回批准')
  expect(api.save.mock.calls[0]![0]).toMatchObject({ action: 'WITHDRAW', expectedActiveRef: persisted.activeRef }); expect(api.save.mock.calls[0]![0]).not.toHaveProperty('body')
})
it('edits typed dependency constraints and reads exact historical versions without replacing the draft', async () => {
  await opened(); await click('添加选项定义'); await field('定义名 2').setValue('Guard'); await field('定义类型 2').setValue('BOOLEAN'); await field('定义必选 2').setValue('false'); await field('定义范围 2').setValue('true')
  await click('添加约束'); await field('约束 1 when').setValue('Length'); await field('约束 1 when 值').setValue('12.3400'); await field('约束 1 then').setValue('Guard'); await field('约束 1 then 值').setValue('true'); await field('约束依据 1').setValue('Guard required'); await click('提交审核'); expect(api.save).not.toHaveBeenCalled(); expect(view!.text()).toContain('请先保存草稿，再提交审核'); await submit('规则名称')
  expect(api.save.mock.calls[0]![0].body.constraints[0]).toMatchObject({ kind: 'REQUIRES', when: { name: 'Length', type: 'DECIMAL', decimal: '12.3400' }, then: { name: 'Guard', type: 'BOOLEAN', boolean: true } })
  api.read.mockResolvedValueOnce(ok({ ...row, isCurrent: false, body: { ...row.body, name: 'Historical options' } })); await submit('规则历史版本'); expect(api.read).toHaveBeenLastCalledWith(id, '1'); expect((field('规则名称').element as HTMLInputElement).value).toBe('Original option rules')
})
it('does not offer approval without its current action and clears private content after query denial', async () => {
  api.denied = ['plm-configuration-rule:approve']; persisted.state = 'UNDER_REVIEW'; await opened(); expect(button('批准此版本')).toBeUndefined(); expect(button('退回草稿')).toBeUndefined()
  api.read.mockRejectedValueOnce({ response: { status: 403, data: { message: 'OBJECT_FORBIDDEN' } } }); await submit('规则历史版本'); expect(view!.find('[aria-label="规则名称"]').exists()).toBe(false); expect(view!.text()).not.toContain('Original option rules')
})
it('retains unknown-operation recovery without resending and clears a stale editor after recovery', async () => {
  api.save.mockRejectedValue(new Error('lost response')); await opened(); await submit('规则名称'); const pending = pendingConfigurationOperation(scope, 'CONFIGURATION_RULE')!
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'UNKNOWN' })); await click('查询原操作'); expect(api.save).toHaveBeenCalledTimes(1); expect(pendingConfigurationOperation(scope, 'CONFIGURATION_RULE')).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'COMMITTED', result: { operationId: pending.operationId, kind: 'CONFIGURATION_RULE', objectId: id, version: '3', digest: 'b'.repeat(64) } })); await click('查询原操作'); expect(pendingConfigurationOperation(scope, 'CONFIGURATION_RULE')).toBeNull(); expect(view!.find('[aria-label="规则名称"]').exists()).toBe(false)
})
it('discards private editor and late results after identity changes', async () => {
  await opened(); await field('规则名称').setValue('Private rule draft'); let done!: (value: ReturnType<typeof ok<EngineeringVersion[]>>) => void
  api.products.mockImplementationOnce(() => new Promise(resolve => { done = resolve })); field('工程产品搜索').element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises()
  api.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT)); done(ok([product])); await flushPromises()
  expect(view!.text()).not.toContain('Private rule draft'); expect(view!.text()).not.toContain('Original product'); expect(view!.find('[aria-label="规则名称"]').exists()).toBe(false)
})
