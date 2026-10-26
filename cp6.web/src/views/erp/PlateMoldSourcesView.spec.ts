import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import type { PlateSourceVersion } from '@/api/erp/plateMoldSources'
const api = vi.hoisted(() => ({ scope: vi.fn(), list: vi.fn(), read: vi.fn(), authenticated: true }))
vi.mock('@/api/erp/plateMoldSources', () => ({ plateSourceApi: api }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => api.authenticated }))
import View from './PlateMoldSourcesView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const id = '30000000-0000-4000-8000-000000000003'
const row: PlateSourceVersion = {
  tenantId: '10000000-0000-4000-8000-000000000001', reference: { owner: 'Main.Erp.PlateMoldSource.v1', id, version: '2', digest: 'a'.repeat(64) },
  isCurrent: true, sourcePresent: true, observedNativeRowVersion: 'AAAABQ==', currentNativeRowVersion: 'AAAABg==', observedAt: '2026-10-26T00:00:00Z',
  technicalApproval: 'UNKNOWN', physicalQualification: 'UNKNOWN', originalJson: '{"original":"private-native-source"}',
  source: { identity: { baseCode: 'FULL-BASE-12345678901', number: '123456789012345NNNNNNNNNNNNNNNNNNNNNNNNA', businessRevision: 7, name: 'Original tool' },
    technical: { sheetWidth: '99999999999.9999', flexoThickness: null, compositionQuantity: 3 }, validity: { startDate: '2026-01-01', endDate: '2026-12-31' },
    legacyAttachmentFlags: { actual: true }, requirements: { proof: 1 }, control: { isDeleted: false, creator: 'actor', legacyTransferStatus: 9, legacyMcTransferred: true } },
}
const ok = <T>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined
const button = (text: string) => view!.findAll('button').find(b => b.text() === text)!
async function click(text: string) { await button(text).trigger('click'); await flushPromises() }
async function render() { view = mount(View); await flushPromises(); await click('查看来源') }
async function submit(label: string) { view!.get(`[aria-label="${label}"]`).element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises() }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); api.authenticated = true
  api.scope.mockResolvedValue(ok({ tenantId: row.tenantId, actorId: 'actor' })); api.list.mockResolvedValue(ok([structuredClone(row)])); api.read.mockResolvedValue(ok(structuredClone(row)))
})
afterEach(() => { view?.unmount(); view = undefined })
it('shows full native identity, exact values, null and separate observation and business revisions without inferring approval', async () => {
  await render()
  expect(api.read).toHaveBeenCalledWith(id); expect(view!.text()).toContain(row.source.identity.number)
  expect(view!.text()).toContain('99999999999.9999'); expect(view!.text()).toContain('未填写'); expect(view!.text()).toContain('业务修订 7')
  expect(view!.text()).toContain('当前观察版本 2'); expect(view!.text()).toContain('技术批准：未知'); expect(view!.text()).toContain('实物资格：未知')
  expect(view!.text()).toContain('旧传输状态：9'); expect(view!.text()).toContain('AAAABQ=='); expect(view!.text()).toContain('AAAABg==')
  expect(view!.findAll('button').some(b => b.text().includes('保存'))).toBe(false)
})
it('reads an exact observation history without replacing current source and reports missing pre-observation history', async () => {
  await render(); const old = structuredClone(row); old.reference.version = '1'; old.isCurrent = false; old.originalJson = '{"original":"historical"}'
  api.read.mockResolvedValueOnce(ok(old)); await submit('工具观察历史版本')
  expect(api.read).toHaveBeenLastCalledWith(id, '1'); expect(view!.get('[aria-label="工具观察历史"]').text()).toContain('historical')
  expect(view!.get('[aria-label="当前工具来源"]').text()).toContain('当前观察版本 2')
  api.read.mockRejectedValueOnce({ response: { status: 404 } }); await submit('工具观察历史版本')
  expect(view!.find('[aria-label="工具观察历史"]').exists()).toBe(false); expect(view!.text()).toContain('此前的历史不会自动补建')
})
it('searches and pages using the current scoped list and clears the previous selection', async () => {
  await render(); api.list.mockResolvedValueOnce(ok(Array.from({ length: 20 }, (_, i) => ({ ...structuredClone(row), reference: { ...row.reference, id: `${id}-${i}` } }))))
  await view!.get('[aria-label="工具来源搜索"]').setValue('full-case'); await submit('工具来源搜索')
  expect(api.list).toHaveBeenLastCalledWith('full-case', 1); expect(view!.find('[aria-label="当前工具来源"]').exists()).toBe(false)
  api.list.mockResolvedValueOnce(ok([])); await click('下一页'); expect(api.list).toHaveBeenLastCalledWith('full-case', 2)
})
it('clears current and historical source data after a current permission denial', async () => {
  await render(); await submit('工具观察历史版本'); api.read.mockRejectedValueOnce({ response: { status: 403 } }); await submit('工具观察历史版本')
  expect(view!.text()).not.toContain('Original tool'); expect(view!.text()).not.toContain('private-native-source')
  expect(view!.find('[aria-label="当前工具来源"]').exists()).toBe(false); expect(view!.text()).toContain('当前无权读取')
})
it('discards an old identity response and removes all source data on sign-out', async () => {
  await render(); let resolve!: (value: unknown) => void
  api.read.mockReturnValueOnce(new Promise(r => { resolve = r })); const reading = submit('工具观察历史版本')
  await flushPromises(); api.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT)); await flushPromises()
  resolve(ok(row)); await reading; expect(view!.text()).not.toContain('Original tool'); expect(view!.text()).not.toContain('private-native-source')
  expect(view!.find('[aria-label="工具观察历史"]').exists()).toBe(false)
})
