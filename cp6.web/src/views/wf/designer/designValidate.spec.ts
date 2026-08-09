import { describe, it, expect } from 'vitest'
import { validateFormSchema } from './designValidate'
import type { FormSchema } from '@/types/wf/wf'

// OA 章09 §5 表单设计时校验。
describe('validateFormSchema', () => {
  it('合法 schema 无错', () => {
    const s: FormSchema = {
      fields: [
        { name: 'reason', label: '事由', type: 'input', required: true },
        { name: 'type', label: '类型', type: 'select', options: [{ label: 'A', value: 'a' }] },
      ],
    }
    expect(validateFormSchema(s)).toEqual([])
  })

  it('空字段标识报错', () => {
    const s: FormSchema = { fields: [{ name: '', label: 'x', type: 'input' }] }
    expect(validateFormSchema(s).length).toBe(1)
  })

  it('重复字段标识报错', () => {
    const s: FormSchema = {
      fields: [
        { name: 'a', type: 'input' },
        { name: 'a', type: 'number' },
      ],
    }
    expect(validateFormSchema(s).some((e) => e.includes('重复'))).toBe(true)
  })

  it('选项类控件缺选项报错', () => {
    const s: FormSchema = { fields: [{ name: 'sel', label: '选择', type: 'select', options: [] }] }
    expect(validateFormSchema(s).some((e) => e.includes('缺少选项'))).toBe(true)
  })

  it('合法子表 schema 无错', () => {
    const s: FormSchema = {
      fields: [{
        name: 'items',
        label: '采购明细',
        type: 'table',
        minRows: 1,
        maxRows: 20,
        columns: [
          { name: 'material', label: '物料', type: 'input', required: true, maxLength: 50 },
          {
            name: 'unit',
            label: '单位',
            type: 'select',
            options: [{ label: '个', value: 'pc' }],
          },
          { name: 'qty', label: '数量', type: 'number', required: true },
        ],
      }],
    }
    expect(validateFormSchema(s)).toEqual([])
  })

  it('子表列标识、选项和行数范围错误会被阻止', () => {
    const s: FormSchema = {
      fields: [{
        name: 'items',
        label: '采购明细',
        type: 'table',
        minRows: 3,
        maxRows: 2,
        columns: [
          { name: 'unit', label: '单位', type: 'select', options: [] },
          { name: 'unit', label: '重复单位', type: 'upload' },
        ],
      }],
    }
    const errors = validateFormSchema(s)
    expect(errors.some((e) => e.includes('列标识重复'))).toBe(true)
    expect(errors.some((e) => e.includes('缺少选项'))).toBe(true)
    expect(errors.some((e) => e.includes('列类型不受支持'))).toBe(true)
    expect(errors.some((e) => e.includes('行数范围无效'))).toBe(true)
  })
})
