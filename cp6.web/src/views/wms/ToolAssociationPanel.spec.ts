import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { ToolInstanceView } from '@/api/wms/toolInstances'
import type { ToolAssociationProposal, ToolAssociationDecision, ToolAssociationView } from '@/api/wms/toolAssociations'
const api = vi.hoisted(() => ({ scope: vi.fn(), candidates: vi.fn(), list: vi.fn(), read: vi.fn(), propose: vi.fn(), decide: vi.fn(), operation: vi.fn(), allowed: new Set<string>() }))
vi.mock('@/api/wms/toolInstances', () => ({ toolInstanceApi: api }))
vi.mock('@/api/wms/toolAssociations', () => ({ toolAssociationApi: api, associationEtag: (v: ToolAssociationView) => `"tool-association:${v.subject.proposalId}:${v.revision}"` }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ has: (key: string) => api.allowed.has(key) }) }))
import View from './ToolAssociationPanel.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import { pendingToolAssociation } from '@/auth/toolAssociationRecovery'
const scope = { tenantId: '10000000-0000-4000-8000-000000000001', actorId: '20000000-0000-4000-8000-000000000002' }
const id = '30000000-0000-4000-8000-000000000003', proposalId = '40000000-0000-4000-8000-000000000004', roundId = '50000000-0000-4000-8000-000000000005'
const reference = { owner: 'Main.Wms.ToolInstance.v1' as const, id, version: '7', digest: 'a'.repeat(64) }
const technical = { owner: 'Main.Plm.ToolSpecification.v1', id: '60000000-0000-4000-8000-000000000006', version: '9', digest: 'b'.repeat(64) }
const selected = { tenantId: scope.tenantId, reference, etag: '"original-native"', source: { status: 0 } } as ToolInstanceView
const candidate = { technical, native: { owner: 'Main.Erp.PlateMoldSource.v1', id: technical.id, version: '4', digest: 'c'.repeat(64) }, baseCode: 'BASE1', number: 'Exact raw e\u0301', businessRevision: 7, requirements: { technicalRequirements: 'Exact technical text', documentRequirements: 'Original DOC pending' }, technicalState: 'DRAFT', approval: 'NOT_REQUESTED' }
const relation = { subject: { tenantId: scope.tenantId, proposalId, technical, physical: reference, baseCode: 'BASE1', number: candidate.number, businessRevision: 7, scope: { purpose: 'Test private scope', productApplicability: 'A', processApplicability: 'Cut', configurationApplicability: 'A' } }, subjectDigest: 'd'.repeat(64), revision: '2', controlRevision: '1', generation: '1', roundId, state: 'UNKNOWN', lastKnownState: 'UNKNOWN', everConfirmed: false, currentKnowledge: 'UNKNOWN', unresolvedRoles: ['PHYSICAL_INSTANCE_ASSOCIATION'], engineering: { owner: 'Main.Plm.ToolInstanceConformance.v1' }, physical: null, asOf: '2026-10-27T00:00:00Z', notExecutionPermit: true } as ToolAssociationView
const ok = <T>(data: T) => ({ code: 0, data })
const receipt = (body: ToolAssociationProposal | ToolAssociationDecision) => ({ operationId: body.operationId, objectId: id, proposalId: body.proposalId, kind: 'TOOL_ASSOCIATION', version: '3', digest: 'e'.repeat(64), applied: true, outcome: 'UNKNOWN', roundId, generation: '1', notExecutionPermit: true })
let view: VueWrapper | undefined
async function click(text: string) { const button = view!.findAll('button').find(b => b.text() === text); expect(button, text).toBeDefined(); await button!.trigger('click'); await flushPromises() }
async function open() { view = mount(View, { props: { scope, selected } }); await flushPromises() }
async function prepare() {
  await open(); await view!.get('input[aria-label="完整工程编号"]').setValue(candidate.number); await view!.get('input[aria-label="业务修订号"]').setValue('7'); await click('查找准确工程版本')
  await view!.get('select[aria-label="技术来源"]').setValue(technical.id)
  for (const label of ['关联产品范围', '关联工艺范围', '关联配置范围', '关联用途', '关联理由']) await view!.get(`textarea[aria-label="${label}"]`).setValue(`Private ${label}`)
  await view!.get('input[aria-label="关联生效起日"]').setValue('2026-10-27')
}
async function submit() { await view!.get('form').trigger('submit'); await flushPromises() }
async function readRelation() { await click('读取当前关联'); await click(`BASE1 / ${candidate.number} · Rev 7 · 技术版本 9`) }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'one'); api.allowed = new Set(['wms-plate-mold:engineering-link', 'plm-tool-specification:instance-associate'])
  api.scope.mockResolvedValue(ok(scope)); api.candidates.mockResolvedValue(ok([candidate])); api.list.mockResolvedValue(ok([relation])); api.read.mockResolvedValue(ok(relation))
  api.propose.mockImplementation(async (_id: string, _etag: string, body: ToolAssociationProposal) => ok(receipt(body)))
  api.decide.mockImplementation(async (_engineering: boolean, _etag: string, body: ToolAssociationDecision) => ok(receipt(body)))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _opts: unknown, fn: (lock: object | null) => unknown) => fn({}) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('chooses exact No/Rev and submits original technical and physical references with native If-Match', async () => {
  await prepare(); await submit(); expect(api.candidates).toHaveBeenCalledWith(id, candidate.number, 7)
  expect(api.propose.mock.calls[0]!.slice(0, 2)).toEqual([id, selected.etag]); expect(api.propose.mock.calls[0]![2]).toMatchObject({ technical, physical: reference, scope: { effectiveFrom: '2026-10-27T00:00:00Z', effectiveTo: null }, supersedes: null })
  expect(pendingToolAssociation(scope)).toBeNull(); await submit(); expect(api.propose).toHaveBeenCalledTimes(1); expect(view!.text()).toContain('技术批准尚未取得')
})
it('does not substitute a newer candidate after a query changes', async () => {
  await prepare(); await view!.get('input[aria-label="业务修订号"]').setValue('8'); await submit(); expect(api.propose).not.toHaveBeenCalled(); expect(view!.find('select').text()).not.toContain('技术版本 9')
})
it('keeps input and selected original on 412, stores only recovery IDs, and closes without resending', async () => {
  api.propose.mockRejectedValue({ response: { status: 412 } }); await prepare(); await submit(); const pending = pendingToolAssociation(scope)!
  expect(Object.keys(pending).sort()).toEqual(['objectId', 'operationId', 'proposalId']); expect(JSON.stringify(localStorage)).not.toContain('Private'); expect(view!.text()).toContain('输入和原版本已保留')
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'CLOSED' })); await click('结束未提交关联操作')
  expect(api.propose).toHaveBeenCalledTimes(1); expect((view!.get('select[aria-label="技术来源"]').element as HTMLSelectElement).value).toBe(technical.id)
  expect((view!.get('textarea[aria-label="关联理由"]').element as HTMLTextAreaElement).value).toBe('Private 关联理由')
})
it('requires the independent engineering action and sends one exact round to the chosen Owner', async () => {
  api.allowed.delete('plm-tool-specification:instance-associate'); await open(); await readRelation(); expect(view!.findAll('button').some(b => b.text() === '工程确认对应')).toBe(false)
  await view!.get('textarea[aria-label="关联确认依据"]').setValue('Physical inspection on this exact pair'); await click('实物确认对应')
  expect(api.decide.mock.calls[0]![0]).toBe(false); expect(api.decide.mock.calls[0]![2]).toMatchObject({ proposalId, roundId, subjectDigest: relation.subjectDigest, expectedControlRevision: '1', expectedGeneration: '1', decision: 'CONFIRM' })
  expect(view!.text()).not.toContain('双方已确认对应'); expect(view!.text()).toContain('本方操作已保存')
})
it('recovery rejects another proposal and retains unknown operations after identity changes', async () => {
  api.propose.mockRejectedValue(new Error('lost response')); await prepare(); await submit(); const pending = pendingToolAssociation(scope)!, body = api.propose.mock.calls[0]![2]
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'COMMITTED', result: { ...receipt(body), proposalId } })); await click('查询原关联操作'); expect(pendingToolAssociation(scope)).not.toBeNull()
  api.operation.mockResolvedValue(ok({ operationId: pending.operationId, state: 'COMMITTED', result: receipt(body) })); await click('查询原关联操作'); expect(pendingToolAssociation(scope)).toBeNull(); expect(api.propose).toHaveBeenCalledTimes(1)
  localStorage.setItem(AUTH_EPOCH, 'two'); window.dispatchEvent(new Event(AUTH_EVENT)); await flushPromises(); expect(view!.text()).not.toContain('Private')
})
it('clears private state on revoked authority and ignores late responses for another instance', async () => {
  await prepare(); api.list.mockRejectedValue({ response: { status: 403 } }); await click('读取当前关联'); expect(view!.emitted('authority-denied')).toHaveLength(1); expect(view!.text()).not.toContain('Exact technical text')
  let resolve!: (value: unknown) => void; api.list.mockReturnValue(new Promise(r => { resolve = r })); await click('读取当前关联'); await view!.setProps({ selected: { ...selected, reference: { ...reference, id: proposalId } } }); resolve(ok([relation])); await flushPromises(); expect(view!.text()).not.toContain('Test private scope')
})
