import http from '../http'
export interface EngineeringEnvelope<T> { code: number; data: T }
export interface EngineeringScope { tenantId: string; actorId: string }
export interface EngineeringReference { owner: string; id: string; version: string; digest: string }
export interface EngineeringProduct { code: string; name: string; category: string; definition: string; identificationSource: string; enabled: boolean }
export interface EngineeringVersion { tenantId: string; reference: EngineeringReference; isCurrent: boolean; product: EngineeringProduct; originalJson: string }
export interface EngineeringCreation extends EngineeringScope { authorityVersion: string; allowsUnbound: boolean; departments: { id: string; code: string; name: string }[] }
export interface EngineeringCommand { operationId: string; action: 'CREATE' | 'REVISE'; productId: string; expectedVersion: string | null; expectedAuthorityVersion: string | null; departmentId: string | null; product: EngineeringProduct }
export interface MappingCandidate { engineeringRef: EngineeringReference; businessRef: EngineeringReference | null; siteRef: EngineeringReference | null; purpose: string; evidence: string; conflicts: string; conflictsResolved: boolean | null }
export interface OwnerConfirmation { owner: string; resource: string; action: string; actorId: string; permissionVersion: string; registryVersion: string; organizationVersion: string }
export interface MappingVersion { tenantId: string; reference: EngineeringReference; state: 'DRAFT' | 'CONFIRMED' | 'DISABLED'; isCurrent: boolean; candidate: MappingCandidate; engineeringConfirmation: OwnerConfirmation | null; businessConfirmation: OwnerConfirmation | null; originalJson: string; coverage: 'IDENTITY_SITE_ONLY'; technicalApplicability: 'REQUIRED_FENCED'; legalEntityApplicability: 'REQUIRED_FENCED'; canAdoptIdentitySite: boolean; reasons: string[] }
export interface MappingCommand { operationId: string; mappingId: string; action: 'DRAFT' | 'CONFIRM' | 'DISABLE'; expectedVersion: string; candidate: MappingCandidate | null }
export interface SourceChoice { reference: EngineeringReference; code: string; name?: string; enabled?: boolean }
export type EngineeringOperationKind = 'ENGINEERING_PRODUCT' | 'ENGINEERING_MAPPING'
export interface EngineeringResult { operationId: string; kind: EngineeringOperationKind; objectId: string; version: string; digest: string }
export interface EngineeringOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: EngineeringResult | null }
const products = '/engineering-products/owner-v1', mappings = '/engineering-mappings/owner-v1'
export const engineeringApi = {
  scope: () => http.get<any, EngineeringEnvelope<EngineeringScope>>(`${products}/scope`),
  creationContext: () => http.get<any, EngineeringEnvelope<EngineeringCreation>>(`${products}/creation-context`),
  list: (search = '', page = 1) => http.get<any, EngineeringEnvelope<EngineeringVersion[]>>(products, { params: { search, page } }),
  read: (id: string, version?: string) => http.get<any, EngineeringEnvelope<EngineeringVersion>>(`${products}/${encodeURIComponent(id)}`, { params: { version } }),
  save: (body: EngineeringCommand) => http.post<any, EngineeringEnvelope<EngineeringResult>>(`${products}/commands`, body),
  mappings: (engineeringId: string, page = 1) => http.get<any, EngineeringEnvelope<MappingVersion[]>>(mappings, { params: { engineeringId, page } }),
  mapping: (id: string, version?: string) => http.get<any, EngineeringEnvelope<MappingVersion>>(`${mappings}/${encodeURIComponent(id)}`, { params: { version } }),
  saveMapping: (body: MappingCommand) => http.post<any, EngineeringEnvelope<EngineeringResult>>(`${mappings}/commands`, body),
  businessChoices: (search: string) => http.get<any, EngineeringEnvelope<SourceChoice[]>>(`${mappings}/business-choices`, { params: { search } }),
  siteChoices: (search: string) => http.get<any, EngineeringEnvelope<SourceChoice[]>>(`${mappings}/site-choices`, { params: { search } }),
  operation: (kind: EngineeringOperationKind, id: string, close: boolean) => {
    const root = kind === 'ENGINEERING_PRODUCT' ? products : mappings, path = `${root}/operations/${encodeURIComponent(id)}`
    return close ? http.post<any, EngineeringEnvelope<EngineeringOperation>>(`${path}/close`, {}) : http.get<any, EngineeringEnvelope<EngineeringOperation>>(path)
  },
}
