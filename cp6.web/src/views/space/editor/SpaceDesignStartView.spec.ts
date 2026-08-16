// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { designProjectApi } from '@/api/space/designProject'
import SpaceDesignStartView from './SpaceDesignStartView.vue'
import { SpaceSceneFloorDto } from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

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
    createBlankVersion: vi.fn(),
    createFloor: vi.fn(),
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

describe('SpaceDesignStartView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    Object.defineProperty(window, 'innerWidth', { value: 1440, configurable: true })
    vi.mocked(designProjectApi.getModel).mockResolvedValue(model)
    vi.mocked(designProjectApi.getVersion).mockResolvedValue(version)
    vi.mocked(designProjectApi.getFloors).mockResolvedValue([floor])
  })

  it('lists the active Draft floors and opens the selected floor', async () => {
    const wrapper = mount(SpaceDesignStartView)
    await flushPromises()

    expect(designProjectApi.getModel).toHaveBeenCalledWith('site-1')
    expect(designProjectApi.getVersion).toHaveBeenCalledWith('version-1')
    expect(wrapper.text()).toContain('Blank warehouse')
    expect(wrapper.text()).toContain('F1 · Ground floor')

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
    vi.mocked(designProjectApi.createBlankVersion).mockResolvedValue({
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
    await wrapper.get('[data-testid="create-floor"]').trigger('submit')
    await flushPromises()

    expect(designProjectApi.createBlankVersion).toHaveBeenCalledWith(
      'site-1',
      'Blank warehouse',
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
})
