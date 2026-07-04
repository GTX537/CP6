// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import CpFilterBar, { type FilterField } from '../CpFilterBar.vue'

const fields: FilterField[] = [
  { key: 'q', label: '出库单号', type: 'text', placeholder: '单号搜索' },
  { key: 'cust', label: '得意先', type: 'select', options: [{ label: 'ASAHI', value: 'a' }] },
  { key: 'wh', label: '出库仓库', type: 'select', options: [{ label: '東京', value: 'tk' }] },
  { key: 'date', label: '计划出库日', type: 'daterange' }
]

function findButton(w: ReturnType<typeof mount>, text: string) {
  return w.findAll('button').find((b) => b.text().includes(text))!
}

describe('CpFilterBar', () => {
  it('renders one field block per field with its label', () => {
    const w = mount(CpFilterBar, { props: { fields, modelValue: {} } })
    const flds = w.findAll('.fld')
    expect(flds).toHaveLength(4)
    expect(flds[0].get('label').text()).toBe('出库单号')
    // a text field renders a real <input>
    expect(flds[0].find('input').exists()).toBe(true)
  })

  it('emits search (no payload) when the search button is clicked', async () => {
    const w = mount(CpFilterBar, { props: { fields, modelValue: {} } })
    await findButton(w, '查询').trigger('click')
    expect(w.emitted('search')).toEqual([[]])
  })

  it('reset clears every field and emits update:modelValue + reset', async () => {
    const w = mount(CpFilterBar, {
      props: { fields, modelValue: { q: 'SHP-1', cust: 'a' } }
    })
    await findButton(w, '重置').trigger('click')
    const cleared = w.emitted('update:modelValue')![0][0] as Record<string, unknown>
    for (const f of fields) {
      expect(cleared).toHaveProperty(f.key)
      expect(cleared[f.key]).toBeUndefined()
    }
    expect(w.emitted('reset')).toEqual([[]])
  })

  it('emits a new object (no prop mutation) when a field changes', async () => {
    const model = { q: '' }
    const w = mount(CpFilterBar, { props: { fields, modelValue: model } })
    await w.get('.fld input').setValue('SHP-9')
    const payload = w.emitted('update:modelValue')![0][0] as Record<string, unknown>
    expect(payload.q).toBe('SHP-9')
    expect(payload).not.toBe(model) // brand new object
    expect(model.q).toBe('') // original prop untouched
  })

  it('hides fields beyond the 4th until 展开更多 is toggled', async () => {
    const many: FilterField[] = [
      ...fields,
      { key: 'operator', label: '担当', type: 'text' }
    ]
    const w = mount(CpFilterBar, { props: { fields: many, modelValue: {} } })
    expect(w.findAll('.fld')).toHaveLength(4)
    expect(w.text()).not.toContain('担当')

    await findButton(w, '展开更多').trigger('click')
    expect(w.findAll('.fld')).toHaveLength(5)
    expect(w.text()).toContain('担当')
  })

  it('shows no expand toggle when fields.length <= 4', () => {
    const w = mount(CpFilterBar, { props: { fields, modelValue: {} } })
    expect(w.findAll('button').some((b) => b.text().includes('展开更多'))).toBe(false)
  })
})
