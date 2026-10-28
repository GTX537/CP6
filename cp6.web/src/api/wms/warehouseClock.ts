import http from '../http'
import type { EngineeringEnvelope, EngineeringReference } from '../plm/engineeringProducts'
export interface ClockAuthority { environmentId: string; id: string; version: string; provider: string; evidenceMode: 'REAL' | 'DEMO'; attestationRef: string; validFrom: string; validTo: string; maxErrorMilliseconds: number; maxReadMilliseconds: number }
export interface ClockSource { reference: EngineeringReference; authority: ClockAuthority }
export interface ClockDefinition { enabled: boolean; source: EngineeringReference; validFrom: string; validTo: string }
export interface ClockPolicy { reference: EngineeringReference; original: { tenantId: string; id: string; warehouseId: string; version: string; actorId: string; definition: ClockDefinition; source: ClockSource; reason: string }; originalJson: string }
export interface ClockContext { tenantId: string; actorId: string; warehouseId: string; warehouseCode: string; version: string; policy: ClockPolicy | null; source: ClockSource | null; qualification: 'CLOCK_CONFIGURATION_ONLY'; admissionGrant: false }
export interface ClockCommand { operationId: string; expectedVersion: string; definition: ClockDefinition; reason: string }
export interface ClockResult { operationId: string; warehouseId: string; reference: EngineeringReference; changed: boolean }
export interface ClockOperation { operationId: string; warehouseId: string; state: 'COMMITTED' | 'UNKNOWN' | 'CLOSED'; result?: ClockResult | null }
export interface ClockEvidence { reference: EngineeringReference; original: { tenantId: string; environmentId: string; warehouseId: string; id: string; policy: EngineeringReference; source: EngineeringReference; evidenceMode: 'REAL' | 'DEMO'; observedAt: string; earliest: string; latest: string; readElapsedTicks: number } }
export interface RuleTimeRequest { proposal: EngineeringReference; kind: 'CURRENT' | 'PLANNED'; at: string | null }
export interface RuleTimeAssessment { tenantId: string; actorId: string; locationId: string; warehouseId: string; proposal: EngineeringReference; etag: string; requested: RuleTimeRequest; outcome: 'UNKNOWN' | 'TIME_SATISFIED' | 'NOT_YET_VALID' | 'EXPIRED'; reason: string; clock: ClockEvidence | null; evaluatedAt: string | null; isPrediction: boolean; qualification: 'RULE_TIME_ONLY'; admissionGrant: false; requiresFinalEffectRecheck: true; remainingGates: string[] }
const root = (w: string) => `/wms/warehouses/${encodeURIComponent(w)}/clock-policy-v1`
export const warehouseClockApi = {
  context: (w: string) => http.get<any, EngineeringEnvelope<ClockContext>>(root(w)),
  history: (w: string, v: string) => http.get<any, EngineeringEnvelope<ClockPolicy>>(`${root(w)}/versions/${encodeURIComponent(v)}`),
  save: (w: string, c: ClockCommand) => http.post<any, EngineeringEnvelope<ClockResult>>(`${root(w)}/commands`, c),
  operation: (w: string, id: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<ClockOperation>>(`${root(w)}/operations/${encodeURIComponent(id)}/close`, {}) : http.get<any, EngineeringEnvelope<ClockOperation>>(`${root(w)}/operations/${encodeURIComponent(id)}`),
  evaluate: (location: string, id: string, etag: string, c: RuleTimeRequest) => http.post<any, EngineeringEnvelope<RuleTimeAssessment>>(`/location-rule/owner-v1/locations/${encodeURIComponent(location)}/proposals/${encodeURIComponent(id)}/time-evaluation`, c, { headers: { 'If-Match': etag } }),
}
