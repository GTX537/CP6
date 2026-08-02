import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import WarehouseOverviewPanel from '../WarehouseOverviewPanel.vue'
import type { SpaceWarehouseOverviewResponse } from '@/types/space/runtime'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (value: string) => value }),
}))

const source = {
  kind: 'Real' as const,
  adapterId: 'cp6-wms-v1',
  dataSourceId: 'CP6_WMS',
  observedAtUtc: '2026-08-02T12:00:00Z',
  receivedAtUtc: '2026-08-02T12:00:01Z',
  delayMilliseconds: 1000,
  clockSkewMilliseconds: 0,
  isSimulated: false,
  isAvailable: true,
}

const response: SpaceWarehouseOverviewResponse = {
  siteId: 'site-1',
  publishedVersionId: 'version-1',
  warehouseCode: 'WH-01',
  capturedAtUtc: '2026-08-02T12:00:00Z',
  isRuntimeComplete: false,
  model: {
    floorCount: 2,
    areaAvailableFloorCount: 1,
    areaMissingFloorCount: 1,
    totalFloorAreaSquareMeters: null,
    zoneCount: 3,
    rackCount: 4,
    rackFootprintSquareMeters: 25,
    rackFootprintRatePercent: null,
    activeLocationCount: 10,
  },
  inventory: {
    source,
    inventoryLineCount: 4,
    occupiedLocationCount: 3,
    unoccupiedLocationCount: 7,
    occupiedLocationRatePercent: 30,
    occupiedLocationRateMethod: 'POSITIVE_PHYSICAL_INVENTORY_LOCATION_COUNT',
    capacityUtilizationPercent: null,
    capacityUtilizationStatus: 'Unavailable',
    capacityUtilizationReason: 'WMS_LOCATION_CAPACITY_NOT_AVAILABLE',
    distinctOwnerCount: 1,
    distinctMaterialCount: 3,
    distinctLotCount: 2,
    distinctContainerCount: 1,
  },
  tasks: { source, activeTaskCount: 2, activeTaskStopCount: 5 },
  anomalies: {
    activeDeviceAlarmCount: 1,
    criticalDeviceAlarmCount: 1,
    codeMismatchLocationCount: 1,
    overAllocatedInventoryLineCount: 0,
    areaMissingFloorCount: 1,
    unclassifiedAbcMaterialCount: 1,
  },
  abc: {
    source,
    windowDays: 90,
    windowStartDate: '2026-05-04',
    windowEndDateExclusive: '2026-08-02',
    transactionTimeBasis: 'COMPLETE_UTC_NATURAL_DAYS',
    rankingMethod: 'PREVIOUS_CUMULATIVE_SHARE',
    aThresholdPercent: 80,
    bThresholdPercent: 95,
    spatialMappingAvailable: true,
    materialCount: 4,
    aCount: 1,
    bCount: 1,
    cCount: 1,
    unclassifiedCount: 1,
    materials: [],
    locations: [],
  },
  floors: [{
    floorLogicalId: 'floor-1',
    floorCode: 'F1',
    floorName: 'Floor 1',
    floorLevel: 1,
    areaSquareMeters: 100,
    activeLocationCount: 10,
    occupiedLocationCount: 3,
    occupiedLocationRatePercent: 30,
    aLocationCount: 1,
    bLocationCount: 1,
    cLocationCount: 0,
    unclassifiedLocationCount: 1,
  }],
}

const mountPanel = () => mount(WarehouseOverviewPanel, {
  props: {
    loading: false,
    response,
    abcOverlayOn: false,
    currentFloorId: 'floor-1',
  },
})

describe('WarehouseOverviewPanel', () => {
  it('keeps exact occupancy separate from unavailable capacity utilization', () => {
    const wrapper = mountPanel()

    expect(wrapper.text()).toContain('30.0%')
    expect(wrapper.text()).toContain('WMS_LOCATION_CAPACITY_NOT_AVAILABLE')
    expect(wrapper.text()).toContain('部分可用')
    expect(wrapper.text()).toContain('CP6_WMS')
    expect(wrapper.text()).toContain('2026')
  })

  it('emits a bounded refresh window and ABC overlay intent', async () => {
    const wrapper = mountPanel()
    const input = wrapper.get('input[type="number"]')
    await input.setValue('999')
    await wrapper.get('.overview-controls button').trigger('click')
    await wrapper.get('input[type="checkbox"]').setValue(true)

    expect(wrapper.emitted('refresh')?.[0]).toEqual([365])
    expect(wrapper.emitted('toggle-abc')?.[0]).toEqual([true])
  })

  it('emits the selected floor identity', async () => {
    const wrapper = mountPanel()
    await wrapper.get('.floor-list button').trigger('click')
    expect(wrapper.emitted('switch-floor')?.[0]).toEqual(['floor-1'])
  })
})
