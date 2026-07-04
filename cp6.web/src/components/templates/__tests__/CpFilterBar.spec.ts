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

  it('daterange field renders and wires start/end placeholders from field.placeholder', () => {
    const drFields: FilterField[] = [{ key: 'date', label: '计划出库日', type: 'daterange', placeholder: '选择日期' }]
    const w = mount(CpFilterBar, { props: { fields: drFields, modelValue: {} } })
    const inputs = w.findAll('.el-range-input')
    expect(inputs).toHaveLength(2) // 起 / 止 两个输入
    expect(inputs[0].attributes('placeholder')).toBe('选择日期')
    expect(inputs[1].attributes('placeholder')).toBe('选择日期')
  })

  it('date 字段渲染单日 el-date-picker（非 range，一个输入框）', () => {
    const w = mount(CpFilterBar, {
      props: { fields: [{ key: 'from', label: '予定入荷 From', type: 'date' }], modelValue: {} }
    })
    expect(w.findComponent({ name: 'ElDatePicker' }).exists()).toBe(true)
    expect(w.findAll('.el-range-input')).toHaveLength(0) // 不是 range 形态
    expect(w.find('.el-date-editor input').exists()).toBe(true)
  })

  it('date + valueFormat：用户输入日期后 model 值为格式化字符串（非 Date 对象）', async () => {
    const w = mount(CpFilterBar, {
      props: {
        fields: [{ key: 'from', label: 'From', type: 'date', valueFormat: 'YYYY-MM-DD' }],
        modelValue: {}
      }
    })
    const input = w.get('.el-date-editor input')
    await input.setValue('2026-07-04')
    await input.trigger('change')
    const emits = w.emitted('update:modelValue')!
    const payload = emits.at(-1)![0] as Record<string, unknown>
    expect(payload.from).toBe('2026-07-04') // 字符串，不是 Date
    expect(typeof payload.from).toBe('string')
  })

  it('date 无 valueFormat：el-date-picker 不收到 value-format（保持返回 Date 的默认行为）', () => {
    const w = mount(CpFilterBar, {
      props: { fields: [{ key: 'from', label: 'From', type: 'date' }], modelValue: {} }
    })
    expect(w.findComponent({ name: 'ElDatePicker' }).props('valueFormat')).toBeUndefined()
  })

  it('daterange 透传 valueFormat 到 el-date-picker', () => {
    const w = mount(CpFilterBar, {
      props: {
        fields: [{ key: 'range', label: '期間', type: 'daterange', valueFormat: 'YYYY-MM-DD' }],
        modelValue: {}
      }
    })
    expect(w.findComponent({ name: 'ElDatePicker' }).props('valueFormat')).toBe('YYYY-MM-DD')
  })

  it('number 字段渲染 el-input-number 且透传 min/max/step', () => {
    const w = mount(CpFilterBar, {
      props: {
        fields: [{ key: 'days', label: 'N日以内', type: 'number', min: 1, max: 365, step: 1 }],
        modelValue: {}
      }
    })
    const num = w.findComponent({ name: 'ElInputNumber' })
    expect(num.exists()).toBe(true)
    expect(num.props('min')).toBe(1)
    expect(num.props('max')).toBe(365)
    expect(num.props('step')).toBe(1)
  })

  it('number 字段变更后 v-model 值为数值型', async () => {
    // 真实 v-model 回写（el-input-number 在 prop 不回写时会自行重同步，直接断言 emit 序列不可靠）
    const Host = {
      components: { CpFilterBar },
      data: () => ({
        model: {} as Record<string, unknown>,
        fields: [{ key: 'days', label: 'N日以内', type: 'number', min: 1, max: 365 }] as FilterField[]
      }),
      template: `<CpFilterBar v-model="model" :fields="fields" />`
    }
    const w = mount(Host)
    const input = w.get('.el-input-number input')
    await input.setValue('30')
    await input.trigger('change')
    expect((w.vm as unknown as { model: Record<string, unknown> }).model.days).toBe(30)
  })

  it('renders custom button labels when labels prop supplied', () => {
    const many: FilterField[] = [...fields, { key: 'operator', label: '担当', type: 'text' }]
    const w = mount(CpFilterBar, {
      props: {
        fields: many,
        modelValue: {},
        labels: { search: '検索', reset: 'クリア', expand: 'もっと見る', collapse: '閉じる' }
      }
    })
    expect(w.findAll('button').some((b) => b.text().includes('検索'))).toBe(true)
    expect(w.findAll('button').some((b) => b.text().includes('クリア'))).toBe(true)
    expect(w.findAll('button').some((b) => b.text().includes('もっと見る'))).toBe(true)
    // 无 labels 时的中文默认不应再出现
    expect(w.text()).not.toContain('查询')
    expect(w.text()).not.toContain('重置')
  })
})
