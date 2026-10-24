import { masterUuid } from './warehouseOwner'
export interface PackagingQuery { page: number; pageSize: number; packageNo?: string; outboundNo?: string; carrierCd?: string; trackingNo?: string; from?: string; to?: string }
export interface PackagingRecord {
  id: string; packageNo: string; outboundId: string; outboundNo: string; caseQty: number
  totalWeightKg: string | null; totalVolumeM3: string | null; departureTime: string | null
  carrierCd: string | null; trackingNo: string | null; remarks: string | null
  carrierReadable: boolean; trackingReadable: boolean; remarksReadable: boolean
  recordKind: 'LEGACY_SHIPPING_PACKAGE'; formalPackageState: 'UNPROVEN'; deliveryProofState: 'NOT_RECORDED'; timeBasis: 'LEGACY_UNSPECIFIED'
}
export interface PackagingPage { version: 'PACKAGING-OWNER/1'; coverage: 'CURRENT_AUTHORIZED_RECORDS'; page: number; pageSize: number; total: number; items: PackagingRecord[] }
function requireValue(ok: unknown): asserts ok { if (!ok) throw new Error('PACKAGING_RESPONSE_INVALID') }
const visible = (read: unknown, value: unknown) => typeof read === 'boolean' && (value === null || typeof value === 'string') && (read || value === null)
const decimal = (value: unknown) => value === null || typeof value === 'string' && /^-?\d+(\.\d+)?$/.test(value)
export function checkPackagingRecord(r: PackagingRecord, number?: string) {
  requireValue(r && masterUuid(r.id) && masterUuid(r.outboundId) && typeof r.packageNo === 'string' && r.packageNo.length > 0
    && (!number || r.packageNo === number) && typeof r.outboundNo === 'string' && r.outboundNo.length > 0 && Number.isInteger(r.caseQty)
    && decimal(r.totalWeightKg) && decimal(r.totalVolumeM3) && (r.departureTime === null || typeof r.departureTime === 'string' && /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d{1,7})?$/.test(r.departureTime))
    && visible(r.carrierReadable, r.carrierCd) && visible(r.trackingReadable, r.trackingNo) && visible(r.remarksReadable, r.remarks)
    && r.recordKind === 'LEGACY_SHIPPING_PACKAGE' && r.formalPackageState === 'UNPROVEN' && r.deliveryProofState === 'NOT_RECORDED' && r.timeBasis === 'LEGACY_UNSPECIFIED')
  return r
}
export function checkPackagingPage(p: PackagingPage, q: PackagingQuery) {
  requireValue(p && p.version === 'PACKAGING-OWNER/1' && p.coverage === 'CURRENT_AUTHORIZED_RECORDS' && p.page === q.page && p.pageSize === q.pageSize
    && Number.isSafeInteger(p.total) && p.total >= 0 && Array.isArray(p.items)
    && p.items.length === Math.max(0, Math.min(q.pageSize, p.total - (q.page - 1) * q.pageSize)) && new Set(p.items.map(x => x.id)).size === p.items.length)
  // Search uses the original PostgreSQL business-text semantics, not JavaScript ordinal matching.
  for (const row of p.items) checkPackagingRecord(row)
  return p
}
