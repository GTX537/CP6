import { describe, expect, it } from 'vitest'
import { parseEditorPolygon } from './polygon'

describe('parseEditorPolygon', () => {
  it('兼容旧版裸坐标数组', () => {
    expect(parseEditorPolygon('[[0,0],[100,0],[100,100]]')).toEqual([
      [0, 0],
      [100, 0],
      [100, 100],
    ])
  })

  it('解析 schemaVersion=1 的版本化几何对象', () => {
    expect(parseEditorPolygon(JSON.stringify({
      schemaVersion: 1,
      points: [[0, 0], [100, 0], [100, 100]],
    }))).toEqual([
      [0, 0],
      [100, 0],
      [100, 100],
    ])
  })

  it.each([
    '',
    'not-json',
    JSON.stringify({ schemaVersion: 2, points: [[0, 0]] }),
    JSON.stringify({ schemaVersion: 1, points: [[0, null]] }),
  ])('无效或不支持的几何返回空数组: %s', value => {
    expect(parseEditorPolygon(value)).toEqual([])
  })
})
