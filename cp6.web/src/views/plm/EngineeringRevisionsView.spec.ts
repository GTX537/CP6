import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import type { RevisionCommand, RevisionSnapshot } from '@/api/plm/engineeringRevisions'
import type { EngineeringVersion } from '@/api/plm/engineeringProducts'
const api = vi.hoisted(() => ({ scope: vi.fn(), creationContext: vi.fn(), list: vi.fn(), read: vi.fn(), diff: vi.fn(), save: vi.fn(), operation: vi.fn(), products: vi.fn(), rules: vi.fn(), rule: vi.fn(), authenticated: true, allowed: true }))
vi.mock('@/api/plm/engineeringRevisions', () => ({ revisionApi: api }))
vi.mock('@/api/plm/configurationRules', () => ({ configurationApi: { list: api.rules, read: api.rule } }))
vi.mock('@/api/plm/engineeringProducts', () => ({ engineeringApi: { list: api.products } }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: () => api.allowed, loadMyActions: vi.fn() }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => api.authenticated }))
import View from './EngineeringRevisionsView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import { pendingRevisionOperation } from '@/auth/revisionOperationRecovery'
const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const id = '30000000-0000-4000-8000-000000000003', department = '40000000-0000-4000-8000-000000000004'
const row: RevisionSnapshot = { tenantId: scope.tenantId, reference: { owner: 'Main.Plm.EngineeringRevision.v1', id, version: '2', digest: 'a'.repeat(64) }, revisionCode: 'ENG0001', etag: '50000000-0000-4000-8000-000000000005', candidateRef: { owner: 'Main.Plm.EngineeringCandidateDraft.v1', id: '60000000-0000-4000-8000-000000000006', version: '2', digest: 'b'.repeat(64) }, isCurrent: true, state: 'DRAFT', origin: { type: 'NEW_PRODUCT', noParentReason: 'First product' }, draft: { productRef: null, goal: 'Original design', configuration: null, components: [] }, reason: 'Initial goal', parentWorkingVersion: '1', recordedAt: '2026-10-26T00:00:00Z', originalJson: '{}', readiness: 'DRAFT_ONLY', missing: ['PRODUCT_UNRESOLVED', 'CONFIGURATION_MISSING'], review: 'NOT_REQUESTED', freeze: 'NOT_REQUESTED' }
const product: EngineeringVersion = { tenantId: scope.tenantId, reference: { owner: 'Main.Plm.EngineeringProduct.v1', id: '70000000-0000-4000-8000-000000000007', version: '3', digest: 'c'.repeat(64) }, isCurrent: true, product: { code: 'P', name: 'Original product', category: 'PART', definition: 'Original source', identificationSource: 'Explicit identity', enabled: true }, originalJson: '{}' }
const productKey = `${product.reference.owner}:${product.reference.id}:${product.reference.version}:${product.reference.digest}`
const ok = <T>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined, persisted: RevisionSnapshot
async function render() { view = mount(View); await flushPromises() }
const button = (text: string) => view!.findAll('button').find(value => value.text() === text)!
const field = (label: string) => view!.get(`[aria-label="${label}"]`)
async function click(text: string) { await button(text).trigger('click'); await flushPromises() }
async function submit(label: string) { field(label).element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises() }
async function opened() { await render(); await click('打开'); await field('修改原因').setValue('Reviewed draft edit') }
async function form() { await click('新建修订'); await field('设计目标').setValue('New design'); await field('无父基线原因').setValue('No previous baseline'); await field('修改原因').setValue('New product'); await field('修订所属部门').setValue(department) }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); api.authenticated = true; api.allowed = true; persisted = structuredClone(row)
  api.scope.mockResolvedValue(ok(scope)); api.list.mockImplementation(async () => ok([structuredClone(persisted)])); api.read.mockImplementation(async () => ok(structuredClone(persisted)))
  api.creationContext.mockResolvedValue(ok({ ...scope, authorityVersion: 'current', allowsUnbound: false, departments: [{ id: department, code: 'D', name: 'Department' }] })); api.products.mockResolvedValue(ok([product]))
  api.save.mockImplementation(async (body: RevisionCommand) => {
    if (body.action === 'CREATE') persisted = { ...structuredClone(row), reference: { ...row.reference, id: body.revisionId, version: '1' }, draft: { productRef: body.productRef ?? null, goal: body.goal!, configuration: null, components: [] } }
    else {
      persisted.reference.version = String(Number(persisted.reference.version) + 1)
      for (const patch of body.patches ?? []) {
        if (patch.op === 'SET_GOAL') persisted.draft.goal = patch.goal!
        if (patch.op === 'SET_PRODUCT') persisted.draft.productRef = patch.productRef!
        if (patch.op === 'SET_CONFIGURATION') persisted.draft.configuration = { reference: product.reference, input: structuredClone(patch.configuration!) }
        if (patch.op === 'UPSERT_COMPONENT') persisted.draft.components.push(structuredClone(patch.component!))
        if (patch.op === 'REMOVE_COMPONENT') persisted.draft.components = persisted.draft.components.filter(x => x.occurrenceId !== patch.occurrenceId)
      }
    }
    return ok({ operationId: body.operationId, kind: 'ENGINEERING_REVISION', objectId: body.revisionId, version: persisted.reference.version, digest: 'b'.repeat(64) })
  })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('creates an unresolved new-product draft with explicit reason and department; goal patch preserves omitted fields', async () => {
  await render(); await form(); expect((field('本修订的工程产品').element as HTMLSelectElement).value).toBe('')
  await submit('设计目标'); expect(api.save.mock.calls[0]![0]).toMatchObject({ action: 'CREATE', expectedAuthorityVersion: 'current', departmentId: department, origin: { type: 'NEW_PRODUCT', noParentReason: 'No previous baseline' }, productRef: null, goal: 'New design', reason: 'New product' })
  await field('设计目标').setValue('Changed goal'); await field('修改原因').setValue('Goal only'); await submit('设计目标')
  expect(api.save.mock.calls[1]![0]).toMatchObject({ action: 'PATCH', expectedVersion: '1', expectedEtag: row.etag, patches: [{ op: 'SET_GOAL', goal: 'Changed goal' }] })
  expect(api.save.mock.calls[1]![0]).not.toHaveProperty('productRef'); expect(view!.text()).toContain('尚未评审或冻结')
})
it('requires an explicit source selection and retains exact native reference for binding', async () => {
  await opened(); await field('工程产品搜索').setValue('P'); await submit('工程产品搜索')
  expect((field('本修订的工程产品').element as HTMLSelectElement).value).toBe(''); expect(button('保存产品引用').attributes('disabled')).toBeDefined()
  await field('本修订的工程产品').setValue(productKey); await click('保存产品引用')
  expect(api.save.mock.calls[0]![0].patches).toEqual([{ op: 'SET_PRODUCT', productRef: product.reference }])
})
it('keeps configuration decimals as exact strings and does not infer rule approval', async () => {
  await opened(); await click('添加选项'); await field('选项名 1').setValue('Length'); await field('选项类型 1').setValue('DECIMAL')
  const exact = '100000000000000000000000000000.000000000000000000012300'; await field('选项值 1').setValue(exact); await submit('配置适用性')
  expect(api.save.mock.calls[0]![0].patches).toEqual([{ op: 'SET_CONFIGURATION', configuration: { options: [{ name: 'Length', type: 'DECIMAL', decimal: exact }], applicability: 'UNKNOWN', reason: '' } }]); expect(view!.text()).toContain('完整适用范围仍需核准')
})
it('keeps two occurrences of one product separate and removes only the selected occurrence', async () => {
  await opened()
  for (const path of ['assembly/left', 'assembly/right']) {
    await field('修改原因').setValue('Add separate mount'); await submit('工程产品搜索'); await click('添加组成位置'); await field('位置路径').setValue(path); await field('语义顺序').setValue(path.endsWith('left') ? '20' : '10'); await field('组成来源').setValue(productKey); await submit('位置路径')
  }
  const a = api.save.mock.calls[0]![0].patches[0].component, b = api.save.mock.calls[1]![0].patches[0].component
  expect(a.occurrenceId).not.toBe(b.occurrenceId); expect(a.sourceRef).toEqual(b.sourceRef); expect(persisted.draft.components).toHaveLength(2)
  await field('修改原因').setValue('Remove left only'); await click('移除位置'); expect(api.save.mock.calls[2]![0].patches).toEqual([{ op: 'REMOVE_COMPONENT', occurrenceId: a.occurrenceId }]); expect(persisted.draft.components).toEqual([b])
})
it('reads selected history and compares exact working versions without replacing current editor', async () => {
  await opened(); api.read.mockResolvedValueOnce(ok({ ...row, isCurrent: false, reference: { ...row.reference, version: '1' }, draft: { ...row.draft, goal: 'Historical design' } }))
  await submit('历史工作版本'); expect(api.read).toHaveBeenLastCalledWith(id, '1'); expect(view!.text()).toContain('Historical design'); expect((field('设计目标').element as HTMLTextAreaElement).value).toBe('Original design')
  api.diff.mockResolvedValue(ok({ before: { ...row.reference, version: '1' }, after: row.reference, changes: [{ path: 'goal', before: 'Historical design', after: row.draft.goal }] })); await field('差异结束版本').setValue('2'); await submit('差异起始版本'); expect(api.diff).toHaveBeenCalledWith(id, '1', '2')
})
it('keeps unknown results without resend and clears stale editor when original commit is recovered', async () => {
  api.save.mockRejectedValue(new Error('response lost')); await opened(); await submit('设计目标'); const original = pendingRevisionOperation(scope, 'ENGINEERING_REVISION')!
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'UNKNOWN' })); await click('查询原操作'); expect(pendingRevisionOperation(scope, 'ENGINEERING_REVISION')).not.toBeNull(); expect(api.save).toHaveBeenCalledTimes(1)
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: { operationId: original.operationId, kind: 'ENGINEERING_REVISION', objectId: id, version: '3', digest: 'd'.repeat(64) } })); await click('查询原操作')
  expect(pendingRevisionOperation(scope, 'ENGINEERING_REVISION')).toBeNull(); expect(view!.find('[aria-label="设计目标"]').exists()).toBe(false); expect(api.save).toHaveBeenCalledTimes(1)
})
it('clears private drafts and rejects late responses after identity change', async () => {
  await opened(); await field('设计目标').setValue('Private draft'); let done!: (value: ReturnType<typeof ok<EngineeringVersion[]>>) => void
  api.products.mockImplementationOnce(() => new Promise(resolve => { done = resolve })); field('工程产品搜索').element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises()
  api.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT)); done(ok([product])); await flushPromises()
  expect(view!.text()).not.toContain('Private draft'); expect(view!.text()).not.toContain('Original product'); expect(view!.find('[aria-label="设计目标"]').exists()).toBe(false)
})
it('does not read without query rights and erases previously loaded private content on current denial', async () => {
  api.allowed = false; await render(); expect(api.scope).not.toHaveBeenCalled(); expect(api.list).not.toHaveBeenCalled(); view!.unmount(); view = undefined
  api.allowed = true; await render(); await click('打开'); api.read.mockRejectedValueOnce({ response: { status: 403, data: { message: 'OBJECT_FORBIDDEN' } } }); await submit('历史工作版本')
  expect(view!.find('[aria-label="设计目标"]').exists()).toBe(false); expect(view!.text()).not.toContain('Original design'); expect(view!.text()).toContain('当前权限无法访问')
})

it('explicitly selects the active approved version even when the rule head is a newer draft', async () => {
  persisted.draft.productRef = product.reference
  const ruleRef = { owner: 'Main.Plm.ConfigurationRule.v1', id: '90000000-0000-4000-8000-000000000009', version: '3', digest: 'f'.repeat(64) }
  api.rules.mockResolvedValue(ok([{ reference: { ...ruleRef, version: '4' }, activeRef: ruleRef }]))
  api.rule.mockResolvedValue(ok({ reference: ruleRef, isActive: true, body: { name: 'Current approved options', productRef: product.reference }, missing: [] }))
  await opened(); await click('查找获准选项规则'); expect(api.rule).toHaveBeenCalledWith(ruleRef.id, '3')
  expect((field('获准选项规则').element as HTMLSelectElement).value).toBe(''); expect(api.save).not.toHaveBeenCalled()
  await field('获准选项规则').setValue(`${ruleRef.owner}:${ruleRef.id}:${ruleRef.version}:${ruleRef.digest}`); await submit('配置适用性')
  expect(api.save.mock.calls[0]![0].patches[0].configuration.ruleRef).toEqual(ruleRef)
})
it('excludes withdrawn or source-mismatched rule choices and clears private configuration on source denial', async () => {
  persisted.draft.productRef = product.reference
  const reference = { owner: 'Main.Plm.ConfigurationRule.v1', id: '90000000-0000-4000-8000-000000000009', version: '3', digest: 'f'.repeat(64) }
  api.rules.mockResolvedValue(ok([{ activeRef: reference }])); api.rule.mockResolvedValue(ok({ reference, isActive: false, body: { name: 'Withdrawn rule', productRef: product.reference }, missing: [] }))
  await opened(); await click('查找获准选项规则'); expect(view!.text()).not.toContain('Withdrawn rule')
  api.rule.mockRejectedValue({ response: { status: 403, data: { message: 'OBJECT_FORBIDDEN' } } }); await click('查找获准选项规则'); expect(view!.find('[aria-label="配置适用性"]').exists()).toBe(false)
})

it('requires an explicit boolean choice before applying an approved option rule', async () => {
  await opened(); await click('添加选项'); await field('选项名 1').setValue('Guard'); await field('选项类型 1').setValue('BOOLEAN')
  expect((field('选项值 1').element as HTMLSelectElement).value).toBe(''); expect(field('选项值 1').attributes('required')).toBeDefined()
  await field('选项值 1').setValue('false'); await submit('配置适用性')
  expect(api.save.mock.calls[0]![0].patches[0].configuration.options).toEqual([{ name: 'Guard', type: 'BOOLEAN', boolean: false }])
})
