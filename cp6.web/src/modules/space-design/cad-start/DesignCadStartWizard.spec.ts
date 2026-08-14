import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { designCadParseApi } from '@/api/space/designCadParse'
import DesignCadStartWizard from './DesignCadStartWizard.vue'

vi.mock('@/api/space/designCadParse', () => ({
  designCadParseApi: {
    getPreparationStatus: vi.fn(),
    listMappingProfiles: vi.fn(),
    previewPreparation: vi.fn(),
    start: vi.fn(),
  },
}))

describe('DesignCadStartWizard', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(designCadParseApi.listMappingProfiles).mockResolvedValue([
      {
        profileId: 'profile-1',
        version: 1,
        name: 'Warehouse profile',
        scope: 'System',
        definitionSha256: 'a'.repeat(64),
        ruleCount: 8,
      },
    ])
    vi.mocked(designCadParseApi.getPreparationStatus).mockResolvedValue({
      sourceId: 'source-1',
      sourceState: 'Ready',
      fileState: 'Clean',
      readyForPreparation: true,
    })
  })

  it('requires explicit unit, profile, preview and two confirmations before start', async () => {
    const startRequest = {
      preparationId: 'preparation-1',
      floorLogicalId: 'floor-1',
      confirmedUnit: 'Millimeter',
      confirmedScaleToMillimeters: 1,
      coordinateMetadataJson: '{}',
      coordinateTransformSha256: 'b'.repeat(64),
      mappingProfileId: 'profile-1',
      mappingProfileVersion: 1,
      mappingDefinitionSha256: 'a'.repeat(64),
      mappingPreviewSha256: 'c'.repeat(64),
    }
    vi.mocked(designCadParseApi.previewPreparation).mockResolvedValue({
      preparationId: 'preparation-1',
      baseContentRevision: 3,
      readyForParsing: true,
      coordinateAnalysis: {
        suggestedUnit: 'Millimeter',
        isSuggestedExtentPlausible: true,
        issues: [],
      },
      coordinateMetadata: {
        confirmedUnit: 'Millimeter',
        confirmedScaleToMillimeters: 1,
      },
      mappingProfile: {
        profileId: 'profile-1',
        version: 1,
        name: 'Warehouse profile',
        scope: 'System',
        definitionSha256: 'a'.repeat(64),
        ruleCount: 8,
      },
      startRequest,
    })
    vi.mocked(designCadParseApi.start).mockResolvedValue({
      jobId: 'job-1',
      status: 'Queued',
    })
    const wrapper = mount(DesignCadStartWizard, {
      props: {
        versionId: 'version-1',
        sourceId: 'source-1',
        floorLogicalId: 'floor-1',
      },
      attachTo: document.body,
    })
    await flushPromises()

    const startButton = wrapper.get('footer .primary')
    expect(startButton.attributes('disabled')).toBeDefined()
    await wrapper.get('select[aria-label="来源单位"]').setValue('Millimeter')
    await wrapper.get('select[aria-label="映射 Profile"]').setValue('profile-1:1')
    await wrapper.get('.fields > .primary').trigger('click')
    await flushPromises()

    expect(startButton.attributes('disabled')).toBeDefined()
    const confirmations = wrapper.findAll('.confirmation input')
    await confirmations[0]!.setValue(true)
    await confirmations[1]!.setValue(true)
    expect(startButton.attributes('disabled')).toBeUndefined()
    await startButton.trigger('click')
    await flushPromises()

    expect(designCadParseApi.start).toHaveBeenCalledWith(
      'version-1',
      'source-1',
      startRequest,
    )
    expect(wrapper.emitted('started')).toEqual([['job-1']])
    wrapper.unmount()
  })

  it('moves focus into the modal and supports Escape without waiting for APIs', async () => {
    const wrapper = mount(DesignCadStartWizard, {
      props: {
        versionId: 'version-1',
        sourceId: 'source-1',
        floorLogicalId: 'floor-1',
      },
      attachTo: document.body,
    })
    await flushPromises()

    expect(document.activeElement).toBe(wrapper.get('[role="dialog"]').element)
    await wrapper.get('[role="dialog"]').trigger('keydown', { key: 'Escape' })
    expect(wrapper.emitted('close')).toHaveLength(1)
    wrapper.unmount()
  })
})
