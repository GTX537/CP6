import http from '../http'
import type { EngineeringEnvelope, EngineeringReference } from '../plm/engineeringProducts'
export interface LocationStockFact {
  stockId: string; productCode: string; lotNumber: string; unitCode: string | null
  physicalQuantity: string; allocatedQuantity: string; availableQuantity: string; rowVersion: string | null
}
export interface LocationStockAssessment {
  tenantId: string; actorId: string; locationId: string; warehouseId: string
  proposal: EngineeringReference; etag: string; observedAt: string
  stockCoverage: 'COMPLETE_CURRENT_ROWS' | 'VISIBLE_ROWS_ONLY' | 'TRUNCATED' | 'ARCHIVED_BALANCE_UNRESOLVED'
  truncated: boolean; facts: LocationStockFact[]; missingEvidence: string[]
  sourceOwner: 'Main.Wms.NativeStockObservation.v1'; qualification: 'CURRENT_NATIVE_STOCK_ONLY'
  occupancyCoverage: 'UNKNOWN'; capacityQuantity: null; admissionGrant: false; requiresFinalEffectRecheck: true
}
export const locationStockAssessmentApi = {
  assess: (location: string, id: string, etag: string, proposal: EngineeringReference) =>
    http.post<any, EngineeringEnvelope<LocationStockAssessment>>(`/wms/locations/${encodeURIComponent(location)}/rules-v1/${encodeURIComponent(id)}/stock-assessment`, { proposal }, { headers: { 'If-Match': etag } }),
}
