import http from '../http'
import type { EngineeringEnvelope } from '../plm/engineeringProducts'
import { toolInstanceApi, type ToolInstanceReference } from './toolInstances'
export type DispositionKind = 'REQUEST' | 'BLOCK'
export interface DispositionRef { owner: string; id: string; version: string; digest: string }
export interface DispositionObligations { version: string; local: { owner: string; id: string; version: string; digest: string; kind: string; state: string }[]; unknownOwners: string[]; canFinalize: false }
export interface DispositionFact { reference: DispositionRef; fact: { tenantId: string; toolId: string; recordId: string; kind: DispositionKind; actorId: string; recordedAt: string; native: ToolInstanceReference; site: DispositionRef; reasonCode: string; reason: string; request: DispositionRef | null; obligations: DispositionObligations } }
export interface DispositionContext { tenantId: string; toolId: string; etag: string; native: ToolInstanceReference; site: DispositionRef; state: string; newUseBlocked: boolean; request: DispositionFact | null; block: DispositionFact | null; obligations: DispositionObligations; approvalKnowledge: string; isDiscarded: boolean; isUsePermit: false }
export interface DispositionResult { operationId: string; objectId: string; kind: DispositionKind; fact: DispositionRef; outcome: string; isDiscarded: false; isUsePermit: false }
export interface DispositionOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: DispositionResult | null }
const tools = '/wms/specials/v1/tools', operations = '/wms/tool-disposition/v1/operations', id = encodeURIComponent
export const toolDispositionApi = {
  scope: toolInstanceApi.scope,
  context: (tool: string) => http.get<any, EngineeringEnvelope<DispositionContext>>(`${tools}/${id(tool)}/disposition-context`),
  write: (tool: string, kind: DispositionKind, etag: string, body: Record<string, unknown>) => http.post<any, EngineeringEnvelope<DispositionResult>>(`${tools}/${id(tool)}/${kind === 'REQUEST' ? 'disposition-requests' : 'new-use-blocks'}`, body, { headers: { 'If-Match': etag } }),
  operation: (operation: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<DispositionOperation>>(`${operations}/${id(operation)}/close`, {}) : http.get<any, EngineeringEnvelope<DispositionOperation>>(`${operations}/${id(operation)}`),
}
