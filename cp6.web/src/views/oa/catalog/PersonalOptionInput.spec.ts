// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import Input from './PersonalOptionInput.vue'
import Fields from './PersonalFormFields.vue'
import { FormNumber, schemaFields } from './personalFormJson'
const mock = vi.hoisted(() => ({ query: vi.fn(), identity: 'actor-a' }))
vi.mock('@/api/oa/formOptions', () => ({ formOptionApi: { query: mock.query } }))
vi.mock('@/auth/identity', () => ({ AUTH_EVENT: 'option-auth', identityStamp: () => mock.identity, requireIdentity: (v: string) => { if (v !== mock.identity) throw new Error('changed') } }))
const context = { kind: 'FORM' as const, key: 'personal', id: null, formVersionId: 'form-one' }
const wrappers: ReturnType<typeof mount>[] = []
function create(props = {}) { const w = mount(Input, { props: { context, fieldPath: ['choice'], multiple: false, disabled: false, ...props } }); wrappers.push(w); return w }
beforeEach(() => { vi.clearAllMocks(); mock.identity = 'actor-a'; mock.query.mockResolvedValue({ items: [{ value: '1', label: 'One' }, { value: '1.0', label: 'Exact' }], page: 1, hasMore: false, scope: 'SHARED' }) })
afterEach(() => wrappers.splice(0).forEach(w => w.unmount()))
it('preserves exact strings and a stored value absent from current candidates until the user edits it', async () => {
  const w = create({ modelValue: 'retired' }); await flushPromises()
  expect(w.get('select').element.value).toBe('retired'); expect(w.text()).toContain('retired（已保存原值）'); expect(w.emitted('update:modelValue')).toBeUndefined()
  await w.get('select').setValue('1.0'); expect(w.emitted('update:modelValue')?.at(-1)).toEqual(['1.0'])
})
it('does not resolve historical labels from current source or fetch while read-only', async () => {
  const w = create({ disabled: true, modelValue: '1.0' }); await flushPromises()
  expect(w.text()).toBe('"1.0"'); expect(mock.query).not.toHaveBeenCalled(); expect(w.find('select').exists()).toBe(false)
})
it('shows a retained numeric value after rebase and requires an explicit string selection without coercion', async () => {
  const w = create({ modelValue: new FormNumber('1.0') }); await flushPromises()
  expect(w.get('[role=status]').text()).toContain('当前保留值：1.0'); expect(w.emitted('update:modelValue')).toBeUndefined()
  await w.get('select').setValue('1.0'); expect(w.emitted('update:modelValue')?.at(-1)).toEqual(['1.0'])
})
it('discards responses for a previous form version and clears candidates on current permission refusal', async () => {
  let release!: (v: any) => void; mock.query.mockImplementationOnce(() => new Promise(resolve => { release = resolve }))
  const w = create(); await w.setProps({ context: { ...context, formVersionId: 'form-two' } }); await flushPromises()
  release({ items: [{ value: 'old-private', label: 'OLD PRIVATE' }], page: 1, hasMore: false }); await flushPromises(); expect(w.text()).not.toContain('OLD PRIVATE')
  mock.query.mockRejectedValueOnce({ response: { status: 403, data: { code: 'FIELD_OPTION_FORBIDDEN' } } }); await w.get('button').trigger('click'); await flushPromises()
  expect(w.emitted('forbidden')).toBeTruthy(); expect(w.findAll('option')).toHaveLength(1)
})
it('identity changes clear choices immediately and reject late results', async () => {
  let release!: (v: any) => void; mock.query.mockImplementationOnce(() => new Promise(resolve => { release = resolve }))
  const w = create(); mock.identity = 'actor-b'; window.dispatchEvent(new Event('option-auth')); await flushPromises()
  release({ items: [{ value: 'private', label: 'PRIVATE' }], page: 1, hasMore: false }); await flushPromises()
  expect(w.text()).not.toContain('PRIVATE'); expect(w.emitted('forbidden')).toBeTruthy()
})
it('pages current choices without losing selections outside the loaded page', async () => {
  mock.query.mockResolvedValueOnce({ items: [{ value: 'A', label: 'A' }], page: 1, hasMore: true }).mockResolvedValueOnce({ items: [{ value: 'a', label: 'a' }], page: 2, hasMore: false })
  const w = create({ multiple: true, modelValue: ['retired', 'A'] }); await flushPromises()
  expect(Array.from(w.get('select').element.selectedOptions).map(o => o.value)).toEqual(['retired', 'A'])
  await w.findAll('button').find(b => b.text() === '更多选项')!.trigger('click'); await flushPromises()
  expect(mock.query).toHaveBeenLastCalledWith(context, ['choice'], '', 2); expect(w.findAll('option').map(x => x.element.value)).toEqual(['retired', 'A', 'a']); expect(w.emitted('update:modelValue')).toBeUndefined()
  expect(Array.from(w.get('select').element.selectedOptions).map(o => o.value)).toEqual(['retired', 'A'])
  await w.get('select').setValue(['retired', 'A', 'a']); expect(w.emitted('update:modelValue')?.at(-1)).toEqual([['retired', 'A', 'a']])
})
it('nested columns pass exact two-part paths and the parent form context', async () => {
  const schema = '{"fields":[{"name":"row.with.dot","type":"table","columns":[{"name":"cell.with.dot","type":"select","optionSourceV1":{"owner":"Main.Sys.DictionaryOptions.v1","scope":"SHARED","typeId":1}}]}]}'
  const w = mount(Fields, { props: { fields: schemaFields(schema), modelValue: { 'row.with.dot': [{ 'cell.with.dot': '1.0' }] }, mask: { 'row.with.dot': 'edit' }, fileContext: { scope: context } } }); wrappers.push(w); await flushPromises()
  expect(mock.query).toHaveBeenCalledWith(context, ['row.with.dot', 'cell.with.dot'], '', 1)
  await w.get('select').setValue('1'); expect(w.emitted('update:modelValue')?.at(-1)).toEqual([{ 'row.with.dot': [{ 'cell.with.dot': '1' }] }])
})
