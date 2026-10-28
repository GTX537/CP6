import http from '../http'
import type { EngineeringEnvelope, EngineeringReference, OwnerConfirmation } from '../plm/engineeringProducts'
export type IdentityRef = EngineeringReference
export interface IdentityScope { tenantId: string; actorId: string; environmentId: string; evidenceMode: string }
export interface IdentityCreation { tenantId: string; actorId: string; authorityVersion: string; allowsUnbound: boolean; departments: { id: string; code: string; name: string }[] }
export interface IdentityParty { kind: string; legalName: string | null; jurisdiction: string | null; registrationNumber: string | null; customer: IdentityRef | null }
export interface IdentityProduct { product: IdentityRef; unitBinding: IdentityRef; mapping: IdentityRef; technicalKind: string; baseline: IdentityRef | null; technicalBasis: string; party: IdentityRef | null; purpose: IdentityRef | null }
export interface IdentityDefinition { kind: 'PARTY' | 'PURPOSE' | 'PRODUCT'; code: string; name: string; basis: string; party: IdentityParty | null; purpose: { category: string; meaning: string } | null; product: IdentityProduct | null }
export interface IdentitySource { path: string; reference: IdentityRef; originalJson: string }
export interface IdentityView { reference: IdentityRef; original: { environmentId: string; tenantId: string; evidenceMode: string; version: string; authorId: string; actorId: string; state: string; definition: IdentityDefinition; sources: IdentitySource[]; confirmations: OwnerConfirmation[] }; originalJson: string; isCurrent: boolean; canSelect: boolean; reasons: string[] }
export interface IdentityCommand { operationId: string; id: string; action: 'SAVE' | 'APPROVE' | 'DISABLE'; expectedVersion: string; definition: IdentityDefinition | null; reason: string; expectedAuthorityVersion: string | null; departmentId: string | null }
export interface IdentityResult { operationId: string; objectId: string; version: string; digest: string; changed: boolean }
export interface IdentityOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: IdentityResult | null }
export interface IdentityCatalogue { catalogueRef: IdentityRef; option: { productRef: IdentityRef; label: string; baseUomRef: { id: string; version: string }; precisionPolicyRef: { id: string; version: string }; technicalIdentity: { kind: string; revisionRef: IdentityRef | null; applicabilityRef: IdentityRef | null }; originPurposeRef: { id: string; version: string }; owner: { kind: string; reference: IdentityRef } }; sources: IdentitySource[]; physicalEligibility: string }
export interface CustomerIdentity { reference: IdentityRef; source: { bpCd: string; bpName: string | null; customerFlg: boolean; isDeleted: boolean; isFrozen: boolean }; projection: string; isCurrent: boolean }
const root = '/inventory-identities/owner-v1'
export const inventoryIdentityApi = {
  scope: () => http.get<any, EngineeringEnvelope<IdentityScope>>(`${root}/scope`),
  creation: () => http.get<any, EngineeringEnvelope<IdentityCreation>>(`${root}/creation-context`),
  list: (kind: string, search = '', page = 1) => http.get<any, EngineeringEnvelope<IdentityView[]>>(root, { params: { kind, search, page } }),
  read: (id: string, version?: string) => http.get<any, EngineeringEnvelope<IdentityView>>(`${root}/${encodeURIComponent(id)}`, { params: { version } }),
  save: (c: IdentityCommand) => http.post<any, EngineeringEnvelope<IdentityResult>>(`${root}/commands`, c),
  operation: (id: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<IdentityOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {}) : http.get<any, EngineeringEnvelope<IdentityOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
  catalogue: (r: IdentityRef) => http.post<any, EngineeringEnvelope<IdentityCatalogue>>(`${root}/catalogue`, r),
  customers: (code = '', page = 1) => http.get<any, EngineeringEnvelope<{ items: CustomerIdentity[]; total: number }>>('/business-partners/identity-owner-v1', { params: { code: code || undefined, page, pageSize: 20, customersOnly: true } }),
}
