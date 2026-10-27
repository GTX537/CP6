import http from '../http'
import type { EngineeringEnvelope } from '../plm/engineeringProducts'
import { toolInstanceApi } from './toolInstances'
import type { DispositionContext, DispositionRef } from './toolDisposition'
export type ToolApprovalKind = 'POLICY' | 'SUBMIT'
export interface ToolApprovalPolicyBody { operationId: string; state: 'ENABLED' | 'REVOKED'; site: DispositionRef; reasonCodes: string[]; requiredHumanApprovals: number; separateRequesterAndHandler: boolean; effectiveFrom: string; effectiveTo: string; reason: string }
export interface ToolApprovalPolicy { reference: DispositionRef; fact: { actorId: string; recordedAt: string; body: ToolApprovalPolicyBody } }
export interface ToolApprovalSubmission { submissionId: string; toolId: string; state: 'DISPATCH_PENDING'; starterId: string; frozenAt: string; snapshotDigest: string; isApproved: false; isDiscarded: false }
export interface ToolApprovalDelivery { submissionId: string; outboxId: string; state: 'PENDING' | 'OUTCOME_UNKNOWN' | 'BLOCKED' | 'CONFLICT' | 'RECEIVED' | 'DUPLICATE_OF'; attempts: number; errorCode: string | null; receipt: { submissionId: string; state: 'DISPATCH_PENDING'; instanceId: null; alias: { requestedSubmissionId: string; canonicalSubmissionId: string; aliasId: string } | null } | null }
export interface ToolApprovalContext { tenantId: string; toolId: string; etag: string; disposition: DispositionContext; policy: ToolApprovalPolicy | null; policyKnowledge: string; submission: ToolApprovalSubmission | null; delivery?: ToolApprovalDelivery | null }
export interface ToolApprovalResult { operationId: string; objectId: string; kind: ToolApprovalKind; fact: DispositionRef; submission?: ToolApprovalSubmission | null; isDiscarded: false; isUsePermit: false }
export interface ToolApprovalOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: ToolApprovalResult | null }
const tools = '/wms/specials/v1/tools', operations = '/wms/tool-approval/v1/operations', id = encodeURIComponent
export const toolApprovalApi = {
  scope: toolInstanceApi.scope,
  context: (tool: string) => http.get<any, EngineeringEnvelope<ToolApprovalContext>>(`${tools}/${id(tool)}/approval-context`),
  write: (tool: string, kind: ToolApprovalKind, etag: string, body: Record<string, unknown>) => http.post<any, EngineeringEnvelope<ToolApprovalResult>>(`${tools}/${id(tool)}/${kind === 'POLICY' ? 'disposition-policies' : 'approval-submissions'}`, body, { headers: { 'If-Match': etag } }),
  operation: (operation: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<ToolApprovalOperation>>(`${operations}/${id(operation)}/close`, {}) : http.get<any, EngineeringEnvelope<ToolApprovalOperation>>(`${operations}/${id(operation)}`),
}
