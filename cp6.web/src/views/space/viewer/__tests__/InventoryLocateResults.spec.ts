import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import InventoryLocateResults from '../InventoryLocateResults.vue'
import type { SpaceRuntimeInventoryLocateResponse } from '@/types/space/runtime'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (value: string) => value }),
}))

function response(
  overrides: Partial<SpaceRuntimeInventoryLocateResponse> = {},
): SpaceRuntimeInventoryLocateResponse {
  return {
    siteId: 'site-1',
    publishedVersionId: 'version-1',
    warehouseCode: 'WH1',
    source: {
      kind: 'Simulated',
      adapterId: 'mock-v1',
      dataSourceId: 'STANDARD_SAMPLE',
      observedAtUtc: '2026-08-01T12:00:00Z',
      receivedAtUtc: '2026-08-01T12:00:01Z',
      delayMilliseconds: 1000,
      clockSkewMilliseconds: 0,
      isSimulated: true,
      isAvailable: true,
    },
    criteria: {
      materialNumber: 'SKU-01',
      lotNumber: null,
      containerNumber: null,
    },
    locationCount: 2,
    floorCount: 2,
    items: [
      {
        locationLogicalId: 'location-1',
        wmsLogicalId: 'wms-1',
        spaceLocationCode: 'F1-A',
        wmsLocationCode: 'F1-A',
        codeMatches: true,
        floorLogicalId: 'floor-1',
        floorCode: 'F1',
        floorName: 'Floor 1',
        floorLevel: 1,
        physicalQuantity: 5,
        allocatedQuantity: 1,
        materialNumbers: ['SKU-01'],
        lotNumbers: ['LOT-01'],
        containerNumbers: [],
      },
      {
        locationLogicalId: 'location-2',
        wmsLogicalId: 'wms-2',
        spaceLocationCode: 'F2-A',
        wmsLocationCode: 'OLD-F2-A',
        codeMatches: false,
        floorLogicalId: 'floor-2',
        floorCode: 'F2',
        floorName: 'Floor 2',
        floorLevel: 2,
        physicalQuantity: 8,
        allocatedQuantity: 0,
        materialNumbers: ['SKU-01'],
        lotNumbers: [],
        containerNumbers: ['BOX-01'],
      },
    ],
    ...overrides,
  }
}

describe('InventoryLocateResults', () => {
  beforeEach(() => vi.clearAllMocks())

  it('explains cross-floor results and emits the selected Space hit', async () => {
    const wrapper = mount(InventoryLocateResults, {
      props: { response: response() },
    })

    expect(wrapper.text()).toContain('找到 2 个库位，分布在 2 个楼层')
    expect(wrapper.text()).toContain('Floor 1 · F1')
    expect(wrapper.text()).toContain('Floor 2 · F2')
    expect(wrapper.text()).toContain('WMS 编码不一致')

    await wrapper.findAll('.locate-hit')[1]!.trigger('click')
    expect(wrapper.emitted('select')?.[0]?.[0]).toMatchObject({
      locationLogicalId: 'location-2',
      spaceLocationCode: 'F2-A',
    })
  })

  it('distinguishes authoritative empty from unavailable', () => {
    const empty = mount(InventoryLocateResults, {
      props: { response: response({ locationCount: 0, floorCount: 0, items: [] }) },
    })
    expect(empty.text()).toContain('没有库位匹配当前物料、批次或容器条件')

    const unavailable = mount(InventoryLocateResults, {
      props: {
        response: response({
          locationCount: 0,
          floorCount: 0,
          items: [],
          source: { ...response().source, kind: 'Unavailable', isAvailable: false },
        }),
      },
    })
    expect(unavailable.text()).toContain('库存数据源不可用，不能判定是否存在匹配库存')
    expect(unavailable.text()).not.toContain('没有库位匹配当前物料、批次或容器条件')
  })
})
