import { masterUuid } from './warehouseOwner'
export interface RoutingQuery { targetWarehouseId?: string; enabled?: boolean; page: number; pageSize: number }
export interface RoutingObservation {
  id: string; ruleName: string | null; sortOrder: number; customerCd: string | null
  customerCondition: 'HIDDEN' | 'LEGACY_ANY' | 'LEGACY_EXACT'; productCdPrefix: string | null
  productCondition: 'HIDDEN' | 'LEGACY_ANY' | 'LEGACY_PREFIX'; outboundType: number | null
  targetWarehouseId: string; targetWarehouseCd: string; legacyEnabled: boolean; remarks: string | null; etag: string
  nameReadable: boolean; customerReadable: boolean; productReadable: boolean; remarksReadable: boolean
  formalPolicyState: 'UNPROVEN'; domainState: 'NOT_RECORDED'; selectorState: 'NOT_ADOPTED'
}
export interface RoutingPage {
  version: 'ROUTING-OWNER/1'; coverage: 'CURRENT_AUTHORIZED_RECORDS'; formalPreviewAvailable: false
  legacyResolverEnabled: boolean; items: RoutingObservation[]; total: number; page: number; pageSize: number
}
function requireValue(ok: unknown): asserts ok { if (!ok) throw new Error('ROUTING_RESPONSE_INVALID') }
const visible = (read: unknown, v: unknown) => typeof read === 'boolean' && (v === null || typeof v === 'string') && (read || v === null)
export function checkRoutingRow(r: RoutingObservation, id?: string) {
  requireValue(r && masterUuid(r.id) && (!id || r.id === id) && masterUuid(r.targetWarehouseId) && typeof r.targetWarehouseCd === 'string' && r.targetWarehouseCd.length > 0
    && Number.isInteger(r.sortOrder) && (r.outboundType === null || Number.isInteger(r.outboundType)) && typeof r.legacyEnabled === 'boolean'
    && typeof r.etag === 'string' && /^"[A-Za-z0-9+/]{11}="$/.test(r.etag)
    && r.formalPolicyState === 'UNPROVEN' && r.domainState === 'NOT_RECORDED' && r.selectorState === 'NOT_ADOPTED'
    && visible(r.nameReadable, r.ruleName) && visible(r.customerReadable, r.customerCd) && visible(r.productReadable, r.productCdPrefix) && visible(r.remarksReadable, r.remarks))
  requireValue(r.customerCondition === (!r.customerReadable ? 'HIDDEN' : r.customerCd === null ? 'LEGACY_ANY' : 'LEGACY_EXACT')
    && r.productCondition === (!r.productReadable ? 'HIDDEN' : !r.productCdPrefix ? 'LEGACY_ANY' : 'LEGACY_PREFIX'))
  return r
}
export function checkRoutingPage(p: RoutingPage, q: RoutingQuery) {
  requireValue(p && p.version === 'ROUTING-OWNER/1' && p.coverage === 'CURRENT_AUTHORIZED_RECORDS' && p.formalPreviewAvailable === false
    && typeof p.legacyResolverEnabled === 'boolean' && p.page === q.page && p.pageSize === q.pageSize && Number.isSafeInteger(p.total) && p.total >= 0
    && Array.isArray(p.items) && p.items.length === Math.max(0, Math.min(q.pageSize, p.total - (q.page - 1) * q.pageSize))
    && new Set(p.items.map(x => x.id)).size === p.items.length)
  for (const row of p.items) { checkRoutingRow(row); requireValue((!q.targetWarehouseId || row.targetWarehouseId === q.targetWarehouseId) && (q.enabled === undefined || row.legacyEnabled === q.enabled)) }
  return p
}
