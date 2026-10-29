import http from '../http'
import type { RmaQty, RmaRef } from './rmaFormalApplications'
import type { ReturnRange } from './rmaAuthorizations'
export const correctionActions = { submit: '提交独立审批', 'bind-approval': '读取并绑定审批', prepare: '冻结冲正方案', apply: '执行冲正', withdraw: '撤回未执行冲正' } as const
export type CorrectionAction = keyof typeof correctionActions
export interface CorrectionTarget { axis: 'PHYSICAL_RECEIPT' | 'DISPOSITION'; id: string; reference: RmaRef; kind: string; ranges: ReturnRange[] }
export interface CorrectionChoice { target: CorrectionTarget; availableRanges: ReturnRange[]; policy: { label: string; errorEvidenceRefs: RmaRef[]; simulation: boolean } | null }
export interface CorrectionWork {
  candidate: { id: string; version: number; target: CorrectionTarget; ranges: ReturnRange[]; quantity: RmaQty; errorEvidenceRef: RmaRef; reason: string }
  headRef: RmaRef; state: 'DRAFT' | 'SUBMITTED' | 'APPROVED' | 'POSTED' | 'WITHDRAWN'; allowedActions: string[]
  preparation: { executionKey: string } | null; noEffectProof: RmaRef | null
  result: { reference: RmaRef; sourceClosureRef: RmaRef; successorRef: RmaRef; qualityHeadRef: RmaRef | null } | null
}
export interface CorrectionContext { targets: CorrectionChoice[]; proposals: CorrectionWork[]; capabilities: string[] }
export interface CorrectionResult { action: string; value: CorrectionWork }
export type CorrectionPending = { version: 1; applicationId: string; commandId: string } & (
  { kind: 'draft'; body: { axis: string; targetId: string; expectedTargetRef: RmaRef; ranges: ReturnRange[]; errorEvidenceRef: RmaRef; reason: string } }
  | { kind: 'edit'; proposalId: string; body: { expectedHeadRef: RmaRef; ranges: ReturnRange[]; errorEvidenceRef: RmaRef; reason: string } }
  | { kind: CorrectionAction; proposalId: string; body: { expectedHeadRef: RmaRef; reason: string } })
const root = '/wms/rma/v1/corrections'
const headers = () => ({ 'X-Correlation-Id': crypto.randomUUID(), 'Cache-Control': 'no-store' })
export const rmaCorrectionApi = {
  context: (app: string) => http.get<any, CorrectionContext>(`${root}/applications/${encodeURIComponent(app)}/context`, { headers: headers() }),
  dispatch: (p: CorrectionPending) => http.post<any, CorrectionResult>(`${root}/applications/${encodeURIComponent(p.applicationId)}/proposals${p.kind === 'draft' ? '' : `/${encodeURIComponent(p.proposalId)}/${p.kind}`}`, p.body, { headers: { ...headers(), 'Idempotency-Key': p.commandId } }),
  query: (p: CorrectionPending) => http.post<any, { status: 'FOUND'; value: CorrectionResult } | { status: 'NOT_OBSERVED'; value: null }>(`${root}/commands/query`, { applicationId: p.applicationId, commandId: p.commandId }, { headers: headers() }),
}
