import http from '../http'
import type { EngineeringEnvelope, EngineeringReference, EngineeringScope } from './engineeringProducts'
import type { RevisionDraft } from './engineeringRevisions'
import type { ConfigurationBody } from './configurationRules'
export interface CompositionPolicy { kind: string; applicability: string; minimumOccurrences: number; reason: string }
export interface EngineeringCheckPolicy { schema: 'engineering-check-policy/1'; productCategory: string; manufacturing: string; configurationApplicability: string; reason: string; composition: CompositionPolicy[] }
export interface CheckCommand { operationId: string; revision: EngineeringReference; candidate: EngineeringReference; expectedEtag: string; policy: EngineeringReference; reason: string; outputRelations?: EngineeringReference }
export interface CheckItem { path: string; state: 'PASS' | 'BLOCK' | 'UNKNOWN' | 'NOT_APPLICABLE'; code: string; reason: string; source: EngineeringReference | null }
export interface SourcePin { path: string; reference: EngineeringReference; provider: string; nativeSchema: string; capability: string; watermark: string; originalJson: string; findings: string[] }
export interface CheckRun { tenantId: string; id: string; actorId: string; checkedAt: string; command: CheckCommand; input: RevisionDraft; policyBody: ConfigurationBody; inputDigest: string; manifest: { schema: string; canonicalization: string; algorithm: string; originalJson: string; digest: string } | null; items: CheckItem[]; sources: SourcePin[]; state: string; authorityWatermark: string; purpose: string }
export interface CheckView { reference: EngineeringReference; original: CheckRun; originalJson: string; freshness: 'CURRENT' | 'STALE'; changes: string[]; freeze: 'NOT_REQUESTED' }
export interface CheckResult { operationId: string; checkRunId: string; revisionId: string; digest: string }
export interface CheckOperation { operationId: string; state: 'UNKNOWN' | 'CLOSED' | 'COMMITTED'; result?: CheckResult | null }
const root = '/engineering-source-checks/owner-v1'
export const sourceCheckApi = {
  scope: () => http.get<any, EngineeringEnvelope<EngineeringScope>>(`${root}/scope`),
  save: (body: CheckCommand) => http.post<any, EngineeringEnvelope<CheckResult>>(`${root}/commands`, body),
  read: (id: string) => http.get<any, EngineeringEnvelope<CheckView>>(`${root}/${encodeURIComponent(id)}`),
  history: (id: string, page = 1) => http.get<any, EngineeringEnvelope<CheckView[]>>(`${root}/revisions/${encodeURIComponent(id)}`, { params: { page } }),
  operation: (id: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<CheckOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {})
    : http.get<any, EngineeringEnvelope<CheckOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
}
