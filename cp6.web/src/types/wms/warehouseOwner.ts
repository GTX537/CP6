export type MasterKind = 'warehouses' | 'locations'
export interface MasterQuery { warehouseCd?: string; locationCd?: string; page: number; pageSize: number }
export interface MasterRecord {
  id: string; warehouseCd: string; etag: string; nameReadable: boolean; nameWritable: boolean
  lifecycleState: 'UNPROVEN'
}
export interface WarehouseObservation extends MasterRecord {
  warehouseName: string | null; warehouseType: number; baseCd: string | null
  addressText: string | null; managerCd: string | null; remarks: string | null
  addressReadable: boolean; managerReadable: boolean; remarksReadable: boolean
  legacyAllowNegative: boolean; legacyOutboundPriority: number; siteId: null; siteState: 'NOT_RECORDED'
}
export interface LocationObservation extends MasterRecord {
  warehouseId: string; locationCd: string; locationName: string | null; locationLevel: number
  parentId: string | null; parentLocationCd: string | null
  parentState: 'ROOT_RECORD' | 'SOURCE_DISCLOSURE_UNAVAILABLE' | 'AUTHORIZED_ORIGINAL_REFERENCE'
  rawCapacityQty: string; rawReservedCapacityQty: string; areaCd: string | null
  rawAllowedProductType: string | null; legacyIsPickable: boolean; legacyIsBlocked: boolean
  barcode: string | null; barcodeReadable: boolean; capacityState: 'UNKNOWN'; typedCapacity: null
  confirmedReservedQty: null; reservationState: 'UNPROVEN'; hierarchyState: 'ORIGINAL_REFERENCES_ONLY'
}
export type MasterRow = WarehouseObservation | LocationObservation
export interface MasterPage<T extends MasterRow = MasterRow> {
  version: 'WAREHOUSE-OWNER/1'; coverage: 'CURRENT_AUTHORIZED_RECORDS'; formalMasterState: 'UNPROVEN'
  items: T[]; total: number; page: number; pageSize: number
}
export interface MasterScope { tenantId: string; actorId: string }
export interface MasterLocator extends MasterScope { kind: MasterKind; objectId: string; operationId: string }
export interface MasterNameResult { kind: MasterKind; objectId: string; operationId: string; effect: 'CHANGED' | 'NO_CHANGE'; etag: string }
export interface MasterOperation { kind: MasterKind; objectId: string; operationId: string; state: 'UNKNOWN' | 'CLOSED' | 'COMMITTED'; result: MasterNameResult | null }
export type MasterNamePatch = { name: { op: 'SET'; value: string } | { op: 'CLEAR' } }
export const masterUuid = (v: unknown): v is string => typeof v === 'string' && /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(v) && v !== '00000000-0000-0000-0000-000000000000'
const etag = (v: unknown) => typeof v === 'string' && /^"[A-Za-z0-9+/]{11}="$/.test(v)
const text = (v: unknown) => v === null || typeof v === 'string'
const visible = (readable: unknown, value: unknown) => typeof readable === 'boolean' && text(value) && (readable || value === null)
const exact = (v: unknown) => typeof v === 'string' && /^-?(?:0|[1-9]\d{0,12})(?:\.\d{1,8})?$/.test(v)
function requireValue(ok: unknown): asserts ok { if (!ok) throw new Error('MASTER_RESPONSE_INVALID') }
export function checkMasterScope(v: MasterScope) { requireValue(v && masterUuid(v.tenantId) && masterUuid(v.actorId)); return v }
export function checkMasterLocator(v: MasterLocator, scope: MasterScope, kind: MasterKind) {
  requireValue(v && v.kind === kind && v.tenantId === scope.tenantId && v.actorId === scope.actorId && masterUuid(v.objectId) && masterUuid(v.operationId)); return v
}
export function checkMasterPage<T extends MasterRow>(p: MasterPage<T>, kind: MasterKind, q: MasterQuery, warehouseId?: string) {
  requireValue(p && p.version === 'WAREHOUSE-OWNER/1' && p.coverage === 'CURRENT_AUTHORIZED_RECORDS' && p.formalMasterState === 'UNPROVEN'
    && p.page === q.page && p.pageSize === q.pageSize && Number.isSafeInteger(p.total) && p.total >= 0 && Array.isArray(p.items)
    && p.items.length <= q.pageSize && p.items.length <= p.total && new Set(p.items.map(x => x.id)).size === p.items.length)
  for (const r of p.items) {
    requireValue(masterUuid(r.id) && typeof r.warehouseCd === 'string' && etag(r.etag) && typeof r.nameReadable === 'boolean' && typeof r.nameWritable === 'boolean'
      && (r.nameReadable || !r.nameWritable) && r.lifecycleState === 'UNPROVEN')
    if (kind === 'warehouses') {
      const w = r as unknown as WarehouseObservation
      requireValue(w.siteId === null && w.siteState === 'NOT_RECORDED' && visible(w.nameReadable, w.warehouseName)
        && visible(w.addressReadable, w.addressText) && visible(w.managerReadable, w.managerCd) && visible(w.remarksReadable, w.remarks)
        && Number.isInteger(w.warehouseType) && typeof w.legacyAllowNegative === 'boolean')
    } else {
      const l = r as unknown as LocationObservation
      requireValue(l.warehouseId === warehouseId && masterUuid(l.warehouseId) && typeof l.locationCd === 'string' && visible(l.nameReadable, l.locationName)
        && visible(l.barcodeReadable, l.barcode) && exact(l.rawCapacityQty) && exact(l.rawReservedCapacityQty)
        && l.capacityState === 'UNKNOWN' && l.typedCapacity === null && l.reservationState === 'UNPROVEN' && l.confirmedReservedQty === null && l.hierarchyState === 'ORIGINAL_REFERENCES_ONLY')
      requireValue(l.parentState === 'AUTHORIZED_ORIGINAL_REFERENCE'
        ? masterUuid(l.parentId) && l.parentId !== l.id && typeof l.parentLocationCd === 'string'
        : ['ROOT_RECORD', 'SOURCE_DISCLOSURE_UNAVAILABLE'].includes(l.parentState) && l.parentId === null && l.parentLocationCd === null)
    }
  }
  return p
}
function same(v: { kind: MasterKind; objectId: string; operationId: string }, p: MasterLocator) {
  return v && v.kind === p.kind && v.objectId === p.objectId && v.operationId === p.operationId
}
export function checkMasterResult(v: MasterNameResult, p: MasterLocator) {
  requireValue(same(v, p) && ['CHANGED', 'NO_CHANGE'].includes(v.effect) && etag(v.etag)); return v
}
export function checkMasterOperation(v: MasterOperation, p: MasterLocator) {
  requireValue(same(v, p) && ['UNKNOWN', 'CLOSED', 'COMMITTED'].includes(v.state))
  if (v.state === 'COMMITTED') checkMasterResult(v.result!, p)
  else requireValue(v.result === null)
  return v
}
