import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import Panel from './RmaAuthorizationPanel.vue'
import type { AuthorizationContext, AuthorizationDecision, AuthorizationPending } from '@/api/wms/rmaAuthorizations'
const mock = vi.hoisted(() => ({ context: vi.fn(), read: vi.fn(), dispatch: vi.fn(), query: vi.fn(), storageRead: vi.fn(), storageSave: vi.fn(), storageRemove: vi.fn() }))
vi.mock('@/api/wms/rmaAuthorizations', async original => ({ ...await original<object>(), rmaAuthorizationApi: mock }))
vi.mock('@/auth/rmaFormalPendingStore', () => ({ rmaPendingStore: { read: mock.storageRead, save: mock.storageSave, remove: mock.storageRemove } }))
const id = (n: number) => `10000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const reference = (n: number) => ({ owner: 'Simulation.Owner.v1', id: id(n), version: '1', digest: 'a'.repeat(64) })
const who = { tenantId: id(1), actorId: id(2) }, app = id(3), key = `authorization:${who.tenantId}:${who.actorId}`, amount = '9999999999999.12345678', unit = { code: 'EA', version: '1' }
let view: VueWrapper | undefined, context: AuthorizationContext, decisions: AuthorizationDecision[], stored: Map<string, unknown>
async function render(write = true) { view = mount(Panel, { props: { scope: who, applicationId: app, submitted: true, canDecide: write, canRevoke: write } }); await flushPromises() }
async function click(text: string) { await view!.findAll('button').find(x => x.text() === text)!.trigger('click'); await flushPromises() }
async function approve() { await click('读取可退额度'); await view!.get('[aria-label="授权决定原因"]').setValue('ACCEPTED_RETURN'); await click('批准退货') }
function accept(p: AuthorizationPending): AuthorizationDecision {
  expect(stored.get(key)).toEqual(p)
  const previous = decisions.at(-1)
  const value: AuthorizationDecision = { decisionId: id(20), decisionVersion: decisions.length + 1, decisionRef: { ...reference(20), version: String(decisions.length + 1) }, applicationRef: reference(3), outcome: p.kind === 'REVOKE' ? 'REVOKED' : p.body.outcome, authorizedShares: p.kind === 'DECIDE' ? p.body.authorizedShares.map(x => ({ shareId: id(21), rmaId: app, ranges: x.ranges, quantity: x.quantity, rangeClaimRef: reference(22), state: 'AUTHORIZED', original: { deliveryProofRef: reference(23) } })) : [], reasonCodes: p.kind === 'DECIDE' ? p.body.reasonCodes : ['AUTHORIZATION_REVOKED'], decidedAt: '2026-10-29T10:00:00Z', validFrom: '2026-10-29T10:00:00Z', validTo: null, previousDecisionRef: previous?.decisionRef ?? null }
  decisions.push(value); return structuredClone(value)
}
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); stored = new Map(); decisions = []
  const quantity = (value: string) => ({ value, uom: unit })
  context = { applicationRef: reference(3), authorityRef: reference(4), policy: { reference: reference(5), policy: { name: '模拟验收退货政策', enabled: true, reasonOptions: [], allowedOutcomes: ['RETURN_ONLY'], returnWindowDaysAfterDelivery: 30, requireIndependentApprover: true }, scope: { warehouseId: id(6), siteId: id(7) }, publishedAt: '2026-10-29', publishedBy: reference(2), reason: 'Simulation', previousRef: null }, currentDecision: null, currentShareRefs: [], lines: [{ requestedLineId: id(8), applicationLineRef: reference(8), coordinate: { baseUom: unit }, entitlement: { ref: reference(9), activeClaimQty: quantity('0'), receivedQty: quantity('0'), creditedQty: quantity('0'), remainingAuthorizableQty: quantity(amount), currentHeadRef: reference(10), coverage: 'COMPLETE' }, availableRanges: [{ from: '0', to: amount }], evidenceMode: 'SIMULATED' }] }
  mock.storageRead.mockImplementation(async (k: string) => stored.get(k)); mock.storageSave.mockImplementation(async (k: string, p: unknown) => { stored.set(k, structuredClone(p)) }); mock.storageRemove.mockImplementation(async (k: string) => { stored.delete(k) })
  mock.context.mockImplementation(async (_app: string, include: boolean) => structuredClone({ ...context, lines: include ? context.lines : [], currentDecision: decisions.at(-1) ?? null, currentShareRefs: decisions.at(-1)?.outcome === 'APPROVED' ? [reference(21)] : [] }))
  mock.read.mockImplementation(async (_app: string, version?: number) => structuredClone(version ? decisions[version - 1] : decisions.at(-1) ?? null))
  mock.dispatch.mockImplementation(async p => accept(p)); mock.query.mockResolvedValue({ status: 'NOT_OBSERVED', value: null, canInferNoEffect: false })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_k: string, _o: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined; vi.restoreAllMocks() })
describe('return authorization with simulated business sources', () => {
  it('keeps exact decimal ranges through approval and revocation, with immutable history', async () => {
    await render(); expect(mock.context).toHaveBeenLastCalledWith(app, false); await approve()
    const p = mock.dispatch.mock.calls[0]![0]; expect(p.body.authorizedShares[0]).toMatchObject({ ranges: [{ from: '0', to: amount }], quantity: { value: amount, uom: unit } }); expect(p.body.expectedEntitlementHeadRefs).toEqual([reference(10)])
    expect(view!.text()).toContain(amount); expect(view!.text()).toContain('签收来源 Simulation.Owner.v1'); expect(stored.size).toBe(0)
    await view!.get('[aria-label="授权撤销原因"]').setValue('Customer changed request'); await click('撤销当前授权')
    expect(mock.dispatch.mock.lastCall![0].body.affectedReturnShareRefs).toEqual([reference(21)]); expect(view!.text()).toContain('已撤销'); await click('上一授权版本'); expect(view!.text()).toContain('已批准'); expect(view!.text()).toContain(amount)
  })
  it('requires a reason and loads complete simulated ranges only for approval; rejects without source access', async () => {
    await render(); await click('拒绝退货'); expect(mock.dispatch).not.toHaveBeenCalled()
    await view!.get('[aria-label="授权决定原因"]').setValue('UNSUPPORTED_REASON'); await click('拒绝退货')
    expect(mock.context.mock.calls.every(x => x[1] === false)).toBe(true); expect(mock.dispatch.mock.lastCall![0].body).toMatchObject({ outcome: 'REJECTED', authorizedShares: [], expectedEntitlementHeadRefs: [], exceptionApprovalRef: null })
    await click('读取可退额度'); expect(view!.text()).toContain('模拟业务数据')
    await view!.get('[aria-label="批准终点1-1"]').setValue('0'); await view!.get('[aria-label="授权决定原因"]').setValue('VALID'); await click('批准退货'); expect(mock.dispatch).toHaveBeenCalledTimes(1); expect(view!.text()).toContain('区间终点必须大于起点')
  })
  it('recovers an accepted original after write permission is removed without resubmission', async () => {
    mock.dispatch.mockImplementationOnce(async p => { accept(p); throw new Error('lost acknowledgement') }); await render(); await approve(); const original = structuredClone(stored.get(key))
    view!.unmount(); await render(false); mock.query.mockResolvedValue({ status: 'FOUND', value: decisions[0], canInferNoEffect: false }); await click('查询原授权请求')
    expect(mock.query).toHaveBeenCalledWith(original); expect(mock.dispatch).toHaveBeenCalledTimes(1); expect(stored.size).toBe(0); expect(view!.find('fieldset').exists()).toBe(false)
  })
  it('retains an unknown original and failed retries, then resends its exact frozen body on explicit retry', async () => {
    mock.dispatch.mockRejectedValueOnce(new Error('unknown')); await render(); await approve(); const original = structuredClone(stored.get(key)); await click('查询原授权请求')
    expect(stored.get(key)).toEqual(original); expect(mock.dispatch).toHaveBeenCalledTimes(1)
    mock.dispatch.mockRejectedValueOnce({ response: { status: 403, data: { code: 'OBJECT_FORBIDDEN', commitKnowledge: 'NOT_ACCEPTED', bindingCreated: false } } }); await click('查询并重派原授权请求'); expect(stored.get(key)).toEqual(original)
    await click('查询并重派原授权请求'); expect(mock.dispatch.mock.lastCall![0]).toEqual(original); expect(stored.size).toBe(0)
  })
  it('drops a stale response after changing applications while preserving the in-flight original', async () => {
    let finish!: () => void
    mock.dispatch.mockImplementationOnce(p => new Promise(resolve => { const value = accept(p); finish = () => resolve(value) }))
    await render(); await approve(); const original = structuredClone(stored.get(key)); mock.read.mockResolvedValue(null); mock.context.mockImplementation(async () => ({ ...context, lines: [], currentDecision: null, currentShareRefs: [] }))
    await view!.setProps({ applicationId: id(30) }); await flushPromises(); finish(); await flushPromises()
    expect(stored.get(key)).toEqual(original); expect(view!.text()).not.toContain('授权决定已保存'); expect(view!.find('article').exists()).toBe(false)
  })
  it('clears disclosed decisions on current denial and keeps private requests out of localStorage', async () => {
    await render(); await approve(); mock.read.mockRejectedValueOnce({ response: { status: 403, data: { detail: 'SCOPE_FORBIDDEN' } } }); await click('刷新授权')
    expect(view!.find('article').exists()).toBe(false); expect(view!.text()).toContain('SCOPE_FORBIDDEN'); expect(JSON.stringify(localStorage)).not.toMatch(/ACCEPTED_RETURN|authorityRef|authorizedShares/)
  })
  it('preserves an unrecognized stored original and prevents a new decision', async () => {
    stored.set(key, { version: 99 }); await render(); expect(view!.text()).toContain('原请求记录异常'); expect(view!.get('fieldset').attributes('disabled')).toBeDefined(); expect(stored.get(key)).toEqual({ version: 99 })
  })
})
