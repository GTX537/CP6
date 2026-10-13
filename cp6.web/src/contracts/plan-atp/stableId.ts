export const STABLE_ID_PROFILE = 'ATP-STABLEID-LEXICAL/20261011-R1'
export type StableId = string & { readonly __brand: 'StableId' }

// Absolute end is required: '$' also matches before a final line terminator.
const pattern = /^[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}(?![\s\S])/u

export function stableId(value: unknown): StableId {
  if (typeof value !== 'string') throw new TypeError('ATP StableId requires a string')
  if (!pattern.test(value)) throw new RangeError('Invalid ATP StableId')
  return value as StableId
}
