import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
const f = vi.hoisted(() => ({ scope: vi.fn(), list: vi.fn(), creationContext: vi.fn(), read: vi.fn(), save: vi.fn(), operation: vi.fn(), members: vi.fn(), sites: vi.fn(), setList: vi.fn(), setRead: vi.fn(), documentList: vi.fn(), documentRead: vi.fn(), download: vi.fn(), authenticated: true, denied: [] as string[] }))
vi.mock('@/api/plm/engineeringIteration', async original => ({ ...await original<any>(), engineeringIterationApi: f }))
vi.mock('@/api/plm/requirementSet', () => ({ requirementSetApi: { list: f.setList, read: f.setRead } }))
vi.mock('@/api/plm/controlledDocument', () => ({ documentApi: { list: f.documentList, read: f.documentRead, download: f.download } }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (key: string) => !f.denied.includes(key) }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => f.authenticated }))
import Page from './EngineeringIterationsView.vue'
import { pendingEngineeringIteration } from '@/auth/engineeringIterationRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope = { environmentId: id(99), evidenceMode: 'DEMO' as const, tenantId: id(1), actorId: id(2) }
const ref = (n: number, owner = 'Main.Plm.EngineeringIteration.v1') => ({ owner, id: id(n), version: '1', digest: 'a'.repeat(64) })
const ok = <T,>(data: T) => ({ code: 0, data })
const project = () => ({ reference: ref(7), etag: id(70), body: { kind: 'PROJECT', name: 'Exact project', number: 'PRJ0001', project: { state: 'ACTIVE', definition: { name: 'Exact project', ownerId: scope.actorId, members: [scope.actorId, id(3)], site: null, product: null, objective: 'Project goal', startDate: '2026-10-01', endDate: '2026-12-31', milestones: [] } }, iteration: null }, missing: [], originalJson: '{}' })
const set = () => ({ reference: ref(9, 'Main.Plm.RequirementSet.v1'), state: 'APPROVED', body: { name: 'Approved fixed set' }, missing: [] })
const iteration = () => ({ reference: ref(8), etag: id(80), body: { kind: 'ITERATION', name: 'Native iteration', number: 'ITER0001', project: null, iteration: { projectId: id(7), projectReference: ref(7), plan: { name: 'Native iteration', ownerId: scope.actorId, goal: 'Accurate design', startDate: '2026-10-28', endDate: '2026-11-30' }, stage: 'IN_DESIGN', holdState: 'NORMAL', planVersion: '3', inputVersion: '2', input: { proof: { requirementSet: set().reference }, adoptedAt: '2026-10-28', requirementSetOriginal: 'Exact original requirement' }, tasks: [] as any[] } }, sourceDrift: 'UNCHANGED', missing: [], progress: { confirmedWeight: '0', plannedWeight: '0', percent: null }, originalJson: '{}' })
let value = iteration(), view: VueWrapper | undefined
const button = (text: string) => view!.findAll('button').find(x => x.text() === text)!
async function click(text: string) { await button(text).trigger('click'); await flushPromises() }
async function start() { view = mount(Page); await flushPromises() }
async function openIteration() { await start(); await click('打开项目'); await click('打开迭代') }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); f.authenticated = true; f.denied = []; value = iteration()
  f.scope.mockResolvedValue(ok(scope)); f.list.mockImplementation((kind: string) => Promise.resolve(ok(kind === 'PROJECT' ? [project()] : [value])))
  f.read.mockImplementation((n: string) => Promise.resolve(ok(n === id(7) ? project() : value)))
  f.creationContext.mockResolvedValue(ok({ ...scope, authorityVersion: 'current-authority', allowsUnbound: true, departments: [] }))
  f.members.mockResolvedValue(ok([{ id: scope.actorId, name: 'Owner' }, { id: id(3), name: 'Independent reviewer' }]))
  f.setList.mockResolvedValue(ok([set()])); f.setRead.mockResolvedValue(ok(set())); f.save.mockRejectedValue(new Error('Response lost'))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('saves a project using current creation authority and preserves a minimal recovery record after response loss', async () => {
  await start(); await click('新建项目'); await view!.get('[aria-label="项目名称"]').setValue('New native project'); await view!.get('[aria-label="操作原因"]').setValue('Create approved scope')
  await click('查找成员'); await view!.get('[aria-label="添加成员"]').setValue(id(3)); await click('加入项目')
  await button('保存项目').trigger('submit'); await flushPromises()
  expect(f.save).toHaveBeenCalledTimes(1); expect(f.save.mock.calls[0]![0]).toMatchObject({ action: 'CREATE_PROJECT', expectedAuthorityVersion: 'current-authority', project: { members: [scope.actorId, id(3)] } })
  expect(pendingEngineeringIteration(scope)).not.toBeNull(); expect(view!.text()).toContain('上次保存结果尚未确认'); expect(JSON.stringify(localStorage)).not.toContain('New native project')
})
it('creates from an explicitly read exact approved requirement version and does not submit an unavailable version', async () => {
  await start(); await click('打开项目'); await click('新建迭代'); await click('查找需求集'); await view!.get('[aria-label="需求集"]').setValue(id(9)); await view!.get('[aria-label="需求集版本"]').setValue('1')
  f.setRead.mockResolvedValueOnce(ok({ ...set(), state: 'DRAFT' })); await click('核对并选取需求集'); expect(button('创建迭代').attributes('disabled')).toBeDefined()
  await click('核对并选取需求集'); expect(f.setRead).toHaveBeenLastCalledWith(id(9), '1'); await view!.get('[aria-label="操作原因"]').setValue('Capture accurate source'); await view!.get('[aria-label="无父基线原因"]').setValue('First product')
  await button('创建迭代').trigger('submit'); await flushPromises(); expect(f.save.mock.calls[0]![0]).toMatchObject({ action: 'CREATE_ITERATION', requirementSet: set().reference, projectId: id(7), origin: { type: 'NEW_PRODUCT', noParentReason: 'First product' } })
  expect(f.save.mock.calls[0]![0]).not.toHaveProperty('approved')
})
it('keeps task weights as exact text and sends a task command without overwriting adopted inputs', async () => {
  await openIteration(); await click('添加计划任务'); const weight = '100000000000000000000000000000.000000000000000000001'
  await view!.get('[aria-label="任务标题"]').setValue('Verify native deliverable'); await view!.get('[aria-label="任务权重"]').setValue(weight); await view!.get('[aria-label="任务确认者"]').setValue(id(3)); await view!.get('[aria-label="操作原因"]').setValue('Explicit plan weight')
  await button('保存任务计划').trigger('submit'); await flushPromises(); expect(f.save.mock.calls[0]![0]).toMatchObject({ action: 'SAVE_TASK', expectedVersion: '1', expectedEtag: id(80), task: { weight, confirmerId: id(3) } }); expect(f.save.mock.calls[0]![0]).not.toHaveProperty('requirementSet')
})
it('requires real available file contents before selecting task deliverables', async () => {
  value.body.iteration.tasks = [{ plan: { id: id(12), title: 'Drawing', ownerId: scope.actorId, confirmerId: id(3), weight: '1' }, state: 'IN_PROGRESS', deliverables: [] }]
  const document = { reference: ref(13, 'Main.Plm.ControlledDocument.v1'), body: { name: 'Exact drawing' }, contentStatus: [{ availability: 'HELD' }] }
  f.documentList.mockResolvedValue(ok([document])); f.documentRead.mockResolvedValue(ok(document)); await openIteration(); await click('选择交付物'); await click('查找文档'); await view!.get('[aria-label="交付文档"]').setValue(id(13)); await click('核对并加入交付物')
  expect(view!.text()).toContain('文件当前不可用'); expect(button('提交任务交付').attributes('disabled')).toBeDefined(); expect(f.save).not.toHaveBeenCalled()
})
it('shows source drift and disables execution while held without treating progress as release', async () => {
  value.body.iteration.holdState = 'HELD'; value.sourceDrift = 'CHANGED'; value.progress = { confirmedWeight: '1', plannedWeight: '1', percent: '100.00' } as any
  await openIteration(); expect(view!.text()).toContain('来源已变化'); expect(view!.text()).toContain('100.00%'); expect(view!.text()).toContain('设计中'); expect(button('添加计划任务').attributes('disabled')).toBeDefined(); expect(view!.text()).toContain('核对来源并恢复'); expect(view!.text()).not.toContain('关闭迭代')
})
it('offers confirmation and return only to the assigned confirmer and retains exact deliverables for inspection', async () => {
  value.body.iteration.tasks = [{ plan: { id: id(12), title: 'Drawing', ownerId: id(3), confirmerId: scope.actorId, weight: '1', acceptance: 'Read original drawing' }, state: 'SUBMITTED', deliverables: [ref(13, 'Main.Plm.ControlledDocument.v1')] }]
  const doc = { reference: ref(13, 'Main.Plm.ControlledDocument.v1'), body: { name: 'Controlled original', source: { ownerName: 'Drawing Owner', organization: 'Design', sourceVersion: 'A', acquisitionBasis: 'Actual file' }, contents: [] }, contentStatus: [] }
  f.documentRead.mockResolvedValue(ok(doc)); await openIteration(); await click('查看准确交付物'); expect(f.documentRead).toHaveBeenCalledWith(id(13), '1'); expect(view!.text()).toContain('Controlled original')
  await view!.get('[aria-label="操作原因"]').setValue('Clarify missing basis'); await click('退回补充交付'); expect(f.save.mock.calls[0]![0]).toMatchObject({ action: 'RETURN_TASK', taskId: id(12) })
})
it('discards a delayed private member response after identity changes', async () => {
  let resolve!: (value: unknown) => void
  f.members.mockReturnValueOnce(new Promise(done => { resolve = done })); await start(); await click('新建项目'); await click('查找成员')
  f.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT)); resolve(ok([{ id: id(3), name: 'Obsolete private member' }])); await flushPromises()
  expect(view!.text()).not.toContain('Obsolete private member'); expect(view!.find('[aria-label="项目名称"]').exists()).toBe(false); expect(f.save).not.toHaveBeenCalled()
})
it('does not offer create for query-only permission and clears selected data on current authorization failure', async () => {
  f.denied = ['plm-engineering-iteration:add', 'plm-engineering-iteration:edit']; await start(); expect(view!.text()).not.toContain('新建项目'); await click('打开项目'); f.read.mockRejectedValueOnce({ response: { status: 403 } }); await click('打开迭代'); expect(view!.text()).not.toContain('Exact project'); expect(view!.find('[aria-label="项目名称"]').exists()).toBe(false)
})
