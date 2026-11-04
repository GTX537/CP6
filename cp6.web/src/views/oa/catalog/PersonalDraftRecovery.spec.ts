// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { ElMessageBox } from 'element-plus'
import { newEpoch } from '@/auth/identity'
import { personalFormApi as api, type PersonalDraft, type RecoveryPreview } from '@/api/oa/personalForm'
import Recovery from './PersonalDraftRecovery.vue'
vi.mock('@/api/oa/personalForm', () => ({ personalFormApi: { rebaseHistory: vi.fn(), recoveryPreview: vi.fn() } }))
vi.mock('element-plus', () => ({ ElMessageBox: { confirm: vi.fn() } }))
const draft: PersonalDraft = { draftId: 'source', formKey: 'personal', formVersionId: 'two', formVersion: 2, latestPublishedVersion: 2, latestPublishedVersionId: 'two', name: 'Form', schemaJson: '{"fields":[]}', dataJson: '{}', title: 'current', rowVersion: 'current-token', status: 0, stale: false, updatedAtUtc: 'now', fieldMask: {}, capabilities: [], titleAccess: 'edit' }
const preview: RecoveryPreview = { expectedVersion: 'current-token', source: { historyId: 'exact-history', rebaseCommandId: 'exact-rebase', sourceVersionId: 'one', sourceVersion: 1, targetVersionId: 'two', targetVersion: 2, rebasedAtUtc: 'now', canRecover: true, blockedReason: null }, snapshot: { ...draft, formVersionId: 'one', formVersion: 1, dataJson: '{"amount":9007199254740993.0001}', schemaJson: '{"fields":[{"name":"amount","type":"number"}]}', fieldMask: { amount: 'readonly' } } }
const wrappers: ReturnType<typeof mount>[] = []
const state = (w: any): any => w.vm.$.setupState
function mounted() { const w = mount(Recovery, { props: { draft, locked: false } }); wrappers.push(w); return w }
beforeEach(() => { vi.resetAllMocks(); vi.mocked(api.recoveryPreview).mockResolvedValue(preview); vi.mocked(api.rebaseHistory).mockResolvedValue({ expectedVersion: draft.rowVersion, items: [preview.source], page: 1, pageSize: 20, total: 1 }); vi.mocked(ElMessageBox.confirm).mockImplementation(async () => 'confirm' as Awaited<ReturnType<typeof ElMessageBox.confirm>>) })
afterEach(() => { wrappers.splice(0).forEach(w => w.unmount()) })
it('requires an explicit read and confirmation and sends only the selected source identity, CAS and reason', async () => {
  const w = mounted(); expect(api.rebaseHistory).not.toHaveBeenCalled(); await state(w).read(); await state(w).read('exact-history'); await flushPromises()
  expect(api.recoveryPreview).toHaveBeenCalledWith('source', 'exact-history', expect.any(AbortSignal))
  expect(w.findComponent({ name: 'PersonalFormFields' }).props('disabled')).toBe(true)
  state(w).reason = '  recover original  '; vi.mocked(ElMessageBox.confirm).mockRejectedValueOnce('cancel'); await state(w).recover(); expect(w.emitted('recover')).toBeUndefined()
  await state(w).recover(); expect(w.emitted('recover')).toEqual([['exact-history', 'current-token', 'recover original']])
  expect(ElMessageBox.confirm).toHaveBeenLastCalledWith(expect.stringContaining('尚未保存的修改不会复制'), '恢复为新草稿')
})
it('clears old contents immediately when selecting another snapshot and keeps a failed preview non-actionable', async () => {
  const w = mounted(); await state(w).read('exact-history'); state(w).reason = 'recover'
  let reject!: (error: unknown) => void; vi.mocked(api.recoveryPreview).mockImplementation(() => new Promise((_, r) => { reject = r }))
  const reading = state(w).read('other-history'); expect(state(w).snapshot).toBeUndefined(); await state(w).recover(); expect(w.emitted('recover')).toBeUndefined()
  reject({ response: { status: 403 } }); await reading; expect(state(w).snapshot).toBeUndefined(); expect(state(w).error).not.toBe('')
})
it('does not restore a blocked snapshot or while another command is unresolved', async () => {
  const w = mounted(); vi.mocked(api.recoveryPreview).mockResolvedValue({ ...preview, source: { ...preview.source, canRecover: false, blockedReason: 'FORM_RECOVERY_RETENTION_EXPIRED' } }); await state(w).read('exact-history'); state(w).reason = 'recover'; await state(w).recover()
  expect(w.find('el-alert').attributes('title')).toContain('不允许恢复'); expect(w.emitted('recover')).toBeUndefined(); expect(ElMessageBox.confirm).not.toHaveBeenCalled()
  vi.mocked(api.recoveryPreview).mockResolvedValue(preview); await state(w).read('exact-history'); await w.setProps({ locked: true }); await state(w).recover(); expect(w.emitted('recover')).toBeUndefined()
})
it('discards delayed snapshots after draft selection or authentication identity changes', async () => {
  const w = mounted(); let resolve!: (value: RecoveryPreview) => void; vi.mocked(api.recoveryPreview).mockImplementation(() => new Promise(r => { resolve = r }))
  const first = state(w).read('exact-history'); await w.setProps({ draft: { ...draft, draftId: 'other' } }); resolve(preview); await first; expect(state(w).snapshot).toBeUndefined()
  const second = state(w).read('exact-history'); newEpoch(); resolve(preview); await second; expect(state(w).snapshot).toBeUndefined(); expect(w.emitted('recover')).toBeUndefined()
})
it('cancels an outstanding confirmation when the source version changes', async () => {
  const w = mounted(); await state(w).read('exact-history'); state(w).reason = 'recover'
  let confirm!: (value: any) => void; vi.mocked(ElMessageBox.confirm).mockImplementation(() => new Promise(r => { confirm = r }))
  const pending = state(w).recover(); await w.setProps({ draft: { ...draft, rowVersion: 'changed-version' } }); confirm('confirm'); await pending
  expect(w.emitted('recover')).toBeUndefined(); expect(state(w).snapshot).toBeUndefined()
})
