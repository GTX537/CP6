import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { RequirementBody, RequirementCommand, RequirementSnapshot } from '@/api/plm/engineeringRequirements'
const api = vi.hoisted(() => ({ scope: vi.fn(), creationContext: vi.fn(), list: vi.fn(), read: vi.fn(), save: vi.fn(), operation: vi.fn(), allowed: true, authenticated: true }))
vi.mock('@/api/plm/engineeringRequirements', () => ({ requirementApi: api }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: () => api.allowed, loadMyActions: vi.fn() }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => api.authenticated }))
import View from './EngineeringRequirementsView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import { pendingRequirementOperation } from '@/auth/requirementOperationRecovery'
const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const sourceId = '30000000-0000-4000-8000-000000000003', id = '40000000-0000-4000-8000-000000000004', department = '50000000-0000-4000-8000-000000000005'
const source: RequirementSnapshot = { tenantId: scope.tenantId, reference: { owner: 'Main.Plm.EngineeringRequirement.v1', id: sourceId, version: '2', digest: 'a'.repeat(64) }, etag: '60000000-0000-4000-8000-000000000006', isCurrent: true, body: { kind: 'SOURCE', name: 'Original internal proposal', source: { kind: 'INTERNAL_PROPOSAL', originDescription: 'Internal manager proposal', sourceLabel: 'Proposal A', acquisitionBasis: 'Recorded by proposer', clauses: [{ id: 'R1', originalText: 'Original strength and moisture', locator: 'Section 1' }] }, requirement: null }, authorId: scope.actorId, actorId: scope.actorId, parentVersion: '1', reason: 'New original', recordedAt: '2026-10-28T00:00:00Z', originalJson: '{}', missing: [], state: 'DRAFT_ONLY' }
const body: RequirementBody = { kind: 'REQUIREMENT', name: 'Interpreted demand', source: null, requirement: { source: source.reference, clauseIds: ['R1'], interpretation: 'Keep strength separate from moisture', applicableObject: '', necessity: '', clarificationQuestions: [], criteria: [], relations: [] } }
const requirement: RequirementSnapshot = { ...source, reference: { ...source.reference, id, digest: 'b'.repeat(64) }, body, missing: ['CRITERIA_REQUIRED', 'REQUIREMENT_APPROVAL_PENDING'] }
const ok = <T>(data: T) => ({ code: 0, data })
const key = (v: RequirementSnapshot) => `${v.reference.owner}:${v.reference.id}:${v.reference.version}:${v.reference.digest}`
let view: VueWrapper | undefined, current: RequirementSnapshot
const button = (text: string) => view!.findAll('button').find(v => v.text() === text)!
const field = (name: string) => view!.get(`[aria-label="${name}"]`)
async function click(text: string) { await button(text).trigger('click'); await flushPromises() }
async function render() { view = mount(View); await flushPromises() }
async function submit(name: string) { field(name).element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises() }
async function openRequirement() { await render(); await field('目录类别').setValue('REQUIREMENT'); await submit('目录搜索'); await click('打开') }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); api.allowed = true; api.authenticated = true; current = structuredClone(requirement)
  api.scope.mockResolvedValue(ok(scope)); api.creationContext.mockResolvedValue(ok({ ...scope, authorityVersion: 'current', allowsUnbound: false, departments: [{ id: department, code: 'D', name: 'Department' }] }))
  api.list.mockImplementation(async (kind: string) => ok([structuredClone(kind === 'SOURCE' ? source : current)]))
  api.read.mockImplementation(async (selected: string) => ok(structuredClone(selected === sourceId ? source : current)))
  api.save.mockImplementation(async (c: RequirementCommand) => {
    current = { ...structuredClone(requirement), reference: { ...requirement.reference, id: c.objectId, version: c.action === 'CREATE' ? '1' : '3' }, body: structuredClone(c.body) }
    return ok({ operationId: c.operationId, objectId: c.objectId, kind: c.body.kind, version: current.reference.version, digest: current.reference.digest })
  })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('creates an unresolved source with explicit original clauses and trusted department selection', async () => {
  await render(); await click('新建来源包'); expect((field('来源类别').element as HTMLSelectElement).value).toBe('UNRESOLVED')
  await field('草稿名称').setValue('Unresolved letter'); await field('所属部门').setValue(department); await field('来源描述').setValue('Source channel needs clarification'); await field('条款标识 1').setValue('C1'); await field('来源原文 1').setValue('Exact unedited original'); await field('保存原因').setValue('Record unclear source'); await submit('保存原因')
  expect(api.save.mock.calls[0]![0]).toMatchObject({ action: 'CREATE', departmentId: department, expectedAuthorityVersion: 'current', body: { kind: 'SOURCE', source: { kind: 'UNRESOLVED', clauses: [{ id: 'C1', originalText: 'Exact unedited original', locator: '' }] }, requirement: null } })
})
it('requires explicit source and clause selection without replacing original text by interpretation', async () => {
  await render(); await click('新建需求'); await field('草稿名称').setValue('New requirement'); await field('所属部门').setValue(department); await click('查找来源包')
  expect((field('来源包版本').element as HTMLSelectElement).value).toBe(''); expect(button('保存草稿').attributes('disabled')).toBeDefined()
  await field('来源包版本').setValue(key(source)); await click('采用所选来源'); expect(api.read).toHaveBeenLastCalledWith(sourceId, '2'); expect(view!.text()).toContain('Original strength and moisture')
  await field('引用条款 R1').setValue(true); await field('需求解释').setValue('Only interpreted strength'); await field('保存原因').setValue('New draft'); await submit('保存原因')
  expect(api.save.mock.calls[0]![0].body.requirement).toMatchObject({ source: source.reference, clauseIds: ['R1'], interpretation: 'Only interpreted strength', criteria: [] })
  expect(api.save.mock.calls[0]![0].body.source).toBeNull(); expect(view!.text()).toContain('尚待批准')
})
it('preserves long exact bounds and unknown endpoint unit and precision', async () => {
  await openRequirement(); await click('添加判据'); const exact = '123456789012345678901234567890.0000000000000000000123400'; await field('下界 1').setValue(exact); await field('评价对象 1').setValue('Strength'); await field('保存原因').setValue('Threshold draft'); await submit('保存原因')
  expect(api.save.mock.calls[0]![0]).toMatchObject({ expectedVersion: '2', expectedEtag: requirement.etag, body: { requirement: { criteria: [{ lower: exact, lowerInclusive: null, upper: null, proposedUnit: null, precision: null }] } } })
})
it('retains exact relation version, coverage remainder and clarification separately', async () => {
  await openRequirement(); await click('查找关联需求'); await field('关联版本').setValue(key(requirement)); await click('添加关系'); await field('未覆盖内容 1').setValue('Moisture still unknown'); await field('覆盖理由 1').setValue('Strength only'); await click('添加澄清问题'); await field('澄清问题 1').setValue('Who confirms moisture limits?'); await field('保存原因').setValue('Relationship proposal'); await submit('保存原因')
  const r = api.save.mock.calls[0]![0].body.requirement; expect(r.relations[0]).toMatchObject({ target: requirement.reference, uncovered: 'Moisture still unknown', coverageReason: 'Strength only' }); expect(r.clarificationQuestions).toEqual(['Who confirms moisture limits?'])
})
it('shows historical source input without replacing the current editor', async () => {
  await openRequirement(); api.read.mockResolvedValueOnce(ok({ ...requirement, isCurrent: false, body: { ...body, requirement: { ...body.requirement, interpretation: 'Old interpretation' } } })); await submit('历史版本')
  expect(api.read).toHaveBeenLastCalledWith(id, '1'); expect(view!.text()).toContain('Old interpretation'); expect((field('需求解释').element as HTMLTextAreaElement).value).toBe(body.requirement!.interpretation)
})
it('retains a lost operation and prevents another save until original recovery', async () => {
  await openRequirement(); api.save.mockRejectedValueOnce(new Error('lost')); await field('保存原因').setValue('Save once'); await submit('保存原因'); const pending = pendingRequirementOperation(scope, 'REQUIREMENT')!
  expect(pending).not.toBeNull(); expect(button('保存草稿').element.closest('fieldset')?.disabled).toBe(true)
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'UNKNOWN' })); await click('查询原操作'); expect(api.save).toHaveBeenCalledTimes(1); expect(pendingRequirementOperation(scope, 'REQUIREMENT')).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'CLOSED' })); await click('结束未提交操作'); expect(pendingRequirementOperation(scope, 'REQUIREMENT')).toBeNull()
})
it('clears original source and editor after current source permission denial', async () => {
  await openRequirement(); api.read.mockRejectedValueOnce({ response: { status: 403, data: { message: 'OBJECT_FORBIDDEN' } } }); await submit('历史版本')
  expect(view!.find('[aria-label="需求解释"]').exists()).toBe(false); expect(view!.text()).not.toContain('Original strength and moisture'); expect(view!.text()).not.toContain('Interpreted demand')
})
it('discards delayed source responses when the browser identity changes', async () => {
  await render(); let resolve!: (v: unknown) => void; api.read.mockReturnValueOnce(new Promise(r => { resolve = r })); const opening = click('打开'); await flushPromises(); localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT)); resolve(ok(source)); await opening
  expect(view!.text()).not.toContain('Original internal proposal'); expect(view!.find('[aria-label="草稿名称"]').exists()).toBe(false)
})
