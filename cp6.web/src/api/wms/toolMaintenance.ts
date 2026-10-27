import http from '../http'
import type { EngineeringEnvelope } from '../plm/engineeringProducts'
import { toolInstanceApi, type ToolInstanceReference } from './toolInstances'
export interface MaintenanceRef { owner: string; id: string; version: string; digest: string }
export type MaintenanceKind = 'POLICY' | 'START' | 'REPORT' | 'COMPLETE' | 'ASSESSMENT' | 'RESET'
export interface MaintenancePolicy { state: 'ENABLED' | 'REVOKED'; site: MaintenanceRef; requiredChecks: string[]; allowReset: boolean; allowUnserviceableRepair: boolean; independentInspector: boolean; effectiveFrom: string; effectiveTo: string; reason: string }
export interface MaintenanceReport { outcome: string; occurredAt: string; checks: { code: string; result: string; evidence: string }[]; evidence: string; policy: MaintenanceRef | null }
export interface MaintenanceAssessment { completionRef: MaintenanceRef; policy: MaintenanceRef; decision: string; validUntil: string; evidence: string }
export interface MaintenanceFact { reference: MaintenanceRef; fact: { tenantId: string; toolId: string; recordId: string; kind: MaintenanceKind; maintenanceId: string | null; actorId: string; recordedAt: string; nativeBefore: ToolInstanceReference; nativeAfter: ToolInstanceReference; body: Record<string, unknown> } }
export interface MaintenanceEntry { maintenanceId: string; status: string; start: MaintenanceFact; completion: MaintenanceFact | null; reset: MaintenanceFact | null; reports: MaintenanceFact[] }
export interface MaintenanceContext { tenantId: string; toolId: string; etag: string; native: ToolInstanceReference; site: MaintenanceRef; siteEnabled: boolean; cycle: { reference: MaintenanceRef; number: string | null; shots: string | null; knowledge: string; lifetimeKnowledge: string; lifetimeShots: string | null }; policy: MaintenanceFact | null; policyKnowledge: string; maintenance: MaintenanceEntry[]; assessments: MaintenanceFact[]; currentEligibility: string; notExecutionPermit: true; page: number }
export interface MaintenanceResult { operationId: string; objectId: string; kind: MaintenanceKind; fact: MaintenanceRef; maintenanceId: string | null; outcome: string; notExecutionPermit: true }
export interface MaintenanceOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: MaintenanceResult | null }
const tools = '/wms/specials/v1/tools', operations = '/wms/tool-maintenance/v1/operations', id = encodeURIComponent
export const toolMaintenanceApi = {
  scope: toolInstanceApi.scope,
  context: (tool: string, page = 1) => http.get<any, EngineeringEnvelope<MaintenanceContext>>(`${tools}/${id(tool)}/maintenance-context`, { params: { page } }),
  write: (tool: string, maintenance: string | null, kind: MaintenanceKind, etag: string, body: Record<string, unknown>) => {
    const base = `${tools}/${id(tool)}`
    const path = kind === 'POLICY' ? `${base}/maintenance-policies` : kind === 'START' ? `${base}/maintenance`
      : kind === 'ASSESSMENT' ? `/master/tool-maintenance/v1/${id(tool)}/${id(maintenance!)}/assessments`
        : `${base}/maintenance/${id(maintenance!)}/${({ REPORT: 'reports', COMPLETE: 'complete', RESET: 'reset-applications' } as const)[kind]}`
    return http.post<any, EngineeringEnvelope<MaintenanceResult>>(path, body, { headers: { 'If-Match': etag } })
  },
  operation: (operation: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<MaintenanceOperation>>(`${operations}/${id(operation)}/close`, {}) : http.get<any, EngineeringEnvelope<MaintenanceOperation>>(`${operations}/${id(operation)}`),
}
