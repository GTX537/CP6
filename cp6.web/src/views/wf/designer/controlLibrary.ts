// OA 章09 §2 表单设计器控件库。控件 → 默认字段 schema（与运行时 DynamicForm 同构，OA4-D6）。
import type { FormFieldDef } from '@/types/wf/wf'

/** 控件库项（左栏拖/点产生字段） */
export interface ControlDef {
  /** 控件类型（= FormFieldDef.type） */
  type: string
  /** 控件库显示名（i18n key） */
  label: string
  /** element-plus 图标名 */
  icon: string
}

/** 可用控件（对齐 DynamicForm 支持的 type） */
export const CONTROL_LIBRARY: ControlDef[] = [
  { type: 'input', label: '单行文本', icon: 'EditPen' },
  { type: 'textarea', label: '多行文本', icon: 'Document' },
  { type: 'number', label: '数字', icon: 'Histogram' },
  { type: 'select', label: '下拉选择', icon: 'ArrowDown' },
  { type: 'radio', label: '单选', icon: 'Select' },
  { type: 'checkbox', label: '多选', icon: 'Finished' },
  { type: 'date', label: '日期', icon: 'Calendar' },
  { type: 'datetime', label: '日期时间', icon: 'Clock' },
  { type: 'user', label: '人员', icon: 'User' },
  { type: 'dept', label: '部门', icon: 'OfficeBuilding' },
  { type: 'upload', label: '附件', icon: 'Paperclip' },
  { type: 'table', label: '子表', icon: 'Grid' },
]

/** 选项类控件（带 options） */
export function isOptionType(type: string): boolean {
  return type === 'select' || type === 'radio' || type === 'checkbox'
}

/** P1 子表列保持扁平，不允许人员、部门、附件或嵌套子表。 */
export const TABLE_COLUMN_TYPES: ControlDef[] = [
  { type: 'input', label: '单行文本', icon: 'EditPen' },
  { type: 'textarea', label: '多行文本', icon: 'Document' },
  { type: 'number', label: '数字', icon: 'Histogram' },
  { type: 'select', label: '下拉选择', icon: 'ArrowDown' },
  { type: 'date', label: '日期', icon: 'Calendar' },
  { type: 'datetime', label: '日期时间', icon: 'Clock' },
]

/**
 * 造一个控件的默认字段 schema。<paramref name="existing"/> 用于生成不重复的 name。
 * name 形如 <type><n>，n 从 1 起取首个未占用值（与字段顺序无关，保证唯一）。
 */
export function defaultFieldOf(type: string, existing: ReadonlyArray<{ name: string }> = []): FormFieldDef {
  const used = new Set(existing.map((f) => f.name))
  let i = 1
  while (used.has(`${type}${i}`)) i++
  const name = `${type}${i}`

  const field: FormFieldDef = {
    name,
    label: name,
    type,
    required: false,
  }
  if (isOptionType(type)) {
    field.options = [
      { label: '选项1', value: '1' },
      { label: '选项2', value: '2' },
    ]
  }
  if (type === 'table') {
    field.minRows = 0
    field.maxRows = 20
    field.columns = [
      { name: 'column1', label: '列1', type: 'input', required: false },
    ]
  }
  return field
}
