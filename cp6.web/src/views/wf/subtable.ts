import type { FormFieldDef, FormTableColumnDef } from '@/types/wf/wf'

export const DEFAULT_MAX_SUBTABLE_ROWS = 100
export const HARD_MAX_SUBTABLE_ROWS = 200
export const SUBTABLE_COLUMN_TYPES = ['input', 'textarea', 'number', 'select', 'date', 'datetime'] as const

export type SubtableRow = Record<string, unknown>

export function rowsOf(value: unknown): SubtableRow[] {
  return Array.isArray(value) ? value as SubtableRow[] : []
}

export function createSubtableRow(field: FormFieldDef): SubtableRow {
  return Object.fromEntries((field.columns || []).map((column) => [column.name, defaultCellValue(column)]))
}

export function maxRowsOf(field: FormFieldDef): number {
  return Math.min(field.maxRows ?? DEFAULT_MAX_SUBTABLE_ROWS, HARD_MAX_SUBTABLE_ROWS)
}

/** 与后端相同的运行态约束。返回首屏可直接展示的中文错误清单。 */
export function validateSubtable(
  field: FormFieldDef,
  value: unknown,
  required = !!field.required,
): string[] {
  const label = field.label || field.name
  if (value === null || value === undefined) {
    if (required) return [`${label} 必填`]
    value = []
  }
  if (!Array.isArray(value)) return [`${label} 必须是子表数组`]

  const errors: string[] = []
  if (required && value.length === 0) errors.push(`${label} 必填`)
  const minRows = field.minRows ?? 0
  const maxRows = maxRowsOf(field)
  if (value.length < minRows) errors.push(`${label} 至少需要 ${minRows} 行`)
  if (value.length > maxRows) {
    errors.push(`${label} 最多允许 ${maxRows} 行`)
    return errors
  }

  const columns = field.columns || []
  const known = new Set(columns.map((column) => column.name))
  value.forEach((item, index) => {
    const rowNo = index + 1
    if (!isPlainObject(item)) {
      errors.push(`${label} 第 ${rowNo} 行必须是对象`)
      return
    }

    Object.keys(item).forEach((name) => {
      if (!known.has(name)) errors.push(`${label} 第 ${rowNo} 行包含未知列 ${name}`)
    })

    columns.forEach((column) => {
      const cell = item[column.name]
      const cellLabel = `${label} 第 ${rowNo} 行 ${column.label || column.name}`
      if (isEmptyCell(cell)) {
        if (column.required) errors.push(`${cellLabel} 必填`)
        return
      }
      validateCell(column, cell, cellLabel, errors)
    })
  })
  return errors
}

function validateCell(column: FormTableColumnDef, value: unknown, label: string, errors: string[]) {
  if (column.type === 'number') {
    if (typeof value !== 'number' || !Number.isFinite(value)) errors.push(`${label} 必须是数字`)
    return
  }
  if (column.type === 'select') {
    if (typeof value !== 'string' && typeof value !== 'number') errors.push(`${label} 必须是文本或数字`)
    return
  }
  if (typeof value !== 'string') {
    errors.push(`${label} 必须是文本`)
    return
  }
  if (column.maxLength && value.length > column.maxLength) {
    errors.push(`${label} 超出最大长度 ${column.maxLength}`)
  }
  if (column.pattern) {
    try {
      if (!new RegExp(column.pattern).test(value)) errors.push(`${label} 格式不符`)
    } catch {
      errors.push(`${label} 校验规则无效`)
    }
  }
}

function defaultCellValue(column: FormTableColumnDef): unknown {
  return column.type === 'number' ? undefined : ''
}

function isEmptyCell(value: unknown): boolean {
  return value === null || value === undefined || value === ''
}

function isPlainObject(value: unknown): value is SubtableRow {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}
