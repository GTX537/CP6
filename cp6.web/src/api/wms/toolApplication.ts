import http from '../http'
import type { EngineeringEnvelope } from '../plm/engineeringProducts'
import type { DispositionContext } from './toolDisposition'
import { toolInstanceApi } from './toolInstances'
export interface ToolApplicationReceipt {
  schema: 'oa.app/2.0'; scope: { environmentId: string; tenantId: string }; receiptId: string; receiptStreamId: string; receiptRevision: string; receiptEventId: string
  producerOwner: string; submissionId: string; instanceId: string; decisionId: string; action: string; application: 'BLOCKED' | 'STALE' | 'CONFLICT'; outcomeCode: string
  actionActorId: string; appliedAt: null; businessResultRef: null; businessResultVersion: null; previousReceiptId: string | null; recordedAt: string
  payloadDigest: { algorithm: string; canonicalizationVersion: string; value: string }
}
export interface ToolApplicationContext { tenantId: string; toolId: string; etag: string; state: 'NOT_RECEIVED' | 'RECEIVED'; receiptStreamId: string | null; decisionId: string | null; decision: 'APPROVED' | 'REJECTED' | 'WITHDRAWN' | null; decidedAt: string | null; receipt: ToolApplicationReceipt | null; obligations: DispositionContext['obligations']; isDiscarded: boolean; canFinalize: false }
export interface ToolApplicationResult { operationId: string; objectId: string; receipt: ToolApplicationReceipt }
export interface ToolApplicationOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: ToolApplicationResult | null }
const tools = '/wms/specials/v1/tools', operations = '/wms/tool-application/v2/operations', id = encodeURIComponent
export const toolApplicationApi = {
  scope: toolInstanceApi.scope,
  context: (tool: string) => http.get<any, EngineeringEnvelope<ToolApplicationContext>>(`${tools}/${id(tool)}/application-context`),
  write: (tool: string, etag: string, body: Record<string, unknown>) => http.post<any, EngineeringEnvelope<ToolApplicationResult>>(`${tools}/${id(tool)}/approval-applications`, body, { headers: { 'If-Match': etag } }),
  operation: (operation: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<ToolApplicationOperation>>(`${operations}/${id(operation)}/close`, {}) : http.get<any, EngineeringEnvelope<ToolApplicationOperation>>(`${operations}/${id(operation)}`),
}
