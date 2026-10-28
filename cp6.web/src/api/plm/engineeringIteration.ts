import http from '../http'
import type { EngineeringEnvelope, EngineeringReference, EngineeringCreation, SourceChoice } from './engineeringProducts'
import type { RequirementSetScope } from './requirementSet'
import type { RevisionOrigin } from './engineeringRevisions'
export type EngineeringIterationScope = RequirementSetScope
export interface IterationMilestone { id: string; name: string; criterion: string; dueDate: string }
export interface IterationProjectInput { name: string; ownerId: string; members: string[]; site: EngineeringReference | null; product: EngineeringReference | null; objective: string; startDate: string | null; endDate: string | null; milestones: IterationMilestone[] }
export interface IterationPlan { name: string; ownerId: string; goal: string; startDate: string; endDate: string }
export interface IterationTaskPlan { id: string; title: string; ownerId: string; confirmerId: string; weight: string; dueDate: string; acceptance: string }
export interface IterationTask { plan: IterationTaskPlan; state: string; deliverables: EngineeringReference[]; submittedBy: string | null; confirmedBy: string | null; confirmedAt: string | null; lastReason: string }
export interface IterationWork { projectId: string; projectReference: EngineeringReference; plan: IterationPlan; origin: RevisionOrigin; stage: string; holdState: string; planVersion: string; inputVersion: string; input: { proof: { environmentId: string; tenantId: string; evidenceMode: string; requirementSet: EngineeringReference; submissionId: string; decisionId: string; approvalId: string }; requirementSetOriginal: string; adoptedAt: string; adoptedBy: string }; tasks: IterationTask[] }
export interface EngineeringIterationSnapshot { tenantId: string; reference: EngineeringReference; etag: string; isCurrent: boolean; body: { kind: 'PROJECT' | 'ITERATION'; name: string; number: string; project: { definition: IterationProjectInput; state: string } | null; iteration: IterationWork | null }; missing: string[]; sourceDrift: string; progress: { confirmedWeight: string; plannedWeight: string; percent: string | null } | null; originalJson: string }
export interface EngineeringIterationCommand { operationId: string; objectId: string; action: string; expectedVersion?: string | null; expectedEtag?: string | null; expectedAuthorityVersion?: string | null; departmentId?: string | null; reason: string; project?: IterationProjectInput; projectId?: string; plan?: IterationPlan; origin?: RevisionOrigin; requirementSet?: EngineeringReference; task?: IterationTaskPlan; taskId?: string; deliverables?: EngineeringReference[] }
export interface EngineeringIterationResult { operationId: string; kind: 'PROJECT' | 'ITERATION'; objectId: string; version: string; digest: string }
export interface EngineeringIterationOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: EngineeringIterationResult | null }
const root = '/engineering-iterations/owner-v1'
export const engineeringIterationApi = {
  scope: () => http.get<any, EngineeringEnvelope<EngineeringIterationScope>>(`${root}/scope`),
  creationContext: () => http.get<any, EngineeringEnvelope<EngineeringCreation>>(`${root}/creation-context`),
  list: (kind: string, projectId?: string, search = '', page = 1) => http.get<any, EngineeringEnvelope<EngineeringIterationSnapshot[]>>(root, { params: { kind, projectId, search, page } }),
  read: (id: string, version?: string) => http.get<any, EngineeringEnvelope<EngineeringIterationSnapshot>>(`${root}/${encodeURIComponent(id)}`, { params: { version } }),
  members: (search = '') => http.get<any, EngineeringEnvelope<{ id: string; name: string }[]>>(`${root}/members`, { params: { search } }),
  sites: (search = '') => http.get<any, EngineeringEnvelope<SourceChoice[]>>(`${root}/sites`, { params: { search } }),
  save: (command: EngineeringIterationCommand) => http.post<any, EngineeringEnvelope<EngineeringIterationResult>>(`${root}/commands`, command),
  operation: (id: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<EngineeringIterationOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {}) : http.get<any, EngineeringEnvelope<EngineeringIterationOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
}
export function iterationLabel(code: string) {
  return ({ DRAFT: '草稿', ACTIVE: '进行中', PLANNED: '待启动', IN_DESIGN: '设计中', HELD: '暂停', NORMAL: '正常', TODO: '待办', IN_PROGRESS: '处理中', BLOCKED: '阻塞', SUBMITTED: '待确认', DONE: '已确认', CANCELLED: '已取消', UNCHANGED: '准确来源可用', CHANGED: '来源已变化', UNKNOWN: '来源状态未知', SITE_REQUIRED: '需选择场地', SITE_SOURCE_CHANGED: '场地来源已变化', PRODUCT_SOURCE_CHANGED: '工程产品来源已变化', OBJECTIVE_REQUIRED: '需填写项目目标', PROJECT_DATES_REQUIRED: '需填写计划起止日期', MILESTONE_CRITERION_REQUIRED: '需定义里程碑判据', MILESTONE_OUTSIDE_PROJECT_DATES: '里程碑超出项目日期', INPUT_SOURCE_CHANGED: '需求输入已变化，需处理影响', INPUT_SOURCE_UNKNOWN: '无法确认需求输入来源', PROJECT_INPUT_CHANGED: '项目的产品或场地已变化，需显式重新采用输入' } as Record<string, string>)[code] || '有待核对事项'
}
