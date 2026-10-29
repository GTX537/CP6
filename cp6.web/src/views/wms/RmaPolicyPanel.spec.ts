import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import Panel from './RmaPolicyPanel.vue'
import type { RmaPolicyPending, RmaPolicyVersion } from '@/api/wms/rmaPolicies'
const mock = vi.hoisted(() => ({ read: vi.fn(), publish: vi.fn(), query: vi.fn(), storageRead: vi.fn(), storageSave: vi.fn(), storageRemove: vi.fn() }))
vi.mock('@/api/wms/rmaPolicies', () => ({ rmaPolicyApi: mock }))
vi.mock('@/auth/rmaFormalPendingStore', () => ({ rmaPendingStore: { read: mock.storageRead, save: mock.storageSave, remove: mock.storageRemove } }))
const id = (n: number) => `10000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const reference = (n: number, version = '1') => ({ owner: 'Main.Wms.RmaPolicy.v1', id: id(n), version, digest: 'a'.repeat(64) })
const who = { tenantId: id(1), actorId: id(2) }, key = `policy:${who.tenantId}:${who.actorId}`, option = reference(3)
let view: VueWrapper | undefined, head: RmaPolicyVersion | null, versions: RmaPolicyVersion[], stored: Map<string, unknown>
async function render(canPublish = true) { view = mount(Panel, { props: { scope: who, option, canPublish } }); await flushPromises() }
async function click(text: string) { await view!.findAll('button').find(x => x.text() === text)!.trigger('click'); await flushPromises() }
async function save() { await view!.get('form').trigger('submit'); await flushPromises() }
async function fill() {
  await view!.get('[aria-label="政策名称"]').setValue('Actual policy')
  await view!.get('[aria-label="政策原因代码1"]').setValue('DAMAGED'); await view!.get('[aria-label="政策原因名称1"]').setValue('Damaged')
  await view!.get('[aria-label="政策允许仅退回实物"]').setValue(true); await view!.get('[aria-label="政策期限类型"]').setValue('LIMITED'); await view!.get('[aria-label="政策期限天数"]').setValue('30')
  await view!.get('[aria-label="政策独立审批"]').setValue('YES'); await view!.get('[aria-label="政策发布原因"]').setValue('Approved business rule')
}
function accept(p: RmaPolicyPending) {
  expect(stored.get(key)).toEqual(p)
  head = { reference: reference(4, String(versions.length + 1)), scope: { warehouseId: id(5), siteId: id(6) }, policy: structuredClone(p.body.policy), publishedAt: '2026-10-29T08:00:00Z', publishedBy: reference(7), reason: p.body.reason, previousRef: head?.reference ?? null }
  versions.push(head); return structuredClone(head)
}
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); head = null; versions = []; stored = new Map()
  mock.storageRead.mockImplementation(async (k: string) => stored.get(k)); mock.storageSave.mockImplementation(async (k: string, value: unknown) => { stored.set(k, structuredClone(value)) }); mock.storageRemove.mockImplementation(async (k: string) => { stored.delete(k) })
  mock.read.mockImplementation(async (_scope, ref) => { const value = ref ? versions[Number(ref.version) - 1] : head; return { state: !value ? 'NOT_CONFIGURED' : value.policy.enabled ? 'ENABLED' : 'DISABLED', value: value ? structuredClone(value) : null } })
  mock.publish.mockImplementation(async p => accept(p)); mock.query.mockResolvedValue({ status: 'NOT_OBSERVED', value: null, canInferNoEffect: false })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_k: string, _o: unknown, action: (lock: object) => unknown) => action({}) } })
})
afterEach(() => { view?.unmount(); view = undefined; vi.restoreAllMocks() })
describe('return policy publication', () => {
  it('starts without a policy, requires explicit choices, publishes a disabled successor and reads immutable history', async () => {
    await render(); expect(view!.text()).toContain('尚未配置'); await save(); expect(mock.publish).not.toHaveBeenCalled()
    await fill(); await save(); expect(stored.size).toBe(0); expect(view!.text()).toContain('Actual policy · 版本 1')
    expect(mock.publish.mock.calls[0]![0].body).toMatchObject({ expectedPolicyRef: null, policy: { returnWindowDaysAfterDelivery: 30, requireIndependentApprover: true, allowedOutcomes: ['RETURN_ONLY'] } })
    await view!.get('[aria-label="政策启用"]').setValue(false); await view!.get('[aria-label="政策发布原因"]').setValue('Stop using policy'); await save()
    expect(mock.publish.mock.calls[1]![0].body.expectedPolicyRef).toEqual(reference(4)); expect(view!.text()).toContain('停用'); await click('上一政策版本')
    expect(view!.text()).toContain('Actual policy · 版本 1'); expect(mock.read.mock.lastCall![1]).toEqual(reference(4))
  })
  it('recovers the committed original after publication permission is removed, without publishing again', async () => {
    mock.publish.mockImplementationOnce(async p => { accept(p); throw new Error('Connection lost') })
    await render(); await fill(); await save(); const original = structuredClone(stored.get(key)); expect(original).toBeTruthy(); view!.unmount()
    await render(false); mock.query.mockResolvedValue({ status: 'FOUND', value: versions[0], canInferNoEffect: false }); await click('查询原政策请求')
    expect(stored.size).toBe(0); expect(mock.query).toHaveBeenCalledWith(original); expect(mock.publish).toHaveBeenCalledTimes(1); expect(view!.find('form').exists()).toBe(false)
    expect(view!.text()).toContain('已找回原政策版本1')
  })
  it('retains NOT_OBSERVED and uncertain retry failures, then resends exactly the stored command and policy', async () => {
    mock.publish.mockRejectedValueOnce(new Error('lost')); await render(); await fill(); await save(); const original = structuredClone(stored.get(key))
    await click('查询原政策请求'); expect(stored.get(key)).toEqual(original); expect(mock.publish).toHaveBeenCalledTimes(1)
    mock.publish.mockRejectedValueOnce({ response: { status: 403, data: { code: 'OBJECT_FORBIDDEN', commitKnowledge: 'NOT_ACCEPTED', bindingCreated: false } } }); await click('查询并重派原政策请求')
    expect(stored.get(key)).toEqual(original); await click('读取当前政策'); await click('查询并重派原政策请求')
    expect(mock.publish.mock.lastCall![0]).toEqual(original); expect(stored.size).toBe(0)
  })
  it('drops stale completion when the page is replaced and preserves the old identity recovery record', async () => {
    let finish!: (value: RmaPolicyVersion) => void
    mock.publish.mockImplementationOnce(() => new Promise(resolve => { finish = resolve })); await render(); await fill(); await save()
    expect(stored.size).toBe(1); const original = stored.get(key) as RmaPolicyPending; view!.unmount(); view = undefined
    const value = accept(original); finish(value); await flushPromises(); expect(stored.get(key)).toEqual(original)
  })
  it('stops before sending on storage failure and keeps malformed records for recovery', async () => {
    await render(); await fill(); mock.storageSave.mockRejectedValueOnce(new Error('storage unavailable')); await save(); expect(mock.publish).not.toHaveBeenCalled()
    stored.set(key, { malformed: true }); await click('读取当前政策'); expect(view!.text()).toContain('政策原请求记录异常'); await save(); expect(mock.publish).not.toHaveBeenCalled(); expect(stored.get(key)).toEqual({ malformed: true })
  })
  it('shows a CAS rejection, clears proven initial rejection and requires a fresh current policy read', async () => {
    await render(); await fill(); mock.publish.mockRejectedValueOnce({ response: { status: 412, data: { detail: 'CAS_CONFLICT', code: 'CAS_CONFLICT', commitKnowledge: 'NOT_ACCEPTED', bindingCreated: false } } }); await save()
    expect(view!.text()).toContain('CAS_CONFLICT'); expect(stored.size).toBe(0)
    mock.read.mockRejectedValueOnce({ response: { status: 403, data: { detail: 'FIELD_READ_FORBIDDEN' } } }); await click('读取当前政策'); expect(view!.text()).toContain('FIELD_READ_FORBIDDEN'); expect(view!.text()).not.toContain('Actual policy ·')
  })
})
