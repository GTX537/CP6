import { describe, it, expect } from 'vitest'
import { TIMEZONE_OPTIONS, normalizeTimeZoneId } from '../timezones'

describe('timezone options (E-T2 租户时区下拉数据源)', () => {
  it('提供非空候选集，含常用亚洲/欧美时区', () => {
    expect(TIMEZONE_OPTIONS.length).toBeGreaterThan(10)
    const values = TIMEZONE_OPTIONS.map((o) => o.value)
    expect(values).toContain('Asia/Tokyo')
    expect(values).toContain('America/New_York')
    expect(values).toContain('UTC')
  })

  it('每个候选都有 value 和 label', () => {
    for (const o of TIMEZONE_OPTIONS) {
      expect(typeof o.value).toBe('string')
      expect(o.value.length).toBeGreaterThan(0)
      expect(typeof o.label).toBe('string')
      expect(o.label.length).toBeGreaterThan(0)
    }
  })

  it('value 唯一（无重复下拉项）', () => {
    const values = TIMEZONE_OPTIONS.map((o) => o.value)
    expect(new Set(values).size).toBe(values.length)
  })
})

describe('normalizeTimeZoneId (保存前规整)', () => {
  it('空/空白/null/undefined → null（清空，沿用默认时区）', () => {
    expect(normalizeTimeZoneId('')).toBeNull()
    expect(normalizeTimeZoneId('   ')).toBeNull()
    expect(normalizeTimeZoneId(null)).toBeNull()
    expect(normalizeTimeZoneId(undefined)).toBeNull()
  })

  it('有值 → trim 后原样返回（校验交后端 E-WF-028）', () => {
    expect(normalizeTimeZoneId('Asia/Tokyo')).toBe('Asia/Tokyo')
    expect(normalizeTimeZoneId('  America/New_York  ')).toBe('America/New_York')
  })
})
