import { describe, it, expect } from 'vitest'
import { InstancedBuckets } from './InstancedBuckets'

describe('InstancedBuckets', () => {
  const baseLoc = { absX: 0, absY: 0, absZ: 0, sizeW: 1, sizeH: 1, sizeD: 1, rotationZ: 0 }

  it('buckets by zoneId and maps instance<->location, placed only', () => {
    const locs = [
      { id: 'L1', zoneId: 'Z1', placed: true, ...baseLoc },
      { id: 'L2', zoneId: 'Z1', placed: true, absX: 1, absY: 0, absZ: 0, sizeW: 1, sizeH: 1, sizeD: 1, rotationZ: 0 },
      { id: 'L3', zoneId: 'Z2', placed: false, ...baseLoc },
    ]
    const b = new InstancedBuckets()
    b.build(locs as any)

    expect(b.bucketCount()).toBe(1)  // only Z1 has placed locs

    const meshId = b.meshIdForZone('Z1')!
    expect(meshId).toBeDefined()
    expect(b.instanceToLocation(meshId, 0)).toBe('L1')
    expect(b.instanceToLocation(meshId, 1)).toBe('L2')
  })

  it('excludes unplaced locations', () => {
    const locs = [
      { id: 'L1', zoneId: 'Z1', placed: false, ...baseLoc },
      { id: 'L2', zoneId: 'Z1', placed: false, ...baseLoc },
    ]
    const b = new InstancedBuckets()
    b.build(locs as any)
    expect(b.bucketCount()).toBe(0)
  })

  it('locationToInstance reverse mapping is consistent', () => {
    const locs = [
      { id: 'LA', zoneId: 'ZA', placed: true, ...baseLoc },
      { id: 'LB', zoneId: 'ZA', placed: true, absX: 500, absY: 0, absZ: 0, sizeW: 1, sizeH: 1, sizeD: 1, rotationZ: 0 },
      { id: 'LC', zoneId: 'ZB', placed: true, absX: 1000, absY: 0, absZ: 0, sizeW: 1, sizeH: 1, sizeD: 1, rotationZ: 0 },
    ]
    const b = new InstancedBuckets()
    b.build(locs as any)

    expect(b.bucketCount()).toBe(2)

    const refA = b.locationToInstance('LA')!
    expect(refA).not.toBeNull()
    expect(b.instanceToLocation(refA.meshId, refA.instanceId)).toBe('LA')

    const refC = b.locationToInstance('LC')!
    expect(b.instanceToLocation(refC.meshId, refC.instanceId)).toBe('LC')
  })

  it('zones without any placed locations create no bucket', () => {
    const locs = [
      { id: 'L1', zoneId: 'Z1', placed: true, ...baseLoc },
      { id: 'L2', zoneId: 'Z2', placed: false, ...baseLoc },
      { id: 'L3', zoneId: 'Z3', placed: false, ...baseLoc },
    ]
    const b = new InstancedBuckets()
    b.build(locs as any)
    expect(b.bucketCount()).toBe(1)
    expect(b.meshIdForZone('Z2')).toBeUndefined()
    expect(b.meshIdForZone('Z3')).toBeUndefined()
  })
})
