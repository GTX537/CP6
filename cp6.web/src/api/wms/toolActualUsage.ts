import http from '../http'
import { toolInstanceApi, type ToolInstanceReference } from './toolInstances'
import type { EngineeringEnvelope } from '../plm/engineeringProducts'
export interface ActualRef { owner: string; id: string; version: string; digest: string }
export type ActualKind = 'EVIDENCE' | 'OCCURRENCE' | 'OBSERVATION' | 'POLICY' | 'RECOGNITION' | 'APPLICATION' | 'USE'
export type ActualCommandKind = Exclude<ActualKind, 'USE'>
export interface ActualFact { reference: ActualRef; fact: { toolId: string; recordId: string; kind: ActualKind; actorId: string; recordedAt: string; identityKey: string; nativeBefore: ToolInstanceReference; nativeAfter: ToolInstanceReference; body: Record<string, unknown> } }
export interface ActualCycle { reference: ActualRef; number: string | null; shots: string; current: boolean; startedAt: string | null; closedAt: string | null }
export interface ActualObservation { original: ActualFact; applicationState: string; applicationRevision: string; decisions: ActualFact[]; application: ActualFact | null }
export interface ActualContext { tenantId: string; toolId: string; scope: { environmentId: string; tenantId: string; siteId: string; mode: string }; etag: string; native: ToolInstanceReference; site: ActualRef; knowledge: string; currentEligibility: string; cycles: ActualCycle[]; lifetimeKnowledge: string; lifetimeShots: string | null; policy: ActualFact | null; policyKnowledge: string; evidence: ActualFact[]; occurrences: ActualFact[]; observations: ActualObservation[]; uses: ActualFact[]; isUsePermit: false; page: number }
export interface ActualApplicationContext { observation: ActualRef; revision: string; contextRef: string; cycles: ActualCycle[]; policy: ActualFact | null; missing: string[]; evidence?: ActualFact[]; isUsePermit: false }
export interface ActualResult { operationId: string; objectId: string; kind: ActualCommandKind; fact: ActualRef; outcome: string; isUsePermit: false }
export interface ActualOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: ActualResult | null }
const root = '/wms/specials/v1/tools', operationRoot = '/wms/tool-actual-usage/v1/operations', enc = encodeURIComponent
export const toolActualUsageApi = {
  scope: toolInstanceApi.scope,
  context: (id: string, page = 1) => http.get<any, EngineeringEnvelope<ActualContext>>(`${root}/${enc(id)}/actual-usage-context`, { params: { page } }),
  applicationContext: (id: string, observation: string) => http.get<any, EngineeringEnvelope<ActualApplicationContext>>(`${root}/${enc(id)}/observations/${enc(observation)}/application-context`),
  write: (id: string, observation: string | null, kind: ActualCommandKind, etag: string, body: Record<string, unknown>) => {
    const path = ({ EVIDENCE: 'actual-evidence', OCCURRENCE: 'use-occurrences/register', OBSERVATION: 'observations', POLICY: 'actual-recognition-policies', RECOGNITION: `observations/${enc(observation!)}/recognition-decisions`, APPLICATION: `observations/${enc(observation!)}/apply` })[kind]
    return http.post<any, EngineeringEnvelope<ActualResult>>(`${root}/${enc(id)}/${path}`, body, { headers: { 'If-Match': etag } })
  },
  operation: (id: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<ActualOperation>>(`${operationRoot}/${enc(id)}/close`, {}) : http.get<any, EngineeringEnvelope<ActualOperation>>(`${operationRoot}/${enc(id)}`),
}
