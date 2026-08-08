import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import OperationsDiagnosticPanel from '../OperationsDiagnosticPanel.vue'
import type { SpaceOperationsDiagnosticResponse } from '@/types/space/runtime'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (value: string) => value }),
}))

describe('OperationsDiagnosticPanel', () => {
  it('keeps exact occupancy separate from unavailable physical capacity', () => {
    const wrapper = mountPanel(response)

    expect(wrapper.text()).toContain('20 m')
    expect(wrapper.text()).toContain('1 已知段 · 1 未知段')
    expect(wrapper.text()).toContain('50.0%')
    expect(wrapper.text()).toContain('WMS_LOCATION_CAPACITY_NOT_AVAILABLE')
    expect(wrapper.text()).toContain('库位占用不等于体积、重量或托盘容量')
    expect(wrapper.text()).toContain('排除模拟事件 2')
  })

  it('emits a bounded window, hotspot locate, and close intent', async () => {
    const wrapper = mountPanel(response)

    await wrapper.get('select').setValue('24')
    await wrapper.get('.run').trigger('click')
    await wrapper.get('.hotspot-list button').trigger('click')
    await wrapper.get('.floor-occupancy-list button').trigger('click')
    await wrapper.get('.close').trigger('click')

    expect(wrapper.emitted('run')?.[0]).toEqual([24])
    expect(wrapper.emitted('select-location')?.[0]).toEqual(['F1-L01'])
    expect(wrapper.emitted('switch-floor')?.[0]).toEqual(['floor-1'])
    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('distinguishes empty, loading, failure, and retained-result states', async () => {
    const wrapper = mountPanel(null)
    expect(wrapper.text()).toContain('尚无诊断结果')

    await wrapper.setProps({ loading: true })
    expect(wrapper.text()).toContain('正在计算运营诊断')

    await wrapper.setProps({ loading: false, error: 'diagnostic source unavailable' })
    expect(wrapper.text()).toContain('diagnostic source unavailable')

    await wrapper.setProps({ result: response, loading: true })
    expect(wrapper.text()).toContain('正在更新，当前显示上次成功结果')
    expect(wrapper.text()).toContain('50.0%')
  })
})

function mountPanel(value: SpaceOperationsDiagnosticResponse | null) {
  return mount(OperationsDiagnosticPanel, {
    props: { result: value, loading: false, error: '' },
  })
}

const source = {
  kind: 'Real' as const,
  adapterId: 'cp6-wms-v1',
  dataSourceId: 'CP6_WMS',
  observedAtUtc: '2026-08-02T11:59:58Z',
  receivedAtUtc: '2026-08-02T12:00:00Z',
  delayMilliseconds: 2000,
  clockSkewMilliseconds: 0,
  isSimulated: false,
  isAvailable: true,
}

const response: SpaceOperationsDiagnosticResponse = {
  siteId: 'site-1',
  publishedVersionId: 'version-1',
  warehouseCode: 'WH-01',
  windowFromUtc: '2026-08-02T04:00:00Z',
  windowToUtc: '2026-08-02T12:00:00Z',
  calculatedAtUtc: '2026-08-02T12:00:00Z',
  definitionVersion: 'space-operations-diagnostics-v1',
  thresholds: {
    maximumObservationGapSeconds: 300,
    minimumBacktrackSegmentMillimeters: 1000,
    backtrackAngleDegrees: 150,
    dwellThresholdSeconds: 300,
    congestionMinimumConcurrentPeople: 2,
    occupancyWatchPercent: 85,
    occupancyCriticalPercent: 95,
  },
  personnelSource: {
    evidenceEventCount: 9,
    eligibleRealEventCount: 6,
    excludedSimulatedEventCount: 2,
    excludedOutsidePublishedModelEventCount: 1,
    personCount: 2,
    sourceCount: 1,
    firstObservedAtUtc: '2026-08-02T11:00:00Z',
    lastObservedAtUtc: '2026-08-02T11:55:00Z',
    lastReceivedAtUtc: '2026-08-02T12:00:00Z',
    sources: [],
  },
  path: {
    personCount: 2,
    observedTransitionCount: 2,
    knownDistanceSegmentCount: 1,
    unknownDistanceSegmentCount: 1,
    observedDistanceMeters: 20,
    backtrackCount: 1,
    backtrackDistanceMeters: 10,
    backtracksTruncated: false,
    backtracks: [{
      floorLogicalId: 'floor-1',
      floorCode: 'F1',
      locationLogicalId: 'location-1',
      spaceLocationCode: 'F1-L01',
      xMillimeters: 10_000,
      yMillimeters: 0,
      occurredAtUtc: '2026-08-02T11:30:00Z',
      turnAngleDegrees: 180,
      returnSegmentMeters: 10,
    }],
  },
  congestion: {
    locationCount: 1,
    peakConcurrentPeople: 2,
    concurrentSeconds: 270,
    hotspotsTruncated: false,
    hotspots: [{
      locationLogicalId: 'location-1',
      spaceLocationCode: 'F1-L01',
      floorLogicalId: 'floor-1',
      floorCode: 'F1',
      peakConcurrentPeople: 2,
      concurrentSeconds: 270,
      observedPersonCount: 2,
    }],
  },
  dwell: {
    episodeCount: 2,
    personCount: 2,
    locationCount: 1,
    totalDwellSeconds: 600,
    hotspotsTruncated: false,
    hotspots: [{
      locationLogicalId: 'location-1',
      spaceLocationCode: 'F1-L01',
      floorLogicalId: 'floor-1',
      floorCode: 'F1',
      episodeCount: 2,
      personCount: 2,
      totalDwellSeconds: 600,
      maximumDwellSeconds: 300,
    }],
  },
  capacity: {
    source,
    isAvailable: true,
    occupancyBasis: 'POSITIVE_PHYSICAL_INVENTORY_DISTINCT_ACTIVE_LOCATION_COUNT',
    locationCount: 2,
    occupiedLocationCount: 1,
    locationOccupancyPercent: 50,
    locationOccupancyPressure: 'Normal',
    capacityUtilizationPercent: null,
    capacityUtilizationStatus: 'Unavailable',
    capacityUtilizationReason: 'WMS_LOCATION_CAPACITY_NOT_AVAILABLE',
    floors: [{
      floorLogicalId: 'floor-1',
      floorCode: 'F1',
      floorName: 'Floor 1',
      floorLevel: 1,
      locationCount: 2,
      occupiedLocationCount: 1,
      locationOccupancyPercent: 50,
      locationOccupancyPressure: 'Normal',
    }],
  },
  limitations: ['CAPACITY_UTILIZATION_NOT_AVAILABLE'],
}
