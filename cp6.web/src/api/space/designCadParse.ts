import http from '@/api/http'
import type { CadReviewWorkspace } from '@/modules/space-design/cad-review/cadReviewWorkspace'

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
  source: { id: string; state: string; sha256: string }
  scanJobId?: string
  jobStatusUrl?: string
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
  mappingProfile: SpaceCadMappingProfile
  mappingPreview?: {
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
      layerOverrides: unknown[]
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
    return http.post<unknown, {
      commandBatchId: string
      floorRevision: number
      versionContentRevision: number
      appliedChangeCount: number
      workspaceSha256: string
      idempotentReplay: boolean
    }>(`${url(versionId, sourceId, jobId)}/review-workspace:apply`, request)
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
