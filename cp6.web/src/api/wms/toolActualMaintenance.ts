import http from '../http'
import type { EngineeringEnvelope } from '../plm/engineeringProducts'
import type { ActualRef, ActualFact, ActualObservation, ActualMaintenanceKind, ActualResult } from './toolActualUsage'
import type { MaintenanceFact, MaintenanceEntry } from './toolMaintenance'
export interface ActualMaintenanceContext {
  toolId: string; scope: { environmentId: string; tenantId: string; siteId: string; mode: string }; etag: string; site: ActualRef;
  knowledge: string; policy: ActualFact | null; policyKnowledge: string; evidence: MaintenanceFact[];
  occurrences: ActualFact[]; observations: ActualObservation[]; maintenance: MaintenanceEntry[]; page: number; isUsePermit: false
}
export interface MaintenanceApplicationContext { observation: ActualRef; revision: string; contextRef: string; policy: ActualFact | null; missing: string[]; evidence: MaintenanceFact; isUsePermit: false }
const root = '/wms/specials/v1/tools', enc = encodeURIComponent
export const toolActualMaintenanceApi = {
  context: (id: string, page = 1) => http.get<any, EngineeringEnvelope<ActualMaintenanceContext>>(`${root}/${enc(id)}/actual-maintenance-context`, { params: { page } }),
  applicationContext: (id: string, observation: string) => http.get<any, EngineeringEnvelope<MaintenanceApplicationContext>>(`${root}/${enc(id)}/observations/${enc(observation)}/application-context`),
  write: (id: string, observation: string | null, kind: ActualMaintenanceKind, etag: string, body: Record<string, unknown>) => {
    const path = ({ M_OCCURRENCE: 'maintenance-occurrences/register', M_OBSERVATION: 'observations', M_POLICY: 'maintenance-recognition-policies', M_RECOGNITION: `observations/${enc(observation!)}/recognition-decisions`, M_APPLICATION: `observations/${enc(observation!)}/apply` })[kind]
    return http.post<any, EngineeringEnvelope<ActualResult>>(`${root}/${enc(id)}/${path}`, body, { headers: { 'If-Match': etag } })
  },
}
