import { expect, it } from 'vitest'
import { diagnosticSourceSpan } from './diagnosticSourceSpan'
it('locates the exact offending source even after nested structures, quoted delimiters and large numbers', () => {
  const raw = '{"fields":[{"name":"quoted\\\"{}[]","options":[{"value":9007199254740993.00}]},{"name":"second","columns":[{"name":"line","type":"table"}]}],"rules":[{"when":"x > 9007199254740993","then":[{"action":"compute","expr":"x + 1"}]}]}'
  const field = diagnosticSourceSpan(raw, '$.fields[1].columns[0].type')!
  expect(raw.slice(...field)).toBe('"table"')
  const rule = diagnosticSourceSpan(raw, '$.rules[0].then[0].expr')!
  expect(raw.slice(...rule)).toBe('"x + 1"')
  const number = diagnosticSourceSpan(raw, '$.fields[0].options[0].value')!
  expect(raw.slice(...number)).toBe('9007199254740993.00')
  expect(diagnosticSourceSpan(raw, '$.fields[1].missing')).toBeUndefined()
})
