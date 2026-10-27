import http from '../http'
import type { EngineeringEnvelope, EngineeringScope } from '../plm/engineeringProducts'
export interface ToolInstanceReference { owner: 'Main.Wms.ToolInstance.v1'; id: string; version: string; digest: string }
export interface ToolInstanceSource { plateNo: string; plateType: string; customerCd: string | null; customerName: string | null; productCd: string | null; productName: string | null; colorCount: number | null; sizeNote: string | null; madeDate: string | null; madeCost: string | null; maxShots: number | null; usedShots: number; lastUsedAt: string | null; nextMaintenanceDate: string | null; warehouseCd: string | null; locationCd: string | null; status: number; remarks: string | null; isDeleted: boolean }
export interface ToolInstanceView { tenantId: string; reference: ToolInstanceReference; isCurrent: boolean; etag: string; observedNativeRowVersion: string; observedAt: string; source: ToolInstanceSource; readableFields: string[]; writableFields: string[]; lifetimeKnowledge: 'UNKNOWN'; lifetimeShots: null; cycleNo: null; siteId: null; siteKnowledge: 'LEGACY_UNMAPPED'; currentEligibility: 'UNKNOWN'; engineeringLinkKnowledge: 'LEGACY_UNMAPPED' }
export interface ToolInstancePage { items: ToolInstanceView[]; total: number; page: number; pageSize: number }
export interface ToolInstanceCommand { operationId: string; expected: ToolInstanceReference; patches: { field: string; value: string | null }[] }
export interface ToolInstanceResult { operationId: string; kind: 'TOOL_PROFILE'; objectId: string; version: string; digest: string; changed: boolean }
export interface ToolInstanceOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: ToolInstanceResult | null }
const root = '/wms/specials/v1/tools'
export const toolInstanceApi = {
  scope: () => http.get<any, EngineeringEnvelope<EngineeringScope>>(`${root}/scope`),
  list: (plateNo = '', page = 1) => http.get<any, EngineeringEnvelope<ToolInstancePage>>(root, { params: { plateNo, page, pageSize: 50 } }),
  read: (id: string, version?: string) => http.get<any, EngineeringEnvelope<ToolInstanceView>>(`${root}/${encodeURIComponent(id)}`, { params: { version } }),
  save: (id: string, etag: string, body: ToolInstanceCommand) => http.put<any, EngineeringEnvelope<ToolInstanceResult>>(`${root}/${encodeURIComponent(id)}/profile`, body, { headers: { 'If-Match': etag } }),
  operation: (id: string, close: boolean) => { const path = `${root}/operations/${encodeURIComponent(id)}`; return close ? http.post<any, EngineeringEnvelope<ToolInstanceOperation>>(`${path}/close`, {}) : http.get<any, EngineeringEnvelope<ToolInstanceOperation>>(path) },
}
