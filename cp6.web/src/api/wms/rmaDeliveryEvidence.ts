import http from '../http'
import type { RmaOriginal, RmaQty, RmaRef } from './rmaFormalApplications'
export interface DeliveryEvidenceRecord { scopeOptionRef: RmaRef; shipmentRef: RmaRef; shipmentLineId: string; evidenceId: string; documentReference: string; occurredAt: string; ranges: { from: string; to: string }[]; fileRefs: RmaRef[]; notes: string | null }
export interface DeliveryEvidenceWithdraw { scopeOptionRef: RmaRef; evidenceRef: RmaRef; reason: string }
export interface DeliveryEvidenceView {
  evidence: { reference: RmaRef; original: RmaOriginal; documentReference: string; occurredAt: string; ranges: { from: string; to: string }[]; reportedQuantity: RmaQty; fileRefs: RmaRef[]; notes: string | null; recordedAt: string; recordedBy: RmaRef }
  withdrawal: { reason: string; recordedAt: string; recordedBy: RmaRef } | null
}
export interface DeliveryEvidenceFile { reference: RmaRef; name: string; mediaType: string; availability: string }
export type DeliveryEvidencePending = { version: 1; commandId: string } & ({ kind: 'RECORD'; body: DeliveryEvidenceRecord } | { kind: 'WITHDRAW'; body: DeliveryEvidenceWithdraw })
const root = '/wms/rma/v1/delivery-evidence', headers = () => ({ 'X-Correlation-Id': crypto.randomUUID(), 'Cache-Control': 'no-store' })
export const rmaDeliveryEvidenceApi = {
  download: (file: RmaRef) => http.get<any, Blob>(`/file-content/owner-v1/${encodeURIComponent(file.id)}/download`, { params: { version: file.version, digest: file.digest }, responseType: 'blob' }),
  search: (scopeOptionRef: RmaRef, original: RmaOriginal, page: number) => http.post<any, { items: DeliveryEvidenceView[]; hasMore: boolean; coverage: 'COLLECTED_EVIDENCE_ONLY'; establishesDeliveryProof: false }>(`${root}/search`, { scopeOptionRef, shipmentRef: original.shipmentRef, shipmentLineId: original.shipmentLineId, page, pageSize: 20 }, { headers: headers() }),
  files: (scopeOptionRef: RmaRef, keyword: string | null, page: number) => http.post<any, { items: DeliveryEvidenceFile[]; hasMore: boolean }>(`${root}/files/search`, { scopeOptionRef, keyword, page }, { headers: headers() }),
  dispatch: (pending: DeliveryEvidencePending) => http.post<any, DeliveryEvidenceView>(`${root}/${pending.kind === 'RECORD' ? 'record' : 'withdrawals'}`, pending.body, { headers: { ...headers(), 'Idempotency-Key': pending.commandId } }),
  query: (pending: DeliveryEvidencePending) => http.post<any, { status: 'FOUND'; value: DeliveryEvidenceView; canInferNoEffect: false } | { status: 'NOT_OBSERVED'; value: null; canInferNoEffect: false }>(`${root}/commands/query`, { scopeOptionRef: pending.body.scopeOptionRef, commandId: pending.commandId }, { headers: headers() }),
}
