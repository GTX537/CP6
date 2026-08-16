import http from '@/api/http'
import type { CadReviewWorkspace } from '@/modules/space-design/cad-review/cadReviewWorkspace'
import type { IApplySpaceCadChangesetResponse } from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const root = '/space/design/v1'

export interface SpaceCadParse {
  jobId: string
  status: string
  sourceState: string
  lastErrorCode?: string
  lastErrorSummary?: string
  artifacts: Array<{
    artifactId: string
    artifactType: string
    sizeBytes: number
  }>
}

export interface UploadSpaceCadSourceResponse {
  file: { id: string; state: string; sha256?: string }
  source: { id: string; state: string; sha256: string }
  scanJobId?: string
  jobStatusUrl?: string
  reused: boolean
}

export interface SpaceCadMappingProfile {
  profileId: string
  version: number
  name: string
  scope: string
  definitionSha256: string
  ruleCount: number
}

export interface SpaceCadPreparationStatus {
  sourceId: string
  sourceState: string
  fileState: string
  readyForPreparation: boolean
  blockingCode?: string
}

export interface SpaceCadProviderSlot {
  providerKey: string
  providerVersion: string
  displayName: string
  role: string
  deploymentMode: string
  dataBoundary: string
  approvalEvidenceReference: string
  secretReferenceConfigured: boolean
  validFromUtc: string
  expiresAtUtc: string
  supportsDwg: boolean
  supportsDxf: boolean
  licensingApproved: boolean
  securityApproved: boolean
  dataRegionApproved: boolean
  deletionRetentionApproved: boolean
  qualificationScore?: number
  qualificationRubricVersion?: string
  goldenDatasetSha256?: string
  frozenEnvironmentSha256?: string
  qualificationEvidenceReference?: string
  qualified: boolean
  runtimeAvailable: boolean
  currentlyValid: boolean
}

export interface SpaceCadSiteCapability {
  siteId: string
  configurationRevision: number
  canPrepareCad: boolean
  cadGaReady: boolean
  primary?: SpaceCadProviderSlot
  backup?: SpaceCadProviderSlot
  blockingCodes: string[]
  evaluatedAtUtc: string
  updatedAtUtc?: string
  updatedBy?: string
}

export interface StartSpaceCadParseRequest {
  preparationId: string
  floorLogicalId: string
  confirmedUnit: string
  confirmedScaleToMillimeters: number
  coordinateMetadataJson: string
  coordinateTransformSha256: string
  mappingProfileId: string
  mappingProfileVersion: number
  mappingDefinitionSha256: string
  mappingPreviewSha256: string
}

export type SpaceCadSemanticTarget =
  | 'Wall'
  | 'Column'
  | 'Door'
  | 'Dock'
  | 'Zone'
  | 'Aisle'
  | 'Rack'
  | 'Equipment'
  | 'VerticalCirculation'
  | 'Annotation'
  | 'Guide'
  | 'RestrictedArea'

export type SpaceCadGeometryRule =
  | 'DirectGeometry'
  | 'Centerline'
  | 'ClosedBoundary'

export interface SpaceCadLayerMappingOverride {
  layerId: string
  ignore: boolean
  target?: SpaceCadSemanticTarget
  targetSubtype?: string
  geometryRule?: SpaceCadGeometryRule
  defaultHeightMillimeters?: number
  defaultThicknessMillimeters?: number
  confidenceWeight?: number
}

export interface SpaceCadLayerInventory {
  layerId: string
  name: string
  color?: string
  lineType?: string
  isVisible: boolean
  entityCount: number
  supportedEntityCount: number
  unsupportedEntityCount: number
  blockReferenceCount: number
  attributedEntityCount: number
  entityTypeCounts: Record<string, number>
  bounds?: { minX: number; minY: number; maxX: number; maxY: number }
}

export interface SpaceCadBlockInventory {
  blockId: string
  name: string
  isDefined: boolean
  isExternalReference: boolean
  definitionEntityCount: number
  referenceCount: number
  attributedReferenceCount: number
  attributes: Array<{
    name: string
    referenceCount: number
    distinctValueCount: number
  }>
  referenceBounds?: { minX: number; minY: number; maxX: number; maxY: number }
}

export interface PreviewSpaceCadPreparationResponse {
  preparationId?: string
  expiresAtUtc?: string
  baseContentRevision: number
  baseContentHash?: string
  readyForParsing: boolean
  coordinateAnalysis: {
    suggestedUnit: string
    suggestedScaleToMillimeters?: number
    isSuggestedExtentPlausible: boolean
    issues: Array<{ code: string; severity: string }>
  }
  coordinateMetadata: {
    confirmedUnit: string
    confirmedScaleToMillimeters: number
    preparedBounds?: { minX: number; minY: number; maxX: number; maxY: number }
  }
  inventorySummary?: {
    layerCount: number
    blockCount: number
    entityCount: number
    supportedEntityCount: number
    unsupportedEntityCount: number
  }
  inventory?: {
    summary: {
      layerCount: number
      emptyLayerCount: number
      blockCount: number
      undefinedBlockCount: number
      blockReferenceCount: number
      attributedBlockReferenceCount: number
      entityCount: number
      supportedEntityCount: number
      unsupportedEntityCount: number
    }
    layers: SpaceCadLayerInventory[]
    blocks: SpaceCadBlockInventory[]
  }
  mappingProfile: SpaceCadMappingProfile
  mappingPreview?: {
    layerOverrides: SpaceCadLayerMappingOverride[]
    decisions: Array<{
      sourceKind: 'Layer' | 'Block'
      sourceKey: string
      layerId?: string
      objectCount: number
      status: 'Mapped' | 'Unmapped' | 'Ignored' | 'Conflict'
      decisionSource: 'ProfileRule' | 'LayerOverride' | 'None'
      ruleId?: string
      target?: SpaceCadSemanticTarget
      targetSubtype?: string
      geometryRule?: string
      confidenceWeight?: number
    }>
    summary: {
      mappedLayerCount: number
      unmappedLayerCount: number
      conflictLayerCount: number
      mappedBlockCount: number
      unmappedBlockCount: number
      blockingCount: number
      warningCount: number
    }
  }
  semanticPreview?: {
    items: Array<{
      previewObjectId: string
      target: string
      confidence: number
      disposition: string
      isConfirmable: boolean
      source: { sourceRef: string; layerId: string; blockName?: string }
    }>
    summary: {
      autoAcceptedCount: number
      candidateCount: number
      rejectedCount: number
      blockingCount: number
      warningCount: number
    }
  }
  startRequest?: StartSpaceCadParseRequest
}

function url(versionId: string, sourceId: string, jobId: string) {
  return `${root}/versions/${versionId}/sources/${sourceId}/cad-parses/${jobId}`
}

export const designCadParseApi = {
  getCadCapability(siteId: string) {
    return http.get<unknown, SpaceCadSiteCapability>(
      `${root}/sites/${siteId}/cad-capability`,
    )
  },

  upload(versionId: string, file: File) {
    const form = new FormData()
    form.append('SourceFormat', file.name.toLowerCase().endsWith('.dwg') ? 'Dwg' : 'Dxf')
    form.append('File', file)
    return http.post<unknown, UploadSpaceCadSourceResponse>(
      `${root}/versions/${versionId}/cad-sources`,
      form,
    )
  },

  getPreparationStatus(versionId: string, sourceId: string) {
    return http.get<unknown, SpaceCadPreparationStatus>(
      `${root}/versions/${versionId}/sources/${sourceId}/cad-preparations/status`,
    )
  },

  listMappingProfiles(versionId: string) {
    return http.get<unknown, SpaceCadMappingProfile[]>(
      `${root}/versions/${versionId}/cad-mapping-profiles`,
    )
  },

  previewPreparation(
    versionId: string,
    sourceId: string,
    request: {
      floorLogicalId: string
      confirmedUnit: string
      sourceOriginInSourceUnits: { x: number; y: number }
      floorOriginMillimeters: { x: number; y: number; z: number }
      rotationZDegrees: number
      mappingProfileId: string
      mappingProfileVersion: number
      layerOverrides: SpaceCadLayerMappingOverride[]
    },
  ) {
    return http.post<unknown, PreviewSpaceCadPreparationResponse>(
      `${root}/versions/${versionId}/sources/${sourceId}/cad-preparations:preview`,
      request,
    )
  },

  start(
    versionId: string,
    sourceId: string,
    request: StartSpaceCadParseRequest,
    idempotencyKey: string = crypto.randomUUID(),
  ) {
    return http.post<unknown, { jobId: string; status: string }>(
      `${root}/versions/${versionId}/sources/${sourceId}/cad-parses`,
      request,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
  },

  get(versionId: string, sourceId: string, jobId: string) {
    return http.get<unknown, SpaceCadParse>(url(versionId, sourceId, jobId))
  },

  getReviewWorkspace(versionId: string, sourceId: string, jobId: string) {
    return http.get<unknown, CadReviewWorkspace>(
      `${url(versionId, sourceId, jobId)}/review-workspace`,
    )
  },

  applyReviewChanges(
    versionId: string,
    sourceId: string,
    jobId: string,
    request: {
      commandBatchId: string
      clientInstanceId: string
      leaseId: string
      expectedFloorRevision: number
      expectedContentRevision: number
      expectedContentHash?: string
      workspaceSha256: string
      changeIds: string[]
    },
  ) {
    return http.post<unknown, IApplySpaceCadChangesetResponse>(
      `${url(versionId, sourceId, jobId)}/review-workspace:apply`,
      request,
    )
  },

  cancel(versionId: string, sourceId: string, jobId: string) {
    return http.post(`${url(versionId, sourceId, jobId)}:cancel`)
  },

  retry(
    versionId: string,
    sourceId: string,
    jobId: string,
    idempotencyKey: string = crypto.randomUUID(),
  ) {
    return http.post<unknown, { jobId: string; status: string }>(
      `${url(versionId, sourceId, jobId)}:retry`,
      undefined,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
  },
}
