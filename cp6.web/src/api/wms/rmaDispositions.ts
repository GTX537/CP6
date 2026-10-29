import http from '../http'
import type { RmaQty, RmaRef } from './rmaFormalApplications'
import type { ReturnRange } from './rmaAuthorizations'
export const dispositionLabels = { RESALE_STOCK: '可售入库', REPAIR: '内部返修', SCRAP: '报废', SUPPLIER_RETURN: '退供应商', RETURN_TO_CUSTOMER: '退客户' } as const
export type DispositionKind = keyof typeof dispositionLabels
export interface DispositionProposal { proposalRef: RmaRef; selectionRef: RmaRef; quality: 'ACCEPTED' | 'REJECTED' | 'HOLD' | 'PENDING'; eligibleRanges: ReturnRange[]; eligibleQuantity: RmaQty; blockingReasons: string[] }
export interface DispositionOption { reference: RmaRef; disposition: DispositionKind; label: string; businessRef: RmaRef; destinationLocationId: string | null; simulation: boolean }
export interface DispositionChoice { lineId: string; inspectionId: string; proposal: DispositionProposal; options: DispositionOption[] }
export interface DispositionWork { preparation: { workId: string; definition: { kind: DispositionKind; owner: string }; ranges: ReturnRange[]; quantity: RmaQty }; workRef: RmaRef; state: 'PREPARED' | 'APPLIED' | 'SEALED_NO_EFFECT'; qualityCurrent: boolean; noEffectProof: RmaRef | null; result: { reference: RmaRef; qualityReleaseRef: RmaRef | null; stockResult: { result: { relatedMovementIds: string[] } } } | null }
export interface DispositionContext { definition: { kind: DispositionKind; owner: string }; choices: DispositionChoice[]; works: DispositionWork[]; capabilities: string[] }
export interface DispositionResult { action: string; value: DispositionWork }
export interface DispositionPrepare { proposalRef: RmaRef; selectionRef: RmaRef; optionRef: RmaRef; ranges: ReturnRange[]; reason: string }
export type DispositionPending = { version: 1; applicationId: string; disposition: DispositionKind; commandId: string } & (
  { kind: 'PREPARE'; body: DispositionPrepare } | { kind: 'COMMIT' | 'SEAL'; workId: string; body: { expectedWorkRef: RmaRef; reason: string } })
const root = (kind: DispositionKind) => `/wms/rma/v2/dispositions/${kind}`
const headers = () => ({ 'X-Correlation-Id': crypto.randomUUID(), 'Cache-Control': 'no-store' })
export const rmaDispositionApi = {
  context: (kind: DispositionKind, app: string) => http.get<any, DispositionContext>(`${root(kind)}/applications/${encodeURIComponent(app)}/context`, { headers: headers() }),
  dispatch: (p: DispositionPending) => http.post<any, DispositionResult>(`${root(p.disposition)}/applications/${encodeURIComponent(p.applicationId)}/works${p.kind === 'PREPARE' ? '' : `/${encodeURIComponent(p.workId)}/${p.kind.toLowerCase()}`}`, p.body, { headers: { ...headers(), 'Idempotency-Key': p.commandId } }),
  query: (p: DispositionPending) => http.post<any, { status: 'FOUND'; value: DispositionResult } | { status: 'NOT_OBSERVED'; value: null }>(`${root(p.disposition)}/commands/query`, { applicationId: p.applicationId, commandId: p.commandId }, { headers: headers() }),
}
