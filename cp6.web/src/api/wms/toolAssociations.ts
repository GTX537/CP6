import http from '../http'
import type { EngineeringEnvelope } from '../plm/engineeringProducts'
import { toolInstanceApi, type ToolInstanceReference } from './toolInstances'
export interface TechnicalReference { owner: string; id: string; version: string; digest: string }
export interface ToolAssociationScope { productApplicability: string; processApplicability: string; configurationApplicability: string; purpose: string; effectiveFrom: string; effectiveTo: string | null }
export interface ToolAssociationCandidate { technical: TechnicalReference; native: TechnicalReference; baseCode: string; number: string; businessRevision: number; requirements: { productApplicability: string; processRequirements: string; configurationApplicability: string; technicalRequirements: string; documentRequirements: string }; technicalState: string; approval: string }
export interface ToolAssociationProposal { operationId: string; proposalId: string; technical: TechnicalReference; physical: ToolInstanceReference; scope: ToolAssociationScope; reason: string; supersedes: string | null }
export interface ToolAssociationDecision { operationId: string; proposalId: string; roundId: string; subjectDigest: string; expectedControlRevision: string; expectedGeneration: string; decision: 'CONFIRM' | 'REJECT' | 'WITHDRAW' | 'REFRESH'; evidence: string }
export interface AssociationDecisionReference { owner: string; id: string; version: string; digest: string }
export interface ToolAssociationView { subject: { tenantId: string; proposalId: string; technical: TechnicalReference; physical: ToolInstanceReference; baseCode: string; number: string; businessRevision: number; scope: ToolAssociationScope; reason: string; supersedes: string | null }; subjectDigest: string; revision: string; controlRevision: string; generation: string; roundId: string; state: string; lastKnownState: string; everConfirmed: boolean; currentKnowledge: string; unresolvedRoles: string[]; engineering: AssociationDecisionReference | null; physical: AssociationDecisionReference | null; asOf: string; inEffectiveInterval: boolean; notExecutionPermit: true }
export interface ToolAssociationResult { operationId: string; kind: 'TOOL_ASSOCIATION'; objectId: string; proposalId: string; version: string; digest: string; applied: boolean; outcome: string; roundId: string; generation: string; notExecutionPermit: true }
export interface ToolAssociationOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: ToolAssociationResult | null }
const root = '/tool-associations/owner-v1', tools = '/wms/specials/v1/tools', id = encodeURIComponent
export const associationEtag = (view: ToolAssociationView) => `"tool-association:${view.subject.proposalId}:${view.revision}"`
export const toolAssociationApi = {
  scope: toolInstanceApi.scope,
  candidates: (physical: string, engineeringNo: string, revision: number) => http.get<any, EngineeringEnvelope<ToolAssociationCandidate[]>>(`${tools}/${id(physical)}/engineering-candidates`, { params: { engineeringNo, revision } }),
  list: (physical: string, page = 1) => http.get<any, EngineeringEnvelope<ToolAssociationView[]>>(`${tools}/${id(physical)}/engineering-links`, { params: { page } }),
  read: (proposal: string, version?: string) => http.get<any, EngineeringEnvelope<ToolAssociationView>>(`${root}/${id(proposal)}`, { params: { version } }),
  propose: (physical: string, etag: string, body: ToolAssociationProposal) => http.post<any, EngineeringEnvelope<ToolAssociationResult>>(`${tools}/${id(physical)}/engineering-links`, body, { headers: { 'If-Match': etag } }),
  decide: (engineering: boolean, etag: string, body: ToolAssociationDecision) => http.post<any, EngineeringEnvelope<ToolAssociationResult>>(engineering ? '/tool-specifications/owner-v1/instance-association-decisions' : `${tools}/engineering-link-decisions`, body, { headers: { 'If-Match': etag } }),
  operation: (operation: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<ToolAssociationOperation>>(`${root}/operations/${id(operation)}/close`, {}) : http.get<any, EngineeringEnvelope<ToolAssociationOperation>>(`${root}/operations/${id(operation)}`),
}
