import { describe, expect, it } from 'vitest'
import type { FormFieldDef } from '@/types/wf/wf'
import { createSubtableRow, maxRowsOf, validateSubtable } from './subtable'

const field: FormFieldDef = {
  name: 'items',
  label: '采购明细',
  type: 'table',
  required: true,
  minRows: 1,
  maxRows: 2,
  columns: [
    { name: 'material', label: '物料', type: 'input', required: true, maxLength: 5 },
    { name: 'qty', label: '数量', type: 'number', required: true },
    {
      name: 'unit',
      label: '单位',
      type: 'select',
      options: [{ label: '个', value: 'pc' }],
    },
  ],
}

describe('subtable runtime', () => {
  it('合法扁平行通过校验', () => {
    expect(validateSubtable(field, [{ material: 'A-01', qty: 2, unit: 'pc' }])).toEqual([])
  })

  it('必填、最少和最多行数与字段规则一致', () => {
    expect(validateSubtable(field, [])).toContain('采购明细 必填')
    expect(validateSubtable(field, [])).toContain('采购明细 至少需要 1 行')
    expect(validateSubtable(field, [{}, {}, {}])).toContain('采购明细 最多允许 2 行')
  })

  it('拒绝未知列、错误类型和超长文本', () => {
    const errors = validateSubtable(field, [{
      material: 'TOO-LONG',
      qty: 'two',
      unexpected: true,
    }])
    expect(errors.some((error) => error.includes('未知列 unexpected'))).toBe(true)
    expect(errors.some((error) => error.includes('物料 超出最大长度 5'))).toBe(true)
    expect(errors.some((error) => error.includes('数量 必须是数字'))).toBe(true)
  })

  it('新行按列生成，最多行数采用 schema 值', () => {
    expect(createSubtableRow(field)).toEqual({ material: '', qty: undefined, unit: '' })
    expect(maxRowsOf(field)).toBe(2)
  })

  it('非必填且未赋值的子表可以保存为空', () => {
    expect(validateSubtable({ ...field, required: false, minRows: 0 }, undefined)).toEqual([])
  })
})
