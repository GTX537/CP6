// @vitest-environment node
import { describe, expect, it } from 'vitest'
import vectors from '../../../../eng/contracts/s01/vectors.json'
import dispatch from '../../../../docs/development/dispatch/20261012-wave2/dispatch.json'
import { CreditAmount, type CreditProfile } from './creditAmount'
import { InventoryDecimal, type InventoryProfile } from './inventoryDecimal'
import { ExactDecimal } from './exactDecimal'

const assignment = dispatch.tasks.find((task) => task.id === 'S01-05')!
const shared = vectors.cases.filter((item) => assignment.profiles.includes(item.profile_id))

function parse(profile: string, input: unknown) {
  switch (profile) {
    case 'fin-credit-signed':
    case 'fin-credit-nonnegative':
    case 'fin-credit-positive':
      return CreditAmount.parse(input, profile)
    case 'fin-inv-decimal18':
    case 'fin-inv-qty12':
      return InventoryDecimal.parse(input, profile)
    default:
      throw new Error(`Unsupported profile: ${profile}`)
  }
}

describe('S01-05 fixed shared input package', () => {
  it('discovers exactly the 34 assigned cases, without duplicate IDs or ignored operations', () => {
    expect(shared).toHaveLength(34)
    expect(new Set(shared.map((item) => item.id)).size).toBe(34)
    expect(shared.map((item) => item.id).sort()).toEqual([...assignment.case_ids].sort())
    expect([...new Set(shared.map((item) => item.operation))]).toEqual(['lexical'])
    expect([...new Set(shared.map((item) => item.profile_id))].sort()).toEqual(
      [...assignment.profiles].sort(),
    )
  })

  it.each(shared)('$id / $profile_id', (item) => {
    if (item.operation !== 'lexical') throw new Error(`Unsupported operation: ${item.operation}`)
    if (!item.expected.accepted) {
      expect(() => parse(item.profile_id, item.input)).toThrow()
      return
    }
    const value = parse(item.profile_id, item.input)
    const pair = value instanceof CreditAmount
      ? { unscaledValue: value.exact.coefficient.toString(), scale: value.exact.scale }
      : {
          unscaledDigits: value.exact.coefficient.toString(),
          scale: value.exact.scale,
          originalWire: value.originalWire,
        }
    expect(pair).toEqual(item.expected.pair)
    expect(value.toWire()).toBe(item.input)
    expect(JSON.stringify({ value })).toBe(JSON.stringify({ value: item.input }))
  })
})

describe.each(assignment.profiles)('%s rejects non-string tokens and malformed wire', (profile) => {
  it.each([null, undefined, 0, 1.25, 9007199254740992, true, false, {}, [], 1n, new String('1')])(
    'rejects token %s without coercion', (input) => {
      expect(() => parse(profile, input)).toThrow(TypeError)
    },
  )
  it.each(['', ' 1', '1 ', '1\n', '1\r\n', '1\u2028', '1\u2029', '+1', '01', '-01', '.1', '1.', '1e3', 'NaN', 'Infinity', '１', '١'])('rejects %j', (wire) => {
    expect(() => parse(profile, wire)).toThrow()
  })
})

describe('FIN-CREDIT field boundaries', () => {
  const signed: CreditProfile = 'fin-credit-signed'
  it.each(['-0', '-0.0', '0.0', '1.0', '1.2300', '0.000000001'])('rejects noncanonical %s', (wire) => {
    expect(() => CreditAmount.parse(wire, signed)).toThrow(RangeError)
  })
  it.each([
    ['fin-credit-signed', '-0.00000001', '-1', 8],
    ['fin-credit-positive', '0.1', '1', 1],
    ['fin-credit-nonnegative', '0', '0', 0],
  ] as const)('accepts %s %s exactly', (profile, wire, coefficient, scale) => {
    const value = CreditAmount.parse(wire, profile)
    expect(value.exact.coefficient.toString()).toBe(coefficient)
    expect(value.exact.scale).toBe(scale)
  })
  it('rejects unknown profiles, even for otherwise valid zero', () => {
    expect(() => CreditAmount.parse('0', 'unknown' as CreditProfile)).toThrow()
    expect(() => InventoryDecimal.parse('0', 'unknown' as InventoryProfile)).toThrow()
  })
  it('validates the final sign profile independently of the input profile', () => {
    const negative = CreditAmount.parse('-1', signed)
    expect(() => negative.toWire('fin-credit-nonnegative')).toThrow()
    expect(() => CreditAmount.parse('0', signed).toWire('fin-credit-positive')).toThrow()
    expect(negative.toWire(signed)).toBe('-1')
  })
  it('allows exact intermediate overflow and rejects it at the final 38-digit field', () => {
    const max = CreditAmount.parse('9'.repeat(38), signed).exact
    const one = ExactDecimal.fromParts(1n, 0)
    const overflow = max.add(one)
    expect(overflow.toString()).toBe('100000000000000000000000000000000000000')
    expect(() => CreditAmount.fromExact(overflow, signed)).toThrow()
    expect(CreditAmount.fromExact(overflow.subtract(one), signed).toWire()).toBe('9'.repeat(38))
  })
  it('rejects fractional overflow without rounding or truncation', () => {
    expect(() => CreditAmount.fromExact(ExactDecimal.fromParts(1n, 9), signed)).toThrow()
    expect(() => CreditAmount.fromExact(ExactDecimal.fromParts(0n, 0), 'fin-credit-positive')).toThrow()
    // Value-preserving trailing-zero normalization precedes final-field validation.
    expect(CreditAmount.fromExact(ExactDecimal.fromParts(100n, 9), signed).toWire()).toBe('0.0000001')
  })
})

describe('FIN-INV arithmetic value and original wire are independent', () => {
  it.each([
    ['-0', '0', 0], ['-0.000', '0', 0], ['1.2300', '123', 2],
    ['-100.000', '-100', 0], ['0.000000000001', '1', 12],
  ] as const)('preserves %s while normalizing arithmetic', (wire, coefficient, scale) => {
    const value = InventoryDecimal.parse(wire, 'fin-inv-qty12')
    expect(value.exact.coefficient.toString()).toBe(coefficient)
    expect(value.exact.scale).toBe(scale)
    expect(value.originalWire).toBe(wire)
    expect(value.toWire()).toBe(wire)
    expect(JSON.stringify(value)).toBe(JSON.stringify(wire))
    value.exact.add(ExactDecimal.fromParts(1n, 0))
    expect(value.toWire()).toBe(wire)
  })
  it('does not impose an integer digit cap beyond the 90-digit shared example', () => {
    const wire = `${'9'.repeat(200)}.123456789012345678`
    const value = InventoryDecimal.parse(wire, 'fin-inv-decimal18')
    expect(value.toWire()).toBe(wire)
    expect(value.exact.coefficient.toString()).toBe(`${'9'.repeat(200)}123456789012345678`)
    expect(value.exact.scale).toBe(18)
  })
  it('enforces the raw fractional boundary before normalizing zeros', () => {
    expect(() => InventoryDecimal.parse('1.0000000000000', 'fin-inv-qty12')).toThrow()
    expect(() => InventoryDecimal.parse('0.' + '0'.repeat(19), 'fin-inv-decimal18')).toThrow()
    const value = InventoryDecimal.parse('1.0000000000000', 'fin-inv-decimal18')
    expect(value.exact.toString()).toBe('1')
    expect(() => value.toWire('fin-inv-qty12')).toThrow()
    expect(InventoryDecimal.fromExact(value.exact, 'fin-inv-qty12').toWire()).toBe('1')
    expect(value.originalWire).toBe('1.0000000000000')
  })
  it('rejects final quantity/decimal overflow without rounding', () => {
    const value = InventoryDecimal.parse('0.0000000000001', 'fin-inv-decimal18')
    expect(() => InventoryDecimal.fromExact(value.exact, 'fin-inv-qty12')).toThrow()
    expect(() => InventoryDecimal.fromExact(ExactDecimal.fromParts(1n, 19), 'fin-inv-decimal18')).toThrow()
  })
})

describe('exact BigInt arithmetic', () => {
  const credit = (wire: string) => CreditAmount.parse(wire, 'fin-credit-signed').exact
  it('adds across the JS safe-integer boundary without losing a unit', () => {
    expect(credit('9007199254740992').add(credit('1')).toString()).toBe('9007199254740993')
    expect(credit('9007199254740993').subtract(credit('9007199254740992')).toString()).toBe('1')
  })
  it('aligns scales and normalizes exact cancellation and carries', () => {
    expect(credit('0.1').add(credit('0.2')).toString()).toBe('0.3')
    expect(credit('1.23').subtract(credit('2')).toString()).toBe('-0.77')
    expect(credit('-0.1').add(credit('0.1')).toString()).toBe('0')
    expect(credit('999.99999999').add(credit('0.00000001')).toString()).toBe('1000')
  })
  it('compares signs and different scales using integer coefficients', () => {
    expect(credit('9007199254740993').compare(credit('9007199254740992'))).toBe(1)
    expect(credit('-1').compare(credit('-0.1'))).toBe(-1)
    expect(InventoryDecimal.parse('-0.000', 'fin-inv-qty12').exact.compare(credit('0'))).toBe(0)
    expect(InventoryDecimal.parse('1.00', 'fin-inv-qty12').exact.compare(credit('1'))).toBe(0)
  })
  it('keeps 90-digit calculations exact without changing original wire', () => {
    const original = InventoryDecimal.parse(`${'9'.repeat(90)}.999999999999`, 'fin-inv-qty12')
    const sum = original.exact.add(ExactDecimal.fromParts(1n, 12))
    expect(sum.toString()).toBe('1' + '0'.repeat(90))
    expect(original.originalWire).toBe(`${'9'.repeat(90)}.999999999999`)
  })
  it('validates coefficient and scale metadata', () => {
    for (const scale of [-1, 0.5, NaN, Infinity, 9007199254740992]) {
      expect(() => ExactDecimal.fromParts(1n, scale)).toThrow(RangeError)
    }
    expect(() => ExactDecimal.fromParts(1 as unknown as bigint, 0)).toThrow(TypeError)
    expect(ExactDecimal.fromParts(-1200n, 3).toString()).toBe('-1.2')
  })
  it('freezes parsed values and refuses implicit arithmetic coercion', () => {
    for (const value of [CreditAmount.parse('1', 'fin-credit-signed'), InventoryDecimal.parse('1.0', 'fin-inv-qty12')]) {
      expect(Object.isFrozen(value)).toBe(true)
      expect(Object.isFrozen(value.exact)).toBe(true)
      expect(() => +(value as unknown as number)).toThrow(TypeError)
      expect(() => (value as unknown as number) + 1).toThrow(TypeError)
      expect(() => +(value.exact as unknown as number)).toThrow(TypeError)
    }
  })
})
