import http from '../http'
import type { EngineeringEnvelope, EngineeringScope } from '../plm/engineeringProducts'
import type { ToolInstanceReference } from './toolInstances'
export interface ToolSiteRef { owner: 'Main.Space.PhysicalSiteIdentity.v1'; id: string; version: string; digest: string }
export interface ToolSite { reference: ToolSiteRef; code: string; name: string; enabled: boolean }
export interface ToolCreationContext extends EngineeringScope { authorityVersion: string; allowsUnbound: boolean; departments: { id: string; code: string; name: string }[]; writableFields: string[] }
export interface ToolNewProfile { plateType: string; colorCount: number | null; sizeNote: string | null; madeDate: string | null; madeCost: string | null; maxShots: number | null; remarks: string | null }
export interface ToolRegistrationCommand { operationId: string; objectId: string; kind: 'CREATE' | 'BIND_LEGACY'; site: ToolSiteRef; evidence: string; expected?: ToolInstanceReference | null; creationAuthorityVersion?: string | null; departmentId?: string | null; profile?: ToolNewProfile | null }
export interface ToolRegistrationView { tenantId: string; reference: { owner: 'Main.Wms.ToolRegistration.v1'; id: string; version: string; digest: string }; currentNative: ToolInstanceReference; siteAtRegistration: ToolSiteRef; currentSite: ToolSite; cycleAnchorId: string; origin: 'NEW_REGISTERED' | 'LEGACY_OBSERVED'; initialCycleShots: string; cycleNo: string | null; cycleKnowledge: 'NEW_REGISTERED' | 'LEGACY_OBSERVED' | 'RECONCILIATION_REQUIRED'; currentCycleShots: string | null; lifetimeKnowledge: string; lifetimeShots: string | null; originalJson: string; currentEligibility: 'UNKNOWN'; inputContract?: 'tool.registration.v1' | 'tool.registration.v2' }
export interface ToolRegistrationResult { operationId: string; kind: 'TOOL_CREATE' | 'TOOL_INITIALIZE'; objectId: string; version: string; digest: string; inputContract?: 'tool.registration.v1' | 'tool.registration.v2' }
export interface ToolRegistrationOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: ToolRegistrationResult | null }
const root = '/wms/specials/v1/tools'
export const toolRegistrationApi = {
  context: () => http.get<any, EngineeringEnvelope<ToolCreationContext>>(`${root}/creation-context`),
  sites: (search = '') => http.get<any, EngineeringEnvelope<ToolSite[]>>(`${root}/sites`, { params: { search } }),
  read: (id: string) => http.get<any, EngineeringEnvelope<ToolRegistrationView | null>>(`${root}/${encodeURIComponent(id)}/registration`),
  create: (body: ToolRegistrationCommand) => http.post<any, EngineeringEnvelope<ToolRegistrationResult>>(root, body),
  bind: (id: string, etag: string, body: ToolRegistrationCommand) => http.put<any, EngineeringEnvelope<ToolRegistrationResult>>(`${root}/${encodeURIComponent(id)}/registration`, body, { headers: { 'If-Match': etag } }),
  operation: (id: string, close: boolean) => { const path = `${root}/registration-operations/${encodeURIComponent(id)}`; return close ? http.post<any, EngineeringEnvelope<ToolRegistrationOperation>>(`${path}/close`, {}) : http.get<any, EngineeringEnvelope<ToolRegistrationOperation>>(path) },
}
