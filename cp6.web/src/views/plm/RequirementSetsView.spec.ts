import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
const f = vi.hoisted(() => ({ scope: vi.fn(), list: vi.fn(), creationContext: vi.fn(), read: vi.fn(), save: vi.fn(), operation: vi.fn(), members: vi.fn(), source: vi.fn(), authenticated: true, denied: [] as string[] }))
vi.mock('@/api/plm/requirementSet', async original => ({ ...await original<any>(), requirementSetApi: f }))
vi.mock('@/api/plm/engineeringRequirements', () => ({ requirementApi: { list: f.members } }))
vi.mock('@/api/plm/requirementEvaluation', () => ({ evaluationApi: { source: f.source } }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (key: string) => !f.denied.includes(key) }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => f.authenticated }))
import Page from './RequirementSetsView.vue'
import { pendingRequirementSet } from '@/auth/requirementSetRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope = { environmentId: id(99), evidenceMode: 'DEMO' as const, tenantId: id(1), actorId: id(2) }
const reference = { owner: 'Main.Plm.EngineeringRequirement.v1', id: id(3), version: '7', digest: 'a'.repeat(64) }
const member = { reference, body: { name: 'Original label', requirement: { necessity: 'Mandatory exact source', applicableObject: 'Product label' } } }
const proof = { environmentId: scope.environmentId, tenantId: scope.tenantId, evidenceMode: 'DEMO', requirement: reference, availability: 'AVAILABLE', control: { ...reference, owner: 'Main.Plm.RequirementEvaluationControl.v1', version: '8' }, approval: { ...reference, owner: 'Main.Plm.RequirementApproval.v1', id: id(4), version: '1' } }
const ok = <T,>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined
const button = (text: string) => view!.findAll('button').find(x => x.text() === text)!
async function click(text: string) { await button(text).trigger('click'); await flushPromises() }
async function start() { view = mount(Page, { global: { stubs: { RequirementSetReviewPanel: true } } }); await flushPromises(); await click('新建需求集') }
async function choose() { await click('查找需求'); await view!.get('[aria-label="需求版本"]').setValue(reference.id); await click('核对批准并加入或更新成员') }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); f.authenticated = true; f.denied = []
  f.scope.mockResolvedValue(ok(scope)); f.list.mockResolvedValue(ok([])); f.members.mockResolvedValue(ok([member])); f.source.mockResolvedValue(ok(proof))
  f.creationContext.mockResolvedValue(ok({ ...scope, authorityVersion: 'trusted-version', allowsUnbound: true, departments: [] }))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('selects the exact approved member, shows original necessity and preserves it through a lost save response', async () => {
  await start(); await choose(); expect(f.source).toHaveBeenCalledWith(reference); expect(view!.text()).toContain('Mandatory exact source')
  await view!.get('[aria-label="需求集名称"]').setValue('Label set'); await view!.get('[aria-label="保存原因"]').setValue('Compile exact source')
  f.save.mockRejectedValue(new Error('Lost response')); await button('保存草稿').trigger('submit'); await flushPromises()
  expect(f.save).toHaveBeenCalledTimes(1); expect(f.save.mock.calls[0]![0].body.members[0]).toMatchObject({ requirement: reference, evaluationControl: proof.control, approval: proof.approval, disposition: 'INCLUDED' })
  expect(f.save.mock.calls[0]![0].expectedAuthorityVersion).toBe('trusted-version'); expect(pendingRequirementSet(scope)).not.toBeNull()
  expect(view!.text()).toContain('上次保存结果尚未确认'); expect(JSON.stringify(localStorage)).not.toContain('Compile exact source')
})
it('does not silently include an unapproved version or reuse an approval from a different version', async () => {
  f.source.mockResolvedValueOnce(ok({ ...proof, availability: 'UNAVAILABLE', approval: null })); await start(); await choose()
  expect(view!.text()).toContain('尚无可用批准'); expect(view!.text()).not.toContain('必要性原文：Mandatory')
  f.source.mockResolvedValueOnce(ok({ ...proof, requirement: { ...reference, version: '8' } })); await choose(); expect(view!.text()).toContain('尚无可用批准')
})
it('discards a delayed member response when the active identity changes', async () => {
  let release!: (value: ReturnType<typeof ok>) => void
  f.members.mockReturnValueOnce(new Promise(resolve => { release = resolve })); await start(); await click('查找需求')
  f.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT)); release(ok([member])); await flushPromises()
  expect(view!.text()).not.toContain('Original label'); expect(view!.find('[aria-label="需求集名称"]').exists()).toBe(false); expect(f.save).not.toHaveBeenCalled()
})
it('does not offer create when only query permission is present', async () => {
  f.denied = ['plm-requirement-set:add', 'plm-requirement-set:edit']; view = mount(Page); await flushPromises()
  expect(view.text()).not.toContain('新建需求集'); expect(f.creationContext).not.toHaveBeenCalled()
})

const set = (n: number) => ({ tenantId: scope.tenantId, reference: { owner: 'Main.Plm.RequirementSet.v1', id: id(n), version: '1', digest: 'b'.repeat(64) }, body: { kind: 'SET', name: `Set ${n}`, scope: 'Scope', completenessBasis: 'Complete', members: [], conflicts: [] }, missing: [], memberStatus: [] })
const reviewStub = { props: ['requirementSet', 'scope', 'disabled', 'initialSubmission'], template: '<div data-testid="review" :data-submission="initialSubmission" :data-object="requirementSet.reference.id" />' }
async function detail(n: number, submission: number) {
  view = mount(Page, { props: { requirementSetId: id(n), reviewSubmission: id(submission) }, global: { stubs: { RequirementSetReviewPanel: reviewStub } } }); await flushPromises()
}
it('opens the linked requirement set without depending on its catalog page and retains the exact review on refresh', async () => {
  f.read.mockResolvedValue(ok(set(10))); await detail(10, 20)
  expect(f.read).toHaveBeenCalledWith(id(10)); expect(f.list).not.toHaveBeenCalled()
  expect(view!.get('[aria-label="需求集名称"]').element).toHaveProperty('value', 'Set 10')
  expect(view!.get('[data-testid="review"]').attributes('data-submission')).toBe(id(20))
  await click('刷新'); expect(f.read).toHaveBeenCalledTimes(2)
  expect(view!.get('[data-testid="review"]').attributes('data-object')).toBe(id(10))
})
it('drops an old deep-link response when a different requirement set replaces the route', async () => {
  let finish!: (value: ReturnType<typeof ok>) => void
  f.read.mockReturnValueOnce(new Promise(resolve => { finish = resolve })).mockResolvedValueOnce(ok(set(11)))
  await detail(10, 20); await view!.setProps({ requirementSetId: id(11), reviewSubmission: id(21) }); await flushPromises()
  expect(f.read).toHaveBeenNthCalledWith(2, id(11))
  finish(ok(set(10))); await flushPromises()
  expect(view!.get('[aria-label="需求集名称"]').element).toHaveProperty('value', 'Set 11')
  expect(view!.get('[data-testid="review"]').attributes('data-submission')).toBe(id(21))
})
it('clears the previous linked requirement set when current read access is denied', async () => {
  f.read.mockResolvedValueOnce(ok(set(10))).mockRejectedValueOnce({ response: { status: 403, data: { message: 'RESOURCE_FORBIDDEN' } } })
  await detail(10, 20); expect(view!.find('[data-testid="review"]').exists()).toBe(true)
  await view!.setProps({ requirementSetId: id(11), reviewSubmission: id(21) }); await flushPromises()
  expect(view!.find('[data-testid="review"]').exists()).toBe(false); expect(view!.find('[aria-label="需求集名称"]').exists()).toBe(false)
  expect(view!.get('[role="alert"]').text()).toContain('无法完成')
})
it('retains the exact review when the detail URL uses uppercase UUID spelling', async () => {
  const value = set(10); value.reference.id = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'; f.read.mockResolvedValue(ok(value))
  view = mount(Page, { props: { requirementSetId: value.reference.id.toUpperCase(), reviewSubmission: id(20) }, global: { stubs: { RequirementSetReviewPanel: reviewStub } } }); await flushPromises()
  expect(view.get('[data-testid="review"]').attributes('data-submission')).toBe(id(20))
})
