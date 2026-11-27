import type { CapabilityReference } from '../mes/resourceCapabilities'
import http from '../http'
import type { EngineeringEnvelope, EngineeringReference, EngineeringScope } from './engineeringProducts'
import type { DraftApplicability, RevisionDiff, RevisionOption } from './engineeringRevisions'
import type { ResourceReference } from '../mes/resourceSources'
import type { UomReference } from '../erp/uomProfiles'
import type { TimeReference } from '../erp/uomTimeBases'
export interface RoutingParameterEvidence { specification: EngineeringReference; approval: EngineeringReference; requirementId: string }
export interface RoutingParameter { name: string; value: string | null; minimum: string | null; maximum: string | null; unitRef: UomReference | null; method: string; dimensionKey?: string | null; evidence?: RoutingParameterEvidence | null }
export interface RoutingAlternative { replacesOccurrenceId: string; rule: EngineeringReference; when: RevisionOption }
export interface RoutingStep { occurrenceId: string; processCode: string; name: string; semanticOrder: number; dependsOn: string[]; alternativeGroup: string | null; applicability: DraftApplicability; reason: string; instructions: string; resources: ResourceReference[]; tools: EngineeringReference[]; parameters: RoutingParameter[]; documentRequirement: string; legacyRowId?: string | null; resourceCapabilities?: CapabilityReference[] | null; alternative?: RoutingAlternative | null }
export interface RoutingBody { name: string; revisionOrigin: EngineeringReference; productRef: EngineeringReference; siteRef: EngineeringReference | null; manufacturingViewRequirement: string; operations: RoutingStep[] }
export interface RoutingLegacy { mappingRef: EngineeringReference; productRef: EngineeringReference; digest: string; rows: unknown[]; capturedAt: string; suggestedOperations: RoutingStep[] }
export interface RoutingCatalogueObservation {
  occurrenceId: string
  reference: CapabilityReference
  timeUnit: UomReference | null
  timeBasis: TimeReference | null
  approvalOperationId: string | null
  state: 'APPROVED' | 'CATALOGUE_VERIFIED' | 'UNKNOWN'
}
export interface RoutingInputValidation { state: 'PASS' | 'BLOCK' | 'UNKNOWN'; findings: string[]; catalogues: RoutingCatalogueObservation[] }
export interface RoutingSnapshot { tenantId: string; reference: EngineeringReference; etag: string; isCurrent: boolean; body: RoutingBody; action: string; reason: string; parentVersion: string | null; actorId: string; recordedAt: string; legacy: RoutingLegacy | null; originalJson: string; missing: string[]; state: 'DRAFT'; readiness: 'DRAFT_ONLY'; inputValidation?: RoutingInputValidation | null }
export interface RoutingCommand { operationId: string; routingId: string; action: 'CREATE' | 'REVISE'; expectedVersion: string | null; expectedEtag: string | null; body: RoutingBody; reason: string; legacyMappingRef?: EngineeringReference | null; legacyDigest?: string | null }
export interface RoutingResult { operationId: string; kind: 'ENGINEERING_ROUTING'; objectId: string; version: string; digest: string }
export interface RoutingOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: RoutingResult | null }
const root = '/engineering-routing/owner-v1'
export const routingApi = {
  scope: () => http.get<any, EngineeringEnvelope<EngineeringScope>>(`${root}/scope`),
  list: (revisionId?: string, search = '', page = 1) => http.get<any, EngineeringEnvelope<RoutingSnapshot[]>>(root, { params: { revisionId, search, page } }),
  read: (id: string, version?: string) => http.get<any, EngineeringEnvelope<RoutingSnapshot>>(`${root}/${encodeURIComponent(id)}`, { params: { version } }),
  diff: (id: string, before: string, after: string) => http.get<any, EngineeringEnvelope<RevisionDiff>>(`${root}/${encodeURIComponent(id)}/diff`, { params: { before, after } }),
  legacy: (reference: EngineeringReference) => http.post<any, EngineeringEnvelope<RoutingLegacy>>(`${root}/legacy-preview`, reference),
  save: (command: RoutingCommand) => http.post<any, EngineeringEnvelope<RoutingResult>>(`${root}/commands`, command),
  operation: (id: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<RoutingOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {})
    : http.get<any, EngineeringEnvelope<RoutingOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
}
