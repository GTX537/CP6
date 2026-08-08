import { describe, expect, it } from 'vitest'
import { SpaceSourceType } from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import { sourceTypeForUnderlay } from './underlayFile'

describe('sourceTypeForUnderlay', () => {
  it.each([
    ['floor.PDF', 'application/pdf', SpaceSourceType._2],
    ['floor.png', 'image/png', SpaceSourceType._3],
    ['floor.jpg', 'image/jpeg', SpaceSourceType._4],
    ['floor.jpeg', '', SpaceSourceType._4],
  ])('maps %s to the frozen source enum', (name, type, expected) => {
    expect(sourceTypeForUnderlay({ name, type })).toBe(expected)
  })

  it.each([
    ['floor.dwg', 'application/octet-stream'],
    ['floor.pdf.exe', 'application/pdf'],
    ['floor.png', 'image/jpeg'],
    ['floor', 'image/png'],
  ])('rejects unsupported or mismatched %s', (name, type) => {
    expect(() => sourceTypeForUnderlay({ name, type })).toThrow(
      'Unsupported underlay file',
    )
  })
})
