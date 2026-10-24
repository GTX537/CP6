import { masterUuid } from './warehouseOwner'
export interface SampleQuery { sampleNo?: string; sampleType?: string; customerCd?: string; productCd?: string; legacyStatus?: number; awaitingReturnEvidence?: boolean; page: number; pageSize: number }
export interface SampleObservation {
  id: string; sampleNo: string; sampleType: string; declaredQuantity: string; unitCd: string; legacyStatus: number
  warehouseCd: string | null; locationCd: string | null; registeredAt: string; lentAt: string | null; expectedReturnDate: string | null; returnedAt: string | null
  customerCd: string | null; customerName: string | null; productCd: string | null; productName: string | null; lentTo: string | null; remarks: string | null
  customerCdReadable: boolean; customerNameReadable: boolean; productCdReadable: boolean; productNameReadable: boolean; borrowerReadable: boolean; remarksReadable: boolean
  sourceMode: 'LEGACY_OBSERVATION'; stockSourceState: 'SOURCE_UNPROVEN'; historyState: 'HISTORY_INCOMPLETE'; siteDateState: 'UNPROVEN'
  returnEvidenceState: 'OUTSTANDING_UNKNOWN' | 'RETURN_RECORDED_UNVERIFIED' | 'NO_LOAN_RECORDED'; outstandingQuantity: null; etag: string
}
export interface SamplePage { version: 'SAMPLE-OWNER/1'; coverage: 'CURRENT_AUTHORIZED_RECORDS'; formalOperationsAvailable: false; items: SampleObservation[]; total: number; page: number; pageSize: number }
function requireValue(ok: unknown): asserts ok { if (!ok) throw new Error('SAMPLE_RESPONSE_INVALID') }
const optionalText = (x: unknown) => x === null || typeof x === 'string'
const visible = (read: unknown, x: unknown) => typeof read === 'boolean' && optionalText(x) && (read || x === null)
export function checkSampleRow(r: SampleObservation, id?: string) {
  requireValue(r && masterUuid(r.id) && (!id || r.id === id) && typeof r.sampleNo === 'string' && typeof r.sampleType === 'string'
    && typeof r.declaredQuantity === 'string' && /^-?\d{1,14}(\.\d{1,4})?$/.test(r.declaredQuantity) && typeof r.unitCd === 'string' && Number.isInteger(r.legacyStatus)
    && optionalText(r.warehouseCd) && optionalText(r.locationCd) && typeof r.registeredAt === 'string' && optionalText(r.lentAt) && optionalText(r.expectedReturnDate) && optionalText(r.returnedAt)
    && visible(r.customerCdReadable, r.customerCd) && visible(r.customerNameReadable, r.customerName) && visible(r.productCdReadable, r.productCd) && visible(r.productNameReadable, r.productName)
    && visible(r.borrowerReadable, r.lentTo) && visible(r.remarksReadable, r.remarks) && typeof r.etag === 'string' && /^"[A-Za-z0-9+/]{11}="$/.test(r.etag)
    && r.sourceMode === 'LEGACY_OBSERVATION' && r.stockSourceState === 'SOURCE_UNPROVEN' && r.historyState === 'HISTORY_INCOMPLETE' && r.siteDateState === 'UNPROVEN' && r.outstandingQuantity === null)
  requireValue(r.returnEvidenceState === (r.returnedAt !== null ? 'RETURN_RECORDED_UNVERIFIED' : r.lentAt !== null || r.legacyStatus === 1 ? 'OUTSTANDING_UNKNOWN' : 'NO_LOAN_RECORDED'))
  return r
}
export function checkSamplePage(p: SamplePage, q: SampleQuery) {
  requireValue(p && p.version === 'SAMPLE-OWNER/1' && p.coverage === 'CURRENT_AUTHORIZED_RECORDS' && p.formalOperationsAvailable === false
    && p.page === q.page && p.pageSize === q.pageSize && Number.isSafeInteger(p.total) && p.total >= 0 && Array.isArray(p.items)
    && p.items.length === Math.max(0, Math.min(q.pageSize, p.total - (q.page - 1) * q.pageSize)) && new Set(p.items.map(x => x.id)).size === p.items.length)
  for (const row of p.items) { checkSampleRow(row); requireValue((q.legacyStatus === undefined || row.legacyStatus === q.legacyStatus) && (q.awaitingReturnEvidence !== true || row.returnEvidenceState === 'OUTSTANDING_UNKNOWN')) }
  return p
}
