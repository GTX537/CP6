// @vitest-environment node
import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import vectors from '../../../../eng/contracts/s01/vectors.json'
import profiles from '../../../../eng/contracts/s01/profile-inputs.json'
import dispatch from '../../../../docs/development/dispatch/20261012-wave3/dispatch.json'
import { stableId, STABLE_ID_PROFILE } from './stableId'

const assignment = dispatch.tasks.find((task) => task.id === 'S01-06')!
const shared = vectors.cases.filter((item) => item.profile_id === 'atp-stableid-r1')
const sourceDefinition = profiles.profiles.find((profile) => profile.id === 'atp-stableid-r1')!.schema_or_type_identity.definition!
const schema = JSON.parse(readFileSync(new URL('../../../../eng/contracts/s01/atp-stable-id.schema.json', import.meta.url), 'utf8')) as {
  $id: string; $comment: string; type: string; minLength: number; maxLength: number; pattern: string
}

// This fragment has exactly four assertion keywords. Exercise each using ECMA-262
// regexp and Unicode code-point length; this is not a general JSON Schema validator.
function fragmentAccepts(input: unknown): boolean {
  return schema.type === 'string' && typeof input === 'string' &&
    [...input].length >= schema.minLength && [...input].length <= schema.maxLength &&
    new RegExp(schema.pattern, 'u').test(input)
}

describe('S01-06 exact shared vectors', () => {
  it('discovers exactly all assigned IDs and only supported lexical operations', () => {
    expect(vectors.package_version).toBe('CP6-S01-CONTRACT-INPUTS/1.0.0')
    expect(shared).toHaveLength(22)
    expect(new Set(shared.map((item) => item.id)).size).toBe(22)
    expect(shared.map((item) => item.id).sort()).toEqual([...assignment.case_ids].sort())
    expect(assignment.profiles).toEqual(['atp-stableid-r1'])
    expect([...new Set(shared.map((item) => item.operation))]).toEqual(['lexical'])
  })

  it('extracts the exact four source assertions with a distinct fragment identity', () => {
    expect({ type: schema.type, minLength: schema.minLength, maxLength: schema.maxLength, pattern: schema.pattern }).toEqual(sourceDefinition)
    expect(schema.$id).toBe('urn:cp6:plan-atp:stable-id:20261011-r1')
    expect(schema.$comment).toContain('urn:cp6:plan-atp:wire:design-revision-20261011-r1#/$defs/StableId')
    expect(schema.$comment).toContain('c2e657f668fb72a147754add284a5c4d5c80739189000ca2711eb88d58e6b0c5')
    expect(STABLE_ID_PROFILE).toBe('ATP-STABLEID-LEXICAL/20261011-R1')
  })

  it.each(shared)('$id / TypeScript lexical', (item) => {
    if (item.operation !== 'lexical') throw new Error(`Unsupported operation: ${item.operation}`)
    if (item.expected.accepted) {
      const value = stableId(item.input)
      expect(value).toBe(item.input)
      expect(JSON.stringify(value)).toBe(JSON.stringify(item.input))
    } else expect(() => stableId(item.input)).toThrow()
  })

  it.each(shared)('$id / Schema fragment assertions', (item) => {
    expect(fragmentAccepts(item.input)).toBe(item.expected.accepted)
  })
})

describe('R1 boundaries without coercion or normalization', () => {
  it.each([1, 16, 17, 128])('accepts all %i characters without truncation', (length) => {
    const wire = 'U'.repeat(length)
    expect(stableId(wire)).toBe(wire)
    expect(fragmentAccepts(wire)).toBe(true)
  })
  it('rejects 129 characters in both scalar and fragment', () => {
    expect(() => stableId('U'.repeat(129))).toThrow(RangeError)
    expect(fragmentAccepts('U'.repeat(129))).toBe(false)
  })
  it.each(['', 'EA\n', 'EA\r\n', 'EA\r', 'EA\u2028', 'EA\u2029', 'EA\0', ' EA', 'EA ', 'EA\t', '.EA', '_EA', ':EA', '/EA', '-EA', '公斤', 'ＥＡ', 'A😀', 'é'])('rejects %j', (input) => {
    expect(() => stableId(input)).toThrow(RangeError)
    expect(fragmentAccepts(input)).toBe(false)
  })
  it.each([null, undefined, 0, 1.5, true, false, {}, [], 1n, new String('EA')])('rejects wrong token %s without coercion', (input) => {
    expect(() => stableId(input)).toThrow(TypeError)
    expect(fragmentAccepts(input)).toBe(false)
  })
  it('preserves case and legal punctuation and emits the original scalar string', () => {
    const wire = 'Az09._:/-' + 'X'.repeat(119)
    expect(stableId(wire)).toBe(wire)
    expect(stableId('EA')).not.toBe(stableId('ea'))
    expect(JSON.stringify({ id: stableId(wire) })).toBe(JSON.stringify({ id: wire }))
  })
})
