import { describe, it, expect } from 'vitest'
import { parseRuleEntity } from '../codeRule'

// GET /space/code-rule 直出实体：segments 是 JSON 字符串，经全局 camelCase 序列化后
// 键为小写 `segments`。parseRuleEntity 把实体归一为 CodeRuleVO（segments 恒为数组）。
describe('parseRuleEntity', () => {
  const seg = {
    key: 'zone',
    name: '库区',
    source: 'zone-code',
    width: 2,
    pad: '0',
    start: 1,
    step: 1,
    sep: '-',
    upper: true,
    fixedValue: '',
    optional: false,
  }

  it('合法 JSON 字符串 → segments 数组（小写键）', () => {
    const vo = parseRuleEntity({
      id: 'g1',
      ruleName: 'R1',
      scopeType: 1,
      scopeId: 'f1',
      isDefault: true,
      segments: JSON.stringify([seg]),
    })
    expect(vo.segments).toHaveLength(1)
    expect(vo.segments[0]).toMatchObject({ key: 'zone', width: 2, upper: true })
    expect(vo.ruleName).toBe('R1')
    expect(vo.scopeType).toBe(1)
    expect(vo.scopeId).toBe('f1')
    expect(vo.isDefault).toBe(true)
    expect(vo.id).toBe('g1')
  })

  it('"[]" → 空数组', () => {
    const vo = parseRuleEntity({ ruleName: 'R', scopeType: 0, isDefault: false, segments: '[]' })
    expect(vo.segments).toEqual([])
  })

  it('空串 / undefined → 空数组', () => {
    expect(parseRuleEntity({ segments: '' } as any).segments).toEqual([])
    expect(parseRuleEntity({} as any).segments).toEqual([])
  })

  it('非法串不抛异常 → 空数组', () => {
    expect(() => parseRuleEntity({ segments: '{not json' } as any)).not.toThrow()
    expect(parseRuleEntity({ segments: '{not json' } as any).segments).toEqual([])
  })

  it('非数组 JSON（对象）→ 空数组', () => {
    expect(parseRuleEntity({ segments: '{"a":1}' } as any).segments).toEqual([])
  })

  it('PascalCase 键容错（Segments/RuleName/…）', () => {
    const vo = parseRuleEntity({
      Id: 'g2',
      RuleName: 'RP',
      ScopeType: 2,
      ScopeId: 'z9',
      IsDefault: true,
      Segments: JSON.stringify([seg]),
    } as any)
    expect(vo.segments).toHaveLength(1)
    expect(vo.ruleName).toBe('RP')
    expect(vo.scopeType).toBe(2)
    expect(vo.scopeId).toBe('z9')
    expect(vo.isDefault).toBe(true)
    expect(vo.id).toBe('g2')
  })
})
