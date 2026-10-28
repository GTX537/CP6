import http from '../http'
import type { EngineeringEnvelope, EngineeringReference } from '../plm/engineeringProducts'
export interface HierarchyEdge { parentLevel: number; childLevel: number }
export interface HierarchyPolicy { reference: EngineeringReference; original: { tenantId: string; warehouseId: string; actorId: string; enabled: boolean; rootLevels: number[]; edges: HierarchyEdge[]; reason: string }; originalJson: string }
export interface HierarchyContext { tenantId: string; actorId: string; warehouseId: string; warehouseCode: string; version: string; hierarchyRevision: string; policy: HierarchyPolicy | null; compatibility: string; creationGrant: false; initializationFence: 'UNAVAILABLE' }
export interface HierarchyCommand { operationId: string; expectedVersion: string; expectedHierarchyRevision: string; enabled: boolean; rootLevels: number[]; edges: HierarchyEdge[]; reason: string }
export interface HierarchyResult { operationId: string; warehouseId: string; reference: EngineeringReference; changed: boolean }
export interface HierarchyOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: HierarchyResult | null }
export interface HierarchyCheck { warehouseId: string; parentLocationId: string | null; locationLevel: number; policyRef: EngineeringReference; hierarchyRevision: string; structureValid: true; creationGrant: false; initializationFence: 'UNAVAILABLE'; requiresFinalRecheck: true }
const root = (w: string) => `/wms/warehouses/${encodeURIComponent(w)}/hierarchy-v1`
export const locationHierarchyApi = {
  context: (w: string) => http.get<any, EngineeringEnvelope<HierarchyContext>>(root(w)),
  history: (w: string, v: string) => http.get<any, EngineeringEnvelope<HierarchyPolicy>>(`${root(w)}/versions/${encodeURIComponent(v)}`),
  save: (w: string, c: HierarchyCommand) => http.post<any, EngineeringEnvelope<HierarchyResult>>(`${root(w)}/commands`, c),
  operation: (w: string, id: string, close: boolean) => close
    ? http.post<any, EngineeringEnvelope<HierarchyOperation>>(`${root(w)}/operations/${encodeURIComponent(id)}/close`, {})
    : http.get<any, EngineeringEnvelope<HierarchyOperation>>(`${root(w)}/operations/${encodeURIComponent(id)}`),
  precheck: (w: string, c: { expectedVersion: string; expectedHierarchyRevision: string; parentLocationId: string | null; locationLevel: number }) => http.post<any, EngineeringEnvelope<HierarchyCheck>>(`${root(w)}/precheck`, c),
}
