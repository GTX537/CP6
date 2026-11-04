// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { personalFormApi as api, type PersonalContext, type PersonalDraft } from '@/api/oa/personalForm'
import { formDispositionApi } from '@/api/oa/formDisposition'
import { identityStamp, newEpoch } from '@/auth/identity'
import Editor from './PersonalFormEditor.vue'
import Fields from './PersonalFormFields.vue'
import { usePersonalCommand } from './usePersonalCommand'
import { formObject, readFormJson, schemaFields, writeFormJson } from './personalFormJson'
vi.mock('@/api/oa/personalForm', async importOriginal => ({ ...await importOriginal<object>(), personalFormApi: { context: vi.fn(), draft: vi.fn(), list: vi.fn(), send: vi.fn(), original: vi.fn(), close: vi.fn(), preview: vi.fn(), rebasePreview: vi.fn() } }))
vi.mock('element-plus', () => ({ ElMessageBox: { confirm: vi.fn().mockResolvedValue(true) } }))
vi.mock('@/api/oa/formDisposition', () => ({ formDispositionApi: { read: vi.fn() } }))
const wrappers: ReturnType<typeof mount>[] = []
const state = (w: any): any => w.vm.$.setupState
const context: PersonalContext = { formKey: 'personal', formVersionId: 'form-one', formVersion: 1, name: '金额表单', schemaJson: '{"fields":[{"name":"amount","type":"number","default":9007199254740993.0001}]}', activationRevision: 'form-active', bindingRevision: null, flowVersionId: null, flowVersion: null, flowActivationRevision: null, allowsUnbound: true, capabilities: ['CREATE_DRAFT', 'SUBMIT'], contextVersion: 'current', fieldMask: { amount: 'edit' }, titleAccess: 'edit' }
const draft: PersonalDraft = { ...context, draftId: 'draft-one', latestPublishedVersion: 2, latestPublishedVersionId: 'form-two', dataJson: '{"amount":9007199254740993.0001}', title: 'draft', rowVersion: 'draft-token', status: 0, stale: true, updatedAtUtc: 'now', capabilities: ['SAVE', 'SUBMIT_WITH_CURRENT_CONTEXT', 'REBASE'] }
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear(); vi.mocked(api.context).mockResolvedValue(context); vi.mocked(api.draft).mockResolvedValue(draft) })
afterEach(() => { wrappers.splice(0).forEach(w => w.unmount()); vi.restoreAllMocks() })
async function editor(props = { formKey: 'personal' } as { formKey?: string; draftId?: string }) { const w = mount(Editor, { props }); wrappers.push(w); await flushPromises(); return w }
const controlled = { requestId: 'exact-request', receiptVersion: 'retained-receipt' }
const controlledPreview = { previewId: 'qualified-preview', digest: 'digest', sourceVersionId: 'form-one', targetVersionId: 'form-two', expectedVersion: 'draft-token', sourceDraftVersion: 'draft-token', targetSchemaDigest: 'schema', expiresAtUtc: 'later', changes: [], validationErrors: [], requiresConfirmation: false, retainedPaths: ['amount'], removedPaths: [], addedPaths: [], typeChangedPaths: [], defaultedPaths: [], requiredAfterRebase: [], diagnostics: [], dispositionRef: controlled }
it('focuses the newly recovered draft without a duplicate read and can return to the source draft', async () => {
  const w = await editor({ draftId: draft.draftId }); const recovered = { ...draft, draftId: 'new-draft', rowVersion: 'new-token' }
  vi.mocked(api.send).mockResolvedValue({ commandId: 'recover', outcome: 'SUCCEEDED', resourceId: recovered.draftId, resourceVersion: recovered.rowVersion, result: recovered }); vi.mocked(api.draft).mockResolvedValue(recovered)
  await state(w).recoverSnapshot('exact-history', 'source-current-token', 'restore original'); await flushPromises()
  expect(vi.mocked(api.send).mock.calls[0]![0]).toBe('/oa/v2/drafts/draft-one/recover-before-rebase')
  expect(JSON.parse(vi.mocked(api.send).mock.calls[0]![2])).toMatchObject({ expectedVersion: 'source-current-token', body: { historyId: 'exact-history', reason: 'restore original' } })
  expect(w.emitted('changed')?.at(-1)).toEqual(['new-draft']); expect(api.draft).toHaveBeenCalledTimes(2)
  await w.setProps({ draftId: recovered.draftId }); await flushPromises(); expect(api.draft).toHaveBeenCalledTimes(2)
  vi.mocked(api.draft).mockResolvedValue(draft); await w.setProps({ draftId: draft.draftId }); await flushPromises(); expect(state(w).draft.draftId).toBe(draft.draftId); expect(api.draft).toHaveBeenCalledTimes(3)
})
it('stays idle without a selected row and reads only when the user selects a draft', async () => {
  const w = await editor({})
  expect(api.draft).not.toHaveBeenCalled(); expect(api.context).not.toHaveBeenCalled(); expect(state(w).readError).toBe('')
  expect(w.text()).not.toContain('重新读取')
  await w.setProps({ draftId: draft.draftId }); await flushPromises()
  expect(api.draft).toHaveBeenCalledExactlyOnceWith(draft.draftId); expect(state(w).draft.draftId).toBe(draft.draftId)
})
it('recovers a missing row through the original command and retains its identity if the current read fails', async () => {
  const intent = { identity: identityStamp(), commandId: 'delete-original', operation: 'FORM_DRAFT_DELETE', resource: draft.draftId }
  sessionStorage.setItem('cp6-personal-form-pending', JSON.stringify(intent))
  const w = await editor({}); expect(state(w).locked).toBe(true); expect(api.draft).not.toHaveBeenCalled()
  vi.mocked(api.original).mockResolvedValue({ commandId: intent.commandId, outcome: 'SUCCEEDED', resourceId: draft.draftId, resourceVersion: 'deleted-version', result: { ...draft, status: 2, rowVersion: 'deleted-version' } })
  vi.mocked(api.draft).mockRejectedValueOnce({ response: { status: 503 } })
  await state(w).commands.recover(); await flushPromises()
  expect(api.original).toHaveBeenCalledWith(intent.operation, intent.resource, intent.commandId, expect.any(AbortSignal))
  expect(state(w).draft.status).toBe(2); expect(state(w).draft.capabilities).toEqual([]); expect(state(w).readError).toContain('当前草稿读取失败')
  vi.mocked(api.draft).mockResolvedValue({ ...draft, status: 2, capabilities: ['RESTORE'] })
  await state(w).reload(); await flushPromises()
  expect(api.draft).toHaveBeenLastCalledWith(draft.draftId); expect(state(w).draft.capabilities).toEqual(['RESTORE'])
  expect(state(w).readError).toBe(''); expect(api.send).not.toHaveBeenCalled(); expect(sessionStorage.getItem('cp6-personal-form-pending')).toBeNull()
})
it('keeps corrupt recovery storage visibly locked even before a row is selected', async () => {
  sessionStorage.setItem('cp6-personal-form-pending', '{broken')
  const w = await editor({}); expect(state(w).storageUnavailable).toBe(true); expect(state(w).locked).toBe(true)
  expect(state(w).error).toContain('停止发送新操作'); await state(w).save(); await state(w).submit()
  expect(api.send).not.toHaveBeenCalled(); expect(api.draft).not.toHaveBeenCalled(); expect(api.context).not.toHaveBeenCalled()
})
it('keeps a real controlled request after hidden-loss denial and sends only the exact adopted receipt', async () => {
  vi.mocked(api.rebasePreview).mockRejectedValueOnce({ response: { status: 403, data: { code: 'REBASE_HIDDEN_LOSS_BLOCKED', dispositionRequestId: controlled.requestId } } }).mockResolvedValue(controlledPreview)
  vi.mocked(formDispositionApi.read).mockResolvedValue({ request: { requestId: controlled.requestId, state: 'RETAINED', dispositionRef: controlled, hold: null } } as any)
  const w = await editor({ draftId: 'draft-one' }); await state(w).previewRebase(); await flushPromises()
  expect(w.text()).toContain('exact-request'); expect(state(w).draft.draftId).toBe('draft-one'); expect(state(w).rebase).toBeUndefined()
  await state(w).applyRebase(); expect(api.send).not.toHaveBeenCalled()
  await state(w).refreshDisposition(); expect(api.rebasePreview).toHaveBeenLastCalledWith('draft-one', 'form-two', 'draft-token', controlled)
  vi.mocked(api.send).mockRejectedValue({ response: { status: 503 } }); await state(w).applyRebase()
  expect(JSON.parse(vi.mocked(api.send).mock.calls[0]![2]).body).toMatchObject({ dispositionRef: controlled, confirmRemovedValues: false })
  expect(state(w).locked).toBe(true); await state(w).refreshDisposition(); expect(formDispositionApi.read).toHaveBeenCalledTimes(1)
  expect(sessionStorage.getItem('cp6-personal-form-pending')).not.toContain('retained-receipt')
})
it('does not preview while the actual disposition is held or no longer retained', async () => {
  const w = await editor({ draftId: 'draft-one' }); state(w).dispositionRequest = controlled.requestId
  vi.mocked(formDispositionApi.read).mockResolvedValue({ request: { dispositionRef: controlled, hold: { active: true } } } as any)
  await state(w).refreshDisposition(); expect(api.rebasePreview).not.toHaveBeenCalled(); expect(state(w).dispositionNotice).toContain('保留限制')
  vi.mocked(formDispositionApi.read).mockResolvedValue({ request: { dispositionRef: null, hold: null, state: 'REVOKED' } } as any)
  await state(w).refreshDisposition(); expect(api.rebasePreview).not.toHaveBeenCalled(); expect(state(w).rebase).toBeUndefined()
})
it('discards a late qualified disposition read after authentication changes', async () => {
  const w = await editor({ draftId: 'draft-one' }); state(w).dispositionRequest = controlled.requestId
  let resolve!: (value: any) => void; vi.mocked(formDispositionApi.read).mockImplementation(() => new Promise(r => { resolve = r }))
  const loading = state(w).refreshDisposition(); newEpoch(); resolve({ request: { dispositionRef: controlled } }); await loading; await flushPromises()
  expect(api.rebasePreview).not.toHaveBeenCalled(); expect(state(w).dispositionRequest).toBe(''); expect(state(w).draft).toBeUndefined()
})
it('shows classified rebase losses and required rows, requires confirmation and invalidates the preview after edits', async () => {
  vi.mocked(api.rebasePreview).mockResolvedValue({ previewId: 'preview', digest: 'digest', sourceVersionId: 'form-one', targetVersionId: 'form-two', expectedVersion: 'draft-token', sourceDraftVersion: 'draft-token', targetSchemaDigest: 'schema', expiresAtUtc: 'later', changes: [{ path: 'removed', kind: 'REMOVED', hasValue: true }], validationErrors: ['FORM_VALIDATION_FAILED'], requiresConfirmation: true, retainedPaths: ['amount'], removedPaths: ['removed'], addedPaths: ['required'], typeChangedPaths: [], defaultedPaths: [], requiredAfterRebase: ['required'], diagnostics: [{ path: 'required', code: 'REQUIRED' }, { path: 'rows[1]', code: 'ROW_COUNT_EXCEEDS_TARGET' }] })
  const w = await editor({ draftId: 'draft-one' }); await state(w).previewRebase(); await flushPromises()
  expect(w.text()).toContain('保留'); expect(w.text()).toContain('移除'); expect(w.text()).toContain('待补填'); expect(w.text()).toContain('行数超过新版限制')
  await state(w).applyRebase(); expect(api.send).not.toHaveBeenCalled()
  state(w).confirmedLoss = true; state(w).title = 'changed'; await flushPromises(); expect(state(w).rebase).toBeUndefined(); expect(state(w).confirmedLoss).toBe(false)
  await state(w).applyRebase(); expect(api.send).not.toHaveBeenCalled()
  state(w).title = draft.title; await flushPromises(); await state(w).previewRebase(); state(w).model = formObject(readFormJson('{"amount":2}')); await flushPromises(); expect(state(w).rebase).toBeUndefined()
  state(w).model = formObject(readFormJson(draft.dataJson)); await flushPromises(); await state(w).previewRebase(); state(w).confirmedLoss = true
  vi.mocked(api.send).mockRejectedValue({ response: { status: 503 } }); await state(w).applyRebase()
  const raw = JSON.parse(vi.mocked(api.send).mock.calls[0]![2]); expect(raw.expectedVersion).toBe('draft-token')
  expect(raw.body).toEqual({ previewId: 'preview', previewDigest: 'digest', targetVersionId: 'form-two', confirmRemovedValues: true })
})
it('sends DIRECT once with the displayed five-context snapshot and exact number lexemes', async () => {
  vi.mocked(api.send).mockRejectedValue({ response: { status: 503 } })
  const w = await editor(); await state(w).submit(); await state(w).submit()
  expect(api.send).toHaveBeenCalledTimes(1)
  const raw = vi.mocked(api.send).mock.calls[0]![2]
  expect(raw).toContain('9007199254740993.0001')
  expect(JSON.parse(raw).body).toMatchObject({ kind: 'DIRECT', expectedFormVersionId: 'form-one', expectedActivationRevision: 'form-active', expectedBindingRevision: null, expectedFlowVersionId: null, expectedFlowActivationRevision: null })
  const metadata = sessionStorage.getItem('cp6-personal-form-pending')!
  expect(metadata).not.toContain('amount'); expect(metadata).not.toContain('9007199'); expect(metadata).not.toContain('dataJson')
})
it('submits the saved draft with no data and does not confuse newer publication with the active admission version', async () => {
  vi.mocked(api.send).mockRejectedValue({ response: { status: 409, data: { code: 'DRAFT_VERSION_CONFLICT' } } })
  const w = await editor({ draftId: 'draft-one' }); await state(w).submit()
  const raw = JSON.parse(vi.mocked(api.send).mock.calls[0]![2]); expect(raw.expectedVersion).toBe('draft-token'); expect(raw.body.kind).toBe('DRAFT'); expect(raw.body).not.toHaveProperty('data')
  expect(state(w).draft.dataJson).toContain('9007199254740993.0001'); expect(state(w).pending).toBeUndefined()
  state(w).model = formObject(readFormJson('{"amount":2}')); await state(w).submit(); expect(api.send).toHaveBeenCalledTimes(1); expect(state(w).readError).toContain('先保存')
})
it('discards a late context response after the authentication identity changes', async () => {
  let resolve!: (v: PersonalContext) => void; vi.mocked(api.context).mockImplementation(() => new Promise(r => { resolve = r }))
  const w = mount(Editor, { props: { formKey: 'personal' } }); wrappers.push(w)
  newEpoch(); resolve(context); await flushPromises(); expect(state(w).context).toBeUndefined(); expect(w.text()).not.toContain('金额表单')
})
it('loads a newly selected draft while the old selection is still in flight', async () => {
  let first!: (v: PersonalDraft) => void, second!: (v: PersonalDraft) => void
  vi.mocked(api.draft).mockImplementation(id => new Promise(resolve => { if (id === 'one') first = resolve; else second = resolve }))
  const w = mount(Editor, { props: { draftId: 'one' } }); wrappers.push(w)
  await w.setProps({ draftId: 'two' }); first({ ...draft, draftId: 'one' }); await flushPromises()
  expect(state(w).loading).toBe(true); expect(state(w).draft).toBeUndefined()
  second({ ...draft, draftId: 'two' }); await flushPromises()
  expect(state(w).draft.draftId).toBe('two'); expect(state(w).loading).toBe(false)
})
it('retains unknown metadata across denied recovery, and closes instead of minting a new intent from NOT_OBSERVED', async () => {
  let command: ReturnType<typeof usePersonalCommand> | undefined
  const accepted = vi.fn(), clear = vi.fn()
  const Harness = defineComponent({ setup() { command = usePersonalCommand(clear, accepted); return () => h('div') } })
  const w = mount(Harness); wrappers.push(w)
  vi.mocked(api.send).mockRejectedValue({ response: { status: 503 } })
  await command!.execute('FORM_DIRECT_SUBMIT', 'personal', null, '{"data":{"private":"sensitive"}}')
  const original = command!.pending.value!
  vi.mocked(api.original).mockResolvedValue({ commandId: original.commandId, outcome: 'NOT_OBSERVED', resourceId: null, resourceVersion: null, result: null })
  await command!.recover(); expect(command!.pending.value).toEqual(original)
  vi.mocked(api.original).mockRejectedValue({ response: { status: 403 } }); await command!.recover(); expect(clear).toHaveBeenCalled(); expect(command!.canRetry()).toBe(false); expect(command!.pending.value).toEqual(original)
  vi.mocked(api.close).mockResolvedValue({ commandId: original.commandId, outcome: 'CLOSED', resourceId: null, resourceVersion: null, result: null }); await command!.close()
  expect(command!.pending.value).toBeUndefined(); expect(command!.message.value).toContain('没有业务效果'); expect(accepted).not.toHaveBeenCalled(); expect(api.send).toHaveBeenCalledTimes(1)
})
it('does not send if recovery metadata cannot be stored', async () => {
  const w = await editor(); vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') })
  await state(w).submit(); expect(api.send).not.toHaveBeenCalled(); expect(state(w).storageUnavailable).toBe(true)
})
it('keeps the accepted draft identity and clears write capabilities when the post-save refresh fails', async () => {
  const w = await editor()
  vi.mocked(api.send).mockResolvedValue({ commandId: 'create', outcome: 'SUCCEEDED', resourceId: draft.draftId, resourceVersion: draft.rowVersion, result: draft })
  vi.mocked(api.draft).mockRejectedValue({ response: { status: 503 } })
  await state(w).save()
  expect(state(w).draft.draftId).toBe(draft.draftId); expect(state(w).context).toBeUndefined(); expect(state(w).draft.capabilities).toEqual([])
  expect(state(w).readError).toContain('原操作已完成'); expect(state(w).pending).toBeUndefined(); expect(api.send).toHaveBeenCalledTimes(1)
})
it('edits actual scalar and table inputs without rounding, keeps invalid text blocked and distinguishes omitted from null', async () => {
  const schema = '{"fields":[{"name":"amount","type":"number"},{"name":"rows","type":"table","columns":[{"name":"qty","type":"number"}]}]}'
  const model = ref(formObject(readFormJson('{"amount":9007199254740993.1,"rows":[{"qty":12345678901234567890.0001}]}'))), valid = ref(true)
  const Harness = defineComponent({ setup: () => () => h(Fields, { fields: schemaFields(schema), modelValue: model.value, 'onUpdate:modelValue': v => { model.value = v }, onValidity: v => { valid.value = v } }) })
  const w = mount(Harness); wrappers.push(w)
  await w.find('[data-field="amount"] input').setValue('9007199254740993.25')
  await w.find('[data-field="qty"] input').setValue('12345678901234567890.0002')
  expect(writeFormJson(model.value)).toContain('9007199254740993.25'); expect(writeFormJson(model.value)).toContain('12345678901234567890.0002')
  await w.find('[data-field="amount"] input').setValue('1e'); expect(valid.value).toBe(false)
  await w.find('[data-field="amount"] input').setValue('0'); expect(valid.value).toBe(true)
  const clear = w.findAll('[data-field="amount"] button'); await clear[0]!.trigger('click'); expect(model.value.amount).toBeUndefined()
  await clear[1]!.trigger('click'); expect(model.value.amount).toBeNull()
})
it('keeps explicit no-unset patch fields distinct from readonly fields', async () => {
  const model = ref(formObject(readFormJson('{"value":"original"}')))
  const Harness = defineComponent({ setup: () => () => h(Fields, { fields: schemaFields('{"fields":[{"name":"value","type":"input"}]}'), modelValue: model.value, allowUnset: false, 'onUpdate:modelValue': value => { model.value = value } }) })
  const w = mount(Harness); wrappers.push(w)
  expect(w.findAll('button').map(button => button.text())).toEqual(['设为空值'])
  await w.get('button').trigger('click'); expect(Object.hasOwn(model.value, 'value')).toBe(true); expect(model.value.value).toBeNull()
  const readonly = mount(Fields, { props: { fields: schemaFields('{"fields":[{"name":"value","type":"input"}]}'), modelValue: model.value, disabled: true } }); wrappers.push(readonly)
  expect(readonly.findAll('button')).toHaveLength(0); expect(readonly.get('input').attributes('disabled')).toBeDefined()
})
