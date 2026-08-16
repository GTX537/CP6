import http from '../http'
import type {
  SpaceCadGeometryRule,
  SpaceCadSemanticTarget,
} from './designCadParse'

const root = '/space/design/v1/mapping-profiles/cad'

export type SpaceCadMappingSourceKind = 'Layer' | 'Block'
export type SpaceCadMappingMatchKind = 'Exact' | 'Glob' | 'Regex'

export interface SpaceCadMappingRule {
  ruleId: string
  priority: number
  sourceKind: SpaceCadMappingSourceKind
  matchKind: SpaceCadMappingMatchKind
  pattern: string
  attributeName?: string | null
  attributeMatchKind?: SpaceCadMappingMatchKind | null
  attributePattern?: string | null
  target: SpaceCadSemanticTarget
  targetSubtype?: string | null
  geometryRule: SpaceCadGeometryRule
  defaultHeightMillimeters?: number | null
  defaultThicknessMillimeters?: number | null
  confidenceWeight: number
  isRequired: boolean
}

export interface SpaceCadMappingProfileDetail {
  id: string
  name: string
  scope: 'System' | 'Tenant'
  version: number
  isReadOnly: boolean
  isEnabled: boolean
  definitionSha256: string
  rules: SpaceCadMappingRule[]
  basedOnProfileId?: string | null
  basedOnVersion?: number | null
  rowVersion?: string | null
  createdAtUtc?: string | null
  createdBy?: string | null
}

export interface SaveSpaceCadMappingProfileRequest {
  profileId?: string | null
  name: string
  isEnabled: boolean
  rules: SpaceCadMappingRule[]
  expectedRowVersion?: string | null
  copyFromProfileId?: string | null
  copyFromVersion?: number | null
}

export interface SaveSpaceCadMappingProfileResponse {
  profile: SpaceCadMappingProfileDetail
  created: boolean
  idempotentReplay: boolean
}

export const designCadMappingProfileApi = {
  listProfiles() {
    return http.get<unknown, SpaceCadMappingProfileDetail[]>(root)
  },
  getProfile(profileId: string, version?: number) {
    return http.get<unknown, SpaceCadMappingProfileDetail>(
      `${root}/${encodeURIComponent(profileId)}`,
      { params: version === undefined ? undefined : { version } },
    )
  },
  save(request: SaveSpaceCadMappingProfileRequest, idempotencyKey: string) {
    return http.post<unknown, SaveSpaceCadMappingProfileResponse>(root, request, {
      headers: { 'Idempotency-Key': idempotencyKey },
    })
  },
}
