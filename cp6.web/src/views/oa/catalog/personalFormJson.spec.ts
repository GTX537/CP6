import { describe, expect, it } from 'vitest'
import { FormNumber, formObject, initialFormData, readFormJson, writeFormJson } from './personalFormJson'

describe('personal form exact values', () => {
  it('edits a table decimal without rounding untouched numbers or changing missing/null/empty', () => {
    const data = formObject(readFormJson('{"amount":12345678901234567890.0001,"rows":[{"n":9007199254740993}],"empty":"","nullable":null}'))
    const row = formObject((data.rows as unknown[])[0] as ReturnType<typeof readFormJson>)
    row.n = new FormNumber('99999999999999999999.00000001')
    expect(writeFormJson(data)).toBe('{"amount":12345678901234567890.0001,"rows":[{"n":99999999999999999999.00000001}],"empty":"","nullable":null}')
    expect(Object.hasOwn(data, 'unset')).toBe(false)
  })
  it('applies exact numeric defaults and case-insensitive schema keys without normalizing field names', () => {
    expect(initialFormData('{"Fields":[{"Name":"Price","Type":"number","Default":1.0000000000000000001},{"Name":"price","Default":null}]}'))
      .toBe('{"Price":1.0000000000000000001,"price":null}')
  })
  it('retains string and numeric choices as distinct and rejects ambiguous/invalid input', () => {
    expect(writeFormJson(readFormJson('["9007199254740993",9007199254740993,-0,1e10000]'))).toBe('["9007199254740993",9007199254740993,-0,1e10000]')
    expect(() => readFormJson('{"x":1,"\\u0078":2}')).toThrow('DUPLICATE_PROPERTY_NAME')
    expect(() => new FormNumber('1.')).toThrow()
    expect(() => new FormNumber('NaN')).toThrow()
  })
})
