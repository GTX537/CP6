import { ExactDecimal } from './exactDecimal'

export type CreditProfile = 'fin-credit-signed' | 'fin-credit-nonnegative' | 'fin-credit-positive'

// Absolute end: JS $ alone also matches before a final newline.
const canonicalWire = /^-?(?:0|[1-9][0-9]*)(?:\.[0-9]*[1-9])?(?![\s\S])/

function validate(wire: unknown, profile: CreditProfile): asserts wire is string {
  switch (profile) {
    case 'fin-credit-signed':
    case 'fin-credit-nonnegative':
    case 'fin-credit-positive':
      break
    default:
      throw new RangeError('Unknown FIN-CREDIT profile')
  }
  if (typeof wire !== 'string') throw new TypeError('FIN-CREDIT requires a JSON string')
  if (!canonicalWire.test(wire) || wire === '-0') {
    throw new RangeError('FIN-CREDIT requires canonical decimal wire')
  }
  const magnitude = wire.startsWith('-') ? wire.slice(1) : wire
  const point = magnitude.indexOf('.')
  const scale = point < 0 ? 0 : magnitude.length - point - 1
  const digits = magnitude.length - (point < 0 ? 0 : 1)
  if (digits > 38 || scale > 8) throw new RangeError('FIN-CREDIT exceeds 38 digits or scale 8')
  if (profile !== 'fin-credit-signed' && wire.startsWith('-')) {
    throw new RangeError('FIN-CREDIT field does not allow negative values')
  }
  if (profile === 'fin-credit-positive' && wire === '0') {
    throw new RangeError('FIN-CREDIT field requires a positive value')
  }
}

/** FIN-CREDIT only; never use this profile for ERP Money or FIN-INV. */
export class CreditAmount {
  readonly exact: ExactDecimal
  readonly profile: CreditProfile

  private constructor(exact: ExactDecimal, profile: CreditProfile) {
    this.exact = exact
    this.profile = profile
    Object.freeze(this)
  }

  static parse(wire: unknown, profile: CreditProfile): CreditAmount {
    validate(wire, profile)
    const point = wire.indexOf('.')
    const scale = point < 0 ? 0 : wire.length - point - 1
    return new CreditAmount(ExactDecimal.fromParts(BigInt(wire.replace('.', '')), scale), profile)
  }

  /** Validate a computed value against the destination field; never round to fit. */
  static fromExact(value: ExactDecimal, profile: CreditProfile): CreditAmount {
    return CreditAmount.parse(value.toString(), profile)
  }

  toWire(profile: CreditProfile = this.profile): string {
    const wire = this.exact.toString()
    validate(wire, profile)
    return wire
  }

  toJSON(): string { return this.toWire() }
  toString(): string { return this.toWire() }

  [Symbol.toPrimitive](hint: string): string {
    if (hint === 'string') return this.toWire()
    throw new TypeError('Use exact for arithmetic; numeric coercion is forbidden')
  }
}
