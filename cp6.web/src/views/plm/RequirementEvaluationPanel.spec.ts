import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { EvaluationCommand, EvaluationScope, EvaluationView } from '@/api/plm/requirementEvaluation'
const api = vi.hoisted(() => ({ scope: vi.fn(), read: vi.fn(), history: vi.fn(), roles: vi.fn(), save: vi.fn(), source: vi.fn(), operation: vi.fn(), denied: [] as string[] }))
vi.mock('@/api/plm/requirementEvaluation', () => ({ evaluationApi: api }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (key: string) => !api.denied.includes(key) }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => true }))
import Panel from './RequirementEvaluationPanel.vue'
import { pendingEvaluation } from '@/auth/requirementEvaluationRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const reference = (owner: string, n: number) => ({ owner, id: id(n), version: '1', digest: 'a'.repeat(64) })
const scope: EvaluationScope = { environmentId: id(1), tenantId: id(2), actorId: id(3), evidenceMode: 'DEMO' }, requirement = reference('Main.Plm.EngineeringRequirement.v1', 4), control = reference('Main.Plm.RequirementEvaluationControl.v1', 4)
const row: EvaluationView = { ...scope, requirement: { tenantId: scope.tenantId, reference: requirement, etag: id(5), isCurrent: true, body: { kind: 'REQUIREMENT', name: 'Exact engineering definition', source: null, requirement: { source: reference('Main.Plm.EngineeringRequirement.v1', 6), clauseIds: ['C1'], interpretation: 'Original interpretation', applicableObject: 'Label', necessity: 'Required', clarificationQuestions: [], criteria: [], relations: [] } }, authorId: id(7), actorId: id(7), parentVersion: null, reason: 'Draft', recordedAt: '2026-10-28T00:00:00Z', originalJson: '{}', missing: [], state: 'DRAFT_ONLY' }, etag: '"re1-current"', controlReference: control, control: { requirementId: id(4), revision: 1, state: 'DRAFT', reason: 'Policy', actorId: id(7), recordedAt: '2026-10-28T00:00:00Z' }, policy: null, packet: null, votes: [], currentApproval: null, missing: [], controlIsCurrent: true }
const ok = <T>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined, persisted: EvaluationView
const button = (text: string) => view!.findAll('button').find(x => x.text() === text)!
const field = (label: string) => view!.get(`[aria-label="${label}"]`)
async function click(text: string) { await button(text).trigger('click'); await flushPromises() }
async function opened() { view = mount(Panel, { props: { requirementId: id(4) } }); await click('读取已保存版本的评价'); await field('评价操作原因').setValue('Reviewed exact definition') }
function pendingPacket(author = id(7), submitter = id(8)) { persisted.control!.state = 'IN_REVIEW'; persisted.packet = { reference: reference('Main.Plm.RequirementReviewPacket.v1', 9), body: { id: id(9), requirement, policy: reference('Main.Plm.RequirementReviewPolicy.v1', 10), authorId: author, submitterId: submitter, inputDigest: 'b'.repeat(64), inputs: { requirement: persisted.requirement, relatedOriginals: [], units: [], missing: [] }, submittedAt: '2026-10-28T00:00:00Z', purpose: 'DEFINITION_ONLY' } } }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); api.denied = []; persisted = structuredClone(row)
  api.scope.mockResolvedValue(ok(scope)); api.read.mockImplementation(async () => ok(structuredClone(persisted))); api.roles.mockResolvedValue(ok([{ roleId: 74, roleName: 'Actual domain professional' }]))
  api.save.mockImplementation(async (c: EvaluationCommand) => ok({ operationId: c.operationId, objectId: c.requirementId, action: c.action, requirement, control, state: 'IN_REVIEW', environmentId: scope.environmentId, evidenceMode: scope.evidenceMode }))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_: string, _opts: unknown, run: (lock: object) => unknown) => run({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('submits only the server persisted exact version with current etag and an explicit reason', async () => {
  await opened(); await click('送交专业评价'); expect(api.save).toHaveBeenCalledTimes(1)
  expect(api.save.mock.calls[0]![0]).toMatchObject({ requirementId: requirement.id, action: 'SUBMIT', expectedEtag: row.etag, requirement, reason: 'Reviewed exact definition' })
  expect(view!.text()).toContain('不表示需求已满足'); expect(view!.text()).toContain('演示证据环境')
})
it('requires actual selected roles, policy basis and dates rather than assigning default approvers', async () => {
  await opened(); expect(button('保存专业规则').attributes('disabled')).toBeDefined(); await click('读取可用角色'); await click('添加专业职责')
  expect((field('专业角色 1').element as HTMLSelectElement).value).toBe('0'); await field('专业职责 1').setValue('Materials'); await field('专业角色 1').setValue('74'); await field('专业人数 1').setValue('2'); await field('评价规则依据').setValue('Owner approved professional rule'); await field('评价生效时间').setValue('2026-10-28T00:00'); await field('评价失效时间').setValue('2026-10-29T00:00')
  await field('评价规则依据').element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises()
  expect(api.save.mock.calls[0]![0]).toMatchObject({ action: 'POLICY', policy: { basis: 'Owner approved professional rule', roles: [{ code: 'Materials', roleId: 74, minimumApprovals: 2 }] } })
})
it('blocks self signature, incomplete submission and hides unavailable professional actions', async () => {
  pendingPacket(scope.actorId); await opened(); expect(button('签署同意定义').attributes('disabled')).toBeDefined(); expect(button('退回需求定义').attributes('disabled')).toBeDefined()
  persisted.control!.state = 'RETURNED'; persisted.missing = ['UPPER_BOUND_INTENT_REQUIRED:C1']; await click('读取已保存版本的评价'); expect(button('送交专业评价').attributes('disabled')).toBeDefined(); expect(view!.text()).toContain('上界未定')
  view!.unmount(); api.denied = ['plm-engineering-requirement:review-decide']; await opened(); expect(view!.findAll('button').some(x => x.text() === '签署同意定义')).toBe(false)
})
it('retains a lost signature key, blocks resubmission and recovers the original without replay', async () => {
  pendingPacket(); await opened(); api.save.mockRejectedValueOnce(new Error('lost')); await click('签署同意定义'); const pending = pendingEvaluation(scope)!
  expect(pending.action).toBe('APPROVE'); expect(localStorage.getItem(localStorage.key(1)!)).not.toContain('Original interpretation')
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'UNKNOWN' })); await click('查询评价原操作'); expect(button('签署同意定义').attributes('disabled')).toBeDefined(); expect(api.save).toHaveBeenCalledTimes(1)
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'CLOSED' })); await click('结束未提交评价操作'); expect(pendingEvaluation(scope)).toBeNull()
})
it('shows old approved version separately from successor draft and queries source without manufacturing a use token', async () => {
  persisted.requirement.reference = { ...requirement, version: '2' }; persisted.currentApproval = { reference: reference('Main.Plm.RequirementApproval.v1', 11), body: { id: id(11), requirement, packet: reference('Main.Plm.RequirementReviewPacket.v1', 9), policy: reference('Main.Plm.RequirementReviewPolicy.v1', 10), votes: [], inputDigest: 'c'.repeat(64), approvedAt: '2026-10-28T01:00:00Z', purpose: 'DEFINITION_ONLY' } }
  api.source.mockResolvedValue(ok({ availability: 'UNAVAILABLE', missing: ['REQUIREMENT_APPROVAL_REQUIRED'] })); await opened(); expect(view!.text()).toContain('需求版本 2'); expect(view!.text()).toContain('当前批准指向需求版本 1'); await click('核对当前批准来源'); expect(api.source).toHaveBeenCalledWith({ ...requirement, version: '2' }); expect(view!.text()).toContain('后续采用仍须重新核对')
})
it('clears private text and discards an old response after authentication changes', async () => {
  await opened(); let resolve!: (v: unknown) => void; api.read.mockImplementationOnce(() => new Promise(r => { resolve = r })); void button('读取已保存版本的评价').trigger('click'); await flushPromises(); localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT)); resolve(ok(row)); await flushPromises(); expect(view!.text()).not.toContain('Exact engineering definition'); expect(view!.find('[aria-label="评价操作原因"]').exists()).toBe(false)
})

it('reads the exact historical control without making prior signatures current or allowing mutation', async () => {
  await opened(); api.history.mockResolvedValue(ok({ ...structuredClone(row), controlIsCurrent: false })); await field('评价历史版本').setValue('2'); field('评价历史版本').element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises(); expect(api.history).toHaveBeenCalledWith(id(4), '2'); expect(view!.text()).toContain('历史记录，仅供查阅'); expect(button('送交专业评价').attributes('disabled')).toBeDefined(); expect(api.save).not.toHaveBeenCalled()
})
