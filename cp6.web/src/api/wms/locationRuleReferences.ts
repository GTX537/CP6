import http from '../http'
import type { EngineeringEnvelope, EngineeringReference } from '../plm/engineeringProducts'
import type { UomReference } from '../erp/uomProfiles'
export interface RuleMetric { id: string; dimensionKey: string; units: UomReference[] }
export interface RulePrecision { id: string; scale: number; rounding: 'REJECT' }
export interface RuleDomain { id: string; ruleFamilies: string[]; metricIds: string[]; precisionIds: string[]; productTypes: string[]; operations: string[]; allowEmptyProducts: boolean; allowEmptyOperations: boolean; allowUnlimited: boolean; allowOpenEnded: boolean; allowPickable: boolean }
export interface RuleDefinition { enabled: boolean; metrics: RuleMetric[]; precisions: RulePrecision[]; domains: RuleDomain[] }
export interface RuleUnit { reference: UomReference; baseReference: UomReference; code: string; dimensionKey: string }
export interface RuleReferencePolicy { reference: EngineeringReference; original: { tenantId: string; id: string; warehouseId: string; version: string; actorId: string; definition: RuleDefinition; units: RuleUnit[]; reason: string }; originalJson: string }
export interface RuleReferenceContext { tenantId: string; actorId: string; warehouseId: string; warehouseCode: string; version: string; policy: RuleReferencePolicy | null; qualification: 'CONFIGURATION_ONLY'; admissionGrant: false }
export interface RuleReferenceCommand { operationId: string; expectedVersion: string; definition: RuleDefinition; reason: string }
export interface RuleReferenceResult { operationId: string; warehouseId: string; reference: EngineeringReference; changed: boolean }
export interface RuleReferenceOperation { operationId: string; warehouseId: string; state: 'COMMITTED' | 'UNKNOWN' | 'CLOSED'; result?: RuleReferenceResult | null }
export interface RuleReferenceBinding { policy: EngineeringReference; registryVersion: string; fields: { field: string; owner: string; id: string; version: string; registryVersion: string }[]; units: RuleUnit[]; requiresCurrentRecheck: true; admissionGrant: false }
const root = (w: string) => `/wms/warehouses/${encodeURIComponent(w)}/rule-references-v1`
export const ruleReferencesApi = {
  context: (w: string) => http.get<any, EngineeringEnvelope<RuleReferenceContext>>(root(w)),
  history: (w: string, version: string) => http.get<any, EngineeringEnvelope<RuleReferencePolicy>>(`${root(w)}/versions/${encodeURIComponent(version)}`),
  save: (w: string, c: RuleReferenceCommand) => http.post<any, EngineeringEnvelope<RuleReferenceResult>>(`${root(w)}/commands`, c),
  operation: (w: string, id: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<RuleReferenceOperation>>(`${root(w)}/operations/${encodeURIComponent(id)}/close`, {}) : http.get<any, EngineeringEnvelope<RuleReferenceOperation>>(`${root(w)}/operations/${encodeURIComponent(id)}`),
}
