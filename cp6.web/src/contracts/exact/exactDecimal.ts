/** Exact intermediate arithmetic. Field limits belong to the domain serializers. */
export class ExactDecimal {
  readonly coefficient: bigint
  readonly scale: number

  private constructor(coefficient: bigint, scale: number) {
    this.coefficient = coefficient
    this.scale = scale
    Object.freeze(this)
  }

  static fromParts(coefficient: bigint, scale: number): ExactDecimal {
    if (typeof coefficient !== 'bigint') throw new TypeError('Coefficient must be a bigint')
    // Only scale metadata uses a JS number; the amount never does.
    if (!Number.isSafeInteger(scale) || scale < 0) {
      throw new RangeError('Scale must be a nonnegative safe integer')
    }
    if (coefficient === 0n) return new ExactDecimal(0n, 0)
    while (scale > 0 && coefficient % 10n === 0n) {
      coefficient /= 10n
      scale -= 1
    }
    return new ExactDecimal(coefficient, scale)
  }

  private align(other: ExactDecimal): readonly [bigint, bigint, number] {
    const scale = Math.max(this.scale, other.scale)
    return [
      this.coefficient * 10n ** BigInt(scale - this.scale),
      other.coefficient * 10n ** BigInt(scale - other.scale),
      scale,
    ]
  }

  add(other: ExactDecimal): ExactDecimal {
    const [left, right, scale] = this.align(other)
    return ExactDecimal.fromParts(left + right, scale)
  }

  subtract(other: ExactDecimal): ExactDecimal {
    const [left, right, scale] = this.align(other)
    return ExactDecimal.fromParts(left - right, scale)
  }

  compare(other: ExactDecimal): -1 | 0 | 1 {
    const [left, right] = this.align(other)
    return left < right ? -1 : left > right ? 1 : 0
  }

  /** Canonical display of the arithmetic value, not an original wire or field approval. */
  toString(): string {
    const negative = this.coefficient < 0n
    const digits = (negative ? -this.coefficient : this.coefficient).toString()
    const padded = digits.padStart(this.scale + 1, '0')
    const magnitude = this.scale === 0
      ? padded
      : `${padded.slice(0, -this.scale)}.${padded.slice(-this.scale)}`
    return `${negative ? '-' : ''}${magnitude}`
  }

  [Symbol.toPrimitive](hint: string): string {
    if (hint === 'string') return this.toString()
    throw new TypeError('Use explicit exact arithmetic; numeric coercion is forbidden')
  }
}
