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
        providerVersion: '1.0',
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
        providerVersion: '1.0',
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
        suggestedScaleToMillimeters: 1,
        sourceBounds: { minX: 0, minY: 0, maxX: 100, maxY: 50 },
        suggestedBoundsMillimeters: { minX: 0, minY: 0, maxX: 100, maxY: 50 },
        isSuggestedExtentPlausible: true,
        requiresUnitConfirmation: true,
        issues: [],
      },
      coordinateMetadata: {
        confirmedUnit: 'Millimeter',
        confirmedScaleToMillimeters: 1,
      },
      coordinateIssues: [],
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
    expect(wrapper.get('[aria-label="Site CAD Provider 能力"]').text())
      .toContain('v1.0')

    const startButton = wrapper.get('footer .primary')
    expect(startButton.attributes('disabled')).toBeDefined()
    await wrapper.get('select[aria-label="来源单位"]').setValue('Millimeter')
    await wrapper.get('select[aria-label="映射 Profile"]').setValue('profile-1:1')
    await wrapper.get('.fields > .primary').trigger('click')
    await flushPromises()

    const coordinateAnalysis = wrapper.get('[aria-label="CAD 单位、比例与范围建议"]')
    expect(coordinateAnalysis.text()).toContain('1 来源单位 = 1 mm')
    expect(coordinateAnalysis.text()).toContain('宽 100 × 高 50 来源单位')
    expect(coordinateAnalysis.text()).toContain('自动比例与图纸范围合理，仍需人工确认')
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

  it('shows layer and block inventory and submits explicit per-layer overrides', async () => {
    vi.mocked(designCadParseApi.previewPreparation).mockResolvedValue({
      baseContentRevision: 3,
      readyForParsing: false,
      coordinateAnalysis: {
        suggestedUnit: 'Millimeter',
        suggestedScaleToMillimeters: 1,
        sourceBounds: { minX: 0, minY: 0, maxX: 0.1, maxY: 0.1 },
        suggestedBoundsMillimeters: { minX: 0, minY: 0, maxX: 0.1, maxY: 0.1 },
        isSuggestedExtentPlausible: false,
        requiresUnitConfirmation: true,
        issues: [{
          code: 'SPACE_CAD_EXTENT_IMPLAUSIBLE',
          severity: 'Blocking',
          detailToken: 'below-minimum',
        }],
      },
      coordinateMetadata: {
        confirmedUnit: 'Millimeter',
        confirmedScaleToMillimeters: 1,
      },
      coordinateIssues: [
        {
          code: 'SPACE_CAD_FLOOR_BOUNDARY_EXCEEDED',
          severity: 'Blocking',
          detailToken: 'outside-target-floor',
        },
        {
          code: 'SPACE_CAD_ENTITY_FLOOR_BOUNDARY_EXCEEDED',
          severity: 'Warning',
          sourceRef: 'H:100',
          detailToken: 'outside-target-floor',
        },
      ],
      inventorySummary: {
        layerCount: 2,
        blockCount: 1,
        entityCount: 12,
        supportedEntityCount: 11,
        unsupportedEntityCount: 1,
      },
      inventory: {
        summary: {
          layerCount: 2,
          emptyLayerCount: 0,
          blockCount: 1,
          undefinedBlockCount: 0,
          blockReferenceCount: 3,
          attributedBlockReferenceCount: 2,
          entityCount: 12,
          supportedEntityCount: 11,
          unsupportedEntityCount: 1,
        },
        layers: [
          {
            layerId: 'WALL',
            name: 'WALL',
            color: 'ACI:7',
            lineType: 'CONTINUOUS',
            isVisible: true,
            entityCount: 8,
            supportedEntityCount: 8,
            unsupportedEntityCount: 0,
            blockReferenceCount: 0,
            attributedEntityCount: 0,
            entityTypeCounts: { Line: 8 },
          },
          {
            layerId: 'MISC',
            name: 'MISC',
            color: '#ff6600',
            lineType: 'DASHED',
            isVisible: false,
            entityCount: 4,
            supportedEntityCount: 3,
            unsupportedEntityCount: 1,
            blockReferenceCount: 3,
            attributedEntityCount: 2,
            entityTypeCounts: { BlockReference: 3, Unknown: 1 },
          },
        ],
        blocks: [
          {
            blockId: 'B:RACK-A',
            name: 'RACK-A',
            isDefined: true,
            isExternalReference: false,
            definitionEntityCount: 4,
            referenceCount: 3,
            attributedReferenceCount: 2,
            attributes: [{ name: 'CODE', referenceCount: 2, distinctValueCount: 2 }],
          },
        ],
      },
      mappingProfile: {
        profileId: 'profile-1',
        version: 1,
        name: 'Warehouse profile',
        scope: 'System',
        definitionSha256: 'a'.repeat(64),
        ruleCount: 8,
      },
      mappingPreview: {
        layerOverrides: [],
        decisions: [
          {
            sourceKind: 'Layer',
            sourceKey: 'WALL',
            layerId: 'WALL',
            objectCount: 8,
            status: 'Mapped',
            decisionSource: 'ProfileRule',
            ruleId: 'wall-layer',
            target: 'Wall',
            geometryRule: 'Centerline',
            confidenceWeight: .95,
          },
          {
            sourceKind: 'Layer',
            sourceKey: 'MISC',
            layerId: 'MISC',
            objectCount: 4,
            status: 'Unmapped',
            decisionSource: 'None',
          },
        ],
        summary: {
          mappedLayerCount: 1,
          unmappedLayerCount: 1,
          conflictLayerCount: 0,
          mappedBlockCount: 1,
          unmappedBlockCount: 0,
          blockingCount: 1,
          warningCount: 0,
        },
      },
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

    await wrapper.get('select[aria-label="来源单位"]').setValue('Millimeter')
    await wrapper.get('select[aria-label="映射 Profile"]').setValue('profile-1:1')
    expect(wrapper.get('select[aria-label="映射 Profile"]').text()).toContain('系统公共')
    await wrapper.get('.fields > .primary').trigger('click')
    await flushPromises()

    const inventory = wrapper.get('[aria-label="CAD 图层与块清单"]')
    expect(inventory.text()).toContain('ACI:7')
    expect(inventory.text()).toContain('CONTINUOUS')
    expect(inventory.text()).toContain('MISC · 隐藏')
    expect(inventory.text()).toContain('未映射')
    const coordinateIssues = wrapper.get('[aria-label="CAD 坐标与越界问题"]')
    expect(coordinateIssues.text()).toContain('SPACE_CAD_FLOOR_BOUNDARY_EXCEEDED')
    expect(coordinateIssues.text()).toContain('对象 H:100')
    expect(wrapper.get('[aria-label="CAD 自动单位与范围问题"]').text())
      .toContain('换算范围过小，可能选择了错误单位或比例')
    expect(wrapper.get('[aria-label="CAD 单位、比例与范围建议"]').text())
      .toContain('自动比例或图纸范围异常')
    expect(wrapper.get('.metrics .blocking').text()).toContain('阻断 2')
    await wrapper.get('.block-review summary').trigger('click')
    expect(wrapper.get('[aria-label="CAD 块清单"]').text()).toContain('RACK-A')

    await wrapper.get('select[aria-label="图层 MISC 覆盖方式"]').setValue('Zone')
    expect(wrapper.get('[role="status"]').text()).toContain('必须重新生成预览')
    expect(wrapper.get('footer .primary').attributes('disabled')).toBeDefined()
    await wrapper.get('.fields > .primary').trigger('click')
    await flushPromises()

    expect(designCadParseApi.previewPreparation).toHaveBeenLastCalledWith(
      'version-1',
      'source-1',
      expect.objectContaining({
        layerOverrides: [{
          layerId: 'MISC',
          ignore: false,
          target: 'Zone',
          geometryRule: 'ClosedBoundary',
          confidenceWeight: .95,
        }],
      }),
    )
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
