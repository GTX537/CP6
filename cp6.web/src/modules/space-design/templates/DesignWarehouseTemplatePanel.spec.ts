// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import DesignWarehouseTemplatePanel from './DesignWarehouseTemplatePanel.vue'
import {
  SpaceWarehouseTemplateDto,
  SpaceWarehouseTemplateInstantiationPreviewDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const template = SpaceWarehouseTemplateDto.fromJS({
  id: 'template-1',
  scope: 'System',
  templateCode: 'SPACE-STANDARD-01',
  name: 'CP6 标准货架仓',
  description: 'Standard warehouse',
  status: 'Active',
  latestVersion: {
    id: 'template-version-1',
    versionNo: 1,
    schemaVersion: 1,
    contentHash: 'a'.repeat(64),
    status: 'Ready',
    counts: { floors: 2, zones: 2, aisles: 2, racks: 2, locations: 40 },
  },
})

const tenantTemplate = SpaceWarehouseTemplateDto.fromJS({
  id: 'tenant-template-1',
  scope: 'Tenant',
  templateCode: 'PRIVATE-01',
  name: '华东仓私有模板',
  description: 'Tenant warehouse',
  status: 'Active',
  latestVersion: {
    id: 'tenant-template-version-1',
    versionNo: 1,
    schemaVersion: 1,
    contentHash: 'c'.repeat(64),
    status: 'Ready',
    counts: { floors: 1, zones: 1, aisles: 1, racks: 1, locations: 20 },
  },
})

const preview = SpaceWarehouseTemplateInstantiationPreviewDto.fromJS({
  schemaVersion: 1,
  templateId: 'template-1',
  templateVersionId: 'template-version-1',
  templateContentHash: 'a'.repeat(64),
  proposalHash: 'b'.repeat(64),
  counts: { floors: 2, zones: 2, aisles: 2, racks: 2, locations: 40 },
  floors: [
    { key: 'floor-1', floorCode: 'F1', name: '一层', level: 1, elevation: 0, width: 100, depth: 80, height: 6 },
    { key: 'floor-2', floorCode: 'F2', name: '二层', level: 2, elevation: 6, width: 100, depth: 80, height: 6 },
  ],
  zones: [
    { key: 'zone-1', floorKey: 'floor-1' },
    { key: 'zone-2', floorKey: 'floor-2' },
  ],
  aisles: [
    { key: 'aisle-1', floorKey: 'floor-1' },
    { key: 'aisle-2', floorKey: 'floor-2' },
  ],
  racks: [
    { key: 'rack-1', floorKey: 'floor-1', columns: 4, levels: 5, depths: 1 },
    { key: 'rack-2', floorKey: 'floor-2', columns: 4, levels: 5, depths: 1 },
  ],
  writesDraft: false,
})

describe('DesignWarehouseTemplatePanel', () => {
  it('previews and selects the floor matching the current Draft floor code', async () => {
    const wrapper = mount(DesignWarehouseTemplatePanel, {
      props: { templates: [template], preview: null, currentFloorCode: 'F2' },
    })

    await wrapper.get('[data-testid="warehouse-template-preview"]').trigger('click')
    expect(wrapper.emitted('preview')?.[0]?.[0]).toEqual(template)

    await wrapper.setProps({ preview })
    expect(wrapper.text()).toContain('F2 · 二层')
    expect(wrapper.text()).toContain('20 库位')
    expect((wrapper.get('input[value="floor-2"]').element as HTMLInputElement).checked)
      .toBe(true)
  })

  it('emits only the sealed template and selected floor identity for Apply', async () => {
    const wrapper = mount(DesignWarehouseTemplatePanel, {
      props: { templates: [template], preview, currentFloorCode: 'F1' },
    })

    await wrapper.get('input[value="floor-2"]').setValue(true)
    await wrapper.get('[data-testid="warehouse-template-apply"]').trigger('click')

    expect(wrapper.emitted('apply')?.[0]?.[0]).toEqual({
      templateId: 'template-1',
      templateFloorKey: 'floor-2',
    })
  })

  it('disables Apply in read-only mode without blocking preview', () => {
    const wrapper = mount(DesignWarehouseTemplatePanel, {
      props: { templates: [template], preview, readonly: true },
    })

    expect(wrapper.get('[data-testid="warehouse-template-preview"]')
      .attributes('disabled')).toBeUndefined()
    expect(wrapper.get('[data-testid="warehouse-template-apply"]')
      .attributes('disabled')).toBeDefined()
  })

  it('freezes the sealed choice while an idempotent retry is pending', () => {
    const wrapper = mount(DesignWarehouseTemplatePanel, {
      props: {
        templates: [template],
        preview,
        retryPending: true,
        pendingFloorKey: 'floor-1',
      },
    })

    expect(wrapper.get('[data-testid="warehouse-template-preview"]')
      .attributes('disabled')).toBeDefined()
    expect(wrapper.get('input[value="floor-2"]')
      .attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-testid="warehouse-template-apply"]').text())
      .toContain('安全重试')
  })

  it('labels tenant templates and hides a preview sealed for another template', async () => {
    const wrapper = mount(DesignWarehouseTemplatePanel, {
      props: { templates: [template, tenantTemplate], preview },
    })

    expect(wrapper.text()).toContain('租户私有 · 华东仓私有模板')
    await wrapper.get('[data-testid="warehouse-template-select"]')
      .setValue('tenant-template-1')
    expect(wrapper.find('.seal').exists()).toBe(false)
    expect(wrapper.find('[data-testid="warehouse-template-apply"]').exists())
      .toBe(false)

    await wrapper.get('[data-testid="warehouse-template-preview"]').trigger('click')
    expect(wrapper.emitted('preview')?.at(-1)?.[0]).toEqual(tenantTemplate)
  })
})
