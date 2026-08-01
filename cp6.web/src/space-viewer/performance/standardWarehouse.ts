import type { EditorScene, LocationVO, RackVO } from '@/types/space/scene'
import type { EnrichedLocation } from '../build/InstancedBuckets'
import { SPACE_PERFORMANCE_BUDGETS as budgets } from './budgets'

const floorId = 'performance-floor'

function rectangle(x: number, y: number, width: number, depth: number): string {
  return JSON.stringify([
    [x, y],
    [x + width, y],
    [x + width, y + depth],
    [x, y + depth],
  ])
}

export function createStandardWarehouseScene(): EditorScene {
  const zones = Array.from({ length: budgets.standardZoneCount }, (_, index) => ({
    id: `zone-${index + 1}`,
    floorId,
    zoneCode: `Z${String(index + 1).padStart(2, '0')}`,
    zoneName: `Performance Zone ${index + 1}`,
    zoneType: 1,
    polygon: rectangle(index * 32_000, 0, 30_000, 84_000),
    color: index % 2 === 0 ? '#1f6feb' : '#238636',
    enable: true,
  }))
  const aisles = Array.from({ length: budgets.aisleCount }, (_, index) => {
    const zone = zones[index % zones.length]!
    const zoneIndex = zones.indexOf(zone)
    const lane = Math.floor(index / zones.length)
    const x = zoneIndex * 32_000 + 1_000
    const y = 2_000 + lane * 24_000
    return {
      id: `aisle-${index + 1}`,
      zoneId: zone.id,
      aisleCode: `A${String(index + 1).padStart(2, '0')}`,
      polygon: rectangle(x, y, 28_000, 4_000),
      centerline: JSON.stringify([[x, y + 2_000], [x + 28_000, y + 2_000]]),
    }
  })
  const racks: RackVO[] = []
  const locations: LocationVO[] = []
  const locationsPerRack = budgets.locationCount / budgets.rackCount

  for (let rackIndex = 0; rackIndex < budgets.rackCount; rackIndex++) {
    const zone = zones[rackIndex % zones.length]!
    const aisle = aisles[rackIndex % aisles.length]!
    const zoneIndex = zones.indexOf(zone)
    const localRack = Math.floor(rackIndex / zones.length)
    const rackColumn = localRack % 10
    const rackRow = Math.floor(localRack / 10)
    const rack: RackVO = {
      id: `rack-${rackIndex + 1}`,
      zoneId: zone.id,
      aisleId: aisle.id,
      floorId,
      rackCode: `R${String(rackIndex + 1).padStart(3, '0')}`,
      x: zoneIndex * 32_000 + 1_500 + rackColumn * 2_800,
      y: 8_000 + rackRow * 7_000,
      z: 0,
      rotationZ: rackRow % 2 === 0 ? 0 : 180,
      cols: 10,
      levels: 2,
      depthCount: 1,
      cellW: 1_000,
      cellH: 1_200,
      cellD: 1_100,
      enable: true,
    }
    racks.push(rack)
    for (let slot = 0; slot < locationsPerRack; slot++) {
      const col = slot % rack.cols
      const level = Math.floor(slot / rack.cols)
      const locationIndex = rackIndex * locationsPerRack + slot
      locations.push({
        id: `location-${locationIndex + 1}`,
        rackId: rack.id,
        floorId,
        locationCode: `P-${String(locationIndex + 1).padStart(5, '0')}`,
        codeOrigin: 1,
        col: col + 1,
        level: level + 1,
        depth: 1,
        absX: rack.x + col * rack.cellW + rack.cellW / 2,
        absY: rack.y + rack.cellD / 2,
        absZ: level * rack.cellH + rack.cellH / 2,
        sizeW: rack.cellW,
        sizeH: rack.cellH,
        sizeD: rack.cellD,
        placed: true,
        status: 1,
        version: 1,
      })
    }
  }

  return {
    source: {
      kind: 'Simulated',
      dataSourceId: 'E08-S05-STANDARD',
      observedAtUtc: '2026-08-01T00:00:00Z',
      isSimulated: true,
      isAvailable: true,
    },
    floor: {
      id: floorId,
      siteId: 'performance-site',
      level: 1,
      floorCode: 'F1',
      floorName: 'Performance Floor',
      height: 6_000,
      underlayOffsetX: 0,
      underlayOffsetY: 0,
      originX: 0,
      originY: 0,
    },
    zones,
    aisles,
    racks,
    locations,
    markers: [],
  }
}

export function createStressLocations(): EnrichedLocation[] {
  const perBucket = budgets.locationCount / budgets.stressBucketCount
  return Array.from({ length: budgets.locationCount }, (_, index) => {
    const bucket = Math.floor(index / perBucket)
    const local = index % perBucket
    return {
      id: `stress-location-${index + 1}`,
      zoneId: `stress-zone-${bucket + 1}`,
      placed: true,
      absX: bucket * 4_000 + (local % 20) * 160,
      absY: Math.floor(local / 20) * 180,
      absZ: 600,
      sizeW: 140,
      sizeH: 1_200,
      sizeD: 160,
      rotationZ: 0,
    }
  })
}
