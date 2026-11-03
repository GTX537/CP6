// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import type { FormAuthoringView } from '@/api/oa/formAuthoring'
import { formRulePreviewApi as api, type FormRulePreview } from '@/api/oa/formRulePreview'
import { AUTH_EVENT, newEpoch } from '@/auth/identity'
import Panel from './FormRulePreviewPanelV2.vue'
vi.mock('@/api/oa/formRulePreview', () => ({ formRulePreviewApi: { preview: vi.fn() } }))
const schema = { fields: [{ name: 'amount', type: 'number' }, { name: 'next', type: 'number' }, { name: 'choice', type: 'select' }],
  rules: [{ then: [{ action: 'compute', target: 'next', expr: 'amount + 1' }, { action: 'disable', target: 'amount' }, { action: 'setOptions', target: 'choice', options: [{ label: '可选', value: 'yes' }] }] }] }
const current: FormAuthoringView = { definitionId: 'head', draftId: 'draft', key: 'rules', version: 'token', enabled: false,
  fields: { SchemaJson: JSON.stringify(schema) }, writableFields: [] }
const preview: FormRulePreview = { mode: 'EXPLANATION_ONLY', definitionId: 'head', draftVersionId: 'draft', expectedVersion: 'token',
  dataJson: '{"amount":9007199254740993,"next":9007199254740994}', validationErrors: [], presentation: [], appliedEffects: [], unproven: [] }
const mounted: ReturnType<typeof mount>[] = [], state = (w: any): any => w.vm.$.setupState
function setup(extra = {}) { const w = mount(Panel, { props: { current, dirty: false, disabled: false, ...extra } }); mounted.push(w); return w }
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear(); vi.mocked(api.preview).mockResolvedValue(preview) })
afterEach(() => { mounted.forEach(w => w.unmount()); mounted.length = 0 })
it('sends raw numeric input and shows authoritative exact results beside the existing browser experience', async () => {
  const w = setup(); await w.get('button').trigger('click')
  const raw = '{"amount":9007199254740993}'
  await w.get('textarea').setValue(raw); await state(w).preview()
  expect(api.preview).toHaveBeenCalledWith('rules', { draftVersionId: 'draft', expectedVersion: 'token', dataJson: raw }, expect.any(AbortSignal))
  expect(w.get('[data-test=server-rule-result]').text()).toContain('9007199254740994')
  expect(w.get('[data-test=client-rule-result]').text()).toContain('9007199254740992')
  expect(state(w).clientEffects.amount.disabled).toBe(true); expect(state(w).clientEffects.choice.options).toEqual([{ label: '可选', value: 'yes' }])
  expect(w.text()).toContain('最终提交以后端为准'); expect(sessionStorage.length).toBe(0)
  window.dispatchEvent(new StorageEvent('storage', { key: 'theme' })); await flushPromises(); expect(state(w).result).toBeDefined()
})
it('requires a current saved readable draft and invalidates results when input or saved version changes', async () => {
  const w = setup({ dirty: true }); await state(w).preview(); expect(api.preview).not.toHaveBeenCalled()
  await w.setProps({ dirty: false, current: { ...current, published: true } }); await state(w).preview(); expect(api.preview).not.toHaveBeenCalled()
  await w.setProps({ current: { ...current, fields: {} } }); await state(w).preview(); expect(api.preview).not.toHaveBeenCalled()
  await w.setProps({ current }); await state(w).preview(); expect(state(w).result).toBeDefined()
  state(w).raw = '{"amount":2}'; await flushPromises(); expect(state(w).result).toBeUndefined()
  await state(w).preview(); await w.setProps({ current: { ...current, version: 'new-token' } }); expect(state(w).result).toBeUndefined(); expect(state(w).raw).toBe('{}')
})
it('discards late replies after version or identity changes and clears private input after denial', async () => {
  let complete!: (value: FormRulePreview) => void
  vi.mocked(api.preview).mockImplementation(() => new Promise(resolve => { complete = resolve }))
  const w = setup(); const first = state(w).preview(); await w.setProps({ current: { ...current, version: 'new-token' } }); complete(preview); await first
  expect(state(w).result).toBeUndefined()
  state(w).raw = '{"amount":8}'; await flushPromises(); const second = state(w).preview(); newEpoch(); window.dispatchEvent(new Event(AUTH_EVENT)); complete(preview); await second
  expect(state(w).result).toBeUndefined(); expect(state(w).raw).toBe('{}')
  vi.mocked(api.preview).mockRejectedValue({ response: { status: 403 } }); await state(w).preview()
  expect(w.emitted('privateClear')).toBeDefined(); expect(state(w).result).toBeUndefined(); expect(state(w).clientJson).toBe('')
})
it('retains simulated input and reports stale, invalid or unavailable responses without a successful result', async () => {
  const w = setup(); await w.get('button').trigger('click'); await w.get('textarea').setValue('{"amount":1}')
  for (const status of [409, 400, 422, 503]) {
    vi.mocked(api.preview).mockRejectedValue({ response: { status } }); await state(w).preview()
    expect(state(w).raw).toBe('{"amount":1}'); expect(state(w).result).toBeUndefined(); expect(w.get('[role=alert]').text()).not.toBe('')
  }
})
it('locates a failed runtime rule while retaining the simulated input and hiding previous results', async () => {
  const w = setup(); await w.get('button').trigger('click'); await w.get('textarea').setValue('{"amount":0}')
  const detail = { path: '$.rules[0].then[0].expr', code: 'FORM_RULE_EVALUATION_FAILED', severity: 'ERROR', fieldName: 'next' }
  vi.mocked(api.preview).mockRejectedValue({ response: { status: 422, data: { code: detail.code, fieldErrors: [detail] } } })
  await state(w).preview(); await w.findAll('button').find(b => b.text() === '定位 next')!.trigger('click')
  expect(w.emitted('locate')?.[0]).toEqual([detail]); expect(state(w).raw).toBe('{"amount":0}'); expect(state(w).result).toBeUndefined()
  await w.get('textarea').setValue('{"amount":1}'); expect(w.find('[aria-label="设计诊断"]').exists()).toBe(false)
})
