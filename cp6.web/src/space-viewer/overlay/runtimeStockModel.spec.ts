import { describe, expect, it } from 'vitest'
import { aggregateRuntimeStock } from './runtimeStockModel'
import type {
  RuntimeLocationRef,
  SpaceRuntimeInventoryItem,
  SpaceRuntimeInventoryResponse,
  SpaceRuntimeSource,
} from '@/types/space/runtime'

const source: SpaceRuntimeSource = {
  kind: 'Real',
  adapterId: 'cp6-wms-v1',
  dataSourceId: 'CP6_WMS',
  observedAtUtc: '2026-08-01T12:00:00Z',
  receivedAtUtc: '2026-08-01T12:00:02Z',
  delayMilliseconds: 2000,
  clockSkewMilliseconds: 0,
  isSimulated: false,
  isAvailable: true,
}

const locations: RuntimeLocationRef[] = [
  { locationLogicalId: 'space-1', locationCode: 'SPACE-A' },
  { locationLogicalId: 'space-2', locationCode: 'SPACE-B' },
]

function row(overrides: Partial<SpaceRuntimeInventoryItem>): SpaceRuntimeInventoryItem {
  return {
    locationLogicalId: 'space-1',
    wmsLogicalId: 'wms-1',
    spaceLocationCode: 'SPACE-A',
    wmsLocationCode: 'WMS-A',
    codeMatches: false,
    floorLogicalId: 'floor-1',
    floorCode: 'F1',
    floorName: 'Floor 1',
    floorLevel: 1,
    physicalQuantity: 0,
    allocatedQuantity: 0,
    materialNumber: null,
    lotNumber: null,
    containerNumber: null,
    ownerId: null,
    ...overrides,
  }
}

function response(items: SpaceRuntimeInventoryItem[]): SpaceRuntimeInventoryResponse {
  return {
    siteId: 'site-1',
    publishedVersionId: 'version-1',
    warehouseCode: 'WH1',
    source,
    items,
  }
}

describe('aggregateRuntimeStock', () => {
  it('aggregates rows by logical identity and emits explicit empty requested locations', () => {
    const result = aggregateRuntimeStock(response([
      row({ physicalQuantity: 3, allocatedQuantity: 1, materialNumber: 'SKU-B' }),
      row({ physicalQuantity: 7, allocatedQuantity: 2, materialNumber: 'SKU-A' }),
      row({ physicalQuantity: 1, materialNumber: 'SKU-A' }),
    ]), locations)

    expect(result).toEqual([
      {
        locationLogicalId: 'space-1',
        locationCode: 'SPACE-A',
        binStatus: 1,
        qty: 11,
        allocatedQty: 3,
        capacity: null,
        topMaterial: 'SKU-A',
        productKinds: 2,
      },
      {
        locationLogicalId: 'space-2',
        locationCode: 'SPACE-B',
        binStatus: 0,
        qty: 0,
        allocatedQty: 0,
        capacity: null,
        topMaterial: null,
        productKinds: 0,
      },
    ])
  })

  it('keeps Space logical identity authoritative when WMS code differs', () => {
    const [item] = aggregateRuntimeStock(response([
      row({ physicalQuantity: 2, wmsLocationCode: 'DIFFERENT-WMS-CODE' }),
    ]), locations)

    expect(item?.locationLogicalId).toBe('space-1')
    expect(item?.locationCode).toBe('SPACE-A')
  })

  it('does not turn unavailable data into empty real stock', () => {
    const unavailable = response([])
    unavailable.source = {
      ...source,
      kind: 'Unavailable',
      isAvailable: false,
    }

    expect(aggregateRuntimeStock(unavailable, locations)).toEqual([])
  })
})
