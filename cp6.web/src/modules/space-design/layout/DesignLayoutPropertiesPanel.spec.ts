// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import DesignLayoutPropertiesPanel from './DesignLayoutPropertiesPanel.vue'
import { SpaceSceneRevisionDto } from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const zoneId = '11111111-1111-1111-1111-111111111111'
const aisleId = '22222222-2222-2222-2222-222222222222'
const rackId = '33333333-3333-3333-3333-333333333333'
const revision = (logicalId: string) => new SpaceSceneRevisionDto({ logicalId, lifecycleState: 'Active' })

describe('DesignLayoutPropertiesPanel', () => {
  it('emits a full zone replacement and an explicit delete request', async () => {
    const wrapper = mount(DesignLayoutPropertiesPanel, {
      props: {
        zone: { revision: revision(zoneId), zoneCode: 'Z-A', name: 'Ambient', zoneType: 1, polygonJson: '[]', color: '#00a6b2' },
        rackLevels: [], zones: [], aisles: [], readonly: false, busy: false,
      },
    })
    const inputs = wrapper.findAll('input')
    await inputs[0]!.setValue('Z-UPDATED')
    await wrapper.get('form').trigger('submit')
    expect(wrapper.emitted('saveZone')?.[0]?.[0]).toMatchObject({ zoneCode: 'Z-UPDATED', zoneType: 1, polygonJson: '[]' })
    await wrapper.get('button.danger').trigger('click')
    expect(wrapper.emitted('remove')).toHaveLength(1)
  })

  it('edits rack levels without exposing location code mutation', async () => {
    const wrapper = mount(DesignLayoutPropertiesPanel, {
      props: {
        rack: { revision: revision(rackId), zoneLogicalId: zoneId, aisleLogicalId: aisleId, rackCode: 'R-01', x: 0, y: 0, z: 0, rotationZ: 0, width: 2400, depth: 1000, height: 4000 },
        rackLevels: [{ revision: revision('44444444-4444-4444-4444-444444444444'), rackLogicalId: rackId, levelNo: 1, bottomZ: 0, clearHeight: 1600, binCount: 2, depthCount: 1, cellWidth: 1200, cellDepth: 1000, beamHeight: 100 }],
        zones: [{ logicalId: zoneId, code: 'Z-A' }], aisles: [{ logicalId: aisleId, code: 'A-01', zoneLogicalId: zoneId }], readonly: false, busy: false,
      },
    })
    expect(wrapper.text()).toContain('新增库位保持未编码')
    expect(wrapper.text()).not.toContain('编码前缀')
    await wrapper.get('form').trigger('submit')
    const payload = wrapper.emitted('saveRack')?.[0]?.[0] as Record<string, unknown>
    expect(payload).toMatchObject({ rackCode: 'R-01', zoneLogicalId: zoneId })
    expect(payload.levels).toHaveLength(1)
  })

  it('disables all mutations in readonly mode', () => {
    const wrapper = mount(DesignLayoutPropertiesPanel, {
      props: {
        aisle: { revision: revision(aisleId), zoneLogicalId: zoneId, aisleCode: 'A-01', direction: 1, polygonJson: '[]', centerlineJson: '[]' },
        rackLevels: [], zones: [{ logicalId: zoneId, code: 'Z-A' }], aisles: [], readonly: true, busy: false,
      },
    })
    expect(wrapper.get('button[type="submit"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('button.danger').attributes('disabled')).toBeDefined()
  })
})
