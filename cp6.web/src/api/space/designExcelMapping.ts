import http from '../http'

const root = '/space/design/v1/mapping-profiles/excel'

export interface SpaceExcelEnumConversion {
  sourceValue: string
  targetValue: string
}

export interface SpaceExcelColumnMapping {
  targetField: string
  sourceHeader?: string | null
  sourceColumn?: string | null
  dataType: 'Text' | 'Integer' | 'Decimal'
  format?: string | null
  defaultValue?: string | null
  isBusinessKey: boolean
  referenceTarget?: string | null
  enumConversions?: SpaceExcelEnumConversion[] | null
  unitConversionMultiplier?: number | null
}

export interface SpaceExcelSheetMapping {
  targetSheet: string
  sourceSheet: string
  sheetMatchMode: 'Exact' | 'Wildcard'
  headerRow: number
  dataStartRow: number
  columns: SpaceExcelColumnMapping[]
}

export interface SpaceExcelMappingDefinition {
  schemaVersion: number
  unknownColumnPolicy: 'Ignore' | 'Warning' | 'Reject'
  emptyValuePolicy: 'Reject' | 'UseDefault' | 'KeepEmpty'
  duplicateRowPolicy: 'Reject' | 'KeepFirst' | 'KeepLast'
  sheets: SpaceExcelSheetMapping[]
}

export interface SpaceExcelMappingProfile {
  id: string
  name: string
  scope: 'System' | 'Tenant'
  version: number
  isReadOnly: boolean
  definitionHash: string
  definition: SpaceExcelMappingDefinition
  basedOnProfileId?: string | null
  basedOnVersion?: number | null
  rowVersion?: string | null
  createdAtUtc?: string | null
  createdBy?: string | null
}

export interface SpaceExcelHeaderSample {
  sheetName: string
  headers: string[]
}

export interface SpaceExcelColumnPreview {
  targetField: string
  required: boolean
  sourceHeader?: string | null
  sourceColumn?: string | null
  sourceColumnIndex?: number | null
  status: string
}

export interface SpaceExcelSheetPreview {
  targetSheet: string
  sourceSheetPattern: string
  matchedSourceSheet?: string | null
  status: string
  columns: SpaceExcelColumnPreview[]
  unknownHeaders: string[]
}

export interface SpaceExcelMappingIssue {
  code: string
  severity: 'Error' | 'Warning'
  sheet?: string | null
  column?: string | null
  message: string
  fixHint: string
}

export interface SpaceExcelMappingPreview {
  canSave: boolean
  normalizedDefinition: SpaceExcelMappingDefinition
  sheets: SpaceExcelSheetPreview[]
  issues: SpaceExcelMappingIssue[]
}

export interface SaveSpaceExcelMappingProfileRequest {
  profileId?: string | null
  name: string
  definition: SpaceExcelMappingDefinition
  expectedRowVersion?: string | null
  copyFromProfileId?: string | null
  copyFromVersion?: number | null
}

export interface SaveSpaceExcelMappingProfileResponse {
  profile: SpaceExcelMappingProfile
  created: boolean
  idempotentReplay: boolean
}

export const designExcelMappingApi = {
  listProfiles() {
    return http.get<unknown, SpaceExcelMappingProfile[]>(root)
  },
  getProfile(profileId: string, version?: number) {
    return http.get<unknown, SpaceExcelMappingProfile>(
      `${root}/${encodeURIComponent(profileId)}`,
      { params: version === undefined ? undefined : { version } },
    )
  },
  preview(definition: SpaceExcelMappingDefinition, workbook: SpaceExcelHeaderSample[]) {
    return http.post<unknown, SpaceExcelMappingPreview>(`${root}/preview`, {
      definition,
      workbook,
    })
  },
  save(request: SaveSpaceExcelMappingProfileRequest, idempotencyKey: string) {
    return http.post<unknown, SaveSpaceExcelMappingProfileResponse>(root, request, {
      headers: { 'Idempotency-Key': idempotencyKey },
    })
  },
}
