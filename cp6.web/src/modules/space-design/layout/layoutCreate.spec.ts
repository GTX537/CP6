import { describe, expect, it } from 'vitest'
import {
  rackLocationCount,
  rectangleCenterlineJson,
  rectanglePolygonJson,
} from './layoutCreate'

describe('layout create geometry helpers', () => {
  it('builds deterministic rectangle and centerline payloads in millimeters', () => {
    expect(JSON.parse(rectanglePolygonJson(100, 200, 4_000, 2_000))).toEqual({
      schemaVersion: 1,
      points: [
        [100, 200],
        [4_100, 200],
        [4_100, 2_200],
        [100, 2_200],
      ],
    })
    expect(JSON.parse(rectangleCenterlineJson(100, 200, 4_000, 2_000, 1))).toEqual({
      schemaVersion: 1,
      points: [
        [100, 1_200],
        [4_100, 1_200],
      ],
    })
    expect(JSON.parse(rectangleCenterlineJson(100, 200, 4_000, 2_000, 2))).toEqual({
      schemaVersion: 1,
      points: [
        [2_100, 200],
        [2_100, 2_200],
      ],
    })
  })

  it('previews the total deterministic location count', () => {
    expect(rackLocationCount([
      { binCount: 4, depthCount: 2 },
      { binCount: 3, depthCount: 1 },
    ])).toBe(11)
  })
})
