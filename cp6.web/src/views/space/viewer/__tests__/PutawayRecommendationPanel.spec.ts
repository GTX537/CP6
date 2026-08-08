import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import PutawayRecommendationPanel from '../PutawayRecommendationPanel.vue'
import type { SpacePutawayRecommendation } from '@/types/space/runtime'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (value: string) => value }),
}))

describe('PutawayRecommendationPanel', () => {
  it('emits a manual bounded request scoped to the current floor', async () => {
    const wrapper = mountPanel(null)
    const inputs = wrapper.findAll('input')
    await inputs[0]!.setValue(' SKU-01 ')
    await inputs[1]!.setValue('OWNER-1')
    await inputs[2]!.setValue('LOT-1')
    await inputs[3]!.setValue('5')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.emitted('generate')?.[0]).toEqual([{
      materialNumber: 'SKU-01',
      ownerId: 'OWNER-1',
      lotNumber: 'LOT-1',
      inboundQuantity: 5,
      floorLogicalId: 'floor-1',
      requiredWidthMillimeters: null,
      requiredHeightMillimeters: null,
      requiredDepthMillimeters: null,
      requiredMaxLoad: null,
      allowExactStockConsolidation: true,
      maximumCandidates: 10,
    }])
  })

  it('renders candidate, source, exclusions and explicit non-execution limits', async () => {
    const wrapper = mountPanel(result)

    expect(wrapper.text()).toContain('推荐不会预留库位、移动库存或创建任务')
    expect(wrapper.text()).toContain('#1 · F1-L01')
    expect(wrapper.text()).toContain('ConsolidateExactStockIdentity')
    expect(wrapper.text()).toContain('ACTIVE_TASK_AT_OBSERVATION')
    expect(wrapper.text()).toContain('RECOMMENDATION_DOES_NOT_RESERVE_MOVE_OR_WRITE_INVENTORY')

    await wrapper.get('.candidate-section .location-list button').trigger('click')
    await wrapper.get('.exclusion-section .location-list button').trigger('click')
    await wrapper.get('.close').trigger('click')

    expect(wrapper.emitted('locate')?.[0]).toEqual(['F1-L01'])
    expect(wrapper.emitted('locate')?.[1]).toEqual(['F1-L03'])
    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('keeps the last successful result visible during refresh failure', async () => {
    const wrapper = mountPanel(result)
    await wrapper.setProps({ loading: true, error: 'generation failed' })

    expect(wrapper.text()).toContain('generation failed')
    expect(wrapper.text()).toContain('正在更新，当前显示上次成功推荐')
    expect(wrapper.text()).toContain('F1-L01')
  })
})

function mountPanel(value: SpacePutawayRecommendation | null) {
  return mount(PutawayRecommendationPanel, {
    props: {
      currentFloorId: 'floor-1',
      result: value,
      loading: false,
      error: '',
    },
  })
}

const source = {
  kind: 'Real',
  adapterId: 'cp6-wms-v1',
  dataSourceId: 'CP6_WMS',
  observedAtUtc: '2026-08-02T17:59:58Z',
  receivedAtUtc: '2026-08-02T18:00:00Z',
  delayMilliseconds: 2_000,
  clockSkewMilliseconds: 0,
  isSimulated: false,
  isAvailable: true,
}

const result: SpacePutawayRecommendation = {
  recommendationId: 'recommendation-1',
  siteId: 'site-1',
  publishedVersionId: 'version-1',
  warehouseCode: 'WH-01',
  generatedAtUtc: '2026-08-02T18:00:00Z',
  generatedBy: 'actor-1',
  definitionVersion: 'space-putaway-v1',
  outcome: 'CandidatesGenerated',
  request: {
    materialNumber: 'SKU-1',
    ownerId: 'OWNER-1',
    lotNumber: 'LOT-1',
    inboundQuantity: 5,
    allowExactStockConsolidation: true,
    maximumCandidates: 10,
  },
  sources: { inventory: source, activeTasks: source },
  examinedLocationCount: 3,
  eligibleCandidateCount: 2,
  returnedCandidateCount: 1,
  isTruncated: true,
  exclusions: {
    missingSpatialMetadata: 0,
    outsideRequestedScope: 0,
    activeTask: 1,
    invalidInventory: 0,
    locationCodeMismatch: 0,
    occupiedIncompatible: 0,
    dimensionTooSmall: 0,
    loadUnverifiable: 0,
    loadInsufficient: 0,
  },
  exclusionSamplesTruncated: false,
  exclusionSamples: [{
    locationLogicalId: 'location-3',
    spaceLocationCode: 'F1-L03',
    floorLogicalId: 'floor-1',
    floorCode: 'F1',
    zoneLogicalId: 'zone-1',
    zoneCode: 'Z1',
    reason: 'ACTIVE_TASK_AT_OBSERVATION',
  }],
  candidates: [{
    rank: 1,
    category: 'ConsolidateExactStockIdentity',
    locationLogicalId: 'location-1',
    spaceLocationCode: 'F1-L01',
    floorLogicalId: 'floor-1',
    floorCode: 'F1',
    floorName: 'Floor 1',
    floorLevel: 1,
    zoneLogicalId: 'zone-1',
    zoneCode: 'Z1',
    rackLogicalId: 'rack-1',
    rackCode: 'R1',
    columnNo: 1,
    levelNo: 1,
    depthNo: 1,
    widthMillimeters: 1_000,
    heightMillimeters: 1_000,
    depthMillimeters: 1_000,
    maxLoad: 200,
    currentPhysicalQuantity: 10,
    currentAllocatedQuantity: 2,
    sameFloorAsExistingStock: true,
    sameZoneAsExistingStock: true,
    distanceToMatchingStockMeters: 0,
    ruleHits: ['EXACT_SKU_OWNER_LOT_CONSOLIDATION'],
  }],
  limitations: ['RECOMMENDATION_DOES_NOT_RESERVE_MOVE_OR_WRITE_INVENTORY'],
}
