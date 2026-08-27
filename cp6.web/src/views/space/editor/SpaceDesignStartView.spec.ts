// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { designProjectApi } from '@/api/space/designProject'
import SpaceDesignStartView from './SpaceDesignStartView.vue'
import {
  SpaceSceneFloorDto,
  SpaceWarehouseTemplateDto,
  SpaceWarehouseTemplateInstantiationPreviewDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const { push } = vi.hoisted(() => ({ push: vi.fn() }))

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { siteId: 'site-1' } }),
  useRouter: () => ({ push }),
}))

vi.mock('@/api/space/designProject', () => ({
  designProjectApi: {
    getModel: vi.fn(),
    getVersion: vi.fn(),
    getFloors: vi.fn(),
    createVersion: vi.fn(),
    createFloor: vi.fn(),
    getWarehouseTemplates: vi.fn(),
    previewWarehouseTemplate: vi.fn(),
  },
}))

const model = {
  id: 'model-1',
  siteId: 'site-1',
  mode: 'DesignV1',
  cutoverState: 'Active',
  activeDraftVersionId: 'version-1',
  currentPublishedVersionId: 'published-1',
  rowVersion: 'rv-model',
}

const version = {
  id: 'version-1',
  modelId: 'model-1',
  siteId: 'site-1',
  versionNo: 'V2',
  name: 'Blank warehouse',
  status: 'Draft',
  contentRevision: 0,
  rowVersion: 'rv-version',
  purpose: 'Production',
  creationSource: 'Blank',
  createdBy: '00000000-0000-0000-0000-000000000001',
  createdAtUtc: new Date('2026-08-15T12:00:00Z'),
  updatedAtUtc: new Date('2026-08-15T12:30:00Z'),
  openBlockingCount: 2,
}

const floor = SpaceSceneFloorDto.fromJS({
  revision: {
    revisionId: 'floor-revision-1',
    logicalId: 'floor-1',
    lifecycleState: 'Active',
    rowVersion: 'rv-floor',
  },
  siteLogicalId: 'site-1',
  level: 1,
  floorCode: 'F1',
  name: 'Ground floor',
  elevation: 0,
  height: 6000,
  boundaryJson: '[]',
  coordinateSystem: 'LOCAL_MM_Z_UP',
  underlayOffsetX: 0,
  underlayOffsetY: 0,
  underlayRotationZ: 0,
  revisionNumber: 0,
})

const warehouseTemplate = SpaceWarehouseTemplateDto.fromJS({
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
    counts: { floors: 2, zones: 7, aisles: 20, racks: 500, locations: 10_000 },
  },
})

const warehouseTemplatePreview = SpaceWarehouseTemplateInstantiationPreviewDto.fromJS({
  schemaVersion: 1,
  templateId: 'template-1',
  templateVersionId: 'template-version-1',
  templateContentHash: 'a'.repeat(64),
  proposalHash: 'b'.repeat(64),
  counts: { floors: 2, zones: 7, aisles: 20, racks: 500, locations: 10_000 },
  floors: [],
  zones: [],
  aisles: [],
  racks: [],
  writesDraft: false,
})

describe('SpaceDesignStartView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    Object.defineProperty(window, 'innerWidth', { value: 1440, configurable: true })
    vi.mocked(designProjectApi.getModel).mockResolvedValue(model)
    vi.mocked(designProjectApi.getVersion).mockResolvedValue(version)
    vi.mocked(designProjectApi.getFloors).mockResolvedValue([floor])
    vi.mocked(designProjectApi.getWarehouseTemplates)
      .mockResolvedValue([warehouseTemplate])
    vi.mocked(designProjectApi.previewWarehouseTemplate)
      .mockResolvedValue(warehouseTemplatePreview)
  })

  it('lists the active Draft floors and opens the selected floor', async () => {
    const wrapper = mount(SpaceDesignStartView)
    await flushPromises()

    expect(designProjectApi.getModel).toHaveBeenCalledWith('site-1')
    expect(designProjectApi.getVersion).toHaveBeenCalledWith('version-1')
    expect(wrapper.text()).toContain('Blank warehouse')
    expect(wrapper.text()).toContain('F1 · Ground floor')
    expect(wrapper.text()).toContain('空白')
    expect(wrapper.text()).toContain('00000000-0000-0000-0000-000000000001')
    expect(wrapper.text()).toContain('Blocking')
    expect(wrapper.text()).toContain('2')

    await wrapper.get('[data-floor-id="floor-1"]').trigger('click')
    expect(push).toHaveBeenCalledWith({
      name: 'space-design-underlay',
      params: { versionId: 'version-1', floorLogicalId: 'floor-1' },
    })
  })

  it('creates a Blank Draft then requires explicit floor measurements', async () => {
    vi.mocked(designProjectApi.getModel).mockResolvedValue({
      ...model,
      activeDraftVersionId: undefined,
    })
    vi.mocked(designProjectApi.getVersion).mockResolvedValue(version)
    vi.mocked(designProjectApi.getFloors).mockResolvedValue([])
    vi.mocked(designProjectApi.createVersion).mockResolvedValue({
      id: 'version-1',
      siteId: 'site-1',
      versionNo: 'V2',
      status: 'Draft',
      rowVersion: 'rv-version',
      jobId: 'job-1',
      jobStatusUrl: '/jobs/job-1',
      idempotentReplay: false,
    })
    vi.mocked(designProjectApi.createFloor).mockResolvedValue({
      floor,
      versionContentRevision: 1,
      idempotentReplay: false,
    })

    const wrapper = mount(SpaceDesignStartView)
    await flushPromises()
    await wrapper.get('[data-testid="draft-name"]').setValue('Blank warehouse')
    await wrapper.get('[data-testid="create-draft"]').trigger('submit')
    await flushPromises()

    await wrapper.get('[data-testid="floor-code"]').setValue('F1')
    await wrapper.get('[data-testid="floor-name"]').setValue('Ground floor')
    await wrapper.get('[data-testid="floor-level"]').setValue('1')
    await wrapper.get('[data-testid="floor-elevation"]').setValue('0')
    await wrapper.get('[data-testid="floor-height"]').setValue('6000')
    await wrapper.get('[data-testid="floor-width"]').setValue('120000')
    await wrapper.get('[data-testid="floor-depth"]').setValue('80000')
    await wrapper.get('[data-testid="create-floor"]').trigger('submit')
    await flushPromises()

    expect(designProjectApi.createVersion).toHaveBeenCalledWith(
      'site-1',
      {
        name: 'Blank warehouse',
        basedOnVersionId: undefined,
        createMode: 'Blank',
        templateId: undefined,
        templateVersionId: undefined,
        templateProposalHash: undefined,
      },
    )
    expect(designProjectApi.createFloor).toHaveBeenCalledWith(
      'version-1',
      {
        floorCode: 'F1',
        name: 'Ground floor',
        level: 1,
        elevation: 0,
        height: 6000,
        expectedContentRevision: 0,
        width: 120000,
        depth: 80000,
      },
    )
    expect(push).toHaveBeenCalledWith({
      name: 'space-design-underlay',
      params: { versionId: 'version-1', floorLogicalId: 'floor-1' },
    })
  })

  it('keeps creation read-only below 1280 pixels', async () => {
    Object.defineProperty(window, 'innerWidth', { value: 1024, configurable: true })
    vi.mocked(designProjectApi.getModel).mockResolvedValue({
      ...model,
      activeDraftVersionId: undefined,
    })

    const wrapper = mount(SpaceDesignStartView)
    await flushPromises()

    expect(wrapper.find('[data-testid="narrow-notice"]').exists()).toBe(true)
    expect(wrapper.get('[data-testid="create-draft"]').attributes('disabled'))
      .toBeDefined()
  })

  it('previews a system warehouse template without writing a Draft', async () => {
    vi.mocked(designProjectApi.getModel).mockResolvedValue({
      ...model,
      activeDraftVersionId: undefined,
    })

    const wrapper = mount(SpaceDesignStartView)
    await flushPromises()

    expect(wrapper.text()).toContain('CP6 标准货架仓')
    await wrapper.get('[data-template-id="template-1"]').trigger('click')
    await flushPromises()

    expect(designProjectApi.previewWarehouseTemplate).toHaveBeenCalledWith(
      'template-1',
      'template-version-1',
    )
    expect(wrapper.text()).toContain('预览已密封，未写入 Draft')
    expect(wrapper.text()).toContain('10000 库位')
    expect(wrapper.text()).toContain('b'.repeat(64))
  })

  it('creates a complete Draft from the sealed system template mode', async () => {
    vi.mocked(designProjectApi.getModel).mockResolvedValue({
      ...model,
      activeDraftVersionId: undefined,
    })
    vi.mocked(designProjectApi.createVersion).mockResolvedValue({
      id: 'version-1',
      siteId: 'site-1',
      versionNo: 'V2',
      status: 'Draft',
      rowVersion: 'rv-version',
      jobId: 'job-1',
      jobStatusUrl: '/jobs/job-1',
      idempotentReplay: false,
    })

    const wrapper = mount(SpaceDesignStartView)
    await flushPromises()
    await wrapper.get('[data-testid="create-mode"]').setValue('SystemTemplate')
    await wrapper.get('[data-testid="create-template-select"]').setValue('template-1')
    await wrapper.get('[data-testid="seal-create-template"]').trigger('click')
    await flushPromises()
    await wrapper.get('[data-testid="draft-name"]').setValue('System warehouse')
    await wrapper.get('[data-testid="create-draft"]').trigger('submit')
    await flushPromises()

    expect(designProjectApi.createVersion).toHaveBeenCalledWith(
      'site-1',
      {
        name: 'System warehouse',
        basedOnVersionId: undefined,
        createMode: 'SystemTemplate',
        templateId: 'template-1',
        templateVersionId: 'template-version-1',
        templateProposalHash: 'b'.repeat(64),
      },
    )
  })

  it('keeps Blank creation available when the template catalog fails', async () => {
    vi.mocked(designProjectApi.getModel).mockResolvedValue({
      ...model,
      activeDraftVersionId: undefined,
    })
    vi.mocked(designProjectApi.getWarehouseTemplates)
      .mockRejectedValue(new Error('catalog unavailable'))

    const wrapper = mount(SpaceDesignStartView)
    await flushPromises()

    expect(wrapper.text()).toContain('模板目录暂不可用')
    expect(wrapper.text()).toContain('空白 Draft 创建仍可继续')
    expect(wrapper.get('[data-testid="create-draft"]').attributes('disabled'))
      .toBeUndefined()
  })
})
