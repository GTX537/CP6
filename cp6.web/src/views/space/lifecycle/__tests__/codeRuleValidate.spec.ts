import { describe, it, expect } from 'vitest'
import { validateSegmentsLocal, newSegment } from '../codeRuleValidate'
import type { CodeSegmentDef } from '@/types/space/scene'

// 造段助手：以 newSegment 为底覆盖 source/optional
function seg(source: string, optional = false): CodeSegmentDef {
  return { ...newSegment(), source, optional }
}

describe('validateSegmentsLocal', () => {
  // —— E-303 缺 Zone 区分段 ——
  it('E-303 反例：有 zone-code 段则不报（另配库位粒度段避免 E-306 干扰）', () => {
    const errs = validateSegmentsLocal([seg('zone-code'), seg('col')])
    expect(errs).not.toContain('E-303')
  })
  it('E-303 反例：site-code + floor-level 组合替代 Zone 段', () => {
    const errs = validateSegmentsLocal([seg('site-code'), seg('floor-level'), seg('col')])
    expect(errs).not.toContain('E-303')
  })
  it('E-303 正例：既无 Zone 段也无 site+floor 组合', () => {
    const errs = validateSegmentsLocal([seg('rack-code'), seg('col')])
    expect(errs).toContain('E-303')
  })

  // —— E-305 巷道段未 Optional ——
  it('E-305 正例：aisle-code 段 optional=false', () => {
    const errs = validateSegmentsLocal([seg('zone-code'), seg('aisle-code', false), seg('col')])
    expect(errs).toContain('E-305')
  })
  it('E-305 反例：aisle-seq 段 optional=true 不报', () => {
    const errs = validateSegmentsLocal([seg('zone-code'), seg('aisle-seq', true), seg('col')])
    expect(errs).not.toContain('E-305')
  })

  // —— E-306 缺库位粒度段 ——
  it('E-306 正例：col/level/depth 全无', () => {
    const errs = validateSegmentsLocal([seg('zone-code')])
    expect(errs).toContain('E-306')
  })
  it('E-306 反例：含 level 段不报', () => {
    const errs = validateSegmentsLocal([seg('zone-code'), seg('level')])
    expect(errs).not.toContain('E-306')
  })

  it('全合规组合无任何错误码', () => {
    const errs = validateSegmentsLocal([seg('zone-code'), seg('aisle-code', true), seg('col'), seg('level')])
    expect(errs).toEqual([])
  })
})
