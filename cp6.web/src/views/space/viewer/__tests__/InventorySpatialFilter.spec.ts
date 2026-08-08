import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import InventorySpatialFilter from '../InventorySpatialFilter.vue'
import type { SpaceRuntimeInventoryLocateResponse } from '@/types/space/runtime'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (value: string) => value }),
}))

const response = (): SpaceRuntimeInventoryLocateResponse => ({
  siteId: 'site-1',
  publishedVersionId: 'version-1',
  warehouseCode: 'WH1',
  source: {
    kind: 'Real', adapterId: 'cp6-wms-v1', dataSourceId: 'CP6_WMS',
    observedAtUtc: '2026-08-02T12:00:00Z', receivedAtUtc: '2026-08-02T12:00:01Z',
    delayMilliseconds: 1000, clockSkewMilliseconds: 0,
    isSimulated: false, isAvailable: true,
  },
  criteria: {
    ownerId: 'OWNER-A', materialNumber: 'SKU-01', lotNumber: null, containerNumber: null,
  },
  locationCount: 2,
  floorCount: 2,
  items: [
    {
      locationLogicalId: 'location-1', wmsLogicalId: 'wms-1',
      spaceLocationCode: 'F1-A', wmsLocationCode: 'F1-A', codeMatches: true,
      floorLogicalId: 'floor-1', floorCode: 'F1', floorName: 'Floor 1', floorLevel: 1,
      physicalQuantity: 3, allocatedQuantity: 0,
      materialNumbers: ['SKU-01'], lotNumbers: [], containerNumbers: [], ownerIds: ['OWNER-A'],
    },
    {
      locationLogicalId: 'location-2', wmsLogicalId: 'wms-2',
      spaceLocationCode: 'F2-A', wmsLocationCode: 'F2-A', codeMatches: true,
      floorLogicalId: 'floor-2', floorCode: 'F2', floorName: 'Floor 2', floorLevel: 2,
      physicalQuantity: 4, allocatedQuantity: 0,
      materialNumbers: ['SKU-01'], lotNumbers: [], containerNumbers: [], ownerIds: ['OWNER-A'],
    },
  ],
})

describe('InventorySpatialFilter', () => {
  it('emits a normalized four-dimensional exact filter', async () => {
    const wrapper = mount(InventorySpatialFilter, {
      props: { loading: false, response: null, currentFloorId: 'floor-1' },
    })
    const inputs = wrapper.findAll('input')
    await inputs[0]!.setValue(' owner-a ')
    await inputs[1]!.setValue(' SKU-01 ')
    await inputs[2]!.setValue(' LOT-01 ')
    await inputs[3]!.setValue(' BOX-01 ')
    await wrapper.find('.actions button').trigger('click')

    expect(wrapper.emitted('apply')?.[0]?.[0]).toEqual({
      ownerId: 'OWNER-A', materialNumber: 'SKU-01', lotNumber: 'LOT-01', containerNumber: 'BOX-01',
    })
  })

  it('explains current-floor and cross-floor hits and can switch floors', async () => {
    const wrapper = mount(InventorySpatialFilter, {
      props: { loading: false, response: response(), currentFloorId: 'floor-1' },
    })

    expect(wrapper.text()).toContain('本层 1 / 全站 2 个库位 / 2 个楼层')
    expect(wrapper.text()).toContain('Floor 1 · 1')
    expect(wrapper.text()).toContain('Floor 2 · 1')
    await wrapper.findAll('.floor-groups button')[1]!.trigger('click')
    expect(wrapper.emitted('switch-floor')?.[0]).toEqual(['floor-2'])
  })
})
