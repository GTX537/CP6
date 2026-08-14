import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { designCadParseApi } from '@/api/space/designCadParse'
import DesignCadStartWizard from './DesignCadStartWizard.vue'

vi.mock('@/api/space/designCadParse', () => ({
  designCadParseApi: {
    getCadCapability: vi.fn(),
    getPreparationStatus: vi.fn(),
    listMappingProfiles: vi.fn(),
    previewPreparation: vi.fn(),
    start: vi.fn(),
  },
}))

describe('DesignCadStartWizard', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(designCadParseApi.getCadCapability).mockResolvedValue({
      siteId: 'site-1',
      configurationRevision: 4,
      canPrepareCad: true,
      cadGaReady: true,
      primary: {
        providerKey: 'primary',
        displayName: 'Primary CAD',
        role: 'Primary',
        deploymentMode: 'OnPremisesIsolatedWorker',
        dataBoundary: 'SiteLocal',
        approvalEvidenceReference: 'evidence-primary',
        secretReferenceConfigured: false,
        validFromUtc: '2026-01-01T00:00:00Z',
        expiresAtUtc: '2027-01-01T00:00:00Z',
        supportsDwg: true,
        supportsDxf: true,
        licensingApproved: true,
        securityApproved: true,
        dataRegionApproved: true,
        deletionRetentionApproved: true,
        qualificationScore: 92,
        qualificationRubricVersion: 'cad-ga-v1',
        goldenDatasetSha256: 'd'.repeat(64),
        frozenEnvironmentSha256: 'e'.repeat(64),
        qualificationEvidenceReference: 'evidence-qualification-primary',
        qualified: true,
        runtimeAvailable: true,
        currentlyValid: true,
      },
      backup: {
        providerKey: 'backup',
        displayName: 'Backup CAD',
        role: 'Backup',
        deploymentMode: 'ApprovedCloudService',
        dataBoundary: 'CustomerApprovedCloudRegion',
        approvalEvidenceReference: 'evidence-backup',
        secretReferenceConfigured: true,
        validFromUtc: '2026-01-01T00:00:00Z',
        expiresAtUtc: '2027-01-01T00:00:00Z',
        supportsDwg: true,
        supportsDxf: true,
        licensingApproved: true,
        securityApproved: true,
        dataRegionApproved: true,
        deletionRetentionApproved: true,
        qualificationScore: 86,
        qualificationRubricVersion: 'cad-ga-v1',
        goldenDatasetSha256: 'd'.repeat(64),
        frozenEnvironmentSha256: 'e'.repeat(64),
        qualificationEvidenceReference: 'evidence-qualification-backup',
        qualified: true,
        runtimeAvailable: true,
        currentlyValid: true,
      },
      blockingCodes: [],
      evaluatedAtUtc: '2026-08-14T00:00:00Z',
    })
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
        siteId: 'site-1',
        versionId: 'version-1',
        sourceId: 'source-1',
        floorLogicalId: 'floor-1',
      },
      attachTo: document.body,
    })
    await flushPromises()

    expect(wrapper.get('[aria-label="Site CAD Provider 能力"]').text())
      .toContain('Primary CAD')
    expect(wrapper.get('[aria-label="Site CAD Provider 能力"]').text())
      .toContain('Backup CAD')

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
        siteId: 'site-1',
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

  it('keeps preview disabled when the Site has no approved runtime provider', async () => {
    vi.mocked(designCadParseApi.getCadCapability).mockResolvedValue({
      siteId: 'site-1',
      configurationRevision: 0,
      canPrepareCad: false,
      cadGaReady: false,
      blockingCodes: ['CAD_PRIMARY_PROVIDER_MISSING'],
      evaluatedAtUtc: '2026-08-14T00:00:00Z',
    })
    const wrapper = mount(DesignCadStartWizard, {
      props: {
        siteId: 'site-1',
        versionId: 'version-1',
        sourceId: 'source-1',
        floorLogicalId: 'floor-1',
      },
    })
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain('没有可用且有效')
    expect(wrapper.get('.fields > .primary').attributes('disabled')).toBeDefined()
    expect(designCadParseApi.getPreparationStatus).not.toHaveBeenCalled()
  })
})
