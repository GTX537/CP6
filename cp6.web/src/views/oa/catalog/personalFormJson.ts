import { parseStrictJson } from '@/contracts/quality/strictJson'

/** Numbers remain JSON lexemes through editing, including table cells and select values. */
export class FormNumber {
  constructor(readonly raw: string) {
    if (!/^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?$/.test(raw)) throw new Error('请输入有效数字')
  }
}
export type FormValue = null | boolean | string | FormNumber | FormValue[] | FormObject
export interface FormObject { [key: string]: FormValue | undefined }
export function readFormJson(text: string): FormValue {
  parseStrictJson(text) // Grammar and duplicate-name validation at this input boundary.
  let at = 0
  const white = () => { while (at < text.length && /[\t\n\r ]/.test(text[at]!)) at++ }
  function string(): string {
    const start = at++
    while (at < text.length) { const c = text[at++]; if (c === '\\') at++; else if (c === '"') break }
    return JSON.parse(text.slice(start, at)) as string
  }
  function read(): FormValue {
    white()
    if (text[at] === '"') return string()
    if (text[at] === '{') {
      const value: FormObject = Object.create(null); at++; white()
      while (text[at] !== '}') {
        const key = string(); white(); at++; value[key] = read(); white()
        if (text[at] !== ',') break
        at++; white()
      }
      at++; return value
    }
    if (text[at] === '[') {
      const value: FormValue[] = []; at++; white()
      while (text[at] !== ']') { value.push(read()); white(); if (text[at] !== ',') break; at++ }
      at++; return value
    }
    const start = at
    while (at < text.length && !/[\s,}\]]/.test(text[at]!)) at++
    const raw = text.slice(start, at)
    if (raw === 'null') return null
    if (raw === 'true' || raw === 'false') return raw === 'true'
    return new FormNumber(raw)
  }
  return read()
}
export function writeFormJson(value: FormValue): string {
  if (value instanceof FormNumber) return value.raw
  if (Array.isArray(value)) return '[' + value.map(writeFormJson).join(',') + ']'
  if (value !== null && typeof value === 'object') return '{' + Object.entries(value)
    .filter((entry): entry is [string, FormValue] => entry[1] !== undefined)
    .map(([key, item]) => JSON.stringify(key) + ':' + writeFormJson(item)).join(',') + '}'
  return JSON.stringify(value)
}
export function formObject(value: FormValue | undefined): FormObject {
  if (value === null || typeof value !== 'object' || Array.isArray(value) || value instanceof FormNumber) throw new Error('表单数据必须为对象')
  return value
}
/** Only interpreted schema property names are case-insensitive; field identifiers stay exact. */
export function schemaProperty(value: FormObject, key: string): FormValue | undefined {
  return Object.entries(value).find(([name]) => name.toLowerCase() === key.toLowerCase())?.[1]
}
export function schemaText(value: FormObject, key: string, fallback = ''): string {
  const result = schemaProperty(value, key)
  return typeof result === 'string' ? result : fallback
}
export function schemaNumber(value: FormObject, key: string, fallback: number): number {
  const result = schemaProperty(value, key)
  return result instanceof FormNumber ? Number(result.raw) : fallback
}
export function schemaFields(schema: string): FormObject[] {
  return (schemaProperty(formObject(readFormJson(schema)), 'fields') as FormValue[]).map(formObject)
}
export function initialFormData(schema: string): string {
  const result: FormObject = Object.create(null)
  for (const field of schemaFields(schema)) {
    const value = schemaProperty(field, 'default')
    if (value !== undefined) result[schemaText(field, 'name')] = value
  }
  return writeFormJson(result)
}
