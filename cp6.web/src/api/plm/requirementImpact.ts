import http from '../http'
import type { EngineeringEnvelope, EngineeringReference } from './engineeringProducts'
import type { RequirementSetResult } from './requirementSet'
import type { EngineeringIterationResult } from './engineeringIteration'
export interface ImpactAssessment { id: string; caseId: string; iteration: EngineeringReference; inputVersion: string; planVersion: string; decision: string; actorId: string; reason: string; recordedAt: string; previousId: string | null }
export interface ImpactCase { id: string; requestId: string; candidate: EngineeringReference; capturedIteration: EngineeringReference; adopted: EngineeringReference; inputVersion: string; capturedPlanVersion: string; capturedStage: string; actorId: string; recordedAt: string; assessment: ImpactAssessment | null; assessmentHistory?: ImpactAssessment[] }
export interface ImpactConsumer { iteration: EngineeringReference; name: string; adopted: EngineeringReference; inputVersion: string; planVersion: string; stage: string; state: string; case: ImpactCase | null }
export interface ImpactContext { candidate: EngineeringReference; coverage: string; etag: string; request: { id: string; action: string; actorId: string; reason: string; recordedAt: string } | null; consumers: ImpactConsumer[]; candidateState?: string; isCurrent?: boolean }
export interface IterationImpactContext { iteration: EngineeringReference; cases: ImpactConsumer[] }
export interface SuccessorDifference { before: EngineeringReference; after: EngineeringReference; changes: { requirementId: string | null; area: string; item: string; change: string; before: string | null; after: string | null }[] }
export type ImpactDecision = 'KEEP_CURRENT' | 'READY_TO_ADOPT' | 'BLOCKED'
export const requirementImpactApi = {
  source: (id: string, version: string) => http.get<any, EngineeringEnvelope<ImpactContext>>(`/requirement-set/owner-v1/${encodeURIComponent(id)}/impact`, { params: { version } }),
  difference: (id: string, before: string, after: string) => http.get<any, EngineeringEnvelope<SuccessorDifference>>(`/requirement-set/owner-v1/${encodeURIComponent(id)}/successor-difference`, { params: { before, after } }),
  request: (body: { operationId: string; candidate: EngineeringReference; action: 'REQUEST' | 'WITHDRAW'; expectedEtag: string; reason: string }) => http.post<any, EngineeringEnvelope<RequirementSetResult>>('/requirement-set/owner-v1/impact-requests', body),
  iteration: (id: string) => http.get<any, EngineeringEnvelope<IterationImpactContext>>(`/engineering-iterations/owner-v1/${encodeURIComponent(id)}/impact`),
  assess: (body: { operationId: string; caseId: string; iteration: EngineeringReference; expectedAssessmentId: string | null; decision: ImpactDecision; reason: string }) => http.post<any, EngineeringEnvelope<EngineeringIterationResult>>('/engineering-iterations/owner-v1/impact-assessments', body),
}
export const impactLabel = (code: string) => ({ NOT_REQUESTED: '尚未发起', PENDING: '待负责人评估', REASSESSMENT_REQUIRED: '计划已变化，需重新评估', KEEP_CURRENT: '保留原输入继续执行', READY_TO_ADOPT: '待新版批准后受控替换输入', BLOCKED: '影响尚未解决', ADOPTED: '已实际采用此新版', INPUT_REPLACED: '输入已另行替换', WITHDRAWN: '请求已撤回', REQUEST: '请求生效' } as Record<string, string>)[code] || code
