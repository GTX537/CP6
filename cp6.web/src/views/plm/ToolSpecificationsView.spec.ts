import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { ToolSpecificationCommand, ToolSpecificationResult, ToolSpecificationView } from '@/api/plm/toolSpecifications'
const api = vi.hoisted(() => ({ scope: vi.fn(), list: vi.fn(), read: vi.fn(), save: vi.fn(), operation: vi.fn(), products: vi.fn(), canWrite: true }))
vi.mock('@/api/plm/toolSpecifications', () => ({ toolSpecificationApi: api }))
vi.mock('@/api/plm/engineeringProducts', () => ({ engineeringApi: { list: api.products } }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => false }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ has: () => api.canWrite }) }))
import View from './ToolSpecificationsView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import { pendingToolSpecification } from '@/auth/toolSpecificationRecovery'
const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const id = '30000000-0000-4000-8000-000000000003', productId = '40000000-0000-4000-8000-000000000004'
const source = { owner: 'Main.Erp.PlateMoldSource.v1' as const, id, version: '7', digest: 'a'.repeat(64) }
const productRef = { owner: 'Main.Plm.EngineeringProduct.v1', id: productId, version: '3', digest: 'b'.repeat(64) }
const current: ToolSpecificationView = { source: { tenantId: scope.tenantId, reference: source, currentNativeRowVersion: 'AQ==', observedNativeRowVersion: 'AA==', observedAt: '2026-10-26T00:00:00Z', originalJson: 'private native original', isCurrent: true, sourcePresent: true, technicalApproval: 'UNKNOWN', physicalQualification: 'UNKNOWN',
  source: { identity: { baseCode: 'BASE', number: 'FULL-ORIGINAL-40', businessRevision: 9, name: 'Private tool' }, technical: {}, validity: { startDate: '', endDate: '' }, legacyAttachmentFlags: {}, requirements: {}, control: { isDeleted: false, creator: 'actor', legacyTransferStatus: 9, legacyMcTransferred: true } } },
  currentControl: { knowledge: 'TECHNICAL_CONTROL_UNPROVEN', ...source }, specification: null, isCurrent: true, sourceMatches: false, productMatches: false }
const requirements = { productRef, productApplicability: 'Product A', processRequirements: 'Cutting requirements', configurationApplicability: 'Configuration A', technicalRequirements: 'Exact technical requirements', documentRequirements: 'Drawing original pending' }
const draft: ToolSpecificationView = { ...current, currentControl: { knowledge: 'TECHNICAL_SPECIFICATION_DRAFT', owner: 'Main.Plm.ToolSpecification.v1', id, version: '2', digest: 'c'.repeat(64) }, sourceMatches: false, productMatches: true,
  specification: { reference: { owner: 'Main.Plm.ToolSpecification.v1', id, version: '2', digest: 'c'.repeat(64) }, source, nativeRowVersion: 'AQ==', requirements, reason: 'Previous reason', actorId: scope.actorId, recordedAt: '2026-10-26T01:00:00Z', originalJson: 'immutable private draft', state: 'DRAFT', approval: 'NOT_REQUESTED', freeze: 'NOT_REQUESTED', physicalQualification: 'UNKNOWN' } }
const receipt = (operationId: string): ToolSpecificationResult => ({ operationId, kind: 'TOOL_SPECIFICATION_DRAFT', objectId: id, version: '1', digest: 'd'.repeat(64) })
const ok = <T>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined
async function click(text: string) { await view!.findAll('button').find(b => b.text() === text)!.trigger('click'); await flushPromises() }
async function render() { view = mount(View); await flushPromises(); await click('打开草稿') }
async function fill() {
  await view!.get('form[aria-label="选择工程产品"]').trigger('submit'); await flushPromises(); await view!.get('select').setValue(productId); await click('采用所选产品')
  for (const field of view!.findAll('textarea')) await field.setValue('Private engineering requirements')
}
async function save() { await view!.get('form[aria-label="保存技术草稿"]').trigger('submit'); await flushPromises() }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); api.canWrite = true
  api.scope.mockResolvedValue(ok(scope)); api.list.mockResolvedValue(ok([structuredClone(current)])); api.read.mockResolvedValue(ok(structuredClone(current)))
  api.products.mockResolvedValue(ok([{ tenantId: scope.tenantId, reference: productRef, isCurrent: true, product: { code: 'P1', name: 'Product', enabled: true }, originalJson: '{}' }]))
  api.save.mockImplementation(async (command: ToolSpecificationCommand) => ok(receipt(command.operationId)))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object | null) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('submits explicit original native, technical and product versions and requires reload after confirmed success', async () => {
  await render(); await fill(); await save()
  expect(api.save.mock.calls[0]![0]).toMatchObject({ action: 'CREATE', source, expectedNativeRowVersion: 'AQ==', expectedTechnicalControl: current.currentControl, requirements: { productRef } })
  expect(view!.text()).toContain('批准：未请求'); expect(view!.text()).toContain('实物资格：未知'); expect(view!.text()).toContain('技术草稿已保存')
  expect(pendingToolSpecification(scope)).toBeNull(); await save(); expect(api.save).toHaveBeenCalledTimes(1)
})
it('keeps input and exact versions on conflict and closes the old key without automatically reloading or resending', async () => {
  api.save.mockRejectedValue({ response: { status: 412 } }); await render(); await fill(); await save()
  const original = pendingToolSpecification(scope)!; expect(view!.text()).toContain('输入和预期版本已保留'); expect(api.read).toHaveBeenCalledTimes(1)
  expect((view!.get('textarea').element as HTMLTextAreaElement).value).toBe('Private engineering requirements')
  expect(Object.keys(original).sort()).toEqual(['objectId', 'operationId']); expect(JSON.stringify(localStorage)).not.toContain('Private engineering')
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'CLOSED' })); await click('结束未提交操作')
  expect(pendingToolSpecification(scope)).toBeNull(); expect(api.save).toHaveBeenCalledTimes(1); expect(api.read).toHaveBeenCalledTimes(1)
})
it('recovers lost results through UNKNOWN and COMMITTED without a second command', async () => {
  api.save.mockRejectedValue(new Error('lost')); await render(); await fill(); await save(); const original = pendingToolSpecification(scope)!
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'UNKNOWN' })); await click('查询原操作'); expect(pendingToolSpecification(scope)).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: receipt(original.operationId) })); await click('查询原操作')
  expect(pendingToolSpecification(scope)).toBeNull(); expect(api.save).toHaveBeenCalledTimes(1); expect(view!.text()).toContain('原操作已提交')
})
it('preserves recovery IDs when the result names another object or claims a different outcome', async () => {
  api.save.mockImplementation(async (command: ToolSpecificationCommand) => ok({ ...receipt(command.operationId), kind: 'APPROVED' })); await render(); await fill(); await save()
  const original = pendingToolSpecification(scope)!; expect(original).not.toBeNull(); expect(view!.text()).toContain('不一致')
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: { ...receipt(original.operationId), objectId: productId } })); await click('查询原操作')
  expect(pendingToolSpecification(scope)).not.toBeNull()
})
it('uses loaded professional control for revision and reads exact history without replacing edited inputs', async () => {
  api.read.mockResolvedValueOnce(ok(structuredClone(draft))); await render(); expect(view!.text()).toContain('来源已变化')
  await view!.get('textarea[aria-label="技术规格要求"]').setValue('Keep my edit')
  api.read.mockResolvedValueOnce(ok({ ...draft, specification: { ...draft.specification!, reference: { ...draft.specification!.reference, version: '1' }, originalJson: 'history original' } }))
  await view!.get('form[aria-label="读取技术历史"]').trigger('submit'); await flushPromises()
  expect(api.read).toHaveBeenLastCalledWith(id, '1'); expect((view!.get('textarea[aria-label="技术规格要求"]').element as HTMLTextAreaElement).value).toBe('Keep my edit')
  await view!.get('textarea[aria-label="草稿理由"]').setValue('New reason'); await save()
  expect(api.save.mock.calls[0]![0]).toMatchObject({ action: 'REVISE', expectedTechnicalControl: draft.currentControl, requirements: { technicalRequirements: 'Keep my edit', productRef } })
})
it('does not expose write controls with only legacy permissions', async () => {
  api.canWrite = false; await render(); expect(view!.find('form[aria-label="选择工程产品"]').exists()).toBe(false)
  expect(view!.findAll('button').some(b => b.text() === '保存技术草稿')).toBe(false); await save(); expect(api.save).not.toHaveBeenCalled()
})
it('requires browser coordination and the unchanged current authenticated scope before any command', async () => {
  await render(); await fill(); Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); await save()
  expect(api.save).not.toHaveBeenCalled(); expect(pendingToolSpecification(scope)).toBeNull()
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
  api.scope.mockResolvedValue(ok({ ...scope, actorId: id })); await save(); expect(api.save).not.toHaveBeenCalled(); expect(pendingToolSpecification(scope)).toBeNull()
})
it('clears private data on identity change and ignores a late source response', async () => {
  let finish!: (value: unknown) => void; api.read.mockReturnValue(new Promise(resolve => { finish = resolve })); view = mount(View); await flushPromises(); await click('打开草稿')
  localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT)); finish(ok(draft)); await flushPromises()
  expect(view!.text()).not.toContain('Private tool'); expect(view!.text()).not.toContain('FULL-ORIGINAL-40'); expect(view!.find('textarea').exists()).toBe(false)
})
it('clears source and requirements on a current permission denial while retaining the minimal original key', async () => {
  api.save.mockRejectedValue(new Error('lost')); await render(); await fill(); await save(); const original = pendingToolSpecification(scope)!
  api.operation.mockRejectedValue({ response: { status: 403 } }); await click('查询原操作')
  expect(view!.text()).not.toContain('Private tool'); expect(view!.text()).not.toContain('FULL-ORIGINAL-40'); expect(view!.find('textarea').exists()).toBe(false)
  expect(pendingToolSpecification(scope)?.operationId).toBe(original.operationId)
})
