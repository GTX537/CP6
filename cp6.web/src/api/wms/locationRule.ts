import http from '../http'
import type { EngineeringEnvelope, EngineeringReference } from '../plm/engineeringProducts'
import type { RuleReferencePolicy, RuleReferenceBinding } from './locationRuleReferences'
export interface RuleCodeRef { id: string; version: string }
export type RuleCapacity = { mode: 'LIMITED'; value: string; metricRef: RuleCodeRef; uomRef: RuleCodeRef; precisionPolicyRef: RuleCodeRef } | { mode: 'UNLIMITED' } | { mode: 'UNKNOWN'; reason: string }
export interface RuleDraft { selectorKey: { ruleFamily: string; metricId: string; applicabilityDomainRef: RuleCodeRef }; capacity: RuleCapacity; allowedProductTypes: string[]; allowedOperations: string[]; isPickable: boolean; validFrom: string | null; validTo: string | null }
export type RulePatch = Partial<{ [K in Exclude<keyof RuleDraft, 'selectorKey'>]: { op: 'SET'; value: NonNullable<RuleDraft[K]> } | (K extends 'validFrom' | 'validTo' ? { op: 'CLEAR' } : never) }>
export interface RuleScope { tenantId: string; actorId: string; locationId: string }
export interface RuleContext { scope: RuleScope; locationCode: string; initializationState: string; fields: { field: string; canRead: boolean; canWrite: boolean }[]; selectors: { id: string; ruleFamily: string; metricId: string; applicabilityDomainId: string; revision: string; currentRuleVersionId: string | null }[]; referenceAuthorities: string; applicationCapability: string; admissionGrant: false; warehouseId?: string; referencePolicy?: EngineeringReference | null; referenceCatalog?: RuleReferencePolicy | null; referenceAuthorityBindings?: { field: string; owner: string; registryVersion: string; availability: string }[] }
export interface RuleView { reference: EngineeringReference; locationId: string; selectorId: string; state: 'DRAFT' | 'WITHDRAWN' | 'SUBMITTED' | 'RETURNED' | 'REJECTED' | 'APPROVED_PENDING'; etag: string; candidate: Partial<RuleDraft>; redactedFields: string[]; originalJson: string | null; referenceResolution: 'UNRESOLVED' | 'RESOLVED'; referenceBinding?: RuleReferenceBinding | null; approval: string; admissionGrant: false }
export interface RuleResult { operationId: string; locationId: string; reference: EngineeringReference; selectorId: string; etag: string; state: 'DRAFT' | 'WITHDRAWN' | 'SUBMITTED' | 'RETURNED' | 'REJECTED' | 'APPROVED_PENDING'; effect: 'CREATED' | 'UPDATED' | 'NO_CHANGE' | 'WITHDRAWN' | 'RESOLVED' }
export interface RuleOperation { operationId: string; locationId: string; state: 'COMMITTED' | 'UNKNOWN' | 'CLOSED'; result?: RuleResult | null }
const root = (id: string) => `/wms/locations/${encodeURIComponent(id)}/rules-v1`
export const locationRuleApi = {
  scope: (id: string) => http.get<any, EngineeringEnvelope<RuleScope>>(`${root(id)}/scope`),
  context: (id: string) => http.get<any, EngineeringEnvelope<RuleContext>>(`${root(id)}/context`),
  list: (id: string, page: number) => http.get<any, EngineeringEnvelope<RuleView[]>>(root(id), { params: { page } }),
  read: (id: string, proposal: string, version?: string) => http.get<any, EngineeringEnvelope<RuleView>>(`${root(id)}/${encodeURIComponent(proposal)}`, { params: { version } }),
  create: (id: string, operationId: string, draft: RuleDraft) => http.post<any, EngineeringEnvelope<RuleResult>>(root(id), { operationId, draft }),
  patch: (id: string, proposal: string, etag: string, operationId: string, patch: RulePatch) => http.patch<any, EngineeringEnvelope<RuleResult>>(`${root(id)}/${encodeURIComponent(proposal)}`, { operationId, patch }, { headers: { 'If-Match': etag } }),
  withdraw: (id: string, proposal: string, etag: string, operationId: string, reason: string) => http.post<any, EngineeringEnvelope<RuleResult>>(`${root(id)}/${encodeURIComponent(proposal)}/withdraw`, { operationId, reason }, { headers: { 'If-Match': etag } }),
  resolve: (id: string, proposal: string, etag: string, operationId: string, expectedPolicy: EngineeringReference) => http.post<any, EngineeringEnvelope<RuleResult>>(`${root(id)}/${encodeURIComponent(proposal)}/resolve`, { operationId, expectedPolicy }, { headers: { 'If-Match': etag } }),
  operation: (id: string, operation: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<RuleOperation>>(`${root(id)}/operations/${encodeURIComponent(operation)}/close`, {}) : http.get<any, EngineeringEnvelope<RuleOperation>>(`${root(id)}/operations/${encodeURIComponent(operation)}`),
}
