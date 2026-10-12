import { ExactDecimal } from './exactDecimal'

export type InventoryProfile = 'fin-inv-decimal18' | 'fin-inv-qty12'

// FIN-INV permits negative zero and trailing zeros. Integer digits have no contract cap.
const inventoryWire = /^-?(?:0|[1-9][0-9]*)(?:\.[0-9]{1,18})?(?![\s\S])/

function validate(wire: unknown, profile: InventoryProfile): asserts wire is string {
  let maxScale: number
  switch (profile) {
    case 'fin-inv-decimal18': maxScale = 18; break
    case 'fin-inv-qty12': maxScale = 12; break
    default: throw new RangeError('Unknown FIN-INV profile')
  }
  if (typeof wire !== 'string') throw new TypeError('FIN-INV requires a JSON string')
  if (!inventoryWire.test(wire)) throw new RangeError('Invalid FIN-INV decimal wire')
  const point = wire.indexOf('.')
  if (point >= 0 && wire.length - point - 1 > maxScale) {
    throw new RangeError(`FIN-INV field exceeds scale ${maxScale}`)
  }
}

export class InventoryDecimal {
  readonly exact: ExactDecimal
  readonly profile: InventoryProfile
  /** Original field string, not the original JSON document bytes used for digest/replay. */
  readonly originalWire: string

  private constructor(exact: ExactDecimal, profile: InventoryProfile, originalWire: string) {
    this.exact = exact
    this.profile = profile
    this.originalWire = originalWire
    Object.freeze(this)
  }

  static parse(wire: unknown, profile: InventoryProfile): InventoryDecimal {
    validate(wire, profile)
    const point = wire.indexOf('.')
    const scale = point < 0 ? 0 : wire.length - point - 1
    return new InventoryDecimal(ExactDecimal.fromParts(BigInt(wire.replace('.', '')), scale), profile, wire)
  }

  /** Create NEW wire for a computed value; does not rewrite any parsed original input. */
  static fromExact(value: ExactDecimal, profile: InventoryProfile): InventoryDecimal {
    return InventoryDecimal.parse(value.toString(), profile)
  }

  /** Preserve original lexical form, and check the destination's raw fractional limit. */
  toWire(profile: InventoryProfile = this.profile): string {
    validate(this.originalWire, profile)
    return this.originalWire
  }

  toJSON(): string { return this.toWire() }
  toString(): string { return this.toWire() }

  [Symbol.toPrimitive](hint: string): string {
    if (hint === 'string') return this.toWire()
    throw new TypeError('Use exact for arithmetic; numeric coercion is forbidden')
  }
}
