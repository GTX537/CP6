import { describe, it, expect } from 'vitest'
import { Vector3 } from 'three'
import { resolveIntersection } from './Picker'
import type { InstancedBuckets } from '../build/InstancedBuckets'
import type { SceneRoot } from '../core/SceneRoot'
import type { Intersection } from 'three'

function makeBuckets(map: Record<number, Record<number, string>>): InstancedBuckets {
  return {
    instanceToLocation: (meshId: number, instanceId: number): string | null =>
      map[meshId]?.[instanceId] ?? null,
  } as unknown as InstancedBuckets
}

function makeSceneRoot(): SceneRoot {
  // Inverse of SceneRoot.dataToWorld: world(x,y,z) → data(x*1000, -z*1000, y*1000)
  return {
    worldToData: (v: Vector3) => ({ x: v.x * 1000, y: -v.z * 1000, z: v.y * 1000 }),
  } as unknown as SceneRoot
}

function makeIntersection(meshId: number, instanceId: number | undefined, point: Vector3): Intersection & { instanceId?: number } {
  return { object: { id: meshId } as any, instanceId, point, distance: 1, face: null } as any
}

describe('resolveIntersection', () => {
  it('returns PickResult with locationId and kind=location for valid hit', () => {
    const buckets = makeBuckets({ 42: { 1: 'LOC-001' } })
    const sceneRoot = makeSceneRoot()
    const hit = makeIntersection(42, 1, new Vector3(1, 2, -3))

    const result = resolveIntersection(hit, buckets, sceneRoot)
    expect(result).not.toBeNull()
    expect(result!.kind).toBe('location')
    expect(result!.locationId).toBe('LOC-001')
  })

  it('returns null when instanceId is undefined', () => {
    const buckets = makeBuckets({ 42: { 0: 'LOC-X' } })
    const sceneRoot = makeSceneRoot()
    const hit = makeIntersection(42, undefined, new Vector3(0, 0, 0))
    expect(resolveIntersection(hit, buckets, sceneRoot)).toBeNull()
  })

  it('returns null when instanceId not found in buckets', () => {
    const buckets = makeBuckets({ 42: { 1: 'LOC-001' } })
    const sceneRoot = makeSceneRoot()
    const hit = makeIntersection(42, 99, new Vector3(0, 0, 0))   // 99 not in map
    expect(resolveIntersection(hit, buckets, sceneRoot)).toBeNull()
  })

  it('computes dataPoint via worldToData', () => {
    const buckets = makeBuckets({ 5: { 0: 'LOC-XYZ' } })
    const sceneRoot = makeSceneRoot()
    // world(2, 0, -1) → data(2000, 1000, 0)
    const hit = makeIntersection(5, 0, new Vector3(2, 0, -1))

    const result = resolveIntersection(hit, buckets, sceneRoot)
    expect(result!.dataPoint.x).toBeCloseTo(2000)
    expect(result!.dataPoint.y).toBeCloseTo(1000)
    expect(result!.dataPoint.z).toBeCloseTo(0)
  })

  it('worldPoint on result is the intersection point', () => {
    const buckets = makeBuckets({ 1: { 0: 'L1' } })
    const sceneRoot = makeSceneRoot()
    const pt = new Vector3(3, 4, 5)
    const hit = makeIntersection(1, 0, pt)
    const result = resolveIntersection(hit, buckets, sceneRoot)
    expect(result!.worldPoint).toBe(pt)   // same reference
  })
})
