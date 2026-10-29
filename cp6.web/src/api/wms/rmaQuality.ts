import http from '../http'
import type { RmaQty, RmaRef } from './rmaFormalApplications'
import type { ReturnRange } from './rmaAuthorizations'
export type QualityState = 'ACCEPTED' | 'REJECTED' | 'HOLD' | 'PENDING'
export interface QualityPartition { quality: QualityState; ranges: ReturnRange[]; quantity: RmaQty; basisRefs: RmaRef[]; reasonCodes: string[] }
export interface QualityDecision { owner: 'QA-NCR-01'; decisionRef: RmaRef; decisionVersion: string; decisionState: 'ACTIVE' | 'WITHDRAWN' | 'SUPERSEDED'; partitions: QualityPartition[]; previousDecisionRef: RmaRef | null }
export interface QualitySource { applicationId: string; lineId: string; registrarId: string; reference: RmaRef; physicalHeadRef: RmaRef; receipt: { receivedRanges: ReturnRange[]; quantity: RmaQty; movementRef: RmaRef } }
export interface QualityView { inspection: { id: string; reference: RmaRef; source: QualitySource }; headRef: RmaRef; decision: { result: QualityDecision; reason: string; actorId: string } | null; observation: { decision: QualityDecision } | null; observationCurrent: boolean }
export interface QualityChoice { source: QualitySource; inspection: QualityView | null; policy: { reference: RmaRef; evidenceRefs: RmaRef[]; simulation: boolean; requireIndependentRegistrar: boolean } | null }
export interface QualityContext { applicationId: string; receipts: QualityChoice[]; capabilities: string[] }
export interface QualityStart { lineId: string; expectedSourceRef: RmaRef; expectedRmaHeadRef: RmaRef; expectedPolicyRef: RmaRef; reason: string }
export interface QualityDecide { expectedHeadRef: RmaRef; expectedPolicyRef: RmaRef; partitions: Omit<QualityPartition, 'quantity'>[]; reason: string }
export interface QualityWithdraw { expectedHeadRef: RmaRef; reason: string }
export interface QualityObserve { expectedDecisionRef: RmaRef; expectedRmaHeadRef: RmaRef }
export type QualityPending = { version: 1; commandId: string; applicationId: string } & (
  { kind: 'START'; body: QualityStart } | { kind: 'DECIDE'; inspectionId: string; body: QualityDecide }
  | { kind: 'WITHDRAW'; inspectionId: string; body: QualityWithdraw } | { kind: 'OBSERVE'; inspectionId: string; body: QualityObserve })
export interface QualityResult { action: string; value: QualityView }
const root = '/wms/rma/v1', headers = () => ({ 'X-Correlation-Id': crypto.randomUUID(), 'Cache-Control': 'no-store' })
const actions = { DECIDE: 'decisions', WITHDRAW: 'withdrawals', OBSERVE: 'observations' }
export const rmaQualityApi = {
  context: (app: string) => http.get<any, QualityContext>(`${root}/applications/${encodeURIComponent(app)}/quality/context`, { headers: headers() }),
  read: (app: string, inspection: string, revision?: number) => http.get<any, QualityView>(`${root}/applications/${encodeURIComponent(app)}/quality/inspections/${encodeURIComponent(inspection)}`, { params: { revision }, headers: headers() }),
  dispatch: (p: QualityPending) => http.post<any, QualityResult>(`${root}/applications/${encodeURIComponent(p.applicationId)}/quality/inspections${p.kind === 'START' ? '' : `/${encodeURIComponent(p.inspectionId)}/${actions[p.kind]}`}`, p.body, { headers: { ...headers(), 'Idempotency-Key': p.commandId } }),
  query: (p: QualityPending) => http.post<any, { status: 'FOUND'; value: QualityResult } | { status: 'NOT_OBSERVED'; value: null }>(`${root}/quality/commands/query`, { applicationId: p.applicationId, commandId: p.commandId }, { headers: headers() }),
}
