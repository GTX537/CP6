export const reportOwnerVersion = 'REPORT-CENTER-OWNER/1'
export type ReportMode = 'stocks' | 'transactions'
export type ReportQuery = { warehouseCd?: string; productCd?: string; lotNo?: string; txnType?: string; fromDate?: string; toDate?: string; page: number; pageSize: number }
type RecordRow = { id: string; warehouseCd: string; locationCd: string; productCd: string; lotNo: string; unitCd: string | null; unitState: 'RECORDED' | 'NOT_RECORDED'; ownerType: string | null; ownerState: 'RECORDED' | 'UNKNOWN'; ownerCd: string | null; ownerCdReadable: boolean }
export type ReportStock = RecordRow & { physicalQty: string; legacyAllocatedQty: string; legacyAvailableQty: string; receiveDate: string | null; expiryDate: string | null; freeQty: null; valuation: null }
export type ReportTransaction = RecordRow & { txnNo: string; txnType: string; txnDateTime: string; qty: string; operatorCd: string | null; operatorCdReadable: boolean; remark: string | null; remarkReadable: boolean; relatedNo: null; relatedType: null; relatedSourceState: 'SOURCE_DISCLOSURE_UNAVAILABLE'; occurrenceState: 'NOT_RECONSTRUCTED'; quantityMeaning: 'ORIGINAL_UNSIGNED_OR_SIGNED_LEGACY_VALUE'; timeBasis: 'RECORDED_TXN_DATETIME_WITHOUT_SITE_TIMEZONE' }
export type ReportPage = { version: typeof reportOwnerVersion; coverage: 'CURRENT_AUTHORIZED_RECORDS'; source: 'CURRENT_STOCK' | 'CURRENT_TRANSACTION_RECORDS'; snapshotState: 'NOT_CAPTURED'; businessTimeState: 'NOT_RECONSTRUCTED'; valuationState: 'UNKNOWN'; freeState: 'NOT_EVALUATED'; exportAvailable: false; total: number; page: number; pageSize: number; items: (ReportStock | ReportTransaction)[] }
const string = (v: unknown): v is string => typeof v === 'string'
const nullable = (v: unknown) => v === null || string(v)
const exact = (v: unknown) => string(v) && /^-?\d+(\.\d+)?$/.test(v)
const disclosed = (r: any, key: string) => typeof r[key + 'Readable'] === 'boolean' && nullable(r[key]) && (r[key + 'Readable'] || r[key] === null)
export function validReportPage(v: any, q: ReportQuery, mode: ReportMode): v is ReportPage {
  if (!v || v.version !== reportOwnerVersion || v.coverage !== 'CURRENT_AUTHORIZED_RECORDS' || v.source !== (mode === 'stocks' ? 'CURRENT_STOCK' : 'CURRENT_TRANSACTION_RECORDS') || v.snapshotState !== 'NOT_CAPTURED' || v.businessTimeState !== 'NOT_RECONSTRUCTED' || v.valuationState !== 'UNKNOWN' || v.freeState !== 'NOT_EVALUATED' || v.exportAvailable !== false || v.page !== q.page || v.pageSize !== q.pageSize || !Number.isSafeInteger(v.total) || v.total < 0 || !Array.isArray(v.items) || v.items.length > q.pageSize || v.items.length > v.total) return false
  const ids = new Set<string>()
  return v.items.every((r: any) => {
    if (!r || ![r.id, r.warehouseCd, r.locationCd, r.productCd, r.lotNo].every(string) || !r.id || ids.has(r.id) || !nullable(r.unitCd) || r.unitState !== (r.unitCd?.trim() ? 'RECORDED' : 'NOT_RECORDED') || !nullable(r.ownerType) || r.ownerState !== (['SELF', 'CUSTOMER'].includes(r.ownerType) ? 'RECORDED' : 'UNKNOWN') || !disclosed(r, 'ownerCd')) return false
    ids.add(r.id)
    if (mode === 'stocks') return [r.physicalQty, r.legacyAllocatedQty, r.legacyAvailableQty].every(exact) && nullable(r.receiveDate) && nullable(r.expiryDate) && r.freeQty === null && r.valuation === null
    return [r.txnNo, r.txnType, r.txnDateTime].every(string) && exact(r.qty) && disclosed(r, 'operatorCd') && disclosed(r, 'remark') && r.relatedNo === null && r.relatedType === null && r.relatedSourceState === 'SOURCE_DISCLOSURE_UNAVAILABLE' && r.occurrenceState === 'NOT_RECONSTRUCTED' && r.quantityMeaning === 'ORIGINAL_UNSIGNED_OR_SIGNED_LEGACY_VALUE' && r.timeBasis === 'RECORDED_TXN_DATETIME_WITHOUT_SITE_TIMEZONE'
  })
}
